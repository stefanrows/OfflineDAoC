using System;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// RvR groups read their doctrine from their real classes and play by habits
/// (caller, soft healer/mezzer priority, stickiness, optional retreat) rather
/// than perfect rules.
/// </summary>
[TestFixture]
public sealed class UT_AutonomousRvrDoctrine
{
    private static RvrDoctrine Derive(AutonomousPlayerType leader, params (eCharacterClass Class, int Level)[] members) =>
        AutonomousRvrDoctrine.Derive(Array.ConvertAll(members, member => new RvrDoctrineMember(member.Class, member.Level)),
            leader, RvrLeaderTraits.Neutral);

    [Test]
    public void AaronsMidgardGroupIsABombGroup()
    {
        RvrDoctrine doctrine = Derive(AutonomousPlayerType.Roamer,
            (eCharacterClass.Spiritmaster, 45), (eCharacterClass.Spiritmaster, 45), (eCharacterClass.Healer, 45),
            (eCharacterClass.Healer, 45), (eCharacterClass.Shaman, 45), (eCharacterClass.Skald, 45),
            (eCharacterClass.Thane, 44), (eCharacterClass.Thane, 44));
        Assert.That(doctrine.Kind, Is.EqualTo(RvrDoctrineKind.BombGroup));
        Assert.That(doctrine.Travel, Is.EqualTo(RvrTravelShape.Clump));
    }

    [Test]
    public void ClassicEightManIsAnAssistTrainWithACaller()
    {
        RvrDoctrine doctrine = Derive(AutonomousPlayerType.Roamer,
            (eCharacterClass.Cleric, 50), (eCharacterClass.Cleric, 50), (eCharacterClass.Sorcerer, 50),
            (eCharacterClass.Minstrel, 50), (eCharacterClass.Armsman, 50), (eCharacterClass.Mercenary, 50),
            (eCharacterClass.Friar, 50), (eCharacterClass.Theurgist, 50));
        Assert.That(doctrine.Kind, Is.EqualTo(RvrDoctrineKind.AssistTrain));
        Assert.That(doctrine.HasCaller, Is.True);
    }

    [TestCase(eCharacterClass.Shadowblade, RvrDoctrineKind.SoloAssassin)]
    [TestCase(eCharacterClass.Warrior, RvrDoctrineKind.LoneRoamer)]
    public void ASoloBotKnowsWhatItIs(eCharacterClass characterClass, RvrDoctrineKind expected)
    {
        Assert.That(Derive(AutonomousPlayerType.Roamer, (characterClass, 40)).Kind, Is.EqualTo(expected));
    }

    [Test]
    public void StealthersTogetherAreAStealthPackAndHealerDuosAreCasterDuos()
    {
        Assert.That(Derive(AutonomousPlayerType.Roamer, (eCharacterClass.Nightshade, 50), (eCharacterClass.Nightshade, 50)).Kind,
            Is.EqualTo(RvrDoctrineKind.StealthPack));
        Assert.That(Derive(AutonomousPlayerType.Roamer, (eCharacterClass.Healer, 50), (eCharacterClass.Spiritmaster, 50)).Kind,
            Is.EqualTo(RvrDoctrineKind.CasterDuo));
    }

    [Test]
    public void AnImperfectGroupStillRoamsAsAPickupGroup()
    {
        RvrDoctrine doctrine = Derive(AutonomousPlayerType.Casual,
            (eCharacterClass.Berserker, 40), (eCharacterClass.Warrior, 40), (eCharacterClass.Runemaster, 40),
            (eCharacterClass.Savage, 40), (eCharacterClass.Hunter, 40), (eCharacterClass.Shaman, 40));
        Assert.That(doctrine.Kind, Is.EqualTo(RvrDoctrineKind.PickupGroup));
        Assert.That(doctrine.RetreatBias, Is.GreaterThan(0));
    }

