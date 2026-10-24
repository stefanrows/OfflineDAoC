using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading;
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
    private static long _snapped, _snapFailed, _gatedSkipped, _gatedDfSkipped, _repickBlocked, _pocketEscapeSkipped;
    private static long _nextReportTick;

    public static void Snapped() => Interlocked.Increment(ref _snapped);

    public const int SnapLinesPerWindow = 30;
    private static long _snapLines, _nextSnapWindowTick;

    /// <summary>
    /// Where a goal was moved to (at most <see cref="SnapLinesPerWindow"/> lines a minute): a snap onto another level
    /// would otherwise look like an ordinary on-mesh dead end in the later give-up lines.
    /// </summary>
    public static void LogSnap(GameBot bot, Vector3 from, Vector3 to)
    {
        long now = GameLoop.GameLoopTime;
        long window = Interlocked.Read(ref _nextSnapWindowTick);
        if (now >= window && Interlocked.CompareExchange(ref _nextSnapWindowTick, now + ReportIntervalMilliseconds, window) == window)
            Interlocked.Exchange(ref _snapLines, 0);
        if (Interlocked.Increment(ref _snapLines) > SnapLinesPerWindow)
            return;
        Log.Info($"AUTONOMOUS_GOAL_SNAP bot={bot.Name} id={bot.DatabaseID} region={bot.CurrentRegionID} " +
                 $"from={from.X:0},{from.Y:0},{from.Z:0} to={to.X:0},{to.Y:0},{to.Z:0} " +
                 $"moved={Vector2.Distance(new(from.X, from.Y), new(to.X, to.Y)):0} dz={to.Z - from.Z:0}");
    }
    public static void SnapFailed() => Interlocked.Increment(ref _snapFailed);
    /// <summary>A PvP goal (an enemy standing in or on a keep whose gates the bot may not pass) was not offered.</summary>
    public static void GatedSkipped() => Interlocked.Increment(ref _gatedSkipped);
    /// <summary>A Darkness Falls entrance inside such a keep was left out of a route or camp search.</summary>
    public static void GatedDfSkipped() => Interlocked.Increment(ref _gatedDfSkipped);
    public static void RepickBlocked() => Interlocked.Increment(ref _repickBlocked);
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
             repick = Interlocked.Exchange(ref _repickBlocked, 0), pocket = Interlocked.Exchange(ref _pocketEscapeSkipped, 0);
        if (snapped + snapFailed + gated + gatedDf + repick + pocket == 0)
            return;
        Log.Info($"AUTONOMOUS_ROUTE_GOAL_FIX window_s={ReportIntervalMilliseconds / 1000} snapped={snapped} " +
                 $"snapFailed={snapFailed} gatedSkipped={gated} gatedDfSkipped={gatedDf} repickBlocked={repick} " +
                 $"pocketEscapeSkipped={pocket}");
    }
}
