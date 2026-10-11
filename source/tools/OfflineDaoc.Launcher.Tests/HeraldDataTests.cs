using System.Data.SQLite;
using System.Text.Json;
using NUnit.Framework;
using OfflineDaoc.Launcher;

[TestFixture, NonParallelizable]
public sealed class HeraldDataTests
{
    private string _directory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        // A local temp folder: SQLite cannot open databases under the UNC work directory used for the WSL build.
        _directory = Path.Combine(Path.GetTempPath(), "daoc-herald-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        SQLiteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [TestCase(0, "RR 1L0")]
    [TestCase(1, "RR 1L1")]
    [TestCase(24, "RR 1L1")]
    [TestCase(25, "RR 1L2")]
    [TestCase(124, "RR 1L2")]
    [TestCase(125, "RR 1L3")]
    [TestCase(1375, "RR 1L6")]
    [TestCase(95566, "RR 3L3")]
    [TestCase(8208749, "RR 10L9")]
    [TestCase(8208750, "RR 11L0")]
    [TestCase(187917143, "RR 14L0")]
    [TestCase(999999999999, "RR 14L0")]
    [TestCase(-5, "RR 1L0")]
    public void RealmRankTextFollowsTheServerRealmPointTable(long realmPoints, string expected)
    {
        Assert.That(HeraldData.RealmRankFromRealmPoints(realmPoints), Is.EqualTo(expected));
    }

    [TestCase(0, "RR 1L0")]
    [TestCase(2, "RR 1L2")]
    [TestCase(12, "RR 2L2")]
    [TestCase(130, "RR 14L0")]
    public void RealmRankTextUsesRankTenPlusLevelDigit(int realmLevel, string expected)
    {
        Assert.That(HeraldData.RealmRankText(realmLevel), Is.EqualTo(expected));
    }

    [Test]
    public void RealmPointTableMatchesTheServerLengthAndTopLevel()
    {
        Assert.That(HeraldData.RealmPointsForLevel, Has.Length.EqualTo(131));
        Assert.That(HeraldData.RealmPointsForLevel[2], Is.EqualTo(25));
        Assert.That(HeraldData.RealmPointsForLevel[100], Is.EqualTo(8208750));
        Assert.That(HeraldData.RealmPointsForLevel[130], Is.EqualTo(187917143));
    }

    [Test]
    public void LoadReadsEveryKindSkipsRetiredBotsAndKeepsPrivateRowsOutOfRankings()
    {
        string path = BuildSave(counters: false);

        HeraldSnapshot snapshot = HeraldData.Load(path);

        Assert.That(snapshot.DatabaseFound, Is.True);
        Assert.That(snapshot.Characters.Select(c => c.Name), Does.Contain("Alpha").And.Contain("Bot Albion").And.Contain("Comp One"));
        Assert.That(snapshot.Characters.Select(c => c.Name), Does.Not.Contain("Bot Retired"));
        HeraldStatistics stats = HeraldData.Statistics(snapshot);
        Assert.Multiple(() =>
        {
            Assert.That(stats.Visible, Is.EqualTo(7));
            Assert.That(stats.Players, Is.EqualTo(3));
            Assert.That(stats.Gamebots, Is.EqualTo(2));
            Assert.That(stats.Companions, Is.EqualTo(2));
            Assert.That(stats.Hidden, Is.EqualTo(2));
            Assert.That(stats.RealmPoints, Is.EqualTo(1125941));
        });

        IReadOnlyList<HeraldRankRow> rows = HeraldData.Rankings(snapshot, new HeraldRankQuery(Limit: 0));
        Assert.That(rows.Select(row => row.Name), Is.EqualTo(new[] { "Charlie", "Alpha", "Bravo", "Bot Albion", "Bot Mid", "Comp One", "Comp Orphan" }));
        Assert.That(rows.Select(row => row.Name), Does.Not.Contain("Hidden").And.Not.Contains("Ghost"));
        Assert.That(rows.Select(row => row.Position), Is.EqualTo(Enumerable.Range(1, 7)));
    }

    [Test]
    public void GameMastersAreTaggedAndEveryCharacterKindGetsItsOwnTypeTag()
    {
        HeraldSnapshot snapshot = HeraldData.Load(BuildSave(counters: false));

        IReadOnlyList<HeraldRankRow> rows = HeraldData.Rankings(snapshot, new HeraldRankQuery(Limit: 0));
        Dictionary<string, string> types = rows.ToDictionary(row => row.Name, row => row.Type);
        Assert.Multiple(() =>
        {
            Assert.That(types["Charlie"], Is.EqualTo("GM"));
            Assert.That(types["Alpha"], Is.EqualTo("Player"));
            Assert.That(types["Bot Albion"], Is.EqualTo("Gamebot"));
            Assert.That(types["Comp One"], Is.EqualTo("Companion"));
        });
    }

    [Test]
    public void RankRealmPointsUseStoredRealmLevelForPlayersAndDerivedLevelForOthers()
    {
        HeraldSnapshot snapshot = HeraldData.Load(BuildSave(counters: false));

        Dictionary<string, string> ranks = HeraldData.Rankings(snapshot, new HeraldRankQuery(Limit: 0))
            .ToDictionary(row => row.Name, row => row.Rank);
        Assert.Multiple(() =>
        {
            Assert.That(ranks["Alpha"], Is.EqualTo("RR 3L3"), "Players keep the stored RealmLevel 23.");
            Assert.That(ranks["Charlie"], Is.EqualTo("RR 5L9"));
            Assert.That(ranks["Bot Albion"], Is.EqualTo("RR 1L4"), "Gamebots store only RP: 350 RP is level 4.");
            Assert.That(ranks["Bot Mid"], Is.EqualTo("RR 1L2"), "25 RP is level 2.");
            Assert.That(ranks["Comp One"], Is.EqualTo("RR 1L0"));
        });
    }

    [Test]
    public void UnrecordedBotCountersAreUnknownNotZeroAndSortLastWhenRankedByKills()
    {
        HeraldSnapshot snapshot = HeraldData.Load(BuildSave(counters: false));

        HeraldCharacter bot = snapshot.Characters.Single(c => c.Name == "Bot Albion");
        Assert.Multiple(() =>
        {
            Assert.That(bot.Combat.Kills, Is.Null, "A missing counter column must not read as zero.");
            Assert.That(bot.Combat.Deaths, Is.Null);
            Assert.That(snapshot.Coverage.GamebotKillCounters, Is.False);
            Assert.That(snapshot.Coverage.CompanionKillCounters, Is.False);
        });

        IReadOnlyList<HeraldRankRow> rows = HeraldData.Rankings(snapshot, new HeraldRankQuery(Metric: HeraldMetric.Kills, Limit: 0));
        Assert.That(rows.Select(row => row.Name).Take(3), Is.EqualTo(new[] { "Bravo", "Charlie", "Alpha" }));
        Assert.That(rows.Skip(3).Select(row => row.Kills), Has.All.Null);
        Assert.That(rows.Skip(3).Select(row => row.Name), Is.EquivalentTo(new[] { "Bot Albion", "Bot Mid", "Comp One", "Comp Orphan" }));
    }

    [Test]
    public void RecordedBotAndCompanionCountersAreReadSummedAndSortByMetric()
    {
        string path = BuildSave(counters: true);

        HeraldSnapshot snapshot = HeraldData.Load(path);

        HeraldCharacter bot = snapshot.Characters.Single(c => c.Name == "Bot Albion");
        HeraldCharacter companion = snapshot.Characters.Single(c => c.Name == "Comp One");
        HeraldCharacter orphan = snapshot.Characters.Single(c => c.Name == "Comp Orphan");
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Coverage.GamebotKillCounters, Is.True);
            Assert.That(snapshot.Coverage.CompanionKillCounters, Is.True);
            Assert.That(bot.Combat.Kills, Is.EqualTo(12));
            Assert.That(bot.Combat.DeathBlows, Is.EqualTo(6));
            Assert.That(bot.Combat.Solo, Is.EqualTo(3));
            Assert.That(bot.Combat.Deaths, Is.EqualTo(6));
            Assert.That(companion.Combat.Kills, Is.EqualTo(2));
            Assert.That(companion.Combat.Deaths, Is.EqualTo(1));
            Assert.That(orphan.Combat.Kills, Is.EqualTo(0), "A recorded zero is a real value, not an unknown.");
        });

        IReadOnlyList<HeraldRankRow> rows = HeraldData.Rankings(snapshot,
            new HeraldRankQuery(Kind: HeraldKindFilter.Gamebots, Metric: HeraldMetric.Kills, Limit: 0));
        Assert.That(rows.Select(row => row.Name), Is.EqualTo(new[] { "Bot Albion", "Bot Mid" }));
        Assert.That(rows[0].Kills, Is.EqualTo(12));
        Assert.That(rows[1].Kills, Is.EqualTo(10), "Bot Mid has only its Midgard kill counter written, and the rest read as recorded zeros.");
    }

    [Test]
    public void MissingOptionalTablesAndColumnsAreToleratedAndReportedInCoverage()
    {
        string path = Path.Combine(_directory, "minimal.db");
        using (var connection = new SQLiteConnection($"Data Source={path};Version=3;Pooling=False;"))
        {
            connection.Open();
            Execute(connection, """
                CREATE TABLE DOLCharacters (DOLCharacters_ID TEXT, Name TEXT, Level INTEGER, Class INTEGER, Realm INTEGER, RealmPoints INTEGER);
                INSERT INTO DOLCharacters VALUES ('char-x', 'Solo', 12, 1, 1, 150);
                """);
        }

        HeraldSnapshot snapshot = HeraldData.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Characters, Has.Count.EqualTo(1));
            HeraldCharacter solo = snapshot.Characters.Single();
            Assert.That(solo.RealmRank, Is.EqualTo("RR 1L3"), "RealmLevel is derived when the column is missing.");
            Assert.That(solo.Combat.Kills, Is.Null);
            Assert.That(solo.IsGameMaster, Is.False);
            Assert.That(snapshot.Guilds, Is.Empty);
            Assert.That(snapshot.Keeps, Is.Empty);
            Assert.That(snapshot.Relics, Is.Empty);
            Assert.That(snapshot.Coverage.Players, Is.True);
            Assert.That(snapshot.Coverage.Gamebots, Is.False);
            Assert.That(snapshot.Coverage.Companions, Is.False);
            Assert.That(snapshot.Coverage.Guilds, Is.False);
            Assert.That(snapshot.Coverage.Keeps, Is.False);
            Assert.That(snapshot.Coverage.Relics, Is.False);
            Assert.That(snapshot.Coverage.Accounts, Is.False);
            Assert.That(snapshot.Coverage.CapturedCounters, Is.False);
            Assert.That(HeraldData.GuildRows(snapshot), Is.Empty);
            Assert.That(HeraldData.Statistics(snapshot).Visible, Is.EqualTo(1));
        });
    }

    [Test]
    public void MissingDatabaseReturnsAnEmptySnapshotAndCreatesNothing()
    {
        string path = Path.Combine(_directory, "not-there.db");

        HeraldSnapshot snapshot = HeraldData.Load(path);

        Assert.That(snapshot.DatabaseFound, Is.False);
        Assert.That(snapshot.Characters, Is.Empty);
        Assert.That(File.Exists(path), Is.False, "The Herald must never create a save.");
    }

    [Test]
    public void LoadNeverChangesTheSaveFile()
    {
        string path = BuildSave(counters: true);
        byte[] before = File.ReadAllBytes(path);

        HeraldData.Load(path);
        HeraldData.Rankings(HeraldData.Load(path), new HeraldRankQuery(Limit: 0));

        Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
    }

    [Test]
    public void GuildTotalsCountVisibleMembersAcrossKindsAndClaimedKeeps()
    {
        HeraldSnapshot snapshot = HeraldData.Load(BuildSave(counters: false));

        IReadOnlyList<HeraldGuildRow> guilds = HeraldData.GuildRows(snapshot);

        Assert.That(guilds.Select(row => row.Name), Is.EqualTo(new[] { "Clan Alpha", "Mid Crew" }),
            "Guilds without members or claimed keeps are left out, and the order is by member RP.");
        HeraldGuildRow alpha = guilds[0];
        Assert.Multiple(() =>
        {
            Assert.That(alpha.Members, Is.EqualTo(3));
            Assert.That(alpha.Players, Is.EqualTo(1));
            Assert.That(alpha.Gamebots, Is.EqualTo(1));
            Assert.That(alpha.Companions, Is.EqualTo(1));
            Assert.That(alpha.MemberRealmPoints, Is.EqualTo(95916));
            Assert.That(alpha.GuildRealmPoints, Is.EqualTo(1000));
            Assert.That(alpha.KeepsClaimed, Is.EqualTo(1));
            Assert.That(alpha.Realm, Is.EqualTo("Albion"));
        });

        HeraldGuildRow mid = guilds[1];
        Assert.Multiple(() =>
        {
            Assert.That(mid.Members, Is.EqualTo(2), "The hidden Albion player in Mid Crew is not counted.");
            Assert.That(mid.MemberRealmPoints, Is.EqualTo(30025));
            Assert.That(mid.KeepsClaimed, Is.EqualTo(0));
        });
    }

    [Test]
    public void StatisticsGiveRealmBalanceClassSplitLevelBandsAndLevelFiftyCounts()
    {
        HeraldSnapshot snapshot = HeraldData.Load(BuildSave(counters: false));

        HeraldStatistics stats = HeraldData.Statistics(snapshot);

        HeraldRealmBalance albion = stats.Realms.Single(r => r.Realm == HeraldData.Albion);
        Assert.Multiple(() =>
        {
            Assert.That(albion.Characters, Is.EqualTo(4));
            Assert.That(albion.RealmPoints, Is.EqualTo(95916));
            Assert.That(albion.KeepsOwned, Is.EqualTo(1));
            Assert.That(albion.Level50, Is.EqualTo(1));
            Assert.That(stats.Realms.Single(r => r.Realm == HeraldData.Midgard).Characters, Is.EqualTo(2));
            Assert.That(stats.Realms.Single(r => r.Realm == HeraldData.Midgard).KeepsOwned, Is.EqualTo(1));
            Assert.That(stats.Realms.Single(r => r.Realm == HeraldData.Hibernia).Level50, Is.EqualTo(1));
        });

        HeraldRealmClasses albionClasses = stats.ClassesByRealm.Single(r => r.Realm == HeraldData.Albion);
        Assert.That(albionClasses.Classes.Select(c => (c.ClassName, c.Count)),
            Is.EqualTo(new[] { ("Paladin", 2), ("Cleric", 1), ("Scout", 1) }));

        Dictionary<string, int> bands = stats.Levels.ToDictionary(band => band.Label, band => band.Count);
        Assert.Multiple(() =>
        {
            Assert.That(bands["50"], Is.EqualTo(2));
            Assert.That(bands["45–49"], Is.EqualTo(1));
            Assert.That(bands["40–44"], Is.EqualTo(1));
            Assert.That(bands["30–34"], Is.EqualTo(1));
            Assert.That(bands["20–24"], Is.EqualTo(1));
            Assert.That(bands["10–14"], Is.EqualTo(1));
            Assert.That(bands.Values.Sum(), Is.EqualTo(stats.Visible));
        });
    }

    [Test]
    public void RankingFiltersApplyKindRealmClassNameAndLimit()
    {
        HeraldSnapshot snapshot = HeraldData.Load(BuildSave(counters: false));

        Assert.That(HeraldData.Rankings(snapshot, new HeraldRankQuery(Kind: HeraldKindFilter.Gamebots, Limit: 0)).Select(r => r.Name),
            Is.EqualTo(new[] { "Bot Albion", "Bot Mid" }));
        Assert.That(HeraldData.Rankings(snapshot, new HeraldRankQuery(Realm: HeraldData.Albion, Limit: 0)).Select(r => r.Name),
            Is.EqualTo(new[] { "Alpha", "Bot Albion", "Comp One", "Comp Orphan" }));
        Assert.That(HeraldData.Rankings(snapshot, new HeraldRankQuery(ClassName: "Scout", Limit: 0)).Select(r => r.Name),
            Is.EqualTo(new[] { "Bot Albion" }));
        Assert.That(HeraldData.Rankings(snapshot, new HeraldRankQuery(NameFilter: "BRAVO", Limit: 0)).Select(r => r.Name),
            Is.EqualTo(new[] { "Bravo" }));
        IReadOnlyList<HeraldRankRow> top = HeraldData.Rankings(snapshot, new HeraldRankQuery(Limit: 2));
        Assert.That(top.Select(r => r.Name), Is.EqualTo(new[] { "Charlie", "Alpha" }));
        Assert.That(top.Select(r => r.Position), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void TitleHoldersDefaultToRealPlayersAndCanIncludeEveryKind()
    {
        string path = BuildSave(counters: true);
        ExecuteOn(path, "UPDATE offline_world_bots SET KillsAlbionPlayers = 2500 WHERE BotId = 1;");
        HeraldSnapshot snapshot = HeraldData.Load(path);
        HeraldTitle bane = HeraldData.Titles.Single(t => t.Name == "Bane of Albion");

        Assert.That(HeraldData.TitleHolders(snapshot, bane, HeraldKindFilter.Players, 0), Is.Empty,
            "Titles are granted to real players in game, so a gamebot's kills do not count by default.");
        IReadOnlyList<HeraldTitleHolder> everyone = HeraldData.TitleHolders(snapshot, bane, HeraldKindFilter.All, HeraldData.Albion);
        Assert.That(everyone.Select(h => (h.Name, h.Type, h.Value)), Is.EqualTo(new[] { ("Bot Albion", "Gamebot", 2500L) }));
    }

    [TestCase("Bane of Albion", new[] { "T2000", "T24999" })]
    [TestCase("Scourge of Albion", new[] { "T25000" })]
    [TestCase("Slayer of Albion", new[] { "T100000" })]
    [TestCase("Master Soldier", new[] { "T2000", "T24999", "MIX" }, Description = "All-realm kills from 2,000 to 24,999, summed across realms")]
    [TestCase("Battle Enforcer", new[] { "DB2000" })]
    [TestCase("Lone Enforcer", new[] { "SOLO2000" })]
    [TestCase("Frontier Challenger", new[] { "K10", "K49" })]
    [TestCase("Frontier Vindicator", new[] { "K50", "K499" })]
    [TestCase("Frontier Protector", new[] { "K500" })]
    [TestCase("Stronghold Soldier", new[] { "W100", "W999" })]
    [TestCase("Stronghold Chief", new[] { "W1000" })]
    public void TitleThresholdsFollowTheServerRanges(string titleName, string[] expectedHolders)
    {
        string path = BuildTitleSave();
        HeraldSnapshot snapshot = HeraldData.Load(path);
        HeraldTitle title = HeraldData.Titles.Single(t => t.Name == titleName);

        IReadOnlyList<HeraldTitleHolder> holders = HeraldData.TitleHolders(snapshot, title, HeraldKindFilter.Players, 0);

        Assert.That(holders.Select(h => h.Name), Is.EquivalentTo(expectedHolders));
    }

    [Test]
    public void TitleThresholdsAreWrittenInTheServerWordingWithoutInventingCaps()
    {
        HeraldTitle bane = HeraldData.Titles.Single(t => t.Name == "Bane of Albion");
        HeraldTitle slayer = HeraldData.Titles.Single(t => t.Name == "Slayer of Albion");
        HeraldTitle challenger = HeraldData.Titles.Single(t => t.Name == "Frontier Challenger");

        Assert.Multiple(() =>
        {
            Assert.That(bane.Threshold, Is.EqualTo("2,000–24,999"));
            Assert.That(slayer.Threshold, Is.EqualTo("100,000+"));
            Assert.That(challenger.Threshold, Is.EqualTo("10–49"));
            Assert.That(HeraldData.Titles, Has.Count.EqualTo(21));
        });
    }

    [Test]
    public void RelicsAreNamedByKeepAndShowTheirCaptureTime()
    {
        HeraldSnapshot snapshot = HeraldData.Load(BuildSave(counters: false));

        IReadOnlyList<HeraldRelicRow> relics = HeraldData.RelicRows(snapshot);

        Assert.That(relics, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(relics[0].Type, Is.EqualTo("Strength"));
            Assert.That(relics[0].OriginalRealm, Is.EqualTo("Albion"));
            Assert.That(relics[0].CurrentRealm, Is.EqualTo("Midgard"));
            Assert.That(relics[0].Keep, Is.EqualTo("Keep Mid"));
            Assert.That(relics[0].LastCaptured, Is.EqualTo("2026-09-14 12:30"));
            Assert.That(relics[1].Type, Is.EqualTo("Magic"));
            Assert.That(relics[1].LastCaptured, Is.EqualTo("—"));
        });
    }

    [Test]
    public void KeepRowsGroupByOwnerRealmThenLevelWithUnclaimedLast()
    {
        HeraldSnapshot snapshot = HeraldData.Load(BuildSave(counters: false));

        IReadOnlyList<HeraldKeepRow> keeps = HeraldData.KeepRows(snapshot);

        Assert.That(keeps.Select(k => k.Name), Is.EqualTo(new[] { "Dun Alpha", "Keep Mid", "Neutral Keep" }));
        Assert.That(keeps.Select(k => k.Owner), Is.EqualTo(new[] { "Albion", "Midgard", "Unclaimed" }));
        Assert.That(keeps[0].ClaimedBy, Is.EqualTo("Clan Alpha"));
        Assert.That(keeps[1].ClaimedBy, Is.EqualTo("—"));
    }

    [Test]
    public void SnapshotSerializesToPlainJsonAndRoundTrips()
    {
        HeraldSnapshot snapshot = HeraldData.Load(BuildSave(counters: true));

        string json = JsonSerializer.Serialize(snapshot);
        HeraldSnapshot copy = JsonSerializer.Deserialize<HeraldSnapshot>(json)!;

        Assert.That(copy, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"Kind\":\"Gamebot\""));
            Assert.That(copy!.Characters, Has.Count.EqualTo(snapshot.Characters.Count));
            Assert.That(copy.Characters.Single(c => c.Name == "Bot Albion").Combat.Kills, Is.EqualTo(12));
            Assert.That(copy.Keeps.Single(k => k.KeepId == 5).ClaimedGuildName, Is.EqualTo("Clan Alpha"));
            Assert.That(copy.Coverage, Is.EqualTo(snapshot.Coverage));
        });
    }

    private string BuildSave(bool counters) => BuildSaveAt(_directory, counters);

    /// <summary>Writes the shared Herald fixture save into <paramref name="directory"/> (also used by the presentation tests).</summary>
    internal static string BuildSaveAt(string directory, bool counters)
    {
        string path = Path.Combine(directory, "opendaoc.sqlite3.db");
        using var connection = new SQLiteConnection($"Data Source={path};Version=3;Pooling=False;");
        connection.Open();
        Execute(connection, SchemaSql + SeedSql);
        if (counters)
            Execute(connection, CounterSql);
        return path;
    }

    private string BuildTitleSave()
    {
        string path = Path.Combine(_directory, "titles.sqlite3.db");
        using var connection = new SQLiteConnection($"Data Source={path};Version=3;Pooling=False;");
        connection.Open();
        Execute(connection, SchemaSql);
        var players = new List<string>();
        void Add(string id, int realm, int albion = 0, int midgard = 0, int hibernia = 0, int deathBlows = 0, int solo = 0, int keeps = 0, int towers = 0)
        {
            players.Add($"('{id}','{id}','acc-{id}',50,6,{realm},'',0,0,{albion},{midgard},{hibernia},{deathBlows},0,0,{solo},0,0,0,{keeps},{towers},0,0)");
        }

        Add("T2000", 1, albion: 2000);
        Add("T1999", 1, albion: 1999);
        Add("T24999", 1, albion: 24999);
        Add("T25000", 1, albion: 25000);
        Add("T100000", 1, albion: 100000);
        Add("MIX", 2, albion: 1000, midgard: 1000);
        Add("DB2000", 2, deathBlows: 2000);
        Add("SOLO2000", 3, solo: 2000);
        Add("K9", 1, keeps: 9);
        Add("K10", 1, keeps: 10);
        Add("K49", 1, keeps: 49);
        Add("K50", 1, keeps: 50);
        Add("K499", 1, keeps: 499);
        Add("K500", 1, keeps: 500);
        Add("W99", 2, towers: 99);
        Add("W100", 2, towers: 100);
        Add("W999", 2, towers: 999);
        Add("W1000", 2, towers: 1000);
        Execute(connection, "INSERT INTO DOLCharacters (DOLCharacters_ID, Name, AccountName, Level, Class, Realm, GuildID, RealmPoints, RealmLevel, " +
            "KillsAlbionPlayers, KillsMidgardPlayers, KillsHiberniaPlayers, KillsAlbionDeathBlows, KillsMidgardDeathBlows, KillsHiberniaDeathBlows, " +
            "KillsAlbionSolo, KillsMidgardSolo, KillsHiberniaSolo, DeathsPvP, CapturedKeeps, CapturedTowers, IgnoreStatistics, IsAnonymous) VALUES "
            + string.Join(",", players) + ";");
        return path;
    }

    private static void ExecuteOn(string path, string sql)
    {
        using var connection = new SQLiteConnection($"Data Source={path};Version=3;Pooling=False;");
        connection.Open();
        Execute(connection, sql);
    }

    private static void Execute(SQLiteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private const string SchemaSql = """
        CREATE TABLE DOLCharacters (
            DOLCharacters_ID TEXT PRIMARY KEY, Name TEXT, AccountName TEXT, Level INTEGER, Class INTEGER, Realm INTEGER,
            GuildID TEXT, RealmPoints INTEGER, RealmLevel INTEGER,
            KillsAlbionPlayers INTEGER, KillsMidgardPlayers INTEGER, KillsHiberniaPlayers INTEGER,
            KillsAlbionDeathBlows INTEGER, KillsMidgardDeathBlows INTEGER, KillsHiberniaDeathBlows INTEGER,
            KillsAlbionSolo INTEGER, KillsMidgardSolo INTEGER, KillsHiberniaSolo INTEGER,
            DeathsPvP INTEGER, CapturedKeeps INTEGER, CapturedTowers INTEGER, IgnoreStatistics INTEGER, IsAnonymous INTEGER);
        CREATE TABLE Account (Name TEXT PRIMARY KEY, PrivLevel INTEGER);
        CREATE TABLE offline_world_bots (
            BotId INTEGER PRIMARY KEY, Name TEXT, Realm INTEGER, ClassId INTEGER, ClassName TEXT, Level INTEGER,
            GuildId TEXT, RealmPoints INTEGER, PlayerType TEXT, IsRetired INTEGER);
        CREATE TABLE player_companions (
            CompanionId TEXT PRIMARY KEY, OwnerCharacterId TEXT, Name TEXT, Realm INTEGER, ClassId INTEGER, Level INTEGER,
            GuildId TEXT, RealmPoints INTEGER);
        CREATE TABLE Guild (GuildID TEXT PRIMARY KEY, GuildName TEXT, Realm INTEGER, RealmPoints INTEGER);
        CREATE TABLE Keep (KeepID INTEGER PRIMARY KEY, Name TEXT, Realm INTEGER, OriginalRealm INTEGER, Level INTEGER, ClaimedGuildName TEXT);
        CREATE TABLE Relic (RelicID INTEGER PRIMARY KEY, Realm INTEGER, OriginalRealm INTEGER, relicType INTEGER, KeepID INTEGER, LastCaptureDate DATETIME);
        """;

    private const string SeedSql = """
        INSERT INTO DOLCharacters (DOLCharacters_ID, Name, AccountName, Level, Class, Realm, GuildID, RealmPoints, RealmLevel,
            KillsAlbionPlayers, KillsMidgardPlayers, KillsHiberniaPlayers, KillsAlbionDeathBlows, KillsMidgardDeathBlows, KillsHiberniaDeathBlows,
            KillsAlbionSolo, KillsMidgardSolo, KillsHiberniaSolo, DeathsPvP, CapturedKeeps, CapturedTowers, IgnoreStatistics, IsAnonymous) VALUES
            ('char-alpha','Alpha','acc-alpha',50,6,1,'guild-a',95566,23, 25,165,46, 21,0,0, 7,0,0, 5,0,0, 0,0),
            ('char-bravo','Bravo','acc-bravo',40,22,2,'guild-b',30000,15, 0,2000,0, 0,500,0, 0,100,0, 9,0,0, 0,0),
            ('char-charlie','Charlie','acc-gm',50,44,3,'',1000000,49, 0,0,300, 0,0,40, 0,0,3, 1,0,0, 0,0),
            ('char-hidden','Hidden','acc-hidden',30,1,1,'guild-b',5000,5, 90,0,0, 0,0,0, 0,0,0, 0,0,0, 1,0),
            ('char-ghost','Ghost','acc-ghost',20,30,2,'',100,0, 0,1,0, 0,0,0, 0,0,0, 0,0,0, 0,1);
        INSERT INTO Account VALUES ('acc-alpha',1),('acc-bravo',1),('acc-gm',2),('acc-hidden',1),('acc-ghost',1);
        INSERT INTO offline_world_bots VALUES
            (1,'Bot Albion',1,3,'Scout',45,'guild-a',350,'Hunter',0),
            (2,'Bot Retired',1,6,'Cleric',50,'guild-a',999999,'Leveler',1),
            (3,'Bot Mid',2,25,'Hunter',20,'guild-b',25,'Casual',0);
        INSERT INTO player_companions VALUES
            ('comp-one','char-alpha','Comp One',1,1,30,'guild-a',0),
            ('comp-orphan','missing-owner','Comp Orphan',1,1,12,'',0);
        INSERT INTO Guild VALUES ('guild-a','Clan Alpha',1,1000),('guild-b','Mid Crew',2,2000),('guild-empty','Empty Guild',3,0);
        INSERT INTO Keep VALUES (5,'Dun Alpha',1,1,3,'Clan Alpha'),(6,'Keep Mid',2,3,2,''),(7,'Neutral Keep',0,2,1,'');
        INSERT INTO Relic VALUES (17,2,1,0,6,'2026-09-14 12:30:00'),(25,1,1,1,5,NULL);
        """;

    /// <summary>Adds the optional kill counters the server is recording for bots and companions.</summary>
    private const string CounterSql = """
        ALTER TABLE offline_world_bots ADD COLUMN KillsAlbionPlayers INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE offline_world_bots ADD COLUMN KillsMidgardPlayers INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE offline_world_bots ADD COLUMN KillsHiberniaPlayers INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE offline_world_bots ADD COLUMN KillsAlbionDeathBlows INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE offline_world_bots ADD COLUMN KillsMidgardDeathBlows INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE offline_world_bots ADD COLUMN KillsHiberniaDeathBlows INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE offline_world_bots ADD COLUMN KillsAlbionSolo INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE offline_world_bots ADD COLUMN KillsMidgardSolo INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE offline_world_bots ADD COLUMN KillsHiberniaSolo INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE offline_world_bots ADD COLUMN DeathsPvP INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE player_companions ADD COLUMN KillsAlbionPlayers INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE player_companions ADD COLUMN KillsMidgardPlayers INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE player_companions ADD COLUMN KillsHiberniaPlayers INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE player_companions ADD COLUMN KillsAlbionDeathBlows INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE player_companions ADD COLUMN KillsMidgardDeathBlows INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE player_companions ADD COLUMN KillsHiberniaDeathBlows INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE player_companions ADD COLUMN KillsAlbionSolo INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE player_companions ADD COLUMN KillsMidgardSolo INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE player_companions ADD COLUMN KillsHiberniaSolo INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE player_companions ADD COLUMN DeathsPvP INTEGER NOT NULL DEFAULT 0;
        UPDATE offline_world_bots SET KillsAlbionPlayers = 3, KillsMidgardPlayers = 4, KillsHiberniaPlayers = 5,
            KillsAlbionDeathBlows = 1, KillsMidgardDeathBlows = 2, KillsHiberniaDeathBlows = 3,
            KillsAlbionSolo = 0, KillsMidgardSolo = 1, KillsHiberniaSolo = 2, DeathsPvP = 6 WHERE BotId = 1;
        UPDATE offline_world_bots SET KillsMidgardPlayers = 10 WHERE BotId = 3;
        UPDATE player_companions SET KillsAlbionPlayers = 2, DeathsPvP = 1 WHERE CompanionId = 'comp-one';
        """;
}
