using System;

namespace DOL.GS
{
    /// <summary>Which friend rule the open BG plays by (owner decision 2026-10-15).</summary>
    public enum OpenBgMode { ServerPvp, PerRealm }

    /// <summary>Whose landing peace holds at a landing: only the landing's own realm, or every realm.</summary>
    public enum OpenBgLandingPeace { Realm, All }

    /// <summary>
    /// The one place that knows the open BG mode. Server PvP is the Camlann rule (friends are group, guild and
    /// battlegroup; crews mixed). Per realm is classic DAoC: inside region 252 the same realm is a friend. The rule is
    /// pure so the unit tests need no server; <see cref="Current"/> reads the property each time (no cache) so a live
    /// /bgsetup change applies on the next check.
    /// </summary>
    public static class OpenBattlegroundMode
    {
        /// <summary>Native Thidranki, the open BG's region.</summary>
        public const ushort OpenBgRegion = 252;

        /// <summary>
        /// The repeat-kill timer (RP_WORTH_SECONDS) is lifted for realm points and experience while the open BG is
        /// switched on and the victim died in its region. Loot drops and party gear keep the timer.
        /// </summary>
        public static bool LiftsRepeatKillTimer(GameLiving victim) =>
            victim != null && LiftsRepeatKillTimer(OpenBattlegroundPolicy.IsEnabled, victim.CurrentRegionID);

        /// <summary>Pure form of <see cref="LiftsRepeatKillTimer(GameLiving)"/> for tests.</summary>
        public static bool LiftsRepeatKillTimer(bool enabled, ushort victimRegion) =>
            enabled && victimRegion == OpenBgRegion;

        public static OpenBgMode ParseMode(string text) =>
            string.Equals(text?.Trim(), "perrealm", StringComparison.OrdinalIgnoreCase) ? OpenBgMode.PerRealm : OpenBgMode.ServerPvp;

        public static OpenBgLandingPeace ParsePeace(string text, OpenBgMode mode)
        {
            string value = text?.Trim() ?? string.Empty;
            if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase)) return OpenBgLandingPeace.All;
            if (string.Equals(value, "realm", StringComparison.OrdinalIgnoreCase)) return OpenBgLandingPeace.Realm;
            return mode == OpenBgMode.ServerPvp && value.Length == 0 ? OpenBgLandingPeace.All : OpenBgLandingPeace.Realm;
        }

        public static OpenBgMode Current => ModeFor(WorldMode.Current, OpenBattlegroundProperties.OPEN_BG_MODE);

        public static OpenBgLandingPeace Peace => PeaceFor(WorldMode.Current, OpenBattlegroundProperties.OPEN_BG_LANDING_PEACE);

        /// <summary>
        /// The RvR world (save marker Realm-1) always plays the open BG per realm: <c>open_bg_mode</c> is a Camlann
        /// switch and is ignored there (one log line at start, <see cref="LogWorldOverride"/>).
        /// </summary>
        public static OpenBgMode ModeFor(WorldModelKind world, string property) =>
            world == WorldModelKind.Realm ? OpenBgMode.PerRealm : ParseMode(property);

        /// <summary>The RvR world keeps the landing peace per realm (the cross-realm "all" peace is a Server PvP rule).</summary>
        public static OpenBgLandingPeace PeaceFor(WorldModelKind world, string property) =>
            world == WorldModelKind.Realm ? OpenBgLandingPeace.Realm : ParsePeace(property, ParseMode(OpenBattlegroundProperties.OPEN_BG_MODE));

        /// <summary>The start line when the RvR world overrides a stored Camlann open BG setting; null when nothing is overridden.</summary>
        public static string WorldOverrideLine(WorldModelKind world, string modeProperty, string peaceProperty)
        {
            if (world != WorldModelKind.Realm)
                return null;
            bool modeOverridden = ParseMode(modeProperty) != OpenBgMode.PerRealm;
            bool peaceOverridden = ParsePeace(peaceProperty, OpenBgMode.PerRealm) != OpenBgLandingPeace.Realm;
            if (!modeOverridden && !peaceOverridden)
                return null;
            return $"OPEN_BG_MODE ignored=\"{modeProperty?.Trim()}\" peace_ignored=\"{peaceProperty?.Trim()}\" mode=perrealm peace=realm reason=rvr-world";
        }

        /// <summary>/bgsetup's mode value: the effective mode, plus "(ignored: rvr-world)" when the RvR world overrides the stored one.</summary>
        public static string ShownMode(WorldModelKind world, string property)
        {
            string stored = ParseMode(property) == OpenBgMode.PerRealm ? "perrealm" : "serverpvp";
            if (world != WorldModelKind.Realm)
                return stored;
            return stored == "perrealm" ? "perrealm" : $"perrealm (ignored: rvr-world; stored {stored})";
        }

        /// <summary>/bgsetup's peace value: "auto" or the stored text in Camlann; the forced realm peace in the RvR world.</summary>
        public static string ShownPeace(WorldModelKind world, string property)
        {
            string stored = string.IsNullOrWhiteSpace(property) ? "auto" : property.Trim().ToLowerInvariant();
            if (world != WorldModelKind.Realm || stored is "auto" or "realm")
                return world == WorldModelKind.Realm ? "realm" : stored;
            return $"realm (ignored: rvr-world; stored {stored})";
        }

        public static void LogWorldOverride(DOL.Logging.Logger log)
        {
            string line = WorldOverrideLine(WorldMode.Current, OpenBattlegroundProperties.OPEN_BG_MODE, OpenBattlegroundProperties.OPEN_BG_LANDING_PEACE);
            if (line != null && log.IsInfoEnabled)
                log.Info(line);
        }

        /// <summary>Per realm only: the open BG's central keep is a friend of every bot and player of its realm.</summary>
        public static bool KeepIsRealmOwned(OpenBgMode mode, ushort keepRegion) => mode == OpenBgMode.PerRealm && keepRegion == OpenBgRegion;

        /// <summary>Per realm only, inside the open BG region, for two real realms that are equal.</summary>
        public static bool RealmsAreFriends(OpenBgMode mode, ushort region, eRealm first, eRealm second) =>
            mode == OpenBgMode.PerRealm && region == OpenBgRegion && first == second && (int)first is >= 1 and <= 3;
    }
}
