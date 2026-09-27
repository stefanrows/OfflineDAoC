using System;
namespace DOL.GS
{
    /// <summary>One timing/stance rule for real players and NPC-backed playerbots.
    /// Native property calculators still apply level, buffs, debuffs and server modifiers.</summary>
    public static class ClassicRestRegeneration
    {
        public static int HealthAndPowerInterval(bool sitting, bool inCombat)
        {
            return (6 - (sitting ? 3 : 0) + (inCombat ? 8 : 0) - (sitting && inCombat ? 1 : 0)) * 1000;
        }

        // Offline balance: a real player sitting out of combat recovers like a
        // resting companion (one tick per second, at least 10% of the pool).
        public const int FastRestIntervalMilliseconds = 1000;

        public static bool IsPlayerFastRest(bool sitting, bool inCombat) => sitting && !inCombat;

        public static int PlayerHealthAndPowerInterval(bool sitting, bool inCombat) =>
            IsPlayerFastRest(sitting, inCombat) ? FastRestIntervalMilliseconds : HealthAndPowerInterval(sitting, inCombat);

        public static int PlayerRestAmount(int nativeAmount, int maximum, bool sitting, bool inCombat) =>
            nativeAmount > 0 && IsPlayerFastRest(sitting, inCombat)
                ? Math.Max(nativeAmount, Math.Max(1, (int)Math.Ceiling(maximum * 0.10)))
                : nativeAmount;

        public static int BaseEndurancePerTick(bool sitting, bool inCombat, bool moving)
        {
            return inCombat || moving ? 0 : sitting ? 4 : 1;
        }
    }
}
