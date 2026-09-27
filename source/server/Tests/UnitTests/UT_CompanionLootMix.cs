using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public class UT_CompanionLootMix
{
    [TestCase(0.00, CompanionLootMix.Kind.Armor)]
    [TestCase(0.44, CompanionLootMix.Kind.Armor)]
    [TestCase(0.45, CompanionLootMix.Kind.Jewelry)]
    [TestCase(0.79, CompanionLootMix.Kind.Jewelry)]
    [TestCase(0.80, CompanionLootMix.Kind.Weapon)]
    [TestCase(0.99, CompanionLootMix.Kind.Weapon)]
    public void Roll_IsMostlyArmorAndJewelry(double roll, CompanionLootMix.Kind expected) =>
        Assert.That(CompanionLootMix.Roll(roll), Is.EqualTo(expected));

    [TestCase(eObjectType.Magical, CompanionLootMix.Kind.Jewelry)]
    [TestCase(eObjectType.Chain, CompanionLootMix.Kind.Armor)]
    [TestCase(eObjectType.Cloth, CompanionLootMix.Kind.Armor)]
    [TestCase(eObjectType.Staff, CompanionLootMix.Kind.Weapon)]
    [TestCase(eObjectType.Shield, CompanionLootMix.Kind.Weapon)]
    public void KindOf_SortsObjectTypes(eObjectType type, CompanionLootMix.Kind expected) =>
        Assert.That(CompanionLootMix.KindOf(type), Is.EqualTo(expected));

    [Test]
    public void KeepValue_DoesNotRankWeaponsAboveJewelryByDps()
    {
        DbInventoryItem weapon = new() { Level = 50, Quality = 90, DPS_AF = 165, Bonus1 = 5 };
        DbInventoryItem ring = new() { Level = 50, Quality = 90, DPS_AF = 0, Bonus1 = 10, Bonus2 = 8 };

        Assert.That(CompanionLootMix.KeepValue(ring), Is.GreaterThan(CompanionLootMix.KeepValue(weapon)));
    }
}
