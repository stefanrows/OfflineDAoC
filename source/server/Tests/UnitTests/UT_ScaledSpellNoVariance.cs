using DOL.Database;
using DOL.GS;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests;

// Bug 62: DirectDamageNoVariance (Scorcher DD / DD AE) was missing from the
// GetScaledSpell switch, so it stayed unscaled and logged "Unhandled spell".
[TestFixture]
public class UT_ScaledSpellNoVariance
{
    private static Spell Make(eSpellType type) => new(new DbSpell
    {
        SpellID = 61058,
        Type = type.ToString(),
        Target = "Enemy",
        Damage = 100,
    }, 1);

    [Test]
    public void DirectDamageNoVarianceScalesLikeDirectDamage()
    {
        var npc = new GameNPC { Level = 25 };
        Spell plain = npc.GetScaledSpell(Make(eSpellType.DirectDamage));
        Spell noVariance = npc.GetScaledSpell(Make(eSpellType.DirectDamageNoVariance));
        Assert.Multiple(() =>
        {
            Assert.That(plain.Damage, Is.EqualTo(50.0).Within(0.001), "Level 25 pet: factor 25/50");
            Assert.That(noVariance.Damage, Is.EqualTo(plain.Damage).Within(0.001));
        });
    }

    [Test]
    public void ScalingDoesNotModifyTheOriginalSpell()
    {
        var npc = new GameNPC { Level = 25 };
        Spell original = Make(eSpellType.DirectDamageNoVariance);
        npc.GetScaledSpell(original);
        Assert.That(original.Damage, Is.EqualTo(100.0));
    }
}
