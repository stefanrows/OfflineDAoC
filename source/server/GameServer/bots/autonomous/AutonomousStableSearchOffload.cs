using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Runs a stable-route plan on a small dedicated pool instead of inside the
/// bot's brain turn. A plan is a snapshot (positions, candidates and a copy of
/// the corridor inputs) plus navmesh queries, which are per-thread, so it does
/// not need the game loop. One 4 ms plan inside the parallel brain stage holds
/// every worker at the barrier; at 10x world speed (3.3 ms per tick) those
/// stalls were about a fifth of wall time. The bot keeps the "pending, think
/// again shortly" behaviour of the sliced search while the plan runs here.
/// </summary>
public static class AutonomousStableSearchOffload
{
    private const int QueueCapacity = 512;

    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
    private static readonly BlockingCollection<Job> Queue = new(new ConcurrentQueue<Job>(), QueueCapacity);
    private static int _started;

    public sealed class Job
    {
        private readonly AutonomousStableRoutePlanner.Search _search;
        private int _done;

        internal Job(AutonomousStableRoutePlanner.Search search) => _search = search;

        public bool IsDone => Volatile.Read(ref _done) != 0;
        /// <summary>The finished choice; null when no horse helps or the plan failed.</summary>
        public AutonomousStableRoutePlanner.Choice Choice { get; private set; }

        internal void Run()
        {
            try
            {
                // The search was built to run to the end in one call.
                AutonomousStableRoutePlanner.Choice choice;
                while (_search.Continue(out choice) == AutonomousStableRoutePlanner.Search.Outcome.Pending) { }
                Choice = choice;
            }
            catch (Exception e)
            {
                if (Log.IsWarnEnabled)
                    Log.Warn("Background stable-route plan failed; treating it as no horse.", e);
                Choice = null;
            }
            finally
            {
                Volatile.Write(ref _done, 1);
            }
        }
    }

    /// <summary>True when the caller should offload (more than 1x speed).</summary>
    public static bool ShouldOffload => OfflineWorldSpeedControl.EffectiveMultiplier > 1;

    /// <returns>The running job, or null when the queue is full (the caller then
    /// plans in its own turn as before).</returns>
    public static Job TryStart(AutonomousStableRoutePlanner.Search search)
    {
        EnsureStarted();
        Job job = new(search);
        return Queue.TryAdd(job) ? job : null;
    }

    private static void EnsureStarted()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            return;

        int threads = Math.Clamp(Environment.ProcessorCount / 4, 1, 3);
        for (int i = 0; i < threads; i++)
        {
            new Thread(static () =>
            {
                foreach (Job job in Queue.GetConsumingEnumerable())
                    job.Run();
            })
            {
                Name = $"StableRouteSearch_{i}",
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            }.Start();
        }
    }
}
