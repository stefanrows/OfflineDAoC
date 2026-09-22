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
    public void NormalRuntimeIsNotRejectedByCamlannPolicy()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, "<Server><GameType>Normal</GameType></Server>");

        Assert.That(ImportEngine.IsCamlannRuntime(_folder), Is.False);
    }
}
