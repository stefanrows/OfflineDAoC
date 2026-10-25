using System;
using System.Collections.Generic;
using System.Linq;
using static DOL.GS.AutonomousRvrEventLayer;

namespace DOL.GS;

/// <summary>
/// Owner decision 2b (2026-09-28, docs/TASKS.md item 48): automatic sieges are
/// limited per attacking guild, not per server. Each guild runs at most two
/// sieges of its own at a time (raised from one on 2026-10-12); other guilds
/// may open theirs concurrently, up to a server-wide safety cap. Guildless
/// (realm-led) events count per realm.
/// Relic-carrier events still pause new sieges (relic raids stay off).
/// </summary>
public static partial class CamlannRvrEventLayer
{
    /// <summary>At most this many attacking sieges (not defense responses) run
    /// server-wide at once. A safety cap for load and for the 21 claimable
    /// keeps; the per-guild rule is the real limit.</summary>
    public const int MaxConcurrentSieges = 6;

    /// <summary>At most this many attacking sieges of one side (guild or realm)
    /// run at once. Owner decision 2026-10-12 raises decision 2b from one to two.</summary>
    public const int MaxSiegesPerSide = 2;

    /// <summary>The side an assault belongs to: the opener's guild, or its
    /// realm for guildless events.</summary>
    public static string SiegeSideKey(string guildName, eRealm realm) => SiegeSideKey(WorldMode.Current, guildName, realm);

    /// <summary>RvR world (R4b): the side is always the realm; a guild never holds a siege slot of its own.</summary>
    public static string SiegeSideKey(WorldModelKind world, string guildName, eRealm realm) =>
        world == WorldModelKind.Realm || string.IsNullOrEmpty(guildName) ? "realm:" + realm : "guild:" + guildName;

    /// <summary>Pure rule: a side may open a new siege while it runs fewer than
    /// <see cref="MaxSiegesPerSide"/> of its own and fewer than
    /// <see cref="MaxConcurrentSieges"/> run in total.</summary>
    public static bool MayOpenSiege(string sideKey, IEnumerable<string> runningSiegeSides, bool carrierEventActive) =>
        MayOpenSiege(sideKey, runningSiegeSides, carrierEventActive, MaxSiegesPerSide);

    /// <summary>As above with the side's own limit (RvR world: the realm's attack plan).</summary>
    public static bool MayOpenSiege(string sideKey, IEnumerable<string> runningSiegeSides, bool carrierEventActive, int maxPerSide)
    {
        if (carrierEventActive || sideKey == null) return false;
        var running = runningSiegeSides?.ToArray() ?? Array.Empty<string>();
        return running.Length < MaxConcurrentSieges &&
            running.Count(side => string.Equals(side, sideKey, StringComparison.Ordinal)) < maxPerSide;
    }

    /// <summary>
    /// RvR world (R4b): a realm opens sieges as its attack plan allows (single 1, double 2, opportunist 1 and
    /// only while no other realm runs one); a forced (GM) start keeps the per-side limit. Camlann: per guild, as
    /// before. Call under <c>Sync</c>.
    /// </summary>
    private static bool MayOpenSiegeLocked(string guildName, eRealm realm, long nowTick, bool forced = false)
    {
        string[] running = Events.Values.Where(active => !active.DefenseReaction)
            .Select(active => SiegeSideKey(active.AttackerGuild, active.AttackerRealm)).ToArray();
        string side = SiegeSideKey(guildName, realm);
        if (!WorldMode.IsRvr || forced)
            return MayOpenSiege(side, running, CarrierEvents.Count != 0);
        OpenBgAttackPlan plan = AutonomousRealmArmyPlans.PlanFor(realm, nowTick);
        int own = running.Count(entry => string.Equals(entry, side, StringComparison.Ordinal));
        return MayOpenSiege(side, running, CarrierEvents.Count != 0, AutonomousRealmArmyPolicy.MaximumOwnSieges(plan)) &&
            AutonomousRealmArmyPolicy.MayOpenOwnSiege(plan, own, running.Length - own);
    }
}
