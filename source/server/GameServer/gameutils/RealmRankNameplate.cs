using System;

namespace DOL.GS
{
    /// <summary>
    /// Realm rank after a name on overhead nameplates, such as "Dairoinlan | RR 2L5", behind the
    /// show_realm_rank_nameplates server property (off by default). Presentation only: the packet writers
    /// use these helpers for the strings they send, and nothing here changes a stored name, last name,
    /// guild or save.
    /// </summary>
    public static class RealmRankNameplate
    {
        /// <summary>The longest NPC name the create packet carries without a GM flag suffix.</summary>
        public const int MaxNpcNameLength = 45;

        /// <summary>
        /// The longest player last-name string. The client copies it into a 32-byte buffer without a
        /// length check, so 31 characters plus the terminator is the limit.
        /// </summary>
        public const int MaxPlayerLastNameLength = 31;

        private const string Separator = " | ";

        /// <summary>True while the show_realm_rank_nameplates server property is on.</summary>
        public static bool Enabled => ServerProperties.Properties.SHOW_REALM_RANK_NAMEPLATES;

        /// <summary>The rank label for a realm level: 0 gives "RR 1L0", 15 gives "RR 2L5".</summary>
        public static string Format(int realmLevel)
        {
            int level = Math.Max(0, realmLevel);
            return "RR " + (level / 10 + 1) + "L" + (level % 10);
        }

        /// <summary>
        /// Autonomous world bots and persistent companions only. Temporary /spawn helpers, battleground
        /// encounter actors and ordinary NPCs never get the rank.
        /// </summary>
        public static bool IsNameplateBot(GameNPC npc)
        {
            if (npc is not GameBot bot)
                return false;
            return IsNameplateBotKind(bot.IsAutonomousWorldBot, bot.IsPersistentPlayerCompanion,
                bot.IsTemporaryGroupHelper, bot.IsBattlegroundEncounterBot);
        }

        /// <summary>The kind rule behind <see cref="IsNameplateBot"/>, kept separate so it can be tested directly.</summary>
        public static bool IsNameplateBotKind(bool autonomousWorldBot, bool persistentCompanion,
            bool temporaryGroupHelper, bool battlegroundEncounterBot)
        {
            return (autonomousWorldBot || persistentCompanion) && !temporaryGroupHelper && !battlegroundEncounterBot;
        }

        /// <summary>
        /// The name for an NPC create or living-data packet. While the property is on, an eligible bot gets
        /// " | RR xLy" after its translated or stored name. The plain name is kept when the result would
        /// exceed the 45-character cap; the name is never cut short to make room for the rank.
        /// </summary>
        public static string NpcName(GameNPC npc, string name)
        {
            if (!Enabled || string.IsNullOrEmpty(name) || !IsNameplateBot(npc))
                return name;
            return ComposeNpcName(name, ((GameBot)npc).RealmLevel);
        }

        /// <summary>
        /// The last-name string for a player create or living-data packet. While the property is on this is
        /// "last | RR xLy", or "| RR xLy" for a player without a last name. The plain last name is kept when the
        /// result would exceed 31 characters.
        /// </summary>
        public static string PlayerLastName(GamePlayer player, string lastName)
        {
            if (!Enabled || player == null)
                return lastName;
            return ComposePlayerLastName(lastName, player.RealmLevel);
        }

        /// <summary>The NPC suffix rule without the property check: "name | RR xLy" when it fits the cap.</summary>
        public static string ComposeNpcName(string name, int realmLevel)
        {
            if (string.IsNullOrEmpty(name))
                return name;
            string labelled = name + Separator + Format(realmLevel);
            return labelled.Length <= MaxNpcNameLength ? labelled : name;
        }

        /// <summary>The player last-name rule without the property check.</summary>
        public static string ComposePlayerLastName(string lastName, int realmLevel)
        {
            string label = Format(realmLevel);
            string labelled = string.IsNullOrEmpty(lastName) ? "| " + label : lastName + Separator + label;
            return labelled.Length <= MaxPlayerLastNameLength ? labelled : lastName;
        }
    }
}
