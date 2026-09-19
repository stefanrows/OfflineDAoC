using System.Data.SQLite;
using NUnit.Framework;
using OfflineDaoc.Launcher;

[TestFixture]
public class CamlannWorldResetTests
{
    private string _folder = null!;
    private string _database = null!;
    private string _eventRecords = null!;

    [SetUp]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "camlann-reset-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _database = Path.Combine(_folder, "world.db");
        _eventRecords = Path.Combine(_folder, "realm-event-records.sqlite3");

        Sql("""
            CREATE TABLE Account(Name TEXT PRIMARY KEY, Password TEXT);
            INSERT INTO Account VALUES ('offline','secret'),('old-account','old');
            CREATE TABLE DOLCharacters(DOLCharacters_ID TEXT PRIMARY KEY,AccountName TEXT);
            INSERT INTO DOLCharacters VALUES ('character-1','offline'),('character-2','old-account');
            CREATE TABLE DOLCharactersBackup(DOLCharacters_ID TEXT PRIMARY KEY,AccountName TEXT);
            INSERT INTO DOLCharactersBackup VALUES ('backup-character-1','offline');
            CREATE TABLE Inventory(Inventory_ID INTEGER PRIMARY KEY,OwnerID TEXT);
            INSERT INTO Inventory VALUES (1,'character-1'),(2,'offlinebot:41'),(3,'backup-character-1');
            CREATE TABLE offline_world_bots(BotId INTEGER PRIMARY KEY,Name TEXT);
            INSERT INTO offline_world_bots VALUES (41,'Old bot');
            CREATE TABLE bot_profiles(BotId INTEGER PRIMARY KEY,Name TEXT);
            INSERT INTO bot_profiles VALUES (41,'Old bot');
            CREATE TABLE bot_settings(BotId INTEGER PRIMARY KEY);
            INSERT INTO bot_settings VALUES (41);
            CREATE TABLE offline_bot_commands(CommandId INTEGER PRIMARY KEY,BotId INTEGER);
            INSERT INTO offline_bot_commands VALUES (1,41);
            CREATE TABLE offline_auction_listings(ListingId INTEGER PRIMARY KEY,SellerBotId INTEGER);
            INSERT INTO offline_auction_listings VALUES (1,41);
            CREATE TABLE offline_auction_ledger(LedgerId INTEGER PRIMARY KEY,SellerBotId INTEGER);
            INSERT INTO offline_auction_ledger VALUES (1,41);
            CREATE TABLE offline_auction_escrow(EscrowId INTEGER PRIMARY KEY,BidderBotId INTEGER);
            INSERT INTO offline_auction_escrow VALUES (1,41);
            CREATE TABLE realm_exchange_sales(SlotId INTEGER PRIMARY KEY);
            INSERT INTO realm_exchange_sales VALUES (1);
            CREATE TABLE AccountXCrafting(AccountId TEXT);
            INSERT INTO AccountXCrafting VALUES ('offline');
            CREATE TABLE AccountXMoney(AccountId TEXT);
            INSERT INTO AccountXMoney VALUES ('offline');
            CREATE TABLE AccountXCustomParam(Name TEXT);
            INSERT INTO AccountXCustomParam VALUES ('offline');
            CREATE TABLE Guild(GuildID TEXT PRIMARY KEY,GuildName TEXT);
            INSERT INTO Guild VALUES ('dummy','DummyGuildToMakePetsUntargetable'),('old-guild','Old guild');
            CREATE TABLE GuildRank(GuildID TEXT);
            INSERT INTO GuildRank VALUES ('dummy'),('old-guild');
            CREATE TABLE GuildAlliance(GuildAlliance_ID INTEGER PRIMARY KEY);
            INSERT INTO GuildAlliance VALUES (1);
            CREATE TABLE Keep(KeepID INTEGER PRIMARY KEY,Realm INTEGER,ClaimedGuildName TEXT);
            INSERT INTO Keep VALUES (1,2,'Old guild'),(2,1,'');
            CREATE TABLE Relic(RelicID INTEGER PRIMARY KEY,OriginalRealm INTEGER,relicType INTEGER,Realm INTEGER,LastRealm INTEGER,LastCaptureDate TEXT,Region INTEGER,X INTEGER,Y INTEGER,Z INTEGER,Heading INTEGER);
            INSERT INTO Relic VALUES (1,1,0,1,1,NULL,999,1,1,1,1),(2,1,1,1,1,NULL,999,1,1,1,1),(3,2,0,2,2,NULL,999,1,1,1,1),(4,2,1,2,2,NULL,999,1,1,1,1),(5,3,0,3,3,NULL,999,1,1,1,1),(6,3,1,3,3,NULL,999,1,1,1,1);
            CREATE TABLE WorldObject(ClassType TEXT,Emblem INTEGER,Region INTEGER,X INTEGER,Y INTEGER,Z INTEGER,Heading INTEGER);
            INSERT INTO WorldObject VALUES ('DOL.GS.GameRelicPad',1,1,100,101,102,103),('DOL.GS.GameRelicPad',11,1,110,111,112,113),('DOL.GS.GameRelicPad',2,2,200,201,202,203),('DOL.GS.GameRelicPad',12,2,210,211,212,213),('DOL.GS.GameRelicPad',3,3,300,301,302,303),('DOL.GS.GameRelicPad',13,3,310,311,312,313);
            CREATE TABLE offline_local_options(Key TEXT PRIMARY KEY,Value TEXT NOT NULL);
            INSERT INTO offline_local_options VALUES ('MakeMeGM','false');
            """);

