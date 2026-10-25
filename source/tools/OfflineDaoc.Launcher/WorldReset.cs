using System.Data.SQLite;
using System.Globalization;
using OfflineDaoc.Configuration;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Creates a fresh world save: "fresh RvR world" or "fresh Camlann world" (docs/RVR_WORLDS.md, spec section 7).
/// Both run one code path with a world parameter:
/// <list type="number">
/// <item>Copy the seed save (the world's own save, else the other world's save) to a build file beside the target.</item>
/// <item>On the copy, in one transaction: the shared scrub (<see cref="CamlannWorldReset.ScrubPlayerState"/>), the
/// world's keep start, the fork's bot bookkeeping cleared, the world marker, today's rates and open BG setup, and the
/// bot roster (600 per realm by default) with the population target.</item>
/// <item>Integrity check of the copy.</item>
/// <item>Move the replaced save and its -wal/-shm/-journal and per-save side files to
/// <c>&lt;install&gt;-backups/&lt;timestamp&gt;-&lt;world&gt;-world-reset/</c>; nothing is deleted.</item>
/// <item>Move the build file into place. Any failure before this point leaves every old file where it was.</item>
/// </list>
/// The server must be fully stopped; it is checked before the copy and again before any file moves.
/// </summary>
public static class WorldReset
{
    public const int DefaultBotsPerRealm = 600;
    public const int MaximumBotsPerRealm = 5000;
    public const string ResetUtcKey = "WorldResetUtc";
    public const string SimulationClockKey = "WorldSimulationClock";
    public const string MakeMeGmKey = "MakeMeGM";

    private const int LevelBatch = 100;

    /// <summary>A build file left this long by an earlier run is an orphan and is swept.</summary>
    public static readonly TimeSpan OrphanAge = TimeSpan.FromMinutes(10);

    /// <summary>The fork's per-save bot bookkeeping a fresh world clears beyond the shared scrub.</summary>
    public static IReadOnlyList<string> FreshWorldClearedTables { get; } =
    [
        "offline_managed_guild_charters", "offline_crew_consolidation", "offline_gothwaite_pvp_home",
        "offline_guild_grudges", "offline_runtime_status", "KeepHookPointItem",
    ];

    /// <summary>The privilege level the kept local account gets: a normal player (owner decision 16, no GM flag).</summary>
    public const int PlayerPrivLevel = 1;

    public sealed record Request(WorldSaveKind World, string RuntimeRoot, string LocalAccount, Func<bool> ServerStopped)
    {
        /// <summary>The save to build from; default the world's own save, else the other world's save.</summary>
        public string? SeedDatabase { get; init; }

        /// <summary>Where the replaced files go; default <see cref="WorldFiles.DefaultBackupRoot"/>.</summary>
        public string? BackupRoot { get; init; }

        public int BotsPerRealm { get; init; } = DefaultBotsPerRealm;

        /// <summary>The level shape of the roster; default the shape in bot-goals.json (FreshLaunch: all level 1).</summary>
        public PopulationWorldShape? WorldShape { get; init; }

        /// <summary>Test seam for the file moves of the backup and swap steps; default <see cref="File.Move(string, string)"/>.</summary>
        public Action<string, string>? MoveFile { get; init; }
    }

    public sealed record Result(
        WorldSaveKind World,
        string Database,
        string Seed,
        string? Backup,
        IReadOnlyList<string> MovedFiles,
        int Characters,
        int Bots,
        int Guilds,
        int Keeps,
        int RealmKeeps,
        int Relics,
        IReadOnlyDictionary<int, int> BotsCreated,
        DateTime CreatedUtc);

    /// <summary>
    /// The seed a reset of this world would use: the world's own save, else the other world's save, each only when it
    /// holds world data (<see cref="IsSeedable"/>). Null when the installation has no such save.
    /// </summary>
    public static string? DefaultSeed(string runtimeRoot, WorldSaveKind world)
    {
        string own = WorldFiles.For(runtimeRoot, world).Database;
        if (IsSeedable(own))
            return own;
        string other = WorldFiles.For(runtimeRoot, WorldSaveLayout.Other(world)).Database;
        return IsSeedable(other) ? other : null;
    }

