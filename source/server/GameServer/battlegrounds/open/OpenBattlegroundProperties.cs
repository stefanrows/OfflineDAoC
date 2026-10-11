using System;
using System.Collections.Concurrent;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Switches and setup of the open BG on native Thidranki 252 (spec 2026-10-11). The loader in
    /// ServerProperties finds these static fields in every assembly and adds their default rows; the
    /// initial values below are the same defaults for code that runs before (or without) the loader,
    /// such as unit tests. Read the clamped accessors, never the raw fields, at use.
    /// </summary>
    public static class OpenBattlegroundProperties
    {
        [ServerProperty("pvp", "open_bg_enabled", "Open the permanently open battleground (Thidranki 252, levels 1-50) through the porters' [open BG] line; it also needs the battleground campaign and zone 252's navmesh", false)]
        public static bool OPEN_BG_ENABLED = false;

        [ServerProperty("pvp", "open_bg_max_bots", "Most autonomous bots in the open BG at once, all realms together (8-120)", 72)]
        public static int OPEN_BG_MAX_BOTS = 72;

        [ServerProperty("pvp", "open_bg_groups8", "Full 8-man bot groups per realm in the open BG (0-4)", 1)]
        public static int OPEN_BG_GROUPS8 = 1;

        [ServerProperty("pvp", "open_bg_groups_small", "Small bot groups (2-4 members) per realm in the open BG (0-6)", 2)]
        public static int OPEN_BG_GROUPS_SMALL = 2;

        [ServerProperty("pvp", "open_bg_solos", "Solo bots (roamers and stealthers) per realm in the open BG (0-12)", 4)]
        public static int OPEN_BG_SOLOS = 4;

        [ServerProperty("pvp", "open_bg_cycle_min_minutes", "Shortest open BG cycle quiet - push - keep fight - retreat, in minutes (5-120, not above the maximum)", 15)]
        public static int OPEN_BG_CYCLE_MIN_MINUTES = 15;

        [ServerProperty("pvp", "open_bg_cycle_max_minutes", "Longest open BG cycle, in minutes (5-120, not below the minimum)", 45)]
        public static int OPEN_BG_CYCLE_MAX_MINUTES = 45;

        [ServerProperty("pvp", "open_bg_player_drift", "Open BG roamers and solos prefer field points within sight of a player; the keep cycle runs independently", true)]
        public static bool OPEN_BG_PLAYER_DRIFT = true;

        public const int MaxBotsMin = 8, MaxBotsMax = 120;
        public const int Groups8Max = 4, GroupsSmallMax = 6, SolosMax = 12;
        public const int CycleMinutesMin = 5, CycleMinutesMax = 120;

        private static readonly Logger Log = LoggerManager.Create(typeof(OpenBattlegroundProperties));
        private static readonly ConcurrentDictionary<string, int> Reported = new();

        public static int MaxBots => Clamped("open_bg_max_bots", OPEN_BG_MAX_BOTS, MaxBotsMin, MaxBotsMax);
        public static int Groups8 => Clamped("open_bg_groups8", OPEN_BG_GROUPS8, 0, Groups8Max);
        public static int GroupsSmall => Clamped("open_bg_groups_small", OPEN_BG_GROUPS_SMALL, 0, GroupsSmallMax);
        public static int Solos => Clamped("open_bg_solos", OPEN_BG_SOLOS, 0, SolosMax);
        public static int CycleMinMinutes => Clamped("open_bg_cycle_min_minutes", OPEN_BG_CYCLE_MIN_MINUTES, CycleMinutesMin, CycleMinutesMax);
        // The maximum never falls below the (clamped) minimum.
        public static int CycleMaxMinutes => Clamped("open_bg_cycle_max_minutes", OPEN_BG_CYCLE_MAX_MINUTES, CycleMinMinutes, CycleMinutesMax);

        /// <summary>The value inside [min, max]; an out-of-range value is logged once per key and value.</summary>
        public static int Clamped(string key, int value, int min, int max)
        {
            int clamped = Math.Clamp(value, min, Math.Max(min, max));
            if (clamped != value && (!Reported.TryGetValue(key, out int last) || last != value))
            {
                Reported[key] = value;
                Log.Warn($"OPEN_BG_SETUP key={key} value={value} clamped={clamped} by=range");
            }
            return clamped;
        }
    }
}
