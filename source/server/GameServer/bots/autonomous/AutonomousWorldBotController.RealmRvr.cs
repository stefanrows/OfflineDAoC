using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;

namespace DOL.GS
{
    // RvR world (Realm-1): the RvR turn of the upstream controller (shadowofze/OfflineDAoC 0.38c,
    // AutonomousWorldBotController.cs ExecuteRvr, FindRvrTarget, ChooseRvrDestination, StageSoloRvrAtBorderKeep, the
    // siege back line and the siege combat attendance), copied verbatim for port session S2 (docs/RVR_PORT_MAP.md).
    // ExecuteRvr hands the RvR world over to ExecuteRvrRealm after the shared battleground prefix; the Camlann world
    // keeps the fork's turn. The fork's "livelier RvR" waves (observe, pause, stealth loop, speed, LFG, roam tastes,
    // grudge trips, mob bends, route variants) are therefore not part of the RvR turn (AutonomousRvrWaves).
    // Local changes are marked "Fork:": friend and foe through the realm rule (map section 4b), the fork's frontier
    // transport fixes (bug 76), the fork's keep approach navigation and the R3b relic temple pads.
    public sealed partial class AutonomousWorldBotController
    {
        /// <summary>The nearer border keep a solo RvR bot stages at (upstream; StageSoloRvrAtBorderKeepRealm).</summary>
        private AutonomousRvrStaging.BorderKeep? _soloRvrBorderKeep;
        private long _nextSiegeCombatAttendanceTick;
        public const int SiegeCombatAttendanceRadius = 9_500;

        /// <summary>
        /// The force belongs to a running siege event. RvR world (upstream 0.38c): muster, march and battle
        /// (<see cref="AutonomousRvrEventLayer.IsEventForce"/>; run 8: the Dun Dagda army waited 90 minutes in group
        /// rendezvous). Camlann: the battle only, as before.
        /// </summary>
        private static bool IsSiegeEventForce(string forceId, long nowTick) => WorldMode.IsRvr
            ? AutonomousRvrEventLayer.IsEventForce(forceId, nowTick)
            : AutonomousRvrEventLayer.IsBattleForce(forceId, nowTick);

        /// <summary>
        /// A siege participant fighting at the keep (inside the rally-post ring) is physically there.
        /// Presence was only reported at a bot's own rally post outside combat: run 12's Albion army
        /// fought Midgard's defenders 2,000-6,000 units from Arvakr Faste and counted 0 present.
        /// </summary>
        private void ReportSiegeCombatAttendance(GameBot bot, long nowTick)
        {
            if (!_rvrSharedEvent || _rvrDestination == null || nowTick < _nextSiegeCombatAttendanceTick ||
                !_rvrDestination.Id.StartsWith("rvr-keep-", StringComparison.Ordinal) ||
                bot.CurrentRegionID != _rvrDestination.RegionId ||
                Distance(bot.X, bot.Y, _rvrDestination.X, _rvrDestination.Y) > SiegeCombatAttendanceRadius)
                return;
            _nextSiegeCombatAttendanceTick = nowTick + 2_000 + bot.ObjectID % 500;
            string forceId = _groupDirective?.IsDynamic == true ? _groupDirective.GroupId : $"rvr-{bot.DatabaseID}";
            AutonomousRvrEventLayer.ReportAttendance(_rvrDestination.Id, forceId, bot.Realm, bot.DatabaseID, true, nowTick,
                bot, new(bot.X, bot.Y, bot.Z));
        }

