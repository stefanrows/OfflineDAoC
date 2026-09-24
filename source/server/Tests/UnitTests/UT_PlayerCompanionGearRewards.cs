using DOL.GS;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace DOL.UnitTests;

[TestFixture]
public class UT_PlayerCompanionGearRewards
{
    [TestCase(1.0, 0.25)]
    [TestCase(2.0, 0.5)]
    [TestCase(4.0, 1.0)]
    [TestCase(10.0, 1.0)]
    [TestCase(0.0, 0.0)]
    [TestCase(-1.0, 0.0)]
    public void PveDropChanceUsesPlayerRateAndCapsAtCertainty(double rate, double expected)
    {
        Assert.That(PlayerCompanionGearRewards.PveDropChance(rate), Is.EqualTo(expected));
    }

    [TestCase(1.0, 0.2499, true)]
    [TestCase(1.0, 0.25, false)]
    [TestCase(4.0, 0.9999, true)]
    [TestCase(10.0, 0.9999, true)]
    [TestCase(10.0, 1.0, false)]
    public void PveRollUsesIndependentPerCompanionThreshold(double rate, double roll, bool expected)
    {
        Assert.That(PlayerCompanionGearRewards.PassesPveRoll(rate, roll), Is.EqualTo(expected));
    }

    [Test]
    public void ItemProvenanceAndSlotLocksRoundTripPerCompanion()
    {
        var first = new PlayerCompanionRecord { CompanionId = "first" };
        var second = new PlayerCompanionRecord { CompanionId = "second" };

        Assert.That(PlayerCompanionRoster.GetEquipmentItemFlags(first, "item-1"), Is.Empty,
            "Items without metadata stay in the protected, unknown-ownership category.");
        PlayerCompanionRoster.SetEquipmentItemFlags(first, "item-1", "EK");
        PlayerCompanionRoster.SetEquipmentSlotLocked(first, eInventorySlot.HeadArmor, true);

        Assert.That(PlayerCompanionRoster.GetEquipmentItemFlags(first, "item-1"), Is.EqualTo("EK"));
        Assert.That(PlayerCompanionRoster.IsEquipmentSlotLocked(first, eInventorySlot.HeadArmor), Is.True);
        Assert.That(PlayerCompanionRoster.GetEquipmentItemFlags(second, "item-1"), Is.Empty);
        Assert.That(PlayerCompanionRoster.IsEquipmentSlotLocked(second, eInventorySlot.HeadArmor), Is.False);

        PlayerCompanionRoster.SetEquipmentSlotLocked(first, eInventorySlot.HeadArmor, false);
        Assert.That(PlayerCompanionRoster.IsEquipmentSlotLocked(first, eInventorySlot.HeadArmor), Is.False);
        Assert.That(PlayerCompanionRoster.GetEquipmentItemFlags(first, "item-1"), Is.EqualTo("EK"));
    }

    [Test]
    public void CompanionInventoryCanRestoreOwnershipWithoutPersistingDuringRollback()
    {
        var inventory = new BotInventory("playercompanion:fixture");
        var item = new DOL.Database.DbInventoryItem();

        Assert.That(inventory.AddItemWithoutDbAddition(eInventorySlot.FirstBackpack, item), Is.True);
        Assert.That(inventory.GetItem(eInventorySlot.FirstBackpack), Is.SameAs(item));
        Assert.That(item.OwnerID, Is.EqualTo("playercompanion:fixture"));
        Assert.That(inventory.RemoveItemWithoutDbDeletion(item), Is.True);
        Assert.That(inventory.AllItems, Is.Empty);
        Assert.That(inventory.AddItemWithoutDbAddition(eInventorySlot.FirstBackpack, item), Is.True);
        Assert.That(inventory.GetItem(eInventorySlot.FirstBackpack), Is.SameAs(item));
    }

