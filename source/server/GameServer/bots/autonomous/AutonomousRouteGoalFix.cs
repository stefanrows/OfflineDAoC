using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading;
using DOL.Database;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Task 139 step 2: of the 1,307 route give-ups in the 2026-10-23 log, most are bad goals, not bad meshes
/// (docs/NAVMESH_HOTSPOTS.md). A goal that stands outside the server's 32/32/64 Detour pick box (the Druim Ligen
/// goal hung 210 units above the floor) is snapped to the nearest walkable floor, and only after a path to it
/// failed. Nothing here touches a goal that already lies on the mesh.
/// </summary>
public static class AutonomousGoalSnap
{
    public enum Outcome
    {
        /// <summary>The goal lies inside the normal pick box: nothing to repair, the goal stays byte-identical.</summary>
        OnMesh,
        /// <summary>No floor within the widest allowed box (or no navmesh): the goal stays as it is.</summary>
        NoFloor,
        /// <summary>The goal moved to the nearest floor of its own zone.</summary>
        Snapped,
        /// <summary>
        /// Task 142: the nearest floor lies far above or below a nearby goal and the bot cannot walk there (another
        /// level of a wall, a tower or a bridge): the goal stays as it is.
        /// </summary>
        WrongFloor,
    }

    /// <summary>
    /// Task 142 height guard. 57 of the 71 snaps of the 0.320.0 run moved the goal 256 units or less in height, and
    /// none of those failed again; the two wrong-floor snaps of 0.314.0 (Hadrian's Wall, dz -838 and -325) both lay
    /// beyond it. A larger move is kept only when the bot can walk to the new floor.
    /// </summary>
    public const float UncheckedHeight = 256f;

    /// <summary>
    /// Only a goal this close (2D) to the bot is checked: both wrong-floor snaps failed again within seconds, so the
    /// bot stood next to them, and the corridor check (16 path queries at most) is conclusive over this distance. A
    /// far goal keeps its snap; should it fail again on arrival the controller does not snap it a second time.
    /// </summary>
    public const float ConnectionCheckRange = 3_000f;

    public static bool NeedsConnectionCheck(Vector3 goal, Vector3 snapped, Vector3 bot) =>
        MathF.Abs(snapped.Z - goal.Z) > UncheckedHeight &&
        Vector2.DistanceSquared(new(bot.X, bot.Y), new(snapped.X, snapped.Y)) <=
        ConnectionCheckRange * ConnectionCheckRange;

    /// <summary>
    /// <see cref="TrySnap"/> plus the height guard: a snap of more than <see cref="UncheckedHeight"/> close to the bot
    /// must be reachable (<paramref name="reachable"/>: a complete corridor from the bot's floor), else
    /// <see cref="Outcome.WrongFloor"/>, with the refused floor in <paramref name="snapped"/> (for the log line only).
    /// The guard costs nothing for the common small snap.
    /// </summary>
    public static Outcome TrySnapGuarded(IPathfindingMgr nav, Zone zone, Vector3 goal, Predicate<Vector3> inOwnZone,
        Vector3 bot, Func<Vector3, bool> reachable, out Vector3 snapped)
    {
        Outcome outcome = TrySnap(nav, zone, goal, inOwnZone, out snapped);
        if (outcome != Outcome.Snapped || !NeedsConnectionCheck(goal, snapped, bot) || reachable == null ||
            reachable(snapped))
            return outcome;
        return Outcome.WrongFloor;
    }

