using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_BattlegroundPortalKeepServices
    {
        private const float FloorZ = 8640f;
        private static readonly int[] Headings = { 0, 63, 90, 180, 295 };

        private static Vector2 Xy(Vector3 point) => new(point.X, point.Y);

        private static float Distance(Vector2 a, Vector2 b) => Vector2.Distance(a, b);

        private static float NearestPiece(Vector2 point, IReadOnlyList<(int Skin, Vector2 Position)> pieces) =>
            pieces.Min(piece => Distance(point, piece.Position));

        private static float SegmentDistance(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 axis = end - start;
            float t = Math.Clamp(Vector2.Dot(point - start, axis) / axis.LengthSquared(), 0f, 1f);
            return Distance(point, start + axis * t);
        }

        private static float Side(Vector2 point, Vector2 landing, Vector2 gate)
        {
            Vector2 axis = gate - landing;
            Vector2 offset = point - landing;
            return axis.X * offset.Y - axis.Y * offset.X;
        }

        private static Vector2 Gate(IReadOnlyList<(int Skin, Vector2 Position)> pieces) =>
            pieces.First(piece => piece.Skin == 0).Position;

        private static IReadOnlyList<Vector3> FloorCandidates(Vector2 landing) =>
            BattlegroundPortalKeepServicePlanner.Candidates(landing).Select(point => new Vector3(point.X, point.Y, FloorZ)).ToList();

        private static IReadOnlyList<PortalServicePoint> PlanOnRing(int heading, Func<Vector3, bool> enclosed = null)
        {
            var pieces = BattlegroundPortalKeepServicePlanner.RingPieces(0, 0, heading);
            return BattlegroundPortalKeepServicePlanner.Plan(Vector2.Zero, pieces, FloorCandidates(Vector2.Zero), enclosed ?? (_ => true));
        }

        [Test]
        public void RingPiecesMatchTheServerFormulaAtHeadingZero()
        {
            var pieces = BattlegroundPortalKeepServicePlanner.RingPieces(0, 0, 0);
            var expected = new (int Skin, Vector2 Position)[]
            {
                (0, new(-296, 296)), (9, new(-296, 148)), (9, new(-296, -296)), (9, new(-444, -444)),
                (9, new(-148, -444)), (9, new(0, -444)), (9, new(444, -148)), (9, new(444, -296)),
            };
            Assert.That(pieces.Select(piece => (piece.Skin, piece.Position)).ToList(), Is.EqualTo(expected));
        }

        [Test]
        public void RingPiecesFollowTheKeepCentreAndHeading()
        {
            var atOffset = BattlegroundPortalKeepServicePlanner.RingPieces(1000, 2000, 0);
            Assert.That(Gate(atOffset), Is.EqualTo(new Vector2(704, 2296)), "the keep centre shifts the ring");

            // Heading 90 turns the ring: the gate at grid (-2,-2) moves to world (-296,-296) and the (3,1) piece to (148,444).
            var quarter = BattlegroundPortalKeepServicePlanner.RingPieces(0, 0, 90);
            Assert.That(Gate(quarter), Is.EqualTo(new Vector2(-296, -296)));
            Assert.That(quarter[6].Position, Is.EqualTo(new Vector2(148, 444)), "the (3,1) piece");
        }

        [Test]
        public void FootprintIsTheMeasuredLandingClearanceOfTheNearestPiece()
        {
            var pieces = BattlegroundPortalKeepServicePlanner.RingPieces(0, 0, 0);
            float footprint = BattlegroundPortalKeepServicePlanner.FootprintRadius(pieces, Vector2.Zero);
            Assert.That(footprint, Is.InRange(110f, 116f), "the nearest piece centre is 331 units away, 218 of it clear");
            Assert.That(BattlegroundPortalKeepServicePlanner.PieceClearance, Is.GreaterThan(footprint),
                "a service point clears a piece footprint");
        }

        [Test]
        public void CandidatesAreRingsAroundTheLanding()
        {
            var candidates = BattlegroundPortalKeepServicePlanner.Candidates(Vector2.Zero);
            Assert.That(candidates, Has.Count.EqualTo(8 * 32));
            foreach (Vector2 point in candidates)
                Assert.That(point.Length(), Is.InRange(229.5f, 580.5f));
        }

        [Test]
        public void EveryRoleIsFilledOnTheFullyEnclosedRing()
        {
            foreach (int heading in Headings)
            {
                IReadOnlyList<PortalServicePoint> plan = PlanOnRing(heading);
                Assert.Multiple(() =>
                {
                    Assert.That(plan.Count(service => service.Role == PortalServiceRole.Hastener), Is.EqualTo(1), $"hastener, heading {heading}");
                    Assert.That(plan.Count(service => service.Role == PortalServiceRole.Fighter), Is.EqualTo(2), $"fighters, heading {heading}");
                    Assert.That(plan.Count(service => service.Role == PortalServiceRole.Caster), Is.InRange(4, 6), $"casters, heading {heading}");
                    Assert.That(plan.Count(service => service.Role == PortalServiceRole.DpsDummy), Is.EqualTo(3), $"DPS dummies, heading {heading}");
                    Assert.That(plan.Count(service => service.Role == PortalServiceRole.HitbackDummy), Is.EqualTo(2), $"hitback dummies, heading {heading}");
                });
            }
        }

        [Test]
        public void EveryPointKeepsTheClearancesFromTheLandingRingAndGate()
        {
            foreach (int heading in Headings)
            {
                var pieces = BattlegroundPortalKeepServicePlanner.RingPieces(0, 0, heading);
                Vector2 gate = Gate(pieces);
                IReadOnlyList<PortalServicePoint> plan = PlanOnRing(heading);
                foreach (PortalServicePoint service in plan)
                {
                    Vector2 point = Xy(service.Point);
                    string label = $"{service.Role} at {point} heading {heading}";
                    Assert.Multiple(() =>
                    {
                        Assert.That(point.Length(), Is.GreaterThanOrEqualTo(BattlegroundPortalKeepServicePlanner.LandingClearance - 0.01f), label);
                        Assert.That(NearestPiece(point, pieces), Is.GreaterThanOrEqualTo(BattlegroundPortalKeepServicePlanner.PieceClearance - 0.01f), label);
                        Assert.That(SegmentDistance(point, Vector2.Zero, gate), Is.GreaterThanOrEqualTo(BattlegroundPortalKeepServicePlanner.GatePathClearance - 0.01f), label);
                    });
                }
            }
        }

        [Test]
        public void ServicePointsNeverCrowdEachOther()
        {
            foreach (int heading in Headings)
            {
                IReadOnlyList<PortalServicePoint> plan = PlanOnRing(heading);
                for (int i = 0; i < plan.Count; i++)
                    for (int j = i + 1; j < plan.Count; j++)
                    {
                        float distance = Distance(Xy(plan[i].Point), Xy(plan[j].Point));
                        float required = plan[i].Role == PortalServiceRole.Caster || plan[j].Role == PortalServiceRole.Caster
                            ? BattlegroundPortalKeepServicePlanner.CasterSpacing
                            : BattlegroundPortalKeepServicePlanner.ServiceSpacing;
                        Assert.That(distance, Is.GreaterThanOrEqualTo(required - 0.01f),
                            $"{plan[i].Role} and {plan[j].Role}, heading {heading}");
                    }
            }
        }

        [Test]
        public void HastenerIsTheFreePointNearestTheLanding()
        {
            var pieces = BattlegroundPortalKeepServicePlanner.RingPieces(0, 0, 0);
            Vector2 gate = Gate(pieces);
            float nearestUsable = BattlegroundPortalKeepServicePlanner.Candidates(Vector2.Zero)
                .Where(point => point.Length() >= BattlegroundPortalKeepServicePlanner.LandingClearance &&
                    NearestPiece(point, pieces) >= BattlegroundPortalKeepServicePlanner.PieceClearance &&
                    SegmentDistance(point, Vector2.Zero, gate) >= BattlegroundPortalKeepServicePlanner.GatePathClearance)
                .Min(point => point.Length());
            PortalServicePoint hastener = PlanOnRing(0).Single(service => service.Role == PortalServiceRole.Hastener);
            Assert.That(Xy(hastener.Point).Length(), Is.EqualTo(nearestUsable).Within(0.01f));
        }

        [Test]
        public void FightersStandOnOppositeSidesOfTheGatePath()
        {
            foreach (int heading in Headings)
            {
                var pieces = BattlegroundPortalKeepServicePlanner.RingPieces(0, 0, heading);
                Vector2 gate = Gate(pieces);
                List<Vector2> fighters = PlanOnRing(heading).Where(service => service.Role == PortalServiceRole.Fighter)
                    .Select(service => Xy(service.Point)).ToList();
                Assert.That(fighters, Has.Count.EqualTo(2), $"heading {heading}");
                Assert.That(Side(fighters[0], Vector2.Zero, gate) * Side(fighters[1], Vector2.Zero, gate), Is.LessThan(0),
                    $"fighters flank the gate, heading {heading}");
            }
        }

        [Test]
        public void DummiesStayTogetherAwayFromTheLandingAndTheGatePath()
        {
            foreach (int heading in Headings)
            {
                var pieces = BattlegroundPortalKeepServicePlanner.RingPieces(0, 0, heading);
                Vector2 gate = Gate(pieces);
                List<Vector2> dummies = PlanOnRing(heading)
                    .Where(service => service.Role is PortalServiceRole.DpsDummy or PortalServiceRole.HitbackDummy)
                    .Select(service => Xy(service.Point)).ToList();
                Assert.That(dummies, Has.Count.EqualTo(5), $"heading {heading}");
                foreach (Vector2 dummy in dummies)
                {
                    Assert.That(dummy.Length(), Is.GreaterThanOrEqualTo(BattlegroundPortalKeepServicePlanner.DummyMemberMinLanding),
                        $"clear of the landing, heading {heading}");
                    Assert.That(SegmentDistance(dummy, Vector2.Zero, gate), Is.GreaterThanOrEqualTo(BattlegroundPortalKeepServicePlanner.DummyMemberMinGate),
                        $"clear of the gate path, heading {heading}");
                    Assert.That(Distance(dummy, dummies[0]), Is.LessThanOrEqualTo(BattlegroundPortalKeepServicePlanner.DummyGroupRadius * 2),
                        $"one group, heading {heading}");
                }
            }
        }

        [Test]
        public void CandidatesOutsideTheProvenEnclosureAreNeverUsed()
        {
            IReadOnlyList<PortalServicePoint> plan = PlanOnRing(0, point => Xy(point).Length() <= 400f);
            Assert.Multiple(() =>
            {
                Assert.That(plan, Is.Not.Empty);
                foreach (PortalServicePoint service in plan)
                    Assert.That(Xy(service.Point).Length(), Is.LessThanOrEqualTo(400f), service.Role.ToString());
            });
        }

        [Test]
        public void NothingIsPlannedWithoutAProvenEnclosureOrWithoutAGate()
        {
            Assert.That(PlanOnRing(0, _ => false), Is.Empty, "no point is inside the closed ring");

            var noGate = BattlegroundPortalKeepServicePlanner.RingPieces(0, 0, 0).Where(piece => piece.Skin != 0).ToList();
            Assert.That(BattlegroundPortalKeepServicePlanner.Plan(Vector2.Zero, noGate, FloorCandidates(Vector2.Zero), _ => true), Is.Empty);
        }

        [Test]
        public void NoCandidatesMeansNoPlan()
        {
            var pieces = BattlegroundPortalKeepServicePlanner.RingPieces(0, 0, 0);
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundPortalKeepServicePlanner.Plan(Vector2.Zero, pieces, Array.Empty<Vector3>(), _ => true), Is.Empty);
                Assert.That(BattlegroundPortalKeepServicePlanner.Plan(Vector2.Zero, pieces, null, _ => true), Is.Empty);
                Assert.That(BattlegroundPortalKeepServicePlanner.Plan(Vector2.Zero, pieces, FloorCandidates(Vector2.Zero), null), Is.Empty);
            });
        }

        [Test]
        public void EveryPointKeepsTheSnappedFloorHeight()
        {
            foreach (PortalServicePoint service in PlanOnRing(63))
                Assert.That(service.Point.Z, Is.EqualTo(FloorZ), service.Role.ToString());
        }
    }
}