    [Test]
    public void PairedEquipmentSlotsPreferMissingThenGreatestLevelDeficitAndHonorLocks()
    {
        Assert.That(AutonomousBotEconomy.ChooseCompanionPairSlot(
            eInventorySlot.LeftRing, 35, false, eInventorySlot.RightRing, null, false,
            30, false, 0), Is.EqualTo(eInventorySlot.RightRing),
            "A missing usable slot wins even if the occupied slot has a higher deficit comparison.");

        Assert.That(AutonomousBotEconomy.ChooseCompanionPairSlot(
            eInventorySlot.LeftBracer, 27, false, eInventorySlot.RightBracer, 15, false,
            30, false, 0), Is.EqualTo(eInventorySlot.RightBracer),
            "The greatest item-level deficit is selected when both slots are occupied.");

        Assert.That(AutonomousBotEconomy.ChooseCompanionPairSlot(
            eInventorySlot.LeftRing, 1, false, eInventorySlot.RightRing, 20, true,
            30, false, 0), Is.EqualTo(eInventorySlot.LeftRing),
            "An unlocked usable slot is selected while the alternative is locked.");
        Assert.That(AutonomousBotEconomy.ChooseCompanionPairSlot(
            eInventorySlot.LeftRing, 1, true, eInventorySlot.RightRing, 20, true,
            30, false, 0), Is.EqualTo(eInventorySlot.Invalid));
    }

    [Test]
    public void TiedPairedEquipmentSlotsUseRandomizedTieBreak()
    {
        eInventorySlot first = AutonomousBotEconomy.ChooseCompanionPairSlot(
            eInventorySlot.LeftRing, 20, false, eInventorySlot.RightRing, 20, false,
            30, false, 0);
        eInventorySlot second = AutonomousBotEconomy.ChooseCompanionPairSlot(
            eInventorySlot.LeftRing, 20, false, eInventorySlot.RightRing, 20, false,
            30, false, 1);

        Assert.That(first, Is.EqualTo(eInventorySlot.LeftRing));
        Assert.That(second, Is.EqualTo(eInventorySlot.RightRing));
    }

