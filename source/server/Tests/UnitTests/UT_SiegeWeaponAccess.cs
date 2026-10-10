using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// Bug 151: siege weapons were realm-bound, so a Camlann group, guild, or
/// battlegroup that mixes birth realms could not share one ram. Anyone allied
/// with the weapon's owner may use it under PvP rules; others stay locked out;
/// other server rules keep the realm comparison.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_SiegeWeaponAccess
{
    private static readonly IObjectDatabase Empty = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
    private GameServer _previous;

    private sealed class PvpServer : GameServer
    {
        protected override IObjectDatabase DataBaseImpl => Empty;
        protected override IServerRules ServerRulesImpl => new PvPServerRules();
    }

    private sealed class NormalServer : GameServer
    {
        protected override IObjectDatabase DataBaseImpl => Empty;
        protected override IServerRules ServerRulesImpl => new NormalServerRules();
    }

    [SetUp]
    public void SetUp() => _previous = GameServer.Instance;

    [TearDown]
    public void TearDown() => GameServer.LoadTestDouble(_previous);

    private static void UseRules<T>() where T : GameServer =>
        GameServer.LoadTestDouble((T)RuntimeHelpers.GetUninitializedObject(typeof(T)));

    private sealed class TestPlayer : GamePlayer
    {
        public TestPlayer() : base(null, null) { }

        public override bool IsAlive => true;
        public override byte Level { get; set; } = 50;
        public override eRealm Realm { get; set; }
        public override GameClient Client { get; } = new BotDummyClient();
        public override bool IsInvulnerableToAttack => false;
        public override ushort CurrentRegionID { get; set; }
        public override bool SafetyFlag { get; set; }
    }

    private sealed class TestBot : GameBot
    {
        public TestBot() : base((OfflineWorldBotRecord)null) { }

        public override bool IsAlive => true;
        public override byte Level { get; set; } = 50;
        public override eRealm Realm { get; set; }
    }

    private static TestPlayer Player(eRealm realm) =>
        new TestPlayer { Realm = realm, ObjectState = GameObject.eObjectState.Active };

    private static TestBot Bot(eRealm realm)
    {
        var bot = (TestBot)RuntimeHelpers.GetUninitializedObject(typeof(TestBot));
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(bot, new ArrayList());
        typeof(GameNPC).GetField("m_ownBrain", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(bot, new DOL.AI.Brain.BotBrain { Body = bot });
        typeof(GameLiving).GetField("<TempProperties>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(bot, new PropertyCollection());
        bot.Realm = realm;
        bot.ObjectState = GameObject.eObjectState.Active;
        return bot;
    }

    private static void PutInGroup(params GameLiving[] members)
    {
        var group = new Group(members[0]);
        var groupMembers = (List<GameLiving>)typeof(Group)
            .GetField("_groupMembers", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(group);
        groupMembers.AddRange(members);
        foreach (GameLiving member in members)
            member.Group = group;
    }

    private static GameSiegeRam Ram(eRealm realm)
    {
        var ram = (GameSiegeRam)RuntimeHelpers.GetUninitializedObject(typeof(GameSiegeRam));
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(ram, new ArrayList());
        typeof(GameNPC).GetField("m_ownBrain", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(ram, new DOL.AI.Brain.BlankBrain());
        // An inactive object sets its realm without broadcasting to the world.
        ram.ObjectState = GameObject.eObjectState.Inactive;
        ram.Realm = realm;
        return ram;
    }

    private static void RecordController(GameSiegeWeapon weapon, GameLiving controller) =>
        typeof(GameSiegeWeapon).GetMethod("RecordController", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(weapon, new object[] { controller });

    [Test]
    public void AlliedPlayerOfAnotherRealmMayUseTheWeaponOnCamlann()
    {
        var owner = Player(eRealm.Albion);
        var ally = Player(eRealm.Hibernia);
        PutInGroup(owner, ally);

        Assert.Multiple(() =>
        {
            Assert.That(SiegeWeaponAccess.MayUse(true, eRealm.Albion, ally, new GameLiving[] { owner }, null, null), Is.True);
            Assert.That(SiegeWeaponAccess.MayUse(true, eRealm.Albion, owner, new GameLiving[] { owner }, null, null), Is.True);
        });
    }

    [Test]
    public void UnalliedSameRealmPlayerIsLockedOutOnCamlann()
    {
        var owner = Player(eRealm.Albion);
        var stranger = Player(eRealm.Albion);

        Assert.That(SiegeWeaponAccess.MayUse(true, eRealm.Albion, stranger, new GameLiving[] { owner }, null, null), Is.False);
    }

    [Test]
    public void UnalliedOtherRealmPlayerIsLockedOutOnCamlann()
    {
        var owner = Player(eRealm.Albion);
        var enemy = Player(eRealm.Midgard);

        Assert.That(SiegeWeaponAccess.MayUse(true, eRealm.Albion, enemy, new GameLiving[] { owner }, null, null), Is.False);
    }

    [Test]
    public void BotInTheOwnersGroupOfAnotherBirthRealmMayTakeControl()
    {
        var owner = Player(eRealm.Albion);
        var groupBot = Bot(eRealm.Midgard);
        var strangerBot = Bot(eRealm.Albion);
        PutInGroup(owner, groupBot);

        Assert.Multiple(() =>
        {
            Assert.That(SiegeWeaponAccess.MayUse(true, eRealm.Albion, groupBot, new GameLiving[] { owner }, null, null), Is.True);
            Assert.That(SiegeWeaponAccess.MayUse(true, eRealm.Albion, strangerBot, new GameLiving[] { owner }, null, null), Is.False);
        });
    }

    [Test]
    public void NonPvpRulesKeepTheRealmComparison()
    {
        var owner = Player(eRealm.Albion);
        var sameRealm = Player(eRealm.Albion);
        var alliedOtherRealm = Player(eRealm.Hibernia);
        PutInGroup(owner, alliedOtherRealm);

        Assert.Multiple(() =>
        {
            Assert.That(SiegeWeaponAccess.MayUse(false, eRealm.Albion, sameRealm, new GameLiving[] { owner }, null, null), Is.True);
            Assert.That(SiegeWeaponAccess.MayUse(false, eRealm.Albion, alliedOtherRealm, new GameLiving[] { owner }, null, null), Is.False);
        });
    }

    [Test]
    public void AbandonedWeaponFallsBackToItsRealmWhenNoOwnerIsAround()
    {
        var sameRealm = Player(eRealm.Albion);
        var otherRealm = Player(eRealm.Midgard);

        Assert.Multiple(() =>
        {
            Assert.That(SiegeWeaponAccess.MayUse(true, eRealm.Albion, sameRealm, Array.Empty<GameLiving>(), null, null), Is.True);
            Assert.That(SiegeWeaponAccess.MayUse(true, eRealm.Albion, otherRealm, Array.Empty<GameLiving>(), null, null), Is.False);
            Assert.That(SiegeWeaponAccess.MayUse(true, eRealm.Albion, null, Array.Empty<GameLiving>(), null, null), Is.False);
        });
    }

    [Test]
    public void RamBuiltByTheFounderAdmitsHisGroupAndNotStrangersOnCamlann()
    {
        UseRules<PvpServer>();
        var founder = Player(eRealm.Albion);
        var ally = Player(eRealm.Midgard);
        var strangerSameRealm = Player(eRealm.Albion);
        PutInGroup(founder, ally);
        GameSiegeRam ram = Ram(eRealm.Albion);

        ram.SetFounder(founder);

        Assert.Multiple(() =>
        {
            Assert.That(ram.MayUseSiegeWeapon(founder), Is.True);
            Assert.That(ram.MayUseSiegeWeapon(ally), Is.True);
            Assert.That(ram.MayUseSiegeWeapon(strangerSameRealm), Is.False);
            Assert.That(ram.Realm, Is.EqualTo(eRealm.Albion), "the weapon keeps its realm for packets and door logic");
        });
    }

    [Test]
    public void LatestControllerAlsoAnchorsAndAFoundersLogoutFallsBackToTheRealm()
    {
        UseRules<PvpServer>();
        var founder = Player(eRealm.Albion);
        var controller = Player(eRealm.Hibernia);
        var controllersFriend = Player(eRealm.Midgard);
        var albionStranger = Player(eRealm.Albion);
        PutInGroup(founder, controller);
        GameSiegeRam ram = Ram(eRealm.Albion);
        ram.SetFounder(founder);
        RecordController(ram, controller);
        PutInGroup(controller, controllersFriend);

        Assert.That(ram.MayUseSiegeWeapon(controllersFriend), Is.True);

        founder.ObjectState = GameObject.eObjectState.Deleted;
        controller.ObjectState = GameObject.eObjectState.Deleted;

        Assert.Multiple(() =>
        {
            Assert.That(ram.MayUseSiegeWeapon(albionStranger), Is.True, "nobody owns it any more: realm rule");
            Assert.That(ram.MayUseSiegeWeapon(controllersFriend), Is.False);
        });
    }

    [Test]
    public void NormalServerRulesStillRequireTheWeaponsRealm()
    {
        UseRules<NormalServer>();
        var founder = Player(eRealm.Albion);
        var ally = Player(eRealm.Midgard);
        var sameRealm = Player(eRealm.Albion);
        PutInGroup(founder, ally);
        GameSiegeRam ram = Ram(eRealm.Albion);
        ram.SetFounder(founder);

        Assert.Multiple(() =>
        {
            Assert.That(ram.MayUseSiegeWeapon(ally), Is.False);
            Assert.That(ram.MayUseSiegeWeapon(sameRealm), Is.True);
        });
    }
}
