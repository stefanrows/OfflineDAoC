using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// P6: RvR groups roam with a speed class and move as one body. Recruitment
/// wants a Bard, Skald or Minstrel when a group of three or more has none;
/// the speed is kept up while travelling and never in combat; the leader
/// waits for a straggler and members sprint to close a gap; RvR bots never
/// fall back to a walking pace.
/// </summary>
[TestFixture]
public sealed class UT_AutonomousRvrSpeed
{
    [Test]
    public void SiegeColumnKeepsDistantAndCrossRegionOperatorsUntilRecoveryOrExplicitFailure()
    {
        Assert.That(AutonomousRvrSpeed.SiegeCohesion(false, 27_000, 0, 0),
            Is.EqualTo(AutonomousRvrSpeed.SiegeCohesionDecision.Hold));
        Assert.That(AutonomousRvrSpeed.SiegeCohesion(true, float.PositiveInfinity, 60_000, 60_000),
            Is.EqualTo(AutonomousRvrSpeed.SiegeCohesionDecision.Hold));
        Assert.That(AutonomousRvrSpeed.SiegeCohesion(true, 27_000, 120_000, 120_000),
            Is.EqualTo(AutonomousRvrSpeed.SiegeCohesionDecision.Fail));
        Assert.That(AutonomousRvrSpeed.SiegeCohesion(true, 900, 300_000, 1_000),
            Is.EqualTo(AutonomousRvrSpeed.SiegeCohesionDecision.Fail));
        Assert.That(AutonomousRvrSpeed.SiegeCohesion(true, 600, 300_000, 120_000),
            Is.EqualTo(AutonomousRvrSpeed.SiegeCohesionDecision.Advance));
    }

    [TestCase(eCharacterClass.Bard)]
    [TestCase(eCharacterClass.Skald)]
    [TestCase(eCharacterClass.Minstrel)]
    public void SpeedClassesAreTheRealmPerformers(eCharacterClass characterClass)
    {
        Assert.That(AutonomousRvrSpeed.IsSpeedClass(characterClass), Is.True);
        Assert.That(AutonomousRvrSpeed.ProvidesSpeed(characterClass, false), Is.True);
    }

    [Test]
    public void HealerCountsAsSpeedOnlyWithItsGroupSpeed()
    {
        Assert.That(AutonomousRvrSpeed.IsSpeedClass(eCharacterClass.Healer), Is.False);
        Assert.That(AutonomousRvrSpeed.ProvidesSpeed(eCharacterClass.Healer, true), Is.True);
        Assert.That(AutonomousRvrSpeed.ProvidesSpeed(eCharacterClass.Healer, false), Is.False);
        Assert.That(AutonomousRvrSpeed.ProvidesSpeed(eCharacterClass.Cleric, true), Is.False);
    }

    [Test]
    public void RecruitmentStronglyPrefersSpeedWhenTheGroupHasNone()
    {
        int skald = AutonomousRvrSpeed.RecruitmentSpeedBonus(eCharacterClass.Skald, false, groupHasSpeed: false, plannedSize: 8);
        int healer = AutonomousRvrSpeed.RecruitmentSpeedBonus(eCharacterClass.Healer, true, groupHasSpeed: false, plannedSize: 8);
        int warrior = AutonomousRvrSpeed.RecruitmentSpeedBonus(eCharacterClass.Warrior, false, groupHasSpeed: false, plannedSize: 8);

        Assert.That(skald, Is.EqualTo(AutonomousRvrSpeed.PrimarySpeedBonus));
        // Below a missing healer, above a missing tank.
        Assert.That(skald, Is.LessThan(AutonomousRvrSpeed.MissingHealerWeight));
        Assert.That(skald, Is.GreaterThan(AutonomousRvrSpeed.MissingTankWeight));
        Assert.That(healer, Is.EqualTo(AutonomousRvrSpeed.FallbackSpeedBonus));
        Assert.That(warrior, Is.Zero);
    }

    [Test]
    public void RecruitmentDoesNotPreferSpeedOnceItIsInOrForSmallGroups()
    {
        Assert.That(AutonomousRvrSpeed.RecruitmentSpeedBonus(eCharacterClass.Minstrel, false, groupHasSpeed: true, plannedSize: 8),
            Is.Zero);
        Assert.That(AutonomousRvrSpeed.RecruitmentSpeedBonus(eCharacterClass.Bard, false, groupHasSpeed: false, plannedSize: 2),
            Is.Zero);
        Assert.That(AutonomousRvrSpeed.RecruitmentSpeedBonus(eCharacterClass.Bard, false, groupHasSpeed: false, plannedSize: 3),
            Is.EqualTo(AutonomousRvrSpeed.PrimarySpeedBonus));
    }

    [Test]
    public void TravellingAutonomousRvrPerformerSingsItsSpeed()
    {
        Assert.That(AutonomousRvrSpeed.FocusTravelSongs(autonomousRvrGroup: true, traveling: true, immediateCombat: false), Is.True);
    }