        private bool ExecuteRvrRealm(BotBrain brain, GameBot bot)
        {
            bool dynamicWarband = _groupDirective?.IsDynamic == true &&
                                  _groupDirective.ObjectiveKind == eAutonomousObjectiveKind.RvR;
            string forceId = dynamicWarband ? _groupDirective.GroupId : $"rvr-{bot.DatabaseID}";
            bot.TempProperties.SetProperty("RvrEventForce", forceId);
            bool battleActive = AutonomousRvrEventLayer.IsBattleForce(forceId, GameLoop.GameLoopTime);
            var committedPlan = AutonomousRvrEventLayer.KeepPlan(forceId, bot.Realm, GameLoop.GameLoopTime);
            if (committedPlan != null)
            {
                // Fork: the fork's frontier transport keeps a passage that rejoins the leader (bug 76); otherwise as upstream.
                ClearUnrelatedFrontierRequest(bot, committedPlan);
                if (_rvrDestination?.Id != committedPlan.TargetId)
                    _rvrApproachDestination = null;
                _rvrDestination = new(committedPlan.TargetId, committedPlan.Name, committedPlan.Name,
                    committedPlan.RegionId, committedPlan.X, committedPlan.Y, committedPlan.Z, 1, false, true);
                _rvrSharedEvent = true;
                _rvrIntent = committedPlan.Intent;
                bot.TempProperties.SetProperty("RvrWarbandIntent", (int)_rvrIntent);
            }
            var rally = AutonomousRvrEventLayer.GetRallyOrder(forceId, bot.Realm, GameLoop.GameLoopTime);
            // A marching army's siege operators set up rams, catapults and trebuchets as soon as they reach
            // the keep instead of waiting at rally posts for the battle to be declared. Siege jobs ran only
            // after the battle started, and no bot had ever deployed a siege weapon (logs since 2026-10-02).
            if (rally != null && !rally.HomeMuster && PvpCombatant.AllianceRealm(bot) == rally.Attacker && TryRunSiegeJobRealm(bot)) return true;
            if (rally != null) return HandleSiegeRallyRealm(bot, forceId, rally);
            if (committedPlan == null && dynamicWarband && _groupDirective.Leader != bot && _groupDirective.Leader != null)
                _rvrIntent = (AutonomousRvrEventLayer.Intent)_groupDirective.Leader.TempProperties
                    .GetProperty<int>("RvrWarbandIntent", (int)AutonomousRvrEventLayer.Intent.Roam);
            // Fork: a warband bound for another region gathers at the fork's frontier porter, not in formation; the leader
            // holding here while its members waited at the porter for it deadlocked 500 bots (bug 76, 0.171.0).
            bool crossingToObjective = _rvrDestination != null && _rvrDestination.RegionId != 0 &&
                bot.CurrentRegionID != _rvrDestination.RegionId;
            if (!battleActive && !(dynamicWarband && _groupDirective.GroupCombatActive) && !crossingToObjective &&
                HoldBeforeNewGroupPull(brain, bot))
                return true;
            if (!battleActive && !dynamicWarband && !GameRelic.IsPlayerCarryingRelic(bot) && !StageSoloRvrAtBorderKeepRealm(bot))
                return true;

            // Native keep/relic events remove resolved objectives immediately.
            // Release this controller's stale destination on its next AI pulse,
            // rather than letting it march toward a captured or timed-out site.
            if (_rvrSharedEvent && _rvrDestination != null &&
                !AutonomousRvrEventLayer.IsTargetActive(_rvrDestination.Id, GameLoop.GameLoopTime))
            {
                _rvrDestination = null;
                _rvrApproachDestination = null;
                _rvrSharedEvent = false;
                _nextRvrPlanReview = 0;
            }

            // RvR has no PvE puller. Every cohesive member can acquire a local
            // target, with pressure spread over the closest visible enemies.
            if (TryRunSiegeJobRealm(bot)) return true;
            GameLiving enemy = FindRvrTargetRealm(bot);
            if (enemy != null && battleActive && HoldSiegeBackLine(bot, enemy))
                return true;
            if (enemy != null && battleActive && KeepsSiegeBackLine(bot, enemy))
                enemy = null;
            if (enemy != null)
            {
                bot.StopMovingOnPath();
                bot.StopMoving();
                bot.TargetObject = enemy;
                AutonomousBotGroupCoordinator.MarkCombatObserved(bot.Group);
                brain.AddToAggroList(enemy, Math.Max(100, enemy.EffectiveLevel * 12));
                brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                eAutonomousRvrPhase phase = AutonomousRvrObjectiveState.ForTarget(enemy);
                SetRvrStatus(bot, phase.ToString(), "Live RvR target in awareness range", enemy.Name);
                return false;
            }

            // The leader owns navigation and the single siege operator role.
            // Followers fight independently above, otherwise they keep the
            // warband together around that leader.
            if (!battleActive && dynamicWarband && _groupDirective.Leader != bot && !GameRelic.IsPlayerCarryingRelic(bot))
            {
                int defendingId = _groupDirective.Leader?.TempProperties.GetProperty<int>("RvrDefendingKeep", -1) ?? -1;
                AbstractGameKeep wallKeep = defendingId >= 0
                    ? GameServer.KeepManager.GetKeepsOfRegion(bot.CurrentRegionID).FirstOrDefault(keep =>
                        keep.KeepID == defendingId && !AutonomousRvrKeepPolicy.IsHostileTo(keep, bot.Realm) &&
                        bot.GetDistanceTo(new Point3D(keep.X, keep.Y, keep.Z)) <= 2500) : null;
                if (wallKeep != null)
                {
                    return HoldDefensiveKeepPost(bot, wallKeep);
                }
                return FollowDynamicGroupLeader(bot, _groupDirective);
            }

            GameRelic carriedRelic = bot.TempProperties.GetProperty<GameRelic>(GameRelic.PLAYER_CARRY_RELIC_WEAK);
            if (carriedRelic?.CurrentCarrier == bot)
            {
                // Fork: the realm's temple pad of the relic's type (R3b; the same rule as upstream's pad search).
                GameRelicPad home = RelicMgr.FindDeliveryPad(bot, bot.Guild, carriedRelic);
                if (home == null)
                {
                    SetRvrStatus(bot, "Relic return blocked", "Protect the carried relic", "No matching home shrine is available");
                    return true;
                }
                if (bot.IsWithinRadius(home, 250) && carriedRelic.RelicPadTakesOver(home, false))
                {
                    _rvrDestination = null;
                    SetRvrStatus(bot, "Relic captured", "Relic delivered to the home shrine", carriedRelic.Name);
                    return true;
                }
                SetRvrStatus(bot, "Returning relic", "Escort the real relic to its home shrine", home.Name);
                return TravelRvrObjective(bot, new("relic-home", home.Name, home.CurrentZone?.Description ?? "home shrine",
                    home.CurrentRegionID, home.X, home.Y, home.Z, 50, false, true));
            }

            if (_rvrDestination?.Id.StartsWith("rvr-relic-carrier-", StringComparison.Ordinal) == true)
            {
                GameRelic escorted = RelicMgr.GetRelics().FirstOrDefault(relic =>
                    RelicObjectiveId(relic) == _rvrDestination.Id);
                GameLiving carrier = escorted?.CurrentCarrier;
                if (carrier?.IsAlive == true)
                {
                    _rvrDestination = _rvrDestination with { RegionId = carrier.CurrentRegionID, X = carrier.X, Y = carrier.Y, Z = carrier.Z };
                    // Fork: the carrier's side by the realm rule (map section 4b).
                    bool alliedCarrier = PvpCombatant.AreAllied(bot, carrier);
                    SetRvrStatus(bot, alliedCarrier ? "Escorting relic" : "Intercepting relic", escorted.Name, carrier.Name);
                    if (alliedCarrier && bot.IsWithinRadius(carrier, 450))
                    {
                        bot.StopMovingOnPath();
                        bot.StopMoving();
                        return true;
                    }
                    return TravelRvrObjective(bot, _rvrDestination);
                }
                if (escorted is { IsMounted: false, ObjectState: GameObject.eObjectState.Active })
                {
                    _rvrDestination = _rvrDestination with { RegionId = escorted.CurrentRegionID, X = escorted.X, Y = escorted.Y, Z = escorted.Z };
                    if (bot.IsWithinRadius(escorted, WorldMgr.INTERACT_DISTANCE))
                    {
                        bot.StopMovingOnPath();
                        bot.StopMoving();
                        escorted.TryPickup(bot);
                    }
                    else TravelRvrObjective(bot, _rvrDestination);
                    SetRvrStatus(bot, "Recovering dropped relic", escorted.Name, "Recovering the real dropped item for the realm");
                    return true;
                }
                _rvrDestination = null;
            }

            if (_rvrDestination == null || _rvrDestination.RegionId == 0 ||
                (!_rvrSharedEvent && GameLoop.GameLoopTime >= _nextRvrPlanReview && !BotSiegeRuntime.Assigned(bot)) ||
                (_rvrIntent == AutonomousRvrEventLayer.Intent.Roam && bot.CurrentRegionID == _rvrDestination.RegionId &&
                 Distance(bot.X, bot.Y, _rvrDestination.X, _rvrDestination.Y) <= CampArrivalRadius))
            {
                _rvrDestination = ChooseRvrDestinationRealm(bot);
                _nextRvrPlanReview = GameLoop.GameLoopTime + 45_000 + bot.ObjectID % 15_000;
                _campStartedTick = GameLoop.GameLoopTime;
                _patrolDestination = null;
                _rvrApproachDestination = null;
                if (_rvrDestination == null)
                {
                    SetRvrStatus(bot, "Awaiting a frontier objective", "Roam active frontier keeps, relic routes, and enemy forces",
                        "No reachable live enemy force or frontier keep is currently available");
                    return true;
                }
            }

            rally = AutonomousRvrEventLayer.GetRallyOrder(forceId, bot.Realm, GameLoop.GameLoopTime);
            if (rally != null) return HandleSiegeRallyRealm(bot, forceId, rally);

            // The ram owns its normal decay after release; the controller never
            // deletes it or mutates the door when the native objective changes.
            ReleaseOwnedSiegeRams(bot);

            GameRelic relic = FindInteractableRelicRealm(bot);
            if (relic != null)
            {
                if (!bot.IsWithinRadius(relic, WorldMgr.INTERACT_DISTANCE))
                    IssuePath(bot, new(relic.X, relic.Y, relic.Z));
                else
                {
                    bot.StopMovingOnPath();
                    bot.StopMoving();
                    bool pickedUp = relic.TryPickup(bot);
                    SetRvrStatus(bot, pickedUp ? "Returning relic" : "Rallying at relic",
                        relic.Name, pickedUp ? "Real relic secured; escorts follow the carrier home" :
                        "Waiting for the required raiders and a free inventory slot");
                }
                return true;
            }

            if (bot.CurrentRegionID != _rvrDestination.RegionId)
            {
                return TravelRvrObjective(bot,_rvrDestination);
            }

            Vector3 destination = new(_rvrDestination.X, _rvrDestination.Y, _rvrDestination.Z);
            Zone keepZone = bot.CurrentRegion?.GetZone(_rvrDestination.X, _rvrDestination.Y);
            AbstractGameKeep defendedKeep = _rvrIntent == AutonomousRvrEventLayer.Intent.DefendEvent
                ? GameServer.KeepManager.GetKeepsOfRegion(bot.CurrentRegionID).FirstOrDefault(keep =>
                    $"rvr-keep-{keep.KeepID}" == _rvrDestination.Id && !AutonomousRvrKeepPolicy.IsHostileTo(keep, bot.Realm)) : null;
            if (defendedKeep != null)
            {
                if (Vector2.DistanceSquared(new(bot.X,bot.Y),new(defendedKeep.X,defendedKeep.Y))>3500*3500)
                    return TravelToDefensivePost(bot,defendedKeep,new(defendedKeep.X,defendedKeep.Y,defendedKeep.Z));
                return HoldDefensiveKeepPost(bot, defendedKeep);
            }
            if (IsKeepOrKeepPatrolDestination(_rvrDestination))
            {
                if (FollowKeepTravel(bot, _rvrDestination)) return true;
                destination = _rvrApproachDestination.Value;
            }
            if (Vector2.Distance(new(bot.X, bot.Y), new(destination.X, destination.Y)) > 650)
            {
                // The first outbound leg leaves the safe border keep on foot,
                // keeping the assembled force together through the frontier
                // threshold. Normal faster travel remains available once the
                // force is actually roaming inside the frontier.
                if (IsInFrontier(bot) && TryBeginFasterStableRoute(bot, destination, _rvrDestination.ZoneName))
                    return true;
                IssueVariedRvrPathRealm(bot, destination);
                SetRvrStatus(bot, $"Roaming toward {_rvrDestination.MonsterName}",
                    "Roam active frontier keeps, relic routes, and enemy forces", "Using a collision-safe frontier route", _rvrDestination.MonsterName);
                return true;
            }

            if (_rvrSharedEvent && _rvrDestination.Id.StartsWith("rvr-keep-",StringComparison.Ordinal))
            {
                // A siege is not a random patrol around the gate: a random
                // nearby polygon can be INSIDE the closed enemy keep. Retain
                // the proved approach until real door geometry changes.
                if(Vector3.DistanceSquared(new(bot.X,bot.Y,bot.Z),destination)>120*120)
                    IssuePath(bot,destination);
                else { bot.StopMovingOnPath();bot.StopMoving(); }
                SetRvrStatus(bot,"At keep assault approach",_rvrDestination.MonsterName,
                    "Engaging reachable enemies; advancing through gates only after a real breach");
                return true;
            }
            PatrolRvr(bot);
            if (GameLoop.GameLoopTime - _campStartedTick > 90_000)
            {
                _rvrDestination = null;
                _rvrApproachDestination = null;
                SetRvrStatus(bot, "Refreshing frontier patrol", "Roam active frontier keeps, relic routes, and enemy forces",
                    "No opposing force arrived at this patrol point; choosing another live objective");
                return true;
            }
            SetRvrStatus(bot, $"Searching near {_rvrDestination.MonsterName}",
                "Roam active frontier keeps, relic routes, and enemy forces", "Scanning for opposing-realm RvR forces", _rvrDestination.MonsterName);
            return true;
        }

