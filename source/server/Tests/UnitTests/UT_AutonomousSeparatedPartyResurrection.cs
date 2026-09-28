using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS.PlayerClass;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    // Bug 29 (live 0.115.0 log): a dead member far from its party held the
    // whole GroupPve party in "Waiting for resurrection" until the shared task
    // expired, and a released member that died again at its bind point on
    // every return kept the party waiting for 1 h 43 min.
    [TestFixture, NonParallelizable]
    public sealed class UT_AutonomousSeparatedPartyResurrection
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Type Coordinator = typeof(AutonomousBotGroupCoordinator);
        private static IDictionary Sessions => (IDictionary)Coordinator.GetField("Sessions", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        private const string Waiting = "Waiting for resurrection";
        private GameServer _previousServer;
        private long _previousTick;
        private Group _registeredGroup;

        private sealed class TestServer : GameServer
        {
            public override GameServerConfiguration Configuration => new() { ServerType = EGameServerType.GST_Normal };
            protected override DOL.Database.IObjectDatabase DataBaseImpl =>
                DispatchProxy.Create<DOL.Database.IObjectDatabase, DOL.UnitTests.UT_UnobservedConcentration.EmptyReads>();
        }

        private sealed class TestBot : GameBot
        {
            private TestBot() : base((OfflineWorldBotRecord)null) { }
            public ICharacterClass TestClass;
            public bool Alive;
            public string TestName;
            public override string Name { get => TestName; set { } }
            public override ICharacterClass CharacterClass => TestClass;
            public override byte Level { get; set; }
            public override int EffectiveLevel => Level;
            public override eRealm Realm { get; set; }
            public override ushort CurrentRegionID { get; set; }
            public override int X { get; set; }
            public override int Y { get; set; }
            public override int Z { get; set; }
            public override bool IsAlive => Alive;
            public override bool InCombat => false;
            public override bool IsAttacking => false;
            public override bool IsCasting => false;
            public override GameObject.eObjectState ObjectState { get; set; }
        }

        [SetUp]
        public void SetUp()
        {
            _previousServer = GameServer.Instance;
            _previousTick = GameLoop.GameLoopTime;
            GameServer.LoadTestDouble((TestServer)RuntimeHelpers.GetUninitializedObject(typeof(TestServer)));
            SetTick(1_000_000);
        }

        [TearDown]
        public void TearDown()
        {
            if (_registeredGroup != null) Sessions.Remove(_registeredGroup);
            _registeredGroup = null;
            SetTick(_previousTick);
            GameServer.LoadTestDouble(_previousServer);
        }

        [Test]
        public void DeadMemberInAnotherRegionOrFarAwayIsReleasedAtOnceWithoutHoldingTheParty()
        {
            (object session, Group _, TestBot leader, TestBot _, TestBot dead) = Party();
            dead.Alive = false;
            dead.CurrentRegionID = 100;
            Update(session);
            Assert.That(Get<string>(session, "Phase"), Is.EqualTo(Waiting));
            Assert.That(AutonomousBotGroupCoordinator.PveCorpseRecovery(dead),
                Is.EqualTo(AutonomousBotGroupCoordinator.PveCorpseDisposition.ReleaseAndRejoin),
                "A corpse two regions away from every living member has nobody to raise it.");

            dead.CurrentRegionID = leader.CurrentRegionID;
            dead.X = leader.X + 20_000;
            Assert.That(AutonomousBotGroupCoordinator.PveCorpseRecovery(dead),
                Is.EqualTo(AutonomousBotGroupCoordinator.PveCorpseDisposition.ReleaseAndRejoin),
                "A corpse far outside visibility range in the same region is not held either.");
        }

        [Test]
        public void CampPulsesDoNotRestartTheReleaseWindowOfANearbyCorpse()
        {
            (object session, Group _, TestBot leader, TestBot _, TestBot dead) = Party();
            dead.Alive = false;
            dead.X = leader.X + 500;
            Update(session);
            Assert.That(AutonomousBotGroupCoordinator.PveCorpseRecovery(dead),
                Is.EqualTo(AutonomousBotGroupCoordinator.PveCorpseDisposition.HoldForResurrection),
                "A corpse beside its party still waits for a resurrection.");

            long start = GameLoop.GameLoopTime;
            for (long now = start + 3_000; now <= start + 63_000; now += 3_000)
            {
                SetTick(now);
                // Members at the camp report grinding on every AI pulse.
                AutonomousBotGroupCoordinator.MarkGrinding(leader);
                Assert.That(Get<string>(session, "Phase"), Is.EqualTo(Waiting),
                    "A camp pulse must not end the casualty hold.");
                Update(session);
            }
            Assert.That(Get<string>(session, "PhaseBeforeCasualty"), Is.EqualTo("Grinding"));
            Assert.That(AutonomousBotGroupCoordinator.PveCorpseRecovery(dead),
                Is.EqualTo(AutonomousBotGroupCoordinator.PveCorpseDisposition.ReleaseAndRejoin),
                "Sixty quiet seconds must release the corpse even while the camp keeps pulsing.");
        }

        [Test]
        public void RepeatedDeathsBeforeRejoiningDropTheMemberAndThePartyPlaysOn()
        {
            (object session, Group group, TestBot leader, TestBot healer, TestBot dead) = Party();
            dead.Alive = false;
            dead.CurrentRegionID = 100;
            dead.PersistentRecord.DeathCount = 20;
            Update(session);
            Assert.That(dead.Group, Is.SameAs(group), "One death is not a failed rejoin.");

            for (int failed = 1; failed < AutonomousBotGroupCoordinator.MaxFailedRejoinDeaths; failed++)
            {
                // Released at its bind, walked back and died there again.
                dead.PersistentRecord.DeathCount++;
                Update(session);
                Assert.That(dead.Group, Is.SameAs(group));
            }
            dead.PersistentRecord.DeathCount++;
            Update(session);

            Assert.That(dead.Group, Is.Null, "The party must stop waiting for a member that keeps dying on its way back.");
            Assert.That(group.GetMembersInTheGroup(), Is.EquivalentTo(new GameLiving[] { leader, healer }));
            Assert.That(Sessions.Contains(group), Is.True, "The remaining party keeps its shared task.");
            Assert.That(Get<string>(session, "Phase"), Is.EqualTo("Choosing group target"),
                "Dropping the only corpse ends the hold and resumes target selection.");
            Assert.That(Get<int>(session, "CasualtiesWhileWaiting"), Is.EqualTo(0));
            Assert.That(Get<int>(session, "LockedSize"), Is.EqualTo(2));
        }

        [Test]
        public void ACampRejectedDuringTheHoldResumesTargetSelection()
        {
            (object session, Group _, TestBot leader, TestBot _, TestBot dead) = Party();
            dead.Alive = false;
            dead.X = leader.X + 500;
            Update(session);
            AutonomousBotGroupCoordinator.RejectUnreachableCamp(leader, "bug29-camp", "test route rejected");
            Assert.That(Get<string>(session, "Phase"), Is.EqualTo(Waiting));

            dead.Alive = true;
            Update(session);
            Assert.That(Get<object>(session, "Camp"), Is.Null);
            Assert.That(Get<string>(session, "Phase"), Is.EqualTo("Choosing group target"),
                "Without a camp the party must choose again, not travel nowhere until the travel window ends.");
        }

        [Test]
        public void AMemberBackBesideItsLeaderStartsAFreshRejoinCount()
        {
            (object session, Group group, TestBot leader, TestBot _, TestBot dead) = Party();
            dead.Alive = false;
            dead.CurrentRegionID = 100;
            dead.PersistentRecord.DeathCount = 20;
            Update(session);
            dead.PersistentRecord.DeathCount++;
            Update(session);

            dead.Alive = true;
            dead.CurrentRegionID = leader.CurrentRegionID;
            dead.X = leader.X + 100;
            Update(session);

            for (int death = 0; death < AutonomousBotGroupCoordinator.MaxFailedRejoinDeaths; death++)
            {
                dead.Alive = false;
                dead.PersistentRecord.DeathCount++;
                Update(session);
            }
            Assert.That(dead.Group, Is.SameAs(group), "Deaths before a successful rejoin must not count against the next trip.");
        }

        private (object Session, Group Group, TestBot Leader, TestBot Healer, TestBot Dead) Party()
        {
            TestBot leader = Bot(2901, "Giselisbel", eRealm.Albion, new ClassArmsman());
            TestBot healer = Bot(2902, "Osobert", eRealm.Albion, new ClassCleric());
            TestBot dead = Bot(2903, "Sivildrid", eRealm.Midgard, new ClassSkald());
            healer.X = leader.X + 200;
            dead.X = leader.X + 300;
            Group group = new(leader);
            var nativeMembers = (List<GameLiving>)typeof(Group).GetField("_groupMembers", PrivateInstance).GetValue(group);
            TestBot[] members = { leader, healer, dead };
            foreach (TestBot member in members)
            {
                member.Group = group;
                nativeMembers.Add(member);
            }
            object session = Activator.CreateInstance(Coordinator.GetNestedType("Session", BindingFlags.NonPublic), true);
            var clock = new AutonomousGroupTaskClock(eAutonomousObjectiveKind.GroupPve, new Random(29));
            clock.Start(GameLoop.GameLoopTime, WorldSimulationClock.UtcNow);
            Set(session, "Group", group);
            Set(session, "Leader", leader);
            Set(session, "Id", "bug29-separated-party");
            Set(session, "ObjectiveKind", eAutonomousObjectiveKind.GroupPve);
            Set(session, "TaskClock", clock);
            Set(session, "RendezvousRegion", (ushort)1);
            Set(session, "Rendezvous", new System.Numerics.Vector3(leader.X, leader.Y, leader.Z));
            Set(session, "Phase", "Grinding");
            Set(session, "LockedSize", 3);
            Set(session, "Camp", new AutonomousBotGroupCoordinator.SharedCamp("bug29-camp", "forest ettin",
                "Campacorentin Forest", 1, leader.X, leader.Y, leader.Z, false, false, 17));
            Invoke("AssignPveRoles", members.Cast<GameBot>().ToArray(), Get<Dictionary<long, BotPveGroupRole>>(session, "PveRoles"));
            Sessions.Add(group, session);
            _registeredGroup = group;
            return (session, group, leader, healer, dead);
        }

        private static void Update(object session)
        {
            Group group = Get<Group>(session, "Group");
            GameBot[] members = group.GetMembersInTheGroup().OfType<GameBot>().ToArray();
            Invoke("UpdateSession", session, members);
        }

        private static TestBot Bot(long id, string name, eRealm realm, ICharacterClass characterClass)
        {
            var bot = (TestBot)RuntimeHelpers.GetUninitializedObject(typeof(TestBot));
            bot.DatabaseID = id;
            bot.TestName = name;
            bot.TestClass = characterClass;
            bot.Level = 17;
            bot.Realm = realm;
            bot.Alive = true;
            bot.CurrentRegionID = 1;
            bot.X = 585_000;
            bot.Y = 531_000;
            bot.Z = 2_000;
            bot.ObjectState = GameObject.eObjectState.Active;
            typeof(GameBot).GetField("<IsAutonomousWorldBot>k__BackingField", PrivateInstance).SetValue(bot, true);
            typeof(GameNPC).GetField("m_brains", PrivateInstance).SetValue(bot, new ArrayList());
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", PrivateInstance).SetValue(bot, new PropertyCollection());
            typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord)).SetValue(bot, new OfflineWorldBotRecord());
            return bot;
        }

        private static object Invoke(string name, params object[] args) =>
            Coordinator.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        private static void Set(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);
        private static T Get<T>(object target, string name) => (T)target.GetType().GetProperty(name).GetValue(target);
        private static void SetTick(long value) => typeof(GameLoop).GetProperty(nameof(GameLoop.GameLoopTime)).SetValue(null, value);
    }
}
