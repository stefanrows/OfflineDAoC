using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
public sealed class UT_AutonomousRouteRecoveryPolicy
{
    [Test]
    public void SideStepAloneDoesNotResetTheRecoveryBudget()
    {
        Assert.That(AutonomousRouteRecoveryPolicy.HasMeaningfulForwardProgress(5_000, 4_900), Is.False);
        Assert.That(AutonomousRouteRecoveryPolicy.HasMeaningfulForwardProgress(5_000, 4_760), Is.True);
    }

    [Test]
    public void ExactlyThreeLocalRecoveriesAreAllowedBeforeGoalSwap()
    {
        Assert.That(AutonomousRouteRecoveryPolicy.ShouldAbandon(1), Is.False);
        Assert.That(AutonomousRouteRecoveryPolicy.ShouldAbandon(3), Is.False);
        Assert.That(AutonomousRouteRecoveryPolicy.ShouldAbandon(4), Is.True);
    }

    [Test]
    public void ContinuousMovementKeepsOwnershipOfTheExistingOrder()
    {
        Assert.That(AutonomousRouteRecoveryPolicy.ShouldRetainMovementOrder(true, 50_000, 0), Is.True);
        Assert.That(AutonomousRouteRecoveryPolicy.ShouldRetainMovementOrder(false, 1_000, 1_500), Is.True);
        Assert.That(AutonomousRouteRecoveryPolicy.ShouldRetainMovementOrder(false, 1_500, 1_500), Is.False);
        Assert.That(AutonomousRouteRecoveryPolicy.ShouldRecoverActiveRouteStall(1_000, 6_000, 5_000), Is.True,
            "a collision-stopped route must enter bounded recovery even when IsMoving is false");
        Assert.That(AutonomousRouteRecoveryPolicy.ShouldRecoverActiveRouteStall(1_000, 5_999, 5_000), Is.False);
    }

    [Test]
    public void ImmediateRouteFailureGetsAQuietCooldownBeforeAnotherGoal()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRouteRecoveryPolicy.ReplanDelayMilliseconds(false, 17),
                Is.EqualTo(AutonomousRouteRecoveryPolicy.ImmediateRouteFailureCooldownMilliseconds + 17));
            Assert.That(AutonomousRouteRecoveryPolicy.ReplanDelayMilliseconds(true, 17), Is.EqualTo(2_517));
        });
    }

    [Test]
    public void SafeRelocationRequiresRepeatedTerminalFailuresInTheSamePocket()
    {
        var origin = new System.Numerics.Vector3(1000, 1000, 100);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRouteRecoveryPolicy.IsSameRepeatedFailurePocket(
                1, 1, origin, new(1200, 1100, 900), 30_000), Is.True,
                "Z differences do not hide an isolated XY navigation component");
            Assert.That(AutonomousRouteRecoveryPolicy.IsSameRepeatedFailurePocket(
                1, 1, origin, new(1500, 1000, 100), 30_000), Is.False);
            Assert.That(AutonomousRouteRecoveryPolicy.IsSameRepeatedFailurePocket(
                1, 200, origin, origin, 30_000), Is.False);
            Assert.That(AutonomousRouteRecoveryPolicy.IsSameRepeatedFailurePocket(
                1, 1, origin, origin, AutonomousRouteRecoveryPolicy.RepeatedFailureWindowMilliseconds + 1), Is.False);
            Assert.That(AutonomousRouteRecoveryPolicy.FailuresBeforeSafeRelocation, Is.EqualTo(3));
        });
    }

    [Test]
    public void PocketEscapeMustLeaveTheActorsOwnFloor()
    {
        var floor = new System.Numerics.Vector3(311960, 470002, 5203);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRouteRecoveryPolicy.IsRealPocketEscape(floor, floor), Is.False,
                "bug 132: the Connacht road escape resolved to the bot's own floor and looped every replan");
            Assert.That(AutonomousRouteRecoveryPolicy.IsRealPocketEscape(floor,
                floor + new System.Numerics.Vector3(0, -1, 0)), Is.False,
                "bug 132: the Muire pocket snapped its escape one unit from the actor");
            Assert.That(AutonomousRouteRecoveryPolicy.IsRealPocketEscape(floor,
                floor + new System.Numerics.Vector3(32, 0, 0)), Is.False);
            Assert.That(AutonomousRouteRecoveryPolicy.IsRealPocketEscape(floor,
                floor + new System.Numerics.Vector3(33, 0, 0)), Is.True);
            Assert.That(AutonomousRouteRecoveryPolicy.IsRealPocketEscape(floor,
                floor + new System.Numerics.Vector3(0, 0, -100)), Is.True,
                "a height-only offset still moves the actor off its ledge");
        });
    }

    [Test]
    public void MovingOrderRetargetsOnlyForMarchLegsOrFarGoals()
    {
        var old = new System.Numerics.Vector3(1_000, 1_000, 0);
        var nearby = new System.Numerics.Vector3(1_300, 1_000, 0);
        var far = new System.Numerics.Vector3(1_000 + 1_600, 1_000, 0);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRouteRecoveryPolicy.ShouldRetargetMovingOrder(true, false, old, nearby), Is.False,
                "a small shift keeps the walking order");
            Assert.That(AutonomousRouteRecoveryPolicy.ShouldRetargetMovingOrder(true, true, old, nearby), Is.True,
                "a march leg or formation slot turns the bot at once");
            Assert.That(AutonomousRouteRecoveryPolicy.ShouldRetargetMovingOrder(true, false, old, far), Is.True,
                "a goal more than 1,500 units away retargets without the flag");
            Assert.That(AutonomousRouteRecoveryPolicy.ShouldRetargetMovingOrder(true, false, null, far), Is.False,
                "no previous destination: the ordinary gate decides");
            Assert.That(AutonomousRouteRecoveryPolicy.ShouldRetargetMovingOrder(false, true, old, far), Is.False,
                "an unchanged destination never retargets");
        });
    }
}
