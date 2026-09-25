using System;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
public sealed class UT_AutonomousPickupPlanning
{
    [Test]
    public void NearbyMixedPartyWinsButRealmDiversityCannotDemandALongDetour()
    {
        Assert.That(AutonomousPickupPlanning.PreferMixedParty(6, 2), Is.True);
        Assert.That(AutonomousPickupPlanning.PreferMixedParty(8, 2), Is.False);
        Assert.That(AutonomousPickupPlanning.PreferMixedParty(18, double.PositiveInfinity), Is.True);
        Assert.That(AutonomousPickupPlanning.PreferMixedParty(21, double.PositiveInfinity), Is.False);
        Assert.That(AutonomousPickupPlanning.PreferMixedParty(double.NaN, 2), Is.False);
        Assert.That(AutonomousPickupPlanning.WithinTravelBudget(-1), Is.False);
    }

    [Test]
    public void BetterUncrowdedCampCanJustifyChangingRegions()
    {
        double nearbyCrowded = AutonomousPickupPlanning.CampScore(13, 13, 4, 12, false, 1, true, 0);
        double otherRealm = AutonomousPickupPlanning.CampScore(13, 13, 12, 2, false, 6, false, 0);
        Assert.That(otherRealm, Is.LessThan(nearbyCrowded));
    }

    [Test]
    public void EquivalentCampsFavorShortTravelAndFamiliarityWhileDeathsAndDepletionDeterReturn()
    {
        double familiar = AutonomousPickupPlanning.CampScore(13, 13, 10, 0, false, 2, true, 0);
        double unfamiliar = AutonomousPickupPlanning.CampScore(13, 13, 10, 0, false, 2, false, 0);
        Assert.That(familiar, Is.LessThan(unfamiliar));
        Assert.That(AutonomousPickupPlanning.CampScore(13, 13, 10, 0, true, 2, true, 0), Is.GreaterThan(unfamiliar));
        Assert.That(AutonomousPickupPlanning.CampScore(13, 13, 10, 0, false, 2, true, 3), Is.GreaterThan(unfamiliar));
        Assert.That(AutonomousPickupPlanning.CampScore(13, 13, 10, 0, false, 8, false, 0), Is.GreaterThan(unfamiliar));
    }
}
