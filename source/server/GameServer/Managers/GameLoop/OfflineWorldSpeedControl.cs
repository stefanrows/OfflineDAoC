using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using DOL.GS.ServerProperties;
using DOL.Logging;
using Newtonsoft.Json;

namespace DOL.GS
{
    /// <summary>Local launcher control for offline world tick pacing.</summary>
    public static class OfflineWorldSpeedControl
    {
        public const int MaximumMultiplier = 3;

        private const int RequestFreshnessSeconds = 30;
        private const int RequestFutureToleranceSeconds = 5;
        private const int DisconnectGraceSeconds = 5;
        private const int WorkerPollMilliseconds = 200;
        private const int AchievementWindowSeconds = 10;
        private const int MaximumRecentRequestIds = 4096;
        private const int AcceptedRequestRetentionSeconds = RequestFreshnessSeconds + RequestFutureToleranceSeconds + 5;

        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly object Gate = new();
        private static readonly object TickSamplesGate = new();
        private static readonly AutoResetEvent WakeWorker = new(false);
        private static readonly long[] TickCompletionTimes = new long[2048];
        private static readonly HashSet<string> AcceptedRequestIds = new(StringComparer.Ordinal);
        private static readonly Queue<(string RequestId, long AcceptedAt)> AcceptedRequestOrder = new();
        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateFormatString = "yyyy'-'MM'-'dd'T'HH':'mm':'ss.fffffff'Z'"
        };

        private static Thread _worker;
        private static bool _running;
        private static string _sessionId = Guid.NewGuid().ToString("D");
        private static string _requestPath;
        private static string _statusPath;
        private static string _lastAcceptedRequestText;
        private static string _lastRejectedRequestText;
        private static string _requestError;
        private static string _clockError;
        private static string _statusError;
        private static int _selectedMultiplier = 1;
        private static int _effectiveMultiplier = 1;
        private static int _registeredClients;
        private static readonly HashSet<GameClient> PendingClients = new();
        private static int _connectedClients;
        private static bool _hasDisconnected;
        private static long _lastDisconnectTimestamp;
        private static bool _forceCheckpoint;
        private static bool _speedChangeCheckpointPending;
        private static long _lastCheckpointTimestamp;
        private static long _lastStatusTimestamp;
        private static int _tickWriteIndex;
        private static int _tickSampleCount;
        private static double _achievedMultiplier;

        public static string SessionId => Volatile.Read(ref _sessionId);
        public static int SelectedMultiplier => Volatile.Read(ref _selectedMultiplier);
        public static int EffectiveMultiplier
        {
            get
            {
                lock (Gate)
                {
                    RefreshEffectiveMultiplierLocked(Stopwatch.GetTimestamp());
                    return _effectiveMultiplier;
                }
            }
        }
        public static int ConnectedClients => Volatile.Read(ref _connectedClients);
        public static double AchievedMultiplier => Volatile.Read(ref _achievedMultiplier);

        public static bool Initialize()
        {
            lock (Gate)
            {
                if (_running)
                    return false;

                _sessionId = Guid.NewGuid().ToString("D");
                _requestPath = Path.Combine(AppContext.BaseDirectory, "world-speed.request.json");
                _statusPath = Path.Combine(AppContext.BaseDirectory, "world-speed.status.json");
                AcceptedRequestIds.Clear();
                AcceptedRequestOrder.Clear();
                _lastAcceptedRequestText = null;
                _lastRejectedRequestText = null;
                _requestError = null;
                _clockError = WorldSimulationClock.LastError;
                _statusError = null;
                _selectedMultiplier = 1;
                _effectiveMultiplier = 1;
                _registeredClients = ClientService.Instance.ClientCount;
                PendingClients.Clear();
                _connectedClients = Math.Max(0, _registeredClients);
                _hasDisconnected = false;
                _lastDisconnectTimestamp = 0;
                _forceCheckpoint = false;
                _speedChangeCheckpointPending = false;
                _lastCheckpointTimestamp = Stopwatch.GetTimestamp();
                _lastStatusTimestamp = 0;
                _achievedMultiplier = 0;

                lock (TickSamplesGate)
                {
                    _tickWriteIndex = 0;
                    _tickSampleCount = 0;
                    Array.Clear(TickCompletionTimes);
                }

                try
                {
                    Directory.CreateDirectory(AppContext.BaseDirectory);
                    if (File.Exists(_requestPath))
                        _lastRejectedRequestText = File.ReadAllText(_requestPath, Encoding.UTF8);
                }
                catch (Exception e)
                {
                    _statusError = $"Could not access the local world speed control directory: {e.Message}";
                }

                _running = true;
                _worker = new Thread(ControlWorker)
                {
                    Name = "OfflineWorldSpeedControl",
                    IsBackground = true
                };
                _worker.Start();
            }

            WakeWorker.Set();
            return true;
        }

