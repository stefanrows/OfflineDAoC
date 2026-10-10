using System;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS;

public sealed partial class AutonomousWorldBotController
{
    private long _keepAbandonLogAfter;
    private string _siegeColumnKey;
    private long _siegeColumnStarted, _siegeColumnProgress, _siegeColumnLogAfter;
    private float _siegeColumnBestGap;
    private double _siegePorterBestGap = double.PositiveInfinity;
    private bool _siegePorterHadTicket;
    private bool _siegeColumnHolding;
    /// <summary>The keep whose arrival this leader last logged (RVR_KEEP_ARRIVED), so one visit logs once.</summary>
    private string _keepArrivedLogged;
    /// <summary>Earliest game time of the next RVR_ASSAULT_GATE line for this controller.</summary>
    private long _assaultGateLogAfter;

    private bool HoldSiegeColumn(GameBot leader, string forceId, CampDestination destination)
    {
        Group group = leader?.Group;
        if (destination == null || group == null) return false;
        string targetId = destination.Id;
        long now = GameLoop.GameLoopTime;
        string key = $"{forceId}:{targetId}:{leader.CurrentRegionID}";
        if (_siegeColumnKey != key)
        {
            _siegeColumnKey = key;
            _siegeColumnHolding = false;
            _siegePorterBestGap = double.PositiveInfinity;
            _siegePorterHadTicket = false;
        }
        // Include living operators beyond the old 4,000-unit cutoff and on
        // another region leg. Corpses cannot catch up and do not hold travel.
        GameBot[] missing = group.GetMembersInTheGroup().OfType<GameBot>()
            .Where(member => member != leader && member.IsAlive &&
                (member.CurrentRegionID != leader.CurrentRegionID || member.IsOnStableMasterRoute ||
                 member.GetDistanceTo(leader) > AutonomousRvrSpeed.ResumeGap)).ToArray();
        float worst = missing.Select(member => member.CurrentRegionID == leader.CurrentRegionID
            ? (float)member.GetDistanceTo(leader) : float.PositiveInfinity).DefaultIfEmpty(0).Max();
        // Regroup progress is measured from the fixed hold centre, so the
        // leader's loop cannot reset the stall timer by running toward a straggler.
        Vector3 anchor = _siegeColumnHolding && AutonomousLeaderLoop.TryGetHoldCentre(leader, out Vector3 centre)
            ? centre : new(leader.X, leader.Y, leader.Z);
        float worstFromAnchor = AutonomousRvrSpeed.WorstGapFrom(anchor, missing.Select(member =>
            (member.CurrentRegionID == leader.CurrentRegionID, new Vector3(member.X, member.Y, member.Z))));
        if (!_siegeColumnHolding)
        {
            _siegeColumnStarted = _siegeColumnProgress = now;
            _siegeColumnBestGap = worstFromAnchor;
        }
        else if (AutonomousRvrSpeed.SiegeColumnClosedUp(_siegeColumnBestGap, worstFromAnchor))
        {
            _siegeColumnBestGap = worstFromAnchor;
            _siegeColumnProgress = now;
        }
        // A column meeting at a porter must let its leader approach and buy
        // the ticket. Otherwise followers wait at boarding radius while the
        // leader holds just outside it forever (observed in 0.217.0).
        var passage = leader.TempProperties.GetProperty<AutonomousFrontierTransport.Request>(AutonomousFrontierTransport.RequestKey);
        if (leader.CurrentRegionID != destination.RegionId && passage?.Porter?.CurrentRegion == leader.CurrentRegion)
        {
            double porterGap = leader.GetDistanceTo(passage.Porter);
            bool ticket = AutonomousFrontierTransport.Ticket(leader, passage.Passage) != null;
            if (porterGap < _siegePorterBestGap - 100 || ticket && !_siegePorterHadTicket)
            { _siegePorterBestGap = porterGap; _siegeColumnProgress = now; }
            _siegePorterHadTicket = ticket;
        }
        var decision = AutonomousRvrSpeed.SiegeCohesion(_siegeColumnHolding, worst,
            now - _siegeColumnStarted, now - _siegeColumnProgress);
        if (decision != AutonomousRvrSpeed.SiegeCohesionDecision.Advance && missing.Length > 0 &&
            AutonomousSiegeProperties.SIEGE_COLUMN_QUORUM_MARCH)
        {
            // Bug 75: march on once the leader, the ram carriers and a quorum
            // stand together; stragglers follow (AutonomousRvrSiegeMuster.FollowsLeader).
            // The tighter resume gap while holding keeps the column from flapping.
            float togetherGap = _siegeColumnHolding ? AutonomousRvrSpeed.ResumeGap : AutonomousRvrSpeed.HoldGap;
            GameBot[] living = group.GetMembersInTheGroup().OfType<GameBot>()
                .Where(member => member != leader && member.IsAlive).ToArray();
            bool Together(GameBot member) => member.CurrentRegionID == leader.CurrentRegionID &&
                !member.IsOnStableMasterRoute && member.GetDistanceTo(leader) <= togetherGap;
            GameBot[] left = living.Where(member => !Together(member)).ToArray();
            int together = 1 + living.Length - left.Length;
            bool carriersTogether = !left.Any(member =>
                AutonomousSiegeJobs.HasRamAssignment(member, destination.Id, destination.RegionId));
            if (AutonomousRvrSpeed.SiegeQuorumMarch(true, AutonomousSiegeProperties.SIEGE_COLUMN_QUORUM,
                    living.Length + 1, together, carriersTogether))
            {
                if (now >= _siegeColumnLogAfter)
                {
                    _siegeColumnLogAfter = now + 30_000;
                    Log.Info($"RVR_SIEGE_COLUMN force={forceId} target={targetId} action=quorum leader={leader.Name} " +
                        $"together={together} living={living.Length + 1} left={left.Length} " +
                        $"members=\"{string.Join(";", left.Select(member => $"{member.Name}/{member.DatabaseID}@{member.CurrentRegionID}"))}\"");
                }
                decision = AutonomousRvrSpeed.SiegeCohesionDecision.Advance;
            }
        }
        if (decision == AutonomousRvrSpeed.SiegeCohesionDecision.Advance)
        {
            if (_siegeColumnHolding) Log.Info($"RVR_SIEGE_COLUMN force={forceId} target={targetId} action=resumed leader={leader.Name}");
            _siegeColumnHolding = false;
            return false;
        }
        if (!_siegeColumnHolding || now >= _siegeColumnLogAfter)
        {
            _siegeColumnLogAfter = now + 30_000;
            Log.Info($"RVR_SIEGE_COLUMN force={forceId} target={targetId} action={decision} leader={leader.Name} " +
                $"actor={leader.CurrentRegionID}:{leader.X},{leader.Y},{leader.Z} gap={worst} heldMs={now-_siegeColumnStarted} " +
                $"members=\"{string.Join(";", missing.Select(member => $"{member.Name}/{member.DatabaseID}@{member.CurrentRegionID}:{member.X},{member.Y},{member.Z}:combat={member.InCombat}:operator={BotSiegeRuntime.Assigned(member)}"))}\"");
        }
        _siegeColumnHolding = true;
        if (decision != AutonomousRvrSpeed.SiegeCohesionDecision.Fail &&
            leader.CurrentRegionID != destination.RegionId && TryFrontierTransport(leader, destination)) return true;
        if (decision == AutonomousRvrSpeed.SiegeCohesionDecision.Fail)
        {
            leader.StopMovingOnPath(); leader.StopMoving();
            AbandonKeepTarget(leader, destination, "March cohesion recovery exhausted", now, routeFailure: false);
        }
        else
        {
            bool looping = LoopInsteadOfHold(leader);
            AutonomousStuckWatchdog.MarkProgress(leader, eAutonomousProgressKind.Objective);
            SetRvrStatus(leader, "Regrouping the siege column", destination.MonsterName, looping
                ? "Circling at a run while living members and operators close up"
                : "Waiting for living members and operators to finish combat or their legal return route");
        }
        return true;
    }

