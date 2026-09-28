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
    /// <summary>Task 47 package C: group camp time, near camps, no reassignment in flight, RvR tour expiry.</summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_AutonomousGroupCampTime
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Type Coordinator = typeof(AutonomousBotGroupCoordinator);
        private static readonly DateTime Utc = new(2026, 9, 28, 4, 0, 0, DateTimeKind.Utc);
        private GameServer _previousServer;
        private long _previousTick;
        private IPathfindingMgr _previousNav;
        private Group _registeredGroup;
        private static IDictionary Sessions => (IDictionary)Coordinator.GetField("Sessions", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        private sealed class UnavailableNavigation : PathfindingMgrBase
        {
            public override bool IsAvailable => false;
            public override bool HasNavmesh(Zone zone) => false;
        }

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
            public override bool IsMoving => false;
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
        public void TaskClockDoesNotTickDuringMatchmakingOrTravelAndRunsFullDurationFromCamp()
        {
            var clock = new AutonomousGroupTaskClock(eAutonomousObjectiveKind.GroupPve, new Random(5));
            long duration = clock.DurationMilliseconds;

            // Matchmaking and meetup: nothing runs.
            Assert.That(clock.RemainingMilliseconds(10_000_000), Is.EqualTo(duration));
            Assert.That(clock.HasExpired(long.MaxValue), Is.False);

            // Outbound trip: only the separate 30-minute travel window runs.
            Assert.That(clock.BeginTravel(1_000, Utc), Is.True);
            Assert.That(clock.RemainingMilliseconds(1_000 + 25 * 60_000L), Is.EqualTo(duration));
            Assert.That(clock.HasTravelTimedOut(1_000 + AutonomousGroupTaskClock.CampTravelTimeoutMilliseconds - 1), Is.False);
            Assert.That(clock.HasTravelTimedOut(1_000 + AutonomousGroupTaskClock.CampTravelTimeoutMilliseconds), Is.True,
                "A party that never arrives still ends.");

            // Camp arrival after 25 minutes of road: the whole duration is left.
            long arrival = 1_000 + 25 * 60_000L;
            Assert.That(clock.Start(arrival, Utc.AddMinutes(25)), Is.True);
            Assert.That(clock.RemainingMilliseconds(arrival), Is.EqualTo(duration));
            Assert.That(clock.HasTravelTimedOut(arrival + AutonomousGroupTaskClock.CampTravelTimeoutMilliseconds), Is.False);
            Assert.That(clock.RemainingMilliseconds(arrival + 10 * 60_000L), Is.EqualTo(duration - 10 * 60_000L));
            Assert.That(clock.HasExpired(arrival + duration - 1), Is.False);
            Assert.That(clock.HasExpired(arrival + duration), Is.True);
        }

        [TestCase(true, 900, 3, true, ExpectedResult = true)]
        [TestCase(true, 2_500, 2, true, ExpectedResult = true)]
        [TestCase(true, 900, 3, false, ExpectedResult = false)]
        [TestCase(true, 2_501, 5, true, ExpectedResult = false)]
        [TestCase(true, 800, 1, true, ExpectedResult = false)]
        [TestCase(false, 800, 5, true, ExpectedResult = false)]
        [TestCase(true, double.PositiveInfinity, 5, true, ExpectedResult = false)]
        public bool TravelDeadlineStartsOnlyAPartyAlreadyFightingAtItsCamp(bool inRegion, double distance, int present,
            bool fought) =>
            AutonomousGroupTaskClock.ShouldStartAtTravelDeadline(inRegion, distance, present, fought);

        [Test]
        public void RecentFightProgressCoversOnlyTheLastTenMinutes()
        {
            long now = 50 * 60_000L;
            Assert.That(AutonomousGroupTaskClock.HasRecentFightProgress(now, 0), Is.False);
            Assert.That(AutonomousGroupTaskClock.HasRecentFightProgress(now, now - 10 * 60_000L), Is.True);
            Assert.That(AutonomousGroupTaskClock.HasRecentFightProgress(now, now - 10 * 60_000L - 1), Is.False);
            Assert.That(AutonomousGroupTaskClock.HasRecentFightProgress(now, now + 1), Is.False);
        }

        [Test]
        public void PartyFightingNearItsDungeonCampAtTheTravelDeadlineStartsItsTask()
        {
            (Group group, object session, TestBot[] members) = TravelingParty(campDistance: 900);
            SetTick(GameLoop.GameLoopTime + AutonomousGroupTaskClock.CampTravelTimeoutMilliseconds);
            SetField(session, "LastFightTick", GameLoop.GameLoopTime - 2 * 60_000L);
            var clock = Get<AutonomousGroupTaskClock>(session, "TaskClock");
            Assert.That(clock.HasTravelTimedOut(GameLoop.GameLoopTime), Is.True);

            bool ended = (bool)Invoke("EndOrStartAtTravelDeadline", session, members.Cast<GameBot>().ToArray());

            Assert.That(ended, Is.False);
            Assert.That(clock.HasStarted, Is.True);
            Assert.That(clock.RemainingMilliseconds(GameLoop.GameLoopTime), Is.EqualTo(clock.DurationMilliseconds),
                "The task starts with its full duration at the camp.");
            Assert.That(Sessions.Contains(group), Is.True);
            Assert.That(members.All(member => member.Group == group), Is.True);
        }

        [Test]
        public void PartyLoopingNearItsCampWithoutFightingStillEndsAtTheTravelDeadline()
        {
            (Group group, object session, TestBot[] members) = TravelingParty(campDistance: 900);
            SetTick(GameLoop.GameLoopTime + AutonomousGroupTaskClock.CampTravelTimeoutMilliseconds);
            // Last fight long before the deadline: no progress in the last ten minutes.
            SetField(session, "LastFightTick", GameLoop.GameLoopTime - 20 * 60_000L);
            var clock = Get<AutonomousGroupTaskClock>(session, "TaskClock");

            bool ended = (bool)Invoke("EndOrStartAtTravelDeadline", session, members.Cast<GameBot>().ToArray());

            Assert.That(ended, Is.True);
            Assert.That(clock.HasStarted, Is.False);
            Assert.That(Get<bool>(session, "Ending"), Is.True);
            Assert.That(Sessions.Contains(group), Is.False);
        }

        [Test]
        public void PausedClockPublishesANonEmptyFutureMemberExpiry()
        {
            var clock = new AutonomousGroupTaskClock(eAutonomousObjectiveKind.RvR, new Random(9));
            DateTime expiry = clock.MemberExpiresUtc(Utc);
            Assert.That(clock.IsPaused, Is.True);
            Assert.That(expiry, Is.EqualTo(Utc.AddMilliseconds(clock.DurationMilliseconds)));
            Assert.That(expiry, Is.GreaterThanOrEqualTo(Utc.AddMinutes(45)));

            var record = new OfflineWorldBotRecord
            {
                ObjectiveKind = nameof(eAutonomousObjectiveKind.RvR),
                ObjectiveAssignmentId = "crew-20-1-RvR",
                ObjectiveExpiresUtc = expiry.ToString("O"),
            };
            Assert.That(record.ObjectiveExpiresUtc, Is.Not.Empty);
            Assert.That(AutonomousObjectiveAssignments.HasActiveRvrTenure(record, Utc.AddMinutes(20)), Is.True,
                "A member leaving a party that is still meeting up keeps its tour.");

            // Refreshed every 15 minutes, so the published end stays at least 30 minutes ahead.
            Assert.That(AutonomousGroupTaskClock.NeedsPausedExpiryRefresh(default, Utc), Is.True);
            Assert.That(AutonomousGroupTaskClock.NeedsPausedExpiryRefresh(Utc, Utc.AddMinutes(14)), Is.False);
            Assert.That(AutonomousGroupTaskClock.NeedsPausedExpiryRefresh(Utc, Utc.AddMinutes(15)), Is.True);

            clock.Start(1_000, Utc.AddMinutes(30));
            Assert.That(clock.MemberExpiresUtc(Utc.AddMinutes(40)), Is.EqualTo(clock.ExpiresUtc),
                "A running clock publishes its fixed deadline.");
        }

        [Test]
        public void PausedSessionWritesTheFutureExpiryIntoEveryMemberRecord()
        {
            (Group _, object session, TestBot[] members) = TravelingParty(campDistance: 900);
            foreach (TestBot member in members)
                typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord)).SetValue(member, new OfflineWorldBotRecord
                {
                    ObjectiveKind = nameof(eAutonomousObjectiveKind.RvR),
                    ObjectiveAssignmentId = "crew-20-1-RvR",
                    ObjectiveExpiresUtc = string.Empty,
                });
            var clock = Get<AutonomousGroupTaskClock>(session, "TaskClock");
            Assert.That(clock.IsPaused, Is.True);

            Invoke("WriteSessionMetadata", session, members.Cast<GameBot>().ToArray());

            DateTime now = WorldSimulationClock.UtcNow;
            foreach (TestBot member in members)
            {
                Assert.That(member.PersistentRecord.ObjectiveExpiresUtc, Is.Not.Empty);
                Assert.That(AutonomousObjectiveAssignments.HasActiveRvrTenure(member.PersistentRecord, now.AddMinutes(30)), Is.True);
            }
        }

        [Test]
        public void GroupCampDrawPrefersTheCampNearTheRendezvous()
        {
            var near = Camp("near", 5);
            var mid = Camp("mid", 12);
            var far = Camp("far", 18);
            var random = new Random(47);
            var picks = new Dictionary<string, int> { ["near"] = 0, ["mid"] = 0, ["far"] = 0 };
            for (int i = 0; i < 4_000; i++)
                picks[AutonomousPickupPlanning.SelectNearbyGroupCamp(new[] { far, mid, near }, random).Id]++;

            Assert.That(picks["near"], Is.GreaterThan(picks["mid"] * 2));
            Assert.That(picks["near"], Is.GreaterThan(picks["far"] * 3));
            Assert.That(picks["far"], Is.GreaterThan(0), "A far camp stays possible.");
            Assert.That(AutonomousPickupPlanning.GroupCampLocalityWeight(10),
                Is.GreaterThan(AutonomousPickupPlanning.GroupCampLocalityWeight(10.5) * 1.9));
        }

        [Test]
        public void GroupCampDrawKeepsCrowdingAndValidity()
        {
            var crowdedNear = Camp("crowded-near", 3, population: 6);
            var quietMid = Camp("quiet-mid", 8);
            var random = new Random(3);
            int quiet = Enumerable.Range(0, 2_000)
                .Count(_ => AutonomousPickupPlanning.SelectNearbyGroupCamp(new[] { crowdedNear, quietMid }, random).Id == "quiet-mid");
            Assert.That(quiet, Is.GreaterThan(1_000), "A crowded spot next door does not beat a quiet one a few minutes on.");

            var overBudget = Camp("over-budget", 25);
            var dungeon = new AutonomousBotDecisionEngine.Camp("dungeon", "zone", "mob", eRealm.Albion, 1, ConColor.YELLOW,
                ConColor.YELLOW, true, true, false, 5, 0, 2, 30);
            Assert.That(AutonomousPickupPlanning.SelectNearbyGroupCamp(new[] { overBudget, dungeon }, random), Is.Null);
            Assert.That(AutonomousPickupPlanning.SelectNearbyGroupCamp(null, random), Is.Null);
        }

        [Test]
        public void TravelingPartyMembersAreOwnedByTheCoordinatorNotTheAllocationPass()
        {
            (Group _, object _, TestBot[] members) = TravelingParty(campDistance: 20_000);
            TestBot solo = Bot(99, eRealm.Albion, new ClassArmsman());
            Assert.That(members.All(AutonomousObjectiveAssignments.IsOwnedByGroupCoordinator), Is.True);
            Assert.That(AutonomousObjectiveAssignments.IsOwnedByGroupCoordinator(solo), Is.False);
            Assert.That(AutonomousObjectiveAssignments.IsOwnedByGroupCoordinator(null), Is.False);
        }

        [Test]
        public void DissolvedPartyIsLoggedAsGroupEndNotReassignment()
        {
            Assert.That(AutonomousGoalDiagnostics.GroupTaskEndReason(true), Is.EqualTo(GoalAttemptEnd.Expired));
            Assert.That(AutonomousGoalDiagnostics.GroupTaskEndReason(false), Is.EqualTo(GoalAttemptEnd.GroupChanged));
            Assert.That(AutonomousGoalAttempt.Classify(GoalAttemptEnd.GroupChanged, 0), Is.EqualTo("interrupted"));
        }

        private (Group Group, object Session, TestBot[] Members) TravelingParty(int campDistance)
        {
            TestBot leader = Bot(1, eRealm.Albion, new ClassArmsman());
            TestBot healer = Bot(2, eRealm.Albion, new ClassCleric());
            TestBot caster = Bot(3, eRealm.Albion, new ClassWizard());
            TestBot[] members = { leader, healer, caster };
            foreach (TestBot member in members)
                member.CurrentRegionID = 222;
            healer.X = 200;
            caster.Y = 300;
            Group group = new(leader);
            var nativeMembers = (List<GameLiving>)typeof(Group).GetField("_groupMembers", PrivateInstance).GetValue(group);
            foreach (TestBot member in members)
            {
                member.Group = group;
                nativeMembers.Add(member);
            }
            var clock = new AutonomousGroupTaskClock(eAutonomousObjectiveKind.GroupPve, new Random(1));
            clock.BeginTravel(GameLoop.GameLoopTime, WorldSimulationClock.UtcNow);
            object session = Activator.CreateInstance(Coordinator.GetNestedType("Session", BindingFlags.NonPublic), true);
            Set(session, "Group", group);
            Set(session, "Leader", leader);
            Set(session, "Id", "task47c-party");
            Set(session, "ObjectiveKind", eAutonomousObjectiveKind.GroupPve);
            Set(session, "TaskClock", clock);
            Set(session, "RendezvousRegion", (ushort)222);
            Set(session, "Rendezvous", Vector3.Zero);
            Set(session, "Phase", "Traveling");
            Set(session, "LockedSize", 3);
            Set(session, "Camp", new AutonomousBotGroupCoordinator.SharedCamp("222:1:1:root worm", "root worm",
                "Spraggon Den", 222, campDistance, 0, 0, true, false, 20));
            Sessions.Add(group, session);
            _registeredGroup = group;
            return (group, session, members);
        }

        private static AutonomousBotDecisionEngine.Camp Camp(string id, double travelMinutes, int population = 0) =>
            new(id, "zone", "mob", eRealm.Albion, 1, ConColor.YELLOW, ConColor.YELLOW,
                true, false, false, 5, 0, travelMinutes, 30, 0, 0, population);

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
        private static void SetField(object target, string name, object value) => target.GetType().GetField(name).SetValue(target, value);
        private static void SetTick(long value) => typeof(GameLoop).GetProperty(nameof(GameLoop.GameLoopTime)).SetValue(null, value);
    }
}
