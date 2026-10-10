using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CEM.Utils;
using CEM.World;
using Emgu.CV;
using Emgu.CV.Structure;
using MNL;
using OpenTK;

namespace CEM.Client.ZoneExporter
{
    /// <summary>One battleground keep site from tools/dev/battleground-keeps.json.</summary>
    internal sealed record BattlegroundKeepSite(int Region, int KeepId, string Name, int X, int Y, int Z, int Heading, string Template, bool ExistingRow);

    /// <summary>One keep component, using the raw DbKeepComponent values the server loads.</summary>
    internal sealed record BattlegroundKeepComponent(int Id, int Skin, int X, int Y, int Heading);

    /// <summary>A KeepPosition door row (skins 0, 10 and 11 skip the rotation filter on the server).</summary>
    internal sealed record BattlegroundDoorOffset(int Skin, int Rotation, string TemplateId, int XOff, int YOff);

    /// <summary>
    /// Battleground keep data shared with the server (BattlegroundKeepLayouts.cs). Loaded once before the export;
    /// the site finder writes new site coordinates back to the same file.
    /// </summary>
    internal sealed class BattlegroundKeepData
    {
        public const double DoorTolerance = 96;
        private const int ComponentSpacing = 148;
        private const int FootprintPadding = 200;
        private const int LeirvikSourceRegion = 242;
        private const int LeirvikZone = 254;

        private static readonly (bool Mirror, bool HeadingSign)[] Variants =
        {
            (true, true), (true, false), (false, true), (false, false),
        };

        private readonly string _path;
        private readonly Dictionary<int, string> _pieceBySkin = new();
        private JsonNode _root;

        private BattlegroundKeepData(string path) => _path = path;

        public static BattlegroundKeepData Current { get; private set; }

        public List<BattlegroundKeepSite> Sites { get; } = new();

        /// <summary>Sites the finder could not place. The finder retries them and moves a placed one into Sites.</summary>
        public List<BattlegroundKeepSite> Blocked { get; } = new();

        public Dictionary<string, List<BattlegroundKeepComponent>> Templates { get; } = new(StringComparer.Ordinal);

        public List<BattlegroundDoorOffset> DoorOffsets { get; } = new();

        /// <summary>Portal keep centres by region, used as the centroid for the site finder.</summary>
        public Dictionary<int, List<(int X, int Y)>> PortalKeeps { get; } = new();

        /// <summary>Campaign region to nav zone. Leirvik is region 242 in zone 254; TestBG zone 242 is never used.</summary>
        public static int ZoneForRegion(int region) => region == LeirvikSourceRegion ? LeirvikZone : region;

        public static bool IsPlausibleOffset(int x, int y) => x >= -4096 && x <= 4096 && y >= -4096 && y <= 4096;

        /// <summary>The server reads component offsets as signed bytes.</summary>
        public static int SignedOffset(int raw) => unchecked((sbyte)raw);

        public static void Load(string path)
        {
            var data = new BattlegroundKeepData(path);
            data._root = JsonNode.Parse(File.ReadAllText(path));
            foreach (JsonNode node in (JsonArray)data._root["sites"])
                data.Sites.Add(ReadSite(node));
            if (data._root["blockedSites"] is JsonArray blocked)
            {
                foreach (JsonNode node in blocked)
                    data.Blocked.Add(ReadSite(node));
            }
            foreach (KeyValuePair<string, JsonNode> template in (JsonObject)data._root["templates"])
            {
                var components = new List<BattlegroundKeepComponent>();
                foreach (JsonNode node in (JsonArray)template.Value)
                    components.Add(new BattlegroundKeepComponent((int)node["id"], (int)node["skin"], (int)node["x"], (int)node["y"], (int)node["heading"]));
                data.Templates[template.Key] = components;
            }
            foreach (JsonNode node in (JsonArray)data._root["doorOffsets"])
            {
                data.DoorOffsets.Add(new BattlegroundDoorOffset((int)node["skin"], (int)node["rotation"], (string)node["templateId"],
                    (int)node["xOff"], (int)node["yOff"]));
            }
            foreach (KeyValuePair<string, JsonNode> portal in (JsonObject)data._root["portalKeeps"])
            {
                data.PortalKeeps[int.Parse(portal.Key, CultureInfo.InvariantCulture)] = ((JsonArray)portal.Value)
                    .Select(node => ((int)node["x"], (int)node["y"])).ToList();
            }
            data.ReadPieceTable();
            Current = data;
            Log.Normal($"Battleground keep data loaded from {path}: {data.Sites.Count} sites, {data.Templates.Count} templates, {data._pieceBySkin.Count} skins mapped to pieces.");
        }

