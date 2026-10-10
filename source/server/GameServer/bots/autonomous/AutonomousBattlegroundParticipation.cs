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
    public const int AssignmentTimeoutMilliseconds = 15 * 60_000;
    public const int DepartureLimit = 8;
    public const string BattlegroundMedallionId = "battlegrounds_necklace";
    private const int DirectorMaximum = 24;
    private const int MerchantReach = 3000;
    private const int ProgressDistance = 96;
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousBattlegroundParticipation));
    private static readonly object Sync = new();
    private static readonly Dictionary<GameBot, Entry> Entries = new(ReferenceEqualityComparer.Instance);
    private static ECSGameTimer _timer;

    public enum LeaveReason { Graduated, TourEnded, Unassigned, Stuck }

    private sealed class Entry
    {
        public GameBot Bot;
        public BattlegroundDefinition Definition;
        public string Group = "solo";
        public bool Entered;
        public LeaveReason? Leaving;
        public long AssignedTick;
        public long LastProgressTick;
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
        _ => "unassigned",
    };

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

    /// <summary>A real merchant within reach of the porter sells the medallion (the free source players use).</summary>
    public static bool SellsMedallion(OFTeleporter porter) =>
        porter != null && porter.GetNPCsInRadius(MerchantReach).OfType<GameMerchant>().Any(npc =>
            npc.TradeItems?.GetAllItems().Values.OfType<DbItemTemplate>().Any(item => item.Id_nb == BattlegroundMedallionId) == true);

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
            if (bot != null && Entries.TryGetValue(bot, out Entry entry)) entry.LastProgressTick = GameLoop.GameLoopTime;
        AutonomousStuckWatchdog.MarkProgress(bot, kind);
    }

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
            if (!BattlegroundCampaignPolicy.TryEnterBot(bot, out string reason))
            {
                Log.Debug($"AUTONOMOUS_BG_ENTRY_REFUSED region={porter.CurrentRegionID} bot=\"{bot.Name}\" reason=\"{reason}\"");
                continue;
            }
            ushort region = porter.CurrentRegionID;
            lock (Sync)
                if (Entries.TryGetValue(bot, out Entry entry))
                {
                    entry.Entered = true;
                    entry.LastProgressTick = GameLoop.GameLoopTime;
                }
            Log.Info($"AUTONOMOUS_BG_ENTERED region={region} bot=\"{bot.Name}\"");
        }
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
                if (entry.Entered && reason == null && now - entry.LastProgressTick > StuckMilliseconds)
                    reason = LeaveReason.Stuck;
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
                lock (Sync) Entries[bot] = new Entry { Bot = bot, Definition = inside, Entered = true, Leaving = reason ?? LeaveReason.Unassigned, AssignedTick = now, LastProgressTick = now };
                TryFinishLeaving(bot);
                continue;
            }
            lock (Sync) Entries[bot] = new Entry { Bot = bot, Definition = inside, Entered = true, AssignedTick = now, LastProgressTick = now };
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
                    Entries[member] = new Entry { Bot = member, Definition = definition, Group = label, AssignedTick = now, LastProgressTick = now };
            used[(ushort)definition.RegionId] = present + members.Length;
            foreach (GameBot member in members)
                Log.Info($"AUTONOMOUS_BG_ASSIGNED region={definition.RegionId} bot=\"{member.Name}\" level={member.Level} group=\"{label}\"");
        }
    }
}
