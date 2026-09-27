using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>
/// Live glue for <see cref="AutonomousRvrDoctrine"/>: reads a warband's
/// doctrine from its members, applies the combat habits to real targets, and
/// runs the optional retreat. Autonomous RvR world bots only; companions and
/// PvE parties are untouched.
/// </summary>
public static class AutonomousRvrDoctrineRuntime
{
    private sealed class GroupState
    {
        public RvrDoctrine Doctrine;
        public int MemberCount;
        public long RefreshAfter;
        public int FormedMembers;
        public int FormedHealers;
        public long RetreatUntil;
        public Vector3 RetreatPoint;
        public long NextRetreatCheck;
    }

    private sealed class HabitState
    {
        public GameLiving Target;
        public long ChosenAt;
    }

    private static readonly ConditionalWeakTable<Group, GroupState> Groups = new();
    private static readonly ConditionalWeakTable<GameBot, HabitState> Habits = new();
    private const int DoctrineRefreshMilliseconds = 60_000;
    private const int CallerRange = 2_000;

    public static bool Applies(GameBot bot) => bot?.IsAutonomousWorldBot == true && !bot.IsPlayerLedGroup &&
        !bot.IsTemporaryGroupHelper && AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR);

    public static RvrDoctrine For(GameBot bot)
    {
        if (!Applies(bot))
            return null;
        if (bot.Group == null)
            return AutonomousRvrDoctrine.Derive([Member(bot)], AutonomousPlayerBehavior.TypeOf(bot.PersistentRecord),
                Traits(bot), AutonomousRvrDefense.IsCommittedSiegeFighter(bot), AutonomousRvrDefense.IsCommittedDefender(bot));
        return State(bot.Group).Doctrine;
    }

    private static GroupState State(Group group)
    {
        GroupState state = Groups.GetOrCreateValue(group);
        lock (state)
        {
            GameBot[] members = group.GetMembersInTheGroup().OfType<GameBot>().ToArray();
            long now = GameLoop.GameLoopTime;
            if (state.Doctrine == null || members.Length != state.MemberCount || now >= state.RefreshAfter)
            {
                GameBot leader = group.LivingLeader as GameBot ?? members.FirstOrDefault();
                state.Doctrine = AutonomousRvrDoctrine.Derive(members.Select(Member).ToArray(),
                    AutonomousPlayerBehavior.TypeOf(leader?.PersistentRecord), Traits(leader),
                    AutonomousRvrDefense.IsCommittedSiegeFighter(leader), AutonomousRvrDefense.IsCommittedDefender(leader));
                if (members.Length > state.MemberCount)
                {
                    state.FormedMembers = members.Length;
                    state.FormedHealers = members.Count(IsHealer);
                }
                state.MemberCount = members.Length;
                state.RefreshAfter = now + DoctrineRefreshMilliseconds;
            }
            return state;
        }
    }

    private static RvrDoctrineMember Member(GameBot bot) =>
        new((eCharacterClass)(bot.CharacterClass?.ID ?? 0), bot.Level);

    private static RvrLeaderTraits Traits(GameBot bot) => bot?.PersistentRecord is { } record
        ? new(record.Aggression, record.RiskTolerance, record.Patience)
        : RvrLeaderTraits.Neutral;

    private static bool IsHealer(GameLiving member) => member is GameBot bot &&
        AutonomousRvrDoctrine.TraitsOf((eCharacterClass)(bot.CharacterClass?.ID ?? 0)).HasFlag(RvrClassTraits.Healer);

    private static RvrClassTraits TraitsOf(GameLiving living)
    {
        GameLiving identity = PvpCombatant.Resolve(living) ?? living;
        int classId = identity switch
        {
            GamePlayer player => player.CharacterClass?.ID ?? 0,
            GameBot bot => bot.CharacterClass?.ID ?? 0,
            _ => 0,
        };
        return classId == 0 ? RvrClassTraits.None : AutonomousRvrDoctrine.TraitsOf((eCharacterClass)classId);
    }

    // ---------------------------------------------------------------- targets

    /// <summary>The group leader's current PvP target when the leader is actually fighting it.</summary>
    public static GameLiving CallerTarget(GameBot bot)
    {
        if (bot?.Group?.LivingLeader is not GameLiving leader || leader == bot || !leader.IsAlive ||
            leader.TargetObject is not GameLiving target || !target.IsAlive ||
            !(leader.IsAttacking || leader.IsCasting) || !PvpCombatant.IsPlayerShaped(target) ||
            target.CurrentRegionID != bot.CurrentRegionID || !bot.IsWithinRadius(target, CallerRange) ||
            !GameServer.ServerRules.IsAllowedToAttack(bot, target, true))
            return null;
        return target;
    }

    /// <summary>
    /// Picks a target from <paramref name="candidates"/> by the group's habits.
    /// Returns null when the habits do not apply, so callers keep their own rule.
    /// </summary>
    public static GameLiving Choose(GameBot bot, IReadOnlyList<GameLiving> candidates, GameLiving current)
    {
        RvrDoctrine doctrine = For(bot);
        if (doctrine == null || candidates == null || candidates.Count == 0)
            return null;

        GameLiving caller = doctrine.HasCaller ? CallerTarget(bot) : null;
        List<GameLiving> pool = candidates.Where(candidate => candidate != null).Distinct().ToList();
        if (caller != null && !pool.Contains(caller))
            pool.Add(caller);

        HabitState habit = Habits.GetOrCreateValue(bot);
        long now = GameLoop.GameLoopTime;
        GameLiving held = current != null && pool.Contains(current) ? current :
            habit.Target != null && pool.Contains(habit.Target) ? habit.Target : null;
        int patience = bot.PersistentRecord?.Patience ?? 50;
        bool keep = held != null && now - habit.ChosenAt < AutonomousRvrDoctrine.StickMilliseconds(doctrine, patience);

        DateTime nowUtc = WorldSimulationClock.UtcNow;
        HashSet<GameLiving> ours = bot.Group?.GetMembersInTheGroup().ToHashSet() ?? [bot];
        RvrTargetView[] views = pool.Select(target => new RvrTargetView(
                TraitsOf(target),
                bot.GetDistanceTo(target),
                target.HealthPercent,
                target == caller,
                target.TargetObject is GameLiving victim && ours.Contains(victim) && victim != bot &&
                    (IsHealer(victim) || TraitsOf(victim).HasFlag(RvrClassTraits.Caster)),
                AutonomousGuildGrudgeMemory.IsActiveTarget(bot, target, nowUtc),
                PvpCombatant.Resolve(target) != target))
            .ToArray();

        int index = AutonomousRvrCombatHabits.Choose(views, held == null ? -1 : pool.IndexOf(held), keep, doctrine,
            Random.Shared.NextDouble(), Random.Shared.NextDouble());
        if (index < 0)
            return null;
        GameLiving chosen = pool[index];
        if (chosen != habit.Target || !keep)
        {
            habit.Target = chosen;
            habit.ChosenAt = now;
        }
        return chosen;
    }

    // ---------------------------------------------------------- fight appetite

    /// <summary>
    /// Whether this bot's group takes on the visible enemy party. Null when the
    /// doctrine does not apply, so callers keep the plain strength check.
    /// </summary>
    public static bool? AcceptsFight(GameBot actor, int ownCount, int ownLevel, int enemyCount, int enemyLevel,
        GameLiving enemy)
    {
        RvrDoctrine doctrine = For(actor);
        if (doctrine == null)
            return null;
        // Stable for a minute per pair, so the decision does not flicker each scan.
        long minute = GameLoop.GameLoopTime / 60_000;
        int seed = HashCode.Combine(actor.Group != null ? RuntimeHelpers.GetHashCode(actor.Group) : actor.ObjectID,
            PvpCombatant.Resolve(enemy)?.ObjectID ?? enemy?.ObjectID ?? 0, minute);
        double dareRoll = (uint)seed % 1000 / 1000d;
        // Recent history against that guild makes a crew bolder or warier.
        double history = AutonomousGuildEncounterMemory.AppetiteFactor(actor, enemy, WorldSimulationClock.UtcNow);
        return AutonomousRvrDoctrine.AcceptsFight(doctrine with { Appetite = doctrine.Appetite * history },
            ownCount, ownLevel, enemyCount, enemyLevel, dareRoll);
    }

    // --------------------------------------------------------------- formation

    /// <summary>
    /// The travel slot for an RvR group member while its leader is moving, or
    /// null to keep the standing ring (clump doctrines, standing leader).
    /// </summary>
    public static Vector3? TravelFormationPoint(GameBot bot, Vector3 center)
    {
        RvrDoctrine doctrine = For(bot);
        if (doctrine == null || doctrine.Travel == RvrTravelShape.Clump || bot.Group?.LivingLeader is not GameLiving leader ||
            leader == bot || !leader.IsMoving)
            return null;
        GameBot[] followers = bot.Group.GetMembersInTheGroup().OfType<GameBot>()
            .Where(member => member != leader && member.IsAlive)
            .OrderBy(member => AutonomousRvrDoctrineGeometry.MarchRank(TraitsOf(member)))
            .ThenBy(member => member.DatabaseID > 0 ? member.DatabaseID : member.ObjectID)
            .ToArray();
        int slot = Array.IndexOf(followers, bot);
        if (slot < 0)
            return null;
        Point2D ahead = leader.GetPointFromHeading(leader.Heading, 100);
        Vector2 forward = new(ahead.X - leader.X, ahead.Y - leader.Y);
        return AutonomousRvrDoctrineGeometry.TravelSlot(doctrine.Travel, slot, followers.Length, center, forward);
    }

    // ----------------------------------------------------------------- retreat

    public static bool IsRetreating(GameBot bot, out Vector3 point)
    {
        point = default;
        if (!Applies(bot) || bot.Group == null || !Groups.TryGetValue(bot.Group, out GroupState state))
            return false;
        lock (state)
        {
            point = state.RetreatPoint;
            return GameLoop.GameLoopTime < state.RetreatUntil;
        }
    }

    /// <summary>
    /// The leader weighs the fight every few seconds; a decided retreat is a
    /// short run away from the enemy's centre, then the normal regroup.
    /// </summary>
    public static void EvaluateRetreat(GameBot leader)
    {
        if (!Applies(leader) || leader.Group?.LivingLeader != leader || leader.CurrentZone == null ||
            !leader.InCombatInLast(10_000))
            return;
        GroupState state = State(leader.Group);
        long now = GameLoop.GameLoopTime;
        lock (state)
        {
            if (now < state.NextRetreatCheck || now < state.RetreatUntil)
                return;
            state.NextRetreatCheck = now + 3_000;
        }

        GameLiving[] members = leader.Group.GetMembersInTheGroup().ToArray();
        int alive = members.Count(member => member.IsAlive);
        int healersAlive = members.Count(member => member.IsAlive && IsHealer(member));
        GameLiving[] enemies = leader.GetPlayersInRadius(1_800).Cast<GameLiving>()
            .Concat(leader.GetNPCsInRadius(1_800).OfType<GameBot>())
            .Where(enemy => enemy.IsAlive && PvpCombatant.IsPlayerShaped(enemy) && enemy.InCombat &&
                GameServer.ServerRules.IsAllowedToAttack(leader, enemy, true))
            .ToArray();
        if (enemies.Length == 0 ||
            !AutonomousRvrDoctrine.ShouldRetreat(state.Doctrine, alive, state.FormedMembers, healersAlive,
                state.FormedHealers, enemies.Length, Random.Shared.NextDouble()))
            return;

        Vector3 here = new(leader.X, leader.Y, leader.Z);
        Vector3 threat = AutonomousRvrDoctrineGeometry.Centroid(enemies.Select(enemy => new Vector3(enemy.X, enemy.Y, enemy.Z)));
        Vector3 away = AutonomousRvrDoctrineGeometry.AwayFrom(here, threat, 2_200);
        Vector3 point = PathfindingProvider.Instance.GetMoveAlongSurface(leader.CurrentZone, here, away,
            PathfindingProvider.Instance.DefaultFilters) ?? away;
        lock (state)
        {
            state.RetreatPoint = point;
            state.RetreatUntil = now + 25_000 + Random.Shared.Next(15_000);
        }
    }

    /// <summary>
    /// A member of a retreating group breaks off and runs to the rally point.
    /// It never blocks a bot that cannot move; the retreat ends on its own.
    /// </summary>
    public static bool TryRunRetreat(BotBrain brain)
    {
        GameBot bot = brain?.BotBody;
        if (bot == null || !bot.IsAlive || bot.IsIncapacitated || !IsRetreating(bot, out Vector3 point))
            return false;
        Vector3 here = new(bot.X, bot.Y, bot.Z);
        if (Vector3.Distance(here, point) <= 150)
            return false;

        if (brain.HasAggro)
            brain.ClearAggroList();
        bot.StopAttack();
        if (bot.IsCasting)
            bot.StopCurrentSpellcast();
        bot.TargetObject = null;
        Vector3 slot = AutonomousRvrDoctrineGeometry.Scatter(point, bot.ObjectID, 90);
        if (!bot.IsMoving || !bot.IsMovingOnPath)
            bot.PathTo(slot, bot.MaxSpeed);
        return true;
    }
}

