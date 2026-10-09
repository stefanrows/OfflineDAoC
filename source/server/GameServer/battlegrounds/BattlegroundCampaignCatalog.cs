using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.Database;
using DOL.GS.Keeps;

namespace DOL.GS
{
    public sealed class BattlegroundDefinition
    {
        public ushort RegionId { get; }
        public ushort ZoneId { get; }
        public string Name { get; }
        public byte MinLevel { get; }
        public byte MaxLevel { get; }
        // Exclusive realm-level ceiling; zero means unrestricted.
        public byte MaxRealmLevel { get; }

        public BattlegroundDefinition(ushort regionId, ushort zoneId, string name, byte minLevel, byte maxLevel, byte maxRealmLevel)
        {
            RegionId = regionId; ZoneId = zoneId; Name = name;
            MinLevel = minLevel; MaxLevel = maxLevel; MaxRealmLevel = maxRealmLevel;
        }
    }

    public static class BattlegroundCampaignCatalog
    {
        // Native client zones.dat and world region metadata. Zone 242 is TestBG;
        // Leirvik is zone 254 in region 242. Never substitute one for the other.
        public static IReadOnlyList<BattlegroundDefinition> Definitions { get; } = Array.AsReadOnly(new[]
        {
            new BattlegroundDefinition(234, 234, "The Proving Grounds", 1, 4, 0),
            new BattlegroundDefinition(235, 235, "The Lion's Den", 5, 9, 0),
            new BattlegroundDefinition(236, 236, "The Hills of Claret", 10, 14, 5),
            new BattlegroundDefinition(237, 237, "Killaloe", 15, 19, 10),
            new BattlegroundDefinition(238, 238, "Thidranki", 20, 24, 15),
            new BattlegroundDefinition(251, 251, "Murdaigean", 25, 29, 20),
            new BattlegroundDefinition(240, 240, "Wilton", 30, 34, 25),
            new BattlegroundDefinition(241, 241, "Molvik", 35, 39, 30),
            new BattlegroundDefinition(242, 254, "Leirvik", 40, 44, 35),
            new BattlegroundDefinition(165, 165, "Cathal Valley", 45, 49, 40),
        });

        public static BattlegroundDefinition Find(ushort region) => Definitions.FirstOrDefault(x => x.RegionId == region);
        public static BattlegroundDefinition ForLevel(int level) => Definitions.FirstOrDefault(x => level >= x.MinLevel && level <= x.MaxLevel);
        public static AbstractGameKeep CentralKeep(BattlegroundDefinition definition) => definition == null ? null :
            GameServer.KeepManager.GetKeepsOfRegion(definition.RegionId).FirstOrDefault(x => !x.IsPortalKeep && !x.IsRelic);
        public static GameLocation[] GetLandings(BattlegroundDefinition definition)
        {
            if (definition == null) return Array.Empty<GameLocation>();
            Zone zone = WorldMgr.GetZone(definition.ZoneId);
            var nav = PathfindingProvider.Instance;
            if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return Array.Empty<GameLocation>();
            var result = new List<GameLocation>();
            foreach (AbstractGameKeep keep in GameServer.KeepManager.GetKeepsOfRegion(definition.RegionId)
                .Where(x => x.IsPortalKeep).OrderBy(x => x.KeepID))
            {
                Vector3 native = new(keep.X, keep.Y, keep.Z);
                Vector3 snapped = native;
                if (WorldMgr.GetRegion(definition.RegionId)?.GetZone(keep.X, keep.Y) != zone)
                    return Array.Empty<GameLocation>();
                if (!nav.TrySnapToMesh(zone, ref snapped, 100) || Vector3.Distance(native, snapped) > 100)
                {
                    // Some native keep rows store a structural height rather
                    // than their arrival floor. Derive the floor from this same
                    // native portal centre, never from a made-up destination.
                    Vector3? floor = nav.GetClosestPoint(zone, native, 100, 100, 2048, nav.DefaultFilters);
                    if (!floor.HasValue || Vector2.Distance(new(native.X, native.Y), new(floor.Value.X, floor.Value.Y)) > 100 ||
                        Math.Abs(native.Z - floor.Value.Z) > 2048) return Array.Empty<GameLocation>();
                    snapped = floor.Value;
                    Vector3 proved = snapped;
                    if (!nav.TrySnapToMesh(zone, ref proved, 100) || Vector3.Distance(snapped, proved) > 100)
                        return Array.Empty<GameLocation>();
                    snapped = proved;
                }
                if (keep.Area?.IsContaining((int)snapped.X, (int)snapped.Y, (int)snapped.Z, false) != true)
                    return Array.Empty<GameLocation>();
                result.Add(new GameLocation(keep.Name, definition.RegionId, (int)snapped.X, (int)snapped.Y, (int)snapped.Z, keep.Heading));
            }
            return result.ToArray();
        }

        public static void RegisterCaps(List<DbBattleground> caps)
        {
            foreach (BattlegroundDefinition definition in Definitions)
            {
                caps.RemoveAll(x => x.RegionID == definition.RegionId);
                caps.Add(new DbBattleground { RegionID = definition.RegionId, MinLevel = definition.MinLevel,
                    MaxLevel = definition.MaxLevel, MaxRealmLevel = definition.MaxRealmLevel });
            }
        }
    }
}
