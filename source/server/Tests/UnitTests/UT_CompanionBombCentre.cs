using System;
using System.Numerics;
using System.Reflection;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// PBAoE damage falls off linearly from the caster, so a bomber must stand in
/// the middle of the pile rather than at the edge of its radius.
/// </summary>
[TestFixture]
public sealed class UT_CompanionBombCentre
{
    private static readonly Type ApproachType = typeof(CompanionBombingPolicy).Assembly.GetType(
        "DOL.GS.CompanionBombCentreApproach", true)!;

    [Test]
    public void CentroidIsTheMiddleOfThePile()
    {
        Vector3 centre = CompanionBombingPolicy.Centroid([new(0, 0, 0), new(200, 0, 0), new(100, 300, 30)]);
        Assert.That(centre, Is.EqualTo(new Vector3(100, 100, 10)));
        Assert.That(CompanionBombingPolicy.Centroid([]), Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void ToleranceHasAFloorWithoutASpell()
    {
        Assert.That(CompanionBombingPolicy.CentreTolerance(null), Is.EqualTo(30));
    }

    [Test]
    public void BomberArrivesInTheCentreOrGivesUpAfterAShortRun()
    {
        object approach = Activator.CreateInstance(ApproachType, nonPublic: true)!;
        MethodInfo arrived = ApproachType.GetMethod("Arrived")!;
        bool Arrived(object target, float distance, long now) => (bool)arrived.Invoke(approach, [target, distance, 50, now])!;
        object pile = new(), nextPile = new();

        Assert.That(Arrived(pile, 150, 1_000), Is.False, "Standing at half the radius is not the centre.");
        Assert.That(Arrived(pile, 40, 1_800), Is.True);
        Assert.That(Arrived(pile, 90, 2_000), Is.True, "A pile drifting a little does not start a new run.");
        Assert.That(Arrived(nextPile, 200, 5_000), Is.False);
        Assert.That(Arrived(nextPile, 200, 8_000), Is.True, "A blocked run bombs from where it stands.");
    }
}
