using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// The porters' Atlantis lines (docs/TOA_TRAVEL.md). Every <see cref="AllRealmsTeleporter"/> offers
    /// [Atlantis] (the player's realm ruins city, 70/71/72), [Atlantis Havens] and [Atlantis Dungeons].
    /// Destinations always resolve in the asking player's own realm copy: Teleport rows of type
    /// <see cref="TeleportType"/> with Realm 1, 2 or 3 (data pack 026-toa-travel-realms). The menu and the
    /// lines need the account privilege <c>atlantis_teleport_plvl</c>.
    /// </summary>
    public static class AtlantisTravel
    {
        /// <summary>Teleport.Type of the per-realm Atlantis destinations.</summary>
        public const string TeleportType = "atlantis";

        /// <summary>The ruins city of the player's realm: Arbiter, scholars, trainers, vault, healer.</summary>
        public const string Hub = "Atlantis";
        public const string HavensMenuKey = "Atlantis Havens";
        public const string DungeonsMenuKey = "Atlantis Dungeons";
        public const string NotAuthorized = "I'm sorry, but you are not authorized to enter Atlantis at this time.";

        /// <summary>The four havens: towns with a bind stone and a porter, no aggressive mob near the landing.</summary>
        public static readonly IReadOnlyList<string> Havens = ["Haven of Oceanus", "Haven of Stygia", "Haven of Volcanus", "Haven of Aerus"];

        /// <summary>The dungeon entrances that hold mobs (Deep Volcanus and Celestius are empty); landing = the zone point arrival.</summary>
        public static readonly IReadOnlyList<string> Dungeons =
            ["Cetus' Pit", "Sobekite Eternal", "Temple of Twilight", "Halls of Ma'ati", "Great Pyramid", "City of Aerus"];

        /// <summary>Every destination name, which is also the TeleportID of its Teleport rows.</summary>
        public static IReadOnlyList<string> Destinations { get; } = new[] { Hub }.Concat(Havens).Concat(Dungeons).ToArray();

        public const string MenuLine = "[Atlantis] [Atlantis Havens] [Atlantis Dungeons]";

        public const string HavensMenu = "Which haven of Atlantis do you seek?\n" +
                                         "[Haven of Oceanus] in Oceanus Hesperos\n" +
                                         "[Haven of Stygia] in the Stygian Delta\n" +
                                         "[Haven of Volcanus] in Oceanus Anatole, before Typhon's Reach\n" +
                                         "[Haven of Aerus] in Oceanus Notos, before the Green Glades";

        public const string DungeonsMenu = "Which of the dungeons of Atlantis do you dare?\n" +
                                           "[Cetus' Pit] [Sobekite Eternal] [Temple of Twilight]\n" +
                                           "[Halls of Ma'ati] [Great Pyramid] [City of Aerus]";

        /// <summary>The account privilege level may use the Atlantis lines.</summary>
        public static bool CanTravel(int privLevel) => privLevel >= Properties.ATLANTIS_TELEPORT_PLVL;

        /// <summary>The player's account may use the Atlantis lines; an account-less test player counts as a player.</summary>
        public static bool CanTravel(GamePlayer player) =>
            player != null && CanTravel((int)(player.Client?.Account?.PrivLevel ?? (uint)ePrivLevel.Player));

        /// <summary>Matches a whispered text to a destination name, ignoring case and surrounding blanks.</summary>
        public static bool TryGetDestination(string text, out string destination)
        {
            string trimmed = text?.Trim() ?? string.Empty;
            destination = Destinations.FirstOrDefault(name => name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
            return destination != null;
        }

        /// <summary>The installed Teleport row of a destination in the realm's own copy, or null.</summary>
        public static DbTeleport Find(eRealm realm, string destination) =>
            realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia
                ? WorldMgr.GetTeleportLocation(realm, TeleportType + ":" + destination)
                : null;

        public static string ArrivalMessage(string destination) => destination == Hub
            ? "The currents of Atlantis will carry you to the ruins of your realm."
            : "You will soon arrive at " + destination + ".";
    }
}
