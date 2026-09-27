using System;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public class UT_CompanionCrowdControlRole
{
    [Test]
    public void ExistingRoleValuesKeepTheirNumbersForTheNativeFilter()
    {
        Assert.Multiple(() =>
        {
            Assert.That((int)BotPveGroupRole.Tank, Is.EqualTo(0));
            Assert.That((int)BotPveGroupRole.Healer, Is.EqualTo(1));
            Assert.That((int)BotPveGroupRole.Buffer, Is.EqualTo(2));
            Assert.That((int)BotPveGroupRole.Attacker, Is.EqualTo(3));
            Assert.That((int)BotPveGroupRole.CrowdControl, Is.EqualTo(4));
        });
    }

    [TestCase("cc", true, BotPveGroupRole.CrowdControl)]
    [TestCase("crowd control", true, BotPveGroupRole.CrowdControl)]
    [TestCase("Crowd-Control", true, BotPveGroupRole.CrowdControl)]
    [TestCase("crowdcontrol", true, BotPveGroupRole.CrowdControl)]
    [TestCase("healer", true, BotPveGroupRole.Healer)]
    [TestCase("4", false, BotPveGroupRole.Tank)]
    [TestCase("mezzer", false, BotPveGroupRole.Tank)]
    [TestCase("", false, BotPveGroupRole.Tank)]
    public void RoleTextAcceptsNamesAndCrowdControlAliasesOnly(string text, bool accepted, BotPveGroupRole expected)
    {
        Assert.That(BotPartyRoles.TryParseRole(text, out BotPveGroupRole role), Is.EqualTo(accepted));
        if (accepted)
            Assert.That(role, Is.EqualTo(expected));
    }

    [Test]
    public void OnlyClassesWithAPlainMezzCanFillCrowdControl()
    {
        eCharacterClass[] crowdControl = Enum.GetValues<eCharacterClass>()
            .Where(characterClass => BotPartyRoles.CanFill(characterClass, BotPveGroupRole.CrowdControl)).ToArray();
        Assert.That(crowdControl, Is.EquivalentTo(new[]
        {
            eCharacterClass.Healer, eCharacterClass.Sorcerer, eCharacterClass.Bard,
            eCharacterClass.Mentalist, eCharacterClass.Spiritmaster,
        }));
        Assert.That(BotPartyRoles.Label(eCharacterClass.Healer), Does.EndWith("/Crowd control"));
        Assert.That(BotPartyRoles.Label(eCharacterClass.Cleric), Does.Not.Contain("Crowd control"));
    }

    [TestCase("trispec", BotPveGroupRole.Healer, true)]
    [TestCase("mending", BotPveGroupRole.Healer, false)]
    [TestCase("augmentation", BotPveGroupRole.Buffer, false)]
    [TestCase("pacification", BotPveGroupRole.CrowdControl, false)]
    [TestCase("support", BotPveGroupRole.Healer, false)]
    public void HealerBuildsFollowTheOwnersRoleMapping(string key, BotPveGroupRole role, bool addControl)
    {
        Assert.That(CompanionBuildPlanCatalog.TryFindPlan(eCharacterClass.Healer, key, out CompanionBuildPlan plan), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(plan.PrimaryRole, Is.EqualTo(role));
            Assert.That(plan.CrowdControlDuty, Is.EqualTo(addControl));
        });
    }

    [Test]
    public void EveryBuildRoleIsClassLegal()
    {
        foreach (CompanionBuildPlan plan in CompanionBuildPlanCatalog.GetEnabledPlans())
        {
            Assert.That(BotPartyRoles.CanFill(plan.CharacterClass, plan.PrimaryRole), Is.True,
                $"{plan.Id} sets {plan.PrimaryRole}, which a {plan.CharacterClass} cannot fill.");
            if (plan.CrowdControlDuty)
                Assert.That(BotPartyRoles.IsCrowdControlClass(plan.CharacterClass), Is.True, plan.Id);
        }
        Assert.That(CompanionBuildPlanCatalog.GetEnabledPlans()
            .Where(plan => plan.PrimaryRole == BotPveGroupRole.CrowdControl || plan.CrowdControlDuty)
            .Select(plan => plan.Id), Is.EquivalentTo(new[]
        {
            "general-pve-v1-healer", "pacification-v1-healer", "general-pve-v1-sorcerer",
            "music-v1-bard", "battle-v1-bard", "mentalism-v1-mentalist",
        }));
    }

    [Test]
    public void SmiteClericAndNatureDruidCanFillAttackerRole()
    {
        Assert.That(CompanionBuildPlanCatalog.TryFindPlan(eCharacterClass.Cleric, "smite", out CompanionBuildPlan smite), Is.True);
        Assert.That(CompanionBuildPlanCatalog.TryFindPlan(eCharacterClass.Druid, "nature", out CompanionBuildPlan nature), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(smite.PrimaryRole, Is.EqualTo(BotPveGroupRole.Attacker));
            Assert.That(BotPartyRoles.CanFill(eCharacterClass.Cleric, BotPveGroupRole.Attacker), Is.True);
            Assert.That(nature.PrimaryRole, Is.EqualTo(BotPveGroupRole.Attacker));
            Assert.That(BotPartyRoles.CanFill(eCharacterClass.Druid, BotPveGroupRole.Attacker), Is.True);
        });
    }

    [TestCase("automatic", "battle-v1-bard", "attacker", true)]
    [TestCase("manual", "battle-v1-bard", "attacker", false)]
    [TestCase("automatic", "battle-v1-bard", "buffer", false)]
    [TestCase("automatic", "nurture-v1-bard", "attacker", false)]
    public void BardMeleeBehaviorRequiresMatchingAutomaticBuildAndRole(
        string mode, string planId, string savedRole, bool expected)
    {
        Assert.That(BotPartyRoles.HasSelectedAutomaticBuildRole(eCharacterClass.Bard,
            mode, planId, savedRole, BotPveGroupRole.Attacker), Is.EqualTo(expected));
    }

    [Test]
    public void BuildTextNamesTheRoleItSets()
    {
        CompanionBuildPlanCatalog.TryFindPlan(eCharacterClass.Healer, "trispec", out CompanionBuildPlan trispec);
        CompanionBuildPlanCatalog.TryFindPlan(eCharacterClass.Healer, "pacification", out CompanionBuildPlan pacification);
        Assert.Multiple(() =>
        {
            Assert.That(DOL.GS.Commands.CompanionManager.BuildRoleText(trispec),
                Does.EndWith("Sets role: Healer, also controls adds."));
            Assert.That(DOL.GS.Commands.CompanionManager.BuildRoleText(pacification),
                Does.EndWith("Sets role: Crowd control."));
        });
    }
}
