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

        public static OpenBgMode ParseMode(string text) =>
            string.Equals(text?.Trim(), "perrealm", StringComparison.OrdinalIgnoreCase) ? OpenBgMode.PerRealm : OpenBgMode.ServerPvp;

        public static OpenBgLandingPeace ParsePeace(string text, OpenBgMode mode)
        {
            string value = text?.Trim() ?? string.Empty;
            if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase)) return OpenBgLandingPeace.All;
            if (string.Equals(value, "realm", StringComparison.OrdinalIgnoreCase)) return OpenBgLandingPeace.Realm;
            return mode == OpenBgMode.ServerPvp && value.Length == 0 ? OpenBgLandingPeace.All : OpenBgLandingPeace.Realm;
        }

        public static OpenBgMode Current => ParseMode(OpenBattlegroundProperties.OPEN_BG_MODE);

        public static OpenBgLandingPeace Peace => ParsePeace(OpenBattlegroundProperties.OPEN_BG_LANDING_PEACE, Current);

        /// <summary>Per realm only, inside the open BG region, for two real realms that are equal.</summary>
        public static bool RealmsAreFriends(OpenBgMode mode, ushort region, eRealm first, eRealm second) =>
            mode == OpenBgMode.PerRealm && region == OpenBgRegion && first == second && (int)first is >= 1 and <= 3;
    }
}
