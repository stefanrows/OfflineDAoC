using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Port S3, task 143 (upstream 0.38c road fallback): a bot's partial path that dead-ends far short of its goal
    /// follows the proven zone road legs (AutonomousZoneRoads + Pathfinder.TryRoadPath); a first leg back through a
    /// door the bot just came out of is refused; no-path results, near-end partials, short walks, Darkness Falls,
    /// zones without road data and road data built for another mesh change nothing.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_ZoneRoadFallback
    {
        private static readonly Vector3 Start = new(1_000, 1_000, 0);
        private static readonly Vector3 Goal = new(9_000, 1_000, 0);
        private static readonly Vector3 AnchorA = new(1_000, 3_000, 0);
        private static readonly Vector3 AnchorB = new(9_000, 3_000, 0);
        private static readonly Vector3 DeadEnd = new(2_000, 1_000, 0);

        // Zone 7, two anchors of area 1 joined both ways (walked length 8,000), proven on mesh "aa".
        private const string Roads = "{\"version\":1,\"cell\":2048,\"meshes\":{\"7\":\"aa\"},\"zones\":{\"7\":{" +
            "\"anchors\":[[1000,3000,0,1],[9000,3000,0,1]],\"edges\":[[0,1,8000],[1,0,8000]]}}}";

        private static Zone NewZone(ushort id = 7, ushort region = 1)
        {
            var owner = (Region)RuntimeHelpers.GetUninitializedObject(typeof(Region));
            typeof(Region).GetField("m_regionData", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(owner, new RegionData { Id = region });
            typeof(Region).GetField("m_zones", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(owner, new List<Zone>());
            return new Zone(owner, id, $"zone{id}", 0, 0, 65_536, 65_536, id, false, 0, false, 0, 0, 0, 0, 0);
        }

        /// <summary>Straight two-node paths; the direct start-to-goal search dead-ends (partial, stops at DeadEnd).</summary>
        private sealed class FakeNav : IPathfindingMgr
        {
            public Func<Vector3, Vector3, bool> DoorOnLeg = (_, _) => false;
            public bool DirectNoPath;
            public int Queries;

            public PathfindingResult GetPathStraight(Zone zone, Vector3 start, Vector3 end, EDtPolyFlags[] filters,
                Span<WrappedPathfindingNode> destination)
            {
                Queries++;
                bool direct = Vector3.DistanceSquared(start, Start) < 1 && Vector3.DistanceSquared(end, Goal) < 1;
                if (direct)
                {
                    if (DirectNoPath) return new(PathfindingStatus.NoPathFound, 0);
                    destination[0] = new(start, 0);
                    destination[1] = new(DeadEnd, 0);
                    return new(PathfindingStatus.PartialPathFound, 2);
                }
                EDtPolyFlags flags = DoorOnLeg(start, end) ? EDtPolyFlags.AnyDoor : 0;
                destination[0] = new(start, 0);
                destination[1] = new(end, flags);
                return new(PathfindingStatus.PathFound, 2);
            }

            public bool Init() => true;
            public void Stop() { }
            public bool RegisterDoor(GameDoorBase door) => false;
            public bool UpdateDoorFlags(GameDoorBase door) => false;
            public Vector3? GetMoveAlongSurface(Zone zone, Vector3 start, Vector3 end, EDtPolyFlags[] filters) => end;
            public Vector3? GetRandomPoint(Zone zone, Vector3 position, float radius, EDtPolyFlags[] filters) => position;
            public Vector3? GetClosestPoint(Zone zone, Vector3 position, EDtPolyFlags[] filters) => position;
            public Vector3? GetClosestPoint(Zone zone, Vector3 position, float xRange, float yRange, float zRange, EDtPolyFlags[] filters) => position;
            public Vector3? GetClosestPointInBounds(Zone zone, Vector3 origin, Vector3 minOffset, Vector3 maxOffset, EDtPolyFlags[] filters) => origin;
            public Vector3? GetRoofAbove(Zone zone, Vector3 position, float maxHeight, EDtPolyFlags[] filters) => null;
            public Vector3? GetFloorBeneath(Zone zone, Vector3 position, float maxDepth, EDtPolyFlags[] filters) => position;
            public bool TrySnapToMesh(Zone zone, ref Vector3 position, float range) => true;
            public bool HasLineOfSight(Zone zone, Vector3 position, Vector3 target, EDtPolyFlags[] filters) => true;
            public bool HasNavmesh(Zone zone) => true;
            public bool IsAvailable => true;
            public EDtPolyFlags[] DefaultFilters { get; } = [EDtPolyFlags.AnyDoor, 0];
            public EDtPolyFlags[] BlockingDoorAvoidanceFilters { get; } = [EDtPolyFlags.AnyDoor, 0];
        }

        private static bool Fallback(FakeNav nav, Zone zone, out PathfindingResult result, out WrappedPathfindingNode[] nodes)
        {
            nodes = new WrappedPathfindingNode[512];
            PathfindingResult direct = nav.GetPathStraight(zone, Start, Goal, nav.DefaultFilters, nodes);
            return Pathfinder.TryRoadPath(nav, zone, Start, Goal, nav.DefaultFilters, direct, nodes, 512, out result);
        }

        [Test]
        public void ADeadEndOnALongWalkFollowsTheProvenRoad()
        {
            using var roads = AutonomousZoneRoads.UseForTests(Roads);
            Assert.That(Fallback(new FakeNav(), NewZone(), out PathfindingResult result, out WrappedPathfindingNode[] nodes), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(PathfindingStatus.PathFound), "start, both anchors and the goal form one complete path");
                Assert.That(result.NodeCount, Is.EqualTo(4));
                Assert.That(nodes[1].Position, Is.EqualTo(AnchorA));
                Assert.That(nodes[2].Position, Is.EqualTo(AnchorB));
                Assert.That(nodes[3].Position, Is.EqualTo(Goal));
            });
        }

        [Test]
        public void AFirstLegBackThroughADoorTheBotJustLeftIsRefused()
        {
            using var roads = AutonomousZoneRoads.UseForTests(Roads);
            // Anchor A is no nearer the goal than the start and the way there crosses a door: the way the bot came out.
            var nav = new FakeNav { DoorOnLeg = (from, to) => Vector3.DistanceSquared(from, Start) < 1 && Vector3.DistanceSquared(to, AnchorA) < 1 };
            var zone = NewZone();
            Assert.That(AutonomousZoneRoads.TryRoute(nav, zone, Start, Goal, out _), Is.False);
            Assert.That(Fallback(nav, zone, out PathfindingResult result, out _), Is.False);
            Assert.That(result.Status, Is.EqualTo(PathfindingStatus.PartialPathFound), "the direct partial stays as it was");
        }

        [Test]
        public void NoPathNearEndPartialsAndShortWalksAreUntouched()
        {
            using var roads = AutonomousZoneRoads.UseForTests(Roads);
            var zone = NewZone();
            var nav = new FakeNav { DirectNoPath = true };
            Assert.That(Fallback(nav, zone, out PathfindingResult none, out _), Is.False);
            Assert.That(none.Status, Is.EqualTo(PathfindingStatus.NoPathFound));

            var nodes = new WrappedPathfindingNode[512];
            nodes[0] = new(Start, 0);
            nodes[1] = new(Goal - new Vector3(200, 0, 0), 0);
            Assert.That(Pathfinder.TryRoadPath(new FakeNav(), zone, Start, Goal, [], new(PathfindingStatus.PartialPathFound, 2), nodes, 512, out _),
                Is.False, "a partial that stops within 256 of the goal is kept");

            Assert.That(AutonomousZoneRoads.TryRoute(new FakeNav(), zone, Start, Start + new Vector3(900, 0, 0), out _), Is.False,
                "walks under 1,024 units never use the roads");
        }

        [Test]
        public void DarknessFallsKeepsItsOwnNavigation()
        {
            using var roads = AutonomousZoneRoads.UseForTests(Roads);
            Assert.That(Fallback(new FakeNav(), NewZone(7, AutonomousDarknessFallsPolicy.RegionId), out _, out _), Is.False);
        }

        [Test]
        public void WithoutRoadDataEverythingBehavesAsBefore()
        {
            using var roads = AutonomousZoneRoads.UseForTests(null);
            var nav = new FakeNav();
            Assert.That(AutonomousZoneRoads.Covers(NewZone()), Is.False);
            Assert.That(Fallback(nav, NewZone(), out PathfindingResult result, out _), Is.False);
            Assert.That(result.Status, Is.EqualTo(PathfindingStatus.PartialPathFound));
            Assert.That(nav.Queries, Is.EqualTo(1), "no extra navmesh query without road data");
        }

        [Test]
        public void RoadDataBuiltForAnotherMeshIsIgnored()
        {
            using (AutonomousZoneRoads.UseForTests(Roads, zone => "bb"))
            {
                Assert.That(AutonomousZoneRoads.Covers(NewZone()), Is.False, "the installed mesh differs from the proven one");
                Assert.That(Fallback(new FakeNav(), NewZone(), out _, out _), Is.False);
            }
            using (AutonomousZoneRoads.UseForTests(Roads, zone => "AA"))
                Assert.That(AutonomousZoneRoads.Covers(NewZone()), Is.True, "the hash compare ignores case");
            using (AutonomousZoneRoads.UseForTests(Roads, zone => null))
                Assert.That(AutonomousZoneRoads.Covers(NewZone()), Is.False, "a missing mesh drops its zone");
        }

        [Test]
        public void TheShippedRoadDataIsEmbeddedAndCoversTheRealmZones()
        {
            string name = Array.Find(typeof(AutonomousZoneRoads).Assembly.GetManifestResourceNames(),
                resource => resource.EndsWith(AutonomousZoneRoads.EmbeddedResourceName, StringComparison.Ordinal));
            Assert.That(name, Is.Not.Null, "GameServer.dll carries zone_roads.json");
            using var stream = typeof(AutonomousZoneRoads).Assembly.GetManifestResourceStream(name);
            using var document = System.Text.Json.JsonDocument.Parse(stream);
            var zones = document.RootElement.GetProperty("zones");
            var meshes = document.RootElement.GetProperty("meshes");
            Assert.Multiple(() =>
            {
                // Old Frontiers hotspots of task 143: zone 214 (Hibernia) and zone 115 (Midgard).
                Assert.That(zones.TryGetProperty("214", out _), Is.True);
                Assert.That(zones.TryGetProperty("115", out _), Is.True);
                foreach (var zone in zones.EnumerateObject())
                    Assert.That(meshes.TryGetProperty(zone.Name, out var hash) && hash.GetString()?.Length == 64, Is.True,
                        $"zone {zone.Name} names the mesh it was proven on");
            });
        }
    }
}
