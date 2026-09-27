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
    public void SprintCostsFivePerSecondAndAnEnduranceBuffOffsetsIt()
    {
        Assert.That(CompanionSprint.SprintingRegen(regen: 1, 100, endurance: 50, maxEndurance: 100), Is.EqualTo(-4));
        Assert.That(CompanionSprint.SprintingRegen(regen: 6, 100, endurance: 50, maxEndurance: 100), Is.EqualTo(1),
            "A strong endurance regeneration buff out-regenerates the sprint.");
        Assert.That(CompanionSprint.SprintingRegen(regen: 6, 100, endurance: 100, maxEndurance: 100), Is.EqualTo(-5),
            "Like a player, a sprinter never sits at full endurance.");
    }
}
