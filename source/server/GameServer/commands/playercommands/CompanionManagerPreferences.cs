using System;
using System.Collections.Generic;
using DOL.Database;

namespace DOL.GS.Commands
{
    /// <summary>
    /// Remembers how a player arranged the Companion Manager list (grouping, sorting, and how many
    /// rows to fill) in the save's fork-local options table, one row per account, so the choice
    /// survives closing the window and logging out. Nothing else about the window is saved, and a
    /// missing database or an unreadable value simply leaves the defaults.
    /// </summary>
    public static class CompanionManagerPreferences
    {
        private const string KeyPrefix = "companion_manager.view.";

        public static string Serialize(CompanionManagerGroup group, CompanionManagerSort sort, int rows) =>
            $"group={group};sort={sort};rows={rows}";

        /// <summary>Unknown names and row counts that are not an offered size are rejected.</summary>
        public static bool TryParse(string value, out CompanionManagerGroup group, out CompanionManagerSort sort, out int rows)
        {
            group = CompanionManagerGroup.Smart;
            sort = CompanionManagerSort.Level;
            rows = CompanionManagerProtocol.RowSizes[1];
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int split = part.IndexOf('=');
                if (split > 0)
                    fields[part[..split].Trim()] = part[(split + 1)..].Trim();
            }
            return fields.TryGetValue("group", out string groupText) && Enum.TryParse(groupText, true, out group) &&
                   Enum.IsDefined(group) &&
                   fields.TryGetValue("sort", out string sortText) && Enum.TryParse(sortText, true, out sort) &&
                   Enum.IsDefined(sort) &&
                   fields.TryGetValue("rows", out string rowsText) && int.TryParse(rowsText, out rows) &&
                   Array.IndexOf(CompanionManagerProtocol.RowSizes, rows) >= 0;
        }

        public static void Load(GamePlayer player, CompanionManagerSession session)
        {
            string key = KeyFor(player);
            if (key == null)
                return;
            try
            {
                DbOfflineLocalOption option = GameServer.Database?.FindObjectByKey<DbOfflineLocalOption>(key);
                if (option != null && TryParse(option.Value, out CompanionManagerGroup group, out CompanionManagerSort sort, out int rows))
                {
                    session.Group = group;
                    session.Sort = sort;
                    session.RowCount = rows;
                }
            }
            catch (Exception)
            {
                // The window works with its defaults when the preference cannot be read.
            }
        }

        public static void Save(GamePlayer player, CompanionManagerSession session)
        {
            string key = KeyFor(player);
            if (key == null)
                return;
            try
            {
                IObjectDatabase database = GameServer.Database;
                if (database == null)
                    return;
                string value = Serialize(session.Group, session.Sort, session.RowCount);
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
            }
            catch (Exception)
            {
                // A preference is never worth interrupting the player over; it simply is not remembered.
            }
        }

        private static string KeyFor(GamePlayer player)
        {
            string account = player?.AccountName;
            return string.IsNullOrWhiteSpace(account) ? null : KeyPrefix + account.Trim().ToLowerInvariant();
        }
    }
}