    /// <summary>
    /// The height guard's walk check, from the bot's own floor. A bot in another zone (<paramref name="zone"/> null)
    /// or one without a floor in the normal box cannot be checked; its snap stands as before the guard.
    /// </summary>
    public static bool IsReachableFrom(IPathfindingMgr nav, Zone zone, Vector3 bot, Vector3 floor)
    {
        if (zone == null)
            return true;
        Vector3? start = nav.GetClosestPoint(zone, bot, NormalHorizontal, NormalHorizontal, NormalVertical,
            nav.DefaultFilters);
        return !start.HasValue || AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, start.Value, floor);
    }

    /// <summary>The path query's own pick box (LocalPathfindingMgr's default half extents).</summary>
    public const float NormalHorizontal = 32f;
    public const float NormalVertical = 64f;

    /// <summary>
    /// Wider boxes, tried in this order. Never go larger: a 512/512/2048 box crashed the Detour host with an
    /// access violation in FindClosestPoint (task 139 probe). 96/96/512 and 256/256/1024 completed 416 of 421
    /// off-mesh goals.
    /// </summary>
    public static readonly (float Horizontal, float Vertical)[] WideBoxes = [(96f, 512f), (256f, 1024f)];

    public static Outcome TrySnap(IPathfindingMgr nav, Zone zone, Vector3 goal, Predicate<Vector3> inOwnZone,
        out Vector3 snapped)
    {
        snapped = goal;
        if (nav == null || zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
            return Outcome.NoFloor;
        if (nav.GetClosestPoint(zone, goal, NormalHorizontal, NormalHorizontal, NormalVertical, nav.DefaultFilters).HasValue)
            return Outcome.OnMesh;
        foreach ((float horizontal, float vertical) in WideBoxes)
        {
            Vector3? floor = nav.GetClosestPoint(zone, goal, horizontal, horizontal, vertical, nav.DefaultFilters);
            if (floor is not { } p || !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z) ||
                inOwnZone != null && !inOwnZone(p))
                continue;
            snapped = p;
            return Outcome.Snapped;
        }
        return Outcome.NoFloor;
    }
}

/// <summary>
/// Task 141: a bot standing outside the Detour pick box (a ledge, a wall top or a slope the mesh does not carry, or
/// under the terrain) gets NoPathFound for every goal, and the side-step search cannot help either, because each of
/// its probes starts from the same point. 56 of the 62 such give-ups of the 0.320.0 run connect once the start is
/// moved to the nearest floor (the task 139 probe's "snap-connects"). The same boxes as the goal snap find that
/// floor; it is used only when it has a way on (<c>connects</c>), so the bot is never put on an isolated patch.
/// </summary>
public static class AutonomousStartSnap
{
    public enum Outcome
    {
        /// <summary>The bot stands inside the normal pick box: its start is not the fault.</summary>
        OnMesh,
        /// <summary>No floor within the widest allowed box (or no navmesh).</summary>
        NoFloor,
        /// <summary>A floor was found, but it leads nowhere (no corridor to the goal and no local exit).</summary>
        Unconnected,
        /// <summary>The bot may be put on the floor found.</summary>
        Snapped,
    }

    /// <summary>
    /// One start snap per bot in this time. A bot that keeps ending up off the mesh on the same route is not
    /// teleported every few seconds; it takes the ordinary recovery in between.
    /// </summary>
    public const long CooldownMilliseconds = 45_000;
    /// <summary>At most this many start snaps for one destination; then the route's own give-up rules decide.</summary>
    public const int MaximumPerDestination = 2;
    /// <summary>The controller's "destination changed" distance.</summary>
    public const float SameDestination = 96f;

    /// <summary>The floor repair's own rule: a bot that is fighting, casting or riding is never moved.</summary>
    public static bool MayMove(bool casting, bool attacking, bool inCombat, bool hasAggro, bool onStableRoute) =>
        !casting && !attacking && !inCombat && !hasAggro && !onStableRoute;

    /// <summary>Start snaps already spent on <paramref name="destination"/> (0 when the destination changed).</summary>
    public static int SnapsFor(Vector3 destination, Vector3 lastDestination, int lastCount) =>
        Vector3.DistanceSquared(destination, lastDestination) <= SameDestination * SameDestination ? lastCount : 0;

    public static bool BudgetAllows(long now, long nextAllowedTick, int snapsForDestination) =>
        now >= nextAllowedTick && snapsForDestination < MaximumPerDestination;

