using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS.Keeps;

namespace DOL.GS;

/// <summary>
/// The route state of one walk (docs/GOTHWAITE_PVP_MODE.md 6.4): the goal last proven, its route, and goals recently
/// proven unreachable. Stage 3 patrols reuse it, so it is a plain object a driver keeps per bot.
/// </summary>
public sealed class GothwaitePvpRoute
{
    public Point3D RouteGoal;
    public Vector3 RoutePosition;
    public bool RouteOk;
    /// <summary>The proven route ends at the (snapped) goal, not at the end of a first leg.</summary>
    public bool RouteFinal;
    public long RouteUntil;
    /// <summary>Why the last proof failed (a navmesh reason such as no_route or closed_gate), empty when it did not.</summary>
    public string LastReason = string.Empty;
    public readonly Dictionary<(int, int, int), long> UnreachableUntil = new();
}

/// <summary>
/// Walks a Gothwaite PvP bot to a goal along a proven navmesh route. Like <see cref="AutonomousBattlegroundDriver"/>, a
/// route is proved with chained capped legs (<see cref="AutonomousBattlegroundDriver.ChainRoute"/>), first avoiding
/// blocking doors and then with the default filters when every closed gate on it is one the bot may pass. The
/// walk is re-proved every <see cref="RouteIntervalMilliseconds"/>, and a failed goal is not proved again for
/// <see cref="UnreachableRetryMilliseconds"/>.
/// </summary>
public static class GothwaitePvpNavigation
{
    public const int RouteIntervalMilliseconds = 3_000;
    public const int UnreachableRetryMilliseconds = 15_000;
    public const int ArrivalDistance = AutonomousBattlegroundDriver.ArrivalDistance;
    public const int FollowDistance = AutonomousBattlegroundDriver.FollowDistance;
    // Snap search boxes (horizontal half-size, then vertical half-size), the same two steps the installed-mesh probe proves:
    // a tight floor first, then a tall one. A goal's height is an estimate: the castle exit's floor lies about 900 units
    // below its estimate on the installed mesh (2026-10-13), so a narrow height band would reject a reachable point.
    private const int SnapHorizontal = 48;
    private const int SnapTightVertical = 96;
    private const int SnapTallVertical = 4096;
    private const int ProofNodes = 512;
    private const int UnreachableCacheLimit = 32;

    /// <summary>
    /// True when the bot has arrived: it stands within the arrival distance of the goal on the ground plane (goal heights
    /// are estimates), or of the end of a proven route that ends at the snapped goal.
    /// </summary>
    public static bool Arrived(GameBot bot, Point3D goal, GothwaitePvpRoute route) =>
        WithinGround(bot, goal.X, goal.Y, ArrivalDistance) ||
        route is { RouteOk: true, RouteFinal: true } && WithinGround(bot, (int)route.RoutePosition.X, (int)route.RoutePosition.Y, ArrivalDistance);

    public static void Stand(GameBot bot)
    {
        bot.StopMovingOnPath();
        bot.StopMoving();
    }