    [Test]
    public void NoSpeedFocusInCombatWhenParkedOrOutsideAutonomousRvr()
    {
        Assert.That(AutonomousRvrSpeed.FocusTravelSongs(true, traveling: true, immediateCombat: true), Is.False);
        Assert.That(AutonomousRvrSpeed.FocusTravelSongs(true, traveling: false, immediateCombat: false), Is.False);
        // Companions and player-led groups keep their own rule.
        Assert.That(AutonomousRvrSpeed.FocusTravelSongs(false, traveling: true, immediateCombat: false), Is.False);
    }

    [Test]
    public void HealerStopsOnceForItsGroupSpeedOnTheMarchButNotInCombat()
    {
        Assert.That(AutonomousRvrSpeed.CastsSpeedOnTheMarch(true, groupSpeedSpell: true, inCombat: false), Is.True);
        Assert.That(AutonomousRvrSpeed.CastsSpeedOnTheMarch(true, groupSpeedSpell: true, inCombat: true), Is.False);
        Assert.That(AutonomousRvrSpeed.CastsSpeedOnTheMarch(true, groupSpeedSpell: false, inCombat: false), Is.False);
        Assert.That(AutonomousRvrSpeed.CastsSpeedOnTheMarch(false, groupSpeedSpell: true, inCombat: false), Is.False);
    }

    [Test]
    public void LeaderHoldsWhenAMemberFallsMoreThan1200BehindOutOfCombat()
    {
        Assert.That(AutonomousRvrSpeed.LeaderHolds(false, 0, 60_000, worstGap: 1_300, anyCombat: false), Is.True);
        Assert.That(AutonomousRvrSpeed.LeaderHolds(false, 0, 60_000, worstGap: 1_100, anyCombat: false), Is.False);
        Assert.That(AutonomousRvrSpeed.LeaderHolds(false, 0, 60_000, worstGap: 1_300, anyCombat: true), Is.False);
    }

    [Test]
    public void HoldEndsWhenTheGroupClosesUpOrAfterSixSeconds()
    {
        Assert.That(AutonomousRvrSpeed.LeaderHolds(true, 2_000, 0, worstGap: 900, anyCombat: false), Is.True);
        Assert.That(AutonomousRvrSpeed.LeaderHolds(true, 2_000, 0, worstGap: 550, anyCombat: false), Is.False);
        Assert.That(AutonomousRvrSpeed.LeaderHolds(true, 6_000, 0, worstGap: 1_500, anyCombat: false), Is.False);
        // After a hold the group walks on a while before it waits again.
        Assert.That(AutonomousRvrSpeed.LeaderHolds(false, 0, 5_000, worstGap: 1_500, anyCombat: false), Is.False);
    }

    [Test]
    public void MemberSprintsToCloseASmallGapWithEndurance()
    {
        Assert.That(AutonomousRvrSpeed.FollowerSprints(gapToSlot: 500, inCombat: false, endurancePercent: 80, sprinting: false), Is.True);
        Assert.That(AutonomousRvrSpeed.FollowerSprints(600, false, 80, false), Is.True);
        Assert.That(AutonomousRvrSpeed.FollowerSprints(100, false, 80, false), Is.False);
        Assert.That(AutonomousRvrSpeed.FollowerSprints(500, false, 10, false), Is.False);
        Assert.That(AutonomousRvrSpeed.FollowerSprints(500, true, 80, false), Is.False);
    }

    [Test]
    public void SprintEndsAtTheSlotOrWhenEnduranceRunsOut()
    {
        Assert.That(AutonomousRvrSpeed.FollowerSprints(120, false, 50, sprinting: true), Is.True);
        Assert.That(AutonomousRvrSpeed.FollowerSprints(60, false, 50, sprinting: true), Is.False);
        Assert.That(AutonomousRvrSpeed.FollowerSprints(400, false, 8, sprinting: true), Is.False);
    }

    [Test]
    public void MemberAheadOfItsLeaderNeverOutrunsIt()
    {
        Assert.That(AutonomousRvrSpeed.CapFollowerSpeed(300, 250, aheadOfLeader: true), Is.EqualTo(250));
        Assert.That(AutonomousRvrSpeed.CapFollowerSpeed(300, 250, aheadOfLeader: false), Is.EqualTo(300));
    }

    [Test]
    public void RvrBotsNeverWalkAfterRelease()
    {
        Assert.That(AutonomousRvrSpeed.WalksAfterRelease(true, rvrObjective: true, inRvrGroup: false), Is.False);
        Assert.That(AutonomousRvrSpeed.WalksAfterRelease(true, rvrObjective: false, inRvrGroup: true), Is.False);
        Assert.That(AutonomousRvrSpeed.WalksAfterRelease(true, rvrObjective: false, inRvrGroup: false), Is.True);
    }