        /// <summary>Largest component offset from the keep centre, in units: the layout's own extent.</summary>
        public int LayoutExtent(string template)
        {
            int largest = 0;
            if (Templates.TryGetValue(template, out List<BattlegroundKeepComponent> components))
                foreach (BattlegroundKeepComponent component in components)
                    largest = Math.Max(largest, Math.Max(Math.Abs(SignedOffset(component.X)), Math.Abs(SignedOffset(component.Y))));
            return largest * ComponentSpacing;
        }

        /// <summary>Layout extent plus the guard room the server search uses (BattlegroundKeepLayouts.SearchRadius).</summary>
        public int Footprint(string template) => LayoutExtent(template) + FootprintPadding;

        public string PieceForSkin(int skin) => _pieceBySkin.TryGetValue(skin, out string piece) ? piece : null;

        /// <summary>Component centre, computed exactly as GameKeepComponent.LoadFromDatabase does.</summary>
        public static (int X, int Y) ComponentCentre(BattlegroundKeepSite keep, BattlegroundKeepComponent component)
        {
            double angle = keep.Heading * (Math.PI * 2 / 360);
            int sx = SignedOffset(component.X), sy = SignedOffset(component.Y);
            int x = (int)(keep.X + (sx * ComponentSpacing * Math.Cos(angle) + sy * ComponentSpacing * Math.Sin(angle)));
            int y = (int)(keep.Y - (sy * ComponentSpacing * Math.Cos(angle) - sx * ComponentSpacing * Math.Sin(angle)));
            return (x, y);
        }

        /// <summary>Door world position, computed exactly as PositionMgr.LoadXY does for the component.</summary>
        public static (double X, double Y) ServerDoorPosition(BattlegroundKeepSite keep, (int X, int Y) centre, int componentHeading, int xOff, int yOff)
        {
            double angle = keep.Heading * (Math.PI * 2 / 360);
            double c = Math.Cos(angle), s = Math.Sin(angle);
            switch (componentHeading)
            {
                case 0: return ((int)(centre.X + c * xOff + s * yOff), (int)(centre.Y - c * yOff + s * xOff));
                case 1: return ((int)(centre.X + c * yOff - s * xOff), (int)(centre.Y + c * xOff + s * yOff));
                case 2: return ((int)(centre.X - c * xOff - s * yOff), (int)(centre.Y + c * yOff - s * xOff));
                case 3: return ((int)(centre.X - c * yOff + s * xOff), (int)(centre.Y - c * xOff - s * yOff));
                default: return (0, 0);
            }
        }

        /// <summary>Server door positions for a component. Only gate, keep and tower skins carry doors.</summary>
        public List<(double X, double Y)> ServerDoors(BattlegroundKeepSite keep, BattlegroundKeepComponent component)
        {
            var doors = new List<(double X, double Y)>();
            if (component.Skin != 0 && component.Skin != 10 && component.Skin != 11) return doors;
            (int X, int Y) centre = ComponentCentre(keep, component);
            foreach (BattlegroundDoorOffset row in DoorOffsets.Where(candidate => candidate.Skin == component.Skin))
            {
                if (!IsPlausibleOffset(row.XOff, row.YOff)) continue;
                doors.Add(ServerDoorPosition(keep, centre, component.Heading, row.XOff, row.YOff));
            }
            return doors;
        }

        /// <summary>Rotation of a component's piece, in degrees, as the server applies it.</summary>
        public static double PieceAngle(BattlegroundKeepSite keep, BattlegroundKeepComponent component) => component.Heading * 90 + keep.Heading;

        /// <summary>
        /// Piece transform. The mirror and heading sign are the four variants the orientation check tries; the baseline
        /// (mirror, heading sign) matches the fixture convention in Zone2Obj.ExportNifs.
        /// </summary>
        public static Matrix4 PieceMatrix(int x, int y, int z, double angleDegrees, bool mirror, bool headingSign)
        {
            float angle = (float)((headingSign ? 1 : -1) * angleDegrees * Math.PI / 180.0);
            return Matrix4.CreateScale(1f, mirror ? -1f : 1f, 1f) * Matrix4.CreateRotationZ(angle) *
                Matrix4.CreateTranslation(x, y, z);
        }

