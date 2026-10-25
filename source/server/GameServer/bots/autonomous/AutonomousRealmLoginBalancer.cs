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

    /// <summary>The playable realms in login tie order.</summary>
    public static readonly eRealm[] Realms = [eRealm.Albion, eRealm.Midgard, eRealm.Hibernia];

    /// <summary>
    /// The online target of each realm in the RvR world: the configured target per realm, capped by that realm's
    /// non-retired roster; a target of zero or less means the realm's whole roster. A realm with fewer bots
    /// than the target is a shortfall: it logs in everything it has, and the other realms do not fill its gap,
    /// so the realms stay even rather than the total.
    /// </summary>
    public static Dictionary<eRealm, int> Targets(int realmTarget, IEnumerable<eRealm> rosterRealms)
    {
        Dictionary<eRealm, int> roster = (rosterRealms ?? []).GroupBy(realm => realm)
            .ToDictionary(group => group.Key, group => group.Count());
        return Realms.ToDictionary(realm => realm, realm =>
        {
            int count = roster.GetValueOrDefault(realm);
            return realmTarget > 0 ? Math.Min(realmTarget, count) : count;
        });
    }

    /// <summary>The realms whose roster is below the configured target, with the bots they lack.</summary>
    public static Dictionary<eRealm, int> Shortfalls(int realmTarget, IEnumerable<eRealm> rosterRealms)
    {
        if (realmTarget <= 0)
            return new Dictionary<eRealm, int>();
        Dictionary<eRealm, int> targets = Targets(realmTarget, rosterRealms);
        return targets.Where(pair => pair.Value < realmTarget)
            .ToDictionary(pair => pair.Key, pair => realmTarget - pair.Value);
    }

    /// <summary>
    /// The realm of the next login in the RvR world: among the realms below their target that still have a bot
    /// to log in, the one with the fewest active and pending bots (the upstream rule), ties in Albion, Midgard,
    /// Hibernia order. Null when every realm is at its target or has nobody left. A realm above its target
    /// (the owner lowered it) gets no logins; nobody is logged out.
    /// </summary>
    public static eRealm? SelectRealm(IReadOnlyDictionary<eRealm, int> targets,
        IReadOnlyDictionary<eRealm, int> activeAndPending, IEnumerable<eRealm> availableRealms)
    {
        HashSet<eRealm> available = (availableRealms ?? []).ToHashSet();
        eRealm[] open = Realms
            .Where(realm => available.Contains(realm) &&
                Load(activeAndPending, realm) < (targets != null && targets.TryGetValue(realm, out int target) ? target : 0))
            .ToArray();
        return open.Length == 0
            ? null
            : open.OrderBy(realm => Load(activeAndPending, realm)).ThenBy(realm => Array.IndexOf(Realms, realm)).First();
    }

    /// <summary>The startup ramp applied to each realm's own target (33 % in five minutes, all by the ramp end).</summary>
    public static Dictionary<eRealm, int> RampedTargets(IReadOnlyDictionary<eRealm, int> targets, bool enabled,
        int rampMinutes, TimeSpan sinceStartup) =>
        Realms.ToDictionary(realm => realm, realm => AutonomousPopulationRamp.DesiredActiveCount(enabled,
            targets != null && targets.TryGetValue(realm, out int target) ? target : 0, rampMinutes, sinceStartup));

    /// <summary>
    /// The logins still owed: the sum of every realm's deficit below its (ramped) target. A realm above its
    /// target counts zero, so it cannot hide another realm's gap.
    /// </summary>
    public static int Missing(IReadOnlyDictionary<eRealm, int> targets, IReadOnlyDictionary<eRealm, int> activeAndPending) =>
        Realms.Sum(realm => Math.Max(0, (targets != null && targets.TryGetValue(realm, out int target) ? target : 0) -
            Load(activeAndPending, realm)));

    private static int Load(IReadOnlyDictionary<eRealm, int> load, eRealm realm) =>
        load != null && load.TryGetValue(realm, out int count) ? count : 0;
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
