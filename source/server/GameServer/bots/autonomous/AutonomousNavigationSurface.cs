using System;
using System.Numerics;

namespace DOL.GS;

public static class AutonomousNavigationSurface
{
    // Persisted world coordinates are integral while Detour floors are
    // fractional, and some city geometry records its standing height slightly
    // above the Detour polygon. This remains deliberately small:
    // it repairs rounding/step drift without snapping down through a bridge,
    // shelf, upper floor, or other legitimate stacked surface.
    private const float MaximumDownwardFloorCorrection = 32f;

    public static Vector3? MoveAlongGround(IPathfindingMgr nav, Zone zone, Vector3 start, Vector3 end)
    {
        if (nav is LocalPathfindingMgr local)
            return local.GetMoveAlongSurfaceGrounded(zone, start, end, nav.DefaultFilters);
        // No guessed height/nearest island for unsupported remote providers.
        return null;
    }

    // Task 142: Detour's moveAlongSurface is a 2D query, so its result keeps
    // the start's height. A retreat, disengage or stealth break point drawn
    // that way down a slope hung 140-500 units off the mesh, its path failed,
    // and the bot's next route consumed that failure and gave up (at least 13
    // of the 38 off-mesh-goal give-ups of the 0.320.0 run). Ground the point
    // where the provider can; otherwise keep the former surface point.
    public static Vector3? MoveAlongGroundOrSurface(IPathfindingMgr nav, Zone zone, Vector3 start, Vector3 end) =>
        zone == null ? null :
        MoveAlongGround(nav, zone, start, end) ?? nav.GetMoveAlongSurface(zone, start, end, nav.DefaultFilters);
    // Repair stale imported/interpolated Z, not horizontal path obstruction.
    // A wide nearest-poly search can select a table, another floor, or the far
    // side of a wall. Keep XY effectively fixed and retain bounded vertical range.
    public static bool TryFloor(IPathfindingMgr nav, Zone zone, Vector3 position, out Vector3 floor)
    {
        floor = position;
        if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;
        Vector3? candidate = nav.GetClosestPoint(zone, position, 2, 2, 128, nav.DefaultFilters);
        if (!candidate.HasValue || !IsLocalFloor(position, candidate.Value)) return false;
        floor = candidate.Value;
        return true;
    }

    public static bool IsLocalFloor(Vector3 position, Vector3 floor) =>
        float.IsFinite(floor.X) && float.IsFinite(floor.Y) && float.IsFinite(floor.Z) &&
        Vector2.DistanceSquared(new(position.X, position.Y), new(floor.X, floor.Y)) <= 4 &&
        floor.Z >= position.Z - MaximumDownwardFloorCorrection && floor.Z - position.Z <= 128;
}