    /// <summary>
    /// True for a save with world data: it exists and has the ServerProperty table. An empty file, such as one a
    /// server started by hand against a missing save leaves behind, is never a seed.
    /// </summary>
    public static bool IsSeedable(string database)
    {
        if (!File.Exists(database))
            return false;
        try
        {
            using SQLiteConnection connection = CamlannWorldReset.Open(database, true);
            return CamlannWorldReset.TableExists(connection, "ServerProperty");
        }
        catch (Exception exception) when (exception is SQLiteException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static Result CreateFresh(Request request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.LocalAccount))
            throw new ArgumentException("The local account name is required.", nameof(request));
        if (request.BotsPerRealm is < 0 or > MaximumBotsPerRealm)
            throw new ArgumentOutOfRangeException(nameof(request), $"Bots per realm must be 0-{MaximumBotsPerRealm}.");
        ArgumentNullException.ThrowIfNull(request.ServerStopped);

        WorldFiles target = WorldFiles.For(request.RuntimeRoot, request.World);
        string seed = request.SeedDatabase ?? DefaultSeed(request.RuntimeRoot, request.World)
            ?? throw new FileNotFoundException("This installation has no save to build the new world from.", target.Database);
        if (!File.Exists(seed))
            throw new FileNotFoundException("The seed save is missing.", seed);
        string? seedMarker = CamlannWorldReset.ReadWorldModel(seed);
        if (seedMarker is not null && WorldSaveLayout.FromMarker(seedMarker) is null &&
            !string.Equals(seedMarker, "Normal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The seed save has an unsupported world marker: {seedMarker}.");

        PopulationWorldShape shape = request.WorldShape ??
            BotGoalSettings.Load(Path.Combine(target.ServerDirectory, BotGoalSettings.FileName)).WorldShape;

        CamlannWorldReset.RequireStopped(request.ServerStopped);
        string dataDirectory = Path.GetDirectoryName(target.Database)!;
        Directory.CreateDirectory(dataDirectory);
        SweepOrphanedBuildFiles(dataDirectory, target.Database);
        string building = Path.Combine(dataDirectory, $".{Path.GetFileName(target.Database)}.building-{Guid.NewGuid():N}");
        DateTime now = DateTime.UtcNow;

        CamlannWorldReset.ScrubCounts counts;
        Dictionary<int, int> created;
        try
        {
            CopySave(seed, building);
            (counts, created) = BuildFreshWorld(building, request, shape, now);
            RequireIntact(building);
        }
        catch
        {
            DeleteBuildFiles(building);
            throw;
        }

        CamlannWorldReset.RequireStopped(request.ServerStopped);
        string backupRoot = request.BackupRoot ?? WorldFiles.DefaultBackupRoot(request.RuntimeRoot);
        Action<string, string> move = request.MoveFile ?? ((from, to) => File.Move(from, to));
        var moved = new List<(string From, string To)>();
        string? backup = null;
        try
        {
            List<string> replaced = ReplacedFiles(target).Where(File.Exists).ToList();
            if (replaced.Count > 0)
            {
                backup = NewBackupDirectory(backupRoot, request.World, now);
                foreach (string file in replaced)
                {
                    string destination = Path.Combine(backup, Path.GetFileName(file));
                    move(file, destination);
                    moved.Add((file, destination));
                }
            }

            move(building, target.Database);
        }
        catch (Exception failure)
        {
            // Put every moved file back; the build file stays out of the way and is removed.
            var stranded = new List<string>();
            for (int index = moved.Count - 1; index >= 0; index--)
            {
                try { move(moved[index].To, moved[index].From); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    stranded.Add($"{moved[index].To} (belongs at {moved[index].From})");
                }
            }
            DeleteBuildFiles(building);
            if (backup is null)
                throw;
            string message = stranded.Count == 0
                ? $"The new world was not created: {failure.Message} Every moved file was put back; the backup folder {backup} holds nothing of it."
                : $"The new world was not created: {failure.Message} These files could not be put back and are in the backup folder {backup}; " +
                  "move them back by hand before starting the server: " + string.Join("; ", stranded);
            throw new IOException(message, failure);
        }

        if (request.World == WorldSaveKind.Camlann && backup is not null)
            RestoreSettingFiles(target, backup);

        return new(request.World, target.Database, seed, backup, moved.Select(entry => entry.From).ToArray(),
            counts.Characters, counts.Bots, counts.Guilds, counts.Keeps, counts.RealmKeeps, counts.Relics, created, now);
    }

    /// <summary>
    /// The server properties a fresh world writes (today's rates, owner decision 16, and today's open BG setup).
    /// An existing row keeps its category and description; only the value changes. The open BG friend rule is
    /// perrealm in both worlds, as today; the RvR world additionally forces it in code (spec section 3.4).
    /// </summary>
    public static IReadOnlyList<(string Category, ServerPropertyRow Row)> FreshWorldProperties(WorldSaveKind world)
    {
        var rows = new List<(string Category, ServerPropertyRow Row)>
        {
            ("rates", Rate("xp_rate", "Real player character experience rate selected in the Offline DAoC launcher.", "10")),
            ("rates", Rate("bot_xp_rate", "Autonomous player-bot experience rate selected in the Offline DAoC launcher.", "10")),
            ("rates", Rate("rp_rate", "Real player character realm point rate selected in the Offline DAoC launcher.", "50")),
            ("rates", Rate("bot_rp_rate", "Autonomous player-bot realm point rate selected in the Offline DAoC launcher.", "15")),
            (ToaSettings.Field("ml_xp").Category, ToaSettings.NumberRow("ml_xp", 10)),
            (ToaSettings.Field("artifact_xp").Category, ToaSettings.NumberRow("artifact_xp", 350)),
            (ToaSettings.Field("loot").Category, ToaSettings.SwitchRow("loot", true)),
        };
        // Today's open BG (owner decision 2026-10-20: perrealm, 450 bots). The server's own ranges allow these
        // values; the launcher tab's narrower ranges are display limits and are not applied here.
        foreach ((string key, string value) in new[]
                 {
                     ("open_bg_enabled", "True"), ("open_bg_groups8", "12"), ("open_bg_groups_small", "8"),
                     ("open_bg_solos", "22"), ("open_bg_max_bots", "450"), ("open_bg_cycle_min_minutes", "15"),
                     ("open_bg_cycle_max_minutes", "45"), ("open_bg_player_drift", "True"), ("open_bg_band_min", "50"),
                     ("open_bg_band_max", "50"), ("open_bg_units_albion", ""), ("open_bg_units_midgard", ""),
                     ("open_bg_units_hibernia", ""), ("open_bg_mode", "perrealm"), ("open_bg_landing_peace", ""),
                     ("open_bg_presets", "std8:1,caster8:1,raid8:8"),
                 })
            rows.Add((OpenBgSettings.Category, OpenBgRow(key, value)));
        return rows;
    }

    private static ServerPropertyRow Rate(string key, string description, string value) => new(key, description, "1", value);

    private static ServerPropertyRow OpenBgRow(string key, string value)
    {
        OpenBgField? number = OpenBgSettings.Fields.FirstOrDefault(field => field.RowKey == key);
        if (number is not null)
        {
            string fallback = number.IsSwitch ? (number.Default == 1 ? "True" : "False") : number.Default.ToString(CultureInfo.InvariantCulture);
            return new(key, number.Description, fallback, value);
        }
        OpenBgTextField? text = OpenBgSettings.TextFields.FirstOrDefault(field => field.RowKey == key);
        if (text is not null)
            return new(key, text.Description, text.DefaultText, value);
        OpenBgUnitsField? units = OpenBgSettings.UnitFields.FirstOrDefault(field => field.RowKey == key);
        return new(key, units?.Description ?? string.Empty, string.Empty, value);
    }

    private static void CopySave(string seed, string destination)
    {
        // The backup API reads a consistent image of the seed, including pages still in its WAL.
        using SQLiteConnection source = CamlannWorldReset.Open(seed, true);
        using var target = new SQLiteConnection(new SQLiteConnectionStringBuilder
        {
            DataSource = destination,
            Pooling = false,
            FailIfMissing = false,
            DefaultTimeout = 10,
        }.ConnectionString);
        target.Open();
        source.BackupDatabase(target, "main", "main", -1, null, 0);
    }

    private static (CamlannWorldReset.ScrubCounts Counts, Dictionary<int, int> Created) BuildFreshWorld(
        string database, Request request, PopulationWorldShape shape, DateTime now)
    {
        using SQLiteConnection connection = CamlannWorldReset.Open(database, false);
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA foreign_keys=OFF;";
            pragma.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
        CamlannWorldReset.ScrubCounts counts =
            CamlannWorldReset.ScrubPlayerState(connection, transaction, request.LocalAccount, request.World);
        foreach (string table in FreshWorldClearedTables)
            CamlannWorldReset.DeleteAll(connection, transaction, table);
        ResetCampaignCamps(connection, transaction);
        ResetLocalAccountPrivilege(connection, transaction, request.LocalAccount);

        CamlannWorldReset.UpsertOption(connection, transaction, CamlannWorldReset.WorldModelKey, WorldSaveLayout.Marker(request.World));
        CamlannWorldReset.UpsertOption(connection, transaction, ResetUtcKey, now.ToString("O"));
        CamlannWorldReset.UpsertOption(connection, transaction, MakeMeGmKey, "false");
        DeleteOption(connection, transaction, SimulationClockKey);
        DeleteOption(connection, transaction, CamlannWorldReset.ResetUtcKey);

        if (!CamlannWorldReset.TableExists(connection, "ServerProperty"))
            throw new InvalidOperationException("The seed save has no ServerProperty table; the new world was not created.");
        foreach (IGrouping<string, (string Category, ServerPropertyRow Row)> group in
                 FreshWorldProperties(request.World).GroupBy(entry => entry.Category))
            ServerPropertyRows.Upsert(connection, transaction, group.Key, group.Select(entry => entry.Row).ToArray());

        var created = new Dictionary<int, int>();
        if (request.BotsPerRealm > 0)
        {
            HashSet<string> reserved = BotRosterWriter.ReservedNames(connection, transaction);
            foreach (int realm in new[] { 1, 2, 3 })
            {
                for (int remaining = request.BotsPerRealm; remaining > 0; remaining -= LevelBatch)
                {
                    int count = Math.Min(LevelBatch, remaining);
                    int[] levels = FreshLevels(request, shape, count);
                    BotRosterWriter.Add(connection, transaction, realm, levels, reserved);
                }
                created[realm] = request.BotsPerRealm;
            }
        }
        BotRosterWriter.UpdatePopulationTarget(connection, transaction);

        CamlannWorldReset.RequireStopped(request.ServerStopped);
        transaction.Commit();
        return (counts, created);
    }

    /// <summary>The level every bot of a fresh RvR world starts at (upstream's "+ Lv.50" roster).</summary>
    public const int RvrRosterLevel = 50;

    /// <summary>
    /// The levels of a fresh world's roster. RvR world (port session S1, docs/RVR_PORT_MAP.md): upstream ships an empty
    /// world and its players create gamebots with the realm cards' "+ Lv.1" and "+ Lv.50" buttons; only level 50
    /// bots take RvR objectives there and every siege needs level 50 (average and minimum). A fresh RvR world is
    /// therefore created as upstream's level-50 roster, with the class loadout. An explicit
    /// <see cref="Request.WorldShape"/> still wins. Camlann: the bot-goals world shape, unchanged.
    /// </summary>
    public static int[] FreshLevels(Request request, PopulationWorldShape shape, int count) =>
        request.World == WorldSaveKind.Realm && request.WorldShape is null
            ? Enumerable.Repeat(RvrRosterLevel, count).ToArray()
            : AutonomousPopulationShape.NewLevels(shape, count);

    /// <summary>The battleground campaign camps keep their rows; sponsors, tokens and timers belong to the old world.</summary>
    private static void ResetCampaignCamps(SQLiteConnection connection, SQLiteTransaction transaction)
    {
        if (!CamlannWorldReset.TableExists(connection, "BattlegroundCampaignCamp"))
            return;
        var assignments = new List<string>();
        foreach ((string column, string value) in new[]
                 {
                     ("SponsorGuildId", "''"), ("Tokens", "0"), ("ExpiresAt", "'2000-01-01 00:00:00'"),
                     ("CaptainRespawnAt", "'2000-01-01 00:00:00'"),
                 })
            if (CamlannWorldReset.ColumnExists(connection, transaction, "BattlegroundCampaignCamp", column))
                assignments.Add($"[{column}]={value}");
        if (assignments.Count == 0)
            return;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE BattlegroundCampaignCamp SET " + string.Join(",", assignments);
        command.ExecuteNonQuery();
    }

    /// <summary>The kept local account plays as a normal player in the fresh world, as MakeMeGM=false says.</summary>
    private static void ResetLocalAccountPrivilege(SQLiteConnection connection, SQLiteTransaction transaction, string account)
    {
        if (!CamlannWorldReset.ColumnExists(connection, transaction, "Account", "PrivLevel"))
            return;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE Account SET PrivLevel=@level WHERE Name=@account";
        command.Parameters.AddWithValue("@level", PlayerPrivLevel);
        command.Parameters.AddWithValue("@account", account);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Removes build files an earlier run left behind (a crash or a killed launcher). They are incomplete copies of a
    /// seed, never a save the server opens. Runs only after the stopped check, before this run's build file exists;
    /// a file written in the last ten minutes is left alone in case another launcher is building right now.
    /// </summary>
    private static void SweepOrphanedBuildFiles(string dataDirectory, string database)
    {
        DateTime cutoff = DateTime.UtcNow - OrphanAge;
        foreach (string file in Directory.EnumerateFiles(dataDirectory, $".{Path.GetFileName(database)}.building-*"))
        {
            if (File.GetLastWriteTimeUtc(file) > cutoff)
                continue;
            try { File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void RequireIntact(string database)
    {
        using SQLiteConnection connection = CamlannWorldReset.Open(database, true);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check";
        string? result = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (!string.Equals(result, "ok", StringComparison.Ordinal))
            throw new InvalidOperationException($"The new world failed its integrity check ({result}); nothing was replaced.");
    }

    private static void DeleteOption(SQLiteConnection connection, SQLiteTransaction transaction, string key)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM offline_local_options WHERE Key=@key";
        command.Parameters.AddWithValue("@key", key);
        command.ExecuteNonQuery();
    }

    /// <summary>The save, its SQLite sidecars, and every per-save side file (with the event store's sidecars).</summary>
    public static IEnumerable<string> ReplacedFiles(WorldFiles files)
    {
        yield return files.Database;
        foreach (string suffix in WorldSaveLayout.SqliteSidecarSuffixes)
            yield return files.Database + suffix;
        foreach (string side in files.StateFiles.Concat(files.SettingFiles))
        {
            yield return side;
            if (side.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase))
                foreach (string suffix in WorldSaveLayout.SqliteSidecarSuffixes)
                    yield return side + suffix;
        }
    }

    private static string NewBackupDirectory(string backupRoot, WorldSaveKind world, DateTime now)
    {
        string name = $"{now:yyyyMMdd-HHmmss}-{(world == WorldSaveKind.Realm ? "rvr" : "camlann")}-world-reset";
        string directory = Path.Combine(backupRoot, name);
        for (int suffix = 2; Directory.Exists(directory); suffix++)
            directory = Path.Combine(backupRoot, $"{name}-{suffix}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void RestoreSettingFiles(WorldFiles target, string backup)
    {
        foreach (string setting in target.SettingFiles)
        {
            string saved = Path.Combine(backup, Path.GetFileName(setting));
            if (File.Exists(saved) && !File.Exists(setting))
                File.Copy(saved, setting);
        }
    }

    private static void DeleteBuildFiles(string building)
    {
        SQLiteConnection.ClearAllPools();
        foreach (string file in new[] { building }.Concat(WorldSaveLayout.SqliteSidecarSuffixes.Select(suffix => building + suffix)))
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
