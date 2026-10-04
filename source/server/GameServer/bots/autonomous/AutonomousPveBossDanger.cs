using System;
using System.Collections.Generic;
using System.Threading;

namespace DOL.GS;

/// <summary>
/// Places where a monster far above the victim's level killed an autonomous
/// bot. Live 0.162.0: two named bosses (levels 65 and 75) killed level-50 bots
/// about 5,500 times in 19 hours, because their XP camps sat next to them.
/// Players learn to keep away from such a spot; so do the bots: no XP camp
/// within <see cref="AvoidRadius"/> of a remembered spot for
/// <see cref="Lifetime"/>. In memory only: a restart forgets.
/// </summary>
public static class AutonomousPveBossDanger
{
    /// <summary>A killer this many levels above its victim marks the spot.</summary>
    public const int LevelGap = 10;
    public const float AvoidRadius = 3_000;
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);
    private const int MaximumSpots = 512;

    private readonly record struct Spot(ushort RegionId, int X, int Y, DateTime Utc);

    private static readonly object Sync = new();
    // Deaths publish a new bounded snapshot; camp planners never hold a shared
    // lock while checking thousands of candidate cells.
    private static Spot[] _spots = [];

    /// <summary>Whether a monster kill marks its place as a boss spot.</summary>
    public static bool Marks(string killerType, int killerLevel, int victimLevel, bool countsAsPvp) =>
        !countsAsPvp && string.Equals(killerType, "mob", StringComparison.Ordinal) &&
        killerLevel >= victimLevel + LevelGap;

    public static void Record(ushort regionId, int x, int y, DateTime utc)
    {
        lock (Sync)
        {
            var spots = new List<Spot>(_spots.Length + 1);
            foreach (Spot spot in _spots)
                if (utc - spot.Utc <= Lifetime)
                    spots.Add(spot);
            if (spots.Count >= MaximumSpots)
                spots.RemoveAt(0);
            spots.Add(new Spot(regionId, x, y, utc));
            Volatile.Write(ref _spots, spots.ToArray());
        }
    }

    public static bool IsNear(ushort regionId, int x, int y, DateTime utc)
    {
        foreach (Spot spot in Volatile.Read(ref _spots))
        {
            if (spot.RegionId != regionId || utc - spot.Utc > Lifetime)
                continue;
            double dx = (double)spot.X - x, dy = (double)spot.Y - y;
            if (dx * dx + dy * dy <= (double)AvoidRadius * AvoidRadius)
                return true;
        }
        return false;
    }

    /// <summary>Tests only.</summary>
    public static void Clear()
    {
        lock (Sync)
            Volatile.Write(ref _spots, []);
    }
}