        public static IReadOnlyList<(bool Mirror, bool HeadingSign)> OrientationVariants => Variants;

        /// <summary>Records a placed site. A blocked site moves from blockedSites to sites with its coordinates.</summary>
        public void PlaceSite(int keepId, int x, int y, int z)
        {
            int blockedIndex = Blocked.FindIndex(site => site.KeepId == keepId);
            if (blockedIndex >= 0)
            {
                BattlegroundKeepSite placed = Blocked[blockedIndex] with { X = x, Y = y, Z = z };
                Blocked.RemoveAt(blockedIndex);
                Sites.Add(placed);
                JsonArray blockedNodes = (JsonArray)_root["blockedSites"];
                for (int i = blockedNodes.Count - 1; i >= 0; i--)
                {
                    if ((int)blockedNodes[i]["keepId"] == keepId) blockedNodes.RemoveAt(i);
                }
                ((JsonArray)_root["sites"]).Add(new JsonObject
                {
                    ["region"] = JsonValue.Create(placed.Region), ["keepId"] = JsonValue.Create(placed.KeepId),
                    ["name"] = JsonValue.Create(placed.Name), ["x"] = JsonValue.Create(x), ["y"] = JsonValue.Create(y),
                    ["z"] = JsonValue.Create(z), ["heading"] = JsonValue.Create(placed.Heading),
                    ["template"] = JsonValue.Create(placed.Template), ["existingRow"] = JsonValue.Create(false),
                });
                return;
            }
            int index = Sites.FindIndex(site => site.KeepId == keepId);
            if (index < 0) return;
            Sites[index] = Sites[index] with { X = x, Y = y, Z = z };
            foreach (JsonNode node in (JsonArray)_root["sites"])
            {
                if ((int)node["keepId"] != keepId) continue;
                JsonObject site = (JsonObject)node;
                site["x"] = JsonValue.Create(x);
                site["y"] = JsonValue.Create(y);
                site["z"] = JsonValue.Create(z);
            }
        }

        private static BattlegroundKeepSite ReadSite(JsonNode node) => new((int)node["region"], (int)node["keepId"], (string)node["name"],
            (int)node["x"], (int)node["y"], (int)node["z"], (int)node["heading"], (string)node["template"], (bool)node["existingRow"]);

        public void Save()
        {
            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            // System.Text.Json indents with Environment.NewLine; keep the repository's LF convention for this file.
            File.WriteAllText(_path, _root.ToJsonString(options).Replace("\r\n", "\n") + "\n");
        }

        private void ReadPieceTable()
        {
            string tiers = ReadClientText("frontiers/frontiers.mpk/fr_tiers.csv");
            string pieces = ReadClientText("frontiers/frontiers.mpk/fr_pieces.csv");
            if (tiers == null || pieces == null)
            {
                Log.Warn("BG_KEEP_PIECES_UNAVAILABLE reason=frontiers_mpk_missing");
                return;
            }
            // Two header rows. Tier-1 values index the data rows (ID - 1), so blank rows are skipped only at the end.
            var pieceNames = new List<string>();
            string[] pieceLines = pieces.Split('\n');
            for (int i = 2; i < pieceLines.Length; i++)
            {
                string line = pieceLines[i].Trim();
                if (line.Length == 0) continue;
                string[] fields = line.Split(',');
                pieceNames.Add(fields.Length > 2 ? fields[2].Trim() : string.Empty);
            }
            string[] tierLines = tiers.Split('\n');
            for (int i = 2; i < tierLines.Length; i++)
            {
                string[] fields = tierLines[i].Trim().Split(',');
                if (fields.Length < 3 || !int.TryParse(fields[0], out int skin) || !int.TryParse(fields[2], out int tier)) continue;
                if (tier < 0 || tier >= pieceNames.Count || pieceNames[tier].Length == 0) continue;
                _pieceBySkin[skin] = pieceNames[tier];
            }
        }

        private static string ReadClientText(string path)
        {
            using Stream stream = ClientData.OpenExactly(path);
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Replace("\r", string.Empty);
        }
    }

