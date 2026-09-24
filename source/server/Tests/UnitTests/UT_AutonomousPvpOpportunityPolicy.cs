using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_AutonomousPvpOpportunityPolicy
{
    [Test]
    public void OnlyRvrObjectiveSeeksUnprovokedFights()
    {
        Assert.That(AutonomousPvpOpportunityPolicy.CanSeekOpportunity(eAutonomousObjectiveKind.RvR, 1), Is.True);
        Assert.That(AutonomousPvpOpportunityPolicy.CanSeekOpportunity(eAutonomousObjectiveKind.GroupPve, 1), Is.False);
        Assert.That(AutonomousPvpOpportunityPolicy.CanSeekOpportunity(eAutonomousObjectiveKind.GroupPve, 2), Is.False);
        Assert.That(AutonomousPvpOpportunityPolicy.CanSeekOpportunity(eAutonomousObjectiveKind.SoloPve, 1), Is.False);
        Assert.That(AutonomousPvpOpportunityPolicy.CanUseMatchmakingCamp(false, false, 100, 100), Is.True);
        Assert.That(AutonomousPvpOpportunityPolicy.CanUseMatchmakingCamp(true, false, 100, 100), Is.False);
        Assert.That(AutonomousPvpOpportunityPolicy.CanUseMatchmakingCamp(false, true, 100, 100), Is.False);
        Assert.That(AutonomousPvpOpportunityPolicy.CanUseMatchmakingCamp(false, false, 100, 1), Is.False);
    }

    [Test]
    public void RoamingMovesOnInsteadOfSelectingTheSameHuntingGroundForever()
    {
        for (int previous = 0; previous < 4; previous++)
            for (int roll = 0; roll < 16; roll++)
                Assert.That(AutonomousPvpOpportunityPolicy.ChooseRoamIndex(4, previous, roll),
                    Is.InRange(0, 3).And.Not.EqualTo(previous));
        Assert.That(AutonomousPvpOpportunityPolicy.ChooseRoamIndex(1, 0, 5), Is.Zero);
    }

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
