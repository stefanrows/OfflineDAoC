using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// Owner decision 2b (2026-09-28, docs/TASKS.md item 48): automatic sieges are
/// limited per attacking guild, not per server. Each guild runs at most two
/// sieges of its own at a time (raised from one on 2026-10-12); other guilds
/// may open theirs concurrently, up to a server-wide safety cap. Guildless
/// (realm-led) events count per realm.
/// Relic-carrier events still pause new sieges (relic raids stay off).
/// </summary>
public static partial class AutonomousRvrEventLayer
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
    public static string SiegeSideKey(string guildName, eRealm realm) =>
        string.IsNullOrEmpty(guildName) ? "realm:" + realm : "guild:" + guildName;

    /// <summary>Pure rule: a side may open a new siege while it runs fewer than
    /// <see cref="MaxSiegesPerSide"/> of its own and fewer than
    /// <see cref="MaxConcurrentSieges"/> run in total.</summary>
    public static bool MayOpenSiege(string sideKey, IEnumerable<string> runningSiegeSides, bool carrierEventActive)
    {
        if (carrierEventActive || sideKey == null) return false;
        var running = runningSiegeSides?.ToArray() ?? Array.Empty<string>();
        return running.Length < MaxConcurrentSieges &&
            running.Count(side => string.Equals(side, sideKey, StringComparison.Ordinal)) < MaxSiegesPerSide;
    }

    /// <summary>Call under <c>Sync</c>.</summary>
    private static bool MayOpenSiegeLocked(string guildName, eRealm realm) =>
        MayOpenSiege(SiegeSideKey(guildName, realm),
            Events.Values.Where(active => !active.DefenseReaction)
                .Select(active => SiegeSideKey(active.AttackerGuild, active.AttackerRealm)),
            CarrierEvents.Count != 0);
}
