using System;
using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// Port session S4 (docs/RVR_PORT_MAP.md "S4 done"): the upstream 0.38c pieces left out of S1-S3. The RvR world
/// (Realm-1) takes upstream's rule; Camlann keeps the fork's where it had one. Cases marked "upstream" are carried
/// verbatim from the named upstream test class (renamed only where the fork's names differ).
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_RvrPortS4
{
    private sealed class PetBrain : ControlledMobBrain
    {
        public PetBrain(GameLiving owner) : base(owner) { }
    }

    private static GamePlayer Player() => (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));

    private static T Uninitialized<T>() where T : GameNPC
    {
        var npc = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(npc, new ArrayList());
        return npc;
    }

    private static GameNPC PetOf(GameLiving owner)
    {
        GameNPC pet = Uninitialized<GameNPC>();
        typeof(GameNPC).GetField("m_ownBrain", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(pet, new PetBrain(owner) { Body = pet });
        return pet;
    }

    // ---- 1. Lord shield (upstream Lord.cs), RvR world only --------------------------------------------------------

    [TestCase(WorldModelKind.Realm, true, false, false, true, TestName = "RvR: a siege engine never hurts the lord")]
    [TestCase(WorldModelKind.Realm, false, true, true, true, TestName = "RvR: a bot cannot hurt him while a door stands")]
    [TestCase(WorldModelKind.Realm, false, true, false, false, TestName = "RvR: a bot hurts him once the doors are down")]
    [TestCase(WorldModelKind.Realm, false, false, true, false, TestName = "RvR: players are unaffected")]
    [TestCase(WorldModelKind.Camlann, true, false, false, false, TestName = "Camlann: siege engines still count")]
    [TestCase(WorldModelKind.Camlann, false, true, true, false, TestName = "Camlann: the fork's targeting rule, no damage shield")]
    public void TheLordIsShieldedOnlyInTheRvrWorld(WorldModelKind world, bool siege, bool bot, bool shielded, bool ignored)
    {
        Assert.That(GuardLord.IgnoresDamage(world, siege, bot, () => shielded), Is.EqualTo(ignored));
    }

    [Test]
    public void BotSourcesIncludeTheirPetsButNotPlayers()
    {
        GameBot bot = Uninitialized<GameBot>();
        GamePlayer player = Player();
        Assert.Multiple(() =>
        {
            Assert.That(GuardLord.IsBotSource(bot), Is.True);
            Assert.That(GuardLord.IsBotSource(PetOf(bot)), Is.True, "a bot's pet");
            Assert.That(GuardLord.IsBotSource(player), Is.False);
            Assert.That(GuardLord.IsBotSource(PetOf(player)), Is.False, "a player's pet");
            Assert.That(GuardLord.IsBotSource(null), Is.False);
        });
    }

    // ---- 2. No item loot from guards, gamebots and their pets (upstream AbstractServerRules), both worlds -----------

    [Test]
    public void KeepGuardsGamebotsAndTheirPetsDropNoItems()
    {
        GameBot bot = Uninitialized<GameBot>();
        GamePlayer player = Player();
        GameNPC monster = Uninitialized<GameNPC>();
        Assert.Multiple(() =>
        {
            Assert.That(AbstractServerRules.NoItemLoot(Uninitialized<GuardFighter>()), Is.True, "keep guard");
            Assert.That(AbstractServerRules.NoItemLoot(Uninitialized<GuardLord>()), Is.True, "keep lord");
            Assert.That(AbstractServerRules.NoItemLoot(bot), Is.True, "gamebot");
            Assert.That(AbstractServerRules.NoItemLoot(PetOf(bot)), Is.True, "a gamebot's pet");
            Assert.That(AbstractServerRules.NoItemLoot(PetOf(player)), Is.True, "a player's pet");
            Assert.That(AbstractServerRules.NoItemLoot(monster), Is.False, "an ordinary monster keeps its loot");
            Assert.That(AbstractServerRules.NoItemLoot(PetOf(monster)), Is.False, "a monster's own add keeps its loot");
        });
    }

    // ---- 3. AutonomousGroupPace (upstream), RvR world only ---------------------------------------------------------

    // Upstream UT_AuditFixesOct5.GroupLeaderNeverSlowsDownAndStopsOnlyForAMemberFarBehind.
    [TestCase(300f, false, AutonomousGroupPace.Decision.FullSpeed)]
    [TestCase(900f, false, AutonomousGroupPace.Decision.FullSpeed)]
    [TestCase(1500f, false, AutonomousGroupPace.Decision.FullSpeed)]
    [TestCase(1600f, false, AutonomousGroupPace.Decision.Wait)]
    [TestCase(float.MaxValue, false, AutonomousGroupPace.Decision.Wait)]
    [TestCase(1000f, true, AutonomousGroupPace.Decision.Wait)]
    [TestCase(900f, true, AutonomousGroupPace.Decision.FullSpeed)]
    public void GroupLeaderNeverSlowsDownAndStopsOnlyForAMemberFarBehind(float farthest, bool waiting,
        AutonomousGroupPace.Decision expected) =>
        Assert.That(AutonomousGroupPace.Decide(farthest, waiting), Is.EqualTo(expected));

    // Upstream UT_GroupTravelAndEventsOct5.FollowersBehindTheirFormationSpotRunFasterToCatchUp.
    [TestCase(0f, 1f)]
    [TestCase(120f, 1f)]
    [TestCase(570f, 1.125f)]
    [TestCase(1020f, 1.25f)]
    [TestCase(5000f, 1.25f)]
    public void FollowersBehindTheirFormationSpotRunFasterToCatchUp(float distance, float factor) =>
        Assert.That(AutonomousGroupPace.CatchUpFactor(distance), Is.EqualTo(factor).Within(0.001f));

    // Upstream UT_SupportReachAndGroupStall.AGroupMemberStuckFor45SecondsNoLongerHoldsTheGroup.
    [TestCase(0f, 1L, 44_000L, ExpectedResult = false, TestName = "Not yet 45 s")]
    [TestCase(0f, 0L, 45_000L, ExpectedResult = false, TestName = "No first observation")]
    [TestCase(20f, 1L, 45_001L, ExpectedResult = true, TestName = "Barely moved in 45 s")]
    [TestCase(400f, 1L, 45_001L, ExpectedResult = false, TestName = "Moving, just behind a moving leader")]
    public bool AGroupMemberStuckFor45SecondsNoLongerHoldsTheGroup(float moved, long since, long now) =>
        AutonomousGroupMemberStall.IsStalled(moved, since, now);

    [Test]
    public void TheCatchUpBoostAppliesOnlyToABotThatWasToldToCatchUp()
    {
        // Camlann never calls CatchUp, so every bot keeps its own speed there.
        GameBot bot = Uninitialized<GameBot>();
        Assert.That(AutonomousGroupPace.Apply(bot, 200), Is.EqualTo(200), "no boost without a CatchUp order");
        Assert.That(AutonomousGroupPace.Apply(null, 200), Is.EqualTo(200));
    }

    // ---- 4. Battleground siege announcements (upstream event layer), RvR world --------------------------------------

    // Upstream UT_GameWideAnnouncements.BattlegroundSiegeAnnouncesOncePerQuietSpell (the layer is RealmRvrEventLayer here).
    [Test]
    public void BattlegroundSiegeAnnouncesOncePerQuietSpell()
    {
        long quiet = RealmRvrEventLayer.BattlegroundSiegeQuietMilliseconds;
        Assert.That(RealmRvrEventLayer.BattlegroundSiegeIsNew(0, 10_000), Is.True);
        Assert.That(RealmRvrEventLayer.BattlegroundSiegeIsNew(10_000, 10_000 + quiet - 1), Is.False);
        Assert.That(RealmRvrEventLayer.BattlegroundSiegeIsNew(10_000, 10_000 + quiet), Is.True);
    }

    // Upstream UT_GameWideAnnouncements.BattlegroundSiegeLinesNameTheHolder.
    [Test]
    public void BattlegroundSiegeLinesNameTheHolder()
    {
        Assert.That(GameWideAnnouncements.BattlegroundSiegeStarted(eRealm.Albion, "Dun Abermenai", "Abermenai", eRealm.None),
            Is.EqualTo("Albion's forces are assaulting the unclaimed Dun Abermenai in Abermenai!"));
        Assert.That(GameWideAnnouncements.BattlegroundSiegeStarted(eRealm.Midgard, "Dun Abermenai", "Abermenai", eRealm.Hibernia),
            Is.EqualTo("Midgard's forces have laid siege to Hibernia's Dun Abermenai in Abermenai!"));
    }

    // Upstream UT_UnreachableServicesAndDoorDetours.ARealmWithFewerThanAWaveInABattlegroundDoesNotAssault.
    [Test]
    public void ARealmWithFewerThanAWaveInABattlegroundDoesNotAssault()
    {
        Assert.That(AutonomousBattlegroundWave.IsTooFew(2), Is.True);
        Assert.That(AutonomousBattlegroundWave.IsTooFew(AutonomousBattlegroundWave.MinimumWave - 1), Is.True);
        Assert.That(AutonomousBattlegroundWave.IsTooFew(AutonomousBattlegroundWave.MinimumWave), Is.False);
    }

    // Upstream UT_EveningAuditFixes.BattlegroundWaveSize.
    [TestCase(30, 18)]
    [TestCase(10, 8)]
    [TestCase(0, 8)]
    public void BattlegroundWaveSize(int inside, int wave) =>
        Assert.That(AutonomousBattlegroundWave.WaveSize(inside), Is.EqualTo(wave));

    // ---- 5. Dashboard muster column (upstream AutonomousRvrDashboard) ------------------------------------------------

    [Test]
    public void TheRvrDashboardShowsTheMusterAndTheAttackWindowApart()
    {
        TimeSpan window = TimeSpan.FromMinutes(75);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrDashboard.MusterLines(5 * 60_000 + 1, window, rvrWorld: true),
                Is.EqualTo("Gathering: marching in 6m\nAttack window: 1h 15m left (present = at the keep)"));
            Assert.That(AutonomousRvrDashboard.MusterLines(0, window, rvrWorld: true),
                Is.EqualTo("Marching to the keep\nAttack window: 1h 15m left (present = at the keep)"));
            Assert.That(AutonomousRvrDashboard.MusterLines(-1, window, rvrWorld: true),
                Is.EqualTo("Attack window: 1h 15m left (present = at the keep)"), "no muster stage");
            Assert.That(AutonomousRvrDashboard.MusterLines(-1, window, rvrWorld: false),
                Is.EqualTo("Preparation: 1h 15m left"), "Camlann keeps its line");
        });
    }

    // ---- 6. Raid refill / regroup fixes (upstream AutonomousRealmRaid + coordinator) --------------------------------

    // Upstream UT_AuditFixesOct5.PooledForcedPartiesKeepTheFinalFourRule.
    [Test]
    public void PooledForcedPartiesKeepTheFinalFourRule()
    {
        Assert.That(AutonomousBotGroupCoordinator.ForcedPartyCanUseFinalFour(4, 4), Is.True);
        Assert.That(AutonomousBotGroupCoordinator.ForcedPartyCanUseFinalFour(4, 12), Is.False, "never strand the rest");
        Assert.That(AutonomousBotGroupCoordinator.ForcedPartyCanUseFinalFour(3, 4), Is.False);
    }

    // Upstream UT_RealmRaidRecruitment.ForcedRecruitsAreRankedByDistanceToTheRallyHub.
    [Test]
    public void ForcedRecruitsAreRankedByDistanceToTheRallyHub()
    {
        var hub = new Vector3(1000, 1000, 0);
        double near = AutonomousBotGroupCoordinator.ForcedRecruitDistance(1, 1300, 1400, 1, hub);
        double far = AutonomousBotGroupCoordinator.ForcedRecruitDistance(1, 90000, 1000, 1, hub);
        double otherRegion = AutonomousBotGroupCoordinator.ForcedRecruitDistance(100, 1000, 1000, 1, hub);
        Assert.That(near, Is.EqualTo(500).Within(0.001));
        Assert.That(near, Is.LessThan(far));
        Assert.That(far, Is.LessThan(otherRegion), "bots in another region come after every same-region bot");
    }

    // Upstream UT_StallAndRecoveryPolicies.RallyMemberEscapesAfterThreeFailedRoutes.
    [Test]
    public void RallyMemberEscapesAfterThreeFailedRoutes()
    {
        Assert.That(RealmRaidHubEscape.ShouldEscape(2), Is.False);
        Assert.That(RealmRaidHubEscape.ShouldEscape(3), Is.True);
    }

    // Upstream UT_LevelFiftyAuditFixes.StrandedExpeditionMemberRegroupsAfterThreeFailures.
    [TestCase(1, true, false, false)]
    [TestCase(2, true, false, false)]
    [TestCase(3, true, false, true)]
    [TestCase(5, true, false, true)]
    [TestCase(3, false, false, false)]   // dead members use normal corpse recovery
    [TestCase(3, true, true, false)]     // never teleport out of a fight
    public void StrandedExpeditionMemberRegroupsAfterThreeFailures(int failures, bool alive, bool inCombat, bool expected) =>
        Assert.That(AutonomousWorldBotController.ShouldRegroupStrandedMember(failures, alive, inCombat), Is.EqualTo(expected));

    [Test]
    public void TrashRespawnStaysAsItIsWithoutANeutralRaid()
    {
        GameNPC monster = Uninitialized<GameNPC>();
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRealmRaid.HasActiveRaidIn(249), Is.False, "no neutral raids are ported");
            Assert.That(AutonomousRealmRaid.HasActiveRaidIn(60), Is.False);
            Assert.That(AutonomousRealmRaid.RaidRespawnInterval(monster, 0), Is.EqualTo(0));
            Assert.That(AutonomousRealmRaid.RaidRespawnInterval(monster, AutonomousRealmRaid.RaidTrashRespawnMilliseconds),
                Is.EqualTo(AutonomousRealmRaid.RaidTrashRespawnMilliseconds));
            Assert.That(AutonomousRealmRaid.RaidRespawnInterval(Uninitialized<GameBot>(), 30_000), Is.EqualTo(30_000), "a bot keeps its own timer");
        });
    }

    [Test]
    public void AFailedFormationPostIsRetriedAfterThirtySeconds()
    {
        Assert.That(AutonomousRealmRaid.FailedPostRetryMilliseconds, Is.EqualTo(30_000));
        Assert.That(AutonomousRealmRaid.QueuedApproachDistance, Is.EqualTo(2_500f));
    }

    // ---- 8. BotBrain.SupportOffense MinValue overflow (both worlds) --------------------------------------------------

    [Test]
    public void ASupportBotsFirstTurnStartsAFight()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrSupportOffense.StartsNewFight(0, long.MinValue), Is.True, "never ran: new fight");
            Assert.That(AutonomousRvrSupportOffense.StartsNewFight(5_000_000, long.MinValue), Is.True);
            Assert.That(AutonomousRvrSupportOffense.StartsNewFight(10_000, 0), Is.False, "same fight within the gap");
            Assert.That(AutonomousRvrSupportOffense.StartsNewFight(AutonomousRvrSupportOffense.FightGapMilliseconds + 1, 0), Is.True);
        });
        // The old expression: the overflow made the very first gap negative.
        Assert.That(unchecked(0L - long.MinValue) > AutonomousRvrSupportOffense.FightGapMilliseconds, Is.False,
            "the bug: now - long.MinValue overflows");
    }

    // ---- 9. Relic rules: upstream in Realm-1, the R3b temple rule belongs to Camlann ---------------------------------

    [Test]
    public void TheR3bTempleRuleBelongsToCamlannAndTheRvrWorldRunsUpstream()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RvrRelicRules.TempleRulesApply(WorldModelKind.Realm), Is.False, "upstream 0.38c: the relic is the objective");
            Assert.That(RvrRelicRules.TempleRulesApply(WorldModelKind.Camlann), Is.True);
            Assert.That(PvpKeepCampaign.RealmCaptureApplies(WorldModelKind.Realm, relicKeep: true), Is.True);
            Assert.That(PvpKeepCampaign.RelicKeepStartRealm(WorldModelKind.Realm, true, eRealm.Midgard, eRealm.Albion), Is.Null,
                "a captured relic keep keeps its captor at start");
            // Upstream's pickup with no milegate: the own-relic rule and the own pad remain.
            Assert.That(RvrRelicRules.Pickup(eRealm.Midgard, true, eRealm.Albion, eRealm.Albion, templeOpen: true, takerHoldsOwnRelicOfType: true),
                Is.EqualTo(RvrRelicRules.PickupVerdict.Allowed));
            Assert.That(RvrRelicRules.Pickup(eRealm.Midgard, true, eRealm.Albion, eRealm.Albion, templeOpen: true, takerHoldsOwnRelicOfType: false),
                Is.EqualTo(RvrRelicRules.PickupVerdict.OwnRelicMissing));
            Assert.That(RvrRelicRules.Pickup(eRealm.Albion, true, eRealm.Albion, eRealm.Albion, templeOpen: true, takerHoldsOwnRelicOfType: true),
                Is.EqualTo(RvrRelicRules.PickupVerdict.OwnRealmHolds));
        });
    }
}
