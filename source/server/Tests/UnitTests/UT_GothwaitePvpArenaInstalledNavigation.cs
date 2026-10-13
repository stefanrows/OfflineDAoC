using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// Read-only proof of the Gothwaite PvP arena patrol points on the installed navmesh. Run with OFFLINE_DAOC_NAV_ROOT
/// (the installed runtime/server folder) and optionally OFFLINE_DAOC_WORLD_DB (a read-only world database copy).
/// Each point snaps to the floor with a tight, then tall, then wide search. A route from the castle landing is
/// chained exactly as AutonomousBattlegroundDriver.PathExists does (QueryLeg legs, MaxRouteLegs, blocking-door
/// filters first). The landings and the castle exit are required; candidates only report, and a failing candidate
/// gets a GOTH_NUDGE search of +-300..800 units for hand-applied moves.
/// </summary>
[Explicit("Read-only installed Gothwaite arena probe"), NonParallelizable]
public class UT_GothwaitePvpArenaInstalledNavigation
{
    private const int RegionId = 51;
    private const double CastleSafeRadius = 4000;
    private const double SpreadFloor = 4500;
    private const double LowBandLimit = 20000;
    private const double ArenaLimit = 30000;
    private static readonly Vector3 CastleCentre = new(534958, 548813, 0);
    private static readonly Vector3 Landing1 = new(535000, 548790, 4800);
    private static readonly Vector3 Landing2 = new(535158, 548589, 4800);
    private static readonly Vector3 CastleExit = new(535020, 544020, 4800);

    // name, x, y, z (estimate, replaced by the snapped floor), and whether the arena audit allowed the low band.
    private static readonly (string Name, int X, int Y, int Z, bool LowBandOk)[] Candidates =
    {
        ("GP01", 532069, 545279, 4800, true),
        ("GP02", 535020, 544020, 4800, true),
        ("GP03", 540020, 544020, 3116, true),
        ("GP04", 528538, 542032, 3168, true),
        ("GP05", 544020, 546020, 3116, true),
        ("GP06", 543020, 542020, 3116, true),
        ("GP07", 525020, 545020, 3168, true),
        ("GP08", 526020, 539020, 3142, true),
        ("GP09", 540020, 536020, 3793, true),
        ("GP10", 522020, 544020, 3230, true),
        ("GP11", 527651, 536998, 3137, true),
        ("GP12", 548020, 544020, 3248, true),
        ("GP13", 525020, 537020, 3100, true),
        ("GP14", 520020, 542020, 3230, true),
        ("GP15", 539020, 532020, 3772, true),
        ("GP16", 526020, 534020, 2953, true),
        ("GP17", 524020, 533020, 2881, true),
        ("GP18", 541020, 530020, 3766, true),
        ("GP19", 526122, 541138, 3168, false),
        ("GP20", 524020, 542020, 3230, false),
        ("GP21", 522020, 532020, 3281, false),
        ("GP22", 550020, 531020, 3168, false),
        ("GP23", 519020, 531020, 3320, false),
        ("GP24", 511020, 548020, 3168, false),
        ("GP25", 511020, 550020, 3168, false),
        ("GP26", 514020, 537020, 3274, false),
        ("GP27", 512020, 538020, 3270, false),
        ("GP28", 514020, 533020, 3301, false),
        ("GP29", 512020, 536020, 3272, false),
        ("GP30", 537990, 522470, 3251, false),
    };

    private sealed record ProbeResult(string Name, Vector3 Raw, Vector3? Snapped, string Snap, string Route, int Legs, double Distance)
    {
        public bool Ok => Snapped.HasValue && Route == "ok";
    }

