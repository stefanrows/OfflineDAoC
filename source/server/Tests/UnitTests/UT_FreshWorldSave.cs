using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.Database.Handlers;
using DOL.GS;
using DOL.GS.DataPacks;
using NUnit.Framework;
using OfflineDaoc.Configuration;

namespace DOL.UnitTests;

/// <summary>
/// The offline boot check of a save the launcher's world reset produced (docs/RVR_WORLDS.md), without starting a
/// server: on a copy, every DataTable is registered as GameServer.InitDB does, the world marker is validated as
/// ValidateWorldModel does, the shipped world data packs apply, and the bot roster plans its guilds for the world.
/// Set OFFLINE_DAOC_RESET_SAVES to the folder holding opendaoc.sqlite3.db and/or opendaoc-camlann.sqlite3.db
/// (the launcher test FullFreshWorldFromARealSaveCopy writes them there); otherwise the test is ignored.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_FreshWorldSave
{
    private string _root;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "offline-daoc-fresh-world-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        SQLiteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [TestCase(WorldSaveLayout.RealmDatabaseFileName, WorldModelKind.Realm, 45)]
    [TestCase(WorldSaveLayout.CamlannDatabaseFileName, WorldModelKind.Camlann, 15)]
    public void FreshWorldSaveBootsLogically(string fileName, WorldModelKind expectedWorld, int expectedGuilds)
    {
        string folder = Environment.GetEnvironmentVariable("OFFLINE_DAOC_RESET_SAVES");
        string source = string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, fileName);
        if (source == null || !File.Exists(source))
            Assert.Ignore($"No reset output {fileName}; set OFFLINE_DAOC_RESET_SAVES (docs/RVR_WORLDS.md).");

        string db = Path.Combine(_root, fileName);
        File.Copy(source, db);
        // The live install's connection options (serverconfig.xml).
        string connection = $"Data Source={db};Version=3;Pooling=False;Journal Mode=WAL;Synchronous=Normal;Foreign Keys=True;Default Timeout=60";
        var database = new SqliteObjectDatabase(connection);

        int tables = 0;
        foreach (Assembly assembly in new[] { typeof(GameServer).Assembly, typeof(DbKeep).Assembly }.Distinct())
            foreach (Type type in assembly.GetTypes().Where(type => type.IsClass && !type.IsAbstract &&
                                                                     type.GetCustomAttributes<DataTable>(false).Any()))
            {
                database.RegisterDataObject(type);
                tables++;
            }
        TestContext.Out.WriteLine($"{fileName}: {tables} DataTables registered");

        DbOfflineLocalOption marker = database.FindObjectByKey<DbOfflineLocalOption>(CamlannWorldModel.MarkerKey);
        Assert.That(CamlannWorldModel.Parse(EGameServerType.GST_PvP, marker), Is.EqualTo(expectedWorld), "the startup marker check accepts the save");
        Assert.That(CamlannWorldModel.Parse(EGameServerType.GST_Normal, marker), Is.EqualTo(WorldModelKind.Unsupported));

        WorldDataPackRun run = new WorldDataPackApplier(connection, ShippedPackDirectory(), WorldDataPacks.CacheWatches).ApplyPending();
        foreach (WorldDataPackResult result in run.Results.Where(result => result.AppliedNow))
            TestContext.Out.WriteLine($"{fileName}: pack {result.Name} applied now, {result.Rows} rows, {result.Elapsed.TotalSeconds:0.0} s");
        Assert.That(run.Results.Select(result => result.State), Is.All.EqualTo(WorldDataPackState.Applied), "every shipped pack is on the save after boot");
        Assert.That(new WorldDataPackApplier(connection, ShippedPackDirectory()).ApplyPending().AppliedCount, Is.Zero, "a second boot applies nothing");

        IList<OfflineWorldBotRecord> bots = database.SelectAllObjects<OfflineWorldBotRecord>();
        Assert.That(bots.Count(bot => !bot.IsRetired), Is.EqualTo(1800));
        Assert.That(bots.GroupBy(bot => bot.Realm).Select(group => group.Count()), Is.All.EqualTo(600));
        AutonomousGuildSeeding.GuildPool[] pools = AutonomousGuildSeeding.PlanPools(expectedWorld, bots.Select(bot => (eRealm)bot.Realm));
        Assert.That(pools.Sum(pool => pool.GuildCount), Is.EqualTo(expectedGuilds), "the crew manager seeds this many bot guilds at first start");

        IList<DbKeep> keeps = database.SelectAllObjects<DbKeep>();
        Assert.That(keeps.Where(keep => !string.IsNullOrEmpty(keep.ClaimedGuildName)), Is.Empty);
        if (expectedWorld == WorldModelKind.Realm)
            Assert.That(keeps.Where(keep => keep.Realm != 0).All(keep => keep.Realm == keep.OriginalRealm && (keep.BaseLevel >= 100 || keep.SkinType == 99)), Is.True);
        else
            Assert.That(keeps.Where(keep => keep.Realm != 0), Is.Empty);
        IList<DbRelic> relics = database.SelectAllObjects<DbRelic>();
        Assert.That(relics, Has.Count.EqualTo(6));
        Assert.That(relics.All(relic => relic.Realm == relic.OriginalRealm && relic.KeepID == 0), Is.True, "relics at home");
        Assert.That(database.SelectAllObjects<DbCoreCharacter>(), Is.Empty);
    }

    private static string ShippedPackDirectory()
    {
        for (DirectoryInfo directory = new(TestContext.CurrentContext.TestDirectory); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "CoreServer", WorldDataPacks.FolderName);
            if (File.Exists(Path.Combine(candidate, WorldDataPackApplier.ManifestFileName)))
                return candidate;
            candidate = Path.Combine(directory.FullName, "source", "server", "CoreServer", WorldDataPacks.FolderName);
            if (File.Exists(Path.Combine(candidate, WorldDataPackApplier.ManifestFileName)))
                return candidate;
        }

        Assert.Fail("source/server/CoreServer/data-packs/manifest.json not found above the test directory.");
        return null;
    }
}
