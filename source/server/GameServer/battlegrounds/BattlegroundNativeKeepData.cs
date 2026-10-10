using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.Database;
using DOL.GS.Keeps;

namespace DOL.GS
{
    /// <summary>Restores only proved native fixtures missing from the clean world.</summary>
    public static class BattlegroundNativeKeepData
    {
        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(BattlegroundNativeKeepData));
        // Exported from the supported client's Cathal Hfrontkeep.nif fixtures.
        private static readonly (int Id, int X, int Y, int Z)[] CathalDoors =
        {
            (165181101, 584483, 557538, 5670),
            (165181102, 584963, 557538, 5670),
        };

        public static void EnsureDoors(BattlegroundDefinition definition, AbstractGameKeep keep)
        {
            if (definition?.RegionId != 165 || definition.ZoneId != 165 || keep?.KeepID != 5 ||
                keep.Region != 165 || keep.IsPortalKeep || keep.IsRelic ||
                !keep.Guards.Values.OfType<GuardLord>().Any()) return;
            Zone zone = WorldMgr.GetZone(165);
            var nav = PathfindingProvider.Instance;
            if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone) || !HasProvedApproach(definition, keep, zone)) return;
            foreach (var fixture in CathalDoors)
            {
                // Existing saved or live fixture state remains authoritative.
                if (DoorMgr.GetDoorByID(fixture.Id) != null || DOLDB<DbDoor>.SelectObject(
                    DB.Column("InternalID").IsEqualTo(fixture.Id)) != null) continue;
                Vector3 native = new(fixture.X, fixture.Y, fixture.Z);
                Vector3? floor = nav.GetClosestPoint(zone, native, 128, 128, 100, nav.DefaultFilters);
                if (WorldMgr.GetRegion(165).GetZone(fixture.X, fixture.Y) != zone ||
                    keep.Area?.IsContaining(fixture.X, fixture.Y, fixture.Z, false) != true || !floor.HasValue ||
                    Math.Abs(floor.Value.Z - fixture.Z) > 100 ||
                    Vector2.Distance(new(native.X, native.Y), new(floor.Value.X, floor.Value.Y)) > 128) continue;
                // Calculate HP through the real keep-door calculator, with its
                // native keep binding, before saving the first fixture row.
                GameKeepDoor prototype = new() { Component = new GameKeepComponent { Keep = keep } };
                int maximumHealth = prototype.MaxHealth;
                if (maximumHealth <= 0) continue;
                DbDoor row = new()
                {
                    InternalID = fixture.Id, Name = "Dun Orseo Keep Door", Type = 1,
                    X = fixture.X, Y = fixture.Y, Z = fixture.Z, Heading = 3072,
                    Level = keep.Level, Realm = (byte)keep.Realm, Health = maximumHealth,
                    IsPostern = false, State = (int)eDoorState.Closed,
                };
                if (!GameServer.Database.AddObject(row)) continue;
                if (!DoorMgr.LoadDoor(row) || DoorMgr.GetDoorByID(fixture.Id) is not GameKeepDoor door ||
                    door.Component?.Keep != keep || !door.IsAttackableDoor)
                {
                    Log.Warn($"BATTLEGROUND_NATIVE_DOOR_UNAVAILABLE region=165 door={fixture.Id} reason=native_load_failed");
                    continue;
                }
                door.Health = door.MaxHealth;
                door.State = eDoorState.Closed;
                door.SaveIntoDatabase();
                Log.Info($"BATTLEGROUND_NATIVE_DOOR_READY region=165 keep=5 door={fixture.Id}");
            }
        }
        // Murdaigean (251) has its native Hfrontkeep.nif and gate rows, but no keep row or lord in the clean world.
        public const int MurdaigeanKeepId = 139;
        private const int MurdaigeanRegion = 251;
        private const int MurdaigeanX = 33280;
        private const int MurdaigeanY = 38272;
        private const string NativePrefix = "offline-native-bg:";
        private const string PendingCreateInfo = "offline-native-bg:251:pending";
        private const string ReadyCreateInfo = "offline-native-bg:251";
        private const int NativeSearchRadius = 1100;
        private const int SearchStep = 128;
        private const int MaximumPathQueries = 400;

        public static DbKeep MurdaigeanKeepRow() => new()
        {
            KeepID = MurdaigeanKeepId, Name = "Murdaigean Keep", Region = MurdaigeanRegion,
            X = MurdaigeanX, Y = MurdaigeanY, Z = 3720, Heading = 0, Realm = 0, OriginalRealm = 0,
            Level = 1, BaseLevel = 29, KeepType = 0, SkinType = 0, ClaimedGuildName = string.Empty,
            CreateInfo = PendingCreateInfo,
        };

        // Runs before keeps load, so each battleground keep gets its area, doors and guards like any other keep.
        public static void EnsureKeepRows()
        {
            if (!BattlegroundCampaignPolicy.IsEnabled) return;
            EnsureMurdaigeanKeepRow();
            foreach (KeepSite site in BattlegroundKeepLayouts.Sites) EnsureSiteKeep(site);
            foreach (KeepSite blocked in BattlegroundKeepLayouts.BlockedSites)
                Log.Warn($"BATTLEGROUND_KEEP_BLOCKED region={blocked.Region} keep={blocked.KeepId} reason=no_flat_clear_site");
        }

        private static void EnsureMurdaigeanKeepRow()
        {
            if (GameServer.Database.SelectObject<DbKeep>(DB.Column("Region").IsEqualTo(MurdaigeanRegion).And(DB.Column("BaseLevel").IsLessThan(100))) != null) return;
            if (GameServer.Database.SelectObject<DbKeep>(DB.Column("KeepID").IsEqualTo(MurdaigeanKeepId)) != null)
            {
                Log.Warn($"BATTLEGROUND_NATIVE_KEEP_UNAVAILABLE region={MurdaigeanRegion} reason=id_conflict");
                return;
            }
            if (!GameServer.Database.AddObject(MurdaigeanKeepRow()))
            {
                Log.Warn($"BATTLEGROUND_NATIVE_KEEP_UNAVAILABLE region={MurdaigeanRegion} reason=keep_row_not_saved");
                return;
            }
            Log.Info($"BATTLEGROUND_NATIVE_KEEP_ROW_ADDED region={MurdaigeanRegion} keep={MurdaigeanKeepId}");
        }

        // A missing row is created from the site. Components are added only when the keep has none, so
        // existing component rows stay authoritative. A keep that gets components is marked pending, so
        // EnsureGarrison heals its doors and proves its lord position on the next keep start.
        private static void EnsureSiteKeep(KeepSite site)
        {
            BattlegroundDefinition definition = BattlegroundCampaignCatalog.Find(site.Region);
            if (definition == null) return;
            string pending = BattlegroundKeepLayouts.OfflineKeepPrefix + site.Region + BattlegroundKeepLayouts.PendingSuffix;
            DbKeep row = GameServer.Database.SelectObject<DbKeep>(DB.Column("KeepID").IsEqualTo(site.KeepId));
            if (row == null)
            {
                row = new DbKeep
                {
                    KeepID = site.KeepId, Name = site.Name, Region = site.Region,
                    X = site.X, Y = site.Y, Z = site.Z, Heading = (ushort)site.Heading, Realm = 0, OriginalRealm = 0,
                    Level = 1, BaseLevel = (byte)definition.MaxLevel, KeepType = 0, SkinType = 0, ClaimedGuildName = string.Empty,
                    CreateInfo = pending,
                };
                if (!GameServer.Database.AddObject(row))
                {
                    Log.Warn($"BATTLEGROUND_KEEP_UNAVAILABLE region={site.Region} keep={site.KeepId} reason=keep_row_not_saved");
                    return;
                }
                Log.Info($"BATTLEGROUND_KEEP_ROW_ADDED region={site.Region} keep={site.KeepId}");
            }
            else if (row.Region != site.Region)
            {
                Log.Warn($"BATTLEGROUND_KEEP_UNAVAILABLE region={site.Region} keep={site.KeepId} reason=id_conflict");
                return;
            }
            if (GameServer.Database.SelectObjects<DbKeepComponent>(DB.Column("KeepID").IsEqualTo(site.KeepId)).Count != 0) return;

            if (!(row.CreateInfo ?? string.Empty).StartsWith(BattlegroundKeepLayouts.OfflineKeepPrefix, StringComparison.Ordinal))
            {
                row.CreateInfo = pending;
                GameServer.Database.SaveObject(row);
            }
            int count = 0;
            foreach (ComponentSpec spec in BattlegroundKeepLayouts.Template(site.Template))
            {
                if (GameServer.Database.AddObject(new DbKeepComponent(spec.Id, spec.Skin, spec.X, spec.Y, spec.Heading, 0, 3200, site.KeepId, pending)))
                    count++;
            }
            Log.Info($"BATTLEGROUND_KEEP_COMPONENTS_ADDED region={site.Region} keep={site.KeepId} count={count}");
        }

        public static bool IsOfflineKeep(AbstractGameKeep keep)
        {
            string info = keep?.DBKeep?.CreateInfo ?? string.Empty;
            return info.StartsWith(NativePrefix, StringComparison.Ordinal) ||
                info.StartsWith(BattlegroundKeepLayouts.OfflineKeepPrefix, StringComparison.Ordinal);
        }

        // Garrison search reach: the native Murdaigean keep keeps its historical radius; server-built keeps scale with their template.
        public static int GarrisonSearchRadius(AbstractGameKeep keep)
        {
            KeepSite site = keep == null ? null : BattlegroundKeepLayouts.FindSite(keep.KeepID);
            return site == null ? NativeSearchRadius : BattlegroundKeepLayouts.SearchRadius(site.Template);
        }

        // Places the lord on a point proved to sit behind the closed gates: an outside camp
        // reaches it with the default filters and not with the blocking-door filters. When no
        // such point exists, the ungated fallback places it inside the keep instead (logged as a warning).
        public static bool EnsureGarrison(BattlegroundDefinition d, AbstractGameKeep keep, IReadOnlyList<Point3D> outside, int searchRadius)
        {
            if (d == null || keep == null || outside == null || outside.Count == 0 || (ushort)keep.Region != d.RegionId || !IsOfflineKeep(keep)) return false;
            DbKeep row = keep.DBKeep;
            ushort region = d.RegionId;
            if (row.CreateInfo.EndsWith(BattlegroundKeepLayouts.PendingSuffix, StringComparison.Ordinal))
            {
                foreach (GameKeepDoor door in keep.Doors.Values)
                {
                    door.Health = door.MaxHealth;
                    door.State = eDoorState.Closed;
                    door.SaveIntoDatabase();
                }
                if (!keep.Doors.Values.Any(door => door.State == eDoorState.Closed))
                    return Unavailable(keep, "no_closed_door");
                row.CreateInfo = row.CreateInfo[..^BattlegroundKeepLayouts.PendingSuffix.Length];
                GameServer.Database.SaveObject(row);
            }

            Zone zone = WorldMgr.GetZone(d.ZoneId);
            var nav = PathfindingProvider.Instance;
            List<GameKeepDoor> doors = keep.Doors.Values.ToList();
            if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone) || keep.Area == null)
                return Unavailable(keep, "no_navmesh_or_area");

            // A keep without gates has no gated point at all; it takes the ungated placement below.
            double doorZ = doors.Count == 0 ? keep.Z : doors.Average(door => door.Z);
            var nodes = new WrappedPathfindingNode[512];
            int queries = 0;
            bool Routed(Vector3 start, Vector3 end, EDtPolyFlags[] filters)
            {
                queries++;
                return nav.GetPathStraight(zone, start, end, filters, nodes).Status == PathfindingStatus.PathFound;
            }
            bool Gated(Vector3 point)
            {
                foreach (Point3D camp in outside)
                {
                    if (queries + 2 > MaximumPathQueries) return false;
                    Vector3 origin = new(camp.X, camp.Y, camp.Z);
                    if (Routed(origin, point, nav.BlockingDoorAvoidanceFilters) || !Routed(origin, point, nav.DefaultFilters)) continue;
                    return true;
                }
                return false;
            }

            // A native lord is never moved. It is only checked, and a failed proof is logged.
            GuardLord existing = keep.Guards.Values.OfType<GuardLord>().FirstOrDefault();
            if (existing != null)
            {
                if (!Gated(new Vector3(existing.X, existing.Y, existing.Z)))
                    Log.Warn($"BATTLEGROUND_KEEP_LORD_OUTSIDE region={region} keep={keep.KeepID}");
                return true;
            }

            var candidates = new List<Vector3>();
            foreach (int lift in new[] { 1200, 600, 0 })
                for (int dx = -searchRadius; dx <= searchRadius; dx += SearchStep)
                    for (int dy = -searchRadius; dy <= searchRadius; dy += SearchStep)
                    {
                        if (dx * dx + dy * dy > searchRadius * searchRadius) continue;
                        Vector3? floor = nav.GetClosestPoint(zone, new Vector3(keep.X + dx, keep.Y + dy, (float)(doorZ + lift)), 64, 64, 256, nav.DefaultFilters);
                        if (!floor.HasValue) continue;
                        Vector3 point = floor.Value;
                        if (keep.Area?.IsContaining((int)point.X, (int)point.Y, (int)point.Z, false) != true) continue;
                        if (doors.Any(door => Vector2.Distance(new(door.X, door.Y), new(point.X, point.Y)) <= 250)) continue;
                        if (candidates.Any(existingPoint => Vector3.Distance(existingPoint, point) < 32)) continue;
                        candidates.Add(point);
                    }

            var passing = new List<(Vector3 Point, float DoorDistance)>();
            var notGated = new List<Vector3>();
            foreach (Vector3 candidate in candidates)
            {
                if (queries >= MaximumPathQueries) break;
                if (!Gated(candidate)) { notGated.Add(candidate); continue; }
                passing.Add((candidate, DoorDistance(doors, candidate)));
            }

            // Diagnostics, after the gate decision: a candidate outside the gate is either open (an outside
            // camp reaches it through the doors) or unreachable from every camp. Bounded like the gate proof.
            int probeBudget = MaximumPathQueries, open = 0, unreachable = 0;
            foreach (Vector3 candidate in notGated)
            {
                if (probeBudget <= 0) break;
                if (ReachableFromCamps(candidate, zone, outside, ref probeBudget)) open++;
                else unreachable++;
            }
            Log.Info($"BATTLEGROUND_KEEP_GARRISON_PROBE region={region} keep={keep.KeepID} candidates={candidates.Count} gated={passing.Count} open={open} unreachable={unreachable} queries={queries + MaximumPathQueries - probeBudget}");

            if (passing.Count == 0) return PlaceUngatedGarrison(keep, region, zone, outside, candidates, doors);

            var ordered = passing.OrderByDescending(point => point.Point.Z).ThenByDescending(point => point.DoorDistance).ToList();
            Vector3 lordPoint = ordered[0].Point;
            if (!TrySpawnGarrison(keep, region, lordPoint, ordered.Select(point => point.Point).ToList(), out int guards))
                return Unavailable(keep, "load_failed");
            string tag = row.CreateInfo.StartsWith(NativePrefix, StringComparison.Ordinal) ? "BATTLEGROUND_NATIVE_KEEP_READY" : "BATTLEGROUND_KEEP_READY";
            Log.Info($"{tag} region={region} keep={keep.KeepID} lord={(int)lordPoint.X},{(int)lordPoint.Y},{(int)lordPoint.Z} guards={guards}");
            return true;
        }

        /// <summary>No two garrison members stand closer than this.</summary>
        public const float GuardSpacing = 200f;

        /// <summary>The first point, in preference order, at least <paramref name="spacing"/> from every used point; null when none.</summary>
        public static Vector3? FirstSpacedPoint(IEnumerable<Vector3> ordered, IReadOnlyList<Vector3> used, float spacing = GuardSpacing)
        {
            foreach (Vector3 candidate in ordered)
                if (used.All(other => Vector3.Distance(other, candidate) >= spacing)) return candidate;
            return null;
        }

        // Ungated fallback, used only when no candidate sits behind a closed gate. The lord stands on the navmesh
        // point nearest the keep centre that lies inside the keep and that an outside camp reaches with the default
        // filters; retainers take the nearest other reachable candidates. The gated placement is always tried first.
        private static bool PlaceUngatedGarrison(AbstractGameKeep keep, ushort region, Zone zone, IReadOnlyList<Point3D> outside,
            IReadOnlyList<Vector3> candidates, IReadOnlyList<GameKeepDoor> doors)
        {
            var nav = PathfindingProvider.Instance;
            int budget = MaximumPathQueries;
            Vector3? centre = null;
            foreach (int lift in new[] { 0, 600, 1200 })
            {
                Vector3? floor = nav.GetClosestPoint(zone, new Vector3(keep.X, keep.Y, keep.Z + lift), 64, 64, 256, nav.DefaultFilters);
                if (!floor.HasValue || keep.Area?.IsContaining((int)floor.Value.X, (int)floor.Value.Y, (int)floor.Value.Z, false) != true) continue;
                if (!ReachableFromCamps(floor.Value, zone, outside, ref budget)) continue;
                centre = floor.Value;
                break;
            }
            if (centre is not Vector3 lordPoint) return Unavailable(keep, "no_lord_point");

            var ordered = new List<(Vector3 Point, float DoorDistance)>();
            foreach (Vector3 candidate in candidates.OrderBy(point => Vector2.DistanceSquared(new(point.X, point.Y), new(lordPoint.X, lordPoint.Y))))
            {
                if (budget <= 0) break;
                if (!ReachableFromCamps(candidate, zone, outside, ref budget)) continue;
                ordered.Add((candidate, DoorDistance(doors, candidate)));
            }
            if (!TrySpawnGarrison(keep, region, lordPoint, ordered.Select(point => point.Point).ToList(), out int guards))
                return Unavailable(keep, "load_failed");
            Log.Warn($"BATTLEGROUND_KEEP_LORD_UNGATED region={region} keep={keep.KeepID} lord={(int)lordPoint.X},{(int)lordPoint.Y},{(int)lordPoint.Z} guards={guards}");
            return true;
        }

        // Spawns the lord on lordPoint, then the retainers on the first spaced points of the ordered candidates.
        private static bool TrySpawnGarrison(AbstractGameKeep keep, ushort region, Vector3 lordPoint, IReadOnlyList<Vector3> ordered, out int guards)
        {
            guards = 0;
            string name = keep.Name;
            if (!SpawnKeepGuard<GuardLord>(keep, region, lordPoint, $"{name} Lord", GuardTemplateMgr.HighlanderMale)) return false;

            // A single tower keeps one archer; a full keep keeps two fighters, an archer and a healer.
            bool tower = BattlegroundKeepLayouts.IsTower(keep.KeepComponents.Select(component => component.Skin).ToList());
            var retainers = new List<Func<Vector3, bool>>();
            if (tower)
                retainers.Add(point => SpawnKeepGuard<GuardArcher>(keep, region, point, $"{name} Archer", GuardTemplateMgr.SaracenMale));
            else
            {
                retainers.Add(point => SpawnKeepGuard<GuardFighter>(keep, region, point, $"{name} Guard", GuardTemplateMgr.BritonMale));
                retainers.Add(point => SpawnKeepGuard<GuardFighter>(keep, region, point, $"{name} Guard", GuardTemplateMgr.IcconuMale));
                retainers.Add(point => SpawnKeepGuard<GuardArcher>(keep, region, point, $"{name} Archer", GuardTemplateMgr.SaracenMale));
                retainers.Add(point => SpawnKeepGuard<GuardHealer>(keep, region, point, $"{name} Healer", GuardTemplateMgr.AvalonianMale));
            }
            var used = new List<Vector3> { lordPoint };
            foreach (Func<Vector3, bool> retain in retainers)
            {
                if (FirstSpacedPoint(ordered, used) is not Vector3 place) continue;
                if (!retain(place)) continue;
                used.Add(place);
                guards++;
            }
            return true;
        }

        private static float DoorDistance(IReadOnlyList<GameKeepDoor> doors, Vector3 point) =>
            doors.Count == 0 ? float.MaxValue : doors.Min(door => Vector2.Distance(new(door.X, door.Y), new(point.X, point.Y)));

        // Default-filter reachability from any outside camp. Every path query spends the budget.
        private static bool ReachableFromCamps(Vector3 target, Zone zone, IReadOnlyList<Point3D> outside, ref int budget)
        {
            var nav = PathfindingProvider.Instance;
            var nodes = new WrappedPathfindingNode[512];
            foreach (Point3D camp in outside)
            {
                if (budget <= 0) return false;
                budget--;
                Vector3 origin = new(camp.X, camp.Y, camp.Z);
                if (nav.GetPathStraight(zone, origin, target, nav.DefaultFilters, nodes).Status == PathfindingStatus.PathFound) return true;
            }
            return false;
        }

        private static bool SpawnKeepGuard<T>(AbstractGameKeep keep, ushort region, Vector3 point, string name, ushort model) where T : GameKeepGuard, new()
        {
            // A garrison guard is its own DbMob row; loading it binds it to the keep area it stands in.
            DbMob row = new()
            {
                ClassType = typeof(T).ToString(), Name = name, Guild = string.Empty, Region = region,
                X = (int)point.X, Y = (int)point.Y, Z = (int)point.Z, Heading = 0, Realm = 0, Level = 50,
                Model = model, Size = 50,
            };
            if (!GameServer.Database.AddObject(row)) return false;
            T guard = new();
            try
            {
                guard.LoadFromDatabase(row);
                if (guard.Component?.Keep == keep && guard.AddToWorld())
                {
                    guard.ChangeGuild();
                    return true;
                }
            }
            catch (Exception exception)
            {
                Log.Error("BATTLEGROUND_NATIVE_KEEP_GUARD_FAILED", exception);
            }
            keep.Guards.Remove(row.ObjectId);
            guard.Delete();
            GameServer.Database.DeleteObject(row);
            return false;
        }

        private static bool Unavailable(AbstractGameKeep keep, string reason)
        {
            string tag = IsNative(keep) ? "BATTLEGROUND_NATIVE_KEEP_UNAVAILABLE" : "BATTLEGROUND_KEEP_UNAVAILABLE";
            Log.Warn($"{tag} region={(ushort)keep.Region} keep={keep.KeepID} reason={reason}");
            return false;
        }

        private static bool IsNative(AbstractGameKeep keep) => (keep?.DBKeep?.CreateInfo ?? string.Empty).StartsWith(NativePrefix, StringComparison.Ordinal);

        private static bool HasProvedApproach(BattlegroundDefinition definition, AbstractGameKeep keep, Zone zone)
        {
            var nav = PathfindingProvider.Instance;
            GameLocation[] landings = BattlegroundCampaignCatalog.GetLandings(definition);
            Span<WrappedPathfindingNode> nodes = stackalloc WrappedPathfindingNode[512];
            foreach (GameLocation landing in landings)
            {
                Vector3 start = new(landing.X, landing.Y, landing.Z);
                double direction = Math.Atan2(keep.Y - landing.Y, keep.X - landing.X);
                foreach (int offset in new[] { 0, 1, -1, 2, -2, 3, -3, 4 })
                {
                    double angle = direction + offset * Math.PI / 4;
                    Vector3 ring = new(landing.X + (float)Math.Cos(angle) * 4500, landing.Y + (float)Math.Sin(angle) * 4500, landing.Z);
                    Vector3? camp = nav.GetClosestPoint(zone, ring, 128, 128, 2048, nav.DefaultFilters);
                    if (!camp.HasValue || Vector2.Distance(new(ring.X, ring.Y), new(camp.Value.X, camp.Value.Y)) > 128 ||
                        keep.CurrentRegion.GetAreasOfSpot(new Point3D(camp.Value.X, camp.Value.Y, camp.Value.Z))
                            .OfType<KeepArea>().Any(area => area.Keep?.IsPortalKeep == true) ||
                        nav.GetPathStraight(zone, start, camp.Value, nav.BlockingDoorAvoidanceFilters, nodes).Status != PathfindingStatus.PathFound) continue;
                    foreach (var fixture in CathalDoors)
                        for (int i = 0; i < 8; i++)
                        {
                            double approachAngle = i * Math.PI / 4;
                            Vector3 native = new(fixture.X + (float)Math.Cos(approachAngle) * 180,
                                fixture.Y + (float)Math.Sin(approachAngle) * 180, fixture.Z);
                            Vector3 approach = native;
                            if (nav.TrySnapToMesh(zone, ref approach, 100) && Vector3.Distance(native, approach) <= 100 &&
                                nav.GetPathStraight(zone, camp.Value, approach, nav.BlockingDoorAvoidanceFilters, nodes).Status == PathfindingStatus.PathFound)
                                return true;
                        }
                }
            }
            return false;
        }
    }
}
