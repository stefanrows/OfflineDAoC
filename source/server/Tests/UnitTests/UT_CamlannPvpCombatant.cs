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

[TestFixture, NonParallelizable]
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
        public override ushort CurrentRegionID { get; set; }
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
    public void LowLevelAutonomousSafetyCoversPetsButAllowsMonsterCombat()
    {
        var bot = AutonomousBot(eRealm.Midgard, 5);
        var human = new TestPlayer { Realm = eRealm.Albion, Level = 20, CurrentRegionID = 1 };
        var monster = (TestPet)RuntimeHelpers.GetUninitializedObject(typeof(TestPet));
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(monster, new ArrayList());
        var pet = (TestPet)RuntimeHelpers.GetUninitializedObject(typeof(TestPet));
        var brain = new PetBrain(bot) { Body = pet };
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(pet, new ArrayList());
        typeof(GameNPC).GetField("m_ownBrain", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(pet, brain);

        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.BlocksLowLevelAutonomousPvp(pet, human), Is.True);
            Assert.That(PvpCombatant.BlocksLowLevelAutonomousPvp(human, pet), Is.True);
            Assert.That(PvpCombatant.BlocksLowLevelAutonomousPvp(bot, monster), Is.False);
            Assert.That(PvpCombatant.BlocksLowLevelAutonomousPvp(monster, bot), Is.False);
            Assert.That(PvpCombatant.BlocksLowLevelAutonomousPvp(bot, bot), Is.False);
            Assert.That(PvpCombatant.BlocksLowLevelAutonomousPvp(pet, bot), Is.False);
        });
    }

    [Test]
    public void DirectDamageCannotBypassLowLevelAutonomousSafety()
    {
        var bot = AutonomousBot(eRealm.Albion, 5);
        var human = new TestPlayer { Realm = eRealm.Midgard, Level = 20, CurrentRegionID = 1 };
        typeof(GameLiving).GetField("m_health", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(bot, 100);
        typeof(GameLiving).GetField("m_health", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(human, 100);

        bot.TakeDamage(human, eDamageType.Slash, 40, 0);
        human.TakeDamage(bot, eDamageType.Slash, 40, 0);

        Assert.Multiple(() =>
        {
            Assert.That(bot.Health, Is.EqualTo(100));
            Assert.That(human.Health, Is.EqualTo(100));
        });
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
    public void SubTenAutonomousBotsStayProtectedAfterRelinquishingSafety()
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
        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.IsSafetyProtected(first), Is.True);
            Assert.That(rules.IsAllowedToAttack(first, second, true), Is.False);
            Assert.That(rules.IsAllowedToAttack(human, first, true), Is.False);
            Assert.That(rules.IsAllowedToAttack(first, human, true), Is.False);
            Assert.That(PvpCombatant.BlocksLowLevelAutonomousPvp(first, second), Is.True);
        });
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
    public void OpenBattlegroundAlliesAreGroupGuildAndBattleGroupNeverRealm()
    {
        // Owner decision 2026-10-11: inside the open BG (Thidranki, region 252) alliances are the server's Camlann rule
        // (group, guild, battlegroup); the 0.243.0 same-realm crew alliance is gone, not even behind a property.
        const ushort openBattleground = 252;
        var albionCrew = AutonomousBot(eRealm.Albion, 50);
        var otherAlbionCrew = AutonomousBot(eRealm.Albion, 50);
        albionCrew.CurrentRegionID = otherAlbionCrew.CurrentRegionID = openBattleground;
        // No shared group, guild or battlegroup (Guild needs a running server, so these bots stay unguilded; different
        // guilds are covered by GuildIdentityIsRealmAgnosticAndDifferentGuildsRemainHostile).
        Assert.That(PvpCombatant.AreAllied(albionCrew, otherAlbionCrew), Is.False,
            "two same-realm crew bots inside the open BG are not allied");

        // Two raid units of different realms share the raid's battlegroup: allied until it ends.
        var midgardRaider = AutonomousBot(eRealm.Midgard, 50);
        midgardRaider.CurrentRegionID = openBattleground;
        var raid = new BattleGroup();
        Assert.That(PvpCombatant.AreAllied(albionCrew, midgardRaider), Is.False, "before the raid forms");
        RealmRaidBattleGroup.Attach(raid, [albionCrew, midgardRaider]);
        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.AreAllied(albionCrew, midgardRaider), Is.True, "one battlegroup across realms");
            Assert.That(PvpCombatant.AreAllied(albionCrew, otherAlbionCrew), Is.False, "the realm mate outside the raid stays hostile");
        });
        RealmRaidBattleGroup.Detach(raid, [albionCrew, midgardRaider]);
        Assert.That(PvpCombatant.AreAllied(albionCrew, midgardRaider), Is.False, "the raid ended");
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

    // ---- RvR twins (R1b): the Camlann answers above stay; the Realm-1 world
    // answers "same realm is a friend" and nothing bridges realms there.

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void SameRealmStrangerPerWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        var human = new TestPlayer { Realm = eRealm.Albion, CurrentRegionID = 1 };
        var bot = Bot(eRealm.Albion);

        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.AreAllied(human, bot), Is.EqualTo(BothWorlds.Expect(world, false, true)));
            Assert.That(rules.IsAllowedToAttack(human, bot, true), Is.EqualTo(BothWorlds.Expect(world, true, false)));
            Assert.That(rules.IsSameRealm(human, bot, true), Is.EqualTo(BothWorlds.Expect(world, false, true)));
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void MixedRealmGroupMemberPerWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        var human = new TestPlayer { Realm = eRealm.Albion, CurrentRegionID = 1 };
        var bot = Bot(eRealm.Hibernia);
        PutInGroup(human, bot);

        Assert.Multiple(() =>
        {
            // A cross-realm group cannot form in RvR (R1a), and a stale one is not allied.
            Assert.That(PvpCombatant.AreAllied(human, bot), Is.EqualTo(BothWorlds.Expect(world, true, false)));
            Assert.That(rules.IsAllowedToAttack(human, bot, true), Is.EqualTo(BothWorlds.Expect(world, false, true)));
            Assert.That(rules.IsSameRealm(human, bot, true), Is.EqualTo(BothWorlds.Expect(world, true, false)));
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void OpenBattlegroundAlliesPerWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        const ushort openBattleground = 252;
        var albionCrew = AutonomousBot(eRealm.Albion, 50);
        var otherAlbionCrew = AutonomousBot(eRealm.Albion, 50);
        var midgardRaider = AutonomousBot(eRealm.Midgard, 50);
        albionCrew.CurrentRegionID = otherAlbionCrew.CurrentRegionID = midgardRaider.CurrentRegionID = openBattleground;
        var raid = new BattleGroup();
        RealmRaidBattleGroup.Attach(raid, [albionCrew, midgardRaider]);

        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.AreAllied(albionCrew, otherAlbionCrew), Is.EqualTo(BothWorlds.Expect(world, false, true)));
            Assert.That(PvpCombatant.AreAllied(albionCrew, midgardRaider), Is.EqualTo(BothWorlds.Expect(world, true, false)),
                "a battlegroup never bridges realms in RvR");
        });
        RealmRaidBattleGroup.Detach(raid, [albionCrew, midgardRaider]);
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void TemporaryCompanionPerWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        var owner = new TestPlayer { Realm = eRealm.Albion, CurrentRegionID = 1 };
        var companion = Bot(eRealm.Hibernia);
        var albionWorldBot = Bot(eRealm.Albion);
        var hiberniaWorldBot = Bot(eRealm.Hibernia);
        SetGameBotProperty(companion, "Owner", owner);
        SetGameBotProperty(companion, "IsTemporaryGroupHelper", true);

        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.AreAllied(companion, owner), Is.True);
            Assert.That(rules.IsAllowedToAttack(companion, owner, true), Is.False);
            // The helper fights for its owner's realm, not for its Hibernian class.
            Assert.That(rules.IsAllowedToAttack(companion, albionWorldBot, true), Is.EqualTo(BothWorlds.Expect(world, true, false)));
            Assert.That(rules.IsAllowedToAttack(companion, hiberniaWorldBot, true), Is.True);
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void BotOwnedPetPerWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        var bot = Bot(eRealm.Midgard);
        var midgardHuman = new TestPlayer { Realm = eRealm.Midgard, CurrentRegionID = 1 };
        var pet = (TestPet)RuntimeHelpers.GetUninitializedObject(typeof(TestPet));
        var brain = new PetBrain(bot) { Body = pet };
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(pet, new ArrayList());
        typeof(GameNPC).GetField("m_ownBrain", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(pet, brain);

        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.AreAllied(bot, pet), Is.True);
            Assert.That(PvpCombatant.AreAllied(midgardHuman, pet), Is.EqualTo(BothWorlds.Expect(world, false, true)));
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
