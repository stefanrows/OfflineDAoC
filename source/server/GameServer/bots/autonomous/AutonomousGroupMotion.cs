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
/// A slot that cannot be reached (off the mesh, behind a wall) or lies over a
/// zone border sends the follower after the leader's own track instead of
/// leaving it standing until the slot moves somewhere reachable.
/// The steering itself runs in the movement tick
/// (<see cref="AutonomousGroupFormationFollow"/>); this class holds the shared rules.
/// </summary>
public static class AutonomousGroupMotion
{
    private sealed class Order
    {
        public Vector3 Target;
        public long IssuedTick;
        public short Speed;
    }

    /// <summary>What a follower aims at while its leader walks.</summary>
    public enum FollowAim
    {
        /// <summary>The formation slot (the normal case).</summary>
        Slot,
        /// <summary>A point just behind the leader on its own track.</summary>
        Trail,
        /// <summary>The leader itself (the trail point lies over a zone border, the leader does not).</summary>
        Leader,
        /// <summary>The leader already crossed a zone border: the controller routes over it (validated zone itinerary).</summary>
        CrossZone,
    }

    private sealed class GroupHeading
    {
        public Vector2 Forward;
        public long LastMovingTick;
    }

    private static readonly ConditionalWeakTable<GameBot, Order> Orders = new();
    private static readonly ConditionalWeakTable<Group, GroupHeading> Headings = new();

    /// <summary>How far ahead of the leader the AI turn places the formation centre, in seconds of its travel.</summary>
    public const double LookaheadSeconds = 0.8;
    public const float ResteerDistance = 48;
    public const int TravelShapeGraceMilliseconds = 2_000;
    /// <summary>A follower whose slot cannot be reached walks this far behind the leader instead...</summary>
    public const int TrailLeaderDistance = 64;
    /// <summary>...for this long, then tries its slot again.</summary>
    public const long TrailLeaderMilliseconds = 3_000;

    /// <summary>The leader's position a moment ahead along its travel direction.</summary>
    public static Vector3 PredictLeader(GameLiving leader) => PredictLeader(leader, LookaheadSeconds);

    /// <summary>The leader's position <paramref name="seconds"/> ahead along its heading (no allocation).</summary>
    public static Vector3 PredictLeader(GameLiving leader, double seconds)
    {
        Vector3 here = new(leader.X, leader.Y, leader.Z);
        if (!leader.IsMoving || leader.CurrentSpeed <= 0 || seconds <= 0)
            return here;
        Vector2 ahead = HeadingForward(leader.Heading) * (float)(leader.CurrentSpeed * seconds);
        return new Vector3(here.X + ahead.X, here.Y + ahead.Y, here.Z);
    }