    /// <summary>Battleground keep export and site finding. Runs inside the zone export and in --bg-keep-sites mode.</summary>
    internal sealed class BattlegroundKeepSiteFinder
    {
        private const int SearchStep = 256;
        private const int SearchLimit = 6000;
        private const int GroundSampleStep = 128;
        private const int PortalClearance = 4500;
        private const int SolidFixtureClearance = 300;
        private const int WaterCellSize = 256;
        private const int WaterEmpty = 255;
        private static readonly int[] FinderRegions = { 234, 235, 236, 238, 240 };
        // Thidranki (238) has no spot at the 96 tolerance within 6,000 units (best 183). Its best spot is accepted at 200,
        // its keep Z is the lowest ground across the layout footprint so the walls sink into the slope, and the solid
        // fixture test is not applied (the plan scores fixtures; the nav keeps them as obstacles). Water still rejects.
        private static readonly Dictionary<int, (double MaxVariance, bool MinimumGround, bool IgnoreFixtures)> Relaxed = new()
        {
            [143] = (200, true, true),
        };

        /// <summary>Searches outward from the portal-keep centroid for flat, clear ground and writes accepted sites.</summary>
        public static bool Run()
        {
            BattlegroundKeepData data = BattlegroundKeepData.Current;
            if (data == null)
            {
                Log.Error("BG_KEEP_SITES needs --bg-keeps=<battleground-keeps.json>.");
                return false;
            }
            WorldMgr.Init();
            bool all = true;
            BattlegroundKeepSite[] pending = data.Sites.Concat(data.Blocked)
                .Where(candidate => !candidate.ExistingRow && FinderRegions.Contains(candidate.Region)).ToArray();
            foreach (BattlegroundKeepSite site in pending)
                all &= FindSite(data, site);
            data.Save();
            Log.Normal($"Battleground keep site search finished. All sites accepted: {all}.");
            return all;
        }

        private static bool FindSite(BattlegroundKeepData data, BattlegroundKeepSite site)
        {
            int zoneId = BattlegroundKeepData.ZoneForRegion(site.Region);
            Zone2 zone = WorldMgr.GetZone(zoneId);
            if (zone == null || !zone.HasHeightmap)
            {
                Log.Error($"BG_KEEP_SITE_UNAVAILABLE zone={zoneId} keep={site.KeepId} reason=no_heightmap");
                return false;
            }
            if (!data.PortalKeeps.TryGetValue(site.Region, out List<(int X, int Y)> portals) || portals.Count == 0)
            {
                Log.Error($"BG_KEEP_SITE_UNAVAILABLE zone={zoneId} keep={site.KeepId} reason=no_portal_keeps");
                return false;
            }
            double centreX = portals.Average(portal => portal.X), centreY = portals.Average(portal => portal.Y);
            // Ground and water are scored over the layout's own extent; guard room is not flattened.
            int footprint = data.LayoutExtent(site.Template);
            (double MaxVariance, bool MinimumGround, bool IgnoreFixtures) rule = Relaxed.TryGetValue(site.KeepId, out var relaxed)
                ? relaxed : (BattlegroundKeepData.DoorTolerance, false, false);
            List<(double X, double Y)> fixtures = ReadSolidFixtures(zone);
            byte[,] waterMap = zone.LoadWaterMap();
            int[] waterHeights = zone.GetWaterHeights();

            var rejected = new Dictionary<string, int>(StringComparer.Ordinal);
            double smallestSlope = double.MaxValue;
            for (int ring = 0; ring * SearchStep <= SearchLimit; ring++)
            {
                double radius = ring * SearchStep;
                int count = ring == 0 ? 1 : Math.Max(8, (int)(2 * Math.PI * radius / SearchStep));
                (int X, int Y, double Variance, int Z)? best = null;
                for (int i = 0; i < count; i++)
                {
                    double angle = 2 * Math.PI * i / count;
                    double x = ring == 0 ? centreX : centreX + radius * Math.Cos(angle);
                    double y = ring == 0 ? centreY : centreY + radius * Math.Sin(angle);
                    (bool ok, string reason, double variance, int z) = Evaluate(zone, portals, fixtures, waterMap, waterHeights, x, y, footprint, rule.MaxVariance, rule.MinimumGround, !rule.IgnoreFixtures);
                    if (!ok)
                    {
                        rejected[reason] = rejected.GetValueOrDefault(reason) + 1;
                        if (reason == "slope") smallestSlope = Math.Min(smallestSlope, variance);
                        continue;
                    }
                    if (best == null || variance < best.Value.Variance) best = ((int)Math.Round(x), (int)Math.Round(y), variance, z);
                }
                if (best is not { } accepted) continue;
                data.PlaceSite(site.KeepId, accepted.X, accepted.Y, accepted.Z);
                int nearbyFixtures = fixtures.Count(fixture => Distance(accepted.X, accepted.Y, fixture.X, fixture.Y) <= footprint + SolidFixtureClearance);
                Log.Normal($"BG_KEEP_SITE zone={zoneId} keep={site.KeepId} x={accepted.X} y={accepted.Y} z={accepted.Z} variance={accepted.Variance:F0} ring={ring} radius={footprint} max_variance={rule.MaxVariance:F0} ground={(rule.MinimumGround ? "minimum" : "centre")} solid_fixtures_within_clearance={nearbyFixtures}");
                return true;
            }
            string tally = string.Join(" ", rejected.Select(pair => $"{pair.Key}={pair.Value}"));
            Log.Error($"BG_KEEP_SITE_UNAVAILABLE zone={zoneId} keep={site.KeepId} reason=no_flat_clear_spot_within_{SearchLimit} rejected=[{tally}] smallest_slope={smallestSlope:F0} extent={footprint}");
            return false;
        }

