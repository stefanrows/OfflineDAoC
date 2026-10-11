using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DOL.Events;
using DOL.GS.Keeps;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>What the phase director reports for the status page. Stage 2 fills it; until then no source exists.</summary>
    public readonly record struct OpenBgPhaseStatus(string Phase, string Attacker, int SecondsLeft);

    /// <summary>One open-BG crew member as the status page and the launcher see it.</summary>
    /// <param name="State">inside, outside (entered but not in region 252 now), travelling or leaving.</param>
    public sealed record OpenBgBotStatus(string Realm, string Name, int Level, string ClassName, int Unit, string Kind,
        string Leader, string State, bool Alive);

    /// <summary>A human player standing in region 252.</summary>
    public sealed record OpenBgPlayerStatus(string Realm, string Name, int Level);

    /// <summary>
    /// A read-only picture of the open BG at one moment (spec 2026-10-11 §7): switch and readiness, the director's
    /// phase (n/a until stage 2), keep 11's owner, the achieved world speed and the crew per realm. The /bgsetup
    /// window renders it; the launcher reads the same record from <see cref="OpenBattlegroundStatus.FileName"/>.
    /// </summary>
    public sealed record OpenBgStatusSnapshot(DateTime UpdatedUtc, bool Running, bool CampaignEnabled, bool OpenEnabled,
        bool Ready, string NotReadyReason, string Phase, string Attacker, int PhaseSecondsLeft, string KeepOwner,
        double AchievedSpeed, int SelectedSpeed, int Cap, OpenBgPlayerStatus[] Players, OpenBgBotStatus[] Bots)
    {
        public const string PhaseNotAvailable = "n/a (stage 2)";
    }

    /// <summary>
    /// Captures <see cref="OpenBgStatusSnapshot"/> for /bgsetup and publishes it every 15 wall seconds to
    /// open-bg-status.json beside the server for the launcher's open BG tab. Capture runs on the game loop (an
    /// <see cref="ECSGameTimer"/>), the file write on the thread pool. Nothing here writes the save.
    /// </summary>
    public static class OpenBattlegroundStatus
    {
        public const string FileName = "open-bg-status.json";
        public const int PublishWallMilliseconds = 15_000;

        /// <summary>
        /// The phase director's report. Null until the director (stage 2) sets it; the status then shows
        /// <see cref="OpenBgStatusSnapshot.PhaseNotAvailable"/>.
        /// </summary>
        public static Func<OpenBgPhaseStatus> PhaseSource { get; set; }

        private static readonly Logger Log = LoggerManager.Create(typeof(OpenBattlegroundStatus));
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
        private static ECSGameTimer _timer;
        private static int _writing;
        private static bool _warned;

        public static string FilePath => Path.Combine(AppContext.BaseDirectory, FileName);

        /// <summary>The kind a unit of this size has when the roster no longer tracks it.</summary>
        public static string KindFromSize(int size) => size <= 1 ? "solo" : size >= OpenBattlegroundRoster.Group8Minimum ? "group8" : "small";

        /// <summary>A crew member's state: leaving wins, then travelling, then inside or outside region 252.</summary>
        public static string StateOf(bool entered, bool leaving, bool inRegion) =>
            leaving ? "leaving" : !entered ? "travelling" : inRegion ? "inside" : "outside";

        /// <summary>The live state now. Call on the game loop (command handler or the publish timer).</summary>
        public static OpenBgStatusSnapshot Capture(bool running = true)
        {
            BattlegroundDefinition open = BattlegroundCampaignCatalog.FindOpen();
            string reason = null;
            bool ready = open != null && BattlegroundCampaignPolicy.IsReady(open, out reason);
            if (open == null) reason = "no open BG definition";

            string phase = OpenBgStatusSnapshot.PhaseNotAvailable, attacker = null;
            int secondsLeft = 0;
            Func<OpenBgPhaseStatus> source = PhaseSource;
            if (source != null)
            {
                try
                {
                    OpenBgPhaseStatus status = source();
                    phase = string.IsNullOrWhiteSpace(status.Phase) ? OpenBgStatusSnapshot.PhaseNotAvailable : status.Phase;
                    attacker = status.Attacker;
                    secondsLeft = Math.Max(0, status.SecondsLeft);
                }
                catch (Exception error)
                {
                    phase = "unavailable (" + error.GetType().Name + ")";
                }
            }

            AbstractGameKeep keep = BattlegroundCampaignCatalog.CentralKeep(open);
            string owner = keep == null ? "no central keep registered"
                : BattlegroundKeepOwnership.Describe(keep.Name, keep.Realm, keep.Guild?.Name, keep.DBKeep?.LordDefeated == true, true);

            Region region = open == null ? null : WorldMgr.GetRegion(open.RegionId);
            OpenBgPlayerStatus[] players = region == null ? Array.Empty<OpenBgPlayerStatus>()
                : ClientService.Instance.GetPlayersOfRegion(region)
                    .Select(player => new OpenBgPlayerStatus(GlobalConstants.RealmToName(player.Realm), player.Name, player.Level))
                    .OrderBy(player => player.Realm).ThenBy(player => player.Name, StringComparer.OrdinalIgnoreCase).ToArray();

            AutonomousBattlegroundParticipation.OpenCrewMember[] crew = AutonomousBattlegroundParticipation.OpenCrew();
            var unitSizes = crew.GroupBy(member => member.Unit).ToDictionary(unit => unit.Key, unit => unit.Count());
            OpenBgBotStatus[] bots = crew.Where(member => member.Bot != null).Select(member =>
            {
                GameBot bot = member.Bot;
                string kind = OpenBattlegroundRoster.TryGetUnitKind(member.Unit, out OpenBgUnitKind known)
                    ? OpenBattlegroundRoster.KindName(known) : KindFromSize(unitSizes[member.Unit]);
                string leader = bot.Group?.Leader?.Name;
                return new OpenBgBotStatus(GlobalConstants.RealmToName(bot.Realm), bot.Name, bot.Level,
                    bot.CharacterClass?.Name ?? "unknown", member.Unit, kind, leader,
                    StateOf(member.Entered, member.Leaving, open != null && bot.CurrentRegionID == open.RegionId), bot.IsAlive);
            }).ToArray();

            return new OpenBgStatusSnapshot(DateTime.UtcNow, running, BattlegroundCampaignPolicy.IsEnabled,
                OpenBattlegroundProperties.OPEN_BG_ENABLED, ready, ready ? null : reason, phase, attacker, secondsLeft, owner,
                Math.Round(OfflineWorldSpeedControl.AchievedMultiplier, 2), OfflineWorldSpeedControl.EffectiveMultiplier,
                OpenBattlegroundProperties.MaxBots, players, bots);
        }

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            _timer?.Stop();
            _warned = false;
            _timer = new ECSGameTimer(null, PublishTick, 1000);
        }

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
        {
            _timer?.Stop();
            _timer = null;
            Write(new OpenBgStatusSnapshot(DateTime.UtcNow, false, false, false, false, "server stopped",
                OpenBgStatusSnapshot.PhaseNotAvailable, null, 0, null, 0, 0, 0, Array.Empty<OpenBgPlayerStatus>(),
                Array.Empty<OpenBgBotStatus>()));
        }

        /// <summary>Game milliseconds that last about <paramref name="wallMilliseconds"/> at this world speed.</summary>
        public static int GameIntervalFor(int wallMilliseconds, int worldSpeed) =>
            (int)Math.Min(int.MaxValue, (long)wallMilliseconds * Math.Max(1, worldSpeed));

        // The timer runs in game time; scale it so the file is written every 15 wall seconds at any world speed.
        private static int PublishTick(ECSGameTimer timer)
        {
            try
            {
                OpenBgStatusSnapshot snapshot = Capture();
                if (Interlocked.CompareExchange(ref _writing, 1, 0) == 0)
                    Task.Run(() =>
                    {
                        try { Write(snapshot); }
                        finally { Volatile.Write(ref _writing, 0); }
                    });
            }
            catch (Exception error)
            {
                if (!_warned)
                {
                    _warned = true;
                    Log.Warn("OPEN_BG_STATUS capture failed", error);
                }
            }
            return GameIntervalFor(PublishWallMilliseconds, OfflineWorldSpeedControl.EffectiveMultiplier);
        }

        private static void Write(OpenBgStatusSnapshot snapshot)
        {
            string temporaryPath = FilePath + ".tmp";
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    JsonSerializer.Serialize(stream, snapshot, Json);
                AtomicFilePublish.MoveWithRetry(temporaryPath, FilePath);
            }
            catch (Exception error)
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
                if (!_warned)
                {
                    _warned = true;
                    Log.Warn("OPEN_BG_STATUS publish failed", error);
                }
            }
        }
    }
}
