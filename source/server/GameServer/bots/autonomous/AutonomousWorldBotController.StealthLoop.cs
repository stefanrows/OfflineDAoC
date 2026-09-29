using System;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// Wave 5 of livelier RvR bots (P9): a stealth-doctrine assassin that leads
/// (or roams alone) waits at its roaming spot beside the road, 600-1,200
/// units off the leg it came in on, instead of walking circles on the spot.
/// </summary>
public sealed partial class AutonomousWorldBotController
{
    private object _stealthWaitFor;
    private Vector3? _stealthWaitSpot;

    /// <summary>
    /// Holds the stealther at its wait spot. False when the loop does not
    /// apply or no reachable spot beside the road exists; the ordinary patrol
    /// then runs.
    /// </summary>
    private bool HoldStealthWaitSpot(GameBot bot)
    {
        if (_rvrDestination == null || !AutonomousRvrStealthLoop.Applies(bot) ||
            bot.Group != null && bot.Group.LivingLeader != bot)
            return false;
        if (!ReferenceEquals(_stealthWaitFor, _rvrDestination))
        {
            _stealthWaitFor = _rvrDestination;
            Vector3 center = _rvrApproachDestination ?? new(_rvrDestination.X, _rvrDestination.Y, _rvrDestination.Z);
            Vector3 here = new(bot.X, bot.Y, bot.Z);
            // The leg the stealther arrived on stands in for the road.
            Vector2 leg = new(center.X - here.X, center.Y - here.Y);
            Vector3[] candidates = AutonomousRvrGroupPause.OffRoadCandidates(center, leg, null,
                Random.Shared.NextDouble(), Random.Shared.NextDouble());
            _stealthWaitSpot = AutonomousRvrTravel.FirstReachableOffRoad(bot, candidates);
        }
        if (!_stealthWaitSpot.HasValue)
            return false;
        Vector3 spot = _stealthWaitSpot.Value;
        if (Vector2.DistanceSquared(new(bot.X, bot.Y), new(spot.X, spot.Y)) > 80 * 80)
            IssuePath(bot, spot);
        else if (bot.IsMoving)
        {
            bot.StopMovingOnPath();
            bot.StopMoving();
        }
        SetRvrStatus(bot, "Waiting in stealth", "Roam active frontier keeps, relic routes, and enemy forces",
            "Watching the road from cover beside it", _rvrDestination.MonsterName);
        return true;
    }
}
