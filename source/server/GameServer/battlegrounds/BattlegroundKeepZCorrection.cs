using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>A saved keep row that was given a Z before its ground was checked. Only this exact value is corrected.</summary>
    public sealed record KeepZCorrection(int KeepId, int WrongZ);

    /// <summary>The saved keep row fields the Z correction reads.</summary>
    public readonly record struct KeepRowZState(bool RowExists, ushort Region, int X, int Y, int Z);

    /// <summary>Pure rule for the keep Z correction. No database or world access.</summary>
    public static class BattlegroundKeepZPlanner
    {
        /// <summary>
        /// Correct only the same keep (region and centre) that still holds the known wrong Z. A row already at the
        /// site Z no longer matches WrongZ, so the correction runs once and never moves a row that was changed later.
        /// </summary>
        public static bool ShouldCorrect(KeepSite site, int wrongZ, KeepRowZState row) =>
            site != null && row.RowExists && row.Region == site.Region && row.X == site.X && row.Y == site.Y &&
            row.Z == wrongZ && site.Z != wrongZ;
    }

    /// <summary>
    /// Sets the saved Z of three battleground keeps to the ground the navigation builder sampled under them. Runs before
    /// keeps load, next to BattlegroundNativeKeepData.EnsureKeepRows, so the pieces, doors, lord and portal landings
    /// of those keeps load at the corrected height. Components, guards and keep doors store offsets from the keep Z,
    /// so only the keep row changes.
    /// </summary>
    public static class BattlegroundKeepZCorrection
    {
        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(BattlegroundKeepZCorrection));

        public static IReadOnlyList<KeepZCorrection> Corrections { get; } = Array.AsReadOnly(new KeepZCorrection[]
        {
            // Leirvik Castle: the builder samples terrain 10976 under all 25 pieces and both gates (keep Z minus 3305).
            new KeepZCorrection(134, 14281),
            // Killaloe Albion portal keep: terrain 8288 under all 8 ring pieces (keep Z minus 588).
            new KeepZCorrection(201, 7700),
            // Killaloe Hibernia portal keep: terrain 8288 under 7 of 8 ring pieces and the gate (the north corner reads 8280).
            new KeepZCorrection(203, 8000),
        });

        public static void EnsureKeepZ()
        {
            if (!BattlegroundCampaignPolicy.IsEnabled) return;
            foreach (KeepZCorrection correction in Corrections)
            {
                KeepSite site = BattlegroundKeepLayouts.Sites.Concat(BattlegroundKeepLayouts.PortalSites)
                    .FirstOrDefault(candidate => candidate.KeepId == correction.KeepId);
                if (site == null) continue;
                DbKeep row = GameServer.Database.SelectObject<DbKeep>(DB.Column("KeepID").IsEqualTo(correction.KeepId));
                KeepRowZState state = row == null ? default : new KeepRowZState(true, row.Region, row.X, row.Y, row.Z);
                if (!BattlegroundKeepZPlanner.ShouldCorrect(site, correction.WrongZ, state)) continue;

                int from = row.Z;
                row.Z = site.Z;
                if (!GameServer.Database.SaveObject(row))
                {
                    row.Z = from;
                    Log.Warn($"BATTLEGROUND_KEEP_Z_NOT_SAVED keep={correction.KeepId} from={from} to={site.Z}");
                    continue;
                }
                Log.Info($"BATTLEGROUND_KEEP_Z_CORRECTED keep={correction.KeepId} from={from} to={site.Z}");
            }
        }
    }
}
