using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_CompanionPetPull
{
    private static Spell NewSpell(eSpellType type) =>
        new(new DbSpell { Type = type.ToString(), Target = "Realm", Duration = 1200 }, 50);

    [Test]
    public void OnlyBuffsThatWorkOnPetsGetPetPriority()
    {
        foreach (eSpellType type in new[] { eSpellType.StrengthConstitutionBuff, eSpellType.DexterityQuicknessBuff,
                     eSpellType.StrengthBuff, eSpellType.DamageAdd, eSpellType.DamageShield, eSpellType.HealOverTime })
            Assert.That(CompanionPetPull.HelpsPet(NewSpell(type)), Is.True, type.ToString());

        // No other concentration buff affects pets: armor, haste and acuity stay with the group.
        foreach (eSpellType type in new[] { eSpellType.ArmorFactorBuff, eSpellType.CombatSpeedBuff, eSpellType.AcuityBuff })
            Assert.That(CompanionPetPull.HelpsPet(NewSpell(type)), Is.False, type.ToString());
    }
}