    /// <summary>
    /// A holding travel leader runs a small loop around its hold point
    /// instead of standing still (<see cref="AutonomousLeaderLoop"/>); without
    /// a valid loop it stops exactly as before. Returns whether it loops.
    /// </summary>
    private bool LoopInsteadOfHold(GameBot leader, bool nearHazard = false)
    {
        // The hold (and its loop) lasts while each controller turn holds; the
        // first turn that does not hold ends it (see Tick).
        _holdingThisTurn = true;
        if (!AutonomousLeaderLoop.TryLoop(leader, nearHazard))
        {
            leader.StopMovingOnPath();
            leader.StopMoving();
            return false;
        }
        // The interrupted travel order is issued afresh once the hold ends.
        _issuedRouteDestination = null;
        _nextMoveOrderTick = 0;
        // Think again soon enough to run on to the next loop point.
        if (leader.Brain is BotBrain brain)
            brain.ThinkInterval = Math.Min(brain.ThinkInterval, AutonomousLeaderLoop.LoopThinkIntervalMilliseconds);
        return true;
    }

    private bool _holdingThisTurn;

    /// <summary>Ends a leader loop once a controller turn no longer holds.</summary>
    private void EndLeaderLoopUnlessHolding(GameBot bot)
    {
        if (!_holdingThisTurn)
            AutonomousLeaderLoop.EndHold(bot);
        _holdingThisTurn = false;
    }

