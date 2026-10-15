using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using DOL.Database;
using DOL.Database.Handlers;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// PvP kill and death counters on autonomous bots and persistent companions:
/// the counting rules, the record round trip, the legacy schema migration, and
/// the attacker filter that decides whether a death counts as PvP.
/// </summary>
[TestFixture, NonParallelizable]
public class UT_BotPvpKillStats
{
    private sealed class PvpStatsServer : GameServer
    {
        protected override IServerRules ServerRulesImpl => new NormalServerRules();
        protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
    }

    private static readonly IObjectDatabase EmptyDatabase =
        DispatchProxy.Create<IObjectDatabase, EmptyReadDatabase>();

    public class EmptyReadDatabase : DispatchProxy
    {
        protected override object Invoke(MethodInfo method, object[] args)
        {
            Type result = method.ReturnType;
            if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(result.GetGenericArguments()[0]));
            return null;
        }
    }

    private static readonly string[] KillColumns =
    {
        "KillsAlbionPlayers", "KillsMidgardPlayers", "KillsHiberniaPlayers",
        "KillsAlbionDeathBlows", "KillsMidgardDeathBlows", "KillsHiberniaDeathBlows",
        "KillsAlbionSolo", "KillsMidgardSolo", "KillsHiberniaSolo", "DeathsPvP",
    };

    private GameServer _previousServer;

    [SetUp]
    public void SetUp()
    {
        _previousServer = GameServer.Instance;
        GameServer.LoadTestDouble((PvpStatsServer)RuntimeHelpers.GetUninitializedObject(typeof(PvpStatsServer)));
    }

    [TearDown]
    public void TearDown() => GameServer.LoadTestDouble(_previousServer);

    private sealed class PvpBot : GameBot
    {
        private PvpBot() : base((OfflineWorldBotRecord)null) { }
    }

    private static void SetField(Type type, object target, string name, object value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    // A bare test bot has no movement component, and GameNPC reads X, Y and Z from it.
    // This gives the bot one whose owner position is the requested point.
    private static void PlaceAt(GameNPC npc, int x, int y)
    {
        var movement = (NpcMovementComponent)RuntimeHelpers.GetUninitializedObject(typeof(NpcMovementComponent));
        SetField(typeof(MovementComponent), movement, "_ownerPosition", new System.Numerics.Vector3(x, y, 0));
        typeof(GameNPC).GetField(nameof(GameNPC.movementComponent))!.SetValue(npc, movement);
    }

    // Builds a bot without running its constructor, then sets only the state these
    // tests read. The counter gate and stats are normally created by field initializers.
    private static PvpBot Bot(bool autonomous, bool temporary = false, bool encounter = false, bool companion = false)
    {
        PvpBot bot = (PvpBot)RuntimeHelpers.GetUninitializedObject(typeof(PvpBot));
        SetField(typeof(GameLiving), bot, "<TempProperties>k__BackingField", new PropertyCollection());
        SetField(typeof(GameNPC), bot, "m_brains", new ArrayList());
        SetField(typeof(GameBot), bot, "_pvpKillStatsGate", new object());
        SetField(typeof(GameBot), bot, "_pvpKillStats", new BotPvpKillStats());
        typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot))!.SetValue(bot, autonomous);
        typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper))!.SetValue(bot, temporary);
        typeof(GameBot).GetProperty(nameof(GameBot.IsBattlegroundEncounterBot))!.SetValue(bot, encounter);
        if (companion)
        {
            SetField(typeof(GameBot), bot, "<PlayerCompanionRecord>k__BackingField",
                new PlayerCompanionRecord { CompanionId = string.Empty });
        }
        bot.ObjectState = GameObject.eObjectState.Active;
        PlaceAt(bot, 0, 0);
        return bot;
    }

    // Order: Albion/Midgard/Hibernia players, then death blows, solo kills, deaths.
    private static int[] Counts(OfflineWorldBotRecord record) => new[]
    {
        record.KillsAlbionPlayers, record.KillsMidgardPlayers, record.KillsHiberniaPlayers,
        record.KillsAlbionDeathBlows, record.KillsMidgardDeathBlows, record.KillsHiberniaDeathBlows,
        record.KillsAlbionSolo, record.KillsMidgardSolo, record.KillsHiberniaSolo,
        record.DeathsPvP,
    };

    private static int[] Counts(PlayerCompanionRecord record) => new[]
    {
        record.KillsAlbionPlayers, record.KillsMidgardPlayers, record.KillsHiberniaPlayers,
        record.KillsAlbionDeathBlows, record.KillsMidgardDeathBlows, record.KillsHiberniaDeathBlows,
        record.KillsAlbionSolo, record.KillsMidgardSolo, record.KillsHiberniaSolo,
        record.DeathsPvP,
    };

    private static int[] Counts(BotPvpKillStats stats)
    {
        var record = new OfflineWorldBotRecord();
        stats.CopyTo(record);
        return Counts(record);
    }

    private static int[] Counts(GameBot bot)
    {
        var record = new OfflineWorldBotRecord();
        bot.CopyPvpKillStatsTo(record);
        return Counts(record);
    }

    [TestCase(eRealm.Albion, false, false, new[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]
    [TestCase(eRealm.Midgard, true, false, new[] { 0, 1, 0, 0, 1, 0, 0, 0, 0, 0 })]
    [TestCase(eRealm.Hibernia, true, true, new[] { 0, 0, 1, 0, 0, 1, 0, 0, 1, 0 })]
    [TestCase(eRealm.None, true, true, new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]
    public void RecordKillCreditsTheVictimRealmAndTheSameFlagsAsPlayers(
        eRealm victimRealm, bool deathBlow, bool soloKill, int[] expected)
    {
        var stats = new BotPvpKillStats();

        stats.RecordKill(victimRealm, deathBlow, soloKill);

        Assert.That(Counts(stats), Is.EqualTo(expected),
            "a kill adds one player kill for the victim's realm, plus a death blow and a solo kill when flagged; other realms add nothing");
    }

    [TestCase(1.0, 1, true)]
    [TestCase(1.0, 2, false)]
    [TestCase(0.99, 1, false)]
    [TestCase(0.5, 1, false)]
    public void SoloKillNeedsOneContributorWhoDealtAllTheDamage(double damagePercent, int contributors, bool expected)
    {
        Assert.That(BotPvpKillStats.IsSoloKill(damagePercent, contributors), Is.EqualTo(expected));
    }

    [Test]
    public void RecordDeathCountsOnlyDeathsPvP()
    {
        var stats = new BotPvpKillStats();

        stats.RecordDeath();

        Assert.That(Counts(stats), Is.EqualTo(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 }));
    }

    [Test]
    public void SavedCountersRoundTripThroughBothRecordTypes()
    {
        int[] saved = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var autonomousSaved = new OfflineWorldBotRecord
        {
            KillsAlbionPlayers = 1, KillsMidgardPlayers = 2, KillsHiberniaPlayers = 3,
            KillsAlbionDeathBlows = 4, KillsMidgardDeathBlows = 5, KillsHiberniaDeathBlows = 6,
            KillsAlbionSolo = 7, KillsMidgardSolo = 8, KillsHiberniaSolo = 9, DeathsPvP = 10,
        };
        var companionSaved = new PlayerCompanionRecord
        {
            KillsAlbionPlayers = 1, KillsMidgardPlayers = 2, KillsHiberniaPlayers = 3,
            KillsAlbionDeathBlows = 4, KillsMidgardDeathBlows = 5, KillsHiberniaDeathBlows = 6,
            KillsAlbionSolo = 7, KillsMidgardSolo = 8, KillsHiberniaSolo = 9, DeathsPvP = 10,
        };

        var botStats = new BotPvpKillStats();
        botStats.LoadFrom(autonomousSaved);
        var botCopy = new OfflineWorldBotRecord();
        botStats.CopyTo(botCopy);

        var companionStats = new BotPvpKillStats();
        companionStats.LoadFrom(companionSaved);
        var companionCopy = new PlayerCompanionRecord();
        companionStats.CopyTo(companionCopy);

        Assert.That(Counts(botCopy), Is.EqualTo(saved));
        Assert.That(Counts(companionCopy), Is.EqualTo(saved));
    }

    [Test]
    public void LegacyRecordsLoadAsZeroAndNegativeSavedValuesClamp()
    {
        var stats = new BotPvpKillStats();
        stats.LoadFrom(new OfflineWorldBotRecord());
        Assert.That(Counts(stats), Has.All.EqualTo(0), "a record saved before these columns existed");

        stats.LoadFrom(new PlayerCompanionRecord { KillsAlbionPlayers = -4, DeathsPvP = -1 });
        Assert.That(Counts(stats), Has.All.EqualTo(0), "negative saved values clamp to zero, as realm points do");
    }

    [Test]
    public void OnlyAutonomousBotsAndPersistentCompanionsTrackAndCreditKills()
    {
        PvpBot autonomous = Bot(autonomous: true);
        PvpBot companion = Bot(autonomous: false, companion: true);
        PvpBot temporaryHelper = Bot(autonomous: false, temporary: true);
        PvpBot encounterBot = Bot(autonomous: false, encounter: true);
        PvpBot ordinaryBot = Bot(autonomous: false);

        Assert.That(new[]
        {
            autonomous.TracksPvpKillStats, companion.TracksPvpKillStats, temporaryHelper.TracksPvpKillStats,
            encounterBot.TracksPvpKillStats, ordinaryBot.TracksPvpKillStats,
        }, Is.EqualTo(new[] { true, true, false, false, false }));

        foreach (PvpBot bot in new[] { autonomous, companion, temporaryHelper, encounterBot, ordinaryBot })
        {
            bot.CreditPvpKill(eRealm.Albion, deathBlow: true, soloKill: true);
            bot.CountPvpDeath();
        }

        int[] expected = { 1, 0, 0, 1, 0, 0, 1, 0, 0, 1 };
        Assert.That(Counts(autonomous), Is.EqualTo(expected));
        Assert.That(Counts(companion), Is.EqualTo(expected));
        Assert.That(Counts(temporaryHelper), Has.All.EqualTo(0), "/spawn helpers never save, so they keep no counters");
        Assert.That(Counts(encounterBot), Has.All.EqualTo(0), "battleground encounter bots never save");
        Assert.That(Counts(ordinaryBot), Has.All.EqualTo(0));
    }

    [Test]
    public void CreditingAnAutonomousBotMarksItsStateDirtySoTheNextSaveWritesTheCounters()
    {
        PvpBot autonomous = Bot(autonomous: true);
        Assert.That(autonomous.AutonomousStateDirty, Is.False);

        autonomous.CreditPvpKill(eRealm.Hibernia, deathBlow: false, soloKill: false);

        Assert.That(autonomous.AutonomousStateDirty, Is.True);
        Assert.That(Counts(autonomous)[2], Is.EqualTo(1));
    }

    [Test]
    public void ConcurrentCreditsOnOneCompanionAreNotLost()
    {
        PvpBot companion = Bot(autonomous: false, companion: true);

        Parallel.For(0, 8, _ =>
        {
            for (int i = 0; i < 500; i++)
                companion.CreditPvpKill(eRealm.Hibernia, deathBlow: i % 2 == 0, soloKill: false);
        });

        int[] counts = Counts(companion);
        Assert.That(counts[2], Is.EqualTo(4000), "every concurrent player kill is counted");
        Assert.That(counts[5], Is.EqualTo(2000), "every concurrent death blow is counted");
    }

    [Test]
    public void DeathCountsOnlyWhenACreditedAttackerIsActiveAndInRange()
    {
        PvpBot victim = Bot(autonomous: true);
        PvpBot companionAttacker = Bot(autonomous: false, companion: true);
        PvpBot autonomousAttacker = Bot(autonomous: true);
        PvpBot temporaryHelper = Bot(autonomous: false, temporary: true);
        PvpBot encounterBot = Bot(autonomous: false, encounter: true);
        PvpBot ordinaryBot = Bot(autonomous: false);

        Assert.That(BotPvpKillStats.HasCreditedPvpAttacker(victim, Array.Empty<GameLiving>()), Is.False,
            "no attackers means no PvP death");
        Assert.That(BotPvpKillStats.HasCreditedPvpAttacker(victim, new GameLiving[] { companionAttacker }), Is.True);
        Assert.That(BotPvpKillStats.HasCreditedPvpAttacker(victim, new GameLiving[] { autonomousAttacker }), Is.True);
        Assert.That(BotPvpKillStats.HasCreditedPvpAttacker(victim,
            new GameLiving[] { temporaryHelper, encounterBot, ordinaryBot }), Is.False,
            "/spawn helpers, encounter bots and ordinary bots are not credited PvP attackers");

        companionAttacker.ObjectState = GameObject.eObjectState.Inactive;
        Assert.That(BotPvpKillStats.HasCreditedPvpAttacker(victim, new GameLiving[] { companionAttacker }), Is.False,
            "an attacker that is no longer active earns no credit");
        companionAttacker.ObjectState = GameObject.eObjectState.Active;

        PlaceAt(companionAttacker, 100_000, 0);
        Assert.That(BotPvpKillStats.HasCreditedPvpAttacker(victim, new GameLiving[] { companionAttacker }), Is.False,
            "an attacker beyond kill range earns no credit");
    }

    [Test]
    public void CountPvpDeathOnlyCountsTrackedBots()
    {
        PvpBot autonomous = Bot(autonomous: true);
        PvpBot companion = Bot(autonomous: false, companion: true);
        PvpBot temporaryHelper = Bot(autonomous: false, temporary: true);

        autonomous.CountPvpDeath();
        companion.CountPvpDeath();
        temporaryHelper.CountPvpDeath();

        Assert.That(Counts(autonomous)[9], Is.EqualTo(1));
        Assert.That(Counts(companion)[9], Is.EqualTo(1));
        Assert.That(Counts(temporaryHelper)[9], Is.Zero);
    }

    [Test]
    public void LegacyCompanionTableKeepsItsRowsAndGainsZeroKillCounters()
    {
        string path = TemporaryPath();
        try
        {
            // The table as it was before the kill columns existed.
            new SqliteObjectDatabase(ConnectionString(path)).RegisterDataObject(typeof(PlayerCompanionRecord));
            DropKillColumns(path, "player_companions");
            ExecuteSql(path,
                "INSERT INTO `player_companions` (`CompanionId`, `Name`, `Level`, `RealmPoints`, `Experience`) " +
                "VALUES ('legacy-companion', 'Old Companion', 12, 345, 9001)");

            SqliteObjectDatabase migrated = new(ConnectionString(path));
            migrated.RegisterDataObject(typeof(PlayerCompanionRecord));
            PlayerCompanionRecord record = migrated.SelectObjects<PlayerCompanionRecord>(
                DB.Column(nameof(PlayerCompanionRecord.CompanionId)).IsEqualTo("legacy-companion")).Single();

            Assert.That(record.Name, Is.EqualTo("Old Companion"));
            Assert.That(record.Level, Is.EqualTo(12));
            Assert.That(record.RealmPoints, Is.EqualTo(345));
            Assert.That(record.Experience, Is.EqualTo(9001));
            Assert.That(Counts(record), Has.All.EqualTo(0), "the new columns default to zero");

            record.KillsMidgardSolo = 2;
            record.DeathsPvP = 5;
            record.Dirty = true;
            Assert.That(migrated.SaveObject(record), Is.True);

            SqliteObjectDatabase restarted = new(ConnectionString(path));
            restarted.RegisterDataObject(typeof(PlayerCompanionRecord));
            PlayerCompanionRecord reloaded = restarted.SelectObjects<PlayerCompanionRecord>(
                DB.Column(nameof(PlayerCompanionRecord.CompanionId)).IsEqualTo("legacy-companion")).Single();

            Assert.That(reloaded.KillsMidgardSolo, Is.EqualTo(2));
            Assert.That(reloaded.DeathsPvP, Is.EqualTo(5));
            Assert.That(reloaded.RealmPoints, Is.EqualTo(345), "earlier columns survive the later start");
        }
        finally
        {
            DeleteTemporaryFile(path);
        }
    }

    [Test]
    public void LegacyAutonomousBotTableKeepsItsRowsAndGainsZeroKillCounters()
    {
        string path = TemporaryPath();
        try
        {
            new SqliteObjectDatabase(ConnectionString(path)).RegisterDataObject(typeof(OfflineWorldBotRecord));
            DropKillColumns(path, "offline_world_bots");
            ExecuteSql(path,
                "INSERT INTO `offline_world_bots` (`Name`, `Realm`, `Level`, `RealmPoints`) " +
                "VALUES ('Old Bot', 2, 30, 900)");

            SqliteObjectDatabase migrated = new(ConnectionString(path));
            migrated.RegisterDataObject(typeof(OfflineWorldBotRecord));
            OfflineWorldBotRecord record = migrated.SelectObjects<OfflineWorldBotRecord>(
                DB.Column(nameof(OfflineWorldBotRecord.Name)).IsEqualTo("Old Bot")).Single();

            Assert.That(record.Level, Is.EqualTo(30));
            Assert.That(record.RealmPoints, Is.EqualTo(900));
            Assert.That(Counts(record), Has.All.EqualTo(0), "the new columns default to zero");
        }
        finally
        {
            DeleteTemporaryFile(path);
        }
    }

    private static string ConnectionString(string path) => $"Data Source={path};Version=3;Pooling=False;";

    private static void DropKillColumns(string path, string table)
    {
        using var connection = new SQLiteConnection(ConnectionString(path));
        connection.Open();
        foreach (string column in KillColumns)
        {
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = $"ALTER TABLE `{table}` DROP COLUMN `{column}`";
            command.ExecuteNonQuery();
        }
    }

    private static void ExecuteSql(string path, string sql)
    {
        using var connection = new SQLiteConnection(ConnectionString(path));
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string TemporaryPath() => Path.Combine(Path.GetTempPath(),
        "daoc-bot-pvp-" + Guid.NewGuid().ToString("N") + ".sqlite3");

    private static void DeleteTemporaryFile(string path)
    {
        // Removes only the GUID-named SQLite file this test created.
        if (File.Exists(path))
            File.Delete(path);
    }
}