        private GameLiving FindRvrTargetRealm(GameBot bot)
        {
            // Player/bot/pet threats are acquired by the bounded early frontier
            // scan, before staging/recovery can consume a turn. Do not repeat
            // that scan here; this late path handles siege objectives only.

            // Doors/lords use the normal GameLiving combat pipeline. There is no
            // direct ownership mutation: if normal server rules reject an NPC
            // attacker (for example, no permitted siege damage), the warband
            // safely continues its live frontier patrol.
            if (_rvrIntent is not (AutonomousRvrEventLayer.Intent.AssaultKeep or AutonomousRvrEventLayer.Intent.AssaultRelicKeep))
                return null; // Roamers still retaliate through normal aggro; they do not initiate an unregistered siege.
            // Any closed door of the target keep keeps its lord off-limits, however far this bot stands from it (the
            // nearby-door check let a bot whose search circle reached the lord but not the inner door attack him).
            bool closedDoor = GameServer.KeepManager.GetKeepsOfRegion(bot.CurrentRegionID)
                .Where(keep => $"rvr-keep-{keep.KeepID}" == _rvrDestination?.Id)
                .SelectMany(keep => keep.Doors.Values)
                .Any(door => door.IsAlive && door.IsAttackableDoor && door.State == eDoorState.Closed);
            var keepNavigation=AutonomousKeepApproachNavigation.ForBot(PathfindingProvider.Instance,bot); // Fork: friendly doors by the keep rule
            // Staged assault: outer/inner guards first, then the real lord only
            // after a real gate opens.  The lord's normal death pipeline is the
            // only mechanism that can capture/reset a keep.
            GameKeepGuard[] guards = bot.GetNPCsInRadius(TargetSearchRadius)
                .OfType<GameKeepGuard>()
                // Fork: enemy guards by the realm rule (map section 4b); a realm-less guard only in a battleground
                // (regions 250-253, upstream BattlegroundBrackets.IsBattlegroundRegion; brackets follow in S3).
                .Where(guard => guard.IsAlive && !PvpCombatant.AreAllied(bot, guard) &&
                    (guard.Realm != eRealm.None || guard.CurrentRegionID is >= 250 and <= 253))
                .Where(guard => guard.Component?.Keep != null && $"rvr-keep-{guard.Component.Keep.KeepID}" == _rvrDestination?.Id)
                .Where(guard => !guard.IsPortalKeepGuard && AutonomousRvrKeepPolicy.IsSiegeObjective(guard.Component?.Keep))
                .Where(guard => !closedDoor || guard is not GuardLord)
                .Where(guard => GameServer.ServerRules.IsAllowedToAttack(bot, guard, true))
                .Where(guard => PathfindingProvider.Instance.HasLineOfSight(bot.CurrentZone, new(bot.X, bot.Y, bot.Z),
                    new(guard.X, guard.Y, guard.Z), PathfindingProvider.Instance.BlockingDoorAvoidanceFilters))
                .Where(guard => AutonomousRvrDefense.IsRangedDefender(bot) ||
                    AutonomousZoneItinerary.HasCompleteCorridor(keepNavigation, bot.CurrentZone,
                        new(bot.X, bot.Y, bot.Z), new(guard.X, guard.Y, guard.Z)))
                .OrderBy(guard => guard is GuardLord ? 1 : 0)
                .ThenBy(guard => bot.GetDistanceTo(guard))
                .ToArray();
            // Both attacking realms seek the native capture, not an endless
            // guard patrol. Nearby PvP is handled first by the threat scan.
            if (!closedDoor && guards.OfType<GuardLord>().FirstOrDefault() is { } exposedLord) return exposedLord;
            return SelectDistributedRvrTarget(bot, guards.Cast<GameLiving>().ToArray());
        }

