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
        /// A keep of the open world (frontier or dungeon), never one inside a campaign
        /// battleground region (level brackets and the open BG). World bots must not be
        /// recalled to, sent to, or roam to a battleground keep: those belong to the
        /// battleground crews (their own defence runs inside the region).
        /// </summary>
        public static bool IsWorldKeep(ushort regionId) => BattlegroundCampaignCatalog.Find(regionId) == null;

        public static bool IsWorldKeep(AbstractGameKeep keep) => keep != null && IsWorldKeep((ushort)keep.Region);

        /// <summary>
        /// Roaming groups pass by portal keeps and relic temples (bug 146):
        /// relic-keep assaults stay off, and a lone roamer only feeds the guards.
        /// </summary>
        public static bool IsRoamDestination(bool portalKeep, bool relicKeep) => !portalKeep && !relicKeep;

        /// <summary>
        /// Mirrors AbstractGameKeep.CheckForClaim: relic keeps (skin 99) are
        /// shrine raids, and keeps whose base level is not 50 (relic keeps at 60,
        /// battleground keeps) cannot be claimed unless allow_bg_claim is on or
        /// the keep belongs to an open campaign battleground.
        /// Automatic world-bot assaults only open on keeps a guild can claim.
        /// </summary>
        public static bool IsClaimableKeep(bool portalKeep, bool relicKeep, int baseLevel, bool allowBattlegroundClaim) =>
            IsClaimableKeep(portalKeep, relicKeep, baseLevel, allowBattlegroundClaim, campaignBattleground: false);

        public static bool IsClaimableKeep(bool portalKeep, bool relicKeep, int baseLevel, bool allowBattlegroundClaim, bool campaignBattleground) =>
            !portalKeep && !relicKeep && (baseLevel == 50 || allowBattlegroundClaim || campaignBattleground);

        /// <summary>
        /// RvR world (R3a): a world bot's keep objectives are the keeps hostile to
        /// its realm (Warden, neutral and other realms' keeps), plus a keep of its
        /// own realm that awaits a claim its guild may make. Its own realm's keeps
        /// are never sieged. Camlann keeps every keep (the guild rules decide).
        /// </summary>
        public static bool IsRealmObjective(bool rvrWorld, bool hostileToBot, bool awaitingClaim, bool botMayClaim) =>
            !rvrWorld || hostileToBot || awaitingClaim && botMayClaim;

        public static bool IsRealmObjectiveFor(AbstractGameKeep keep, GameLiving bot) =>
            !WorldMode.IsRvr || keep?.DBKeep != null && bot != null && IsRealmObjective(true,
                GameServer.KeepManager.IsEnemy(keep, bot, false),
                keep.Guild == null && keep.DBKeep.LordDefeated, PvpKeepCampaign.MayClaimInWorld(keep, bot));

        public static bool IsClaimableKeep(AbstractGameKeep keep) => keep?.DBKeep != null &&
            IsClaimableKeep(keep.IsPortalKeep, keep.IsRelic, keep.DBKeep.BaseLevel, ServerProperties.Properties.ALLOW_BG_CLAIM,
                BattlegroundCampaignPolicy.IsEnabled && BattlegroundCampaignCatalog.Find(keep.Region) != null);
    }
}
