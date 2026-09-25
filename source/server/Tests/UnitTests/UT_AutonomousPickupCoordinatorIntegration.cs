using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS.PlayerClass;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture, NonParallelizable]
    public sealed class UT_AutonomousPickupCoordinatorIntegration
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Type Coordinator = typeof(AutonomousBotGroupCoordinator);
        private GameServer _previousServer;
        private long _previousTick;
        private IPathfindingMgr _previousNav;
        private sealed class UnavailableNavigation : PathfindingMgrBase
        {
            public override bool IsAvailable => false;
            public override bool HasNavmesh(Zone zone) => false;
        }
        private Group _registeredGroup;
        private static IDictionary Sessions => (IDictionary)Coordinator.GetField("Sessions", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        private sealed class TestServer : GameServer
        {
            public override GameServerConfiguration Configuration => new() { ServerType = EGameServerType.GST_Normal };
        }

        // Only actor inputs are doubled. Roster selection, attendance rebasing,
        // no-show removal, native Group bookkeeping and role reassignment are real.
        private sealed class TestBot : GameBot
        {
            private TestBot() : base((OfflineWorldBotRecord)null) { }
            public ICharacterClass TestClass;
            public override ICharacterClass CharacterClass => TestClass;
            public override byte Level { get; set; }
            public override int EffectiveLevel => Level;
            public override eRealm Realm { get; set; }
            public override ushort CurrentRegionID { get; set; }
            public override int X { get; set; }
            public override int Y { get; set; }
            public override int Z { get; set; }
            public override bool IsAlive => true;
            public override bool InCombat => false;
            public override bool IsAttacking => false;
            public override GameObject.eObjectState ObjectState { get; set; }
        }

        [SetUp]
        public void SetUp()
        {
            _previousServer = GameServer.Instance;
            _previousTick = GameLoop.GameLoopTime;
            _previousNav = PathfindingProvider.Instance;
            PathfindingProvider.SetPathfindingMgr(new UnavailableNavigation());
            GameServer.LoadTestDouble((TestServer)RuntimeHelpers.GetUninitializedObject(typeof(TestServer)));
            SetTick(100_000);
        }

        [TearDown]
        public void TearDown()
        {
            if (_registeredGroup != null) Sessions.Remove(_registeredGroup);
            _registeredGroup = null;
            PathfindingProvider.SetPathfindingMgr(_previousNav);
            SetTick(_previousTick);
            GameServer.LoadTestDouble(_previousServer);
        }

        [Test]
        public void ActualRosterSelectsRemoteHealerAndThirdRealmTankBeforeSpareAttacker()
        {
            TestBot leader = Bot(1, eRealm.Hibernia, new ClassEnchanter());
            TestBot spare = Bot(2, eRealm.Hibernia, new ClassEnchanter());
            TestBot tank = Bot(3, eRealm.Albion, new ClassArmsman());
            TestBot healer = Bot(4, eRealm.Midgard, new ClassHealer());
            object[] args = { leader, new GameBot[] { spare, tank, healer }, null, null, healer, 3 };

            Assert.That((bool)Invoke("TryBuildPveRoster", args), Is.True);
            var selected = (GameBot[])args[2];
            var roles = (Dictionary<long, BotPveGroupRole>)args[3];
            Assert.That(selected, Is.EquivalentTo(new[] { tank, healer }));
            Assert.That(selected.Append(leader).Select(bot => bot.Realm).Distinct().Count(), Is.EqualTo(3));
            Assert.That(roles[tank.DatabaseID], Is.EqualTo(BotPveGroupRole.Tank));
            Assert.That(roles[healer.DatabaseID], Is.EqualTo(BotPveGroupRole.Healer));
            Assert.That(roles[leader.DatabaseID], Is.EqualTo(BotPveGroupRole.Attacker));
        }

        [Test]
        public void ActualRosterKeepsViableUnbalancedPartyWithoutInventingHealerOrDuplicateMembers()
        {
            TestBot leader = Bot(10, eRealm.Albion, new ClassArmsman());
            TestBot attacker = Bot(11, eRealm.Midgard, new ClassBerserker());
            TestBot caster = Bot(12, eRealm.Hibernia, new ClassEnchanter());
            TestBot incomplete = Bot(13, eRealm.Hibernia, null);
            object[] args = { leader, new GameBot[] { attacker, attacker, incomplete, caster }, null, null, caster, 3 };

            Assert.That((bool)Invoke("TryBuildPveRoster", args), Is.True);
            Assert.That((GameBot[])args[2], Is.EquivalentTo(new[] { attacker, caster }));
            var roles = (Dictionary<long, BotPveGroupRole>)args[3];
            Assert.That(roles, Has.Count.EqualTo(3));
            Assert.That(roles.Values, Does.Not.Contain(BotPveGroupRole.Healer));
            object[] invalid = { leader, new GameBot[] { incomplete, leader }, null, null, null, 3 };
            Assert.That((bool)Invoke("TryBuildPveRoster", invalid), Is.False);
        }

        [Test]
        public void CoordinatorExpelsRemoteNoShowAt45MinutesAndPreservesActualAttendees()
        {
            TestBot leader = Bot(21, eRealm.Albion, new ClassArmsman());
            TestBot present = Bot(22, eRealm.Hibernia, new ClassEnchanter());
            TestBot remote = Bot(23, eRealm.Midgard, new ClassHealer());
            leader.CurrentRegionID = present.CurrentRegionID = 1;
            remote.CurrentRegionID = 100;
            Group group = new(leader);
            var nativeMembers = (List<GameLiving>)typeof(Group).GetField("_groupMembers", PrivateInstance).GetValue(group);
            GameBot[] members = { leader, present, remote };
            foreach (GameBot member in members)
            {
                member.Group = group;
                nativeMembers.Add(member);
            }
            object session = Activator.CreateInstance(Coordinator.GetNestedType("Session", BindingFlags.NonPublic), true);
            long formed = GameLoop.GameLoopTime;
            long deadline = formed + AutonomousBotGroupCoordinator.RemoteMeetupTimeoutMilliseconds;
            Set(session, "Group", group);
            Set(session, "Leader", leader);
            Set(session, "Id", "pickup-integration");
            Set(session, "ObjectiveKind", eAutonomousObjectiveKind.GroupPve);
            Set(session, "TaskClock", new AutonomousGroupTaskClock(eAutonomousObjectiveKind.GroupPve, new Random(1)));
            Set(session, "RendezvousRegion", (ushort)1);
            Set(session, "Rendezvous", Vector3.Zero);
            Set(session, "Phase", "Meeting up");
            Set(session, "SoftMeetupStarted", false);
            Set(session, "LockedSize", 3);
            Set(session, "RemoteMeetupDeadlineTick", deadline);
            Set(session, "RemoteMeetupDeadlineUtc", WorldSimulationClock.UtcNow.AddMinutes(45));
            Get<HashSet<long>>(session, "RemoteMemberIds").Add(remote.DatabaseID);
            Get<HashSet<long>>(session, "RemoteMemberIds").Add(present.DatabaseID);
            Invoke("AssignPveRoles", members, Get<Dictionary<long, BotPveGroupRole>>(session, "PveRoles"));
            foreach (GameBot member in members)
                Get<Dictionary<long, Vector3>>(session, "RendezvousSlots")[member.DatabaseID] = Vector3.Zero;
            Sessions.Add(group, session);
            _registeredGroup = group;
            Invoke("RebaseAttendance", session, members, false);

            Pulse(formed + 15 * 60_000L);
            Assert.That(group.GetMembersInTheGroup(), Has.Count.EqualTo(3), "Remote invite must not use the local 15-minute cutoff.");
            Pulse(deadline - 1);
            Assert.That(remote.Group, Is.SameAs(group));
            Pulse(deadline);

            Assert.That(remote.Group, Is.Null);
            Assert.That(Sessions.Contains(group), Is.True, "No-show cleanup must preserve the remaining party session.");
            Assert.That(group.GetMembersInTheGroup(), Is.EquivalentTo(new[] { leader, present }));
            Assert.That(leader.Group, Is.SameAs(group));
            Assert.That(present.Group, Is.SameAs(group));
            Assert.That(Get<int>(session, "LockedSize"), Is.EqualTo(2));
            Assert.That(Get<Dictionary<long, BotPveGroupRole>>(session, "PveRoles").Keys,
                Is.EquivalentTo(new[] { leader.DatabaseID, present.DatabaseID }));
            Assert.That(Get<bool>(session, "ProcessingAttendanceRemovals"), Is.False);
            Assert.That(Get<bool>(session, "SoftMeetupStarted"), Is.False);
            Assert.That(Get<AutonomousGroupTaskClock>(session, "TaskClock").HasStarted, Is.False,
                "Meetup cleanup must not start the camp activity clock.");

            // Rebuild requires installed native geometry. Exercise its rebase
            // contract directly with the real coordinator after shifting slots:
            // an already-arrived remote must not become a fresh zero-time no-show.
            Get<Dictionary<long, Vector3>>(session, "RendezvousSlots")[present.DatabaseID] = new(1000, 1000, 0);
            GameBot[] survivors = { leader, present };
            Invoke("RebaseAttendance", session, survivors, true);
            Assert.That(Get<AutonomousRendezvousAttendance>(session, "Attendance").HasArrived(present.DatabaseID), Is.True);
            Pulse(deadline + 5_000);
            Assert.That(group.GetMembersInTheGroup(), Is.EquivalentTo(survivors));
            Assert.That(present.Group, Is.SameAs(group), "Slot reassignment must preserve a remote member's earlier arrival.");

            void Pulse(long tick)
            {
                SetTick(tick);
                Set(session, "NextAttendanceTick", 0L);
                Invoke("ExpelRendezvousNoShows", session, group.GetMembersInTheGroup().OfType<GameBot>().ToArray());
            }
        }

        private static TestBot Bot(long id, eRealm realm, ICharacterClass characterClass)
        {
            var bot = (TestBot)RuntimeHelpers.GetUninitializedObject(typeof(TestBot));
            bot.DatabaseID = id;
            bot.TestClass = characterClass;
            bot.Level = 30;
            bot.Realm = realm;
            bot.ObjectState = GameObject.eObjectState.Active;
            typeof(GameBot).GetField("<IsAutonomousWorldBot>k__BackingField", PrivateInstance).SetValue(bot, true);
            typeof(GameNPC).GetField("m_brains", PrivateInstance).SetValue(bot, new ArrayList());
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", PrivateInstance).SetValue(bot, new PropertyCollection());
            return bot;
        }

        private static object Invoke(string name, params object[] args) =>
            Coordinator.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        private static void Set(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);
        private static T Get<T>(object target, string name) => (T)target.GetType().GetProperty(name).GetValue(target);
        private static void SetTick(long value) => typeof(GameLoop).GetProperty(nameof(GameLoop.GameLoopTime)).SetValue(null, value);
    }
}
