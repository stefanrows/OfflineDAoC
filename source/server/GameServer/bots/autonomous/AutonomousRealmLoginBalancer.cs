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
    public readonly record struct Candidate(long BotId, string GuildId, int Level = 0, int RegionId = 0,
        eCharacterClass CharacterClass = default);

    public static long? SelectNext(
        IEnumerable<Candidate> candidates,
        IReadOnlyDictionary<string, int> activeAndPending,
        IEnumerable<Candidate> activeCandidates = null)
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

        Candidate[] active = activeCandidates?.ToArray() ?? [];
        return available.Where(candidate => string.Equals(
                string.IsNullOrWhiteSpace(candidate.GuildId) ? string.Empty : candidate.GuildId,
                selectedCrew,
                StringComparison.Ordinal))
            .OrderByDescending(candidate => CohortMatches(candidate, active))
            .ThenByDescending(candidate => FillsMissingRole(candidate, active))
            .ThenBy(candidate => candidate.BotId)
            .First().BotId;
    }

    private static int CohortMatches(Candidate candidate, Candidate[] active) => active.Count(member =>
        string.Equals(member.GuildId, candidate.GuildId, StringComparison.Ordinal) &&
        member.RegionId == candidate.RegionId && AutonomousBotGroupCoordinator.LevelsCompatible(member.Level, candidate.Level));

    private static int FillsMissingRole(Candidate candidate, Candidate[] active)
    {
        Candidate[] cohort = active.Where(member => string.Equals(member.GuildId, candidate.GuildId, StringComparison.Ordinal) &&
            member.RegionId == candidate.RegionId && AutonomousBotGroupCoordinator.LevelsCompatible(member.Level, candidate.Level)).ToArray();
        bool needsHealer = !cohort.Any(member => BotPartyRoles.IsHealingClass(member.CharacterClass));
        bool needsFrontline = !cohort.Any(member => BotPartyRoles.For(member.CharacterClass) == BotPartyRole.Tank);
        return (needsHealer && BotPartyRoles.IsHealingClass(candidate.CharacterClass) ? 2 : 0) +
               (needsFrontline && BotPartyRoles.For(candidate.CharacterClass) == BotPartyRole.Tank ? 1 : 0);
    }
}
