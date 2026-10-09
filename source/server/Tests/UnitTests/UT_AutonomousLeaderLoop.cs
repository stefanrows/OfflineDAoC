using System;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// Groups run properly: followers of a moving autonomous leader match its
/// full run speed, a holding leader loops at a run instead of standing still,
/// and MOVE_PACE measures the speeds actually commanded.
/// </summary>
[TestFixture]
public sealed class UT_AutonomousLeaderLoop
{
    [Test]
    public void FollowersOfAMovingAutonomousLeaderMatchItsRunSpeedNotItsLastOrder()
    {
        // The leader's last order was a 75-unit crawl; it can run 191.
        Assert.That(AutonomousGroupMotion.LeaderPace(true, true, false, 75, 191), Is.EqualTo(191));
        Assert.That(AutonomousGroupMotion.LeaderPace(true, true, true, 75, 191), Is.EqualTo(75), "Stealthed groups creep as before");
        Assert.That(AutonomousGroupMotion.LeaderPace(false, true, false, 75, 191), Is.EqualTo(75), "Companions and player-led groups unchanged");
        Assert.That(AutonomousGroupMotion.LeaderPace(true, false, false, 0, 191), Is.EqualTo(1), "A stopped leader gives no run pace");
        Assert.That(AutonomousGroupMotion.LeaderPace(true, true, false, 75, 120), Is.EqualTo(120), "A snared leader's MaxSpeed still counts");

        short pace = AutonomousGroupMotion.LeaderPace(true, true, false, 75, 191);
        short inSlot = AutonomousGroupMotion.FollowSpeed(3, pace, 191, 30);
        Assert.That(inSlot, Is.InRange(183, 199), "Personal stride of +/-4 % around the run speed");
        Assert.That(AutonomousGroupMotion.FollowSpeed(3, pace, 191, 400), Is.GreaterThan(inSlot), "Catch-up bonus kept");
    }

    [Test]
    public void AFollowerAheadOfItsLeaderDoesNotOverrunItBeyondTheFormation()
    {
        Assert.That(AutonomousGroupMotion.CapAheadOfLeader(240, 191, false, false), Is.EqualTo(240), "Behind the leader: catch up freely");
        Assert.That(AutonomousGroupMotion.CapAheadOfLeader(240, 191, true, false), Is.EqualTo(191), "Ahead and in place: leader's pace");
        Assert.That(AutonomousGroupMotion.CapAheadOfLeader(240, 191, true, true), Is.EqualTo(210), "A front slot still ahead: 10 % more");
        Assert.That(AutonomousGroupMotion.CapAheadOfLeader(150, 191, true, true), Is.EqualTo(150));
    }

    [Test]
    public void LoopShapeStaysWithinTheAgreedRadiusPointsAndKeepsEachLeadersTurningHabit()
    {
        for (int i = 0; i <= 10; i++)
        {
            var shape = AutonomousLeaderLoop.ShapeFor(42, i / 10d, i / 10d, i / 10d);
            Assert.That(shape.Radius, Is.InRange(AutonomousLeaderLoop.MinimumRadius, AutonomousLeaderLoop.MaximumRadius));
            Assert.That(shape.Points, Is.InRange(AutonomousLeaderLoop.MinimumPoints, AutonomousLeaderLoop.MaximumPoints));
            Assert.That(shape.Clockwise, Is.EqualTo(AutonomousLeaderLoop.ShapeFor(42, 0, 0, 0).Clockwise), "Direction is a per-leader habit");
        }
        bool[] directions = Enumerable.Range(1, 64).Select(id => AutonomousLeaderLoop.ShapeFor(id, .5, .5, .5).Clockwise).ToArray();
        Assert.That(directions, Does.Contain(true).And.Contain(false), "Different leaders turn different ways");
    }

