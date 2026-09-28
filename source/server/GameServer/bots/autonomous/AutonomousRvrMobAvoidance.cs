using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>
/// How an autonomous RvR warband treats PvE monsters on its way (live
/// 0.125.0 evidence, docs/BUGS.md 65/66): a 2003 eight-man group stepped
/// around the Archwizard and the big camps, never went into the shared
/// frontier dungeons to roam, and ran from a named mob far above its level
/// instead of fighting it. Keep guards and lords are never PvE threats here;
/// they are the objective. Pure decisions live here so they are testable; the
/// controller applies them with real navmesh checks.
/// </summary>
public static class AutonomousRvrMobAvoidance
{
    /// <summary>Named and epic frontier monsters from this level up are
    /// stepped around even when they only con orange.</summary>
    public const int NamedLevelFloor = 55;
    /// <summary>Aggressive monsters of blue con or higher standing this close
    /// together form a camp a roaming group walks around.</summary>
    public const int DenseCampMinimumMobs = 4;
    public const float DenseCampSpacing = 700;
    public const float DenseCampRadius = 900;
    /// <summary>A roaming patrol does not park in a camp this crowded.</summary>
    public const int PatrolDenseMobCount = 6;
    public const float ScanRadius = 3_000;
    /// <summary>Only the next stretch of the route is bent; the rest is
    /// re-examined as the group walks.</summary>
    public const float LookAhead = 3_000;
    public const float Margin = 200;
    /// <summary>A bend costs at most this many extra units; a larger detour
    /// is not taken (the group walks on and the disengage rule protects it).</summary>
    public const float MaximumDetour = 4_000;

    public readonly record struct Danger(Vector2 Center, float Radius, string Name, int Level);
    public readonly record struct MobView(Vector2 Position, int Level, string Name, bool Aggressive);

    /// <summary>DAoC names ordinary monsters in lower case ("reanimated
    /// guardian"); named monsters are capitalised ("Black Lady").</summary>
    public static bool IsNamed(string name) => !string.IsNullOrWhiteSpace(name) && char.IsUpper(name.Trim()[0]);

    public static ConColor Con(int actorLevel, int mobLevel) =>
        ConLevels.GetConColor(ConLevels.GetConLevel(Math.Clamp(actorLevel, 0, 127), Math.Clamp(mobLevel, 0, 127)));

    /// <summary>Red or purple to the actor: a fight no roaming group took on.</summary>
    public static bool IsFarAbove(int actorLevel, int mobLevel) => Con(actorLevel, mobLevel) >= ConColor.RED;

    /// <summary>A monster the route and the patrol choice stay clear of.</summary>
    public static bool IsAvoidedMob(int actorLevel, int mobLevel, string name) =>
        IsFarAbove(actorLevel, mobLevel) ||
        IsNamed(name) && mobLevel >= NamedLevelFloor && mobLevel > actorLevel;

    /// <summary>The same disengage check for a monster already attacking us.</summary>
    public static bool ShouldDisengage(int actorLevel, int mobLevel, string name) => IsAvoidedMob(actorLevel, mobLevel, name);

    /// <summary>Same growth as <see cref="AutonomousThreatAwarePathing.AvoidanceRadius"/>:
    /// 700 units plus 55 per level above the actor, at most 1,650.</summary>
    public static float AvoidanceRadius(int actorLevel, int mobLevel) =>
        Math.Clamp(700 + Math.Max(0, mobLevel - actorLevel) * 55, 700, 1650);

    /// <summary>A frontier clearing is a sensible patrol spot: no named or
    /// far-above monster in it and not a crowded camp.</summary>
    public static bool IsSuitablePatrolCamp(int actorLevel, IReadOnlyCollection<int> levels, int liveMobCount, string name) =>
        liveMobCount < PatrolDenseMobCount &&
        (levels == null || !levels.Any(level => IsAvoidedMob(actorLevel, level, name)));

