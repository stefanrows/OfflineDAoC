using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// How the crew manager seeds the autonomous bot guilds of a world (docs/RVR_WORLDS.md). The Camlann
/// world plans one mixed pool over the whole roster, as before: up to five 1:2:4 size triplets, 15 guilds,
/// each mixing all three realms. The RvR world plans one pool per realm: the same size rule per realm
/// (15 guilds for about 600 bots, 45 in total), every guild created with its realm and offered only to
/// bots of that realm, so no bot guild ever mixes realms.
/// </summary>
public static class AutonomousGuildSeeding
{
    /// <summary>The cap of one pool: the historical 15 managed guilds.</summary>
    public const int MaximumGuildsPerPool = AutonomousCrewManager.MaximumManagedGuilds;

    /// <summary>
    /// One planning pool. <see cref="Realm"/> is <see cref="eRealm.None"/> for the Camlann mixed pool.
    /// <see cref="FirstOrdinal"/> is the first charter ordinal of the pool, fixed per realm (Albion 0,
    /// Midgard 15, Hibernia 30) so guild names stay unique and do not shift when another realm grows.
    /// </summary>
    public readonly record struct GuildPool(eRealm Realm, int BotCount, int GuildCount, int FirstOrdinal);

    public static bool SingleRealmGuilds(WorldModelKind world) => world == WorldModelKind.Realm;

    /// <summary>The pools for a roster of these realms, in Albion, Midgard, Hibernia order for the RvR world.</summary>
    public static GuildPool[] PlanPools(WorldModelKind world, IEnumerable<eRealm> botRealms)
    {
        eRealm[] realms = (botRealms ?? []).ToArray();
        if (!SingleRealmGuilds(world))
            return [new(eRealm.None, realms.Length, AutonomousCrewManager.DesiredManagedGuildCount(realms.Length), 0)];

        return realms
            .GroupBy(realm => realm)
            .OrderBy(group => RealmOrder(group.Key))
            .Select(group => new GuildPool(group.Key, group.Count(),
                AutonomousCrewManager.DesiredManagedGuildCount(group.Count()), FirstOrdinal(group.Key)))
            .ToArray();
    }

    /// <summary>The realm a new guild of this pool is created with: the pool's realm in RvR, none in Camlann.</summary>
    public static eRealm GuildRealm(WorldModelKind world, GuildPool pool) =>
        SingleRealmGuilds(world) ? pool.Realm : eRealm.None;

    /// <summary>Whether an existing managed guild of this realm serves the pool (Camlann: every guild).</summary>
    public static bool GuildServesPool(WorldModelKind world, eRealm guildRealm, GuildPool pool) =>
        !SingleRealmGuilds(world) || guildRealm == pool.Realm;

    /// <summary>Whether a bot of this realm may be placed in a managed guild of that realm.</summary>
    public static bool MayJoin(WorldModelKind world, eRealm botRealm, eRealm guildRealm) =>
        !SingleRealmGuilds(world) || botRealm == guildRealm;

    /// <summary>The managed-guild cap of the world: 15 for Camlann, 15 per realm pool in RvR.</summary>
    public static int MaximumManagedGuilds(WorldModelKind world, IReadOnlyCollection<GuildPool> pools) =>
        SingleRealmGuilds(world) ? MaximumGuildsPerPool * Math.Max(1, pools?.Count ?? 0) : MaximumGuildsPerPool;

    /// <summary>
    /// Plans the guild of every candidate. Camlann: one call of <see cref="AutonomousCrewManager.PlanAssignments"/>
    /// over all targets, unchanged. RvR: one call per pool, with that pool's realm members and targets only.
    /// </summary>
    public static Dictionary<long, string> PlanAssignments(WorldModelKind world,
        IReadOnlyList<(GuildPool Pool, AutonomousCrewManager.GuildTarget[] Targets)> plan,
        IEnumerable<AutonomousCrewManager.AssignmentCandidate> fixedMembers,
        IEnumerable<AutonomousCrewManager.AssignmentCandidate> candidates)
    {
        AutonomousCrewManager.AssignmentCandidate[] fixedArray = (fixedMembers ?? []).ToArray();
        AutonomousCrewManager.AssignmentCandidate[] candidateArray = (candidates ?? []).ToArray();
        if (plan == null || plan.Count == 0)
            return new Dictionary<long, string>();
        if (!SingleRealmGuilds(world))
            return AutonomousCrewManager.PlanAssignments(fixedArray, candidateArray, plan.SelectMany(entry => entry.Targets).ToArray());

        var result = new Dictionary<long, string>();
        foreach ((GuildPool pool, AutonomousCrewManager.GuildTarget[] targets) in plan)
        {
            foreach (KeyValuePair<long, string> assignment in AutonomousCrewManager.PlanAssignments(
                         fixedArray.Where(member => member.Realm == pool.Realm),
                         candidateArray.Where(candidate => candidate.Realm == pool.Realm),
                         targets))
                result[assignment.Key] = assignment.Value;
        }
        return result;
    }

    private static int RealmOrder(eRealm realm) => realm switch
    {
        eRealm.Albion => 0,
        eRealm.Midgard => 1,
        eRealm.Hibernia => 2,
        _ => 3 + (int)realm,
    };

    private static int FirstOrdinal(eRealm realm) => RealmOrder(realm) * MaximumGuildsPerPool;
}
