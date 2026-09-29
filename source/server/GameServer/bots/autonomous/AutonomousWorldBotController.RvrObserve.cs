using System;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS;

/// <summary>
/// Wave 3 of livelier RvR bots (C5, principle P2): the group holds outside
/// caster reach while its leader watches a fight, then acts on the leader's
/// decision. Self-defence runs earlier in the bot's turn and always wins.
/// </summary>
public sealed partial class AutonomousWorldBotController
{
    private int _seenRvrObserveSerial;
    private WeakReference<object> _seenRvrObserveOwner;
    private Vector3? _rvrObserveAvoid;
    private long _rvrObserveAvoidUntil;
    private bool _rvrObserveReplan;

    /// <summary>How long a roam-on keeps the next legs away from the fight.</summary>
    private const int RvrObserveAvoidMilliseconds = 120_000;

    internal static bool IsFrontierForObserve(GameLiving living) => IsInFrontier(living);

    /// <summary>
    /// After a leave, forget the roam target (a new one is chosen after the
    /// retreat run); after a roam-on, keep the destination unless it lies at
    /// the fight, and bend the next leg away from the fight.
    /// </summary>
    private void HandleRvrObserveOutcome(GameBot bot)
    {
        if (bot.Group != null && bot.Group.LivingLeader != bot ||
            !AutonomousRvrObserve.TryGetOutcome(bot, out int serial, out RvrObserveDecision outcome, out Vector3 centre,
                out object owner))
            return;
        object seenOwner = null;
        if (_seenRvrObserveOwner?.TryGetTarget(out seenOwner) != true || !ReferenceEquals(seenOwner, owner))
        {
            // A new, re-formed or joined group: its past outcomes are not ours.
            _seenRvrObserveOwner = new WeakReference<object>(owner);
            _seenRvrObserveSerial = serial;
            return;
        }
        if (serial == _seenRvrObserveSerial)
            return;
        _seenRvrObserveSerial = serial;
        if (outcome == RvrObserveDecision.Leave ||
            _rvrDestination != null && _rvrDestination.RegionId == bot.CurrentRegionID &&
            Distance(_rvrDestination.X, _rvrDestination.Y, (int)centre.X, (int)centre.Y) <= AutonomousRvrObserve.HoldDistance)
        {
            if (_rvrSharedEvent)
                return;
            _rvrDestination = null;
            _rvrApproachDestination = null;
            _rvrTravelWaypoint = null;
            _hunterPatrolArrivedTick = 0;
            _nextRvrPlanReview = 0;
            return;
        }
        if (outcome != RvrObserveDecision.RoamOn || _rvrDestination == null || _rvrDestination.RegionId != bot.CurrentRegionID)
            return;
        _rvrObserveAvoid = centre;
        _rvrObserveAvoidUntil = GameLoop.GameLoopTime + RvrObserveAvoidMilliseconds;
        _rvrObserveReplan = AutonomousRvrObserve.PassesNear(new(bot.X, bot.Y),
            new(_rvrDestination.X, _rvrDestination.Y), new(centre.X, centre.Y), AutonomousRvrObserve.HoldDistance);
    }

    /// <summary>The fight to keep away from on the next Cover leg, if a roam-on set one.</summary>
    private Vector3? RvrObserveAvoidPoint() =>
        _rvrObserveAvoid.HasValue && GameLoop.GameLoopTime < _rvrObserveAvoidUntil ? _rvrObserveAvoid : null;

    /// <summary>
    /// While the leader watches, the group holds: the leader walks to its
    /// hold point (out of caster reach, off the road when it stood on it),
    /// members gather around it and heal, nobody sits down.
    /// </summary>
    private bool TryRvrObserveHold(BotBrain brain, GameBot bot)
    {
        if (!AutonomousRvrDoctrineRuntime.Applies(bot) ||
            !AutonomousRvrObserve.IsObserving(bot, out Vector3? hold, out Vector3 nearestHostile, out float margin))
            return false;
        bool leader = bot.Group == null || bot.Group.LivingLeader == bot;
        if (!hold.HasValue)
        {
            if (!leader)
                return false;
            hold = FindRvrObserveSpot(bot, nearestHostile, margin);
            AutonomousRvrObserve.SetHoldPoint(bot, hold.Value);
        }

        Vector3 here = new(bot.X, bot.Y, bot.Z);
        Vector3 slot = leader ? hold.Value : AutonomousRvrDoctrineGeometry.Scatter(hold.Value, bot.ObjectID, 250);
        bot.WakeRecoveryRest();
        if (Vector2.Distance(new(here.X, here.Y), new(hold.Value.X, hold.Value.Y)) > (leader ? 150 : 400))
        {
            IssuePath(bot, slot);
            SetRvrStatus(bot, "Pulling back to watch", "Observe before engaging",
                "Holding outside caster range while the leader watches the fight");
            return true;
        }
        bot.StopMovingOnPath();
        bot.StopMoving();
        // Face the fight, like players watching it; turn only when clearly off.
        ushort heading = bot.GetHeading((int)nearestHostile.X, (int)nearestHostile.Y);
        int off = Math.Abs(heading - bot.Heading) % 4096;
        if (Math.Min(off, 4096 - off) > 256)
            bot.TurnTo((int)nearestHostile.X, (int)nearestHostile.Y);
        brain.CheckHeals();
        SetRvrStatus(bot, "Watching the fight", "Observe before engaging",
            "Holding position and waiting for the leader's call");
        return true;
    }

    /// <summary>
    /// The hold point: here when the nearest hostile is already 2,200 away
    /// and we are not on a road leg; otherwise a step back to 2,200 plus this
    /// hold's 0-400 margin from it (picked again when the fight drifts within
    /// 1,800 of the hold point),
    /// moved 600-1,200 to the side away from it when we stood on the road.
    /// Every candidate must be reachable; else the ground-clamped step back,
    /// else here.
    /// </summary>
    private Vector3 FindRvrObserveSpot(GameBot bot, Vector3 nearestHostile, float margin)
    {
        Vector3 here = new(bot.X, bot.Y, bot.Z);
        Vector3? back = AutonomousRvrObserve.StepBackPoint(here, nearestHostile, margin);
        bool onRoad = _rvrTravelWaypoint.HasValue && _rvrRouteVariant == RvrRouteVariant.Road;
        if (!back.HasValue && !onRoad)
            return here;
        Vector3 origin = back ?? here;
        Vector3[] candidates;
        if (onRoad)
        {
            Vector2 leg = new(_rvrTravelGoal.X - here.X, _rvrTravelGoal.Y - here.Y);
            Vector3[] side = AutonomousRvrGroupPause.OffRoadCandidates(origin, leg, nearestHostile,
                Random.Shared.NextDouble(), Random.Shared.NextDouble());
            candidates = back.HasValue ? [side[0], side[1], back.Value] : side;
        }
        else
            candidates = [back.Value];
        Vector3? spot = AutonomousRvrTravel.FirstReachableOffRoad(bot, candidates);
        if (spot.HasValue)
            return spot.Value;
        if (back.HasValue && bot.CurrentZone != null)
            return PathfindingProvider.Instance.GetMoveAlongSurface(bot.CurrentZone, here, back.Value,
                PathfindingProvider.Instance.DefaultFilters) ?? here;
        return here;
    }
}