        private static (bool Ok, string Reason, double Variance, int Z) Evaluate(Zone2 zone, List<(int X, int Y)> portals,
            List<(double X, double Y)> fixtures, byte[,] waterMap, int[] waterHeights, double x, double y, int footprint,
            double maxVariance, bool minimumGround, bool checkFixtures)
        {
            int gx = (int)Math.Round(x), gy = (int)Math.Round(y);
            if (!zone.Contains(new Vector3(gx, gy, 0))) return (false, "outside_zone", 0, 0);
            foreach ((int px, int py) in portals)
                if (Distance(gx, gy, px, py) < PortalClearance) return (false, "portal", 0, 0);

            double min = double.MaxValue, max = double.MinValue;
            for (int dx = -footprint; dx <= footprint; dx += GroundSampleStep)
                for (int dy = -footprint; dy <= footprint; dy += GroundSampleStep)
                {
                    if (dx * dx + dy * dy > footprint * footprint) continue;
                    double sx = gx + dx, sy = gy + dy;
                    if (IsWater(zone, waterMap, waterHeights, sx, sy)) return (false, "water", 0, 0);
                    double z = zone.GetNearestGround((float)(sx - zone.XOffset), (float)(sy - zone.YOffset), 0).Z;
                    min = Math.Min(min, z);
                    max = Math.Max(max, z);
                }
            double variance = max - min;
            if (variance > maxVariance) return (false, "slope", variance, 0);
            if (checkFixtures)
                foreach ((double fx, double fy) in fixtures)
                    if (Distance(gx, gy, fx, fy) <= footprint + SolidFixtureClearance) return (false, "fixtures", variance, 0);
            int groundZ = (int)zone.GetNearestGround((float)(gx - zone.XOffset), (float)(gy - zone.YOffset), 0).Z;
            return (true, string.Empty, variance, minimumGround ? (int)Math.Floor(min) : groundZ);
        }

        private static bool IsWater(Zone2 zone, byte[,] waterMap, int[] waterHeights, double x, double y)
        {
            if (waterMap == null || waterHeights == null) return false;
            int mapX = Math.Clamp((int)((x - zone.XOffset) / WaterCellSize), 0, waterMap.GetLength(0) - 1);
            int mapY = Math.Clamp((int)((y - zone.YOffset) / WaterCellSize), 0, waterMap.GetLength(1) - 1);
            int type = waterMap[mapX, mapY];
            return type != WaterEmpty && type < waterHeights.Length;
        }

        private static List<(double X, double Y)> ReadSolidFixtures(Zone2 zone)
        {
            var fixtures = new List<(double X, double Y)>();
            using Stream stream = ClientData.FindCSV(zone, "fixtures.csv");
            if (stream == null) return fixtures;
            using var reader = new StreamReader(stream);
            reader.ReadLine();
            reader.ReadLine();
            string input;
            while ((input = reader.ReadLine()) != null)
            {
                if (input.Trim() == string.Empty) continue;
                string[] fixture = input.Split(',');
                if (fixture.Length < 10) continue;
                bool collide = fixture[8] != "0";
                int radius = int.Parse(fixture[9], CultureInfo.InvariantCulture);
                if (!collide && radius == 0) continue;
                double x = float.Parse(fixture[3], CultureInfo.InvariantCulture) + zone.XOffset;
                double y = float.Parse(fixture[4], CultureInfo.InvariantCulture) + zone.YOffset;
                fixtures.Add((x, y));
            }
            return fixtures;
        }

