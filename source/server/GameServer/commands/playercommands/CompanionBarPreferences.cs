using System;
using DOL.Database;

namespace DOL.GS.Commands
{
    /// <summary>
    /// Remembers, per account, whether the COMPANIONS window (Custom7) is enabled. The value lives in the save's
    /// fork-local options table as "on" or "off". A missing or unreadable value means on, the default.
    /// </summary>
    public static class CompanionBarPreferences
    {
        public const string KeyPrefix = "companion_bar.enabled.";
        public const bool DefaultEnabled = true;

        public static string Serialize(bool enabled) => enabled ? "on" : "off";

        /// <summary>Accepts only "on" or "off", ignoring case and surrounding spaces. Anything else is rejected.</summary>
        public static bool TryParse(string value, out bool enabled)
        {
            enabled = DefaultEnabled;
            string text = value?.Trim();
            if (string.Equals(text, "on", StringComparison.OrdinalIgnoreCase))
            {
                enabled = true;
                return true;
            }
            if (string.Equals(text, "off", StringComparison.OrdinalIgnoreCase))
            {
                enabled = false;
                return true;
            }
            return false;
        }

        /// <summary>The option key for an account, lower-cased. Null when the account name is blank.</summary>
        public static string KeyForAccount(string accountName)
        {
            string account = accountName?.Trim();
            return string.IsNullOrEmpty(account) ? null : KeyPrefix + account.ToLowerInvariant();
        }

        /// <summary>The stored preference for the player's account. Missing, unreadable or invalid values mean on.</summary>
        public static bool Load(GamePlayer player)
        {
            string key = KeyForAccount(player?.AccountName);
            if (key == null)
                return DefaultEnabled;
            try
            {
                DbOfflineLocalOption option = GameServer.Database?.FindObjectByKey<DbOfflineLocalOption>(key);
                if (option != null && TryParse(option.Value, out bool enabled))
                    return enabled;
            }
            catch (Exception)
            {
                // The window works with its default when the preference cannot be read.
            }
            return DefaultEnabled;
        }

        /// <summary>Stores the preference. Returns false when it could not be stored; the window still works for the session.</summary>
        public static bool Save(GamePlayer player, bool enabled)
        {
            string key = KeyForAccount(player?.AccountName);
            if (key == null)
                return false;
            try
            {
                IObjectDatabase database = GameServer.Database;
                if (database == null)
                    return false;
                string value = Serialize(enabled);
                lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                {
                    DbOfflineLocalOption option = database.FindObjectByKey<DbOfflineLocalOption>(key);
                    if (option == null)
                        database.AddObject(new DbOfflineLocalOption { Key = key, Value = value });
                    else if (option.Value != value)
                    {
                        option.Value = value;
                        database.SaveObject(option);
                    }
                }
                return true;
            }
            catch (Exception)
            {
                // A preference is never worth interrupting the player over; it simply is not remembered.
                return false;
            }
        }
    }
}