    [Test]
    public void OnlyAutonomousRvrBotsKeepPaceWhenHurt()
    {
        Assert.That(AutonomousRvrSpeed.KeepsPaceAtLowHealth(true, playerLed: false, rvrObjective: true), Is.True);
        Assert.That(AutonomousRvrSpeed.KeepsPaceAtLowHealth(true, playerLed: false, rvrObjective: false), Is.False);
        Assert.That(AutonomousRvrSpeed.KeepsPaceAtLowHealth(true, playerLed: true, rvrObjective: true), Is.False);
        Assert.That(AutonomousRvrSpeed.KeepsPaceAtLowHealth(false, playerLed: false, rvrObjective: true), Is.False);
    }

    [Test]
    public void GroupsWithoutSpeedFavourSpotsNearKeepsAndHubs()
    {
        double near = AutonomousRvrSpeed.NoSpeedRoamFactor(false, 5, 6_000);
        double far = AutonomousRvrSpeed.NoSpeedRoamFactor(false, 5, 12_000);
        Assert.That(near, Is.GreaterThan(1.0));
        Assert.That(far, Is.LessThan(1.0));
        Assert.That(AutonomousRvrSpeed.NoSpeedRoamFactor(true, 5, 12_000), Is.EqualTo(1.0));
        Assert.That(AutonomousRvrSpeed.NoSpeedRoamFactor(false, 2, 12_000), Is.EqualTo(1.0));
    }

    [Test]
    public void HealerComesFirstThenSpeedThenTank()
    {
        int speed = AutonomousRvrSpeed.RecruitmentSpeedBonus(eCharacterClass.Skald, false, false, 8);
        int healer = AutonomousRvrSpeed.RvrRecruitmentWeight(true, true, false, true, 0);
        int skald = AutonomousRvrSpeed.RvrRecruitmentWeight(false, true, false, true, speed);
        int tank = AutonomousRvrSpeed.RvrRecruitmentWeight(false, true, true, true, 0);
        int bard = AutonomousRvrSpeed.RvrRecruitmentWeight(true, true, false, true,
            AutonomousRvrSpeed.RecruitmentSpeedBonus(eCharacterClass.Bard, false, false, 8));
        Assert.That(healer, Is.GreaterThan(skald));
        Assert.That(skald, Is.GreaterThan(tank));
        Assert.That(bard, Is.GreaterThan(healer));
        // Once a healer is in, speed leads.
        Assert.That(AutonomousRvrSpeed.RvrRecruitmentWeight(false, false, false, true, speed),
            Is.GreaterThan(AutonomousRvrSpeed.RvrRecruitmentWeight(true, false, false, true, 0)));
    }

    [Test]
    public void SprintSurvivesTwoConsecutiveThinksForAnRvrWorldBot()
    {
        bool sprinting = false;
        int toggles = 0;
        for (int think = 0; think < 2; think++)
        {
            // The group speed rules decide first (the follower is 500 behind, then 400).
            bool wanted = AutonomousRvrSpeed.FollowerSprints(think == 0 ? 500 : 400, false, 80, sprinting);
            if (wanted != sprinting) { sprinting = wanted; toggles++; }
            // Then the companion stick cleanup runs; no stick run ever set its timestamp.
            if (AutonomousRvrSpeed.StickCleanupEndsSprint(sprinting, 1_000_000, 2_500, rvrOwnsSprint: true))
            { sprinting = false; toggles++; }
        }
        Assert.That(sprinting, Is.True);
        Assert.That(toggles, Is.EqualTo(1), "the sprint effect is created once, not recreated every think");
    }

    [Test]
    public void StickCleanupStillEndsACompanionsStaleSprint()
    {
        Assert.That(AutonomousRvrSpeed.StickCleanupEndsSprint(true, 3_000, 2_500, rvrOwnsSprint: false), Is.True);
        Assert.That(AutonomousRvrSpeed.StickCleanupEndsSprint(true, 1_000, 2_500, rvrOwnsSprint: false), Is.False);
    }

    [Test]
    public void HoldTimersVaryByAQuarterEitherWay()
    {
        Assert.That(AutonomousRvrSpeed.Jitter(6_000, 0), Is.EqualTo(4_500));
        Assert.That(AutonomousRvrSpeed.Jitter(6_000, 1), Is.EqualTo(7_500));
        Assert.That(AutonomousRvrSpeed.Jitter(10_000, 0.5), Is.EqualTo(10_000));
        Assert.That(AutonomousRvrSpeed.LeaderHolds(true, 5_000, 0, 1_500, false, 4_500, 10_000), Is.False);
        Assert.That(AutonomousRvrSpeed.LeaderHolds(true, 5_000, 0, 1_500, false, 7_500, 10_000), Is.True);
    }

    [Test]
    public void LeaderGivesUpOnAStragglerAfterThreeHolds()
    {
        Assert.That(AutonomousRvrSpeed.GivesUpOnStraggler(2), Is.False);
        Assert.That(AutonomousRvrSpeed.GivesUpOnStraggler(3), Is.True);
    }
}
