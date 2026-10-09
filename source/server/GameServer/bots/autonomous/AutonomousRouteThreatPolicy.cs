using System;
using System.Numerics;
using DOL.GS.ServerProperties;

namespace DOL.GS
{

    public enum RouteThreatAction
    {
        Pull,
        Detour,
        RejectCamp,
    }

    /// <summary>Bounded decisions for autonomous outdoor PvE route threats.</summary>
    public static class AutonomousRouteThreatPolicy
    {
        [ServerProperty("autonomous", "bot_route_threat_awareness",
            "Enable bounded threat checks while autonomous PvE leaders travel to outdoor camps. Disabled until a controlled performance comparison is accepted.", false)]
        public static bool Enabled;

        public const int ScanMilliseconds = 2_500;
        public const int ScanStaggerMilliseconds = 500;
        public const int MaximumThreatCandidates = 24;
        // This cap covers route-corridor checks below. An actual level-50 party
        // pull separately uses AutonomousDefensivePull's existing bounded firing proof.
        public const int MaximumNativePathQueriesPerScan = 6;
        public const int MaximumRememberedThreats = 64;
        public const long ThreatHistoryExpiryMilliseconds = 30 * 60_000L;
        public const int MaximumPullAttemptsPerThreat = 2;
        public const int MaximumDetoursPerThreat = 2;
        public const int UnreachableFlyerHeight = 400;
        public const float LookAhead = 1_500;
        public const int ScanRadius = 1_800;
        public const int MeleeMargin = 80;
        public const int RangedMargin = 220;
        public const int DetourClearance = 120;
        public const float CampSkipRadius = 700;
        public const float EndpointTolerance = 128;

        public static bool IsUnreachableFlyer(GameNPC target, GameBot actor) =>
            target != null && actor != null && (target.Flags & GameNPC.eFlags.FLYING) != 0 &&
            Math.Abs(target.Z - actor.Z) > UnreachableFlyerHeight;

        public static bool IsCompleteCorridor(PathfindingResult result, int nodeCapacity) =>
            result.Status == PathfindingStatus.PathFound && result.NodeCount > 0 &&
            result.NodeCount <= nodeCapacity;

        public static bool IsEndpointClose(Vector3 actual, Vector3 requested, float tolerance = EndpointTolerance) =>
            Vector3.DistanceSquared(actual, requested) <= tolerance * tolerance;

        public static bool ShouldTryDetour(ConColor con, bool grouped, int packSize, bool bossLike,
            bool pullAllowed, bool alreadyDetouring, int pullAttempts, int detoursSoFar) =>
            Decide(con, grouped, packSize, bossLike, pullAllowed, true,
                alreadyDetouring, pullAttempts, detoursSoFar) == RouteThreatAction.Detour;

        public static RouteThreatAction Decide(ConColor con, bool grouped, int packSize, bool bossLike,
            bool pullAllowed, bool detourAvailable, bool alreadyDetouring, int pullAttempts, int detoursSoFar)
        {
            bool mayPull = pullAllowed && !bossLike && pullAttempts < MaximumPullAttemptsPerThreat;
            if (mayPull && IsEasy(con, grouped, packSize))
                return RouteThreatAction.Pull;

            if (!alreadyDetouring && detourAvailable && detoursSoFar < MaximumDetoursPerThreat)
                return RouteThreatAction.Detour;

            return mayPull && IsManageable(con, grouped, packSize)
                ? RouteThreatAction.Pull
                : RouteThreatAction.RejectCamp;
        }

        private static bool IsEasy(ConColor con, bool grouped, int packSize)
        {
            int maximumPack = grouped
                ? con <= ConColor.BLUE ? 5 : 3
                : con <= ConColor.GREEN ? 3 : con <= ConColor.BLUE ? 2 : 1;
            return con <= (grouped ? ConColor.ORANGE : ConColor.YELLOW) && packSize <= maximumPack;
        }

        private static bool IsManageable(ConColor con, bool grouped, int packSize)
        {
            int maximumPack = grouped
                ? con <= ConColor.BLUE ? 6 : 4
                : con <= ConColor.GREEN ? 5 : con <= ConColor.BLUE ? 3 : 2;
            return con <= (grouped ? ConColor.RED : ConColor.ORANGE) && packSize <= maximumPack;
        }

        public static bool NearestOnRoute(ReadOnlySpan<Vector3> route, Vector3 position, float lookAhead,
            out Vector3 point, out Vector2 direction, out float along)
        {
            point = default;
            direction = default;
            along = 0;
            float best = float.MaxValue;
            float travelled = 0;
            for (int i = 1; i < route.Length && travelled < lookAhead; i++)
            {
                Vector3 start = route[i - 1], delta = route[i] - start;
                float length = delta.Length();
                if (length < 0.01f) continue;
                float available = Math.Min(length, lookAhead - travelled);
                float t = Math.Clamp(Vector3.Dot(position - start, delta) / (length * length), 0, available / length);
                Vector3 nearest = start + delta * t;
                float distance = Vector3.DistanceSquared(nearest, position);
                if (distance < best)
                {
                    best = distance;
                    point = nearest;
                    Vector2 flat = new(delta.X, delta.Y);
                    direction = flat.LengthSquared() > 0.01f ? Vector2.Normalize(flat) : Vector2.UnitY;
                    along = travelled + t * length;
                }
                travelled += length;
            }
            return best < float.MaxValue;
        }

        public static bool PointAtDistance(ReadOnlySpan<Vector3> route, float distance, out Vector3 point)
        {
            point = default;
            if (route.Length < 2 || distance < 0) return false;
            float travelled = 0;
            for (int i = 1; i < route.Length; i++)
            {
                Vector3 start = route[i - 1], delta = route[i] - start;
                float length = delta.Length();
                if (length < 0.01f) continue;
                if (travelled + length >= distance)
                {
                    point = start + delta * ((distance - travelled) / length);
                    return true;
                }
                travelled += length;
            }
            return false;
        }

        public static Vector3 DetourPoint(Vector3 threat, Vector2 routeDirection, float aggroRange,
            int margin, float floorZ, int side)
        {
            Vector2 perpendicular = new(-routeDirection.Y, routeDirection.X);
            float clearance = aggroRange + margin + DetourClearance;
            return new(threat.X + perpendicular.X * side * clearance,
                threat.Y + perpendicular.Y * side * clearance, floorZ);
        }

    }

}
