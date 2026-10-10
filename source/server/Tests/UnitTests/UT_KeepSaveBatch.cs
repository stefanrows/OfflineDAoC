using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using DOL.Database;
using DOL.Database.Handlers;
using DOL.GS;
using DOL.GS.Keeps;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Bug 134: a battleground lord death saved the keep, every component
/// and every door in separate SQLite transactions on the game tick. Keep rows
/// must reach the database in one transaction, each dirty row once, and the
/// saved end state must equal the in-memory state.</summary>
[TestFixture, NonParallelizable]
public sealed class UT_KeepSaveBatch
{
    private static readonly DateTime Start = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private const int KeepId = 9001;
    private const int ComponentCount = 20;
    private const int DoorCount = 2;
    private const ushort BattlegroundRegion = 165;

    /// <summary>Counts every write transaction and connection opened on the
    /// keep's SQLite file, without changing what is written.</summary>
    private sealed class CountingDatabase : SqliteObjectDatabase
    {
        public CountingDatabase(string connectionString) : base(connectionString) { }

        public int Connections { get; private set; }
        public int AtomicCommits { get; private set; }
        public List<string> PerTypeTransactions { get; } = new();
        public List<string> AtomicRows { get; } = new();

        protected override void OpenConnection(DbConnection connection)
        {
            Connections++;
            base.OpenConnection(connection);
        }

        protected override IEnumerable<bool> SaveObjectImpl(DataTableHandler tableHandler, IEnumerable<DataObject> dataObjects)
        {
            List<DataObject> rows = dataObjects.ToList();
            if (rows.Count > 0) PerTypeTransactions.Add(tableHandler.TableName);
            return base.SaveObjectImpl(tableHandler, rows);
        }

        protected override bool SaveObjectsAtomicallyCore(DataObject[] rows)
        {
            AtomicCommits++;
            AtomicRows.AddRange(rows.Select(row => row.GetType().Name));
            return base.SaveObjectsAtomicallyCore(rows);
        }

        public string Summary() =>
            $"connections={Connections} atomicCommits={AtomicCommits} atomicRows=[{string.Join(",", AtomicRows.GroupBy(name => name).Select(group => $"{group.Key}x{group.Count()}"))}] " +
            $"perTypeTransactions=[{string.Join(",", PerTypeTransactions.GroupBy(name => name).Select(group => $"{group.Key}x{group.Count()}"))}]";

        public void ResetCounters()
        {
            Connections = 0;
            AtomicCommits = 0;
            PerTypeTransactions.Clear();
            AtomicRows.Clear();
        }
    }

    /// <summary>Routes GameServer.Database to the counting file for one test.</summary>
    private sealed class KeepDatabaseScope : IDisposable
    {
        private static CountingDatabase _database;
        private readonly GameServer _previous = GameServer.Instance;

        private static readonly GameServerConfiguration Normal = new() { ServerType = EGameServerType.GST_Normal };

        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => _database;
            protected override GS.ServerRules.IServerRules ServerRulesImpl => new GS.ServerRules.NormalServerRules();
            public override GameServerConfiguration Configuration => Normal;
        }

        public KeepDatabaseScope(CountingDatabase database)
        {
            _database = database;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            // Guild's static constructor reads the server configuration.
            RuntimeHelpers.RunClassConstructor(typeof(Guild).TypeHandle);
        }

        public void Dispose()
        {
            GameServer.LoadTestDouble(_previous);
            _database = null;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(string path, CountingDatabase database, KeepDatabaseScope scope, GameKeep keep, DbKeep keepRow,
            GameKeepComponent[] components, DbKeepComponent[] componentRows, GameKeepDoor[] doors, DbDoor[] doorRows)
        {
            Path = path; Database = database; Scope = scope; Keep = keep; KeepRow = keepRow;
            Components = components; ComponentRows = componentRows; Doors = doors; DoorRows = doorRows;
        }

        public string Path { get; }
        public CountingDatabase Database { get; }
        public KeepDatabaseScope Scope { get; }
        public GameKeep Keep { get; }
        public DbKeep KeepRow { get; }
        public GameKeepComponent[] Components { get; }
        public DbKeepComponent[] ComponentRows { get; }
        public GameKeepDoor[] Doors { get; }
        public DbDoor[] DoorRows { get; }

        public void Dispose()
        {
            Scope.Dispose();
            foreach (string suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(Path + suffix)) File.Delete(Path + suffix);
        }
    }