        private CampDestination ChooseRvrDestinationRealm(GameBot bot)
        {
            HashSet<ushort> reachable = ReachableRegions(bot.Realm, bot.CurrentRegionID);
            // A warband that chose a frontier stays on it for a while. Re-planning every minute across all
            // three frontiers sent bots home and out again through the portal keeps over and over
            // (run 16: 6,500 departures an hour, one Nightshade ported 11 times in 30 minutes). Relic
            // carriers anywhere and shared siege or defense calls (handled before this) still override.
            HashSet<ushort> local = GameLoop.GameLoopTime < _rvrFrontierCommittedUntil && reachable.Contains(_rvrFrontierRegion)
                ? [_rvrFrontierRegion] : reachable;
            var choices = new List<CampDestination>();
            var objectives = new List<AutonomousRvrEventLayer.LiveObjective>();
            RvrPlanningView planning = GetRvrPlanningView();
            foreach (GameBot enemy in planning.Candidates
                         .Where(candidate => candidate.IsAlive && PvpCombatant.AllianceRealm(candidate) != eRealm.None &&
                                             !PvpCombatant.AreAllied(bot, candidate) && // Fork: realm rule (map 4b)
                                             AutonomousObjectiveAssignments.Is(candidate, eAutonomousObjectiveKind.RvR) &&
                                             local.Contains(candidate.CurrentRegionID) && IsInFrontier(candidate))
                         .OrderBy(candidate => unchecked((ulong)(candidate.DatabaseID ^ bot.DatabaseID * 397) * 11400714819323198485UL))
                         .Take(24))
            {
                string id = $"rvr-enemy-{enemy.DatabaseID}";
                CampDestination destination = new(id, enemy.Name, enemy.CurrentZone?.Description ?? "frontier",
                    enemy.CurrentRegionID, enemy.X, enemy.Y, enemy.Z, 1, false, true);
                choices.Add(destination);
                objectives.Add(new(id, enemy.Name, AutonomousRvrEventLayer.Intent.HuntEnemy, enemy.Realm,
                    enemy.CurrentRegionID, enemy.X, enemy.Y, enemy.Z, false, 1, 0, 0, 0));
            }

            // Relic ownership remains wholly native. A player or bot carrier
            // is merely a live three-realm event anchor: allies escort, while
            // both other realms can contest the carrier under per-realm caps.
            foreach (GameRelic relic in RelicMgr.GetRelics())
            {
                GameLiving carrier = relic?.CurrentCarrier;
                if (carrier == null && !relic.IsMounted && relic.ObjectState == GameObject.eObjectState.Active &&
                    reachable.Contains(relic.CurrentRegionID))
                {
                    string groundId = RelicObjectiveId(relic);
                    choices.Add(new(groundId, $"dropped {relic.Name}", relic.CurrentZone?.Description ?? "frontier",
                        relic.CurrentRegionID, relic.X, relic.Y, relic.Z, 1, false, true));
                    objectives.Add(new(groundId, $"dropped {relic.Name}", AutonomousRvrEventLayer.Intent.HuntEnemy,
                        relic.OriginalRealm, relic.CurrentRegionID, relic.X, relic.Y, relic.Z, false, 1, 0, 0, 0, true));
                    continue;
                }
                if (carrier == null || !carrier.IsAlive || !reachable.Contains(carrier.CurrentRegionID) || !IsInFrontier(carrier))
                    continue;
                string id = RelicObjectiveId(relic);
                CampDestination destination = new(id, $"active {relic.Name}",
                    carrier.CurrentZone?.Description ?? "frontier", carrier.CurrentRegionID, carrier.X, carrier.Y, carrier.Z, 1, false, true);
                choices.Add(destination);
                objectives.Add(new(id, destination.MonsterName, AutonomousRvrEventLayer.Intent.HuntEnemy, carrier.Realm,
                    carrier.CurrentRegionID, carrier.X, carrier.Y, carrier.Z, false, 1, 0, 0, 0, true));
            }

            // Keep/relic locations are live server objectives, not synthesized
            // camps. All legal reachable frontier keeps get equal selection weight.
            foreach (Region region in WorldMgr.GetAllRegions().Where(region => region != null && local.Contains(region.ID)))
            {
                foreach (AbstractGameKeep keep in GameServer.KeepManager.GetKeepsOfRegion(region.ID)
                             .Where(keep => AutonomousRvrKeepPolicy.IsSiegeObjective(keep) && IsFrontierRegionPoint(keep.Region, keep.X, keep.Y)))
                {
                    string id = $"rvr-keep-{keep.KeepID}";
                    string label = keep.IsRelic ? $"relic keep {keep.Name}" : $"keep {keep.Name}";
                    CampDestination destination = new(id, label, keep.Name, keep.Region, keep.X, keep.Y, keep.Z, 1, false, true);
                    choices.Add(destination);
                    int closedDoors = keep.Doors.Values.Count(door => door.IsAlive && door.State == eDoorState.Closed);
                    int guards = keep.Guards.Values.Count(guard => guard.IsAlive);
                    objectives.Add(new(id, label,
                        !AutonomousRvrKeepPolicy.IsHostileTo(keep, bot.Realm) ? AutonomousRvrEventLayer.Intent.Roam : // Fork: keep rule
                        keep.IsRelic ? AutonomousRvrEventLayer.Intent.AssaultRelicKeep : AutonomousRvrEventLayer.Intent.AssaultKeep,
                        keep.Realm, keep.Region, keep.X, keep.Y, keep.Z, keep.IsRelic,
                        planning.Count(keep.Region, bot.Realm).Enemies,
                        planning.Count(keep.Region, bot.Realm).Allies,
                        guards, closedDoors, UnderAttack:
                        keep.LastAttackedByEnemyTick > 0 && keep.CurrentRegion.Time - keep.LastAttackedByEnemyTick < 120_000 ||
                        keep.Guards.Values.Any(guard => guard.IsAlive && guard.InCombat)));
                    // Every live frontier keep also supplies a patrol anchor;
                    // small warbands can roam there without declaring a siege.
                    string roamId = $"rvr-roam-{keep.KeepID}";
                    choices.Add(new(roamId, $"frontier near {keep.Name}", keep.Name, keep.Region, keep.X, keep.Y, keep.Z, 1, false, true));
                    objectives.Add(new(roamId, $"frontier near {keep.Name}", AutonomousRvrEventLayer.Intent.Roam,
                        keep.Realm, keep.Region, keep.X, keep.Y, keep.Z, keep.IsRelic, 0, 0, guards, closedDoors));
                }
            }

            // Roaming is not limited to the road between keeps. Reuse the
            // already-built live camp catalog and sample a bounded set of
            // frontier clearings; this does not query the database or create
            // PvE goals. Combat remains normal RvR/nearby self-defense.
            var patrolNav = PathfindingProvider.Instance;
            if (patrolNav.IsAvailable)
            {
                foreach (CampCatalogCell cell in CampCatalogSnapshot()
                    .Where(c => c.IsFrontier && !c.IsDungeon && c.LiveMobCount > 0 && local.Contains(c.RegionId) &&
                        c.Zone != null && IsFrontierRegionPoint(c.RegionId, c.X, c.Y))
                    .OrderBy(_ => Random.Shared.Next()).Take(24))
                {
                    Vector3 raw = new(cell.X, cell.Y, cell.Z);
                    Vector3? floor = patrolNav.GetClosestPoint(cell.Zone, raw, 64, 64, 96, patrolNav.DefaultFilters);
                    if (!floor.HasValue || !AutonomousRendezvousNavigation.HasLocalExit(patrolNav, cell.Zone, floor.Value)) continue;
                    string id = "rvr-camp-" + cell.Id;
                    string name = "frontier patrol near " + cell.MonsterName;
                    int x = (int)floor.Value.X, y = (int)floor.Value.Y, z = (int)floor.Value.Z;
                    choices.Add(new(id, name, cell.ZoneName, cell.RegionId, x, y, z, 1, false, true));
                    objectives.Add(new(id, name, AutonomousRvrEventLayer.Intent.Roam, eRealm.None, cell.RegionId,
                        x, y, z, false, 0, 0, 0, 0));
                }
            }

            if (choices.Count == 0)
                return null;

            GameBot[] warband = bot.Group?.GetMembersInTheGroup().OfType<GameBot>()
                .Where(member => member.IsAutonomousWorldBot && AutonomousObjectiveAssignments.Is(member, eAutonomousObjectiveKind.RvR))
                .ToArray() ?? [];
            if (warband.Length == 0)
                warband = [bot];
            int averageLevel = (int)Math.Round(warband.Average(member => member.Level));
            int healers = warband.Count(member => member.ClassName.Contains("cleric", StringComparison.OrdinalIgnoreCase) ||
                                                    member.ClassName.Contains("healer", StringComparison.OrdinalIgnoreCase) ||
                                                    member.ClassName.Contains("druid", StringComparison.OrdinalIgnoreCase) ||
                                                    member.ClassName.Contains("bard", StringComparison.OrdinalIgnoreCase));
            bool siegeReady = warband.Length >= 4 && averageLevel >= 35 && CanSupplySiege(bot);
            AutonomousRvrEventLayer.Plan plan = AutonomousRvrEventLayer.ChooseOrJoin(
                new AutonomousRvrEventLayer.Force(_groupDirective?.GroupId ?? $"rvr-{bot.DatabaseID}", bot.Realm,
                    warband.Length, averageLevel, healers, siegeReady,
                    (unchecked((ulong)(_groupDirective?.Leader?.DatabaseID ?? bot.DatabaseID)) * 2654435761UL % 100) < 30,
                    warband.Select(member => member.DatabaseID).ToArray(), warband.Min(member => member.Level)),
                objectives, GameLoop.GameLoopTime, Random.Shared.NextDouble());
            _rvrSharedEvent = plan?.IsSharedEvent == true;
            _rvrIntent = plan?.Intent ?? AutonomousRvrEventLayer.Intent.Roam;
            bot.TempProperties.SetProperty("RvrWarbandIntent", (int)_rvrIntent);
            bot.TempProperties.SetProperty("RvrDefendingKeep",
                _rvrIntent == AutonomousRvrEventLayer.Intent.DefendEvent && plan.TargetId.StartsWith("rvr-keep-") &&
                int.TryParse(plan.TargetId.Substring(9), out int defendingKeep) ? defendingKeep : -1);
            CampDestination chosen = choices.FirstOrDefault(destination => destination.Id == plan?.TargetId) ??
                (plan == null ? null : new CampDestination(plan.TargetId, plan.Name, plan.Name,
                    plan.RegionId, plan.X, plan.Y, plan.Z, 1, false, true));
            if (chosen != null && chosen.RegionId is 1 or 100 or 200 &&
                (chosen.RegionId != _rvrFrontierRegion || GameLoop.GameLoopTime >= _rvrFrontierCommittedUntil))
            {
                _rvrFrontierRegion = chosen.RegionId;
                _rvrFrontierCommittedUntil = GameLoop.GameLoopTime + FrontierCommitMilliseconds + bot.DatabaseID % 300_000;
            }
            return chosen;
        }

