using System;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// When and where a bot presses a Master Level ability (TOA_PLAN WP2.6). The owner's rule
    /// (2026-10-24): it must look like a decent player, not a machine. A bot notices the moment
    /// (a reaction chance per think), waits a human 1-3 seconds before the key press, presses at
    /// most one ability per think, respects every cooldown, leaves a pause before the next one,
    /// skips an ability that would be pointless, and now and then places a ground target a little
    /// off. Everything here is pure (positions, numbers, a random source), so it is unit-tested;
    /// BotBrain.MasterLevels gathers the world state and calls it.
    /// </summary>
    public static class BotMasterLevelCasting
    {
        /// <summary>Reaction time between noticing the moment and pressing the key.</summary>
        public const int REACTION_MIN_MS = 1_000;
        public const int REACTION_MAX_MS = 3_000;

        /// <summary>A decision not pressed within this long after its moment is dropped (the bot was busy).</summary>
        public const int PRESS_WINDOW_MS = 4_000;

        /// <summary>Pause after a press before the bot considers the next Master Level ability.</summary>
        public const int GAP_MIN_MS = 8_000;
        public const int GAP_MAX_MS = 16_000;

        /// <summary>Pause after a decision that came to nothing (moment gone, target moved away).</summary>
        public const int DROPPED_GAP_MS = 3_000;

        /// <summary>
        /// After an ability was judged pointless, it is not judged again for this long: a player
        /// glances at the situation now and then, and the world scans are not repeated every think.
        /// </summary>
        public const int NOT_NOW_MIN_MS = 4_000;
        public const int NOT_NOW_MAX_MS = 8_000;

        /// <summary>Standing within this distance of one spot this long counts as holding position.</summary>
        public const int HOLD_STILL_MS = 6_000;
        public const int HOLD_STILL_DRIFT = 64;

        /// <summary>A storm's and a trap's radius.</summary>
        public const int STORM_RADIUS = 350;

        /// <summary>Fewest enemies worth an offensive storm or a push.</summary>
        public const int CLUSTER_MIN = 2;

        /// <summary>Enemies seen this far away make a storm or a trap worth laying before contact.</summary>
        public const int ENEMY_SIGHT = 3_000;

        /// <summary>No enemy may be closer than this when a long (20 second) storm is started out of combat.</summary>
        public const int LONG_CAST_SAFE_DISTANCE = 800;

        /// <summary>A bot hit within this long does not start a long cast: it would be interrupted.</summary>
        public const int NOT_ATTACKED_MS = 8_000;

        /// <summary>No second storm of the same kind, or trap, of the bot's own within these distances.</summary>
        public const int STORM_SPACING = 700;
        public const int TRAP_SPACING = 500;

        /// <summary>A Stormlord keeps at most this many of its own storms on one spot (STORM_SPACING).</summary>
        public const int MAX_OWN_STORMS = 2;

        /// <summary>A storm farther than this from an enemy cluster is not worth pushing toward it.</summary>
        public const int STEER_MAX_DISTANCE = 1_000;

        /// <summary>The Vortex point sits this far behind the storm, on the side away from the enemies.</summary>
        public const int STEER_BEHIND = 150;

        /// <summary>The Battle Warder stands this far from the defended mate, toward the attacker.</summary>
        public const int WARDER_STEP = 60;

        /// <summary>An attacker this close to a group mate counts as fighting it in melee.</summary>
        public const int MELEE_REACH = 300;

        /// <summary>How long a bot stands still to keep its Battle Warder (it ends when the caster moves or casts).</summary>
        public const int WARDER_HOLD_MIN_MS = 8_000;
        public const int WARDER_HOLD_MAX_MS = 15_000;

        /// <summary>Fewest player-like enemies within <see cref="TITAN_SIGHT"/> worth a Crystal Titan.</summary>
        public const int TITAN_ENEMIES = 3;
        public const int TITAN_SIGHT = 1_500;

        /// <summary>Chance and size of a placement mistake: the ground target lands a little off.</summary>
        public const int MISTAKE_PERCENT = 20;
        public const int MISTAKE_MIN = 40;
        public const int MISTAKE_MAX = 120;

        /// <summary>
        /// The key can be pressed: the ability is off its cooldown (the timer the spell handler set
        /// through DisableSkill when it was last cast, shared timers included) and the bot has the power.
        /// </summary>
        public static bool IsReady(GameLiving caster, Spell spell, int powerCost) =>
            caster != null && spell != null && caster.GetSkillDisabledDuration(spell) <= 0 && caster.Mana >= powerCost;

        public static int ReactionDelay(Random random) => random.Next(REACTION_MIN_MS, REACTION_MAX_MS + 1);

        public static int GapAfterPress(Random random, int castTime) =>
            Math.Max(0, castTime) + random.Next(GAP_MIN_MS, GAP_MAX_MS + 1);

        public static int WarderHold(Random random) => random.Next(WARDER_HOLD_MIN_MS, WARDER_HOLD_MAX_MS + 1);

        #region Placement

        /// <summary>A group of enemies: the mean point of the members and how many there are.</summary>
        public readonly record struct Cluster(Point3D Centre, int Count);

        /// <summary>
        /// The densest group of <paramref name="points"/>: for each point, the points within
        /// <paramref name="radius"/> of it; the largest such set wins (the nearer to the list's
        /// start on a tie) and its mean is the centre. Null when no set has
        /// <paramref name="minCount"/> members.
        /// </summary>
        public static Cluster? BestCluster(IReadOnlyList<Point3D> points, int radius, int minCount)
        {
            if (points == null || points.Count < Math.Max(1, minCount))
                return null;

            long limit = (long)radius * radius;
            int bestCount = 0;
            long sumX = 0, sumY = 0, sumZ = 0;

            foreach (Point3D seed in points)
            {
                int count = 0;
                long x = 0, y = 0, z = 0;

                foreach (Point3D other in points)
                {
                    long dx = other.X - seed.X, dy = other.Y - seed.Y;

                    if (dx * dx + dy * dy > limit)
                        continue;

                    count++;
                    x += other.X;
                    y += other.Y;
                    z += other.Z;
                }

                if (count > bestCount)
                {
                    bestCount = count;
                    sumX = x;
                    sumY = y;
                    sumZ = z;
                }
            }

            if (bestCount < minCount)
                return null;

            return new Cluster(new Point3D((int)(sumX / bestCount), (int)(sumY / bestCount), (int)(sumZ / bestCount)), bestCount);
        }

        /// <summary>The Battle Warder's spot: at the defended mate's feet, one step toward the attacker.</summary>
        public static Point3D WarderSpot(Point3D mate, Point3D attacker) => Toward(mate, attacker, WARDER_STEP);

        /// <summary>
        /// Where a Stormlord sets Vacuum Vortex to push its storm into an enemy cluster: just
        /// behind the storm, on the side away from the enemies (Vortex pushes storms directly away
        /// from its point).
        /// </summary>
        public static Point3D SteerSpot(Point3D storm, Point3D cluster) => Toward(storm, cluster, -STEER_BEHIND);

        /// <summary><paramref name="from"/> moved <paramref name="distance"/> toward <paramref name="to"/> (away for a negative one), on the flat.</summary>
        public static Point3D Toward(Point3D from, Point3D to, int distance)
        {
            double dx = to.X - from.X, dy = to.Y - from.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);

            if (length < 1)
                return new Point3D(from.X, from.Y, from.Z);

            double step = distance > 0 ? Math.Min(distance, length) : distance;
            return new Point3D(from.X + (int)Math.Round(dx / length * step), from.Y + (int)Math.Round(dy / length * step), from.Z);
        }

        /// <summary>
        /// A player's aim: usually exact, in <see cref="MISTAKE_PERCENT"/> of the presses
        /// <see cref="MISTAKE_MIN"/>-<see cref="MISTAKE_MAX"/> units off in a random direction.
        /// </summary>
        public static Point3D Aim(Point3D point, Random random)
        {
            if (random.Next(100) >= MISTAKE_PERCENT)
                return new Point3D(point.X, point.Y, point.Z);

            double angle = random.NextDouble() * 2 * Math.PI;
            int off = random.Next(MISTAKE_MIN, MISTAKE_MAX + 1);
            return new Point3D(point.X + (int)Math.Round(Math.Cos(angle) * off), point.Y + (int)Math.Round(Math.Sin(angle) * off), point.Z);
        }

        /// <summary>
        /// The point on the walkable floor nearest <paramref name="point"/>, as the fork's movement
        /// code snaps (navmesh closest point, 64 across, 256 up and down), so a ground target never
        /// sits inside a wall or a rock. Without a navmesh for the zone the point is used as it is;
        /// null when the zone has a navmesh but no floor there.
        /// </summary>
        public static Point3D OnFloor(GameObject near, Point3D point)
        {
            Zone zone = near?.CurrentRegion?.GetZone(point.X, point.Y);

            if (zone == null)
                return null;

            IPathfindingMgr nav = PathfindingProvider.Instance;

            if (nav == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
                return new Point3D(point.X, point.Y, point.Z);

            Vector3? floor = nav.GetClosestPoint(zone, new Vector3(point.X, point.Y, point.Z), 64, 64, 256, nav.DefaultFilters);

            if (!floor.HasValue)
                return null;

            return new Point3D((int)Math.Round(floor.Value.X), (int)Math.Round(floor.Value.Y), (int)Math.Round(floor.Value.Z));
        }

        /// <summary>The caster sees the point (navmesh ray at chest height); true where the zone has no navmesh.</summary>
        public static bool InSight(GameLiving caster, Point3D point)
        {
            Zone zone = caster?.CurrentZone;

            if (zone == null)
                return false;

            IPathfindingMgr nav = PathfindingProvider.Instance;

            if (nav == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
                return true;

            return nav.HasLineOfSight(zone, new Vector3(caster.X, caster.Y, caster.Z + 48),
                new Vector3(point.X, point.Y, point.Z + 48), nav.DefaultFilters);
        }

        public static long DistanceSquared(Point3D a, Point3D b)
        {
            long dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        public static bool Within(Point3D a, Point3D b, int radius) => DistanceSquared(a, b) <= (long)radius * radius;

        #endregion

        #region When

        /// <summary>Out of combat, holding position, before contact: is a long storm worth starting?</summary>
        public static bool WantsStormBeforeContact(bool holding, int enemiesSeen, int nearestEnemy, bool sameStormNear, int minEnemies) =>
            holding && !sameStormNear && enemiesSeen >= minEnemies && nearestEnemy > LONG_CAST_SAFE_DISTANCE;

        /// <summary>In combat: an offensive storm at the bot's feet when enemies cluster there and nobody is hitting it.</summary>
        public static bool WantsStormInFight(Cluster? cluster, Point3D self, bool attackedRecently, bool sameStormNear) =>
            !attackedRecently && !sameStormNear && cluster is { Count: >= CLUSTER_MIN } c && Within(c.Centre, self, STORM_RADIUS);

        /// <summary>In combat: Dazzling Array for group mates fighting around the bot while it is left alone.</summary>
        public static bool WantsDefensiveStormInFight(int matesFightingAround, bool attackedRecently, bool sameStormNear) =>
            !attackedRecently && !sameStormNear && matesFightingAround >= 2;

        /// <summary>
        /// A storm worth pushing: the cluster is outside the storm (not already covered) but within
        /// <see cref="STEER_MAX_DISTANCE"/>, and big enough.
        /// </summary>
        public static bool WorthSteering(Point3D storm, Cluster? cluster) =>
            cluster is { Count: >= CLUSTER_MIN } c && !Within(storm, c.Centre, STORM_RADIUS) && Within(storm, c.Centre, STEER_MAX_DISTANCE);

        /// <summary>A trap is worth laying: holding position on an RvR spot with none of the bot's own nearby.</summary>
        public static bool WantsTrap(bool holding, bool rvr, bool ownTrapNear) => holding && rvr && !ownTrapNear;

        #endregion
    }

    /// <summary>What the Master Level step needs from a bot; BotBrain implements it, tests fake it.</summary>
    public interface IBotMasterLevelActor
    {
        /// <summary>The held abilities the AI may press (BotMasterLevels.UseOf is not None).</summary>
        IReadOnlyList<Spell> MasterLevelSpells { get; }

        /// <summary>Casting, a cast queued, or unable to act: no key press now.</summary>
        bool IsBusyForMasterLevel { get; }

        /// <summary>Off cooldown and affordable (BotMasterLevelCasting.IsReady).</summary>
        bool IsMasterLevelReady(Spell spell);

        /// <summary>The moment is there; <paramref name="spot"/> is the ground target, null when none is used.</summary>
        bool WantsMasterLevel(BotMasterLevelUse use, Spell spell, bool combat, out Point3D spot);

        /// <summary>Sets the ground target a player would (aim, floor, sight, range). False when no point is left.</summary>
        bool AimMasterLevel(Spell spell, Point3D spot);

        /// <summary>Starts the cast. False when the spell handler refused it.</summary>
        bool CastMasterLevel(BotMasterLevelUse use, Spell spell);
    }

    /// <summary>
    /// The Master Level decision loop of one bot (TOA_PLAN WP2.6): notice, wait the reaction
    /// time, look again, press once, pause. <see cref="Think"/> runs once per brain think and
    /// returns true only on the think that pressed.
    /// </summary>
    public sealed class BotMasterLevelStep
    {
        /// <summary>Abilities judged per think, which also bounds the world scans.</summary>
        public const int JUDGED_PER_THINK = 2;

        private readonly Dictionary<int, long> _notBefore = new();
        private readonly Random _random;
        private readonly Func<int, bool> _notices;

        public BotMasterLevelStep(Random random = null, Func<int, bool> notices = null)
        {
            _random = random ?? Random.Shared;
            _notices = notices ?? (chance => _random.Next(100) < chance);
        }

        public BotMasterLevelPlan Plan { get; } = new();

        /// <summary>The press of this think, if any. At most one ability per think.</summary>
        public bool Think(IBotMasterLevelActor actor, bool combat, long now)
        {
            IReadOnlyList<Spell> spells = actor?.MasterLevelSpells;

            if (spells == null || spells.Count == 0 || actor.IsBusyForMasterLevel)
                return false;

            if (Plan.IsPending)
                return Press(actor, combat, now);

            if (Plan.CanDecide(now))
                Decide(actor, spells, combat, now);

            // Deciding is not pressing: the press comes on a later think, after the reaction time.
            return false;
        }

        private void Decide(IBotMasterLevelActor actor, IReadOnlyList<Spell> spells, bool combat, long now)
        {
            // A different order every think: a player does not always reach for the same key first.
            int start = _random.Next(spells.Count);
            int judged = 0;

            for (int i = 0; i < spells.Count && judged < JUDGED_PER_THINK; i++)
            {
                Spell spell = spells[(start + i) % spells.Count];
                BotMasterLevelUse use = BotMasterLevels.UseOf(spell);

                if (!BotMasterLevels.WorksIn(use, combat) || (_notBefore.TryGetValue(spell.ID, out long notBefore) && now < notBefore) ||
                    !actor.IsMasterLevelReady(spell) || !_notices(BotMasterLevels.ReactionChance(use)))
                    continue;

                judged++;

                if (!actor.WantsMasterLevel(use, spell, combat, out _))
                {
                    _notBefore[spell.ID] = now + _random.Next(BotMasterLevelCasting.NOT_NOW_MIN_MS, BotMasterLevelCasting.NOT_NOW_MAX_MS + 1);
                    continue;
                }

                Plan.Decide(spell, use, now, _random);
                return;
            }
        }

        private bool Press(IBotMasterLevelActor actor, bool combat, long now)
        {
            if (!Plan.IsDue(now))
                return false;

            Spell spell = Plan.Spell;
            BotMasterLevelUse use = Plan.Use;

            // The situation is judged again at the key press, as a player looks again before pressing.
            if (Plan.IsStale(now) || !BotMasterLevels.WorksIn(use, combat) || !actor.IsMasterLevelReady(spell) ||
                !actor.WantsMasterLevel(use, spell, combat, out Point3D spot) ||
                (spot != null && !actor.AimMasterLevel(spell, spot)) ||
                !actor.CastMasterLevel(use, spell))
            {
                Plan.Drop(now);
                return false;
            }

            Plan.Pressed(now, _random);
            return true;
        }
    }

    /// <summary>
    /// The one Master Level press a bot has decided on and not yet made (TOA_PLAN WP2.6). There is
    /// never more than one: a bot that has noticed a moment waits its reaction time, presses (or
    /// drops the idea when the moment has passed) and then pauses before the next decision.
    /// </summary>
    public sealed class BotMasterLevelPlan
    {
        public Spell Spell { get; private set; }
        public BotMasterLevelUse Use { get; private set; }

        /// <summary>The earliest press: decision time plus the reaction delay.</summary>
        public long DueTick { get; private set; }

        /// <summary>No new decision before this tick (the pause after a press or a dropped idea).</summary>
        public long NextDecisionTick { get; private set; }

        public bool IsPending => Spell != null;

        public bool CanDecide(long now) => !IsPending && now >= NextDecisionTick;

        public bool IsDue(long now) => IsPending && now >= DueTick;

        /// <summary>The moment passed while the bot was busy: too late to press.</summary>
        public bool IsStale(long now) => IsPending && now > DueTick + BotMasterLevelCasting.PRESS_WINDOW_MS;

        public void Decide(Spell spell, BotMasterLevelUse use, long now, Random random)
        {
            Spell = spell;
            Use = use;
            DueTick = now + BotMasterLevelCasting.ReactionDelay(random);
        }

        public void Pressed(long now, Random random)
        {
            int castTime = Spell?.CastTime ?? 0;
            Clear();
            NextDecisionTick = now + BotMasterLevelCasting.GapAfterPress(random, castTime);
        }

        public void Drop(long now)
        {
            Clear();
            NextDecisionTick = now + BotMasterLevelCasting.DROPPED_GAP_MS;
        }

        private void Clear()
        {
            Spell = null;
            Use = BotMasterLevelUse.None;
            DueTick = 0;
        }
    }
}
