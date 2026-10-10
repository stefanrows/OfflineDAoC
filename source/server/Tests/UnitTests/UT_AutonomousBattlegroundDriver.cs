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
        public void PartialLegEndingBesideTheGoalReachesIt()
        {
            var leg = new AutonomousBattlegroundDriver.RouteLeg(PathfindingStatus.PartialPathFound, Goal - new Vector3(40, 0, 0));
            Assert.That(Chain(_ => leg, out _), Is.EqualTo(AutonomousBattlegroundDriver.RouteChain.Reached));
        }
    }
}
