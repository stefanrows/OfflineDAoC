using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS
{

    public sealed partial class AutonomousWorldBotController
    {
        private readonly record struct ValidatedRouteDetour(Vector3 Waypoint, Vector3 Rejoin);

        private sealed class RouteThreatHistory
        {
            public int PullAttempts;
            public int Detours;
            public long LastSeenTick;
            public long RangedPullRetryUntil;
            public long LastDecisionLogTick;
            public string LastLoggedAction;
        }

        private readonly Dictionary<GameNPC, RouteThreatHistory> _routeThreatHistory = new();
        private long _nextRouteThreatScanTick;
        private GameNPC _routeThreatHoldTarget;
        private string _routeThreatHoldAction = string.Empty;
        private long _routeThreatHoldUntilTick;
        private int _routeThreatHoldPackSize;
        private ConColor _routeThreatHoldCon;
        private string _routeThreatHoldPullerName = string.Empty;
        private string _routeThreatCampId = string.Empty;
        private ushort _routeThreatCampRegion;
        private Vector3 _routeThreatCampPoint;
        private bool _hasRouteThreatCamp;
        private bool _routeThreatHandledCurrentPath;
        private Vector3? _routeThreatDetourRejoinWaypoint;
        private bool _routeThreatNeedsCampRescan;

        /// <summary>Called only from IssuePath. True means the travel order was consumed.</summary>
        private bool GuardOpenWorldTravel(GameBot bot, Vector3 campDestination)
        {
            if (!AutonomousRouteThreatPolicy.Enabled || !IsOutdoorPveTravel(bot, campDestination))
            {
                ClearRouteThreatHold();
                ClearRouteThreatDetourSequence();
                return false;
            }

            using var profile = BotThinkProfiler.Measure(BotThinkPhase.OutdoorRouteThreat);
            long now = GameLoop.GameLoopTime;
            SynchronizeRouteThreatCamp();
            Zone zone = bot.CurrentZone;
            if (TryMaintainOutdoorRouteThreatHold(bot, campDestination)) return true;
            if (now < _nextRouteThreatScanTick) return false;
            _nextRouteThreatScanTick = now + AutonomousRouteThreatPolicy.ScanMilliseconds +
                bot.ObjectID % AutonomousRouteThreatPolicy.ScanStaggerMilliseconds;

            IPathfindingMgr nav = PathfindingProvider.Instance;

            GameNPC[] threats = bot.GetNPCsInRadius(AutonomousRouteThreatPolicy.ScanRadius)
                .Where(npc => IsEligibleRouteThreat(bot, zone, npc) &&
                    Vector2.DistanceSquared(new(npc.X, npc.Y), new(campDestination.X, campDestination.Y)) >
                    AutonomousRouteThreatPolicy.CampSkipRadius * AutonomousRouteThreatPolicy.CampSkipRadius)
                .OrderBy(npc => bot.GetDistanceTo(npc))
                .Take(AutonomousRouteThreatPolicy.MaximumThreatCandidates)
                .ToArray();
            PruneRouteThreatHistory(now, zone);
            if (threats.Length == 0 || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;

            bool detouring = _routeRecoveryWaypoint.HasValue;
            Vector3 scanTarget = _routeRecoveryWaypoint ?? campDestination;
            int pathQueriesRemaining = AutonomousRouteThreatPolicy.MaximumNativePathQueriesPerScan;
            Span<Vector3> route = stackalloc Vector3[257];
            Vector3 current = new(bot.X, bot.Y, bot.Z);
            if (!TryGetCompleteRoute(nav, zone, current, scanTarget, route, ref pathQueriesRemaining, out int routeCount))
                return false;

            GameNPC blocker = null;
            float firstAlong = float.MaxValue;
            foreach (GameNPC npc in threats)
            {
                var mob = (StandardMobBrain)npc.Brain;
                Vector3 position = new(npc.X, npc.Y, npc.Z);
                if (Vector2.DistanceSquared(new(position.X, position.Y),
                        new(campDestination.X, campDestination.Y)) <=
                    AutonomousRouteThreatPolicy.CampSkipRadius * AutonomousRouteThreatPolicy.CampSkipRadius)
                    continue;
                if (!AutonomousDungeonPolicy.IntersectsCorridor(route[..routeCount], position,
                        mob.AggroRange + ThreatMargin(bot), AutonomousRouteThreatPolicy.LookAhead, out float along) ||
                    along >= firstAlong)
                    continue;
                blocker = npc;
                firstAlong = along;
            }

            if (blocker == null) return false;

            var blockerBrain = (StandardMobBrain)blocker.Brain;
            int packSize = CountNearbyPack(blocker, blockerBrain, threats);
            ConColor con = ConLevels.GetConColor(bot.GetConLevel(blocker));
            bool grouped = bot.Group != null;
            GameBot puller = grouped ? _groupDirective?.Puller : bot;
            bool pullAllowed = puller != null && puller.CurrentRegion == bot.CurrentRegion &&
                puller.CurrentZone == zone && !blocker.InCombat;
            RouteThreatHistory history = GetRouteThreatHistory(blocker, now);
            if (history.RangedPullRetryUntil > now)
            {
                return HoldForRouteThreat(bot, blocker, history, "WaitingForRangedPull",
                    history.RangedPullRetryUntil, now, packSize, con);
            }
            history.RangedPullRetryUntil = 0;

            bool bossLike = blocker is IGameEpicNpc ||
                AutonomousRouteThreatPolicy.IsUnreachableFlyer(blocker, bot) &&
                !IsRangedClass(bot);

            ValidatedRouteDetour? detour = null;
            if (!detouring && AutonomousRouteThreatPolicy.ShouldTryDetour(con, grouped, packSize,
                    bossLike, pullAllowed, alreadyDetouring: false, history.PullAttempts, history.Detours))
            {
                detour = FindValidatedRouteDetour(nav, zone, route[..routeCount], current, blocker,
                    blockerBrain.AggroRange, ThreatMargin(bot), threats, ref pathQueriesRemaining);
            }

            RouteThreatAction action = AutonomousRouteThreatPolicy.Decide(con, grouped, packSize, bossLike,
                pullAllowed, detour.HasValue, detouring, history.PullAttempts, history.Detours);

            if (action == RouteThreatAction.Pull && grouped &&
                puller == bot && !AutonomousBotGroupCoordinator.CanInitiateNewPull(bot, corridorBlocker: true))
            {
                return HoldForRouteThreat(bot, blocker, history, "WaitingForGroup",
                    _nextRouteThreatScanTick, now, packSize, con);
            }

            if (action == RouteThreatAction.Pull)
            {
                Span<Vector3> targetRoute = stackalloc Vector3[257];
                Vector3 target = new(blocker.X, blocker.Y, blocker.Z);
                Vector3 pullOrigin = puller == null ? current : new(puller.X, puller.Y, puller.Z);
                bool reachable = TryGetCompleteRoute(nav, zone, pullOrigin, target, targetRoute,
                    ref pathQueriesRemaining, out int targetRouteCount) &&
                    AutonomousRouteThreatPolicy.IsEndpointClose(targetRoute[targetRouteCount - 1], target);
                if (!reachable && !detouring && history.Detours < AutonomousRouteThreatPolicy.MaximumDetoursPerThreat)
                {
                    detour = FindValidatedRouteDetour(nav, zone, route[..routeCount], current, blocker,
                        blockerBrain.AggroRange, ThreatMargin(bot), threats, ref pathQueriesRemaining);
                    if (detour.HasValue)
                        action = RouteThreatAction.Detour;
                }

                if (action == RouteThreatAction.Pull && !reachable)
                    action = RouteThreatAction.RejectCamp;
            }

            if (action == RouteThreatAction.Detour && detour.HasValue)
            {
                history.Detours++;
                history.LastSeenTick = now;
                LogRouteThreatDecision(bot, blocker, "Detour", packSize, con, history, now);
                ClearRouteThreatDetourSequence();
                _routeRecoveryWaypoint = detour.Value.Waypoint;
                _routeThreatDetourRejoinWaypoint = detour.Value.Rejoin;
                _routeRecoveryArrivalRadius = 60;
                _nextMoveOrderTick = 0;
                bot.ForcePathReplot();
                _routeThreatHandledCurrentPath = true;
                SetStatus(bot, $"Going around {blocker.Name}", GoalText(),
                    $"Following a complete navmesh detour around the {(packSize > 1 ? $"pack of {packSize}" : "route threat")}",
                    blocker.Name, _camp.ZoneName);
                return false;
            }

            if (action == RouteThreatAction.Pull)
            {
                bot.StopMovingOnPath();
                bot.StopMoving();

                if (grouped && puller != bot)
                {
                    OutdoorRouteBlockerHandoffResult handoff = AutonomousBotGroupCoordinator.TryHandoffOutdoorRouteBlocker(
                        bot, blocker, puller);
                    if (handoff == OutdoorRouteBlockerHandoffResult.WaitingForGroup)
                    {
                        return HoldForRouteThreat(bot, blocker, history, "WaitingForGroup",
                            _nextRouteThreatScanTick, now, packSize, con, puller.Name);
                    }
                    if (handoff != OutdoorRouteBlockerHandoffResult.Started)
                    {
                        action = RouteThreatAction.RejectCamp;
                    }
                    else
                    {
                        history.PullAttempts++;
                        history.LastSeenTick = now;
                        history.RangedPullRetryUntil = now + AutonomousDefensivePull.TimeoutMilliseconds;
                        RetainRouteThreatHold(blocker, "WaitingForRangedPull", history.RangedPullRetryUntil,
                            packSize, con);
                        _routeInterruptedByCombat = true;
                        _routeRecoveryWaypoint = null;
                        ClearRouteThreatDetourSequence();
                        AutonomousBotGroupCoordinator.MarkCombatObserved(bot.Group);
                        LogRouteThreatDecision(bot, blocker, "PullHandoff", packSize, con, history, now);
                        SetStatus(bot, $"Clearing route threat: {blocker.Name}", GoalText(),
                            $"The designated puller {puller.Name} is handling one reachable route threat",
                            blocker.Name, _camp.ZoneName);
                        return true;
                    }
                }

                if (action == RouteThreatAction.Pull)
                {
                    history.PullAttempts++;
                    history.LastSeenTick = now;
                    bot.TargetObject = blocker;
                    _lastEngagedCon = con;
                    _routeInterruptedByCombat = true;
                    _routeRecoveryWaypoint = null;
                    ClearRouteThreatDetourSequence();
                    AutonomousBotGroupCoordinator.MarkCombatObserved(bot.Group);
                    if (grouped && AutonomousDefensivePull.TryBegin(bot, blocker))
                    {
                        history.RangedPullRetryUntil = now + AutonomousDefensivePull.TimeoutMilliseconds;
                        RetainRouteThreatHold(blocker, "WaitingForRangedPull", history.RangedPullRetryUntil,
                            packSize, con);
                        LogRouteThreatDecision(bot, blocker, "RangedPull", packSize, con, history, now);
                        SetStatus(bot, $"Preparing route pull: {blocker.Name}", GoalText(),
                            "The ordinary defensive pull system is drawing one reachable monster to the group",
                            blocker.Name, _camp.ZoneName);
                        return true;
                    }
                    RetainRouteThreatHold(blocker, "WaitingForPullResolution", _nextRouteThreatScanTick,
                        packSize, con);
                    if (!grouped) RecordSoloPull(bot);
                    if (bot.Brain is BotBrain brain)
                    {
                        brain.AddToAggroList(blocker, Math.Max(25, blocker.EffectiveLevel * 10));
                        brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                    }
                    LogRouteThreatDecision(bot, blocker, "Pull", packSize, con, history, now);
                    SetStatus(bot, $"Clearing route threat: {blocker.Name}", GoalText(),
                        "Stopped on a complete corridor and engaged one eligible monster with ordinary bot combat",
                        blocker.Name, _camp.ZoneName);
                    return true;
                }
            }

            LogRouteThreatDecision(bot, blocker, "RejectCamp", packSize, con, history, now);
            AbandonCamp(bot, $"Outdoor route blocked by {blocker.Name} (level {blocker.EffectiveLevel}, pack {packSize}); no bounded safe pull or validated detour remains");
            return true;
        }

        private bool IsOutdoorPveTravel(GameBot bot, Vector3 destination)
        {
            if (bot is not { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false,
                    IsPersistentPlayerCompanion: false, IsPlayerLedGroup: false } ||
                bot.Brain is not BotBrain brain || bot.CurrentZone == null || bot.CurrentZone.IsDungeon ||
                bot.CurrentRegion?.IsDungeon == true || bot.IsOnStableMasterRoute || bot.IsReturningAfterRelease ||
                bot.InCombat || bot.IsAttacking || brain.HasAggro || IsSafeArea(bot) ||
                AutonomousObjectiveAssignments.IsBetweenPveTasks(bot) ||
                AutonomousObjectiveAssignments.KindFor(bot) is not (eAutonomousObjectiveKind.SoloPve or eAutonomousObjectiveKind.GroupPve) ||
                AutonomousRealmRaid.GetView(bot.Group) != null || AutonomousSiegeMarch.IsMarching(bot) ||
                _camp == null || _camp.IsDungeon || _camp.IsFrontier || _camp.RegionId != bot.CurrentRegionID ||
                bot.CurrentRegion?.GetZone((int)destination.X, (int)destination.Y) != bot.CurrentZone ||
                _serviceNpc != null || _trainingTrainer != null || _capitalTransit != null ||
                _rvrDestination != null || _patrolDestination.HasValue || _walkToCampAfterRelease ||
                Vector3.DistanceSquared(destination, new(_camp.X, _camp.Y, _camp.Z)) > 32 * 32 ||
                IsFrontierZone(bot.CurrentRegionID, bot.CurrentZone.ID))
                return false;

            if (bot.Group == null)
                return AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.SoloPve);

            return _groupDirective is { IsDynamic: true, ObjectiveKind: eAutonomousObjectiveKind.GroupPve } directive &&
                directive.Leader == bot;
        }

        private bool TryMaintainOutdoorRouteThreatHold(GameBot bot, Vector3 campDestination)
        {
            if (_routeThreatHoldTarget == null) return false;
            if (!AutonomousRouteThreatPolicy.Enabled || !IsOutdoorPveTravel(bot, campDestination))
            {
                ClearRouteThreatHold();
                return false;
            }

            SynchronizeRouteThreatCamp();
            long now = GameLoop.GameLoopTime;
            GameNPC blocker = _routeThreatHoldTarget;
            if (!IsEligibleRouteThreat(bot, bot.CurrentZone, blocker) || now >= _routeThreatHoldUntilTick)
            {
                ClearRouteThreatHold();
                return false;
            }

            RouteThreatHistory history = GetRouteThreatHistory(blocker, now);
            return HoldForRouteThreat(bot, blocker, history, _routeThreatHoldAction,
                _routeThreatHoldUntilTick, now, _routeThreatHoldPackSize, _routeThreatHoldCon,
                _routeThreatHoldPullerName);
        }

        private static bool IsEligibleRouteThreat(GameBot bot, Zone zone, GameNPC npc) =>
            npc is not GameBot and not GameSummonedPet && IsExperienceMonster(npc) && npc.IsAlive &&
            npc.CurrentRegion == bot.CurrentRegion &&
            npc.CurrentZone == zone && npc.Brain is StandardMobBrain mob && mob.AggroRange > 0 &&
            !IsProtectedQuestOrServiceActor(npc) && GameServer.ServerRules.IsAllowedToAttack(bot, npc, true) &&
            mob.CanAggroTarget(bot);

        private bool HoldForRouteThreat(GameBot bot, GameNPC blocker, RouteThreatHistory history,
            string action, long untilTick, long now, int packSize, ConColor con, string designatedPullerName = "")
        {
            RetainRouteThreatHold(blocker, action, untilTick, packSize, con, designatedPullerName);
            bot.StopMovingOnPath();
            bot.StopMoving();
            LogRouteThreatDecision(bot, blocker, action, packSize, con, history, now);
            string detail = action == "WaitingForRangedPull"
                ? $"Holding the camp route while the designated puller completes its bounded firing attempt on {blocker.Name}"
                : action == "WaitingForPullResolution"
                    ? $"Holding the camp route while ordinary combat establishes on {blocker.Name}"
                : string.IsNullOrEmpty(designatedPullerName)
                    ? $"Waiting for the group to be ready before clearing {blocker.Name}"
                    : $"Waiting for designated puller {designatedPullerName} and the rest of the party before clearing {blocker.Name}";
            string activity = action switch
            {
                "WaitingForRangedPull" => "Waiting for route pull",
                "WaitingForPullResolution" => "Holding for route combat",
                _ => "Waiting before route threat",
            };
            SetStatus(bot, activity,
                GoalText(), detail, blocker.Name, _camp.ZoneName);
            return true;
        }

        private void RetainRouteThreatHold(GameNPC blocker, string action, long untilTick,
            int packSize, ConColor con, string designatedPullerName = "")
        {
            _routeThreatHoldTarget = blocker;
            _routeThreatHoldAction = action;
            _routeThreatHoldUntilTick = untilTick;
            _routeThreatHoldPackSize = packSize;
            _routeThreatHoldCon = con;
            _routeThreatHoldPullerName = designatedPullerName;
        }

        private void ClearRouteThreatHold()
        {
            _routeThreatHoldTarget = null;
            _routeThreatHoldAction = string.Empty;
            _routeThreatHoldUntilTick = 0;
            _routeThreatHoldPackSize = 0;
            _routeThreatHoldCon = default;
            _routeThreatHoldPullerName = string.Empty;
        }

        private static bool IsProtectedQuestOrServiceActor(GameNPC npc) =>
            npc.QuestListToGive.Count > 0 || npc.DataQuestList.Count > 0 || npc.ShowTeleporterIndicator ||
            npc is GameMerchant or GameGuard or GameTrainer or GameTeleporter or GameHealer or GameTaxi or CraftNPC;

        private static int CountNearbyPack(GameNPC blocker, StandardMobBrain blockerBrain, GameNPC[] threats)
        {
            int pack = 1;
            foreach (GameNPC other in threats)
            {
                if (other == blocker || Math.Abs(other.Z - blocker.Z) > 160 ||
                    other.Brain is not StandardMobBrain otherBrain)
                    continue;
                float sharedAggroEnvelope = Math.Max(320,
                    blockerBrain.AggroRange + otherBrain.AggroRange + 80);
                if (Vector2.DistanceSquared(new(other.X, other.Y), new(blocker.X, blocker.Y)) <
                    sharedAggroEnvelope * sharedAggroEnvelope)
                    pack++;
            }
            return pack;
        }

        private static int ThreatMargin(GameBot bot) => IsRangedClass(bot)
            ? AutonomousRouteThreatPolicy.RangedMargin
            : AutonomousRouteThreatPolicy.MeleeMargin;

        private static bool IsRangedClass(GameBot bot)
        {
            eCharacterClass characterClass = (eCharacterClass)(bot.CharacterClass?.ID ?? 0);
            return BotSpellPower.IsOffensiveCaster(bot) || characterClass is
                eCharacterClass.Scout or eCharacterClass.Hunter or eCharacterClass.Ranger;
        }

        private ValidatedRouteDetour? FindValidatedRouteDetour(IPathfindingMgr nav, Zone zone, ReadOnlySpan<Vector3> corridor,
            Vector3 start, GameNPC blocker, int aggroRange, int margin, GameNPC[] threats,
            ref int pathQueriesRemaining)
        {
            Vector3 threatPosition = new(blocker.X, blocker.Y, blocker.Z);
            if (!AutonomousRouteThreatPolicy.NearestOnRoute(corridor, threatPosition,
                    AutonomousRouteThreatPolicy.LookAhead, out Vector3 routePoint, out Vector2 direction,
                    out float along))
                return null;

            float rejoinDistance = aggroRange + margin + AutonomousRouteThreatPolicy.DetourClearance;
            if (!AutonomousRouteThreatPolicy.PointAtDistance(corridor, along + rejoinDistance * 1.5f,
                    out Vector3 rejoin))
                return null;

            ValidatedRouteDetour? best = null;
            float bestCost = float.MaxValue;
            Span<Vector3> firstLeg = stackalloc Vector3[257];
            Span<Vector3> secondLeg = stackalloc Vector3[257];
            for (int side = 1; side >= -1; side -= 2)
            {
                Vector3 rawCandidate = AutonomousRouteThreatPolicy.DetourPoint(threatPosition, direction,
                    aggroRange, margin, routePoint.Z, side);

                if (!TryGetCompleteRoute(nav, zone, start, rawCandidate, firstLeg,
                        ref pathQueriesRemaining, out int firstCount) ||
                    !AutonomousRouteThreatPolicy.IsEndpointClose(firstLeg[firstCount - 1], rawCandidate))
                    continue;
                Vector3 candidate = firstLeg[firstCount - 1];
                if (!PathClearOfThreats(firstLeg[..firstCount], threats, margin)) continue;

                if (!TryGetCompleteRoute(nav, zone, candidate, rejoin, secondLeg,
                        ref pathQueriesRemaining, out int secondCount) ||
                    !AutonomousRouteThreatPolicy.IsEndpointClose(secondLeg[secondCount - 1], rejoin) ||
                    !PathClearOfThreats(secondLeg[..secondCount], threats, margin))
                    continue;

                float cost = Vector3.Distance(start, candidate) + Vector3.Distance(candidate, rejoin);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = new ValidatedRouteDetour(candidate, rejoin);
                }
            }
            return best;
        }

        private static bool PathClearOfThreats(ReadOnlySpan<Vector3> route, GameNPC[] threats, int margin)
        {
            foreach (GameNPC npc in threats)
            {
                if (npc.Brain is not StandardMobBrain mob ||
                    AutonomousDungeonPolicy.IntersectsCorridor(route, new(npc.X, npc.Y, npc.Z),
                        mob.AggroRange + margin, float.MaxValue, out _))
                    return false;
            }
            return true;
        }

        private static bool TryGetCompleteRoute(IPathfindingMgr nav, Zone zone, Vector3 start, Vector3 end,
            Span<Vector3> route, ref int pathQueriesRemaining, out int count)
        {
            count = 0;
            if (pathQueriesRemaining <= 0 || route.Length < 2) return false;
            pathQueriesRemaining--;
            Span<WrappedPathfindingNode> nodes = stackalloc WrappedPathfindingNode[256];
            PathfindingResult result = nav.GetPathStraight(zone, start, end,
                nav.BlockingDoorAvoidanceFilters, nodes);
            if (!AutonomousRouteThreatPolicy.IsCompleteCorridor(result, nodes.Length) ||
                result.NodeCount > route.Length - 1)
                return false;

            route[0] = start;
            count = 1;
            for (int i = 0; i < result.NodeCount; i++)
            {
                Vector3 next = nodes[i].Position;
                if (Vector3.DistanceSquared(route[count - 1], next) < 1) continue;
                route[count++] = next;
            }
            return count > 1 && AutonomousRouteThreatPolicy.IsEndpointClose(route[count - 1], end);
        }

        private RouteThreatHistory GetRouteThreatHistory(GameNPC threat, long now)
        {
            if (_routeThreatHistory.TryGetValue(threat, out RouteThreatHistory history))
            {
                history.LastSeenTick = now;
                return history;
            }

            if (_routeThreatHistory.Count >= AutonomousRouteThreatPolicy.MaximumRememberedThreats)
            {
                GameNPC oldestThreat = null;
                long oldestTick = long.MaxValue;
                foreach (var entry in _routeThreatHistory)
                {
                    if (entry.Value.LastSeenTick >= oldestTick) continue;
                    oldestThreat = entry.Key;
                    oldestTick = entry.Value.LastSeenTick;
                }
                if (oldestThreat != null) _routeThreatHistory.Remove(oldestThreat);
            }

            history = new RouteThreatHistory { LastSeenTick = now };
            _routeThreatHistory[threat] = history;
            return history;
        }

        private void PruneRouteThreatHistory(long now, Zone zone)
        {
            foreach (GameNPC threat in _routeThreatHistory.Keys.ToArray())
            {
                RouteThreatHistory history = _routeThreatHistory[threat];
                if (now - history.LastSeenTick >= AutonomousRouteThreatPolicy.ThreatHistoryExpiryMilliseconds ||
                    threat.ObjectState != GameObject.eObjectState.Active || !threat.IsAlive ||
                    threat.CurrentRegion != zone.ZoneRegion)
                    _routeThreatHistory.Remove(threat);
            }
        }

        private void SynchronizeRouteThreatCamp()
        {
            if (_camp != null && _hasRouteThreatCamp &&
                string.Equals(_routeThreatCampId, _camp.Id, StringComparison.Ordinal) &&
                _routeThreatCampRegion == _camp.RegionId &&
                _routeThreatCampPoint == new Vector3(_camp.X, _camp.Y, _camp.Z))
                return;

            _routeThreatHistory.Clear();
            _nextRouteThreatScanTick = 0;
            ClearRouteThreatHold();
            ClearRouteThreatDetourSequence();
            _hasRouteThreatCamp = _camp != null;
            _routeThreatCampId = _camp?.Id ?? string.Empty;
            _routeThreatCampRegion = _camp?.RegionId ?? 0;
            _routeThreatCampPoint = _camp == null ? default : new(_camp.X, _camp.Y, _camp.Z);
        }

        private void ClearRouteThreatDetourSequence()
        {
            if (_routeThreatDetourRejoinWaypoint.HasValue || _routeThreatNeedsCampRescan)
                _routeRecoveryWaypoint = null;
            _routeThreatDetourRejoinWaypoint = null;
            _routeThreatNeedsCampRescan = false;
        }

        private void LogRouteThreatDecision(GameBot bot, GameNPC blocker, string action,
            int packSize, ConColor con, RouteThreatHistory history, long now)
        {
            if (string.Equals(history.LastLoggedAction, action, StringComparison.Ordinal) &&
                now - history.LastDecisionLogTick < 30_000)
                return;
            history.LastLoggedAction = action;
            history.LastDecisionLogTick = now;
            Log.Info($"AUTONOMOUS_OUTDOOR_ROUTE_THREAT bot={bot.Name} id={bot.DatabaseID} camp={_camp?.Id} " +
                $"action={action} target=\"{blocker.Name}\" level={blocker.EffectiveLevel} con={con} pack={packSize} " +
                $"pullAttempts={history.PullAttempts} detours={history.Detours} zone=\"{bot.CurrentZone?.Description}\"");
        }

    }

}
