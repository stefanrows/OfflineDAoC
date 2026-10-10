using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.Keeps;

namespace DOL.GS
{
    /// <summary>A portal keep service guard that takes a bracket level and never auto-aggroes.</summary>
    public interface IPortalKeepServiceGuard
    {
        /// <summary>Set before LoadFromDatabase, so the spell scaling of a caster uses the bracket level.</summary>
        void SetBracketLevel(byte level);
    }

    /// <summary>
    /// Eden-style portal keep caster. The portal keep is a sanctuary (AbstractGameKeep.IsPortalKeep:
    /// KeepManager.IsEnemy is false there), so the guard is a visual defender: aggro range 0 as well.
    /// </summary>
    public sealed class PortalKeepServiceCaster : GuardStaticCaster, IPortalKeepServiceGuard
    {
        private byte _bracketLevel;

        public override byte Level
        {
            get => _bracketLevel > 0 ? _bracketLevel : base.Level;
            set => base.Level = value;
        }

        public void SetBracketLevel(byte level) => _bracketLevel = level;

        protected override void SetAggression() => SetAggression(0, 0);
    }

    /// <summary>Eden-style portal keep fighter: a visual defender beside the gate. See <see cref="PortalKeepServiceCaster"/>.</summary>
    public sealed class PortalKeepServiceFighter : GuardFighter, IPortalKeepServiceGuard
    {
        private byte _bracketLevel;

        public override byte Level
        {
            get => _bracketLevel > 0 ? _bracketLevel : base.Level;
            set => base.Level = value;
        }

        public void SetBracketLevel(byte level) => _bracketLevel = level;

        protected override void SetAggression() => SetAggression(0, 0);
    }

    /// <summary>
    /// Eden-style portal keep hastener. It keeps FrontierHastener's behaviour: the realm speed buff for same-realm
    /// players who are not in combat. Only its aggression is disabled.
    /// </summary>
    public sealed class PortalKeepServiceHastener : FrontierHastener, IPortalKeepServiceGuard
    {
        private byte _bracketLevel;

        public override byte Level
        {
            get => _bracketLevel > 0 ? _bracketLevel : base.Level;
            set => base.Level = value;
        }

        public void SetBracketLevel(byte level) => _bracketLevel = level;

        protected override void SetAggression() => SetAggression(0, 0);
    }

    /// <summary>
    /// Adds the Eden portal keep service set (casters, fighters, a hastener and training dummies) to each campaign portal
    /// keep that has no service NPC (keep guard, merchant or hastener) within <see cref="SavedNpcRadius"/> units. Runs from BattlegroundCampaignManager.Initialize,
    /// after the rings, the landings and the campaign's monster check. The NPCs are created at runtime from in-memory rows
    /// that are never saved, so the database gets no mob rows and every start decides again.
    /// </summary>
    public static class BattlegroundPortalKeepServices
    {
        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(BattlegroundPortalKeepServices));

        /// <summary>A keep with a service NPC this close to its centre gets no service set. Just outside the ring corners
        /// (~630), so camp commanders, captains, monsters and stray siegemasters around the landing never count.</summary>
        public const int SavedNpcRadius = 700;

        private const int GuardSize = 50;
        private const int DummyModel = 34;
        private const int FloorXRange = 64, FloorYRange = 64, FloorZRange = 256;
        private const int PathNodeBuffer = 512;

        // Keep ids handled by this process, so a repeated call never adds a second set.
        private static readonly HashSet<ushort> Handled = new();

