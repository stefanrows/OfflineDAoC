using System.Data.SQLite;
using System.Reflection;
using NUnit.Framework;
using OfflineDaoc.Launcher;

[TestFixture]
public class KeepRelicResetTests
{
    private string _folder = null!, _database = null!;
    [SetUp] public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "keep-reset-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _database = Path.Combine(_folder, "world.db");
        Sql("CREATE TABLE [Keep](KeepID INTEGER PRIMARY KEY,Realm INT,OriginalRealm INT,ClaimedGuildName TEXT,ClaimedAt TEXT,Level INT);" +
            "INSERT INTO [Keep] VALUES(1,2,1,'Enemy guild','2026-01-01T00:00:00.0000000Z',5),(2,1,0,'BG guild','2026-01-01T00:00:00.0000000Z',3);" +
            "CREATE TABLE Relic(RelicID INT PRIMARY KEY,Realm INT,OriginalRealm INT,LastRealm INT,relicType INT,KeepID INT,Region INT,X INT,Y INT,Z INT,Heading INT);" +
            "CREATE TABLE WorldObject(ClassType TEXT,Emblem INT,Region INT,X INT,Y INT,Z INT,Heading INT);" +
            "CREATE TABLE PlayerProgress(Name TEXT,Coins INT,Inventory TEXT); INSERT INTO PlayerProgress VALUES('Player',12345,'Original gear');" +
            "CREATE TABLE offline_local_options(Key TEXT PRIMARY KEY,Value TEXT); INSERT INTO offline_local_options VALUES('MakeMeGM','false');");
        for (int realm = 1; realm <= 3; realm++) for (int type = 0; type <= 1; type++)
        {
            int emblem = realm + 10 * type;
            Sql($"INSERT INTO Relic VALUES({emblem},0,{realm},2,{type},42,999,5,5,5,5); INSERT INTO WorldObject VALUES('DOL.GS.GameRelicPad',{emblem},{realm},{emblem * 100},200,300,400);");
        }
    }
    [TearDown] public void Cleanup() { SQLiteConnection.ClearAllPools(); Directory.Delete(_folder, true); }
    private object Sql(string text)
    {
        using var c = new SQLiteConnection($"Data Source={_database};Pooling=False"); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = text; return cmd.ExecuteScalar();
    }
    private KeepRelicReset.Result Apply(Func<bool> stopped = null) => KeepRelicReset.Apply(_database, Path.Combine(_folder, "backups"), stopped ?? (() => true));

    [Test] public void ResetIsAtomicAndIdempotentAndPreservesUnrelatedData()
    {
        var result = Apply();
        Assert.That(result.Keeps, Is.EqualTo(2)); Assert.That(result.Relics, Is.EqualTo(6));
        Assert.That(File.ReadAllText(result.Backup), Does.Contain("Enemy guild"));
        Assert.That(Sql("SELECT count(*) FROM [Keep] WHERE Realm<>0 OR ClaimedGuildName<>'' OR ClaimedAt<>'0001-01-01T00:00:00.0000000Z'"), Is.EqualTo(0L));
        Assert.That(Sql("SELECT sum(Level) FROM [Keep]"), Is.EqualTo(8L));
        Assert.That(Sql("SELECT count(*) FROM Relic WHERE Realm<>OriginalRealm OR LastRealm<>OriginalRealm OR KeepID<>0 OR Region<>OriginalRealm OR X<>RelicID*100"), Is.EqualTo(0L));
        Assert.That(Sql("SELECT Coins FROM PlayerProgress"), Is.EqualTo(12345L));
        Assert.That(Sql("SELECT Inventory FROM PlayerProgress"), Is.EqualTo("Original gear"));
        Assert.That(Sql("SELECT Value FROM offline_local_options WHERE Key='MakeMeGM'"), Is.EqualTo("false"));
        Assert.That(KeepRelicReset.ReadResetUtc(_database), Is.EqualTo(result.UpdatedUtc));
        Apply();
        Assert.That(Sql("SELECT count(*) FROM Relic WHERE Realm=OriginalRealm"), Is.EqualTo(6L));
    }
    [Test] public void RunningServerCannotChangeAnything()
    {
        Assert.Throws<InvalidOperationException>(() => Apply(() => false));
        Assert.That(Sql("SELECT Realm FROM [Keep] WHERE KeepID=1"), Is.EqualTo(2L));
        Assert.That(Directory.Exists(Path.Combine(_folder,"backups")), Is.False);
    }
    [Test] public void ServerStartingBeforeCommitRollsBackBothTables()
    {
        int checks = 0;
        Assert.Throws<InvalidOperationException>(() => Apply(() => ++checks < 3));
        Assert.That(Sql("SELECT Realm FROM [Keep] WHERE KeepID=1"), Is.EqualTo(2L));
        Assert.That(Sql("SELECT count(*) FROM Relic WHERE Realm=0"), Is.EqualTo(6L));
        Assert.That(KeepRelicReset.ReadResetUtc(_database), Is.EqualTo(DateTime.MinValue));
    }
    [TestCase(false)] [TestCase(true)] public void MissingOrAmbiguousShrineRejectsTheEntireReset(bool duplicate)
    {
        Sql(duplicate ? "INSERT INTO WorldObject SELECT * FROM WorldObject WHERE Emblem=1" : "DELETE FROM WorldObject WHERE Emblem=1");
        Assert.Throws<InvalidOperationException>(() => Apply());
        Assert.That(Sql("SELECT Realm FROM [Keep] WHERE KeepID=1"), Is.EqualTo(2L));
    }
    [Test] public void DatabaseFailureRollsBackAlreadyUpdatedKeeps()
    {
        Sql("CREATE TRIGGER block_relic BEFORE UPDATE ON Relic BEGIN SELECT RAISE(ABORT,'simulated failure'); END");
        Assert.Throws<SQLiteException>(() => Apply());
        Assert.That(Sql("SELECT Realm FROM [Keep] WHERE KeepID=1"), Is.EqualTo(2L));
    }

    // The live reset button sits in the Realm Events panel (its actions row inside the controls row).
    [Test, Apartment(ApartmentState.STA)] public void ResetButtonFitsRealmEventsAtCompactAndLargeWidths()
    {
        var type = Assembly.Load("OfflineDAoC").GetType("OfflineDaoc.Launcher.MainForm")!;
        using var form = (Form)Activator.CreateInstance(type)!;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var button = (Button)type.GetField("_resetKeepsRelics", flags)!.GetValue(form)!;
        Control panel = button.Parent!.Parent!.Parent!;
        panel.Parent!.Controls.Remove(panel);
        panel.Dock = DockStyle.None;
        foreach (int width in new[] { 850, 1100, 1850 })
        {
            panel.Size = new System.Drawing.Size(width, 600); panel.CreateControl(); panel.PerformLayout();
            Assert.That(button.Text, Is.EqualTo("RESET ALL KEEPS && RELICS…"));
            Assert.That(button.AccessibleName, Is.EqualTo("Reset all keeps & relics"));
            Rectangle bounds = BoundsIn(panel, button);
            Assert.That(panel.ClientRectangle.Contains(bounds), Is.True, $"{width}: {bounds}");
            Assert.That(TextRenderer.MeasureText(button.Text,button.Font).Width, Is.LessThan(button.Width));
            Assert.That(button.Enabled, Is.False, "Must stay disabled until stopped status is established");
            using var bitmap = new System.Drawing.Bitmap(width,600);
            panel.DrawToBitmap(bitmap,panel.ClientRectangle);
            bitmap.Save(Path.Combine(TestContext.CurrentContext.WorkDirectory,$"realm-events-reset-button-{width}.png"));
        }
    }

    private static Rectangle BoundsIn(Control ancestor, Control descendant)
    {
        Rectangle bounds = descendant.Bounds;
        for (Control? parent = descendant.Parent; parent != null && parent != ancestor; parent = parent.Parent)
            bounds.Offset(parent.Location);
        return bounds;
    }
}
