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

    // The shared frontier dungeon complex as authored in the zonepoint table:
    // 1 - 277 (Hall of the Corrupt) - 248 - 276 (Marfach) - 200, 100 - 246
    // (Dodens) - 248. It links all three realms' Darkness Falls entrances.
    private static readonly DbZonePoint MidgardToDodens = DfEdge(93, 100, 718392, 654864, 32259, 30624, 16045, 246);
    private static readonly DbZonePoint[] FrontierDungeonEdges =
    [
        DfEdge(88, 1, 598581, 361828, 32395, 31847, 16158, 277),
        DfEdge(89, 277, 31870, 31072, 598397, 361947, 3600, 1),
        DfEdge(90, 277, 26191, 37697, 37690, 29013, 16512, 248),
        DfEdge(98, 248, 37591, 29010, 26428, 37869, 13478, 277),
        MidgardToDodens,
        DfEdge(91, 246, 33914, 30693, 718069, 654836, 6365, 100),
        DfEdge(92, 246, 46897, 31390, 24912, 30905, 16511, 248),
        DfEdge(99, 248, 24908, 31015, 47362, 31508, 13303, 246),
        DfEdge(94, 200, 445881, 392547, 32915, 29238, 16268, 276),
        DfEdge(95, 276, 32878, 28267, 445571, 392603, 6365, 200),
        DfEdge(96, 276, 32127, 44077, 41144, 44332, 16512, 248),
        DfEdge(97, 248, 41143, 44240, 32125, 43898, 14725, 276),
    ];
    private static readonly DbZonePoint[] WorldEdges = [.. DarknessFallsEdges, .. FrontierDungeonEdges];
    private static readonly (int X, int Y) MidgardRoad = (756903, 898252);
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

    // Live 0.240.0 (bug 145): a Midgard Skald in region 100 drew the
    // Hibernian-only room "dungeon-live:249:(36, 55, 99):cursed necyomancer"
    // and failed with "No legal region route" in the same tick. Planning
    // searched the raw edges and found the road through the shared frontier
    // dungeons; the route itself never takes it while a way around exists.
    [Test]
    public void APveRouteNeverReachesAnotherRealmsRoomThroughTheFrontierDungeons()
    {
        (int X, int Y) hibernianLiveRoom = (32912, 49633);
        DbZonePoint planned040 = AutonomousZoneCrossingSearch.FindFirstCrossing(WorldEdges, 100, DarknessFalls,
            hibernianLiveRoom.X, hibernianLiveRoom.Y,
            point => AutonomousDungeonGoalCatalog.CanUseEntrance(point, DarknessFalls, hibernianLiveRoom.X, hibernianLiveRoom.Y),
            null, MidgardRoad);
        DbZonePoint[] routeEdges = AutonomousZoneCrossingSearch.WithoutFrontierDungeonRoads(WorldEdges, 100, DarknessFalls);
        Assert.Multiple(() =>
        {
            Assert.That(planned040, Is.SameAs(MidgardToDodens), "0.240.0 planning: road through Dodens to Hibernia");
            Assert.That(routeEdges, Does.Not.Contain(MidgardToDodens));
            Assert.That(routeEdges, Does.Contain(MidgardDf84));
            Assert.That(FindDfCrossing(WorldEdges, 100, MidgardRoad, hibernianLiveRoom.X, hibernianLiveRoom.Y), Is.Null);
            // Inside the complex, or with a goal there, the road stays usable.
            Assert.That(AutonomousZoneCrossingSearch.WithoutFrontierDungeonRoads(WorldEdges, 246, DarknessFalls),
                Is.SameAs(WorldEdges));
            Assert.That(AutonomousZoneCrossingSearch.WithoutFrontierDungeonRoads(WorldEdges, 100, 248),
                Is.SameAs(WorldEdges));
            // Without a way around, the road is the only route and is kept.
            Assert.That(AutonomousZoneCrossingSearch.WithoutFrontierDungeonRoads(FrontierDungeonEdges, 100, 200),
                Is.SameAs(FrontierDungeonEdges));
        });
    }

    [Test]
    public void AMidgardBotPlansOnlyMidgardRoomsAndEveryRealmMatchesItsRoute()
    {
        AutonomousDungeonGoalCatalog.Point[] rooms = AutonomousDungeonGoalCatalog.VerifiedPointsForRegion(DarknessFalls);
        (int X, int Y)[] cells = rooms.Select(room => ((int)room.Position.X, (int)room.Position.Y)).Distinct().ToArray();
        int groups = cells.Select(cell => AutonomousDungeonGoalCatalog.EntranceGroup(DarknessFalls, cell.X, cell.Y))
            .Distinct().Count();
        // Live-discovered camps from the failing run, keyed by their audited cell.
        (int X, int Y)[] hibernianOnly = [(27296, 40685), (32912, 49633), (37360, 49501)];
        (int X, int Y)[] midgardOnly = [(18896, 26792), (16079, 21782)];
        Assert.Multiple(() =>
        {
            var midgardSearches = 0;
            var midgardRoutes = new Dictionary<int, bool>();
            foreach ((int x, int y) in hibernianOnly)
                Assert.That(PlanDfRoom(WorldEdges, 100, MidgardRoad, x, y, midgardRoutes, ref midgardSearches), Is.False,
                    $"Hibernian-only room {x},{y} from Midgard");
            foreach ((int x, int y) in midgardOnly)
                Assert.That(PlanDfRoom(WorldEdges, 100, MidgardRoad, x, y, midgardRoutes, ref midgardSearches), Is.True,
                    $"Midgard room {x},{y} from Midgard");
            Assert.That(PlanDfRoom(WorldEdges, 100, MidgardRoad, AlbionRoom.X, AlbionRoom.Y, midgardRoutes,
                ref midgardSearches), Is.False, "Albion room from Midgard");

            foreach ((ushort region, (int X, int Y) origin, (int X, int Y) own) in new[]
                     {
                         ((ushort)200, ConnachtRoad, HibernianRoom),
                         ((ushort)1, (599000, 536000), AlbionRoom),
                         ((ushort)100, MidgardRoad, MidgardRoom),
                     })
            {
                var searches = 0;
                var routes = new Dictionary<int, bool>();
                (int X, int Y)[] planned = cells.Where(cell =>
                    PlanDfRoom(WorldEdges, region, origin, cell.X, cell.Y, routes, ref searches)).ToArray();
                (int X, int Y)[] routed = cells.Where(cell => FindDfCrossing(WorldEdges, region, origin,
                    cell.X, cell.Y) != null).ToArray();
                Assert.That(planned, Is.EquivalentTo(routed), $"region {region}");
                Assert.That(planned, Does.Contain(own), $"region {region}");
                Assert.That(planned.All(cell => FindDfCrossing(DarknessFallsEdges, region, origin, cell.X, cell.Y) != null),
                    Is.True, $"region {region}: only rooms behind the realm's own entrance");
                Assert.That(searches, Is.EqualTo(groups), $"region {region}: one search per entrance set and pass");
            }
        });
    }

    // Mirrors SelectCamp: the controller's CrossingEdges drops isolated
    // entrances, its PvE route edges drop the frontier dungeon road when a way
    // around exists, and EstimateLocalSoloTravelMinutes returns 9999 without a
    // crossing.
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
            AutonomousZoneCrossingSearch.WithoutFrontierDungeonRoads(
                edges.Where(edge => !AutonomousDarknessFallsPolicy.IsIsolatedEntrance(edge)).ToArray(),
                region, DarknessFalls),
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
