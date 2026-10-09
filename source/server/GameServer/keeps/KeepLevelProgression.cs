using System;
using DOL.Database;

namespace DOL.GS.Keeps
{
    /// <summary>Persisted guild keep progression. NPC holdings never participate.</summary>
    public static class KeepLevelProgression
    {
        public static bool IsGuildKeep(DbKeep keep) => keep != null && keep.BaseLevel < 100 &&
            keep.SkinType != 99 && !keep.LordDefeated &&
            !string.IsNullOrEmpty(keep.ClaimedGuildName) &&
            keep.ClaimedGuildName != PvpKeepCampaign.GarrisonName;

        // Per-keep marker makes the owner-requested reset restart-safe, including
        // guilds that have not yet been resolved by the autonomous crew loader.
        public static bool Initialize(DbKeep keep, DateTime now)
        {
            if (!IsGuildKeep(keep) || keep.ProgressionInitialized) return false;
            keep.ProgressionInitialized = true;
            keep.Level = 1;
            keep.NextLevelAt = now.AddMilliseconds(GameKeep.UpgradeTime[2]);
            return true;
        }

        public static bool Advance(DbKeep keep, DateTime now, int maximum)
        {
            if (!IsGuildKeep(keep)) return false;
            int cap = Math.Clamp(maximum, 1, 10);
            if (keep.Level >= cap)
            {
                if (keep.NextLevelAt == DateTime.MinValue) return false;
                keep.NextLevelAt = DateTime.MinValue;
                return true;
            }
            if (keep.NextLevelAt == DateTime.MinValue)
            {
                keep.NextLevelAt = now.AddMilliseconds(GameKeep.UpgradeTime[keep.Level + 1]);
                return true;
            }
            bool changed = false;
            while (keep.Level < cap && now >= keep.NextLevelAt)
            {
                DateTime reachedAt = keep.NextLevelAt;
                keep.Level++;
                keep.NextLevelAt = keep.Level == cap ? DateTime.MinValue :
                    reachedAt.AddMilliseconds(GameKeep.UpgradeTime[keep.Level + 1]);
                changed = true;
            }
            return changed;
        }
    }
}