    [Test]
    public void SiegeAndDefenseOverrideComposition()
    {
        var members = new[] { new RvrDoctrineMember(eCharacterClass.Warrior, 50) };
        Assert.That(AutonomousRvrDoctrine.Derive(members, AutonomousPlayerType.KeepWarrior, RvrLeaderTraits.Neutral,
            siegeCommitted: true).Kind, Is.EqualTo(RvrDoctrineKind.KeepRaid));
        Assert.That(AutonomousRvrDoctrine.Derive(members, AutonomousPlayerType.KeepWarrior, RvrLeaderTraits.Neutral,
            defendingKeep: true).Kind, Is.EqualTo(RvrDoctrineKind.KeepDefense));
    }

    [Test]
    public void PersonalityColoursTheHabit()
    {
        var members = new[] { new RvrDoctrineMember(eCharacterClass.Healer, 50), new RvrDoctrineMember(eCharacterClass.Hero, 50),
            new RvrDoctrineMember(eCharacterClass.Bard, 50) };
        RvrDoctrine bold = AutonomousRvrDoctrine.Derive(members, AutonomousPlayerType.Roamer, new(85, 85, 85));
        RvrDoctrine timid = AutonomousRvrDoctrine.Derive(members, AutonomousPlayerType.Roamer, new(15, 15, 15));
        Assert.Multiple(() =>
        {
            Assert.That(bold.Appetite, Is.GreaterThan(timid.Appetite));
            Assert.That(bold.RetreatBias, Is.LessThan(timid.RetreatBias));
            Assert.That(bold.LingerMaxSeconds, Is.GreaterThan(timid.LingerMaxSeconds));
        });
    }

    [Test]
    public void HealersAndMezzersDrawTheMostAttentionTanksTheLeast()
    {
        double healer = AutonomousRvrDoctrine.TargetPriority(AutonomousRvrDoctrine.TraitsOf(eCharacterClass.Cleric));
        double mezzer = AutonomousRvrDoctrine.TargetPriority(AutonomousRvrDoctrine.TraitsOf(eCharacterClass.Sorcerer));
        double caster = AutonomousRvrDoctrine.TargetPriority(AutonomousRvrDoctrine.TraitsOf(eCharacterClass.Wizard));
        double tank = AutonomousRvrDoctrine.TargetPriority(AutonomousRvrDoctrine.TraitsOf(eCharacterClass.Armsman));
        Assert.That(healer, Is.GreaterThan(mezzer));
        Assert.That(mezzer, Is.GreaterThan(caster));
        Assert.That(caster, Is.GreaterThan(tank));
    }

