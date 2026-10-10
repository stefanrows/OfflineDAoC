using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
public class UT_AutonomousZoneCrossingSearch
{
    private const ushort World = 1;
    private const ushort Dungeon = 22;

    private static readonly DbZonePoint EntranceA = Edge(World, Dungeon, 10_000, 10_000, 1_000, 1_000);
    private static readonly DbZonePoint EntranceB = Edge(World, Dungeon, 60_000, 60_000, 50_000, 50_000);

    [Test]
    public void CellsBehindDifferentEntrancesResolveTheirOwnCrossing()
    {
        DbZonePoint[] edges = [EntranceA, EntranceB];

        // Cell near B that the catalog admits only through A (the far entrance),
        // and a cell admitted only through B. Planned in either order, each
        // cell gets its own entrance; nothing from the first cell leaks over.
        DbZonePoint onlyA = AutonomousZoneCrossingSearch.FindFirstCrossing(edges, World, Dungeon, 49_000, 49_000,
            point => point == EntranceA);
        DbZonePoint onlyB = AutonomousZoneCrossingSearch.FindFirstCrossing(edges, World, Dungeon, 2_000, 2_000,
            point => point == EntranceB);
        DbZonePoint unrestricted = AutonomousZoneCrossingSearch.FindFirstCrossing(edges, World, Dungeon, 49_000, 49_000,
            null);

        Assert.Multiple(() =>
        {
            Assert.That(onlyA, Is.SameAs(EntranceA));
            Assert.That(onlyB, Is.SameAs(EntranceB));
            Assert.That(unrestricted, Is.SameAs(EntranceB), "Nearest entrance to the goal when unrestricted");
        });
    }

