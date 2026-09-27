using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS;
using DOL.GS.Styles;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_CompanionBuildRuntimeAlignment
{
    [Test]
    public void BardBattleUsesItsTrainedBluntLineAndInstrumentProfile()
    {
        Assert.That(CompanionBuildPlanCatalog.TryFindPlan(eCharacterClass.Bard, "battle", out var plan), Is.True);
        var profile = new BotSpec();

        Assert.That(BotLifetimeBuild.ConfigureForCompanionPlan(profile, plan), Is.True);
        Assert.That(profile.SpecType, Is.EqualTo(eSpecType.Instrument));
        Assert.That(profile.WeaponOneType, Is.EqualTo(eObjectType.Blunt));
        Assert.That(profile.SpecLines.Single(line => line.Spec == "Blunt").SpecCap, Is.EqualTo((uint)29));
    }

    [Test]
    public void ChampionLargeWeaponsShieldPlanKeepsAClassLegalOneHandGuardWeapon()
    {
        Assert.That(CompanionBuildPlanCatalog.TryFindPlan(eCharacterClass.Champion, "large", out var plan), Is.True);
        var profile = new BotSpec();

        Assert.That(BotLifetimeBuild.ConfigureForCompanionPlan(profile, plan), Is.True);
        Assert.That(profile.WeaponOneType, Is.EqualTo(eObjectType.Blades));
        Assert.That(profile.WeaponTwoType, Is.EqualTo(eObjectType.LargeWeapons));
        Assert.That(profile.SpecType, Is.EqualTo(eSpecType.TwoHandHybrid));
        Assert.That(profile.Is2H, Is.True);
    }

    [Test]
    public void HunterSpearBuildUsesItsClassTwoHandEquipmentPath()
    {
        Assert.That(CompanionBuildPlanCatalog.TryFindPlan(eCharacterClass.Hunter, "archery", out var plan), Is.True);
        var profile = new BotSpec();

        Assert.That(BotLifetimeBuild.ConfigureForCompanionPlan(profile, plan), Is.True);
        Assert.That(profile.WeaponOneType, Is.EqualTo(eObjectType.Spear));
        Assert.That(profile.SpecType, Is.EqualTo(eSpecType.None));
        Assert.That(profile.Is2H, Is.True);
    }

    [TestCase(eCharacterClass.Scout, "melee", false)]
    [TestCase(eCharacterClass.Scout, "bow", true)]
    [TestCase(eCharacterClass.Scout, "hybrid", true)]
    [TestCase(eCharacterClass.Hunter, "general-pve-v1-hunter", false)]
    [TestCase(eCharacterClass.Hunter, "archery", true)]
    [TestCase(eCharacterClass.Ranger, "melee", false)]
    [TestCase(eCharacterClass.Ranger, "archery", true)]
    public void RangedAutoChoiceFollowsTheSelectedBuildBowInvestment(
        eCharacterClass characterClass, string planId, bool expected)
    {
        Assert.That(CompanionBuildPlanCatalog.TryFindPlan(characterClass, planId, out var plan), Is.True);

        Assert.That(BotRangedCombat.PlanTrainsRangedWeapon(characterClass, plan.TargetAllocations), Is.EqualTo(expected));
    }

    [Test]
    public void CasterDamageSelectionWeightsTrainedLinesButKeepsSecondaryLinesAvailable()
    {
        Spell darkness = NewSpell(1001, "Darkness bolt", 30);
        Spell suppression = NewSpell(1002, "Suppression bolt", 40);
        var candidates = new[] { suppression, darkness };
        var lineBySpell = new Dictionary<int, string>
        {
            [darkness.ID] = "Darkness",
            [suppression.ID] = "Suppression",
        };
        CompanionBuildRank[] allocations =
        [
            new("Darkness", 50),
            new("Suppression", 20),
        ];

        Spell preferred = BotCasterPriority.ChoosePlanDamageSpell(candidates,
            spell => lineBySpell[spell.ID], allocations, new FixedRandom(first: true));
        Spell secondary = BotCasterPriority.ChoosePlanDamageSpell(candidates,
            spell => lineBySpell[spell.ID], allocations, new FixedRandom(first: false));

        Assert.That(preferred, Is.SameAs(darkness));
        Assert.That(secondary, Is.SameAs(suppression));
    }

    [Test]
    public void DurationDamageIsAppliedOnceBeforePlanWeightedDirectDamage()
    {
        Spell dot = NewSpell(1003, "Damage over time", 20, eSpellType.DamageOverTime, duration: 10);
        Spell nuke = NewSpell(1004, "Direct damage", 30);

        Assert.That(BotCasterPriority.ChooseDurationApplication([nuke, dot], _ => false), Is.SameAs(dot));
        Assert.That(BotCasterPriority.ChooseDurationApplication([nuke, dot], spell => spell == dot), Is.Null);
    }

    [Test]
    public void PositionalStealthStyleIsAllowedOnlyByTheStealthedOpenerGate()
    {
        var style = new Style(new DbStyle
        {
            ID = 1005,
            StyleID = 1005,
            Name = "Perforate Artery",
            SpecKeyName = "Critical Strike",
            SpecLevelRequirement = 21,
            StealthRequirement = true,
            OpeningRequirementType = (int)Style.eOpening.Positional,
            OpeningRequirementValue = (int)Style.eOpeningPosition.Front,
        }, null);

        Assert.That(BotMeleeStylePolicy.IsEligibleStealthOpener(style, isStealthed: true), Is.True);
        Assert.That(BotMeleeStylePolicy.IsEligibleStealthOpener(style, isStealthed: false), Is.False);
        Assert.That(BotMeleeStylePolicy.IsEligible(style), Is.False);
    }

    private static Spell NewSpell(int id, string name, int level,
        eSpellType type = eSpellType.DirectDamage, int duration = 0) =>
        new(new DbSpell
        {
            SpellID = id,
            Name = name,
            Target = eSpellTarget.ENEMY.ToString(),
            Type = type.ToString(),
            Range = 1_500,
            Damage = 35,
            Duration = duration,
        }, level);

    private sealed class FixedRandom(bool first) : Random
    {
        public override int Next(int maxValue) => first ? 0 : maxValue - 1;
    }
}