    [Test]
    public void FightAppetiteTakesFairFightsAndSometimesDares()
    {
        RvrDoctrine assist = Derive(AutonomousPlayerType.Roamer,
            (eCharacterClass.Cleric, 50), (eCharacterClass.Cleric, 50), (eCharacterClass.Sorcerer, 50),
            (eCharacterClass.Minstrel, 50), (eCharacterClass.Armsman, 50), (eCharacterClass.Mercenary, 50),
            (eCharacterClass.Friar, 50), (eCharacterClass.Theurgist, 50));
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrDoctrine.AcceptsFight(assist, 8, 50, 8, 50, 0.99), Is.True);
            Assert.That(AutonomousRvrDoctrine.AcceptsFight(assist, 8, 50, 30, 50, 0.0), Is.False, "Never a zerg head-on.");
            Assert.That(AutonomousRvrDoctrine.AcceptsFight(null, 4, 50, 5, 50, 0.0), Is.False, "No doctrine keeps the old rule.");
        });
    }

    [Test]
    public void RetreatIsADecisionNotAReflex()
    {
        RvrDoctrine pickup = Derive(AutonomousPlayerType.Casual,
            (eCharacterClass.Berserker, 40), (eCharacterClass.Warrior, 40), (eCharacterClass.Shaman, 40));
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrDoctrine.ShouldRetreat(pickup, 3, 3, 1, 1, 2, 0.5), Is.False, "Even fight: stay.");
            Assert.That(AutonomousRvrDoctrine.ShouldRetreat(pickup, 2, 3, 0, 1, 5, 0.5), Is.True, "Healer dead, outnumbered: leave.");
            Assert.That(AutonomousRvrDoctrine.ShouldRetreat(pickup, 2, 3, 0, 1, 5, 0.05), Is.False, "Some groups stay in anyway.");
        });
    }

    [Test]
    public void MembersStickFollowTheCallerOrLeanTowardHealers()
    {
        RvrDoctrine assist = Derive(AutonomousPlayerType.Roamer,
            (eCharacterClass.Cleric, 50), (eCharacterClass.Cleric, 50), (eCharacterClass.Sorcerer, 50),
            (eCharacterClass.Minstrel, 50), (eCharacterClass.Armsman, 50), (eCharacterClass.Mercenary, 50));
        RvrTargetView tank = new(AutonomousRvrDoctrine.TraitsOf(eCharacterClass.Armsman), 300, 100, false, false, false);
        RvrTargetView healer = new(AutonomousRvrDoctrine.TraitsOf(eCharacterClass.Cleric), 300, 100, false, false, false);
        RvrTargetView called = new(AutonomousRvrDoctrine.TraitsOf(eCharacterClass.Wizard), 600, 100, true, false, false);
        RvrTargetView[] views = [tank, healer, called];

        Assert.That(AutonomousRvrCombatHabits.Choose(views, 0, keepCurrent: true, assist, 0.0, 0.0), Is.EqualTo(0),
            "A member stays on its target until its stick time runs out.");
        Assert.That(AutonomousRvrCombatHabits.Choose(views, -1, false, assist, 0.1, 0.99), Is.EqualTo(2),
            "Most of the time the called target wins.");
        Assert.That(AutonomousRvrCombatHabits.Weight(healer, assist), Is.GreaterThan(AutonomousRvrCombatHabits.Weight(tank, assist)));
    }

    [Test]
    public void ColumnMarchesTwoFileBehindTheLeader()
    {
        Vector3 leader = new(1000, 1000, 0);
        Vector3 first = AutonomousRvrDoctrineGeometry.TravelSlot(RvrTravelShape.Column, 0, 6, leader, new Vector2(0, 1));
        Vector3 second = AutonomousRvrDoctrineGeometry.TravelSlot(RvrTravelShape.Column, 1, 6, leader, new Vector2(0, 1));
        Vector3 fourth = AutonomousRvrDoctrineGeometry.TravelSlot(RvrTravelShape.Column, 3, 6, leader, new Vector2(0, 1));
        Assert.Multiple(() =>
        {
            Assert.That(first.Y, Is.LessThan(leader.Y), "Behind the leader.");
            Assert.That(first.X, Is.Not.EqualTo(second.X), "Two files side by side.");
            Assert.That(fourth.Y, Is.LessThan(second.Y), "The next row is further back.");
            Assert.That(AutonomousRvrDoctrineGeometry.MarchRank(AutonomousRvrDoctrine.TraitsOf(eCharacterClass.Warrior)),
                Is.LessThan(AutonomousRvrDoctrineGeometry.MarchRank(AutonomousRvrDoctrine.TraitsOf(eCharacterClass.Healer))));
        });
    }

    [Test]
    public void RetreatRunsAwayFromTheEnemy()
    {
        Vector3 away = AutonomousRvrDoctrineGeometry.AwayFrom(new Vector3(0, 0, 0), new Vector3(100, 0, 0), 2000);
        Assert.That(away.X, Is.EqualTo(-2000).Within(0.01));
    }

    [Test]
    public void FightsLeaveHeatThatFades()
    {
        typeof(AutonomousRvrHeat).GetMethod("ResetForTests", System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static)!.Invoke(null, null);
        DateTime now = new(2026, 9, 27, 20, 0, 0, DateTimeKind.Utc);
        AutonomousRvrHeat.Record(163, new Vector3(1000, 1000, 0), now);
        AutonomousRvrHeat.Record(163, new Vector3(1200, 1000, 0), now.AddMinutes(1));
        AutonomousRvrHeat.Record(163, new Vector3(9000, 9000, 0), now.AddMinutes(2));
        Assert.That(AutonomousRvrHeat.Recent(163, now.AddMinutes(3)), Has.Count.EqualTo(2), "Nearby fights merge.");
        Assert.That(AutonomousRvrHeat.Recent(163, now.AddMinutes(30)), Is.Empty);
    }

    [Test]
    public void GuildmatesAnswerByTheirSociabilityAndLossesMakeCrewsWary()
    {
        int social = 0, loner = 0;
        for (long helper = 1; helper <= 400; helper++)
        {
            if (AutonomousGuildCohesion.Answers(85, helper, 7, 0)) social++;
            if (AutonomousGuildCohesion.Answers(15, helper, 7, 0)) loner++;
        }
        Assert.That(social, Is.GreaterThan(loner));
        Assert.That(AutonomousGuildEncounterMemory.Factor(0, 5), Is.LessThan(1));
        Assert.That(AutonomousGuildEncounterMemory.Factor(5, 0), Is.GreaterThan(1));
    }
}

