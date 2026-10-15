using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DOL.GS;

/// <summary>
/// The reserved world bots of the open battleground while the Gothwaite PvP mode is on: a small, stable set per realm
/// that is not a mode bot, so the open BG crew can still be drawn from ordinary world bots. Chosen once at startup
/// from the saved bot rows; immutable and published with <see cref="Volatile"/> (read on brain threads, no locks).
/// </summary>
public static class GothwaitePvpOpenBgReserve
{
    private static readonly IReadOnlySet<long> Empty = FrozenSet<long>.Empty;
    private static IReadOnlySet<long> _set = Empty;

    /// <summary>The current reserved bot ids. Never null, never mutated after publication.</summary>
    public static IReadOnlySet<long> Set => Volatile.Read(ref _set);

    public static void Install(IReadOnlySet<long> set) => Volatile.Write(ref _set, set == null || set.Count == 0 ? Empty : set.ToFrozenSet());

    public static bool IsReserved(long botId) => Set.Contains(botId);

    /// <summary>True for a mode-on autonomous world bot whose database id is reserved.</summary>
    public static bool IsOpenBgReserved(GameLiving living) =>
        living is GameBot bot &&
        IsReservedWorldBot(GothwaitePvpMode.IsActive, bot.IsAutonomousWorldBot, bot.IsTemporaryGroupHelper,
            bot.IsBattlegroundEncounterBot, bot.IsPersistentPlayerCompanion, bot.DatabaseID);

    /// <summary>Reserved bot that is idle between deployments (see <see cref="GothwaitePvpOpenBgStandby"/>).</summary>
    public static bool IsStandby(GameLiving living) => GothwaitePvpOpenBgStandby.IsStandby(living);

    /// <summary>Pure form of <see cref="IsOpenBgReserved"/>: the id is the bot's DatabaseID (the BotId of its row).</summary>
    public static bool IsReservedWorldBot(bool modeActive, bool autonomousWorldBot, bool temporaryHelper, bool encounterBot,
        bool companion, long databaseId) =>
        modeActive && autonomousWorldBot && !temporaryHelper && !encounterBot && !companion && IsReserved(databaseId);

    /// <summary>ceil(2.5 * min(seats, ceil(maxBots / 3))): the crew's seats plus a replacement margin.</summary>
    public static int ReserveSize(int seats, int maxBots)
    {
        int share = (int)Math.Ceiling(Math.Max(0, maxBots) / 3.0);
        return (int)Math.Ceiling(2.5 * Math.Min(Math.Max(0, seats), share));
    }

    /// <summary>Deterministic spread of an id (Knuth multiplicative hash, low 32 bits).</summary>
    public static ulong Order(long botId) => unchecked((ulong)botId * 2654435761UL) & 0xFFFFFFFF;

    /// <summary>The reserved ids: empty when the mode or the open BG is off; per realm 1-3 the first
    /// <see cref="ReserveSize"/> non-retired in-band candidates by <see cref="Order"/>, capped at 30% of the candidates.</summary>
    public static IReadOnlySet<long> Choose(bool modeActive, bool openBgEnabled, int bandMin, int bandMax, Func<int, int> seatsFor,
        int maxBots, IEnumerable<(long BotId, int Realm, int Level, bool Retired)> bots)
    {
        var result = new HashSet<long>();
        if (!modeActive || !openBgEnabled || bots == null)
            return result;
        foreach (var realm in bots.Where(b => b.Realm is >= 1 and <= 3 && !b.Retired && b.Level >= bandMin && b.Level <= bandMax)
                     .GroupBy(b => b.Realm))
        {
            int size = ReserveSize(seatsFor(realm.Key), maxBots);
            long[] candidates = realm.Select(b => b.BotId).OrderBy(Order).ThenBy(id => id).ToArray();
            int take = Math.Min(size, (int)Math.Ceiling(candidates.Length * 0.3));
            foreach (long id in candidates.Take(Math.Max(0, take)))
                result.Add(id);
        }
        return result;
    }

    /// <summary>Per-realm counts for the startup log line.</summary>
    public static string PerRealm(IReadOnlySet<long> set, IEnumerable<(long BotId, int Realm, int Level, bool Retired)> bots) =>
        string.Join("/", Enumerable.Range(1, 3).Select(r => bots.Count(b => b.Realm == r && set.Contains(b.BotId))));
}
