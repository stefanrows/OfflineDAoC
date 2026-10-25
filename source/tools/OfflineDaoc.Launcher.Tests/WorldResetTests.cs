using System.Data.SQLite;
using System.Security.Cryptography;
using NUnit.Framework;
using OfflineDaoc.Configuration;
using OfflineDaoc.Launcher;

/// <summary>The fresh RvR and fresh Camlann world routines (docs/RVR_WORLDS.md) on a temporary installation.</summary>
[TestFixture]
public class WorldResetTests
{
    private string _install = null!;
    private string _runtime = null!;
    private string _backups = null!;
    private WorldFiles _rvr = null!;
    private WorldFiles _camlann = null!;

    [SetUp]
    public void Setup()
    {
        _install = Path.Combine(Path.GetTempPath(), "world-reset-test-" + Guid.NewGuid().ToString("N"), "playable");
        _runtime = Path.Combine(_install, "runtime");
        _backups = _install + "-backups";
        Directory.CreateDirectory(Path.Combine(_runtime, "data"));
        Directory.CreateDirectory(Path.Combine(_runtime, "server"));
        _rvr = WorldFiles.For(_runtime, WorldSaveKind.Realm);
        _camlann = WorldFiles.For(_runtime, WorldSaveKind.Camlann);
        CreateSeed(_rvr.Database, "Camlann-1");
    }

    [TearDown]
    public void Cleanup()
    {
        SQLiteConnection.ClearAllPools();
        string root = Path.GetDirectoryName(_install)!;
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Test]
    public void FreshRvrWorldReplacesTheDefaultSaveAndMovesItsFilesToTheBackup()
    {
        string oldHash = Hash(_rvr.Database);
        File.WriteAllText(_rvr.OpenBgStatus, "{}");
        File.WriteAllText(_rvr.BotWorld, "{}");
        File.WriteAllText(_rvr.RvrWorld, "{}");
        File.WriteAllText(_rvr.GothwaiteSwitch, "{\"version\":1,\"enabled\":true}");
        CreateEventRecords(_rvr.EventRecords);

        WorldReset.Result result = Create(WorldSaveKind.Realm, botsPerRealm: 7);

        Assert.That(result.Database, Is.EqualTo(_rvr.Database));
        Assert.That(result.Seed, Is.EqualTo(_rvr.Database));
        Assert.That(result.Backup, Does.StartWith(_backups + Path.DirectorySeparatorChar));
        Assert.That(Path.GetFileName(result.Backup), Does.Match(@"^\d{8}-\d{6}-rvr-world-reset$"));
        Assert.That(Hash(Path.Combine(result.Backup!, "opendaoc.sqlite3.db")), Is.EqualTo(oldHash), "the old save is moved, unchanged");
        foreach (string name in new[] { "realm-event-records.sqlite3", "open-bg-status.json", "bot-world.json", "rvr-world.json", "gothwaite-pvp.json" })
        {
            Assert.That(File.Exists(Path.Combine(result.Backup!, name)), Is.True, name + " is in the backup");
            Assert.That(File.Exists(Path.Combine(_rvr.ServerDirectory, name)), Is.False, name + " is gone from the server folder");
        }
        Assert.That(result.MovedFiles, Has.Count.EqualTo(6));
        Assert.That(Scalar(_rvr.Database, "SELECT Value FROM offline_local_options WHERE Key='WorldModel'"), Is.EqualTo("Realm-1"));
        Assert.That(CamlannWorldReset.ReadWorldModel(_rvr.Database), Is.EqualTo(WorldSaveLayout.RealmMarker));
        Assert.That(Directory.GetFiles(Path.Combine(_runtime, "data"), "*.building-*"), Is.Empty);
    }

    [Test]
    public void FreshRvrWorldStartsPortalAndRelicKeepsWithTheirRealmAndEveryOtherKeepNeutral()
    {
        WorldReset.Result result = Create(WorldSaveKind.Realm, botsPerRealm: 0);

        Assert.That(result.RealmKeeps, Is.EqualTo(3));
        Assert.That(Rows(_rvr.Database, "SELECT KeepID || ':' || Realm || ':' || ClaimedGuildName FROM [Keep] ORDER BY KeepID"),
            Is.EqualTo(new[] { "1:1:", "2:0:", "3:2:", "4:0:", "5:3:" }),
            "portal keep 1 Albion, ordinary keep 2 neutral, relic keep 3 Midgard, BG central keep 4 neutral, BG portal keep 5 Hibernia");
        Assert.That(Scalar(_rvr.Database, "SELECT COUNT(*) FROM Relic WHERE Realm<>OriginalRealm OR KeepID<>0 OR Region=999"), Is.EqualTo(0L),
            "every relic is home with its realm");
    }

