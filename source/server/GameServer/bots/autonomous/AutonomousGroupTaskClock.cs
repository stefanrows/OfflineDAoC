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

        // Task 47 package C (advisor 48 cause e): an empty member expiry while the
        // clock waits for the camp made HasActiveRvrTenure false, so a member
        // that left the party (raid transfer, restart) ended its tour at once.
        // Members carry "now plus the untouched duration" instead; the publisher
        // refreshes it every 15 minutes, so it always lies at least 30 minutes ahead.
        public const long PausedExpiryRefreshMilliseconds = 15 * 60_000L;

        public DateTime MemberExpiresUtc(DateTime utcNow) =>
            IsPaused ? utcNow.AddMilliseconds(PausedRemainingMilliseconds) : ExpiresUtc;

        public static bool NeedsPausedExpiryRefresh(DateTime lastPublishedUtc, DateTime utcNow) =>
            lastPublishedUtc == default ||
            utcNow - lastPublishedUtc >= TimeSpan.FromMilliseconds(PausedExpiryRefreshMilliseconds);

        // Task 47 package C: at the 30-minute travel deadline a party fighting its
        // way through the last rooms of a dungeon is at its spot by 2003 standards.
        // Start the full task there instead of disbanding; a party still out on
        // the road keeps ending as before.
        public const int TravelDeadlineCampRadius = 2_500;
        public const int TravelDeadlineCohesionRadius = 1_500;
        // Only a party that fought or earned experience recently counts: a leader
        // looping on a path next to the camp still ends at the deadline.
        public const long TravelDeadlineProgressWindowMilliseconds = 10 * 60_000L;

        public static bool HasRecentFightProgress(long nowTick, long lastFightTick) =>
            lastFightTick > 0 && nowTick >= lastFightTick &&
            nowTick - lastFightTick <= TravelDeadlineProgressWindowMilliseconds;

        public static bool ShouldStartAtTravelDeadline(bool leaderInCampRegion, double leaderDistanceToCamp,
            int membersWithLeader, bool recentFightProgress) =>
            leaderInCampRegion && double.IsFinite(leaderDistanceToCamp) &&
            leaderDistanceToCamp <= TravelDeadlineCampRadius && membersWithLeader >= 2 && recentFightProgress;
    }
}
