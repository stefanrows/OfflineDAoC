using System;
using System.Threading;
using DOL.Database;
using Newtonsoft.Json;

namespace DOL.GS
{
    /// <summary>
    /// UTC gameplay time. It advances only when the game loop completes a
    /// logical tick; wall UTC remains the source for operational timestamps.
    /// </summary>
    public static class WorldSimulationClock
    {
        public const string CheckpointKey = "WorldSimulationClock";
        public const int CheckpointVersion = 1;

        private static readonly object Gate = new();
        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateFormatString = "yyyy'-'MM'-'dd'T'HH':'mm':'ss.fffffff'Z'"
        };

        private static long _simulationUtcTicks = DateTime.UtcNow.Ticks;
        private static IObjectDatabase _database;
        private static DbOfflineLocalOption _checkpointOption;
        private static bool _initialized;
        private static string _lastError;

        public static DateTime UtcNow
        {
            get
            {
                if (!Volatile.Read(ref _initialized))
                    return DateTime.UtcNow;

                return new DateTime(Interlocked.Read(ref _simulationUtcTicks), DateTimeKind.Utc);
            }
        }

        public static DateTime LocalNow => UtcNow.ToLocalTime();
        public static string LastError => Volatile.Read(ref _lastError);

        public static void Initialize(IObjectDatabase database)
        {
            if (database == null)
                throw new ArgumentNullException(nameof(database));

            DateTime wallNow = DateTime.UtcNow;
            DateTime simulationNow = wallNow;
            string error = null;
            DbOfflineLocalOption option = null;

            try
            {
                option = database.FindObjectByKey<DbOfflineLocalOption>(CheckpointKey);
                if (option != null)
                {
                    simulationNow = RestoreCheckpoint(option.Value, wallNow, out error);
                    if (error != null)
                        throw new InvalidOperationException(error);
                }
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"Could not initialize the world simulation clock: {e.Message}", e);
            }

            lock (Gate)
            {
                _database = database;
                _checkpointOption = option;
                Interlocked.Exchange(ref _simulationUtcTicks, simulationNow.Ticks);
                Volatile.Write(ref _lastError, error);
                Volatile.Write(ref _initialized, true);
            }
        }

        /// <summary>Advances gameplay UTC after one complete logical tick.</summary>
        public static void AdvanceMilliseconds(int logicalMilliseconds)
        {
            if (logicalMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(logicalMilliseconds));

            if (!Volatile.Read(ref _initialized))
                return;

            Interlocked.Add(ref _simulationUtcTicks, (long) logicalMilliseconds * TimeSpan.TicksPerMillisecond);
        }

        public static bool FlushCheckpoint(out string error)
        {
            lock (Gate)
            {
                try
                {
                    if (!_initialized || _database == null)
                        throw new InvalidOperationException("The world simulation clock has not been initialized.");

                    DateTime wallNow = DateTime.UtcNow;
                    DateTime simulationNow = new(Interlocked.Read(ref _simulationUtcTicks), DateTimeKind.Utc);
                    string value = CreateCheckpointJson(simulationNow, wallNow);

                    lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                    {
                        if (_checkpointOption == null)
                        {
                            DbOfflineLocalOption option = new() { Key = CheckpointKey, Value = value };
                            if (!_database.AddObject(option))
                                throw new InvalidOperationException("The world simulation clock checkpoint could not be inserted.");
                            _checkpointOption = option;
                        }
                        else
                        {
                            _checkpointOption.Value = value;
                            if (!_database.SaveObject(_checkpointOption))
                                throw new InvalidOperationException("The world simulation clock checkpoint could not be saved.");
                        }
                    }

                    Volatile.Write(ref _lastError, null);
                    error = null;
                    return true;
                }
                catch (Exception e)
                {
                    error = $"Could not save the world simulation clock checkpoint: {e.Message}";
                    Volatile.Write(ref _lastError, error);
                    return false;
                }
            }
        }

        public static string CreateCheckpointJson(DateTime simulationUtc, DateTime wallUtc)
        {
            return JsonConvert.SerializeObject(new Checkpoint
            {
                Version = CheckpointVersion,
                SimulationUtc = AsUtc(simulationUtc),
                WallUtc = AsUtc(wallUtc)
            }, JsonSettings);
        }

        public static DateTime RestoreCheckpoint(string json, DateTime wallNowUtc, out string error)
        {
            DateTime wallNow = AsUtc(wallNowUtc);
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "The saved world simulation clock checkpoint is empty.";
                return wallNow;
            }

            try
            {
                Checkpoint checkpoint = JsonConvert.DeserializeObject<Checkpoint>(json, JsonSettings);
                if (checkpoint == null || checkpoint.Version != CheckpointVersion)
                    throw new InvalidOperationException("The checkpoint version is missing or unsupported.");

                DateTime simulationUtc = AsUtc(checkpoint.SimulationUtc);
                DateTime savedWallUtc = AsUtc(checkpoint.WallUtc);
                if (simulationUtc <= DateTime.UnixEpoch || savedWallUtc <= DateTime.UnixEpoch)
                    throw new InvalidOperationException("The checkpoint is missing valid UTC timestamps.");

                TimeSpan downtime = wallNow - savedWallUtc;
                if (downtime < TimeSpan.Zero)
                    downtime = TimeSpan.Zero;

                long availableTicks = DateTime.MaxValue.Ticks - simulationUtc.Ticks;
                if (downtime.Ticks > availableTicks)
                    downtime = TimeSpan.FromTicks(availableTicks);

                return simulationUtc.Add(downtime);
            }
            catch (Exception e)
            {
                error = $"The saved world simulation clock checkpoint is invalid: {e.Message}";
                return wallNow;
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

        private sealed class Checkpoint
        {
            public int Version { get; set; }
            public DateTime SimulationUtc { get; set; }
            public DateTime WallUtc { get; set; }
        }
    }
}