    [Test]
    public void FocusStaffExceptionStillHonorsItemRealmAndClassRestrictions()
    {
        int eldritch = (int)eCharacterClass.Eldritch;
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotEconomy.FocusStaffPassesRealmAndClassRestrictions(
                eRealm.Hibernia, eldritch, (int)eRealm.Hibernia, eldritch.ToString(), false), Is.True);
            Assert.That(AutonomousBotEconomy.FocusStaffPassesRealmAndClassRestrictions(
                eRealm.Hibernia, eldritch, (int)eRealm.Albion, eldritch.ToString(), false), Is.False);
            Assert.That(AutonomousBotEconomy.FocusStaffPassesRealmAndClassRestrictions(
                eRealm.Hibernia, eldritch, (int)eRealm.Albion, eldritch.ToString(), true), Is.True);
            Assert.That(AutonomousBotEconomy.FocusStaffPassesRealmAndClassRestrictions(
                eRealm.Hibernia, eldritch, (int)eRealm.Hibernia,
                ((int)eCharacterClass.Bard).ToString(), false), Is.False);
            Assert.That(AutonomousBotEconomy.FocusStaffPassesRealmAndClassRestrictions(
                eRealm.Hibernia, eldritch, 0, string.Empty, false), Is.True);
        });
    }

    [Test]
    public void AutomaticPlansHaveBudgetSafeTargetsAtEveryLevel()
    {
        Assert.That(CompanionBuildPlanCatalog.GetEnabledPlans(), Has.Count.EqualTo(57));
        foreach (CompanionBuildPlan plan in CompanionBuildPlanCatalog.GetEnabledPlans())
        {
            Dictionary<string, int> previous = null;
            for (int level = 1; level <= 50; level++)
            {
                IReadOnlyDictionary<string, int> targets = plan.GetTargetsAtLevel(level,
                    plan.ExpectedSpecPointsMultiplier);
                int budget = -1;
                for (int earnedLevel = 1; earnedLevel <= level; earnedLevel++)
                    budget += CompanionBuildPlan.AwardPointsAtLevel(earnedLevel, plan.ExpectedSpecPointsMultiplier);

                Assert.That(plan.GetRequiredPointsAtLevel(level, plan.ExpectedSpecPointsMultiplier),
                    Is.LessThanOrEqualTo(budget), $"{plan.Id} exceeds its point budget at level {level}.");
                Assert.That(targets.Values, Has.All.LessThanOrEqualTo(level),
                    $"{plan.Id} trains above character level {level}.");
                if (previous != null)
                    foreach (KeyValuePair<string, int> rank in targets)
                        Assert.That(rank.Value, Is.GreaterThanOrEqualTo(previous[rank.Key]),
                            $"{plan.Id} reduces {rank.Key} at level {level}.");
                previous = targets.ToDictionary(pair => pair.Key, pair => pair.Value);
            }

            foreach (CompanionBuildRank target in plan.TargetAllocations)
                Assert.That(previous[target.Specialization], Is.EqualTo(target.Level),
                    $"{plan.Id} does not reach its level-50 target for {target.Specialization}.");
        }
    }

    [Test]
    public void UnsupportedClassesHavePreciseManualOnlyBlockers()
    {
        foreach (eCharacterClass characterClass in new[]
                 {
                     eCharacterClass.Necromancer, eCharacterClass.Blademaster, eCharacterClass.Hero,
                     eCharacterClass.Warrior,
                 })
        {
            Assert.That(CompanionBuildPlanCatalog.TryGetEnabledPlan(characterClass, out _), Is.False);
            Assert.That(CompanionBuildPlanCatalog.GetBlocker(characterClass), Is.Not.Empty);
        }
    }

    [Test]
    public void OriginalPlanIdsStayTheDefaultBuildOfTheirClass()
    {
        eCharacterClass[] originalClasses =
        {
            eCharacterClass.Armsman, eCharacterClass.Cabalist, eCharacterClass.Cleric, eCharacterClass.Friar,
            eCharacterClass.Infiltrator, eCharacterClass.Mercenary, eCharacterClass.Minstrel, eCharacterClass.Paladin,
            eCharacterClass.Reaver, eCharacterClass.Scout, eCharacterClass.Sorcerer, eCharacterClass.Theurgist,
            eCharacterClass.Berserker, eCharacterClass.Bonedancer, eCharacterClass.Healer, eCharacterClass.Hunter,
            eCharacterClass.Runemaster, eCharacterClass.Savage, eCharacterClass.Shadowblade, eCharacterClass.Shaman,
            eCharacterClass.Skald, eCharacterClass.Spiritmaster, eCharacterClass.Thane, eCharacterClass.Bard,
            eCharacterClass.Champion, eCharacterClass.Druid, eCharacterClass.Eldritch, eCharacterClass.Enchanter,
            eCharacterClass.Mentalist, eCharacterClass.Nightshade, eCharacterClass.Ranger, eCharacterClass.Valewalker,
            eCharacterClass.Warden,
        };
        foreach (eCharacterClass characterClass in originalClasses)
        {
            string savedId = $"general-pve-v1-{characterClass.ToString().ToLowerInvariant()}";
            Assert.That(CompanionBuildPlanCatalog.TryGetEnabledPlan(characterClass, out string defaultId), Is.True);
            Assert.That(defaultId, Is.EqualTo(savedId), $"{characterClass} changed its default build.");
            Assert.That(CompanionBuildPlanCatalog.TryGetPlanById(characterClass, savedId, out _), Is.True);
        }
    }

    [Test]
    public void BuildIdsAndKeysAreUniqueAndSelectable()
    {
        IReadOnlyCollection<CompanionBuildPlan> plans = CompanionBuildPlanCatalog.GetEnabledPlans();
        Assert.That(plans.Select(plan => plan.Id).Distinct().Count(), Is.EqualTo(plans.Count));
        Assert.That(plans.Select(plan => plan.CharacterClass).Distinct().Count(), Is.EqualTo(35));
        foreach (IGrouping<eCharacterClass, CompanionBuildPlan> group in plans.GroupBy(plan => plan.CharacterClass))
        {
            Assert.That(group.Select(plan => plan.Key).Distinct().Count(), Is.EqualTo(group.Count()),
                $"{group.Key} has duplicate build keys.");
            foreach (CompanionBuildPlan plan in group)
            {
                Assert.That(plan.Key, Does.Match("^[a-z]+$"), $"{plan.Id} needs a one-word command key.");
                Assert.That(plan.Name, Is.Not.Empty);
                foreach (string query in new[] { plan.Key, plan.Key.ToUpperInvariant(), plan.Id, plan.Name })
                {
                    Assert.That(CompanionBuildPlanCatalog.TryFindPlan(group.Key, query, out CompanionBuildPlan found), Is.True);
                    Assert.That(found.Id, Is.EqualTo(plan.Id));
                }
            }
        }

        Assert.That(CompanionBuildPlanCatalog.TryFindPlan(eCharacterClass.Healer, "summoning", out _), Is.False);
        Assert.That(CompanionBuildPlanCatalog.TryFindPlan(eCharacterClass.Healer, " ", out _), Is.False);
        Assert.That(CompanionBuildPlanCatalog.TryGetPlanById(eCharacterClass.Healer, "general-pve-v1-shaman", out _), Is.False);
        Assert.That(CompanionBuildPlanCatalog.GetPlans(eCharacterClass.Necromancer), Is.Empty);
    }

    [Test]
    public void OwnerExampleBuildsAreOffered()
    {
        Assert.That(CompanionBuildPlanCatalog.GetPlans(eCharacterClass.Healer).Select(plan => plan.Name),
            Is.EquivalentTo(new[] { "Tri-spec", "Mending (healer)", "Augmentation (buffer)", "Pacification (crowd control)" }));
        Assert.That(CompanionBuildPlanCatalog.GetPlans(eCharacterClass.Spiritmaster).Select(plan => plan.Name),
            Is.EquivalentTo(new[] { "Darkness (bomb)", "Suppression", "Summoning (pet)" }));
    }

    [Test]
    public void BuildSwitchResetsEveryLineAndRetrainsWithinBudgetAtEveryLevel()
    {
        foreach (CompanionBuildPlan plan in CompanionBuildPlanCatalog.GetEnabledPlans())
        {
            string[] career = CompanionBuildPlanCatalog.GetPlans(plan.CharacterClass)
                .SelectMany(entry => entry.TargetAllocations).Select(rank => rank.Specialization)
                .Append("Unrelated Line").Distinct().ToArray();
            for (int level = 1; level <= 50; level++)
            {
                int multiplier = plan.ExpectedSpecPointsMultiplier;
                Assert.That(plan.TryGetSwitchedAllocation(career, level, multiplier,
                    out Dictionary<string, int> allocation, out int unspent), Is.True, $"{plan.Id} at level {level}");
                IReadOnlyDictionary<string, int> targets = plan.GetTargetsAtLevel(level, multiplier);
                foreach (string line in career)
                {
                    int expected = targets.TryGetValue(line, out int target) ? target : 1;
                    Assert.That(allocation[line], Is.EqualTo(expected), $"{plan.Id} {line} at level {level}");
                }
                int spent = allocation.Values.Sum(rank => CompanionBuildPlan.CostToReach(1, rank));
                Assert.That(spent + unspent, Is.EqualTo(CompanionBuildPlan.GetPointBudgetAtLevel(level, multiplier)),
                    $"{plan.Id} loses or creates points at level {level}");
            }
        }
    }

    [Test]
    public void SwitchingBuildsAndBackRestoresTheSameAllocation()
    {
        IReadOnlyList<CompanionBuildPlan> healer = CompanionBuildPlanCatalog.GetPlans(eCharacterClass.Healer);
        string[] career = { "Mending", "Augmentation", "Pacification" };
        Assert.That(healer[0].TryGetSwitchedAllocation(career, 37, 10, out Dictionary<string, int> original, out int originalPoints), Is.True);
        Assert.That(healer[3].TryGetSwitchedAllocation(original.Keys, 37, 10, out Dictionary<string, int> pacification, out _), Is.True);
        Assert.That(healer[0].TryGetSwitchedAllocation(pacification.Keys, 37, 10, out Dictionary<string, int> restored, out int restoredPoints), Is.True);
        Assert.That(pacification, Is.Not.EqualTo(original));
        Assert.That(restored, Is.EqualTo(original));
        Assert.That(restoredPoints, Is.EqualTo(originalPoints));
    }
}