    /// <summary>Shared frontier dungeons (Dodens Gruva, Summoner's Hall,
    /// Marfach Caverns, Hall of the Corrupt). They hold no keep, so an RvR
    /// force never roams, hunts or travels through them.</summary>
    public static bool IsForbiddenRvrRegion(ushort region) => AutonomousDungeonPolicy.IsSharedFrontierDungeon(region);

    /// <summary>
    /// The dungeon complex links all three frontiers (1 - 277 - 248 - 276 - 200,
    /// 100 - 246 - 248). For an RvR bot it is not a shortcut: an edge into the
    /// complex is usable only when the goal itself lies inside it. Inside the
    /// complex only edges leading out remain, so the nearest exit is taken.
    /// </summary>
    public static bool AllowsRvrCrossing(ushort edgeTargetRegion, ushort goalRegion) =>
        !IsForbiddenRvrRegion(edgeTargetRegion) || IsForbiddenRvrRegion(goalRegion);

    /// <summary>
    /// For an RvR bot already inside the dungeon complex with a goal outside
    /// it: the first crossing of the shortest way out (nearest direct exit
    /// first), not the way deeper toward the goal. Null when none exists.
    /// </summary>
    public static DbZonePoint NearestExit(IReadOnlyList<DbZonePoint> edges, ushort current, int x, int y)
    {
        if (edges == null)
            return null;
        DbZonePoint direct = edges
            .Where(edge => edge.SourceRegion == current && !IsForbiddenRvrRegion(edge.TargetRegion))
            .OrderBy(edge => (double)(edge.SourceX - x) * (edge.SourceX - x) + (double)(edge.SourceY - y) * (edge.SourceY - y))
            .FirstOrDefault();
        if (direct != null)
            return direct;
        var first = new Dictionary<ushort, DbZonePoint>();
        var seen = new HashSet<ushort> { current };
        var queue = new Queue<ushort>();
        queue.Enqueue(current);
        while (queue.Count > 0)
        {
            ushort region = queue.Dequeue();
            foreach (DbZonePoint edge in edges.Where(edge => edge.SourceRegion == region))
            {
                if (!seen.Add(edge.TargetRegion))
                    continue;
                DbZonePoint start = region == current ? edge : first[region];
                if (!IsForbiddenRvrRegion(edge.TargetRegion))
                    return start;
                first[edge.TargetRegion] = start;
                queue.Enqueue(edge.TargetRegion);
            }
        }
        return null;
    }

    /// <summary>
    /// The crossing an RvR bot takes toward <paramref name="goal"/>. The
    /// shared frontier dungeons are skipped, except for a relic party (the
    /// carrier can never use a porter and its escort stays with it), a bot that
    /// already committed to the dungeon road (<paramref name="tunnelCommitted"/>),
    /// and as a last resort when <paramref name="porterUnavailable"/>: a force
    /// is never stranded in a foreign frontier. Only a bot that did not choose
    /// the road (an old route, a release inside) takes the nearest exit.
    /// <paramref name="commits"/> is true when the chosen crossing enters the
    /// complex on that road; the caller keeps the commitment until the bot is
    /// outside again.
    /// </summary>
    public static DbZonePoint ChooseRvrCrossing(IReadOnlyList<DbZonePoint> edges, ushort current, ushort goal,
        int x, int y, bool relicParty, bool porterUnavailable, bool tunnelCommitted,
        Func<IReadOnlyList<DbZonePoint>, DbZonePoint> search, out bool commits)
    {
        commits = false;
        if (edges == null || search == null)
            return null;
        DbZonePoint crossing;
        if (relicParty || tunnelCommitted && IsForbiddenRvrRegion(current))
            crossing = search(edges);
        else if (IsForbiddenRvrRegion(current) && !IsForbiddenRvrRegion(goal))
            return NearestExit(edges, current, x, y) ?? search(edges);
        else
        {
            crossing = search(edges.Where(edge => AllowsRvrCrossing(edge.TargetRegion, goal)).ToArray());
            if (crossing != null || !porterUnavailable)
                return crossing;
            crossing = search(edges);
        }
        commits = crossing != null && IsForbiddenRvrRegion(crossing.TargetRegion) && !IsForbiddenRvrRegion(goal);
        return crossing;
    }