        /// <summary>How long an ordinary warband keeps to the frontier it chose (plus up to five minutes per bot).</summary>
        internal const long FrontierCommitMilliseconds = 20 * 60_000;
        private ushort _rvrFrontierRegion;
        private long _rvrFrontierCommittedUntil;

        private GameRelic FindInteractableRelicRealm(GameBot bot) =>
            RelicMgr.GetRelics()
                .Where(relic => relic.ObjectState is GameObject.eObjectState.Active && relic.CurrentRegionID == bot.CurrentRegionID)
                .Where(relic => Distance(bot.X, bot.Y, relic.X, relic.Y) <= 600)
                .Where(relic => !relic.IsMounted || relic.Realm != PvpCombatant.AllianceRealm(bot) && RelicMgr.CanPickupRelicFromShrine(bot, relic))
                // A nearby shrine is not reachable through an intact gate.
                // Once breached this is checked again on the next AI turn.
                .Where(relic => !relic.IsMounted || FindClosedEnemyDoor(bot, _rvrDestination?.Id) == null)
                .FirstOrDefault();

        private bool StageSoloRvrAtBorderKeepRealm(GameBot bot)
        {
            // Preserve a resumed roamer already beyond its home frontier gate;
            // a restart must not send it all the way back to staging.
            if (_soloRvrBorderStaged || IsInFrontier(bot))
            {
                _soloRvrBorderStaged = true;
                return true;
            }

            // The nearer of the realm's two border keeps, kept for this staging (the staging point belongs to it).
            if (_soloRvrBorderKeep == null && AutonomousRvrStaging.TryGetNearestBorderKeep(bot, out var nearest))
            {
                _soloRvrBorderKeep = nearest;
                Log.Info($"RVR_BORDER_STAGING bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} keep=\"{nearest.Name}\" from={bot.CurrentRegionID}:{bot.X},{bot.Y}");
            }
            if (_soloRvrBorderKeep is not AutonomousRvrStaging.BorderKeep keep)
            {
                SetRvrStatus(bot, "Awaiting a border keep", "Stage safely before frontier roaming",
                    "No realm border-keep contract is configured");
                return false;
            }

            Vector3 destination;
            if (_soloRvrStagingPoint.HasValue)
                destination = _soloRvrStagingPoint.Value;
            else if (TryResolveRvrBorderStaging(keep, out destination, bot.DatabaseID))
                _soloRvrStagingPoint = destination;
            else
            {
                SetRvrStatus(bot, $"Awaiting {keep.Name}", "Stage safely before frontier roaming",
                    "The border keep has no currently validated connected staging floor");
                return false;
            }
            if (bot.CurrentRegionID == keep.RegionId &&
                Distance(bot.X, bot.Y, (int)destination.X, (int)destination.Y) <= 650)
            {
                _soloRvrBorderStaged = true;
                bot.StopMovingOnPath();
                bot.StopMoving();
                SetRvrStatus(bot, $"Staged at {keep.Name}", "Enter the frontier and roam for enemy forces",
                    "Safe border staging complete; selecting a frontier objective");
                return true;
            }

            if (bot.CurrentRegionID != keep.RegionId)
            {
                CampDestination previous = _camp;
                _camp = new($"rvr-stage-{bot.Realm}", keep.Name, keep.Name, keep.RegionId,
                    (int)destination.X, (int)destination.Y, (int)destination.Z, 0, false, false);
                bool moving = TravelAcrossRegions(bot);
                _camp = previous;
                SetRvrStatus(bot, $"Traveling to {keep.Name}", "Stage safely before frontier roaming",
                    "Using the normal collision-safe cross-region route to the realm border keep");
                return moving;
            }

            if (!TryBeginFasterStableRoute(bot, destination, keep.Name))
                IssuePath(bot, destination);
            SetRvrStatus(bot, $"Traveling to {keep.Name}", "Stage safely before frontier roaming",
                "Approaching the safe faction-side border staging point");
            return false;
        }

