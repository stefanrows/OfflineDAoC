using DOL.AI.Brain;
using DOL.GS.GameEvents;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// Bug 146: a level-66 "Renegade Fensalir Jarl" (stock relic-temple guard at
/// Mjollner Faste) chased Midgard bots 40,000 units to the Svasud Faste bind
/// stone. Camlann disables the stock relic guards, keep guards leash back to
/// their post, and roaming bots no longer pick relic temples.
/// </summary>
[TestFixture]
public sealed class UT_RelicGuardLeash
{
    [TestCase(false, true)]
    [TestCase(true, false)]
    public void StockRelicGuardsRunOnlyOutsideCamlann(bool camlann, bool expected)
    {
        Assert.That(RelicGuardManager.ShouldRun(camlann), Is.EqualTo(expected));
    }

    [TestCase(0, false)]
    [TestCase(1800, false)]
    [TestCase(3000, false)]
    [TestCase(3001, true)]
    // The bind stone kill site: 764915,668377 vs the spawn 771263,628729.
    [TestCase(40153, true)]
    public void KeepGuardLeashesBeyondItsRange(double distanceFromSpawn, bool expected)
    {
        Assert.That(KeepGuardBrain.ShouldLeash(distanceFromSpawn, KeepGuardBrain.LEASH_RANGE), Is.EqualTo(expected));
    }

    [Test]
    public void LeashIsLongerThanEveryKeepGuardAggroRange()
    {
        // GuardTemplateMgr.SetGuardAggression: fighters 1800, casters 1850, archers 2100.
        Assert.That(KeepGuardBrain.LEASH_RANGE, Is.GreaterThan(2100));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void NoLeashWithoutAThreshold(int threshold)
    {
        Assert.That(KeepGuardBrain.ShouldLeash(40000, threshold), Is.False);
    }

    [Test]
    public void KeepGuardBrainUsesTheLeashedAggroState()
    {
        var brain = new KeepGuardBrain();
        Assert.That(brain.FSM.GetState(eFSMStateType.AGGRO), Is.InstanceOf<GuardState_AGGRO>());
        Assert.That(brain.FSM.GetState(eFSMStateType.RETURN_TO_SPAWN), Is.InstanceOf<GuardState_RETURN_TO_SPAWN>());
    }

    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(true, false, false)]
    [TestCase(true, true, false)]
    public void RoamingSkipsPortalKeepsAndRelicTemples(bool portalKeep, bool relicKeep, bool expected)
    {
        Assert.That(AutonomousRvrKeepPolicy.IsRoamDestination(portalKeep, relicKeep), Is.EqualTo(expected));
    }
}
