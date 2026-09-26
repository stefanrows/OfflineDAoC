using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_CompanionInventoryAccess
{
    private static readonly PlayerCompanionRoster.InventoryAccess Ready = new(
        "Astrid", Linked: true, SameRegion: true, Distance: 150,
        OwnerCombatMs: 0, CompanionCombatMs: 0, Casting: false, Aggro: false, PetInCombat: false, Travelling: false);

    [Test]
    public void CompanionAtFollowDistanceCanTradeGear()
    {
        Assert.That(PlayerCompanionRoster.InventoryBlocker(Ready), Is.Empty);
        Assert.That(PlayerCompanionRoster.InventoryBlocker(Ready with { Distance = 390 }), Is.Empty,
            "Companions follow up to 400 units away, so that must count as nearby.");
    }

    [Test]
    public void EachBlockerNamesItsOwnReason()
    {
        Assert.That(PlayerCompanionRoster.InventoryBlocker(Ready with { Distance = 520 }),
            Does.Contain("520").And.Contain("400"));
        Assert.That(PlayerCompanionRoster.InventoryBlocker(Ready with { OwnerCombatMs = 5_200 }),
            Does.Contain("You").And.Contain("6 s"));
        Assert.That(PlayerCompanionRoster.InventoryBlocker(Ready with { CompanionCombatMs = 800 }),
            Does.Contain("Astrid").And.Contain("1 s"));
        Assert.That(PlayerCompanionRoster.InventoryBlocker(Ready with { Casting = true }), Does.Contain("casting"));
        Assert.That(PlayerCompanionRoster.InventoryBlocker(Ready with { PetInCombat = true }), Does.Contain("pet"));
        Assert.That(PlayerCompanionRoster.InventoryBlocker(Ready with { Linked = false }), Does.Contain("group"));
    }
}
