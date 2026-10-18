using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// The live open BG reserve of the Gothwaite PvP mode (owner request 2026-10-18): the reserved set follows the open BG
/// setup while the server runs instead of being fixed at start. Every <see cref="IntervalMilliseconds"/> of game time each
/// realm's reserve is brought to its target: free online mode bots of the band join it (their standby turn parks them at
/// the border hub, the roster recruits them there), surplus standby bots leave it (the mode driver walks them back to the
/// arena). A bot in a battleground, in combat or dead never moves. The decision is pure (<see cref="Rebalance"/>).
/// </summary>
public static class GothwaitePvpOpenBgLiveReserve
{
    private static readonly Logger Log = LoggerManager.Create(typeof(GothwaitePvpOpenBgLiveReserve));

    public const long IntervalMilliseconds = 30_000;
    /// <summary>Reserved bots per open BG seat: the crew plus room for the dead, the resting and the relieved.</summary>
    public const double SeatMargin = 1.6;
    /// <summary>The reserve never takes more than this share of a realm's band, so the Gothwaite arena keeps its fights.</summary>
    public const double BandShareCap = 0.6;
    /// <summary>Most bots one realm gains or loses per pass, so a big step does not move hundreds of bots in one tick.</summary>
    public const int MaxMovesPerPass = 40;

    /// <summary>One bot as the rebalance sees it.</summary>
    public readonly record struct Bot(long BotId, int Realm, int Level, bool Retired, int SpeedRank, bool Online, bool Free, bool Grouped);

    /// <summary>A realm's reserve target: <see cref="SeatMargin"/> per seat of its share of the cap, at most <see cref="BandShareCap"/> of its band.</summary>
    public static int Target(int seats, int maxBots, int bandBots)
    {
        int share = (int)Math.Ceiling(Math.Max(0, maxBots) / 3.0);
        int wanted = (int)Math.Ceiling(SeatMargin * Math.Min(Math.Max(0, seats), share));
        return Math.Max(0, Math.Min(wanted, (int)Math.Floor(Math.Max(0, bandBots) * BandShareCap)));
    }

    /// <summary>
    /// The reserve after one pass. Per realm 1-3: reserved bots out of the band or retired leave when free; then the reserve
    /// grows with free, online, ungrouped bots first (primary then secondary speed classes while the realm has fewer than
    /// <paramref name="speedWanted"/>, then by <see cref="GothwaitePvpOpenBgReserve.Order"/>) or shrinks by free bots in
    /// reverse order, keeping speed classes; at most <see cref="MaxMovesPerPass"/> moves each way.
    /// </summary>
    public static HashSet<long> Rebalance(IReadOnlySet<long> current, IReadOnlyList<Bot> bots, int bandMin, int bandMax,
        Func<int, int> targetFor, Func<int, int> speedWanted, out int added, out int removed)
    {
        added = removed = 0;
        var result = new HashSet<long>(current ?? new HashSet<long>());
        foreach (IGrouping<int, Bot> realm in (bots ?? Array.Empty<Bot>()).Where(bot => bot.Realm is >= 1 and <= 3).GroupBy(bot => bot.Realm))
        {
            bool InBand(Bot bot) => !bot.Retired && bot.Level >= bandMin && bot.Level <= bandMax;
            int moves = 0;
            foreach (Bot gone in realm.Where(bot => result.Contains(bot.BotId) && !InBand(bot) && bot.Free).ToArray())
            {
                if (moves >= MaxMovesPerPass) break;
                result.Remove(gone.BotId);
                removed++;
                moves++;
            }
            Bot[] mine = realm.Where(bot => result.Contains(bot.BotId)).ToArray();
            int target = Math.Max(0, targetFor?.Invoke(realm.Key) ?? 0);
            if (mine.Length < target)
            {
                int speed = mine.Count(bot => bot.SpeedRank > 0);
                int want = Math.Max(0, speedWanted?.Invoke(realm.Key) ?? 0);
                Bot[] pool = realm.Where(bot => !result.Contains(bot.BotId) && InBand(bot) && bot.Online && bot.Free)
                    .OrderBy(bot => bot.Grouped)
                    .ThenByDescending(bot => speed < want ? bot.SpeedRank : 0)
                    .ThenBy(bot => GothwaitePvpOpenBgReserve.Order(bot.BotId)).ThenBy(bot => bot.BotId).ToArray();
                foreach (Bot bot in pool.Take(Math.Min(target - mine.Length, MaxMovesPerPass - moves)))
                {
                    result.Add(bot.BotId);
                    added++;
                }
            }
            else if (mine.Length > target)
            {
                Bot[] spare = mine.Where(bot => bot.Free)
                    .OrderBy(bot => bot.SpeedRank)
                    .ThenByDescending(bot => GothwaitePvpOpenBgReserve.Order(bot.BotId)).ThenByDescending(bot => bot.BotId).ToArray();
                foreach (Bot bot in spare.Take(Math.Min(mine.Length - target, MaxMovesPerPass - moves)))
                {
                    result.Remove(bot.BotId);
                    removed++;
                }
            }
        }
        return result;
    }

