using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.Database;
using DOL.Database.Handlers;
using DOL.GS;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// R3a of the RvR mode: keeps by realm (docs/RVR_KEEPS.md). A lord kill flips
/// a campaign keep to the killer's realm and the capture is saved; only a guild
/// of the keep's realm claims it; guards and portal keeps are friends of their
/// realm and enemies of the others; Darkness Falls goes to the realm with the
/// most keeps. Every world-dependent case runs in both worlds: the Camlann world
/// keeps its guild campaign answers.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_RvrKeeps
{
    private const ushort FrontierRegion = 1;
    private const ushort CampaignRegion = 165;

    // ---- doubles ----------------------------------------------------------------

    private sealed class Server : GameServer
    {
        public static IObjectDatabase Db;
        public static IServerRules Rules;
        private static readonly GameServerConfiguration Pvp = new() { ServerType = EGameServerType.GST_PvP };
        protected override IObjectDatabase DataBaseImpl => Db;
        protected override IServerRules ServerRulesImpl => Rules;
        public override GameServerConfiguration Configuration => Pvp;
    }

    private sealed class GuildServer : GameServer
    {
        private static readonly IObjectDatabase Empty = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private static readonly GameServerConfiguration Normal = new() { ServerType = EGameServerType.GST_Normal };
        protected override IObjectDatabase DataBaseImpl => Empty;
        public override GameServerConfiguration Configuration => Normal;
    }

    private sealed class ServerScope : IDisposable
    {
        private readonly GameServer _previous = GameServer.Instance;

        public ServerScope(IServerRules rules, IObjectDatabase database = null)
        {
            // Guild's static constructor reads the server type; on PvP it writes the dummy guild.
            GameServer.LoadTestDouble((GuildServer)RuntimeHelpers.GetUninitializedObject(typeof(GuildServer)));
            RuntimeHelpers.RunClassConstructor(typeof(Guild).TypeHandle);
            Server.Rules = rules;
            Server.Db = database ?? DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }

        public void Dispose()
        {
            GameServer.LoadTestDouble(_previous);
            Server.Rules = null;
            Server.Db = null;
        }
    }

    private sealed class TestBot : GameBot
    {
        public TestBot() : base((OfflineWorldBotRecord)null) { }

        public override bool IsAlive => true;
        public override byte Level { get; set; } = 50;
        public override eRealm Realm { get; set; }
        public override ushort CurrentRegionID { get; set; } = FrontierRegion;
        public override string Name { get; set; } = "Bot";
    }

    private sealed class PvpKeeps : DefaultKeepManager
    {
        protected override bool IsPvpServer => true;
    }

    private sealed class Db : SqliteObjectDatabase
    {
        public Db(string connectionString) : base(connectionString) { }
    }

    private static TestBot Bot(eRealm realm, Guild guild = null)
    {
        var bot = (TestBot)RuntimeHelpers.GetUninitializedObject(typeof(TestBot));
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, new ArrayList());
        typeof(GameBot).GetField("_dummyClient", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, new BotDummyClient());
        typeof(GameBot).GetField("_dummyLib", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, new BotDummyPacketLib());
        bot.Realm = realm;
        bot.Guild = guild;
        return bot;
    }

    private static Guild MakeGuild(string id, string name, eRealm realm) =>
        new(new DbGuild { GuildID = id, GuildName = name, Realm = (byte)realm });

    private static GameKeep Keep(eRealm realm, Guild guild, ushort region = FrontierRegion, int baseLevel = 50,
        bool lordDefeated = false, byte level = 1) =>
        new()
        {
            DBKeep = new DbKeep
            {
                Name = "Test keep", KeepID = 9101, Region = region, Realm = (byte)realm, OriginalRealm = (byte)realm,
                BaseLevel = (byte)baseLevel, Level = level, LordDefeated = lordDefeated,
                ClaimedGuildName = guild?.Name ?? string.Empty
            },
            Guild = guild
        };

    private static GameKeepComponent ComponentOf(AbstractGameKeep keep)
    {
        var component = (GameKeepComponent)RuntimeHelpers.GetUninitializedObject(typeof(GameKeepComponent));
        component.Keep = keep;
        return component;
    }

    private static GameKeepDoor Gate(AbstractGameKeep keep) => new() { Component = ComponentOf(keep) };

    private static GameKeepGuard Guard(AbstractGameKeep keep, eRealm realm)
    {
        var guard = (GameKeepGuard)RuntimeHelpers.GetUninitializedObject(typeof(GameKeepGuard));
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(guard, new ArrayList());
        guard.Component = ComponentOf(keep);
        guard.Realm = realm;
        return guard;
    }

    private static GuardLord LordOf(GameKeepComponent component)
    {
        var lord = (GuardLord)RuntimeHelpers.GetUninitializedObject(typeof(GuardLord));
        typeof(GameLiving).GetField(nameof(GameLiving.XpGainersLock), BindingFlags.Instance | BindingFlags.Public)
            .SetValue(lord, new Lock());
        typeof(GameLiving).GetField("m_xpGainers", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(lord, new Dictionary<GameLiving, double>());
        lord.Component = component;
        return lord;
    }

    // ---- a keep in a real save file ---------------------------------------------

    private sealed class SavedKeep : IDisposable
    {
        private readonly ServerScope _scope;
        public string Path { get; }
        public GameKeep Keep { get; }
        public DbKeep Row { get; }

        public SavedKeep(IServerRules rules, eRealm realm, Guild holder, ushort region = FrontierRegion, byte level = 1,
            bool lordDefeated = false)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "daoc-rvr-keep-" + Guid.NewGuid().ToString("N") + ".sqlite3");
            var database = new Db($"Data Source={Path};Version=3;Pooling=False;Journal Mode=WAL;Synchronous=Normal;Foreign Keys=True;Default Timeout=60");
            foreach (Type type in new[] { typeof(DbKeep), typeof(DbKeepComponent), typeof(DbDoor), typeof(DbNews) })
                database.RegisterDataObject(type);
            _scope = new ServerScope(rules, database);

            Row = new DbKeep
            {
                Name = "Test keep", KeepID = 9102, Region = region, Realm = (byte)realm, OriginalRealm = (byte)realm,
                BaseLevel = 50, Level = level, LordDefeated = lordDefeated, ClaimedGuildName = holder?.Name ?? string.Empty,
                ProgressionInitialized = holder != null && !PvpKeepCampaign.IsGarrison(holder)
            };
            Assert.That(database.AddObject(Row), Is.True);
            Keep = new GameKeep { DBKeep = Row, Guild = holder, InternalID = Row.ObjectId };
            holder?.ClaimedKeeps.Add(Keep);
            typeof(AbstractGameKeep).GetMethod("InitialiseTimers", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(Keep, null);

            for (int i = 0; i < 3; i++)
            {
                var componentRow = new DbKeepComponent { KeepID = Row.KeepID, ID = i, X = i * 100, Y = 0, Heading = 0, Health = 1000, Skin = 0, CreateInfo = string.Empty };
                Assert.That(database.AddObject(componentRow), Is.True);
                Keep.KeepComponents.Add(new GameKeepComponent { Keep = Keep, InternalID = componentRow.ObjectId, ID = i });
            }
            var doorRow = new DbDoor { Name = "Test gate", Type = 0, Health = 10000, Level = level, Realm = (byte)realm, State = 0 };
            Assert.That(database.AddObject(doorRow), Is.True);
            Keep.Doors.Add("gate", new GameKeepDoor { Component = Keep.KeepComponents[0], DbDoor = doorRow, Health = 2000 });
        }

        public DbKeep Reload()
        {
            var reopened = new Db($"Data Source={Path};Version=3;Pooling=False;Journal Mode=WAL;Synchronous=Normal;Foreign Keys=True;Default Timeout=60");
            reopened.RegisterDataObject(typeof(DbKeep));
            return reopened.SelectAllObjects<DbKeep>().Single();
        }

        public void Dispose()
        {
            _scope.Dispose();
            foreach (string suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(Path + suffix)) File.Delete(Path + suffix);
        }
    }

    private static Guild Wardens() => MakeGuild("wardens", PvpKeepCampaign.GarrisonName, eRealm.None);

    // ---- capture -------------------------------------------------------------------

    [Test]
    public void AWardenKeepTakenByMidgardBecomesMidgardsAndTheCaptureIsSaved()
    {
        using var world = BothWorlds.Use(WorldModelKind.Realm);
        using var saved = new SavedKeep(BothWorlds.Rules(WorldModelKind.Realm), eRealm.None, Wardens());

        bool captured = PvpKeepCampaign.CaptureForRealm(saved.Keep, eRealm.Midgard, Bot(eRealm.Midgard));

        DbKeep row = saved.Reload();
        Assert.Multiple(() =>
        {
            Assert.That(captured, Is.True);
            Assert.That(saved.Keep.Realm, Is.EqualTo(eRealm.Midgard));
            Assert.That(saved.Keep.Guild, Is.Null, "the Wardens are released");
            // Port session S2: upstream's capture level (starting_keep_level 4), not the fork's level 1.
            Assert.That(saved.Keep.Level, Is.EqualTo(PvpKeepCampaign.RvrCaptureLevel), "a realm keep without a claim stands at level 4");
            Assert.That(saved.Keep.Doors["gate"].Realm, Is.EqualTo(eRealm.Midgard), "the gate is Midgard's");
            Assert.That(row.Realm, Is.EqualTo((byte)eRealm.Midgard), "the realm is in the save");
            Assert.That(row.ClaimedGuildName, Is.Empty);
            Assert.That(row.LordDefeated, Is.True, "the keep waits at the steward for a Midgard guild");
            Assert.That(row.Level, Is.EqualTo(PvpKeepCampaign.RvrCaptureLevel));
            Assert.That(PvpKeepCampaign.NeedsGarrison(WorldModelKind.Realm, row.LordDefeated, (eRealm)row.Realm), Is.False,
                "a restart never hands the capture back to the Wardens");
        });
    }

    [Test]
    public void ACapturedKeepLosesItsGuildClaimAndItsLevel()
    {
        using var world = BothWorlds.Use(WorldModelKind.Realm);
        Guild albionGuild = MakeGuild("alb", "Lions of Albion", eRealm.Albion);
        using var saved = new SavedKeep(BothWorlds.Rules(WorldModelKind.Realm), eRealm.Albion, albionGuild, level: 5);

        Assert.That(PvpKeepCampaign.CaptureForRealm(saved.Keep, eRealm.Hibernia, Bot(eRealm.Hibernia)), Is.True);

        DbKeep row = saved.Reload();
        Assert.Multiple(() =>
        {
            Assert.That(saved.Keep.Realm, Is.EqualTo(eRealm.Hibernia));
            Assert.That(saved.Keep.Guild, Is.Null);
            Assert.That(albionGuild.ClaimedKeeps, Does.Not.Contain(saved.Keep), "the previous claim is removed");
            Assert.That(row.ClaimedGuildName, Is.Empty);
            Assert.That(row.Realm, Is.EqualTo((byte)eRealm.Hibernia));
            // Port session S2: upstream's capture level 4 until a Hibernian guild claims it (then 5).
            Assert.That(row.Level, Is.EqualTo(PvpKeepCampaign.RvrCaptureLevel), "level 4 until a Hibernian guild claims it");
            Assert.That(row.LordDefeated, Is.True);
        });
    }

    // R6: a guild that gives up its keep leaves it at the world's starting level: RvR upstream's 4, Camlann the property.
    [TestCase(WorldModelKind.Realm, 4, TestName = "RvR: a released keep drops to upstream's 4, never to 1")]
    [TestCase(WorldModelKind.Camlann, 1, TestName = "Camlann: a released keep drops to starting_keep_level")]
    public void AReleasedKeepDropsToTheWorldsStartingLevel(WorldModelKind world, int expected)
    {
        int saved = DOL.GS.ServerProperties.Properties.STARTING_KEEP_LEVEL;
        DOL.GS.ServerProperties.Properties.STARTING_KEEP_LEVEL = 1;
        try
        {
            using var _ = BothWorlds.Use(world);
            // Guild's static constructor needs the test server (ServerScope); run alone, nothing else has built it yet.
            using (new ServerScope(BothWorlds.Rules(world))) { }
            Guild albionGuild = MakeGuild("alb-release", "Lions of Albion", eRealm.Albion);
            using var saved5 = new SavedKeep(BothWorlds.Rules(world), eRealm.Albion, albionGuild, level: 5);

            saved5.Keep.Release();

            Assert.Multiple(() =>
            {
                Assert.That(saved5.Keep.Guild, Is.Null);
                Assert.That(saved5.Keep.Level, Is.EqualTo(expected));
                Assert.That(saved5.Keep.Realm, Is.EqualTo(eRealm.Albion), "a release keeps the realm");
            });
        }
        finally { DOL.GS.ServerProperties.Properties.STARTING_KEEP_LEVEL = saved; }
    }

    [Test]
    public void TheOwnRealmOrARealmLessKillerCapturesNothing()
    {
        using var world = BothWorlds.Use(WorldModelKind.Realm);
        using var saved = new SavedKeep(BothWorlds.Rules(WorldModelKind.Realm), eRealm.Albion, null, lordDefeated: true);

        Assert.Multiple(() =>
        {
            Assert.That(PvpKeepCampaign.CaptureForRealm(saved.Keep, eRealm.Albion, Bot(eRealm.Albion)), Is.False);
            Assert.That(PvpKeepCampaign.CaptureForRealm(saved.Keep, eRealm.None, null), Is.False);
            Assert.That(saved.Keep.Realm, Is.EqualTo(eRealm.Albion));
            Assert.That(saved.Reload().Realm, Is.EqualTo((byte)eRealm.Albion));
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void ALordKillFlipsTheRealmOnlyInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        Guild albionGuild = MakeGuild("alb", "Lions of Albion", eRealm.Albion);
        using var saved = new SavedKeep(rules, eRealm.Albion, albionGuild, region: CampaignRegion, level: 3);

        rules.ResetKeep(LordOf(saved.Keep.KeepComponents[0]), Bot(eRealm.Midgard));

        DbKeep row = saved.Reload();
        Assert.Multiple(() =>
        {
            Assert.That(saved.Keep.Guild, Is.Null, "both worlds release the claim");
            Assert.That(row.LordDefeated, Is.True, "both worlds wait at the steward");
            Assert.That(saved.Keep.Realm, Is.EqualTo(BothWorlds.Expect(world, eRealm.Albion, eRealm.Midgard)),
                "Camlann keeps the realm until a guild claims; RvR flips to the killer's realm at once");
            Assert.That(row.Realm, Is.EqualTo((byte)BothWorlds.Expect(world, eRealm.Albion, eRealm.Midgard)));
        });
    }

    [TestCase(eRealm.None, true, eRealm.Midgard, true, TestName = "A realm takes a Warden keep")]
    [TestCase(eRealm.Albion, true, eRealm.Albion, true, TestName = "A stale Warden realm is no claim")]
    [TestCase(eRealm.None, false, eRealm.Hibernia, true, TestName = "A realm takes a neutral keep")]
    [TestCase(eRealm.Albion, false, eRealm.Midgard, true, TestName = "A realm takes another realm's keep")]
    [TestCase(eRealm.Albion, false, eRealm.Albion, false, TestName = "A keep never changes to its own realm")]
    [TestCase(eRealm.Albion, false, eRealm.None, false, TestName = "A realm-less killer takes nothing")]
    public void CaptureChangesOwnerRule(eRealm keepRealm, bool garrison, eRealm captor, bool changes)
    {
        Assert.That(PvpKeepCampaign.CaptureChangesOwner(keepRealm, garrison, captor), Is.EqualTo(changes));
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void TheCapturingRealmIsTheRealmTheKillerFightsFor(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        using var scope = new ServerScope(BothWorlds.Rules(world));

        Assert.Multiple(() =>
        {
            Assert.That(PvpKeepCampaign.CapturingRealm(Bot(eRealm.Hibernia)), Is.EqualTo(eRealm.Hibernia));
            Assert.That(PvpKeepCampaign.CapturingRealm(Bot(eRealm.None)), Is.EqualTo(eRealm.None));
            Assert.That(PvpKeepCampaign.CapturingRealm(null), Is.EqualTo(eRealm.None));
        });
    }

    // ---- garrison and start ----------------------------------------------------------

    [TestCase(WorldModelKind.Camlann, false, eRealm.None, true, TestName = "Camlann: an unheld keep goes to the Wardens")]
    [TestCase(WorldModelKind.Camlann, false, eRealm.Albion, true, TestName = "Camlann: whatever its realm")]
    [TestCase(WorldModelKind.Camlann, true, eRealm.None, false, TestName = "Camlann: not while its lord is down")]
    [TestCase(WorldModelKind.Realm, false, eRealm.None, true, TestName = "RvR: a neutral keep goes to the Wardens")]
    [TestCase(WorldModelKind.Realm, false, eRealm.Midgard, false, TestName = "RvR: a relic or realm keep keeps its realm")]
    [TestCase(WorldModelKind.Realm, true, eRealm.Midgard, false, TestName = "RvR: a captured keep awaiting a claim stays its realm's")]
    public void NeedsGarrisonRule(WorldModelKind world, bool lordDefeated, eRealm realm, bool garrison)
    {
        Assert.That(PvpKeepCampaign.NeedsGarrison(world, lordDefeated, realm), Is.EqualTo(garrison));
    }

    [TestCase(WorldModelKind.Realm, true, eRealm.None, eRealm.Hibernia, eRealm.Hibernia, TestName = "RvR: a realm-less portal keep takes its original realm")]
    [TestCase(WorldModelKind.Realm, true, eRealm.Midgard, eRealm.Midgard, null, TestName = "RvR: a portal keep of its realm stays")]
    [TestCase(WorldModelKind.Realm, false, eRealm.None, eRealm.Hibernia, null, TestName = "RvR: an ordinary keep stays neutral")]
    [TestCase(WorldModelKind.Camlann, true, eRealm.None, eRealm.Hibernia, null, TestName = "Camlann: portal keeps keep their row")]
    public void PortalKeepStartRealmRule(WorldModelKind world, bool portal, eRealm realm, eRealm original, eRealm? start)
    {
        Assert.That(PvpKeepCampaign.PortalKeepStartRealm(world, portal, realm, original), Is.EqualTo(start));
    }

    // ---- review fixes: combat, open BG lord state, steward, status text ---------------------

    [TestCase(WorldModelKind.Camlann, true, true, true, false, TestName = "Camlann: a defeated lord opens the steward in combat")]
    [TestCase(WorldModelKind.Camlann, true, true, false, true, TestName = "Camlann: no claim in combat before the lord falls")]
    [TestCase(WorldModelKind.Camlann, true, false, false, true, TestName = "Camlann: no claim of a non-campaign keep in combat")]
    [TestCase(WorldModelKind.Realm, true, true, true, true, TestName = "RvR: no claim of a captured keep in combat")]
    [TestCase(WorldModelKind.Realm, false, true, true, false, TestName = "RvR: a captured keep out of combat is claimable")]
    public void ClaimBlockedByCombatRule(WorldModelKind world, bool inCombat, bool campaign, bool lordDefeated, bool blocked)
    {
        Assert.That(PvpKeepCampaign.ClaimBlockedByCombat(world, inCombat, campaign, lordDefeated), Is.EqualTo(blocked));
    }

    [TestCase(WorldModelKind.Camlann, false, true, false, true, TestName = "Camlann: the lord stays down until a claim")]
    [TestCase(WorldModelKind.Camlann, false, true, true, true, TestName = "Camlann: the saved flag decides")]
    [TestCase(WorldModelKind.Camlann, true, true, false, false, TestName = "Camlann: a claimed keep is not lord-down")]
    [TestCase(WorldModelKind.Realm, false, true, true, false, TestName = "RvR: a captured keep with its lord standing is not lord-down")]
    [TestCase(WorldModelKind.Realm, false, true, false, true, TestName = "RvR: the lord really lies dead")]
    [TestCase(WorldModelKind.Realm, false, false, false, false, TestName = "RvR: a keep not awaiting a claim is not lord-down")]
    public void OpenBattlegroundLordDownRule(WorldModelKind world, bool claimed, bool lordDefeated, bool lordStands, bool lordDown)
    {
        Assert.That(PvpKeepCampaign.LordDownUnclaimed(world, claimed, lordDefeated, lordStands), Is.EqualTo(lordDown));
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void TheStewardStandsBesideTheLordOnlyInTheRvrWorld(WorldModelKind world)
    {
        (int x, int y) = KeepClaimPoint.StewardSpot(world, 10000, 20000, 1024);
        double distance = Math.Sqrt(Math.Pow(x - 10000, 2) + Math.Pow(y - 20000, 2));

        Assert.That(distance, Is.EqualTo(BothWorlds.Expect(world, 0d, KeepClaimPoint.StewardLordDistance)).Within(2d));
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void ACapturedKeepReadsAwaitingAClaimInTheRvrWorld(WorldModelKind world)
    {
        string line = BattlegroundKeepOwnership.Describe(world, "Thidranki Faste", eRealm.Midgard, null, true, true);

        Assert.Multiple(() =>
        {
            Assert.That(line, Is.EqualTo(BothWorlds.Expect(world, "Thidranki Faste: Midgard - lord defeated, unclaimed",
                "Thidranki Faste: Midgard - captured, awaiting claim")));
            Assert.That(DOL.GS.Commands.OpenBgWindowScreen.BannerText(new OpenBgStatusSnapshot(DateTime.UtcNow, true, true, true, true,
                    null, null, null, 0, line, 1, 1, 0, [], [])),
                Is.EqualTo(BothWorlds.Expect(world, "Thidranki Faste: lord down, unclaimed",
                    "Thidranki Faste: Midgard holds it, awaiting a claim")));
        });
    }

    // ---- claims ----------------------------------------------------------------------

    [TestCase(eRealm.Midgard, eRealm.Midgard, eRealm.Midgard, true, TestName = "A guild of the keep's realm claims")]
    [TestCase(eRealm.Midgard, eRealm.Midgard, eRealm.None, true, TestName = "A realm-less guild claims through its member")]
    [TestCase(eRealm.Midgard, eRealm.Hibernia, eRealm.Hibernia, false, TestName = "Another realm's guild is refused")]
    [TestCase(eRealm.Midgard, eRealm.Midgard, eRealm.Hibernia, false, TestName = "A member of the realm in another realm's guild is refused")]
    [TestCase(eRealm.None, eRealm.Midgard, eRealm.Midgard, false, TestName = "A neutral keep is captured, not claimed")]
    public void RealmMayClaimRule(eRealm keepRealm, eRealm claimerRealm, eRealm guildRealm, bool allowed)
    {
        Assert.That(PvpKeepCampaign.RealmMayClaim(keepRealm, claimerRealm, guildRealm), Is.EqualTo(allowed));
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void AHibernianGuildCannotClaimAMidgardKeepInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        using var scope = new ServerScope(BothWorlds.Rules(world));
        GameKeep keep = Keep(eRealm.Midgard, null, lordDefeated: true);
        TestBot hibernian = Bot(eRealm.Hibernia, MakeGuild("hib", "Tir na Nog", eRealm.Hibernia));
        TestBot midgardian = Bot(eRealm.Midgard, MakeGuild("mid", "Jordheim", eRealm.Midgard));

        keep.CheckForClaim(hibernian, out string refusal);

        Assert.Multiple(() =>
        {
            Assert.That(PvpKeepCampaign.MayClaimInWorld(keep, hibernian), Is.EqualTo(BothWorlds.Expect(world, true, false)));
            Assert.That(PvpKeepCampaign.MayClaimInWorld(keep, midgardian), Is.True);
            Assert.That(PvpKeepCampaign.RealmClaimRefusal(keep, midgardian), Is.Null);
            Assert.That(refusal, BothWorlds.Expect(world,
                Is.Not.EqualTo(PvpKeepCampaign.RealmClaimRefusal(keep, hibernian)),
                Is.EqualTo("Test keep belongs to Midgard; only a guild of that realm can claim it.")));
        });
    }

    [Test]
    public void ANeutralKeepMustBeCapturedBeforeAnyClaim()
    {
        using var world = BothWorlds.Use(WorldModelKind.Realm);
        using var scope = new ServerScope(BothWorlds.Rules(WorldModelKind.Realm));
        GameKeep keep = Keep(eRealm.None, Wardens());

        Assert.That(PvpKeepCampaign.RealmClaimRefusal(keep, Bot(eRealm.Albion, MakeGuild("alb", "Lions", eRealm.Albion))),
            Is.EqualTo("Defeat the keep lord to win Test keep for your realm first."));
    }

    // ---- guards, gates and portal keeps --------------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void APortalKeepIsHostileToOtherRealmsOnlyInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        using var scope = new ServerScope(BothWorlds.Rules(world));
        var keeps = new PvpKeeps();
        GameKeepDoor gate = Gate(Keep(eRealm.Albion, null, baseLevel: 100));

        Assert.Multiple(() =>
        {
            Assert.That(keeps.IsEnemy(gate, Bot(eRealm.Albion)), Is.False, "the own realm is safe");
            Assert.That(keeps.IsEnemy(gate, Bot(eRealm.Midgard)), Is.EqualTo(BothWorlds.Expect(world, false, true)));
            Assert.That(keeps.IsEnemy(gate, Bot(eRealm.Hibernia)), Is.EqualTo(BothWorlds.Expect(world, false, true)));
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void ACapturedKeepAwaitingAClaimIsItsRealmsFriendInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        using var scope = new ServerScope(BothWorlds.Rules(world));
        var keeps = new PvpKeeps();
        GameKeepDoor gate = Gate(Keep(eRealm.Midgard, null, region: CampaignRegion, lordDefeated: true));

        Assert.Multiple(() =>
        {
            Assert.That(keeps.IsEnemy(gate, Bot(eRealm.Midgard)), Is.False);
            Assert.That(keeps.IsEnemy(gate, Bot(eRealm.Albion)), Is.EqualTo(BothWorlds.Expect(world, false, true)),
                "Camlann: a defeated keep is open to all until a guild claims it");
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void TheNeutralFrontierWardensAreHostileToEveryRealm(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        using var scope = new ServerScope(BothWorlds.Rules(world));
        var keeps = new PvpKeeps();
        GameKeepDoor gate = Gate(Keep(eRealm.None, Wardens()));

        Assert.Multiple(() =>
        {
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
                Assert.That(keeps.IsEnemy(gate, Bot(realm)), Is.True, realm.ToString());
        });
    }

    [Test]
    public void KeepGuardsShowTheirKeepsTrueRealmInTheRvrWorld()
    {
        using var world = BothWorlds.Use(WorldModelKind.Realm);
        IServerRules rules = BothWorlds.Rules(WorldModelKind.Realm);
        using var scope = new ServerScope(rules);
        var midgardian = new TestPlayerViewer { Realm = eRealm.Midgard };
        var albion = new TestPlayerViewer { Realm = eRealm.Albion };
        GameKeepGuard midgardGuard = Guard(Keep(eRealm.Midgard, null), eRealm.Midgard);
        GameKeepGuard warden = Guard(Keep(eRealm.None, Wardens()), eRealm.None);

        Assert.Multiple(() =>
        {
            Assert.That(rules.GetLivingRealm(midgardian, midgardGuard), Is.EqualTo((byte)eRealm.Midgard), "a friend of its realm");
            Assert.That(rules.GetLivingRealm(albion, midgardGuard), Is.EqualTo((byte)eRealm.Midgard), "an enemy of the others");
            Assert.That(rules.GetLivingRealm(albion, warden), Is.EqualTo((byte)eRealm.None), "Wardens are neutrals");
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void GuardsOfACapturedKeepAwaitingAClaimStillPayRealmPointsInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        using var scope = new ServerScope(BothWorlds.Rules(world));
        GameKeepGuard guard = Guard(Keep(eRealm.Midgard, null, region: CampaignRegion, lordDefeated: true), eRealm.Midgard);

        Assert.That(guard.RealmPointsValue, Is.EqualTo(BothWorlds.Expect(world, 0, PvpKeepCampaign.GuardRealmPoints)));
    }

    private sealed class TestPlayerViewer : GamePlayer
    {
        public TestPlayerViewer() : base(null, null) { }
        public override eRealm Realm { get; set; }
        public override GameClient Client { get; } = new BotDummyClient();
    }

    // ---- bots -----------------------------------------------------------------------------

    [TestCase(false, false, false, false, true, TestName = "Camlann keeps every keep as an objective")]
    [TestCase(true, true, false, false, true, TestName = "RvR: a hostile keep is an objective")]
    [TestCase(true, false, false, false, false, TestName = "RvR: an own-realm keep is never sieged")]
    [TestCase(true, false, true, true, true, TestName = "RvR: an own-realm keep awaiting the bot's claim is an objective")]
    [TestCase(true, false, true, false, false, TestName = "RvR: not when the bot may not claim it")]
    public void BotKeepObjectivesByRealm(bool rvr, bool hostile, bool awaitingClaim, bool mayClaim, bool objective)
    {
        Assert.That(AutonomousRvrKeepPolicy.IsRealmObjective(rvr, hostile, awaitingClaim, mayClaim), Is.EqualTo(objective));
    }

    // ---- Darkness Falls -------------------------------------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void DarknessFallsIsOpenToAllInCamlannAndToTheOwnerInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        EGameServerType branch = WorldMode.RulesBranch(EGameServerType.GST_PvP);

        Assert.Multiple(() =>
        {
            Assert.That(DFEnterJumpPoint.CanRealmEnter(branch, false, eRealm.Albion, eRealm.Albion, eRealm.None, false), Is.True);
            Assert.That(DFEnterJumpPoint.CanRealmEnter(branch, false, eRealm.Midgard, eRealm.Albion, eRealm.None, false),
                Is.EqualTo(BothWorlds.Expect(world, true, false)));
            Assert.That(DFEnterJumpPoint.CanRealmEnter(branch, false, eRealm.Midgard, eRealm.Albion, eRealm.Midgard, true), Is.True,
                "the previous owner keeps its grace period");
            Assert.That(DFEnterJumpPoint.CanRealmEnter(branch, false, eRealm.Albion, eRealm.None, eRealm.None, false),
                Is.EqualTo(BothWorlds.Expect(world, true, false)), "nobody owns Darkness Falls before the first capture");
            Assert.That(DFEnterJumpPoint.CanRealmEnter(branch, true, eRealm.Midgard, eRealm.Albion, eRealm.None, false), Is.True,
                "allow_all_realms_df opens it");
        });
    }

    [TestCase(eRealm.None, eRealm.Midgard, 0, 1, 0, eRealm.Midgard, TestName = "The first capture wins Darkness Falls")]
    [TestCase(eRealm.None, eRealm.Midgard, 0, 0, 0, eRealm.None, TestName = "A tower or relic keep counts nothing")]
    [TestCase(eRealm.Albion, eRealm.Midgard, 2, 2, 0, eRealm.Albion, TestName = "A tie leaves the owner")]
    [TestCase(eRealm.Albion, eRealm.Midgard, 2, 3, 0, eRealm.Midgard, TestName = "More keeps take it")]
    [TestCase(eRealm.Albion, eRealm.Midgard, 1, 3, 3, eRealm.Midgard, TestName = "Of two tied realms the taker wins")]
    [TestCase(eRealm.Albion, eRealm.Midgard, 1, 2, 4, eRealm.Hibernia, TestName = "The realm with the most keeps wins")]
    [TestCase(eRealm.Albion, eRealm.Hibernia, 0, 0, 0, eRealm.None, TestName = "An owner without keeps loses it")]
    [TestCase(eRealm.None, eRealm.None, 0, 0, 0, eRealm.None, TestName = "Startup: no keeps, no owner")]
    [TestCase(eRealm.None, eRealm.None, 1, 3, 2, eRealm.Midgard, TestName = "Startup: the realm with the most keeps")]
    [TestCase(eRealm.None, eRealm.None, 2, 2, 0, eRealm.Albion, TestName = "Startup: a tie goes to the first realm in order")]
    public void DarknessFallsOwner(eRealm owner, eRealm taker, int albion, int midgard, int hibernia, eRealm next)
    {
        Assert.That(DFEnterJumpPoint.NextOwner(owner, taker, albion, midgard, hibernia), Is.EqualTo(next));
    }
}
