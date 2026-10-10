using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS.Keeps;
using NUnit.Framework;
using Wall = DOL.GS.BattlegroundKeepWallGuards;

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

        [Test]
        public void KeepFrameRoundTripsAtHeadingZero()
        {
            // Molvik Faste (557677, 551751), heading 0: a guard's local offset is its offset with Y mirrored.
            Wall.PostFrame frame = Wall.KeepFrame(557677, 551751, 0);

            (double localX, double localY) = Wall.ToLocal(frame, 556912, 552961);
            (double worldX, double worldY) = Wall.ToWorld(frame, localX, localY);

            Assert.That(localX, Is.EqualTo(-765).Within(1e-9));
            Assert.That(localY, Is.EqualTo(-1210).Within(1e-9));
            Assert.That(worldX, Is.EqualTo(556912).Within(1e-9));
            Assert.That(worldY, Is.EqualTo(552961).Within(1e-9));
        }

        [Test]
        public void KeepFrameRoundTripsAtLeirvikHeadingOf87Degrees()
        {
            Wall.PostFrame frame = Wall.KeepFrame(294287, 295659, 87);
            double radians = 87 * Math.PI / 180;

            // A point one hundred units along the keep's local x lies along the keep heading: mostly north (+Y) at 87 degrees.
            (double localX, double localY) = Wall.ToLocal(frame, 294287 + 100 * Math.Cos(radians), 295659 + 100 * Math.Sin(radians));
            (double worldX, double worldY) = Wall.ToWorld(frame, 37, -58);
            (double backX, double backY) = Wall.ToLocal(frame, worldX, worldY);

            Assert.That(localX, Is.EqualTo(100).Within(1e-6));
            Assert.That(localY, Is.EqualTo(0).Within(1e-6));
            Assert.That(backX, Is.EqualTo(37).Within(1e-9));
            Assert.That(backY, Is.EqualTo(-58).Within(1e-9));
        }

        [Test]
        public void LayoutCopiedToAHeading87KeepTurnsWithTheKeep()
        {
            // The same local offset lands due east of a heading 0 keep and turned by 87 degrees for a heading 87 keep.
            (double eastX, double eastY) = Wall.ToWorld(Wall.KeepFrame(557677, 551751, 0), 100, 0);
            (double turnedX, double turnedY) = Wall.ToWorld(Wall.KeepFrame(294287, 295659, 87), 100, 0);
            double radians = 87 * Math.PI / 180;

            Assert.That(eastX, Is.EqualTo(557777).Within(1e-9));
            Assert.That(eastY, Is.EqualTo(551751).Within(1e-9));
            Assert.That(turnedX, Is.EqualTo(294287 + 100 * Math.Cos(radians)).Within(1e-9));
            Assert.That(turnedY, Is.EqualTo(295659 + 100 * Math.Sin(radians)).Within(1e-9));
        }

        [Test]
        public void LayoutKeepsEveryNonLordNativeGuardAndNoGarrisonRow()
        {
            var keep = new Wall.KeepSample("Test Keep", 1000, 2000, 500, 0);
            var guards = new[]
            {
                new Wall.GuardSample("DOL.GS.Keeps.GuardArcher", "Renegade Hunter", "", 1020, 1970, 502, 100, 781),
                new Wall.GuardSample("DOL.GS.Keeps.GuardFighter", "Renegade Guardian", "", 1400, 2300, 500, 0, 318),
                new Wall.GuardSample("DOL.GS.Keeps.GuardHealer", "new mob", "", 1000, 1900, 500, 0, 408),
                new Wall.GuardSample("DOL.GS.Keeps.GuardCaster", "Renegade Wizard", "", 900, 1800, 500, 0, 35),
                new Wall.GuardSample("DOL.GS.Keeps.GuardArcher", "Test Keep Archer", "", 1010, 1990, 500, 0, 48),
                new Wall.GuardSample("DOL.GS.Keeps.GuardLord", "Renegade Chieftain", "", 1000, 1995, 500, 0, 318),
                new Wall.GuardSample("DOL.GS.Keeps.FrontierHastener", "new mob", "", 1100, 1100, 500, 0, 408),
            };

            List<Wall.PostTemplate> templates = Wall.DeriveTemplates(keep, guards);

            Assert.That(templates.Count, Is.EqualTo(4));
            Assert.That(templates[0].GuardType, Is.EqualTo(typeof(WallPostArcher)));
            Assert.That(templates[0].LocalX, Is.EqualTo(20));
            Assert.That(templates[0].LocalY, Is.EqualTo(30));
            Assert.That(templates[0].DeltaZ, Is.EqualTo(2));
            Assert.That(templates[0].RelativeHeading, Is.EqualTo(100));
            Assert.That(templates[0].Model, Is.EqualTo(781));
            Assert.That(templates[1].GuardType, Is.EqualTo(typeof(WallPostFighter)));
            Assert.That(templates[2].Model, Is.EqualTo(GuardTemplateMgr.AvalonianMale), "a placeholder model takes the class default");
            Assert.That(templates[3].GuardType, Is.EqualTo(typeof(WallPostCaster)));
        }

        [Test]
        public void PlannedGuardsStopAtTheCapOfForty()
        {
            var keep = new Wall.KeepSample("Test Keep", 1000, 2000, 500, 0);
            // Fifty templates 200 units apart along x: spacing never drops one, so only the cap does.
            var templates = Enumerable.Range(0, 50).Select(i => new Wall.PostTemplate(200 * i, 0, 0, 0, typeof(WallPostHealer), 61)).ToList();

            Wall.PostPlan plan = Wall.PlanPlacements(keep, templates, Array.Empty<Vector3>(), hint => hint, _ => true);

            Assert.That(plan.Candidates, Is.EqualTo(50));
            Assert.That(plan.Placements.Count, Is.EqualTo(Wall.MaximumGuardsPerKeep));
            Assert.That(plan.DroppedSpacing, Is.EqualTo(10));
            Assert.That(plan.DroppedSnap, Is.EqualTo(0));
            Assert.That(plan.DroppedUnreachable, Is.EqualTo(0));
        }

        [Test]
        public void PlannedGuardsKeepSpacingFromStandingGuardsAndFromEachOther()
        {
            var keep = new Wall.KeepSample("Test Keep", 1000, 2000, 500, 0);
            // At heading 0 a local y of -100 is due north: world (1000, 2100).
            var templates = new[]
            {
                new Wall.PostTemplate(0, -100, 0, 0, typeof(WallPostHealer), 61),   // on a standing guard
                new Wall.PostTemplate(0, -130, 0, 0, typeof(WallPostHealer), 61),   // 30 from the standing guard
                new Wall.PostTemplate(0, -400, 0, 0, typeof(WallPostHealer), 61),   // 300 clear: placed
                new Wall.PostTemplate(0, -420, 0, 0, typeof(WallPostHealer), 61),   // 20 from the guard just planned
            };
            var existing = new[] { new Vector3(1000, 2100, 500) };

            Wall.PostPlan plan = Wall.PlanPlacements(keep, templates, existing, hint => hint, _ => true);

            Assert.That(plan.Candidates, Is.EqualTo(4));
            Assert.That(plan.Placements.Count, Is.EqualTo(1));
            Assert.That(plan.Placements[0].Point, Is.EqualTo(new Vector3(1000, 2400, 500)));
            Assert.That(plan.DroppedSpacing, Is.EqualTo(3));
        }

        [Test]
        public void SnapAndReachabilityDropCandidatesWithTheirOwnCounts()
        {
            var keep = new Wall.KeepSample("Test Keep", 1000, 2000, 500, 0);
            var templates = new[]
            {
                new Wall.PostTemplate(0, -100, 0, 0, typeof(WallPostHealer), 61),  // no floor
                new Wall.PostTemplate(0, -200, 0, 0, typeof(WallPostHealer), 61),  // floor 400 above the layout height
                new Wall.PostTemplate(0, -300, 0, 0, typeof(WallPostHealer), 61),  // floor fine, no camp reaches it
                new Wall.PostTemplate(0, -400, 0, 0, typeof(WallPostHealer), 61),  // floor 200 above, reachable: placed
            };
            Func<Vector3, Vector3?> snap = hint =>
            {
                if (hint.Y == 2100) return null;
                if (hint.Y == 2200) return hint + new Vector3(0, 0, 400);
                if (hint.Y == 2400) return hint + new Vector3(0, 0, 200);
                return hint;
            };

            Wall.PostPlan plan = Wall.PlanPlacements(keep, templates, Array.Empty<Vector3>(), snap, point => point.Y != 2300);

            Assert.That(plan.Candidates, Is.EqualTo(4));
            Assert.That(plan.Placements.Count, Is.EqualTo(1));
            Assert.That(plan.Placements[0].Point, Is.EqualTo(new Vector3(1000, 2400, 700)));
            Assert.That(plan.DroppedSnap, Is.EqualTo(2));
            Assert.That(plan.DroppedUnreachable, Is.EqualTo(1));
            Assert.That(plan.DroppedSpacing, Is.EqualTo(0));
        }
    }
}
