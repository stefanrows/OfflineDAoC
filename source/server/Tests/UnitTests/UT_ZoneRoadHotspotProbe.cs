using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Opt-in evidence probe for task 143 (port S3): the two dead-end detour hotspots of docs/NAVMESH_HOTSPOTS.md
    /// (Odin's Gate field, Emain Macha Albion portal keep approach) on the installed meshes with real Detour, once
    /// with the direct search alone and once with the shipped road data (Pathfinder.TryRoadPath). Read only: loads
    /// meshes and opens the database read-only; never starts a server or changes a file.
    /// Env: OFFLINE_DAOC_NAV_ROOT (runtime\server), OFFLINE_DAOC_TEST_DETOUR (Detour.dll), OFFLINE_DAOC_DB_PATH.
    /// </summary>
    [TestFixture, NonParallelizable, Explicit("Read-only installed-navmesh road fallback probe")]
    public class UT_ZoneRoadHotspotProbe
    {
        [Test]
        public void DeadEndHotspotsCompleteOverTheRoads()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            Assume.That(!string.IsNullOrEmpty(root));
            string previous = Environment.CurrentDirectory;
            var loaded = new List<Zone>();
            try
            {
                string native = Environment.GetEnvironmentVariable("OFFLINE_DAOC_TEST_DETOUR");
                if (!string.IsNullOrEmpty(native))
                {
                    try
                    {
                        NativeLibrary.SetDllImportResolver(typeof(LocalPathfindingMgr).Assembly,
                            (name, assembly, search) => name == "lib/Detour" ? NativeLibrary.Load(native) : IntPtr.Zero);
                    }
                    catch (InvalidOperationException) { }
                }
                Environment.CurrentDirectory = root;
                string databasePath = Environment.GetEnvironmentVariable("OFFLINE_DAOC_DB_PATH") ??
                    Path.GetFullPath(Path.Combine(root, "..", "data", "opendaoc.sqlite3.db"));
                using var db = new SQLiteConnection($"Data Source={databasePath};Read Only=True;Pooling=False;");
                db.Open();

                Zone Load(ushort id)
                {
                    using var command = db.CreateCommand();
                    command.CommandText = "select RegionID,Name,OffsetX,OffsetY,Width,Height from Zones where ZoneID=@zone";
                    command.Parameters.AddWithValue("@zone", (int)id);
                    using var reader = command.ExecuteReader();
                    Assert.That(reader.Read(), Is.True, $"zone {id} in the Zones table");
                    var region = (Region)RuntimeHelpers.GetUninitializedObject(typeof(Region));
                    typeof(Region).GetField("m_regionData", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(region, new RegionData { Id = (ushort)reader.GetInt32(0) });
                    typeof(Region).GetField("m_zones", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(region, new List<Zone>());
                    var zone = new Zone(region, id, reader.GetString(1), reader.GetInt32(2) * 8192, reader.GetInt32(3) * 8192,
                        reader.GetInt32(4) * 8192, reader.GetInt32(5) * 8192, id, false, 0, false, 0, 0, 0, 0, 0);
                    LocalPathfindingMgr.LoadNavMesh(zone);
                    loaded.Add(zone);
                    return zone;
                }

                var nav = PathfindingProvider.LocalPathfindingMgr;
                Assert.That(AutonomousZoneRoads.ZoneCount, Is.GreaterThan(0), "the shipped road data loads against the installed meshes");
                int improved = 0;
                foreach (var (name, zoneId, from, to) in new (string, ushort, Vector3, Vector3)[]
                {
                    ("Odin's Gate field (cluster 7)", 115, new(606721, 600858, 6688), new(604966, 618990, 4654)),
                    ("Emain Macha Albion portal keep approach", 214, new(474010, 318918, 0), new(475538, 343121, 0)),
                })
                {
                    Zone zone = Load(zoneId);
                    Vector3 start = Snap(nav, zone, from), goal = Snap(nav, zone, to);
                    var nodes = new WrappedPathfindingNode[512];
                    PathfindingResult direct = nav.GetPathStraight(zone, start, goal, nav.DefaultFilters, nodes);
                    float directShort = direct.NodeCount > 0 ? Vector3.Distance(nodes[direct.NodeCount - 1].Position, goal) : float.NaN;
                    // Walk it as the bot mover does: each order follows the joined road legs as far as the buffer holds,
                    // then the partial continuation replots from the last node (direct search first, roads again if it dead-ends).
                    Vector3 position = start;
                    float walked = 0;
                    int orders = 0;
                    bool reached = false;
                    string stop = "orders";
                    while (orders++ < 40)
                    {
                        PathfindingResult step = nav.GetPathStraight(zone, position, goal, nav.DefaultFilters, nodes);
                        if (Pathfinder.TryRoadPath(nav, zone, position, goal, nav.DefaultFilters, step, nodes, 512, out PathfindingResult road))
                            step = road;
                        if (step.NodeCount < 1) { stop = step.Status.ToString(); break; }
                        Vector3 last = nodes[step.NodeCount - 1].Position;
                        for (int i = 1; i < step.NodeCount; i++) walked += Vector3.Distance(nodes[i - 1].Position, nodes[i].Position);
                        if (step.Status == PathfindingStatus.PathFound || Vector3.Distance(last, goal) <= 256) { reached = true; break; }
                        if (Vector3.DistanceSquared(last, position) < 16) { stop = "stalled"; break; }
                        position = last;
                    }
                    TestContext.Progress.WriteLine($"{name}: direct={direct.Status}/{direct.NodeCount} short={directShort:0} " +
                                                   $"chord={Vector3.Distance(start, goal):0} roads: reached={reached} orders={orders} walked={walked:0} " +
                                                   (reached ? string.Empty : $"stop={stop} left={Vector3.Distance(position, goal):0}"));
                    if (reached && direct.Status != PathfindingStatus.PathFound) improved++;
                }
                Assert.That(improved, Is.GreaterThan(0), "at least one dead-end hotspot reaches its goal over the roads");
            }
            finally
            {
                foreach (Zone zone in loaded) LocalPathfindingMgr.UnloadNavMesh(zone);
                Environment.CurrentDirectory = previous;
            }
        }

        private static Vector3 Snap(IPathfindingMgr nav, Zone zone, Vector3 point) =>
            nav.GetClosestPoint(zone, point, 256, 256, 8192, nav.DefaultFilters) ?? point;
    }
}
