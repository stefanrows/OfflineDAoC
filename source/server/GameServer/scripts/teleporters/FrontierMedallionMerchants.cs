using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.Database;
using DOL.Events;
using DOL.Logging;

namespace DOL.GS.Scripts
{
    /// <summary>
    /// Gives each Midgard frontier porter a native medallion merchant when none is
    /// within the participation reach, so Midgard autonomous gamebots can buy the
    /// free battlegrounds medallion as Albion and Hibernia bots do. The merchant is
    /// the existing Gwulla OFMerchant, persisted as one Mob row. Only a missing
    /// source is added: a row created here loads as an ordinary mob on later starts.
    /// </summary>
    public static class FrontierMedallionMerchants
    {
        public const ushort MidgardFrontierRegion = 100;
        public const string MerchantName = "Gwulla";
        public const string MerchantGuild = "Medallion Merchant";
        public const string MerchantListId = "OFMerchant_Mid";
        // The Midgard look already used by the native Gwulla merchant.
        public const string MerchantEquipmentId = "a802abbe-c419-4fcd-9846-0ec353f25e1a";
        public const ushort MerchantModel = 215;
        public const int MerchantLevel = 75;
        public const int MerchantSize = 50;
        private const int SnapRange = 100;
        private const int MerchantZTolerance = 200;
        private static readonly Logger Log = LoggerManager.Create(typeof(FrontierMedallionMerchants));

        // The seeder reads the world once startup has settled: at the Started event the region's
        // objects and the porter's radius index are not yet complete, so a native seller was missed.
        public const int SeedDelayMilliseconds = 30_000;
        // The exact ClassType the seeder writes; the native seller is DOL.GS.GameMerchant.
        public const string SeederClassType = "DOL.GS.Scripts.OFMerchant";
        private static ECSGameTimer _seedTimer;

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            if (!BattlegroundCampaignPolicy.IsEnabled) return;
            _seedTimer?.Stop();
            _seedTimer = new ECSGameTimer(null, _ => { RunSeeder(); return 0; }, SeedDelayMilliseconds);
        }

        private static void RunSeeder()
        {
            try
            {
                EnsureMidgardMerchants();
            }
            catch (Exception exception)
            {
                Log.Error("FRONTIER_MEDALLION_MERCHANT_FAILED", exception);
            }
        }

        /// <summary>The seeder's own row: the exact class, the Gwulla name, the Midgard frontier and the medallion list.</summary>
        public static bool IsSeederDuplicate(string classType, string name, ushort region, string listId) =>
            classType == SeederClassType && name == MerchantName && region == MidgardFrontierRegion && listId == MerchantListId;

        /// <summary>
        /// Candidate merchant positions relative to the porter, in order of preference.
        /// Each lies about 500 units away; the first that snaps to the navmesh within
        /// the height tolerance and has a straight path from the porter wins.
        /// </summary>
        public static IEnumerable<(int dx, int dy)> MerchantOffsets()
        {
            yield return (-373, -383);
            yield return (373, -383);
            yield return (-373, 383);
            yield return (373, 383);
            yield return (-500, 0);
            yield return (500, 0);
        }

        /// <summary>
        /// Adds the Midgard medallion merchant beside each Midgard porter that has no seller in reach.
        /// Where a porter also has the native seller, a seeder-made duplicate in reach is removed.
        /// </summary>
        public static void EnsureMidgardMerchants()
        {
            GameNPC[] npcs = WorldMgr.GetNPCsFromRegion(MidgardFrontierRegion);
            foreach (OFTeleporter porter in npcs.OfType<OFTeleporter>()
                .Where(npc => npc.Realm == eRealm.Midgard && npc.ObjectState == GameObject.eObjectState.Active)
                .ToArray())
            {
                GameMerchant[] sellers = AutonomousBattlegroundParticipation.MedallionMerchantsInReach(porter);
                GameMerchant[] duplicates = sellers.Where(IsSeederDuplicateNpc).ToArray();
                if (duplicates.Length > 0 && sellers.Any(seller => !IsSeederDuplicateNpc(seller)))
                {
                    foreach (GameMerchant duplicate in duplicates) RemoveDuplicate(duplicate);
                    continue;
                }
                if (sellers.Length > 0) continue;
                AddMerchant(porter);
            }
        }

        // Reads the persisted row, so the test is the saved class, name, region and list rather than a runtime guess.
        private static bool IsSeederDuplicateNpc(GameMerchant merchant)
        {
            if (merchant?.InternalID == null) return false;
            DbMob row = GameServer.Database.FindObjectByKey<DbMob>(merchant.InternalID);
            return row != null && IsSeederDuplicate(row.ClassType, row.Name, row.Region, row.ItemsListTemplateID);
        }

