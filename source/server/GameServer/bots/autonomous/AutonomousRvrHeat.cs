using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// Where the frontier has recently been fighting. Players drifted toward the
/// noise ("fight at the bridge!"); roaming groups use these spots as one of
/// their destinations. In memory only: a restart starts a quiet frontier.
/// </summary>
public static class AutonomousRvrHeat
{
    public readonly record struct Spot(Vector3 Position, double Heat);

    private sealed record Entry(Vector3 Position, DateTime AtUtc);

    private static readonly object Sync = new();
    private static readonly Dictionary<ushort, List<Entry>> Regions = new();
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(12);
    private const int MaximumPerRegion = 48;
    private const float MergeDistance = 900;

    public static void Record(GameLiving fighter)
    {
        if (fighter?.CurrentRegion == null)
            return;
        Record(fighter.CurrentRegionID, new Vector3(fighter.X, fighter.Y, fighter.Z), WorldSimulationClock.UtcNow);
    }

    public static void Record(ushort regionId, Vector3 position, DateTime nowUtc)
    {
        lock (Sync)
        {
            if (!Regions.TryGetValue(regionId, out List<Entry> entries))
                Regions[regionId] = entries = [];
            entries.RemoveAll(entry => nowUtc - entry.AtUtc > Lifetime ||
                Vector2.Distance(new(entry.Position.X, entry.Position.Y), new(position.X, position.Y)) < MergeDistance);
            entries.Add(new Entry(position, nowUtc));
            if (entries.Count > MaximumPerRegion)
                entries.RemoveAt(0);
        }
    }

    /// <summary>Recent fight spots, hottest (newest) first; heat fades from 1 to 0 over the lifetime.</summary>
    public static IReadOnlyList<Spot> Recent(ushort regionId, DateTime nowUtc)
    {
        lock (Sync)
        {
            if (!Regions.TryGetValue(regionId, out List<Entry> entries))
                return [];
            entries.RemoveAll(entry => nowUtc - entry.AtUtc > Lifetime);
            return entries
                .Select(entry => new Spot(entry.Position,
                    1 - (nowUtc - entry.AtUtc).TotalMilliseconds / Lifetime.TotalMilliseconds))
                .OrderByDescending(spot => spot.Heat)
                .ToArray();
        }
    }

    internal static void ResetForTests()
    {
        lock (Sync)
            Regions.Clear();
    }
}