    [Test]
    public void FreshCamlannWorldKeepsEveryKeepNeutralAsToday()
    {
        Create(WorldSaveKind.Camlann, botsPerRealm: 0);

        Assert.That(Scalar(_camlann.Database, "SELECT COUNT(*) FROM [Keep] WHERE Realm<>0 OR ClaimedGuildName<>''"), Is.EqualTo(0L));
        Assert.That(Scalar(_camlann.Database, "SELECT Value FROM offline_local_options WHERE Key='WorldModel'"), Is.EqualTo("Camlann-1"));
    }

    [Test]
    public void FreshCamlannWorldIsBuiltFromTheRvrSaveWithoutTouchingIt()
    {
        string rvrHash = Hash(_rvr.Database);

        WorldReset.Result result = Create(WorldSaveKind.Camlann, botsPerRealm: 2);

        Assert.That(result.Database, Is.EqualTo(Path.Combine(_runtime, "data", "opendaoc-camlann.sqlite3.db")));
        Assert.That(result.Seed, Is.EqualTo(_rvr.Database));
        Assert.That(result.Backup, Is.Null, "nothing was replaced, so no backup folder");
        Assert.That(Directory.Exists(_backups), Is.False);
        Assert.That(Hash(_rvr.Database), Is.EqualTo(rvrHash), "the RvR save is only read");
        Assert.That(Scalar(_camlann.Database, "SELECT COUNT(*) FROM offline_world_bots"), Is.EqualTo(6L));
    }

    [Test]
    public void FreshCamlannWorldMovesItsOwnTwinsAndKeepsItsGothwaiteSettings()
    {
        Create(WorldSaveKind.Camlann, botsPerRealm: 0);
        File.WriteAllText(_camlann.GothwaiteSwitch, "{\"version\":1,\"enabled\":true}");
        File.WriteAllText(_camlann.RvrWorld, "{}");
        File.WriteAllText(_rvr.RvrWorld, "{\"rvr\":true}");

        WorldReset.Result result = Create(WorldSaveKind.Camlann, botsPerRealm: 0);

        Assert.That(_camlann.GothwaiteSwitch, Does.EndWith("gothwaite-pvp-camlann.json"));
        Assert.That(result.Seed, Is.EqualTo(_camlann.Database), "a world's own save is its seed");
        Assert.That(File.Exists(Path.Combine(result.Backup!, "opendaoc-camlann.sqlite3.db")), Is.True);
        Assert.That(File.Exists(Path.Combine(result.Backup!, "rvr-world-camlann.json")), Is.True);
        Assert.That(File.Exists(_camlann.RvrWorld), Is.False);
        Assert.That(File.ReadAllText(_camlann.GothwaiteSwitch), Does.Contain("\"enabled\":true"), "Camlann keeps its Gothwaite switch");
        Assert.That(File.ReadAllText(_rvr.RvrWorld), Is.EqualTo("{\"rvr\":true}"), "the RvR world's side files are not touched");
    }

    [Test]
    public void BothWorldsWriteTodaysRatesAndOpenBgSetup([Values] WorldSaveKind world)
    {
        Create(world, botsPerRealm: 0);
        string database = WorldFiles.For(_runtime, world).Database;

        Assert.Multiple(() =>
        {
            foreach ((string key, string value) in new[]
                     {
                         ("xp_rate", "10"), ("bot_xp_rate", "10"), ("rp_rate", "50"), ("bot_rp_rate", "15"),
                         ("ml_xp_per_level", "10"), ("artifact_xp_rate", "350"), ("toa_loot_in_atlantis", "True"),
                         ("open_bg_enabled", "True"), ("open_bg_mode", "perrealm"), ("open_bg_max_bots", "450"),
                         ("open_bg_groups8", "12"), ("open_bg_groups_small", "8"), ("open_bg_solos", "22"),
                         ("open_bg_band_min", "50"), ("open_bg_presets", "std8:1,caster8:1,raid8:8"),
                     })
                Assert.That(Scalar(database, $"SELECT Value FROM ServerProperty WHERE `Key`='{key}'"), Is.EqualTo(value), key);
            Assert.That(Scalar(database, "SELECT Category FROM ServerProperty WHERE `Key`='xp_rate'"), Is.EqualTo("rates"),
                "an existing row keeps its category");
            Assert.That(Scalar(database, "SELECT Category FROM ServerProperty WHERE `Key`='toa_loot_in_atlantis'"), Is.EqualTo("atlas_rog"),
                "a missing row is created in the server's category");
            Assert.That(Scalar(database, "SELECT COUNT(*) FROM ServerProperty WHERE ServerProperty_ID IS NOT NULL"), Is.EqualTo(0L));
        });
    }

