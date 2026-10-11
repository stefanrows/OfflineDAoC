using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    public enum OpenBgRole { BridgeWatch, Roam, Lurk, Assault, Defend, Retreat }

    /// <summary>What a roster unit's role depends on: the cycle, its realm and the size of the group it plays as.</summary>
    public readonly record struct OpenBgContext(OpenBgPhase Phase, eRealm Attacker, eRealm Owner, eRealm Realm, int GroupSize, bool IsLeaderOfGroup);

    /// <summary>A bridge of the open BG map: its log name, the realm whose side it faces, and its deck floor.</summary>
    public readonly record struct BridgePoint(string Name, eRealm Realm, Point3D Point);

    /// <summary>
    /// Pure role rules of the open BG (spec 2026-10-11 §6). Roles belong to a roster unit (a group or a solo); a group's
    /// members follow their leader, so <see cref="OpenBgContext.IsLeaderOfGroup"/> only marks who the role was decided
    /// for (a member whose leader is down plays the same role). Every rule is a function of the phase, the realm, the
    /// keep owner, the group size, the bridges and the human positions; the director applies them.
    /// </summary>
    public static class OpenBattlegroundRoles
    {
        /// <summary>Solos (and stealthers) wait this far off a bridge: close enough to see it, not standing on it.</summary>
        public const int LurkMinDistance = 300;
        public const int LurkMaxDistance = 800;
        /// <summary>Roaming groups fight in the field, never on or next to a bridge.</summary>
        public const int RoamBridgeDistance = 1_500;
        /// <summary>Light player drift: a point within sight of a human, but never on top of one.</summary>
        public const int DriftSightRange = 6_000;
        public const int DriftMinDistance = 800;
        /// <summary>Persistent 40-50 bots leave humans below this level alone unless the human attacked first.</summary>
        public const int ProtectedHumanLevel = 30;
        public const int CrewMinimumLevel = 40;

        /// <summary>
        /// The three Thidranki bridges over the river around Thidranki Faste (zone 252 fixtures, deck floors read from
        /// the zone 252 navmesh; the river bed under each lies at about Z 2960). The names are cosmetic.
        /// </summary>
        public static readonly IReadOnlyList<BridgePoint> ThidrankiBridges = Array.AsReadOnly(new[]
        {
            new BridgePoint("E", eRealm.Midgard, new Point3D(38656, 36096, 3364)),
            new BridgePoint("NW", eRealm.Hibernia, new Point3D(30208, 34816, 3292)),
            new BridgePoint("SW", eRealm.Albion, new Point3D(29056, 41088, 3183)),
        });

        /// <summary>
        /// The role of one unit. Quiet: the realm's first group watches its own bridge, its other groups roam the field.
        /// Push: the attacking realm's groups assault (they muster at their bridge first), the others keep their quiet
        /// posts. Keep fight: the owner's groups defend, the attacker's assault, the third realm roams near the keep.
        /// Retreat: the owner holds, a failed attacker walks home, everyone else roams. Solos lurk off a bridge in quiet
        /// and push and roam afterwards. A realm that already owns the keep never assaults it.
        /// </summary>
        public static OpenBgRole Assign(OpenBgContext ctx, int groupIndexInRealm)
        {
            bool owner = ctx.Owner != eRealm.None && ctx.Realm == ctx.Owner;
            bool attacker = ctx.Realm == ctx.Attacker && !owner;
            if (ctx.GroupSize <= 1)
                return ctx.Phase is OpenBgPhase.Quiet or OpenBgPhase.Push ? OpenBgRole.Lurk : OpenBgRole.Roam;
            OpenBgRole quiet = groupIndexInRealm <= 0 ? OpenBgRole.BridgeWatch : OpenBgRole.Roam;
            return ctx.Phase switch
            {
                OpenBgPhase.Quiet => quiet,
                OpenBgPhase.Push => attacker ? OpenBgRole.Assault : quiet,
                OpenBgPhase.KeepFight => owner ? OpenBgRole.Defend : attacker ? OpenBgRole.Assault : OpenBgRole.Roam,
                _ => owner ? OpenBgRole.Defend : attacker ? OpenBgRole.Retreat : OpenBgRole.Roam,
            };
        }

        /// <summary>
        /// Camlann keeps are friendly by guild, not realm: a unit the keep counts as a friend (its guild or alliance, or
        /// anyone while the lord lies defeated) never assaults it and defends instead. Other roles are unchanged.
        /// </summary>
        public static OpenBgRole ForKeepFriendship(OpenBgRole role, bool keepExists, bool keepEnemy) =>
            role == OpenBgRole.Assault && keepExists && !keepEnemy ? OpenBgRole.Defend : role;

        /// <summary>The realm's own bridge, or the first bridge when the realm has none.</summary>
        public static BridgePoint? OwnBridge(eRealm realm, IReadOnlyList<BridgePoint> bridges)
        {
            if (bridges == null || bridges.Count == 0) return null;
            foreach (BridgePoint bridge in bridges)
                if (bridge.Realm == realm) return bridge;
            return bridges[0];
        }

        /// <summary>
        /// Where a role goes. Bridge watch: the realm's own bridge. Lurk: 300-800 units off a bridge (the own bridge half
        /// the time, any bridge otherwise), at the deck's height for the caller to snap to a floor. Roam: a field point at
        /// least 1,500 from every bridge, or null when no field point qualifies. Assault and defend: the keep. Retreat:
        /// the realm's own bridge, which lies on the way home to its portal keep.
        /// </summary>
        public static Point3D RolePoint(OpenBgRole role, eRealm realm, IReadOnlyList<BridgePoint> bridges, Point3D keep,
            Point3D[] fieldPoints, Random rng)
        {
            rng ??= Random.Shared;
            BridgePoint? own = OwnBridge(realm, bridges);
            switch (role)
            {
                case OpenBgRole.BridgeWatch:
                case OpenBgRole.Retreat:
                    return own?.Point ?? keep;
                case OpenBgRole.Lurk:
                {
                    if (own == null) return keep;
                    BridgePoint bridge = rng.Next(2) == 0 ? own.Value : bridges[rng.Next(bridges.Count)];
                    double angle = rng.NextDouble() * Math.PI * 2;
                    double distance = LurkMinDistance + rng.NextDouble() * (LurkMaxDistance - LurkMinDistance);
                    return new Point3D((int)Math.Round(bridge.Point.X + Math.Cos(angle) * distance),
                        (int)Math.Round(bridge.Point.Y + Math.Sin(angle) * distance), bridge.Point.Z);
                }
                case OpenBgRole.Roam:
                {
                    Point3D[] field = FieldAwayFromBridges(fieldPoints, bridges);
                    return field.Length == 0 ? null : field[rng.Next(field.Length)];
                }
                default:
                    return keep;
            }
        }

        /// <summary>The field points at least <see cref="RoamBridgeDistance"/> (2D) from every bridge.</summary>
        public static Point3D[] FieldAwayFromBridges(Point3D[] fieldPoints, IReadOnlyList<BridgePoint> bridges) =>
            (fieldPoints ?? Array.Empty<Point3D>())
                .Where(point => point != null && (bridges ?? Array.Empty<BridgePoint>())
                    .All(bridge => Distance2D(point, bridge.Point) >= RoamBridgeDistance))
                .ToArray();

        /// <summary>
        /// Light player drift (applied once per cycle per group by the director): a roam or lurk point that already lies
        /// within sight of a human (800-6,000) stays; otherwise the point moves to 1,200-4,500 units from the human
        /// nearest to it, on the side it came from with some spread, so the group drifts into sight without a straight
        /// line onto the player. Never within 800 of any human. Off, or without humans, the point is unchanged. Null only
        /// when the chosen point sits on a human and no free side was found; the caller then keeps its own point.
        /// </summary>
        public static Point3D DriftedPoint(Point3D chosen, IReadOnlyList<Point3D> humans, bool driftEnabled, Random rng)
        {
            if (!driftEnabled || chosen == null || humans == null) return chosen;
            Point3D[] present = humans.Where(human => human != null).ToArray();
            if (present.Length == 0) return chosen;
            rng ??= Random.Shared;
            Point3D nearest = present.OrderBy(human => Distance2D(human, chosen)).First();
            double current = Distance2D(nearest, chosen);
            if (current >= DriftMinDistance && current <= DriftSightRange) return chosen;
            double baseAngle = current < 1 ? rng.NextDouble() * Math.PI * 2 : Math.Atan2(chosen.Y - nearest.Y, chosen.X - nearest.X);
            for (int attempt = 0; attempt < 16; attempt++)
            {
                // Spread widens with each attempt, so a crowd of humans still leaves a free side.
                double spread = Math.PI / 4 + attempt * Math.PI / 16;
                double angle = baseAngle + (rng.NextDouble() * 2 - 1) * spread;
                double distance = 1_200 + rng.NextDouble() * 3_300;
                var point = new Point3D((int)Math.Round(nearest.X + Math.Cos(angle) * distance),
                    (int)Math.Round(nearest.Y + Math.Sin(angle) * distance), nearest.Z);
                if (present.All(human => Distance2D(human, point) >= DriftMinDistance)) return point;
            }
            return current >= DriftMinDistance ? chosen : null;
        }

        /// <summary>
        /// Low-level protection: a crew bot (level 40 or more) does not pick a human below level 30 as its target unless
        /// that human (or its companion) attacked it or its group first. Self-defence always stays allowed.
        /// </summary>
        public static bool MayHuntHuman(int botLevel, int humanLevel, bool humanAttackedBot) =>
            humanAttackedBot || botLevel < CrewMinimumLevel || humanLevel >= ProtectedHumanLevel;

        /// <summary>The log name of a role.</summary>
        public static string Describe(OpenBgRole role) => role switch
        {
            OpenBgRole.BridgeWatch => "bridge_watch",
            OpenBgRole.Roam => "roam",
            OpenBgRole.Lurk => "lurk",
            OpenBgRole.Assault => "assault",
            OpenBgRole.Defend => "defend",
            _ => "retreat",
        };

        public static double Distance2D(Point3D a, Point3D b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
