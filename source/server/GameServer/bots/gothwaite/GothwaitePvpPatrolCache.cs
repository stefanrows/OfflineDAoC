using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OfflineDaoc.Configuration;

namespace DOL.GS;

/// <summary>
/// The patrol points a bot may use, cached per arena edge (docs/GOTHWAITE_PVP_MODE.md section 8, stage 4). A bot's edge takes only
/// two values (the low band's edge and the high band's edge), so two slots are enough. Each slot is an immutable array published with a
/// volatile write, so the driver reads it without a lock and without allocating. The points are exactly those
/// <see cref="GothwaitePvpArena.PatrolPointsFor"/> returns; a new arena instance (every <see cref="GothwaitePvpMode.Apply"/> makes one) or
/// another edge rebuilds the slot.
/// </summary>
public static class GothwaitePvpPatrolCache
{
    private sealed record Slot(GothwaitePvpArena Arena, int Edge, IReadOnlyList<GothwaitePvpSpot> Points);

    private static Slot _lowBand;
    private static Slot _highBand;

    /// <summary>The patrol points of a bot of this level in this arena and these settings: the cached array, built on first use.</summary>
    public static IReadOnlyList<GothwaitePvpSpot> PointsFor(GothwaitePvpArena arena, GothwaitePvpSettings settings, int level)
    {
        int edge = GothwaitePvpArena.EdgeFor(level, settings);
        ref Slot slot = ref (edge == settings.LowBandEdge ? ref _lowBand : ref _highBand);
        Slot current = Volatile.Read(ref slot);
        if (current != null && ReferenceEquals(current.Arena, arena) && current.Edge == edge)
            return current.Points;
        var built = new Slot(arena, edge, arena.PatrolPointsFor(level, settings).ToArray());
        Volatile.Write(ref slot, built);
        return built.Points;
    }

    /// <summary>Forgets both slots. Tests use it.</summary>
    public static void Clear()
    {
        Volatile.Write(ref _lowBand, null);
        Volatile.Write(ref _highBand, null);
    }
}
