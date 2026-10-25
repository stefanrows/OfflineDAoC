using System;
using System.Linq;
using System.Numerics;

namespace DOL.GS;

public sealed partial class AutonomousWorldBotController
{
    private long _armyReportAfter, _armyScoutAfter;

    private bool HandleGuildArmy(GameBot bot, string forceId, AutonomousRvrEventLayer.GuildArmyOrder order)
    {
        long now = GameLoop.GameLoopTime;
        CampDestination destination = _rvrDestination;
        if (destination == null || destination.Id != order.TargetId) return true;
        GameBot leader = _groupDirective?.Leader;
        if (leader == bot && now >= _armyReportAfter)
        {
            _armyReportAfter = now + 5000;
            var members = bot.Group?.GetMembersInTheGroup().OfType<GameBot>().ToArray() ?? [bot];
            long[] operators = members.Where(member => member.IsAlive &&
                AutonomousSiegeJobs.HasRamAssignment(member, destination.Id, destination.RegionId) &&
                BotSiegeRuntime.Item(member, BotSiegeRuntime.Kit(member.Realm, BotSiegeKind.Ram)) != null)
                .Select(member => member.DatabaseID).ToArray();
            GameLiving[] sightings = [];
            if (now >= _armyScoutAfter)
            {
                _armyScoutAfter = now + 15_000;
                // Two local scouts per party, bounded visibility checks. Count
                // visible player-shaped actors, never pets as additional owners
                // or remote event reservations as defenders.
                sightings = members.Where(member => member.IsAlive && member.CurrentRegionID == destination.RegionId)
                    .OrderBy(member => Vector2.DistanceSquared(new(member.X, member.Y), new(destination.X, destination.Y)))
                    .Take(2).SelectMany(scout => scout.GetNPCsInRadius(WorldMgr.VISIBILITY_DISTANCE).OfType<GameLiving>()
                        .Concat(scout.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE).OfType<GameLiving>())
                        .Where(enemy => enemy is IGamePlayer && enemy.IsAlive && AutonomousRvrTargetPolicy.IsEnemyCombatant(scout, enemy) &&
                            Vector2.DistanceSquared(new(enemy.X, enemy.Y), new(destination.X, destination.Y)) <= 6500 * 6500)
                        .OrderBy(scout.GetDistanceTo).Take(48)
                        .Where(enemy => GameServer.ServerRules.IsAllowedToAttack(scout, enemy, true) && BotSiegeRuntime.Visible(scout, enemy)))
                    .Distinct().ToArray();
            }
            AutonomousRvrEventLayer.ReportGuildArmy(forceId, bot, members, operators, sightings, now);
            order = AutonomousRvrEventLayer.GuildArmyPlan(forceId, bot, now);
            if (order == null) { ClearKeepObjective(bot); return true; }
        }
        if (order.Failed)
        {
            ClearKeepObjective(bot);
            SetRvrStatus(bot, WorldMode.IsRvr ? "Realm assault called off" : "Guild assault called off", destination.MonsterName,
                "The army could not assemble enough ready troops; choosing another objective");
            return true;
        }
        if (order.Released)
        {
            if (!order.HoldColumn || leader != bot) return false;
            bot.StopMovingOnPath(); bot.StopMoving();
            AutonomousStuckWatchdog.MarkProgress(bot, eAutonomousProgressKind.Objective);
            SetRvrStatus(bot, WorldMode.IsRvr ? "Regrouping the realm army" : "Regrouping the guild army", destination.MonsterName,
                "The attacking parties are closing the gap before the final approach");
            return true;
        }
        if (leader != bot)
        {
            if (leader?.IsAlive == true) return FollowDynamicGroupLeader(bot, _groupDirective);
            bot.StopMovingOnPath(); bot.StopMoving();
            return true;
        }
        // Boarding/ticket acquisition owns the meeting point on a crossing.
        // The normal native porter quorum still controls who actually boards.
        if (bot.CurrentRegionID != destination.RegionId && TryFrontierTransport(bot, destination)) return true;
        if (HoldSiegeColumn(bot, forceId, destination)) return true;
        if (bot.CurrentRegionID != destination.RegionId) return TravelRvrObjective(bot, destination);
        if (order.Camp is Vector3 camp &&
            Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), camp) <= 350 * 350)
        {
            bot.StopMovingOnPath(); bot.StopMoving();
            AutonomousStuckWatchdog.MarkProgress(bot, eAutonomousProgressKind.Objective);
            SetRvrStatus(bot, WorldMode.IsRvr ? "Gathering the realm army" : "Gathering the guild army", destination.MonsterName,
                $"{order.Parties} ready parties, {order.Present}/{order.Required} troops; waiting for healers and siege equipment ({order.Remaining/60000}m left)");
            return true;
        }
        FollowKeepTravel(bot, destination, order);
        return true;
    }

    /// <summary>
    /// RvR world: a walkable spot inside the realm muster (portal outpost or border keep), connected to the actor.
    /// The same probe ring as the border-hub staging; at most twelve candidates per attempt.
    /// </summary>
    private bool TryRealmArmyMuster(GameBot bot, CampDestination destination, IPathfindingMgr nav, Vector3 actor,
        AutonomousRvrEventLayer.GuildArmyOrder order, out Vector3 point)
    {
        point = default;
        if (!AutonomousRealmArmyPolicy.TryGetRealmMuster(bot.Realm, destination.RegionId, new(destination.X, destination.Y),
                out AutonomousRvrStaging.BorderKeep muster) ||
            !AutonomousRealmArmyPolicy.IsUsableMuster(muster, destination.RegionId, new(destination.X, destination.Y)) ||
            bot.CurrentRegionID != muster.RegionId)
            return false;
        foreach (Vector3 raw in AutonomousRvrStaging.CandidateAnchors(muster, bot.DatabaseID).Take(12))
        {
            Zone zone = bot.CurrentRegion.GetZone((int)raw.X, (int)raw.Y);
            if (zone == null || !AutonomousRealmBoundary.Allows(bot.Realm, bot.CurrentRegionID, zone.ID) || !nav.HasNavmesh(zone)) continue;
            var floor = nav.GetClosestPoint(zone, raw, 72, 72, 512, nav.DefaultFilters);
            if (!floor.HasValue || !AutonomousRendezvousNavigation.HasLocalExit(nav, zone, floor.Value) ||
                !RvrKeepRoute.TryBuild(bot.CurrentRegion, nav, bot.Realm, actor, floor.Value, out _)) continue;
            var shared = AutonomousRvrEventLayer.SetGuildArmyCamp(RvrForceOf(bot), bot, order.Generation,
                floor.Value, GameLoop.GameLoopTime, muster.Name);
            if (!shared.HasValue) return false;
            point = shared.Value;
            return true;
        }
        return false;
    }

    /// <summary>Find one shared exterior camp through the existing sliced navigation budget.</summary>
    private bool TryGuildArmyCamp(GameBot bot, CampDestination destination, IPathfindingMgr nav,
        Vector3 actor, AutonomousRvrEventLayer.GuildArmyOrder order, out Vector3 point)
    {
        if (order.Camp is Vector3 existing) { point = existing; return true; }
        point = default;
        // RvR world (R4b): the realm army musters at its own portal outpost in the keep's frontier, or at its
        // nearer border keep in the home frontier. Only when no walkable, connected spot is found there does it
        // fall back to the forward camp below.
        if (WorldMode.IsRvr && TryRealmArmyMuster(bot, destination, nav, actor, order, out point)) return true;
        double bearing = Math.Atan2(actor.Y - destination.Y, actor.X - destination.X);
        for (int i = 0; i < 12; i++)
        {
            double angle = bearing + (i % 2 == 0 ? i / 2 : -(i + 1) / 2) * Math.PI / 12;
            Vector3 raw = new(destination.X + (float)Math.Cos(angle) * AutonomousGuildAssault.CampDistance,
                destination.Y + (float)Math.Sin(angle) * AutonomousGuildAssault.CampDistance, destination.Z);
            Zone zone = bot.CurrentRegion.GetZone((int)raw.X, (int)raw.Y);
            if (zone == null || !AutonomousRealmBoundary.Allows(bot.Realm, bot.CurrentRegionID, zone.ID) || !nav.HasNavmesh(zone)) continue;
            // Camp terrain projection is not an actor relocation. The route
            // from the real actor origin must still prove every seam and floor.
            var floor = nav.GetClosestPoint(zone, raw, 72, 72, 4096, nav.DefaultFilters);
            if (!floor.HasValue || Vector2.DistanceSquared(new(floor.Value.X, floor.Value.Y),
                    new(destination.X, destination.Y)) < AutonomousGuildAssault.MinimumKeepDistance * AutonomousGuildAssault.MinimumKeepDistance ||
                !AutonomousRendezvousNavigation.HasLocalExit(nav, zone, floor.Value) ||
                !RvrKeepRoute.TryBuild(bot.CurrentRegion, nav, bot.Realm, actor, floor.Value, out _)) continue;
            var shared = AutonomousRvrEventLayer.SetGuildArmyCamp(RvrForceOf(bot), bot, order.Generation,
                floor.Value, GameLoop.GameLoopTime);
            if (!shared.HasValue) return false;
            point = shared.Value;
            return true;
        }
        return false;
    }
}
