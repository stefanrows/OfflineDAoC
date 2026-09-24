using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace DOL.GS;

/// <summary>Live, advisory crowd signals for outdoor camp selection.</summary>
public static class AutonomousOutdoorCampPressure
{
    private const long EmptyPenaltyMilliseconds = 5 * 60_000;
    private static IReadOnlyDictionary<string, int> _population = new Dictionary<string, int>();
    private static readonly ConcurrentDictionary<string, long> EmptyUntil = new(StringComparer.Ordinal);

    public static void PublishPopulation(IReadOnlyDictionary<string, int> population) =>
        Volatile.Write(ref _population, population ?? new Dictionary<string, int>());

    public static int Population(string campId) => string.IsNullOrWhiteSpace(campId) ? 0 :
        Volatile.Read(ref _population).TryGetValue(campId, out int count) ? count : 0;

    public static void MarkEmpty(string campId, long now)
    {
        if (!string.IsNullOrWhiteSpace(campId))
            EmptyUntil[campId] = now + EmptyPenaltyMilliseconds;
    }

    public static bool WasRecentlyEmpty(string campId, long now) =>
        !string.IsNullOrWhiteSpace(campId) && EmptyUntil.TryGetValue(campId, out long until) && now < until;

    public static void Prune(long now)
    {
        foreach (var entry in EmptyUntil)
            if (entry.Value <= now)
                ((ICollection<KeyValuePair<string, long>>)EmptyUntil).Remove(entry);
    }
}