        // Run 29 (2026-10-20, Dun Dagda): the siege army's healers fought in the melee like everyone else, so they were
        // under attack or dead whenever their army fell, and not one Midgard attacker was raised (the 57 raises there were
        // Hibernian). Players kept their healers behind the line. A siege attacker that can raise, not hit for 5 seconds,
        // stays out of the melee: it steps back from an enemy closer than SiegeBackLineDistance and picks no fight while
        // its army is near, so its brain heals and raises (CheckHeals). Hit, it fights back as before.
        public const int SiegeBackLineDistance = 900;
        public const int SiegeBackLineArmyRadius = 2_500;

        private static bool IsSiegeBackLineHealer(GameBot bot) =>
            bot.ResurrectionSpell != null && GameLoop.GameLoopTime - bot.LastAttackedByEnemyTick >= AutonomousSiegeResurrection.QuietMilliseconds &&
            AutonomousSiegeResurrection.IsSiegeAttacker(bot);

        /// <summary>A back-line healer with an enemy close by steps away from it. True when it stepped.</summary>
        private bool HoldSiegeBackLine(GameBot bot, GameLiving enemy)
        {
            if (!IsSiegeBackLineHealer(bot) || !bot.IsWithinRadius(enemy, SiegeBackLineDistance))
                return false;
            Vector3 here = new(bot.X, bot.Y, bot.Z);
            Vector2 away = new(bot.X - enemy.X, bot.Y - enemy.Y);
            if (away.LengthSquared() < 1) away = new(1, 0);
            away = Vector2.Normalize(away) * 600;
            AutonomousThreatAwarePathing.SafeStep step = AutonomousThreatAwarePathing.ChooseStep(bot, here,
                new(bot.X + away.X, bot.Y + away.Y, bot.Z), 600);
            if (Vector3.DistanceSquared(step.Position, here) < 100 * 100)
                return false;
            if (bot.Brain is BotBrain brain) brain.ClearAggroList();
            bot.StopAttack();
            bot.PathTo(step.Position, bot.MaxSpeed);
            SetRvrStatus(bot, "Healing behind the line", "Siege back line", enemy.Name);
            return true;
        }

