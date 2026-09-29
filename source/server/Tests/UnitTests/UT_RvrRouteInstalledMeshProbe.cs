using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Opt-in evidence probe for the RvR route variants (wave 1b): runs
    /// ChooseRoute against the installed Midgard frontier navmeshes around
    /// Svasud Faste and Odin's Gate and prints the fallback share per variant.
    /// Loads only local navmeshes and opens the database read-only.
    /// </summary>
    [TestFixture, NonParallelizable, Explicit("Read-only installed-navmesh route probe")]
    public class UT_RvrRouteInstalledMeshProbe
    {
        [Test]
        public void RouteVariantsAroundSvasudAndOdinsGate()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            Assert.That(root, Is.Not.Null.And.Not.Empty);
            string previous = Environment.CurrentDirectory;
            try
            {
                string native = Environment.GetEnvironmentVariable("OFFLINE_DAOC_TEST_DETOUR");
                if (!string.IsNullOrEmpty(native))
                    NativeLibrary.SetDllImportResolver(typeof(LocalPathfindingMgr).Assembly,
                        (name, assembly, search) => name == "lib/Detour" ? NativeLibrary.Load(native) : IntPtr.Zero);
                Environment.CurrentDirectory = root;
                string databasePath = Environment.GetEnvironmentVariable("OFFLINE_DAOC_DB_PATH") ??
                    Path.GetFullPath(Path.Combine(root, "..", "data", "opendaoc.sqlite3.db"));
                using var db = new SQLiteConnection($"Data Source={databasePath};Read Only=True;Pooling=False;");
                db.Open();
                var region = (Region)RuntimeHelpers.GetUninitializedObject(typeof(Region));
                typeof(Region).GetField("m_regionData", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(region, new RegionData { Id = 100 });
                var zones = new List<Zone>();
                typeof(Region).GetField("m_zones", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(region, zones);
                using (var command = db.CreateCommand())
                {
                    command.CommandText = "select ZoneID,Name,OffsetX,OffsetY,Width,Height from Zones where RegionID=100";
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        ushort id = (ushort)reader.GetInt32(0);
                        Zone zone = new(region, id, reader.GetString(1), reader.GetInt32(2) * 8192,
                            reader.GetInt32(3) * 8192, reader.GetInt32(4) * 8192, reader.GetInt32(5) * 8192,
                            id, false, 0, false, 0, 0, 0, 0, 0);
                        zones.Add(zone);
                        LocalPathfindingMgr.LoadNavMesh(zone);
                    }
                }
                var nav = PathfindingProvider.LocalPathfindingMgr;

                Vector3 Ground(float x, float y)
                {
                    Zone zone = region.GetZone((int)x, (int)y);
                    return nav.GetClosestPoint(zone, new(x, y, 5000), 256, 256, 6000, nav.DefaultFilters)
                        ?? throw new InvalidOperationException($"no floor near {x},{y}");
                }

                var starts = new (string Name, Vector3 At)[]
                {
                    ("svasud-centre", Ground(766235, 669173)),
                    ("svasud-south-edge", Ground(765000, 664000)),
                    ("uppland-mid", Ground(750000, 640000)),
                    ("odin-gate", Ground(598000, 628000)),
                };
                var goals = new (string Name, Vector3 At)[]
                {
                    ("odin-gate", Ground(598000, 628000)),
                    ("uppland-sw", Ground(730000, 620000)),
                    ("uppland-e", Ground(780000, 640000)),
                    ("jamtland", Ground(670000, 640000)),
                    ("near-3k", default),
                };
                var tally = new Dictionary<string, int>();
                int cases = 0;
                foreach ((string startName, Vector3 start) in starts)
                foreach ((string goalName, Vector3 goalAt) in goals)
                {
                    Vector3 goal = goalName == "near-3k" ? Ground(start.X - 2_000, start.Y - 2_000) : goalAt;
                    if (Vector2.Distance(new(start.X, start.Y), new(goal.X, goal.Y)) < 1) continue;
                    Zone zone = region.GetZone((int)start.X, (int)start.Y);
                    RvrRouteProbe probe = AutonomousRvrTravel.Probe(nav, region, zone, goal);
                    foreach (RvrRouteVariant variant in new[] { RvrRouteVariant.Road, RvrRouteVariant.Flank,
                                 RvrRouteVariant.Cover, RvrRouteVariant.HubFan })
                    for (int seed = 0; seed < 10; seed++)
                    {
                        Vector3? hub = variant == RvrRouteVariant.HubFan ? new Vector3(766235, 669173, start.Z) : null;
                        RvrRouteChoice choice = AutonomousRvrRoutePolicy.ChooseRoute(start, goal, variant, probe,
                            new Random(seed), heat: null, hubCentre: hub);
                        string key = $"{AutonomousRvrRoutePolicy.Label(variant)} fallback={choice.Fallback} reason={Reason(choice)}";
                        tally[key] = tally.GetValueOrDefault(key) + 1;
                        cases++;
                        if (seed == 0)
                            TestContext.Progress.WriteLine($"ROUTE_PROBE {startName}->{goalName} {AutonomousRvrRoutePolicy.Label(variant)} " +
                                $"leg={(int)choice.LegLength} via={(int)choice.Waypoint.X},{(int)choice.Waypoint.Y},{(int)choice.Waypoint.Z} " +
                                $"fallback={choice.Fallback} reason={Reason(choice)}");
                    }
                }
                // Floor search boxes at random frontier points, Z guessed from a nearby start.
                var rng = new Random(11);
                var boxes = new (float H, float V)[] { (2, 128), (64, 512), (64, 1024), (64, 2048), (192, 2048), (192, 4096), (512, 4096) };
                var hits = new int[boxes.Length];
                int samples = 0;
                foreach ((string startName, Vector3 start) in starts)
                for (int i = 0; i < 50; i++)
                {
                    double angle = rng.NextDouble() * Math.PI * 2;
                    float distance = 1_500 + (float)rng.NextDouble() * 4_500;
                    Vector3 raw = new(start.X + (float)Math.Cos(angle) * distance, start.Y + (float)Math.Sin(angle) * distance, start.Z);
                    Zone zone = region.GetZone((int)raw.X, (int)raw.Y);
                    if (zone != region.GetZone((int)start.X, (int)start.Y)) continue;
                    samples++;
                    for (int b = 0; b < boxes.Length; b++)
                        if (nav.GetClosestPoint(zone, raw, boxes[b].H, boxes[b].H, boxes[b].V, nav.DefaultFilters) is { } f &&
                            float.IsFinite(f.Z)) hits[b]++;
                }
                for (int b = 0; b < boxes.Length; b++)
                    TestContext.Progress.WriteLine($"ROUTE_PROBE_FLOOR box={boxes[b].H}x{boxes[b].V} hits={hits[b]}/{samples}");
                foreach ((string key, int count) in tally.OrderBy(pair => pair.Key))
                    TestContext.Progress.WriteLine($"ROUTE_PROBE_TALLY {key} {count}");
                Assert.That(cases, Is.GreaterThan(0));
            }
            finally
            {
                Environment.CurrentDirectory = previous;
            }
        }

        private static string Reason(RvrRouteChoice choice) => choice.Reason;
    }
}