        private static double Distance(double ax, double ay, double bx, double by) =>
            Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
    }

    internal sealed partial class Zone2Obj
    {
        // Count of triangles written by AddModelToObj for the current zone. Used for the piece log.
        private int _exportedTriangleCount;

        private const int BattlegroundFixtureBase = 9000;
        private const double BattlegroundDoorHalfWidth = 20;
        private const double BattlegroundDoorHeight = 180;
        private const double BattlegroundDoorBase = 16;
        // AddCylinder's world radius is 4 x its scale argument (the unit circle is 0.4 wide, scaled by 10), so 16 gives 64 units.
        private const int BattlegroundCylinderScale = 16;
        private static readonly double[] BattlegroundWallSamples = { -74, 22, 74 };

        private sealed class BattlegroundPiece
        {
            public BattlegroundKeepComponent Component;
            public (int X, int Y) Centre;
            public double AngleDegrees;
            public string Name;
            public NiFile Nif;
            public List<(double X, double Y)> ServerDoors;
            public int FixtureId;
            public List<int> VolumeOnlyDoors = new();
        }

        private void ExportBattlegroundKeeps()
        {
            BattlegroundKeepData data = BattlegroundKeepData.Current;
            if (data == null) return;
            foreach (BattlegroundKeepSite site in data.Sites.Where(candidate => BattlegroundKeepData.ZoneForRegion(candidate.Region) == Zone.ID))
                ExportBattlegroundKeep(data, site);
        }

