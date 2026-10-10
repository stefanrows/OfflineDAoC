using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_BattlegroundGarrisonFallback
    {
        [Test]
        public void GarrisonMembersKeepTwoHundredUnitsApart()
        {
            Assert.That(BattlegroundNativeKeepData.GuardSpacing, Is.EqualTo(200f));
        }

        [Test]
        public void FirstSpacedPointTakesThePreferredPointThatClearsEveryUsedPoint()
        {
            var used = new List<Vector3> { new(0, 0, 0) };
            var ordered = new List<Vector3> { new(100, 0, 0), new(250, 0, 0), new(0, 300, 0) };
            Assert.That(BattlegroundNativeKeepData.FirstSpacedPoint(ordered, used), Is.EqualTo(new Vector3(250, 0, 0)));
        }

        [Test]
        public void FirstSpacedPointIsNullWhenEveryCandidateCrowdsAUsedPoint()
        {
            var used = new List<Vector3> { new(0, 0, 0), new(300, 0, 0) };
            var ordered = new List<Vector3> { new(100, 0, 0), new(0, 150, 0) };
            Assert.That(BattlegroundNativeKeepData.FirstSpacedPoint(ordered, used), Is.Null);
        }

        [Test]
        public void KeepWithoutGatesHasNothingToCloseSoItTakesTheUngatedPlacement()
        {
            Assert.That(BattlegroundNativeKeepData.GatesCannotBeClosed(0, false), Is.False);
            Assert.That(BattlegroundNativeKeepData.GatesCannotBeClosed(0, true), Is.False);
        }

        [Test]
        public void KeepWithGatesFailsOnlyWhenNoneCanBeClosed()
        {
            Assert.That(BattlegroundNativeKeepData.GatesCannotBeClosed(2, false), Is.True);
            Assert.That(BattlegroundNativeKeepData.GatesCannotBeClosed(2, true), Is.False);
        }

        [Test]
        public void SpacingIsMeasuredInThreeDimensions()
        {
            // 150 across the plane and 150 in height is about 212 apart: spaced.
            var used = new List<Vector3> { new(0, 0, 0) };
            var ordered = new List<Vector3> { new(150, 0, 150) };
            Assert.That(BattlegroundNativeKeepData.FirstSpacedPoint(ordered, used), Is.Not.Null);
        }

        [Test]
        public void CandidatesOrderNearestFirstAndRankAnotherFloorAfterTheKeepFloor()
        {
            // The ground point 300 units away comes before the raised floor 600 units above the origin, although both are on the same column.
            var ordered = BattlegroundNativeKeepData.OrderByDistance(new List<Vector3> { new(0, 0, 600), new(300, 0, 0) }, Vector3.Zero);
            Assert.That(ordered, Is.EqualTo(new List<Vector3> { new(300, 0, 0), new(0, 0, 600) }));
        }

        [Test]
        public void CandidatesAtEqualDistanceKeepTheirInputOrder()
        {
            var input = new List<Vector3> { new(0, 100, 0), new(100, 0, 0), new(0, -100, 0) };
            Assert.That(BattlegroundNativeKeepData.OrderByDistance(input, Vector3.Zero), Is.EqualTo(input));
        }

        [Test]
        public void ProbeCostIsOneQueryPerCampWithAFloorOfOne()
        {
            Assert.That(BattlegroundNativeKeepData.ProbeCost(3), Is.EqualTo(3));
            Assert.That(BattlegroundNativeKeepData.ProbeCost(0), Is.EqualTo(1));
        }

        [Test]
        public void ReachableSearchSkipsPointsNearAUsedPointWithoutProbingThem()
        {
            var probed = new List<Vector3>();
            int budget = 100;
            BattlegroundNativeKeepData.ReachabilityProbe probe = (Vector3 point, ref int remaining) =>
            {
                probed.Add(point);
                remaining -= 1;
                return true;
            };
            var ordered = new List<Vector3> { new(50, 0, 0), new(300, 0, 0) };
            var used = new List<Vector3> { new(0, 0, 0) };

            Vector3? found = BattlegroundNativeKeepData.FirstReachableSpacedPoint(ordered, used, probe, 1, ref budget);

            Assert.That(found.HasValue, Is.True);
            Assert.That(found.Value, Is.EqualTo(new Vector3(300, 0, 0)));
            Assert.That(probed, Has.Count.EqualTo(1));
        }

        [Test]
        public void ReachableSearchStopsWhenTheBudgetCannotPayForAnotherProbe()
        {
            int probes = 0;
            int budget = 6;
            BattlegroundNativeKeepData.ReachabilityProbe probe = (Vector3 point, ref int remaining) =>
            {
                probes++;
                remaining -= 3;
                return false;
            };
            var ordered = Enumerable.Range(1, 10).Select(i => new Vector3(i * 500, 0, 0)).ToList();

            Vector3? found = BattlegroundNativeKeepData.FirstReachableSpacedPoint(ordered, Array.Empty<Vector3>(), probe, 3, ref budget);

            Assert.That(found.HasValue, Is.False);
            Assert.That(probes, Is.EqualTo(2));
            Assert.That(budget, Is.EqualTo(0));
        }

        [Test]
        public void ReachableSearchFindsAReachablePointBehindAnUnreachableRunWithinTheGarrisonBudget()
        {
            // Three camps cost three queries per unreachable candidate, so 400 queries cover 133 candidates: a reachable point after 120 unreachable ones is still found.
            int budget = 400;
            var ordered = Enumerable.Range(1, 121).Select(i => new Vector3(i * 128, 0, 0)).ToList();
            BattlegroundNativeKeepData.ReachabilityProbe probe = (Vector3 point, ref int remaining) =>
            {
                remaining -= BattlegroundNativeKeepData.ProbeCost(3);
                return point.X == 121 * 128;
            };

            Vector3? found = BattlegroundNativeKeepData.FirstReachableSpacedPoint(ordered, Array.Empty<Vector3>(), probe, BattlegroundNativeKeepData.ProbeCost(3), ref budget);

            Assert.That(found.HasValue, Is.True);
            Assert.That(found.Value.X, Is.EqualTo(121 * 128));
        }

        [Test]
        public void ReachableSearchIsNullWhenNothingIsReachable()
        {
            int budget = 100;
            BattlegroundNativeKeepData.ReachabilityProbe probe = (Vector3 point, ref int remaining) =>
            {
                remaining -= 1;
                return false;
            };
            var ordered = new List<Vector3> { new(10, 0, 0), new(500, 0, 0) };

            Assert.That(BattlegroundNativeKeepData.FirstReachableSpacedPoint(ordered, Array.Empty<Vector3>(), probe, 1, ref budget).HasValue, Is.False);
        }

        [Test]
        public void ReachableGarrisonMemberStaysPutAndAnUncheckedOneIsNeverMoved()
        {
            Assert.That(BattlegroundNativeKeepData.DecideGarrisonMove(true, false), Is.EqualTo(BattlegroundNativeKeepData.GarrisonMoveDecision.Keep));
            Assert.That(BattlegroundNativeKeepData.DecideGarrisonMove(true, true), Is.EqualTo(BattlegroundNativeKeepData.GarrisonMoveDecision.Keep));
            Assert.That(BattlegroundNativeKeepData.DecideGarrisonMove(null, true), Is.EqualTo(BattlegroundNativeKeepData.GarrisonMoveDecision.Keep));
        }

        [Test]
        public void UnreachableGarrisonMemberRelocatesWhenAReachablePointExistsAndIsStrandedOtherwise()
        {
            Assert.That(BattlegroundNativeKeepData.DecideGarrisonMove(false, true), Is.EqualTo(BattlegroundNativeKeepData.GarrisonMoveDecision.Relocate));
            Assert.That(BattlegroundNativeKeepData.DecideGarrisonMove(false, false), Is.EqualTo(BattlegroundNativeKeepData.GarrisonMoveDecision.Stranded));
        }
    }
}