/// <summary>Plain vector helpers for the doctrine runtime (kept testable).</summary>
public static class AutonomousRvrDoctrineGeometry
{
    /// <summary>
    /// Marching order in travel: guards and melee right behind the leader,
    /// then the speed singer, healers in the middle, casters and archers at
    /// the back, stealthers trailing wide.
    /// </summary>
    public static int MarchRank(RvrClassTraits traits) =>
        traits.HasFlag(RvrClassTraits.Stealth) && !traits.HasFlag(RvrClassTraits.Archer) ? 5 :
        traits.HasFlag(RvrClassTraits.Guard) ? 0 :
        traits.HasFlag(RvrClassTraits.Speed) ? 1 :
        traits.HasFlag(RvrClassTraits.Healer) ? 3 :
        traits.HasFlag(RvrClassTraits.Caster) || traits.HasFlag(RvrClassTraits.Archer) ? 4 :
        2;

    /// <summary>
    /// A member's travel slot relative to a moving leader. <paramref name="forward"/>
    /// is the leader's unit direction of travel. Column is a two-file line
    /// behind the leader; Loose spreads members wide around and behind.
    /// </summary>
    public static Vector3 TravelSlot(RvrTravelShape shape, int slot, int count, Vector3 leader, Vector2 forward)
    {
        if (forward.LengthSquared() < 0.0001f)
            forward = new Vector2(0, 1);
        forward = Vector2.Normalize(forward);
        Vector2 right = new(forward.Y, -forward.X);
        Vector2 offset = shape switch
        {
            RvrTravelShape.Column => -forward * (110 + slot / 2 * 85) + right * (slot % 2 == 0 ? 45 : -45),
            RvrTravelShape.Loose => Loose(slot, Math.Max(1, count), forward, right),
            _ => Vector2.Zero,
        };
        return new Vector3(leader.X + offset.X, leader.Y + offset.Y, leader.Z);
    }

