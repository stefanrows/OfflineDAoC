namespace DOL.GS
{
    /// <summary>
    /// Buff timing for player-led companions: long buffs are topped off out of
    /// combat shortly before they expire; short buffs are never maintained out of
    /// combat, so casters spend their power on the fight instead of on upkeep.
    /// </summary>
    public static class BotBuffTimingPolicy
    {
        /// <summary>A buff lasting at least this long (or held by concentration) is a long buff.</summary>
        public const int LongBuffMilliseconds = 5 * 60 * 1000;

        /// <summary>Out of combat, a long buff with less than this left is refreshed.</summary>
        public const int RefreshWindowMilliseconds = 60 * 1000;

        public static bool IsLongBuff(int durationMilliseconds, bool concentration) =>
            concentration || durationMilliseconds >= LongBuffMilliseconds;

        /// <summary>May a companion keep this buff up out of combat at all?</summary>
        public static bool MaintainOutOfCombat(bool isLongBuff, bool isSpeed, bool traveling) =>
            isSpeed ? traveling : isLongBuff;

        /// <summary>A present long buff is refreshed only when it is about to run out.</summary>
        public static bool ExpiresSoon(long remainingMilliseconds, bool concentration) =>
            !concentration && remainingMilliseconds > 0 && remainingMilliseconds < RefreshWindowMilliseconds;
    }
}
