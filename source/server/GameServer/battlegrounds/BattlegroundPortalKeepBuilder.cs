using System.Collections.Generic;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>What the database holds for one portal keep, read before keeps load.</summary>
    public readonly record struct PortalKeepRowState(bool RowExists, ushort Region, byte BaseLevel, byte SkinType,
        int X, int Y, int Z, int Heading, int ComponentCount);

    /// <summary>The decision for one portal keep: build it, or skip it with the reason that is logged.</summary>
    public readonly record struct PortalKeepDecision(bool Build, string Reason);

    /// <summary>Pure rules for the server-built portal keep rings. No database or world access.</summary>
    public static class BattlegroundPortalKeepPlanner
    {
        public static PortalKeepDecision Decide(KeepSite site, PortalKeepRowState row)
        {
            if (site == null) return Skip("site_missing");
            if (BattlegroundKeepLayouts.Template(site.Template).Count == 0) return Skip("template_missing");
            if (!row.RowExists) return Skip("row_missing");
            if (row.Region != site.Region) return Skip("id_conflict");
            if (row.BaseLevel < BattlegroundKeepLayouts.PortalBaseLevel) return Skip("not_portal_row");
            // 0 = any and 1 = old skins use the old component numbering the ring is written in; 2 (new) and 99 (relic) do not.
            if (row.SkinType != 0 && row.SkinType != 1) return Skip("skin_type_unsupported");
            // The saved row is authoritative. The navigation builder bakes the ring from the same JSON values, so a moved row must not get a ring.
            if (row.X != site.X || row.Y != site.Y || row.Z != site.Z || row.Heading != site.Heading) return Skip("site_mismatch");
            // Additive only: a row that already has any component is never touched.
            if (row.ComponentCount != 0) return Skip("has_components");
            return new PortalKeepDecision(true, string.Empty);
        }

        private static PortalKeepDecision Skip(string reason) => new(false, reason);
    }

    /// <summary>
    /// Adds the portal keep rings before keeps load, so every portal keep loads its gate, walls and towers like
    /// any other keep. Runs from KeepManager.LoadKeeps next to BattlegroundNativeKeepData.EnsureKeepRows.
    /// </summary>
    public static class BattlegroundPortalKeepBuilder
    {
        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(BattlegroundPortalKeepBuilder));

        /// <summary>Component CreateInfo prefix. The keep row's own CreateInfo stays empty, so the garrison code never claims these keeps.</summary>
        public const string ComponentInfo = "offline-bg-portal-keep";

        public static void EnsureComponents()
        {
            if (!BattlegroundCampaignPolicy.IsEnabled) return;
            foreach (NativePortalKeep native in BattlegroundKeepLayouts.PortalNativeKeeps)
                Log.Info($"BATTLEGROUND_PORTAL_KEEP_SKIPPED region={native.Region} keep={native.KeepId} reason=native_client_keep");
            foreach (KeepSite site in BattlegroundKeepLayouts.PortalSites)
                EnsureSite(site);
        }

        private static void EnsureSite(KeepSite site)
        {
            DbKeep row = GameServer.Database.SelectObject<DbKeep>(DB.Column("KeepID").IsEqualTo(site.KeepId));
            int components = GameServer.Database.SelectObjects<DbKeepComponent>(DB.Column("KeepID").IsEqualTo(site.KeepId)).Count;
            PortalKeepRowState state = row == null
                ? new PortalKeepRowState(false, 0, 0, 0, 0, 0, 0, 0, components)
                : new PortalKeepRowState(true, row.Region, row.BaseLevel, row.SkinType, row.X, row.Y, row.Z, row.Heading, components);
            PortalKeepDecision decision = BattlegroundPortalKeepPlanner.Decide(site, state);
            if (!decision.Build)
            {
                Log.Info($"BATTLEGROUND_PORTAL_KEEP_SKIPPED region={site.Region} keep={site.KeepId} reason={decision.Reason}");
                return;
            }

            var added = new List<DbKeepComponent>();
            foreach (ComponentSpec spec in BattlegroundKeepLayouts.Template(site.Template))
            {
                var component = new DbKeepComponent(spec.Id, spec.Skin, spec.X, spec.Y, spec.Heading, 0, 3200, site.KeepId,
                    $"{ComponentInfo}:{site.Region}");
                if (!GameServer.Database.AddObject(component))
                {
                    // Roll back, so a half-built ring is not left for the has_components check to accept on the next start.
                    foreach (DbKeepComponent done in added)
                        GameServer.Database.DeleteObject(done);
                    Log.Warn($"BATTLEGROUND_PORTAL_KEEP_SKIPPED region={site.Region} keep={site.KeepId} reason=component_not_saved");
                    return;
                }
                added.Add(component);
            }
            Log.Info($"BATTLEGROUND_PORTAL_KEEP_BUILT region={site.Region} keep={site.KeepId} components={added.Count}");
        }
    }
}
