using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// R1a of the RvR mode: the Realm-1 world selects <see cref="RvrServerRules"/>
/// on GameType=PvP, with realm colours, the true realm byte and realm-only
/// group, guild, trade and speech. The Camlann answers are asserted next to
/// them so a change to either world shows up here.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_RvrServerRules
{
    private EpicTestServerScope _server;

    [SetUp]
    public void SetUp() => _server = new EpicTestServerScope();

    [TearDown]
    public void TearDown() => _server.Dispose();

    private sealed class TestPlayer : GamePlayer
    {
        public TestPlayer() : base(null, null) { }

        public override bool IsAlive => true;
        public override byte Level { get; set; } = 50;
        public override eRealm Realm { get; set; }
        public override GameClient Client { get; } = new BotDummyClient();
        public override ushort CurrentRegionID { get; set; } = 1;
    }

    private sealed class TestBot : GameBot
    {
        public TestBot() : base((OfflineWorldBotRecord)null) { }

        public override bool IsAlive => true;
        public override byte Level { get; set; } = 50;
        public override eRealm Realm { get; set; }
        public override ushort CurrentRegionID { get; set; } = 1;
    }

    // ---- rules class selection -------------------------------------------

    [Test]
    public void CamlannMarkerKeepsTheExactPvPRules()
    {
        Assert.That(ScriptMgr.CreateServerRules(EGameServerType.GST_PvP, WorldModelKind.Camlann).GetType(),
            Is.EqualTo(typeof(PvPServerRules)));
        Assert.That(ScriptMgr.CreateServerRules(EGameServerType.GST_PvP).GetType(),
            Is.EqualTo(typeof(PvPServerRules)), "the attribute lookup must never pick the RvR class");
    }

    [Test]
    public void RealmMarkerSelectsRvrRulesOnPvP()
    {
        IServerRules rules = ScriptMgr.CreateServerRules(EGameServerType.GST_PvP, WorldModelKind.Realm);

        Assert.That(rules, Is.TypeOf<RvrServerRules>());
        Assert.That(rules, Is.InstanceOf<PvPServerRules>(), "fork PvP features hang on the PvP rules type");
    }

    [Test]
    public void RealmMarkerDoesNotChangeOtherServerTypes()
    {
        Assert.That(ScriptMgr.CreateServerRules(EGameServerType.GST_Normal, WorldModelKind.Realm).GetType(),
            Is.EqualTo(typeof(NormalServerRules)));
    }

    [Test]
    public void RvrRulesCarryNoServerTypeAttribute()
    {
        Assert.That(typeof(RvrServerRules).GetCustomAttributes(typeof(ServerRulesAttribute), false), Is.Empty);
    }

    // ---- client presentation ---------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void ColourHandlingPerWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);

        Assert.That(BothWorlds.Rules(world).GetColorHandling(null), Is.EqualTo(BothWorlds.Expect<byte>(world, 1, 0)));
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void RealmBytePerWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        var viewer = new TestPlayer { Realm = eRealm.Albion };
        var sameRealmBot = Bot(eRealm.Albion);
        var enemyBot = Bot(eRealm.Hibernia);
        var sameRealmPlayer = new TestPlayer { Realm = eRealm.Albion };

        Assert.Multiple(() =>
        {
            // Camlann spoofs a same-realm stranger as an enemy realm; RvR sends the true realm.
            Assert.That(rules.GetLivingRealm(viewer, sameRealmBot),
                Is.EqualTo(BothWorlds.Expect(world, (byte)eRealm.Midgard, (byte)eRealm.Albion)));
            Assert.That(rules.GetLivingRealm(viewer, sameRealmPlayer),
                Is.EqualTo(BothWorlds.Expect(world, (byte)eRealm.Midgard, (byte)eRealm.Albion)));
            Assert.That(rules.GetLivingRealm(viewer, enemyBot), Is.EqualTo((byte)eRealm.Hibernia));
            Assert.That(rules.GetLivingRealm(null, enemyBot), Is.EqualTo((byte)0));
        });
    }

    [Test]
    public void RvrRealmByteShowsStaffAsTheViewersRealm()
    {
        var viewer = new TestPlayer { Realm = eRealm.Midgard };
        var gm = new TestPlayer { Realm = eRealm.Albion };
        gm.Client.Account.PrivLevel = (uint)ePrivLevel.GM;

        Assert.That(new RvrServerRules().GetLivingRealm(viewer, gm), Is.EqualTo((byte)eRealm.Midgard));
    }

    // ---- realm-only social rules -----------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void GroupingAcrossRealmsPerWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        var albion = new TestPlayer { Realm = eRealm.Albion };
        var albion2 = new TestPlayer { Realm = eRealm.Albion };
        var midgard = new TestPlayer { Realm = eRealm.Midgard };

        Assert.Multiple(() =>
        {
            Assert.That(rules.IsAllowedToGroup(albion, albion2, true), Is.True);
            Assert.That(rules.IsAllowedToGroup(albion, midgard, true), Is.EqualTo(BothWorlds.Expect(world, true, false)));
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void TradingAcrossRealmsPerWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        var albion = new TestPlayer { Realm = eRealm.Albion };
        var albion2 = new TestPlayer { Realm = eRealm.Albion };
        var hibernia = new TestPlayer { Realm = eRealm.Hibernia };

        Assert.Multiple(() =>
        {
            Assert.That(rules.IsAllowedToTrade(albion, albion2, true), Is.True);
            Assert.That(rules.IsAllowedToTrade(albion, hibernia, true), Is.EqualTo(BothWorlds.Expect(world, true, false)));
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void SpeechAcrossRealmsPerWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        var albion = new TestPlayer { Realm = eRealm.Albion };
        var albion2 = new TestPlayer { Realm = eRealm.Albion };
        var midgard = new TestPlayer { Realm = eRealm.Midgard };

        Assert.Multiple(() =>
        {
            Assert.That(rules.IsAllowedToUnderstand(albion, albion2), Is.True);
            Assert.That(rules.IsAllowedToUnderstand(midgard, albion), Is.EqualTo(BothWorlds.Expect(world, true, false)));
        });

        // Region 27 lets every realm understand each other, as in the Normal rules.
        midgard.CurrentRegionID = 27;
        Assert.That(rules.IsAllowedToUnderstand(midgard, albion), Is.True);
    }

    // Guild objects need a live database (the Guild type initializer creates the
    // dummy guild), so the RvR guild rule is a pure function tested directly. The
    // garrison refusal it adds to is covered by the PvP rules' own tests.
    [TestCase(eRealm.Albion, eRealm.Albion, true)]
    [TestCase(eRealm.Albion, eRealm.Midgard, false)]
    [TestCase(eRealm.Hibernia, eRealm.Albion, false)]
    [TestCase(eRealm.None, eRealm.Midgard, true)]
    public void RvrGuildsTakeTheirOwnRealmOnly(eRealm guildRealm, eRealm memberRealm, bool expected)
    {
        Assert.That(RvrServerRules.GuildAcceptsRealm(guildRealm, memberRealm), Is.EqualTo(expected));
    }

    [Test]
    public void RvrGuildRuleRefusesAMissingPlayer()
    {
        Assert.That(new RvrServerRules().IsAllowedToJoinGuild(null, null), Is.False);
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void CharactersInEveryRealmStayAllowed(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);

        Assert.That(BothWorlds.Rules(world).IsAllowedCharsInAllRealms(null), Is.True);
    }

    private static TestBot Bot(eRealm realm)
    {
        var bot = (TestBot)RuntimeHelpers.GetUninitializedObject(typeof(TestBot));
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(bot, new ArrayList());
        var brain = new BotBrain { Body = bot };
        typeof(GameNPC).GetField("m_ownBrain", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(bot, brain);
        typeof(GameLiving).GetField("<TempProperties>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(bot, new PropertyCollection());
        bot.Realm = realm;
        return bot;
    }
}
