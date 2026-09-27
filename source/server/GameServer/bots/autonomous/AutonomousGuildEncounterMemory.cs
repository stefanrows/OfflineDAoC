using System;
using System.Collections.Generic;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>
/// How a guild's recent fights against another guild went. A crew that keeps
/// losing to one guild gets careful around its groups; one that keeps winning
/// gets bold. Grudges (persisted, see <see cref="AutonomousGuildGrudgeMemory"/>)
/// still decide whom a guild hunts; this only colours the odds it accepts.
/// In memory, fading after an hour.
/// </summary>
public static class AutonomousGuildEncounterMemory
{
    private sealed class Tally
    {
        public int Wins;
        public int Losses;
        public DateTime LastUtc;
    }

    private static readonly object Sync = new();
    private static readonly Dictionary<(string Own, string Enemy), Tally> Tallies = new();
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public static void RecordDeath(GameLiving victim, GameObject killer, DateTime nowUtc)
    {
        string own = GuildId(victim);
        string enemy = GuildId(PvpCombatant.Resolve(killer as GameLiving));
        if (own == null || enemy == null || own == enemy)
            return;
        lock (Sync)
        {
            Get((own, enemy), nowUtc).Losses++;
            Get((enemy, own), nowUtc).Wins++;
        }
    }

    /// <summary>1.0 when even; below 1 after repeated losses, above 1 after repeated wins.</summary>
    public static double AppetiteFactor(GameLiving own, GameLiving enemy, DateTime nowUtc)
    {
        string ownId = GuildId(own);
        string enemyId = GuildId(PvpCombatant.Resolve(enemy));
        if (ownId == null || enemyId == null)
            return 1;
        lock (Sync)
        {
            if (!Tallies.TryGetValue((ownId, enemyId), out Tally tally) || nowUtc - tally.LastUtc > Lifetime)
                return 1;
            return Factor(tally.Wins, tally.Losses);
        }
    }

    public static double Factor(int wins, int losses) => Math.Clamp(1 + (wins - losses) * 0.08, 0.6, 1.3);

    private static Tally Get((string, string) key, DateTime nowUtc)
    {
        if (!Tallies.TryGetValue(key, out Tally tally) || nowUtc - tally.LastUtc > Lifetime)
            Tallies[key] = tally = new Tally();
        tally.LastUtc = nowUtc;
        return tally;
    }

    private static string GuildId(GameLiving living) => living switch
    {
        GameBot bot when bot.Guild != null => bot.Guild.GuildID,
        GamePlayer player when player.Guild != null => player.Guild.GuildID,
        _ => null,
    };
}