    private string _keepTravelKey;
    private long _keepTravelGeometry, _keepTravelRetry;
    private int _keepTravelFailures, _keepTravelIndex;
    private Vector3 _keepPlanningOrigin;
    private Vector3? _keepTravelLastPosition;
    private Vector3[] _keepTravelPoints;
    private RvrPlanningNavigation _keepPlanning;
    private const string KeepAssaultApproachProperty = "KeepAssaultApproach";
    private sealed record KeepAssaultApproach(string Target, ushort Region, Vector3 Point);

    public static bool HasReachedKeepAssaultApproach(GameBot leader, string targetId)
    {
        var arrival = leader?.TempProperties.GetProperty<KeepAssaultApproach>(KeepAssaultApproachProperty);
        return leader?.IsAlive == true && arrival != null && arrival.Target == targetId && leader.CurrentRegionID == arrival.Region &&
            Vector3.DistanceSquared(new(leader.X, leader.Y, leader.Z), arrival.Point) <= 650 * 650;
    }

    /// <summary>A 2003 warband tried a keep route about three times, then
    /// called it off and roamed from where it stood instead of porting home:
    /// the group is already in the frontier, and a failed route to one keep
    /// says nothing about the fights around it. The decision belongs to the
    /// leader (stored in the event layer), not an isolated follower.</summary>
    public const int KeepRouteGiveUpFailures = 3;

    public static bool ShouldAbandonKeepRoute(int consecutiveFailures) => consecutiveFailures >= KeepRouteGiveUpFailures;

    private static string RvrForceOf(GameBot bot) =>
        bot.TempProperties.GetProperty<string>("RvrEventForce") ?? $"rvr-{bot.DatabaseID}";

    /// <summary>Drop the keep objective after repeated route failures: the
    /// force leaves the siege and will not rejoin or reopen this keep for
    /// twenty minutes; the normal planner picks a roam target from the
    /// current spot on the next turn.</summary>
    private void AbandonKeepTarget(GameBot bot, CampDestination destination, string failure, long now, bool routeFailure = true)
    {
        if (destination == null) return;
        // A follower's failed return or repeated roadside death cannot cancel
        // the column's battle. Even a released leader waits for its living
        // members' actual keep combat to finish before abandoning the force.
        // The coordinator's leader decides (bug 75: a dead group leader in
        // another region deferred every abandon for 21 minutes).
        GameLiving decider = bot.Group != null && _groupDirective?.ObjectiveKind == eAutonomousObjectiveKind.RvR &&
            _groupDirective.Leader?.Group == bot.Group ? _groupDirective.Leader : bot.Group?.LivingLeader;
        if (decider is GameBot leader &&
            (leader != bot || bot.Group.GetMembersInTheGroup().OfType<GameBot>().Any(member =>
                member != bot && member.IsAlive && member.CurrentRegionID == destination.RegionId &&
                (member.InCombat || member.IsAttacking || (member.Brain as BotBrain)?.HasAggro == true) &&
                Vector2.DistanceSquared(new(member.X, member.Y), new(destination.X, destination.Y)) <= 6500 * 6500)))
        {
            if (now >= _keepAbandonLogAfter)
            {
                _keepAbandonLogAfter = now + 30_000;
                Log.Info($"RVR_KEEP_ABANDON_DEFERRED force={RvrForceOf(bot)} bot={bot.Name} target={destination.Id} reason=\"{failure}\" leader={leader.Name}");
            }
            return;
        }
        string forceId = RvrForceOf(bot);
        AutonomousRvrEventLayer.AbandonTarget(forceId, destination.Id, now);
        // The opener also skips this keep for a while (an hour, doubling on repeats).
        if (routeFailure) AutonomousRvrEventLayer.NoteKeepRouteFailure(destination.Id, now);
        ClearKeepObjective(bot);
        SetRvrStatus(bot, "Keep route abandoned", destination.MonsterName,
            $"{failure}; the warband roams the frontier from here instead");
        Log.Warn($"RVR_KEEP_ROUTE_ABANDONED bot=\"{bot.Name}\" id={bot.DatabaseID} realm={bot.Realm} " +
            $"target=\"{destination.Id}\" region={bot.CurrentRegionID} force={forceId} reason=\"{failure}\" " +
            $"avoidMs={AutonomousRvrEventLayer.AbandonedTargetMilliseconds}");
    }

