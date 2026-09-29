using System;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public class UT_RvrRouteWork
{
    private sealed class Navigation : PathfindingMgrBase
    {
        public long Clock;
        public int Calls;
        public bool Failed;
        public bool Partial;
        public override bool IsAvailable => true;
        public override bool HasNavmesh(Zone zone) => true;
        public override Vector3? GetClosestPoint(Zone zone, Vector3 point, float x, float y, float z, EDtPolyFlags[] filters)
        { Calls++; Clock += 10; return point; }
        public override Vector3? GetClosestPoint(Zone zone, Vector3 point, EDtPolyFlags[] filters) => point + Vector3.UnitY;
        public override PathfindingResult GetPathStraight(Zone zone, Vector3 start, Vector3 end,
            EDtPolyFlags[] filters, Span<WrappedPathfindingNode> destination)
        {
            Calls++; Clock += 10;
            if (Failed) return new(PathfindingStatus.NoPathFound, 0);
            if (Partial && start.X < 100)
            { destination[0] = new(new(100, 0, 0), 0); return new(PathfindingStatus.PartialPathFound, 1); }
            destination[0] = new(end, 0); return new(PathfindingStatus.PathFound, 1);
        }
    }

    [Test]
    public void YieldIsNotRecordedAsFailureAndResumesPartialCorridor()
    {
        var nav = new Navigation { Partial = true };
        var work = new RvrPlanningNavigation(nav, () => nav.Clock);
        work.BeginSlice();
        Assert.Throws<RvrPlanningNavigation.Yield>(() => AutonomousZoneItinerary.HasCompleteCorridor(work, null, Vector3.Zero, new(1000, 0, 0)));
        Assert.That(nav.Calls, Is.EqualTo(1));
        work.BeginSlice();
        Assert.That(AutonomousZoneItinerary.HasCompleteCorridor(work, null, Vector3.Zero, new(1000, 0, 0)), Is.True);
        Assert.That(nav.Calls, Is.EqualTo(2), "Replay retains the completed native segment");
        work.BeginSlice();
        Assert.That(AutonomousZoneItinerary.HasCompleteCorridor(work, null, Vector3.Zero, new(1000, 0, 0)), Is.True);
        Assert.That(nav.Calls, Is.EqualTo(2));
    }

    [Test]
    public void FailedCorridorIsMemoizedWithoutChangingOtherEndpoints()
    {
        var nav = new Navigation { Failed = true };
        var work = new RvrPlanningNavigation(nav, () => nav.Clock);
        work.BeginSlice();
        Assert.That(AutonomousZoneItinerary.HasCompleteCorridor(work, null, Vector3.Zero, Vector3.One), Is.False);
        work.BeginSlice();
        Assert.That(AutonomousZoneItinerary.HasCompleteCorridor(work, null, Vector3.Zero, Vector3.One), Is.False);
        Assert.That(nav.Calls, Is.EqualTo(1));
        nav.Failed = false;
        Assert.That(AutonomousZoneItinerary.HasCompleteCorridor(work, null, Vector3.Zero, new(2, 0, 0)), Is.True);
    }

    [Test]
    public void NewRequestDoesNotInheritOldDoorOrFailureAnswers()
    {
        var nav = new Navigation { Failed = true };
        var first = new RvrPlanningNavigation(nav, () => nav.Clock); first.BeginSlice();
        Assert.That(AutonomousZoneItinerary.HasCompleteCorridor(first, null, Vector3.Zero, Vector3.One), Is.False);
        nav.Failed = false;
        var next = new RvrPlanningNavigation(nav, () => nav.Clock); next.BeginSlice();
        Assert.That(AutonomousZoneItinerary.HasCompleteCorridor(next, null, Vector3.Zero, Vector3.One), Is.True);
    }

    [Test]
    public void PathWorkAndMemoryHaveAHardQueryCeiling()
    {
        var nav = new Navigation(); var work = new RvrPlanningNavigation(nav, () => nav.Clock);
        for (int i = 0; i < RvrPlanningNavigation.MaximumQueries; i++)
        { work.BeginSlice(); work.GetClosestPoint(null, new(i, 0, 0), 2, 2, 128, work.DefaultFilters); }
        work.BeginSlice();
        Assert.Throws<RvrPlanningNavigation.Limit>(() => work.GetClosestPoint(null, new(-1, 0, 0), 2, 2, 128, work.DefaultFilters));
        Assert.That(nav.Calls, Is.EqualTo(RvrPlanningNavigation.MaximumQueries));
    }

    [Test]
    public void DoorRevisionInvalidatesTheSameRequestsCorridorAnswer()
    {
        var zone=(Zone)RuntimeHelpers.GetUninitializedObject(typeof(Zone));
        var nav=new Navigation { Failed=true };
        var work=new RvrPlanningNavigation(nav,()=>nav.Clock);work.BeginSlice();
        Assert.That(AutonomousZoneItinerary.HasCompleteCorridor(work,zone,Vector3.Zero,Vector3.One),Is.False);
        nav.Failed=false;NavigationGeometryRevision.Changed(zone);work.BeginSlice();
        Assert.That(AutonomousZoneItinerary.HasCompleteCorridor(work,zone,Vector3.Zero,Vector3.One),Is.True);
    }

    [Test]
    public void TimeBetweenSlicesDoesNotSpendTheActiveWorkBudget()
    {
        var nav = new Navigation(); var work = new RvrPlanningNavigation(nav, () => nav.Clock);
        work.BeginSlice();
        work.GetClosestPoint(null, Vector3.Zero, 2, 2, 128, work.DefaultFilters);
        work.EndSlice();
        nav.Clock += 4 * 60 * 60_000L;
        work.BeginSlice();
        Assert.DoesNotThrow(() => work.GetClosestPoint(null, Vector3.One, 2, 2, 128, work.DefaultFilters));
        work.EndSlice();
        Assert.That(work.Queries, Is.EqualTo(2));
    }

    [Test]
    public void ActiveWorkBudgetStillStopsAnExpensiveRequest()
    {
        var nav = new Navigation(); var work = new RvrPlanningNavigation(nav, () => nav.Clock);
        work.BeginSlice(); nav.Clock += RvrPlanningNavigation.MaximumActiveMilliseconds; work.EndSlice();
        work.BeginSlice();
        Assert.Throws<RvrPlanningNavigation.Limit>(() => work.GetClosestPoint(null, Vector3.Zero, 2, 2, 128, work.DefaultFilters));
    }

    [Test]
    public void DefaultProjectionDelegatesItsOriginalExtents()
    {
        var nav = new Navigation(); var work = new RvrPlanningNavigation(nav); work.BeginSlice();
        Assert.That(work.GetClosestPoint(null, Vector3.Zero, work.DefaultFilters), Is.EqualTo(Vector3.UnitY));
    }
    // ---- Wave 1 (P3): route variants and the hub-exit fan ------------------

    private static double Share(RvrDoctrineKind kind, RvrLeaderTraits traits, RvrRouteVariant variant, Random random)
    {
        const int rolls = 20_000;
        int hits = 0;
        for (int i = 0; i < rolls; i++)
            if (AutonomousRvrRoutePolicy.PickVariant(kind, traits, random.NextDouble()) == variant) hits++;
        return hits / (double)rolls;
    }

    [TestCase(RvrDoctrineKind.SoloAssassin)]
    [TestCase(RvrDoctrineKind.StealthPack)]
    [TestCase(RvrDoctrineKind.GankSquad)]
    public void StealthDoctrinesLeaveTheRoadMostOfTheTime(RvrDoctrineKind kind)
    {
        double road = Share(kind, RvrLeaderTraits.Neutral, RvrRouteVariant.Road, new Random(4711));
        Assert.That(road, Is.LessThanOrEqualTo(0.3));
        Assert.That(road, Is.EqualTo(0.2).Within(0.02));
    }

    [TestCase(RvrDoctrineKind.AssistTrain)]
    [TestCase(RvrDoctrineKind.MeleeTrain)]
    [TestCase(RvrDoctrineKind.KeepRaid)]
    public void TrainsAndRaidsMostlyWalkTheRoad(RvrDoctrineKind kind) =>
        Assert.That(Share(kind, RvrLeaderTraits.Neutral, RvrRouteVariant.Road, new Random(17)), Is.EqualTo(0.7).Within(0.02));

    [Test]
    public void OtherDoctrinesSplitFiftyThirtyTwenty()
    {
        (double road, double flank, double cover) = AutonomousRvrRoutePolicy.Weights(RvrDoctrineKind.SmallMan, RvrLeaderTraits.Neutral);
        Assert.Multiple(() =>
        {
            Assert.That(road, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(flank, Is.EqualTo(0.3).Within(1e-9));
            Assert.That(cover, Is.EqualTo(0.2).Within(1e-9));
            Assert.That(Share(RvrDoctrineKind.PickupGroup, RvrLeaderTraits.Neutral, RvrRouteVariant.Cover, new Random(99)),
                Is.EqualTo(0.2).Within(0.02));
        });
    }

    [Test]
    public void CautiousLeaderShiftsTwentyPercentToCover()
    {
        var cautious = new RvrLeaderTraits(50, 30, 50);
        (double road, double flank, double cover) = AutonomousRvrRoutePolicy.Weights(RvrDoctrineKind.SmallMan, cautious);
        (double stealthRoad, _, double stealthCover) = AutonomousRvrRoutePolicy.Weights(RvrDoctrineKind.SoloAssassin, cautious);
        Assert.Multiple(() =>
        {
            Assert.That(road, Is.EqualTo(0.3).Within(1e-9));
            Assert.That(flank, Is.EqualTo(0.3).Within(1e-9));
            Assert.That(cover, Is.EqualTo(0.4).Within(1e-9));
            Assert.That(stealthRoad, Is.EqualTo(0).Within(1e-9));
            Assert.That(stealthCover, Is.EqualTo(0.6).Within(1e-9));
            Assert.That(AutonomousRvrRoutePolicy.Weights(RvrDoctrineKind.SmallMan, new RvrLeaderTraits(50, 40, 50)).Road,
                Is.EqualTo(0.5).Within(1e-9), "40 is not cautious");
        });
    }

    private static RvrRouteProbe OpenGround(Func<Vector3, bool> zone = null, Func<Vector3, Vector3, bool> corridor = null) =>
        new((raw, _) => raw, zone ?? (_ => true), corridor ?? ((_, _) => true), true);

    [Test]
    public void ChooseRouteFallsBackToTheDestinationWithoutNavigationOrOnAShortLeg()
    {
        Vector3 destination = new(20_000, 0, 0);
        RvrRouteChoice noNav = AutonomousRvrRoutePolicy.ChooseRoute(Vector3.Zero, destination, RvrRouteVariant.Flank, null, new Random(1));
        RvrRouteChoice shortLeg = AutonomousRvrRoutePolicy.ChooseRoute(Vector3.Zero, new(3_000, 0, 0), RvrRouteVariant.Cover,
            OpenGround(), new Random(1));
        Assert.Multiple(() =>
        {
            Assert.That(noNav.Waypoint, Is.EqualTo(destination));
            Assert.That(noNav.Fallback, Is.True);
            Assert.That(shortLeg.Waypoint, Is.EqualTo(new Vector3(3_000, 0, 0)));
            Assert.That(shortLeg.Variant, Is.EqualTo(RvrRouteVariant.Road));
        });
    }

    [Test]
    public void RoadIsStraightAheadWithoutASideOffset()
    {
        RvrRouteChoice road = AutonomousRvrRoutePolicy.ChooseRoute(Vector3.Zero, new(20_000, 0, 0), RvrRouteVariant.Road,
            OpenGround(), new Random(3));
        Assert.That(road.Waypoint, Is.EqualTo(new Vector3(2_400, 0, 0)));
        Assert.That(road.Fallback, Is.False);
    }

    [Test]
    public void FlankViaLiesBesideTheMiddleOfTheLeg()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            RvrRouteChoice flank = AutonomousRvrRoutePolicy.ChooseRoute(Vector3.Zero, new(10_000, 0, 0), RvrRouteVariant.Flank,
                OpenGround(), new Random(seed));
            Assert.That(flank.Variant, Is.EqualTo(RvrRouteVariant.Flank));
            // 40-60 % of the next 8,000 units, the widest side distance first.
            Assert.That(flank.Waypoint.X, Is.InRange(3_200f, 4_800f));
            Assert.That(Math.Abs(flank.Waypoint.Y), Is.EqualTo(2_000f).Within(0.5f));
        }
    }

    [Test]
    public void CoverViaLiesOnTheSideAwayFromTheLatestFight()
    {
        Vector3 heatLeft = new(5_000, 3_000, 0); // left of an eastward leg (+Y)
        for (int seed = 0; seed < 30; seed++)
        {
            RvrRouteChoice cover = AutonomousRvrRoutePolicy.ChooseRoute(Vector3.Zero, new(10_000, 0, 0), RvrRouteVariant.Cover,
                OpenGround(), new Random(seed), heat: heatLeft);
            Assert.That(cover.Waypoint.Y, Is.LessThan(-1_000f));
        }
        // A fight far away does not steer the route; it behaves like Flank.
        bool sawBothSides = Enumerable.Range(0, 40).Select(seed => AutonomousRvrRoutePolicy.ChooseRoute(Vector3.Zero,
                new(10_000, 0, 0), RvrRouteVariant.Cover, OpenGround(), new Random(seed), heat: new(5_000, 40_000, 0)).Waypoint.Y > 0)
            .Distinct().Count() == 2;
        Assert.That(sawBothSides, Is.True);
    }

    [Test]
    public void RejectedViaPointFallsBackToRoadThenToTheDestination()
    {
        // Only the straight line (Y == 0) is walkable.
        RvrRouteChoice onlyRoad = AutonomousRvrRoutePolicy.ChooseRoute(Vector3.Zero, new(10_000, 0, 0), RvrRouteVariant.Flank,
            OpenGround(zone: point => Math.Abs(point.Y) < 1), new Random(5));
        RvrRouteChoice nothing = AutonomousRvrRoutePolicy.ChooseRoute(Vector3.Zero, new(10_000, 0, 0), RvrRouteVariant.Flank,
            OpenGround(corridor: (_, _) => false), new Random(5));
        Assert.Multiple(() =>
        {
            Assert.That(onlyRoad.Waypoint, Is.EqualTo(new Vector3(2_400, 0, 0)));
            Assert.That(onlyRoad.Variant, Is.EqualTo(RvrRouteVariant.Road));
            Assert.That(onlyRoad.Fallback, Is.True);
            Assert.That(onlyRoad.Reason, Is.EqualTo("zone"));
            Assert.That(nothing.Waypoint, Is.EqualTo(new Vector3(10_000, 0, 0)));
            Assert.That(nothing.Fallback, Is.True);
            Assert.That(nothing.Reason, Is.EqualTo("corridor_a"));
        });
    }

    [Test]
    public void HubFanPointLiesOutsideTheSafeRingOnAForwardBearing()
    {
        Vector3 hub = new(766_235, 669_173, 0);
        Vector3 start = hub + new Vector3(300, 0, 0);
        Vector3 destination = hub + new Vector3(30_000, 0, 0);
        var bearings = new System.Collections.Generic.HashSet<int>();
        for (int seed = 0; seed < 60; seed++)
        {
            RvrRouteChoice fan = AutonomousRvrRoutePolicy.ChooseRoute(start, destination, RvrRouteVariant.HubFan,
                OpenGround(), new Random(seed), hubCentre: hub);
            float radius = Vector2.Distance(new(fan.Waypoint.X, fan.Waypoint.Y), new(hub.X, hub.Y));
            double bearing = Math.Atan2(fan.Waypoint.Y - hub.Y, fan.Waypoint.X - hub.X) * 180 / Math.PI;
            Assert.That(fan.Variant, Is.EqualTo(RvrRouteVariant.HubFan));
            Assert.That(radius, Is.InRange(4_499f, 6_001f));
            Assert.That(Math.Abs(bearing), Is.LessThanOrEqualTo(120.01));
            bearings.Add((int)Math.Floor(bearing / 30));
        }
        Assert.That(bearings.Count, Is.GreaterThanOrEqualTo(6), "groups fan out, not one corridor");
    }

    [Test]
    public void HubFanTriesFourBearingsThenLeavesTheRouteToTheCaller()
    {
        Vector3 hub = Vector3.Zero;
        int probes = 0;
        RvrRouteChoice fan = AutonomousRvrRoutePolicy.ChooseRoute(new(100, 0, 0), new(30_000, 0, 0), RvrRouteVariant.HubFan,
            OpenGround(zone: point => { if (Vector2.Distance(new(point.X, point.Y), Vector2.Zero) > 4_000) { probes++; return false; } return true; }),
            new Random(8), hubCentre: hub);
        Assert.Multiple(() =>
        {
            Assert.That(probes, Is.EqualTo(AutonomousRvrRoutePolicy.HubFanAttempts));
            Assert.That(fan.Variant, Is.EqualTo(RvrRouteVariant.HubFan));
            Assert.That(fan.Fallback, Is.True);
            Assert.That(fan.Waypoint, Is.EqualTo(new Vector3(30_000, 0, 0)), "no Road step is computed and discarded");
        });
    }

    [TestCase(eRealm.Midgard, 766_235, 669_173, ExpectedResult = true, TestName = "Svasud keep circle starts the fan")]
    [TestCase(eRealm.Midgard, 764_890, 672_960, ExpectedResult = true, TestName = "Svasud outer bindstone landing starts the fan")]
    [TestCase(eRealm.Midgard, 766_235, 676_173, ExpectedResult = false, TestName = "7,000 out of Svasud is the frontier")]
    [TestCase(eRealm.Albion, 766_235, 669_173, ExpectedResult = false, TestName = "Another realm's hub is not home")]
    public bool HubFanStartsOnlyInsideTheOwnSafeHub(eRealm realm, int x, int y) =>
        AutonomousRvrTravel.IsInOwnSafeHub(realm, 100, x, y, out _);

    [Test]
    public void HubFanFromTheSauvageLandingStartsBeyondTheLandingEdge()
    {
        Assert.That(AutonomousRvrTravel.IsInOwnSafeHub(eRealm.Albion, 1, 584_340, 486_620,
            out AutonomousHubDeparture.SafeAnchor landing), Is.True);
        Vector3 centre = new(landing.Centre.X, landing.Centre.Y, 0);
        for (int seed = 0; seed < 30; seed++)
        {
            RvrRouteChoice fan = AutonomousRvrRoutePolicy.ChooseRoute(centre, centre + new Vector3(0, 30_000, 0),
                RvrRouteVariant.HubFan, OpenGround(), new Random(seed), hubCentre: centre, hubSafeRadius: landing.Radius);
            Assert.That(Vector2.Distance(new(fan.Waypoint.X, fan.Waypoint.Y), landing.Centre), Is.InRange(2_499f, 4_001f));
        }
    }

    [Test]
    public void HubFanSearchesTheFloorWellBelowARaisedHub()
    {
        // Svasud Faste sits on a rise: the land 5,000 out is 800 lower.
        Vector3 hub = new(0, 0, 5_000);
        var probe = new RvrRouteProbe((raw, range) => range >= 800 ? raw with { Z = 4_200 } : null,
            _ => true, (_, _) => true, true);
        RvrRouteChoice fan = AutonomousRvrRoutePolicy.ChooseRoute(hub, new(30_000, 0, 4_000), RvrRouteVariant.HubFan,
            probe, new Random(2), hubCentre: hub);
        Assert.That(fan.Fallback, Is.False);
        Assert.That(fan.Waypoint.Z, Is.EqualTo(4_200f));
    }
    // ---- Wave 1b: why via-points fell back, and the bounded retry ----------

    private sealed class CountingProbe
    {
        public int Floors, Corridors;
        public Func<Vector3, float, Vector3?> Floor = (raw, _) => raw;
        public Func<Vector3, Vector3, bool> Corridor = (_, _) => true;
        public Func<Vector3, bool> Zone = _ => true;
        public bool DestinationInZone = true;
        public RvrRouteProbe Probe => new((raw, range) => { Floors++; return Floor(raw, range); }, Zone,
            (a, b) => { Corridors++; return Corridor(a, b); }, DestinationInZone);
    }

    [Test]
    public void FlankTriesThreeSideDistancesWidestFirst()
    {
        var probe = new CountingProbe();
        var tried = new System.Collections.Generic.List<float>();
        probe.Floor = (raw, _) => { tried.Add(Math.Abs(raw.Y)); return Math.Abs(raw.Y) < 700 ? raw : null; };
        RvrRouteChoice flank = AutonomousRvrRoutePolicy.ChooseRoute(Vector3.Zero, new(20_000, 0, 0), RvrRouteVariant.Flank,
            probe.Probe, new Random(4));
        Assert.Multiple(() =>
        {
            Assert.That(tried, Is.EqualTo(new[] { 2_000f, 1_200f, 600f }).Within(0.5f));
            Assert.That(flank.Variant, Is.EqualTo(RvrRouteVariant.Flank));
            Assert.That(flank.Fallback, Is.False);
            Assert.That(Math.Abs(flank.Waypoint.Y), Is.EqualTo(600f).Within(0.5f));
        });
    }

    [TestCase(RvrRouteVariant.Flank)]
    [TestCase(RvrRouteVariant.Cover)]
    [TestCase(RvrRouteVariant.HubFan)]
    [TestCase(RvrRouteVariant.Road)]
    public void OneRouteChoiceSpendsAtMostSixNavigationQueries(RvrRouteVariant variant)
    {
        var failing = new CountingProbe { Corridor = (_, _) => false };
        AutonomousRvrRoutePolicy.ChooseRoute(Vector3.Zero, new(6_000, 0, 0), variant, failing.Probe, new Random(1),
            hubCentre: Vector3.Zero);
        var noFloor = new CountingProbe { Floor = (_, _) => null };
        AutonomousRvrRoutePolicy.ChooseRoute(Vector3.Zero, new(6_000, 0, 0), variant, noFloor.Probe, new Random(1),
            hubCentre: Vector3.Zero);
        Assert.That(failing.Floors + failing.Corridors, Is.LessThanOrEqualTo(AutonomousRvrRoutePolicy.QueryBudget));
        Assert.That(noFloor.Floors + noFloor.Corridors, Is.LessThanOrEqualTo(AutonomousRvrRoutePolicy.QueryBudget));
    }

    [Test]
    public void ALongCrossZoneLegBendsNearTheStartAndSkipsTheFarCheck()
    {
        // 80 km legs are normal; 40-60 % of them lies zones away.
        var probe = new CountingProbe();
        RvrRouteChoice flank = AutonomousRvrRoutePolicy.ChooseRoute(Vector3.Zero, new(80_000, 0, 0), RvrRouteVariant.Flank,
            probe.Probe, new Random(2));
        Assert.Multiple(() =>
        {
            Assert.That(flank.Fallback, Is.False);
            Assert.That(flank.Waypoint.X, Is.LessThanOrEqualTo(AutonomousRvrRoutePolicy.LocalLegLength));
            Assert.That(probe.Corridors, Is.EqualTo(1), "only start to via-point; the 76 km remainder is not queried");
        });
    }

    [Test]
    public void ReasonNamesTheRejectingCheck()
    {
        var farWall = new CountingProbe { Corridor = (from, _) => from == Vector3.Zero };
        RvrRouteChoice secondLeg = AutonomousRvrRoutePolicy.ChooseRoute(Vector3.Zero, new(6_000, 0, 0), RvrRouteVariant.Flank,
            farWall.Probe, new Random(3));
        var cliffs = new CountingProbe { Floor = (_, _) => null };
        RvrRouteChoice floor = AutonomousRvrRoutePolicy.ChooseRoute(Vector3.Zero, new(6_000, 0, 0), RvrRouteVariant.Road,
            cliffs.Probe, new Random(3));
        Assert.Multiple(() =>
        {
            Assert.That(secondLeg.Reason, Is.EqualTo("corridor_b"));
            Assert.That(floor.Reason, Is.EqualTo("floor"));
            Assert.That(AutonomousRvrRoutePolicy.ChooseRoute(Vector3.Zero, new(2_000, 0, 0), RvrRouteVariant.Flank,
                new CountingProbe().Probe, new Random(3)).Reason, Is.EqualTo("short_leg"));
            Assert.That(AutonomousRvrRoutePolicy.ChooseRoute(Vector3.Zero, new(9_000, 0, 0), RvrRouteVariant.Flank,
                null, new Random(3)).Reason, Is.EqualTo("no_nav"));
            Assert.That(AutonomousRvrRoutePolicy.ChooseRoute(Vector3.Zero, new(9_000, 0, 0), RvrRouteVariant.HubFan,
                new CountingProbe().Probe, new Random(3)).Reason, Is.EqualTo("no_hub"));
            Assert.That(AutonomousRvrRoutePolicy.ChooseRoute(Vector3.Zero, new(9_000, 0, 0), RvrRouteVariant.Flank,
                new CountingProbe().Probe, new Random(3)).Reason, Is.EqualTo("none"));
        });
    }

    private sealed class DetourNavigation : PathfindingMgrBase
    {
        public Vector3[] Corners = [];
        public override bool IsAvailable => true;
        public override bool HasNavmesh(Zone zone) => true;
        public override PathfindingResult GetPathStraight(Zone zone, Vector3 start, Vector3 end,
            EDtPolyFlags[] filters, Span<WrappedPathfindingNode> destination)
        {
            int n = 0;
            destination[n++] = new(start, 0);
            foreach (Vector3 corner in Corners) destination[n++] = new(corner, 0);
            destination[n++] = new(end, 0);
            return new(PathfindingStatus.PathFound, n);
        }
    }

    [Test]
    public void ViaPathMayWindButNotDetourFarAround()
    {
        var nav = new DetourNavigation();
        Vector3 to = new(4_000, 0, 0);
        Assert.That(AutonomousRvrTravel.HasPathWithin(nav, null, Vector3.Zero, to, 1.5f), Is.True, "straight");
        nav.Corners = [new(2_000, 1_500, 0)];
        Assert.That(AutonomousRvrTravel.HasPathWithin(nav, null, Vector3.Zero, to, 1.5f), Is.True, "over a hill: 5,000 of 6,300");
        nav.Corners = [new(0, 4_000, 0), new(4_000, 4_000, 0)];
        Assert.That(AutonomousRvrTravel.HasPathWithin(nav, null, Vector3.Zero, to, 1.5f), Is.False, "around a lake: 12,000");
    }
    [TestCase(20_000, TestName = "Road stays affordable after failed bends on a long leg")]
    [TestCase(6_000, TestName = "Road stays affordable after failed bends with a checked second leg")]
    public void RoadIsStillTriedAfterEveryBendFails(int legLength)
    {
        // Every bend is cut off; only the straight line connects.
        var probe = new CountingProbe { Corridor = (a, b) => Math.Abs(a.Y) < 1 && Math.Abs(b.Y) < 1 };
        RvrRouteChoice choice = AutonomousRvrRoutePolicy.ChooseRoute(Vector3.Zero, new(legLength, 0, 0), RvrRouteVariant.Flank,
            probe.Probe, new Random(6));
        Assert.Multiple(() =>
        {
            Assert.That(choice.Variant, Is.EqualTo(RvrRouteVariant.Road));
            Assert.That(choice.Waypoint, Is.EqualTo(new Vector3(2_400, 0, 0)));
            Assert.That(choice.Reason, Is.EqualTo("corridor_a"));
            Assert.That(probe.Floors + probe.Corridors, Is.LessThanOrEqualTo(AutonomousRvrRoutePolicy.QueryBudget));
        });
    }
}
