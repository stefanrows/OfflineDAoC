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
        Assert.That(CompanionBuildPlanCatalog.GetEnabledPlans(), Has.Count.EqualTo(33));
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
                     eCharacterClass.Animist, eCharacterClass.Wizard, eCharacterClass.Necromancer,
                     eCharacterClass.Blademaster, eCharacterClass.Hero, eCharacterClass.Warrior,
                 })
        {
            Assert.That(CompanionBuildPlanCatalog.TryGetEnabledPlan(characterClass, out _), Is.False);
            Assert.That(CompanionBuildPlanCatalog.GetBlocker(characterClass), Is.Not.Empty);
        }
    }
}
