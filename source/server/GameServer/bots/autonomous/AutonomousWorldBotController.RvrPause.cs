using System;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS;

/// <summary>
/// Wave 2 of livelier RvR bots (C3, principles P5 and P6): a roaming group
/// rests after a fight before it walks on, and a finished retreat makes the
/// leader choose a new destination instead of walking back into the same
/// fight. Self-defence runs earlier in the bot's turn and always wins.
/// </summary>
public sealed partial class AutonomousWorldBotController
{
    private int _seenRvrRetreatSerial;
    /// <summary>The group the seen serial belongs to; serials are per group,
    /// so a new or re-formed group starts counting again.</summary>
    private WeakReference<Group> _seenRvrRetreatGroup;

    /// <summary>After a PvP retreat ends, forget the old roam target once.</summary>
    private void HandleFinishedRvrRetreat(GameBot bot)
    {
        long now = GameLoop.GameLoopTime;
        bool known = AutonomousRvrDoctrineRuntime.TryGetRetreat(bot, out int serial, out long until);
        Group seenGroup = null;
        if (_seenRvrRetreatGroup?.TryGetTarget(out seenGroup) != true || !ReferenceEquals(seenGroup, bot.Group))
        {
            _seenRvrRetreatGroup = bot.Group == null ? null : new WeakReference<Group>(bot.Group);
            _seenRvrRetreatSerial = known ? AutonomousRvrGroupPause.SeenSerialAfterGroupChange(now, until, serial) : 0;
        }
        if (!known || !AutonomousRvrGroupPause.RetreatFinished(now, until, serial, _seenRvrRetreatSerial))
            return;
        _seenRvrRetreatSerial = serial;
        if (bot.Group?.LivingLeader == bot)
            AutonomousRvrGroupPause.NoteRetreatEnded(bot, now);
        _rvrDestination = null;
        _rvrApproachDestination = null;
        _rvrTravelWaypoint = null;
        _hunterPatrolArrivedTick = 0;
        _nextRvrPlanReview = 0;
    }

    /// <summary>
    /// The group rests (sit, heal, rez) while its leader holds a pause; the
    /// leader first walks to an off-road spot when one was found.
    /// </summary>
    private bool TryRvrGroupPause(BotBrain brain, GameBot bot)
    {
        if (bot.Group == null || !AutonomousRvrDoctrineRuntime.Applies(bot))
            return false;
        bool leader = bot.Group.LivingLeader == bot;
        // Nobody sits down while the group escorts a relic home.
        bool relicInGroup = bot.Group.GetMembersInTheGroup().Any(GameRelic.IsPlayerCarryingRelic);
        if (leader)
            AutonomousRvrGroupPause.EvaluateLeader(bot,
                _rvrTravelWaypoint.HasValue && _rvrRouteVariant == RvrRouteVariant.Road, relicInGroup,
                fight => FindRvrRestSpot(bot, fight));
        if (relicInGroup || !AutonomousRvrGroupPause.IsPausing(bot, out Vector3 restPoint))
            return false;

        Vector3 here = new(bot.X, bot.Y, bot.Z);
        Vector3 slot = leader ? restPoint : AutonomousRvrDoctrineGeometry.Scatter(restPoint, bot.ObjectID, 220);
        if (Vector2.Distance(new(here.X, here.Y), new(restPoint.X, restPoint.Y)) > (leader ? 150 : 400))
        {
            bot.WakeRecoveryRest();
            IssuePath(bot, slot);
            SetRvrStatus(bot, "Moving off the road to rest", "Recover after the fight",
                "The group is leaving the road before it sits down");
            return true;
        }
        bot.StopMovingOnPath();
        bot.StopMoving();
        if (brain.CheckHeals())
            return true;
        bool full = AutonomousRestPolicy.IsFullyRecovered(bot.HealthPercent, bot.ManaPercent,
            bot.EndurancePercent, bot.MaxMana > 0);
        if (full)
            bot.WakeRecoveryRest();
        else if (!BotRestRecovery.BlocksRest(bot))
            bot.BeginRecoveryRest();
        SetRvrStatus(bot, "Resting after the fight", "Recover after the fight",
            $"Resting, rezzing and regaining power before moving on: HP {bot.HealthPercent}% • power {bot.ManaPercent}%");
        return true;
    }

    /// <summary>
    /// A validated rest spot 600-1,200 units beside the current leg, or null.
    /// The leg is the route toward the travel goal, else away from the fight.
    /// </summary>
    private Vector3? FindRvrRestSpot(GameBot bot, Vector3? fight)
    {
        Vector3 here = new(bot.X, bot.Y, bot.Z);
        Vector2 leg = _rvrTravelWaypoint.HasValue
            ? new Vector2(_rvrTravelGoal.X - here.X, _rvrTravelGoal.Y - here.Y)
            : fight.HasValue ? new Vector2(here.X - fight.Value.X, here.Y - fight.Value.Y) : Vector2.Zero;
        Vector3[] candidates = AutonomousRvrGroupPause.OffRoadCandidates(here, leg, fight,
            Random.Shared.NextDouble(), Random.Shared.NextDouble());
        return AutonomousRvrTravel.FirstReachableOffRoad(bot, candidates);
    }
}