    [Test]
    public void FreshWorldCreatesTheRosterPerRealmAndPointsThePopulationAtIt()
    {
        WorldReset.Result result = Create(WorldSaveKind.Realm, botsPerRealm: 7);

        Assert.That(result.BotsCreated, Is.EquivalentTo(new Dictionary<int, int> { [1] = 7, [2] = 7, [3] = 7 }));
        Assert.That(Rows(_rvr.Database, "SELECT Realm || ':' || COUNT(*) FROM offline_world_bots GROUP BY Realm ORDER BY Realm"),
            Is.EqualTo(new[] { "1:7", "2:7", "3:7" }));
        Assert.That(Scalar(_rvr.Database, "SELECT COUNT(*) FROM offline_world_bots WHERE Level<>1 OR IsOnline<>0 OR GuildId<>''"), Is.EqualTo(0L),
            "FreshLaunch: level 1, queued, no guild (the server's crew manager seeds the guilds)");
        Assert.That(Scalar(_rvr.Database, "SELECT COUNT(DISTINCT Name) FROM offline_world_bots"), Is.EqualTo(21L));
        Assert.That(Scalar(_rvr.Database, "SELECT COUNT(*) FROM offline_world_bots WHERE Name='Old bot'"), Is.EqualTo(0L));
        Assert.That(Scalar(_rvr.Database, "SELECT Value FROM offline_population_settings WHERE Key='ActiveTarget'"), Is.EqualTo("21"));
        Assert.That(Scalar(_rvr.Database, "SELECT Value FROM ServerProperty WHERE `Key`='population_enabled'"), Is.EqualTo("true"));
        Assert.That(Scalar(_rvr.Database, "SELECT Value FROM ServerProperty WHERE `Key`='active_target'"), Is.EqualTo("21"));
        Assert.That(WorldReset.DefaultBotsPerRealm, Is.EqualTo(600), "owner decision 9: about 600 per realm");
    }

    [Test]
    public void FreshWorldClearsProgressAndTheForkBookkeepingButKeepsTheLocalAccount()
    {
        Create(WorldSaveKind.Realm, botsPerRealm: 0);
        string db = _rvr.Database;

        Assert.Multiple(() =>
        {
            Assert.That(Scalar(db, "SELECT COUNT(*) FROM DOLCharacters"), Is.EqualTo(0L));
            Assert.That(Scalar(db, "SELECT COUNT(*) FROM Inventory"), Is.EqualTo(0L));
            Assert.That(Scalar(db, "SELECT COUNT(*) FROM player_companions"), Is.EqualTo(0L));
            Assert.That(Scalar(db, "SELECT COUNT(*) FROM Account WHERE Name='offline'"), Is.EqualTo(1L));
            Assert.That(Scalar(db, "SELECT COUNT(*) FROM Account"), Is.EqualTo(1L));
            Assert.That(Scalar(db, "SELECT PrivLevel FROM Account WHERE Name='offline'"), Is.EqualTo(1L),
                "the kept local account is a normal player, not a GM");
            Assert.That(Scalar(db, "SELECT COUNT(*) FROM BattlegroundCampaignCamp"), Is.EqualTo(1L), "the camp rows stay");
            Assert.That(Scalar(db, "SELECT SponsorGuildId || '|' || Tokens || '|' || ExpiresAt || '|' || CaptainRespawnAt FROM BattlegroundCampaignCamp"),
                Is.EqualTo("|0|2000-01-01 00:00:00|2000-01-01 00:00:00"), "sponsors, tokens and timers belong to the old world");
            Assert.That(Rows(db, "SELECT GuildName FROM Guild"), Is.EqualTo(new[] { "DummyGuildToMakePetsUntargetable" }));
            foreach (string table in WorldReset.FreshWorldClearedTables)
                Assert.That(Scalar(db, $"SELECT COUNT(*) FROM [{table}]"), Is.EqualTo(0L), table);
            Assert.That(Scalar(db, "SELECT COUNT(*) FROM offline_local_options WHERE Key IN ('WorldSimulationClock','CamlannWorldResetUtc')"), Is.EqualTo(0L),
                "a fresh world starts its own clock");
            Assert.That(Scalar(db, "SELECT Value FROM offline_local_options WHERE Key='MakeMeGM'"), Is.EqualTo("false"));
            Assert.That(Scalar(db, "SELECT COUNT(*) FROM offline_local_options WHERE Key='WorldResetUtc'"), Is.EqualTo(1L));
            Assert.That(Scalar(db, "SELECT COUNT(*) FROM ItemTemplate"), Is.EqualTo(1L), "world data stays");
            Assert.That(Scalar(db, "PRAGMA quick_check"), Is.EqualTo("ok"));
        });
    }

    [Test]
    public void RunningServerIsRefusedBeforeAnythingIsCopiedOrMoved()
    {
        string hash = Hash(_rvr.Database);
        File.WriteAllText(_rvr.RvrWorld, "{}");

        Assert.Throws<InvalidOperationException>(() => Create(WorldSaveKind.Realm, botsPerRealm: 1, stopped: () => false));

        Assert.That(Hash(_rvr.Database), Is.EqualTo(hash));
        Assert.That(File.Exists(_rvr.RvrWorld), Is.True);
        Assert.That(Directory.Exists(_backups), Is.False);
        Assert.That(Directory.GetFiles(Path.Combine(_runtime, "data"), "*.building-*"), Is.Empty);
    }

