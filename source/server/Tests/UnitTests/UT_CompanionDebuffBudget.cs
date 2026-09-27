using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_CompanionDebuffBudget
{
    private static Spell NewSpell(string type, int radius = 0, double damage = 0, int duration = 60) =>
        new(new DbSpell { Type = type, Target = "Enemy", Radius = radius, Damage = damage, Duration = duration }, 50);

    [Test]
    public void SingleTargetDebuffOncePerFightAreaDebuffTwice()
    {
        var budget = new CompanionDebuffBudget();
        Spell strength = NewSpell(nameof(eSpellType.StrengthDebuff));
        Spell areaStrCon = NewSpell(nameof(eSpellType.StrengthConstitutionDebuff), radius: 400);

        budget.Record(strength, 1_000);
        Assert.That(budget.Allows(strength), Is.False, "The next mob of the same fight gets no fresh debuff.");

        budget.Record(areaStrCon, 2_000);
        Assert.That(budget.Allows(areaStrCon), Is.True);
        budget.Record(areaStrCon, 3_000);
        Assert.That(budget.Allows(areaStrCon), Is.False);
    }

    [Test]
    public void DamageSnaresAndDotsAreNotRationed()
    {
        var budget = new CompanionDebuffBudget();
        Spell ravenBolt = NewSpell(nameof(eSpellType.DirectDamageWithDebuff), damage: 175);
        Spell nuke = NewSpell(nameof(eSpellType.DirectDamage), damage: 179, duration: 0);
        Spell snare = NewSpell(nameof(eSpellType.SpeedDecrease));

        foreach (Spell spell in new[] { ravenBolt, nuke, snare })
        {
            budget.Record(spell, 1_000);
            budget.Record(spell, 2_000);
            Assert.That(budget.Allows(spell), Is.True, spell.SpellType.ToString());
        }
    }

    [Test]
    public void BudgetRenewsOnlyAfterTheFightEnds()
    {
        var budget = new CompanionDebuffBudget();
        Spell strength = NewSpell(nameof(eSpellType.StrengthDebuff));
        budget.Record(strength, 10_000);

        budget.EndFightIfIdle(inCombat: true, 60_000);
        Assert.That(budget.Allows(strength), Is.False, "A long chain pull stays one fight.");

        budget.EndFightIfIdle(inCombat: false, 12_000);
        Assert.That(budget.Allows(strength), Is.False, "A just-cast debuff has not yet put the bot in combat.");

        budget.EndFightIfIdle(inCombat: false, 10_000 + CompanionDebuffBudget.FightGapMilliseconds);
        Assert.That(budget.Allows(strength), Is.True, "The next pull is a new fight.");
    }
}
