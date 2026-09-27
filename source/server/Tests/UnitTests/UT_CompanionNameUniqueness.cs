using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.Database.Handlers;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

// Task: a new companion recruit's name must not collide with a real character
// or an autonomous world bot (see AutonomousAltTrickle for the equivalent
// check on a new world-bot alt). Existing companion records are untouched.
[TestFixture, NonParallelizable]
public sealed class UT_CompanionNameUniqueness
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    // Delegates persistent-companion storage to a real (temporary) SQLite
    // database, while serving DbCoreCharacter/OfflineWorldBotRecord reads from
    // an in-memory list. This exercises the real recruit/name-collision code
    // path without needing to satisfy every NOT NULL column of a full
    // DOLCharacters row.
    public class HybridDatabase : DispatchProxy
    {
        public IObjectDatabase Inner;
        public List<DbCoreCharacter> Characters = new();
        public List<OfflineWorldBotRecord> WorldBots = new();

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (targetMethod.Name == nameof(IObjectDatabase.SelectAllObjects) && targetMethod.IsGenericMethod)
            {
                Type elementType = targetMethod.GetGenericArguments()[0];
                if (elementType == typeof(DbCoreCharacter))
                    return Characters;
                if (elementType == typeof(OfflineWorldBotRecord))
                    return WorldBots;
            }

            return targetMethod.Invoke(Inner, args);
        }
    }

    private sealed class DatabaseServer : GameServer
    {
        public static IObjectDatabase CurrentDatabase;
        public static readonly GameServerConfiguration TestConfiguration = new();
        protected override IObjectDatabase DataBaseImpl => CurrentDatabase;
        public override GameServerConfiguration Configuration => TestConfiguration;
    }

    private sealed class Owner : GamePlayer
    {
        private string _internalId;

        private Owner() : base(null, null) { }

        public override string InternalID { get => _internalId; set => _internalId = value; }
        public override byte Level { get => 50; set { } }
        public override bool IsAlive => true;

        public void Configure(string id, eRealm realm)
        {
            InternalID = id;
            SetField(typeof(GameObject), this, "m_name", id);
            Realm = realm;
        }
    }

    private string _databasePath;
    private SqliteObjectDatabase _innerDatabase;
    private HybridDatabase _hybridDatabase;
    private GameServer _previousServer;

    [SetUp]
    public void SetUp()
    {
        _previousServer = GameServer.Instance;
        _databasePath = Path.Combine(Path.GetTempPath(),
            "daoc-companion-name-uniqueness-" + Guid.NewGuid().ToString("N") + ".sqlite3");
        _innerDatabase = new SqliteObjectDatabase($"Data Source={_databasePath};Version=3;Pooling=False;");
        _innerDatabase.RegisterDataObject(typeof(PlayerCompanionRecord));
        _innerDatabase.RegisterDataObject(typeof(DbInventoryItem));
        _innerDatabase.RegisterDataObject(typeof(DbDataQuest));

        _hybridDatabase = (HybridDatabase)DispatchProxy.Create<IObjectDatabase, HybridDatabase>();
        _hybridDatabase.Inner = _innerDatabase;
        DatabaseServer.CurrentDatabase = (IObjectDatabase)_hybridDatabase;
        GameServer.LoadTestDouble((DatabaseServer)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseServer)));
    }

    [TearDown]
    public void TearDown()
    {
        DatabaseServer.CurrentDatabase = null;
        GameServer.LoadTestDouble(_previousServer);
        DeleteTemporaryFile(_databasePath);
        DeleteTemporaryFile(_databasePath + "-wal");
        DeleteTemporaryFile(_databasePath + "-shm");
    }

    private static Owner NewOwner(string id, eRealm realm)
    {
        Owner owner = (Owner)RuntimeHelpers.GetUninitializedObject(typeof(Owner));
        owner.Configure(id, realm);
        return owner;
    }

    [Test]
    public void RecruitRejectsAnAuthoredNameAlreadyUsedByARealCharacter()
    {
        _hybridDatabase.Characters.Add(new DbCoreCharacter { Name = "Kiri" });
        Owner owner = NewOwner("character-collision-owner", eRealm.Midgard);

        Assert.That(PlayerCompanionRoster.TryRecruitAuthored(owner, "Kiri", out PlayerCompanionRecord record, out string message),
            Is.False, "A companion name must not collide with an existing player character");
        Assert.That(record, Is.Null);
        Assert.That(message, Does.Contain("Kiri"));
    }

    [Test]
    public void RecruitRejectsAnAuthoredNameAlreadyUsedByAnAutonomousWorldBot()
    {
        _hybridDatabase.WorldBots.Add(new OfflineWorldBotRecord { Name = "Kiri" });
        Owner owner = NewOwner("world-bot-collision-owner", eRealm.Midgard);

        Assert.That(PlayerCompanionRoster.TryRecruitAuthored(owner, "Kiri", out PlayerCompanionRecord record, out string message),
            Is.False, "A companion name must not collide with an existing autonomous world bot");
        Assert.That(record, Is.Null);
        Assert.That(message, Does.Contain("Kiri"));
    }

    [Test]
    public void RecruitStillSucceedsWhenNoNameCollisionExists()
    {
        Owner owner = NewOwner("no-collision-owner", eRealm.Midgard);

        Assert.That(PlayerCompanionRoster.TryRecruitAuthored(owner, "Kiri", out PlayerCompanionRecord record, out string message),
            Is.True, message);
        Assert.That(record?.Name, Is.EqualTo("Kiri"));
    }

    private static void SetField(Type type, object instance, string name, object value) =>
        type.GetField(name, Hidden).SetValue(instance, value);

    private static void DeleteTemporaryFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