    /// <summary>A bend is walked for at most this long, then dropped.</summary>
    public const int BendLifetimeMilliseconds = 40_000;
    /// <summary>After a dropped or failed bend the plain road order runs this long.</summary>
    public const int BendSuppressMilliseconds = 15_000;

    /// <summary>Whether a chosen bend is still walked: same leg, not reached, not expired.</summary>
    public static bool KeepBend(bool sameLeg, bool reached, long startedTick, long nowTick) =>
        sameLeg && !reached && nowTick - startedTick < BendLifetimeMilliseconds;

    /// <summary>
    /// Issues the bend order when there is one. A failed bend order is dropped
    /// (<paramref name="drop"/>) and false is returned, so the caller issues
    /// its ordinary road order with its own failure handling.
    /// </summary>
    public static bool TryWalkBend(Vector3 leg, Vector3 bend, Func<Vector3, bool> issue, Action drop)
    {
        if (bend == leg)
            return false;
        if (issue(bend))
            return true;
        drop();
        return false;
    }

    /// <summary>Single dangerous monsters plus dense camps of aggressive monsters.</summary>
    public static List<Danger> BuildDangers(int actorLevel, IReadOnlyList<MobView> mobs)
    {
        var dangers = new List<Danger>();
        if (mobs == null || mobs.Count == 0)
            return dangers;
        var pack = new List<MobView>();
        foreach (MobView mob in mobs)
        {
            if (!mob.Aggressive)
                continue;
            if (IsAvoidedMob(actorLevel, mob.Level, mob.Name))
                dangers.Add(new(mob.Position, AvoidanceRadius(actorLevel, mob.Level), mob.Name, mob.Level));
            else if (Con(actorLevel, mob.Level) >= ConColor.BLUE)
                pack.Add(mob);
        }

        var used = new bool[pack.Count];
        for (int i = 0; i < pack.Count; i++)
        {
            if (used[i])
                continue;
            var members = new List<int>();
            for (int j = 0; j < pack.Count; j++)
                if (!used[j] && Vector2.Distance(pack[i].Position, pack[j].Position) <= DenseCampSpacing)
                    members.Add(j);
            if (members.Count < DenseCampMinimumMobs)
                continue;
            Vector2 centre = Vector2.Zero;
            foreach (int index in members)
                centre += pack[index].Position;
            centre /= members.Count;
            float spread = members.Max(index => Vector2.Distance(centre, pack[index].Position));
            foreach (int index in members)
                used[index] = true;
            MobView strongest = members.Select(index => pack[index]).OrderByDescending(mob => mob.Level).First();
            dangers.Add(new(centre, Math.Max(DenseCampRadius, spread + 500), $"camp of {strongest.Name}", strongest.Level));
        }
        return dangers;
    }