[TestFixture]
public sealed class UT_AutonomousRvrHotspots
{
    [Test]
    public void EmainMachaDrawsMoreThanBorderZonesAndOrdinaryZones()
    {
        Assert.That(AutonomousRvrHotspots.Weight("Emain Macha"), Is.GreaterThan(AutonomousRvrHotspots.Weight("Odin's Gate")));
        Assert.That(AutonomousRvrHotspots.Weight("Hadrian's Wall"), Is.GreaterThan(AutonomousRvrHotspots.Weight("Uppland")));
        Assert.That(AutonomousRvrHotspots.Weight(null), Is.EqualTo(1.0));
    }
}

[TestFixture]
public sealed class UT_AutonomousRvrLfg
{
    [TestCase(eCharacterClass.Healer, AutonomousPlayerType.Roamer, 50, true)]
    [TestCase(eCharacterClass.Warrior, AutonomousPlayerType.KeepWarrior, 50, true)]
    [TestCase(eCharacterClass.Shadowblade, AutonomousPlayerType.Roamer, 50, false)]
    [TestCase(eCharacterClass.Hunter, AutonomousPlayerType.Roamer, 50, false)]
    [TestCase(eCharacterClass.Healer, AutonomousPlayerType.Hunter, 50, false)]
    [TestCase(eCharacterClass.Healer, AutonomousPlayerType.Roamer, 15, false)]
    public void GroupSeekersWaitAtTheKeepSoloistsLeave(eCharacterClass characterClass, AutonomousPlayerType type,
        int level, bool seeks)
    {
        Assert.That(AutonomousRvrLfg.SeeksGroup(characterClass, type, level), Is.EqualTo(seeks));
    }

    [Test]
    public void PatienceDecidesTheWaitAndAPartialDeathIsNoWipe()
    {
        Assert.That(AutonomousRvrLfg.PatienceMilliseconds(0), Is.EqualTo(8 * 60_000));
        Assert.That(AutonomousRvrLfg.PatienceMilliseconds(100), Is.EqualTo(20 * 60_000));
        Assert.That(AutonomousRvrLfg.IsWipe(6, 8, true), Is.False, "Two dead: fight on, rez later.");
        Assert.That(AutonomousRvrLfg.IsWipe(3, 8, true), Is.False, "A living rezzer can bring them back.");
        Assert.That(AutonomousRvrLfg.IsWipe(3, 8, false), Is.True, "Most dead and nobody to rez.");
        Assert.That(AutonomousRvrLfg.IsWipe(1, 8, true), Is.True);
    }
}

[TestFixture]
public sealed class UT_AutonomousGroupMotion
{
    [Test]
    public void FollowersMatchTheLeaderWithAPersonalStrideAndCatchUpWhenBehind()
    {
        short near = AutonomousGroupMotion.FollowSpeed(1, 200, 250, 30);
        short other = AutonomousGroupMotion.FollowSpeed(5, 200, 250, 30);
        short behind = AutonomousGroupMotion.FollowSpeed(1, 200, 250, 400);
        Assert.Multiple(() =>
        {
            Assert.That(near, Is.InRange(190, 210), "In the slot: the leader's pace, give or take a stride.");
            Assert.That(near, Is.Not.EqualTo(other), "Members do not march in lockstep.");
            Assert.That(behind, Is.GreaterThan(near), "Behind: catch up.");
            Assert.That(AutonomousGroupMotion.FollowSpeed(1, 200, 100, 2_000), Is.LessThanOrEqualTo(130), "Never absurdly fast.");
        });
    }
}

