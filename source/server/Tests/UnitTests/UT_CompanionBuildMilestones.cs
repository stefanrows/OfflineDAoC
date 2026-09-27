using System.Collections.Generic;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// Automatic companions level breakpoint-first, the way players did: the key
/// spell of a line is trained as soon as the character level allows, instead
/// of every line trailing proportionally below its breakpoint.
/// </summary>
[TestFixture]
public sealed class UT_CompanionBuildMilestones
{
    private static IReadOnlyDictionary<string, int> Targets(eCharacterClass characterClass, string key, int level)
    {
        Assert.That(CompanionBuildPlanCatalog.TryFindPlan(characterClass, key, out CompanionBuildPlan plan), Is.True);
        return plan.GetTargetsAtLevel(level, plan.ExpectedSpecPointsMultiplier);
    }

    [TestCase("battlesongs")]
    [TestCase("songs")]
    [TestCase("hammer")]
    public void SkaldHasSpeedFiveAtLevel43(string key)
    {
        Assert.That(Targets(eCharacterClass.Skald, key, 43)["Battlesongs"], Is.EqualTo(43));
        Assert.That(Targets(eCharacterClass.Skald, key, 30)["Battlesongs"], Is.EqualTo(30));
    }

    [Test]
    public void AugmentationShamanHasInstantAreaDiseaseBeforeLevel45()
    {
        Assert.That(Targets(eCharacterClass.Shaman, "augmentation", 43)["Subterranean"], Is.EqualTo(27));
        Assert.That(Targets(eCharacterClass.Shaman, "augmentation", 25)["Mending"], Is.GreaterThanOrEqualTo(7));
    }

    [TestCase("stormcalling")]
    [TestCase("melee")]
    public void ShieldThaneHasSlamBy45(string key)
    {
        Assert.That(Targets(eCharacterClass.Thane, key, 45)["Shields"], Is.EqualTo(42));
    }

    [Test]
    public void StormcallingThaneHasInstantRamAt34()
    {
        Assert.That(Targets(eCharacterClass.Thane, "stormcalling", 34)["Stormcalling"], Is.EqualTo(34));
    }

    [Test]
    public void SupportHealerHasCelerityAndCureMezBy40()
    {
        IReadOnlyDictionary<string, int> targets = Targets(eCharacterClass.Healer, "support", 40);
        Assert.Multiple(() =>
        {
            Assert.That(targets["Augmentation"], Is.GreaterThanOrEqualTo(18));
            Assert.That(targets["Pacification"], Is.EqualTo(23));
        });
    }

    [Test]
    public void PacificationHealerHasInstantAreaStunBy43()
    {
        Assert.That(Targets(eCharacterClass.Healer, "pacification", 43)["Pacification"], Is.GreaterThanOrEqualTo(38));
    }

    [Test]
    public void PlansWithoutMilestonesKeepTheProportionalSchedule()
    {
        Assert.That(CompanionBuildPlanCatalog.TryFindPlan(eCharacterClass.Thane, "twohanded", out CompanionBuildPlan plan), Is.True);
        Assert.That(plan.Milestones, Is.Empty);
        IReadOnlyDictionary<string, int> targets = plan.GetTargetsAtLevel(45, plan.ExpectedSpecPointsMultiplier);
        Assert.That(targets["Hammer"], Is.EqualTo(44));
    }
}
