using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.Events;
using DOL.GS.Scripts;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Bounded autonomous-gamebot population for the battleground campaign.
/// Only RvR-tour bots standing on a frontier (regions 1, 100, 200) and fitting a
/// bracket are assigned to it. Assigned bots buy the free battlegrounds medallion
/// from a real merchant, board a real porter, then fight the keep through
/// <see cref="AutonomousBattlegroundDriver"/>. Players, companions and temporary
/// helpers are never participants. Assignments live in memory only.
/// </summary>
public static class AutonomousBattlegroundParticipation
{
    public const int MaximumAutonomousPerMap = 24;
    public const int SharedActorCap = 40;
    public const int ReconcileIntervalMilliseconds = 60_000;
    public const int StuckMilliseconds = 5 * 60_000;
    /// <summary>Wall-clock floor for the stuck and no-turn rules, so a fast world speed cannot eject bots in seconds.</summary>
    public const long StuckWallMilliseconds = 90_000;
    public const int AssignmentTimeoutMilliseconds = 15 * 60_000;
    public const int DepartureLimit = 8;
    public const string BattlegroundMedallionId = "battlegrounds_necklace";
    private const int DirectorMaximum = 24;
    public const int MerchantReach = 3000;
    private const int ProgressDistance = 96;
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousBattlegroundParticipation));
    private static readonly object Sync = new();
    private static readonly Dictionary<GameBot, Entry> Entries = new(ReferenceEqualityComparer.Instance);
    private static ECSGameTimer _timer;

    public enum LeaveReason { Graduated, TourEnded, Unassigned, Stuck, NoTurn }

    private sealed class Entry
    {
        public GameBot Bot;
        public BattlegroundDefinition Definition;
        public string Group = "solo";
        public bool Entered;
        public LeaveReason? Leaving;
        public long AssignedTick;
        // Game-loop and wall-clock (Environment.TickCount64) stamps of the last real progress.
        public long LastProgressTick;
        public long LastProgressWall;
        // Driver turns taken inside the battleground; the stuck rule applies only after one.
        public int DriverTurns;
        public HashSet<string> IdleReasons;
    }

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        lock (Sync)
        {
            _timer?.Stop();
            _timer = null;
            Entries.Clear();
        }
        if (!BattlegroundCampaignPolicy.IsEnabled) return;
        _timer = new ECSGameTimer(null, Reconcile, ReconcileIntervalMilliseconds);
    }

    [GameServerStoppedEvent]
    public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
    {
        lock (Sync)
        {
            _timer?.Stop();
            _timer = null;
            Entries.Clear();
        }
    }

    public static string Describe(LeaveReason reason) => reason switch
    {
        LeaveReason.Graduated => "graduated",
        LeaveReason.TourEnded => "tour_ended",
        LeaveReason.Stuck => "stuck",
        LeaveReason.NoTurn => "no_turn",
        _ => "unassigned",
    };

    /// <summary>
    /// A participant that has taken driver turns and has recorded no real progress for both five game
    /// minutes and ninety wall-clock seconds. Without a turn it is not stuck; see <see cref="IsNoTurn"/>.
    /// </summary>
    public static bool IsStuck(long gameNow, long gameLast, long wallNow, long wallLast, bool hadTurn) =>
        hadTurn && SilentForBothClocks(gameNow, gameLast, wallNow, wallLast);

    /// <summary>A participant that never took a driver turn within the same two-clock window.</summary>
    public static bool IsNoTurn(long gameNow, long gameLast, long wallNow, long wallLast, bool hadTurn) =>
        !hadTurn && SilentForBothClocks(gameNow, gameLast, wallNow, wallLast);

    private static bool SilentForBothClocks(long gameNow, long gameLast, long wallNow, long wallLast) =>
        gameNow - gameLast > StuckMilliseconds && wallNow - wallLast >= StuckWallMilliseconds;

    /// <summary>Whether the last real progress is still inside the stuck wall-clock window (the complement of its wall test).</summary>
    public static bool IsRecentProgress(long wallNow, long wallLast) => wallNow - wallLast < StuckWallMilliseconds;

    /// <summary>Square-distance reach test in the 2D plane, used for merchants around a porter.</summary>
    public static bool IsWithinReach2D(int dx, int dy, int reach) =>
        (long)dx * dx + (long)dy * dy <= (long)reach * reach;

    public static bool IsFrontierRegion(ushort region) => region is 1 or 100 or 200;

    private static bool IsEligible(byte level, int realmLevel, bool rvrTour, bool playerLed, bool relic, BattlegroundDefinition definition) =>
        definition != null && rvrTour && !playerLed && !relic &&
        level >= definition.MinLevel && level <= definition.MaxLevel &&
        (definition.MaxRealmLevel == 0 || realmLevel < definition.MaxRealmLevel);

    /// <summary>True only for an RvR-tour bot on a frontier that fits the bracket.</summary>
    public static bool IsCandidate(byte level, int realmLevel, bool rvrTour, ushort region, bool playerLed, bool relic, BattlegroundDefinition d) =>
        IsFrontierRegion(region) && IsEligible(level, realmLevel, rvrTour, playerLed, relic, d);

    /// <summary>
    /// Why an assigned or present bot must leave its bracket, or null to keep it.
    /// A character above the bracket or at its Realm Rank ceiling graduates.
    /// </summary>
    public static LeaveReason? LeaveReasonFor(byte level, int realmLevel, bool rvrTour, bool playerLed, bool relic, BattlegroundDefinition d)
    {
        if (d == null) return LeaveReason.Unassigned;
        if (level > d.MaxLevel || d.MaxRealmLevel != 0 && realmLevel >= d.MaxRealmLevel) return LeaveReason.Graduated;
        if (!rvrTour) return LeaveReason.TourEnded;
        return IsEligible(level, realmLevel, rvrTour, playerLed, relic, d) ? null : LeaveReason.Unassigned;
    }

    /// <summary>A group is admitted only when every member is eligible and the map has room for all of them.</summary>
    public static bool AdmitsGroup(int memberCount, int eligibleCount, int assignedOnMap) =>
        memberCount > 0 && eligibleCount == memberCount && assignedOnMap + memberCount <= MaximumAutonomousPerMap;

    /// <summary>Director actors allowed beside the autonomous participants already present in the region.</summary>
    public static int DirectorCap(int autonomousPresent) => Math.Clamp(SharedActorCap - autonomousPresent, 0, DirectorMaximum);

    public static bool HasBattlegroundMedallion(GameBot bot) =>
        bot?.Inventory?.GetItem(eInventorySlot.Mythical)?.Id_nb == BattlegroundMedallionId;

    /// <summary>
    /// Real merchants that sell the medallion within <see cref="MerchantReach"/> (2D) of the porter, nearest first.
    /// Scans the region's object list rather than the sub-zone radius index: that index is empty or stale
    /// until objects have been relocated, so a startup check missed a merchant standing 500 units away.
    /// </summary>
    public static GameMerchant[] MedallionMerchantsInReach(OFTeleporter porter)
    {
        if (porter == null) return Array.Empty<GameMerchant>();
        return WorldMgr.GetNPCsFromRegion(porter.CurrentRegionID).OfType<GameMerchant>()
            .Where(npc => npc.ObjectState == GameObject.eObjectState.Active &&
                IsWithinReach2D(npc.X - porter.X, npc.Y - porter.Y, MerchantReach) && SellsMedallionItem(npc))
            .OrderBy(npc => (long)(npc.X - porter.X) * (npc.X - porter.X) + (long)(npc.Y - porter.Y) * (npc.Y - porter.Y))
            .ToArray();
    }

    private static bool SellsMedallionItem(GameMerchant merchant) =>
        merchant.TradeItems?.GetAllItems().Values.OfType<DbItemTemplate>().Any(item => item.Id_nb == BattlegroundMedallionId) == true;

    /// <summary>A real merchant within reach of the porter sells the medallion (the free source players use).</summary>
    public static bool SellsMedallion(OFTeleporter porter) => MedallionMerchantsInReach(porter).Length > 0;

    /// <summary>Active porters in the region whose medallion merchant is in reach. Empty where no legal source exists.</summary>
    public static OFTeleporter[] SourcePorters(ushort region) =>
        AutonomousFrontierTransport.PortersInRegion(region).Where(SellsMedallion).ToArray();

    /// <summary>A bot admitted to the campaign and standing inside its battleground.</summary>
    public static bool IsParticipant(GameNPC npc)
    {
        if (npc is not GameBot { IsAutonomousWorldBot: true } bot) return false;
        // Cheap region test first: this runs on every movement step of every autonomous bot.
        if (BattlegroundCampaignCatalog.Find(bot.CurrentRegionID) == null) return false;
        lock (Sync)
            return Entries.TryGetValue(bot, out Entry entry) && entry.Entered;
    }

    /// <summary>Assigned to a bracket and travelling to its porter, not yet inside.</summary>
    public static bool IsAssigned(GameBot bot)
    {
        lock (Sync)
            return bot != null && Entries.TryGetValue(bot, out Entry entry) && !entry.Entered && entry.Leaving == null;
    }

    public static bool IsLeaving(GameBot bot)
    {
        lock (Sync)
            return bot != null && Entries.TryGetValue(bot, out Entry entry) && entry.Leaving != null;
    }

    /// <summary>A participant still inside the battleground whose last real progress is recent.</summary>
    public static bool IsProgressing(GameBot bot)
    {
        lock (Sync)
            return bot != null && Entries.TryGetValue(bot, out Entry entry) && entry.Entered && entry.Leaving == null &&
                IsRecentProgress(Environment.TickCount64, entry.LastProgressWall);
    }

    /// <summary>Autonomous participants inside a campaign region, for the director cap.</summary>
    public static int PresentCount(ushort region)
    {
        lock (Sync)
            return Entries.Values.Count(entry => entry.Entered && entry.Bot.CurrentRegionID == region);
    }

    /// <summary>Records real progress for the participant's own stuck clock and the existing watchdog.</summary>
    public static void NoteProgress(GameBot bot, eAutonomousProgressKind kind)
    {
        lock (Sync)
            if (bot != null && Entries.TryGetValue(bot, out Entry entry))
            {
                Touch(entry, GameLoop.GameLoopTime);
                entry.Leaving = AfterProgress(entry.Leaving);
            }
        AutonomousStuckWatchdog.MarkProgress(bot, kind);
    }

    /// <summary>
    /// The leave mark after real progress: a stuck mark that could not be carried out (the bot was in combat)
    /// is withdrawn once the bot acts again; every other leave reason stands.
    /// </summary>
    public static LeaveReason? AfterProgress(LeaveReason? leaving) =>
        leaving == LeaveReason.Stuck ? null : leaving;

    /// <summary>
    /// Departure from a porter's cast: up to <see cref="DepartureLimit"/> waiting
    /// participants, leaders first, that stand inside its boarding radius and hold
    /// the real medallion. Each goes through the same admission as players.
    /// </summary>
    public static void Depart(OFTeleporter porter)
    {
        if (porter == null || !BattlegroundCampaignPolicy.IsEnabled) return;
        GameBot[] waiting;
        lock (Sync)
            waiting = Entries.Values
                .Where(entry => !entry.Entered && entry.Leaving == null)
                .Select(entry => entry.Bot)
                .ToArray();
        GameBot[] boarders = waiting
            .Where(bot => bot.ObjectState == GameObject.eObjectState.Active && bot.CurrentRegion == porter.CurrentRegion &&
                bot.IsWithinRadius(porter, AutonomousFrontierTransport.BoardingRadius))
            .OrderBy(bot => bot.Group?.LivingLeader == bot ? 0 : 1)
            .ThenBy(bot => bot.DatabaseID)
            .Take(DepartureLimit)
            .ToArray();
        foreach (GameBot bot in boarders)
        {
            if (!HasBattlegroundMedallion(bot)) continue;
            if (!BattlegroundCampaignPolicy.TryEnterBot(bot, out string reason, out ushort destination))
            {
                Log.Debug($"AUTONOMOUS_BG_ENTRY_REFUSED region={porter.CurrentRegionID} bot=\"{bot.Name}\" reason=\"{reason}\"");
                continue;
            }
            ushort from = porter.CurrentRegionID;
            // A new visit starts with a fresh driver state, so its first turn records progress again.
            AutonomousBattlegroundDriver.ForgetState(bot);
            lock (Sync)
                if (Entries.TryGetValue(bot, out Entry entry))
                {
                    entry.Entered = true;
                    Touch(entry, GameLoop.GameLoopTime);
                }
            Log.Info($"AUTONOMOUS_BG_ENTERED region={destination} from_region={from} bot=\"{bot.Name}\"");
        }
    }

    /// <summary>Records one driver turn. The first turn of an entry is logged with the action it chose.</summary>
    public static void RecordDriverTurn(GameBot bot, ushort region, string action)
    {
        bool first = false;
        lock (Sync)
            if (bot != null && Entries.TryGetValue(bot, out Entry entry) && entry.Entered)
                first = entry.DriverTurns++ == 0;
        if (first)
            Log.Info($"AUTONOMOUS_BG_DRIVER_FIRST_TURN region={region} bot=\"{bot.Name}\" action={action}");
    }

    /// <summary>Why the driver took no action this turn; logged at most once per entry and reason.</summary>
    public static void RecordDriverIdle(GameBot bot, ushort region, string reason)
    {
        bool log = false;
        lock (Sync)
            if (bot != null && Entries.TryGetValue(bot, out Entry entry))
            {
                entry.IdleReasons ??= new HashSet<string>(StringComparer.Ordinal);
                log = entry.IdleReasons.Add(reason);
            }
        if (log)
            Log.Info($"AUTONOMOUS_BG_DRIVER_IDLE region={region} bot=\"{bot.Name}\" reason={reason}");
    }

    /// <summary>Why the driver could not prove a route to a goal. The driver logs each goal once per visit.</summary>
    public static void RecordRouteFailure(GameBot bot, ushort region, string action, string goal, string from, string reason) =>
        Log.Info($"AUTONOMOUS_BG_ROUTE_FAILED region={region} bot=\"{bot.Name}\" action={action} goal={goal} from={from} reason={reason}");

    // Callers hold Sync. Real progress restarts both stuck clocks.
    private static void Touch(Entry entry, long gameNow)
    {
        entry.LastProgressTick = gameNow;
        entry.LastProgressWall = Environment.TickCount64;
    }

    /// <summary>
    /// Leaves the battleground (bind-equivalent exit, out of combat only). Returns
    /// true once the bot is outside and its participation has ended.
    /// </summary>
    public static bool TryFinishLeaving(GameBot bot)
    {
        LeaveReason reason;
        BattlegroundDefinition definition;
        lock (Sync)
        {
            if (bot == null || !Entries.TryGetValue(bot, out Entry entry) || entry.Leaving == null) return false;
            reason = entry.Leaving.Value;
            definition = entry.Definition;
        }
        if (!BattlegroundCampaignPolicy.TryExitBot(bot, out _)) return false;
        lock (Sync) Entries.Remove(bot);
        Log.Info($"AUTONOMOUS_BG_LEFT region={definition.RegionId} bot=\"{bot.Name}\" reason={Describe(reason)}");
        return true;
    }

    /// <summary>Battleground death: the participant releases at its campaign arrival camp.</summary>
    public static bool TryBattlegroundRelease(GameBot bot, ushort deathRegion, out GameLocation landing)
    {
        landing = null;
        BattlegroundDefinition definition = BattlegroundCampaignCatalog.Find(deathRegion);
        if (definition == null || bot == null || bot.CurrentRegionID != deathRegion || !IsParticipant(bot)) return false;
        landing = BattlegroundCampaignPolicy.GetLanding(bot, definition);
        return landing != null && landing.RegionID == deathRegion;
    }

    /// <summary>
    /// Whether a release starts the walk back to the party or death spot. Not at a battleground landing: the
    /// driver leads the participant from there, and a second movement owner pinned it at the portal-keep wall
    /// until the stuck rule ejected it (bug 127).
    /// </summary>
    public static bool StartsReleaseReturnWalk(bool returnToParty, bool battlegroundLanding) =>
        returnToParty && !battlegroundLanding;

    private static int Reconcile(ECSGameTimer timer)
    {
        try
        {
            ReconcileNow(GameLoop.GameLoopTime);
        }
        catch (Exception exception)
        {
            Log.Error("AUTONOMOUS_BG_RECONCILE_FAILED", exception);
        }
        return ReconcileIntervalMilliseconds;
    }

    private static void ReconcileNow(long now)
    {
        bool enabled = BattlegroundCampaignPolicy.IsEnabled;
        GameBot[] roster = AutonomousBotRegistry.Snapshot()
            .Where(bot => bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper && !bot.IsPersistentPlayerCompanion)
            .OrderBy(bot => bot.DatabaseID)
            .ToArray();
        List<Entry> snapshot;
        lock (Sync) snapshot = Entries.Values.ToList();

        foreach (Entry entry in snapshot)
        {
            GameBot bot = entry.Bot;
            if (!enabled || bot.ObjectState != GameObject.eObjectState.Active)
            {
                Remove(entry, LeaveReason.Unassigned);
                continue;
            }
            if (entry.Leaving == null)
            {
                LeaveReason? reason = LeaveReasonFor(bot.Level, bot.RealmLevel,
                    AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR), bot.IsPlayerLedGroup,
                    GameRelic.IsPlayerCarryingRelic(bot), entry.Definition);
                if (!entry.Entered && now - entry.AssignedTick > AssignmentTimeoutMilliseconds)
                    reason = LeaveReason.Unassigned;
                if (entry.Entered && bot.CurrentRegionID != entry.Definition.RegionId)
                    reason = LeaveReason.Unassigned;
                long wall = Environment.TickCount64;
                bool hadTurn;
                lock (Sync) hadTurn = entry.DriverTurns > 0;
                if (entry.Entered && reason == null && IsStuck(now, entry.LastProgressTick, wall, entry.LastProgressWall, hadTurn))
                    reason = LeaveReason.Stuck;
                if (entry.Entered && reason == null && IsNoTurn(now, entry.LastProgressTick, wall, entry.LastProgressWall, hadTurn))
                {
                    reason = LeaveReason.NoTurn;
                    LogNoTurn(bot, entry.Definition.RegionId);
                }
                if (reason != null && !entry.Entered)
                {
                    Remove(entry, reason.Value);
                    continue;
                }
                if (reason != null && bot.CurrentRegionID != entry.Definition.RegionId)
                {
                    Remove(entry, reason.Value);
                    continue;
                }
                if (reason != null)
                    lock (Sync) entry.Leaving = reason;
            }
            if (entry.Leaving != null) TryFinishLeaving(bot);
        }

        // Bots already standing in a bracket map (after a restart) are re-registered
        // when they still qualify, otherwise sent out.
        foreach (GameBot bot in roster)
        {
            if (bot.ObjectState != GameObject.eObjectState.Active || IsRegistered(bot)) continue;
            BattlegroundDefinition inside = BattlegroundCampaignCatalog.Find(bot.CurrentRegionID);
            if (inside == null) continue;
            LeaveReason? reason = LeaveReasonFor(bot.Level, bot.RealmLevel,
                AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR), bot.IsPlayerLedGroup,
                GameRelic.IsPlayerCarryingRelic(bot), inside);
            if (!enabled || reason != null)
            {
                lock (Sync) Entries[bot] = new Entry { Bot = bot, Definition = inside, Entered = true, Leaving = reason ?? LeaveReason.Unassigned, AssignedTick = now, LastProgressTick = now, LastProgressWall = Environment.TickCount64 };
                TryFinishLeaving(bot);
                continue;
            }
            lock (Sync) Entries[bot] = new Entry { Bot = bot, Definition = inside, Entered = true, AssignedTick = now, LastProgressTick = now, LastProgressWall = Environment.TickCount64 };
            Log.Info($"AUTONOMOUS_BG_ENTERED region={inside.RegionId} bot=\"{bot.Name}\" reregistered=true");
        }

        if (enabled) Admit(roster, now);
    }

    /// <summary>Per pass: whether a region has a porter with a medallion merchant in reach.</summary>
    private static bool Sourced(ushort region, Dictionary<ushort, bool> cache)
    {
        if (!cache.TryGetValue(region, out bool sourced))
            cache[region] = sourced = SourcePorters(region).Length > 0;
        return sourced;
    }

    private static bool IsRegistered(GameBot bot)
    {
        lock (Sync) return Entries.ContainsKey(bot);
    }

    /// <summary>
    /// Once per no-turn exit: whether the brain is still scheduled, when it next thinks, and the body's
    /// life and crowd-control state. Separates "never scheduled" from "scheduled but returning idle".
    /// </summary>
    private static void LogNoTurn(GameBot bot, int regionId)
    {
        var brain = bot.Brain;
        bool scheduled = brain != null && brain.ServiceObjectId.IsSet;
        long untilThink = brain == null ? 0 : brain.NextThinkTick - GameLoop.GameLoopTime;
        string idle;
        lock (Sync)
            idle = Entries.TryGetValue(bot, out Entry entry) && entry.IdleReasons != null ? string.Join(",", entry.IdleReasons) : "none";
        Log.Info($"AUTONOMOUS_BG_NO_TURN_DIAGNOSTIC region={regionId} bot=\"{bot.Name}\" brain_scheduled={scheduled} " +
            $"next_think_ms={untilThink} alive={bot.IsAlive} crowd_controlled={bot.IsCrowdControlled} health={bot.Health} " +
            $"object_state={bot.ObjectState} idle_reasons={idle}");
    }

    private static void Remove(Entry entry, LeaveReason reason)
    {
        lock (Sync) Entries.Remove(entry.Bot);
        Log.Info($"AUTONOMOUS_BG_LEFT region={entry.Definition.RegionId} bot=\"{entry.Bot.Name}\" reason={Describe(reason)}");
    }

    private static void Admit(GameBot[] roster, long now)
    {
        Dictionary<ushort, int> used = new();
        lock (Sync)
            foreach (Entry entry in Entries.Values)
            {
                ushort region = (ushort)entry.Definition.RegionId;
                used[region] = used.GetValueOrDefault(region) + 1;
            }
        var handled = new HashSet<GameBot>(ReferenceEqualityComparer.Instance);
        var sourced = new Dictionary<ushort, bool>();
        foreach (GameBot bot in roster)
        {
            if (handled.Contains(bot) || IsRegistered(bot) || !IsFrontierRegion(bot.CurrentRegionID)) continue;
            GameLiving[] raw = bot.Group == null ? new GameLiving[] { bot } : bot.Group.GetMembersInTheGroup().ToArray();
            foreach (GameLiving member in raw) handled.Add(member as GameBot);
            // A group with a player, companion or helper member is never assigned whole.
            if (raw.Any(member => member is not GameBot { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false })) continue;
            GameBot[] members = raw.Cast<GameBot>().ToArray();
            GameBot anchor = bot.Group?.LivingLeader as GameBot ?? bot;
            BattlegroundDefinition definition = BattlegroundCampaignCatalog.ForLevel(anchor.Level);
            if (definition == null) continue;
            // A member may only be admitted where it can buy the medallion from a real merchant.
            int eligible = members.Count(member => member.IsAutonomousWorldBot && !member.IsTemporaryGroupHelper &&
                member.ObjectState == GameObject.eObjectState.Active && !IsRegistered(member) &&
                IsCandidate(member.Level, member.RealmLevel, AutonomousObjectiveAssignments.Is(member, eAutonomousObjectiveKind.RvR),
                    member.CurrentRegionID, member.IsPlayerLedGroup, GameRelic.IsPlayerCarryingRelic(member), definition) &&
                Sourced(member.CurrentRegionID, sourced));
            int present = used.GetValueOrDefault((ushort)definition.RegionId);
            if (!AdmitsGroup(members.Length, eligible, present)) continue;
            string label = bot.Group == null ? "solo" : anchor.Name;
            lock (Sync)
                foreach (GameBot member in members)
                    Entries[member] = new Entry { Bot = member, Definition = definition, Group = label, AssignedTick = now, LastProgressTick = now, LastProgressWall = Environment.TickCount64 };
            used[(ushort)definition.RegionId] = present + members.Length;
            foreach (GameBot member in members)
                Log.Info($"AUTONOMOUS_BG_ASSIGNED region={definition.RegionId} bot=\"{member.Name}\" level={member.Level} group=\"{label}\"");
        }
    }
}
