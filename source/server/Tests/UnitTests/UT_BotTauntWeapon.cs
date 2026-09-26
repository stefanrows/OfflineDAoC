using DOL.Database;
using DOL.GS;
using DOL.GS.Styles;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_BotTauntWeapon
{
    // Armsman taunts: Distract needs a polearm, Enrage a slashing weapon.
    private static readonly Style Distract = Taunt("Distract", 1, Specs.Polearms, 12, (int)eObjectType.PolearmWeapon);
    private static readonly Style Enrage = Taunt("Enrage", 2, Specs.Slash, 8, (int)eObjectType.SlashingWeapon);

    [Test]
    public void TankUsesTheTauntOfTheWeaponInHand()
    {
        using var server = new EpicTestServerScope();
        DbInventoryItem polearm = Weapon(eObjectType.PolearmWeapon);
        DbInventoryItem sword = Weapon(eObjectType.SlashingWeapon);
        DbInventoryItem shield = Weapon(eObjectType.Shield);

        Assert.That(BotMeleeStylePolicy.SelectTaunt([Distract, Enrage], 30, polearm, null, eActiveWeaponSlot.TwoHanded),
            Is.SameAs(Distract));
        Assert.That(BotMeleeStylePolicy.SelectTaunt([Distract, Enrage], 30, sword, shield, eActiveWeaponSlot.Standard),
            Is.SameAs(Enrage), "Sword and shield must not queue the polearm taunt, which would be dropped at the swing.");
        Assert.That(BotMeleeStylePolicy.SelectTaunt([Distract], 30, sword, shield, eActiveWeaponSlot.Standard), Is.Null);
        Assert.That(BotMeleeStylePolicy.SelectTaunt([Distract, Enrage], 10, polearm, null, eActiveWeaponSlot.TwoHanded),
            Is.Null, "A taunt above the character level is not available yet.");
    }

    private static Style Taunt(string name, int id, string spec, int level, int weaponType) =>
        new(new DbStyle { Name = name, ID = id, SpecKeyName = spec, SpecLevelRequirement = level,
            WeaponTypeRequirement = weaponType }, null);

    private static DbInventoryItem Weapon(eObjectType type) =>
        GameInventoryItem.Create(new DbItemTemplate { Id_nb = "test_" + type, Name = type.ToString(), Object_Type = (int)type,
            Item_Type = type == eObjectType.Shield ? Slot.LEFTHAND : Slot.RIGHTHAND, Level = 1 });
}
