using System;
using DOL.Database;
using DOL.GS.ServerProperties;

namespace DOL.GS.DatabaseUpdate
{
    /// <summary>
    /// Siege balance of 2026-09-28 (owner decisions 1a and 2b, docs/TASKS.md
    /// item 48): unclaimed keeps start at level 1 like a 1.65 unclaimed keep,
    /// and a guild may hold up to three keeps. Existing saves carry the old
    /// shipped values in the ServerProperty table, which the code default does
    /// not override. This runs at every server start and moves a row only
    /// while it still holds the old shipped value as both value and default;
    /// afterwards value and default are the new value, so it is a no-op, and a
    /// value an operator chose later (differing from the default) stays.
    /// </summary>
    [DatabaseUpdate]
    public class FrontierKeepBalanceUpdate : IDatabaseUpdater
    {
        private static readonly Logging.Logger log = Logging.LoggerManager.Create(typeof(FrontierKeepBalanceUpdate));

        public static readonly (string Key, string Legacy, string Target)[] Changes =
        {
            ("starting_keep_level", "4", "1"),
            ("guilds_claim_limit", "1", "3"),
        };

        /// <summary>True only for an untouched legacy row: value and default both the old shipped value.</summary>
        public static bool ShouldReplace(string value, string defaultValue, string legacy) =>
            string.Equals(value?.Trim(), legacy, StringComparison.Ordinal) &&
            string.Equals(defaultValue?.Trim(), legacy, StringComparison.Ordinal);

        public void Update()
        {
            bool changed = false;
            foreach (var change in Changes)
            {
                DbServerProperty row = DOLDB<DbServerProperty>.SelectObject(DB.Column("Key").IsEqualTo(change.Key));
                if (row == null || !ShouldReplace(row.Value, row.DefaultValue, change.Legacy))
                    continue;
                row.Value = change.Target;
                row.DefaultValue = change.Target;
                GameServer.Database.SaveObject(row);
                changed = true;
                log.Info($"FRONTIER_BALANCE_PROPERTY key={change.Key} from={change.Legacy} to={change.Target}");
            }

            if (changed)
                Properties.Refresh();
        }
    }
}