    private bool TryRunKeepClaim(GameBot bot)
    {
        if (_rvrIntent != AutonomousRvrEventLayer.Intent.ClaimKeep || _rvrDestination == null ||
            bot.Group != null && bot.Group.LivingLeader != bot) return false;
        string forceId = _groupDirective?.GroupId ?? $"rvr-{bot.DatabaseID}";
        bot.TempProperties.SetProperty("RvrEventForce", forceId);
        var keep = int.TryParse(_rvrDestination.Id.AsSpan(9), out int keepId)
            ? GameServer.KeepManager.GetKeepByID(keepId) : null;
        if (!AutonomousRvrKeepPolicy.IsClaimableKeep(keep) || !PvpClaimAvailable(keep) ||
            !AutonomousRvrEventLayer.RenewClaimPlan(forceId, _rvrDestination.Id, GameLoop.GameLoopTime))
        {
            AutonomousRvrEventLayer.ReleaseClaimPlan(forceId, _rvrDestination.Id);
            ClearKeepObjective(bot);
            _rvrIntent = AutonomousRvrEventLayer.Intent.Roam;
            return false;
        }
        ReleaseOwnedSiegeRams(bot);
        TravelRvrObjective(bot, _rvrDestination);
        if (_rvrDestination != null)
            SetRvrStatus(bot, "Claiming keep", "Secure the free keep for our guild",
                "Traveling to the defeated keep's claim steward", keep.Name);
        return true;
    }

    private static bool PvpClaimAvailable(DOL.GS.Keeps.AbstractGameKeep keep) =>
        keep?.Guild == null && keep?.DBKeep.LordDefeated == true &&
        keep.ClaimPoint?.ObjectState == GameObject.eObjectState.Active;

    private void ClearKeepObjective(GameBot bot)
    {
        AutonomousRvrEventLayer.ReleaseClaimPlan(_groupDirective?.GroupId ?? $"rvr-{bot.DatabaseID}", _rvrDestination?.Id);
        bot.TempProperties.RemoveProperty(KeepAssaultApproachProperty);
        _keepTravelKey = null; _keepTravelPoints = null; _keepPlanning = null;
        _keepTravelFailures = 0; _keepTravelRetry = 0;
        _rvrDestination = null; _rvrApproachDestination = null;
        _rvrTravelWaypoint = null; _patrolDestination = null;
        _hunterPatrolArrivedTick = 0; _nextRvrPlanReview = 0;
        bot.StopMovingOnPath(); bot.StopMoving();
    }