    private static Fixture Build(int componentCount = ComponentCount, int doorCount = DoorCount, bool claimed = true, ushort region = 1)
    {
        string path = Path.Combine(System.IO.Path.GetTempPath(), "daoc-keep-save-" + Guid.NewGuid().ToString("N") + ".sqlite3");
        var database = new CountingDatabase($"Data Source={path};Version=3;Pooling=False;Journal Mode=WAL;Synchronous=Normal;Foreign Keys=True;Default Timeout=60");
        foreach (Type type in new[] { typeof(DbKeep), typeof(DbKeepComponent), typeof(DbDoor) })
            database.RegisterDataObject(type);
        var scope = new KeepDatabaseScope(database);

        var keepRow = new DbKeep
        {
            Name = "Test keep", KeepID = KeepId, Region = region, Realm = 1, BaseLevel = 50, Level = 5,
            ClaimedGuildName = claimed ? "Test guild" : string.Empty, ClaimedAt = claimed ? Start.AddDays(-1) : DateTime.MinValue,
            ProgressionInitialized = true, NextLevelAt = Start.AddHours(2), LastCaptureRewardAt = Start.AddDays(-1),
            LordDefeated = !claimed,
        };
        Assert.That(database.AddObject(keepRow), Is.True);

        var guild = new Guild(new DbGuild { GuildID = "test-guild", GuildName = "Test guild" });
        var keep = new GameKeep { DBKeep = keepRow, Guild = claimed ? guild : null, InternalID = keepRow.ObjectId };
        if (claimed) guild.ClaimedKeeps.Add(keep);
        // Load() normally creates the change-level timer; Release() stops it.
        typeof(AbstractGameKeep).GetMethod("InitialiseTimers", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(keep, null);

        var components = new GameKeepComponent[componentCount];
        var componentRows = new DbKeepComponent[componentCount];
        for (int i = 0; i < componentCount; i++)
        {
            componentRows[i] = new DbKeepComponent { KeepID = KeepId, ID = i, X = i * 100, Y = i * 50, Heading = 0, Health = 1000, Skin = 0, CreateInfo = string.Empty };
            Assert.That(database.AddObject(componentRows[i]), Is.True);
            components[i] = new GameKeepComponent { Keep = keep, InternalID = componentRows[i].ObjectId, ID = i };
            keep.KeepComponents.Add(components[i]);
        }

        var doors = new GameKeepDoor[doorCount];
        var doorRows = new DbDoor[doorCount];
        for (int i = 0; i < doorCount; i++)
        {
            doorRows[i] = new DbDoor { Name = "Test door " + i, Type = 0, Health = 10000, Level = 5, Realm = 1, State = 0 };
            Assert.That(database.AddObject(doorRows[i]), Is.True);
            doors[i] = new GameKeepDoor { Component = components[i % componentCount], DbDoor = doorRows[i], Health = 9000 };
            keep.Doors.Add("door" + i, doors[i]);
        }

        database.ResetCounters();
        return new Fixture(path, database, scope, keep, keepRow, components, componentRows, doors, doorRows);
    }

    private static GuardLord LordOf(GameKeepComponent component)
    {
        var lord = (GuardLord)RuntimeHelpers.GetUninitializedObject(typeof(GuardLord));
        lord.Component = component;
        return lord;
    }

    /// <summary>A bot or player claimer. Claim reads only its guild and name here.</summary>
    private static GameBot ClaimerOf(Guild guild)
    {
        var claimer = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
        // GameNPC.Realm reads its brain list, which an uninitialized object does not have.
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(claimer, new System.Collections.ArrayList());
        claimer.Guild = guild;
        return claimer;
    }

    private static CountingDatabase Reopen(string path) =>
        new($"Data Source={path};Version=3;Pooling=False;Journal Mode=WAL;Synchronous=Normal;Foreign Keys=True;Default Timeout=60");

    [Test]
    public void LordDeathCommitsEveryKeepRowOnceInOneTransaction()
    {
        using Fixture fixture = Build();
        CountingDatabase database = fixture.Database;

        PvpKeepCampaign.DefeatLord(LordOf(fixture.Components[0]));

        Assert.That(database.AtomicCommits, Is.EqualTo(1), database.Summary());
        Assert.That(database.PerTypeTransactions, Is.Empty, database.Summary());
        Assert.That(database.AtomicRows.Count(name => name == nameof(DbKeep)), Is.EqualTo(1), database.Summary());
        Assert.That(database.AtomicRows.Count(name => name == nameof(DbKeepComponent)), Is.EqualTo(ComponentCount), database.Summary());
        Assert.That(database.Connections, Is.LessThanOrEqualTo(2), database.Summary());
    }

    [Test]
    public void LordDeathSavedStateMatchesMemory()
    {
        using Fixture fixture = Build();
        PvpKeepCampaign.DefeatLord(LordOf(fixture.Components[0]));

        Assert.That(fixture.Keep.Guild, Is.Null);
        CountingDatabase reopened = Reopen(fixture.Path);
        reopened.RegisterDataObject(typeof(DbKeep));
        reopened.RegisterDataObject(typeof(DbKeepComponent));
        reopened.RegisterDataObject(typeof(DbDoor));

        DbKeep keep = reopened.SelectAllObjects<DbKeep>().Single();
        Assert.That(keep.LordDefeated, Is.True);
        Assert.That(keep.ClaimedGuildName, Is.EqualTo(string.Empty));
        Assert.That(keep.NextLevelAt, Is.EqualTo(DateTime.MinValue));
        Assert.That(keep.Level, Is.EqualTo(fixture.KeepRow.Level));

        var saved = reopened.SelectAllObjects<DbKeepComponent>().ToDictionary(row => row.ObjectId);
        for (int i = 0; i < fixture.Components.Length; i++)
            Assert.That(saved[fixture.ComponentRows[i].ObjectId].Health, Is.EqualTo(fixture.Components[i].Health), $"component {i}");

        var savedDoors = reopened.SelectAllObjects<DbDoor>().ToDictionary(row => row.ObjectId);
        for (int i = 0; i < fixture.Doors.Length; i++)
            Assert.That(savedDoors[fixture.DoorRows[i].ObjectId].Health, Is.EqualTo(fixture.Doors[i].Health), $"door {i}");
    }

    [Test]
    public void UpgradeTimerLevelChangeFlushesOnceInOneTransaction()
    {
        using Fixture fixture = Build();
        CountingDatabase database = fixture.Database;

        fixture.Keep.ChangeLevel(6);

        Assert.That(database.AtomicCommits, Is.EqualTo(1), database.Summary());
        Assert.That(database.PerTypeTransactions, Is.Empty, database.Summary());
        Assert.That(database.Connections, Is.LessThanOrEqualTo(2), database.Summary());

        CountingDatabase reopened = Reopen(fixture.Path);
        reopened.RegisterDataObject(typeof(DbKeep));
        Assert.That(reopened.SelectAllObjects<DbKeep>().Single().Level, Is.EqualTo(6));
    }

    /// <summary>Bug 143: a claim (steward, bot brain or the keep command) saved the keep and
    /// each component in separate transactions, about 21 on the game tick. Its rows must
    /// reach the database in one transaction.</summary>
    [Test]
    public void ClaimCommitsEveryKeepRowOnceInOneTransaction()
    {
        using Fixture fixture = Build(claimed: false, region: BattlegroundRegion);
        CountingDatabase database = fixture.Database;
        var guild = new Guild(new DbGuild { GuildID = "claim-guild", GuildName = "Claim guild" });

        fixture.Keep.Claim(ClaimerOf(guild));

        Assert.That(fixture.Keep.Guild, Is.SameAs(guild), "The claim must take the keep.");
        Assert.That(database.AtomicCommits, Is.EqualTo(1), database.Summary());
        Assert.That(database.PerTypeTransactions, Is.Empty, database.Summary());
        Assert.That(database.AtomicRows.Count(name => name == nameof(DbKeep)), Is.EqualTo(1), database.Summary());
        Assert.That(database.AtomicRows.Count(name => name == nameof(DbKeepComponent)), Is.EqualTo(ComponentCount), database.Summary());
        Assert.That(database.Connections, Is.LessThanOrEqualTo(2), database.Summary());
    }

    [Test]
    public void ClaimSavedStateMatchesMemory()
    {
        using Fixture fixture = Build(claimed: false, region: BattlegroundRegion);
        var guild = new Guild(new DbGuild { GuildID = "claim-guild", GuildName = "Claim guild" });

        fixture.Keep.Claim(ClaimerOf(guild));

        CountingDatabase reopened = Reopen(fixture.Path);
        reopened.RegisterDataObject(typeof(DbKeep));
        reopened.RegisterDataObject(typeof(DbKeepComponent));
        reopened.RegisterDataObject(typeof(DbDoor));

        DbKeep keep = reopened.SelectAllObjects<DbKeep>().Single();
        Assert.That(keep.ClaimedGuildName, Is.EqualTo("Claim guild"));
        // Reads come back in local time; the claim stores UTC.
        Assert.That(keep.ClaimedAt.ToUniversalTime(), Is.EqualTo(fixture.KeepRow.ClaimedAt).Within(TimeSpan.FromSeconds(1)));
        Assert.That(keep.LordDefeated, Is.EqualTo(fixture.KeepRow.LordDefeated));
        Assert.That(keep.Level, Is.EqualTo(fixture.KeepRow.Level));

        var saved = reopened.SelectAllObjects<DbKeepComponent>().ToDictionary(row => row.ObjectId);
        for (int i = 0; i < fixture.Components.Length; i++)
            Assert.That(saved[fixture.ComponentRows[i].ObjectId].Health, Is.EqualTo(fixture.Components[i].Health), $"component {i}");

        var savedDoors = reopened.SelectAllObjects<DbDoor>().ToDictionary(row => row.ObjectId);
        for (int i = 0; i < fixture.Doors.Length; i++)
            Assert.That(savedDoors[fixture.DoorRows[i].ObjectId].Health, Is.EqualTo(fixture.Doors[i].Health), $"door {i}");
    }

    [Test]
    public void OneRejectedDoorDoesNotDropTheKeepWrite()
    {
        using Fixture fixture = Build();
        CountingDatabase database = fixture.Database;
        // Delete through a second connection: the in-memory door still believes it is
        // persisted, so the atomic flush includes it and must roll back.
        CountingDatabase other = Reopen(fixture.Path);
        other.RegisterDataObject(typeof(DbDoor));
        Assert.That(other.DeleteObject(other.FindObjectByKey<DbDoor>(fixture.DoorRows[0].ObjectId)), Is.True);

        PvpKeepCampaign.DefeatLord(LordOf(fixture.Components[0]));

        Assert.That(database.AtomicCommits, Is.EqualTo(1), database.Summary());
        Assert.That(database.PerTypeTransactions, Does.Contain("Keep").And.Contain("KeepComponent"),
            "The rejected atomic flush must fall back to per-table saves. " + database.Summary());

        CountingDatabase reopened = Reopen(fixture.Path);
        reopened.RegisterDataObject(typeof(DbKeep));
        Assert.That(reopened.SelectAllObjects<DbKeep>().Single().LordDefeated, Is.True,
            "A missing door row must not roll back the keep, the components or the other doors.");
    }

    [Test]
    public void NestedScopeJoinsTheOutermostFlush()
    {
        using Fixture fixture = Build();
        CountingDatabase database = fixture.Database;

        using (KeepSaveBatch.Begin("outer", fixture.Keep))
        {
            fixture.KeepRow.Level = 7;
            using (KeepSaveBatch.Begin("inner", fixture.Keep))
                Assert.That(KeepSaveBatch.Save(fixture.KeepRow), Is.True);
            Assert.That(database.AtomicCommits, Is.Zero, "The inner scope must not flush.");
        }

        Assert.That(database.AtomicCommits, Is.EqualTo(1), database.Summary());
        Assert.That(database.AtomicRows, Is.EqualTo(new[] { nameof(DbKeep) }), database.Summary());
        Assert.That(ReadKeep(fixture.Path).Level, Is.EqualTo(7));
    }

    [Test]
    public void OneRowIsWrittenOnceEvenWhenTwoInstancesOfItAreRecorded()
    {
        using Fixture fixture = Build();
        CountingDatabase database = fixture.Database;
        DbKeepComponent first = fixture.ComponentRows[0];
        DbKeepComponent later = database.FindObjectByKey<DbKeepComponent>(first.ObjectId);
        Assert.That(later, Is.Not.Null);
        Assert.That(later, Is.Not.SameAs(first));

        using (KeepSaveBatch.Begin("test", fixture.Keep))
        {
            first.Health = 1234;
            Assert.That(KeepSaveBatch.Save(first), Is.True);
            later.Health = 4321;
            Assert.That(KeepSaveBatch.Save(later), Is.True);
            Assert.That(KeepSaveBatch.Pending<DbKeepComponent>(first.ObjectId), Is.SameAs(later),
                "The most recently recorded instance is the one that is written.");
        }

        Assert.That(database.AtomicRows.Count(name => name == nameof(DbKeepComponent)), Is.EqualTo(1), database.Summary());
        Assert.That(ReadComponentHealth(fixture.Path, first.ObjectId), Is.EqualTo(4321));
    }

    [Test]
    public void CleanRowsAreNotWrittenAndPendingFindsOnlyTheScopesOwnRows()
    {
        using Fixture fixture = Build();
        DbKeepComponent row = fixture.ComponentRows[3];

        using (KeepSaveBatch.Begin("test", fixture.Keep))
        {
            Assert.That(KeepSaveBatch.Pending<DbKeepComponent>(row.ObjectId), Is.Null);
            Assert.That(KeepSaveBatch.Save(row), Is.True);
            Assert.That(KeepSaveBatch.Save(fixture.KeepRow), Is.True);
            Assert.That(KeepSaveBatch.Pending<DbKeepComponent>(row.ObjectId), Is.SameAs(row));
            Assert.That(KeepSaveBatch.Pending<DbKeepComponent>("unknown-object"), Is.Null);
        }

        Assert.That(fixture.Database.AtomicCommits, Is.Zero, fixture.Database.Summary());
        Assert.That(fixture.Database.PerTypeTransactions, Is.Empty, fixture.Database.Summary());
        Assert.That(KeepSaveBatch.Pending<DbKeepComponent>(row.ObjectId), Is.Null, "A finished scope holds nothing.");
    }

    [Test]
    public void RowsRecordedBeforeAnExceptionAreStillWritten()
    {
        using Fixture fixture = Build();

        Assert.Throws<InvalidOperationException>(() =>
        {
            using (KeepSaveBatch.Begin("test", fixture.Keep))
            {
                fixture.KeepRow.LordDefeated = true;
                KeepSaveBatch.Save(fixture.KeepRow);
                throw new InvalidOperationException("stop after the save request");
            }
        });

        Assert.That(ReadKeep(fixture.Path).LordDefeated, Is.True,
            "A save requested before a failure persists, as it did before batching.");
    }

    [Test]
    public void SavesFromOtherThreadsAreNotDeferred()
    {
        using Fixture fixture = Build();

        using (KeepSaveBatch.Begin("test", fixture.Keep))
        {
            fixture.KeepRow.Level = 8;
            Task.Run(() => KeepSaveBatch.Save(fixture.KeepRow)).Wait();
            Assert.That(ReadKeep(fixture.Path).Level, Is.EqualTo(8), "Another thread writes through at once.");
        }

        Assert.That(fixture.Database.AtomicCommits, Is.Zero, fixture.Database.Summary());
    }

    [Test]
    public void SaveOutsideAScopeWritesImmediately()
    {
        using Fixture fixture = Build();
        fixture.KeepRow.Level = 8;

        Assert.That(KeepSaveBatch.Save(fixture.KeepRow), Is.True);

        Assert.That(fixture.Database.PerTypeTransactions, Does.Contain("Keep"), fixture.Database.Summary());
        Assert.That(fixture.Database.AtomicCommits, Is.Zero, fixture.Database.Summary());
        Assert.That(ReadKeep(fixture.Path).Level, Is.EqualTo(8));
    }

    private static DbKeep ReadKeep(string path)
    {
        CountingDatabase reopened = Reopen(path);
        reopened.RegisterDataObject(typeof(DbKeep));
        return reopened.SelectAllObjects<DbKeep>().Single();
    }

    private static int ReadComponentHealth(string path, string objectId)
    {
        CountingDatabase reopened = Reopen(path);
        reopened.RegisterDataObject(typeof(DbKeepComponent));
        return reopened.FindObjectByKey<DbKeepComponent>(objectId).Health;
    }
}
