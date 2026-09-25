using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DOL.GS;

/// <summary>
/// Owns durable population work allocation.  A bot's displayed activity is not
/// authority: this record is the source of truth across reloads and is consumed
/// by both PvE and RvR matchmaking.
/// </summary>
public enum eAutonomousObjectiveKind
{
    SoloPve,
    GroupPve,
    RvR,
}

public enum eAutonomousPveCompletionMode
{
    Time,
    Kills,
}

public static class AutonomousObjectiveAssignments
{
    private const long RebalanceIntervalMilliseconds = 60_000;
    public static readonly TimeSpan MinimumRvrTenure = TimeSpan.FromMinutes(45);
    public static readonly TimeSpan MaximumRvrTenure = TimeSpan.FromMinutes(120);
    public static readonly TimeSpan MinimumPveTenure = TimeSpan.FromMinutes(45);
    public static readonly TimeSpan MaximumPveTenure = TimeSpan.FromMinutes(120);
    public static readonly TimeSpan GroupMatchmakingTimeout = TimeSpan.FromMinutes(20);
    private const string BetweenTasksPrefix = "between-pve-services-";
    public const string PveCompletionRequired = "awaiting-completed-pve-task";
    public static readonly TimeSpan MaximumBetweenTaskDuration = TimeSpan.FromMinutes(30);
    private static readonly object Sync = new();
    private static readonly ConcurrentDictionary<long, (string Assignment, long StartedTick)> GroupWaits = new();
    private static long _nextRebalanceTick;
    private static long _epoch;
    private static AutonomousLevelingDanger Danger => AutonomousBotGoalPolicy.Danger;

    public readonly record struct BetweenTaskPlan(bool Train, bool Unload);

    public static BetweenTaskPlan RollBetweenTaskPlan(bool canSpendPoints, bool backpackFull, double trainRoll, double unloadRoll) =>
        new(canSpendPoints && trainRoll >= 0 && trainRoll < 0.90,
            backpackFull && unloadRoll >= 0 && unloadRoll < 0.95);

    public static eAutonomousObjectiveKind KindFor(GameBot bot) =>
        Parse(bot?.PersistentRecord?.ObjectiveKind);

    public static eAutonomousObjectiveKind Parse(string value) =>
        Enum.TryParse(value, true, out eAutonomousObjectiveKind result) ? result : eAutonomousObjectiveKind.SoloPve;

    public static bool Is(GameBot bot, eAutonomousObjectiveKind kind) => KindFor(bot) == kind;

    public static TimeSpan RollRvrTenure(Random random = null)
    {
        random ??= Random.Shared;
        return TimeSpan.FromMinutes(random.Next((int)MinimumRvrTenure.TotalMinutes, (int)MaximumRvrTenure.TotalMinutes + 1));
    }

    public static TimeSpan RollPveTenure(Random random = null)
    {
        random ??= Random.Shared;
        return TimeSpan.FromMinutes(random.Next((int)MinimumPveTenure.TotalMinutes, (int)MaximumPveTenure.TotalMinutes + 1));
    }

    public static eAutonomousPveCompletionMode RollSoloPveCompletionMode(Random random = null)
    {
        return eAutonomousPveCompletionMode.Time;
    }

