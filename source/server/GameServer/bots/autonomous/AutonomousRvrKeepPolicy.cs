using DOL.GS.Keeps;

namespace DOL.GS
{
    /// <summary>Portal keeps are protected travel hubs, never siege objectives.
    /// Relic keeps remain valid raid objectives even though the relic, rather
    /// than the keep itself, is the capturable objective.</summary>
    public static class AutonomousRvrKeepPolicy
    {
        public static bool IsSiegeObjective(AbstractGameKeep keep)
        {
            return keep != null && !keep.IsPortalKeep;
        }

        /// <summary>
        /// Mirrors AbstractGameKeep.CheckForClaim: relic keeps (skin 99) are
        /// shrine raids, and keeps whose base level is not 50 (relic keeps at 60,
        /// battleground keeps) cannot be claimed unless allow_bg_claim is on.
        /// Automatic world-bot assaults only open on keeps a guild can claim.
        /// </summary>
        public static bool IsClaimableKeep(bool portalKeep, bool relicKeep, int baseLevel, bool allowBattlegroundClaim) =>
            !portalKeep && !relicKeep && (baseLevel == 50 || allowBattlegroundClaim);

        public static bool IsClaimableKeep(AbstractGameKeep keep) => keep?.DBKeep != null &&
            IsClaimableKeep(keep.IsPortalKeep, keep.IsRelic, keep.DBKeep.BaseLevel, ServerProperties.Properties.ALLOW_BG_CLAIM);
    }
}