        private void ExportBattlegroundKeep(BattlegroundKeepData data, BattlegroundKeepSite keep)
        {
            if (!data.Templates.TryGetValue(keep.Template, out List<BattlegroundKeepComponent> components))
            {
                Log.Error($"BG_KEEP_TEMPLATE_MISSING zone={Zone.ID} keep={keep.KeepId} template={keep.Template}");
                return;
            }

            var pieces = new List<BattlegroundPiece>();
            foreach (BattlegroundKeepComponent component in components)
            {
                string name = data.PieceForSkin(component.Skin);
                pieces.Add(new BattlegroundPiece
                {
                    Component = component,
                    Centre = BattlegroundKeepData.ComponentCentre(keep, component),
                    AngleDegrees = BattlegroundKeepData.PieceAngle(keep, component),
                    Name = name,
                    Nif = name == null ? null : LoadFrontierPiece(name),
                    ServerDoors = data.ServerDoors(keep, component),
                    FixtureId = BattlegroundFixtureBase + component.Id,
                });
            }

            // Orientation. A variant is valid when at least one server door has a NIF door node within the tolerance.
            // Among valid variants the most matched doors win, then the smallest worst delta over those doors; ties keep the baseline.
            // A server door with no NIF door node within the tolerance becomes a Door volume only.
            bool fallback = false;
            (bool Mirror, bool HeadingSign) final = (true, true);
            var doorPieces = pieces.Where(candidate => candidate.ServerDoors.Count > 0).ToList();
            if (doorPieces.Count == 0)
                Log.Normal($"BG_KEEP_NO_SERVER_DOORS zone={Zone.ID} keep={keep.KeepId} template={keep.Template}");
            else if (doorPieces.Any(candidate => candidate.Nif == null))
            {
                Log.Warn($"BG_KEEP_FALLBACK zone={Zone.ID} keep={keep.KeepId} reason=door_piece_missing");
                fallback = true;
            }
            else
            {
                (bool Mirror, bool HeadingSign)? best = null;
                int bestMatched = 0;
                double bestWorst = double.MaxValue;
                foreach ((bool mirror, bool headingSign) in BattlegroundKeepData.OrientationVariants)
                {
                    int matched = 0;
                    double worst = 0;
                    foreach (BattlegroundPiece piece in doorPieces)
                    {
                        List<(float X, float Y)> centres = CollectDoorCentres(piece.Nif, PieceMatrixFor(keep, piece, mirror, headingSign));
                        foreach ((double x, double y) in piece.ServerDoors)
                        {
                            double delta = NearestCentre(centres, x, y);
                            if (delta > BattlegroundKeepData.DoorTolerance) continue;
                            matched++;
                            worst = Math.Max(worst, delta);
                        }
                    }
                    bool valid = matched > 0;
                    Log.Normal($"BG_KEEP_VARIANT zone={Zone.ID} keep={keep.KeepId} mirror={mirror} headingSign={headingSign} matched={matched} worst={worst:F0} valid={valid}");
                    // Most matched doors first, then the smallest worst delta. A variant matching one door cannot beat the baseline's three.
                    if (!valid || (best.HasValue && (matched < bestMatched || (matched == bestMatched && worst >= bestWorst)))) continue;
                    best = (mirror, headingSign);
                    bestMatched = matched;
                    bestWorst = worst;
                }
                if (best is { } chosen)
                    final = chosen;
                else
                {
                    Log.Warn($"BG_KEEP_FALLBACK zone={Zone.ID} keep={keep.KeepId} reason=orientation_no_variant_matches");
                    fallback = true;
                }
            }

            if (!fallback)
            {
                foreach (BattlegroundPiece piece in doorPieces)
                {
                    List<(float X, float Y)> centres = CollectDoorCentres(piece.Nif, PieceMatrixFor(keep, piece, final.Mirror, final.HeadingSign));
                    string nifCentres = string.Join(";", centres.Select(centre => $"{centre.X:F0},{centre.Y:F0}"));
                    string serverDoors = string.Join(";", piece.ServerDoors.Select(door => $"{door.X:F0},{door.Y:F0}"));
                    Log.Normal($"BG_KEEP_NIF_DOORS zone={Zone.ID} keep={keep.KeepId} comp={piece.Component.Id} nif={piece.Name} nodes={centres.Count} nif_centres={nifCentres} server_doors={serverDoors}");
                    for (int door = 0; door < piece.ServerDoors.Count; door++)
                    {
                        (double x, double y) = piece.ServerDoors[door];
                        double delta = NearestCentre(centres, x, y);
                        Log.Normal($"BG_KEEP_DOOR_MATCH zone={Zone.ID} keep={keep.KeepId} comp={piece.Component.Id} door={door} delta={(delta == double.MaxValue ? -1 : delta):F0}");
                        if (delta <= BattlegroundKeepData.DoorTolerance) continue;
                        piece.VolumeOnlyDoors.Add(door);
                        Log.Normal($"BG_KEEP_DOOR_VOLUME_ONLY zone={Zone.ID} keep={keep.KeepId} comp={piece.Component.Id} door={door}");
                    }
                }
            }

            foreach (BattlegroundPiece piece in pieces)
            {
                if (fallback || piece.Nif == null)
                {
                    ExportBattlegroundFallback(keep, piece);
                    Log.Normal($"BG_KEEP_PIECE zone={Zone.ID} keep={keep.KeepId} comp={piece.Component.Id} nif={piece.Name ?? "none"} tris=0");
                    continue;
                }
                Matrix4 matrix = PieceMatrixFor(keep, piece, final.Mirror, final.HeadingSign);
                int before = _exportedTriangleCount;
                AddModelToObj(piece.Nif, matrix, new[] { "collide", "collidee", "collision" }, true, fixtureId: piece.FixtureId);
                int triangles = _exportedTriangleCount - before;
                if (triangles > 0)
                {
                    ExtractDoor(piece.Nif, matrix, piece.FixtureId);
                    double theta = piece.AngleDegrees * Math.PI / 180.0;
                    foreach (int door in piece.VolumeOnlyDoors)
                        WriteBattlegroundDoorBox(keep, piece.ServerDoors[door].X, piece.ServerDoors[door].Y, theta);
                }
                else
                {
                    Log.Warn($"BG_KEEP_FALLBACK zone={Zone.ID} keep={keep.KeepId} comp={piece.Component.Id} reason=piece_has_no_triangles");
                    ExportBattlegroundFallback(keep, piece);
                }
                Log.Normal($"BG_KEEP_PIECE zone={Zone.ID} keep={keep.KeepId} comp={piece.Component.Id} nif={piece.Name} tris={triangles}");
            }
        }

