using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>Which Eden-style service a portal keep point is for.</summary>
    public enum PortalServiceRole : byte { Caster, Fighter, Hastener, DpsDummy, HitbackDummy }

    /// <summary>One planned service position. Z is the navmesh floor the runtime snapped it to.</summary>
    public readonly record struct PortalServicePoint(PortalServiceRole Role, Vector3 Point);

    /// <summary>
    /// Pure placement math for the Eden-style portal keep services (no database, navmesh or world access).
    /// The runtime supplies navmesh-snapped candidates and the inside-the-ring proof; the planner decides
    /// which candidates become guards, the hastener and the training dummies, and where.
    /// Geometry follows GameKeepComponent.LoadFromDatabase: ring pieces sit at 148-unit steps, rotated by
    /// the keep heading, with the Y axis mirrored.
    /// </summary>
    public static class BattlegroundPortalKeepServicePlanner
    {
        /// <summary>Landing centre to the nearest ring piece, measured on the 0.235.0 ring (BattlegroundKeepLayouts).</summary>
        public const float LandingClearance = 218f;
        /// <summary>Services stay this far from every ring piece centre. A piece footprint is about 113 units.</summary>
        public const float PieceClearance = 150f;
        /// <summary>Services stay this far from the landing-to-gate path so the walk through the gate stays clear.</summary>
        public const float GatePathClearance = 120f;
        /// <summary>Two service points are never closer than this.</summary>
        public const float ServiceSpacing = 110f;
        /// <summary>Casters are spread out more than the general minimum.</summary>
        public const float CasterSpacing = 160f;
        /// <summary>The two fighters stand this far apart, one on each side of the gate path.</summary>
        public const float FighterSpacing = 200f;
        public const float FighterMinGate = 150f;
        public const float FighterMaxGate = 320f;
        /// <summary>A caster within this distance of a ring piece stands on the inner side of that wall.</summary>
        public const float CasterWallBand = 280f;
        public const int MaxCasters = 6;
        public const int DpsDummies = 3;
        public const int HitbackDummies = 2;
        /// <summary>Dummy seed: far from the landing and the gate, so the group stays off the arrival and the gate path.</summary>
        public const float DummySeedMinLanding = 350f;
        public const float DummySeedMaxLanding = 600f;
        public const float DummySeedMinGate = 300f;
        /// <summary>The dummies stand together within this radius of the seed.</summary>
        public const float DummyGroupRadius = 300f;
        public const float DummyMemberMinLanding = 300f;
        public const float DummyMemberMinGate = 200f;

        private static readonly float[] CandidateRadii = { 230f, 270f, 310f, 350f, 400f, 460f, 520f, 580f };
        private const int CandidateAngles = 32;
        /// <summary>The ring piece skin of the gate (the only passage).</summary>
        private const int GateSkin = 0;

        /// <summary>
        /// World centres of the ring pieces for a keep centre and heading, exactly as the server places keep components.
        /// The keep heading is used as degrees, as GameKeepComponent does, and each offset is truncated to an integer.
        /// </summary>
        public static IReadOnlyList<(int Skin, Vector2 Position)> RingPieces(int keepX, int keepY, int keepHeading,
            string template = BattlegroundKeepLayouts.PortalTemplate)
        {
            double angle = keepHeading * (Math.PI * 2) / 360;
            var pieces = new List<(int Skin, Vector2 Position)>();
            foreach (ComponentSpec spec in BattlegroundKeepLayouts.Template(template))
            {
                int x = BattlegroundKeepLayouts.SignedOffset(spec.X), y = BattlegroundKeepLayouts.SignedOffset(spec.Y);
                int worldX = (int)(keepX + (x * BattlegroundKeepLayouts.ComponentSpacing * Math.Cos(angle) + y * BattlegroundKeepLayouts.ComponentSpacing * Math.Sin(angle)));
                int worldY = (int)(keepY - (y * BattlegroundKeepLayouts.ComponentSpacing * Math.Cos(angle) - x * BattlegroundKeepLayouts.ComponentSpacing * Math.Sin(angle)));
                pieces.Add((spec.Skin, new Vector2(worldX, worldY)));
            }
            return pieces;
        }

        /// <summary>Footprint radius of the ring piece nearest the landing: its centre distance less the measured landing clearance.</summary>
        public static float FootprintRadius(IReadOnlyList<(int Skin, Vector2 Position)> pieces, Vector2 landing) =>
            pieces.Min(piece => Vector2.Distance(piece.Position, landing)) - LandingClearance;

        /// <summary>The unsnapped candidate grid: rings of points around the landing, every 360 / 32 degrees.</summary>
        public static IReadOnlyList<Vector2> Candidates(Vector2 landing)
        {
            var result = new List<Vector2>(CandidateRadii.Length * CandidateAngles);
            foreach (float radius in CandidateRadii)
            {
                for (int step = 0; step < CandidateAngles; step++)
                {
                    double angle = step * Math.PI * 2 / CandidateAngles;
                    result.Add(new Vector2(landing.X + (float)(radius * Math.Cos(angle)), landing.Y + (float)(radius * Math.Sin(angle))));
                }
            }
            return result;
        }

        /// <summary>
        /// Chooses the service points. <paramref name="candidates"/> are navmesh-snapped floor points; <paramref name="enclosed"/>
        /// answers whether a point is reachable from the landing without crossing the closed gate, which is the inside-the-ring proof.
        /// Order: hastener, fighters, dummy group, casters. Each role takes the preferred free point, so every point keeps the spacing.
        /// </summary>
        public static IReadOnlyList<PortalServicePoint> Plan(Vector2 landing, IReadOnlyList<(int Skin, Vector2 Position)> pieces,
            IReadOnlyList<Vector3> candidates, Func<Vector3, bool> enclosed)
        {
            var plan = new List<PortalServicePoint>();
            if (pieces == null || candidates == null || enclosed == null) return plan;
            int gateIndex = pieces.ToList().FindIndex(piece => piece.Skin == GateSkin);
            if (gateIndex < 0) return plan;
            Vector2 gate = pieces[gateIndex].Position;

            // Geometry first: clear of the landing, every ring piece and the gate path. Enclosure is proved only for these.
            var usable = candidates.Where(point => IsUsable(Xy(point), landing, gate, pieces)).ToList();
            var proven = new Dictionary<Vector3, bool>();
            bool Enclosed(Vector3 point)
            {
                if (!proven.TryGetValue(point, out bool ok))
                {
                    ok = enclosed(point);
                    proven[point] = ok;
                }
                return ok;
            }
            bool Free(Vector3 point, float spacing) => plan.All(placed => Vector2.Distance(Xy(placed.Point), Xy(point)) >= spacing);
            void Add(PortalServiceRole role, Vector3 point) => plan.Add(new PortalServicePoint(role, point));

            // Hastener: the free, enclosed point nearest the landing. It works from there, but never on the arrival clearance.
            if (FirstTakeable(usable.OrderBy(point => Vector2.Distance(Xy(point), landing)),
                point => Free(point, ServiceSpacing) && Enclosed(point)) is Vector3 hastener)
                Add(PortalServiceRole.Hastener, hastener);

            // Fighters: nearest the gate, one on each side of the landing-to-gate path.
            var nearGate = usable.Where(point => Vector2.Distance(Xy(point), gate) is >= FighterMinGate and <= FighterMaxGate)
                .OrderBy(point => Vector2.Distance(Xy(point), gate)).ToList();
            if (FirstTakeable(nearGate, point => Free(point, ServiceSpacing) && Enclosed(point)) is Vector3 firstFighter)
            {
                Add(PortalServiceRole.Fighter, firstFighter);
                float side = Side(Xy(firstFighter), landing, gate);
                Vector3? second = FirstTakeable(nearGate, point => Side(Xy(point), landing, gate) * side < 0 &&
                    Vector2.Distance(Xy(point), Xy(firstFighter)) >= FighterSpacing && Free(point, ServiceSpacing) && Enclosed(point))
                    ?? FirstTakeable(nearGate, point => Vector2.Distance(Xy(point), Xy(firstFighter)) >= FighterSpacing &&
                        Free(point, ServiceSpacing) && Enclosed(point));
                if (second is Vector3 secondFighter)
                    Add(PortalServiceRole.Fighter, secondFighter);
            }

            // Dummy group: a seed far from the landing and the gate, then the nearest free points around it. Placed before the
            // casters, which fill what is left, so the training group is never squeezed out.
            var seedOrder = usable.Where(point => Vector2.Distance(Xy(point), landing) is >= DummySeedMinLanding and <= DummySeedMaxLanding &&
                    Vector2.Distance(Xy(point), gate) >= DummySeedMinGate)
                .OrderByDescending(point => Vector2.Distance(Xy(point), gate)).ToList();
            if (FirstTakeable(seedOrder, point => Free(point, ServiceSpacing) && Enclosed(point)) is Vector3 seed)
            {
                var group = usable.Where(point => Vector2.Distance(Xy(point), Xy(seed)) <= DummyGroupRadius &&
                        Vector2.Distance(Xy(point), landing) >= DummyMemberMinLanding && Vector2.Distance(Xy(point), gate) >= DummyMemberMinGate)
                    .OrderBy(point => Vector2.Distance(Xy(point), Xy(seed))).ToList();
                int dummies = 0;
                foreach (Vector3 point in group)
                {
                    if (dummies == DpsDummies + HitbackDummies) break;
                    if (!Free(point, ServiceSpacing) || !Enclosed(point)) continue;
                    Add(dummies < DpsDummies ? PortalServiceRole.DpsDummy : PortalServiceRole.HitbackDummy, point);
                    dummies++;
                }
            }

            // Casters: the free enclosed points nearest a wall first, then the rest by distance from the nearest wall.
            var casterOrder = usable.OrderBy(point => NearestPiece(Xy(point), pieces) <= CasterWallBand ? 0 : 1)
                .ThenBy(point => NearestPiece(Xy(point), pieces)).ToList();
            for (int casters = 0; casters < MaxCasters; casters++)
            {
                if (FirstTakeable(casterOrder, point => Free(point, CasterSpacing) && Enclosed(point)) is not Vector3 caster)
                    break;
                Add(PortalServiceRole.Caster, caster);
            }
            return plan;
        }

        private static Vector2 Xy(Vector3 point) => new(point.X, point.Y);

        private static bool IsUsable(Vector2 point, Vector2 landing, Vector2 gate, IReadOnlyList<(int Skin, Vector2 Position)> pieces) =>
            Vector2.Distance(point, landing) >= LandingClearance && NearestPiece(point, pieces) >= PieceClearance &&
            SegmentDistance(point, landing, gate) >= GatePathClearance;

        private static float NearestPiece(Vector2 point, IReadOnlyList<(int Skin, Vector2 Position)> pieces) =>
            pieces.Min(piece => Vector2.Distance(point, piece.Position));

        /// <summary>Which side of the landing-to-gate line a point lies on: positive, negative, or zero on the line.</summary>
        private static float Side(Vector2 point, Vector2 landing, Vector2 gate)
        {
            Vector2 axis = gate - landing;
            Vector2 offset = point - landing;
            return axis.X * offset.Y - axis.Y * offset.X;
        }

        private static float SegmentDistance(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 axis = end - start;
            float lengthSquared = axis.LengthSquared();
            float t = lengthSquared <= 0 ? 0 : Math.Clamp(Vector2.Dot(point - start, axis) / lengthSquared, 0f, 1f);
            return Vector2.Distance(point, start + axis * t);
        }

        private static Vector3? FirstTakeable(IEnumerable<Vector3> ordered, Func<Vector3, bool> take)
        {
            foreach (Vector3 point in ordered)
                if (take(point)) return point;
            return null;
        }
    }
}