    public static bool HasActivePveAssignment(OfflineWorldBotRecord record, DateTime utcNow)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.ObjectiveAssignmentId))
            return false;
        eAutonomousObjectiveKind kind = Parse(record.ObjectiveKind);
        if (kind == eAutonomousObjectiveKind.RvR)
            return false;
        // Old persisted "Kills" records are also deadline-only. Real kill/XP
        // accounting remains in the normal server rules, never a task quota.
        return !IsBetweenPveTasks(record) &&
               DateTime.TryParse(record.ObjectiveExpiresUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime expiresUtc) &&
               expiresUtc.ToUniversalTime() > utcNow.ToUniversalTime();
    }

    public static void RecordPveKill(GameBot bot)
    {
        OfflineWorldBotRecord record = bot?.PersistentRecord;
        if (record == null || Parse(record.ObjectiveKind) == eAutonomousObjectiveKind.RvR)
            return;

        // Diagnostic count only; cannot finish or shorten a task.
        record.ObjectivePveKills++;
        bot.MarkAutonomousStateDirty();
    }

    public static bool HasActiveRvrTenure(OfflineWorldBotRecord record, DateTime utcNow)
    {
        return record != null && Parse(record.ObjectiveKind) == eAutonomousObjectiveKind.RvR &&
               DateTime.TryParse(record.ObjectiveExpiresUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime expiresUtc) &&
               expiresUtc.ToUniversalTime() > utcNow.ToUniversalTime();
    }

    public static bool NormalizeLegacyTimedAssignment(OfflineWorldBotRecord record, DateTime utcNow)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.ObjectiveAssignmentId) || IsBetweenPveTasks(record))
            return false;
        bool changed = false;
        if (Parse(record.ObjectiveKind) != eAutonomousObjectiveKind.RvR && record.ObjectivePveMode != "Time")
        {
            record.ObjectivePveMode = "Time";
            changed = true;
        }
        if (record.ObjectivePveKillTarget != 0) { record.ObjectivePveKillTarget = 0; changed = true; }
        if (!DateTime.TryParse(record.ObjectiveAssignedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime assigned))
        {
            assigned = utcNow;
            record.ObjectiveAssignedUtc = assigned.ToString("O");
            changed = true;
        }
        bool validExpiry = DateTime.TryParse(record.ObjectiveExpiresUtc, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out DateTime expiry);
        double duration = validExpiry ? (expiry.ToUniversalTime() - assigned.ToUniversalTime()).TotalMinutes : 45;
        if (!validExpiry || duration < 45 || duration > 120)
        {
            record.ObjectiveExpiresUtc = assigned.ToUniversalTime().AddMinutes(Math.Clamp(duration, 45, 120)).ToString("O");
            changed = true;
        }
        return changed;
    }

    public static bool IsRvrEligible(OfflineWorldBotRecord record, DateTime utcNow)
    {
        if (record?.ObjectiveRvrEligibleUtc == PveCompletionRequired) return false;
        return record == null || string.IsNullOrWhiteSpace(record.ObjectiveRvrEligibleUtc) ||
               !DateTime.TryParse(record.ObjectiveRvrEligibleUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime eligibleUtc) ||
               eligibleUtc.ToUniversalTime() <= utcNow.ToUniversalTime();
    }

    /// <summary>
    /// Rebalances by realm and level band. A stable allocation lives for one
    /// minute (and is persisted), avoiding per-think thrash while distributing
    /// all valid members without travel, density, or class preference.
    /// </summary>
    public static void ReconcileIfDue()
    {
        // This method is called by every active bot's group pulse. The old
        // implementation took the global lock and rebuilt the full roster
        // before checking its seven-minute timer, turning population growth
        // into O(N^2) work. The lock-free gate makes non-due calls constant
        // time; the inner gate prevents two due callers from duplicating work.
        long nowTick = GameLoop.GameLoopTime;
        if (nowTick < Interlocked.Read(ref _nextRebalanceTick))
            return;

        lock (Sync)
        {
            nowTick = GameLoop.GameLoopTime;
            if (nowTick < _nextRebalanceTick)
                return;

            DateTime utcNow = WorldSimulationClock.UtcNow;
            GameBot[] roster = AutonomousBotRegistry.Snapshot()
                .Where(bot => bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper && !bot.IsPlayerLedGroup &&
                              bot.PersistentRecord != null)
                .ToArray();
            // The existing one-minute pass is also a backstop for a service bot
            // whose own movement/maintenance branch is delayed. No new timer.
            foreach (GameBot bot in roster.Where(BetweenTaskServiceExpired))
                CompleteBetweenTaskServices(bot);
            roster = roster.Where(bot => !IsBetweenPveTasks(bot)).ToArray();
            // Group/warband deadlines belong to the coordinator, not individual
            // members' old allocation timestamps. Snapshot once per allocation pass.
            var groupOwned = roster.Where(bot => bot.Group != null &&
                !bot.Group.GetMembersInTheGroup().Any(member => member is GamePlayer)).ToHashSet();
            foreach (GameBot bot in roster.Where(bot => !groupOwned.Contains(bot)))
                if (NormalizeLegacyTimedAssignment(bot.PersistentRecord, utcNow))
                {
                    bot.MarkAutonomousStateDirty();
                    AutonomousBotStatusPersistence.Queue(bot);
                }
            GameBot[] expiredRvr = roster.Where(bot => Parse(bot.PersistentRecord.ObjectiveKind) == eAutonomousObjectiveKind.RvR &&
                                                       !string.IsNullOrWhiteSpace(bot.PersistentRecord.ObjectiveAssignmentId) &&
                                                       !groupOwned.Contains(bot) &&
                                                       !AutonomousRvrEventLayer.IsForceCommitted($"rvr-{bot.DatabaseID}", GameLoop.GameLoopTime) &&
                                                       !HasActiveRvrTenure(bot.PersistentRecord, utcNow))
                .ToArray();
            foreach (GameBot bot in expiredRvr)
            {
                // Solo roaming retains its own clock. Warbands expire together.
                BeginPveIntermission(bot);
            }
            GameBot[] completedPve = roster.Where(bot => Parse(bot.PersistentRecord.ObjectiveKind) != eAutonomousObjectiveKind.RvR &&
                                                          !IsBetweenPveTasks(bot) &&
                                                          !groupOwned.Contains(bot) &&
                                                          !string.IsNullOrWhiteSpace(bot.PersistentRecord.ObjectiveAssignmentId) &&
                                                          !HasActivePveAssignment(bot.PersistentRecord, utcNow))
                .ToArray();
            foreach (GameBot bot in completedPve)
            {
                MarkPveTaskCompleted(bot);
                if (TryBeginBetweenTaskServices(bot))
                    continue;
                bot.PersistentRecord.ObjectiveAssignmentId = string.Empty;
                bot.PersistentRecord.CurrentCampId = string.Empty;
                bot.PersistentRecord.TargetName = string.Empty;
                bot.PersistentRecord.TravelDestination = string.Empty;
                bot.MarkAutonomousStateDirty();
            }
            _epoch++;
            Interlocked.Exchange(ref _nextRebalanceTick, nowTick + RebalanceIntervalMilliseconds);
            var guildLevels = roster.GroupBy(bot => bot.PersistentRecord.GuildId ?? string.Empty)
                .ToDictionary(group => group.Key, group => group.Select(bot => (BotId: bot.DatabaseID, Level: (int)bot.Level)).ToArray(),
                    StringComparer.Ordinal);
            foreach (GameBot bot in roster.OrderBy(bot => bot.DatabaseID))
            {
                if (groupOwned.Contains(bot)) continue;
                OfflineWorldBotRecord record = bot.PersistentRecord;
                bool mayRvr = IsRvrEligible(record, utcNow);
                bool timedOut = Is(bot, eAutonomousObjectiveKind.GroupPve) && bot.Group == null &&
                    GroupMatchmakingTimedOutOnline(bot);
                if (timedOut)
                {
                    eAutonomousObjectiveKind fallback = AutonomousActivityScheduler.Choose(record, utcNow,
                        Random.Shared.NextDouble(), mayRvr, excludeGroup: true, danger: Danger);
                    DOL.Logging.LoggerManager.Create(typeof(AutonomousObjectiveAssignments)).Warn(
                        $"AUTONOMOUS_GROUP_MATCHMAKING_TIMEOUT bot=\"{bot.Name}\" id={bot.DatabaseID} " +
                        $"crew={record.GuildId} realm={bot.Realm} level={bot.Level} waitedMinutes=20 fallback={fallback}");
                    Assign(bot, fallback, CrewBucket(bot), ++_epoch);
                    record.CurrentCampId = record.TargetName = record.TravelDestination = string.Empty;
                    record.ObjectivePhase = fallback == eAutonomousObjectiveKind.GroupPve
                        ? "Group-only setting: no local pickup party yet; restarting the 20-minute queue"
                        : $"No local pickup party formed within 20 minutes; assigned {fallback}";
                    bot.MarkAutonomousStateDirty();
                    AutonomousBotStatusPersistence.Queue(bot);
                    continue;
                }
                if (HasActiveRvrTenure(record, utcNow) || HasActivePveAssignment(record, utcNow))
                    continue;

                bool undergeared = AutonomousActivityScheduler.IsUndergeared(bot.Level,
                    AutonomousPlayerBehavior.BestEquippedWeaponLevel(bot),
                    AutonomousPlayerBehavior.EquippedArmorLevels(bot));
                bool outleveled = guildLevels.TryGetValue(record.GuildId ?? string.Empty, out var peers) &&
                    AutonomousActivityScheduler.IsOutleveledByGuild(bot.Level,
                        peers.Where(peer => peer.BotId != bot.DatabaseID).Select(peer => peer.Level).ToArray());
                if (undergeared || outleveled)
                    AutonomousActivityScheduler.BeginPveBlock(record, utcNow,
                        undergeared ? "equipment behind level" : "guild roam out of level range");
                eAutonomousObjectiveKind kind = AutonomousActivityScheduler.Choose(record, utcNow,
                    Random.Shared.NextDouble(), mayRvr, guildRaid: AutonomousRealmRaid.IsReserved(bot),
                    undergeared: undergeared, outleveledByGuild: outleveled, danger: Danger);
                Assign(bot, kind, CrewBucket(bot), _epoch);
            }
        }
    }

    private static void BeginPveIntermission(GameBot bot)
    {
        OfflineWorldBotRecord record = bot.PersistentRecord;
        record.ObjectiveExpiresUtc = string.Empty;
        record.ObjectivePveMode = string.Empty;
        record.ObjectivePveKillTarget = 0;
        record.ObjectivePveKills = 0;
        record.ObjectiveRvrEligibleUtc = PveCompletionRequired;
        record.ObjectiveAssignmentId = string.Empty;
        record.ObjectivePhase = "Returning to PvE after frontier tour";
        record.CurrentCampId = string.Empty;
        record.TargetName = string.Empty;
        record.TravelDestination = string.Empty;
        record.ObjectiveProgress = "Frontier tenure complete; choosing a PvE objective";
        TryBeginBetweenTaskServices(bot);
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot);
    }

    public static void BeginSoloAfterGroupTask(GameBot bot, string reason, bool forceSoloPve = false)
    {
        if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.Group != null || bot.PersistentRecord == null)
            return;
        // Do not acquire the allocation lock from the group coordinator: the
        // allocation pass itself can remove group members. A fresh independent
        // task resets only work, never progress/items. Every level must finish
        // one PvE assignment after an RvR tour before becoming eligible again.
        bool leavingRvr = Is(bot, eAutonomousObjectiveKind.RvR);
        if (leavingRvr)
            bot.PersistentRecord.ObjectiveRvrEligibleUtc = PveCompletionRequired;
        if (!forceSoloPve && TryBeginBetweenTaskServices(bot))
            return;
        Assign(bot, forceSoloPve ? eAutonomousObjectiveKind.SoloPve :
                AutonomousActivityScheduler.Choose(bot.PersistentRecord, WorldSimulationClock.UtcNow,
                    Random.Shared.NextDouble(), mayRvr: !leavingRvr && IsRvrEligible(bot.PersistentRecord, WorldSimulationClock.UtcNow),
                    danger: Danger),
            CrewBucket(bot), GameLoop.GameLoopTime);
        bot.PersistentRecord.CurrentCampId = string.Empty;
        bot.PersistentRecord.TargetName = string.Empty;
        bot.PersistentRecord.TravelDestination = string.Empty;
        bot.PersistentRecord.ObjectivePhase = reason;
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot);
    }

    public static bool GroupMatchmakingTimedOut(OfflineWorldBotRecord record, DateTime utcNow) =>
        record != null && Parse(record.ObjectiveKind) == eAutonomousObjectiveKind.GroupPve &&
        DateTime.TryParse(record.ObjectiveAssignedUtc, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out DateTime assignedUtc) &&
        utcNow.ToUniversalTime() - assignedUtc.ToUniversalTime() >= GroupMatchmakingTimeout;

    private static bool GroupMatchmakingTimedOutOnline(GameBot bot)
    {
        long key = bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID;
        string assignment = bot.PersistentRecord?.ObjectiveAssignmentId ?? string.Empty;
        if (!GroupWaits.TryGetValue(key, out var wait) ||
            !string.Equals(wait.Assignment, assignment, StringComparison.Ordinal))
        {
            GroupWaits[key] = (assignment, GameLoop.GameLoopTime);
            return false;
        }
        return GameLoop.GameLoopTime - wait.StartedTick >= GroupMatchmakingTimeout.TotalMilliseconds;
    }

    /// <summary>
    /// Matchmaking owns an ungrouped GroupPvE bot until it assigns a compatible
    /// party or performs the twenty-minute fallback.  The generic
    /// fifteen-minute no-movement watchdog must not race that authoritative
    /// deadline and turn a valid queue wait into a capital recovery.
    /// </summary>
    public static bool IsAwaitingGroupMatchmaking(GameBot bot) =>
        bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper && bot.Group == null &&
        bot.PersistentRecord != null && Is(bot, eAutonomousObjectiveKind.GroupPve);

    public static bool IsProtectedGroupMatchmakingState(eAutonomousObjectiveKind kind, bool hasGroup) =>
        kind == eAutonomousObjectiveKind.GroupPve && !hasGroup;

    /// <summary>
    /// A capital relocation invalidates the controller's in-memory route and
    /// therefore must also receive a genuinely new durable assignment id. This
    /// changes no inventory, equipment, coin, level, or character data.
    /// </summary>
    public static void BeginSoloAfterCapitalRecovery(GameBot bot, string reason)
    {
        if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.PersistentRecord == null)
            return;
        Assign(bot, eAutonomousObjectiveKind.SoloPve,
            CrewBucket(bot),
            GameLoop.GameLoopTime + Math.Max(1, bot.PersistentRecord.RecoveryCount));
        bot.PersistentRecord.CurrentCampId = string.Empty;
        bot.PersistentRecord.TargetName = string.Empty;
        bot.PersistentRecord.TravelDestination = string.Empty;
        bot.PersistentRecord.ObjectivePhase = reason;
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot);
    }

    public static void AssignForcedRaid(GameBot bot, string eventId, bool forced = true)
    {
        if (!AutonomousRealmRaid.IsEligible(bot) || AutonomousRealmRaid.GetView(bot.Group)?.EventId != eventId) return;
        Assign(bot, eAutonomousObjectiveKind.GroupPve, (CrewBucket(bot).Crew, 50), GameLoop.GameLoopTime);
        bot.PersistentRecord.ObjectiveAssignmentId = $"{(forced ? "forced" : "automatic")}-raid-{eventId}-{bot.DatabaseID}-{GameLoop.GameLoopTime}";
        bot.PersistentRecord.CurrentCampId = bot.PersistentRecord.TargetName = bot.PersistentRecord.TravelDestination = string.Empty;
        bot.PersistentRecord.ObjectivePhase = $"{(forced ? "Forced" : "Automatic")} realm event — heading to staging";
        bot.MarkAutonomousStateDirty();
    }

    private static (string Crew, int Band) CrewBucket(GameBot bot) =>
        (bot?.PersistentRecord?.GuildId ?? string.Empty, bot?.Level >= 50 ? 50 : bot?.Level >= 20 ? 20 : 0);

    private static void Assign(GameBot bot, eAutonomousObjectiveKind kind, (string Crew, int Band) bucket, long epoch)
    {
        OfflineWorldBotRecord record = bot.PersistentRecord;
        string assignment = $"{(string.IsNullOrWhiteSpace(bucket.Crew) ? "unassigned" : bucket.Crew)}-{bucket.Band}-{epoch}-{kind}";
        if (string.Equals(record.ObjectiveKind, kind.ToString(), StringComparison.Ordinal) &&
            string.Equals(record.ObjectiveAssignmentId, assignment, StringComparison.Ordinal))
            return;

        record.ObjectiveKind = kind.ToString();
        record.ObjectiveAssignmentId = assignment;
        record.ObjectiveAssignedUtc = WorldSimulationClock.UtcNow.ToString("O");
        DateTime now = WorldSimulationClock.UtcNow;
        record.ObjectivePveKills = 0;
        record.ObjectivePveKillTarget = 0;
        if (kind == eAutonomousObjectiveKind.RvR)
        {
            record.ObjectiveExpiresUtc = now.Add(RollRvrTenure()).ToString("O");
            record.ObjectivePveMode = string.Empty;
        }
        else
        {
            // This is only the ungrouped matchmaking allocation lifetime.
            // A formed party's common clock replaces it once assembled.
            record.ObjectivePveMode = eAutonomousPveCompletionMode.Time.ToString();
            record.ObjectiveExpiresUtc = now.Add(RollPveTenure()).ToString("O");
        }
        if (kind == eAutonomousObjectiveKind.RvR)
            record.ObjectiveRvrEligibleUtc = string.Empty;
        record.ObjectivePhase = kind == eAutonomousObjectiveKind.RvR ? "Awaiting crew roam" : "Awaiting objective";
        record.ObjectiveProgress = kind switch
        {
            eAutonomousObjectiveKind.GroupPve => "Awaiting a party; the formed group will share one task timer",
            eAutonomousObjectiveKind.SoloPve => $"Solo timed grind ends {record.ObjectiveExpiresUtc}",
            _ => record.ObjectiveProgress,
        };
        long waitKey = bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID;
        if (kind == eAutonomousObjectiveKind.GroupPve)
            GroupWaits[waitKey] = (assignment, GameLoop.GameLoopTime);
        else
            GroupWaits.TryRemove(waitKey, out _);
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot);
    }

    public static bool IsBetweenPveTasks(OfflineWorldBotRecord record) =>
        record?.ObjectiveAssignmentId?.StartsWith(BetweenTasksPrefix, StringComparison.Ordinal) == true;

    public static bool IsBetweenPveTasks(GameBot bot) => IsBetweenPveTasks(bot?.PersistentRecord);

    public static bool WantsBetweenTaskTraining(GameBot bot) =>
        HasBetweenTaskFlag(bot?.PersistentRecord, 'T');

    public static bool WantsBetweenTaskInventory(GameBot bot) =>
        HasBetweenTaskFlag(bot?.PersistentRecord, 'I');

    public static bool WantsBetweenTaskDowntime(GameBot bot) =>
        HasBetweenTaskFlag(bot?.PersistentRecord, 'D');

    public static bool HasBetweenTaskFlag(OfflineWorldBotRecord record, char flag)
    {
        if (!IsBetweenPveTasks(record)) return false;
        string assignment = record.ObjectiveAssignmentId;
        int end = assignment.IndexOf('-', BetweenTasksPrefix.Length);
        return end > BetweenTasksPrefix.Length &&
            assignment.AsSpan(BetweenTasksPrefix.Length, end - BetweenTasksPrefix.Length).Contains(flag);
    }

    public static bool IsIntentionalTownIdle(OfflineWorldBotRecord record, DateTime now)
    {
        return HasBetweenTaskFlag(record, 'D') && record.ObjectivePhase == AutonomousWorldBotController.TownIdlePhase &&
            record.Activity == AutonomousWorldBotController.TownIdlePhase &&
            DateTime.TryParse(record.ObjectiveExpiresUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime end) &&
            now < end.ToUniversalTime();
    }

    public static void MarkPveTaskCompleted(GameBot bot)
    {
        if (bot?.PersistentRecord == null || Is(bot, eAutonomousObjectiveKind.RvR) || IsBetweenPveTasks(bot)) return;
        bot.PersistentRecord.ObjectiveRvrEligibleUtc = string.Empty;
        bot.MarkAutonomousStateDirty();
    }

    // Called only at a task boundary, never from loot handling. Entering this
    // durable phase commits to unloading even after the first slot is freed.
    private static bool TryBeginBetweenTaskServices(GameBot bot)
    {
        if (bot?.PersistentRecord == null || bot.Group != null)
            return false;
        BetweenTaskPlan plan = RollBetweenTaskPlan(bot.HasSpendableAutonomousTrainingPoints,
            AutonomousBotEconomy.IsBackpackFull(bot), Random.Shared.NextDouble(), Random.Shared.NextDouble());
        bool downtime = Random.Shared.NextDouble() < AutonomousPlayerBehavior.TownBreakChance(
            AutonomousPlayerBehavior.TypeOf(bot.PersistentRecord), bot.PersistentRecord.Patience);
        if (!plan.Train && !plan.Unload && !downtime) return false;
        OfflineWorldBotRecord record = bot.PersistentRecord;
        record.ObjectiveKind = eAutonomousObjectiveKind.SoloPve.ToString();
        record.ObjectiveAssignmentId = BetweenTasksPrefix + (plan.Train ? "T" : "") + (plan.Unload ? "I" : "") + (downtime ? "D" : "") +
            "-" + bot.DatabaseID + "-" + GameLoop.GameLoopTime;
        record.ObjectiveAssignedUtc = WorldSimulationClock.UtcNow.ToString("O");
        // A failed service route cannot trap the bot forever either. Existing
        // movement/progress watchdogs still operate during the trip.
        record.ObjectiveExpiresUtc = WorldSimulationClock.UtcNow.Add(MaximumBetweenTaskDuration).ToString("O");
        record.ObjectivePveMode = eAutonomousPveCompletionMode.Time.ToString();
        record.ObjectivePveKillTarget = 0;
        record.ObjectivePhase = "Between tasks";
        record.CurrentCampId = string.Empty;
        record.TargetName = string.Empty;
        record.TravelDestination = string.Empty;
        record.ObjectiveProgress = "Task complete; " + (plan.Train ? "train earned specialization points; " : "") +
            (plan.Unload ? "unload backpack; " : "") + (downtime ? "15-30 minute town break if reachable within the maintenance budget; " : "") + "then choose the next task";
        AutonomousBotEconomy.MarkInventoryChanged(bot);
        bot.MarkAutonomousStateDirty();
        AutonomousBotStatusPersistence.Queue(bot);
        return true;
    }

    public static bool BetweenTaskServiceExpired(GameBot bot) => IsBetweenPveTasks(bot) &&
        (!DateTime.TryParse(bot.PersistentRecord.ObjectiveExpiresUtc, null,
             System.Globalization.DateTimeStyles.RoundtripKind, out DateTime due) || due.ToUniversalTime() <= WorldSimulationClock.UtcNow);

    public static void CompleteBetweenTaskServices(GameBot bot)
    {
        if (!IsBetweenPveTasks(bot)) return;
        if (BetweenTaskServiceExpired(bot))
            DOL.Logging.LoggerManager.Create(typeof(AutonomousObjectiveAssignments)).Warn(
                $"AUTONOMOUS_BETWEEN_TASK_TIMEOUT bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level} " +
                $"region={bot.CurrentRegionID} x={bot.X} y={bot.Y} z={bot.Z} " +
                $"goal=\"{bot.PersistentRecord.CurrentGoal}\" activity=\"{bot.PersistentRecord.Activity}\" " +
                $"assignment=\"{bot.PersistentRecord.ObjectiveAssignmentId}\"");
        bool pveRequired = bot.PersistentRecord.ObjectiveRvrEligibleUtc == PveCompletionRequired;
        Assign(bot, AutonomousActivityScheduler.Choose(bot.PersistentRecord, WorldSimulationClock.UtcNow,
                Random.Shared.NextDouble(), mayRvr: !pveRequired && IsRvrEligible(bot.PersistentRecord, WorldSimulationClock.UtcNow),
                danger: Danger),
            CrewBucket(bot), GameLoop.GameLoopTime);
    }
}
