using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.Database.Handlers;
using DOL.Events;
using DOL.GS;
using DOL.GS.Commands;
using DOL.GS.PacketHandler;
using DOL.GS.PlayerClass;
using DOL.Logging;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture, NonParallelizable]
public sealed class UT_PlayerCompanionStage6Integration
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private string _databasePath;
    private SqliteObjectDatabase _database;
    private GameServer _previousServer;
    private PetTestLanguageScope _language;
    private int _previousGroupMaximum;
    private Logger _gameBotLogger;
    private object _previousGameBotLogQueue;
    private BlockingCollection<LogEntry> _testLogEntries;

    private sealed class DatabaseServer : GameServer
    {
        public static IObjectDatabase CurrentDatabase;
        public static readonly GameServerConfiguration TestConfiguration = new();
        protected override IObjectDatabase DataBaseImpl => CurrentDatabase;
        public override GameServerConfiguration Configuration => TestConfiguration;
    }

    public class NoOpPacketLib : DispatchProxy
    {
        protected override object Invoke(MethodInfo targetMethod, object[] args) =>
            targetMethod.ReturnType == typeof(void) ? null :
            targetMethod.ReturnType.IsValueType ? Activator.CreateInstance(targetMethod.ReturnType) : null;
    }

    private class SaveFailureDatabase : DispatchProxy
    {
        public IObjectDatabase Inner;
        public bool FailSaves;

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (FailSaves && targetMethod.Name == nameof(IObjectDatabase.SaveObject))
                return false;
            return targetMethod.Invoke(Inner, args);
        }
    }

    private sealed class Owner : GamePlayer
    {
        private string _internalId;
        private ushort _regionId;
        private ICharacterClass _characterClass;
        private long _experience;

        private Owner() : base(null, null) { }

        public override string InternalID { get => _internalId; set => _internalId = value; }
        public override byte Level { get => 50; set { } }
        public override bool IsAlive => true;
        public override long Experience { get => _experience; set => _experience = value; }
        public override IPacketLib Out => DispatchProxy.Create<IPacketLib, NoOpPacketLib>();
        public override ushort CurrentRegionID { get => _regionId; set => _regionId = value; }
        public override ICharacterClass CharacterClass => _characterClass ?? new ClassCleric();
        public void Configure(string id, eRealm realm, ushort region = 65000, ICharacterClass characterClass = null)
        {
            InternalID = id;
            SetField(typeof(GameObject), this, "m_name", id);
            Realm = realm;
            CurrentRegionID = region;
            _characterClass = characterClass;
        }
    }

    private sealed class Companion : GameBot
    {
        public bool Alive;
        public int MoveAttempts;
        public ushort RegionId;
        public eRealm BotRealm;
        public bool WasRemovedFromWorld;
        public bool WasDeleted;

        private Companion() : base((OfflineWorldBotRecord)null) { }

        public override bool IsAlive => Alive;
        public override ushort CurrentRegionID { get => RegionId; set => RegionId = value; }
        public override eRealm Realm { get => BotRealm; set => BotRealm = value; }
        public override int X => 10;
        public override int Y => 20;
        public override int Z => 30;
        public override bool MoveTo(ushort regionID, int x, int y, int z, ushort heading)
        {
            MoveAttempts++;
            return false;
        }
        public override IList<Specialization> GetSpecList() => Array.Empty<Specialization>();
        public override bool RemoveFromWorld()
        {
            WasRemovedFromWorld = true;
            return true;
        }
        public override void Delete()
        {
            WasDeleted = true;
            ObjectState = GameObject.eObjectState.Deleted;
        }
    }

    private sealed class QuietGroup : Group
    {
        public QuietGroup(GamePlayer leader) : base(leader) { }
        public override void SendMessageToGroupMembers(string message, eChatType type, eChatLoc location) { }
    }

    [SetUp]
    public void SetUp()
    {
        _language = new PetTestLanguageScope();
        _previousServer = GameServer.Instance;
        _previousGroupMaximum = DOL.GS.ServerProperties.Properties.GROUP_MAX_MEMBER;
        DOL.GS.ServerProperties.Properties.GROUP_MAX_MEMBER = 8;
        DatabaseServer.TestConfiguration.ServerType = EGameServerType.GST_Normal;
        _databasePath = Path.Combine(Path.GetTempPath(),
            "daoc-companion-stage6-" + Guid.NewGuid().ToString("N") + ".sqlite3");
        _database = Open(_databasePath);
        DatabaseServer.CurrentDatabase = _database;
        GameServer.LoadTestDouble((DatabaseServer)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseServer)));
        InstallQuietGameBotLoggerQueue();
    }

    [TearDown]
    public void TearDown()
    {
        DatabaseServer.CurrentDatabase = null;
        GameServer.LoadTestDouble(_previousServer);
        DOL.GS.ServerProperties.Properties.GROUP_MAX_MEMBER = _previousGroupMaximum;
        typeof(Logger).GetField("_queueProcessor", Hidden).SetValue(_gameBotLogger, _previousGameBotLogQueue);
        _testLogEntries.Dispose();
        _language.Dispose();
        DeleteTemporaryFile(_databasePath);
        DeleteTemporaryFile(_databasePath + "-wal");
        DeleteTemporaryFile(_databasePath + "-shm");
    }

    [Test]
    public void SameClassCompanionsAndTheirInventoriesRemainIndependentAfterFreshDatabaseLoad()
    {
        Owner firstOwner = NewOwner("owner-one", eRealm.Albion);
        Owner secondOwner = NewOwner("owner-two", eRealm.Midgard);

        Assert.That(PlayerCompanionRoster.TryRecruit(firstOwner, eRealm.Albion, eCharacterClass.Cleric,
            out PlayerCompanionRecord first, out string firstMessage), Is.True, firstMessage);
        Assert.That(PlayerCompanionRoster.TryRecruit(firstOwner, eRealm.Albion, eCharacterClass.Cleric,
            out PlayerCompanionRecord second, out string secondMessage), Is.True, secondMessage);
        Assert.That(PlayerCompanionRoster.TryRecruit(secondOwner, eRealm.Albion, eCharacterClass.Cleric,
            out PlayerCompanionRecord otherOwner, out string otherMessage), Is.True, otherMessage);

        Assert.Multiple(() =>
        {
            Assert.That(first.CompanionId, Is.Not.EqualTo(second.CompanionId));
            Assert.That(first.Name, Is.Not.EqualTo(second.Name));
            Assert.That(first.ClassId, Is.EqualTo(second.ClassId));
            Assert.That(PlayerCompanionRoster.TryBench(secondOwner, first.CompanionId, out _), Is.False,
                "A different character cannot access this owner's companion.");
            Assert.That(PlayerCompanionRoster.TryInvite(secondOwner, first.CompanionId, out _), Is.False,
                "A different character cannot invite this owner's companion.");
        });
        Assert.That(PlayerCompanionRoster.TryBench(firstOwner, first.CompanionId, out _), Is.True);
        Assert.That(PlayerCompanionRoster.TryInvite(firstOwner, first.CompanionId, out string inviteMessage), Is.False);
        Assert.That(inviteMessage, Does.Contain("in the world"));

        first.Level = 14;
        first.Experience = 123456;
        first.SerializedSpecs = "Enhancement|10;Rejuvenation|5";
        first.SerializedEquipmentState = "item-a=EK;slot:HeadArmor";
        first.TacticalRole = "healer";
        first.EngagementPreference = "defensive";
        first.IsActive = true;
        first.Dirty = true;
        Assert.That(_database.SaveObject(first), Is.True);

        string playerInventoryOwner = InventoryItemsOwner(firstOwner.ObjectId);
        string firstInventoryOwner = InventoryItemsOwner(PlayerCompanionRoster.InventoryOwnerId(first.CompanionId));
        string secondInventoryOwner = InventoryItemsOwner(PlayerCompanionRoster.InventoryOwnerId(second.CompanionId));

        SqliteObjectDatabase afterRestart = Open(_databasePath);
        DatabaseServer.CurrentDatabase = afterRestart;
        Assert.That(PlayerCompanionRoster.TryGetRoster(firstOwner, out List<PlayerCompanionRecord> restored), Is.True);
        Assert.That(PlayerCompanionRoster.TryGetRoster(secondOwner, out List<PlayerCompanionRecord> otherRestored), Is.True);

        PlayerCompanionRecord restoredFirst = restored.Single(record => record.CompanionId == first.CompanionId);
        PlayerCompanionRecord restoredSecond = restored.Single(record => record.CompanionId == second.CompanionId);
        Assert.Multiple(() =>
        {
            Assert.That(restored, Has.Count.EqualTo(2));
            Assert.That(otherRestored.Select(record => record.CompanionId), Is.EqualTo(new[] { otherOwner.CompanionId }));
            Assert.That(restoredFirst.Level, Is.EqualTo(14));
            Assert.That(restoredFirst.Experience, Is.EqualTo(123456));
            Assert.That(restoredFirst.SerializedSpecs, Is.EqualTo("Enhancement|10;Rejuvenation|5"));
            Assert.That(restoredFirst.SerializedEquipmentState, Is.EqualTo("item-a=EK;slot:HeadArmor"));
            Assert.That(restoredFirst.TacticalRole, Is.EqualTo("healer"));
            Assert.That(restoredFirst.EngagementPreference, Is.EqualTo("defensive"));
            Assert.That(restoredFirst.IsActive, Is.True);
            Assert.That(restoredSecond.Level, Is.EqualTo(1));
            Assert.That(playerInventoryOwner, Is.EqualTo(firstOwner.ObjectId),
                "The player's existing inventory remains in its own owner namespace.");
            Assert.That(firstInventoryOwner, Is.EqualTo(PlayerCompanionRoster.InventoryOwnerId(first.CompanionId)));
            Assert.That(secondInventoryOwner, Is.EqualTo(PlayerCompanionRoster.InventoryOwnerId(second.CompanionId)));
            Assert.That(new[] { playerInventoryOwner, firstInventoryOwner, secondInventoryOwner }.Distinct().ToArray(),
                Has.Length.EqualTo(3));
        });
    }

    [Test]
    public void FailedManualModeSaveRestoresPreviousTrainingMetadata()
    {
        Owner owner = NewOwner("training-owner", eRealm.Albion);
        PlayerCompanionRecord record = NewRecord(owner.ObjectId, "training-companion", "Training Companion",
            eCharacterClass.Cleric);
        record.TrainingMode = "automatic";
        record.TrainingPlanId = "fixture-plan";
        record.SerializedSpecs = "Enhancement|10;Rejuvenation|5";
        record.UnspentSpecPoints = 19;
        Assert.That(_database.AddObject(record), Is.True);

        IObjectDatabase failing = DispatchProxy.Create<IObjectDatabase, SaveFailureDatabase>();
        SaveFailureDatabase failurePolicy = (SaveFailureDatabase)(object)failing;
        failurePolicy.Inner = _database;
        failurePolicy.FailSaves = true;
        DatabaseServer.CurrentDatabase = failing;

        Assert.That(PlayerCompanionRoster.TrySetManualTrainingMode(owner, record.Name, out string message), Is.False);
        PlayerCompanionRecord unchanged = _database.SelectObjects<PlayerCompanionRecord>(
            DB.Column(nameof(PlayerCompanionRecord.CompanionId)).IsEqualTo(record.CompanionId)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("could not be saved"));
            Assert.That(unchanged.TrainingMode, Is.EqualTo("automatic"));
            Assert.That(unchanged.TrainingPlanId, Is.EqualTo("fixture-plan"));
            Assert.That(unchanged.SerializedSpecs, Is.EqualTo("Enhancement|10;Rejuvenation|5"));
            Assert.That(unchanged.UnspentSpecPoints, Is.EqualTo(19));
        });
    }

    [Test]
    public void BuildSelectionRejectsUnknownBuildsWithoutChangingTheCompanion()
    {
        Owner owner = NewOwner("build-owner", eRealm.Albion);
        PlayerCompanionRecord cleric = NewRecord(owner.ObjectId, "build-cleric", "Build Cleric", eCharacterClass.Cleric);
        cleric.TrainingMode = "automatic";
        cleric.TrainingPlanId = "general-pve-v1-cleric";
        cleric.SerializedSpecs = "Enhancement|10;Rejuvenation|5";
        cleric.UnspentSpecPoints = 19;
        PlayerCompanionRecord necromancer = NewRecord(owner.ObjectId, "build-necro", "Build Necro", eCharacterClass.Necromancer);
        Assert.That(_database.AddObject(cleric), Is.True);
        Assert.That(_database.AddObject(necromancer), Is.True);

        Assert.That(PlayerCompanionRoster.TrySelectBuild(owner, cleric.Name, "summoning", out string unknown), Is.False);
        Assert.That(PlayerCompanionRoster.TrySelectBuild(owner, necromancer.Name, "deathsight", out string manualOnly), Is.False);
        Assert.That(PlayerCompanionRoster.TryRecruit(owner, eRealm.Albion, eCharacterClass.Cleric, "summoning",
            out PlayerCompanionRecord recruited, out string recruitMessage), Is.False);
        PlayerCompanionRecord unchanged = _database.SelectObjects<PlayerCompanionRecord>(
            DB.Column(nameof(PlayerCompanionRecord.CompanionId)).IsEqualTo(cleric.CompanionId)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(unknown, Does.Contain("rejuvenation (Rejuvenation (healer))"));
            Assert.That(manualOnly, Does.Contain("has no automatic builds"));
            Assert.That(recruitMessage, Does.Contain("is not a Cleric build"));
            Assert.That(recruited, Is.Null);
            Assert.That(PlayerCompanionRoster.TryGetRoster(owner, out var roster) ? roster.Count : -1, Is.EqualTo(2));
            Assert.That(unchanged.TrainingPlanId, Is.EqualTo("general-pve-v1-cleric"));
            Assert.That(unchanged.SerializedSpecs, Is.EqualTo("Enhancement|10;Rejuvenation|5"));
            Assert.That(unchanged.UnspentSpecPoints, Is.EqualTo(19));
        });
    }

    [Test]
    public void CrossRealmOwnedCompanionJoinsItsOwnersGroupAndMovesOnlyAfterConfirmedTransfer()
    {
        Assert.That(GameServer.Instance.Configuration.ServerType, Is.EqualTo(EGameServerType.GST_Normal));
        Owner owner = NewOwner("cross-realm-owner", eRealm.Albion);
        QuietGroup group = GroupWithOwner(owner);
        Companion companion = NewCompanion(owner, NewRecord(owner.ObjectId, "hib-companion", "Hibernian Companion",
            eCharacterClass.Bard), eRealm.Hibernia);

        Assert.That(group.AddMember(companion), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(companion.Realm, Is.EqualTo(eRealm.Hibernia));
            Assert.That(owner.Realm, Is.EqualTo(eRealm.Albion));
            Assert.That(group.IsInTheGroup(companion), Is.True);
            Assert.That(TemporaryGroupStableTravel.ShouldRelocatePersistentCompanionForOwnerTransfer(
                true, true, true, true), Is.True);
            Assert.That(TemporaryGroupStableTravel.ShouldRelocatePersistentCompanionForOwnerTransfer(
                false, true, true, true), Is.False, "Unconfirmed movement must not relocate a persistent companion.");
            Assert.That(TemporaryGroupStableTravel.ShouldRelocatePersistentCompanionForOwnerTransfer(
                true, false, true, true), Is.False);
            Assert.That(TemporaryGroupStableTravel.ShouldRelocatePersistentCompanionForOwnerTransfer(
                true, true, false, true), Is.False);
            Assert.That(TemporaryGroupStableTravel.ShouldRelocatePersistentCompanionForOwnerTransfer(
                true, true, true, false), Is.False);
        });
    }

    [Test]
    public void RemovingPersistentCompanionFromGroupSavesBenchStateAndDeletesOnlyItsRuntimeActor()
    {
        Owner owner = NewOwner("removal-owner", eRealm.Albion);
        PlayerCompanionRecord record = NewRecord(owner.ObjectId, Guid.NewGuid().ToString("D"),
            "Removed Companion", eCharacterClass.Cleric);
        record.IsActive = true;
        Assert.That(_database.AddObject(record), Is.True);
        Companion companion = NewCompanion(owner, record, eRealm.Albion);
        companion.ObjectState = GameObject.eObjectState.Active;
        SetField(typeof(GameNPC), companion, "m_brains", new System.Collections.ArrayList());
        QuietGroup group = GroupWithOwner(owner);
        AddDirect(group, companion);

        Assert.That(group.RemoveMember(companion, retainSingleRemainingMember: true), Is.True);

        PlayerCompanionRecord stored = _database.SelectObjects<PlayerCompanionRecord>(
            DB.Column(nameof(PlayerCompanionRecord.CompanionId)).IsEqualTo(record.CompanionId)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(group.IsInTheGroup(companion), Is.False);
            Assert.That(stored.IsActive, Is.False);
            Assert.That(companion.WasRemovedFromWorld, Is.True);
            Assert.That(companion.WasDeleted, Is.True);
        });
    }

    [Test]
    public void OwnerLogoutSavesActiveProgressAndFreshDatabaseLoadRestoresItsRosterMarker()
    {
        Owner owner = NewOwner("logout-owner", eRealm.Albion);
        owner.Experience = 500_000;
        PlayerCompanionRecord record = NewRecord(owner.ObjectId, Guid.NewGuid().ToString("D"),
            "Logout Companion", eCharacterClass.Cleric);
        Assert.That(_database.AddObject(record), Is.True);
        Companion companion = NewCompanion(owner, record, eRealm.Albion);
        SetField(typeof(GameObject), companion, "m_level", (byte)17);
        SetProperty(typeof(GameBot), companion, nameof(GameBot.Experience), 123456L);
        companion.ObjectState = GameObject.eObjectState.Active;
        SetField(typeof(GameNPC), companion, "m_brains", new System.Collections.ArrayList());
        QuietGroup group = GroupWithOwner(owner);
        AddDirect(group, companion);

        PlayerCompanionRoster.OnOwnerQuit(companion);

        SqliteObjectDatabase afterRestart = Open(_databasePath);
        DatabaseServer.CurrentDatabase = afterRestart;
        PlayerCompanionRecord restored = afterRestart.SelectObjects<PlayerCompanionRecord>(
            DB.Column(nameof(PlayerCompanionRecord.CompanionId)).IsEqualTo(record.CompanionId)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(GetBooleanPolicy(companion, "SuppressRosterBenchOnGroupRemoval"), Is.True);
            Assert.That(companion.WasDeleted, Is.True);
            Assert.That(restored.IsActive, Is.True,
                "Logout preserves the active roster marker so the login restore path can invite this companion again.");
            Assert.That(restored.Level, Is.EqualTo(17));
            Assert.That(restored.Experience, Is.EqualTo(123456));
        });
    }

    [TestCase(false, 19_999, false)]
    [TestCase(false, 20_000, true)]
    [TestCase(true, 89_999, false)]
    [TestCase(true, 90_000, true)]
    public void PersistentCompanionUsesExistingGameBotCorpseRecoveryTimers(
        bool hasViableResurrector, long deadForMilliseconds, bool expectedReleaseAttempt)
    {
        Owner owner = NewOwner("death-owner", eRealm.Albion, 65000, new ClassCleric());
        Companion companion = NewCompanion(owner,
            NewRecord(owner.ObjectId, "death-companion", "Death Companion", eCharacterClass.Cleric), eRealm.Albion);
        companion.Alive = false;
        companion.ObjectState = GameObject.eObjectState.Active;
        companion.RegionId = 65000;

        if (hasViableResurrector)
        {
            QuietGroup group = GroupWithOwner(owner);
            AddDirect(group, companion);
        }

        SetField(typeof(GameBot), companion, "_deathTick", GameLoop.GameLoopTime - deadForMilliseconds);
        SetField(typeof(GameBot), companion, "_deathRegionId", (ushort)65000);
        SetField(typeof(GameBot), companion, "_deathLocation", new Point3D(10, 20, 30));
        MethodInfo recovery = typeof(GameBot).GetMethod("HandleDeathRecovery", Hidden);

        recovery.Invoke(companion, new object[] { null });

        Assert.That(companion.MoveAttempts > 0, Is.EqualTo(expectedReleaseAttempt));
        Assert.That(GetBooleanPolicy(companion, "RetainCorpseForResurrection"), Is.True);
        Assert.That(GetBooleanPolicy(companion, "RetainGroupWhenDead"), Is.True);
        Assert.That(companion.IsPersistentPlayerCompanion, Is.True);
        Assert.That(companion.TryReturnTemporaryCompanionToLeader(afterRevival: true), Is.False,
            "Persistent companions do not enter the temporary helper release-and-recall path.");
    }

    [TestCase(40)]
    [TestCase(80)]
    public void CompanionRaidsAcceptOwnedTemporaryHelpersAndRejectPersistentCompanions(int capacity)
    {
        Owner owner = NewOwner("raid-owner-" + capacity, eRealm.Albion);
        QuietGroup group = GroupWithOwner(owner);
        Companion helper = NewCompanion(owner, null, eRealm.Albion, temporary: true);
        AddDirect(group, helper);

        Assert.That(group.EnableCompanionRaid(owner, capacity), Is.True);
        Assert.That(CompanionRaid.IsMember(helper), Is.True);

        Companion persistent = NewCompanion(owner,
            NewRecord(owner.ObjectId, "raid-persistent-" + capacity, "Persistent Raid Check", eCharacterClass.Cleric),
            eRealm.Albion);
        Assert.That(group.AddMember(persistent), Is.False);
        Assert.That(CompanionRaid.IsMember(persistent), Is.False);
        Assert.That(group.MemberCount, Is.EqualTo(2));

        for (int i = group.MemberCount; i < capacity; i++)
            AddDirect(group, NewCompanion(owner, null, eRealm.Albion, temporary: true));
        Assert.That(group.MemberCount, Is.EqualTo(capacity));
        Assert.That(group.AddMember(NewCompanion(owner, null, eRealm.Albion, temporary: true)), Is.False,
            "Temporary helpers can fill either raid capacity, but the group cannot exceed it.");

        Owner mixedOwner = NewOwner("mixed-raid-owner-" + capacity, eRealm.Albion);
        QuietGroup mixedGroup = GroupWithOwner(mixedOwner);
        AddDirect(mixedGroup, NewCompanion(mixedOwner,
            NewRecord(mixedOwner.ObjectId, "existing-persistent-" + capacity, "Already Grouped", eCharacterClass.Cleric),
            eRealm.Albion));
        Assert.That(mixedGroup.EnableCompanionRaid(mixedOwner, capacity), Is.False,
            "A group containing a persistent recruit cannot be converted into a temporary-helper raid.");

        Owner autonomousOwner = NewOwner("autonomous-raid-owner-" + capacity, eRealm.Albion);
        QuietGroup autonomousGroup = GroupWithOwner(autonomousOwner);
        Companion autonomous = NewCompanion(autonomousOwner, null, eRealm.Albion);
        SetProperty(typeof(GameBot), autonomous, nameof(GameBot.IsAutonomousWorldBot), true);
        AddDirect(autonomousGroup, autonomous);
        Assert.That(autonomousGroup.EnableCompanionRaid(autonomousOwner, capacity), Is.False,
            "Autonomous world bots are separate from owner-managed temporary raid helpers.");
    }

    [Test]
    public void PersistentCompanionsAcceptNpcXpButRejectPlayerXpAndRealmPoints()
    {
        int previousCap = DOL.GS.ServerProperties.Properties.XP_CAP_PERCENT;
        DOL.GS.ServerProperties.Properties.XP_CAP_PERCENT = 125;
        try
        {
            Owner owner = NewOwner("reward-owner", eRealm.Albion);
            owner.Experience = 10_000;
            PlayerCompanionRecord record = NewRecord(owner.ObjectId, Guid.NewGuid().ToString("D"),
                "Reward Companion", eCharacterClass.Cleric);
            Assert.That(_database.AddObject(record), Is.True);
            Companion companion = NewCompanion(owner, record, eRealm.Albion);
            SetField(typeof(GameObject), companion, "m_level", (byte)1);
            long startingExperience = companion.Experience;

            companion.GainExperience(new GainedExperienceEventArgs(
                1, 0, 0, 0, 0, 0, false, false, eXPSource.NPC));
            long afterNpcExperience = companion.Experience;
            companion.GainExperience(new GainedExperienceEventArgs(
                100, 0, 0, 0, 0, 0, false, false, eXPSource.Player));
            companion.GainExperience(new GainedExperienceEventArgs(
                100, 0, 0, 0, 0, 0, false, false, eXPSource.Quest));
            RemovePendingProgress(record.CompanionId);
            companion.GainRealmPoints(1000);

            Assert.Multiple(() =>
            {
                Assert.That(afterNpcExperience, Is.EqualTo(startingExperience + 1),
                    "Persistent companions progress from NPC experience.");
                Assert.That(companion.Experience, Is.EqualTo(afterNpcExperience),
                    "Quest and player-sourced XP do not progress persistent companions.");
                Assert.That(companion.AutonomousRealmPoints, Is.Zero,
                    "Persistent companions do not earn realm points.");
            });
        }
        finally
        {
            DOL.GS.ServerProperties.Properties.XP_CAP_PERCENT = previousCap;
        }
    }

    [Test]
    public void LowLevelCompanionKillXpUsesOwnCapRateAndCatchUpBoost()
    {
        double previousRate = DOL.GS.ServerProperties.Properties.XP_RATE;
        int previousCap = DOL.GS.ServerProperties.Properties.XP_CAP_PERCENT;
        try
        {
            DOL.GS.ServerProperties.Properties.XP_RATE = 10;
            DOL.GS.ServerProperties.Properties.XP_CAP_PERCENT = 125;
            MethodInfo calculate = typeof(GameBot).GetMethod("CalculateCompanionNpcExperience",
                BindingFlags.Static | BindingFlags.NonPublic);
            long ownCap = (long)(GameLiving.XPForLiving[1] * 1.25);
            Assert.That(calculate.Invoke(null, new object[] { 1_000L, 1, 10, true }),
                Is.EqualTo((long)(ownCap * 10 * 1.5)));
            Assert.That(calculate.Invoke(null, new object[] { 1_000L, 1, 5, true }),
                Is.EqualTo(ownCap * 10), "The catch-up boost ends within four levels of the owner.");
            Assert.That(calculate.Invoke(null, new object[] { 5L, 1, 10, true }),
                Is.EqualTo(75L), "The owner's smaller award remains the limit before scaling.");
        }
        finally
        {
            DOL.GS.ServerProperties.Properties.XP_RATE = previousRate;
            DOL.GS.ServerProperties.Properties.XP_CAP_PERCENT = previousCap;
        }
    }

    private static void RemovePendingProgress(string companionId)
    {
        FieldInfo pending = typeof(PlayerCompanionProgressPersistence).GetField("Pending", Hidden | BindingFlags.Static);
        ((System.Collections.IDictionary)pending.GetValue(null)).Remove(companionId);
    }

    [Test]
    public void CompanionManagerRecruitsOnlyThroughCurrentSessionRowsAndActions()
    {
        Owner owner = NewOwner("manager-owner", eRealm.Midgard);
        CompanionManager.Open(owner);
        Assert.That(CompanionManager.TryGetSession(owner, out CompanionManagerSession session), Is.True);
        Assert.That(session.SentLabels[CompanionManagerProtocol.LabelListIndicator], Is.EqualTo("Roster empty"));

        CompanionManager.HandleClientControl(owner, CompanionManagerProtocol.FormatToken(session.Revision), "31");
        Assert.That(session.Tab, Is.EqualTo(CompanionManagerTab.Recruit));
        Assert.That(session.Recruit.Realm, Is.EqualTo(eRealm.Midgard), "Recruit starts on the player's realm");

        CompanionManager.SetQuery(owner, "shaman");
        string generatedShaman = $"g:{(int)eRealm.Midgard}:{(int)eCharacterClass.Shaman}";
        Assert.That(session.RowKeys.Where(key => key != null),
            Is.EqualTo(new[] { "a:midgard-shaman-brakka", "a:midgard-shaman-kiri", generatedShaman }));

        CompanionManager.HandleClientControl(owner, CompanionManagerProtocol.FormatToken(session.Revision), "01");
        Assert.That(session.Recruit.SelectedKey, Is.EqualTo("a:midgard-shaman-kiri"));
        Assert.That(session.SentLabels[CompanionManagerProtocol.LabelActionBase], Is.EqualTo("[Recruit]"));
        string details = string.Join(' ', Enumerable.Range(CompanionManagerProtocol.LabelDetailBase,
                2 * CompanionManagerProtocol.DetailLines).Select(index => session.SentLabels[index])
            .Where(text => !string.IsNullOrEmpty(text)));
        Assert.That(details, Does.StartWith(CompanionManager.StoryCopy), "The agreed copy is shown in full");
        Assert.That(session.SentLabels[CompanionManagerProtocol.LabelHeaderBase + 1], Is.EqualTo("Kiri"));

        ushort stale = (ushort)(session.Revision == 1 ? ushort.MaxValue : session.Revision - 1);
        CompanionManager.HandleClientControl(owner, CompanionManagerProtocol.FormatToken(stale), "20");
        Assert.That(PlayerCompanionRoster.GetRoster(owner), Is.Empty, "A stale click must not recruit");
        Assert.That(session.Message, Does.Contain("nothing was done"));

        CompanionManager.HandleClientControl(owner, CompanionManagerProtocol.FormatToken(session.Revision), "20");
        PlayerCompanionRecord kiri = PlayerCompanionRoster.GetRoster(owner).Single();
        Assert.Multiple(() =>
        {
            Assert.That(kiri.AuthoredRecruitKey, Is.EqualTo("midgard-shaman-kiri"));
            Assert.That(session.Tab, Is.EqualTo(CompanionManagerTab.Roster));
            Assert.That(session.Roster.SelectedKey, Is.EqualTo("c:" + kiri.CompanionId));
            Assert.That(session.SentLabels[CompanionManagerProtocol.LabelHeaderBase + 1], Is.EqualTo("Kiri"));
            Assert.That(session.SentLabels[CompanionManagerProtocol.LabelActionBase], Is.EqualTo("[Invite]"));
        });

        CompanionManager.HandleClientControl(owner, CompanionManagerProtocol.FormatToken(session.Revision), "31");
        Assert.That(session.Recruit.SelectedKey, Is.EqualTo("a:midgard-shaman-kiri"), "Selection is retained per tab");
        Assert.That(session.SentLabels[CompanionManagerProtocol.LabelActionBase], Is.EqualTo("[Open in roster]"));
        CompanionManager.HandleClientControl(owner, CompanionManagerProtocol.FormatToken(session.Revision), "20");
        Assert.That(PlayerCompanionRoster.GetRoster(owner), Has.Count.EqualTo(1), "An authored person is recruited once");
        Assert.That(PlayerCompanionRoster.TryRecruitAuthored(owner, "Kiri", out _, out string duplicate), Is.False, duplicate);

        CompanionManager.HandleClientControl(owner, CompanionManagerProtocol.FormatToken(session.Revision),
            CompanionManagerProtocol.ControlClose.ToString("x2"));
        Assert.That(CompanionManager.TryGetSession(owner, out _), Is.False);
    }

    [Test]
    public void CompanionManagerSessionsAreScopedToTheirOwner()
    {
        Owner first = NewOwner("manager-first", eRealm.Albion);
        Owner second = NewOwner("manager-second", eRealm.Albion);
        CompanionManager.Open(first);
        CompanionManager.TryGetSession(first, out CompanionManagerSession firstSession);
        CompanionManager.HandleClientControl(first, CompanionManagerProtocol.FormatToken(firstSession.Revision), "31");
        string token = CompanionManagerProtocol.FormatToken(firstSession.Revision);

        CompanionManager.HandleClientControl(second, token, "20");
        Assert.Multiple(() =>
        {
            Assert.That(PlayerCompanionRoster.GetRoster(first), Is.Empty);
            Assert.That(PlayerCompanionRoster.GetRoster(second), Is.Empty,
                "A click without the player's own session reopens the window and does nothing");
            Assert.That(CompanionManager.TryGetSession(second, out CompanionManagerSession secondSession), Is.True);
            Assert.That(secondSession, Is.Not.SameAs(firstSession));
            Assert.That(secondSession.Message, Does.Contain("nothing was changed"));
        });

        CompanionManager.HandleClientControl(first, "ZZZZ", "20");
        CompanionManager.HandleClientControl(first, token, "c0");
        Assert.That(PlayerCompanionRoster.GetRoster(first), Is.Empty, "Malformed controls are ignored");
    }

    private void InstallQuietGameBotLoggerQueue()
    {
        _gameBotLogger = (Logger)typeof(GameBot).GetField("log", BindingFlags.NonPublic | BindingFlags.Static)
            .GetValue(null);
        FieldInfo loggerQueue = typeof(Logger).GetField("_queueProcessor", Hidden);
        _previousGameBotLogQueue = loggerQueue.GetValue(_gameBotLogger);
        LogEntryQueueProcessor processor = new();
        _testLogEntries = new BlockingCollection<LogEntry>(new ConcurrentQueue<LogEntry>());
        typeof(LogEntryQueueProcessor).GetField("_loggingQueue", Hidden).SetValue(processor, _testLogEntries);
        loggerQueue.SetValue(_gameBotLogger, processor);
    }

    private static SqliteObjectDatabase Open(string path)
    {
        SqliteObjectDatabase database = new($"Data Source={path};Version=3;Pooling=False;");
        database.RegisterDataObject(typeof(PlayerCompanionRecord));
        database.RegisterDataObject(typeof(DbInventoryItem));
        // GameObject initializes its process-wide data-quest cache against the active server database.
        database.RegisterDataObject(typeof(DbDataQuest));
        return database;
    }

    private static Owner NewOwner(string id, eRealm realm, ushort region = 65000, ICharacterClass characterClass = null)
    {
        Owner owner = (Owner)RuntimeHelpers.GetUninitializedObject(typeof(Owner));
        owner.Configure(id, realm, region, characterClass);
        return owner;
    }

    private static PlayerCompanionRecord NewRecord(string ownerId, string id, string name, eCharacterClass characterClass) =>
        new()
        {
            CompanionId = id,
            OwnerCharacterId = ownerId,
            Name = name,
            Realm = (int)eRealm.Albion,
            ClassId = (int)characterClass,
            RaceId = (int)eRace.Briton,
            GenderId = (int)eGender.Female,
            Level = 1,
            SerializedSpecs = string.Empty,
            SerializedBuildPlan = string.Empty,
            TrainingMode = "manual",
            TrainingPlanId = string.Empty,
            SerializedEquipmentState = string.Empty,
            TacticalRole = string.Empty,
            EngagementPreference = string.Empty,
            RecruitType = "generated",
            AuthoredRecruitKey = string.Empty,
            PersonalityKey = "steady",
            IsActive = false,
            InventoryInitialized = false,
            StateVersion = 1,
            CreatedUtc = "stage6",
            UpdatedUtc = "stage6",
            Dirty = true,
        };

    private static Companion NewCompanion(Owner owner, PlayerCompanionRecord record, eRealm realm, bool temporary = false)
    {
        Companion companion = (Companion)RuntimeHelpers.GetUninitializedObject(typeof(Companion));
        companion.Name = record?.Name ?? "Temporary Helper";
        companion.Realm = realm;
        companion.RegionId = 65000;
        companion.Alive = true;
        companion.ObjectState = GameObject.eObjectState.Active;
        SetProperty(typeof(GameBot), companion, nameof(GameBot.Owner), owner);
        SetProperty(typeof(GameBot), companion, nameof(GameBot.PlayerCompanionRecord), record);
        SetProperty(typeof(GameBot), companion, nameof(GameBot.IsTemporaryGroupHelper), temporary);
        SetProperty(typeof(GameBot), companion, nameof(GameBot.IsAutonomousWorldBot), false);
        return companion;
    }

    private static QuietGroup GroupWithOwner(Owner owner)
    {
        QuietGroup group = new(owner);
        AddDirect(group, owner);
        return group;
    }

    private static void AddDirect(Group group, GameLiving living)
    {
        List<GameLiving> members = (List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden).GetValue(group);
        living.Group = group;
        living.GroupIndex = (byte)members.Count;
        members.Add(living);
    }

    private static void SetProperty(Type type, object instance, string name, object value) =>
        type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(instance, value);

    private static void SetField(Type type, object instance, string name, object value) =>
        type.GetField(name, Hidden).SetValue(instance, value);

    private static bool GetBooleanPolicy(GameBot companion, string name) =>
        (bool)typeof(GameBot).GetProperty(name, Hidden).GetValue(companion);

    private static string InventoryItemsOwner(string ownerId)
    {
        BotInventory inventory = new(ownerId);
        DbInventoryItem item = new();
        Assert.That(inventory.AddItemWithoutDbAddition(eInventorySlot.FirstBackpack, item), Is.True);
        return item.OwnerID;
    }

    private static void DeleteTemporaryFile(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
