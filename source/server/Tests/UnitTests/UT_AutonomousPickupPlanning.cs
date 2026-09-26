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
    public void GroupCampTravelMustFitWithinTheDeadlineAndEveryMemberNeedsAConnectedEstimate()
    {
        var eligible = GroupCamp("twenty-minute", 20);
        var tooFar = GroupCamp("over-budget", 20.01);
        var unknown = GroupCamp("unknown-route", double.PositiveInfinity);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousPickupPlanning.WithinGroupCampTravelBudget(20), Is.True);
            Assert.That(AutonomousPickupPlanning.WithinGroupCampTravelBudget(20.01), Is.False);
            Assert.That(AutonomousPickupPlanning.WithinGroupCampTravelBudget(9999), Is.False);
            Assert.That(AutonomousPickupPlanning.WithinGroupCampTravelBudget(double.NaN), Is.False);
            Assert.That(AutonomousPickupPlanning.SlowestMemberTravelMinutes([4, 11, 7]), Is.EqualTo(11));
            Assert.That(AutonomousPickupPlanning.SlowestMemberTravelMinutes([4, double.PositiveInfinity]),
                Is.EqualTo(double.PositiveInfinity));
            Assert.That(AutonomousPickupPlanning.SlowestMemberTravelMinutes(Array.Empty<double>()),
                Is.EqualTo(double.PositiveInfinity));
            Assert.That(AutonomousPickupPlanning.GroupCampsWithinTravelBudget([eligible, tooFar, unknown]),
                Is.EqualTo(new[] { eligible }));
            Assert.That(AutonomousPickupPlanning.GroupCampsWithinTravelBudget([tooFar, unknown]), Is.Empty);
        });
    }

    [Test]
    public void AReachableCampAfterFourBlockedCorridorsRemainsEligible()
    {
        var camps = new[]
        {
            GroupCamp("blocked-1", 1), GroupCamp("blocked-2", 2),
            GroupCamp("blocked-3", 3), GroupCamp("blocked-4", 4),
            GroupCamp("reachable", 5)
        };
        int checkedRoutes = 0;

        var verified = AutonomousPickupPlanning.GroupCampsWithVerifiedRoutes(camps, camp =>
        {
            checkedRoutes++;
            return camp.Id == "reachable";
        }, 4, 12);

        Assert.That(checkedRoutes, Is.EqualTo(5));
        Assert.That(verified, Is.EqualTo(new[] { camps[4] }));
    }

    [Test]
    public void UnreachableCampsDoNotTriggerUnboundedRouteChecks()
    {
        var camps = new[]
        {
            GroupCamp("blocked-1", 1), GroupCamp("blocked-2", 2),
            GroupCamp("blocked-3", 3), GroupCamp("blocked-4", 4),
            GroupCamp("blocked-5", 5)
        };
        int checkedRoutes = 0;

        var verified = AutonomousPickupPlanning.GroupCampsWithVerifiedRoutes(camps, _ =>
        {
            checkedRoutes++;
            return false;
        }, 4, 3);

        Assert.That(verified, Is.Empty);
        Assert.That(checkedRoutes, Is.EqualTo(3));
    }

    private static AutonomousBotDecisionEngine.Camp GroupCamp(string id, double travelMinutes) =>
        new(id, "zone", "mob", eRealm.Albion, 1, ConColor.BLUE, ConColor.BLUE,
            true, false, false, 5, 0, travelMinutes, 10);

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