    [Test]
    public void LoopPointsCircleTheHoldPointInTurningOrder()
    {
        var centre = new Vector3(1000, 2000, 300);
        var shape = new AutonomousLeaderLoop.Shape(400, 4, false, 0);
        Vector3[] points = AutonomousLeaderLoop.LoopPoints(centre, shape);
        Assert.That(points, Has.Length.EqualTo(4));
        foreach (Vector3 point in points)
        {
            Assert.That(Vector2.Distance(new(point.X, point.Y), new(centre.X, centre.Y)), Is.EqualTo(400).Within(0.5));
            Assert.That(point.Z, Is.EqualTo(300));
        }
        Assert.That(points[0].X, Is.EqualTo(1400).Within(0.5));
        Assert.That(points[1].Y, Is.EqualTo(2400).Within(0.5), "Counter-clockwise second point");
        Vector3[] clockwise = AutonomousLeaderLoop.LoopPoints(centre, shape with { Clockwise = true });
        Assert.That(clockwise[1].Y, Is.EqualTo(1600).Within(0.5), "Clockwise second point");
    }

    [Test]
    public void LoopAdvancesOnArrivalOrWhenStoppedShortAndWraps()
    {
        Assert.That(AutonomousLeaderLoop.NextIndex(0, 5, 300), Is.EqualTo(0));
        Assert.That(AutonomousLeaderLoop.NextIndex(0, 5, AutonomousLeaderLoop.ArrivalRadius), Is.EqualTo(1));
        Assert.That(AutonomousLeaderLoop.NextIndex(4, 5, 10), Is.EqualTo(0));
        Assert.That(AutonomousLeaderLoop.NextIndex(2, 5, 300, stoppedAfterOrder: true), Is.EqualTo(3));
        Assert.That(AutonomousLeaderLoop.NextIndex(2, 0, 10), Is.EqualTo(0));
    }

    [Test]
    public void LeaderLoopsOnlyWhereStandingStillWasTheOnlyReason()
    {
        Assert.That(AutonomousLeaderLoop.MayLoop(false, false, false, false, false, false), Is.True);
        Assert.That(AutonomousLeaderLoop.MayLoop(true, false, false, false, false, false), Is.False, "combat");
        Assert.That(AutonomousLeaderLoop.MayLoop(false, true, false, false, false, false), Is.False, "keep or courtyard");
        Assert.That(AutonomousLeaderLoop.MayLoop(false, false, true, false, false, false), Is.False, "siege equipment");
        Assert.That(AutonomousLeaderLoop.MayLoop(false, false, false, true, false, false), Is.False, "expedition muster");
        Assert.That(AutonomousLeaderLoop.MayLoop(false, false, false, false, true, false), Is.False, "stealth");
        Assert.That(AutonomousLeaderLoop.MayLoop(false, false, false, false, false, true), Is.False, "dungeon");
        Assert.That(AutonomousLeaderLoop.MayLoop(false, false, false, false, false, false, nearHazard: true), Is.False,
            "zone crossing or PvE camp");
    }

    [Test]
    public void MovePacePercentilesComeFromTheFixedHistogram()
    {
        var bins = new int[AutonomousMovePace.BinCount];
        foreach (int speed in new[] { 75, 75, 80, 190, 191, 191, 191, 191, 191, 250 })
            bins[AutonomousMovePace.BinOf(speed)]++;
        Assert.That(AutonomousMovePace.Percentile(bins, 10, 0.1), Is.EqualTo(70));
        Assert.That(AutonomousMovePace.Percentile(bins, 10, 0.5), Is.EqualTo(190));
        Assert.That(AutonomousMovePace.Percentile(bins, 10, 0.9), Is.EqualTo(190));
        Assert.That(AutonomousMovePace.Percentile(bins, 10, 1.0), Is.EqualTo(250));
        Assert.That(AutonomousMovePace.Percentile(bins, 0, 0.5), Is.Zero);
        Assert.That(AutonomousMovePace.BinOf(5_000), Is.EqualTo(AutonomousMovePace.BinCount - 1));
        Assert.That(AutonomousMovePace.BinOf(-3), Is.Zero);
        Assert.That(AutonomousMovePace.RoleOf(false, false), Is.EqualTo(AutonomousMovePace.Bucket.Solo));
        Assert.That(AutonomousMovePace.RoleOf(true, true), Is.EqualTo(AutonomousMovePace.Bucket.Leader));
        Assert.That(AutonomousMovePace.RoleOf(true, false), Is.EqualTo(AutonomousMovePace.Bucket.Follower));
    }
}
