namespace DOL.GS.DailyQuest
{
    /// <summary>
    /// Shared rules of Pazz's six Thidranki dailies (ThidKeepCapture*, ThidKillQuest*). Region 252 is the open BG for
    /// levels 1-50 (spec 2026-10-11 §8), so the dailies take every level the battleground takes. The coin reward keeps
    /// its old formula, which already scales with the level: level x 2 gold (the kill dailies add 32 silver); the
    /// experience reward stays a share of the current level (half a level for a capture, a third for the kills).
    /// </summary>
    public static class ThidrankiDailies
    {
        public const int MinimumLevel = OpenBattlegroundPolicy.MinLevel;
        public const int MaximumLevel = OpenBattlegroundPolicy.MaxLevel;

        public static bool AcceptsLevel(int level) => level >= MinimumLevel && level <= MaximumLevel;

        /// <summary>The fixed part of a capture daily's coin reward, in copper; the quest adds up to 50 random copper.</summary>
        public static long CoinReward(int level) => Money.GetMoney(0, 0, level * 2, 0, 0);

        /// <summary>The fixed part of a kill daily's coin reward, in copper: the capture reward plus 32 silver.</summary>
        public static long KillCoinReward(int level) => Money.GetMoney(0, 0, level * 2, 32, 0);
    }
}