        /// <summary>Adds the service set to each server-built portal keep of the region. <paramref name="landings"/> are the arrival points, in keep id order.</summary>
        public static void EnsureRegion(BattlegroundDefinition definition, IReadOnlyList<GameLocation> landings)
        {
            if (definition == null || landings == null || landings.Count == 0 || !BattlegroundCampaignPolicy.IsEnabled) return;
            Zone zone = WorldMgr.GetZone(definition.ZoneId);
            // The same order and filter as BattlegroundCampaignCatalog.GetLandings, so each keep pairs with its own arrival point.
            List<AbstractGameKeep> portals = GameServer.KeepManager.GetKeepsOfRegion(definition.RegionId)
                .Where(keep => keep.IsPortalKeep).OrderBy(keep => keep.KeepID).ToList();
            if (portals.Count != landings.Count)
            {
                Log.Warn($"BATTLEGROUND_PORTAL_KEEP_SERVICES_SKIPPED region={definition.RegionId} keep=all reason=landing_count");
                return;
            }
            for (int i = 0; i < portals.Count; i++)
            {
                AbstractGameKeep keep = portals[i];
                // Native portal keeps (Cathal Valley, Murdaigean) keep their own client guards and get no set from here.
                if (!BattlegroundKeepLayouts.PortalSites.Any(site => site.Region == definition.RegionId && site.KeepId == keep.KeepID))
                    continue;
                try
                {
                    EnsureKeep(definition, zone, keep, landings[i]);
                }
                catch (Exception exception)
                {
                    Log.Error($"BATTLEGROUND_PORTAL_KEEP_SERVICES_FAILED region={definition.RegionId} keep={keep.KeepID}", exception);
                }
            }
        }

        private static void EnsureKeep(BattlegroundDefinition definition, Zone zone, AbstractGameKeep keep, GameLocation landing)
        {
            ushort region = definition.RegionId;
            if (!Handled.Add(keep.KeepID))
            {
                Log.Info($"BATTLEGROUND_PORTAL_KEEP_SERVICES_SKIPPED region={region} keep={keep.KeepID} reason=already_handled");
                return;
            }
            if (keep.Realm is not (eRealm.Albion or eRealm.Midgard or eRealm.Hibernia))
            {
                Log.Info($"BATTLEGROUND_PORTAL_KEEP_SERVICES_SKIPPED region={region} keep={keep.KeepID} reason=no_realm");
                return;
            }
            if (HasSavedNpc(keep, region))
            {
                Log.Info($"BATTLEGROUND_PORTAL_KEEP_SERVICES_SKIPPED region={region} keep={keep.KeepID} reason=has_npcs");
                return;
            }
            var nav = PathfindingProvider.Instance;
            if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
            {
                Log.Info($"BATTLEGROUND_PORTAL_KEEP_SERVICES_SKIPPED region={region} keep={keep.KeepID} reason=no_navmesh");
                return;
            }

            var start = new Vector3(landing.X, landing.Y, landing.Z);
            var nodes = new WrappedPathfindingNode[PathNodeBuffer];
            bool Enclosed(Vector3 point) =>
                nav.GetPathStraight(zone, start, point, nav.BlockingDoorAvoidanceFilters, nodes).Status == PathfindingStatus.PathFound;

            var centre = new Vector2(keep.X, keep.Y);
            var pieces = BattlegroundPortalKeepServicePlanner.RingPieces(keep.X, keep.Y, keep.Heading);
            // Each candidate must snap to the navmesh floor, else it is skipped.
            var floors = new List<Vector3>();
            foreach (Vector2 raw in BattlegroundPortalKeepServicePlanner.Candidates(centre))
            {
                Vector3? floor = nav.GetClosestPoint(zone, new Vector3(raw.X, raw.Y, landing.Z), FloorXRange, FloorYRange, FloorZRange, nav.DefaultFilters);
                if (floor.HasValue) floors.Add(floor.Value);
            }

            IReadOnlyList<PortalServicePoint> plan = BattlegroundPortalKeepServicePlanner.Plan(centre, pieces, floors, Enclosed);
            if (plan.Count == 0)
            {
                Log.Info($"BATTLEGROUND_PORTAL_KEEP_SERVICES_SKIPPED region={region} keep={keep.KeepID} reason=no_site");
                return;
            }

            byte bracketLevel = (byte)definition.MaxLevel;
            int casters = 0, fighters = 0, hastener = 0, dps = 0, hitback = 0;
            foreach (PortalServicePoint service in plan)
            {
                bool spawned = service.Role switch
                {
                    PortalServiceRole.Caster => SpawnGuard<PortalKeepServiceCaster>(keep, region, service.Point, GuardTemplateMgr.AvalonianMale, bracketLevel),
                    PortalServiceRole.Fighter => SpawnGuard<PortalKeepServiceFighter>(keep, region, service.Point,
                        fighters % 2 == 0 ? GuardTemplateMgr.BritonMale : GuardTemplateMgr.IcconuMale, bracketLevel),
                    PortalServiceRole.Hastener => SpawnGuard<PortalKeepServiceHastener>(keep, region, service.Point, HastenerModel(keep.Realm), bracketLevel),
                    PortalServiceRole.DpsDummy => SpawnDummy<DPSDummy>(region, service.Point),
                    PortalServiceRole.HitbackDummy => SpawnDummy<HitbackDummy>(region, service.Point),
                    _ => false,
                };
                if (!spawned) continue;
                switch (service.Role)
                {
                    case PortalServiceRole.Caster: casters++; break;
                    case PortalServiceRole.Fighter: fighters++; break;
                    case PortalServiceRole.Hastener: hastener++; break;
                    case PortalServiceRole.DpsDummy: dps++; break;
                    case PortalServiceRole.HitbackDummy: hitback++; break;
                }
            }
            int total = casters + fighters + hastener + dps + hitback;
            Log.Info($"BATTLEGROUND_PORTAL_KEEP_SERVICES region={region} keep={keep.KeepID} spawned={total} casters={casters} fighters={fighters} hastener={hastener} dummies={dps + hitback}");
        }

