using System.Data.SQLite;
using NUnit.Framework;
using OfflineDaoc.ProgressImport;

[TestFixture]
public sealed class ImportPolicyTests
{
    private string _folder = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), "offline-daoc-import-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_folder, "runtime", "data"));
        using var database = new SQLiteConnection($"Data Source={DatabasePath};Pooling=False");
        database.Open();
    }

    [TearDown]
    public void TearDown()
    {
        SQLiteConnection.ClearAllPools();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private string DatabasePath => Path.Combine(_folder, "runtime", "data", "opendaoc.sqlite3.db");
    private string ConfigPath => Path.Combine(_folder, "runtime", "config", "serverconfig.xml");

    [Test]
    public void PvpConfigIsRecognizedAsCamlann()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, "<Server><GameType>PvP</GameType></Server>");

        Assert.That(ImportEngine.IsCamlannRuntime(_folder), Is.True);
        Assert.That(() => ImportEngine.Inspect(_folder), Throws.TypeOf<InvalidOperationException>()
            .With.Message.Contains("Progress import is disabled"));
    }

    [Test]
    public void AnRvrSaveIsAForkWorldAndTheMessageNamesIt()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, "<Server><GameType>Normal</GameType></Server>");
        Mark(DatabasePath, "Realm-1");

        Assert.That(ImportEngine.IsCamlannRuntime(_folder), Is.True);
        Assert.That(ImportEngine.WorldOf(_folder), Is.EqualTo(OfflineDaoc.Configuration.WorldSaveKind.Realm));
        Assert.That(() => ImportEngine.Inspect(_folder), Throws.TypeOf<InvalidOperationException>()
            .With.Message.Contains("Progress import is disabled for the RvR world (Realm vs Realm)"));
    }

    [Test]
    public void ARuntimeWithOnlyTheCamlannSaveIsFoundAndRefused()
    {
        File.Delete(DatabasePath);
        Mark(CamlannPath, "Camlann-1");

        Assert.That(ImportEngine.LocateRuntime(_folder), Is.EqualTo(Path.Combine(_folder, "runtime")));
        Assert.That(ImportEngine.WorldOf(_folder), Is.EqualTo(OfflineDaoc.Configuration.WorldSaveKind.Camlann));
        Assert.That(() => ImportEngine.Inspect(_folder), Throws.TypeOf<InvalidOperationException>()
            .With.Message.Contains("PvP world (Camlann)"));
    }

    [Test]
    public void TheConfiguredSaveNamesTheWorldWhenBothExist()
    {
        Mark(DatabasePath, "Realm-1");
        Mark(CamlannPath, "Camlann-1");
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, @"<Server><GameType>PvP</GameType><DBConnectionString>Data Source=..\data\opendaoc-camlann.sqlite3.db;Version=3</DBConnectionString></Server>");

        Assert.That(ImportEngine.WorldOf(_folder), Is.EqualTo(OfflineDaoc.Configuration.WorldSaveKind.Camlann));
        File.WriteAllText(ConfigPath, @"<Server><GameType>PvP</GameType><DBConnectionString>Data Source=..\data\opendaoc.sqlite3.db</DBConnectionString></Server>");
        Assert.That(ImportEngine.WorldOf(_folder), Is.EqualTo(OfflineDaoc.Configuration.WorldSaveKind.Realm));
    }

    [TestCase("Realm-1", "Camlann-1", "A save of the RvR world (Realm vs Realm) cannot be imported into the PvP world (Camlann)")]
    [TestCase("Camlann-1", "Realm-1", "A save of the PvP world (Camlann) cannot be imported into the RvR world (Realm vs Realm)")]
    public void ASaveOfOneWorldIsNeverImportedIntoTheOther(string oldMarker, string newMarker, string message)
    {
        string other = _folder + "-new";
        try
        {
            Directory.CreateDirectory(Path.Combine(other, "runtime", "data"));
            Mark(DatabasePath, oldMarker);
            Mark(Path.Combine(other, "runtime", "data", "opendaoc.sqlite3.db"), newMarker);

            Assert.That(() => ImportEngine.Import(_folder, other, _ => { }), Throws.TypeOf<InvalidOperationException>()
                .With.Message.EqualTo(message + ": each world keeps its own save."));
            Assert.That(Directory.Exists(Path.Combine(other, "runtime", "progress-backups")), Is.False, "nothing was changed");
        }
        finally
        {
            SQLiteConnection.ClearAllPools();
            if (Directory.Exists(other)) Directory.Delete(other, true);
        }
    }

    private string CamlannPath => Path.Combine(_folder, "runtime", "data", "opendaoc-camlann.sqlite3.db");

    private static void Mark(string database, string marker)
    {
        using var connection = new SQLiteConnection($"Data Source={database};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS offline_local_options (Key TEXT PRIMARY KEY, Value TEXT NOT NULL); " +
            "INSERT OR REPLACE INTO offline_local_options VALUES ('WorldModel', @marker)";
        command.Parameters.AddWithValue("@marker", marker);
        command.ExecuteNonQuery();
    }

    [Test]
    public void NormalRuntimeIsNotRejectedByCamlannPolicy()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, "<Server><GameType>Normal</GameType></Server>");

        Assert.That(ImportEngine.IsCamlannRuntime(_folder), Is.False);
    }
}
