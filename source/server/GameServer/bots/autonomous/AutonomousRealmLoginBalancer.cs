using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>Chooses the next login by realm load rather than database id.</summary>
public static class AutonomousRealmLoginBalancer
{
    public readonly record struct Candidate(long BotId, eRealm Realm);

    public static long? SelectNext(
        IEnumerable<Candidate> candidates,
        IReadOnlyDictionary<eRealm, int> activeAndPending)
    {
        Candidate[] available = candidates?
            .Where(candidate => candidate.Realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia)
            .OrderBy(candidate => candidate.BotId)
            .ToArray() ?? [];
        if (available.Length == 0)
            return null;

        eRealm selectedRealm = available
            .Select(candidate => candidate.Realm)
            .Distinct()
            .OrderBy(realm => activeAndPending != null && activeAndPending.TryGetValue(realm, out int count) ? count : 0)
            .ThenBy(realm => (int)realm)
            .First();

        return available.First(candidate => candidate.Realm == selectedRealm).BotId;
    }
}

/// <summary>Chooses the next login by crew load. Realm is deliberately absent.</summary>
public static class AutonomousCrewLoginBalancer
{
    public readonly record struct Candidate(long BotId, string GuildId);

    public static long? SelectNext(
        IEnumerable<Candidate> candidates,
        IReadOnlyDictionary<string, int> activeAndPending)
    {
        Candidate[] available = candidates?
            .OrderBy(candidate => candidate.BotId)
            .ToArray() ?? [];
        if (available.Length == 0)
            return null;

        string selectedCrew = available
            .Select(candidate => string.IsNullOrWhiteSpace(candidate.GuildId) ? string.Empty : candidate.GuildId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(crew => activeAndPending != null && activeAndPending.TryGetValue(crew, out int count) ? count : 0)
            .ThenBy(crew => crew, StringComparer.Ordinal)
            .First();

        return available.First(candidate => string.Equals(
            string.IsNullOrWhiteSpace(candidate.GuildId) ? string.Empty : candidate.GuildId,
            selectedCrew,
            StringComparison.Ordinal)).BotId;
    }
}
