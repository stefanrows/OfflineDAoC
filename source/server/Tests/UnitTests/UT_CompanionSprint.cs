using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_CompanionSprint
{
    [Test]
    public void SprintsOnlyOnAStickRunBehindASprintingLeader()
    {
        Assert.That(CompanionSprint.ShouldSprint(stickRun: true, leaderSprinting: true), Is.True);
        Assert.That(CompanionSprint.ShouldSprint(stickRun: true, leaderSprinting: false), Is.False);
        Assert.That(CompanionSprint.ShouldSprint(stickRun: false, leaderSprinting: true), Is.False);
    }

    [Test]
    public void ATankInAssistModeSprintsBehindADistantTargetUntilItIsInMeleeRange()
    {
        const int melee = 200;
        const int far = melee + CompanionSprint.ChaseSlack + 1;

        Assert.That(CompanionSprint.ShouldChaseSprint(true, true, true, far, melee, 80, false), Is.True);
        Assert.That(CompanionSprint.ShouldChaseSprint(false, true, true, far, melee, 80, false), Is.False, "not a tank");
        Assert.That(CompanionSprint.ShouldChaseSprint(true, false, true, far, melee, 80, false), Is.False, "not in assist mode");
        Assert.That(CompanionSprint.ShouldChaseSprint(true, true, false, far, melee, 80, false), Is.False, "no hostile target");
        Assert.That(CompanionSprint.ShouldChaseSprint(true, true, true, melee + CompanionSprint.ChaseSlack, melee, 80, false), Is.False,
            "close enough to walk in");
    }

    [Test]
    public void AChasingTankStopsSprintingOnLowEnduranceUnlessChargeMakesTheSprintFree()
    {
        const int far = 1000;

        Assert.That(CompanionSprint.ShouldChaseSprint(true, true, true, far, 200, CompanionSprint.ChaseEnduranceFloorPercent, false, sprinting: true), Is.False);
        Assert.That(CompanionSprint.ShouldChaseSprint(true, true, true, far, 200, 0, true), Is.True);
    }

    [Test]
    public void AChasingTankResumesSprintingOnlyOnceEnduranceHasRecoveredPastTheResumeLevel()
    {
        const int far = 1000;
        int between = (CompanionSprint.ChaseEnduranceFloorPercent + CompanionSprint.ChaseEnduranceResumePercent) / 2;

        Assert.That(CompanionSprint.ShouldChaseSprint(true, true, true, far, 200, between, false, sprinting: true), Is.True,
            "keeps sprinting between the floor and the resume level");
        Assert.That(CompanionSprint.ShouldChaseSprint(true, true, true, far, 200, between, false, sprinting: false), Is.False,
            "does not restart there");
        Assert.That(CompanionSprint.ShouldChaseSprint(true, true, true, far, 200, CompanionSprint.ChaseEnduranceResumePercent + 1, false), Is.True);
    }

    [Test]
    public void SprintCostsFivePerSecondAndAnEnduranceBuffOffsetsIt()
    {
        Assert.That(CompanionSprint.SprintingRegen(regen: 1, 100, endurance: 50, maxEndurance: 100), Is.EqualTo(-4));
        Assert.That(CompanionSprint.SprintingRegen(regen: 6, 100, endurance: 50, maxEndurance: 100), Is.EqualTo(1),
            "A strong endurance regeneration buff out-regenerates the sprint.");
        Assert.That(CompanionSprint.SprintingRegen(regen: 6, 100, endurance: 100, maxEndurance: 100), Is.EqualTo(-5),
            "Like a player, a sprinter never sits at full endurance.");
    }
}
