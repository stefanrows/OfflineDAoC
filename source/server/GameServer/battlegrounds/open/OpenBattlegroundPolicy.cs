using System;
using DOL.Events;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Entry rules of the open BG (spec 2026-10-11 §2, §9): levels 1-50, every realm, no realm-rank limit,
    /// only while it is switched on and zone 252 has its navmesh and verified landings. The brackets never
    /// route into it; players use the porter's [open BG] line, bots the roster's porter departure.
    /// </summary>
    public static class OpenBattlegroundPolicy
    {
        public const int MinLevel = 1;
        public const int MaxLevel = 50;
        private static readonly Logger Log = LoggerManager.Create(typeof(OpenBattlegroundPolicy));

        /// <summary>The open BG is part of the battleground campaign and has its own switch on top.</summary>
        public static bool IsEnabled => BattlegroundCampaignPolicy.IsEnabled && OpenBattlegroundProperties.OPEN_BG_ENABLED;

        public static bool CanEnterOpen(bool enabled, bool ready, int level) =>
            enabled && ready && level >= MinLevel && level <= MaxLevel;

        /// <summary>The first missing piece as a log reason, or null when the open BG can open.</summary>
        public static string UnavailableReason(bool enabled, bool hasNavmesh, int landings) =>
            !enabled ? "disabled" : !hasNavmesh ? "no_navmesh" : landings <= 0 ? "no_landings" : null;

        public static bool HasNavmesh(BattlegroundDefinition definition)
        {
            if (definition == null) return false;
            Zone zone = WorldMgr.GetZone(definition.ZoneId);
            var nav = PathfindingProvider.Instance;
            return WorldMgr.GetRegion(definition.RegionId) != null && zone != null && nav.IsAvailable && nav.HasNavmesh(zone);
        }

        /// <summary>Whether this actor may enter now: switched on, zone ready and level 1-50. Cheap when switched off.</summary>
        public static bool IsOpenFor(GameLiving actor)
        {
            if (actor == null || !IsEnabled) return false;
            return CanEnterOpen(true, BattlegroundCampaignPolicy.IsReady(BattlegroundCampaignCatalog.FindOpen(), out _), actor.Level);
        }

        // Once per server start: why the open BG stays closed (porter line hidden, no bots assigned).
        [GameServerStartedEvent]
        public static void ReportAvailability(DOLEvent e, object sender, EventArgs args)
        {
            try
            {
                BattlegroundDefinition open = BattlegroundCampaignCatalog.FindOpen();
                bool hasNavmesh = HasNavmesh(open);
                int landings = hasNavmesh && BattlegroundCampaignPolicy.IsReady(open, out _)
                    ? BattlegroundCampaignCatalog.GetLandings(open).Length : 0;
                string reason = UnavailableReason(IsEnabled, hasNavmesh, landings);
                if (reason == null) return;
                string line = $"OPEN_BG_UNAVAILABLE reason={reason} region={open?.RegionId} campaign={BattlegroundCampaignPolicy.IsEnabled} " +
                    $"open_bg_enabled={OpenBattlegroundProperties.OPEN_BG_ENABLED}";
                if (reason == "disabled") Log.Info(line);
                else Log.Warn(line);
            }
            catch (Exception error)
            {
                Log.Error("OPEN_BG_UNAVAILABLE reason=check_failed", error);
            }
        }
    }
}
