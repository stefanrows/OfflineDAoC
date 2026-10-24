using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Task 139 step 2: goal snap boxes, gate decision, re-pick cooldown, pocket-escape rule.</summary>
[TestFixture]
public sealed class UT_AutonomousRouteGoalFix
{
    /// <summary>A mesh floor at a fixed point; a query finds it only inside its box (like Detour's pick box).</summary>
    private sealed class FloorNavigation : PathfindingMgrBase
    {
        public readonly Vector3 Floor;
        public bool Available = true;
        public readonly List<(float X, float Y, float Z)> Boxes = new();
        public FloorNavigation(Vector3 floor) => Floor = floor;
        public override bool IsAvailable => Available;
        public override bool HasNavmesh(Zone zone) => true;
        public override Vector3? GetClosestPoint(Zone zone, Vector3 point, float x, float y, float z, EDtPolyFlags[] filters)
        {
            Boxes.Add((x, y, z));
            return MathF.Abs(Floor.X - point.X) <= x && MathF.Abs(Floor.Y - point.Y) <= y && MathF.Abs(Floor.Z - point.Z) <= z
                ? Floor : null;
        }
    }

    private static Zone AZone() => (Zone)RuntimeHelpers.GetUninitializedObject(typeof(Zone));

    [Test]
    public void AGoalOnTheMeshIsLeftByteIdentical()
    {
        var goal = new Vector3(100.5f, 200.25f, 300.125f);
        var nav = new FloorNavigation(new(100, 200, 330));
        var outcome = AutonomousGoalSnap.TrySnap(nav, AZone(), goal, null, out Vector3 snapped);
        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(AutonomousGoalSnap.Outcome.OnMesh));
            Assert.That(snapped, Is.EqualTo(goal));
            Assert.That(nav.Boxes, Is.EqualTo(new[] { (32f, 32f, 64f) }), "only the normal box is asked");
        });
    }

    [Test]
    public void ADruimLigenGoalHighAboveTheFloorSnapsInTheFirstWideBox()
    {
        // The logged goal hung 210 units above the floor: outside 32/32/64, inside 96/96/512.
        var goal = new Vector3(335449, 421910, 5401);
        var floor = new Vector3(335449, 421910, 5191);
        var nav = new FloorNavigation(floor);
        var outcome = AutonomousGoalSnap.TrySnap(nav, AZone(), goal, null, out Vector3 snapped);
        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(AutonomousGoalSnap.Outcome.Snapped));
            Assert.That(snapped, Is.EqualTo(floor));
            Assert.That(nav.Boxes, Is.EqualTo(new[] { (32f, 32f, 64f), (96f, 96f, 512f) }), "widest box not needed");
        });
    }

    [Test]
    public void TheWidestBoxIsTriedLastAndNeverExceeded()
    {
        var goal = new Vector3(0, 0, 5000);
        var nav = new FloorNavigation(new(200, 200, 4200)); // 200 units aside, 800 below
        var outcome = AutonomousGoalSnap.TrySnap(nav, AZone(), goal, null, out Vector3 snapped);
        Assert.That(outcome, Is.EqualTo(AutonomousGoalSnap.Outcome.Snapped));
        Assert.That(nav.Boxes, Is.EqualTo(new[] { (32f, 32f, 64f), (96f, 96f, 512f), (256f, 256f, 1024f) }));

        var far = new FloorNavigation(new(5000, 5000, 0));
        Assert.That(AutonomousGoalSnap.TrySnap(far, AZone(), goal, null, out Vector3 same),
            Is.EqualTo(AutonomousGoalSnap.Outcome.NoFloor));
        Assert.That(same, Is.EqualTo(goal));
        foreach ((float h, float v) in AutonomousGoalSnap.WideBoxes)
            Assert.That(h <= 256f && v <= 1024f, Is.True, "512/512/2048 crashed the Detour host");
        foreach (var box in far.Boxes)
            Assert.That(box.X <= 256f && box.Y <= 256f && box.Z <= 1024f, Is.True);
    }

    [Test]
    public void AFloorOutsideTheGoalsOwnZoneIsRejected()
    {
        var goal = new Vector3(0, 0, 5000);
        var nav = new FloorNavigation(new(0, 0, 4900));
        Assert.That(AutonomousGoalSnap.TrySnap(nav, AZone(), goal, _ => false, out _),
            Is.EqualTo(AutonomousGoalSnap.Outcome.NoFloor));
    }

    [Test]
    public void WithoutANavmeshNothingIsQueried()
    {
        var nav = new FloorNavigation(Vector3.Zero) { Available = false };
        Assert.That(AutonomousGoalSnap.TrySnap(nav, AZone(), Vector3.One, null, out _),
            Is.EqualTo(AutonomousGoalSnap.Outcome.NoFloor));
        Assert.That(nav.Boxes, Is.Empty);
        Assert.That(AutonomousGoalSnap.TrySnap(new FloorNavigation(Vector3.Zero), null, Vector3.One, null, out _),
            Is.EqualTo(AutonomousGoalSnap.Outcome.NoFloor));
    }

    [TestCase(false, true, false, true, TestName = "closed enemy gate blocks")]
    [TestCase(true, true, false, false, TestName = "a keep the bot may pass never blocks")]
    [TestCase(false, false, false, false, TestName = "a breached keep is open")]
    [TestCase(false, true, true, false, TestName = "the keep the force is sieging keeps its targets")]
    public void GateDecision(bool canPass, bool gateIntact, bool sieged, bool blocks) =>
        Assert.That(AutonomousKeepGateGoals.GateBlocks(canPass, gateIntact, sieged), Is.EqualTo(blocks));

    [Test]
    public void KeepFootprintCoversTheLoggedGatedGoalsButNotTheRoadOutside()
    {
        // Saved Excalibur: centre 601562,430054; DF portal 601798,430727 (713 units); smallest saved outer ring 1,270.
        // Caer Renaris: centre 579877,429821; PvP target 579126,429197 (975 units); outer door 1,415.
        float renaris = AutonomousKeepGateGoals.CourtyardRadius(1_415f);
        float smallest = AutonomousKeepGateGoals.CourtyardRadius(1_270f);
        Assert.Multiple(() =>
        {
            Assert.That(renaris, Is.EqualTo(1_115f));
            Assert.That(AutonomousKeepGateGoals.CourtyardRadius(700f), Is.EqualTo(AutonomousKeepGateGoals.MinimumCourtyardRadius));
            Assert.That(AutonomousKeepGateGoals.IsInsideFootprint(601798 - 601562, 430727 - 430054, smallest), Is.True);
            Assert.That(AutonomousKeepGateGoals.IsInsideFootprint(579126 - 579877, 429197 - 429821, renaris), Is.True);
            // A warband at the back wall (about 1,200 out) is outside the courtyard and stays a target.
            Assert.That(AutonomousKeepGateGoals.IsInsideFootprint(1_200, 0, renaris), Is.False);
            Assert.That(AutonomousKeepGateGoals.IsInsideFootprint(2_500, 0, renaris), Is.False);
            // Only doors in the outer ring hold the keep shut; an inner keep door does not.
            Assert.That(AutonomousKeepGateGoals.IsOuterGate(1_415f, 1_415f), Is.True);
            Assert.That(AutonomousKeepGateGoals.IsOuterGate(500f, 1_415f), Is.False);
        });
    }

    [Test]
    public void AFailedGoalIsBlockedForTheCooldownWithinTwoHundredUnitsOnly()
    {
        AutonomousFailedGoalMemory.Clear();
        const long owner = 101;
        AutonomousFailedGoalMemory.Note(owner, 200, 335449, 421910, 1_000);
        long inside = 1_000 + AutonomousFailedGoalMemory.CooldownMilliseconds - 1;
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner, 200, 335449, 421910, 2_000), Is.True);
            Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner, 200, 335449 + 190, 421910, inside), Is.True);
            Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner, 200, 335449 + 210, 421910, 2_000), Is.False, "too far");
            Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner, 100, 335449, 421910, 2_000), Is.False, "other region");
            Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner + 1, 200, 335449, 421910, 2_000), Is.False, "other bot");
            Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner, 200, 335449, 421910,
                1_000 + AutonomousFailedGoalMemory.CooldownMilliseconds), Is.False, "cooldown over");
        });
    }

    [Test]
    public void AWarbandSharesItsMemoryUnderOneForceKey()
    {
        AutonomousFailedGoalMemory.Clear();
        long force = AutonomousFailedGoalMemory.ForceKey("group-17");
        Assert.That(force, Is.Not.EqualTo(0));
        Assert.That(force, Is.EqualTo(AutonomousFailedGoalMemory.ForceKey("group-17")));
        Assert.That(AutonomousFailedGoalMemory.ForceKey(""), Is.EqualTo(0));
        AutonomousFailedGoalMemory.Note(force, 1, 10, 10, 0);
        Assert.That(AutonomousFailedGoalMemory.IsBlocked(force, 1, 50, 50, 10), Is.True);
        AutonomousFailedGoalMemory.Note(0, 1, 10, 10, 0);
        Assert.That(AutonomousFailedGoalMemory.IsBlocked(0, 1, 10, 10, 10), Is.False, "owner 0 is no owner");
    }

    [Test]
    public void TheSameSpotRefreshesAndTheOldestOfFourIsReplaced()
    {
        AutonomousFailedGoalMemory.Clear();
        const long owner = 7;
        for (int i = 0; i < 4; i++)
            AutonomousFailedGoalMemory.Note(owner, 1, i * 1_000, 0, i * 10);
        AutonomousFailedGoalMemory.Note(owner, 1, 9_000, 0, 100); // replaces the spot noted at tick 0
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner, 1, 0, 0, 101), Is.False);
            Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner, 1, 1_000, 0, 101), Is.True);
            Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner, 1, 9_000, 0, 101), Is.True);
        });
        AutonomousFailedGoalMemory.Note(owner, 1, 1_050, 0, AutonomousFailedGoalMemory.CooldownMilliseconds); // same spot, later
        Assert.That(AutonomousFailedGoalMemory.IsBlocked(owner, 1, 1_000, 0, AutonomousFailedGoalMemory.CooldownMilliseconds + 5_000), Is.True);
    }

    [TestCase(true, true, true, TestName = "connected start and an off-mesh goal: no relocation")]
    [TestCase(true, false, false, TestName = "a goal on the mesh keeps the relocation rule")]
    [TestCase(false, true, false, TestName = "a bot in a real pocket still escapes")]
    public void PocketEscapeIsSkippedOnlyWhenTheGoalIsTheFault(bool startConnected, bool goalOffMesh, bool skip) =>
        Assert.That(AutonomousPocketEscapeRule.SkipRelocation(startConnected, goalOffMesh), Is.EqualTo(skip));
}
