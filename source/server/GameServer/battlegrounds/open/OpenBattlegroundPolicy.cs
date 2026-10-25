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

        /// <summary>
        /// RvR world (owner 2026-10-25, bug 179): the open BG is off by default there. An existing Realm-1 save that has
        /// not had the default yet turns it off once; Camlann and a save already migrated keep the owner's setting.
        /// </summary>
        public static bool TurnsOffForRvrWorld(WorldModelKind world, bool defaultApplied) =>
            world == WorldModelKind.Realm && !defaultApplied;

        /// <summary>The one-time switch-off of <see cref="TurnsOffForRvrWorld"/>, run at server start before any roster pulse.</summary>
        public static void ApplyRvrWorldDefault()
        {
            if (!TurnsOffForRvrWorld(WorldMode.Current, OpenBattlegroundProperties.OPEN_BG_RVR_DEFAULT_APPLIED)) return;
            bool wasEnabled = OpenBattlegroundProperties.OPEN_BG_ENABLED;
            if (!DOL.GS.Commands.OpenBgSetupStore.TryWrite("open_bg_enabled", "False", "rvr-world-default", out string error) ||
                !DOL.GS.Commands.OpenBgSetupStore.TryWrite("open_bg_rvr_default_applied", "True", "rvr-world-default", out error))
            {
                Log.Warn($"RVR_OPEN_BG_DEFAULT_OFF failed error=\"{error}\"");
                return;
            }
            Log.Info($"RVR_OPEN_BG_DEFAULT_OFF world=Realm-1 wasEnabled={wasEnabled} enabled={OpenBattlegroundProperties.OPEN_BG_ENABLED}");
        }

        // Once per server start: the RvR world's default, then why the open BG stays closed (porter line hidden, no bots
        // assigned).
        [GameServerStartedEvent]
        public static void ReportAvailability(DOLEvent e, object sender, EventArgs args)
        {
            try
            {
                ApplyRvrWorldDefault();
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
