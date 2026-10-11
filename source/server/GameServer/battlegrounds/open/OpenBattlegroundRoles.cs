using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    public enum OpenBgRole { BridgeWatch, Roam, Lurk, Assault, Defend, Retreat }

    /// <summary>
    /// What a roster unit's role depends on: the cycle's phase, whether the unit is in this cycle's keep raid, whether
    /// the keep counts it as a friend (its claiming guild or alliance; the raid while the lord lies defeated), and the
    /// size of the group it plays as. <paramref name="Realm"/> is geography only (its own bridge and landing); no role
    /// depends on a realm (owner decision 2026-10-11: alliances in the open BG are group, guild and battlegroup).
    /// </summary>
    public readonly record struct OpenBgContext(OpenBgPhase Phase, bool IsRaid, bool IsKeepFriend, eRealm Realm, int GroupSize, bool IsLeaderOfGroup);

    /// <summary>A bridge of the open BG map: its log name, the realm whose side it faces, and its deck floor.</summary>
    public readonly record struct BridgePoint(string Name, eRealm Realm, Point3D Point);

    /// <summary>
    /// Pure role rules of the open BG (spec 2026-10-11 §6). Roles belong to a roster unit (a group or a solo); a group's
    /// members follow their leader, so <see cref="OpenBgContext.IsLeaderOfGroup"/> only marks who the role was decided
    /// for (a member whose leader is down plays the same role). Every rule is a function of the phase, the raid, the
    /// keep's friendship, the group size, the bridges and the human positions; the director applies them.
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
        /// Bystanders (units neither in the raid nor friends of the keep) keep at least this far (2D) from the keep while
        /// it is fought over, so the keep fight is the raid against the keep's friends (live run 0.243.0: the third realm
        /// roamed within 3,600 and ground the attack down).
        /// </summary>
        public const int BystanderKeepDistance = 3_000;

        /// <summary>
        /// The role of one unit. Quiet: the realm's first group watches its own bridge, its other groups roam the field.
        /// Push: every raid unit (8-man, small groups and solos, of any realm) assaults; it musters at its own realm's
        /// bridge first, then marches on the keep, where the raid gathers at the gate; the others keep their quiet posts. Keep fight: the keep's friends defend (their
        /// solos roam), every raid unit assaults (solos and small groups join the siege as helpers), bystanders roam away
        /// from the keep. Retreat: the friends hold, a raid that failed walks home, everyone else roams. Other solos lurk
        /// off a bridge in quiet and push and roam afterwards. A keep friend never assaults the keep, even in the raid.
        /// </summary>
        public static OpenBgRole Assign(OpenBgContext ctx, int groupIndexInRealm)
        {
            bool friend = ctx.IsKeepFriend;
            bool raid = ctx.IsRaid && !friend;
            if (ctx.GroupSize <= 1)
            {
                if (raid && ctx.Phase is OpenBgPhase.Push or OpenBgPhase.KeepFight) return OpenBgRole.Assault;
                return ctx.Phase is OpenBgPhase.Quiet or OpenBgPhase.Push ? OpenBgRole.Lurk : OpenBgRole.Roam;
            }
            OpenBgRole quiet = groupIndexInRealm <= 0 ? OpenBgRole.BridgeWatch : OpenBgRole.Roam;
            return ctx.Phase switch
            {
                OpenBgPhase.Quiet => quiet,
                OpenBgPhase.Push => raid ? OpenBgRole.Assault : quiet,
                OpenBgPhase.KeepFight => friend ? OpenBgRole.Defend : raid ? OpenBgRole.Assault : OpenBgRole.Roam,
                _ => friend ? OpenBgRole.Defend : raid ? OpenBgRole.Retreat : OpenBgRole.Roam,
            };
        }

        /// <summary>
        /// Whether the roles treat a unit as a friend of the keep: its claiming guild or alliance; and, once the lord fell
        /// in the keep fight or retreat and nobody has claimed it yet, the raid, which holds the field until a claim.
        /// </summary>
        public static bool ActsAsKeepFriend(bool keepFriend, bool inRaid, bool lordDefeatedUnclaimed, OpenBgPhase phase) =>
            keepFriend || inRaid && lordDefeatedUnclaimed && phase is OpenBgPhase.KeepFight or OpenBgPhase.Retreat;

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

        /// <summary>The field points away from the bridges that also lie at least <paramref name="keepDistance"/> (2D) from the keep.</summary>
        public static Point3D[] FieldAwayFromKeep(Point3D[] fieldPoints, IReadOnlyList<BridgePoint> bridges, Point3D keep, int keepDistance) =>
            FieldAwayFromBridges(fieldPoints, bridges).Where(point => keep == null || Distance2D(point, keep) >= keepDistance).ToArray();

        /// <summary>
        /// Keep 11's static archers aggro at 2,100 and its static casters at 1,850, and they stand up to 1,260 from
        /// the keep centre (3,200 reach with line of sight; melee guards only 1,000), so a unit the guards count as an
        /// enemy holds the approaches this far (2D) from the keep centre and no nearer.
        /// </summary>
        public const int ApproachMinDistance = 3_400;
        /// <summary>The approaches end here (the bridges lie 4,600-5,800 out): a field point farther out no longer covers the keep.</summary>
        public const int ApproachMaxDistance = 4_400;

        /// <summary>Field points between <see cref="ApproachMinDistance"/> and <see cref="ApproachMaxDistance"/> (2D) of the keep.</summary>
        public static Point3D[] ApproachPoints(Point3D[] fieldPoints, Point3D keep) =>
            (fieldPoints ?? Array.Empty<Point3D>()).Where(point => point != null && keep != null &&
                Distance2D(point, keep) >= ApproachMinDistance && Distance2D(point, keep) <= ApproachMaxDistance).ToArray();

        /// <summary>
        /// After a raid guild claimed the keep in the fight (or the retreat), the raid's other units (not friends of the
        /// keep, whose guards still count them as enemies) defend from the approaches instead of walking off as bystanders.
        /// </summary>
        public static bool RaidPartnerHolds(bool raidTookKeep, bool inRaidList, bool keepFriend) =>
            raidTookKeep && inRaidList && !keepFriend;

        /// <summary>A unit neither in the raid nor a friend of the keep, while the keep is fought over.</summary>
        public static bool IsBystanderInFight(OpenBgPhase phase, bool isRaid, bool isKeepFriend) =>
            phase == OpenBgPhase.KeepFight && !isRaid && !isKeepFriend;

        /// <summary>
        /// An assaulting unit musters at its bridge only in the push and only once per cycle. In the keep fight it never
        /// musters again: a unit that wiped and released goes back to the keep (its gate) after its landing rest.
        /// </summary>
        public static bool NeedsMuster(OpenBgRole role, OpenBgPhase phase, bool musteredThisCycle) =>
            role == OpenBgRole.Assault && phase == OpenBgPhase.Push && !musteredThisCycle;

        /// <summary>A lurk spot within this distance (2D) of a spot that already failed this cycle is not picked again.</summary>
        public const int DeadLurkSpotRadius = 200;

        /// <summary>True when the point lies within <see cref="DeadLurkSpotRadius"/> of a lurk spot that failed this cycle.</summary>
        public static bool NearDeadSpot(Point3D point, IEnumerable<Point3D> deadSpots) =>
            point != null && deadSpots != null && deadSpots.Any(dead => dead != null && Distance2D(dead, point) <= DeadLurkSpotRadius);

        /// <summary>
        /// A lurk point with a proof: each draw (a raw point off a bridge) is snapped to the walkable floor, and the floor
        /// counts only when it is not near a spot that failed this cycle and a route from the realm's landing reaches it
        /// (live run 0.243.0: 61 lurk orders snapped onto dead-end floors). Null when no draw passes.
        /// </summary>
        public static Point3D PickLurkPoint(Func<Point3D> draw, Func<Point3D, Point3D> snap, Func<Point3D, bool> provenFromLanding,
            IEnumerable<Point3D> deadSpots, int attempts)
        {
            if (draw == null || snap == null) return null;
            Point3D[] dead = deadSpots?.Where(spot => spot != null).ToArray() ?? Array.Empty<Point3D>();
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                Point3D raw = draw();
                Point3D floor = raw == null ? null : snap(raw);
                if (floor == null || NearDeadSpot(raw, dead) || NearDeadSpot(floor, dead)) continue;
                if (provenFromLanding == null || provenFromLanding(floor)) return floor;
            }
            return null;
        }

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
        /// that human (or its group or companions) attacked it or its group first. Self-defence always stays allowed.
        /// </summary>
        public static bool MayHuntHuman(int botLevel, int humanLevel, bool humanAttackedBot) =>
            humanAttackedBot || botLevel < CrewMinimumLevel || humanLevel >= ProtectedHumanLevel;

        /// <summary>
        /// The one target rule for an open-BG crew bot, shared by the driver's opponent scan and the class brain's PvP
        /// crowd control (mez sweep, healer area stun): inside the open BG a human-side target (a player or a human's
        /// companion) is engaged only under <see cref="MayHuntHuman"/>. Outside the open BG (brackets, the world) and
        /// for every other target the rule never applies.
        /// </summary>
        public static bool MayEngageInOpenBattleground(bool openBattlegroundRule, bool humanSide, int botLevel, int targetLevel, bool attacked) =>
            !openBattlegroundRule || !humanSide || MayHuntHuman(botLevel, targetLevel, attacked);

        /// <summary>True when the point lies at least <see cref="DriftMinDistance"/> (2D) from every human.</summary>
        public static bool ClearOfHumans(Point3D point, IReadOnlyList<Point3D> humans) =>
            point != null && (humans ?? Array.Empty<Point3D>()).All(human => human == null || Distance2D(human, point) >= DriftMinDistance);

        /// <summary>
        /// A non-drift point for a unit whose drift left it within <see cref="DriftMinDistance"/> of a human: a random
        /// candidate clear of every human, else the candidate farthest from its nearest human. Null without candidates.
        /// </summary>
        public static Point3D PointClearOfHumans(Point3D[] candidates, IReadOnlyList<Point3D> humans, Random rng)
        {
            Point3D[] present = (candidates ?? Array.Empty<Point3D>()).Where(point => point != null).ToArray();
            if (present.Length == 0) return null;
            Point3D[] clear = present.Where(point => ClearOfHumans(point, humans)).ToArray();
            if (clear.Length > 0) return clear[(rng ?? Random.Shared).Next(clear.Length)];
            Point3D[] people = (humans ?? Array.Empty<Point3D>()).Where(human => human != null).ToArray();
            return present.OrderByDescending(point => people.Length == 0 ? double.MaxValue : people.Min(human => Distance2D(human, point))).First();
        }

        /// <summary>
        /// The point a drifting unit walks to. The floor-snapped drift point counts only when it is still clear of every
        /// human; otherwise the unit keeps its own chosen point when that is clear, and takes a non-drift point
        /// (<paramref name="undrifted"/>) when its own point sits within <see cref="DriftMinDistance"/> of a human, so
        /// neither a failed drift nor a snap ever leaves it on top of a player. The chosen point stays only when no
        /// other point exists at all.
        /// </summary>
        public static Point3D AfterDrift(Point3D chosen, Point3D snappedDrift, IReadOnlyList<Point3D> humans, Func<Point3D> undrifted, out bool drifted)
        {
            drifted = false;
            if (snappedDrift != null && ClearOfHumans(snappedDrift, humans))
            {
                drifted = true;
                return snappedDrift;
            }
            if (chosen == null || ClearOfHumans(chosen, humans)) return chosen;
            return undrifted?.Invoke() ?? chosen;
        }

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