    /// <summary>Unit vector of a DAoC heading (0 = south/+Y, as <see cref="Point2D.GetPointFromHeading"/>).</summary>
    public static Vector2 HeadingForward(ushort heading)
    {
        double angle = heading * Point2D.HEADING_TO_RADIAN;
        return new Vector2((float)-Math.Sin(angle), (float)Math.Cos(angle));
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

    /// <summary>
    /// The pace a follower matches. Followers of a moving, unstealthed
    /// autonomous leader run at the leader's full run speed (its MaxSpeed, so
    /// speed songs and snares still count), not at the speed of its latest
    /// order: a leader that slowed or paused for a moment no longer makes the
    /// whole group crawl. Stealthed groups, companions and player-led groups
    /// keep matching the leader's current speed.
    /// </summary>
    public static short LeaderPace(bool autonomousGroup, bool leaderMoving, bool stealthed,
        short leaderCurrentSpeed, short leaderMaxSpeed) =>
        autonomousGroup && leaderMoving && !stealthed && leaderMaxSpeed > 0
            ? leaderMaxSpeed
            : Math.Max(leaderCurrentSpeed, (short)1);

    /// <summary>
    /// A follower ahead of its leader never outruns the leader's pace, except
    /// a little (10 %) while it still has to reach a front slot ahead of it.
    /// </summary>
    public static short CapAheadOfLeader(short speed, short pace, bool aheadOfLeader, bool slotStillAhead)
    {
        if (!aheadOfLeader || pace <= 0)
            return speed;
        int cap = slotStillAhead ? (int)Math.Round(pace * 1.1) : pace;
        return (short)Math.Min(speed, cap);
    }

    /// <summary>
    /// The one speed rule for a follower walking with its leader: the leader's pace
    /// (<see cref="LeaderPace"/>), personal stride and catch-up (<see cref="FollowSpeed"/>), never
    /// overrunning an unstealthed autonomous leader (<see cref="CapAheadOfLeader"/>), and an RvR member
    /// under a speed song never running past its leader (<see cref="AutonomousRvrSpeed.CapFollowerSpeed"/>).
    /// Stealthed groups match the leader's current (stealth) speed.
    /// </summary>
    public static short FollowerSpeed(long botKey, bool autonomousGroup, bool leaderMoving, bool stealthed,
        short leaderCurrentSpeed, short leaderMaxSpeed, short ownMax, float distanceToAim,
        bool aheadOfLeader, bool aimStillAhead, bool rvrDoctrine)
    {
        short pace = LeaderPace(autonomousGroup, leaderMoving, stealthed, leaderCurrentSpeed, leaderMaxSpeed);
        short speed = FollowSpeed(botKey, pace, ownMax, distanceToAim);
        // Run with the leader, but do not overrun it beyond what the formation needs.
        if (autonomousGroup && leaderMoving && !stealthed)
            return CapAheadOfLeader(speed, pace, aheadOfLeader, aimStillAhead);
        // An RvR member under a speed song never runs past its leader.
        return rvrDoctrine ? AutonomousRvrSpeed.CapFollowerSpeed(speed, leaderCurrentSpeed, aheadOfLeader) : speed;
    }

    /// <summary><see cref="FollowerSpeed(long, bool, bool, bool, short, short, short, float, bool, bool, bool)"/> read off the live follower and leader.</summary>
    public static short FollowerSpeed(GameBot bot, GameLiving leader, Vector3 aim, float distanceToAim)
    {
        long key = bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID;
        bool autonomousGroup = bot.IsAutonomousWorldBot && !bot.IsPlayerLedGroup &&
            leader is GameBot { IsAutonomousWorldBot: true, IsPlayerLedGroup: false };
        bool stealthed = bot.IsStealthed || leader.IsStealthed;
        Vector2 forward = HeadingForward(leader.Heading);
        bool ahead = Vector2.Dot(forward, new(bot.X - leader.X, bot.Y - leader.Y)) > 0;
        bool aimAhead = distanceToAim > 60 && Vector2.Dot(forward, new(aim.X - bot.X, aim.Y - bot.Y)) > 0;
        bool fullPace = autonomousGroup && leader.IsMoving && !stealthed;
        return FollowerSpeed(key, autonomousGroup, leader.IsMoving, stealthed, leader.CurrentSpeed, leader.MaxSpeed,
            bot.MaxSpeed, distanceToAim, ahead, aimAhead, !fullPace && AutonomousRvrDoctrineRuntime.Applies(bot));
    }

    /// <summary>
    /// Hysteresis of a moving order: a new order only for a first order, a follower that stopped, an aim that
    /// moved more than <see cref="ResteerDistance"/>, or a speed change of over 10 % (at least 8) after 600 ms.
    /// </summary>
    public static bool NeedsResteer(bool hasOrder, bool moving, float aimShift, short lastSpeed, short speed,
        long sinceIssuedMilliseconds, float resteerDistance = ResteerDistance) =>
        !hasOrder || !moving || aimShift > resteerDistance ||
        Math.Abs(lastSpeed - speed) > Math.Max(8, lastSpeed / 10) && sinceIssuedMilliseconds > 600;

    /// <summary>Re-steer only when the slot moved noticeably, the bot stopped, or its speed must change.</summary>
    public static bool ShouldResteer(GameBot bot, Vector3 target, short speed, long now)
    {
        Order order = Orders.GetOrCreateValue(bot);
        lock (order)
        {
            if (!NeedsResteer(order.IssuedTick != 0, bot.IsMoving, Vector3.Distance(order.Target, target),
                    order.Speed, speed, now - order.IssuedTick))
                return false;
            order.Target = target;
            order.Speed = speed;
            order.IssuedTick = now;
            return true;
        }
    }

    /// <summary>The trail point: a little behind the leader along its heading, on the track it just walked.</summary>
    public static Vector3 TrailPoint(Vector3 leaderPosition, Vector2 forward)
    {
        if (forward.LengthSquared() < 1e-6f) return leaderPosition;
        Vector2 back = Vector2.Normalize(forward) * TrailLeaderDistance;
        return new Vector3(leaderPosition.X - back.X, leaderPosition.Y - back.Y, leaderPosition.Z);
    }

    /// <summary>A failed slot order starts (and renews) a <see cref="TrailLeaderMilliseconds"/> spell of trailing the leader.</summary>
    public static long NextTrailUntil(long trailUntil, bool slotUnreachable, long now) =>
        slotUnreachable ? now + TrailLeaderMilliseconds : trailUntil;

    public static bool TrailActive(long trailUntil, long now) => now < trailUntil;

    /// <summary>
    /// Slot, trail point, leader or cross-zone route. The slot is kept unless a trail spell runs or the slot lies
    /// in another zone than the follower (paths are planned inside one zone). Then the follower aims behind the
    /// leader; if that point is over the border too, it aims at the leader when the leader is on its side, and
    /// otherwise the controller routes it over the border behind the leader.
    /// </summary>
    public static FollowAim ChooseAim(bool trailActive, bool slotInOtherZone, bool trailPointInOtherZone,
        bool leaderInOtherZone)
    {
        if (!trailActive && !slotInOtherZone) return FollowAim.Slot;
        if (!trailPointInOtherZone) return FollowAim.Trail;
        return leaderInOtherZone ? FollowAim.CrossZone : FollowAim.Leader;
    }

    /// <summary>
    /// The follower's last slot order failed or came back partial: it stands away from its slot and Detour found
    /// no (or only a partial) path toward it. Only an order aimed at the slot counts (not a trail or border point).
    /// </summary>
    public static bool SlotUnreachable(bool lastOrderWasSlot, bool standing, PathfindingStatus status,
        float distanceSquaredToSlot, float minimumDistance) =>
        lastOrderWasSlot && standing && status is PathfindingStatus.PartialPathFound or PathfindingStatus.NoPathFound &&
        distanceSquaredToSlot > minimumDistance * minimumDistance;

    /// <summary>The point lies in another zone than the bot (paths are planned inside one zone).</summary>
    public static bool InOtherZone(GameBot bot, Vector3 point)
    {
        Zone here = bot.CurrentZone;
        return here != null && bot.CurrentRegion?.GetZone((int)point.X, (int)point.Y) != here;
    }
}