    [Test]
    public void ServerStartingDuringTheBuildLeavesEveryOldFileInPlace()
    {
        string hash = Hash(_rvr.Database);
        File.WriteAllText(_rvr.RvrWorld, "{}");
        int calls = 0;

        Assert.Throws<InvalidOperationException>(() => Create(WorldSaveKind.Realm, botsPerRealm: 1, stopped: () => ++calls == 1));

        Assert.That(Hash(_rvr.Database), Is.EqualTo(hash));
        Assert.That(File.Exists(_rvr.RvrWorld), Is.True);
        Assert.That(Directory.Exists(_backups), Is.False);
        Assert.That(Directory.GetFiles(Path.Combine(_runtime, "data"), "*.building-*"), Is.Empty);
    }

    [Test]
    public void InvalidSeedDataFailsWithoutAPartialReset()
    {
        Sql(_rvr.Database, "DELETE FROM WorldObject WHERE Emblem=13");
        string hash = Hash(_rvr.Database);

        Assert.Throws<InvalidOperationException>(() => Create(WorldSaveKind.Realm, botsPerRealm: 1));

        Assert.That(Hash(_rvr.Database), Is.EqualTo(hash));
        Assert.That(CamlannWorldReset.ReadWorldModel(_rvr.Database), Is.EqualTo("Camlann-1"));
        Assert.That(Directory.Exists(_backups), Is.False);
        Assert.That(Directory.GetFiles(Path.Combine(_runtime, "data"), "*.building-*"), Is.Empty);
    }

    [Test]
    public void ALockedSideFileStopsTheSwapPutsEverythingBackAndNamesTheBackupFolder()
    {
        string hash = Hash(_rvr.Database);
        File.WriteAllText(_rvr.RvrWorld, "{}");

        IOException error;
        using (new FileStream(_rvr.RvrWorld, FileMode.Open, FileAccess.Read, FileShare.None))
            error = Assert.Throws<IOException>(() => Create(WorldSaveKind.Realm, botsPerRealm: 1))!;

        string backup = Directory.GetDirectories(_backups).Single();
        Assert.That(error.Message, Does.Contain(backup));
        Assert.That(error.Message, Does.Contain("Every moved file was put back"));
        Assert.That(Hash(_rvr.Database), Is.EqualTo(hash), "the old save is back in place, unchanged");
        Assert.That(File.Exists(_rvr.RvrWorld), Is.True);
        Assert.That(Directory.GetFiles(backup), Is.Empty);
        Assert.That(Directory.GetFiles(Path.Combine(_runtime, "data"), "*.building-*"), Is.Empty);
    }

    [Test]
    public void AFileThatCannotBePutBackIsNamedWithTheBackupFolder()
    {
        File.WriteAllText(_rvr.RvrWorld, "{}");
        void Move(string from, string to)
        {
            if (from == _rvr.RvrWorld) throw new IOException("simulated lock");
            if (to == _rvr.Database) throw new UnauthorizedAccessException("simulated move-back failure");
            File.Move(from, to);
        }

        IOException error = Assert.Throws<IOException>(() => WorldReset.CreateFresh(
            new WorldReset.Request(WorldSaveKind.Realm, _runtime, "offline", () => true)
            {
                BotsPerRealm = 0, WorldShape = PopulationWorldShape.FreshLaunch, MoveFile = Move,
            }))!;

        string backup = Directory.GetDirectories(_backups).Single();
        string stranded = Path.Combine(backup, "opendaoc.sqlite3.db");
        Assert.That(error.Message, Does.Contain("could not be put back"));
        Assert.That(error.Message, Does.Contain(backup));
        Assert.That(error.Message, Does.Contain(stranded));
        Assert.That(error.InnerException?.Message, Is.EqualTo("simulated lock"));
        Assert.That(File.Exists(stranded), Is.True, "the save is kept in the backup folder, never deleted");
    }