    [Test]
    public void AnUnreachableCellDoesNotHideTheOthers()
    {
        DbZonePoint[] edges = [EntranceA, EntranceB];
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousZoneCrossingSearch.FindFirstCrossing(edges, World, Dungeon, 5_000, 5_000,
                _ => false), Is.Null);
            Assert.That(AutonomousZoneCrossingSearch.FindFirstCrossing(edges, World, Dungeon, 5_000, 5_000,
                point => point == EntranceA), Is.SameAs(EntranceA));
        });
    }

    [Test]
    public void MultiHopRouteReturnsTheFirstCrossing()
    {
        DbZonePoint toHub = Edge(World, 2, 100, 100, 200, 200);
        DbZonePoint hubToDungeon = Edge(2, Dungeon, 300, 300, 1_000, 1_000);
        Assert.That(AutonomousZoneCrossingSearch.FindFirstCrossing([hubToDungeon, toHub], World, Dungeon,
            1_000, 1_000, null), Is.SameAs(toHub));
    }

    [Test]
    public void QuarantinedDirectEdgeIsSkipped()
    {
        DbZonePoint[] edges = [EntranceA, EntranceB];
        Assert.That(AutonomousZoneCrossingSearch.FindFirstCrossing(edges, World, Dungeon, 1_000, 1_000, null,
            point => point == EntranceA, (10_000, 10_000)), Is.SameAs(EntranceB));
    }

    // Darkness Falls entrances as authored in the zonepoint table (one row per
    // edge; the realm rows of 81 and 84 share their coordinates).
    private const ushort DarknessFalls = AutonomousDarknessFallsPolicy.RegionId;
    private static readonly DbZonePoint AlbionDf81 = DfEdge(81, 1, 599716, 536189, 31211, 27924, 22893);
    private static readonly DbZonePoint AlbionDf79 = DfEdge(79, 1, 601798, 430727, 30550, 27898, 22893);
    private static readonly DbZonePoint MidgardDf84 = DfEdge(84, 100, 760580, 700467, 18798, 18667, 22892);
    private static readonly DbZonePoint HiberniaDf85 = DfEdge(85, 200, 400771, 465841, 46325, 40969, 21357);
    private static readonly DbZonePoint HiberniaDf86 = DfEdge(86, 200, 348776, 373379, 46325, 40969, 21357);
    private static readonly DbZonePoint HiberniaDf87 = DfEdge(87, 200, 325269, 433985, 46325, 40969, 21357);
    private static readonly DbZonePoint HiberniaToTirNaNog = DfEdge(26, 200, 313000, 470500, 34000, 32000, 8000, 201);
    private static readonly DbZonePoint[] DarknessFallsEdges =
        [AlbionDf81, AlbionDf79, MidgardDf84, HiberniaDf85, HiberniaDf86, HiberniaDf87, HiberniaToTirNaNog];
    private static readonly (int X, int Y) ConnachtRoad = (311960, 470002);

    // One audited room per entrance set (darkness_falls_navigation_points.json).
    private static readonly (int X, int Y) MidgardRoom = (13204, 24368);
    private static readonly (int X, int Y) AlbionRoom = (34734, 22213);
    private static readonly (int X, int Y) AlbionRoomVia81 = (29115, 17601);
    private static readonly (int X, int Y) HibernianRoom = (35585, 40045);

    [Test]
    public void FromHiberniaAnotherRealmsDarknessFallsRoomHasNoCrossingAndIsNotPlanned()
    {
        var searches = 0;
        var routes = new Dictionary<int, bool>();
        Assert.Multiple(() =>
        {
            foreach ((int x, int y) in new[] { AlbionRoom, AlbionRoomVia81, MidgardRoom })
            {
                Assert.That(FindDfCrossing(DarknessFallsEdges, 200, ConnachtRoad, x, y), Is.Null);
                Assert.That(PlanDfRoom(DarknessFallsEdges, 200, ConnachtRoad, x, y, routes, ref searches), Is.False,
                    $"room {x},{y} lies behind another realm's entrance");
            }
        });
    }

    [Test]
    public void FromHiberniaAHibernianRoomIsPlannedThroughAConnectedEntrance()
    {
        var searches = 0;
        DbZonePoint crossing = FindDfCrossing(DarknessFallsEdges, 200, ConnachtRoad, HibernianRoom.X, HibernianRoom.Y);
        Assert.Multiple(() =>
        {
            // Unfiltered, the search would take 87, the entrance nearest the road.
            Assert.That(AutonomousZoneCrossingSearch.FindFirstCrossing(DarknessFallsEdges, 200, DarknessFalls,
                HibernianRoom.X, HibernianRoom.Y, null, null, ConnachtRoad), Is.SameAs(HiberniaDf87));
            Assert.That(crossing, Is.SameAs(HiberniaDf85));
            Assert.That(PlanDfRoom(DarknessFallsEdges, 200, ConnachtRoad, HibernianRoom.X, HibernianRoom.Y,
                new Dictionary<int, bool>(), ref searches), Is.True);
        });
    }

    [Test]
    public void ARoomReachableOnlyThroughTheIsolatedEntranceIsSkipped()
    {
        var searches = 0;
        DbZonePoint[] onlyIsolated = [HiberniaDf87, HiberniaToTirNaNog];
        DbZonePoint moved87 = DfEdge(87, 200, 330000, 440000, 46325, 40969, 21357);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousDarknessFallsPolicy.IsIsolatedEntrance(HiberniaDf87), Is.True);
            Assert.That(new[] { AlbionDf81, AlbionDf79, MidgardDf84, HiberniaDf85, HiberniaDf86, HiberniaToTirNaNog, moved87 }
                .Where(AutonomousDarknessFallsPolicy.IsIsolatedEntrance), Is.Empty);
            Assert.That(PlanDfRoom(onlyIsolated, 200, ConnachtRoad, HibernianRoom.X, HibernianRoom.Y,
                new Dictionary<int, bool>(), ref searches), Is.False);
        });
    }

    [Test]
    public void EachRealmPlansOnlyItsOwnRoomsWithOneSearchPerEntranceSet()
    {
        AutonomousDungeonGoalCatalog.Point[] rooms = AutonomousDungeonGoalCatalog.VerifiedPointsForRegion(DarknessFalls);
        (int X, int Y)[] cells = rooms.Select(room => ((int)room.Position.X, (int)room.Position.Y)).Distinct().ToArray();
        int groups = cells.Select(cell => AutonomousDungeonGoalCatalog.EntranceGroup(DarknessFalls, cell.X, cell.Y))
            .Distinct().Count();
        Assert.Multiple(() =>
        {
            Assert.That(groups, Is.EqualTo(4), "Hibernian, Midgard, Albion 79+81 and Albion 81-only rooms");
            foreach ((ushort region, (int X, int Y) origin, (int X, int Y) own) in new[]
                     {
                         ((ushort)200, ConnachtRoad, HibernianRoom),
                         ((ushort)1, (599000, 536000), AlbionRoom),
                         ((ushort)100, (760000, 700000), MidgardRoom),
                     })
            {
                var searches = 0;
                var routes = new Dictionary<int, bool>();
                (int X, int Y)[] planned = cells.Where(cell =>
                    PlanDfRoom(DarknessFallsEdges, region, origin, cell.X, cell.Y, routes, ref searches)).ToArray();
                (int X, int Y)[] ownRooms = cells.Where(cell => FindDfCrossing(DarknessFallsEdges, region, origin,
                    cell.X, cell.Y) != null).ToArray();
                Assert.That(planned, Is.EquivalentTo(ownRooms), $"region {region}");
                Assert.That(planned, Does.Contain(own), $"region {region}");
                Assert.That(planned.Length, Is.LessThan(cells.Length), $"region {region}");
                Assert.That(searches, Is.EqualTo(groups), $"region {region}: one search per entrance set and pass");
            }
        });
    }

    [Test]
    public void OtherCampsAndBotsInsideDarknessFallsAreNotRouteChecked()
    {
        var routes = new Dictionary<int, bool>();
        double Fail() => throw new AssertionException("no route search expected");
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousDarknessFallsPolicy.HasCampRoute(200, 200, -1, routes, Fail), Is.True);
            Assert.That(AutonomousDarknessFallsPolicy.HasCampRoute(200, 1, -1, routes, Fail), Is.True);
            Assert.That(AutonomousDarknessFallsPolicy.HasCampRoute(DarknessFalls, DarknessFalls,
                AutonomousDungeonGoalCatalog.EntranceGroup(DarknessFalls, AlbionRoom.X, AlbionRoom.Y), routes, Fail), Is.True);
            Assert.That(AutonomousDarknessFallsPolicy.HasCampRoute(200, DarknessFalls, 0, routes,
                () => AutonomousDarknessFallsPolicy.NoRouteTravelMinutes), Is.False);
            Assert.That(AutonomousDungeonGoalCatalog.EntranceGroup(DarknessFalls, 1, 1), Is.EqualTo(-1));
        });
    }

    // Mirrors SelectCamp: the controller's CrossingEdges drops isolated
    // entrances, EstimateLocalSoloTravelMinutes returns 9999 without a crossing.
    private static bool PlanDfRoom(DbZonePoint[] edges, ushort region, (int X, int Y) origin, int x, int y,
        Dictionary<int, bool> routes, ref int searches)
    {
        int count = 0;
        bool planned = AutonomousDarknessFallsPolicy.HasCampRoute(region, DarknessFalls,
            AutonomousDungeonGoalCatalog.EntranceGroup(DarknessFalls, x, y), routes, () =>
            {
                count++;
                return FindDfCrossing(edges, region, origin, x, y) == null
                    ? AutonomousDarknessFallsPolicy.NoRouteTravelMinutes : 1;
            });
        searches += count;
        return planned;
    }

    private static DbZonePoint FindDfCrossing(DbZonePoint[] edges, ushort region, (int X, int Y) origin, int x, int y) =>
        AutonomousZoneCrossingSearch.FindFirstCrossing(
            edges.Where(edge => !AutonomousDarknessFallsPolicy.IsIsolatedEntrance(edge)).ToArray(),
            region, DarknessFalls, x, y,
            point => AutonomousDungeonGoalCatalog.CanUseEntrance(point, DarknessFalls, x, y), null, origin);

    private static DbZonePoint DfEdge(ushort id, ushort source, int sourceX, int sourceY, int targetX, int targetY,
        int targetZ, ushort target = DarknessFalls) =>
        new()
        {
            Id = id, SourceRegion = source, TargetRegion = target,
            SourceX = sourceX, SourceY = sourceY, TargetX = targetX, TargetY = targetY, TargetZ = targetZ,
        };

    private static DbZonePoint Edge(ushort source, ushort target, int sourceX, int sourceY, int targetX, int targetY) =>
        new()
        {
            SourceRegion = source, TargetRegion = target,
            SourceX = sourceX, SourceY = sourceY, TargetX = targetX, TargetY = targetY,
        };
}
