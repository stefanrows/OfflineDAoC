using System;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
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
    public void LoopStateLastsTheWholeHoldWithAFixedCentreAndEndsWithIt()
    {
        var leader = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
        var centre = new Vector3(5000, 5000, 100);
        int builds = 0;
        Vector3[] Build(Vector3 at)
        {
            builds++;
            return AutonomousLeaderLoop.LoopPoints(at, new AutonomousLeaderLoop.Shape(400, 4, false, 0));
        }
        try
        {
            var hold = AutonomousLeaderLoop.GetOrBeginHold(leader, 1, centre, 0, Build, out bool started);
            Assert.That(started, Is.True);
            // Think turns 4-12 s apart while the leader runs the loop: one hold, one build, one centre.
            long now = 0;
            foreach (Vector3 point in hold.Points.Concat(hold.Points))
            {
                now += 12_000;
                var again = AutonomousLeaderLoop.GetOrBeginHold(leader, 1, point, now, Build, out started);
                Assert.That(again, Is.SameAs(hold));
                Assert.That(started, Is.False);
            }
            Assert.That(builds, Is.EqualTo(1), "No rebuild (and no nav queries) per think");
            Assert.That(AutonomousLeaderLoop.TryGetHoldCentre(leader, out Vector3 fixedCentre), Is.True);
            Assert.That(fixedCentre, Is.EqualTo(centre), "The centre does not drift with the leader");

            AutonomousLeaderLoop.EndHold(leader);
            Assert.That(AutonomousLeaderLoop.TryGetHoldCentre(leader, out _), Is.False, "Cleared when the hold ends");
            var next = AutonomousLeaderLoop.GetOrBeginHold(leader, 1, centre + new Vector3(3000, 0, 0), now + 1, Build, out started);
            Assert.That(started, Is.True);
            Assert.That(next.Centre.X, Is.EqualTo(8000));
            var stale = AutonomousLeaderLoop.GetOrBeginHold(leader, 1, next.Centre,
                now + 2 + AutonomousLeaderLoop.StaleHoldMilliseconds, Build, out started);
            Assert.That(started, Is.True, "A hold left alone (combat, death) does not resume later");
            Assert.That(stale, Is.Not.SameAs(next));
        }
        finally { AutonomousLeaderLoop.EndHold(leader); }
    }

    [Test]
    public void HoldStepsRunOnFromPointToPointWithoutReissuingEveryTurn()
    {
        var points = AutonomousLeaderLoop.LoopPoints(Vector3.Zero, new AutonomousLeaderLoop.Shape(400, 4, false, 0));
        var hold = new AutonomousLeaderLoop.Hold(1, Vector3.Zero, points, 0);
        Assert.That(hold.Step(Vector3.Zero, false, 0, out Vector3 target), Is.True);
        Assert.That(target, Is.EqualTo(points[0]));
        Assert.That(hold.Step(new Vector3(200, 0, 0), true, 500, out target), Is.False, "Still running to the same point");
        Assert.That(hold.Step(points[0], true, 2_000, out target), Is.True, "Reached: on to the next point");
        Assert.That(target, Is.EqualTo(points[1]));
        Assert.That(hold.Step(new Vector3(300, 100, 0), false, 3_500, out target), Is.True, "Stopped short: on to the next");
        Assert.That(target, Is.EqualTo(points[2]));
        Assert.That(new AutonomousLeaderLoop.Hold(1, Vector3.Zero, [], 0).Step(Vector3.Zero, false, 0, out _), Is.False);
    }

    [Test]
    public void CirclingInsideTheLoopIsNotRouteProgressForTheWatchdog()
    {
        var leader = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
        var centre = new Vector3(1000, 1000, 0);
        try
        {
            AutonomousLeaderLoop.GetOrBeginHold(leader, 7, centre, 0,
                at => AutonomousLeaderLoop.LoopPoints(at, new AutonomousLeaderLoop.Shape(450, 5, true, 0)), out _);
            Assert.That(AutonomousLeaderLoop.StaysInLoop(leader, 7, new(1450, 1000), new(1000, 550)), Is.True);
            Assert.That(AutonomousLeaderLoop.StaysInLoop(leader, 7, new(1450, 1000), new(3000, 1000)), Is.False, "Left the loop: real movement");
            Assert.That(AutonomousLeaderLoop.StaysInLoop(leader, 8, new(1450, 1000), new(1000, 550)), Is.False);
            AutonomousLeaderLoop.EndHold(leader);
            Assert.That(AutonomousLeaderLoop.StaysInLoop(leader, 7, new(1450, 1000), new(1000, 550)), Is.False);
        }
        finally { AutonomousLeaderLoop.EndHold(leader); }
    }

    [Test]
    public void SiegeColumnProgressIsMeasuredFromTheFixedHoldCentre()
    {
        var centre = new Vector3(0, 0, 0);
        (bool, Vector3)[] stragglers = [(true, new Vector3(3000, 0, 0)), (true, new Vector3(0, 1500, 0))];
        float gap = AutonomousRvrSpeed.WorstGapFrom(centre, stragglers);
        Assert.That(gap, Is.EqualTo(3000).Within(0.01), "Independent of where the looping leader happens to be");
        Assert.That(AutonomousRvrSpeed.SiegeColumnClosedUp(gap, gap), Is.False, "Loop motion alone is no progress");
        Assert.That(AutonomousRvrSpeed.SiegeColumnClosedUp(gap,
            AutonomousRvrSpeed.WorstGapFrom(centre, [(true, new Vector3(2700, 0, 0))])), Is.True, "The straggler closed in");
        Assert.That(AutonomousRvrSpeed.WorstGapFrom(centre, [(false, Vector3.Zero)]), Is.EqualTo(float.PositiveInfinity));
        Assert.That(AutonomousRvrSpeed.WorstGapFrom(centre, []), Is.Zero);
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
