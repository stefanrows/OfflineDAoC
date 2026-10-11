using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_AutonomousBattlegroundDriver
    {
        [Test]
        public void ClosedGateBlocksOnlyWhenInteractionCannotOpenItAndTheBotMayNotPass()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundDriver.IsRouteBlockingDoor(closed: true, openableByInteraction: false, friendlyKeepDoor: false), Is.True,
                    "Enemy portal gate: the native mover refuses it too");
                Assert.That(AutonomousBattlegroundDriver.IsRouteBlockingDoor(closed: true, openableByInteraction: false, friendlyKeepDoor: true), Is.False,
                    "A keep gate this bot may pass is walked through");
                Assert.That(AutonomousBattlegroundDriver.IsRouteBlockingDoor(closed: true, openableByInteraction: true, friendlyKeepDoor: false), Is.False,
                    "A door interaction can open");
                Assert.That(AutonomousBattlegroundDriver.IsRouteBlockingDoor(closed: false, openableByInteraction: false, friendlyKeepDoor: false), Is.False,
                    "An open gate never blocks");
            });
        }

        private static readonly Vector3 From = new(0, 0, 0);
        private static readonly Vector3 Goal = new(50_000, 0, 0);

        // Fake Detour: each query from x returns a leg of `reach` units toward the goal, complete once the goal is in reach.
        private static Func<Vector3, AutonomousBattlegroundDriver.RouteLeg> Corridor(float reach, float stopAt = float.MaxValue, List<Vector3> starts = null) =>
            start =>
            {
                starts?.Add(start);
                float end = Math.Min(start.X + reach, Math.Min(stopAt, Goal.X));
                PathfindingStatus status = end >= Goal.X ? PathfindingStatus.PathFound : PathfindingStatus.PartialPathFound;
                return new AutonomousBattlegroundDriver.RouteLeg(status, new Vector3(end, 0, 0));
            };

        private static AutonomousBattlegroundDriver.RouteChain Chain(Func<Vector3, AutonomousBattlegroundDriver.RouteLeg> leg, out Vector3 walkTo) =>
            AutonomousBattlegroundDriver.ChainRoute(From, Goal, AutonomousBattlegroundDriver.MaxRouteLegs, leg, out walkTo);

        [Test]
        public void CompletePathWalksStraightToTheGoal()
        {
            Assert.That(Chain(Corridor(60_000), out Vector3 walkTo), Is.EqualTo(AutonomousBattlegroundDriver.RouteChain.Reached));
            Assert.That(walkTo, Is.EqualTo(Goal));
        }

        [Test]
        public void CorridorCappedPathCountsWhenAChainedLegReachesTheGoal()
        {
            var starts = new List<Vector3>();
            Assert.That(Chain(Corridor(12_000, starts: starts), out Vector3 walkTo), Is.EqualTo(AutonomousBattlegroundDriver.RouteChain.Reached),
                "Five 256-polygon legs reach a camp 50,000 units away");
            Assert.Multiple(() =>
            {
                Assert.That(walkTo, Is.EqualTo(new Vector3(12_000, 0, 0)), "The bot walks to the end of the first leg, which the mover plots whole");
                Assert.That(starts, Has.Count.EqualTo(5));
                Assert.That(starts[1], Is.EqualTo(new Vector3(12_000, 0, 0)), "Each leg starts where the previous one ended");
            });
        }

        [Test]
        public void PartialPathThatNeverReachesTheGoalIsNoRoute()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Chain(Corridor(12_000, stopAt: 30_000), out _), Is.EqualTo(AutonomousBattlegroundDriver.RouteChain.DeadEnd),
                    "Disconnected mesh: the legs stop short of the goal");
                Assert.That(Chain(Corridor(1_000), out _), Is.EqualTo(AutonomousBattlegroundDriver.RouteChain.Capped),
                    "Still progressing after the last leg is not a proven route");
                Assert.That(Chain(_ => new AutonomousBattlegroundDriver.RouteLeg(PathfindingStatus.NoPathFound, From), out _),
                    Is.EqualTo(AutonomousBattlegroundDriver.RouteChain.NoPath));
            });
        }

        [Test]
        public void ThreeNoRouteGoalsInARowAndNoWayToTheLandingMeanAnIsland()
        {
            // Live run 0.241.0: four bots on tiny lower mesh layers beside the portal-keep gates logged 253 no_route lines.
            int streak = 0;
            for (int i = 0; i < AutonomousBattlegroundDriver.IslandNoRouteStreak; i++)
                streak = AutonomousBattlegroundDriver.NextNoRouteStreak(streak, routeOk: false, reason: "no_route");
            Assert.Multiple(() =>
            {
                Assert.That(streak, Is.EqualTo(3));
                Assert.That(AutonomousBattlegroundDriver.NextNoRouteStreak(streak, true, null), Is.Zero, "a proven route ends the streak");
                Assert.That(AutonomousBattlegroundDriver.NextNoRouteStreak(streak, false, "closed_gate"), Is.Zero, "another failure is no island sign");
                Assert.That(AutonomousBattlegroundDriver.NeedsIslandRecovery(streak, atLanding: false, landingRouteReason: "no_route"), Is.True,
                    "the bot cannot even reach its own landing: move it there");
                Assert.That(AutonomousBattlegroundDriver.NeedsIslandRecovery(streak, false, "dead_end"), Is.True);
                Assert.That(AutonomousBattlegroundDriver.NeedsIslandRecovery(streak, false, null), Is.False,
                    "its landing is reachable: the goals are the problem, not the bot's position");
                Assert.That(AutonomousBattlegroundDriver.NeedsIslandRecovery(streak, false, "off_mesh"), Is.False, "no evidence about the bot");
                Assert.That(AutonomousBattlegroundDriver.NeedsIslandRecovery(streak, atLanding: true, "no_route"), Is.False, "already at its landing");
                Assert.That(AutonomousBattlegroundDriver.NeedsIslandRecovery(2, false, "no_route"), Is.False, "two failures are not enough");
            });
        }

        [Test]
        public void OpenRouteFailuresAreLoggedOncePerMinutePerBot()
        {
            const long now = 500_000;
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundDriver.MayLogRouteFailure(now, 0), Is.True, "the first line");
                Assert.That(AutonomousBattlegroundDriver.MayLogRouteFailure(now, now + 1), Is.False, "held back inside the minute");
                Assert.That(AutonomousBattlegroundDriver.MayLogRouteFailure(now + AutonomousBattlegroundDriver.RouteFailureLogMilliseconds,
                    now + AutonomousBattlegroundDriver.RouteFailureLogMilliseconds), Is.True);
                Assert.That(AutonomousBattlegroundDriver.RouteFailureLogMilliseconds, Is.EqualTo(60_000));
            });
        }

        [Test]
        public void ALandingReleaseRestsToEightyPercentBeforeTheNextOrder()
        {
            // Live run 0.241.0: 785 landing releases in 15 minutes; bots walked back into the fight at a third of their health.
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundDriver.LandingRestDone(33, 33, usesPower: true), Is.False, "released at a third");
                Assert.That(AutonomousBattlegroundDriver.LandingRestDone(80, 79, usesPower: true), Is.False, "a caster waits for its power too");
                Assert.That(AutonomousBattlegroundDriver.LandingRestDone(80, 80, usesPower: true), Is.True);
                Assert.That(AutonomousBattlegroundDriver.LandingRestDone(80, 0, usesPower: false), Is.True, "a fighter needs only health");
                Assert.That(AutonomousBattlegroundDriver.LandingRestDone(79, 100, usesPower: false), Is.False);
            });
        }

        [Test]
        public void PartialLegEndingBesideTheGoalReachesIt()
        {
            var leg = new AutonomousBattlegroundDriver.RouteLeg(PathfindingStatus.PartialPathFound, Goal - new Vector3(40, 0, 0));
            Assert.That(Chain(_ => leg, out _), Is.EqualTo(AutonomousBattlegroundDriver.RouteChain.Reached));
        }

        [Test]
        public void UnreachableOrderPointFallsBackOnlyAsFarAsTheOrderAllows()
        {
            var assault = AutonomousBattlegroundDriver.LeadBranch.Assault;
            var defend = AutonomousBattlegroundDriver.LeadBranch.Defend;
            var claim = AutonomousBattlegroundDriver.LeadBranch.Claim;
            Assert.Multiple(() =>
            {
                foreach (OpenBgRole role in new[] { OpenBgRole.Roam, OpenBgRole.Lurk, OpenBgRole.BridgeWatch, OpenBgRole.Retreat })
                {
                    Assert.That(AutonomousBattlegroundDriver.FallbackAllows(role, assault), Is.False,
                        $"{role}: a group with an unreachable point never sieges a door in quiet or push");
                    Assert.That(AutonomousBattlegroundDriver.FallbackAllows(role, defend), Is.False, $"{role} roams the camps instead");
                    Assert.That(AutonomousBattlegroundDriver.FallbackAllows(role, claim), Is.False, "the order tried the claim already");
                }
                Assert.That(AutonomousBattlegroundDriver.FallbackAllows(OpenBgRole.Assault, assault), Is.True);
                Assert.That(AutonomousBattlegroundDriver.FallbackAllows(OpenBgRole.Assault, defend), Is.False);
                Assert.That(AutonomousBattlegroundDriver.FallbackAllows(OpenBgRole.Defend, defend), Is.True);
                Assert.That(AutonomousBattlegroundDriver.FallbackAllows(OpenBgRole.Defend, assault), Is.False);
                foreach (var branch in new[] { assault, defend, claim })
                    Assert.That(AutonomousBattlegroundDriver.FallbackAllows(null, branch), Is.True, "brackets and unordered units: priority unchanged");
            });
        }

        [Test]
        public void AUnitHoldingAnOrderNeverFallsBackToTheCamps()
        {
            // Live run 0.242.0: every unreachable order sent its unit roaming the arrival camps, into the camp grinder.
            var roam = AutonomousBattlegroundDriver.LeadBranch.Roam;
            Assert.Multiple(() =>
            {
                foreach (OpenBgRole role in Enum.GetValues<OpenBgRole>())
                    Assert.That(AutonomousBattlegroundDriver.FallbackAllows(role, roam), Is.False, $"{role}: hold or return to the last order point");
                Assert.That(AutonomousBattlegroundDriver.FallbackAllows(null, roam), Is.True, "brackets and unordered units still roam the camps");
            });
        }

        // Thidranki Faste (keep 11): its centre and its two attackable doors, the outer gate 252000301 and the keep door 252000302.
        private static readonly Point3D ThidKeep = new(33280, 38272, 3720);
        private static readonly Point3D ThidGate = new(34333, 38275, 3720);
        private static readonly Point3D ThidKeepDoor = new(32654, 38180, 3720);

        [Test]
        public void TheAssaultMarchesOutsideTheOuterGateNotIntoTheKeepCentre()
        {
            // Live run 0.242.0: the march goal was the keep centre inside the walls (closed_gate / no_route).
            int[] outer = AutonomousBattlegroundDriver.OuterGates(ThidKeep, new[] { ThidKeepDoor, ThidGate });
            double gateDistance = Distance(ThidKeep, ThidGate);
            Assert.Multiple(() =>
            {
                Assert.That(outer, Is.EqualTo(new[] { 1 }), "only the outer gate, not the keep door behind it");
                foreach (int distance in AutonomousBattlegroundDriver.GateApproachDistances)
                {
                    Point3D point = AutonomousBattlegroundDriver.OutsideGate(ThidKeep, ThidGate, distance);
                    Assert.That(distance, Is.InRange(300, 600));
                    Assert.That(Distance(point, ThidGate), Is.EqualTo(distance).Within(1), "that far from the gate");
                    Assert.That(Distance(point, ThidKeep), Is.EqualTo(gateDistance + distance).Within(1), "outside, away from the keep centre");
                    Assert.That(point.X, Is.GreaterThan(ThidGate.X), "the gate faces east: the point lies in front of it");
                }
                Point3D middle = AutonomousBattlegroundDriver.OutsideGate(ThidKeep, ThidGate, 450);
                Assert.That((middle.X, middle.Y, middle.Z), Is.EqualTo((34783, 38276, 3720)));
                Assert.That(AutonomousBattlegroundDriver.OuterGates(ThidKeep, new[] { new Point3D(34333, 38035, 3720), new Point3D(34333, 38515, 3720) }),
                    Is.EqualTo(new[] { 0, 1 }), "a double gate counts twice; the march picks the side nearest the attacker");
                Assert.That(AutonomousBattlegroundDriver.OuterGates(ThidKeep, Array.Empty<Point3D>()), Is.Empty);
                Assert.That(AutonomousBattlegroundDriver.OutsideGate(ThidKeep, ThidKeep, 450), Is.Null, "no direction at the centre");
            });
        }

        private static double Distance(Point3D a, Point3D b) => Math.Sqrt((double)(a.X - b.X) * (a.X - b.X) + (double)(a.Y - b.Y) * (a.Y - b.Y));

        [Test]
        public void AnAssaultOrderAtTheKeepAlwaysTakesTheGateMarchAndTheSiege()
        {
            // Live run 0.243.0: AUTONOMOUS_BG_GATE_APPROACH was never logged. Units under four got an assault order with a
            // skirmish point (33280,40772, AtKeep false) that the driver walked as a plain point, and the siege needed four
            // of the unit's own members present.
            var skirmish = new Point3D(33280, 40772, 3253);
            var bridge = new Point3D(29056, 41088, 3184);
            var atKeep = new OpenBgOrder(3, OpenBgRole.Assault, ThidKeep, Muster: false, AtKeep: true, Cycle: 1);
            var oldSkirmish = new OpenBgOrder(3, OpenBgRole.Assault, skirmish, Muster: false, AtKeep: false, Cycle: 1);
            var muster = new OpenBgOrder(3, OpenBgRole.Assault, bridge, Muster: true, AtKeep: false, Cycle: 1);
            var defend = new OpenBgOrder(4, OpenBgRole.Defend, ThidKeep, Muster: false, AtKeep: true, Cycle: 1);
            var holdApproach = new OpenBgOrder(4, OpenBgRole.Defend, skirmish, Muster: false, AtKeep: false, Cycle: 1);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundDriver.GoesToKeep(atKeep), Is.True);
                Assert.That(AutonomousBattlegroundDriver.GoesToKeep(oldSkirmish), Is.True, "an assault order is never walked as a plain point");
                Assert.That(AutonomousBattlegroundDriver.GoesToKeep(muster), Is.False, "the bridge muster is walked first");
                Assert.That(AutonomousBattlegroundDriver.GoesToKeep(defend), Is.True);
                Assert.That(AutonomousBattlegroundDriver.GoesToKeep(holdApproach), Is.False, "defenders the guards would shoot hold the approaches");
                foreach (OpenBgRole role in new[] { OpenBgRole.Roam, OpenBgRole.Lurk, OpenBgRole.BridgeWatch, OpenBgRole.Retreat })
                    Assert.That(AutonomousBattlegroundDriver.GoesToKeep(new OpenBgOrder(5, role, skirmish, false, false, 1)), Is.False, $"{role}");
                // Fewer than four attackers at the gate: march there and wait; four or more of the realm (any units): siege.
                for (int present = 0; present < AutonomousBattlegroundDriver.AssaultGroupMinimum; present++)
                    Assert.That(AutonomousBattlegroundDriver.StepAtKeep(atKeep, present), Is.EqualTo(AutonomousBattlegroundDriver.KeepOrderStep.MarchGate),
                        $"{present} present");
                Assert.That(AutonomousBattlegroundDriver.StepAtKeep(atKeep, 4), Is.EqualTo(AutonomousBattlegroundDriver.KeepOrderStep.Siege));
                Assert.That(AutonomousBattlegroundDriver.StepAtKeep(oldSkirmish, 9), Is.EqualTo(AutonomousBattlegroundDriver.KeepOrderStep.Siege));
                Assert.That(AutonomousBattlegroundDriver.StepAtKeep(defend, 9), Is.EqualTo(AutonomousBattlegroundDriver.KeepOrderStep.Defend));
                Assert.That(AutonomousBattlegroundDriver.SiegeGatherRange, Is.EqualTo(1_500));
                // The siege counts the realm's assaulting bots, alive, whatever their unit.
                Assert.That(AutonomousBattlegroundDriver.CountsForSiege(true, eRealm.Albion, eRealm.Albion, OpenBgRole.Assault), Is.True);
                Assert.That(AutonomousBattlegroundDriver.CountsForSiege(false, eRealm.Albion, eRealm.Albion, OpenBgRole.Assault), Is.False, "dead");
                Assert.That(AutonomousBattlegroundDriver.CountsForSiege(true, eRealm.Midgard, eRealm.Albion, OpenBgRole.Assault), Is.False, "other realm");
                Assert.That(AutonomousBattlegroundDriver.CountsForSiege(true, eRealm.Albion, eRealm.Albion, OpenBgRole.Defend), Is.False,
                    "a keep friend of the realm defending");
                Assert.That(AutonomousBattlegroundDriver.CountsForSiege(true, eRealm.Albion, eRealm.Albion, null), Is.False, "no order");
            });
        }

        [Test]
        public void TheLandingRestEndsRecoveredOrAtItsCapNeverInAFight()
        {
            const long until = 120_000;
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundDriver.LandingRestEndReason(53, 53, usesPower: true, now: 10_000, until), Is.Null,
                    "released and still below 80 %: the order waits");
                Assert.That(AutonomousBattlegroundDriver.LandingRestEndReason(80, 80, true, 10_000, until), Is.EqualTo("recovered"));
                Assert.That(AutonomousBattlegroundDriver.LandingRestEndReason(80, 10, false, 10_000, until), Is.EqualTo("recovered"));
                Assert.That(AutonomousBattlegroundDriver.LandingRestEndReason(60, 60, true, until, until), Is.EqualTo("cap"));
                Assert.That(AutonomousBattlegroundDriver.LandingRestEndReason(90, 90, true, until, until), Is.EqualTo("recovered"),
                    "recovered wins over the cap in the same turn");
            });
        }

        [Test]
        public void TheOpenBgDeathLineSaysWhoKilledWhom()
        {
            string line = AutonomousBattlegroundDriver.DeathLine(252, "Renalwell", eRealm.Albion, "The Gatekeepers 3", "Haldimund",
                eRealm.Albion, "crew", "Bandit Farmers 2", 36700, 48200, 3279);
            Assert.Multiple(() =>
            {
                Assert.That(line, Does.StartWith("AUTONOMOUS_BG_DEATH region=252 bot=\"Renalwell\" realm=Albion killer=\"Haldimund\" " +
                    "killer_realm=Albion same_realm=True position=36700,48200,3279"));
                Assert.That(line, Does.Contain("killer_kind=crew guild=\"The Gatekeepers 3\" killer_guild=\"Bandit Farmers 2\""));
                Assert.That(AutonomousBattlegroundDriver.DeathLine(252, "Ysiswen", eRealm.Albion, null, "Aedan", eRealm.Hibernia, "player", null, 1, 2, 3),
                    Does.Contain("same_realm=False"));
                Assert.That(AutonomousBattlegroundDriver.DeathLine(252, "Ysiswen", eRealm.Albion, null, null, eRealm.None, "none", null, 1, 2, 3),
                    Does.Contain("killer=\"none\" killer_realm=None same_realm=False"));
            });
        }
    }
}
