using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.Logging;

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
/// </summary>
public static class AutonomousGroupMotion
{
    private sealed class Order
    {
        public Vector3 Target;
        public long IssuedTick;
        public short Speed;
        /// <summary>The last order aimed at the formation slot (not a trail or border point).</summary>
        public bool WasSlot;
        public long TrailUntil;
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

    /// <summary>How far ahead of the leader followers aim, in seconds of its travel.</summary>
    public const double LookaheadSeconds = 0.8;
    public const float ResteerDistance = 48;
    public const int TravelShapeGraceMilliseconds = 2_000;
    /// <summary>A follower whose slot cannot be reached walks this far behind the leader instead...</summary>
    public const int TrailLeaderDistance = 64;
    /// <summary>...for this long, then tries its slot again.</summary>
    public const long TrailLeaderMilliseconds = 3_000;
    public const long ReportIntervalMilliseconds = 60_000;

    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousGroupMotion));

    // Measurement for the owner: follow checks while the leader walks, those that found the follower standing,
    // checks spent trailing the leader, trail spells started, checks whose slot lay over a zone border, leaders followed over a border by route.
    private static long _checks, _standing, _trail, _trailStarts, _border, _crossZone, _nextReportTick;

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
    /// never hard-stop while it walks, match its pace. False when the leader is
    /// over a zone border: the caller routes to <paramref name="crossZoneTarget"/>
    /// (behind the leader) through its validated zone itinerary.
    /// </summary>
    public static bool FollowMovingLeader(GameBot bot, GameLiving leader, Vector3 slot, out Vector3 crossZoneTarget)
    {
        long now = GameLoop.GameLoopTime;
        Interlocked.Increment(ref _checks);
        if (!bot.IsMoving) Interlocked.Increment(ref _standing);
        Report(now);

        // An unreachable slot, or one over a zone border, sends the follower after the leader's own track.
        Order state = Orders.GetOrCreateValue(bot);
        long trailUntil;
        lock (state)
        {
            bool failed = SlotOrderFailed(bot, state);
            long before = state.TrailUntil;
            if (failed && !TrailActive(before, now)) Interlocked.Increment(ref _trailStarts);
            trailUntil = state.TrailUntil = NextTrailUntil(before, failed, now);
        }
        Vector3 leaderPosition = new(leader.X, leader.Y, leader.Z);
        Vector3 trailPoint = TrailPoint(leaderPosition, Forward(leader));
        bool slotOtherZone = InOtherZone(bot, slot);
        FollowAim aimKind = ChooseAim(TrailActive(trailUntil, now), slotOtherZone, InOtherZone(bot, trailPoint),
            InOtherZone(bot, leaderPosition));
        if (slotOtherZone) Interlocked.Increment(ref _border);
        crossZoneTarget = trailPoint;
        if (aimKind == FollowAim.CrossZone)
        {
            // A straight walk over the border could cut through a wall or gate; only proven crossings are used.
            Interlocked.Increment(ref _crossZone);
            MarkOrder(bot, false);
            return false;
        }
        Vector3 aim = aimKind switch
        {
            FollowAim.Trail => trailPoint,
            FollowAim.Leader => leaderPosition,
            _ => slot,
        };
        if (aimKind != FollowAim.Slot) Interlocked.Increment(ref _trail);

        float distance = Vector3.Distance(new Vector3(bot.X, bot.Y, bot.Z), aim);
        long key = bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID;
        bool autonomousGroup = bot.IsAutonomousWorldBot && !bot.IsPlayerLedGroup &&
            leader is GameBot { IsAutonomousWorldBot: true, IsPlayerLedGroup: false };
        bool fullPace = autonomousGroup && leader.IsMoving && !bot.IsStealthed && !leader.IsStealthed;
        short pace = LeaderPace(autonomousGroup, leader.IsMoving, bot.IsStealthed || leader.IsStealthed,
            leader.CurrentSpeed, leader.MaxSpeed);
        short speed = FollowSpeed(key, pace, bot.MaxSpeed, distance);
        if (fullPace)
        {
            // Run with the leader, but do not overrun it beyond what the formation needs.
            Vector2 forward = Forward(leader);
            bool slotAhead = distance > 60 && Vector2.Dot(forward, new(aim.X - bot.X, aim.Y - bot.Y)) > 0;
            speed = CapAheadOfLeader(speed, pace, IsAhead(bot, leader), slotAhead);
        }
        // An RvR member under a speed song never runs past its leader.
        else if (AutonomousRvrDoctrineRuntime.Applies(bot))
            speed = AutonomousRvrSpeed.CapFollowerSpeed(speed, leader.CurrentSpeed, IsAhead(bot, leader));
        if (ShouldResteer(bot, aim, speed, now))
        {
            MarkOrder(bot, aimKind == FollowAim.Slot);
            bot.PathTo(aim, speed);
        }
        return true;
    }

    /// <summary>The trail point: a little behind the leader along its heading, on the track it just walked.</summary>
    public static Vector3 TrailPoint(Vector3 leaderPosition, Vector2 forward)
    {
        if (forward.LengthSquared() < 1) return leaderPosition;
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
    /// The follower's last slot order failed or came back partial: Detour found no node toward it. Read without
    /// consuming, so the controller's IssuePath still sees a failure meant for its own route; a failure for another
    /// destination (a route order, a trail point) is not the slot's.
    /// </summary>
    private static bool SlotOrderFailed(GameBot bot, Order order)
    {
        lock (order)
        {
            if (!order.WasSlot || order.IssuedTick == 0) return false;
            return bot.PeekAutonomousPathFailure(out PathfindingStatus status, out Vector3 failed) &&
                   status is PathfindingStatus.PartialPathFound or PathfindingStatus.NoPathFound &&
                   Vector2.Distance(new(failed.X, failed.Y), new(order.Target.X, order.Target.Y)) <= ResteerDistance * 2;
        }
    }

    private static void MarkOrder(GameBot bot, bool slot)
    {
        Order order = Orders.GetOrCreateValue(bot);
        lock (order)
            order.WasSlot = slot;
    }

    private static bool InOtherZone(GameBot bot, Vector3 point)
    {
        Zone here = bot.CurrentZone;
        return here != null && bot.CurrentRegion?.GetZone((int)point.X, (int)point.Y) != here;
    }

    private static void Report(long now)
    {
        long next = Interlocked.Read(ref _nextReportTick);
        if (next == 0)
        {
            // First call only arms the window so the line covers a full minute.
            Interlocked.CompareExchange(ref _nextReportTick, now + ReportIntervalMilliseconds, 0);
            return;
        }
        if (now < next || Interlocked.CompareExchange(ref _nextReportTick, now + ReportIntervalMilliseconds, next) != next)
            return;
        long checks = Interlocked.Exchange(ref _checks, 0), standing = Interlocked.Exchange(ref _standing, 0),
             trail = Interlocked.Exchange(ref _trail, 0), starts = Interlocked.Exchange(ref _trailStarts, 0),
             border = Interlocked.Exchange(ref _border, 0), crossZone = Interlocked.Exchange(ref _crossZone, 0);
        if (checks == 0) return;
        Log.Info($"AUTONOMOUS_FORMATION_FOLLOW window_s={ReportIntervalMilliseconds / 1000} checks={checks} " +
                 $"standing={standing} standingPct={100.0 * standing / checks:0.0} trail={trail} trailStarts={starts} " +
                 $"border={border} crossZone={crossZone}");
    }

    /// <summary>The bot stands in front of the leader along the leader's heading.</summary>
    private static bool IsAhead(GameLiving bot, GameLiving leader)
    {
        Vector2 offset = new(bot.X - leader.X, bot.Y - leader.Y);
        return Vector2.Dot(Forward(leader), offset) > 0;
    }

    private static Vector2 Forward(GameLiving leader)
    {
        Point2D ahead = leader.GetPointFromHeading(leader.Heading, 100);
        return new(ahead.X - leader.X, ahead.Y - leader.Y);
    }
}
