using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using DOL.AI.Brain;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// The NPC-only AllRealmsTeleporter path used by autonomous world bots to
    /// reach a standard town in a pickup group's rendezvous region.
    /// </summary>
    public static class AutonomousBotTownTravel
    {
        /// <summary>
        /// Finds an installed active all-realms teleporter in the bot's current
        /// region and resolves a standard town route into the requested region.
        /// Porters in another region are intentionally not searched; callers
        /// must first route the bot through the ordinary world travel system.
        /// </summary>
        public static bool TryGetTownRoute(GameBot bot, ushort rendezvousRegionID,
            out AllRealmsTeleporter teleporter, out DbTeleport destination)
        {
            teleporter = null;
            destination = null;
            if (!IsActiveAutonomousBot(bot) || bot.CurrentRegionID == 0)
                return false;

            Region sourceRegion = WorldMgr.GetRegion(bot.CurrentRegionID);
            if (sourceRegion == null || sourceRegion.IsDisabled ||
                !TryResolveTownDestination(rendezvousRegionID, out destination))
                return false;

            teleporter = WorldMgr.GetNPCsFromRegion(bot.CurrentRegionID)
                .OfType<AllRealmsTeleporter>()
                .Where(candidate => IsActiveAccessibleTeleporter(bot, candidate))
                .OrderBy(bot.GetDistanceTo)
                .FirstOrDefault();
            if (teleporter != null)
                return true;

            destination = null;
            return false;
        }

        /// <summary>
        /// Resolves the first valid standard town route whose installed or
        /// fallback destination belongs to the requested, enabled region.
        /// Installed blank-type rows take precedence, matching player porter
        /// route selection.
        /// </summary>
        public static bool TryResolveTownDestination(ushort rendezvousRegionID, out DbTeleport destination)
        {
            destination = null;
            if (rendezvousRegionID == 0)
                return false;

            Region rendezvousRegion = WorldMgr.GetRegion(rendezvousRegionID);
            if (rendezvousRegion == null || rendezvousRegion.IsDisabled)
                return false;

            eRealm[] realms = [eRealm.Albion, eRealm.Midgard, eRealm.Hibernia];
            foreach (eRealm realm in realms)
            {
                foreach (string routeID in AllRealmsTeleportFallbacks.GetTownRouteIDs(realm))
                {
                    DbTeleport candidate = WorldMgr.GetTeleportLocation(realm, $":{routeID}");
                    if (!IsValidTownDestination(candidate, realm, routeID, rendezvousRegionID))
                        continue;

                    destination = candidate;
                    return true;
                }
            }

            foreach (eRealm realm in realms)
            {
                foreach (string routeID in AllRealmsTeleportFallbacks.GetTownRouteIDs(realm))
                {
                    // An installed row remains authoritative for its menu ID,
                    // even if that row is malformed; mirror player route lookup.
                    if (WorldMgr.GetTeleportLocation(realm, $":{routeID}") != null)
                        continue;

                    DbTeleport candidate = AllRealmsTeleportFallbacks.Get(realm, routeID);
                    if (!IsValidTownDestination(candidate, realm, routeID, rendezvousRegionID))
                        continue;

                    destination = candidate;
                    return true;
                }
            }

            return false;
        }

        // Matchmaking calls this only for its bounded candidate shortlist. Each
        // route performs at most four destination and four porter validations.
        public static bool TryGetTownRoute(GameBot bot, ushort rendezvousRegionID,
            Vector3 rendezvous, out AllRealmsTeleporter teleporter, out DbTeleport destination,
            out double travelMinutes, AllRealmsTeleporter excludedPorter = null)
        {
            teleporter = null;
            destination = null;
            travelMinutes = double.PositiveInfinity;
            if (!IsActiveAutonomousBot(bot)) return false;
            Region source = WorldMgr.GetRegion(bot.CurrentRegionID);
            if (source == null || source.IsDisabled ||
                !TryResolveTownDestination(rendezvousRegionID, rendezvous, out destination)) return false;

            IPathfindingMgr nav = PathfindingProvider.Instance;
            GameNPC[] sourceNpcs = WorldMgr.GetNPCsFromRegion(bot.CurrentRegionID);
            if (!sourceNpcs.Any(npc => ReferenceEquals(npc, bot))) return false;
            Vector3 start = new(bot.X, bot.Y, bot.Z);
            foreach (AllRealmsTeleporter candidate in sourceNpcs
                .OfType<AllRealmsTeleporter>()
                .Where(candidate => !ReferenceEquals(candidate, excludedPorter) &&
                    candidate.ObjectState == GameObject.eObjectState.Active &&
                    candidate.CurrentRegionID == bot.CurrentRegionID)
                .OrderBy(bot.GetDistanceTo).Take(4))
            {
                Vector3 target = new(candidate.X, candidate.Y, candidate.Z);
                Zone zone = source.GetZone(candidate.X, candidate.Y);
                if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) continue;
                // Project on the porter's own floor, then validate the full
                // approach from the bot, including connected zone crossings.
                if (!AutonomousZonePointApproach.TryResolve(nav, zone, source.GetZone(bot.X, bot.Y) == zone ? start : target, target,
                        Math.Max(32, WorldMgr.INTERACT_DISTANCE / 2), out Vector3 approach) ||
                    !CanReachTownPoint(source, source.GetZone(bot.X, bot.Y), start, approach)) continue;
                teleporter = candidate;
                travelMinutes = EstimateRouteTravelMinutes(start, approach,
                    new(destination.X, destination.Y, destination.Z), rendezvous, bot.MaxSpeed);
                return true;
            }
            destination = null;
            return false;
        }

        /// <summary>
        /// Proves every leg of an ordinary region itinerary, including the
        /// final destination corridor. Uses the mover's seam resolver and its
        /// existing 32-zone route limit; it cannot certify only the first seam.
        /// </summary>
        public static bool CanReachTownPoint(Region region, Zone startZone, Vector3 start, Vector3 end)
        {
            IPathfindingMgr nav = PathfindingProvider.Instance;
            Zone targetZone = region?.GetZone((int)end.X, (int)end.Y);
            if (region == null || region.IsDisabled || startZone == null || targetZone == null ||
                !nav.IsAvailable || !nav.HasNavmesh(startZone) || !nav.HasNavmesh(targetZone)) return false;
            var visited = new HashSet<Zone>();
            Zone current = startZone;
            for (int leg = 0; leg < 32; leg++)
            {
                if (current == targetZone)
                    return AutonomousRendezvousNavigation.CanReachFrom(nav, current, start, end);
                if (!visited.Add(current) ||
                    !AutonomousZoneItinerary.TryNextStep(region, current, targetZone, start, end, nav,
                        out AutonomousZoneBoundaryRouting.Step step, zone => !visited.Contains(zone))) return false;
                Zone next = region.GetZone((int)step.Outside.X, (int)step.Outside.Y);
                if (next == null || next == current || !nav.HasNavmesh(next)) return false;
                current = next;
                start = step.Outside;
            }
            return false;
        }

        public static double EstimateRouteTravelMinutes(Vector3 start, Vector3 approach,
            Vector3 arrival, Vector3 rendezvous, double speed) =>
            (Vector3.Distance(start, approach) + Vector3.Distance(arrival, rendezvous)) /
                Math.Max(1, speed) / 60 + 1;

        public static bool TryResolveTownDestination(ushort rendezvousRegionID, Vector3 rendezvous,
            out DbTeleport destination)
        {
            destination = null;
            Region region = WorldMgr.GetRegion(rendezvousRegionID);
            if (region == null || region.IsDisabled) return false;
            IPathfindingMgr nav = PathfindingProvider.Instance;
            Zone targetZone = region.GetZone((int)rendezvous.X, (int)rendezvous.Y);
            if (targetZone == null || !nav.IsAvailable || !nav.HasNavmesh(targetZone)) return false;
            foreach (DbTeleport candidate in GetTownDestinations(rendezvousRegionID)
                .OrderBy(candidate => Vector3.DistanceSquared(new(candidate.X, candidate.Y, candidate.Z), rendezvous))
                .Take(4))
            {
                Vector3 arrival = new(candidate.X, candidate.Y, candidate.Z);
                if (!CanReachTownPoint(region, region.GetZone(candidate.X, candidate.Y), arrival, rendezvous)) continue;
                destination = candidate;
                return true;
            }
            return false;
        }

        private static IEnumerable<DbTeleport> GetTownDestinations(ushort regionID)
        {
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            foreach (string routeID in AllRealmsTeleportFallbacks.GetTownRouteIDs(realm))
            {
                // Installed data overrides only this particular advertised
                // route, not all fallback towns in the destination region.
                DbTeleport candidate = WorldMgr.GetTeleportLocation(realm, $":{routeID}") ??
                    AllRealmsTeleportFallbacks.Get(realm, routeID);
                if (IsValidTownDestination(candidate, realm, routeID, regionID)) yield return candidate;
            }
        }

        public static bool TryTeleportToTown(GameBot bot, AllRealmsTeleporter teleporter,
            ushort rendezvousRegionID, Vector3 rendezvous)
        {
            if (!IsSafeToTeleport(bot) || !IsAtInteractionDistance(bot, teleporter) ||
                !TryResolveTownDestination(rendezvousRegionID, rendezvous, out DbTeleport destination))
                return false;
            return bot.MoveTo((ushort)destination.RegionID, destination.X, destination.Y,
                destination.Z, (ushort)destination.Heading);
        }

        /// <summary>
        /// Reports actual in-world NPC interaction proximity for the specified
        /// active porter. Both actors must still be in the same region.
        /// </summary>
        public static bool IsAtInteractionDistance(GameBot bot, AllRealmsTeleporter teleporter)
        {
            return IsActiveAutonomousBot(bot) && IsActiveAccessibleTeleporter(bot, teleporter) &&
                bot.IsWithinRadius(teleporter, WorldMgr.INTERACT_DISTANCE);
        }

        /// <summary>
        /// Transfers an autonomous NPC bot through a current-region porter to a
        /// validated standard town destination. No arbitrary remote transfer is
        /// possible: the porter must still be registered, active, and in actual
        /// interaction range at the time this method is called.
        /// </summary>
        public static bool TryTeleportToTown(GameBot bot, AllRealmsTeleporter teleporter,
            ushort rendezvousRegionID)
        {
            if (!IsSafeToTeleport(bot) || !IsAtInteractionDistance(bot, teleporter) ||
                !TryResolveTownDestination(rendezvousRegionID, out DbTeleport destination))
                return false;

            return bot.MoveTo((ushort)destination.RegionID, destination.X, destination.Y,
                destination.Z, (ushort)destination.Heading);
        }

        private static bool IsValidTownDestination(DbTeleport destination, eRealm routeRealm,
            string routeID, ushort rendezvousRegionID)
        {
            if (destination == null || !string.IsNullOrWhiteSpace(destination.Type) ||
                destination.Realm != (int)routeRealm ||
                !string.Equals(destination.TeleportID?.Trim(), routeID, StringComparison.OrdinalIgnoreCase) ||
                destination.RegionID != rendezvousRegionID || destination.RegionID <= 0 ||
                destination.RegionID > ushort.MaxValue || destination.X <= 0 || destination.Y <= 0 ||
                destination.Z < 0 || destination.Heading is < 0 or > 4095)
                return false;

            Region destinationRegion = WorldMgr.GetRegion((ushort)destination.RegionID);
            return destinationRegion != null && !destinationRegion.IsDisabled;
        }

        private static bool IsActiveAutonomousBot(GameBot bot)
        {
            return bot?.IsAutonomousWorldBot == true && bot.ObjectState is GameObject.eObjectState.Active;
        }

        private static bool IsSafeToTeleport(GameBot bot)
        {
            return IsActiveAutonomousBot(bot) && bot.IsAlive && !bot.InCombat && !bot.IsAttacking &&
                bot.Brain is not BotBrain { HasAggro: true };
        }

        private static bool IsActiveAccessibleTeleporter(GameBot bot, AllRealmsTeleporter teleporter)
        {
            if (!IsActiveAutonomousBot(bot) || bot.CurrentRegionID == 0 || teleporter == null ||
                teleporter.ObjectState is not GameObject.eObjectState.Active ||
                teleporter.CurrentRegionID != bot.CurrentRegionID)
                return false;

            Region region = WorldMgr.GetRegion(bot.CurrentRegionID);
            if (region == null || region.IsDisabled)
                return false;

            GameNPC[] npcs = WorldMgr.GetNPCsFromRegion(bot.CurrentRegionID);
            return npcs.Any(npc => ReferenceEquals(npc, bot)) &&
                npcs.Any(npc => ReferenceEquals(npc, teleporter));
        }
    }
}
