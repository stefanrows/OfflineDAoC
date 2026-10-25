using System;
using System.Collections.Generic;
using System.Linq;
using OfflineDaoc.Configuration;

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

    /// <summary>
    /// The charter of each slot of an RvR realm pool, slot = ordinal minus the pool's first ordinal. Every realm
    /// uses the same plan, so each realm gets the owner's bot-goals mix in the same proportions (R5a drew the
    /// charter from the global ordinal, which gave the realms an uneven mix). The plan apportions the slots by
    /// guild size (the 1:2:4 weights of <see cref="AutonomousCrewManager.BuildTargets"/>) in proportion to
    /// <see cref="AutonomousBotIdentity.CharterScores"/>; each slot goes to the charter furthest below its share
    /// of the weight planned so far, so every prefix of the plan (a realm with fewer than fifteen guilds) is
    /// balanced too, and a slot keeps its charter when the realm grows.
    /// </summary>
    public static AutonomousGuildCharter[] PlanCharters(PlayerTypeMix mix, int slots = MaximumGuildsPerPool)
    {
        int[] scores = AutonomousBotIdentity.CharterScores(mix);
        double total = Math.Max(1, scores.Sum());
        double[] assigned = new double[scores.Length];
        var plan = new AutonomousGuildCharter[Math.Max(0, slots)];
        double planned = 0;
        for (int slot = 0; slot < plan.Length; slot++)
        {
            int weight = SlotWeight(slot);
            planned += weight;
            int best = Enumerable.Range(0, scores.Length)
                .Where(index => scores[index] > 0 || scores.All(score => score <= 0))
                .OrderByDescending(index => scores[index] / total * planned - assigned[index])
                .ThenByDescending(index => scores[index])
                .ThenBy(index => index)
                .First();
            assigned[best] += weight;
            plan[slot] = (AutonomousGuildCharter)best;
        }
        return plan;
    }

    /// <summary>
    /// The charter of a new guild at this ordinal. Camlann: the historical draw from the global ordinal,
    /// unchanged. RvR: the per-realm plan of <see cref="PlanCharters"/> for the ordinal's slot in its pool.
    /// </summary>
    public static AutonomousGuildCharter CharterFor(WorldModelKind world, GuildPool pool, int ordinal, PlayerTypeMix mix)
    {
        if (!SingleRealmGuilds(world))
            return AutonomousBotIdentity.CharterForOrdinal(ordinal, mix);
        int slot = Math.Clamp(ordinal - pool.FirstOrdinal, 0, MaximumGuildsPerPool - 1);
        return PlanCharters(mix)[slot];
    }

    /// <summary>The 1:2:4 size weight of a pool slot, as <see cref="AutonomousCrewManager.BuildTargets"/> sizes it.</summary>
    public static int SlotWeight(int slot) => (Math.Max(0, slot) % 3) switch { 0 => 1, 1 => 2, _ => 4 };

    /// <summary>
    /// The realm a new guild alt joins in the RvR world: the realm with the fewest non-retired bots among the
    /// realms that have a managed guild, so the alt trickle keeps the realms even. Ties go Albion, Midgard,
    /// Hibernia. Null when no realm has a managed guild.
    /// </summary>
    public static eRealm? AltRealm(IEnumerable<eRealm> rosterRealms, IEnumerable<eRealm> guildRealms)
    {
        HashSet<eRealm> withGuild = (guildRealms ?? []).Where(IsPlayableRealm).ToHashSet();
        if (withGuild.Count == 0)
            return null;
        Dictionary<eRealm, int> counts = (rosterRealms ?? []).GroupBy(realm => realm)
            .ToDictionary(group => group.Key, group => group.Count());
        return withGuild
            .OrderBy(realm => counts.GetValueOrDefault(realm))
            .ThenBy(RealmOrder)
            .First();
    }

    /// <summary>
    /// The survivor an empty obsolete managed guild maps to (its keep claims and alliance follow it). Camlann:
    /// round robin over every target, as before. RvR: round robin over the targets of the guild's own realm, so
    /// a claim never passes to another realm; only when that realm has no guild left does it fall back to every
    /// target.
    /// </summary>
    public static string ObsoleteGuildTarget(WorldModelKind world, eRealm sourceRealm, int sequence,
        IReadOnlyList<(GuildPool Pool, AutonomousCrewManager.GuildTarget[] Targets)> plan)
    {
        AutonomousCrewManager.GuildTarget[] all = (plan ?? []).SelectMany(entry => entry.Targets).ToArray();
        if (all.Length == 0)
            return null;
        AutonomousCrewManager.GuildTarget[] candidates = all;
        if (SingleRealmGuilds(world))
        {
            AutonomousCrewManager.GuildTarget[] own = plan.Where(entry => entry.Pool.Realm == sourceRealm)
                .SelectMany(entry => entry.Targets).ToArray();
            if (own.Length > 0)
                candidates = own;
        }
        return candidates[Math.Max(0, sequence) % candidates.Length].GuildId;
    }

    private static bool IsPlayableRealm(eRealm realm) => realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia;

    private static int RealmOrder(eRealm realm) => realm switch
    {
        eRealm.Albion => 0,
        eRealm.Midgard => 1,
        eRealm.Hibernia => 2,
        _ => 3 + (int)realm,
    };

    private static int FirstOrdinal(eRealm realm) => RealmOrder(realm) * MaximumGuildsPerPool;
}
