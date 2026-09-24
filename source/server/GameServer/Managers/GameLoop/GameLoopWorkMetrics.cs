using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>Game-loop-owner-only, bounded aggregate measurements. No actor
    /// references, per-bot allocations, background sampler or per-tick log IO.</summary>
    public static class GameLoopWorkMetrics
    {
        private sealed class Counters
        {
            public long Count;
            public double TotalMs;
            public double MaxMs;
            public double WaitMs;
            public long OverBudget;
        }

        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly Dictionary<string, Counters> Stages = new();
        private static long _reportAt;
        private static double _stageWaitMs;
        private static readonly double[] TickSamples = new double[1024];
        private static int _tickSampleCount;
        private static long _tickReportAt;
        private static double _latestTickP95Ms;

        public static double LatestTickP95Ms => Volatile.Read(ref _latestTickP95Ms);

        public static void RecordTick(long started)
        {
            TickSamples[_tickSampleCount++ % TickSamples.Length] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            long now = Stopwatch.GetTimestamp();
            if (_tickReportAt == 0) _tickReportAt = now + 60 * Stopwatch.Frequency;
            if (now < _tickReportAt) return;
            _tickReportAt = now + 60 * Stopwatch.Frequency;
            int count = Math.Min(_tickSampleCount, TickSamples.Length);
            var ordered = new double[count];
            Array.Copy(TickSamples, ordered, count);
            Array.Sort(ordered);
            Volatile.Write(ref _latestTickP95Ms, ordered[(int)Math.Ceiling(count * .95) - 1]);
        }

        public static long BeginStage()
        {
            _stageWaitMs = 0;
            return Stopwatch.GetTimestamp();
        }

        public static void RecordBarrierWait(long started) =>
            _stageWaitMs += Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        public static void EndStage(string stage, long started)
        {
            double ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (!Stages.TryGetValue(stage, out Counters counters))
                Stages[stage] = counters = new();
            counters.Count++;
            counters.TotalMs += ms;
            counters.MaxMs = Math.Max(counters.MaxMs, ms);
            counters.WaitMs += _stageWaitMs;
            if (ms > GameLoop.TickDuration) counters.OverBudget++;

            long now = Stopwatch.GetTimestamp();
            if (_reportAt == 0) _reportAt = now + 60 * Stopwatch.Frequency;
            if (now < _reportAt) return;
            _reportAt = now + 60 * Stopwatch.Frequency;
            foreach (var pair in Stages)
            {
                Counters value = pair.Value;
                if (value.Count == 0) continue;
                Log.Info(FormattableString.Invariant($"SERVER_WORK stage={pair.Key} samples={value.Count} avgMs={value.TotalMs / value.Count:F3} maxMs={value.MaxMs:F3} barrierAvgMs={value.WaitMs / value.Count:F3} overBudget={value.OverBudget}"));
                value.Count = value.OverBudget = 0;
                value.TotalMs = value.MaxMs = value.WaitMs = 0;
            }
        }
    }
}
