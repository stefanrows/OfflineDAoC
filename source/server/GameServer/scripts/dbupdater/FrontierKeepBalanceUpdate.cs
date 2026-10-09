using System;
using DOL.Database;
using DOL.GS.ServerProperties;

namespace DOL.GS.DatabaseUpdate
{
    /// <summary>
    /// Unclaimed keep levels migrate only from the untouched shipped value.
    /// The owner removed all guild claim limits on 2026-10-03; existing saves
    /// also get the unlimited marker (-1), including the previously shipped 3.
    /// Claim checks no longer enforce this legacy setting.
    /// </summary>
    [DatabaseUpdate]
    public class FrontierKeepBalanceUpdate : IDatabaseUpdater
    {
        private static readonly Logging.Logger log = Logging.LoggerManager.Create(typeof(FrontierKeepBalanceUpdate));

        public static readonly (string Key, string Legacy, string Target)[] Changes =
        {
            ("starting_keep_level", "4", "1"),
            ("starting_keep_claim_level", "5", "1"),
            ("max_keep_level", "5", "10"),
            ("enable_keep_upgrade_timer", "False", "True"),
            ("guilds_claim_limit", "1", "-1"),
        };

        /// <summary>True only for an untouched legacy row: value and default both the old shipped value.</summary>
        public static bool ShouldReplace(string value, string defaultValue, string legacy) =>
            string.Equals(value?.Trim(), legacy, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(defaultValue?.Trim(), legacy, StringComparison.OrdinalIgnoreCase);

        public void Update()
        {
            bool changed = false;
            foreach (var change in Changes)
            {
                DbServerProperty row = DOLDB<DbServerProperty>.SelectObject(DB.Column("Key").IsEqualTo(change.Key));
                if (row == null) continue;
                bool replace = change.Key == "guilds_claim_limit"
                    ? row.Value != change.Target || row.DefaultValue != change.Target
                    : ShouldReplace(row.Value, row.DefaultValue, change.Legacy);
                if (!replace) continue;
                string previous = row.Value;
                row.Value = change.Target;
                row.DefaultValue = change.Target;
                GameServer.Database.SaveObject(row);
                changed = true;
                log.Info($"FRONTIER_BALANCE_PROPERTY key={change.Key} from={previous} to={change.Target}");
            }

            if (changed)
                Properties.Refresh();
        }
    }
}