        public static void Stop()
        {
            Thread worker;
            lock (Gate)
            {
                if (!_running)
                    return;

                _running = false;
                worker = _worker;
            }

            WakeWorker.Set();
            if (worker != null && worker != Thread.CurrentThread && worker.IsAlive)
                worker.Join();

            if (!WorldSimulationClock.FlushCheckpoint(out string error))
                HandleCheckpointFailure(error);
            else
                HandleCheckpointSuccess();

            PublishStatus();
        }

        /// <summary>Called from the socket callback before registration is queued to the game loop.</summary>
        public static void OnClientConnectionPending(GameClient client)
        {
            lock (Gate)
            {
                if (client != null)
                    PendingClients.Add(client);
                UpdateConnectedClientsLocked(Stopwatch.GetTimestamp());
            }
            WakeWorker.Set();
        }

        /// <summary>Completes one pending connection after ClientService registers the client.</summary>
        public static void OnClientRegistrationCompleted(GameClient client, int registeredClientCount)
        {
            lock (Gate)
            {
                if (client != null)
                    PendingClients.Remove(client);
                _registeredClients = Math.Max(0, registeredClientCount);
                UpdateConnectedClientsLocked(Stopwatch.GetTimestamp());
            }
            WakeWorker.Set();
        }

        public static void OnClientDisconnected(GameClient client, int registeredClientCount)
        {
            lock (Gate)
            {
                if (client != null)
                    PendingClients.Remove(client);
                _registeredClients = Math.Max(0, registeredClientCount);
                UpdateConnectedClientsLocked(Stopwatch.GetTimestamp());
            }
            WakeWorker.Set();
        }

        public static void OnClientCountChanged(int registeredClientCount)
        {
            lock (Gate)
            {
                _registeredClients = Math.Max(0, registeredClientCount);
                UpdateConnectedClientsLocked(Stopwatch.GetTimestamp());
            }
            WakeWorker.Set();
        }

        public static void RefreshClientCount(int registeredClientCount)
        {
            lock (Gate)
            {
                _registeredClients = Math.Max(0, registeredClientCount);
                UpdateConnectedClientsLocked(Stopwatch.GetTimestamp());
            }
        }

        public static void RecordCompletedLogicalTick()
        {
            long now = Stopwatch.GetTimestamp();
            double achieved = 0;

            lock (TickSamplesGate)
            {
                TickCompletionTimes[_tickWriteIndex] = now;
                _tickWriteIndex = (_tickWriteIndex + 1) % TickCompletionTimes.Length;
                _tickSampleCount = Math.Min(_tickSampleCount + 1, TickCompletionTimes.Length);

                long cutoff = now - AchievementWindowSeconds * Stopwatch.Frequency;
                long oldest = 0;
                long newest = 0;
                int valid = 0;
                int start = (_tickWriteIndex - _tickSampleCount + TickCompletionTimes.Length) % TickCompletionTimes.Length;
                for (int i = 0; i < _tickSampleCount; i++)
                {
                    long sample = TickCompletionTimes[(start + i) % TickCompletionTimes.Length];
                    if (sample < cutoff)
                        continue;

                    if (valid == 0)
                        oldest = sample;
                    newest = sample;
                    valid++;
                }

                if (valid > 1 && newest > oldest)
                    achieved = CalculateAchievedMultiplier(valid - 1, (newest - oldest) / (double) Stopwatch.Frequency);
            }

            Volatile.Write(ref _achievedMultiplier, achieved);
        }

