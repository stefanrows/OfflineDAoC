using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>Immutable region snapshots, populated by the existing world-load query.
    /// Death timers must never block on SQLite or race against character saves.</summary>
    public static class BotReleaseBindPoints
    {
        private sealed record Bind(Point3D Point, eRealm Realm);
        private static readonly ConcurrentDictionary<ushort, Bind[]> Regions = new();

        public static void Replace(ushort region, IEnumerable<DbBindPoint> points) =>
            Regions[region] = points.Select(p => new Bind(new Point3D(p.X, p.Y, p.Z), Owner(region, (eRealm)p.Realm))).ToArray();

        public static void Replace(Region region, IEnumerable<DbBindPoint> points, IPathfindingMgr nav)
        {
            // Once at startup, not every death. Reject mesh islands/prop tops
            // instead of repeatedly releasing bots onto the same bad bind.
            Regions[region.ID] = points.Select(p => new Bind(Resolve(nav, region.GetZone(p.X, p.Y),
                    new(p.X, p.Y, p.Z)), Owner(region.ID, (eRealm)p.Realm)))
                .Where(p => p.Point != null).ToArray();
        }

        // Frontier access is not permission to bind at the host realm's border
        // keep. Classic data often leaves those stones tagged Realm=None.
        // Explicit realm tags still allow the invader's own portal bindstones.
        public static eRealm Owner(ushort region, eRealm declaredRealm) => declaredRealm != eRealm.None
            ? declaredRealm : AutonomousWorldBotController.ProtectedRealm(region, ushort.MaxValue);

        public static Point3D? Resolve(IPathfindingMgr nav, Zone zone, Vector3 anchor)
        {
            if (zone == null) return null;
            if (!nav.IsAvailable || !nav.HasNavmesh(zone))
                return new Point3D((int)anchor.X, (int)anchor.Y, (int)anchor.Z);
            Vector3? floor = nav.GetClosestPoint(zone, anchor, 48, 48, 128, nav.DefaultFilters);
            if (!floor.HasValue || !AutonomousRendezvousNavigation.HasLocalExit(nav, zone, floor.Value))
                return null;
            return new Point3D((int)System.Math.Round(floor.Value.X),
                (int)System.Math.Round(floor.Value.Y), (int)System.Math.Round(floor.Value.Z));
        }

        /// <summary>
        /// Whether a point lies within <paramref name="radius"/> of one of the
        /// realm's own bindstones in that region (bug 63: bind-point peace).
        /// </summary>
        public static bool IsNearOwnBind(ushort region, float x, float y, eRealm realm, int radius)
        {
            if (realm == eRealm.None || !Regions.TryGetValue(region, out Bind[] points)) return false;
            float limit = (float)radius * radius;
            foreach (Bind bind in points)
            {
                if (bind.Realm != realm) continue;
                float dx = x - bind.Point.X, dy = y - bind.Point.Y;
                if (dx * dx + dy * dy <= limit) return true;
            }
            return false;
        }

        /// <summary>One bounded floor/local-exit probe; reject spread outside sanctuary.</summary>
        public static Point3D SpreadSafe(ushort region, Point3D anchor, long actor)
        {
            double angle = (unchecked((ulong)actor) % 24) * System.Math.PI * 2 / 24;
            Vector3 raw = new(anchor.X + (float)System.Math.Cos(angle) * 240,
                anchor.Y + (float)System.Math.Sin(angle) * 240, anchor.Z);
            Point3D spread = Resolve(PathfindingProvider.Instance, WorldMgr.GetRegion(region)?.GetZone((int)raw.X, (int)raw.Y), raw);
            return DOL.GS.ServerRules.PvpCombatant.IsSafeReleasePoint(region, spread) ? spread : anchor;
        }

        public static Point3D? Nearest(ushort region, int x, int y, eRealm realm = eRealm.None,
            ushort preferredZone = 0, bool safeOnly = false)
        {
            if (!Regions.TryGetValue(region, out Bind[] points)) return null;
            Point3D? nearest = null;
            Point3D? nearestInZone = null;
            double best = double.MaxValue;
            double bestInZone = double.MaxValue;
            Region currentRegion = preferredZone == 0 ? null : WorldMgr.GetRegion(region);
            foreach (Bind bind in points)
            {
                // Unclassified stones fail closed for autonomous bots. The
                // existing safe-capital fallback handles a missing friendly bind.
                if (realm != eRealm.None && bind.Realm != realm) continue;
                Point3D point = bind.Point;
                if (safeOnly && !DOL.GS.ServerRules.PvpCombatant.IsSafeReleasePoint(region, point)) continue;
                double dx = (double)x - point.X, dy = (double)y - point.Y;
                double distance = dx * dx + dy * dy;
                if (distance < best)
                {
                    best = distance;
                    nearest = point;
                }
                if (currentRegion?.GetZone(point.X, point.Y)?.ID == preferredZone && distance < bestInZone)
                {
                    bestInZone = distance;
                    nearestInZone = point;
                }
            }
            Point3D? selected = nearestInZone ?? nearest;
            return selected == null ? null : new Point3D(selected.X, selected.Y, selected.Z);
        }
    }
}