        private static void RemoveDuplicate(GameMerchant duplicate)
        {
            string mob = duplicate.InternalID;
            try
            {
                duplicate.DeleteFromDatabase();
                duplicate.Delete();
            }
            catch (Exception exception)
            {
                Log.Error("FRONTIER_MEDALLION_MERCHANT_DUPLICATE_FAILED", exception);
                return;
            }
            Log.Info($"FRONTIER_MEDALLION_MERCHANT_DUPLICATE_REMOVED region={MidgardFrontierRegion} mob={mob}");
        }

        private static void AddMerchant(OFTeleporter porter)
        {
            if (!TryFindSpot(porter, out Vector3 spot, out string reason))
            {
                Log.Warn($"FRONTIER_MEDALLION_MERCHANT_UNAVAILABLE region={MidgardFrontierRegion} porter=\"{porter.Name}\" reason={reason}");
                return;
            }

            int x = (int)spot.X;
            int y = (int)spot.Y;
            int z = (int)spot.Z;
            var row = new DbMob
            {
                ClassType = typeof(OFMerchant).FullName,
                Name = MerchantName,
                Guild = MerchantGuild,
                X = x,
                Y = y,
                Z = z,
                Heading = new Point2D(x, y).GetHeading(porter.X, porter.Y),
                Speed = 0,
                Region = MidgardFrontierRegion,
                Model = MerchantModel,
                Size = MerchantSize,
                Level = MerchantLevel,
                Realm = (byte)eRealm.Midgard,
                Flags = (uint)GameNPC.eFlags.PEACE,
                EquipmentTemplateID = MerchantEquipmentId,
                ItemsListTemplateID = MerchantListId,
                RoamingRange = 0,
            };
            if (!GameServer.Database.AddObject(row))
            {
                Log.Warn($"FRONTIER_MEDALLION_MERCHANT_UNAVAILABLE region={MidgardFrontierRegion} porter=\"{porter.Name}\" reason=database_insert_failed");
                return;
            }

            var merchant = new OFMerchant();
            try
            {
                merchant.LoadFromDatabase(row);
                if (merchant.AddToWorld())
                {
                    Log.Info($"FRONTIER_MEDALLION_MERCHANT_ADDED region={MidgardFrontierRegion} porter=\"{porter.Name}\" x={x} y={y} z={z}");
                    return;
                }
                reason = "add_to_world_refused";
            }
            catch (Exception exception)
            {
                reason = "exception";
                Log.Error("FRONTIER_MEDALLION_MERCHANT_FAILED", exception);
            }

            // Roll back so the failed attempt leaves neither an NPC nor a row behind.
            try
            {
                if (merchant.ObjectState == GameObject.eObjectState.Active)
                    merchant.RemoveFromWorld();
                GameServer.Database.DeleteObject(row);
            }
            catch (Exception exception)
            {
                Log.Error("FRONTIER_MEDALLION_MERCHANT_ROLLBACK_FAILED", exception);
            }
            Log.Warn($"FRONTIER_MEDALLION_MERCHANT_UNAVAILABLE region={MidgardFrontierRegion} porter=\"{porter.Name}\" reason={reason}");
        }

        private static bool TryFindSpot(OFTeleporter porter, out Vector3 spot, out string reason)
        {
            spot = default;
            reason = null;
            Zone zone = porter.CurrentZone;
            Region region = porter.CurrentRegion;
            IPathfindingMgr nav = PathfindingProvider.Instance;
            if (zone == null || region == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
            {
                // No navmesh for this zone: the first offset at the porter's own height.
                (int dx, int dy) first = MerchantOffsets().First();
                spot = new(porter.X + first.dx, porter.Y + first.dy, porter.Z);
                return true;
            }

            Vector3 start = new(porter.X, porter.Y, porter.Z);
            Span<WrappedPathfindingNode> nodes = stackalloc WrappedPathfindingNode[512];
            foreach ((int dx, int dy) in MerchantOffsets())
            {
                Vector3 candidate = new(porter.X + dx, porter.Y + dy, porter.Z);
                if (!nav.TrySnapToMesh(zone, ref candidate, SnapRange)) continue;
                if (region.GetZone((int)candidate.X, (int)candidate.Y) != zone) continue;
                if (Math.Abs(candidate.Z - porter.Z) > MerchantZTolerance) continue;
                if (nav.GetPathStraight(zone, start, candidate, nav.DefaultFilters, nodes).Status != PathfindingStatus.PathFound) continue;
                spot = candidate;
                return true;
            }

            reason = "no_navigable_spot";
            return false;
        }
    }
}
