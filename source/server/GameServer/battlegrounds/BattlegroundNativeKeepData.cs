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
        // Relocating garrison members that no camp reaches may spend this many path queries per keep start.
        private const int MaximumRelocationQueries = 1200;

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
                // A keep with no gates at all (Lion's Den) has nothing to close and takes the ungated placement below.
                if (GatesCannotBeClosed(keep.Doors.Count, keep.Doors.Values.Any(door => door.State == eDoorState.Closed)))
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
                    // The default route is tested first: a point no camp reaches costs one query per camp, not two.
                    if (!Routed(origin, point, nav.DefaultFilters) || Routed(origin, point, nav.BlockingDoorAvoidanceFilters)) continue;
                    return true;
                }
                return false;
            }

            // Garrison candidates, nearest the keep floor first, so the query budgets below are spent near the keep.
            List<Vector3> candidates = GarrisonCandidates(keep, zone, doors, searchRadius, doorZ);

            // A member whose spawn point no camp reaches moves to the nearest reachable point of the keep; reachable members stay.
            bool lordMoved = RelocateUnreachableGarrison(keep, region, zone, outside, candidates);

            // A native lord is never moved while it is reachable. It is only checked, and a failed proof is logged.
            GuardLord existing = keep.Guards.Values.OfType<GuardLord>().FirstOrDefault();
            if (existing != null)
            {
                if (!lordMoved && !Gated(new Vector3(existing.X, existing.Y, existing.Z)))
                    Log.Warn($"BATTLEGROUND_KEEP_LORD_OUTSIDE region={region} keep={keep.KeepID}");
                return true;
            }

            var passing = new List<(Vector3 Point, float DoorDistance)>();
            var notGated = new List<Vector3>();
            foreach (Vector3 candidate in candidates)
            {
                if (queries >= MaximumPathQueries) break;
                if (doors.Count == 0 || !Gated(candidate)) { notGated.Add(candidate); continue; }
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

            if (passing.Count == 0) return PlaceUngatedGarrison(keep, region, zone, outside, candidates);

            var ordered = passing.OrderByDescending(point => point.Point.Z).ThenByDescending(point => point.DoorDistance).ToList();
            Vector3 lordPoint = ordered[0].Point;
            if (!TrySpawnGarrison(keep, region, lordPoint, ordered.Select(point => point.Point).ToList(), out int guards))
                return Unavailable(keep, "load_failed");
            string tag = row.CreateInfo.StartsWith(NativePrefix, StringComparison.Ordinal) ? "BATTLEGROUND_NATIVE_KEEP_READY" : "BATTLEGROUND_KEEP_READY";
            Log.Info($"{tag} region={region} keep={keep.KeepID} lord={(int)lordPoint.X},{(int)lordPoint.Y},{(int)lordPoint.Z} guards={guards}");
            return true;
        }

        /// <summary>True when a keep has gates but none is closed; a keep without gates has nothing to close.</summary>
        public static bool GatesCannotBeClosed(int gateCount, bool anyClosed) => gateCount > 0 && !anyClosed;

        /// <summary>No two garrison members stand closer than this.</summary>
        public const float GuardSpacing = 200f;

        /// <summary>The first point, in preference order, at least <paramref name="spacing"/> from every used point; null when none.</summary>
        public static Vector3? FirstSpacedPoint(IEnumerable<Vector3> ordered, IReadOnlyList<Vector3> used, float spacing = GuardSpacing)
        {
            foreach (Vector3 candidate in ordered)
                if (used.All(other => Vector3.Distance(other, candidate) >= spacing)) return candidate;
            return null;
        }

        /// <summary>Which move a garrison member gets: stay, walk to a reachable point, or stay because no reachable point exists.</summary>
        public enum GarrisonMoveDecision { Keep, Relocate, Stranded }

        /// <summary>Path queries one reachability probe can spend in the worst case: one per camp, and at least one.</summary>
        public static int ProbeCost(int campCount) => Math.Max(1, campCount);

        /// <summary>
        /// Stay when a camp reaches the member's home or the budget could not check it (null): a member is never moved without evidence.
        /// Otherwise relocate when a reachable point was found, and report the member as stranded when none was.
        /// </summary>
        public static GarrisonMoveDecision DecideGarrisonMove(bool? homeReachable, bool targetFound) =>
            homeReachable != false ? GarrisonMoveDecision.Keep : targetFound ? GarrisonMoveDecision.Relocate : GarrisonMoveDecision.Stranded;

        /// <summary>Reachability test that spends path queries from the shared budget (passed by reference).</summary>
        public delegate bool ReachabilityProbe(Vector3 point, ref int budget);

        /// <summary>Nearest-first ordering: the three-dimensional distance ranks a floor above or below the origin after the floor level.</summary>
        public static List<Vector3> OrderByDistance(IEnumerable<Vector3> candidates, Vector3 origin) =>
            candidates.OrderBy(point => Vector3.DistanceSquared(point, origin)).ToList();

        /// <summary>
        /// The first point, in the given order, that is at least <paramref name="spacing"/> from every used point and that the probe reaches.
        /// A probe starts only while the budget covers its worst case, so an exhausted budget ends the search with null.
        /// </summary>
        public static Vector3? FirstReachableSpacedPoint(IEnumerable<Vector3> ordered, IReadOnlyList<Vector3> used, ReachabilityProbe probe,
            int probeCost, ref int budget, float spacing = GuardSpacing)
        {
            foreach (Vector3 candidate in ordered)
            {
                if (budget < probeCost) return null;
                if (used.Any(other => Vector3.Distance(other, candidate) < spacing)) continue;
                if (probe(candidate, ref budget)) return candidate;
            }
            return null;
        }

        private static ReachabilityProbe CampProbe(Zone zone, IReadOnlyList<Point3D> outside) =>
            (Vector3 point, ref int budget) => ReachableFromCamps(point, zone, outside, ref budget);

        // Keep-area points for garrison members, nearest the keep floor first: a grid over the keep radius at three lifts
        // (ground, +600, +1200). A point beside a door is excluded, and of two points within 32 units the nearer one is kept.
        private static List<Vector3> GarrisonCandidates(AbstractGameKeep keep, Zone zone, IReadOnlyList<GameKeepDoor> doors, int searchRadius, double doorZ)
        {
            var nav = PathfindingProvider.Instance;
            var origin = new Vector3(keep.X, keep.Y, (float)doorZ);
            var raw = new List<Vector3>();
            int steps = (searchRadius + SearchStep - 1) / SearchStep;
            foreach (int lift in new[] { 0, 600, 1200 })
                for (int i = -steps; i <= steps; i++)
                    for (int j = -steps; j <= steps; j++)
                    {
                        int dx = i * SearchStep, dy = j * SearchStep;
                        if (dx * dx + dy * dy > searchRadius * searchRadius) continue;
                        Vector3? floor = nav.GetClosestPoint(zone, new Vector3(keep.X + dx, keep.Y + dy, (float)(doorZ + lift)), 64, 64, 256, nav.DefaultFilters);
                        if (!floor.HasValue) continue;
                        Vector3 point = floor.Value;
                        if (keep.Area?.IsContaining((int)point.X, (int)point.Y, (int)point.Z, false) != true) continue;
                        if (doors.Any(door => Vector2.Distance(new(door.X, door.Y), new(point.X, point.Y)) <= 250)) continue;
                        raw.Add(point);
                    }
            var candidates = new List<Vector3>();
            foreach (Vector3 point in OrderByDistance(raw, origin))
                if (!candidates.Any(existingPoint => Vector3.Distance(existingPoint, point) < 32)) candidates.Add(point);
            return candidates;
        }

        // Existing garrison: a member whose spawn point no outside camp reaches moves at runtime to the nearest reachable keep
        // point. The lord goes first and needs no spacing; retainers keep GuardSpacing from every other member. The spawn point
        // follows the move, so each respawn lands on the new point for this server run. Nothing is saved: the mob rows keep their
        // coordinates and the next start makes the same decision. Patrol members keep their route; merchants are not garrison.
        // Returns true when the lord moved.
        private static bool RelocateUnreachableGarrison(AbstractGameKeep keep, ushort region, Zone zone, IReadOnlyList<Point3D> outside, IReadOnlyList<Vector3> candidates)
        {
            var nav = PathfindingProvider.Instance;
            int budget = MaximumRelocationQueries;
            int probeCost = ProbeCost(outside.Count);
            ReachabilityProbe probe = CampProbe(zone, outside);

            // Pass 1: classify each member by its spawn point. A member the budget cannot check stays where it is.
            var used = new List<Vector3>();
            var moving = new List<(GameKeepGuard Guard, Vector3 Home)>();
            foreach (GameKeepGuard guard in keep.Guards.Values)
            {
                if (guard is GuardMerchant || guard.PatrolGroup != null) continue;
                Point3D spawn = guard.SpawnPoint;
                if (spawn == null || (spawn.X == 0 && spawn.Y == 0 && spawn.Z == 0)) continue; // never placed in the world
                var home = new Vector3(spawn.X, spawn.Y, spawn.Z);
                bool? reachable = null;
                if (budget >= probeCost)
                {
                    Vector3? floor = nav.GetClosestPoint(zone, home, 64, 64, 256, nav.DefaultFilters);
                    reachable = floor.HasValue && ReachableFromCamps(floor.Value, zone, outside, ref budget);
                }
                if (reachable == false) moving.Add((guard, home));
                else used.Add(home);
            }

            // Pass 2: the lord first, then the retainers, each to the nearest reachable keep point not yet taken.
            bool lordMoved = false;
            foreach ((GameKeepGuard guard, Vector3 home) in moving.OrderBy(member => member.Guard is GuardLord ? 0 : 1))
            {
                IReadOnlyList<Vector3> spacing = guard is GuardLord ? Array.Empty<Vector3>() : used;
                Vector3? target = FirstReachableSpacedPoint(OrderByDistance(candidates, home), spacing, probe, probeCost, ref budget);
                if (DecideGarrisonMove(false, target.HasValue) == GarrisonMoveDecision.Relocate)
                {
                    MoveGarrisonMember(guard, region, target.Value);
                    used.Add(target.Value);
                    if (guard is GuardLord)
                    {
                        lordMoved = true;
                        Log.Warn($"BATTLEGROUND_KEEP_LORD_RELOCATED region={region} keep={keep.KeepID} from={Describe(home)} to={Describe(target.Value)}");
                    }
                    else
                        Log.Warn($"BATTLEGROUND_KEEP_GUARD_RELOCATED region={region} keep={keep.KeepID} type={guard.GetType().Name} from={Describe(home)} to={Describe(target.Value)}");
                }
                else
                {
                    string reason = budget < probeCost ? "budget" : "no_reachable_point";
                    Log.Warn($"BATTLEGROUND_KEEP_GUARD_STRANDED region={region} keep={keep.KeepID} type={guard.GetType().Name} from={Describe(home)} reason={reason}");
                }
            }
            return lordMoved;
        }

        // A live member walks to the point now (MoveTo re-adds it at the new point, so the spawn point is set afterwards).
        // A dead member only takes the new spawn point and respawns there.
        private static void MoveGarrisonMember(GameKeepGuard guard, ushort region, Vector3 target)
        {
            var point = new Point3D((int)target.X, (int)target.Y, (int)target.Z);
            if (guard.IsAlive && guard.ObjectState == GameObject.eObjectState.Active)
                guard.MoveTo(region, point.X, point.Y, point.Z, guard.Heading);
            guard.SpawnPoint = point;
        }

        private static string Describe(Vector3 point) => $"{(int)point.X},{(int)point.Y},{(int)point.Z}";

        // Ungated fallback, used only when no candidate sits behind a closed gate. The lord takes the first candidate, nearest
        // the keep floor, that an outside camp reaches with the default filters; retainers take the reachable candidates nearest
        // the lord. The two searches have their own budgets, so a long unreachable run for the lord cannot starve the retainers.
        private static bool PlaceUngatedGarrison(AbstractGameKeep keep, ushort region, Zone zone, IReadOnlyList<Point3D> outside, IReadOnlyList<Vector3> candidates)
        {
            ReachabilityProbe probe = CampProbe(zone, outside);
            int probeCost = ProbeCost(outside.Count);
            int lordBudget = MaximumPathQueries;
            if (FirstReachableSpacedPoint(candidates, Array.Empty<Vector3>(), probe, probeCost, ref lordBudget) is not Vector3 lordPoint)
                return Unavailable(keep, "no_lord_point");

            int retainerBudget = MaximumPathQueries;
            var ordered = new List<Vector3>();
            foreach (Vector3 candidate in OrderByDistance(candidates.Where(point => point != lordPoint), lordPoint))
            {
                if (retainerBudget < probeCost) break;
                if (probe(candidate, ref retainerBudget)) ordered.Add(candidate);
            }
            if (!TrySpawnGarrison(keep, region, lordPoint, ordered, out int guards))
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
