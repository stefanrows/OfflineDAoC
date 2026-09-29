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
        public long NextMobCheck;
        /// <summary>Counts PvP retreats; a controller replans once per finished one.</summary>
        public int RetreatSerial;
        public long PvpRetreatUntil;
        public Vector3? LastWaypoint;
        public ushort LastWaypointRegion;
    }

    private sealed class HabitState
    {
        public GameLiving Target;
        public long ChosenAt;
    }

    private static readonly ConditionalWeakTable<Group, GroupState> Groups = new();
    /// <summary>Disengage state of RvR bots without a group.</summary>
    private static readonly ConditionalWeakTable<GameBot, GroupState> Solos = new();
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
            leader == bot || !AutonomousGroupMotion.RecentlyMoving(bot.Group, leader))
            return null;
        GameBot[] followers = bot.Group.GetMembersInTheGroup().OfType<GameBot>()
            .Where(member => member != leader && member.IsAlive)
            .OrderBy(member => AutonomousRvrDoctrineGeometry.MarchRank(TraitsOf(member)))
            .ThenBy(member => member.DatabaseID > 0 ? member.DatabaseID : member.ObjectID)
            .ToArray();
        int slot = Array.IndexOf(followers, bot);
        if (slot < 0)
            return null;
        Vector2 forward = AutonomousGroupMotion.SmoothedForward(bot.Group, leader);
        return AutonomousRvrDoctrineGeometry.TravelSlot(doctrine.Travel, slot, followers.Length, center, forward,
            bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID);
    }

    // ----------------------------------------------------------------- retreat

    public static bool IsRetreating(GameBot bot, out Vector3 point)
    {
        point = default;
        if (!Applies(bot))
            return false;
        GroupState state;
        if (bot.Group == null ? !Solos.TryGetValue(bot, out state) : !Groups.TryGetValue(bot.Group, out state))
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
        if (EvaluateMobDisengage(leader))
            return;
        if (!Applies(leader) || leader.Group?.LivingLeader != leader || leader.CurrentZone == null ||
            !leader.InCombatInLast(10_000))
            return;
        if (leader.InCombatInLast(1_500))
            AutonomousRvrGroupPause.NoteFight(leader);
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
        // P6: a retreat runs toward help (own hub, landing or keep) or back to
        // the last roam waypoint, and only falls back to "just away".
        Vector3? waypoint;
        lock (state)
            waypoint = state.LastWaypointRegion == leader.CurrentRegionID ? state.LastWaypoint : null;
        (Vector3 target, string anchor) = AutonomousRvrDoctrineGeometry.ChooseRetreatTarget(here, threat,
            RetreatAnchors(leader), waypoint);
        Vector3 point = PathfindingProvider.Instance.GetMoveAlongSurface(leader.CurrentZone, here, target,
            PathfindingProvider.Instance.DefaultFilters) ?? target;
        int formed, formedHealers;
        lock (state)
        {
            state.RetreatPoint = point;
            state.RetreatUntil = now + 25_000 + Random.Shared.Next(15_000);
            state.PvpRetreatUntil = state.RetreatUntil;
            state.RetreatSerial++;
            formed = state.FormedMembers;
            formedHealers = state.FormedHealers;
        }
        AutonomousRvrDangerMemory.RecordRetreat(leader, here, WorldSimulationClock.UtcNow);
        if (Log.IsInfoEnabled)
            Log.Info($"RVR_RETREAT group=\"{leader.TempProperties.GetProperty<string>("RvrEventForce") ?? $"rvr-{leader.DatabaseID}"}\" " +
                $"doctrine={state.Doctrine?.Kind.ToString() ?? "none"} " +
                $"reason={AutonomousRvrDoctrine.RetreatReason(alive, formed, healersAlive, formedHealers, enemies.Length)} " +
                $"alive={alive}/{formed} healers={healersAlive} enemies={enemies.Length} point={(int)point.X},{(int)point.Y} " +
                $"dest={(int)target.X},{(int)target.Y} anchor={anchor}");
    }

    /// <summary>Safe places in the leader's region: every border hub there
    /// (under Camlann all three are neutral safe hubs) with its bindstone
    /// landings, and keeps or towers the leader may pass (not hostile to it by
    /// guild, alliance or garrison; realm alone decides nothing).</summary>
    private static List<AutonomousRvrDoctrineGeometry.RetreatAnchor> RetreatAnchors(GameBot leader)
    {
        var anchors = new List<AutonomousRvrDoctrineGeometry.RetreatAnchor>();
        ushort region = leader.CurrentRegionID;
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            if (AutonomousRvrStaging.TryGetBorderKeep(realm, out AutonomousRvrStaging.BorderKeep hub) && hub.RegionId == region)
                anchors.Add(new(hub.Position, "hub"));
        foreach (PvpCombatant.SafeHubLanding landing in PvpCombatant.SafeHubLandings)
            if (landing.RegionId == region)
                anchors.Add(new(new(landing.X, landing.Y, leader.Z), "hub"));
        foreach (DOL.GS.Keeps.AbstractGameKeep keep in GameServer.KeepManager.GetKeepsOfRegion(region))
            if (AutonomousRvrTravel.CanPassKeep(leader, keep))
                anchors.Add(new(new(keep.X, keep.Y, keep.Z), "keep"));
        return anchors;
    }

    /// <summary>The group's PvP retreat counter and when the latest one ends.</summary>
    public static bool TryGetRetreat(GameBot bot, out int serial, out long until)
    {
        serial = 0;
        until = 0;
        if (!Applies(bot) || bot.Group == null || !Groups.TryGetValue(bot.Group, out GroupState state))
            return false;
        lock (state)
        {
            serial = state.RetreatSerial;
            until = state.PvpRetreatUntil;
        }
        return serial > 0;
    }

    /// <summary>The leader reached a roaming spot; a later retreat may run back here.</summary>
    public static void NoteWaypoint(GameBot leader, Vector3 point)
    {
        if (!Applies(leader) || leader.Group?.LivingLeader != leader)
            return;
        GroupState state = State(leader.Group);
        lock (state)
        {
            state.LastWaypoint = point;
            state.LastWaypointRegion = leader.CurrentRegionID;
        }
    }

    /// <summary>How far a warband runs from a monster it will not fight.</summary>
    public const int MobDisengageDistance = 2_800;
    private const int MobDisengageCheckMilliseconds = 2_000;
    private const int MobDisengageRadius = 1_800;

    /// <summary>
    /// A 2003 group attacked by a monster far above it (red or purple con, or
    /// a named monster of level 55+ above its level) broke off and walked on
    /// instead of fighting it. The leader (or a solo bot) decides; the group
    /// uses the ordinary retreat run, away from the monster. Keep guards and
    /// lords never trigger this, and a live PvP fight is left to the PvP
    /// retreat rule.
    /// </summary>
    public static bool EvaluateMobDisengage(GameBot bot)
    {
        if (!Applies(bot) || !bot.IsAlive || bot.CurrentZone == null ||
            bot.Group != null && bot.Group.LivingLeader != bot)
            return false;
        // The leader decides for the group when any member was hit recently.
        if (!bot.InCombatInLast(5_000) &&
            bot.Group?.GetMembersInTheGroup().Any(member => member.IsAlive && member.InCombatInLast(5_000)) != true)
            return false;
        GroupState state = bot.Group == null ? Solos.GetOrCreateValue(bot) : State(bot.Group);
        long now = GameLoop.GameLoopTime;
        lock (state)
        {
            if (now < state.NextMobCheck || now < state.RetreatUntil)
                return false;
            state.NextMobCheck = now + MobDisengageCheckMilliseconds;
        }

        HashSet<GameLiving> ours = bot.Group?.GetMembersInTheGroup().ToHashSet() ?? [bot];
        GameNPC[] attackers = bot.GetNPCsInRadius(MobDisengageRadius)
            .Where(npc => npc.TargetObject is GameLiving victim && ours.Contains(victim) &&
                AutonomousRvrMobAvoidance.IsPveMonster(bot, npc) &&
                AutonomousRvrMobAvoidance.ShouldDisengage(victim.Level, npc.EffectiveLevel, npc.Name))
            .ToArray();
        if (attackers.Length == 0)
            return false;
        bool pvpFight = bot.GetPlayersInRadius(MobDisengageRadius).Cast<GameLiving>()
            .Concat(bot.GetNPCsInRadius(MobDisengageRadius).OfType<GameBot>())
            .Any(enemy => enemy.IsAlive && enemy.InCombat && !ours.Contains(enemy) && PvpCombatant.IsPlayerShaped(enemy) &&
                GameServer.ServerRules.IsAllowedToAttack(bot, enemy, true));
        if (pvpFight)
            return false;

        Vector3 here = new(bot.X, bot.Y, bot.Z);
        Vector3 threat = AutonomousRvrDoctrineGeometry.Centroid(attackers.Select(npc => new Vector3(npc.X, npc.Y, npc.Z)));
        Vector3 away = AutonomousRvrDoctrineGeometry.AwayFrom(here, threat, MobDisengageDistance);
        Vector3 point = PathfindingProvider.Instance.GetMoveAlongSurface(bot.CurrentZone, here, away,
            PathfindingProvider.Instance.DefaultFilters) ?? away;
        lock (state)
        {
            state.RetreatPoint = point;
            state.RetreatUntil = now + 20_000 + Random.Shared.Next(10_000);
        }
        GameNPC strongest = attackers.OrderByDescending(npc => npc.EffectiveLevel).First();
        if (Log.IsInfoEnabled)
            Log.Info($"RVR_MOB_DISENGAGE bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level} group_size={ours.Count} " +
                $"mob=\"{strongest.Name}\" mob_level={strongest.EffectiveLevel} attackers={attackers.Length} region={bot.CurrentRegionID} " +
                $"zone=\"{bot.CurrentZone.Description}\" position={bot.X},{bot.Y},{bot.Z}");
        return true;
    }

    private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrDoctrineRuntime));

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
    public static Vector3 TravelSlot(RvrTravelShape shape, int slot, int count, Vector3 leader, Vector2 forward,
        long memberKey = 0)
    {
        if (forward.LengthSquared() < 0.0001f)
            forward = new Vector2(0, 1);
        forward = Vector2.Normalize(forward);
        Vector2 right = new(forward.Y, -forward.X);
        // A loose, staggered march: each member keeps its own lane and gap,
        // so the group reads as people walking together, not a queue.
        ulong personal = unchecked((ulong)memberKey);
        float lane = (slot % 2 == 0 ? 1 : -1) * (55 + (int)(personal % 90));
        float gap = 95 + slot * 48 + (int)(personal / 90 % 45);
        Vector2 offset = shape switch
        {
            RvrTravelShape.Column => -forward * gap + right * lane,
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

    public readonly record struct RetreatAnchor(Vector3 Point, string Kind);

    /// <summary>How far a retreat runs toward an anchor at most (it stops at the anchor when closer).</summary>
    public const float RetreatAnchorRun = 3_000;
    /// <summary>Anchors or waypoints farther than this are not a retreat destination.</summary>
    public const float RetreatAnchorRange = 15_000;
    /// <summary>The plain run away from the enemy, as before.</summary>
    public const float RetreatAwayRun = 2_200;

    /// <summary>
    /// Where a retreat heads (P6): the nearest own anchor (hub, landing, keep)
    /// within 15,000 that does not lie toward the enemy, else the previous
    /// roam waypoint on the same terms, else straight away 2,200 units. A
    /// destination is followed at most 3,000 units per retreat.
    /// </summary>
    public static (Vector3 Target, string Kind) ChooseRetreatTarget(Vector3 here, Vector3 threat,
        IReadOnlyList<RetreatAnchor> anchors, Vector3? waypoint)
    {
        Vector2 origin = new(here.X, here.Y);
        Vector2 away = origin - new Vector2(threat.X, threat.Y);
        bool hasThreatDirection = away.LengthSquared() >= 1;
        if (hasThreatDirection)
            away = Vector2.Normalize(away);

        bool Usable(Vector3 point, out float distance)
        {
            Vector2 delta = new Vector2(point.X, point.Y) - origin;
            distance = delta.Length();
            if (distance > RetreatAnchorRange || distance < 1)
                return false;
            // Not toward the enemy: within 90 degrees of straight away.
            return !hasThreatDirection || Vector2.Dot(delta / distance, away) >= 0;
        }

        Vector3 Toward(Vector3 point, float distance)
        {
            if (distance <= RetreatAnchorRun)
                return point;
            Vector2 step = (new Vector2(point.X, point.Y) - origin) / distance * RetreatAnchorRun;
            return new Vector3(here.X + step.X, here.Y + step.Y, here.Z);
        }

        RetreatAnchor? best = null;
        float bestDistance = float.MaxValue;
        foreach (RetreatAnchor anchor in anchors ?? [])
            if (Usable(anchor.Point, out float distance) && distance < bestDistance)
            {
                best = anchor;
                bestDistance = distance;
            }
        if (best.HasValue)
            return (Toward(best.Value.Point, bestDistance), best.Value.Kind);
        if (waypoint.HasValue && Usable(waypoint.Value, out float waypointDistance))
            return (Toward(waypoint.Value, waypointDistance), "waypoint");
        return (AwayFrom(here, threat, RetreatAwayRun), "away");
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
