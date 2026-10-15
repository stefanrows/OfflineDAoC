using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DOL.Database;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// What one startup does with the home table. <c>ToSnapshot</c>: bot records that get a new home row (mode on).
/// <c>ToRestore</c>: each stored home paired with its bot record (mode off). <c>Orphans</c>: home rows to delete
/// (mode off): rows whose bot record is gone, and extra rows for a bot. <c>Kept</c>: stored home rows left untouched
/// (mode on).
/// </summary>
public sealed record GothwaitePvpHomePlan(
    IReadOnlyList<OfflineWorldBotRecord> ToSnapshot,
    IReadOnlyList<(GothwaitePvpHomeRecord Home, OfflineWorldBotRecord Record)> ToRestore,
    IReadOnlyList<GothwaitePvpHomeRecord> Orphans,
    int Kept);

/// <summary>
/// Stores and restores the home (position, bind, camp, itinerary) of every autonomous world bot around the Gothwaite
/// PvP mode (docs/GOTHWAITE_PVP_MODE.md 6.7). Only those fields move. Level, experience, realm points, money and
/// inventory are never read or written here (G7).
/// </summary>
public static class GothwaitePvpHomeStore
{
    private static readonly Logger Log = LoggerManager.Create(typeof(GothwaitePvpHomeStore));

    /// <summary>
    /// Pure planner for one startup. Mode on: a home for every non-retired bot that has none; an existing home is
    /// never overwritten. Mode off: each stored home goes back to its bot, and homes without a bot are orphans. With
    /// the mode off and no stored homes the plan is empty.
    /// </summary>
    public static GothwaitePvpHomePlan Plan(bool active, IReadOnlyList<OfflineWorldBotRecord> bots,
        IReadOnlyList<GothwaitePvpHomeRecord> homes)
    {
        var recordsById = new Dictionary<long, OfflineWorldBotRecord>();
        foreach (OfflineWorldBotRecord bot in bots ?? Array.Empty<OfflineWorldBotRecord>())
            recordsById[bot.BotId] = bot;

        // One home per bot: the lowest HomeId holds it; any later row for the same bot is an extra.
        var homeByBot = new Dictionary<long, GothwaitePvpHomeRecord>();
        var extraHomes = new List<GothwaitePvpHomeRecord>();
        foreach (GothwaitePvpHomeRecord home in (homes ?? Array.Empty<GothwaitePvpHomeRecord>()).OrderBy(home => home.HomeId))
        {
            if (homeByBot.ContainsKey(home.BotId))
                extraHomes.Add(home);
            else
                homeByBot[home.BotId] = home;
        }

        if (active)
        {
            // Every stored row is left alone while the mode is on (none is overwritten or deleted), so all of them count as kept.
            var snapshot = new List<OfflineWorldBotRecord>();
            foreach (OfflineWorldBotRecord bot in recordsById.Values.OrderBy(bot => bot.BotId))
            {
                if (!bot.IsRetired && !homeByBot.ContainsKey(bot.BotId))
                    snapshot.Add(bot);
            }
            return new GothwaitePvpHomePlan(snapshot,
                Array.Empty<(GothwaitePvpHomeRecord Home, OfflineWorldBotRecord Record)>(),
                Array.Empty<GothwaitePvpHomeRecord>(), homes?.Count ?? 0);
        }

        var restore = new List<(GothwaitePvpHomeRecord Home, OfflineWorldBotRecord Record)>();
        var orphans = new List<GothwaitePvpHomeRecord>();
        foreach (GothwaitePvpHomeRecord home in homeByBot.Values.OrderBy(home => home.BotId))
        {
            if (recordsById.TryGetValue(home.BotId, out OfflineWorldBotRecord record))
                restore.Add((home, record));
            else
                orphans.Add(home);
        }
        orphans.AddRange(extraHomes);
        orphans.Sort((first, second) => first.HomeId.CompareTo(second.HomeId));
        return new GothwaitePvpHomePlan(Array.Empty<OfflineWorldBotRecord>(), restore, orphans, 0);
    }

    /// <summary>The home row of one bot, copied from its saved record and stamped with <paramref name="utc"/>.</summary>
    public static GothwaitePvpHomeRecord Snapshot(OfflineWorldBotRecord record, string utc) => new()
    {
        BotId = record.BotId,
        RegionId = record.RegionId,
        ZoneId = record.ZoneId,
        ZoneName = record.ZoneName,
        X = record.X,
        Y = record.Y,
        Z = record.Z,
        BindRegionId = record.BindRegionId,
        BindX = record.BindX,
        BindY = record.BindY,
        BindZ = record.BindZ,
        CurrentCampId = record.CurrentCampId ?? string.Empty,
        ItineraryJson = record.ItineraryJson ?? string.Empty,
        SnapshotUtc = utc ?? string.Empty,
    };

    /// <summary>Copies the stored position, bind, camp and itinerary back into the bot record. Nothing else changes (G7).</summary>
    public static void Restore(GothwaitePvpHomeRecord home, OfflineWorldBotRecord record)
    {
        record.RegionId = home.RegionId;
        record.ZoneId = home.ZoneId;
        record.ZoneName = home.ZoneName;
        record.X = home.X;
        record.Y = home.Y;
        record.Z = home.Z;
        record.BindRegionId = home.BindRegionId;
        record.BindX = home.BindX;
        record.BindY = home.BindY;
        record.BindZ = home.BindZ;
        record.CurrentCampId = home.CurrentCampId;
        record.ItineraryJson = home.ItineraryJson;
    }

