using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;
using NUnit.Framework;
using NUnit.Framework.Constraints;

namespace DOL.UnitTests;

/// <summary>
/// R1b of the RvR mode: the alliance core. Every case runs in both worlds
/// (spec section 9.2): Camlann keeps its group/guild/battlegroup answer, the
/// RvR world answers "same realm is a friend, another realm an enemy", and
/// guild, battlegroup and companion ties never bridge realms there.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_RvrAllianceCore
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
        public override ushort CurrentRegionID { get; set; } = 1;
        public override short Race { get; set; } = (short)eRace.Briton;
        public override eGender Gender { get; set; } = eGender.Male;
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

    private sealed class TestNpc : GameNPC
    {
        public override bool IsAlive => true;
    }

    private sealed class PetBrain : ControlledMobBrain
    {
        public PetBrain(GameLiving owner) : base(owner) { }
    }

    private sealed class PvpKeeps : DefaultKeepManager
    {
        protected override bool IsPvpServer => true;
    }

    private sealed class ConfiguredServer : GameServer
    {
        private static readonly IObjectDatabase Empty =
            DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private static readonly GameServerConfiguration Normal = new() { ServerType = EGameServerType.GST_Normal };
        protected override IObjectDatabase DataBaseImpl => Empty;
        public override GameServerConfiguration Configuration => Normal;
    }

    // ---- player-shaped pairs ---------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void PlayersOfTheSameRealmAreFriendsOnlyInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        var albion = new TestPlayer { Realm = eRealm.Albion };
        var albion2 = new TestPlayer { Realm = eRealm.Albion };
        var midgard = new TestPlayer { Realm = eRealm.Midgard };

        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.AreAllied(albion, albion2), Is.EqualTo(BothWorlds.Expect(world, false, true)));
            Assert.That(rules.IsAllowedToAttack(albion, albion2, true), Is.EqualTo(BothWorlds.Expect(world, true, false)));
            Assert.That(rules.IsSameRealm(albion, albion2, true), Is.EqualTo(BothWorlds.Expect(world, false, true)));
            Assert.That(PvpCombatant.AreAllied(albion, midgard), Is.False);
            Assert.That(rules.IsAllowedToAttack(albion, midgard, true), Is.True);
            Assert.That(rules.IsSameRealm(albion, midgard, true), Is.False);
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void WorldBotsFollowTheSameRealmRule(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        var human = new TestPlayer { Realm = eRealm.Hibernia };
        var ownRealmBot = AutonomousBot(eRealm.Hibernia);
        var enemyBot = AutonomousBot(eRealm.Albion);
        var ownRealmBot2 = AutonomousBot(eRealm.Hibernia);

        Assert.Multiple(() =>
        {
            Assert.That(rules.IsAllowedToAttack(human, ownRealmBot, true), Is.EqualTo(BothWorlds.Expect(world, true, false)));
            Assert.That(rules.IsAllowedToAttack(ownRealmBot, human, true), Is.EqualTo(BothWorlds.Expect(world, true, false)));
            Assert.That(rules.IsAllowedToAttack(ownRealmBot, ownRealmBot2, true), Is.EqualTo(BothWorlds.Expect(world, true, false)));
            Assert.That(rules.IsAllowedToAttack(human, enemyBot, true), Is.True);
            Assert.That(rules.IsAllowedToAttack(enemyBot, ownRealmBot, true), Is.True);
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void AStaleCrossRealmGroupNeverAlliesInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        var human = new TestPlayer { Realm = eRealm.Albion };
        var bot = AutonomousBot(eRealm.Hibernia);
        PutInGroup(human, bot);

        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.AreAllied(human, bot), Is.EqualTo(BothWorlds.Expect(world, true, false)));
            Assert.That(rules.IsAllowedToAttack(human, bot, true), Is.EqualTo(BothWorlds.Expect(world, false, true)));
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void ACrossRealmBattlegroupNeverAlliesInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        var albion = AutonomousBot(eRealm.Albion);
        var midgard = AutonomousBot(eRealm.Midgard);
        var albion2 = AutonomousBot(eRealm.Albion);
        var raid = new BattleGroup();
        RealmRaidBattleGroup.Attach(raid, [albion, midgard]);

        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.AreAllied(albion, midgard), Is.EqualTo(BothWorlds.Expect(world, true, false)));
            Assert.That(PvpCombatant.AreAllied(albion, albion2), Is.EqualTo(BothWorlds.Expect(world, false, true)),
                "outside the battlegroup the realm decides in RvR");
        });
        RealmRaidBattleGroup.Detach(raid, [albion, midgard]);
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void BattlegroupsAdmitOnlyTheMembersRealmInTheRvrWorld(WorldModelKind world)
    {
        var albion = new TestPlayer { Realm = eRealm.Albion };
        var albion2 = new TestPlayer { Realm = eRealm.Albion };
        var hibernia = new TestPlayer { Realm = eRealm.Hibernia };
        var gm = new TestPlayer { Realm = eRealm.Midgard };
        gm.Client.Account.PrivLevel = (uint)ePrivLevel.GM;

        Assert.Multiple(() =>
        {
            Assert.That(BattleGroup.AdmitsRealm(world, [albion], albion2), Is.True);
            Assert.That(BattleGroup.AdmitsRealm(world, [albion, albion2], hibernia), Is.EqualTo(BothWorlds.Expect(world, true, false)));
            Assert.That(BattleGroup.AdmitsRealm(world, [], hibernia), Is.True, "a new battlegroup takes its first member");
            Assert.That(BattleGroup.AdmitsRealm(world, [albion], gm), Is.True, "staff are exempt");
            Assert.That(BattleGroup.AdmitsRealm(world, [albion], null), Is.False);
        });
    }

    // ---- companions, helpers and pets -------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void ACompanionFightsForItsOwnersRealm(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        var owner = new TestPlayer { Realm = eRealm.Albion };
        var ownRealmStranger = new TestPlayer { Realm = eRealm.Albion };
        var enemy = new TestPlayer { Realm = eRealm.Hibernia };
        // A Hibernian class under an Albion owner cannot be created in the RvR
        // world (R2), but the rules must not break if one exists.
        var companion = Companion(owner, eRealm.Hibernia, temporaryHelper: false);
        PutInGroup(owner, companion);

        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.AllianceRealm(companion), Is.EqualTo(eRealm.Albion));
            Assert.That(PvpCombatant.AreAllied(companion, owner), Is.True);
            Assert.That(PvpCombatant.AreAllied(companion, ownRealmStranger), Is.EqualTo(BothWorlds.Expect(world, false, true)));
            Assert.That(PvpCombatant.AreAllied(companion, enemy), Is.False);
            Assert.That(rules.IsAllowedToAttack(companion, owner, true), Is.False);
            Assert.That(rules.IsAllowedToAttack(enemy, companion, true), Is.True);
            Assert.That(rules.GetLivingRealm(owner, companion), Is.EqualTo((byte)eRealm.Albion),
                "the owner sees his companion as a friend");
            Assert.That(rules.IsAllowedToTrade(owner, companion, true), Is.True, "items can still be handed over");
            Assert.That(rules.IsAllowedToUnderstand(companion, owner), Is.True, "the owner understands its /say");
            Assert.That(rules.IsAllowedToUnderstand(companion, enemy), Is.EqualTo(BothWorlds.Expect(world, true, false)));
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void ASpawnHelperFightsForItsOwnersRealmAndForgetsOtherRealms(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        var owner = new TestPlayer { Realm = eRealm.Midgard };
        var formerGroupMember = new TestPlayer { Realm = eRealm.Hibernia };
        var ownRealmBot = AutonomousBot(eRealm.Midgard);
        var helper = Companion(owner, eRealm.Albion, temporaryHelper: true);
        typeof(GameBot).GetField("_temporaryCompanionProtectedMembers", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(helper, new HashSet<GameLiving> { formerGroupMember });

        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.AreAllied(helper, owner), Is.True);
            Assert.That(PvpCombatant.AreAllied(helper, formerGroupMember), Is.EqualTo(BothWorlds.Expect(world, true, false)),
                "the helper's memory of an old group does not bridge realms in RvR");
            Assert.That(PvpCombatant.AreAllied(helper, ownRealmBot), Is.EqualTo(BothWorlds.Expect(world, false, true)));
            Assert.That(WorldMode.AllowsRealmPair(world, owner, helper), Is.True);
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void PetsFightForTheirOwnersRealm(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        var albion = new TestPlayer { Realm = eRealm.Albion };
        var enemyBot = AutonomousBot(eRealm.Midgard);
        var ownRealmBot = AutonomousBot(eRealm.Albion);
        GameNPC enemyPet = Pet(enemyBot);
        GameNPC ownRealmPet = Pet(ownRealmBot);

        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.AreAllied(albion, enemyPet), Is.False);
            Assert.That(rules.IsAllowedToAttack(albion, enemyPet, true), Is.True);
            Assert.That(rules.IsAllowedToAttack(enemyPet, albion, true), Is.True);
            Assert.That(PvpCombatant.AreAllied(albion, ownRealmPet), Is.EqualTo(BothWorlds.Expect(world, false, true)));
            Assert.That(rules.IsAllowedToAttack(albion, ownRealmPet, true), Is.EqualTo(BothWorlds.Expect(world, true, false)));
            Assert.That(PvpCombatant.AllianceRealm(enemyPet), Is.EqualTo(eRealm.Midgard));
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void AnUnsponsoredEncounterBotIsANeutralOpponentInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        var albion = new TestPlayer { Realm = eRealm.Albion };
        var patrol = AutonomousBot(eRealm.Albion);
        var patrolMate = AutonomousBot(eRealm.Albion);
        SetGameBotProperty(patrol, nameof(GameBot.IsAutonomousWorldBot), false);
        SetGameBotProperty(patrolMate, nameof(GameBot.IsAutonomousWorldBot), false);
        SetGameBotProperty(patrol, nameof(GameBot.IsBattlegroundEncounterBot), true);
        SetGameBotProperty(patrolMate, nameof(GameBot.IsBattlegroundEncounterBot), true);
        PutInGroup(patrol, patrolMate);

        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.AllianceRealm(patrol), Is.EqualTo(eRealm.None), "its Albion class is only a model");
            Assert.That(PvpCombatant.AreAllied(albion, patrol), Is.False, "an Albion patrol still fights Albion players");
            Assert.That(PvpCombatant.AreAllied(patrol, patrolMate), Is.True, "its own group holds it together");
            Assert.That(BothWorlds.Rules(world).GetLivingRealm(albion, patrol), Is.Not.EqualTo((byte)eRealm.Albion),
                "the client shows it as an enemy");
        });
    }

    // ---- realm NPCs and keep guards -----------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void RealmNpcsAreFriendlyToTheirOwnRealmOnlyInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        var albion = new TestPlayer { Realm = eRealm.Albion };
        var enemyBot = AutonomousBot(eRealm.Midgard);
        TestNpc albionGuard = Npc(eRealm.Albion);
        TestNpc midgardGuard = Npc(eRealm.Midgard);

        Assert.Multiple(() =>
        {
            Assert.That(rules.IsAllowedToAttack(albion, albionGuard, true), Is.False);
            Assert.That(rules.IsAllowedToAttack(albion, midgardGuard, true), Is.EqualTo(BothWorlds.Expect(world, false, true)));
            Assert.That(rules.IsAllowedToAttack(albionGuard, enemyBot, true), Is.EqualTo(BothWorlds.Expect(world, false, true)),
                "a town guard attacks enemy-realm bots in RvR");
            Assert.That(rules.IsAllowedToAttack(albionGuard, albion, true), Is.False);
            Assert.That(rules.IsSameRealm(albion, albionGuard, true), Is.True);
            Assert.That(rules.IsSameRealm(albion, midgardGuard, true), Is.EqualTo(BothWorlds.Expect(world, true, false)));
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void ARealmNpcCountsOwnRealmBotsAndCompanionsAsItsRealm(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        TestNpc albionHealer = Npc(eRealm.Albion);
        var owner = new TestPlayer { Realm = eRealm.Albion };
        TestBot ownRealmBot = AutonomousBot(eRealm.Albion);
        TestBot enemyBot = AutonomousBot(eRealm.Midgard);
        // A Hibernian class under an Albion owner counts as Albion.
        TestBot companion = Companion(owner, eRealm.Hibernia, temporaryHelper: false);

        Assert.Multiple(() =>
        {
            Assert.That(rules.IsSameRealm(albionHealer, ownRealmBot, true), Is.EqualTo(BothWorlds.Expect(world, false, true)));
            Assert.That(rules.IsSameRealm(albionHealer, companion, true), Is.EqualTo(BothWorlds.Expect(world, false, true)));
            Assert.That(rules.IsSameRealm(albionHealer, Pet(ownRealmBot), true), Is.EqualTo(BothWorlds.Expect(world, false, true)));
            Assert.That(rules.IsSameRealm(albionHealer, enemyBot, true), Is.False);
            Assert.That(rules.IsSameRealm(albionHealer, owner, true), Is.EqualTo(BothWorlds.Expect(world, false, true)));
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void AClaimedKeepIsTheFriendOfItsRealmInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        Guild northBomb = MakeGuild("north", "North Bomb", eRealm.Albion);
        GameKeepDoor gate = KeepGate(eRealm.Albion, northBomb);
        var keeps = new PvpKeeps();
        TestBot guildMate = KeepBot(eRealm.Albion, northBomb);
        TestBot ownRealmStranger = KeepBot(eRealm.Albion, null);
        TestBot crossRealmGuildMate = KeepBot(eRealm.Midgard, northBomb);
        TestBot enemy = KeepBot(eRealm.Hibernia, null);

        Assert.Multiple(() =>
        {
            Assert.That(keeps.IsEnemy(gate, guildMate), Is.False);
            Assert.That(keeps.IsEnemy(gate, ownRealmStranger), Is.EqualTo(BothWorlds.Expect(world, true, false)));
            Assert.That(keeps.IsEnemy(gate, crossRealmGuildMate), Is.EqualTo(BothWorlds.Expect(world, false, true)),
                "the guild does not bridge realms in RvR");
            Assert.That(keeps.IsEnemy(gate, enemy), Is.True);
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void TheFrontierWardensStayHostileToEveryRealm(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        GameKeepDoor gate = KeepGate(eRealm.Albion, MakeGuild("wardens", PvpKeepCampaign.GarrisonName, eRealm.None));
        var keeps = new PvpKeeps();

        Assert.Multiple(() =>
        {
            Assert.That(keeps.IsEnemy(gate, KeepBot(eRealm.Albion, null)), Is.True, "the keep's database realm is no claim");
            Assert.That(keeps.IsEnemy(gate, KeepBot(eRealm.Midgard, null)), Is.True);
        });
    }

    [TestCase(true, eRealm.Albion, false, eRealm.Albion, false, true, TestName = "Wardens are everyone's enemy")]
    [TestCase(false, eRealm.Midgard, false, eRealm.Midgard, false, false, TestName = "Own realm keep is a friend")]
    [TestCase(false, eRealm.Midgard, false, eRealm.Hibernia, false, true, TestName = "Other realm keep is an enemy")]
    [TestCase(false, eRealm.None, true, eRealm.Albion, false, false, TestName = "Realm-less unheld keep follows the property (off)")]
    [TestCase(false, eRealm.None, true, eRealm.Albion, true, true, TestName = "Realm-less unheld keep follows the property (on)")]
    [TestCase(false, eRealm.None, false, eRealm.Albion, false, true, TestName = "Realm-less held keep is hostile")]
    public void RvrKeepHostilityRule(bool garrison, eRealm keepRealm, bool unheld, eRealm targetRealm, bool property, bool enemy)
    {
        Assert.That(DefaultKeepManager.IsRvrKeepEnemy(garrison, keepRealm, unheld, targetRealm, property), Is.EqualTo(enemy));
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void AnAbandonedRamTakesItsRealmNotAnotherRealmsGuildMate(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        Guild founders = MakeGuild("rams", "Ram Crew", eRealm.None);
        TestBot crossRealmGuildMate = KeepBot(eRealm.Midgard, founders);
        TestBot ownRealmStranger = KeepBot(eRealm.Albion, null);

        Assert.Multiple(() =>
        {
            Assert.That(SiegeWeaponAccess.MayUse(true, eRealm.Albion, crossRealmGuildMate, [], "rams", "Ram Crew"),
                Is.EqualTo(BothWorlds.Expect(world, true, false)));
            Assert.That(SiegeWeaponAccess.MayUse(true, eRealm.Albion, ownRealmStranger, [], "rams", "Ram Crew"), Is.True);
        });
    }

    // ---- presentation --------------------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void EnemyPlayersShowNoNameGuildOrTitleInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        var viewer = new TestPlayer { Realm = eRealm.Albion };
        var friend = new TestPlayer { Realm = eRealm.Albion, Name = "Friend" };
        var enemy = new TestPlayer { Realm = eRealm.Midgard, Name = "Enemy", Race = (short)eRace.Norseman };
        Guild guild = MakeGuild("g", "Northern Lights", eRealm.Midgard);
        typeof(GamePlayer).GetField("m_guild", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(enemy, guild);

        Assert.Multiple(() =>
        {
            Assert.That(rules.GetPlayerName(viewer, friend), Is.EqualTo("Friend"));
            Assert.That(rules.GetPlayerName(viewer, enemy), BothWorlds.Expect<IResolveConstraint>(world, Is.EqualTo("Enemy"), Is.Not.EqualTo("Enemy")),
                "an enemy shows its race name");
            Assert.That(rules.GetPlayerGuildName(viewer, enemy), Is.EqualTo(BothWorlds.Expect(world, "Northern Lights", string.Empty)));
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void EnemyBotsShowLikeEnemyPlayersInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        var viewer = new TestPlayer { Realm = eRealm.Albion };
        TestBot friend = AutonomousBot(eRealm.Albion);
        friend.Name = "Arthen";
        friend.GuildName = "Albion Crew";
        TestBot enemy = AutonomousBot(eRealm.Hibernia);
        enemy.Name = "Fionn";
        enemy.GuildName = "Hibernia Crew";
        enemy.Race = (short)eRace.Celt;

        Assert.Multiple(() =>
        {
            Assert.That(rules.GetNPCName(viewer, friend), Is.EqualTo("Arthen"));
            Assert.That(rules.GetNPCGuildName(viewer, friend), Is.EqualTo("Albion Crew"));
            Assert.That(rules.GetNPCName(viewer, enemy), BothWorlds.Expect<IResolveConstraint>(world, Is.EqualTo("Fionn"), Is.Not.EqualTo("Fionn")));
            Assert.That(rules.GetNPCGuildName(viewer, enemy), Is.EqualTo(BothWorlds.Expect(world, "Hibernia Crew", string.Empty)));
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void OrdinaryNpcsKeepTheirNamesInBothWorlds(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        TestNpc merchant = Npc(eRealm.Midgard);
        merchant.Name = "Ulf";
        merchant.GuildName = "Merchant";

        Assert.Multiple(() =>
        {
            Assert.That(BothWorlds.Rules(world).GetNPCName(new TestPlayer { Realm = eRealm.Albion }, merchant), Is.EqualTo("Ulf"));
            Assert.That(BothWorlds.Rules(world).GetNPCGuildName(new TestPlayer { Realm = eRealm.Albion }, merchant), Is.EqualTo("Merchant"));
        });
    }

    // ---- bind, craft and the rules branch ----------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void BindStonesAndRecipesFollowTheRealmInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        var albion = new TestPlayer { Realm = eRealm.Albion };
        bool previous = GS.ServerProperties.Properties.ALLOW_CRAFT_NOREALM_ITEMS;
        try
        {
            GS.ServerProperties.Properties.ALLOW_CRAFT_NOREALM_ITEMS = true;
            Assert.Multiple(() =>
            {
                Assert.That(rules.IsAllowedToBind(albion, new DbBindPoint { Realm = (int)eRealm.Albion }), Is.True);
                Assert.That(rules.IsAllowedToBind(albion, new DbBindPoint { Realm = 0 }), Is.True);
                Assert.That(rules.IsAllowedToBind(albion, new DbBindPoint { Realm = (int)eRealm.Hibernia }),
                    Is.EqualTo(BothWorlds.Expect(world, true, false)));
                Assert.That(rules.IsAllowedToCraft(albion, new DbItemTemplate { Realm = (int)eRealm.Albion }), Is.True);
                Assert.That(rules.IsAllowedToCraft(albion, new DbItemTemplate { Realm = 0 }), Is.True);
                Assert.That(rules.IsAllowedToCraft(albion, new DbItemTemplate { Realm = (int)eRealm.Midgard }),
                    Is.EqualTo(BothWorlds.Expect(world, true, false)));
            });
        }
        finally
        {
            GS.ServerProperties.Properties.ALLOW_CRAFT_NOREALM_ITEMS = previous;
        }
    }

    [TestCase(WorldModelKind.Camlann, EGameServerType.GST_PvP, EGameServerType.GST_PvP)]
    [TestCase(WorldModelKind.Realm, EGameServerType.GST_PvP, EGameServerType.GST_Normal)]
    [TestCase(WorldModelKind.Realm, EGameServerType.GST_PvE, EGameServerType.GST_PvE)]
    [TestCase(WorldModelKind.Realm, EGameServerType.GST_Normal, EGameServerType.GST_Normal)]
    [TestCase(WorldModelKind.Camlann, EGameServerType.GST_Normal, EGameServerType.GST_Normal)]
    public void TheRvrWorldTakesTheRealmBranchOfServerTypeSites(WorldModelKind world, EGameServerType type, EGameServerType branch)
    {
        Assert.That(WorldMode.RulesBranch(world, type), Is.EqualTo(branch));
    }

    // ---- helpers ------------------------------------------------------------

    private static TestBot AutonomousBot(eRealm realm)
    {
        TestBot bot = Bot(realm);
        SetGameBotProperty(bot, nameof(GameBot.IsAutonomousWorldBot), true);
        return bot;
    }

    private static TestBot Companion(GamePlayer owner, eRealm classRealm, bool temporaryHelper)
    {
        TestBot bot = Bot(classRealm);
        SetGameBotProperty(bot, nameof(GameBot.Owner), owner);
        SetGameBotProperty(bot, nameof(GameBot.IsTemporaryGroupHelper), temporaryHelper);
        return bot;
    }

    private static TestBot KeepBot(eRealm realm, Guild guild)
    {
        TestBot bot = AutonomousBot(realm);
        typeof(GameBot).GetField("_dummyClient", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, new BotDummyClient());
        bot.Guild = guild;
        return bot;
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
        // GetUninitializedObject skips the property initializers: level 50 keeps
        // the sub-10 safety out of the way; region 0 keeps the border-hub
        // geometry (which reads the movement component) out of the way.
        bot.Level = 50;
        bot.Realm = realm;
        return bot;
    }

    private static TestNpc Npc(eRealm realm)
    {
        var npc = (TestNpc)RuntimeHelpers.GetUninitializedObject(typeof(TestNpc));
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(npc, new ArrayList());
        npc.Realm = realm;
        return npc;
    }

    private static GameNPC Pet(GameLiving owner)
    {
        TestNpc pet = Npc(owner.Realm);
        var brain = new PetBrain(owner) { Body = pet };
        typeof(GameNPC).GetField("m_ownBrain", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(pet, brain);
        return pet;
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

    private static void SetGameBotProperty(GameBot bot, string property, object value)
    {
        typeof(GameBot).GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(bot, value);
    }

    private static Guild MakeGuild(string id, string name, eRealm realm)
    {
        GameServer previous = GameServer.Instance;
        GameServer.LoadTestDouble((ConfiguredServer)RuntimeHelpers.GetUninitializedObject(typeof(ConfiguredServer)));
        try { RuntimeHelpers.RunClassConstructor(typeof(Guild).TypeHandle); }
        finally { GameServer.LoadTestDouble(previous); }
        var guild = (Guild)RuntimeHelpers.GetUninitializedObject(typeof(Guild));
        typeof(Guild).GetField("m_DBguild", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(guild, new DbGuild { GuildID = id, GuildName = name, Realm = (byte)realm });
        return guild;
    }

    private static GameKeepDoor KeepGate(eRealm keepRealm, Guild guild)
    {
        var keep = new GameKeep
        {
            DBKeep = new DbKeep { Realm = (byte)keepRealm, BaseLevel = 50, Level = 5 },
            Guild = guild
        };
        var component = (GameKeepComponent)RuntimeHelpers.GetUninitializedObject(typeof(GameKeepComponent));
        component.Keep = keep;
        return new GameKeepDoor { Component = component };
    }
}
