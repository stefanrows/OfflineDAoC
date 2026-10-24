using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Task 138: an autonomous party member keeps its formation slot from the movement tick, the way companions and
/// pets follow, instead of re-steering only on its AI turn. A follower's AI turn comes every 2.6-4.2 s at a full
/// population, so a follower that reached the point it was sent to stood until its next turn: stop-go. Now the AI
/// turn only (re)starts a native follow of the leader (<see cref="Ensure"/>) with the slot offset, and
/// NpcMovementComponent.FollowTick steers to the slot around the leader every <see cref="FollowCheckMilliseconds"/>
/// with the rules of <see cref="AutonomousGroupMotion"/> (pace, catch-up, trail past an unreachable slot, no
/// straight walk over a zone border). Any other order (a route, a fight, a hold, a stable ride) ends it, and an AI
/// turn that does not renew it ends it as well (<see cref="EndTurn"/>). Companions, pets, player-led groups and
/// ordinary NPCs never get a state here and keep the ordinary follow unchanged.
/// </summary>
public static class AutonomousGroupFormationFollow
{
    public const int FollowCheckMilliseconds = 400;
    /// <summary>
    /// The tick places the slot centre this far ahead of a walking leader. The AI turn used 0.8 s because it re-steered
    /// only every few seconds; a follower steered every 400 ms reached a point 0.8 s ahead before the next check about
    /// as often as not and stood on it (upstream's run 24 at 1.0 s: almost all standing checks were followers on the
    /// end of their last walk). 1.5 s keeps the aim well past one check of walking while the follower still matches
    /// the leader's pace, so it walks continuously beside it rather than overtaking it.
    /// </summary>
    public const double LookaheadSeconds = 1.5;
    /// <summary>Backstop: without a refresh from an AI turn for this long the follow is no longer a formation follow.</summary>
    public const long StateLifetimeMilliseconds = 15_000;
    public const int MaximumFollowDistance = 3_000;
    /// <summary>A follower of a standing leader settles within this of its slot instead of jittering onto it.</summary>
    public const int ArrivedTolerance = 24;
    /// <summary>While the leader walks the aim is 1.5 s ahead; only an actual arrival on it stops the follower.</summary>
    public const int WalkingArrivedTolerance = 4;
    /// <summary>A standing follower this far from its slot with a failed or partial path has an unreachable slot.</summary>
    public const float UnreachableSlotDistance = 120;
    /// <summary>A standing follower within this of the slot beside its leader counts as standing at its slot.</summary>
    public const float AtSlotDistance = 120;
    public const long ReportIntervalMilliseconds = 60_000;
    /// <summary>
    /// The tick re-issues a path only when its aim moved this far. A walking leader moves the 1.5 s aim about 76 units
    /// per check; at the AI turn's 48 every check would plan a fresh Detour path (about 2.5 per second per follower).
    /// At 96 a marching follower replans about every second check and still walks toward a point well ahead of it.
    /// </summary>
    public const float TickResteerDistance = 96;

    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousGroupFormationFollow));

    /// <summary>What the movement tick does with a follow.</summary>
    public enum Gate
    {
        /// <summary>Not a formation follow (companions, pets, ordinary NPCs, any other follow target): the ordinary follow runs.</summary>
        None,
        /// <summary>Steer to the slot.</summary>
        Steer,
        /// <summary>Casting out of combat: stand still and keep the follow; walk on when the cast ends.</summary>
        Pause,
        /// <summary>The formation follow is over (a fight, a ride, a rest, a stale or foreign state): release it.</summary>
        End,
    }

    public sealed class State
    {
        // Written by the follower's AI turn.
        public GameBot Leader;
        public Vector3 Offset;
        public bool Tight;
        public long RefreshedTick;
        // Owned by the movement tick.
        public Vector3 LastAim;
        public short LastSpeed;
        public long IssuedTick;
        public bool LastOrderWasSlot;
        public long TrailUntil;
        public bool CrossZoneNudged;
    }

    private static readonly ConditionalWeakTable<GameBot, State> States = new();

    // AUTONOMOUS_FORMATION_FOLLOW (task 137 fields, now per tick check while the leader walks): checks, standing,
    // checks spent trailing the leader, trail spells started, checks whose slot lay over a zone border, checks that
    // left the border to the controller. Task 138 adds why a follower stood and how often the AI turn routed a
    // follower over a border.
    private static long _checks, _standing, _trail, _trailStarts, _border, _crossZone, _nextReportTick;
    private static long _standCasting, _standCombat, _standNoPath, _standAtSlot, _standPartial, _routedCrossZone;

    /// <summary>Only an autonomous world bot of an autonomous (not player-led) party, never a helper, gets a formation follow.</summary>
    public static bool IsEligible(bool autonomousWorldBot, bool playerLedGroup, bool temporaryHelper) =>
        autonomousWorldBot && !playerLedGroup && !temporaryHelper;

    /// <summary>
    /// The movement tick's decision for an existing state whose leader is the follow target. A fight (an attack,
    /// ranged included; not the 10 s combat timer alone) belongs to the bot brain; a stable ride, rest or sitting belongs to the AI turn.
    /// A cast out of combat pauses the follow instead of ending it.
    /// </summary>
    public static Gate Decide(bool eligible, bool sameGroup, bool fresh, bool held, bool fighting, bool casting) =>
        !eligible || !sameGroup || !fresh || held || fighting ? Gate.End
        : casting ? Gate.Pause
        : Gate.Steer;

    /// <summary>The state is renewed by an AI turn within <see cref="StateLifetimeMilliseconds"/>.</summary>
    public static bool IsFresh(long refreshedTick, long now) =>
        refreshedTick > 0 && now - refreshedTick <= StateLifetimeMilliseconds;

    /// <summary>For NpcMovementComponent.FollowTick: what to do with this follow (one table lookup, no allocation).</summary>
    public static Gate Classify(GameNPC owner, GameLiving target, bool casting, out State state)
    {
        state = null;
        if (owner is not GameBot { IsAutonomousWorldBot: true } bot || target == null ||
            !States.TryGetValue(bot, out State found) || found.Leader != target)
            return Gate.None;
        state = found;
        return Decide(IsEligible(true, bot.IsPlayerLedGroup, bot.IsTemporaryGroupHelper),
            bot.Group != null && bot.Group == found.Leader.Group, IsFresh(found.RefreshedTick, GameLoop.GameLoopTime),
            bot.IsOnStableMasterRoute || bot.IsRecoveryResting || bot.IsSitting,
            // The 10 s combat timer is not a fight: a recovering follower whose turn renews the follow keeps it;
            // a real fight attacks or follows its target (another follow target, Gate.None).
            bot.IsAttacking, casting);
    }

    /// <summary>
    /// Called from the follower's AI turn: keep following <paramref name="leader"/> at <paramref name="slot"/>
    /// (computed around <paramref name="center"/>). Starts the native follow only when it is not running yet.
    /// </summary>
    public static void Ensure(GameBot follower, GameBot leader, Vector3 center, Vector3 slot, bool tight)
    {
        if (follower == null || leader == null || follower == leader ||
            !IsEligible(follower.IsAutonomousWorldBot, follower.IsPlayerLedGroup, follower.IsTemporaryGroupHelper))
            return;
        State state = States.GetOrCreateValue(follower);
        if (state.Leader != leader)
        {
            state.IssuedTick = 0;
            state.TrailUntil = 0;
            state.LastOrderWasSlot = false;
            state.CrossZoneNudged = false;
        }
        state.Leader = leader;
        state.Offset = slot - center;
        state.Tight = tight;
        state.RefreshedTick = GameLoop.GameLoopTime;
        if (follower.movementComponent is NpcMovementComponent mover && mover.FollowTarget != leader)
            follower.Follow(leader, 0, MaximumFollowDistance);
    }

    /// <summary>The follower takes another order (a route, a fight, a hold): its formation follow ends.</summary>
    public static void Release(GameBot follower)
    {
        if (follower == null || !States.TryGetValue(follower, out State state))
            return;
        States.Remove(follower);
        if (follower.movementComponent is NpcMovementComponent mover && mover.FollowTarget != null &&
            mover.FollowTarget == state.Leader)
            mover.StopFollowing();
    }

    /// <summary>
    /// A public move order (PathTo/WalkTo) reached the movement component of a following bot: forget the state and
    /// tell the component whether the running follow was this formation follow (it then stops following).
    /// </summary>
    public static bool EndOnMoveOrder(GameNPC owner, GameLiving followTarget)
    {
        if (owner is not GameBot { IsAutonomousWorldBot: true } bot || !States.TryGetValue(bot, out State state))
            return false;
        States.Remove(bot);
        return followTarget != null && state.Leader == followTarget;
    }

    /// <summary>StopMoving on the bot: the follow stops anyway; drop the state with it.</summary>
    public static void Forget(GameNPC owner)
    {
        if (owner is GameBot { IsAutonomousWorldBot: true } bot && States.TryGetValue(bot, out _))
            States.Remove(bot);
    }

    /// <summary>
    /// End of the follower's AI turn: a turn that did not renew the formation follow chose something else (a hold,
    /// a route, a fight, a service), so the follow ends here for every such branch at once. A cast out of combat
    /// started on this turn keeps it: the movement tick pauses it and walks on when the cast ends.
    /// </summary>
    public static void EndTurn(GameBot bot, long turnTick)
    {
        if (bot?.IsAutonomousWorldBot != true || !States.TryGetValue(bot, out State state) ||
            state.RefreshedTick >= turnTick)
            return;
        if (KeepsThroughTurn(bot.InCombat || bot.IsAttacking,
                bot.IsCasting || bot.castingComponent?.HasPendingSkillRequests == true))
            return;
        Release(bot);
    }

    /// <summary>A turn that did not renew the follow keeps it only for a cast out of combat.</summary>
    public static bool KeepsThroughTurn(bool fighting, bool castingOrQueued) => !fighting && castingOrQueued;

    /// <summary>The slot centre: the leader, or its spot <see cref="LookaheadSeconds"/> ahead while it walks (not in tight places).</summary>
    public static Vector3 TickCenter(GameLiving leader, bool tight, bool leaderWalking) =>
        leaderWalking && !tight ? AutonomousGroupMotion.PredictLeader(leader, LookaheadSeconds) : new(leader.X, leader.Y, leader.Z);

    /// <summary>
    /// For the controller, before <see cref="Ensure"/>: the slot as the movement tick will place it and the leader
    /// are both across a zone border, so the AI turn routes the follower over it through the validated zone
    /// itinerary (to <paramref name="trailPoint"/>) instead of a formation follow.
    /// </summary>
    public static bool RoutesAcrossZone(GameBot bot, GameLiving leader, Vector3 center, Vector3 slot, bool tight,
        out Vector3 trailPoint)
    {
        long now = GameLoop.GameLoopTime;
        Vector3 leaderPosition = new(leader.X, leader.Y, leader.Z);
        trailPoint = AutonomousGroupMotion.TrailPoint(leaderPosition, AutonomousGroupMotion.HeadingForward(leader.Heading));
        bool walking = leader.IsMoving && leader.CurrentSpeed > 0;
        Vector3 tickSlot = TickCenter(leader, tight, walking) + (slot - center);
        bool trailActive = States.TryGetValue(bot, out State state) && state.Leader == leader &&
                           AutonomousGroupMotion.TrailActive(state.TrailUntil, now);
        bool slotOther = AutonomousGroupMotion.InOtherZone(bot, tickSlot);
        if (!trailActive && !slotOther)
            return false;
        bool cross = AutonomousGroupMotion.ChooseAim(trailActive, slotOther, AutonomousGroupMotion.InOtherZone(bot, trailPoint),
            AutonomousGroupMotion.InOtherZone(bot, leaderPosition)) == AutonomousGroupMotion.FollowAim.CrossZone;
        if (cross) Interlocked.Increment(ref _routedCrossZone);
        return cross;
    }

    /// <summary>
    /// For NpcMovementComponent.FollowTick while steering: what the follower aims at now. Keeps the trail spell
    /// (a slot order that failed or came back partial while the follower stands away from it) and counts the check.
    /// </summary>
    public static AutonomousGroupMotion.FollowAim PlanTick(GameBot bot, State state, Vector3 position, bool leaderWalking,
        bool standing, PathfindingStatus pathStatus, long now, out Vector3 aim)
    {
        GameBot leader = state.Leader;
        Vector3 leaderPosition = new(leader.X, leader.Y, leader.Z);
        Vector3 slot = TickCenter(leader, state.Tight, leaderWalking) + state.Offset;
        bool trailActive = false;
        if (leaderWalking)
        {
            bool unreachable = AutonomousGroupMotion.SlotUnreachable(state.LastOrderWasSlot, standing, pathStatus,
                Vector3.DistanceSquared(position, slot), UnreachableSlotDistance);
            if (unreachable && !AutonomousGroupMotion.TrailActive(state.TrailUntil, now))
                Interlocked.Increment(ref _trailStarts);
            state.TrailUntil = AutonomousGroupMotion.NextTrailUntil(state.TrailUntil, unreachable, now);
            trailActive = AutonomousGroupMotion.TrailActive(state.TrailUntil, now);
        }
        bool slotOther = AutonomousGroupMotion.InOtherZone(bot, slot);
        AutonomousGroupMotion.FollowAim kind = AutonomousGroupMotion.FollowAim.Slot;
        aim = slot;
        if (trailActive || slotOther)
        {
            Vector3 trailPoint = AutonomousGroupMotion.TrailPoint(leaderPosition, AutonomousGroupMotion.HeadingForward(leader.Heading));
            kind = AutonomousGroupMotion.ChooseAim(trailActive, slotOther, AutonomousGroupMotion.InOtherZone(bot, trailPoint),
                AutonomousGroupMotion.InOtherZone(bot, leaderPosition));
            aim = kind switch
            {
                AutonomousGroupMotion.FollowAim.Trail => trailPoint,
                AutonomousGroupMotion.FollowAim.Leader => leaderPosition,
                AutonomousGroupMotion.FollowAim.CrossZone => trailPoint,
                _ => slot,
            };
        }
        if (kind != AutonomousGroupMotion.FollowAim.CrossZone)
            state.CrossZoneNudged = false;
        if (!leaderWalking)
            return kind;

        Interlocked.Increment(ref _checks);
        if (standing)
        {
            Interlocked.Increment(ref _standing);
            if (pathStatus == PathfindingStatus.NoPathFound) Interlocked.Increment(ref _standNoPath);
            else if (Vector3.DistanceSquared(position, leaderPosition + state.Offset) <= AtSlotDistance * AtSlotDistance)
                Interlocked.Increment(ref _standAtSlot);
            else if (pathStatus == PathfindingStatus.PartialPathFound) Interlocked.Increment(ref _standPartial);
        }
        if (slotOther) Interlocked.Increment(ref _border);
        if (kind is AutonomousGroupMotion.FollowAim.Trail or AutonomousGroupMotion.FollowAim.Leader)
            Interlocked.Increment(ref _trail);
        if (kind == AutonomousGroupMotion.FollowAim.CrossZone)
            Interlocked.Increment(ref _crossZone);
        Report(now);
        return kind;
    }

    /// <summary>A paused (casting) or ending (fighting) check while the leader walks: counted as a standing reason.</summary>
    public static void NoteHeldCheck(GameBot leader, bool standing, bool casting)
    {
        if (leader == null || !leader.IsMoving || leader.CurrentSpeed <= 0)
            return;
        Interlocked.Increment(ref _checks);
        if (standing)
        {
            Interlocked.Increment(ref _standing);
            if (casting) Interlocked.Increment(ref _standCasting);
            else Interlocked.Increment(ref _standCombat);
        }
        Report(GameLoop.GameLoopTime);
    }

    /// <summary>
    /// The leader is over a zone border: the follower keeps its current movement and its AI turn is brought forward
    /// once, so the controller routes it over the border promptly (never an unvalidated straight walk).
    /// </summary>
    public static void NudgeController(GameBot bot, State state, long now)
    {
        if (state.CrossZoneNudged)
            return;
        state.CrossZoneNudged = true;
        if (bot.Brain is DOL.AI.ABrain brain && brain.NextThinkTick > now)
            brain.NextThinkTick = now;
    }

    /// <summary>Re-steer only when the aim moved noticeably, the follower stopped, or its speed must change.</summary>
    public static bool ShouldIssue(State state, bool moving, Vector3 aim, short speed, long now) =>
        AutonomousGroupMotion.NeedsResteer(state.IssuedTick != 0, moving, Vector3.Distance(state.LastAim, aim),
            state.LastSpeed, speed, now - state.IssuedTick, TickResteerDistance);

    public static void NoteIssued(State state, Vector3 aim, short speed, bool slot, long now)
    {
        state.LastAim = aim;
        state.LastSpeed = speed;
        state.IssuedTick = now;
        state.LastOrderWasSlot = slot;
    }

    /// <summary>Approach speed toward a standing leader's slot: the ordinary follow rule (slows over the last 40 units or so).</summary>
    public static short SettleSpeed(short maxSpeed, float distance) =>
        (short)Math.Max(20, Math.Min(maxSpeed, distance * 2.5f));

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
             border = Interlocked.Exchange(ref _border, 0), crossZone = Interlocked.Exchange(ref _crossZone, 0),
             casting = Interlocked.Exchange(ref _standCasting, 0), combat = Interlocked.Exchange(ref _standCombat, 0),
             noPath = Interlocked.Exchange(ref _standNoPath, 0), atSlot = Interlocked.Exchange(ref _standAtSlot, 0),
             partial = Interlocked.Exchange(ref _standPartial, 0), routed = Interlocked.Exchange(ref _routedCrossZone, 0);
        if (checks == 0) return;
        Log.Info($"AUTONOMOUS_FORMATION_FOLLOW window_s={ReportIntervalMilliseconds / 1000} checks={checks} " +
                 $"standing={standing} standingPct={100.0 * standing / checks:0.0} trail={trail} trailStarts={starts} " +
                 $"border={border} crossZone={crossZone} source=tick casting={casting} combat={combat} noPath={noPath} " +
                 $"atSlot={atSlot} partial={partial} crossZoneRoutes={routed}");
    }
}