    private Vector3[] VaryKeepDeparture(GameBot bot, CampDestination destination, Vector3[] steps,
        RvrPlanningNavigation nav, Vector3 origin)
    {
        if (steps.Length == 0 || bot.CurrentZone == null ||
            _groupDirective?.IsDynamic == true && _groupDirective.Leader != bot ||
            Vector2.DistanceSquared(new(origin.X, origin.Y), new(destination.X, destination.Y)) < 11_500 * 11_500)
            return steps;
        // Insert one local connected departure. Keep every proved seam and the assault endpoint.
        int local = 0;
        while (local + 1 < steps.Length && bot.CurrentRegion.GetZone((int)steps[local + 1].X, (int)steps[local + 1].Y) == bot.CurrentZone)
            local++;
        if (bot.CurrentRegion.GetZone((int)steps[local].X, (int)steps[local].Y) != bot.CurrentZone) return steps;
        int seed = unchecked((int)((_groupDirective?.Leader?.DatabaseID ?? bot.DatabaseID) * 397) ^
            StringComparer.Ordinal.GetHashCode(destination.Id) ^
            StringComparer.Ordinal.GetHashCode(bot.PersistentRecord?.ObjectiveAssignmentId ?? RvrForceOf(bot)) ^
            (bot.PersistentRecord?.DeathCount ?? 0));
        var random = new Random(seed);
        bool atHub = AutonomousHubDeparture.TryGetSafeAnchor(bot.Realm, bot.CurrentRegionID, origin.X, origin.Y, out var anchor);
        Vector3? hub = atHub ? new Vector3(anchor.Centre.X, anchor.Centre.Y, origin.Z) : null;
        var danger = AutonomousRvrDangerMemory.Worst(AutonomousRvrDangerMemory.KeyFor(bot), bot.CurrentRegionID,
            new(origin.X, origin.Y), 8000, WorldSimulationClock.UtcNow);
        var variant = hub.HasValue ? RvrRouteVariant.HubFan : danger.HasValue ? RvrRouteVariant.Cover :
            random.Next(3) switch { 0 => RvrRouteVariant.Road, 1 => RvrRouteVariant.Flank, _ => RvrRouteVariant.Cover };
        var choice = AutonomousRvrRoutePolicy.ChooseRoute(origin, steps[local], variant,
            AutonomousRvrTravel.Probe(nav, bot.CurrentRegion, bot.CurrentZone, steps[local]), random,
            heat: danger?.Centre, hubCentre: hub, hubSafeRadius: atHub ? anchor.Radius : AutonomousRvrRoutePolicy.KeepSafeRadius);
        // Both sides of the inserted waypoint must connect to the retained first road point.
        bool insert = bot.CurrentRegion.GetZone((int)steps[0].X, (int)steps[0].Y) == bot.CurrentZone && !choice.Fallback && Vector3.DistanceSquared(origin, choice.Waypoint) > 200 * 200 &&
            AutonomousZoneItinerary.HasCompleteCorridor(nav, bot.CurrentZone, choice.Waypoint, steps[0]);
        Log.Info($"RVR_KEEP_DEPARTURE bot={bot.Name} target={destination.Id} variant={variant} inserted={insert}");
        return insert ? new[] { choice.Waypoint }.Concat(steps).ToArray() : steps;
    }

    private static long KeepTravelGeometry(Region region, Zone destination, string targetId)
    {
        // A distant border door opening must not restart every defender's job.
        // Intermediate cached queries carry their own zone revision instead.
        long stamp = NavigationGeometryRevision.Read(destination);
        // Ownership can change permission to traverse a closed friendly gate.
        foreach (var keep in GameServer.KeepManager.GetKeepsOfRegion(region.ID))
        {
            stamp = unchecked(stamp * 31 + keep.KeepID * 7 + (int)keep.Realm +
                StringComparer.Ordinal.GetHashCode(keep.Guild?.GuildID ?? string.Empty));
            if ($"rvr-keep-{keep.KeepID}" != targetId) continue;
            foreach (var door in keep.Doors.Values)
                stamp = unchecked(stamp * 31 + (int)door.State * 7 + (door.IsAlive ? 1 : 0));
        }
        return stamp;
    }