    /// <summary>A floor inside a keep the bot is gated out of is refused, unless the bot stands in one.</summary>
    public static bool EntersGatedKeep(bool startGated, bool floorGated) => floorGated && !startGated;

    public static Outcome TrySnap(IPathfindingMgr nav, Zone zone, Vector3 start, Predicate<Vector3> inOwnZone,
        Func<Vector3, bool> connects, out Vector3 snapped)
    {
        snapped = start;
        AutonomousGoalSnap.Outcome floor = AutonomousGoalSnap.TrySnap(nav, zone, start, inOwnZone, out Vector3 found);
        if (floor == AutonomousGoalSnap.Outcome.OnMesh)
            return Outcome.OnMesh;
        if (floor != AutonomousGoalSnap.Outcome.Snapped)
            return Outcome.NoFloor;
        if (connects != null && !connects(found))
            return Outcome.Unconnected;
        snapped = found;
        return Outcome.Snapped;
    }

    /// <summary>
    /// The way on from a start floor: a complete corridor to the target's floor (the target is the failed point, or
    /// the goal, in the bot's zone; the probe walked to its snapped floor when it hung off the mesh too), else a local
    /// exit, since a far goal outruns the corridor check's 16 segments and real ground with a way out is enough then.
    /// </summary>
    public static bool LeadsOn(IPathfindingMgr nav, Zone zone, Vector3 floor, Vector3? target)
    {
        if (target is { } goal &&
            AutonomousGoalSnap.TrySnap(nav, zone, goal, null, out Vector3 goalFloor) !=
            AutonomousGoalSnap.Outcome.NoFloor &&
            AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, floor, goalFloor))
            return true;
        return AutonomousRendezvousNavigation.HasLocalExit(nav, zone, floor);
    }
}

/// <summary>
/// Task 142: a NoPathFound whose end point lies well away (over 96 units) from the route's goal and continuation came
/// from another mover (a stealth break run, a retreat slot, a disengage point). Its point hung off the mesh, so the
/// side-step search, which aims at that point, could only fail, and the bot gave up the route it was never asked to
/// walk (33 of the 38 off-mesh-goal give-ups of the 0.320.0 run, among them all 28 a snap would have connected). Such
/// a failure is set aside once and the route is planned again; the same point failing again goes to the ordinary
/// recovery.
/// </summary>
public static class AutonomousForeignRouteFailure
{
    /// <summary>The goal snap's own "same point" distance.</summary>
    public const float SamePoint = 16f;
    /// <summary>
    /// A failure this close to the goal or continuation belongs to the route (AppliesToCurrentDestination's radius):
    /// a formation slot, a march leg or a moving target drifts between turns and must not be set aside each time.
    /// </summary>
    public const float RouteRadius = 96f;

    /// <summary>The goal snap's test: the failed point is the goal or the continuation itself.</summary>
    public static bool IsRoutePoint(Vector3 failed, Vector3 destination, Vector3? continuation) =>
        Vector3.DistanceSquared(destination, failed) <= SamePoint * SamePoint ||
        continuation.HasValue && Vector3.DistanceSquared(continuation.Value, failed) <= SamePoint * SamePoint;

    public static bool IsForeign(Vector3 failed, Vector3 destination, Vector3? continuation) =>
        Vector3.DistanceSquared(destination, failed) > RouteRadius * RouteRadius &&
        (!continuation.HasValue || Vector3.DistanceSquared(continuation.Value, failed) > RouteRadius * RouteRadius);

    public static bool SetAside(bool noPathFound, bool foreign, bool failedOffMesh, bool alreadySetAside) =>
        noPathFound && foreign && failedOffMesh && !alreadySetAside;
}