    [Test]
    public void OrphanedBuildFilesOfAnEarlierRunAreSweptButAFreshOneStays()
    {
        string data = Path.Combine(_runtime, "data");
        string orphan = Path.Combine(data, ".opendaoc.sqlite3.db.building-0123456789abcdef");
        string busy = Path.Combine(data, ".opendaoc.sqlite3.db.building-fedcba9876543210");
        string other = Path.Combine(data, ".opendaoc-camlann.sqlite3.db.building-0123456789abcdef");
        foreach (string file in new[] { orphan, orphan + "-journal", busy, other })
            File.WriteAllText(file, "partial");
        foreach (string file in new[] { orphan, orphan + "-journal", other })
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-1));

        Create(WorldSaveKind.Realm, botsPerRealm: 0);

        Assert.That(File.Exists(orphan), Is.False);
        Assert.That(File.Exists(orphan + "-journal"), Is.False);
        Assert.That(File.Exists(busy), Is.True, "a build file written in the last ten minutes may belong to another run");
        Assert.That(File.Exists(other), Is.True, "only the reset world's build files are swept");
    }

    [Test]
    public void UnknownSeedMarkerAndMissingSaveAreRefused()
    {
        Sql(_rvr.Database, "UPDATE offline_local_options SET Value='Mystery-9' WHERE Key='WorldModel'");
        Assert.Throws<InvalidOperationException>(() => Create(WorldSaveKind.Realm, botsPerRealm: 0));

        File.Delete(_rvr.Database);
        Assert.Throws<FileNotFoundException>(() => Create(WorldSaveKind.Camlann, botsPerRealm: 0));
    }

    [Test]
    public void ReplacedFilesCoverTheSaveItsSidecarsAndEveryPerSaveSideFile()
    {
        string[] names = WorldReset.ReplacedFiles(_camlann).Select(path => Path.GetFileName(path)).ToArray();

        Assert.That(names, Is.EquivalentTo(new[]
        {
            "opendaoc-camlann.sqlite3.db", "opendaoc-camlann.sqlite3.db-wal", "opendaoc-camlann.sqlite3.db-shm",
            "opendaoc-camlann.sqlite3.db-journal", "realm-event-records-camlann.sqlite3", "realm-event-records-camlann.sqlite3-wal",
            "realm-event-records-camlann.sqlite3-shm", "realm-event-records-camlann.sqlite3-journal", "open-bg-status-camlann.json",
            "bot-world-camlann.json", "rvr-world-camlann.json", "gothwaite-pvp-camlann.json",
        }));
    }

    [Test]
    public void WorldFilesNameEachWorldsSaveAndTwins()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_rvr.Database, Is.EqualTo(Path.Combine(_runtime, "data", "opendaoc.sqlite3.db")));
            Assert.That(Path.GetFileName(_rvr.EventRecords), Is.EqualTo("realm-event-records.sqlite3"));
            Assert.That(Path.GetFileName(_rvr.OpenBgStatus), Is.EqualTo("open-bg-status.json"));
            Assert.That(_camlann.Database, Is.EqualTo(Path.Combine(_runtime, "data", "opendaoc-camlann.sqlite3.db")));
            Assert.That(Path.GetFileName(_camlann.EventRecords), Is.EqualTo("realm-event-records-camlann.sqlite3"));
            Assert.That(Path.GetFileName(_camlann.OpenBgStatus), Is.EqualTo("open-bg-status-camlann.json"));
            Assert.That(Path.GetFileName(_camlann.GothwaiteSwitch), Is.EqualTo("gothwaite-pvp-camlann.json"));
            Assert.That(_camlann.World, Is.EqualTo(WorldSaveKind.Camlann));
            Assert.That(WorldFiles.DefaultBackupRoot(_runtime), Is.EqualTo(_backups));
        });
    }

    [Test]
    public void ServerConfigSelectsTheWorldsSaveAndKeepsEverythingElse()
    {
        string path = Path.Combine(_runtime, "server", "serverconfig.xml");
        const string original = "<Server>\r\n  <GameType>Normal</GameType>\r\n  <DBType>SQLITE</DBType>\r\n" +
                                "  <DBConnectionString>Data Source=../data/opendaoc.sqlite3.db;Version=3;Pooling=False;Journal Mode=WAL</DBConnectionString>\r\n</Server>\r\n";
        File.WriteAllText(path, original);

        CamlannServerConfig.EnsureWorld(path, WorldSaveKind.Camlann);
        string camlann = File.ReadAllText(path);
        Assert.That(camlann, Does.Contain("<GameType>PvP</GameType>"));
        Assert.That(camlann, Does.Contain("<DBConnectionString>Data Source=../data/opendaoc-camlann.sqlite3.db;Version=3;Pooling=False;Journal Mode=WAL</DBConnectionString>"));
        Assert.That(camlann.Count(character => character == '\r'), Is.EqualTo(5));
        Assert.That(CamlannServerConfig.ReadDatabaseFileName(path), Is.EqualTo("opendaoc-camlann.sqlite3.db"));

        CamlannServerConfig.EnsureWorld(path, WorldSaveKind.Realm);
        Assert.That(File.ReadAllText(path), Is.EqualTo(original.Replace("<GameType>Normal</GameType>", "<GameType>PvP</GameType>")));

        File.WriteAllText(path, original.Replace("SQLITE", "MYSQL"));
        Assert.Throws<InvalidOperationException>(() => CamlannServerConfig.EnsureWorld(path, WorldSaveKind.Camlann));
    }

    /// <summary>
    /// Builds a full fresh world (600 bots per realm) from a real save copy when OFFLINE_DAOC_RESET_SEED names one, and
    /// leaves the result at OFFLINE_DAOC_RESET_OUT for the server's data pack and boot checks (docs/RVR_WORLDS.md).
    /// Never point the seed at a live save: the routine moves the replaced file to the backup folder.
    /// </summary>
    [Test]
    public void FullFreshWorldFromARealSaveCopy([Values] WorldSaveKind world)
    {
        string? seed = Environment.GetEnvironmentVariable("OFFLINE_DAOC_RESET_SEED");
        string? output = Environment.GetEnvironmentVariable("OFFLINE_DAOC_RESET_OUT");
        string account = Environment.GetEnvironmentVariable("OFFLINE_DAOC_RESET_ACCOUNT") ?? "r5a-probe";
        if (string.IsNullOrEmpty(seed) || !File.Exists(seed))
            Assert.Ignore("Set OFFLINE_DAOC_RESET_SEED to a copy of a real save (and OFFLINE_DAOC_RESET_OUT for the result).");

        File.Delete(_rvr.Database);
        File.Copy(seed, _rvr.Database);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        WorldReset.Result result = WorldReset.CreateFresh(new WorldReset.Request(world, _runtime, account, () => true)
        {
            WorldShape = PopulationWorldShape.FreshLaunch,
        });
        TestContext.Out.WriteLine($"{world}: {stopwatch.Elapsed.TotalSeconds:0.0} s, removed {result.Characters} characters, {result.Bots} bots, " +
                                  $"{result.Guilds} guilds; {result.RealmKeeps} realm keeps; backup {result.Backup}");

        string database = WorldFiles.For(_runtime, world).Database;
        Assert.That(Rows(database, "SELECT Realm || ':' || COUNT(*) FROM offline_world_bots GROUP BY Realm ORDER BY Realm"),
            Is.EqualTo(new[] { "1:600", "2:600", "3:600" }));
        Assert.That(Scalar(database, "SELECT Value FROM offline_local_options WHERE Key='WorldModel'"), Is.EqualTo(WorldSaveLayout.Marker(world)));
        Assert.That(Scalar(database, "SELECT COUNT(*) FROM DOLCharacters"), Is.EqualTo(0L));
        Assert.That(Scalar(database, "SELECT COUNT(*) FROM Guild WHERE GuildName<>'DummyGuildToMakePetsUntargetable'"), Is.EqualTo(0L));
        Assert.That(Scalar(database, "SELECT COUNT(*) FROM [Keep] WHERE ClaimedGuildName<>''"), Is.EqualTo(0L));
        if (world == WorldSaveKind.Realm)
        {
            foreach (string row in Rows(database, "SELECT Name || ' ' || Realm || '/' || OriginalRealm || ' base ' || BaseLevel || ' skin ' || SkinType FROM [Keep] WHERE Realm<>0 ORDER BY KeepID"))
                TestContext.Out.WriteLine("realm keep: " + row);
            Assert.That(Scalar(database, "SELECT COUNT(*) FROM [Keep] WHERE Realm<>0 AND NOT (BaseLevel>=100 OR SkinType=99)"), Is.EqualTo(0L));
            Assert.That(Scalar(database, "SELECT COUNT(*) FROM [Keep] WHERE SkinType=99 AND Realm<>OriginalRealm"), Is.EqualTo(0L));
        }
        else
            Assert.That(Scalar(database, "SELECT COUNT(*) FROM [Keep] WHERE Realm<>0"), Is.EqualTo(0L));
        Assert.That(Scalar(database, "PRAGMA quick_check"), Is.EqualTo("ok"));

        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            SQLiteConnection.ClearAllPools();
            File.Copy(database, Path.Combine(output, Path.GetFileName(database)), overwrite: true);
        }
    }

    private WorldReset.Result Create(WorldSaveKind world, int botsPerRealm, Func<bool>? stopped = null) =>
        WorldReset.CreateFresh(new WorldReset.Request(world, _runtime, "offline", stopped ?? (() => true))
        {
            BotsPerRealm = botsPerRealm,
            WorldShape = PopulationWorldShape.FreshLaunch,
        });

    private static void CreateSeed(string database, string marker)
    {
        Sql(database, $"""
            CREATE TABLE Account(Name TEXT PRIMARY KEY, Password TEXT, PrivLevel INT NOT NULL DEFAULT 0);
            INSERT INTO Account VALUES ('offline','secret',2),('old-account','old',3);
            CREATE TABLE DOLCharacters(DOLCharacters_ID TEXT PRIMARY KEY,AccountName TEXT,Name TEXT);
            INSERT INTO DOLCharacters VALUES ('character-1','offline','Nova');
            CREATE TABLE DOLCharactersBackup(DOLCharacters_ID TEXT PRIMARY KEY,AccountName TEXT);
            CREATE TABLE Inventory(Inventory_ID TEXT PRIMARY KEY,OwnerID TEXT,ITemplate_Id TEXT,SlotPosition INTEGER,Count INTEGER,Condition INTEGER,Durability INTEGER,LastTimeRowUpdated TEXT);
            INSERT INTO Inventory(Inventory_ID,OwnerID) VALUES ('1','character-1'),('2','offlinebot:41'),('3','playercompanion:9');
            CREATE TABLE ItemTemplate(Id_nb TEXT PRIMARY KEY,MaxCondition INTEGER,MaxDurability INTEGER);
            INSERT INTO ItemTemplate VALUES ('sword',100,100);
            CREATE TABLE offline_world_bots(BotId INTEGER PRIMARY KEY AUTOINCREMENT,Name TEXT NOT NULL DEFAULT '' COLLATE NOCASE,Realm INT,ClassId INT,ClassName TEXT,
                RaceId INT,RaceName TEXT,Gender INT,Level INT,Experience INT,RealmPoints INT,ZoneName TEXT,Activity TEXT,IsOnline INT,IsAlive INT,
                LastUpdateUtc TEXT,MoneyCopper INT,InventoryRevision INT,ZoneId INT,X INT,Y INT,Z INT,RegionId INT,Health INT,Mana INT,Endurance INT,
                BindRegionId INT,BindX INT,BindY INT,BindZ INT,CurrentGoal TEXT,ObjectiveProgress TEXT,LastMeaningfulProgressUtc TEXT,IsRetired INT NOT NULL DEFAULT 0,
                SerializedAbilities TEXT,GuildId TEXT NOT NULL DEFAULT '');
            INSERT INTO offline_world_bots(BotId,Name,Realm) VALUES (41,'Old bot',1);
            CREATE TABLE player_companions(CompanionId TEXT PRIMARY KEY,Name TEXT);
            INSERT INTO player_companions VALUES ('9','Isolde');
            CREATE TABLE StartupLocation(Region INT,XPos INT,YPos INT,ZPos INT,MinVersion INT,RealmID INT,RaceID INT,ClassID INT);
            INSERT INTO StartupLocation VALUES (1,100,100,100,0,1,0,0),(100,200,200,200,0,2,0,0),(200,300,300,300,0,3,0,0);
            CREATE TABLE Guild(GuildID TEXT PRIMARY KEY,GuildName TEXT,Realm INT);
            INSERT INTO Guild VALUES ('dummy','DummyGuildToMakePetsUntargetable',0),('crew','Camlann Crew Night Stalkers',0),('wardens','Frontier Wardens',0);
            CREATE TABLE GuildRank(GuildID TEXT);
            INSERT INTO GuildRank VALUES ('dummy'),('crew');
            CREATE TABLE GuildAlliance(GuildAlliance_ID INTEGER PRIMARY KEY);
            CREATE TABLE Keep(KeepID INTEGER PRIMARY KEY,Name TEXT,Realm INTEGER,OriginalRealm INTEGER,BaseLevel INTEGER,SkinType INTEGER,ClaimedGuildName TEXT,
                ClaimedAt TEXT,LordDefeated INTEGER,LastCaptureRewardAt TEXT);
            INSERT INTO Keep VALUES
                (1,'Albion Portal Keep',0,1,255,0,'','',0,''),
                (2,'Caer Benowyc',3,1,50,0,'Camlann Crew Night Stalkers','2026-10-01',1,'2026-10-01'),
                (3,'Grallarhorn Faste',1,2,60,99,'Frontier Wardens','',0,''),
                (4,'Thidranki Faste',1,0,26,0,'Camp Watch','',0,''),
                (5,'Hibernia Portal Keep',0,3,100,0,'','',0,'');
            CREATE TABLE KeepCaptureLog(Id INTEGER PRIMARY KEY);
            INSERT INTO KeepCaptureLog VALUES (1);
            CREATE TABLE Relic(RelicID INTEGER PRIMARY KEY,OriginalRealm INTEGER,relicType INTEGER,Realm INTEGER,LastRealm INTEGER,LastCaptureDate TEXT,Region INTEGER,
                X INTEGER,Y INTEGER,Z INTEGER,Heading INTEGER,KeepID INTEGER);
            INSERT INTO Relic VALUES (1,1,0,2,2,'x',999,1,1,1,1,5),(2,1,1,1,1,NULL,999,1,1,1,1,0),(3,2,0,2,2,NULL,999,1,1,1,1,0),
                (4,2,1,2,2,NULL,999,1,1,1,1,0),(5,3,0,3,3,NULL,999,1,1,1,1,0),(6,3,1,3,3,NULL,999,1,1,1,1,0);
            CREATE TABLE WorldObject(ClassType TEXT,Emblem INTEGER,Region INTEGER,X INTEGER,Y INTEGER,Z INTEGER,Heading INTEGER);
            INSERT INTO WorldObject VALUES ('DOL.GS.GameRelicPad',1,1,100,101,102,103),('DOL.GS.GameRelicPad',11,1,110,111,112,113),
                ('DOL.GS.GameRelicPad',2,100,200,201,202,203),('DOL.GS.GameRelicPad',12,100,210,211,212,213),
                ('DOL.GS.GameRelicPad',3,200,300,301,302,303),('DOL.GS.GameRelicPad',13,200,310,311,312,313);
            CREATE TABLE ServerProperty(Category TEXT NOT NULL DEFAULT '',`Key` VARCHAR(255) NOT NULL DEFAULT '' PRIMARY KEY,Description TEXT NOT NULL DEFAULT '',
                DefaultValue TEXT NOT NULL DEFAULT '',Value TEXT NOT NULL DEFAULT '',LastTimeRowUpdated DATETIME NOT NULL DEFAULT '2000-01-01 00:00:00',
                ServerProperty_ID VARCHAR(255) DEFAULT NULL);
            CREATE UNIQUE INDEX ServerPropertyId ON ServerProperty(ServerProperty_ID);
            INSERT INTO ServerProperty(Category,`Key`,Description,DefaultValue,Value) VALUES
                ('rates','xp_rate','x','1','1'),('rates','bot_xp_rate','x','1','3'),('pvp','open_bg_mode','x','serverpvp','serverpvp'),
                ('autonomous_population','population_enabled','x','false','true'),('autonomous_population','active_target','x','0','1810'),
                ('autonomous_population','hard_active_cap','x','12','0');
            CREATE TABLE offline_population_settings(Key TEXT PRIMARY KEY,Value TEXT,Description TEXT);
            INSERT INTO offline_population_settings VALUES ('PopulationEnabled','true',''),('ActiveTarget','1810',''),('HardActiveCap','0','');
            CREATE TABLE offline_managed_guild_charters(GuildId TEXT PRIMARY KEY);
            INSERT INTO offline_managed_guild_charters VALUES ('crew');
            CREATE TABLE offline_crew_consolidation(SourceGuildId TEXT PRIMARY KEY);
            INSERT INTO offline_crew_consolidation VALUES ('old');
            CREATE TABLE offline_gothwaite_pvp_home(HomeId INTEGER PRIMARY KEY,BotId INTEGER);
            INSERT INTO offline_gothwaite_pvp_home VALUES (1,41);
            CREATE TABLE offline_guild_grudges(Id INTEGER PRIMARY KEY);
            INSERT INTO offline_guild_grudges VALUES (1);
            CREATE TABLE offline_runtime_status(Id INTEGER PRIMARY KEY,ServerState TEXT);
            INSERT INTO offline_runtime_status VALUES (1,'Stopped');
            CREATE TABLE KeepHookPointItem(KeepID INT,ComponentID INT,HookPointID INT,ClassType TEXT,KeepHookPointItem_ID TEXT PRIMARY KEY);
            INSERT INTO KeepHookPointItem VALUES (2,1,3,'DOL.GS.Keeps.GameKeepGuard','hook-1');
            CREATE TABLE BattlegroundCampaignCamp(CampKey TEXT,SponsorGuildId TEXT NOT NULL DEFAULT '',Tokens INT NOT NULL DEFAULT 0,
                ExpiresAt DATETIME NOT NULL DEFAULT '2000-01-01 00:00:00',CaptainRespawnAt DATETIME NOT NULL DEFAULT '2000-01-01 00:00:00',
                BattlegroundCampaignCamp_ID TEXT PRIMARY KEY);
            INSERT INTO BattlegroundCampaignCamp VALUES ('thid-north','crew',7,'2026-10-24 20:00:00','2026-10-24 19:00:00','camp-1');
            CREATE TABLE realm_exchange_sales(SlotId INTEGER PRIMARY KEY);
            INSERT INTO realm_exchange_sales VALUES (1);
            CREATE TABLE offline_local_options(Key TEXT PRIMARY KEY,Value TEXT NOT NULL);
            INSERT INTO offline_local_options VALUES ('MakeMeGM','true'),('WorldModel','{marker}'),('CamlannWorldResetUtc','2026-09-26'),
                ('WorldSimulationClock','{"{"}"Version":1{"}"}');
            """);
    }

    private static void CreateEventRecords(string path)
    {
        using var events = new SQLiteConnection($"Data Source={path};Pooling=False");
        events.Open();
        using var command = events.CreateCommand();
        command.CommandText = "CREATE TABLE Events(Id TEXT PRIMARY KEY,Name TEXT); INSERT INTO Events VALUES ('1','Old event')";
        command.ExecuteNonQuery();
    }

    private static void Sql(string database, string sql)
    {
        using var connection = new SQLiteConnection($"Data Source={database};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(string database, string sql)
    {
        using var connection = new SQLiteConnection($"Data Source={database};Pooling=False;Read Only=True");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static string[] Rows(string database, string sql)
    {
        using var connection = new SQLiteConnection($"Data Source={database};Pooling=False;Read Only=True");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(Convert.ToString(reader.GetValue(0)) ?? string.Empty);
        return rows.ToArray();
    }

    private static string Hash(string path)
    {
        SQLiteConnection.ClearAllPools();
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }
}
