using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_CamlannPvpCombatant
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

    private sealed class TestPet : GameNPC
    {
        public override bool IsAlive => true;
    }

    private sealed class PetBrain : ControlledMobBrain
    {
        public PetBrain(GameLiving owner) : base(owner) { }
    }

    [Test]
    public void SameRealmStrangersAreHostileAndHumanCanAttackBot()
    {
        var human = new TestPlayer { Realm = eRealm.Albion };
        var bot = Bot(eRealm.Albion);
        var rules = new PvPServerRules();

        Assert.That(PvpCombatant.AreAllied(human, bot), Is.False);
        Assert.That(rules.IsAllowedToAttack(human, bot, true), Is.True);
        Assert.That(rules.IsSameRealm(human, bot, true), Is.False);
    }

    [Test]
    public void CapitalsAreSafeButOpenWorldSameRealmStrangersAreNot()
    {
        var human = new TestPlayer { Realm = eRealm.Albion, CurrentRegionID = 10 };
        var bot = Bot(eRealm.Albion);
        var rules = new PvPServerRules();

        Assert.That(PvpCombatant.IsSafeArea(human), Is.True);
        Assert.That(rules.IsAllowedToAttack(human, bot, true), Is.False);

        human.CurrentRegionID = 1;
        Assert.That(PvpCombatant.IsSafeArea(human), Is.False);
        Assert.That(rules.IsAllowedToAttack(human, bot, true), Is.True);
    }

    [Test]
    public void BotVictimsHonorPvPImmunity()
    {
        var human = new TestPlayer { Realm = eRealm.Albion, CurrentRegionID = 1 };
        var bot = Bot(eRealm.Midgard);
        bot.StartPvpInvulnerability(5_000);

        Assert.That(bot.IsInvulnerableToAttack, Is.True);
        Assert.That(new PvPServerRules().IsAllowedToAttack(human, bot, true), Is.False);
    }

    [Test]
    public void SubTenSafetyOnlyProtectsOutsideOldFrontiers()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.IsSafetyProtected(9, true, false), Is.True);
            Assert.That(PvpCombatant.IsSafetyProtected(9, true, true), Is.False);
            Assert.That(PvpCombatant.IsSafetyProtected(10, true, false), Is.False);
            Assert.That(PvpCombatant.IsSafetyProtected(9, false, false), Is.False);
        });
    }

    [Test]
    public void FlaggedSubTenHumanCannotStartOpenWorldPvp()
    {
        var human = new TestPlayer { Realm = eRealm.Albion, Level = 9, SafetyFlag = true, CurrentRegionID = 1 };
        var bot = Bot(eRealm.Midgard);

        Assert.That(new PvPServerRules().IsAllowedToAttack(human, bot, true), Is.False);
    }

    [Test]
    public void PvpAssignmentExplicitlyRelinquishesOptionalSafetyWithoutWeakeningImmunity()
    {
        var human = new TestPlayer { Level = 5, SafetyFlag = true };
        var bot = Bot(eRealm.Midgard);
        bot.StartPvpInvulnerability(5_000);

        PvpCombatant.RelinquishOptionalSafety(human);
        PvpCombatant.RelinquishOptionalSafety(bot);

        Assert.Multiple(() =>
        {
            Assert.That(human.SafetyFlag, Is.False);
            Assert.That(PvpCombatant.HasRelinquishedOptionalSafety(human), Is.True);
            Assert.That(PvpCombatant.HasRelinquishedOptionalSafety(bot), Is.True);
            Assert.That(bot.IsInvulnerableToAttack, Is.True);
        });
    }

    [Test]
    public void GuildIdentityIsRealmAgnosticAndDifferentGuildsRemainHostile()
    {
        Assert.That(PvpCombatant.AreGuildIdsAllied("shared", "Camlann Crew A", "shared", "Camlann Crew A"), Is.True);
        Assert.That(PvpCombatant.AreGuildIdsAllied("shared", "Camlann Crew A", "rival", "Camlann Crew B"), Is.False);
    }

    [Test]
    public void GroupedHumanAndBotAreAlliedAcrossRealms()
    {
        var human = new TestPlayer { Realm = eRealm.Albion };
        var bot = Bot(eRealm.Hibernia);
        PutInGroup(human, bot);

        Assert.That(PvpCombatant.AreAllied(human, bot), Is.True);
        Assert.That(new PvPServerRules().IsAllowedToAttack(human, bot, true), Is.False);
        Assert.That(new PvPServerRules().IsSameRealm(human, bot, true), Is.True);
    }

    [Test]
    public void MixedRealmGroupCanAttackNeutralMobWithoutFriendlyFire()
    {
        var human = new TestPlayer { Realm = eRealm.Albion, CurrentRegionID = 1 };
        var bot = Bot(eRealm.Hibernia);
        PutInGroup(human, bot);
        var mob = (TestPet)RuntimeHelpers.GetUninitializedObject(typeof(TestPet));
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(mob, new ArrayList());
        mob.Realm = eRealm.None;
        var rules = new PvPServerRules();

        Assert.That(rules.IsAllowedToAttack(human, mob, true), Is.True);
        Assert.That(rules.IsAllowedToAttack(bot, mob, true), Is.True);
        Assert.That(rules.IsAllowedToAttack(human, bot, true), Is.False);
    }

    [Test]
    public void BotOwnedPetResolvesToTheBotNotAPlayerOwner()
    {
        var bot = Bot(eRealm.Midgard);
        var pet = (TestPet)RuntimeHelpers.GetUninitializedObject(typeof(TestPet));
        var brain = new PetBrain(bot) { Body = pet };
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(pet, new ArrayList());
        typeof(GameNPC).GetField("m_ownBrain", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(pet, brain);

        Assert.That(PvpCombatant.Resolve(pet), Is.SameAs(bot));
        Assert.That(PvpCombatant.AreAllied(bot, pet), Is.True);
    }

    [Test]
    public void TemporaryCompanionProtectsOwnerButNotAutonomousBot()
    {
        var owner = new TestPlayer { Realm = eRealm.Albion };
        var formerGroupMember = new TestPlayer { Realm = eRealm.Midgard };
        var companion = Bot(eRealm.Hibernia);
        var autonomous = Bot(eRealm.Albion);
        SetGameBotProperty(companion, "Owner", owner);
        SetGameBotProperty(companion, "IsTemporaryGroupHelper", true);
        typeof(GameBot).GetField("_temporaryCompanionProtectedMembers", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(companion, new HashSet<GameLiving> { formerGroupMember });

        Assert.That(PvpCombatant.AreAllied(companion, owner), Is.True);
        Assert.That(PvpCombatant.AreAllied(companion, formerGroupMember), Is.True);
        Assert.That(PvpCombatant.AreAllied(companion, autonomous), Is.False);
        Assert.That(BotPvpCrowdControl.Protected(companion, autonomous), Is.False);
        Assert.That(PvpCombatant.IsSafeArea(companion), Is.False);
        Assert.That(new PvPServerRules().IsAllowedToAttack(companion, owner, true), Is.False);
        Assert.That(new PvPServerRules().IsAllowedToAttack(companion, autonomous, true), Is.True);
    }

    [Test]
    public void SubTenAutonomousBotsCarryImplicitSafetyUntilRelinquished()
    {
        var rules = new PvPServerRules();
        var first = AutonomousBot(eRealm.Albion, 5);
        var second = AutonomousBot(eRealm.Albion, 5);
        var human = new TestPlayer { Realm = eRealm.Albion, Level = 20, CurrentRegionID = 1 };

        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.IsSafetyProtected(first), Is.True);
            Assert.That(rules.IsAllowedToAttack(first, second, true), Is.False);
            Assert.That(rules.IsAllowedToAttack(human, first, true), Is.False);
            Assert.That(rules.IsAllowedToAttack(first, human, true), Is.False);
        });

        PvpCombatant.RelinquishOptionalSafety(first);
        Assert.That(rules.IsAllowedToAttack(first, second, true), Is.False, "the other bot keeps its safety");
        PvpCombatant.RelinquishOptionalSafety(second);
        Assert.That(rules.IsAllowedToAttack(first, second, true), Is.True);
    }

    [Test]
    public void AutonomousSafetyEndsAtLevelTenAndSkipsCompanions()
    {
        var rules = new PvPServerRules();
        var companion = Bot(eRealm.Albion);
        companion.Level = 5;

        Assert.Multiple(() =>
        {
            Assert.That(rules.IsAllowedToAttack(AutonomousBot(eRealm.Midgard, 10),
                AutonomousBot(eRealm.Midgard, 12), true), Is.True);
            Assert.That(PvpCombatant.IsSafetyProtected(companion), Is.False);
        });
    }

    [Test]
    public void EngagementTrackerRemembersWhoStruckFirst()
    {
        var first = AutonomousBot(eRealm.Hibernia, 20);
        var second = AutonomousBot(eRealm.Hibernia, 20);

        AutonomousPvpEngagementTracker.ObserveAttack(second, new AttackData { Attacker = first, Target = second });
        AutonomousPvpEngagementTracker.ObserveAttack(first, new AttackData { Attacker = second, Target = first });

        Assert.Multiple(() =>
        {
            Assert.That(AutonomousPvpEngagementTracker.VictimStartedFight(first, second), Is.True);
            Assert.That(AutonomousPvpEngagementTracker.VictimStartedFight(second, first), Is.False);
        });
    }

    [Test]
    public void EngagementPathsAreClassifiedFromTheFirstBlow()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousPvpEngagementTracker.Classify(true, false, true, "grudge"),
                Is.EqualTo(AutonomousPvpEngagementTracker.Human));
            Assert.That(AutonomousPvpEngagementTracker.Classify(false, true, true, null),
                Is.EqualTo(AutonomousPvpEngagementTracker.Pet));
            Assert.That(AutonomousPvpEngagementTracker.Classify(false, false, false, "cc-sweep"),
                Is.EqualTo(AutonomousPvpEngagementTracker.Collateral));
            Assert.That(AutonomousPvpEngagementTracker.Classify(false, false, true, "cc-sweep"),
                Is.EqualTo(AutonomousPvpEngagementTracker.CrowdControl));
            Assert.That(AutonomousPvpEngagementTracker.Classify(false, false, true, null),
                Is.EqualTo(AutonomousPvpEngagementTracker.Untagged));
            Assert.That(AutonomousPvpEngagementTracker.LevelBand(9), Is.EqualTo("1-9"));
            Assert.That(AutonomousPvpEngagementTracker.LevelBand(50), Is.EqualTo("50"));
        });
    }

    [Test]
    public void GuildGrudgesSkipLowLevelBrawlsGreyKillersAndSelfStartedFights()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousGuildGrudgePolicy.ShouldRemember(false, 5, false, false), Is.False);
            Assert.That(AutonomousGuildGrudgePolicy.ShouldRemember(false, 10, false, false), Is.True);
            Assert.That(AutonomousGuildGrudgePolicy.ShouldRemember(true, 5, false, false), Is.True);
            Assert.That(AutonomousGuildGrudgePolicy.ShouldRemember(true, 30, true, false), Is.False);
            Assert.That(AutonomousGuildGrudgePolicy.ShouldRemember(true, 30, false, true), Is.False);
        });
    }

    private static TestBot AutonomousBot(eRealm realm, byte level)
    {
        TestBot bot = Bot(realm);
        SetGameBotProperty(bot, "IsAutonomousWorldBot", true);
        bot.Level = level;
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

    private static void SetGameBotProperty(GameBot bot, string property, object value)
    {
        typeof(GameBot).GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(bot, value);
    }
}