    /// <summary>
    /// Waypoints that take the next stretch of the route around the first
    /// danger in its way, best (shortest detour, far side first) first. Empty
    /// when nothing blocks, when the goal itself lies inside a danger (the
    /// destination choice owns that), when the walker already stands inside
    /// one (the disengage rule owns that), or when every bend would cost more
    /// than <paramref name="maximumDetour"/>: the route bends or stays, it
    /// never stalls.
    /// </summary>
    public static Vector2[] BypassCandidates(Vector2 start, Vector2 goal, IReadOnlyList<Danger> dangers,
        float lookAhead = LookAhead, float maximumDetour = MaximumDetour)
    {
        if (dangers == null || dangers.Count == 0)
            return [];
        Vector2 delta = goal - start;
        float length = delta.Length();
        if (length < 1)
            return [];
        Vector2 direction = delta / length;
        float leg = Math.Min(length, lookAhead);

        Danger? blocking = null;
        float firstAlong = float.MaxValue;
        foreach (Danger danger in dangers)
        {
            if (Vector2.Distance(start, danger.Center) <= danger.Radius ||
                Vector2.Distance(goal, danger.Center) <= danger.Radius)
                continue;
            float along = Math.Clamp(Vector2.Dot(danger.Center - start, direction), 0, leg);
            if (Vector2.Distance(start + direction * along, danger.Center) >= danger.Radius)
                continue;
            if (along < firstAlong)
            {
                firstAlong = along;
                blocking = danger;
            }
        }
        if (blocking is not Danger block)
            return [];

        Vector2 normal = new(-direction.Y, direction.X);
        // Pass on the side away from the monster; a dead-centre monster is
        // passed on a stable side per route so a group does not dither.
        float side = Vector2.Dot(block.Center - start, normal);
        Vector2 preferred = side > 0 || side == 0 && ((int)start.X ^ (int)goal.Y) % 2 == 0 ? -normal : normal;
        var candidates = new List<(Vector2 Point, float Detour, int Order)>();
        int order = 0;
        foreach (Vector2 away in new[] { preferred, -preferred })
        {
            for (float scale = 1f; scale <= 2.01f; scale += 0.25f)
            {
                Vector2 point = block.Center + away * ((block.Radius + Margin) * scale);
                float detour = Vector2.Distance(start, point) + Vector2.Distance(point, goal) - length;
                if (detour > maximumDetour)
                    break;
                if (!LegClear(start, point, dangers) || !LegClear(point, goal, dangers, lookAhead))
                    continue;
                candidates.Add((point, detour, order));
                break;
            }
            order++;
        }
        return candidates.OrderBy(candidate => candidate.Detour).ThenBy(candidate => candidate.Order)
            .Select(candidate => candidate.Point).ToArray();
    }

    /// <summary>Whether a straight leg keeps clear of every danger (a danger
    /// that contains the leg's end, i.e. the goal, is the destination's
    /// problem and ignored here). Only the first <paramref name="within"/>
    /// units of the leg are checked.</summary>
    public static bool LegClear(Vector2 start, Vector2 end, IReadOnlyList<Danger> dangers, float within = float.MaxValue)
    {
        Vector2 delta = end - start;
        float length = delta.Length();
        if (length < 1)
            return true;
        Vector2 direction = delta / length;
        float leg = Math.Min(length, within);
        foreach (Danger danger in dangers)
        {
            if (Vector2.Distance(end, danger.Center) <= danger.Radius)
                continue;
            float along = Math.Clamp(Vector2.Dot(danger.Center - start, direction), 0, leg);
            if (Vector2.Distance(start + direction * along, danger.Center) < danger.Radius)
                return false;
        }
        return true;
    }

    // ------------------------------------------------------------- runtime

    /// <summary>
    /// A monster that can start a PvE fight with this RvR bot: alive,
    /// aggressive, attackable, and not a keep guard/lord, siege engine,
    /// player, bot or somebody's pet.
    /// </summary>
    public static bool IsPveMonster(GameLiving actor, GameNPC npc) =>
        actor != null && npc != null && npc.IsAlive && npc.ObjectState == GameObject.eObjectState.Active &&
        npc.CurrentRegion == actor.CurrentRegion &&
        npc is not GameKeepGuard && npc is not GameBot && npc is not GameSiegeWeapon &&
        npc.Brain is not IControlledBrain && !PvpCombatant.IsPlayerShaped(npc) &&
        (npc.Flags & (GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET)) == 0 &&
        GameServer.ServerRules?.IsAllowedToAttack(actor, npc, true) == true;

    public static bool IsAggressive(GameNPC npc) =>
        npc?.Brain is StandardMobBrain brain && brain.AggroLevel > 0 && brain.AggroRange > 0;

    public static MobView[] ScanMobs(GameBot bot) =>
        bot.GetNPCsInRadius((ushort)ScanRadius)
            .Where(npc => IsPveMonster(bot, npc))
            .Select(npc => new MobView(new(npc.X, npc.Y), npc.EffectiveLevel, npc.Name, IsAggressive(npc)))
            .ToArray();
}