/// <summary>
/// After a route give-up the same bot (and its warband) leaves that destination alone for a while: the Connacht
/// loop had six bots picking one goal every ~22 s without moving. The memory holds a few spots per owner; the
/// pickers consult it before they offer a destination. It records nothing per tick: a give-up writes one entry.
/// </summary>
public static class AutonomousFailedGoalMemory
{
    /// <summary>
    /// A warband lingers about six minutes at a roam spot (RvrLingerMilliseconds) and re-plans every 45-150 s, so
    /// six minutes keeps a failed spot out of one whole roam cycle. A door, a siege or a route that was merely
    /// blocked then has had time to change, and the spot is not lost for the rest of the session.
    /// </summary>
    public const long CooldownMilliseconds = 6 * 60_000;
    /// <summary>The same destination: close enough that a re-pick would walk the same failed route.</summary>
    public const float Radius = 200f;
    private const int SlotsPerOwner = 4;
    private const int SoftOwnerCap = 512;

    private struct Entry
    {
        public ushort Region;
        public float X, Y;
        public long Until;
    }

    private static readonly ConcurrentDictionary<long, Entry[]> Owners = new();

    /// <summary>A warband as one owner (bit 62 keeps it apart from the positive bot ids).</summary>
    public static long ForceKey(string forceId) =>
        string.IsNullOrEmpty(forceId) ? 0 : (1L << 62) | (uint)StringComparer.Ordinal.GetHashCode(forceId);

    public static void Note(long ownerKey, ushort region, float x, float y, long now)
    {
        if (ownerKey == 0 || region == 0)
            return;
        if (Owners.Count > SoftOwnerCap)
            Prune(now);
        Entry[] entries = Owners.GetOrAdd(ownerKey, _ => new Entry[SlotsPerOwner]);
        lock (entries)
        {
            int slot = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Until > now && entries[i].Region == region && Near(entries[i].X, entries[i].Y, x, y))
                {
                    slot = i; // the same spot again: refresh it
                    break;
                }
                if (entries[i].Until < entries[slot].Until)
                    slot = i; // else replace the oldest
            }
            entries[slot] = new Entry { Region = region, X = x, Y = y, Until = now + CooldownMilliseconds };
        }
    }

    public static bool IsBlocked(long ownerKey, ushort region, float x, float y, long now)
    {
        if (ownerKey == 0 || !Owners.TryGetValue(ownerKey, out Entry[] entries))
            return false;
        lock (entries)
        {
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].Until > now && entries[i].Region == region && Near(entries[i].X, entries[i].Y, x, y))
                    return true;
        }
        return false;
    }

    /// <summary>
    /// Task 140: a zone point whose approach the owner just gave up on. A give-up on the way to a crossing notes the
    /// crossing's approach (the Excalibur DF portal give-ups ended 140-155 units from its source point), so the crossing
    /// search prefers another route until the cooldown ends; it keeps the edge when no other route exists.
    /// </summary>
    public static bool IsBlockedCrossing(long ownerKey, DbZonePoint point, long now) =>
        point != null && IsBlocked(ownerKey, point.SourceRegion, point.SourceX, point.SourceY, now);

    private static bool Near(float ax, float ay, float bx, float by) =>
        (ax - bx) * (ax - bx) + (ay - by) * (ay - by) <= Radius * Radius;

    private static void Prune(long now)
    {
        foreach ((long key, Entry[] entries) in Owners)
        {
            bool live = false;
            lock (entries)
                foreach (Entry entry in entries)
                    live |= entry.Until > now;
            if (!live)
                Owners.TryRemove(key, out _);
        }
    }

    /// <summary>Test seam: forget everything.</summary>
    public static void Clear() => Owners.Clear();
}

/// <summary>
/// The pocket escape (a relocation to the realm capital after repeated route failures in one place) helps a bot
/// that is stuck on a disconnected patch of mesh. It does not help when the bot stands on connected ground and the
/// GOAL is the problem: six Hibernians stood at the Connacht stable master, their goal hung off the mesh, and the
/// escape sent them to Tir na Nog; the horse brought them back and they tried the same goal again.
/// </summary>
public static class AutonomousPocketEscapeRule
{
    public static bool SkipRelocation(bool startHasLocalExit, bool goalOffMesh) => startHasLocalExit && goalOffMesh;
}