[TestFixture]
public sealed class UT_CompanionFollowStyle
{
    [Test]
    public void CompanionsIgnoreTheFirstStepsStickOnSpeedRunsAndOtherwiseSpread()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CompanionFollowStyle.Choose(true, 500, 191, 150, 200, false), Is.EqualTo(CompanionFollowStyle.Hold),
                "The player just started walking and is still close: stay put.");
            Assert.That(CompanionFollowStyle.Choose(true, 2_000, 191, 150, 200, false), Is.EqualTo(CompanionFollowStyle.Spread));
            Assert.That(CompanionFollowStyle.Choose(true, 4_000, 390, 300, 200, true), Is.EqualTo(CompanionFollowStyle.Stick),
                "A long run with speed: /stick behind the player.");
            Assert.That(CompanionFollowStyle.Choose(true, 500, 390, 300, 200, false), Is.EqualTo(CompanionFollowStyle.Stick),
                "A speed run must not give the leader several seconds of head start.");
            Assert.That(CompanionFollowStyle.Choose(true, 500, 191, 150, 200, false), Is.EqualTo(CompanionFollowStyle.Hold),
                "Normal walking still ignores the first steps.");
            Assert.That(CompanionFollowStyle.Choose(false, 0, 0, 150, 200, false), Is.EqualTo(CompanionFollowStyle.Spread));
            Assert.That(CompanionFollowStyle.StickDistance(2), Is.GreaterThan(CompanionFollowStyle.StickDistance(0)));
        });
    }
}

[TestFixture]
public sealed class UT_AutonomousRaidSchedule
{
    [Test]
    public void LevellersSignUpMoreThanKeepWarriorsAndSociableBotsMoreThanLoners()
    {
        Assert.That(AutonomousRaidSchedule.SignUpChance(AutonomousPlayerType.Leveler, 50),
            Is.GreaterThan(AutonomousRaidSchedule.SignUpChance(AutonomousPlayerType.KeepWarrior, 50)));
        Assert.That(AutonomousRaidSchedule.SignUpChance(AutonomousPlayerType.Hybrid, 85),
            Is.GreaterThan(AutonomousRaidSchedule.SignUpChance(AutonomousPlayerType.Hybrid, 15)));
    }

    [Test]
    public void ScheduledRaidsNeedMostSignUpsPresentAndTheirOwnStagingTime()
    {
        int minimum = RealmRaidRecruitmentPolicy.ScheduledMinimumPresent(80);
        Assert.Multiple(() =>
        {
            Assert.That(minimum, Is.EqualTo(48));
            Assert.That(RealmRaidRecruitmentPolicy.ScheduledMinimumPresent(10), Is.EqualTo(24));
            Assert.That(RealmRaidRecruitmentPolicy.Ready(true, RealmRaidRecruitmentPolicy.ScheduledStagingMilliseconds, 48, true,
                minimum, RealmRaidRecruitmentPolicy.ScheduledStagingMilliseconds), Is.True);
            Assert.That(RealmRaidRecruitmentPolicy.Ready(true, RealmRaidRecruitmentPolicy.ScheduledStagingMilliseconds, 47, true,
                minimum, RealmRaidRecruitmentPolicy.ScheduledStagingMilliseconds), Is.False);
            Assert.That(RealmRaidRecruitmentPolicy.DepartHub(false, 48, minimum), Is.True);
            Assert.That(RealmRaidRecruitmentPolicy.Ready(false, RealmRaidRecruitmentPolicy.AutonomousMinimumStagingMilliseconds,
                199, true), Is.False, "Automatic raids keep their 200 head count.");
        });
    }
}
