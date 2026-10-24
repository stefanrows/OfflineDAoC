using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// The first zone-point crossing toward a goal cell. The caller supplies
    /// the realm/region-filtered edge list (which can be reused for a whole
    /// planning pass); the goal's own entrance restriction and the distance to
    /// the goal are applied here per cell, because a dungeon region with
    /// several entrances may admit a goal cell through only one of them.
    /// </summary>
    public static class AutonomousZoneCrossingSearch
    {
        public static DbZonePoint FindFirstCrossing(
            IReadOnlyList<DbZonePoint> edges,
            ushort currentRegion,
            ushort targetRegion,
            int targetX,
            int targetY,
            Func<DbZonePoint, bool> canUseEntrance,
            Func<DbZonePoint, bool> isQuarantined = null,
            (int X, int Y)? origin = null)
        {
            DbZonePoint[] points = edges.Where(point => canUseEntrance == null || canUseEntrance(point)).ToArray();

            DbZonePoint direct = points.Where(point => point.SourceRegion == currentRegion &&
                                                       (isQuarantined == null || !isQuarantined(point)) &&
                                                       point.TargetRegion == targetRegion)
                .OrderBy(point => DistanceSquared(point.TargetX, point.TargetY, targetX, targetY) +
                                  (origin is not { } start ? 0 :
                                      DistanceSquared(point.SourceX, point.SourceY, start.X, start.Y)))
                .FirstOrDefault();
            if (direct != null)
                return direct;

            var previous = new Dictionary<ushort, DbZonePoint>();
            var seen = new HashSet<ushort> { currentRegion };
            var queue = new Queue<ushort>();
            queue.Enqueue(currentRegion);
            while (queue.Count > 0)
            {
                ushort region = queue.Dequeue();
                IEnumerable<DbZonePoint> outgoing = points.Where(point => point.SourceRegion == region &&
                    (isQuarantined == null || region != currentRegion || !isQuarantined(point)));
                if (origin is { } from && region == currentRegion)
                {
                    outgoing = outgoing.OrderBy(point =>
                        DistanceSquared(point.SourceX, point.SourceY, from.X, from.Y) +
                        DistanceSquared(point.TargetX, point.TargetY, targetX, targetY));
                }
                foreach (DbZonePoint edge in outgoing)
                {
                    if (!seen.Add(edge.TargetRegion))
                        continue;
                    previous[edge.TargetRegion] = edge;
                    if (edge.TargetRegion == targetRegion)
                    {
                        DbZonePoint first = edge;
                        while (first.SourceRegion != currentRegion && previous.TryGetValue(first.SourceRegion, out DbZonePoint prior))
                            first = prior;
                        return first;
                    }
                    queue.Enqueue(edge.TargetRegion);
                }
            }
            return null;
        }

        /// <summary>How the give-up memory changed a crossing search.</summary>
        public enum GiveUpSteer
        {
            /// <summary>The chosen crossing was not one the bot gave up reaching (or there was none).</summary>
            Unchanged,
            /// <summary>The chosen crossing was a give-up spot; another route was taken instead.</summary>
            Steered,
            /// <summary>The give-up spot was the only route left, so it was kept.</summary>
            Fallback,
        }

        /// <summary>
        /// Task 140: <see cref="FindFirstCrossing"/>, but a first crossing the bot just gave up reaching
        /// (<paramref name="isGivenUp"/>) is swapped for another route when one exists. The memory only steers the
        /// choice: when the given-up crossing is the last route it is returned, so a bot is never stranded in a region
        /// whose only exit it once failed to reach. The second search runs only when the first result is a give-up spot.
        /// </summary>
        public static DbZonePoint FindFirstCrossingAvoidingGiveUps(
            IReadOnlyList<DbZonePoint> edges,
            ushort currentRegion,
            ushort targetRegion,
            int targetX,
            int targetY,
            Func<DbZonePoint, bool> canUseEntrance,
            Func<DbZonePoint, bool> isQuarantined,
            (int X, int Y)? origin,
            Func<DbZonePoint, bool> isGivenUp,
            out GiveUpSteer steer)
        {
            steer = GiveUpSteer.Unchanged;
            DbZonePoint first = FindFirstCrossing(edges, currentRegion, targetRegion, targetX, targetY,
                canUseEntrance, isQuarantined, origin);
            if (first == null || isGivenUp == null || !isGivenUp(first))
                return first;
            DbZonePoint other = FindFirstCrossing(edges, currentRegion, targetRegion, targetX, targetY,
                canUseEntrance, point => isQuarantined?.Invoke(point) == true || isGivenUp(point), origin);
            steer = other == null ? GiveUpSteer.Fallback : GiveUpSteer.Steered;
            return other ?? first;
        }

        /// <summary>
        /// The edges a PvE route really uses (bug 78): outside the shared
        /// frontier dungeons, and for a goal outside them, a route never enters
        /// them while the goal region is reachable without them. Camp planning
        /// must judge a goal over this same set; over the raw edges it found
        /// the road through the complex to another realm's Darkness Falls
        /// entrance, which the route then refused (bug 145).
        /// </summary>
        public static DbZonePoint[] WithoutFrontierDungeonRoads(DbZonePoint[] edges, ushort currentRegion,
            ushort targetRegion, Func<DbZonePoint[], bool> hasWayAround = null)
        {
            if (AutonomousRvrMobAvoidance.IsForbiddenRvrRegion(currentRegion) ||
                AutonomousRvrMobAvoidance.IsForbiddenRvrRegion(targetRegion) ||
                !(hasWayAround?.Invoke(edges) ?? HasWayAroundFrontierDungeons(edges, currentRegion, targetRegion)))
                return edges;
            return edges.Where(edge => !AutonomousRvrMobAvoidance.IsForbiddenRvrRegion(edge.TargetRegion)).ToArray();
        }

        /// <summary>Whether <paramref name="targetRegion"/> is reachable over <paramref name="edges"/> without entering a shared frontier dungeon.</summary>
        public static bool HasWayAroundFrontierDungeons(IReadOnlyList<DbZonePoint> edges, ushort currentRegion, ushort targetRegion)
        {
            var seen = new HashSet<ushort> { currentRegion };
            var queue = new Queue<ushort>();
            queue.Enqueue(currentRegion);
            while (queue.Count > 0)
            {
                ushort region = queue.Dequeue();
                if (region == targetRegion)
                    return true;
                foreach (DbZonePoint edge in edges)
                    if (edge.SourceRegion == region && !AutonomousRvrMobAvoidance.IsForbiddenRvrRegion(edge.TargetRegion) &&
                        seen.Add(edge.TargetRegion))
                        queue.Enqueue(edge.TargetRegion);
            }
            return false;
        }

        private static long DistanceSquared(int ax, int ay, int bx, int by)
        {
            long dx = (long)ax - bx;
            long dy = (long)ay - by;
            return dx * dx + dy * dy;
        }
    }
}
