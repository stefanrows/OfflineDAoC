using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using DOL.Database;
using DOL.Logging;
using OfflineDaoc.Configuration;

namespace DOL.GS;

/// <summary>Applies launcher population mix requests to the saved autonomous roster.</summary>
public static class AutonomousBotTypeMixControl
{
    public const string RequestFileName = "population-mix.request.json";
    public const string StatusFileName = "population-mix.status.json";
    private const int MaximumOfflineChangesPerPoll = 48;
    private const int MaximumActiveChangesPerSecond = 8;
    private const int MaximumRequestBytes = 16_384;
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousBotTypeMixControl));
    private static readonly object Gate = new();
    private static readonly ConcurrentDictionary<long, int> PendingTargets = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };
    private static string _requestPath = string.Empty;
    private static string _statusPath = string.Empty;
    private static string _settingsPath = string.Empty;
    private static string _lastRequestId = string.Empty;
    private static MixRequest? _request;
    private static string _error = string.Empty;
    private static DateTime _nextRequestReadUtc;
    private static long _activeApplySecond = -1;
    private static int _activeChangesThisSecond;
    private static bool _running;

    private sealed record MixRequest(string SessionId, string RequestId, DateTime CreatedUtc, PlayerTypeMix Mix);

    public sealed record MixStatus
    {
        public string SessionId { get; init; } = string.Empty;
        public string RequestId { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public DateTime UpdatedUtc { get; init; }
        public PlayerTypeMix Mix { get; init; } = new(25, 10, 30, 15, 12, 8);
        public int RosterCount { get; init; }
        public int[] CurrentCounts { get; init; } = new int[6];
        public int[] TargetCounts { get; init; } = new int[6];
        public int ActivePending { get; init; }
        public string Error { get; init; } = string.Empty;
    }

    private sealed record RosterEntry(OfflineWorldBotRecord Record, GameBot? LiveBot, bool SpawnPending)
    {
        public int CurrentType => TypeIndex(Record.PlayerType);
        public bool IsEditableOffline => LiveBot == null && !SpawnPending;
    }

    private sealed record PlannedChange(RosterEntry Entry, int TargetType);

    public static void Start()
    {
        lock (Gate)
        {
            _requestPath = Path.Combine(AppContext.BaseDirectory, RequestFileName);
            _statusPath = Path.Combine(AppContext.BaseDirectory, StatusFileName);
            _settingsPath = Path.Combine(AutonomousBotGoalPolicy.ServerDirectory, BotGoalSettings.FileName);
            _lastRequestId = string.Empty;
            _request = null;
            _error = string.Empty;
            _nextRequestReadUtc = DateTime.MinValue;
            _activeApplySecond = -1;
            _activeChangesThisSecond = 0;
            _running = true;
            PendingTargets.Clear();
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            _running = false;
            PendingTargets.Clear();
        }
    }

    /// <summary>Runs from the existing autonomous population poll, outside bot think turns.</summary>
    public static void Poll(IReadOnlyList<OfflineWorldBotRecord> cachedRoster, Func<long, bool> isSpawnPending)
    {
        lock (Gate)
            if (!_running) return;

        DateTime nowUtc = DateTime.UtcNow;
        if (nowUtc >= _nextRequestReadUtc)
        {
            _nextRequestReadUtc = nowUtc.AddSeconds(1);
            TryReadRequest(nowUtc);
        }

        MixRequest request;
        lock (Gate)
        {
            if (!_running || _request == null) return;
            request = _request;
        }

        RosterEntry[] roster = BuildRoster(cachedRoster, isSpawnPending);

        try
        {
            BotGoalSettings settings = BotGoalSettings.Load(_settingsPath);
            if (settings.Mix != request.Mix || settings.Preset != PopulationPreset.Custom)
                settings = BotGoalSettings.SaveMixForLiveApply(_settingsPath, request.Mix);
            AutonomousBotGoalPolicy.ApplyLiveSettings(settings);
            if (_error.StartsWith("Could not save the requested mix", StringComparison.Ordinal))
                _error = string.Empty;
        }
        catch (Exception exception)
        {
            _error = "Could not save the requested mix; it will retry: " + exception.Message;
            int[] currentCounts = CountTypes(roster);
            int[] targetCounts = TargetCounts(request.Mix, roster.Length);
            int activePending = Plan(roster, targetCounts).Count(change => change.Entry.LiveBot != null);
            PublishStatus("pending", roster, currentCounts, targetCounts, activePending, _error);
            return;
        }

        int[] targets = TargetCounts(request.Mix, roster.Length);
        List<PlannedChange> planned = Plan(roster, targets);
        PlannedChange[] offline = planned.Where(change => change.Entry.IsEditableOffline)
            .Take(MaximumOfflineChangesPerPoll).ToArray();
        string error = string.Empty;
        if (offline.Length > 0)
        {
            if (!TrySaveOfflineChanges(offline, out error))
                _error = error;
            else
                _error = string.Empty;
        }
        else if (_error.StartsWith("Could not save an offline bot type", StringComparison.Ordinal))
            _error = string.Empty;

        lock (Gate)
        {
            if (!_running || _request?.RequestId != request.RequestId) return;
            PendingTargets.Clear();
            foreach (PlannedChange change in planned.Where(change => change.Entry.LiveBot != null))
                PendingTargets[change.Entry.Record.BotId] = change.TargetType;
        }

        RosterEntry[] updatedRoster = RefreshLiveRecords(roster);
        int[] current = CountTypes(updatedRoster);
        bool converged = current.SequenceEqual(targets) && PendingTargets.IsEmpty;
        PublishStatus(converged ? "applied" : "pending", updatedRoster, current, targets,
            PendingTargets.Count, _error);
    }

    /// <summary>Applies a staged type change before a new solo task is selected.</summary>
    public static bool ApplyAtTaskBoundary(GameBot bot, bool taskJustCompleted = false)
    {
        if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.IsPlayerLedGroup ||
            bot.PersistentRecord == null || bot.Group != null || bot.ObjectState != GameObject.eObjectState.Active ||
            !bot.IsAlive || bot.InCombat || bot.IsAttacking || bot.IsCasting || bot.IsReturningAfterRelease ||
            bot.IsOnStableMasterRoute)
            return false;

        OfflineWorldBotRecord record = bot.PersistentRecord;
        if (!taskJustCompleted && !string.IsNullOrWhiteSpace(record.ObjectiveAssignmentId) &&
            !AutonomousObjectiveAssignments.IsBetweenPveTasks(record))
            return false;

        lock (Gate)
        {
            if (!_running || _request == null || !PendingTargets.TryGetValue(bot.DatabaseID, out int target))
                return false;
            if (TypeIndex(record.PlayerType) == target)
            {
                PendingTargets.TryRemove(bot.DatabaseID, out _);
                return false;
            }

            long currentSecond = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (Interlocked.Read(ref _activeApplySecond) != currentSecond)
            {
                Interlocked.Exchange(ref _activeApplySecond, currentSecond);
                Interlocked.Exchange(ref _activeChangesThisSecond, 0);
            }
            if (Interlocked.Increment(ref _activeChangesThisSecond) > MaximumActiveChangesPerSecond)
                return false;

            string previousType = record.PlayerType;
            record.PlayerType = ((AutonomousPlayerType)target).ToString();
            record.Dirty = true;
            bool saved;
            try
            {
                saved = bot.SaveAutonomousState(includeInventory: false);
            }
            catch (Exception exception)
            {
                record.PlayerType = previousType;
                record.Dirty = true;
                _error = $"Could not save a bot type change for autonomous bot {bot.DatabaseID}; it will retry: {exception.Message}";
                return false;
            }
            if (saved)
            {
                PendingTargets.TryRemove(bot.DatabaseID, out _);
                _error = string.Empty;
                return true;
            }

            record.PlayerType = previousType;
            record.Dirty = true;
            _error = $"Could not save a bot type change for autonomous bot {bot.DatabaseID}; it will retry.";
            return false;
        }
    }

    private static void TryReadRequest(DateTime nowUtc)
    {
        MixRequest? request;
        try
        {
            if (!File.Exists(_requestPath) || new FileInfo(_requestPath).Length > MaximumRequestBytes)
                return;
            request = JsonSerializer.Deserialize<MixRequest>(File.ReadAllText(_requestPath), JsonOptions);
        }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        catch (JsonException exception)
        {
            Log.Warn("Could not read the launcher population mix request: " + exception.Message);
            return;
        }

        if (request == null || string.IsNullOrWhiteSpace(request.RequestId)) return;
        lock (Gate)
        {
            if (!_running || request.RequestId == _lastRequestId) return;
        }

        MixStatus? previous = ReadStatus();
        if (previous?.RequestId == request.RequestId &&
            previous.State is "applied" or "failed")
        {
            lock (Gate) _lastRequestId = request.RequestId;
            return;
        }

        bool resume = previous?.RequestId == request.RequestId && previous.State == "pending";
        if (!Guid.TryParse(request.RequestId, out _) || request.Mix == null)
        {
            Reject(request, "The population mix request is invalid.");
            return;
        }
        try
        {
            new BotGoalSettings { Preset = PopulationPreset.Custom, Mix = request.Mix }.Validate();
        }
        catch (Exception exception)
        {
            Reject(request, exception.Message);
            return;
        }

        DateTime createdUtc = AsUtc(request.CreatedUtc);
        if (!resume && (createdUtc > nowUtc.AddSeconds(10) || createdUtc < nowUtc.AddMinutes(-2)))
        {
            Reject(request, "The population mix request expired. Apply the mix again from the launcher.");
            return;
        }
        if (!resume && !string.Equals(request.SessionId, OfflineWorldSpeedControl.SessionId, StringComparison.Ordinal))
        {
            Reject(request, "The population mix request belongs to a different server session. Apply it again.");
            return;
        }

        try
        {
            // Publish pending before changing durable settings. A restart can then
            // recognize and resume the same idempotent request.
            MixStatus pending = new()
            {
                SessionId = OfflineWorldSpeedControl.SessionId,
                RequestId = request.RequestId,
                State = "pending",
                UpdatedUtc = nowUtc,
                Mix = request.Mix,
            };
            WriteStatus(pending);
            lock (Gate)
            {
                _request = request;
                _lastRequestId = request.RequestId;
                _error = string.Empty;
                PendingTargets.Clear();
            }
        }
        catch (Exception exception)
        {
            Log.Error("Could not acknowledge the launcher population mix request.", exception);
            // Leave it unseen so the next population poll retries the atomic status write.
        }
    }

    private static void Reject(MixRequest request, string error)
    {
        try
        {
            WriteStatus(new MixStatus
            {
                SessionId = OfflineWorldSpeedControl.SessionId,
                RequestId = request.RequestId,
                State = "failed",
                UpdatedUtc = DateTime.UtcNow,
                Mix = request.Mix ?? new PlayerTypeMix(0, 0, 0, 0, 0, 0),
                Error = error,
            });
            lock (Gate) _lastRequestId = request.RequestId;
        }
        catch (Exception exception)
        {
            Log.Error("Could not publish a failed launcher population mix request.", exception);
        }
    }

    private static List<PlannedChange> Plan(RosterEntry[] roster, int[] targets)
    {
        int[] counts = CountTypes(roster);
        int[] surplus = counts.Select((count, index) => Math.Max(0, count - targets[index])).ToArray();
        int[] deficit = targets.Select((target, index) => Math.Max(0, target - counts[index])).ToArray();
        var planned = new List<PlannedChange>();
        for (int source = 0; source < surplus.Length; source++)
        {
            if (surplus[source] == 0) continue;
            RosterEntry[] candidates = roster.Where(entry => entry.CurrentType == source)
                .OrderBy(CandidatePriority)
                .ThenBy(entry => entry.Record.BotId)
                .Take(surplus[source]).ToArray();
            foreach (RosterEntry candidate in candidates)
            {
                int destination = Enumerable.Range(0, deficit.Length)
                    .Where(index => deficit[index] > 0)
                    .OrderByDescending(index => deficit[index])
                    .ThenBy(index => index)
                    .FirstOrDefault(-1);
                if (destination < 0) break;
                deficit[destination]--;
                planned.Add(new PlannedChange(candidate, destination));
            }
        }
        return planned;
    }

    private static RosterEntry[] BuildRoster(IReadOnlyList<OfflineWorldBotRecord> cachedRoster,
        Func<long, bool> isSpawnPending)
    {
        var entries = new List<RosterEntry>();
        foreach (OfflineWorldBotRecord record in (cachedRoster ?? Array.Empty<OfflineWorldBotRecord>())
                     .Where(record => record != null && !record.IsRetired)
                     .GroupBy(record => record.BotId)
                     .Select(group => group.First()))
        {
            GameBot? live = AutonomousBotRegistry.TryGet(record.BotId, out GameBot found) ? found : null;
            // Player-led companions and temporary helpers are not autonomous
            // population members, even if a stale roster row points at them.
            if (live != null && (live.IsAutonomousWorldBot != true || live.IsPersistentPlayerCompanion ||
                                 live.IsTemporaryGroupHelper))
                continue;
            if (live?.PersistentRecord == null)
                live = null;
            entries.Add(new RosterEntry(live?.PersistentRecord ?? record, live,
                live == null && isSpawnPending?.Invoke(record.BotId) == true));
        }
        return entries.ToArray();
    }

    private static int CandidatePriority(RosterEntry entry)
    {
        if (entry.IsEditableOffline) return 0;
        if (entry.LiveBot != null && CanApplyAtTaskBoundary(entry.LiveBot, taskJustCompleted: false)) return 1;
        if (entry.LiveBot != null) return 2;
        return 3;
    }

    private static bool CanApplyAtTaskBoundary(GameBot bot, bool taskJustCompleted) =>
        bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper && !bot.IsPlayerLedGroup &&
        bot.PersistentRecord != null && bot.Group == null && bot.ObjectState == GameObject.eObjectState.Active &&
        bot.IsAlive && !bot.InCombat && !bot.IsAttacking && !bot.IsCasting &&
        !bot.IsReturningAfterRelease && !bot.IsOnStableMasterRoute &&
        (taskJustCompleted || string.IsNullOrWhiteSpace(bot.PersistentRecord.ObjectiveAssignmentId) ||
         AutonomousObjectiveAssignments.IsBetweenPveTasks(bot.PersistentRecord));

    private static bool TrySaveOfflineChanges(PlannedChange[] changes, out string error)
    {
        error = string.Empty;
        bool lockTaken = false;
        (OfflineWorldBotRecord Record, string PlayerType)[] oldTypes = changes
            .Select(change => (change.Entry.Record, change.Entry.Record.PlayerType)).ToArray();
        try
        {
            Monitor.TryEnter(AutonomousBotStatusPersistence.DatabaseWriteLock, 0, ref lockTaken);
            if (!lockTaken)
            {
                error = "The save database is busy; offline type changes will retry.";
                return false;
            }

            foreach (PlannedChange change in changes)
            {
                change.Entry.Record.PlayerType = ((AutonomousPlayerType)change.TargetType).ToString();
                change.Entry.Record.Dirty = true;
            }
            if (GameServer.Database.SaveObject(changes.Select(change => (DataObject)change.Entry.Record)))
                return true;

            foreach ((OfflineWorldBotRecord record, string playerType) in oldTypes)
            {
                record.PlayerType = playerType;
                record.Dirty = true;
            }
            error = "Could not save an offline bot type change; it will retry.";
            return false;
        }
        catch (Exception exception)
        {
            foreach ((OfflineWorldBotRecord record, string playerType) in oldTypes)
            {
                record.PlayerType = playerType;
                record.Dirty = true;
            }
            error = "Could not save an offline bot type change; it will retry: " + exception.Message;
            return false;
        }
        finally
        {
            if (lockTaken) Monitor.Exit(AutonomousBotStatusPersistence.DatabaseWriteLock);
        }
    }

    private static RosterEntry[] RefreshLiveRecords(RosterEntry[] roster) => roster.Select(entry =>
    {
        if (AutonomousBotRegistry.TryGet(entry.Record.BotId, out GameBot live) &&
            live?.IsAutonomousWorldBot == true && !live.IsTemporaryGroupHelper && live.PersistentRecord != null)
            return entry with { Record = live.PersistentRecord, LiveBot = live };
        return entry;
    }).ToArray();

    private static int[] TargetCounts(PlayerTypeMix mix, int count)
    {
        int[] weights = mix.Values;
        int[] targets = new int[weights.Length];
        double[] remainders = new double[weights.Length];
        for (int index = 0; index < weights.Length; index++)
        {
            double exact = count * weights[index] / 100d;
            targets[index] = (int)Math.Floor(exact);
            remainders[index] = exact - targets[index];
        }
        int unassigned = count - targets.Sum();
        foreach (int index in Enumerable.Range(0, weights.Length)
                     .OrderByDescending(index => remainders[index])
                     .ThenBy(index => index).Take(unassigned))
            targets[index]++;
        return targets;
    }

    private static int[] CountTypes(IEnumerable<RosterEntry> roster)
    {
        int[] counts = new int[6];
        foreach (RosterEntry entry in roster)
            counts[entry.CurrentType]++;
        return counts;
    }

    private static int TypeIndex(string playerType) =>
        Enum.TryParse(playerType, true, out AutonomousPlayerType parsed) && Enum.IsDefined(parsed)
            ? (int)parsed : (int)AutonomousPlayerType.Leveler;

    private static void PublishStatus(string state, RosterEntry[] roster, int[] current, int[] targets,
        int activePending, string error)
    {
        MixRequest? request;
        lock (Gate) request = _request;
        if (request == null) return;
        try
        {
            WriteStatus(new MixStatus
            {
                SessionId = OfflineWorldSpeedControl.SessionId,
                RequestId = request.RequestId,
                State = state,
                UpdatedUtc = DateTime.UtcNow,
                Mix = request.Mix,
                RosterCount = roster?.Length ?? 0,
                CurrentCounts = current ?? new int[6],
                TargetCounts = targets ?? TargetCounts(request.Mix, roster?.Length ?? 0),
                ActivePending = activePending,
                Error = error ?? string.Empty,
            });
            if (state == "applied")
            {
                lock (Gate)
                {
                    if (_request?.RequestId == request.RequestId)
                    {
                        _request = null;
                        PendingTargets.Clear();
                    }
                }
                Log.Info($"Applied autonomous bot player-type mix across {roster.Length} saved bots: {string.Join(",", current)}.");
            }
        }
        catch (Exception exception)
        {
            lock (Gate) _error = "Could not publish population mix status; completion will retry: " + exception.Message;
            Log.Error("Could not publish the launcher population mix status.", exception);
        }
    }

    private static MixStatus? ReadStatus()
    {
        try
        {
            if (!File.Exists(_statusPath) || new FileInfo(_statusPath).Length > MaximumRequestBytes)
                return null;
            return JsonSerializer.Deserialize<MixStatus>(File.ReadAllText(_statusPath), JsonOptions);
        }
        catch { return null; }
    }

    private static void WriteStatus(MixStatus status)
    {
        string temporary = _statusPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] contents = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(status, JsonOptions));
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(contents);
                stream.Flush(true);
            }
            File.Move(temporary, _statusPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