    /// <returns>True while planning/travelling/waiting. False only at the proved
    /// endpoint, so existing combat and patrol decisions retain control there.</returns>
    private bool FollowKeepTravel(GameBot bot, CampDestination destination,
        AutonomousRvrEventLayer.GuildArmyOrder army = null)
    {
        using var profile = BotThinkProfiler.Measure(BotThinkPhase.KeepTravel);
        long now = GameLoop.GameLoopTime;
        if (!AutonomousGuildKeepDefense.IsRecalled(bot) &&
            AutonomousRvrEventLayer.IsAbandoned(RvrForceOf(bot), destination.Id, now))
        {
            // Another member already called this keep off for the warband.
            ClearKeepObjective(bot);
            SetRvrStatus(bot, "Keep route abandoned", destination.MonsterName,
                "The warband called this keep off; roaming the frontier from here instead");
            return true;
        }
        Vector3 current = new(bot.X, bot.Y, bot.Z);
        string key = $"{bot.PersistentRecord?.ObjectiveAssignmentId}:{bot.CurrentRegionID}:{bot.Realm}:{destination.Id}:{destination.X}:{destination.Y}:{destination.Z}:army={army?.Generation}";
        long geometry = KeepTravelGeometry(bot.CurrentRegion, bot.CurrentRegion.GetZone(destination.X,destination.Y),destination.Id);
        if (_keepTravelKey != key || _keepTravelGeometry != geometry ||
            _keepTravelLastPosition.HasValue && Vector3.DistanceSquared(current, _keepTravelLastPosition.Value) > 8000 * 8000 ||
            _keepTravelPoints != null && _keepTravelIndex >= _keepTravelPoints.Length && Vector3.DistanceSquared(current, _keepTravelPoints[^1]) > 650 * 650 ||
            _keepPlanning != null && Vector3.DistanceSquared(current, _keepPlanningOrigin) > 96 * 96)
        {
            bot.TempProperties.RemoveProperty(KeepAssaultApproachProperty);
            _keepTravelKey = key; _keepTravelGeometry = geometry;
            _keepTravelPoints = null; _keepPlanning = null; _keepTravelIndex = 0;
            _keepTravelRetry = 0; _keepTravelFailures = 0;
            _rvrApproachDestination = null;
            _rvrTravelWaypoint = null; _patrolDestination = null;
        }
        _keepTravelLastPosition = current;
        if (_keepTravelPoints == null)
        {
            if (now < _keepTravelRetry) return true;
            if (_keepPlanning == null)
            {
                // Cancel only the obsolete travel order. Otherwise a redirected
                // roamer moves the search origin every slice and never finishes.
                bot.StopMovingOnPath(); bot.StopMoving();
                // Match the native mover's small start-polygon tolerance for
                // planning only. Never relocate the actor or pick a distant
                // lower floor to repair an unproved position.
                _keepPlanningOrigin = AutonomousKeepApproachNavigation.TryPlanningOrigin(
                    PathfindingProvider.Instance, bot.CurrentZone, current, out var origin) ? origin : current;
                _keepPlanning = new RvrPlanningNavigation(AutonomousKeepApproachNavigation.ForBot(PathfindingProvider.Instance, bot));
            }
            _keepPlanning.BeginSlice();
            var planningWork = _keepPlanning;
            string failure = "No connected exterior route";
            try
            {
                Vector3 endpoint;
                bool resolved = army == null
                    ? TryResolveKeepTravelApproach(bot, destination, _keepPlanning, _keepPlanningOrigin, out endpoint)
                    : TryGuildArmyCamp(bot, destination, _keepPlanning, _keepPlanningOrigin, army, out endpoint);
                if (resolved && RvrKeepRoute.TryBuild(bot.CurrentRegion, _keepPlanning, bot.Realm, _keepPlanningOrigin, endpoint, out var steps))
                {
                    try { steps = VaryKeepDeparture(bot, destination, steps, _keepPlanning, _keepPlanningOrigin); }
                    catch (RvrPlanningNavigation.Limit)
                    {
                        Log.Info($"RVR_KEEP_DEPARTURE bot={bot.Name} target={destination.Id} fallback=optional_budget");
                    }
                    _keepTravelPoints = steps; _keepTravelIndex = 0;
                    _keepArrivedLogged = null;
                    _rvrApproachDestination = endpoint;
                    _keepPlanning = null; _keepTravelFailures = 0;
                }
            }
            catch (RvrPlanningNavigation.Yield)
            {
                _keepTravelRetry = now + 250;
                if (bot.Brain is BotBrain brain) brain.ThinkInterval = 250;
                SetRvrStatus(bot, "Planning keep route", destination.MonsterName,
                    "Continuing a bounded route search; combat remains responsive");
                return true;
            }
            catch (RvrPlanningNavigation.Limit) { failure = "Bounded route-work limit reached"; }
            finally
            {
                // Charge only work actually spent planning. Combat, casting,
                // transport and background AI cadence must not consume the
                // search's time allowance and discard a valid partial route.
                planningWork.EndSlice();
            }
            if (_keepTravelPoints == null)
            {
                int queries = _keepPlanning.Queries;
                _keepPlanning = null;
                _keepTravelFailures = Math.Min(4, _keepTravelFailures + 1);
                _keepTravelRetry = now + _keepTravelFailures * 30_000 + Math.Abs(bot.DatabaseID % 3000);
                bot.StopMovingOnPath(); bot.StopMoving();
                SetRvrStatus(bot, "Keep route retry", destination.MonsterName,
                    $"{failure}; retaining the siege and backing off before retrying");
                Log.Warn($"RVR_KEEP_ROUTE_FAILED bot=\"{bot.Name}\" id={bot.DatabaseID} realm={bot.Realm} " +
                    $"target=\"{destination.Id}\" region={bot.CurrentRegionID} from={current} queries={queries} " +
                    $"reason=\"{failure}\" retryMs={_keepTravelRetry-now} failures={_keepTravelFailures}");
                if (ShouldAbandonKeepRoute(_keepTravelFailures) && !AutonomousGuildKeepDefense.IsRecalled(bot) &&
                    (bot.Group == null || bot.Group.LivingLeader == bot))
                    AbandonKeepTarget(bot, destination, failure, now, routeFailure: army == null);
                return true;
            }
        }
        // Ordinary patrols retain stable travel. An assembled assault keeps
        // its leader on foot with the column instead of outrunning followers.
        if (_keepTravelIndex == 0 && _rvrIntent != AutonomousRvrEventLayer.Intent.DefendEvent &&
            !(_groupDirective?.IsDynamic == true && _groupDirective.ObjectiveKind == eAutonomousObjectiveKind.RvR &&
              AutonomousRvrEventLayer.MusterPhaseOf(_groupDirective.GroupId) != AutonomousRvrSiegeMuster.Phase.None) &&
            IsInFrontier(bot) && Vector2.Distance(new(current.X,current.Y),
                new(_keepTravelPoints[^1].X,_keepTravelPoints[^1].Y)) > 650 &&
            TryBeginFasterStableRoute(bot,_keepTravelPoints[^1],destination.ZoneName))
        {
            _keepTravelKey = null; _keepTravelPoints = null;
            return true;
        }
        int reachedIndex = _keepTravelIndex;
        while (_keepTravelIndex < _keepTravelPoints.Length &&
            Vector3.DistanceSquared(current, _keepTravelPoints[_keepTravelIndex]) <= 80 * 80)
            _keepTravelIndex++;
        // A reached road segment is approach progress only when it brings this
        // member closer than before (the per-member best distance survives
        // replans, so the same segment is never credited twice).
        if (_keepTravelIndex > reachedIndex)
            AutonomousRvrEventLayer.ReportMarch(destination.Id, RvrForceOf(bot), bot.DatabaseID, bot.CurrentRegionID,
                current, bot.InCombat, now);
        if (_keepTravelIndex >= _keepTravelPoints.Length)
        {
            _rvrApproachDestination = _keepTravelPoints[^1];
            if (army == null)
            {
                if (_keepArrivedLogged != destination.Id)
                {
                    _keepArrivedLogged = destination.Id;
                    Log.Info($"RVR_KEEP_ARRIVED bot=\"{bot.Name}\" target={destination.Id} region={bot.CurrentRegionID} intent={_rvrIntent} shared={_rvrSharedEvent} level={bot.Level}");
                }
                bot.TempProperties.SetProperty(KeepAssaultApproachProperty,
                    new KeepAssaultApproach(destination.Id, bot.CurrentRegionID, _keepTravelPoints[^1]));
                // A staging camp is not arrival at the actual siege approach.
                AutonomousRvrEventLayer.ReportBattleActivity(destination.Id, now, RvrForceOf(bot));
            }
            return false;
        }
        Vector3 next = _keepTravelPoints[_keepTravelIndex];
        var zone = bot.CurrentRegion.GetZone((int)next.X, (int)next.Y);
        if (zone != bot.CurrentZone && _keepTravelIndex > 0 &&
            Vector3.DistanceSquared(current, next) <= 256 * 256)
        {
            // Only the explicit inside/outside pair of an already-proved seam.
            // No teleport, wall shortcut, or fresh cross-region chord.
            if (!bot.IsMoving) bot.WalkTo(next, bot.MaxSpeed);
        }
        // Bend around a named/far-above monster, then resume the road; a
        // failed bend falls through to the road order and its failure path.
        else if (AutonomousRvrMobAvoidance.TryWalkBend(next, AvoidDangerousMobs(bot, next), bend => IssuePath(bot, bend), DropMobBypass))
        { }
        else if (!IssuePath(bot, next, preciseArrival: true))
        {
            _keepTravelPoints = null; _keepPlanning = null;
            _keepTravelRetry = now + 30_000;
            return true;
        }
        SetRvrStatus(bot, "Traveling to keep", destination.MonsterName,
            $"Following validated road segment {_keepTravelIndex+1}/{_keepTravelPoints.Length}");
        return true;
    }
}