        public static bool TryValidateRequest(WorldSpeedRequest request, string currentSessionId, ISet<string> acceptedRequestIds, DateTime nowUtc, out string error)
        {
            error = null;
            if (request == null)
            {
                error = "The world speed request is empty or invalid.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.SessionId) || !string.Equals(request.SessionId, currentSessionId, StringComparison.Ordinal))
            {
                error = "The world speed request belongs to a different server session.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.RequestId) || acceptedRequestIds?.Contains(request.RequestId) == true)
            {
                error = "The world speed request ID is missing or has already been used.";
                return false;
            }

            if (request.Multiplier < 1 || request.Multiplier > MaximumMultiplier)
            {
                error = "The world speed multiplier must be 1, 2, or 3.";
                return false;
            }

            DateTime createdUtc = AsUtc(request.CreatedUtc);
            DateTime now = AsUtc(nowUtc);
            TimeSpan age = now - createdUtc;
            if (age > TimeSpan.FromSeconds(RequestFreshnessSeconds) || age < TimeSpan.FromSeconds(-RequestFutureToleranceSeconds))
            {
                error = "The world speed request timestamp is stale or too far in the future.";
                return false;
            }

            return true;
        }

        public static int ComputeEffectiveMultiplier(int selectedMultiplier, int connectedClients, bool hasDisconnected, long lastDisconnectTimestamp, long nowTimestamp, long frequency)
        {
            int selected = Math.Clamp(selectedMultiplier, 1, MaximumMultiplier);
            if (connectedClients > 0)
                return 1;

            if (!hasDisconnected)
                return selected;

            double sinceDisconnect = (nowTimestamp - lastDisconnectTimestamp) / (double) Math.Max(1, frequency);
            return sinceDisconnect >= DisconnectGraceSeconds ? selected : 1;
        }

        public static int GetCheckpointIntervalMilliseconds(int effectiveMultiplier) => 1000;

        public static double CalculateAchievedMultiplier(int elapsedTicks, double elapsedSeconds)
        {
            if (elapsedTicks <= 0 || elapsedSeconds <= 0)
                return 0;

            double multiplier = elapsedTicks / (elapsedSeconds * Properties.GAME_LOOP_TICK_RATE);
            return Math.Clamp(multiplier, 0, MaximumMultiplier);
        }

        public static bool IsAcceptedRequestIdExpired(long acceptedAtTimestamp, long nowTimestamp, long frequency) =>
            nowTimestamp - acceptedAtTimestamp > Math.Max(1, frequency) * AcceptedRequestRetentionSeconds;

        private static void ControlWorker()
        {
            while (true)
            {
                WakeWorker.WaitOne(WorkerPollMilliseconds);
                lock (Gate)
                {
                    if (!_running)
                        break;
                    PruneAcceptedRequestIdsLocked(Stopwatch.GetTimestamp());
                    RefreshEffectiveMultiplierLocked(Stopwatch.GetTimestamp());
                }

                PollRequest();
                long now = Stopwatch.GetTimestamp();
                bool checkpoint;
                lock (Gate)
                {
                    long interval = Stopwatch.Frequency * GetCheckpointIntervalMilliseconds(_effectiveMultiplier) / 1000;
                    checkpoint = _forceCheckpoint || now - _lastCheckpointTimestamp >= interval;
                    if (checkpoint)
                        _forceCheckpoint = false;
                }

                if (checkpoint)
                {
                    if (WorldSimulationClock.FlushCheckpoint(out string error))
                        HandleCheckpointSuccess();
                    else
                        HandleCheckpointFailure(error);

                    lock (Gate)
                        _lastCheckpointTimestamp = Stopwatch.GetTimestamp();
                }

                bool publish;
                lock (Gate)
                {
                    publish = now - _lastStatusTimestamp >= Stopwatch.Frequency || _lastStatusTimestamp == 0;
                    if (publish)
                        _lastStatusTimestamp = now;
                }

                if (publish)
                    PublishStatus();
            }
        }

        private static void PollRequest()
        {
            string contents;
            try
            {
                if (!File.Exists(_requestPath))
                    return;
                contents = File.ReadAllText(_requestPath, Encoding.UTF8);
            }
            catch (Exception e)
            {
                SetRequestError($"Could not read the local world speed request: {e.Message}", null);
                return;
            }

            lock (Gate)
            {
                if (string.Equals(contents, _lastAcceptedRequestText, StringComparison.Ordinal))
                    return;
            }

            WorldSpeedRequest request;
            try
            {
                request = JsonConvert.DeserializeObject<WorldSpeedRequest>(contents, JsonSettings);
            }
            catch (Exception e)
            {
                SetRequestError($"The local world speed request is invalid: {e.Message}", contents);
                return;
            }

            string sessionId;
            string validationError;
            bool valid;
            bool duplicateRequest;
            lock (Gate)
            {
                sessionId = _sessionId;
                PruneAcceptedRequestIdsLocked(Stopwatch.GetTimestamp());
                duplicateRequest = request != null && !string.IsNullOrWhiteSpace(request.RequestId) && AcceptedRequestIds.Contains(request.RequestId);
                valid = TryValidateRequest(request, sessionId, AcceptedRequestIds, DateTime.UtcNow, out validationError);
            }
            if (!valid)
            {
                if (duplicateRequest)
                    return;
                SetRequestError(validationError, contents);
                return;
            }

            lock (Gate)
            {
                if (AcceptedRequestIds.Count >= MaximumRecentRequestIds)
                {
                    _requestError = "Too many recent world speed requests; try again after the request window expires.";
                    return;
                }

                long acceptedAt = Stopwatch.GetTimestamp();
                AcceptedRequestIds.Add(request.RequestId);
                AcceptedRequestOrder.Enqueue((request.RequestId, acceptedAt));
                _lastRejectedRequestText = null;
                _lastAcceptedRequestText = contents;
                _requestError = null;
                if (_selectedMultiplier != request.Multiplier)
                {
                    _selectedMultiplier = request.Multiplier;
                    _speedChangeCheckpointPending = true;
                    _forceCheckpoint = true;
                    SetEffectiveMultiplierLocked(1);
                }
                else
                {
                    RefreshEffectiveMultiplierLocked(Stopwatch.GetTimestamp());
                }
            }
            WakeWorker.Set();
        }

        private static void PruneAcceptedRequestIdsLocked(long now)
        {
            while (AcceptedRequestOrder.Count > 0 && IsAcceptedRequestIdExpired(AcceptedRequestOrder.Peek().AcceptedAt, now, Stopwatch.Frequency))
            {
                (string requestId, _) = AcceptedRequestOrder.Dequeue();
                AcceptedRequestIds.Remove(requestId);
            }
        }

        private static void SetRequestError(string error, string requestText)
        {
            lock (Gate)
            {
                if (requestText != null && string.Equals(requestText, _lastRejectedRequestText, StringComparison.Ordinal))
                    return;

                _lastRejectedRequestText = requestText;
                _requestError = error;
            }
        }

        private static void HandleCheckpointSuccess()
        {
            lock (Gate)
            {
                _clockError = null;
                if (_speedChangeCheckpointPending)
                {
                    _speedChangeCheckpointPending = false;
                    RefreshEffectiveMultiplierLocked(Stopwatch.GetTimestamp());
                }
            }
        }

        private static void HandleCheckpointFailure(string error)
        {
            lock (Gate)
            {
                _clockError = error;
                _selectedMultiplier = 1;
                _speedChangeCheckpointPending = false;
                SetEffectiveMultiplierLocked(1);
            }
            if (Log.IsErrorEnabled)
                Log.Error(error);
        }

        private static void UpdateConnectedClientsLocked(long now)
        {
            int count = Math.Max(0, _registeredClients) + PendingClients.Count;
            int previousCount = _connectedClients;
            _connectedClients = count;

            if (count > 0)
            {
                _hasDisconnected = false;
                _lastDisconnectTimestamp = 0;
                SetEffectiveMultiplierLocked(1);
                return;
            }

            if (previousCount > 0)
            {
                _hasDisconnected = true;
                _lastDisconnectTimestamp = now;
                SetEffectiveMultiplierLocked(1);
                return;
            }

            RefreshEffectiveMultiplierLocked(now);
        }

        private static void RefreshEffectiveMultiplierLocked(long now)
        {
            int multiplier = _speedChangeCheckpointPending
                ? 1
                : ComputeEffectiveMultiplier(_selectedMultiplier, _connectedClients, _hasDisconnected, _lastDisconnectTimestamp, now, Stopwatch.Frequency);
            SetEffectiveMultiplierLocked(multiplier);
        }

        private static void SetEffectiveMultiplierLocked(int multiplier)
        {
            if (_effectiveMultiplier == multiplier)
                return;

            _effectiveMultiplier = multiplier;
            GameLoop.NotifySpeedChanged();
            WakeWorker.Set();
        }

        private static void PublishStatus()
        {
            string path;
            WorldSpeedStatus status;
            lock (Gate)
            {
                path = _statusPath;
                status = new WorldSpeedStatus
                {
                    SessionId = _sessionId,
                    UpdatedUtc = DateTime.UtcNow,
                    SimulatedUtc = WorldSimulationClock.UtcNow,
                    SelectedMultiplier = _selectedMultiplier,
                    EffectiveMultiplier = _effectiveMultiplier,
                    AchievedMultiplier = AchievedMultiplier,
                    ConnectedClients = _connectedClients,
                    TickP95Ms = GameLoopWorkMetrics.LatestTickP95Ms,
                    Error = _clockError ?? _requestError ?? _statusError
                };
            }

            if (string.IsNullOrEmpty(path))
                return;

            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string contents = JsonConvert.SerializeObject(status, JsonSettings);
                File.WriteAllText(tempPath, contents, new UTF8Encoding(false));
                File.Move(tempPath, path, true);
                lock (Gate)
                    _statusError = null;
            }
            catch (Exception e)
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                    // Keep the original status write failure as the useful error.
                }

                lock (Gate)
                    _statusError = $"Could not write the local world speed status: {e.Message}";
                if (Log.IsErrorEnabled)
                    Log.Error($"Could not write the local world speed status file at {path}.", e);
            }
        }

        private static DateTime AsUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }
    }

    public sealed class WorldSpeedRequest
    {
        public string SessionId { get; set; }
        public string RequestId { get; set; }
        public DateTime CreatedUtc { get; set; }
        public int Multiplier { get; set; }
    }

    public sealed class WorldSpeedStatus
    {
        public string SessionId { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public DateTime SimulatedUtc { get; set; }
        public int SelectedMultiplier { get; set; }
        public int EffectiveMultiplier { get; set; }
        public double AchievedMultiplier { get; set; }
        public int ConnectedClients { get; set; }
        public double TickP95Ms { get; set; }
        public string Error { get; set; }
    }
}
