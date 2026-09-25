using System;

namespace DOL.GS
{
    /// <summary>Travel has a bounded window; the task duration begins at the camp and recovery never pauses it.</summary>
    public sealed class AutonomousGroupTaskClock
    {
        public const long CampTravelTimeoutMilliseconds = 30 * 60_000L;
        public long DurationMilliseconds { get; }
        public long? DeadlineTick { get; private set; }
        public long? TravelDeadlineTick { get; private set; }
        public DateTime ExpiresUtc { get; private set; }
        public DateTime TravelExpiresUtc { get; private set; }
        public long PausedRemainingMilliseconds { get; private set; }
        public bool HasStarted { get; private set; }
        public bool IsPaused => !DeadlineTick.HasValue;

        public AutonomousGroupTaskClock(eAutonomousObjectiveKind kind, Random random = null)
        {
            random ??= Random.Shared;
            DurationMilliseconds = random.NextInt64(45 * 60_000L, 120 * 60_000L + 1);
            PausedRemainingMilliseconds = DurationMilliseconds;
        }

        public bool Start(long nowTick, DateTime utcNow)
        {
            if (DeadlineTick.HasValue)
                return false;
            HasStarted = true;
            TravelDeadlineTick = null;
            TravelExpiresUtc = default;
            DeadlineTick = nowTick + PausedRemainingMilliseconds;
            ExpiresUtc = utcNow.AddMilliseconds(PausedRemainingMilliseconds);
            return true;
        }

        public bool BeginTravel(long nowTick, DateTime utcNow)
        {
            if (HasStarted || TravelDeadlineTick.HasValue)
                return false;
            TravelDeadlineTick = nowTick + CampTravelTimeoutMilliseconds;
            TravelExpiresUtc = utcNow.AddMilliseconds(CampTravelTimeoutMilliseconds);
            return true;
        }

        public bool HasTravelTimedOut(long nowTick) =>
            !HasStarted && TravelDeadlineTick.HasValue && nowTick >= TravelDeadlineTick.Value;

        public long RemainingMilliseconds(long nowTick) => DeadlineTick.HasValue
            ? Math.Max(0, DeadlineTick.Value - nowTick) : PausedRemainingMilliseconds;

        public bool HasExpired(long nowTick) => DeadlineTick.HasValue && nowTick >= DeadlineTick.Value;
    }
}