/// <summary>
/// One line a minute (and only when something happened) so the give-up counts of a run can be compared with the
/// 2026-10-23 log: AUTONOMOUS_ROUTE_GOAL_FIX. The counters are Interlocked longs; the string is built at report time.
/// </summary>
public static class AutonomousRouteGoalFixMetrics
{
    public const long ReportIntervalMilliseconds = 60_000;
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousRouteGoalFixMetrics));
    private static long _snapped, _snapFailed, _gatedSkipped, _gatedDfSkipped, _repickBlocked, _pocketEscapeSkipped,
        _crossingRepickBlocked, _crossingGiveUpFallback, _campRepickBlocked, _startSnapped, _startSnapFailed,
        _snapDzRejected, _snapRefailed, _foreignFailureSkipped;
    private static long _nextReportTick;

    public static void Snapped() => Interlocked.Increment(ref _snapped);

    public const int SnapLinesPerWindow = 30;
    private static long _snapLines, _nextSnapWindowTick;

    /// <summary>
    /// Where a goal was moved to (at most <see cref="SnapLinesPerWindow"/> lines a minute): a snap onto another level
    /// would otherwise look like an ordinary on-mesh dead end in the later give-up lines.
    /// </summary>
    public static void LogSnap(GameBot bot, Vector3 from, Vector3 to) => LogMove("AUTONOMOUS_GOAL_SNAP", bot, from, to);

    /// <summary>Task 141: where a bot standing off the mesh was put (same budget as <see cref="LogSnap"/>).</summary>
    public static void LogStartSnap(GameBot bot, Vector3 from, Vector3 to) =>
        LogMove("AUTONOMOUS_START_SNAP", bot, from, to);

    /// <summary>Task 142: a goal snap the height guard refused (same budget as <see cref="LogSnap"/>).</summary>
    public static void LogSnapRejected(GameBot bot, Vector3 from, Vector3 to) =>
        LogMove("AUTONOMOUS_GOAL_SNAP_REJECTED", bot, from, to);

    private static void LogMove(string marker, GameBot bot, Vector3 from, Vector3 to)
    {
        long now = GameLoop.GameLoopTime;
        long window = Interlocked.Read(ref _nextSnapWindowTick);
        if (now >= window && Interlocked.CompareExchange(ref _nextSnapWindowTick, now + ReportIntervalMilliseconds, window) == window)
            Interlocked.Exchange(ref _snapLines, 0);
        if (Interlocked.Increment(ref _snapLines) > SnapLinesPerWindow)
            return;
        Log.Info($"{marker} bot={bot.Name} id={bot.DatabaseID} region={bot.CurrentRegionID} " +
                 $"from={from.X:0},{from.Y:0},{from.Z:0} to={to.X:0},{to.Y:0},{to.Z:0} " +
                 $"moved={Vector2.Distance(new(from.X, from.Y), new(to.X, to.Y)):0} dz={to.Z - from.Z:0}");
    }
    public static void SnapFailed() => Interlocked.Increment(ref _snapFailed);
    /// <summary>Task 141: a bot outside the pick box was put on the nearest connected floor.</summary>
    public static void StartSnapped() => Interlocked.Increment(ref _startSnapped);
    /// <summary>Task 141: a bot outside the pick box found no floor, or only one that leads nowhere.</summary>
    public static void StartSnapFailed() => Interlocked.Increment(ref _startSnapFailed);
    /// <summary>Task 142: the height guard refused a snap onto an unreachable floor near the bot.</summary>
    public static void SnapDzRejected() => Interlocked.Increment(ref _snapDzRejected);
    /// <summary>Task 142: a route to a snapped goal failed again (0.314.0: two wrong floors, 0.320.0: none).</summary>
    public static void SnapRefailed() => Interlocked.Increment(ref _snapRefailed);
    /// <summary>Task 142: another mover's off-mesh failure was set aside instead of ending the route.</summary>
    public static void ForeignFailureSkipped() => Interlocked.Increment(ref _foreignFailureSkipped);
    /// <summary>A PvP goal (an enemy standing in or on a keep whose gates the bot may not pass) was not offered.</summary>
    public static void GatedSkipped() => Interlocked.Increment(ref _gatedSkipped);
    /// <summary>A Darkness Falls entrance inside such a keep was left out of a route or camp search.</summary>
    public static void GatedDfSkipped() => Interlocked.Increment(ref _gatedDfSkipped);
    public static void RepickBlocked() => Interlocked.Increment(ref _repickBlocked);
    /// <summary>A crossing search took another route than the zone point the bot had just given up reaching (task 140).</summary>
    public static void CrossingRepickBlocked() => Interlocked.Increment(ref _crossingRepickBlocked);
    /// <summary>The given-up zone point was the only route, so the search kept it (task 140).</summary>
    public static void CrossingGiveUpFallback() => Interlocked.Increment(ref _crossingGiveUpFallback);
    /// <summary>A camp planning pass left out at least one spot the bot or its warband had just given up on (task 140).</summary>
    public static void CampRepickBlocked() => Interlocked.Increment(ref _campRepickBlocked);
    public static void PocketEscapeSkipped() => Interlocked.Increment(ref _pocketEscapeSkipped);

    public static void ReportIfDue(long now)
    {
        long next = Interlocked.Read(ref _nextReportTick);
        if (next == 0)
        {
            // The first call only arms the window so every line covers a full minute.
            Interlocked.CompareExchange(ref _nextReportTick, now + ReportIntervalMilliseconds, 0);
            return;
        }
        if (now < next || Interlocked.CompareExchange(ref _nextReportTick, now + ReportIntervalMilliseconds, next) != next)
            return;
        long snapped = Interlocked.Exchange(ref _snapped, 0), snapFailed = Interlocked.Exchange(ref _snapFailed, 0),
             gated = Interlocked.Exchange(ref _gatedSkipped, 0), gatedDf = Interlocked.Exchange(ref _gatedDfSkipped, 0),
             repick = Interlocked.Exchange(ref _repickBlocked, 0), pocket = Interlocked.Exchange(ref _pocketEscapeSkipped, 0),
             crossing = Interlocked.Exchange(ref _crossingRepickBlocked, 0),
             fallback = Interlocked.Exchange(ref _crossingGiveUpFallback, 0),
             camp = Interlocked.Exchange(ref _campRepickBlocked, 0),
             startSnapped = Interlocked.Exchange(ref _startSnapped, 0),
             startSnapFailed = Interlocked.Exchange(ref _startSnapFailed, 0),
             dzRejected = Interlocked.Exchange(ref _snapDzRejected, 0),
             refailed = Interlocked.Exchange(ref _snapRefailed, 0),
             foreign = Interlocked.Exchange(ref _foreignFailureSkipped, 0);
        if (snapped + snapFailed + gated + gatedDf + repick + pocket + crossing + fallback + camp + startSnapped +
            startSnapFailed + dzRejected + refailed + foreign == 0)
            return;
        Log.Info($"AUTONOMOUS_ROUTE_GOAL_FIX window_s={ReportIntervalMilliseconds / 1000} snapped={snapped} " +
                 $"snapFailed={snapFailed} gatedSkipped={gated} gatedDfSkipped={gatedDf} repickBlocked={repick} " +
                 $"pocketEscapeSkipped={pocket} crossingRepickBlocked={crossing} crossingGiveUpFallback={fallback} " +
                 $"campRepickBlocked={camp} startSnapped={startSnapped} startSnapFailed={startSnapFailed} " +
                 $"snapDzRejected={dzRejected} snapRefailed={refailed} foreignFailureSkipped={foreign}");
    }
}