    private static Vector2 Loose(int slot, int count, Vector2 forward, Vector2 right)
    {
        // A wide fan behind the leader: stealthers and scouts keep their distance.
        double spread = Math.PI * 0.9;
        double angle = Math.PI + (count == 1 ? 0 : spread * (slot / (double)(count - 1) - 0.5));
        float distance = 230 + slot % 3 * 60;
        Vector2 direction = forward * (float)Math.Cos(angle) + right * (float)Math.Sin(angle);
        return direction * distance;
    }

    public static Vector3 Centroid(IEnumerable<Vector3> points)
    {
        Vector3 sum = Vector3.Zero;
        int count = 0;
        foreach (Vector3 point in points)
        {
            sum += point;
            count++;
        }
        return count == 0 ? Vector3.Zero : sum / count;
    }

    /// <summary>A point <paramref name="distance"/> away from <paramref name="threat"/>, through <paramref name="from"/>.</summary>
    public static Vector3 AwayFrom(Vector3 from, Vector3 threat, float distance)
    {
        Vector2 direction = new(from.X - threat.X, from.Y - threat.Y);
        if (direction.LengthSquared() < 1)
            direction = new Vector2(1, 0);
        direction = Vector2.Normalize(direction) * distance;
        return new Vector3(from.X + direction.X, from.Y + direction.Y, from.Z);
    }

    /// <summary>A stable personal spot around <paramref name="centre"/> so a group does not stack on one point.</summary>
    public static Vector3 Scatter(Vector3 centre, long key, float radius)
    {
        double angle = (unchecked((ulong)key) % 360) * Math.PI / 180d;
        double distance = radius * (0.4 + (unchecked((ulong)key) / 360 % 60) / 100d);
        return new Vector3(centre.X + (float)(Math.Cos(angle) * distance),
            centre.Y + (float)(Math.Sin(angle) * distance), centre.Z);
    }
}