    private static long _nextPass;

    /// <summary>Called by the open BG roster tick; runs a pass at most every <see cref="IntervalMilliseconds"/> while the mode is on.</summary>
    public static void Tick(long now)
    {
        if (!GothwaitePvpMode.IsActive || !OpenBattlegroundPolicy.IsEnabled || now < _nextPass) return;
        _nextPass = now + IntervalMilliseconds;
        try
        {
            Pass();
        }
        catch (Exception exception)
        {
            Log.Error($"GOTH_PVP_OPENBG_RESERVE_LIVE_FAILED error=\"{exception.Message}\"; the reserve stays as it was.");
        }
    }

    private static void Pass()
    {
        OpenBgSetup setup = OpenBgSetup.FromProperties();
        int min = OpenBattlegroundRoster.MinLevel, max = OpenBattlegroundRoster.MaxLevel;
        IReadOnlySet<long> current = GothwaitePvpOpenBgReserve.Set;
        var online = new Dictionary<long, GameBot>();
        foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
            if (bot.DatabaseID > 0 && bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper && !bot.IsBattlegroundEncounterBot &&
                !bot.IsPersistentPlayerCompanion)
                online[bot.DatabaseID] = bot;
        var bots = new List<Bot>();
        foreach (GameBot bot in online.Values)
            bots.Add(new Bot(bot.DatabaseID, (int)bot.Realm, bot.Level, false,
                bot.CharacterClass == null ? 0 : AutonomousRvrSpeed.SpeedRank((eCharacterClass)bot.CharacterClass.ID), true,
                bot.IsAlive && !bot.InCombat && !bot.IsPlayerLedGroup && !AutonomousBattlegroundParticipation.IsInAnyBattleground(bot),
                bot.Group != null));
        // A reserved bot that is offline stays reserved (it logs in at its hub); it is listed as not free.
        foreach (long id in current.Where(id => !online.ContainsKey(id)))
            if (GothwaitePvpOpenBgReserve.RealmOf(id) is int realm and > 0)
                bots.Add(new Bot(id, realm, min, false, 0, false, false, false));
        int Seats(int realm) => 8 * setup.Groups8For((eRealm)realm) + 4 * setup.SmallFor((eRealm)realm) + setup.SolosFor((eRealm)realm);
        int Band(int realm) => bots.Count(bot => bot.Realm == realm && bot.Online && bot.Level >= min && bot.Level <= max);
        int TargetFor(int realm) => Target(Seats(realm), OpenBattlegroundProperties.MaxBots, Band(realm));
        int SpeedWanted(int realm) => GothwaitePvpOpenBgReserve.SpeedWanted(setup.Groups8For((eRealm)realm), setup.SmallFor((eRealm)realm));
        HashSet<long> next = Rebalance(current, bots, min, max, TargetFor, SpeedWanted, out int added, out int removed);
        if (added == 0 && removed == 0) return;
        foreach (Bot bot in bots.Where(bot => next.Contains(bot.BotId)))
            GothwaitePvpOpenBgReserve.NoteRealm(bot.BotId, bot.Realm);
        GothwaitePvpOpenBgReserve.Install(next);
        string perRealm = string.Join("/", Enumerable.Range(1, 3).Select(realm =>
            $"{bots.Count(bot => bot.Realm == realm && next.Contains(bot.BotId))}:{TargetFor(realm)}"));
        Log.Info($"GOTH_PVP_OPENBG_RESERVE_LIVE reserve={next.Count} added={added} removed={removed} per_realm_have_target={perRealm} band={min}-{max}");
    }
}
