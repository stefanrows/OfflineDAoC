using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Database.Handlers;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public class UT_AutonomousBotRealmPointRewards
{
    private sealed class RewardServer : GameServer
    {
        protected override IServerRules ServerRulesImpl => new NormalServerRules();
        protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
    }

    private static readonly IObjectDatabase EmptyDatabase =
        DispatchProxy.Create<IObjectDatabase, EmptyReadDatabase>();

    public class EmptyReadDatabase : DispatchProxy
    {
        protected override object Invoke(MethodInfo method, object[] args)
        {
            Type result = method.ReturnType;
            if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(result.GetGenericArguments()[0]));
            return null;
        }
    }

    private GameServer _previousServer;

    [SetUp]
    public void SetUp()
    {
        _previousServer = GameServer.Instance;
        GameServer.LoadTestDouble((RewardServer)RuntimeHelpers.GetUninitializedObject(typeof(RewardServer)));
    }

    [TearDown]
    public void TearDown() => GameServer.LoadTestDouble(_previousServer);

    private sealed class RewardBot : GameBot
    {
        private RewardBot() : base((OfflineWorldBotRecord)null) { }
        public override byte Level { get; set; }
        public override eRealm Realm { get; set; }
    }

    private static void SetField(Type type, object target, string name, object value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static RewardBot Bot(bool autonomous, bool temporary)
    {
        RewardBot bot = (RewardBot)RuntimeHelpers.GetUninitializedObject(typeof(RewardBot));
        SetField(typeof(GameLiving), bot, "<TempProperties>k__BackingField", new PropertyCollection());
        SetField(typeof(GameNPC), bot, "m_brains", new ArrayList());
        typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot))!.SetValue(bot, autonomous);
        typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper))!.SetValue(bot, temporary);
        SetField(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
        return bot;
    }

    [Test]
    public void OnlyPersistentAutonomousBotIsRealmPointVictim()
    {
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(Bot(true, false)), Is.True);
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(Bot(false, true)), Is.False,
            "/spawn companions must never be worth realm points");
        GameNPC ordinaryNpc = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
        SetField(typeof(GameNPC), ordinaryNpc, "m_brains", new ArrayList());
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(ordinaryNpc), Is.False,
            "ordinary NPCs and pets remain on the zero-RP NPC path");
    }

    [TestCase(25, 0, 125)]
    [TestCase(35, 0, 225)]
    [TestCase(35, 20, 245)]
    [TestCase(50, 0, 900)]
    [TestCase(50, 90, 990)]
    [TestCase(1, 0, 5)]
    [TestCase(2, 0, 10)]
    [TestCase(19, 3, 98)]
    [TestCase(20, 0, 100)]
    [TestCase(21, 0, 105)]
    public void VictimValueScalesThroughLevelingAndPreservesHighLevelCurve(int level, int realmLevel, int expected)
    {
        Assert.That(AutonomousBotRealmPointRewards.GetPlayerEquivalentRealmPointValue(
            (byte)level, realmLevel), Is.EqualTo(expected));
    }

    [Test]
    public void RewardMatchesPlayerKillCapDamageRankAndGroupFormula()
    {
        int actual = AutonomousBotRealmPointRewards.CalculateRealmPointReward(
            victimRealmPointValue: 990,
            victimRealmLevel: 90,
            awarderRealmPointValue: 900,
            awarderRealmLevel: 0,
            participantCount: 2,
            groupContributorCount: 2,
            damagePercent: 0.75,
            applyRealmRankAdjustment: true);

        int baseShare = 990 / 2;
        int expected = (int)(baseShare * 0.75 * (1.0 + 2.0 * 90 / 900.0) * 1.125);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void PurpleVictimEarnsMoreRpAndXpThanEqualLevelKill()
    {
        int EqualRp = AutonomousBotRealmPointRewards.CalculateRealmPointReward(5, 0, 5, 0, 1, 1, 1, true, 1, 1);
        int harderRp = AutonomousBotRealmPointRewards.CalculateRealmPointReward(20, 0, 5, 0, 1, 1, 1, true, 4, 1);
        Assert.That(EqualRp, Is.EqualTo(5));
        Assert.That(harderRp, Is.EqualTo(20));
        long equalXp = AutonomousBotRealmPointRewards.CalculateExperienceReward(100, 100, 1, 1, 1, 1, 100);
        long harderXp = AutonomousBotRealmPointRewards.CalculateExperienceReward(1000, 100, 4, 1, 1, 1, 100);
        Assert.That(harderXp, Is.EqualTo(200).And.GreaterThan(equalXp));
        Assert.That(AutonomousBotRealmPointRewards.ChallengeMultiplier(50, 1), Is.EqualTo(2));
    }

    [Test]
    public void LowLevelSharedKillRetainsPositiveCreditButNoDamagePaysNothing()
    {
        Assert.That(AutonomousBotRealmPointRewards.CalculateRealmPointReward(5, 0, 5, 0, 8, 8, 0.1, true, 1, 1), Is.EqualTo(1));
        Assert.That(AutonomousBotRealmPointRewards.CalculateRealmPointReward(5, 0, 5, 0, 8, 8, 0, true, 1, 1), Is.Zero);
        Assert.That(AutonomousBotRealmPointRewards.CalculateExperienceReward(1000, 100, 4, 1, 1, 0.5, 100), Is.EqualTo(100));
    }

    [TestCase(40, 40, 1.0)]
    [TestCase(44, 40, 1.25)]
    [TestCase(49, 40, 1.5)]
    [TestCase(26, 20, 2.0)]
    public void ChallengeUsesConColor(int victimLevel, int awarderLevel, double expected)
    {
        Assert.That(AutonomousBotRealmPointRewards.ChallengeMultiplier(victimLevel, awarderLevel),
            Is.EqualTo(expected));
    }

    [Test]
    public void CompanionAndControlledPetResolveToPlayerOwnerWithoutBecomingVictims()
    {
        GamePlayer owner = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
        RewardBot companion = Bot(false, true);
        typeof(GameBot).GetProperty(nameof(GameBot.Owner))!.SetValue(companion, owner);
        GameNPC pet = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
        SetField(typeof(GameNPC), pet, "m_brains", new ArrayList());
        SetField(typeof(GameNPC), pet, "m_ownBrain", new ControlledMobBrain(companion) { Body = pet });

        Assert.That(AutonomousBotRealmPointRewards.ResolveRootRewardOwner(companion), Is.SameAs(owner));
        Assert.That(AutonomousBotRealmPointRewards.ResolveRootRewardOwner(pet), Is.SameAs(owner));
        Assert.That(AutonomousRvrDefense.IsCombatant(owner), Is.True);
        Assert.That(AutonomousRvrDefense.IsCombatant(companion), Is.True);
        Assert.That(AutonomousRvrDefense.IsCombatant(pet), Is.True);
        SetField(typeof(GameNPC), pet, "m_ownBrain", new ControlledMobBrain(owner) { Body = pet });
        Assert.That(AutonomousRvrDefense.IsCombatant(pet), Is.True, "Direct human-owned pets are not IGamePlayer but must be defended against");
        Assert.That(AutonomousRvrDefense.IsCombatant(null), Is.False);
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(companion), Is.False);
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(pet), Is.False);
    }
}
