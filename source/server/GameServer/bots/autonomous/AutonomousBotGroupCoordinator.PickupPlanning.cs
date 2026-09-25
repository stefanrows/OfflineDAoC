using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS;

public static partial class AutonomousBotGroupCoordinator
{
    private sealed record PickupDestination(SharedCamp Camp, Vector3 Point, string Name, ushort RegionId);

    private static readonly Dictionary<string, long> PickupGeometryRetryTicks = new(StringComparer.Ordinal);

    private static bool TryPlanPickupDestination(GameBot seed, GameBot[] available, HashSet<string> rejectedCamps, ref int navigationChecks,
        out GameBot leader, out PickupDestination destination)
    {
        leader = null;
        destination = null;
        long now = GameLoop.GameLoopTime;
        foreach (string expired in PickupGeometryRetryTicks.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
            PickupGeometryRetryTicks.Remove(expired);
        var excluded = new HashSet<string>(rejectedCamps, StringComparer.Ordinal);
        excluded.UnionWith(PickupGeometryRetryTicks.Keys);
        GameBot[] cohort = available.Where(member => LevelsCompatible(seed.Level, member.Level)).ToArray();
        foreach (var choice in AutonomousWorldBotController.PickupCampShortlist(seed, cohort, excluded))
        {
            if (navigationChecks >= MaximumRendezvousChecksPerPass) break;
            SharedCamp camp = choice.Camp;
            Region region = WorldMgr.GetRegion(camp.RegionId);
            if (region == null || region.IsDisabled) continue;
            Vector3 campPoint = new(camp.X, camp.Y, camp.Z);
            // A nearby isolated actor must not hide a reachable second leader.
            GameBot[] leaders = cohort.Where(member => member.CurrentRegionID == camp.RegionId)
                .OrderBy(member => Vector3.DistanceSquared(new(member.X, member.Y, member.Z), campPoint))
                .ThenBy(member => LastFormationAttemptTick.GetValueOrDefault(MemberKey(member)))
                .Take(2).ToArray();
            if (leaders.Length == 0) continue;
            // An actor already in a genuine town can provide the anchor even
            // when its town has no merchant/trainer/stablemaster spawn.
            GameObject[] towns = leaders.Where(IsTownArea).Cast<GameObject>()
                .Concat(WorldMgr.GetNPCsFromRegion(camp.RegionId)
                    .Where(npc => npc.ObjectState == GameObject.eObjectState.Active &&
                        (npc is GameMerchant or GameTrainer or GameStableMaster or AllRealmsTeleporter) && IsTownArea(npc)))
                .Where(town => Vector3.DistanceSquared(new(town.X, town.Y, town.Z), campPoint) <= 30_000L * 30_000L)
                .OrderBy(town => Vector3.DistanceSquared(new(town.X, town.Y, town.Z), campPoint))
                .Take(2).ToArray();
            foreach (GameObject town in towns)
            {
                foreach (GameBot candidateLeader in leaders)
                {
                    if (navigationChecks >= MaximumRendezvousChecksPerPass) break;
                    navigationChecks++;
                    var nav = PathfindingProvider.Instance;
                    Zone zone = town.CurrentZone;
                    if (!AutonomousRendezvousNavigation.TryChoosePoint(nav, zone, new(town.X, town.Y, town.Z), out Vector3 point) ||
                        !AutonomousBotTownTravel.CanReachTownPoint(region,
                            candidateLeader.CurrentZone, new(candidateLeader.X, candidateLeader.Y, candidateLeader.Z), point) ||
                        !AutonomousBotTownTravel.CanReachTownPoint(region, zone, point, campPoint))
                        continue;
                    double travel = Vector3.Distance(new(candidateLeader.X, candidateLeader.Y, candidateLeader.Z), point) /
                        Math.Max(1d, candidateLeader.MaxSpeed) / 60d;
                    if (!AutonomousPickupPlanning.WithinTravelBudget(travel)) continue;
                    leader = candidateLeader;
                    destination = new(camp, point, TownName(town), camp.RegionId);
                    return true;
                }
            }
            // Briefly let other camps win future passes after bad geometry.
            // Entries expire automatically; moving actors and changed meshes
            // are reconsidered without retaining a permanent blacklist.
            PickupGeometryRetryTicks[camp.Id] = now + 30_000;
        }
        return false;
    }
}
