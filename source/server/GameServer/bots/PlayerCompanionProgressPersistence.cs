using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.Events;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Coalesces persistent companion XP and training saves without rewriting
    /// inventory rows for every NPC kill.
    /// </summary>
    public static class PlayerCompanionProgressPersistence
    {
        private const int WriteBatchSize = 32;
        private static readonly Logger Log = LoggerManager.Create(typeof(PlayerCompanionProgressPersistence));
        private static readonly object PendingGate = new();
        private static readonly Dictionary<string, GameBot> Pending = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Queue<string> PendingOrder = new();
        private static readonly Timer FlushTimer = new(_ => Flush(), null, 2_000, 2_000);
        private static int _flushing;

        public static void Queue(GameBot companion)
        {
            if (companion?.IsPersistentPlayerCompanion != true ||
                string.IsNullOrWhiteSpace(companion.PlayerCompanionRecord?.CompanionId))
            {
                return;
            }

            lock (PendingGate)
            {
                string id = companion.PlayerCompanionRecord.CompanionId;
                if (!Pending.ContainsKey(id))
                    PendingOrder.Enqueue(id);
                Pending[id] = companion;
            }
        }

        private static int PendingCount
        {
            get { lock (PendingGate) return Pending.Count; }
        }

        public static int Flush()
        {
            if (Interlocked.Exchange(ref _flushing, 1) != 0)
                return 0;

            var batch = new List<GameBot>(WriteBatchSize);
            try
            {
                lock (PendingGate)
                {
                    while (batch.Count < WriteBatchSize && PendingOrder.Count > 0)
                    {
                        string id = PendingOrder.Dequeue();
                        if (Pending.Remove(id, out GameBot companion))
                            batch.Add(companion);
                    }
                }

                int saved = 0;
                foreach (GameBot companion in batch)
                {
                    if (PlayerCompanionRoster.SaveProgress(companion))
                        saved++;
                    else
                        Queue(companion);
                }

                return saved;
            }
            catch (Exception exception)
            {
                foreach (GameBot companion in batch)
                    Queue(companion);
                Log.Error("Persistent companion progress batch failed.", exception);
                return 0;
            }
            finally
            {
                Volatile.Write(ref _flushing, 0);
            }
        }

        public static int FlushAll()
        {
            int saved = 0;
            int stalledAttempts = 0;
            while (Volatile.Read(ref _flushing) != 0 || PendingCount > 0)
            {
                if (Volatile.Read(ref _flushing) != 0)
                {
                    Thread.Sleep(10);
                    continue;
                }

                int before = PendingCount;
                saved += Flush();
                int after = PendingCount;
                if (after >= before && after > 0)
                {
                    if (++stalledAttempts >= 3)
                    {
                        Log.Error($"Companion shutdown persistence stopped after three failed batches; {after} companions remain queued.");
                        break;
                    }
                    Thread.Sleep(50);
                }
                else
                {
                    stalledAttempts = 0;
                }
            }

            return saved;
        }

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
        {
            FlushTimer.Change(Timeout.Infinite, Timeout.Infinite);
            FlushAll();
        }
    }
}
