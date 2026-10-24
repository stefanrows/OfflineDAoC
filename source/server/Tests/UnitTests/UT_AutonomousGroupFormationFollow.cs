using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Task 138: autonomous party members steer to their formation slot from the movement tick. The pure rules: who
    /// gets a formation follow, what the tick does with it, the shared speed rule, re-steer hysteresis, the recent
    /// path failure that suppresses a forced replot, and the keep-travel road lookahead.
    /// </summary>
    [TestFixture]
    public class UT_AutonomousGroupFormationFollow
    {
        [Test]
        public void OnlyAutonomousWorldBotsOfAnAutonomousPartyGetAFormationFollow()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousGroupFormationFollow.IsEligible(true, false, false), Is.True, "autonomous party member");
                Assert.That(AutonomousGroupFormationFollow.IsEligible(false, false, false), Is.False, "companion or ordinary NPC");
                Assert.That(AutonomousGroupFormationFollow.IsEligible(true, true, false), Is.False, "player-led group");
                Assert.That(AutonomousGroupFormationFollow.IsEligible(true, false, true), Is.False, "temporary /spawn helper");
            });
        }

        [Test]
        public void TheTickSteersOnlyALiveFormationFollow()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Decide(), Is.EqualTo(AutonomousGroupFormationFollow.Gate.Steer));
                Assert.That(Decide(eligible: false), Is.EqualTo(AutonomousGroupFormationFollow.Gate.End), "a player now leads the group");
                Assert.That(Decide(sameGroup: false), Is.EqualTo(AutonomousGroupFormationFollow.Gate.End), "left the leader's group");
                Assert.That(Decide(fresh: false), Is.EqualTo(AutonomousGroupFormationFollow.Gate.End), "no AI turn renewed it");
                Assert.That(Decide(held: true), Is.EqualTo(AutonomousGroupFormationFollow.Gate.End), "stable ride, rest or sitting");
                Assert.That(Decide(fighting: true), Is.EqualTo(AutonomousGroupFormationFollow.Gate.End), "a fight belongs to the brain");
            });
        }

        [Test]
        public void ACastOutOfCombatPausesTheFollowAndACastInAFightEndsIt()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Decide(casting: true), Is.EqualTo(AutonomousGroupFormationFollow.Gate.Pause));
                Assert.That(Decide(casting: true, fighting: true), Is.EqualTo(AutonomousGroupFormationFollow.Gate.End),
                    "a ranged attack or a combat cast keeps the ordinary stop rule");
                Assert.That(AutonomousGroupFormationFollow.KeepsThroughTurn(false, true), Is.True,
                    "a turn that only started a cast keeps the follow");
                Assert.That(AutonomousGroupFormationFollow.KeepsThroughTurn(true, true), Is.False);
                Assert.That(AutonomousGroupFormationFollow.KeepsThroughTurn(false, false), Is.False,
                    "a turn that chose anything else ends it");
            });
        }

        [Test]
        public void TheStateLivesFifteenSecondsWithoutARefresh()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousGroupFormationFollow.IsFresh(10_000, 25_000), Is.True);
                Assert.That(AutonomousGroupFormationFollow.IsFresh(10_000, 25_001), Is.False);
                Assert.That(AutonomousGroupFormationFollow.IsFresh(0, 100), Is.False, "never refreshed");
            });
        }

        [Test]
        public void TheSharedSpeedRuleMatchesTheRulesItCombines()
        {
            const long key = 3;
            Assert.Multiple(() =>
            {
                // Autonomous party, leader running unstealthed: its run speed, catch-up when behind, capped when ahead.
                short pace = AutonomousGroupMotion.LeaderPace(true, true, false, 75, 191);
                Assert.That(Speed(key, true, false, 75, 191, 191, 30, false, false, false),
                    Is.EqualTo(AutonomousGroupMotion.FollowSpeed(key, pace, 191, 30)));
                Assert.That(Speed(key, true, false, 75, 191, 191, 400, false, false, false), Is.GreaterThan(191), "catch-up");
                Assert.That(Speed(key, true, false, 75, 191, 250, 400, true, false, false), Is.EqualTo(191),
                    "ahead and in place: never outruns the leader");
                Assert.That(Speed(key, true, false, 75, 191, 250, 400, true, true, false), Is.EqualTo(210),
                    "a front slot still ahead: 10 % more");
                // Stealthed groups creep at the leader's current speed and never use the run cap.
                Assert.That(Speed(key, true, true, 75, 191, 191, 30, false, false, false),
                    Is.EqualTo(AutonomousGroupMotion.FollowSpeed(key, 75, 191, 30)));
                // An RvR member under a speed song never runs past its leader when the full-pace rule does not apply.
                Assert.That(Speed(key, true, true, 75, 191, 191, 400, true, false, true), Is.EqualTo(75));
                Assert.That(Speed(key, false, false, 120, 191, 191, 400, false, false, true),
                    Is.EqualTo(AutonomousGroupMotion.FollowSpeed(key, 120, 191, 400)), "behind: no cap");
            });
        }

        [Test]
        public void TheTickReissuesOnlyWhenTheAimReallyMoved()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousGroupMotion.NeedsResteer(false, true, 0, 0, 191, 0), Is.True, "first order");
                Assert.That(AutonomousGroupMotion.NeedsResteer(true, false, 0, 191, 191, 400), Is.True, "standing");
                Assert.That(AutonomousGroupMotion.NeedsResteer(true, true, 30, 191, 191, 400), Is.False, "slot barely moved");
                Assert.That(AutonomousGroupMotion.NeedsResteer(true, true, 60, 191, 191, 400), Is.True, "slot moved");
                Assert.That(AutonomousGroupMotion.NeedsResteer(true, true, 10, 191, 195, 2_000), Is.False, "speed nearly the same");
                Assert.That(AutonomousGroupMotion.NeedsResteer(true, true, 10, 191, 230, 400), Is.False, "speed change waits 600 ms");
                Assert.That(AutonomousGroupMotion.NeedsResteer(true, true, 10, 191, 230, 700), Is.True);
                // A marching leader moves the 1.5 s aim about 76 units per check: the tick waits for about two.
                Assert.That(AutonomousGroupMotion.NeedsResteer(true, true, 76, 191, 191, 400,
                    AutonomousGroupFormationFollow.TickResteerDistance), Is.False, "one check of march");
                Assert.That(AutonomousGroupMotion.NeedsResteer(true, true, 152, 191, 191, 800,
                    AutonomousGroupFormationFollow.TickResteerDistance), Is.True, "two checks of march");
            });
        }

        [Test]
        public void AStandingFollowerWithAFailedSlotPathHasAnUnreachableSlot()
        {
            float far = 200 * 200, near = 60 * 60;
            Assert.Multiple(() =>
            {
                Assert.That(Unreachable(true, true, PathfindingStatus.NoPathFound, far), Is.True);
                Assert.That(Unreachable(true, true, PathfindingStatus.PartialPathFound, far), Is.True);
                Assert.That(Unreachable(true, true, PathfindingStatus.PathFound, far), Is.False);
                Assert.That(Unreachable(true, false, PathfindingStatus.NoPathFound, far), Is.False, "still walking");
                Assert.That(Unreachable(true, true, PathfindingStatus.NoPathFound, near), Is.False, "close to the slot");
                Assert.That(Unreachable(false, true, PathfindingStatus.NoPathFound, far), Is.False,
                    "the failed order was a trail or leader point");
            });
        }

        [Test]
        public void ARecentNoPathToTheSameSpotSuppressesTheForcedReplot()
        {
            Vector3 target = new(1_000, 1_000, 0);
            Assert.Multiple(() =>
            {
                Assert.That(Pathfinder.IsRecentFailure(PathfindingStatus.NoPathFound, 5_000, 6_999, target, target + new Vector3(30, 0, 0)), Is.True);
                Assert.That(Pathfinder.IsRecentFailure(PathfindingStatus.NoPathFound, 5_000, 7_000, target, target), Is.False, "older than 2 s");
                Assert.That(Pathfinder.IsRecentFailure(PathfindingStatus.NoPathFound, 5_000, 5_500, target, target + new Vector3(100, 0, 0)),
                    Is.False, "another spot");
                Assert.That(Pathfinder.IsRecentFailure(PathfindingStatus.PartialPathFound, 5_000, 5_500, target, target), Is.False);
                Assert.That(Pathfinder.IsRecentFailure(PathfindingStatus.NoPathFound, 0, 500, target, target), Is.False, "never failed");
            });
        }

        [Test]
        public void TheTickSlotLooksFurtherAheadThanTheAiTurnAndHeadingsMatchTheGameGeometry()
        {
            Assert.That(AutonomousGroupFormationFollow.LookaheadSeconds, Is.GreaterThan(AutonomousGroupMotion.LookaheadSeconds));
            Point2D origin = new() { X = 100_000, Y = 100_000 };
            foreach (ushort heading in new ushort[] { 0, 512, 1024, 2048, 3000 })
            {
                Point2D ahead = origin.GetPointFromHeading(heading, 1_000);
                Vector2 forward = AutonomousGroupMotion.HeadingForward(heading) * 1_000;
                Assert.That(forward.X, Is.EqualTo(ahead.X - origin.X).Within(1.01), $"heading {heading} X");
                Assert.That(forward.Y, Is.EqualTo(ahead.Y - origin.Y).Within(1.01), $"heading {heading} Y");
            }
            Vector3 trail = AutonomousGroupMotion.TrailPoint(new Vector3(0, 0, 0), AutonomousGroupMotion.HeadingForward(0));
            Assert.That(trail.Y, Is.EqualTo(-AutonomousGroupMotion.TrailLeaderDistance).Within(0.01), "unit forward vectors work too");
        }

        [Test]
        public void AFollowerOfAStandingLeaderSettlesAtTheOrdinaryApproachSpeed()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousGroupFormationFollow.SettleSpeed(191, 500), Is.EqualTo(191));
                Assert.That(AutonomousGroupFormationFollow.SettleSpeed(191, 40), Is.EqualTo(100));
                Assert.That(AutonomousGroupFormationFollow.SettleSpeed(191, 3), Is.EqualTo(20), "never slower than 20");
            });
        }

        [Test]
        public void AMovingKeepTravellerMovesOnToTheNextRoadPointBeforeItHalts()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRouteRecoveryPolicy.RoadLookaheadRadius(true, 191, 1_000), Is.EqualTo(191).Within(0.01));
                Assert.That(AutonomousRouteRecoveryPolicy.RoadLookaheadRadius(true, 191, 4_000), Is.EqualTo(400), "capped");
                Assert.That(AutonomousRouteRecoveryPolicy.RoadLookaheadRadius(true, 50, 1_000), Is.EqualTo(80), "never below 80");
                Assert.That(AutonomousRouteRecoveryPolicy.RoadLookaheadRadius(false, 191, 3_000), Is.EqualTo(80), "standing: 80 as before");

                Assert.That(AutonomousRouteRecoveryPolicy.AdvancesRoadPointEarly(true, 2, 6, 300 * 300, 400, true), Is.True);
                Assert.That(AutonomousRouteRecoveryPolicy.AdvancesRoadPointEarly(true, 2, 6, 450 * 450, 400, true), Is.False, "too far");
                Assert.That(AutonomousRouteRecoveryPolicy.AdvancesRoadPointEarly(false, 2, 6, 100 * 100, 400, true), Is.False, "standing");
                Assert.That(AutonomousRouteRecoveryPolicy.AdvancesRoadPointEarly(true, 5, 6, 10 * 10, 400, true), Is.False,
                    "the last point keeps its precise arrival");
                Assert.That(AutonomousRouteRecoveryPolicy.AdvancesRoadPointEarly(true, 2, 6, 100 * 100, 400, false), Is.False,
                    "a seam pair keeps its own rule");
            });
        }

        private static AutonomousGroupFormationFollow.Gate Decide(bool eligible = true, bool sameGroup = true, bool fresh = true,
            bool held = false, bool fighting = false, bool casting = false) =>
            AutonomousGroupFormationFollow.Decide(eligible, sameGroup, fresh, held, fighting, casting);

        private static short Speed(long key, bool autonomousGroup, bool stealthed, short leaderCurrent, short leaderMax,
            short ownMax, float distance, bool ahead, bool aimAhead, bool rvr) =>
            AutonomousGroupMotion.FollowerSpeed(key, autonomousGroup, true, stealthed, leaderCurrent, leaderMax, ownMax,
                distance, ahead, aimAhead, rvr);

        private static bool Unreachable(bool slotOrder, bool standing, PathfindingStatus status, float distanceSquared) =>
            AutonomousGroupMotion.SlotUnreachable(slotOrder, standing, status, distanceSquared,
                AutonomousGroupFormationFollow.UnreachableSlotDistance);
    }
}
