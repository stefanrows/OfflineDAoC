using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_AutonomousPvpOpportunityPolicy
{
    [TestCase(1, 6, true)]
    [TestCase(10, 15, true)]
    [TestCase(10, 16, false)]
    public void PreferredTargetsStayWithinFiveLevels(int actor, int target, bool expected) =>
        Assert.That(AutonomousPvpOpportunityPolicy.LevelsPreferred(actor, target), Is.EqualTo(expected));

    [Test]
    public void StrongerPartiesAreAvoidedUnlessRetaliating()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousPvpOpportunityPolicy.ShouldInitiate(2, 10, 3, 8, false), Is.False);
            Assert.That(AutonomousPvpOpportunityPolicy.ShouldInitiate(2, 10, 2, 16, false), Is.False);
            Assert.That(AutonomousPvpOpportunityPolicy.ShouldInitiate(2, 10, 2, 15, false), Is.True);
            Assert.That(AutonomousPvpOpportunityPolicy.ShouldInitiate(2, 10, 8, 50, true), Is.True);
        });
    }

    [Test]
    public void LowLevelHuntsRequireReachableNonSafeOrdinaryLevelingAreas()
    {
        Assert.That(AutonomousPvpOpportunityPolicy.IsLocalHuntArea(9, new[] { 4, 12 }, false, false, false, true), Is.True);
        Assert.That(AutonomousPvpOpportunityPolicy.IsLocalHuntArea(20, new[] { 20 }, false, false, false, true), Is.False);
        Assert.That(AutonomousPvpOpportunityPolicy.IsLocalHuntArea(9, new[] { 12 }, false, false, true, true), Is.False);
        Assert.That(AutonomousPvpOpportunityPolicy.IsLocalHuntArea(9, new[] { 12 }, false, true, false, true), Is.False);
        Assert.That(AutonomousPvpOpportunityPolicy.IsLocalHuntArea(9, new[] { 30 }, false, false, false, true), Is.False);
        Assert.That(AutonomousPvpOpportunityPolicy.IsLocalHuntArea(9, new[] { 12 }, false, false, false, false), Is.False);
    }
}
