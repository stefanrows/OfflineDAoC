using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using DOL.Database;
using DOL.GS.Keeps;

namespace DOL.GS;

/// <summary>
/// Task 139 step 2: a goal that needs a keep gate the bot may not pass is not offered. 440 of the 1,307 give-ups in
/// the 2026-10-23 log were such goals: the Darkness Falls portals inside Castle Excalibur and Dun Dagda (held by the
/// Frontier Wardens garrison) and PvP targets standing in or on Caer Renaris. A bot walked to the closed gate and
/// gave up, again and again. In 1.65 a player does not run at a closed enemy gate to reach a DF portal either.
/// The access rule is the existing one (<see cref="AutonomousRvrTravel.CanPassKeep"/>); a keep whose gates are down
/// is open to every walker, and a keep the bot's own force is sieging keeps its targets. Only the courtyard counts
/// (inside the outer gate's ring, less the wall), so fighters outside the walls, such as a warband at the back wall
/// of another guild's keep, stay valid targets.
/// </summary>
public static class AutonomousKeepGateGoals
{
    /// <summary>
    /// Used for a keep that has no door yet. The farthest door of the saved keeps lies 1,270-1,790 units from the
    /// keep centre; the DF portals (350-740 units) and the Caer Renaris targets (up to 975) lie well inside.
    /// </summary>
    public const float FallbackFootprintRadius = 1_300f;
    /// <summary>
    /// The outer gate stands in the wall, the farthest door from the centre; on every other side the wall is closer.
    /// The courtyard is that ring less this margin, so the ground just outside the walls is not "behind a gate".
    /// </summary>
    public const float WallMargin = 300f;
    public const float MinimumCourtyardRadius = 600f;
    /// <summary>Doors this close to the outer gate's ring are outer gates; inner keep doors do not hold a breached keep.</summary>
    public const float OuterGateBand = 150f;

    private static readonly ConcurrentDictionary<int, float> FootprintByKeep = new();
    private static readonly ConcurrentDictionary<(ushort Region, int X, int Y), AbstractGameKeep> KeepByEntrance = new();

    /// <summary>The goal is held back: a keep the bot may not pass, its gates intact, and not the keep it is sieging.</summary>
    public static bool GateBlocks(bool canPassKeep, bool anyGateIntact, bool isSiegedKeep) =>
        !canPassKeep && anyGateIntact && !isSiegedKeep;

    public static bool IsInsideFootprint(float dx, float dy, float radius) => dx * dx + dy * dy <= radius * radius;

    /// <summary>The courtyard radius for a keep whose outer gate ring is <paramref name="outerRadius"/>.</summary>
    public static float CourtyardRadius(float outerRadius) => Math.Max(MinimumCourtyardRadius, outerRadius - WallMargin);

    public static bool IsOuterGate(float doorDistance, float outerRadius) => doorDistance >= outerRadius - OuterGateBand;

    /// <summary>The keep's walls and gates: its farthest door from the keep centre (cached; doors never move).</summary>
    public static float FootprintRadius(AbstractGameKeep keep)
    {
        if (FootprintByKeep.TryGetValue(keep.KeepID, out float known))
            return known;
        float farthest = 0;
        foreach (GameKeepDoor door in keep.Doors.Values)
            farthest = Math.Max(farthest, Vector2.Distance(new(door.X, door.Y), new(keep.X, keep.Y)));
        if (farthest <= 0)
            return FallbackFootprintRadius;
        FootprintByKeep[keep.KeepID] = farthest;
        return farthest;
    }

    /// <summary>An outer gate still stands closed (a breached outer gate opens the courtyard).</summary>
    private static bool AnyGateIntact(AbstractGameKeep keep)
    {
        float outer = FootprintRadius(keep);
        foreach (GameKeepDoor door in keep.Doors.Values)
            if (door.IsAlive && door.IsAttackableDoor && door.State == eDoorState.Closed &&
                IsOuterGate(Vector2.Distance(new(door.X, door.Y), new(keep.X, keep.Y)), outer))
                return true;
        return false;
    }

    /// <summary>
    /// The keep that encloses the point and that the bot may not pass through its closed gates, or null.
    /// <paramref name="keeps"/> are the keeps of the point's region; <paramref name="siegedKeepId"/> is the
    /// "rvr-keep-N" objective the bot's force is attacking, if any (a siege keeps its targets).
    /// </summary>
    public static AbstractGameKeep FindBlockingKeep(GameBot bot, IEnumerable<AbstractGameKeep> keeps, float x, float y,
        string siegedKeepId = null)
    {
        foreach (AbstractGameKeep keep in keeps)
        {
            if (keep == null || keep.IsPortalKeep ||
                !IsInsideFootprint(x - keep.X, y - keep.Y, CourtyardRadius(FootprintRadius(keep))) ||
                AutonomousRvrTravel.CanPassKeep(bot, keep))
                continue;
            bool sieged = siegedKeepId != null && siegedKeepId == $"rvr-keep-{keep.KeepID}";
            if (GateBlocks(false, AnyGateIntact(keep), sieged))
                return keep;
        }
        return null;
    }

    /// <summary>
    /// A Darkness Falls entrance whose portal stands inside a keep the bot may not pass. Only the entrance edges
    /// (a zone point into the DF region) are tested; the keep of an entrance is found once and kept.
    /// </summary>
    public static bool BlocksEntrance(GameBot bot, DbZonePoint point)
    {
        if (bot == null || point == null || point.TargetRegion != AutonomousDarknessFallsPolicy.RegionId ||
            point.SourceRegion == AutonomousDarknessFallsPolicy.RegionId)
            return false;
        IKeepManager manager = GameServer.Instance?.Configuration != null ? GameServer.KeepManager : null;
        if (manager == null)
            return false;
        var key = (point.SourceRegion, point.SourceX, point.SourceY);
        if (!KeepByEntrance.TryGetValue(key, out AbstractGameKeep keep))
        {
            ICollection<AbstractGameKeep> regionKeeps = manager.GetKeepsOfRegion(point.SourceRegion);
            if (regionKeeps.Count == 0)
                return false; // keeps not loaded yet: ask again later
            keep = null;
            float best = float.MaxValue;
            foreach (AbstractGameKeep candidate in regionKeeps)
            {
                float dx = point.SourceX - candidate.X, dy = point.SourceY - candidate.Y;
                if (!candidate.IsPortalKeep && IsInsideFootprint(dx, dy, CourtyardRadius(FootprintRadius(candidate))) &&
                    dx * dx + dy * dy < best)
                {
                    best = dx * dx + dy * dy;
                    keep = candidate;
                }
            }
            KeepByEntrance[key] = keep;
        }
        if (keep == null || AutonomousRvrTravel.CanPassKeep(bot, keep, manager) || !AnyGateIntact(keep))
            return false;
        AutonomousRouteGoalFixMetrics.GatedDfSkipped();
        return true;
    }
}
