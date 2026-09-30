using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// Bug 74: the parties of one realm raid are one battlegroup, so they cannot
/// attack each other in the full-PvP world (about half of all raider deaths
/// were kills by another party of the same raid).
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_RealmRaidBattleGroup
{
    private EpicTestServerScope _server;

    [SetUp]
    public void SetUp() => _server = new EpicTestServerScope();

    [TearDown]
    public void TearDown() => _server.Dispose();

    private sealed class TestBot : GameBot
    {
        public TestBot() : base((OfflineWorldBotRecord)null) { }
        public override bool IsAlive => true;
        public override byte Level { get; set; } = 50;
        public override eRealm Realm { get; set; }
    }

    private sealed class TestPet : GameNPC
    {
        public override bool IsAlive => true;
    }

    private sealed class PetBrain : ControlledMobBrain
    {
        public PetBrain(GameLiving owner) : base(owner) { }
    }

    private static TestBot Bot(eRealm realm)
    {
        var bot = (TestBot)RuntimeHelpers.GetUninitializedObject(typeof(TestBot));
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, new ArrayList());
        typeof(GameNPC).GetField("m_ownBrain", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, new BotBrain { Body = bot });
        typeof(GameLiving).GetField("<TempProperties>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(bot, new PropertyCollection());
        bot.Realm = realm;
        return bot;
    }

    [Test]
    public void PartiesOfDifferentRealmsAndGuildsAreHostileUntilTheRaidBattleGroupAdmitsThem()
    {
        var first = Bot(eRealm.Albion);
        var second = Bot(eRealm.Midgard);
        var raid = new BattleGroup();

        Assert.That(PvpCombatant.AreAllied(first, second), Is.False, "two parties of the raid, not yet in the battlegroup");
        Assert.That(RealmRaidBattleGroup.Attach(raid, [first, second]), Is.EqualTo(2));
        Assert.That(PvpCombatant.AreAllied(first, second), Is.True);
        Assert.That(new PvPServerRules().IsAllowedToAttack(first, second, true), Is.False, "battlegroup members cannot be attacked");

        Assert.That(RealmRaidBattleGroup.Detach(raid, [second]), Is.EqualTo(1));
        Assert.That(PvpCombatant.AreAllied(first, second), Is.False, "a party that left the raid is fair game again");
    }

    [Test]
    public void AttachIsIdempotentAndCountsOnlyRealChanges()
    {
        var bot = Bot(eRealm.Hibernia);
        var raid = new BattleGroup();
        Assert.That(RealmRaidBattleGroup.Attach(raid, [bot]), Is.EqualTo(1));
        Assert.That(RealmRaidBattleGroup.Attach(raid, [bot]), Is.EqualTo(0));
        Assert.That(bot.TempProperties.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY), Is.SameAs(raid));
        Assert.That(RealmRaidBattleGroup.Attach(null, [bot]), Is.EqualTo(0));
        Assert.That(RealmRaidBattleGroup.Attach(raid, null), Is.EqualTo(0));
    }

    [Test]
    public void DetachLeavesAnotherBattleGroupAlone()
    {
        var bot = Bot(eRealm.Albion);
        var raid = new BattleGroup();
        var other = new BattleGroup();
        RealmRaidBattleGroup.Attach(other, [bot]);

        Assert.That(RealmRaidBattleGroup.Detach(raid, [bot]), Is.EqualTo(0));
        Assert.That(bot.TempProperties.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY), Is.SameAs(other),
            "only the raid's own battlegroup is removed");
        Assert.That(RealmRaidBattleGroup.Detach(other, [bot]), Is.EqualTo(1));
        Assert.That(bot.TempProperties.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY), Is.Null);
    }

    [Test]
    public void TwoRaidsAreNotAlliedWithEachOther()
    {
        var a = Bot(eRealm.Albion);
        var b = Bot(eRealm.Albion);
        RealmRaidBattleGroup.Attach(new BattleGroup(), [a]);
        RealmRaidBattleGroup.Attach(new BattleGroup(), [b]);
        Assert.That(PvpCombatant.AreAllied(a, b), Is.False);
    }

    [Test]
    public void BotPetsResolveToTheirOwnersBattleGroup()
    {
        var owner = Bot(eRealm.Midgard);
        var friend = Bot(eRealm.Hibernia);
        var raid = new BattleGroup();
        RealmRaidBattleGroup.Attach(raid, [owner, friend]);
        var pet = (TestPet)RuntimeHelpers.GetUninitializedObject(typeof(TestPet));
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pet, new ArrayList());
        typeof(GameNPC).GetField("m_ownBrain", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(pet, new PetBrain(owner) { Body = pet });
        typeof(GameLiving).GetField("<TempProperties>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(pet, new PropertyCollection());

        Assert.That(PvpCombatant.Resolve(pet), Is.SameAs(owner));
        Assert.That(PvpCombatant.AreAllied(pet, friend), Is.True, "a pet of one raider is allied with every raider");
    }

    // ---- AutonomousRealmRaid wiring -----------------------------------------

    private static readonly BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>A raid with one two-bot party, registered as membership, without route or staging.</summary>
    private static (object Raid, object Party, Group Group, TestBot[] Bots) RaidWithParty()
    {
        Type raidType = typeof(AutonomousRealmRaid).GetNestedType("Raid", BindingFlags.NonPublic);
        Type partyType = typeof(AutonomousRealmRaid).GetNestedType("Party", BindingFlags.NonPublic);
        object raid = Activator.CreateInstance(raidType, true);
        raidType.GetField("Definition").SetValue(raid, AutonomousRealmRaid.Definitions.First(d => d.IsDungeon));
        object party = Activator.CreateInstance(partyType, true);
        TestBot[] bots = [Bot(eRealm.Albion), Bot(eRealm.Midgard)];
        partyType.GetField("Members").SetValue(party, bots);
        var group = new Group(bots[0]);
        var parties = raidType.GetField("Parties").GetValue(raid);
        parties.GetType().GetMethod("Add").Invoke(parties, [group, party]);
        var membership = typeof(AutonomousRealmRaid).GetField("Membership", Any).GetValue(null);
        membership.GetType().GetMethod("Add").Invoke(membership, [group, raid]);
        return (raid, party, group, bots);
    }

    private static BattleGroup BattleOf(object raid) => (BattleGroup)raid.GetType().GetField("Battle").GetValue(raid);

    [Test]
    public void EnlistingAPartyAlliesItWithTheRestOfTheRaid_AndLeavingReleasesIt()
    {
        var (raid, party, group, bots) = RaidWithParty();
        try
        {
            Assert.That(PvpCombatant.AreAllied(bots[0], bots[1]), Is.False);
            typeof(AutonomousRealmRaid).GetMethod("EnlistBattleGroup", Any).Invoke(null, [raid, party]);
            Assert.That(bots.All(b => b.TempProperties.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY) == BattleOf(raid)), Is.True);
            Assert.That(PvpCombatant.AreAllied(bots[0], bots[1]), Is.True);

            AutonomousRealmRaid.RemoveParty(group);
            Assert.That(bots.All(b => b.TempProperties.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY) == null), Is.True);
            Assert.That(PvpCombatant.AreAllied(bots[0], bots[1]), Is.False);
        }
        finally { AutonomousRealmRaid.RemoveParty(group); }
    }

    [Test]
    public void EndingTheRaidReleasesEveryParty()
    {
        var (raid, party, group, bots) = RaidWithParty();
        try
        {
            typeof(AutonomousRealmRaid).GetMethod("EnlistBattleGroup", Any).Invoke(null, [raid, party]);
            Assert.That(PvpCombatant.AreAllied(bots[0], bots[1]), Is.True);

            typeof(AutonomousRealmRaid).GetMethod("End", Any).Invoke(null, [raid, 1_000L, "The final dungeon encounter has been defeated."]);
            Assert.That(bots.All(b => b.TempProperties.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY) == null), Is.True);
            Assert.That(PvpCombatant.AreAllied(bots[0], bots[1]), Is.False);
        }
        finally { AutonomousRealmRaid.RemoveParty(group); AutonomousRealmRaid.TryConsumeRelease(group, out _); }
    }
}