    [Test]
    public void ProbeArenaFromCastleLanding()
    {
        string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
        Assert.That(root, Is.Not.Null.And.Not.Empty, "Set OFFLINE_DAOC_NAV_ROOT to the installed runtime/server folder.");
        string previous = Environment.CurrentDirectory;
        var loaded = new List<Zone>();
        try
        {
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(LocalPathfindingMgr).Assembly,
                    (name, asm, search) => name == "lib/Detour" ? NativeLibrary.Load(Path.Combine(root, "lib", "Detour.dll")) : IntPtr.Zero);
            }
            catch (InvalidOperationException)
            {
                // An earlier navigation probe in this process already installed the resolver for the same root.
            }
            Environment.CurrentDirectory = root;
            string worldDb = Environment.GetEnvironmentVariable("OFFLINE_DAOC_WORLD_DB");
            if (string.IsNullOrWhiteSpace(worldDb)) worldDb = Path.GetFullPath("../data/opendaoc.sqlite3.db");
            TestContext.WriteLine($"GOTH_NAV_SETUP root={root} worldDb={worldDb}");
            // Read-only open. Point OFFLINE_DAOC_WORLD_DB at a Windows-local copy: SQLite cannot open \\wsl.localhost paths.
            using var db = new SQLiteConnection($"Data Source={worldDb};Read Only=True;Pooling=False;");
            db.Open();
            IPathfindingMgr nav = PathfindingProvider.LocalPathfindingMgr;
            var region = (Region)typeof(UT_AuditedDungeonInstalledMesh)
                .GetMethod("BuildRegion", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { db, RegionId, loaded });
            MethodInfo queryLeg = typeof(AutonomousBattlegroundDriver).GetMethod("QueryLeg", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(queryLeg, Is.Not.Null, "AutonomousBattlegroundDriver.QueryLeg must exist");
            var nodes = new WrappedPathfindingNode[512];
            var failures = new List<string>();

            Zone landingZone = region.GetZone((int)Landing1.X, (int)Landing1.Y);
            Assert.That(landingZone, Is.Not.Null, "The castle landing must lie in a region 51 zone");
            Assert.That(nav.HasNavmesh(landingZone), Is.True, "The landing zone navmesh must be loaded");

            var (origin, _) = Snap(nav, landingZone, Landing1);
            ProbeResult landing1 = ProbePoint(nav, region, landingZone, origin, queryLeg, nodes, "LANDING1", Landing1);
            ProbeResult landing2 = ProbePoint(nav, region, landingZone, origin, queryLeg, nodes, "LANDING2", Landing2);
            ProbeResult exit = ProbePoint(nav, region, landingZone, origin, queryLeg, nodes, "CASTLE_EXIT", CastleExit);
            foreach (ProbeResult mandatory in new[] { landing1, landing2, exit })
            {
                if (!mandatory.Snapped.HasValue) failures.Add($"{mandatory.Name} did not snap to the floor");
                if (mandatory.Route != "ok") failures.Add($"{mandatory.Name} route={mandatory.Route}");
            }

            var candidates = new List<ProbeResult>();
            foreach (var candidate in Candidates)
            {
                var raw = new Vector3(candidate.X, candidate.Y, candidate.Z);
                ProbeResult probe = ProbePoint(nav, region, landingZone, origin, queryLeg, nodes, candidate.Name, raw);
                candidates.Add(probe);
                if (probe.Ok || !origin.HasValue) continue;
                var nudged = Nudge(nav, region, landingZone, origin.Value, queryLeg, nodes, candidate.Name, raw);
                if (nudged is { } hit)
                    TestContext.WriteLine($"GOTH_NUDGE name={candidate.Name} offset={F0(hit.Offset.X)},{F0(hit.Offset.Y)} snapped={Coords(hit.Probe.Snapped)} snap={hit.Probe.Snap} route={hit.Probe.Route} legs={hit.Probe.Legs} distanceFromCastle={F0(hit.Probe.Distance)}");
                else
                    TestContext.WriteLine($"GOTH_NUDGE name={candidate.Name} result=none");
            }

            // The arena's own data, read from code: every castle approach and every patrol point must snap and route from the castle landing.
            GothwaitePvpArena arena = GothwaitePvpArena.Gothwaite;
            Assert.That(arena.CastleApproaches, Is.Not.Empty, "The arena must define castle approaches");
            Assert.That(arena.PatrolPoints, Is.Not.Empty, "The arena must define patrol points");
            int arenaProbes = 0, arenaFailures = 0;
            foreach (GothwaitePvpSpot spot in arena.CastleApproaches.Concat(arena.PatrolPoints))
            {
                ProbeResult probe = ProbePoint(nav, region, landingZone, origin, queryLeg, nodes, spot.Name, new Vector3(spot.X, spot.Y, spot.Z));
                arenaProbes++;
                if (probe.Ok)
                    continue;
                arenaFailures++;
                failures.Add($"{spot.Name} snap={probe.Snap} route={probe.Route}");
            }
            TestContext.WriteLine($"GOTH_NAV_ARENA points={arenaProbes} failed={arenaFailures}");

            int within = candidates.Count(p => p.Ok && p.Distance > SpreadFloor && p.Distance <= LowBandLimit);
            int between = candidates.Count(p => p.Ok && p.Distance > LowBandLimit && p.Distance <= ArenaLimit);
            TestContext.WriteLine($"GOTH_NAV_SUMMARY candidates={candidates.Count} routeOkWithin20000={within} routeOkBetween20000And30000={between}");
            Assert.That(failures, Is.Empty, "Required points failed: " + string.Join("; ", failures));
        }
        finally
        {
            foreach (Zone zone in loaded) LocalPathfindingMgr.UnloadNavMesh(zone);
            Environment.CurrentDirectory = previous;
        }
    }

    private static ProbeResult ProbePoint(IPathfindingMgr nav, Region region, Zone landingZone, Vector3? origin,
        MethodInfo queryLeg, WrappedPathfindingNode[] nodes, string name, Vector3 raw)
    {
        Zone zone = region.GetZone((int)raw.X, (int)raw.Y);
        Vector3? snapped = null;
        string snap = "none";
        string route = "no_zone";
        int legs = 0;
        if (zone != null)
        {
            (snapped, snap) = Snap(nav, zone, raw);
            route = "no_snap";
            if (snapped.HasValue)
            {
                if (zone != landingZone) route = "other_zone";
                else if (origin == null) route = "no_origin";
                else
                {
                    (route, legs) = RouteReason(nav, zone, queryLeg, nodes, origin.Value, snapped.Value);
                }
            }
        }
        double distance = Distance2D(snapped ?? raw, CastleCentre);
        bool inCastle = distance <= CastleSafeRadius;
        TestContext.WriteLine($"GOTH_NAV name={name} snapped={Coords(snapped)} snap={snap} route={route} legs={legs} distanceFromCastle={F0(distance)} inCastle={(inCastle ? "true" : "false")}");
        return new ProbeResult(name, raw, snapped, snap, route, legs, distance);
    }

    // Floor search order from the plan: tight vertical first, then tall up to 4096, then a wider horizontal search.
    private static (Vector3? Point, string Mode) Snap(IPathfindingMgr nav, Zone zone, Vector3 raw)
    {
        EDtPolyFlags[] filters = nav.DefaultFilters;
        Vector3? point = nav.GetClosestPoint(zone, raw, 48, 48, 96, filters);
        if (point.HasValue) return (point, "tight");
        point = nav.GetClosestPoint(zone, raw, 48, 48, 4096, filters);
        if (point.HasValue) return (point, "tall");
        point = nav.GetClosestPoint(zone, raw, 768, 768, 768, filters);
        if (point.HasValue) return (point, "wide");
        return (null, "none");
    }

    // Mirrors AutonomousBattlegroundDriver.PathExists: blocking-door filters first. A route that exists only through
    // door polygons is reported as closed_gate; the driver then decides from the bot's own door rule.
    private static (string Route, int Legs) RouteReason(IPathfindingMgr nav, Zone zone, MethodInfo queryLeg,
        WrappedPathfindingNode[] nodes, Vector3 from, Vector3 goal)
    {
        if (Vector3.Distance(from, goal) < 1) return ("ok", 0);
        var strict = Chain(nav, zone, queryLeg, nodes, from, goal, nav.BlockingDoorAvoidanceFilters);
        if (strict.Outcome == AutonomousBattlegroundDriver.RouteChain.Reached) return ("ok", strict.Legs);
        var open = Chain(nav, zone, queryLeg, nodes, from, goal, nav.DefaultFilters);
        if (open.Outcome == AutonomousBattlegroundDriver.RouteChain.Reached) return ("closed_gate", open.Legs);
        string reason = strict.Outcome switch
        {
            AutonomousBattlegroundDriver.RouteChain.NoPath => "no_path",
            AutonomousBattlegroundDriver.RouteChain.DeadEnd => "dead_end",
            AutonomousBattlegroundDriver.RouteChain.Capped => "corridor_cap",
            _ => "no_route",
        };
        return (reason, strict.Legs);
    }

    private static (AutonomousBattlegroundDriver.RouteChain Outcome, int Legs) Chain(IPathfindingMgr nav, Zone zone,
        MethodInfo queryLeg, WrappedPathfindingNode[] nodes, Vector3 from, Vector3 goal, EDtPolyFlags[] filters)
    {
        int legs = 0;
        AutonomousBattlegroundDriver.RouteChain outcome = AutonomousBattlegroundDriver.ChainRoute(from, goal,
            AutonomousBattlegroundDriver.MaxRouteLegs,
            start =>
            {
                legs++;
                return (AutonomousBattlegroundDriver.RouteLeg)queryLeg.Invoke(null, new object[] { nav, zone, start, goal, filters, nodes });
            }, out _);
        return (outcome, legs);
    }

    private static (ProbeResult Probe, Vector2 Offset)? Nudge(IPathfindingMgr nav, Region region, Zone landingZone, Vector3 origin,
        MethodInfo queryLeg, WrappedPathfindingNode[] nodes, string name, Vector3 raw)
    {
        bool lowBand = Distance2D(raw, CastleCentre) <= LowBandLimit;
        for (int radius = 300; radius <= 800; radius += 100)
        {
            for (int step = 0; step < 16; step++)
            {
                double angle = step * Math.PI / 8;
                var offset = new Vector2((float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)));
                var moved = new Vector3(raw.X + offset.X, raw.Y + offset.Y, raw.Z);
                Zone zone = region.GetZone((int)moved.X, (int)moved.Y);
                if (zone != landingZone) continue;
                var (point, snap) = Snap(nav, zone, moved);
                if (!point.HasValue) continue;
                double distance = Distance2D(point.Value, CastleCentre);
                if (distance <= SpreadFloor || (distance <= LowBandLimit) != lowBand) continue;
                var (route, legs) = RouteReason(nav, zone, queryLeg, nodes, origin, point.Value);
                if (route != "ok") continue;
                return (new ProbeResult(name, moved, point, snap, route, legs, distance), offset);
            }
        }
        return null;
    }

    private static double Distance2D(Vector3 a, Vector3 b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static string F(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);
    private static string F0(double value) => value.ToString("0", CultureInfo.InvariantCulture);
    private static string Coords(Vector3? point) => point is { } p ? $"{F(p.X)},{F(p.Y)},{F(p.Z)}" : "none";
}
