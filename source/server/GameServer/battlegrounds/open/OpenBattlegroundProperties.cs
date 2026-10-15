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

        [ServerProperty("pvp", "open_bg_units_albion", "Albion's open BG crew as 8-man/small/solos, e.g. 1/2/4 (empty: open_bg_groups8, open_bg_groups_small and open_bg_solos)", "")]
        public static string OPEN_BG_UNITS_ALBION = "";

        [ServerProperty("pvp", "open_bg_units_midgard", "Midgard's open BG crew as 8-man/small/solos, e.g. 1/2/4 (empty: open_bg_groups8, open_bg_groups_small and open_bg_solos)", "")]
        public static string OPEN_BG_UNITS_MIDGARD = "";

        [ServerProperty("pvp", "open_bg_units_hibernia", "Hibernia's open BG crew as 8-man/small/solos, e.g. 1/2/4 (empty: open_bg_groups8, open_bg_groups_small and open_bg_solos)", "")]
        public static string OPEN_BG_UNITS_HIBERNIA = "";

        [ServerProperty("pvp", "open_bg_mode", "Open BG friend rule: serverpvp (Camlann: friends are group, guild and battlegroup, crews mixed) or perrealm (classic DAoC: inside the open BG the same realm is a friend)", "serverpvp")]
        public static string OPEN_BG_MODE = "serverpvp";

        [ServerProperty("pvp", "open_bg_landing_peace", "Landing peace at the open BG landings: all (every realm) or realm (only the landing's own realm); empty = by mode (serverpvp: all, perrealm: realm)", "")]
        public static string OPEN_BG_LANDING_PEACE = "";

        [ServerProperty("pvp", "open_bg_presets", "Open BG crew archetypes as name:count list, e.g. std8:2,caster8:1,raid8:1,stealth4:1,small:2,solo:6; empty = the group counts above", "")]
        public static string OPEN_BG_PRESETS = "";

        public const int MaxBotsMin = 8, MaxBotsMax = 120;
        public const int Groups8Max = 4, GroupsSmallMax = 6, SolosMax = 12;
        public const int CycleMinutesMin = 5, CycleMinutesMax = 120;

        private static readonly Logger Log = LoggerManager.Create(typeof(OpenBattlegroundProperties));
        private static readonly ConcurrentDictionary<string, int> Reported = new();

        public static int MaxBots => Clamped("open_bg_max_bots", OPEN_BG_MAX_BOTS, MaxBotsMin, MaxBotsMax);
        public static int Groups8 => Clamped("open_bg_groups8", OPEN_BG_GROUPS8, 0, Groups8Max);
        public static int GroupsSmall => Clamped("open_bg_groups_small", OPEN_BG_GROUPS_SMALL, 0, GroupsSmallMax);
        public static int Solos => Clamped("open_bg_solos", OPEN_BG_SOLOS, 0, SolosMax);
        public static string Mode => OPEN_BG_MODE ?? string.Empty;
        public static string LandingPeace => OPEN_BG_LANDING_PEACE ?? string.Empty;
        public static string Presets => OPEN_BG_PRESETS ?? string.Empty;
        public static int CycleMinMinutes => Clamped("open_bg_cycle_min_minutes", OPEN_BG_CYCLE_MIN_MINUTES, CycleMinutesMin, CycleMinutesMax);
        // The maximum never falls below the (clamped) minimum.
        public static int CycleMaxMinutes => Clamped("open_bg_cycle_max_minutes", OPEN_BG_CYCLE_MAX_MINUTES, CycleMinMinutes, CycleMinutesMax);

        /// <summary>The server property row of a realm's own crew targets (open_bg_units_albion|midgard|hibernia); null for other realms.</summary>
        public static string UnitsRowKey(eRealm realm) => realm switch
        {
            eRealm.Albion => "open_bg_units_albion",
            eRealm.Midgard => "open_bg_units_midgard",
            eRealm.Hibernia => "open_bg_units_hibernia",
            _ => null,
        };

        /// <summary>The raw text of a realm's own crew targets ("" for none or another realm).</summary>
        public static string UnitsText(eRealm realm) => realm switch
        {
            eRealm.Albion => OPEN_BG_UNITS_ALBION ?? string.Empty,
            eRealm.Midgard => OPEN_BG_UNITS_MIDGARD ?? string.Empty,
            eRealm.Hibernia => OPEN_BG_UNITS_HIBERNIA ?? string.Empty,
            _ => string.Empty,
        };

        /// <summary>A realm's own targets, clamped like the globals; null when its row is empty or unreadable (the globals apply).</summary>
        public static OpenBgRealmTargets? UnitsFor(eRealm realm) =>
            OpenBgRealmTargets.TryParse(UnitsText(realm), out OpenBgRealmTargets targets) ? targets : null;

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