        private Matrix4 PieceMatrixFor(BattlegroundKeepSite keep, BattlegroundPiece piece, bool mirror, bool headingSign) =>
            BattlegroundKeepData.PieceMatrix(piece.Centre.X, piece.Centre.Y, keep.Z, piece.AngleDegrees, mirror, headingSign);

        private static double NearestCentre(List<(float X, float Y)> centres, double x, double y)
        {
            double best = double.MaxValue;
            foreach ((float cx, float cy) in centres)
                best = Math.Min(best, Math.Sqrt((cx - x) * (cx - x) + (cy - y) * (cy - y)));
            return best;
        }

        private NiFile LoadFrontierPiece(string pieceName)
        {
            string nif = pieceName + ".nif";
            using Stream stream = ClientData.OpenExactly($"frontiers/nifs/{pieceName.ToLowerInvariant()}.npk/{nif}");
            if (stream == null) return null;
            using var reader = new BinaryReader(stream);
            var model = new NiFile(reader, nif);
            return model.Loaded ? model : null;
        }

        /// <summary>Door centres as ExtractDoor finds them: every door node's minimum-area rectangle centre.</summary>
        private List<(float X, float Y)> CollectDoorCentres(NiFile model, Matrix4 worldMatrix)
        {
            var doorVertices = new Dictionary<string, List<Vector3>>();
            foreach (var obj in model.ObjectsByRef.Values)
            {
                var avNode = obj as NiAVObject;
                if (avNode == null) continue;
                if (!IsMatched(avNode, new[] { "visible", "collidee", "collide" })) continue;
                string doorName = FindMatchRegex(avNode, DoorRegex);
                if (doorName == string.Empty) continue;
                Vector3[] vertices = null;
                Triangle[] triangles = null;
                TryExtractTriShape(obj, worldMatrix, false, false, ref vertices, ref triangles);
                if (vertices == null)
                    TryExtractTriStrips(obj, worldMatrix, false, false, ref vertices, ref triangles);
                if (vertices == null) continue;
                if (!doorVertices.TryGetValue(doorName, out List<Vector3> list))
                    doorVertices[doorName] = list = new List<Vector3>();
                list.AddRange(vertices);
            }
            var centres = new List<(float X, float Y)>();
            foreach (List<Vector3> vertices in doorVertices.Values)
            {
                var points = vertices.Select(vertex => new PointF(vertex.X, vertex.Y)).ToArray();
                RotatedRect box = CvInvoke.MinAreaRect(points);
                centres.Add((box.Center.X, box.Center.Y));
            }
            return centres;
        }

        /// <summary>
        /// Fallback for a piece without usable geometry: cylinders every 96 units along a wall's footprint, and a
        /// Door volume at each server door position so the server still registers blocking doors.
        /// </summary>
        private void ExportBattlegroundFallback(BattlegroundKeepSite keep, BattlegroundPiece piece)
        {
            double theta = piece.AngleDegrees * Math.PI / 180.0;
            double dx = Math.Cos(theta), dy = Math.Sin(theta);
            bool wall = piece.Component.Skin != 0 && piece.Component.Skin != 10 && piece.Component.Skin != 11;
            if (wall)
            {
                foreach (double t in BattlegroundWallSamples)
                    AddCylinder(Matrix4.CreateTranslation((float)(piece.Centre.X + dx * t), (float)(piece.Centre.Y + dy * t), keep.Z), BattlegroundCylinderScale);
            }
            foreach ((double x, double y) in piece.ServerDoors)
                WriteBattlegroundDoorBox(keep, x, y, theta);
        }

        private void WriteBattlegroundDoorBox(BattlegroundKeepSite keep, double x, double y, double theta)
        {
            float zMin = (float)(keep.Z - BattlegroundDoorBase), zMax = (float)(keep.Z + BattlegroundDoorHeight);
            double c = Math.Cos(theta), s = Math.Sin(theta);
            double h = BattlegroundDoorHalfWidth;
            (double U, double V)[] corners = { (-h, -h), (h, -h), (h, h), (-h, h) };
            GeomSetWriter.WriteConvexVolume(corners.Length, zMin, zMax, CEM.GeomSetWriter.eAreas.Door);
            foreach ((double u, double v) in corners)
                GeomSetWriter.WriteConvexVolumeVertex(new Vector3((float)(x + u * c - v * s), (float)(y + u * s + v * c), zMin));
        }
    }
}
