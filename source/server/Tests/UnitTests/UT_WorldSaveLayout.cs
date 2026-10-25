using System;
using System.IO;
using DOL.GS;
using NUnit.Framework;
using OfflineDaoc.Configuration;

namespace DOL.UnitTests;

/// <summary>The per-world save and side-file names shared by the server and the launcher (docs/RVR_WORLDS.md).</summary>
[TestFixture]
public sealed class UT_WorldSaveLayout
{
    [Test]
    public void MarkersAndWorldNumbersMatchTheServersWorldModel()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WorldSaveLayout.RealmMarker, Is.EqualTo(CamlannWorldModel.RealmMarkerValue));
            Assert.That(WorldSaveLayout.CamlannMarker, Is.EqualTo(CamlannWorldModel.CamlannMarkerValue));
            Assert.That((int)WorldSaveKind.Realm, Is.EqualTo((int)WorldModelKind.Realm));
            Assert.That((int)WorldSaveKind.Camlann, Is.EqualTo((int)WorldModelKind.Camlann));
            foreach (WorldSaveKind world in WorldSaveLayout.Worlds)
            {
                Assert.That(CamlannWorldModel.ParseMarker(WorldSaveLayout.Marker(world)), Is.EqualTo((WorldModelKind)(int)world));
                Assert.That(WorldSaveLayout.FromMarker(WorldSaveLayout.Marker(world)), Is.EqualTo(world));
            }
            Assert.That(WorldSaveLayout.FromMarker("Normal"), Is.Null);
            Assert.That(WorldSaveLayout.FromMarker("realm-1"), Is.Null, "markers compare exactly, as the server does");
        });
    }

    [TestCase("opendaoc.sqlite3.db", "realm-event-records.sqlite3", "realm-event-records.sqlite3")]
    [TestCase("opendaoc.sqlite3.db", "open-bg-status.json", "open-bg-status.json")]
    [TestCase("opendaoc-camlann.sqlite3.db", "realm-event-records.sqlite3", "realm-event-records-camlann.sqlite3")]
    [TestCase("opendaoc-camlann.sqlite3.db", "open-bg-status.json", "open-bg-status-camlann.json")]
    [TestCase("opendaoc-camlann.sqlite3.db", "gothwaite-pvp.json", "gothwaite-pvp-camlann.json")]
    [TestCase("../data/OpenDAoC-Camlann.sqlite3.db", "bot-world.json", "bot-world-camlann.json")]
    [TestCase("save.sqlite3.db", "rvr-world.json", "rvr-world.json")]
    [TestCase("", "rvr-world.json", "rvr-world.json")]
    [TestCase(null, "rvr-world.json", "rvr-world.json")]
    public void SideFilesAreNamedAfterTheSaveFile(string database, string baseName, string expected) =>
        Assert.That(WorldSaveLayout.SideFileName(baseName, database), Is.EqualTo(expected));

    [Test]
    public void EachWorldHasItsOwnSaveFile()
    {
        Assert.That(WorldSaveLayout.DatabaseFileName(WorldSaveKind.Realm), Is.EqualTo("opendaoc.sqlite3.db"));
        Assert.That(WorldSaveLayout.DatabaseFileName(WorldSaveKind.Camlann), Is.EqualTo("opendaoc-camlann.sqlite3.db"));
        Assert.That(WorldSaveLayout.SideFileSuffix(WorldSaveLayout.DatabaseFileName(WorldSaveKind.Realm)), Is.Empty);
        Assert.That(WorldSaveLayout.SideFileSuffix(WorldSaveLayout.DatabaseFileName(WorldSaveKind.Camlann)), Is.EqualTo("-camlann"));
    }

    [Test]
    public void ConnectionStringDataSourceIsReadAndReplacedWithoutTouchingTheRest()
    {
        const string live = "Data Source=../data/opendaoc.sqlite3.db;Version=3;Pooling=False;Journal Mode=WAL;Synchronous=Normal;Foreign Keys=True;Default Timeout=60";

        Assert.That(WorldSaveLayout.DataSource(live), Is.EqualTo("../data/opendaoc.sqlite3.db"));
        Assert.That(WorldSaveLayout.WithDatabaseFileName(live, "opendaoc-camlann.sqlite3.db"),
            Is.EqualTo(live.Replace("opendaoc.sqlite3.db", "opendaoc-camlann.sqlite3.db")));
        Assert.That(WorldSaveLayout.WithDatabaseFileName("DataSource=C:\\x\\a.db;Version=3", "b.db"), Is.EqualTo("DataSource=C:\\x\\b.db;Version=3"));
        Assert.That(WorldSaveLayout.WithDatabaseFileName("Data Source=a.db", "b.db"), Is.EqualTo("Data Source=b.db"));
        Assert.That(WorldSaveLayout.DataSource("Version=3"), Is.Null);
        Assert.Throws<InvalidOperationException>(() => WorldSaveLayout.WithDatabaseFileName("Version=3", "b.db"));
        Assert.Throws<ArgumentException>(() => WorldSaveLayout.WithDatabaseFileName(live, "../evil.db"));
    }

    [Test]
    public void ServerSideFilesFollowTheConfiguredSave()
    {
        // Without a SQLite configuration (tools, most unit tests) the name is empty and the plain names apply.
        string database = WorldSaveFiles.DatabaseFileName;
        Assert.That(WorldSaveFiles.ServerFile(WorldSaveLayout.RealmEventRecordsFileName),
            Is.EqualTo(Path.Combine(AppContext.BaseDirectory, WorldSaveLayout.SideFileName(WorldSaveLayout.RealmEventRecordsFileName, database))));
        Assert.That(WorldSaveFiles.InDirectory("x", "gothwaite-pvp.json"),
            Is.EqualTo(Path.Combine("x", WorldSaveLayout.SideFileName("gothwaite-pvp.json", database))));
        Assert.That(WorldSaveFiles.DatabaseFileName, Does.Not.Contain("camlann"), "the test run never opens a Camlann save");
    }
}