        /// <summary>A back-line healer picks no fight of its own while its army is near; the brain heals and raises.</summary>
        private static bool KeepsSiegeBackLine(GameBot bot, GameLiving enemy) =>
            IsSiegeBackLineHealer(bot) && bot.GetNPCsInRadius(SiegeBackLineArmyRadius).OfType<GameBot>()
                .Any(friend => friend != bot && friend.IsAlive && PvpCombatant.AreAllied(bot, friend) && friend.IsAutonomousWorldBot); // Fork: realm rule

        public static double EstimateTravelMinutes(GameBot bot, ushort regionId, int x, int y,
            Dictionary<(eRealm Realm, ushort Region), DbZonePoint> crossings)
        {
            if (bot.CurrentRegionID == regionId)
                return Distance(bot.X, bot.Y, x, y) / Math.Max(1d, bot.MaxSpeed) / 60d;
            var key = (bot.Realm, bot.CurrentRegionID);
            if (!crossings.TryGetValue(key, out DbZonePoint crossing))
                crossings[key] = crossing = FindNextCrossing(bot.Realm, bot.CurrentRegionID, regionId, x, y);
            if (crossing == null)
                return 9999;
            double firstLeg = Distance(bot.X, bot.Y, crossing.SourceX, crossing.SourceY) / Math.Max(1d, bot.MaxSpeed) / 60d;
            double finalBias = crossing.TargetRegion == regionId
                ? Distance(crossing.TargetX, crossing.TargetY, x, y) / Math.Max(1d, bot.MaxSpeed) / 60d
                : 6;
            return firstLeg + finalBias + 1;
        }
    }
}
