using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Task 139 step 1: read-only evidence for the route give-up hotspots of the 2026-10-23 log
    /// (docs/NAVMESH_HOTSPOTS.md). For each logged start/goal pair it snaps both ends to the mesh,
    /// reproduces the server's raw path query, walks a chained corridor (the 256-polygon cap is not
    /// a disconnection), repeats with door polygons excluded (gates closed), measures how far the
    /// start's and goal's walkable areas reach, tests the zone's keeps, bind points and zone-point
    /// landings as known ground, and tries 48-384 unit offsets of both ends. Nothing is asserted
    /// beyond the meshes loading; the output is the evidence.
    /// Env: OFFLINE_DAOC_NAV_ROOT (installed runtime/server: lib/Detour.dll, navmesh/, ../data db),
    /// optional OFFLINE_DAOC_NAV_ALT ("label=dir;..." where dir holds navmesh/zoneNNN.nav) to probe
    /// the same points on other mesh sets (for example upstream 0.35b, kept outside the install).
    /// </summary>
    [TestFixture, NonParallelizable, Explicit("Read-only task 139 hotspot probe on installed meshes")]
    public class UT_Task139HotspotProbe
    {
        private sealed record Hotspot(string Label, int Region, Vector3 Start, Vector3 Goal);

        private static readonly Hotspot[] Hotspots =
        [
            // Forest Sauvage (zone 11): Darkness Falls portal inside Castle Excalibur (89 give-ups, 72 bots).
            new("FS-DF-Excalibur-a", 1, new(595280, 446362, 3171), new(601721, 430860, 5856)),
            new("FS-DF-Excalibur-b", 1, new(580463, 430694, 4704), new(601644, 430727, 5856)),
            // Forest Sauvage: Active PvP targets standing on Caer Renaris (152 give-ups, 3-6 bots).
            new("FS-Renaris-tower", 1, new(591726, 418714, 4986), new(579126, 429197, 5491)),
            new("FS-Renaris-wall", 1, new(591726, 418714, 4986), new(580653, 430038, 5027)),
            new("FS-Renaris-yard", 1, new(569505, 476467, 4223), new(579339, 429802, 4851)),
            // Forest Sauvage: Castle Sauvage border keep / battleground portal (49 give-ups).
            new("FS-Sauvage-a", 1, new(585469, 476851, 2602), new(585889, 474426, 3146)),
            new("FS-Sauvage-b", 1, new(586463, 474782, 3006), new(586736, 473925, 3385)),
            new("FS-Sauvage-c", 1, new(583286, 478003, 3072), new(586359, 477372, 2625)),
            // Odin's Gate (zone 115).
            new("OG-field-a", 100, new(606721, 600858, 6688), new(604966, 618990, 4654)),
            new("OG-seam-b", 100, new(610374, 636723, 6093), new(612306, 638963, 8147)),
            new("OG-hill-c", 100, new(618980, 596077, 5375), new(620051, 596708, 7331)),
            new("OG-albkeep-d", 100, new(595969, 601142, 7296), new(597071, 630821, 5994)),
            // Connacht (zone 207): Druim Ligen gate, 223 give-ups from six bots at the stable master.
            new("CO-stable-DruimLigen", 200, new(311960, 470002, 5203), new(335449, 421910, 5401)),
            new("CO-field-DruimLigen", 200, new(344098, 425524, 6173), new(335449, 421910, 5401)),
            new("CO-near-DruimLigen", 200, new(333340, 420302, 5188), new(335449, 421910, 5401)),
            new("CO-seam-south", 200, new(342584, 420759, 6781), new(342246, 417812, 8671)),
            new("CO-east", 200, new(334517, 483317, 5533), new(351918, 483314, 6973)),
            // Mount Collory (zone 210): Darkness Falls portal inside Dun Dagda (181 give-ups).
            new("MC-DF-Dagda-north", 200, new(403072, 482611, 8775), new(400684, 465966, 3081)),
            new("MC-DF-Dagda-south", 200, new(397466, 457728, 1773), new(400694, 465707, 3081)),
            // Uppland (zone 111) and Munin Sound (zone 155).
            new("UP-field", 100, new(765293, 664835, 7092), new(762102, 662825, 6838)),
            new("MS-shore", 151, new(331668, 384546, 5673), new(334085, 384583, 5769)),
            // Emain Macha (zone 214): Albion portal keep approach (known hotspot, repaired by escape).
            new("EM-albkeep", 200, new(474010, 318918, 5786), new(475538, 343121, 4102)),
        ];

        private static readonly int[] OffsetRadii = [48, 96, 192, 384];

        [Test]
        public void Probe()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            Assume.That(root, Is.Not.Null.And.Not.Empty, "OFFLINE_DAOC_NAV_ROOT must name the installed runtime/server");
            var meshSets = new List<(string Label, string Dir)> { ("installed", root) };
            foreach (string item in (Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ALT") ?? "")
                         .Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair = item.Split('=', 2);
                meshSets.Add((pair[0], pair[1]));
            }
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(LocalPathfindingMgr).Assembly,
                    (name, assembly, search) => name == "lib/Detour" ? NativeLibrary.Load(Path.Combine(root, "lib", "Detour.dll")) : IntPtr.Zero);
            }
            catch (InvalidOperationException)
            {
                // Another explicit fixture in this run already installed the same resolver.
            }
            string previous = Environment.CurrentDirectory;
            using var db = new SQLiteConnection($"Data Source={Path.GetFullPath(Path.Combine(root, "..", "data", "opendaoc.sqlite3.db"))};Read Only=True;Pooling=False;");
            db.Open();
            var nav = PathfindingProvider.LocalPathfindingMgr;
            try
            {
                foreach ((string label, string dir) in meshSets)
                {
                    Environment.CurrentDirectory = dir;
                    foreach (IGrouping<int, Hotspot> byRegion in Hotspots.GroupBy(h => h.Region))
                    {
                        var loaded = new List<Zone>();
                        Region region = BuildRegion(db, byRegion.Key);
                        try
                        {
                            foreach (Zone zone in byRegion.Select(h => region.GetZone((int)h.Start.X, (int)h.Start.Y)).Distinct())
                            {
                                LocalPathfindingMgr.LoadNavMesh(zone);
                                loaded.Add(zone);
                                Out($"MESH [{label}] zone {zone.ID} loaded={nav.HasNavmesh(zone)}");
                            }
                            if (label == "installed")
                                Assert.That(loaded.All(nav.HasNavmesh), Is.True, "every installed hotspot zone has a mesh");
                            foreach (Hotspot hotspot in byRegion)
                                if (nav.HasNavmesh(region.GetZone((int)hotspot.Start.X, (int)hotspot.Start.Y)))
                                    ProbeHotspot(nav, db, region, label, hotspot);
                        }
                        finally
                        {
                            foreach (Zone zone in loaded)
                                LocalPathfindingMgr.UnloadNavMesh(zone);
                            ForgetThreadQueries();
                        }
                    }
                }
            }
            finally { Environment.CurrentDirectory = previous; }
        }

        private static void Out(string line) => TestContext.Progress.WriteLine(line);

        /// <summary>
        /// Classifies every distinct logged give-up pair on the installed meshes. Input: OFFLINE_DAOC_TASK139_GIVEUPS, a
        /// tab-separated file "region sx sy sz gx gy gz loggedStatus goalKind count bots" (one row per distinct start/goal
        /// pair, extracted from AUTONOMOUS_ROUTE_RECOVERY lines). Doors are not registered here, so "open" means every
        /// door passable and "gates closed" excludes every door polygon.
        /// </summary>
        [Test]
        public void ClassifyLoggedGiveUps()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            string input = Environment.GetEnvironmentVariable("OFFLINE_DAOC_TASK139_GIVEUPS");
            Assume.That(!string.IsNullOrEmpty(root) && File.Exists(input), "OFFLINE_DAOC_NAV_ROOT and OFFLINE_DAOC_TASK139_GIVEUPS are required");
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(LocalPathfindingMgr).Assembly,
                    (name, assembly, search) => name == "lib/Detour" ? NativeLibrary.Load(Path.Combine(root, "lib", "Detour.dll")) : IntPtr.Zero);
            }
            catch (InvalidOperationException)
            {
            }
            string previous = Environment.CurrentDirectory;
            Environment.CurrentDirectory = root;
            using var db = new SQLiteConnection($"Data Source={Path.GetFullPath(Path.Combine(root, "..", "data", "opendaoc.sqlite3.db"))};Read Only=True;Pooling=False;");
            db.Open();
            var nav = PathfindingProvider.LocalPathfindingMgr;
            var regions = new Dictionary<int, Region>();
            var loaded = new List<Zone>();
            var refsByZone = new Dictionary<Zone, List<Vector3>>();
            var totals = new SortedDictionary<string, (int Rows, int GiveUps)>(StringComparer.Ordinal);
            EDtPolyFlags[] gatesClosed = [EDtPolyFlags.All ^ EDtPolyFlags.Disabled, EDtPolyFlags.AnyDoor];
            var nodes = new WrappedPathfindingNode[512];
            try
            {
                foreach (string line in File.ReadLines(input))
                {
                    string[] f = line.Split('\t');
                    if (f.Length < 11)
                        continue;
                    int regionId = int.Parse(f[0]);
                    Vector3 start = new(float.Parse(f[1]), float.Parse(f[2]), float.Parse(f[3]));
                    Vector3 goal = new(float.Parse(f[4]), float.Parse(f[5]), float.Parse(f[6]));
                    int count = int.Parse(f[9]);
                    if (!regions.TryGetValue(regionId, out Region region))
                        regions[regionId] = region = BuildRegion(db, regionId);
                    Zone zone = region.GetZone((int)start.X, (int)start.Y);
                    Zone goalZone = region.GetZone((int)goal.X, (int)goal.Y);
                    string cls;
                    string detail = "";
                    if (zone == null)
                        cls = "no-zone";
                    else
                    {
                        if (!loaded.Contains(zone))
                        {
                            LocalPathfindingMgr.LoadNavMesh(zone);
                            loaded.Add(zone);
                        }
                        if (!nav.HasNavmesh(zone))
                            cls = "no-mesh";
                        else if (goalZone != zone)
                            cls = "cross-zone";
                        else
                        {
                            if (!refsByZone.TryGetValue(zone, out List<Vector3> refs))
                                refsByZone[zone] = refs = References(db, region, zone)
                                    .Select(r => nav.GetClosestPoint(zone, r.Point, 96, 96, 512, nav.DefaultFilters))
                                    .Where(p => p.HasValue).Select(p => p.Value).ToList();
                            cls = Classify(nav, zone, start, goal, refs, gatesClosed, nodes, out detail);
                        }
                    }
                    bool seam = goalZone != null && EdgeDistance(goalZone, goal) <= 128;
                    string key = $"{cls}{(seam ? "+seam" : "")}";
                    totals[key] = totals.TryGetValue(key, out var t) ? (t.Rows + 1, t.GiveUps + count) : (1, count);
                    Out($"CLASS {key} r{regionId} z{zone?.ID} logged={f[7]} kind={f[8]} count={count} bots={f[10]} start={Fmt(start)} goal={Fmt(goal)} {detail}");
                }
            }
            finally
            {
                foreach (Zone zone in loaded)
                    LocalPathfindingMgr.UnloadNavMesh(zone);
                ForgetThreadQueries();
                Environment.CurrentDirectory = previous;
            }
            foreach (var (key, value) in totals)
                Out($"TOTAL {key} pairs={value.Rows} giveUps={value.GiveUps}");
        }

        private static string Classify(IPathfindingMgr nav, Zone zone, Vector3 start, Vector3 goal, List<Vector3> refs,
            EDtPolyFlags[] gatesClosed, WrappedPathfindingNode[] nodes, out string detail)
        {
            EDtPolyFlags[] open = nav.DefaultFilters;
            Vector3? startServer = nav.GetClosestPoint(zone, start, 32, 32, 64, open);
            Vector3? goalServer = nav.GetClosestPoint(zone, goal, 32, 32, 64, open);
            Vector3? startWide = nav.GetClosestPoint(zone, start, 96, 96, 512, open) ?? nav.GetClosestPoint(zone, start, 256, 256, 1024, open);
            Vector3? goalWide = nav.GetClosestPoint(zone, goal, 96, 96, 512, open) ?? nav.GetClosestPoint(zone, goal, 256, 256, 1024, open);
            detail = $"goalSnap={Describe(goalWide, goal)} startSnap={Describe(startWide, start)}";
            if (startWide == null)
                return "start-off-mesh";
            if (goalWide == null)
                return "goal-off-mesh";
            Walk walk = Chain(nav, zone, startWide.Value, goalWide.Value, open);
            Walk closed = Chain(nav, zone, startWide.Value, goalWide.Value, gatesClosed);
            detail += $" open={walk.Complete}:{walk.Why}:{walk.EndToTarget:F0}:doors{walk.Doors} closed={closed.Complete}:{closed.Why}:{closed.EndToTarget:F0}";
            if (startServer == null)
                return walk.Complete ? "start-off-mesh(snap-connects)" : "start-off-mesh";
            if (goalServer == null)
                return walk.Complete ? "goal-off-mesh(snap-connects)" : "goal-off-mesh";
            PathfindingResult raw = nav.GetPathStraight(zone, start, goal, open, nodes);
            detail += $" raw={raw.Status}:{raw.NodeCount}";
            if (walk.Complete)
            {
                if (!closed.Complete)
                    return "behind-doors";
                return raw.Status == PathfindingStatus.PathFound ? "path-found-now" : "corridor-cap-only";
            }
            // Connected through known ground (a two-leg route), but the direct search dead-ends.
            var nearest = refs.OrderBy(r => Vector3.DistanceSquared(r, goal)).Take(4)
                .Concat(refs.OrderBy(r => Vector3.DistanceSquared(r, start)).Take(2)).Distinct().ToList();
            bool goalLeaves = false, goalEnterable = false;
            foreach (Vector3 r in nearest)
            {
                bool toGoal = Chain(nav, zone, r, goalWide.Value, open).Complete;
                goalEnterable |= toGoal;
                goalLeaves |= Chain(nav, zone, goalWide.Value, r, open).Complete;
                if (toGoal && Chain(nav, zone, startWide.Value, r, open).Complete)
                {
                    detail += $" via={Fmt(r)}";
                    return "dead-end-detour";
                }
            }
            if (goalLeaves && !goalEnterable)
                return "one-way-goal";
            if (goalEnterable)
                return "start-cut-off";
            return "disconnected";
        }

        /// <summary>
        /// The manager caches one Detour query per zone id and thread. After a mesh is unloaded that query points at freed
        /// memory, so a second mesh set for the same zone id must not reuse it.
        /// </summary>
        private static void ForgetThreadQueries()
        {
            object local = typeof(LocalPathfindingMgr).GetField("_navmeshQueries", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
            if (local?.GetType().GetProperty("Value")?.GetValue(local) is System.Collections.IDictionary queries)
            {
                foreach (object query in queries.Values)
                    (query as IDisposable)?.Dispose();
                queries.Clear();
            }
        }

        private static void ProbeHotspot(IPathfindingMgr nav, SQLiteConnection db, Region region, string set, Hotspot h)
        {
            Zone zone = region.GetZone((int)h.Start.X, (int)h.Start.Y);
            Zone goalZone = region.GetZone((int)h.Goal.X, (int)h.Goal.Y);
            string tag = $"[{set}] {h.Label} r{h.Region} z{zone.ID}";
            Out($"HOTSPOT {tag} start={Fmt(h.Start)} goal={Fmt(h.Goal)} goalZone={goalZone?.ID} " +
                                  $"chord={Vector3.Distance(h.Start, h.Goal):F0} goalToZoneEdge={EdgeDistance(goalZone, h.Goal):F0} startToZoneEdge={EdgeDistance(zone, h.Start):F0}");
            EDtPolyFlags[] open = nav.DefaultFilters;
            EDtPolyFlags[] gatesClosed = [EDtPolyFlags.All ^ EDtPolyFlags.Disabled, EDtPolyFlags.AnyDoor];

            // Snap at the server's query extent (32,32,64) and at the probe's wider extents.
            Vector3? startSnap = Snap(nav, zone, h.Start, tag, "start");
            Vector3? goalSnap = Snap(nav, zone, h.Goal, tag, "goal");

            // The server's own query: raw bot position to raw destination, doors open.
            Span<WrappedPathfindingNode> nodes = new WrappedPathfindingNode[512];
            PathfindingResult raw = nav.GetPathStraight(zone, h.Start, h.Goal, open, nodes);
            Vector3 rawEnd = raw.NodeCount > 0 ? nodes[raw.NodeCount - 1].Position : h.Start;
            Out($"RAW {tag} status={raw.Status} nodes={raw.NodeCount} end={Fmt(rawEnd)} endToGoal={Vector3.Distance(rawEnd, h.Goal):F0} doors={CountDoors(nodes, raw.NodeCount)}");
            if (startSnap == null || goalSnap == null)
                return;

            Walk walk = Chain(nav, zone, startSnap.Value, goalSnap.Value, open);
            Out($"CHAIN {tag} open {walk}");
            Walk closed = Chain(nav, zone, startSnap.Value, goalSnap.Value, gatesClosed);
            Out($"CHAIN {tag} gatesClosed {closed}");
            if (!walk.Complete)
            {
                Walk back = Chain(nav, zone, goalSnap.Value, startSnap.Value, open);
                Out($"CHAIN {tag} reverse {back}");
                // Does the start join the reverse search's end? Then both ends are one walkable area and only the
                // forward search dead-ends (its 4,096-node budget runs out on ground that first leads away).
                Walk join = Chain(nav, zone, startSnap.Value, back.End, open);
                Out($"CHAIN {tag} startToReverseEnd {join}");
            }

            // How far the walkable area around each end reaches (a small island stays small).
            Out($"REACH {tag} start {Reach(nav, zone, startSnap.Value)} goal {Reach(nav, zone, goalSnap.Value)}");

            // Known ground: the zone's keeps, bind points and zone-point landings.
            foreach ((string name, Vector3 point) in References(db, region, zone).OrderBy(r => Vector3.DistanceSquared(r.Point, h.Goal)).Take(8))
            {
                Vector3? refSnap = nav.GetClosestPoint(zone, point, 96, 96, 512, open);
                if (refSnap == null)
                {
                    Out($"REF {tag} {name} {Fmt(point)} unprojected");
                    continue;
                }
                Walk fromStart = Chain(nav, zone, startSnap.Value, refSnap.Value, open);
                Walk fromGoal = Chain(nav, zone, goalSnap.Value, refSnap.Value, open);
                // A two-leg route through known ground (start -> reference -> goal) proves the ends share one
                // walkable area even when the direct search dead-ends: what a road graph would route through.
                Walk toGoal = walk.Complete ? default : Chain(nav, zone, refSnap.Value, goalSnap.Value, open);
                bool via = !walk.Complete && fromStart.Complete && toGoal.Complete;
                Out($"REF {tag} {name} {Fmt(refSnap.Value)} distGoal={Vector3.Distance(point, h.Goal):F0} " +
                                      $"start->ref={fromStart.Complete}({fromStart.EndToTarget:F0}) goal->ref={fromGoal.Complete}({fromGoal.EndToTarget:F0}) " +
                                      $"ref->goal={toGoal.Complete}({toGoal.EndToTarget:F0}) viaRef={via}");
            }

            if (walk.Complete)
                return;
            // Would a short offset of either end connect?
            ReportOffsets(nav, zone, tag, "goal", h.Goal, p => Chain(nav, zone, startSnap.Value, p, open));
            ReportOffsets(nav, zone, tag, "start", h.Start, p => Chain(nav, zone, p, goalSnap.Value, open));
        }

        private static Vector3? Snap(IPathfindingMgr nav, Zone zone, Vector3 point, string tag, string end)
        {
            Vector3? server = nav.GetClosestPoint(zone, point, 32, 32, 64, nav.DefaultFilters);
            Vector3? wide = nav.GetClosestPoint(zone, point, 96, 96, 512, nav.DefaultFilters);
            Vector3? widest = nav.GetClosestPoint(zone, point, 256, 256, 1024, nav.DefaultFilters);
            Out($"SNAP {tag} {end} server32={Describe(server, point)} wide96x512={Describe(wide, point)} widest256x1024={Describe(widest, point)}");
            return wide ?? widest;
        }

        private static void ReportOffsets(IPathfindingMgr nav, Zone zone, string tag, string end, Vector3 centre, Func<Vector3, Walk> test)
        {
            var connected = new List<string>();
            int tried = 0;
            foreach (int radius in OffsetRadii)
            {
                for (int angle = 0; angle < 360; angle += 45)
                {
                    float radians = angle * MathF.PI / 180;
                    Vector3 raw = centre + new Vector3(MathF.Cos(radians) * radius, MathF.Sin(radians) * radius, 0);
                    Vector3? p = nav.GetClosestPoint(zone, raw, 16, 16, 256, nav.DefaultFilters);
                    if (p == null)
                        continue;
                    tried++;
                    if (test(p.Value).Complete)
                        connected.Add($"{radius}@{angle}:{Fmt(p.Value)}");
                }
            }
            Out($"OFFSET {tag} {end} projected={tried}/{OffsetRadii.Length * 8} connected={connected.Count} {string.Join(" ", connected.Take(4))}");
        }

        private readonly record struct Walk(bool Complete, int Segments, PathfindingStatus LastStatus, Vector3 End, float EndToTarget, int Doors, string Why)
        {
            public override string ToString() =>
                $"complete={Complete} segments={Segments} last={LastStatus} end={Fmt(End)} endToTarget={EndToTarget:F0} doors={Doors} why={Why}";
        }

        /// <summary>Chained corridor walk: replans from each partial end (the 256-polygon cap is not a disconnection).</summary>
        private static Walk Chain(IPathfindingMgr nav, Zone zone, Vector3 start, Vector3 end, EDtPolyFlags[] filters)
        {
            var nodes = new WrappedPathfindingNode[512];
            Vector3 current = start;
            int doors = 0;
            PathfindingStatus status = PathfindingStatus.NoPathFound;
            float best = Vector3.Distance(start, end);
            int stale = 0;
            for (int segment = 1; segment <= 48; segment++)
            {
                PathfindingResult result = nav.GetPathStraight(zone, current, end, filters, nodes);
                status = result.Status;
                if (result.NodeCount < 1 || result.NodeCount > nodes.Length)
                    return new(false, segment, status, current, Vector3.Distance(current, end), doors, "no-corridor");
                doors += CountDoors(nodes, result.NodeCount);
                Vector3 last = nodes[result.NodeCount - 1].Position;
                float left = Vector3.Distance(last, end);
                if (status != PathfindingStatus.NoPathFound && left <= 48)
                    return new(true, segment, status, last, left, doors, "reached");
                if (status == PathfindingStatus.PathFound)
                    return new(false, segment, status, last, left, doors, "found-but-far");
                if (Vector3.Distance(last, current) < 16)
                    return new(false, segment, status, last, left, doors, "stalled");
                if (left < best - 64) { best = left; stale = 0; }
                else if (++stale >= 4)
                    return new(false, segment, status, last, left, doors, "no-progress");
                current = last;
            }
            return new(false, 48, status, current, Vector3.Distance(current, end), doors, "segment-cap");
        }

        private static string Reach(IPathfindingMgr nav, Zone zone, Vector3 point)
        {
            float max = 0, sum = 0;
            int found = 0;
            for (int i = 0; i < 48; i++)
            {
                Vector3? p = nav.GetRandomPoint(zone, point, 40_000, nav.DefaultFilters);
                if (p == null)
                    continue;
                float d = Vector2.Distance(new(p.Value.X, p.Value.Y), new(point.X, point.Y));
                max = Math.Max(max, d);
                sum += d;
                found++;
            }
            return found == 0 ? "none" : $"max={max:F0} mean={sum / found:F0} n={found}";
        }

        private static IEnumerable<(string Name, Vector3 Point)> References(SQLiteConnection db, Region region, Zone zone)
        {
            var result = new List<(string, Vector3)>();
            void Read(string sql, Func<SQLiteDataReader, (string, Vector3)> map)
            {
                using var command = db.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("@region", region.ID);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    (string name, Vector3 point) = map(reader);
                    if (region.GetZone((int)point.X, (int)point.Y) == zone)
                        result.Add((name, point));
                }
            }
            Read("select Name,X,Y,Z from Keep where Region=@region", r => ($"keep:{r.GetString(0).Replace(' ', '_')}", new(r.GetInt32(1), r.GetInt32(2), r.GetInt32(3))));
            Read("select X,Y,Z from BindPoint where Region=@region", r => ("bind", new(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2))));
            Read("select Id,TargetX,TargetY,TargetZ from ZonePoint where TargetRegion=@region", r => ($"landing:{r.GetInt32(0)}", new(r.GetInt32(1), r.GetInt32(2), r.GetInt32(3))));
            return result;
        }

        private static int CountDoors(Span<WrappedPathfindingNode> nodes, int count)
        {
            int doors = 0;
            for (int i = 0; i < Math.Min(count, nodes.Length); i++)
                if ((nodes[i].Flags & EDtPolyFlags.AnyDoor) != 0)
                    doors++;
            return doors;
        }

        private static float EdgeDistance(Zone zone, Vector3 p) =>
            zone == null ? -1 : Math.Min(Math.Min(p.X - zone.XOffset, zone.XOffset + zone.Width - p.X),
                Math.Min(p.Y - zone.YOffset, zone.YOffset + zone.Height - p.Y));

        private static string Describe(Vector3? snapped, Vector3 point) =>
            snapped == null ? "none" : $"{Fmt(snapped.Value)}(d={Vector3.Distance(snapped.Value, point):F0},dz={snapped.Value.Z - point.Z:F0})";

        private static string Fmt(Vector3 v) => $"{v.X:F0},{v.Y:F0},{v.Z:F0}";

        /// <summary>A region with every zone of the database (for GetZone); meshes are loaded by the caller.</summary>
        private static Region BuildRegion(SQLiteConnection db, int regionId)
        {
            var region = (Region)RuntimeHelpers.GetUninitializedObject(typeof(Region));
            typeof(Region).GetField("m_regionData", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(region, new RegionData { Id = (ushort)regionId });
            var zones = new List<Zone>();
            typeof(Region).GetField("m_zones", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(region, zones);
            using var command = db.CreateCommand();
            command.CommandText = "select ZoneID,Name,OffsetX,OffsetY,Width,Height from Zones where RegionID=@region";
            command.Parameters.AddWithValue("@region", regionId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                ushort id = (ushort)reader.GetInt32(0);
                zones.Add(new Zone(region, id, reader.GetString(1), reader.GetInt32(2) * 8192,
                    reader.GetInt32(3) * 8192, reader.GetInt32(4) * 8192, reader.GetInt32(5) * 8192,
                    id, false, 0, false, 0, 0, 0, 0, 0));
            }
            return region;
        }
    }
}
