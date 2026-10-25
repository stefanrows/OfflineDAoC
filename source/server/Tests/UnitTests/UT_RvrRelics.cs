using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.GameEvents;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// R3b of the RvR mode: relics on realm pads behind the 1.65 milegates, the
/// realm-wide upstream bonus, relic guards by realm and the bot raid seams.
/// Integration cases run in both worlds (spec section 9.2); Camlann keeps its
/// guild relic answers.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_RvrRelics
{
    // Old Frontiers keep IDs used below.
    private const int Benowyc = 50, Berkstead = 51, Erasleigh = 52, Boldiam = 53, Sursbrooke = 54, Hurbury = 55, Renaris = 56;
    private const int Excalibur = 57, Myrddin = 58;
    private const int Bledmeer = 75, Nottmoor = 76, Hlidskialf = 77, Blendrake = 78, Glenlock = 79;
    private const int Grallarhorn = 82, Mjollner = 198;

    private GameServer _previousServer;
    private GameRelic[] _previousRelics;
    private readonly List<GameRelicPad> _pads = [];
    private RelicKeeps _keeps;

    // ---- doubles ------------------------------------------------------------

    /// <summary>A database that accepts every write and reads nothing.</summary>
    public class AcceptAllDatabase : DispatchProxy
    {
        protected override object Invoke(MethodInfo method, object[] args)
        {
            Type type = method.ReturnType;
            if (type == typeof(void))
                return null;
            if (type == typeof(bool))
                return true;
            if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]));
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }

    private sealed class RelicServer : GameServer
    {
        internal static IServerRules Rules;
        private static readonly IObjectDatabase Db = DispatchProxy.Create<IObjectDatabase, AcceptAllDatabase>();
        protected override IObjectDatabase DataBaseImpl => Db;
        protected override IServerRules ServerRulesImpl => Rules;
    }

    private sealed class RelicKeeps : DefaultKeepManager
    {
        protected override bool IsPvpServer => true;
    }

    private sealed class TestPlayer : GamePlayer
    {
        public TestPlayer() : base(null, null) { }

        public override bool IsAlive => true;
        public override byte Level { get; set; } = 50;
        public override eRealm Realm { get; set; }
        public override GameClient Client { get; } = new BotDummyClient();
        public override bool IsInvulnerableToAttack => false;
        public override ushort CurrentRegionID { get; set; } = 1;
        public override string Name { get; set; } = "Tester";
    }

    private sealed class TestBot : GameBot
    {
        public TestBot() : base((OfflineWorldBotRecord)null) { }

        public override bool IsAlive => true;
        public override byte Level { get; set; } = 50;
        public override eRealm Realm { get; set; }
        public override ushort CurrentRegionID { get; set; } = 1;
    }

    [SetUp]
    public void SetUp()
    {
        _previousServer = GameServer.Instance;
        _previousRelics = RelicMgr.GetRelics();
        _keeps = new RelicKeeps();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameRelicPad pad in _pads)
            RelicMgr.RemoveRelicPad(pad);
        _pads.Clear();
        SetRelics(_previousRelics);
        GameServer.LoadTestDouble(_previousServer);
    }

    private void UseServer(WorldModelKind world)
    {
        RelicServer.Rules = BothWorlds.Rules(world);
        var server = (RelicServer)RuntimeHelpers.GetUninitializedObject(typeof(RelicServer));
        typeof(GameServer).GetField("m_keepManager", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(server, _keeps);
        GameServer.LoadTestDouble(server);
    }

    private static void SetRelics(GameRelic[] relics) =>
        typeof(RelicMgr).GetField("_relicsArray", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, relics);

    private GameRelicPad Pad(eRealm realm, eRelicType type, ushort region)
    {
        int emblem = (int)realm + (type == eRelicType.Magic ? 10 : 0);
        var pad = new GameRelicPad { Emblem = emblem, CurrentRegionID = region, Name = $"{realm} {type} pad" };
        RelicMgr.AddRelicPad(pad);
        _pads.Add(pad);
        return pad;
    }

    private static GameRelic Relic(int id, eRealm original, eRelicType type, GameRelicPad home)
    {
        var relic = new GameRelic(new DbRelic
        {
            RelicID = id, relicType = (int)type, Realm = (int)original, OriginalRealm = (int)original,
            LastRealm = (int)original, Region = home?.CurrentRegionID ?? 1,
        });
        if (home != null)
            Assert.That(relic.RelicPadTakesOver(home, true), Is.True, "the relic starts on its home pad");
        return relic;
    }

    private void Keep(int id, eRealm holder, eRealm original, bool relicKeep = false, Guild guild = null)
    {
        var keep = new GameKeep
        {
            DBKeep = new DbKeep { KeepID = id, Realm = (byte)holder, OriginalRealm = (byte)original, SkinType = (byte)(relicKeep ? 99 : 0), BaseLevel = 50 },
            Guild = guild,
        };
        _keeps.Keeps[id] = keep;
    }

    /// <summary>A fresh RvR world (R5a reset): frontier keeps neutral, relic keeps their realm's.</summary>
    private void FreshFrontier()
    {
        foreach (int id in new[] { Benowyc, Berkstead, Erasleigh, Boldiam, Sursbrooke, Hurbury, Renaris })
            Keep(id, eRealm.None, eRealm.Albion);
        foreach (int id in new[] { Bledmeer, Nottmoor, Hlidskialf, Blendrake, Glenlock, 80, 81 })
            Keep(id, eRealm.None, eRealm.Midgard);
        foreach (int id in new[] { 100, 101, 102, 103, 104, 105, 106 })
            Keep(id, eRealm.None, eRealm.Hibernia);
        Keep(Excalibur, eRealm.Albion, eRealm.Albion, relicKeep: true);
        Keep(Myrddin, eRealm.Albion, eRealm.Albion, relicKeep: true);
        Keep(Grallarhorn, eRealm.Midgard, eRealm.Midgard, relicKeep: true);
        Keep(Mjollner, eRealm.Midgard, eRealm.Midgard, relicKeep: true);
        Keep(110, eRealm.Hibernia, eRealm.Hibernia, relicKeep: true);
        Keep(111, eRealm.Hibernia, eRealm.Hibernia, relicKeep: true);
    }

    private eRealm Holder(int keepId) => RelicMgr.MilegateHolder(keepId);

    private static TestPlayer Player(eRealm realm) => new() { Realm = realm };

    private static TestBot WorldBot(eRealm realm)
    {
        var bot = (TestBot)RuntimeHelpers.GetUninitializedObject(typeof(TestBot));
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, new ArrayList());
        typeof(GameNPC).GetField("m_ownBrain", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, new BotBrain { Body = bot });
        typeof(GameBot).GetField("<IsAutonomousWorldBot>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, true);
        typeof(GameBot).GetField("_dummyClient", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, new BotDummyClient());
        bot.Realm = realm;
        return bot;
    }

    private sealed class GuildServer : GameServer
    {
        private static readonly IObjectDatabase Db = DispatchProxy.Create<IObjectDatabase, AcceptAllDatabase>();
        private static readonly GameServerConfiguration Normal = new() { ServerType = EGameServerType.GST_Normal };
        protected override IObjectDatabase DataBaseImpl => Db;
        public override GameServerConfiguration Configuration => Normal;
    }

    private static Guild MakeGuild(string id, string name, eRealm realm)
    {
        GameServer previous = GameServer.Instance;
        GameServer.LoadTestDouble((GuildServer)RuntimeHelpers.GetUninitializedObject(typeof(GuildServer)));
        try { RuntimeHelpers.RunClassConstructor(typeof(Guild).TypeHandle); }
        finally { GameServer.LoadTestDouble(previous); }
        var guild = (Guild)RuntimeHelpers.GetUninitializedObject(typeof(Guild));
        typeof(Guild).GetField("m_DBguild", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(guild, new DbGuild { GuildID = id, GuildName = name, Realm = (byte)realm });
        return guild;
    }

    // ---- pure rules: the milegate chains -----------------------------------

    [Test]
    public void EveryRealmTempleHasAFirstMilegateOfThreeOwnKeeps()
    {
        Assert.That(RvrRelicRules.Chains, Has.Count.EqualTo(6));
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            foreach (eRelicType type in new[] { eRelicType.Strength, eRelicType.Magic })
            {
                RvrRelicRules.TempleChain chain = RvrRelicRules.ChainFor(realm, type);
                Assert.That(chain, Is.Not.Null, $"{realm} {type}");
                Assert.That(chain.FirstGate, Has.Length.EqualTo(3));
                Assert.That(RvrRelicRules.ChainForRelicKeep(chain.RelicKeepId), Is.SameAs(chain));
            }
        }
        // Geography and the temple guard table: Renaris guards Excalibur, Hurbury Myrddin.
        Assert.That(RvrRelicRules.ChainFor(eRealm.Albion, eRelicType.Strength).RelicKeepId, Is.EqualTo(Excalibur));
        Assert.That(RvrRelicRules.ChainFor(eRealm.Albion, eRelicType.Strength).SecondGate, Is.EqualTo(Renaris));
        Assert.That(RvrRelicRules.ChainFor(eRealm.Albion, eRelicType.Magic).SecondGate, Is.EqualTo(Hurbury));
    }

    [Test]
    public void AFreshWorldSealsEveryTemple()
    {
        FreshFrontier();
        UseServer(WorldModelKind.Realm);
        foreach (RvrRelicRules.TempleChain chain in RvrRelicRules.Chains)
            Assert.That(RvrRelicRules.IsTempleOpen(chain.Realm, chain.Type, Holder), Is.False, chain.TempleName);
    }

    [TestCase(new[] { eRealm.Midgard, eRealm.Midgard, eRealm.None }, false, TestName = "Two of three taken: sealed")]
    [TestCase(new[] { eRealm.Midgard, eRealm.Hibernia, eRealm.Midgard }, true, TestName = "All three in enemy hands, mixed realms: open")]
    [TestCase(new[] { eRealm.Midgard, eRealm.Albion, eRealm.Midgard }, false, TestName = "One retaken by the home realm: sealed")]
    [TestCase(new[] { eRealm.Midgard, eRealm.Midgard, eRealm.Midgard }, true, TestName = "All three taken by one realm: open")]
    public void TheFirstMilegateOpensWhenItsThreeKeepsAreInEnemyHands(eRealm[] holders, bool open)
    {
        // Castle Excalibur's first milegate: Benowyc, Berkstead, Boldiam.
        var map = new Dictionary<int, eRealm> { [Benowyc] = holders[0], [Berkstead] = holders[1], [Boldiam] = holders[2] };
        eRealm Lookup(int id) => map.GetValueOrDefault(id, eRealm.None);

        Assert.That(RvrRelicRules.IsTempleOpen(eRealm.Albion, eRelicType.Strength, Lookup), Is.EqualTo(open));
        // The magic temple shares only Benowyc and stays sealed.
        Assert.That(RvrRelicRules.IsTempleOpen(eRealm.Albion, eRelicType.Magic, Lookup), Is.False);
    }

    [Test]
    public void TheSecondMilegateKeepNeitherOpensNorSealsTheTemple()
    {
        var map = new Dictionary<int, eRealm> { [Benowyc] = eRealm.Midgard, [Berkstead] = eRealm.Midgard, [Boldiam] = eRealm.Midgard, [Renaris] = eRealm.Albion };
        Assert.That(RvrRelicRules.IsTempleOpen(eRealm.Albion, eRelicType.Strength, id => map.GetValueOrDefault(id)), Is.True);
        map = new() { [Renaris] = eRealm.Midgard };
        Assert.That(RvrRelicRules.IsTempleOpen(eRealm.Albion, eRelicType.Strength, id => map.GetValueOrDefault(id)), Is.False);
    }

    [Test]
    public void AWardenKeepIsNobodysCaptureEvenWithAStaleRealmByte()
    {
        FreshFrontier();
        Guild wardens = MakeGuild("wardens", PvpKeepCampaign.GarrisonName, eRealm.None);
        Keep(Benowyc, eRealm.Midgard, eRealm.Albion, guild: wardens);
        Keep(Berkstead, eRealm.Midgard, eRealm.Albion);
        Keep(Boldiam, eRealm.Midgard, eRealm.Albion);
        UseServer(WorldModelKind.Realm);

        Assert.That(RelicMgr.MilegateHolder(Benowyc), Is.EqualTo(eRealm.None));
        Assert.That(RvrRelicRules.SealingKeeps(eRealm.Albion, eRelicType.Strength, Holder), Is.EqualTo(new[] { Benowyc }));
    }

    // ---- pure rules: pickup and bonus ---------------------------------------

    [TestCase(eRealm.Midgard, false, eRealm.None, eRealm.Albion, false, false, RvrRelicRules.PickupVerdict.Allowed, TestName = "A dropped relic is free")]
    [TestCase(eRealm.None, true, eRealm.Albion, eRealm.Albion, true, true, RvrRelicRules.PickupVerdict.NoRealm, TestName = "No realm, no relic")]
    [TestCase(eRealm.Albion, true, eRealm.Albion, eRealm.Albion, true, true, RvrRelicRules.PickupVerdict.OwnRealmHolds, TestName = "Own realm's pad")]
    [TestCase(eRealm.Midgard, true, eRealm.Albion, eRealm.Albion, false, true, RvrRelicRules.PickupVerdict.TempleSealed, TestName = "Sealed temple")]
    [TestCase(eRealm.Midgard, true, eRealm.Albion, eRealm.Albion, true, false, RvrRelicRules.PickupVerdict.OwnRelicMissing, TestName = "Own relic of the type missing")]
    [TestCase(eRealm.Midgard, true, eRealm.Albion, eRealm.Albion, true, true, RvrRelicRules.PickupVerdict.Allowed, TestName = "Open temple, own relic held")]
    [TestCase(eRealm.Albion, true, eRealm.Midgard, eRealm.Albion, true, false, RvrRelicRules.PickupVerdict.Allowed, TestName = "A realm wins back its own relic")]
    [TestCase(eRealm.Albion, true, eRealm.Midgard, eRealm.Albion, false, false, RvrRelicRules.PickupVerdict.TempleSealed, TestName = "Its own relic too waits for the milegate")]
    public void PickupVerdicts(eRealm taker, bool mounted, eRealm holder, eRealm original, bool templeOpen, bool holdsOwn,
        RvrRelicRules.PickupVerdict expected)
    {
        Assert.That(RvrRelicRules.Pickup(taker, mounted, holder, original, templeOpen, holdsOwn), Is.EqualTo(expected));
    }

    [Test]
    public void TheBonusCountsEnemyRelicsOnlyWhileTheOwnRelicIsHeld()
    {
        RvrRelicRules.RelicState Own(eRealm realm) => new(realm, realm, eRelicType.Strength, true);
        RvrRelicRules.RelicState Taken(eRealm by, eRealm from) => new(by, from, eRelicType.Strength, true);

        Assert.Multiple(() =>
        {
            Assert.That(RvrRelicRules.BonusModifier(eRealm.Midgard, eRelicType.Strength, [Own(eRealm.Midgard)], 10), Is.EqualTo(1.0));
            Assert.That(RvrRelicRules.BonusModifier(eRealm.Midgard, eRelicType.Strength,
                [Own(eRealm.Midgard), Taken(eRealm.Midgard, eRealm.Albion)], 10), Is.EqualTo(1.1).Within(1e-9));
            Assert.That(RvrRelicRules.BonusModifier(eRealm.Midgard, eRelicType.Strength,
                [Own(eRealm.Midgard), Taken(eRealm.Midgard, eRealm.Albion), Taken(eRealm.Midgard, eRealm.Hibernia)], 10), Is.EqualTo(1.2).Within(1e-9));
            Assert.That(RvrRelicRules.BonusModifier(eRealm.Midgard, eRelicType.Strength,
                [Taken(eRealm.Midgard, eRealm.Albion), Taken(eRealm.Hibernia, eRealm.Midgard)], 10), Is.EqualTo(1.0), "own relic lost: no bonus");
            Assert.That(RvrRelicRules.BonusModifier(eRealm.Midgard, eRelicType.Magic,
                [Own(eRealm.Midgard), Taken(eRealm.Midgard, eRealm.Albion)], 10), Is.EqualTo(1.0), "strength relics do not raise magic");
            Assert.That(RvrRelicRules.BonusModifier(eRealm.Midgard, eRelicType.Strength,
                [Own(eRealm.Midgard), new(eRealm.None, eRealm.Albion, eRelicType.Strength, false)], 10), Is.EqualTo(1.0), "a carried relic counts for nobody");
            Assert.That(RvrRelicRules.BonusModifier(eRealm.None, eRelicType.Strength, [Own(eRealm.None)], 10), Is.EqualTo(1.0));
        });
    }

    // ---- through RelicMgr, both worlds ---------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void ARelicIsVulnerableOnlyBehindAFallenMilegate(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        FreshFrontier();
        UseServer(world);
        GameRelicPad albStrength = Pad(eRealm.Albion, eRelicType.Strength, 1);
        GameRelicPad midStrength = Pad(eRealm.Midgard, eRelicType.Strength, 100);
        GameRelic scabbard = Relic(30, eRealm.Albion, eRelicType.Strength, albStrength);
        GameRelic hammer = Relic(78, eRealm.Midgard, eRelicType.Strength, midStrength);
        SetRelics([scabbard, hammer]);
        TestPlayer midgard = Player(eRealm.Midgard);

        Assert.That(RelicMgr.CanPickupRelicFromShrine(midgard, scabbard), Is.False, "fresh world: sealed (Camlann: no guild keep)");

        Keep(Benowyc, eRealm.Midgard, eRealm.Albion);
        Keep(Berkstead, eRealm.Hibernia, eRealm.Albion);
        Keep(Boldiam, eRealm.Midgard, eRealm.Albion);

        Assert.Multiple(() =>
        {
            Assert.That(RelicMgr.CanPickupRelicFromShrine(midgard, scabbard), Is.EqualTo(BothWorlds.Expect(world, false, true)),
                "Camlann keeps its guild rule; RvR opens the temple");
            Assert.That(RelicMgr.CanPickupRelicFromShrine(Player(eRealm.Albion), scabbard), Is.False, "never from the own pad");
            Assert.That(RelicMgr.CanPickupRelicFromShrine(midgard, hammer), Is.False);
        });

        if (world == WorldModelKind.Realm)
        {
            Keep(Berkstead, eRealm.Albion, eRealm.Albion);
            Assert.That(RelicMgr.CanPickupRelicFromShrine(midgard, scabbard), Is.False, "Albion retook Berkstead: sealed again");
            Assert.That(RelicMgr.GetRvrPickupRefusal(midgard, scabbard), Does.Contain("Caer Berkstead").Or.Contain("keep 51"));
        }
    }

    [Test]
    public void ARealmWithoutItsOwnRelicCannotTakeAnother()
    {
        using var _ = BothWorlds.Use(WorldModelKind.Realm);
        FreshFrontier();
        foreach (int id in new[] { Benowyc, Berkstead, Boldiam })
            Keep(id, eRealm.Midgard, eRealm.Albion);
        UseServer(WorldModelKind.Realm);
        GameRelicPad albStrength = Pad(eRealm.Albion, eRelicType.Strength, 1);
        GameRelicPad hibStrength = Pad(eRealm.Hibernia, eRelicType.Strength, 200);
        GameRelic scabbard = Relic(30, eRealm.Albion, eRelicType.Strength, albStrength);
        GameRelic hammer = Relic(78, eRealm.Midgard, eRelicType.Strength, hibStrength);
        SetRelics([scabbard, hammer]);

        Assert.That(RelicMgr.GetRvrPickupVerdict(Player(eRealm.Midgard), scabbard), Is.EqualTo(RvrRelicRules.PickupVerdict.OwnRelicMissing));
        Assert.That(RelicMgr.GetRvrPickupRefusal(Player(eRealm.Midgard), scabbard), Does.Contain("Strength relic first"));
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void ACapturedRelicGoesToTheCarrierRealmsPadOfItsType(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        FreshFrontier();
        UseServer(world);
        GameRelicPad albStrength = Pad(eRealm.Albion, eRelicType.Strength, 1);
        GameRelicPad midMagic = Pad(eRealm.Midgard, eRelicType.Magic, 100);
        GameRelicPad midStrength = Pad(eRealm.Midgard, eRelicType.Strength, 100);
        Pad(eRealm.Hibernia, eRelicType.Strength, 200);
        GameRelic scabbard = Relic(30, eRealm.Albion, eRelicType.Strength, null);
        SetRelics([scabbard]);
        TestPlayer carrier = Player(eRealm.Midgard);

        Assert.Multiple(() =>
        {
            Assert.That(RelicMgr.FindDeliveryPad(carrier, null, scabbard), Is.SameAs(BothWorlds.Expect(world, null, midStrength)),
                "Camlann needs the guild's claimed keep");
            Assert.That(midStrength.CanReceiveRelic(carrier, scabbard), Is.True, "strength to strength");
            Assert.That(midMagic.CanReceiveRelic(carrier, scabbard), Is.False, "never strength to magic");
            Assert.That(albStrength.CanReceiveRelic(carrier, scabbard), Is.False, "never to the enemy's pad");
        });

        Assert.That(scabbard.RelicPadTakesOver(midStrength, true), Is.True);
        Assert.That(scabbard.Realm, Is.EqualTo(eRealm.Midgard));
        Assert.That(scabbard.CurrentRelicPad, Is.SameAs(midStrength));
        Assert.That(midStrength.IsMountedHere(scabbard), Is.True);
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void TheBonusReachesTheWholeRealmAndEndsWithTheLoss(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        FreshFrontier();
        UseServer(world);
        GameRelicPad albStrength = Pad(eRealm.Albion, eRelicType.Strength, 1);
        GameRelicPad midStrength = Pad(eRealm.Midgard, eRelicType.Strength, 100);
        GameRelic hammer = Relic(78, eRealm.Midgard, eRelicType.Strength, midStrength);
        GameRelic scabbard = Relic(30, eRealm.Albion, eRelicType.Strength, midStrength);
        SetRelics([hammer, scabbard]);
        TestPlayer midPlayer = Player(eRealm.Midgard);
        TestBot midBot = WorldBot(eRealm.Midgard);

        double expected = BothWorlds.Expect(world, 1.0, 1.0 + DOL.GS.ServerProperties.Properties.RELIC_OWNING_BONUS * 0.01);
        Assert.Multiple(() =>
        {
            Assert.That(RelicMgr.GetRelicBonusModifier(midPlayer, eRelicType.Strength), Is.EqualTo(expected).Within(1e-9),
                "Camlann: no guild keep, no bonus");
            Assert.That(RelicMgr.GetRelicBonusModifier(midBot, eRelicType.Strength), Is.EqualTo(expected).Within(1e-9), "a realm's world bot shares it");
            Assert.That(RelicMgr.GetRelicBonusModifier(midPlayer, eRelicType.Magic), Is.EqualTo(1.0));
            Assert.That(RelicMgr.GetRelicBonusModifier(Player(eRealm.Albion), eRelicType.Strength), Is.EqualTo(1.0), "the loser has nothing");
        });

        // Albion takes its relic back: Midgard's bonus ends at once.
        Assert.That(scabbard.ReturnToShrine(albStrength), Is.True);
        Assert.That(RelicMgr.GetRelicBonusModifier(midPlayer, eRelicType.Strength), Is.EqualTo(1.0));
        Assert.That(RelicMgr.GetRelicBonusModifier(Player(eRealm.Albion), eRelicType.Strength), Is.EqualTo(1.0), "own relic only: no bonus");
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void BotsBenefitFromRelicsOnlyInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        Assert.That(WorldBot(eRealm.Midgard).BenefitsFromRelics, Is.EqualTo(BothWorlds.Expect(world, false, true)));
    }

    // ---- bot raid seams --------------------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void ArmiesRaidOnlyEnemyTemplesWithAnOpenMilegate(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        FreshFrontier();
        UseServer(world);
        GameRelicPad albStrength = Pad(eRealm.Albion, eRelicType.Strength, 1);
        GameRelicPad midStrength = Pad(eRealm.Midgard, eRelicType.Strength, 100);
        GameRelic scabbard = Relic(30, eRealm.Albion, eRelicType.Strength, albStrength);
        GameRelic hammer = Relic(78, eRealm.Midgard, eRelicType.Strength, midStrength);
        SetRelics([scabbard, hammer]);
        AbstractGameKeep excalibur = _keeps.GetKeepByID(Excalibur);
        AbstractGameKeep mjollner = _keeps.GetKeepByID(Mjollner);

        Assert.That(RelicMgr.GetTemplePad(excalibur), Is.SameAs(albStrength));
        Assert.That(RelicMgr.GetTemplePad(_keeps.GetKeepByID(Benowyc)), Is.Null, "not a relic keep");
        Assert.That(RelicMgr.IsRelicRaidTarget(excalibur, WorldBot(eRealm.Midgard)), Is.False, "sealed");

        foreach (int id in new[] { Benowyc, Berkstead, Boldiam })
            Keep(id, eRealm.Midgard, eRealm.Albion);

        Assert.Multiple(() =>
        {
            Assert.That(RelicMgr.IsRelicRaidTarget(excalibur, WorldBot(eRealm.Midgard)), Is.EqualTo(BothWorlds.Expect(world, false, true)),
                "Camlann keeps relic raids off");
            Assert.That(RelicMgr.IsRelicRaidTarget(excalibur, WorldBot(eRealm.Albion)), Is.False, "never the own temple");
            Assert.That(RelicMgr.IsRelicRaidTarget(mjollner, WorldBot(eRealm.Albion)), Is.False, "Midgard's milegate stands");
            Assert.That(RelicMgr.IsRelicRaidTarget(excalibur, WorldBot(eRealm.Hibernia)), Is.False,
                "Hibernia holds no strength relic of its own here");
        });
    }

    // ---- relic temples are not captured ------------------------------------------

    [Test]
    public void ARelicTempleIsNeverCapturedByARealm()
    {
        using var _ = BothWorlds.Use(WorldModelKind.Realm);
        FreshFrontier();
        UseServer(WorldModelKind.Realm);
        AbstractGameKeep excalibur = _keeps.GetKeepByID(Excalibur);

        Assert.That(PvpKeepCampaign.RealmCaptureApplies(true), Is.False);
        Assert.That(PvpKeepCampaign.RealmCaptureApplies(false), Is.True);
        Assert.That(PvpKeepCampaign.CaptureForRealm(excalibur, eRealm.Midgard, WorldBot(eRealm.Midgard)), Is.False);
        Assert.That(excalibur.Realm, Is.EqualTo(eRealm.Albion), "the temple stays its realm's");
        Assert.That(excalibur.DBKeep.LordDefeated, Is.False, "no steward, no claim");
    }

    [TestCase(WorldModelKind.Realm, true, eRealm.None, eRealm.Albion, eRealm.Albion, TestName = "A realm-less temple row takes its original realm")]
    [TestCase(WorldModelKind.Realm, true, eRealm.Midgard, eRealm.Albion, eRealm.Albion, TestName = "A temple flipped under 0.337.0 returns to its realm")]
    [TestCase(WorldModelKind.Realm, true, eRealm.Albion, eRealm.Albion, null, TestName = "A temple of its realm stays as it is")]
    [TestCase(WorldModelKind.Realm, false, eRealm.Midgard, eRealm.Albion, null, TestName = "An ordinary captured keep keeps its captor")]
    [TestCase(WorldModelKind.Camlann, true, eRealm.Midgard, eRealm.Albion, null, TestName = "Camlann leaves the temple row alone")]
    public void ARelicTempleStartsWithItsOriginalRealm(WorldModelKind world, bool relicKeep, eRealm realm, eRealm original, eRealm? start)
    {
        Assert.That(PvpKeepCampaign.RelicKeepStartRealm(world, relicKeep, realm, original), Is.EqualTo(start));
    }

    private sealed class PetBrain : ControlledMobBrain
    {
        public PetBrain(GameLiving owner) : base(owner) { }
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void PetsOfRealmBotsShareTheRelicBonusOnlyInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        var pet = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pet, new ArrayList());
        typeof(GameNPC).GetField("m_ownBrain", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(pet, new PetBrain(WorldBot(eRealm.Midgard)) { Body = pet });
        Assert.That(pet.BenefitsFromRelics, Is.EqualTo(BothWorlds.Expect(world, false, true)));
    }

    // ---- relic guards ------------------------------------------------------------

    [TestCase(false, false, true, TestName = "Normal rules: stock guards")]
    [TestCase(true, false, false, TestName = "Camlann: off (bug 146)")]
    [TestCase(true, true, true, TestName = "RvR world: realm temple guards")]
    public void RelicGuardsRunOutsideCamlann(bool pvpRules, bool rvrWorld, bool expected)
    {
        Assert.That(RelicGuardManager.ShouldRun(pvpRules, rvrWorld), Is.EqualTo(expected));
        Assert.That(RelicGuardManager.ShouldRun(true), Is.False, "the Camlann overload is unchanged");
    }

    [TestCase(false, eRealm.Midgard, eRealm.Midgard, eRealm.None, eRealm.Midgard, true, TestName = "Upstream ignores the temple realm")]
    [TestCase(true, eRealm.Midgard, eRealm.Midgard, eRealm.Midgard, eRealm.Midgard, true, TestName = "RvR: own keep, own temple")]
    [TestCase(true, eRealm.Midgard, eRealm.Midgard, eRealm.None, eRealm.Midgard, false, TestName = "RvR: realm-less temple, no renegade")]
    [TestCase(true, eRealm.Midgard, eRealm.Midgard, eRealm.Albion, eRealm.Midgard, false, TestName = "RvR: captured temple")]
    [TestCase(true, eRealm.None, eRealm.Midgard, eRealm.Midgard, eRealm.Midgard, false, TestName = "RvR: neutral frontier keep adds no guard")]
    [TestCase(true, eRealm.Hibernia, eRealm.Midgard, eRealm.Midgard, eRealm.Midgard, false, TestName = "RvR: captured frontier keep adds no guard")]
    public void RelicTempleGuardsStandForTheirRealm(bool rvr, eRealm keepRealm, eRealm keepOriginal, eRealm temple, eRealm guard, bool stands)
    {
        Assert.That(RelicGuardManager.GuardStands(rvr, keepRealm, keepOriginal, temple, guard), Is.EqualTo(stands));
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void RelicTempleGuardsAreHostileByRealmInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        FreshFrontier();
        UseServer(world);
        var guard = (GuardFighterRK)RuntimeHelpers.GetUninitializedObject(typeof(GuardFighterRK));
        var component = (GameKeepComponent)RuntimeHelpers.GetUninitializedObject(typeof(GameKeepComponent));
        component.Keep = _keeps.GetKeepByID(Mjollner);
        guard.Component = component;

        bool midgard = _keeps.IsEnemy(guard, WorldBot(eRealm.Midgard));
        bool albion = _keeps.IsEnemy(guard, WorldBot(eRealm.Albion));
        bool hibernia = _keeps.IsEnemy(guard, WorldBot(eRealm.Hibernia));
        if (world == WorldModelKind.Realm)
        {
            Assert.That(midgard, Is.False, "the temple's realm is its friend");
            Assert.That(albion, Is.True);
            Assert.That(hibernia, Is.True);
        }
        else
        {
            Assert.That(albion, Is.EqualTo(midgard), "Camlann has no realm logic here");
        }
    }

    // ---- Camlann unchanged -------------------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void AClaimedKeepGetsAGuildRelicMountOnlyInCamlann(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        UseServer(world);
        Guild northBomb = MakeGuild("north", "North Bomb", eRealm.Albion);
        var keep = new GameKeep
        {
            DBKeep = new DbKeep { KeepID = Berkstead, Realm = (byte)eRealm.Albion, OriginalRealm = (byte)eRealm.Albion, BaseLevel = 50, Region = 1 },
            Guild = northBomb,
        };
        try
        {
            keep.EnsureRelicPad();
            Assert.That(keep.RelicPad != null, Is.EqualTo(BothWorlds.Expect(world, true, false)),
                "RvR relics mount only on the realm temple pads");
        }
        finally
        {
            keep.RemoveRelicPad();
        }
    }

    [Test]
    public void CamlannGivesNoBonusForATempleShrine()
    {
        using var _ = BothWorlds.Use(WorldModelKind.Camlann);
        UseServer(WorldModelKind.Camlann);
        GameRelicPad midStrength = Pad(eRealm.Midgard, eRelicType.Strength, 100);
        SetRelics([Relic(78, eRealm.Midgard, eRelicType.Strength, midStrength), Relic(30, eRealm.Albion, eRelicType.Strength, midStrength)]);

        Assert.That(RelicMgr.GetRelicBonusModifier(Player(eRealm.Midgard), eRelicType.Strength), Is.EqualTo(1.0),
            "Camlann counts relics in the guild's claimed keep only");
    }
}