        using var events = new SQLiteConnection($"Data Source={_eventRecords};Pooling=False");
        events.Open();
        using var command = events.CreateCommand();
        command.CommandText = "CREATE TABLE Events(Id TEXT PRIMARY KEY,EventId TEXT,Name TEXT,Kind TEXT,Realm TEXT,StartedUtc TEXT,EndedUtc TEXT,Phase TEXT,Outcome TEXT,Details TEXT,Assigned INTEGER,Present INTEGER); INSERT INTO Events VALUES ('1','event','Old event','Keep','Albion','','','','Ended','old',1,1)";
        command.ExecuteNonQuery();
    }

    [TearDown]
    public void Cleanup()
    {
        SQLiteConnection.ClearAllPools();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    private void Sql(string sql)
    {
        using var connection = new SQLiteConnection($"Data Source={_database};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private object Scalar(string sql)
    {
        using var connection = new SQLiteConnection($"Data Source={_database};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private CamlannWorldReset.Result Apply(Func<bool> stopped = null)
    {
        return CamlannWorldReset.Apply(_database, Path.Combine(_folder, "backups"), "offline", _eventRecords, stopped ?? (() => true));
    }

    [Test]
    public void ResetCreatesFullBackupClearsProgressAndLeavesLocalAccountAndDummyGuild()
    {
        CamlannWorldReset.Result result = Apply();

        Assert.That(result.AlreadyApplied, Is.False);
        Assert.That(File.Exists(Path.Combine(result.Backup, Path.GetFileName(_database))), Is.True);
        Assert.That(File.Exists(Path.Combine(result.Backup, Path.GetFileName(_eventRecords))), Is.True);
        Assert.That(Scalar("SELECT COUNT(*) FROM DOLCharacters"), Is.EqualTo(0L));
        Assert.That(Scalar("SELECT COUNT(*) FROM Inventory"), Is.EqualTo(0L));
        Assert.That(Scalar("SELECT COUNT(*) FROM offline_world_bots"), Is.EqualTo(0L));
        Assert.That(Scalar("SELECT COUNT(*) FROM Account WHERE Name='offline'"), Is.EqualTo(1L));
        Assert.That(Scalar("SELECT COUNT(*) FROM Account WHERE Name='old-account'"), Is.EqualTo(0L));
        Assert.That(Scalar("SELECT COUNT(*) FROM Guild"), Is.EqualTo(1L));
        Assert.That(Scalar("SELECT COUNT(*) FROM Guild WHERE GuildName='DummyGuildToMakePetsUntargetable'"), Is.EqualTo(1L));
        Assert.That(Scalar("SELECT COUNT(*) FROM [Keep] WHERE Realm<>0 OR ClaimedGuildName<>''"), Is.EqualTo(0L));
        Assert.That(Scalar("SELECT COUNT(*) FROM Relic WHERE Region=999"), Is.EqualTo(0L));
        Assert.That(Scalar("SELECT Value FROM offline_local_options WHERE Key='WorldModel'"), Is.EqualTo("Camlann-1"));

        using var events = new SQLiteConnection($"Data Source={_eventRecords};Pooling=False");
        events.Open();
        using var eventCount = events.CreateCommand();
        eventCount.CommandText = "SELECT COUNT(*) FROM Events";
        Assert.That(eventCount.ExecuteScalar(), Is.EqualTo(0L));
    }

    [Test]
    public void SecondRunIsANoOp()
    {
        Apply();
        CamlannWorldReset.Result result = Apply();

        Assert.That(result.AlreadyApplied, Is.True);
        Assert.That(result.Backup, Is.Empty);
    }

    [Test]
    public void RunningServerIsRefusedBeforeBackupOrMutation()
    {
        Assert.Throws<InvalidOperationException>(() => Apply(() => false));
        Assert.That(CamlannWorldReset.ReadWorldModel(_database), Is.Null);
        Assert.That(Scalar("SELECT COUNT(*) FROM DOLCharacters"), Is.EqualTo(2L));
        Assert.That(Directory.Exists(Path.Combine(_folder, "backups")), Is.False);
    }

    [Test]
    public void InvalidRelicShrineRollsBackTheTransaction()
    {
        Sql("DELETE FROM WorldObject WHERE Emblem=13");

        Assert.Throws<InvalidOperationException>(() => Apply());
        Assert.That(CamlannWorldReset.ReadWorldModel(_database), Is.Null);
        Assert.That(Scalar("SELECT COUNT(*) FROM DOLCharacters"), Is.EqualTo(2L));
        Assert.That(Scalar("SELECT COUNT(*) FROM [Keep] WHERE Realm<>0"), Is.EqualTo(2L));
    }

    [Test]
    public void ServerConfigIsPinnedToPvPWithoutChangingLineEndings()
    {
        string path = Path.Combine(_folder, "serverconfig.xml");
        File.WriteAllText(path, "<Server>\r\n  <GameType>Normal</GameType>\r\n</Server>\r\n");

        Assert.That(CamlannServerConfig.EnsurePvP(path), Is.True);
        string updated = File.ReadAllText(path);
        Assert.That(updated, Does.Contain("<GameType>PvP</GameType>"));
        Assert.That(updated.Count(character => character == '\r'), Is.EqualTo(3));
        Assert.That(updated.Count(character => character == '\n'), Is.EqualTo(3));
    }
}