        /// <summary>True when a keep guard, merchant or hastener already stands within <see cref="SavedNpcRadius"/> of the keep.</summary>
        private static bool HasSavedNpc(AbstractGameKeep keep, ushort region)
        {
            var centre = new Vector2(keep.X, keep.Y);
            return WorldMgr.GetNPCsFromRegion(region).Any(npc => npc is GameKeepGuard or GameMerchant or GameHastener &&
                npc.ObjectState == GameObject.eObjectState.Active && Vector2.Distance(new(npc.X, npc.Y), centre) <= SavedNpcRadius);
        }

        private static ushort HastenerModel(eRealm realm) => realm switch
        {
            eRealm.Midgard => (ushort)eLivingModel.MidgardHastener,
            eRealm.Hibernia => (ushort)eLivingModel.HiberniaHastener,
            _ => (ushort)eLivingModel.AlbionHastener,
        };

        /// <summary>
        /// Creates one guard from an in-memory row. The row is never saved: LoadFromDatabase binds the guard to the keep
        /// area it stands in, which is the same path the garrison uses, without the row reaching the database.
        /// </summary>
        private static bool SpawnGuard<T>(AbstractGameKeep keep, ushort region, Vector3 point, ushort model, byte bracketLevel)
            where T : GameKeepGuard, IPortalKeepServiceGuard, new()
        {
            var row = new DbMob
            {
                ClassType = typeof(T).ToString(), Name = string.Empty, Guild = string.Empty, Region = region,
                X = (int)point.X, Y = (int)point.Y, Z = (int)point.Z, Heading = 0, Realm = (byte)keep.Realm,
                Level = bracketLevel, Model = model, Size = GuardSize,
            };
            var guard = new T();
            try
            {
                guard.SetBracketLevel(bracketLevel);
                guard.LoadFromDatabase(row);
                if (guard.Component?.Keep == keep && guard.AddToWorld())
                {
                    guard.ChangeGuild();
                    return true;
                }
            }
            catch (Exception exception)
            {
                Log.Error("BATTLEGROUND_PORTAL_KEEP_SERVICE_FAILED", exception);
            }
            keep.Guards.Remove(row.ObjectId);
            guard.Delete();
            return false;
        }

        /// <summary>Creates one training dummy from an in-memory row. Dummies are level 0 and realm 0, so bot PvE scans skip them.</summary>
        private static bool SpawnDummy<T>(ushort region, Vector3 point) where T : GameNPC, new()
        {
            var row = new DbMob
            {
                ClassType = typeof(T).ToString(), Name = string.Empty, Guild = string.Empty, Region = region,
                X = (int)point.X, Y = (int)point.Y, Z = (int)point.Z, Heading = 0, Realm = 0, Level = 0,
                Model = DummyModel, Size = GuardSize,
            };
            var dummy = new T();
            try
            {
                dummy.LoadFromDatabase(row);
                if (dummy.AddToWorld()) return true;
            }
            catch (Exception exception)
            {
                Log.Error("BATTLEGROUND_PORTAL_KEEP_SERVICE_FAILED", exception);
            }
            dummy.Delete();
            return false;
        }
    }
}