    /// <summary>
    /// Runs once at server start, after the previously online bots are reset and before the population starts. Mode on
    /// stores the missing homes; mode off restores and clears them, and with no stored homes does nothing at all.
    /// Errors are logged, never thrown into server start.
    /// </summary>
    public static void ReconcileAtStartup()
    {
        try
        {
            if (GameServer.Database == null)
                return;
            bool active = GothwaitePvpMode.IsActive;
            GothwaitePvpHomeRecord[] homes = DOLDB<GothwaitePvpHomeRecord>.SelectAllObjects().ToArray();
            if (!active && homes.Length == 0)
                return;

            OfflineWorldBotRecord[] bots = DOLDB<OfflineWorldBotRecord>.SelectAllObjects().ToArray();
            GothwaitePvpHomePlan plan = Plan(active, bots, homes);
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            {
                if (active)
                    StoreHomes(plan);
                else
                    RestoreHomes(plan);
            }
            ChooseOpenBgReserveSafely(active, bots);
        }
        catch (Exception exception)
        {
            Log.Error("GOTH_PVP_HOME_RECONCILE_FAILED: the home table was not changed at start.", exception);
        }
    }

    /// <summary>Never blocks the home snapshot/restore: a failure installs the empty set and is logged.</summary>
    private static void ChooseOpenBgReserveSafely(bool active, OfflineWorldBotRecord[] bots)
    {
        try
        {
            ChooseOpenBgReserve(active, bots);
        }
        catch (Exception exception)
        {
            GothwaitePvpOpenBgReserve.Install(null);
            Log.Error($"GOTH_PVP_OPENBG_RESERVE_FAILED error=\"{exception.Message}\"; no bot is reserved.");
        }
    }

    private static void ChooseOpenBgReserve(bool active, OfflineWorldBotRecord[] bots)
    {
        OpenBgSetup setup = OpenBgSetup.FromProperties();
        int Seats(int realm) => 8 * setup.Groups8For((eRealm)realm) + 4 * setup.SmallFor((eRealm)realm) + setup.SolosFor((eRealm)realm);
        int min = OpenBattlegroundRoster.MinLevel, max = OpenBattlegroundRoster.MaxLevel;
        var rows = bots.Select(b => (b.BotId, b.Realm, b.Level, b.IsRetired)).ToArray();
        var set = GothwaitePvpOpenBgReserve.Choose(active, OpenBattlegroundPolicy.IsEnabled, min, max, Seats,
            OpenBattlegroundProperties.MaxBots, rows);
        GothwaitePvpOpenBgReserve.Install(set);
        if (active)
            Log.Info($"GOTH_PVP_OPENBG_RESERVE reserve={set.Count} per_realm={GothwaitePvpOpenBgReserve.PerRealm(set, rows)} band={min}-{max}");
    }

    /// <summary>Login placement of a mode bot at a castle landing. False, with nothing changed, outside the mode.</summary>
    public static bool TryPlaceAtCastle(GameBot bot)
    {
        if (!GothwaitePvpRules.TryCastleLanding(bot, out ushort regionId, out Point3D point))
            return false;
        GothwaitePvpArena arena = GothwaitePvpMode.Arena;
        bot.CurrentRegionID = regionId;
        bot.X = point.X;
        bot.Y = point.Y;
        bot.Z = point.Z;
        bot.Heading = bot.GetHeading(arena.CastleX, arena.CastleY);
        return true;
    }

    private static void StoreHomes(GothwaitePvpHomePlan plan)
    {
        string utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        int stored = 0;
        foreach (OfflineWorldBotRecord bot in plan.ToSnapshot)
        {
            try
            {
                if (GameServer.Database.AddObject(Snapshot(bot, utc)))
                    stored++;
                else
                    Log.Error($"GOTH_PVP_SNAPSHOT_FAILED bot={bot.BotId}: the home row was not written; it is retried at the next start.");
            }
            catch (Exception exception)
            {
                Log.Error($"GOTH_PVP_SNAPSHOT_FAILED bot={bot.BotId}: the home row was not written; it is retried at the next start.", exception);
            }
        }
        Log.Info($"GOTH_PVP_SNAPSHOT stored={stored} kept={plan.Kept}");
    }

    private static void RestoreHomes(GothwaitePvpHomePlan plan)
    {
        foreach ((GothwaitePvpHomeRecord home, OfflineWorldBotRecord record) in plan.ToRestore)
        {
            try
            {
                Restore(home, record);
                record.Dirty = true;
                if (!GameServer.Database.SaveObject(record))
                {
                    Log.Error($"GOTH_PVP_RESTORE_FAILED bot={record.BotId}: the bot record was not saved; its home row is kept.");
                    continue;
                }
                // The home row goes only after the restored record is saved, so a failed save is retried at the next start.
                if (!GameServer.Database.DeleteObject(home))
                    Log.Error($"GOTH_PVP_RESTORE_INCOMPLETE bot={record.BotId}: the record is restored but its home row was not deleted; " +
                        "the next start restores that row again.");
                Log.Info($"GOTH_PVP_RESTORE bot={record.BotId} name=\"{record.Name}\" region={record.RegionId} " +
                    $"x={record.X} y={record.Y} z={record.Z}");
            }
            catch (Exception exception)
            {
                Log.Error($"GOTH_PVP_RESTORE_FAILED bot={record.BotId}: its home row is kept for the next start.", exception);
            }
        }

        foreach (GothwaitePvpHomeRecord orphan in plan.Orphans)
        {
            try
            {
                if (GameServer.Database.DeleteObject(orphan))
                    Log.Info($"GOTH_PVP_HOME_ORPHAN_DELETED home={orphan.HomeId} bot={orphan.BotId}");
            }
            catch (Exception exception)
            {
                Log.Error($"GOTH_PVP_HOME_ORPHAN_FAILED home={orphan.HomeId} bot={orphan.BotId}", exception);
            }
        }
    }
}
