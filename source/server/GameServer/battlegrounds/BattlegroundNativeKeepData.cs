using System;
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
