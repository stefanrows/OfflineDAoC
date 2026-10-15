using System.Linq;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Pure rules of the companion assist train: what counts as a call and how damage is ranked.</summary>
[TestFixture]
public sealed class UT_CompanionAssistTrain
{
    private static int _nextId = 9_930_000;

    private static Spell Make(string type, string target = "Enemy", double damage = 0, double castSeconds = 0,
        int duration = 0, double value = 0, int radius = 0) =>
        new(new DbSpell
        {
            SpellID = ++_nextId,
            Type = type,
            Target = target,
            Damage = damage,
            CastTime = castSeconds,
            Duration = duration,
            Value = value,
            Radius = radius
        }, 50);

    [Test]
    public void DamageCastsCallTheTrainAndControlCastsDoNot()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CompanionAssistTrain.IsDamage(Make("DirectDamage", damage: 180, castSeconds: 3)), Is.True, "nuke");
            Assert.That(CompanionAssistTrain.IsDamage(Make("Bolt", damage: 300, castSeconds: 2)), Is.True, "bolt");
            Assert.That(CompanionAssistTrain.IsDamage(Make("DamageOverTime", damage: 20, castSeconds: 2, duration: 30)), Is.True, "dot");
            Assert.That(CompanionAssistTrain.IsDamage(Make("DamageSpeedDecrease", damage: 60, castSeconds: 3, value: 40)), Is.True, "damage snare");
            Assert.That(CompanionAssistTrain.IsDamage(Make("Mesmerize", castSeconds: 3, duration: 60)), Is.False, "mez");
            Assert.That(CompanionAssistTrain.IsDamage(Make("SpeedDecrease", castSeconds: 3, duration: 60, value: 99)), Is.False, "root");
            Assert.That(CompanionAssistTrain.IsDamage(Make("Stun", castSeconds: 3, duration: 9)), Is.False, "stun");
            Assert.That(CompanionAssistTrain.IsDamage(Make("Heal", target: "Realm", value: 300, castSeconds: 3)), Is.False, "heal");
            Assert.That(CompanionAssistTrain.IsControl(Make("Mesmerize", castSeconds: 3, duration: 60)), Is.True, "mez is control");
            Assert.That(CompanionAssistTrain.IsControl(Make("DamageSpeedDecrease", damage: 60, value: 40)), Is.False, "damage snare is not control");
        });
    }

    [Test]
    public void DamagePriorityRanksDamagePerCastTimeWithAnInstantFloor()
    {
        Spell bolt = Make("Bolt", damage: 300, castSeconds: 2);
        Spell nuke = Make("DirectDamage", damage: 200, castSeconds: 3);
        Spell instant = Make("DirectDamage", damage: 90, castSeconds: 0);
        Spell mez = Make("Mesmerize", castSeconds: 3, duration: 60);
        Spell[] ranked = new[] { instant, mez, nuke, bolt }.OrderByDescending(CompanionAssistTrain.DamagePriority).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ranked, Is.EqualTo(new[] { bolt, nuke, instant, mez }));
            Assert.That(CompanionAssistTrain.DamagePriority(instant), Is.EqualTo(60).Within(0.01), "instants count as 1.5 s");
            Assert.That(CompanionAssistTrain.DamagePriority(mez), Is.Zero);
        });
    }

    [Test]
    public void RetargetingCallsOnlyWhileTheMainAssistSwingsAtTheSelection()
    {
        GameObject a = new GameNPC();
        GameObject b = new GameNPC();
        GameObject c = new GameNPC();
        Assert.Multiple(() =>
        {
            Assert.That(CompanionAssistTrain.CallsOnRetarget(true, false, null, a, b), Is.True, "attack mode on, new enemy");
            Assert.That(CompanionAssistTrain.CallsOnRetarget(true, false, null, null, b), Is.True, "first selection while swinging");
            Assert.That(CompanionAssistTrain.CallsOnRetarget(false, false, null, a, b), Is.False, "selecting without attack mode");
            Assert.That(CompanionAssistTrain.CallsOnRetarget(true, true, null, a, b), Is.False, "a cast keeps its captured target");
            Assert.That(CompanionAssistTrain.CallsOnRetarget(true, false, null, a, null), Is.False, "clearing the target keeps the call");
            Assert.That(CompanionAssistTrain.CallsOnRetarget(true, false, null, a, a), Is.False, "same target again");
            Assert.That(CompanionAssistTrain.CallsOnRetarget(true, false, c, a, b), Is.False, "bow auto-fire still shoots its own target");
            Assert.That(CompanionAssistTrain.CallsOnRetarget(true, false, b, a, b), Is.True, "auto-fire target is the selection");
        });
    }
}
