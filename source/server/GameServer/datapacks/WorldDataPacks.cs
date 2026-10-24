using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DOL.Database;
using DOL.Database.Connection;
using DOL.Logging;

namespace DOL.GS.DataPacks
{
    /// <summary>
    /// Server glue for <see cref="WorldDataPackApplier"/>: at startup (after the server properties and database
    /// updaters, before any world data is read) every pending pack in the server's data-packs folder is applied to the
    /// SQLite save once. A failed pack is rolled back and logged; the server keeps booting without it.
    /// See docs/TOA_DATA_PACKS.md.
    /// </summary>
    public static class WorldDataPacks
    {
        public const string FolderName = "data-packs";

        private static readonly Logger log = LoggerManager.Create(typeof(WorldDataPacks));

        /// <summary>Pre-cached tables (DataTable PreCache = true) whose rows a pack may change.</summary>
        public static readonly IReadOnlyList<WorldDataPackCacheWatch> CacheWatches = new[]
        {
            new WorldDataPackCacheWatch("ItemTemplate", "Id_nb"),
            new WorldDataPackCacheWatch("MobXAmbientBehaviour", "MobXAmbientBehaviour_ID"),
            new WorldDataPackCacheWatch("Minotaurrelic", "RelicID"),
        };

        public static string PackDirectory => Path.Combine(GameServer.Instance.Configuration.RootDirectory, FolderName);

        /// <summary>Startup component. Never throws: a broken pack must not stop the server.</summary>
        public static void ApplyPending()
        {
            try
            {
                if (!ServerProperties.Properties.WORLD_DATA_PACKS_ENABLED)
                {
                    log.Info("World data packs: disabled by server property world_data_packs_enabled.");
                    return;
                }

                if (!TryCreateApplier(out WorldDataPackApplier applier, out string reason))
                {
                    log.Info($"World data packs: {reason}");
                    return;
                }

                WorldDataPackRun run = applier.ApplyPending();
                Report(run);
            }
            catch (Exception e)
            {
                log.Error("World data packs: unexpected error; continuing without applying packs.", e);
            }
        }

        /// <summary>Creates an applier for the running server, or explains why packs cannot apply here.</summary>
        public static bool TryCreateApplier(out WorldDataPackApplier applier, out string reason)
        {
            applier = null;
            GameServerConfiguration configuration = GameServer.Instance.Configuration;
            if (configuration.DBType != EConnectionType.DATABASE_SQLITE)
            {
                reason = $"skipped, the database is {configuration.DBType}; data packs are SQLite only.";
                return false;
            }

            string directory = PackDirectory;
            if (!File.Exists(Path.Combine(directory, WorldDataPackApplier.ManifestFileName)))
            {
                reason = $"none shipped (no {FolderName}/{WorldDataPackApplier.ManifestFileName}).";
                return false;
            }

            applier = new WorldDataPackApplier(configuration.DBConnectionString, directory, CacheWatches)
            {
                Progress = line => log.Info(line),
            };
            reason = null;
            return true;
        }

        /// <summary>Logs a run and refreshes the pre-cache for rows the packs changed.</summary>
        public static void Report(WorldDataPackRun run)
        {
            foreach (string warning in run.Warnings)
                log.Warn("World data packs: " + warning);

            if (run.BackupPath != null)
                log.Info($"World data packs: database backup before applying: {run.BackupPath}");
            foreach (string pruned in run.PrunedBackups)
                log.Info($"World data packs: removed old backup {pruned} (the newest {WorldDataPackApplier.BackupsKept} are kept).");

            foreach (WorldDataPackResult result in run.Results)
            {
                string line = $"World data pack {result.Name}: {result.State}, {result.Message}" +
                              (result.AppliedNow ? $" in {result.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s" : string.Empty);
                switch (result.State)
                {
                    case WorldDataPackState.Applied when result.AppliedNow:
                        log.Info(line);
                        break;
                    case WorldDataPackState.Applied:
                        if (log.IsDebugEnabled)
                            log.Debug(line);
                        break;
                    case WorldDataPackState.AppliedDifferentHash:
                    case WorldDataPackState.Failed when result.SkippedAfterFailure:
                        log.Warn(line);
                        break;
                    default:
                        log.Error(line);
                        break;
                }
            }

            RefreshCaches(run);
            int pending = run.Results.Count(r => r.State == WorldDataPackState.Pending);
            log.Info($"World data packs: {run.AppliedCount} applied now, {run.FailedCount} refused or failed, " +
                     $"{run.Results.Count(r => r.State == WorldDataPackState.Applied && !r.AppliedNow)} already in this save" +
                     (pending > 0 ? $", {pending} pending." : "."));
        }

        private static void RefreshCaches(WorldDataPackRun run)
        {
            foreach ((string table, HashSet<string> keys) in run.ChangedKeys)
            {
                if (keys.Count == 0)
                    continue;

                try
                {
                    bool complete = table.ToLowerInvariant() switch
                    {
                        "itemtemplate" => GameServer.Database.UpdateObjsInCache<DbItemTemplate>(keys),
                        "mobxambientbehaviour" => GameServer.Database.UpdateObjsInCache<DbMobXAmbientBehavior>(keys),
                        "minotaurrelic" => GameServer.Database.UpdateObjsInCache<DbMinotaurRelic>(
                            keys.Select(k => (object)int.Parse(k, CultureInfo.InvariantCulture))),
                        _ => true,
                    };
                    log.Info($"World data packs: refreshed {keys.Count} cached {table} rows" +
                             (complete ? "." : " (rows a pack deleted stay cached until restart)."));
                }
                catch (Exception e)
                {
                    log.Warn($"World data packs: could not refresh cached {table} rows; restart the server to reload them.", e);
                }
            }
        }
    }
}
