using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bug 153: zone-hop planning had no memory. A zone whose direct edge toward the goal
    /// is dead (the Sheeroe Hills cliff pocket) stepped into a neighbour; the neighbour's
    /// shortest route led back through that zone, so the bot walked the same two-zone loop
    /// and paid a full seam search on every hop (65 nav queries, 130 ms, per think).
    /// </summary>
    [TestFixture]
    public class UT_ZoneItineraryDeadEdges
    {
        // A(east, bottom) borders D(west, bottom) and B(top); B borders C; C borders D.
        // The A-D edge is dead. Zone order puts A before C in B's neighbour list, so B's
        // shortest route to D is B-A-D (a tie with B-C-D) unless A-D is known dead.
        private const ushort A = 1, B = 2, C = 3, D = 4;

        private sealed class Mesh : PathfindingMgrBase
        {
            public int SeamQueries;
            public bool KillAllOfA;
            public override bool IsAvailable => true;
            public override bool HasNavmesh(Zone zone) => true;
            public override PathfindingResult GetPathStraight(Zone zone, Vector3 start, Vector3 end,
                EDtPolyFlags[] filters, System.Span<WrappedPathfindingNode> destination)
            {
                SeamQueries++;
                destination[0] = new(end, EDtPolyFlags.Walk);
                return new(PathfindingStatus.PathFound, 1);
            }
            public override Vector3? GetClosestPoint(Zone zone, Vector3 position, float xRange, float yRange,
                float zRange, EDtPolyFlags[] filters)
            {
                if (xRange != 64) return position; // the planning floor correction, not a seam sample
                SeamQueries++;
                // Zone A's western strip (the seam toward D) is a disconnected pocket.
                if (zone.ID == A && (KillAllOfA || position.X < 65610)) return null;
                return position;
            }
        }

        private static (Region region, Dictionary<ushort, Zone> zones) World(params ushort[] ids)
        {
            var region = new Region(new RegionData { Id = 200, Name = "test", Description = "test", Mobs = [] });
            var all = new Dictionary<ushort, Zone>
            {
                [A] = Make(region, A, 65536, 0), [B] = Make(region, B, 65536, 65536),
                [C] = Make(region, C, 0, 65536), [D] = Make(region, D, 0, 0),
            };
            var zones = new Dictionary<ushort, Zone>();
            foreach (ushort id in ids) { zones[id] = all[id]; region.Zones.Add(all[id]); }
            return (region, zones);
        }

        private static Zone Make(Region region, ushort id, int x, int y) =>
            new(region, id, "test", x, y, 65536, 65536, id, false, 0, false, 0, 0, 0, 0, 0);

        private static Zone ZoneAt(Dictionary<ushort, Zone> zones, Vector3 p) =>
            zones.Values.First(z => p.X >= z.XOffset && p.X < z.XOffset + z.Width &&
                                    p.Y >= z.YOffset && p.Y < z.YOffset + z.Height);

        private static List<ushort> Walk(Region region, Dictionary<ushort, Zone> zones, Mesh mesh,
            ISet<AutonomousZoneItinerary.Edge> dead, int hops)
        {
            Vector3 goal = new(32768, 32768, 0), position = new(100000, 30000, 0);
            Zone here = zones[A];
            var visited = new List<ushort> { A };
            for (int hop = 0; hop < hops && here != zones[D]; hop++)
            {
                Assert.That(AutonomousZoneItinerary.TryNextStep(region, here, zones[D], position, goal, mesh,
                    out var step, null, dead), Is.True, $"hop {hop} from zone {here.ID}");
                position = step.Outside;
                here = ZoneAt(zones, position);
                visited.Add(here.ID);
            }
            return visited;
        }

        [Test]
        public void WithoutMemoryTheDeadEdgeSendsTheBotBackAndForth()
        {
            // The defect, pinned: this is the live loop (Sheeroe Hills <-> Bog of Cullen).
            var (region, zones) = World(A, B, C, D);
            var visited = Walk(region, zones, new Mesh(), null, 6);
            Assert.That(visited, Is.EqualTo(new ushort[] { A, B, A, B, A, B, A }));
        }

        [Test]
        public void DeadEdgeMemoryLetsTheBotWalkTheLongWayRoundOnce()
        {
            var (region, zones) = World(A, B, C, D);
            var visited = Walk(region, zones, new Mesh(), new HashSet<AutonomousZoneItinerary.Edge>(), 6);
            Assert.That(visited, Is.EqualTo(new ushort[] { A, B, C, D }));
        }

        [Test]
        public void DeadEdgeIsNotSearchedAgainAfterItFailed()
        {
            var (region, zones) = World(A, B, C, D);
            var dead = new HashSet<AutonomousZoneItinerary.Edge>();
            var mesh = new Mesh();
            Vector3 goal = new(32768, 32768, 0), start = new(100000, 30000, 0);
            Assert.That(AutonomousZoneItinerary.TryNextStep(region, zones[A], zones[D], start, goal, mesh,
                out _, null, dead), Is.True);
            Assert.That(dead.Count, Is.EqualTo(1));
            Assert.That(dead.Single().To.ID, Is.EqualTo(D));
            int first = mesh.SeamQueries;
            Assert.That(first, Is.GreaterThan(0));
            // The same think again (for example after the cached step was dropped): the dead
            // A-D edge costs nothing now, only the live A-B edge is resolved.
            mesh.SeamQueries = 0;
            Assert.That(AutonomousZoneItinerary.TryNextStep(region, zones[A], zones[D], start, goal, mesh,
                out _, null, dead), Is.True);
            Assert.That(mesh.SeamQueries, Is.LessThan(first));
        }

        [Test]
        public void ARouteWithNoLiveEdgeFailsOnceAndThenCostsNoNavQueries()
        {
            var (region, zones) = World(A, D);
            var dead = new HashSet<AutonomousZoneItinerary.Edge>();
            var mesh = new Mesh { KillAllOfA = true };
            Vector3 goal = new(32768, 32768, 0), start = new(100000, 30000, 0);
            Assert.That(AutonomousZoneItinerary.TryNextStep(region, zones[A], zones[D], start, goal, mesh,
                out _, null, dead), Is.False);
            Assert.That(mesh.SeamQueries, Is.GreaterThan(0));
            mesh.SeamQueries = 0;
            for (int think = 0; think < 20; think++)
                Assert.That(AutonomousZoneItinerary.TryNextStep(region, zones[A], zones[D], start, goal, mesh,
                    out _, null, dead), Is.False);
            Assert.That(mesh.SeamQueries, Is.Zero, "a failed step must back off, not repeat the full seam search");
        }
    }
}
