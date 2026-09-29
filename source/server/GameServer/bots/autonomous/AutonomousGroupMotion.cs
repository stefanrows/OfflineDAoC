using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace DOL.GS;

/// <summary>
/// Smooth group travel. Followers used to walk to a copy of their slot taken
/// when the order was issued, stop there, and think again seconds later: a
/// stop-go conga line along the leader's old route. Now a follower steers
/// toward a slot around where the leader is about to be, keeps walking while
/// the leader walks, re-steers only when the slot has really moved, and
/// matches the leader's speed with a little personal variance and catch-up.
/// </summary>
public static class AutonomousGroupMotion
{
    private sealed class Order
    {
        public Vector3 Target;
        public long IssuedTick;
        public short Speed;
    }

    private sealed class GroupHeading
    {
        public Vector2 Forward;
        public long LastMovingTick;
    }

    private static readonly ConditionalWeakTable<GameBot, Order> Orders = new();
    private static readonly ConditionalWeakTable<Group, GroupHeading> Headings = new();

    /// <summary>How far ahead of the leader followers aim, in seconds of its travel.</summary>
    public const double LookaheadSeconds = 0.8;
    public const float ResteerDistance = 48;
    public const int TravelShapeGraceMilliseconds = 2_000;

    /// <summary>The leader's position a moment ahead along its travel direction.</summary>
    public static Vector3 PredictLeader(GameLiving leader)
    {
        Vector3 here = new(leader.X, leader.Y, leader.Z);
        if (!leader.IsMoving || leader.CurrentSpeed <= 0)
            return here;
        Point2D ahead = leader.GetPointFromHeading(leader.Heading, (int)(leader.CurrentSpeed * LookaheadSeconds));
        return new Vector3(ahead.X, ahead.Y, leader.Z);
    }

    /// <summary>
    /// The leader's travel direction, smoothed so a navmesh corner turns the
    /// formation over a second instead of snapping every slot at once.
    /// </summary>
    public static Vector2 SmoothedForward(Group group, GameLiving leader)
    {
        Point2D ahead = leader.GetPointFromHeading(leader.Heading, 100);
        Vector2 raw = new(ahead.X - leader.X, ahead.Y - leader.Y);
        if (raw.LengthSquared() < 1) raw = new Vector2(0, 1);
        raw = Vector2.Normalize(raw);
        if (group == null) return raw;
        GroupHeading heading = Headings.GetOrCreateValue(group);
        lock (heading)
        {
            if (leader.IsMoving) heading.LastMovingTick = GameLoop.GameLoopTime;
            heading.Forward = heading.Forward.LengthSquared() < 0.01f
                ? raw
                : Vector2.Normalize(Vector2.Lerp(heading.Forward, raw, 0.35f));
            return heading.Forward;
        }
    }

    /// <summary>The group still counts as travelling shortly after the leader paused.</summary>
    public static bool RecentlyMoving(Group group, GameLiving leader)
    {
        if (leader?.IsMoving == true) return true;
        if (group == null || !Headings.TryGetValue(group, out GroupHeading heading)) return false;
        lock (heading)
            return GameLoop.GameLoopTime - heading.LastMovingTick < TravelShapeGraceMilliseconds;
    }

    /// <summary>A follower of a moving group leader (used for faster AI cadence and buff deferral).</summary>
    public static bool GroupTraveling(GameBot bot) =>
        bot?.IsAutonomousWorldBot == true && !bot.IsPlayerLedGroup &&
        bot.Group?.LivingLeader is GameBot leader && leader.IsAlive && leader.IsMoving &&
        leader.CurrentRegionID == bot.CurrentRegionID;

    /// <summary>Leader speed plus catch-up when behind, with a stable personal ±4 % stride.</summary>
    public static short FollowSpeed(long botKey, short leaderSpeed, short ownMax, double distanceToSlot)
    {
        double catchUp = Math.Clamp((distanceToSlot - 60) / 600d, 0, 0.3);
        double stride = 0.96 + (unchecked((ulong)botKey) % 9) / 100d;
        double speed = Math.Max(1, (int)leaderSpeed) * (catchUp > 0 ? 1 + catchUp : stride);
        return (short)Math.Clamp(Math.Round(speed), 1, Math.Max(1, ownMax * 1.3));
    }

    /// <summary>Re-steer only when the slot moved noticeably, the bot stopped, or its speed must change.</summary>
    public static bool ShouldResteer(GameBot bot, Vector3 target, short speed, long now)
    {
        Order order = Orders.GetOrCreateValue(bot);
        lock (order)
        {
            bool fresh = order.IssuedTick == 0 || !bot.IsMoving ||
                         Vector3.Distance(order.Target, target) > ResteerDistance ||
                         Math.Abs(order.Speed - speed) > Math.Max(8, order.Speed / 10) && now - order.IssuedTick > 600;
            if (!fresh) return false;
            order.Target = target;
            order.Speed = speed;
            order.IssuedTick = now;
            return true;
        }
    }

    /// <summary>
    /// Walk with a moving leader: aim at the slot around its predicted spot,
    /// never hard-stop while it walks, match its pace.
    /// </summary>
    public static void FollowMovingLeader(GameBot bot, GameLiving leader, Vector3 slot)
    {
        float distance = Vector3.Distance(new Vector3(bot.X, bot.Y, bot.Z), slot);
        long key = bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID;
        short speed = FollowSpeed(key, Math.Max(leader.CurrentSpeed, (short)1), bot.MaxSpeed, distance);
        // An RvR member under a speed song never runs past its leader.
        if (AutonomousRvrDoctrineRuntime.Applies(bot))
            speed = AutonomousRvrSpeed.CapFollowerSpeed(speed, leader.CurrentSpeed, IsAhead(bot, leader));
        if (ShouldResteer(bot, slot, speed, GameLoop.GameLoopTime))
            bot.PathTo(slot, speed);
    }

    /// <summary>The bot stands in front of the leader along the leader's heading.</summary>
    private static bool IsAhead(GameLiving bot, GameLiving leader)
    {
        Point2D ahead = leader.GetPointFromHeading(leader.Heading, 100);
        Vector2 forward = new(ahead.X - leader.X, ahead.Y - leader.Y);
        Vector2 offset = new(bot.X - leader.X, bot.Y - leader.Y);
        return Vector2.Dot(forward, offset) > 0;
    }
}
