using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_CompanionRangedAoePolicy
{
    [Test]
    public void DefaultAndLegacyValuesResolveToThreeEnemies()
    {
        var record = new PlayerCompanionRecord();

        Assert.That(CompanionRangedAoePolicy.Choice(record), Is.EqualTo("3"));
        Assert.That(CompanionRangedAoePolicy.MinimumTargets(record), Is.EqualTo(3));

        record.RangedAoePreference = null;
        Assert.That(CompanionRangedAoePolicy.Choice(record), Is.EqualTo("3"));
        record.RangedAoePreference = "invalid";
        Assert.That(CompanionRangedAoePolicy.Choice(record), Is.EqualTo("3"));
    }

    [Test]
    public void ThresholdCyclesThroughOffAndTwoThroughEight()
    {
        Assert.That(CompanionRangedAoePolicy.NextChoice("3"), Is.EqualTo("4"));
        Assert.That(CompanionRangedAoePolicy.NextChoice("8"), Is.EqualTo("off"));
        Assert.That(CompanionRangedAoePolicy.NextChoice("off"), Is.EqualTo("2"));
        Assert.That(CompanionRangedAoePolicy.NextChoice("2"), Is.EqualTo("3"));
        Assert.That(CompanionRangedAoePolicy.MinimumTargets(Record("off")), Is.Zero);
        Assert.That(CompanionRangedAoePolicy.MinimumTargets(Record("2")), Is.EqualTo(2));
        Assert.That(CompanionRangedAoePolicy.MinimumTargets(Record("8")), Is.EqualTo(8));
    }

    [TestCase(eSpellTarget.ENEMY, 1500, 350, eSpellType.DirectDamage, 35, true)]
    [TestCase(eSpellTarget.AREA, 1500, 350, eSpellType.DirectDamage, 35, true)]
    [TestCase(eSpellTarget.CONE, 1500, 350, eSpellType.DirectDamage, 35, false)]
    [TestCase(eSpellTarget.AREA, 0, 350, eSpellType.DirectDamage, 35, false)]
    [TestCase(eSpellTarget.AREA, 1500, 350, eSpellType.Mez, 0, false)]
    [TestCase(eSpellTarget.SELF, 1500, 350, eSpellType.DirectDamage, 35, false)]
    [TestCase(eSpellTarget.ENEMY, 1500, 0, eSpellType.DirectDamage, 35, false)]
    public void OnlyRangedEnemyOrAreaDamageQualifies(eSpellTarget target, int range, int radius,
        eSpellType type, int damage, bool expected)
    {
        Spell spell = NewSpell(target, range, radius, type, damage);

        Assert.That(CompanionRangedAoePolicy.IsRangedDamageSpell(spell), Is.EqualTo(expected));
    }

    [Test]
    public void RangeZeroEnemyDamageAreaIsRecognizedOnlyForServantPayloads()
    {
        Spell payload = NewSpell(eSpellTarget.ENEMY, 0, 350, eSpellType.DirectDamage, 31);
        Spell ranged = NewSpell(eSpellTarget.ENEMY, 1500, 350, eSpellType.DirectDamage, 31);

        Assert.That(payload.IsPBAoE, Is.True);
        Assert.That(CompanionRangedAoePolicy.IsDamageAreaSpell(payload), Is.False);
        Assert.That(CompanionRangedAoePolicy.IsRangedDamageSpell(payload), Is.False);
        Assert.That(CompanionRangedAoePolicy.IsServantDamageAreaPayload(payload), Is.True);
        Assert.That(CompanionRangedAoePolicy.IsRangedDamageSpell(ranged), Is.True);
        Assert.That(CompanionRangedAoePolicy.IsServantDamageAreaPayload(ranged), Is.False);
    }

    private static PlayerCompanionRecord Record(string choice) => new() { RangedAoePreference = choice };

    private static Spell NewSpell(eSpellTarget target, int range, int radius, eSpellType type, int damage) =>
        new(new DbSpell
        {
            SpellID = 990_001,
            Name = "Ranged AoE policy test",
            Target = target.ToString(),
            Type = type.ToString(),
            Range = range,
            Radius = radius,
            Damage = damage,
        }, 50);
}