    /// <summary>
    /// Walks toward a goal along a proven route. True when the bot is at the goal or walking toward it; false when no
    /// route exists (the bot then stands, and <see cref="GothwaitePvpRoute.LastReason"/> says why).
    /// </summary>
    public static bool GoTo(GameBot bot, GothwaitePvpRoute route, Point3D goal, long now)
    {
        if (WithinGround(bot, goal.X, goal.Y, ArrivalDistance))
        {
            Stand(bot);
            return true;
        }
        (int, int, int) key = (goal.X, goal.Y, goal.Z);
        bool sameGoal = route.RouteGoal != null && route.RouteGoal.X == goal.X && route.RouteGoal.Y == goal.Y && route.RouteGoal.Z == goal.Z;
        if (route.UnreachableUntil.TryGetValue(key, out long retry) && now < retry)
        {
            // A goal proven unreachable within the retry window is refused without a new proof; stop only when it was being walked.
            if (sameGoal) Stand(bot);
            route.LastReason = "unreachable_cached";
            return false;
        }
        if (!sameGoal || now >= route.RouteUntil)
        {
            route.RouteGoal = goal;
            route.RouteUntil = now + RouteIntervalMilliseconds;
            route.RouteOk = PathExists(bot, goal, out route.RoutePosition, out string reason, out bool final);
            route.RouteFinal = route.RouteOk && final;
            route.LastReason = reason ?? string.Empty;
            if (route.RouteOk && !bot.IsCasting)
                bot.PathTo(route.RoutePosition, bot.MaxSpeed);
            if (!route.RouteOk)
                MarkUnreachable(route, key, now);
        }
        if (!route.RouteOk)
        {
            Stand(bot);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Proves a walkable route from the bot to the goal. The goal is snapped to the navmesh first; on success
    /// <paramref name="position"/> is the point to walk to now (the snapped goal, or the end of the first leg of a chain)
    /// and <paramref name="final"/> says whether that point is the snapped goal itself. On failure
    /// <paramref name="reason"/> names the first failed check.
    /// </summary>
    public static bool PathExists(GameBot bot, Point3D goal, out Vector3 position, out string reason, out bool final)
    {
        position = default;
        reason = "no_navmesh";
        final = false;
        Zone zone = bot.CurrentZone;
        var nav = PathfindingProvider.Instance;
        if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
            return false;
        // Legs are proved inside one zone's mesh; a goal in another zone is not walked to from here.
        if (bot.CurrentRegion?.GetZone(goal.X, goal.Y) != zone)
        {
            reason = "cross_zone";
            return false;
        }
        if (!TrySnapGoal(nav, zone, goal, out Vector3 snapped, out reason))
            return false;
        Vector3 from = new(bot.X, bot.Y, bot.Z);
        Vector3 target = snapped;
        WrappedPathfindingNode[] nodes = ArrayPool<WrappedPathfindingNode>.Shared.Rent(ProofNodes);
        try
        {
            if (AutonomousBattlegroundDriver.ChainRoute(from, target, AutonomousBattlegroundDriver.MaxRouteLegs,
                    start => AutonomousBattlegroundDriver.QueryLeg(nav, zone, start, target, nav.BlockingDoorAvoidanceFilters, nodes),
                    out Vector3 walkTo) == AutonomousBattlegroundDriver.RouteChain.Reached)
            {
                position = walkTo;
                final = walkTo == target;
                reason = null;
                return true;
            }
            bool closedGate = false;
            AutonomousBattlegroundDriver.RouteChain open = AutonomousBattlegroundDriver.ChainRoute(from, target,
                AutonomousBattlegroundDriver.MaxRouteLegs, start =>
                {
                    AutonomousBattlegroundDriver.RouteLeg leg = Leg(nav, zone, start, target, nav.DefaultFilters, nodes, out int count);
                    if (count == 0 || !CrossesBlockedDoor(bot, nodes, count))
                        return leg;
                    closedGate = true;
                    return new AutonomousBattlegroundDriver.RouteLeg(PathfindingStatus.NoPathFound, start);
                }, out walkTo);
            if (open == AutonomousBattlegroundDriver.RouteChain.Reached)
            {
                position = walkTo;
                final = walkTo == target;
                reason = null;
                return true;
            }
            reason = closedGate ? "closed_gate" : open switch
            {
                AutonomousBattlegroundDriver.RouteChain.Capped => "corridor_cap",
                AutonomousBattlegroundDriver.RouteChain.DeadEnd => "dead_end",
                _ => "no_route",
            };
            return false;
        }
        finally
        {
            ArrayPool<WrappedPathfindingNode>.Shared.Return(nodes);
        }
    }

    // The walkable floor under a goal: the tight search first, then the tall one. The goal's own height is not checked,
    // because it is an estimate; the route proof that follows is what decides whether the floor can be walked to.
    private static bool TrySnapGoal(IPathfindingMgr nav, Zone zone, Point3D goal, out Vector3 snapped, out string reason)
    {
        var point = new Vector3(goal.X, goal.Y, goal.Z);
        Vector3? floor = nav.GetClosestPoint(zone, point, SnapHorizontal, SnapHorizontal, SnapTightVertical, nav.DefaultFilters) ??
            nav.GetClosestPoint(zone, point, SnapHorizontal, SnapHorizontal, SnapTallVertical, nav.DefaultFilters);
        if (floor is not { } found || !float.IsFinite(found.Z))
        {
            snapped = default;
            reason = "off_mesh";
            return false;
        }
        snapped = found;
        reason = null;
        return true;
    }

    // Ground-plane distance from the bot to a point is within the range.
    private static bool WithinGround(GameBot bot, int x, int y, int range)
    {
        double dx = bot.X - x, dy = bot.Y - y;
        return dx * dx + dy * dy <= (double)range * range;
    }

    // One native query from start toward the goal; count is the number of route nodes left in the buffer.
    private static AutonomousBattlegroundDriver.RouteLeg Leg(IPathfindingMgr nav, Zone zone, Vector3 start, Vector3 goal,
        EDtPolyFlags[] filters, WrappedPathfindingNode[] nodes, out int count)
    {
        PathfindingResult result = nav.GetPathStraight(zone, start, goal, filters, nodes);
        bool found = result.Status is PathfindingStatus.PathFound or PathfindingStatus.PartialPathFound;
        count = found && result.NodeCount > 0 && result.NodeCount <= nodes.Length ? result.NodeCount : 0;
        return count == 0
            ? new AutonomousBattlegroundDriver.RouteLeg(PathfindingStatus.NoPathFound, start)
            : new AutonomousBattlegroundDriver.RouteLeg(result.Status, nodes[count - 1].Position);
    }

    // True when a closed door near the route's door nodes blocks this bot, as in the native Pathfinder.
    private static bool CrossesBlockedDoor(GameBot bot, Span<WrappedPathfindingNode> nodes, int count)
    {
        if (bot.CurrentRegion == null)
            return false;
        var doors = new List<GameDoorBase>();
        for (int index = 0; index < count; index++)
        {
            if ((nodes[index].Flags & EDtPolyFlags.AnyDoor) == 0)
                continue;
            Vector3 point = nodes[index].Position;
            doors.Clear();
            bot.CurrentRegion.GetInRadius(new Point3D((int)point.X, (int)point.Y, (int)point.Z), eGameObjectType.DOOR,
                Pathfinder.DOOR_SEARCH_DISTANCE, doors);
            foreach (GameDoorBase door in doors)
                if (AutonomousBattlegroundDriver.IsRouteBlockingDoor(door.State == eDoorState.Closed, door.CanBeOpenedViaInteraction,
                    door is GameKeepDoor keepDoor && Pathfinder.CanUseFriendlyKeepDoor(bot, keepDoor)))
                    return true;
        }
        return false;
    }

    private static void MarkUnreachable(GothwaitePvpRoute route, (int, int, int) key, long now) =>
        Store(route, key, now, now + UnreachableRetryMilliseconds);

    /// <summary>
    /// Refuses a goal for <paramref name="milliseconds"/>: <see cref="GoTo"/> returns false for it without a proof. The stage 3
    /// stuck rule uses it for a patrol point that stalled (5 minutes), so the bot picks another.
    /// </summary>
    public static void MarkUnreachableFor(GothwaitePvpRoute route, Point3D goal, long now, int milliseconds)
    {
        if (route == null || goal == null)
            return;
        Store(route, (goal.X, goal.Y, goal.Z), now, now + milliseconds);
    }

    /// <summary>True while a goal sits in the unreachable table (see <see cref="MarkUnreachableFor"/>).</summary>
    public static bool IsUnreachable(GothwaitePvpRoute route, Point3D goal, long now) =>
        route != null && goal != null && route.UnreachableUntil.TryGetValue((goal.X, goal.Y, goal.Z), out long until) && now < until;

    // Goals move with a leader; expired keys are dropped once the table is full, so it stays small.
    private static void Store(GothwaitePvpRoute route, (int, int, int) key, long now, long until)
    {
        if (route.UnreachableUntil.Count >= UnreachableCacheLimit)
            foreach ((int, int, int) stale in route.UnreachableUntil.Keys.ToArray())
                if (route.UnreachableUntil[stale] <= now)
                    route.UnreachableUntil.Remove(stale);
        route.UnreachableUntil[key] = until;
    }
}
