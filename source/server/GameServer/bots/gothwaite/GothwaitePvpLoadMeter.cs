using System;
using System.Diagnostics;
using System.Threading;
using DOL.Logging;

namespace DOL.GS;

/// <summary>The timed parts of the Gothwaite PvP load: a mode bot's whole turn, its opponent scan and the castle former tick.</summary>
public enum GothwaitePvpLoadKind { Turn = 0, Scan = 1, Former = 2 }

/// <summary>
/// One finished load window (or the merge of the last two). Times are in milliseconds; the turn p95 is the upper bound of its
/// histogram bucket, clamped to the largest turn seen, so it is an upper bound and never above the maximum.
/// </summary>
public sealed record GothwaitePvpLoadSnapshot(double WindowSeconds, long Turns, double TurnAverageMilliseconds,
    double TurnP95Milliseconds, double TurnMaxMilliseconds, long Scans, double ScanAverageMilliseconds,
    long Formers, double FormerAverageMilliseconds, double FormerMaxMilliseconds);

/// <summary>
/// The Gothwaite PvP load meter (docs/GOTHWAITE_PVP_MODE.md section 6.8, stage 4). It times every mode bot turn (the whole brain
/// turn of a mode bot, the driver and the class routine together), each opponent scan and each castle former tick, in windows of
/// one real minute. While the mode is on, every finished minute writes one GOTH_PVP_LOAD line, so the load run can be read from the
/// server log even when the minute line of BOT_THINK_PROFILE leaves a phase out. Recording is lock-free (interlocked adds, as the
/// profiler does); the rotation runs on the service thread only. The hooks run only for mode bots and for the former while the mode is on,
/// so nothing is recorded with the mode off.
/// </summary>
public static class GothwaitePvpLoadMeter
{
    /// <summary>The real-time length of one window, in milliseconds.</summary>
    public const long WindowMilliseconds = 60_000;
    /// <summary>The fraction the turn percentile reports (95 per cent of the turns are at or below it).</summary>
    public const double PercentileFraction = 0.95;

    /// <summary>Upper bounds of the turn histogram buckets, in milliseconds. One more bucket holds every longer turn.</summary>
    public static readonly double[] BucketUpperMilliseconds = { 0.25, 0.5, 1, 2, 5, 10, 25, 50, 100, 250, 1000 };

    private static readonly int KindCount = Enum.GetValues<GothwaitePvpLoadKind>().Length;
    private static readonly Logger Log = LoggerManager.Create(typeof(GothwaitePvpLoadMeter));
    private static Window _current = new(Environment.TickCount64);
    private static Window _previous;

    /// <summary>Times one mode bot turn. Use with <c>using</c>.</summary>
    public static GothwaitePvpLoadScope MeasureTurn() => new(GothwaitePvpLoadKind.Turn);

    /// <summary>Times one opponent scan. Use with <c>using</c>.</summary>
    public static GothwaitePvpLoadScope MeasureScan() => new(GothwaitePvpLoadKind.Scan);

    /// <summary>Times one castle former tick. Use with <c>using</c>.</summary>
    public static GothwaitePvpLoadScope MeasureFormer() => new(GothwaitePvpLoadKind.Former);

    /// <summary>Adds one measured duration of a kind to the current window. The pure entry point; the scopes call it.</summary>
    public static void RecordMilliseconds(GothwaitePvpLoadKind kind, double milliseconds) =>
        Volatile.Read(ref _current).Add(kind, milliseconds);

    /// <summary>The histogram bucket of a turn: the first bound it does not exceed, or the last bucket for a longer turn.</summary>
    public static int BucketOf(double milliseconds)
    {
        int index = 0;
        while (index < BucketUpperMilliseconds.Length && milliseconds > BucketUpperMilliseconds[index])
            index++;
        return index;
    }

    /// <summary>
    /// The percentile of a turn histogram (pure). A finite bucket reports its bound, never above the largest turn; the last bucket
    /// reports the largest turn. Zero when there are no turns.
    /// </summary>
    public static double Percentile(long[] buckets, long total, double maxMilliseconds, double fraction)
    {
        if (buckets == null || total <= 0)
            return 0;
        long target = Math.Max(1, (long)Math.Ceiling(total * fraction));
        long cumulative = 0;
        for (int index = 0; index < buckets.Length; index++)
        {
            cumulative += buckets[index];
            if (cumulative < target)
                continue;
            return index < BucketUpperMilliseconds.Length ? Math.Min(BucketUpperMilliseconds[index], maxMilliseconds) : maxMilliseconds;
        }
        return maxMilliseconds;
    }

    /// <summary>True once the current window has run its minute (real time).</summary>
    public static bool IsWindowDue(long nowMilliseconds) =>
        nowMilliseconds - Volatile.Read(ref _current).StartedMilliseconds >= WindowMilliseconds;

    /// <summary>
    /// Ends the current window when it is due (called from the service thread once per service tick, a read when it is not due) and
    /// writes its GOTH_PVP_LOAD line. <paramref name="onlineBots"/> is read only then.
    /// </summary>
    public static void PublishIfDue(long nowMilliseconds, Func<int> onlineBots)
    {
        if (!IsWindowDue(nowMilliseconds))
            return;
        GothwaitePvpLoadSnapshot finished = FinishWindow(nowMilliseconds);
        try
        {
            int bots = onlineBots == null ? 0 : onlineBots();
            if (Log.IsInfoEnabled)
                Log.Info(GothwaitePvpLines.Load(finished, bots));
        }
        catch (Exception exception)
        {
            Log.Warn($"GOTH_PVP_LOAD could not be written: {exception.Message}");
        }
    }

    /// <summary>Ends the current window at <paramref name="nowMilliseconds"/>, keeps it for the next status merge, and returns its numbers.</summary>
    public static GothwaitePvpLoadSnapshot FinishWindow(long nowMilliseconds)
    {
        Window finished = Volatile.Read(ref _current);
        GothwaitePvpLoadSnapshot snapshot = Describe(nowMilliseconds, finished);
        Volatile.Write(ref _previous, finished);
        Volatile.Write(ref _current, new Window(nowMilliseconds));
        return snapshot;
    }

    /// <summary>
    /// The numbers of the status command: the last finished window and the current one together, so the figures cover one to two
    /// real minutes. Zero turns when nothing was measured.
    /// </summary>
    public static GothwaitePvpLoadSnapshot Snapshot(long nowMilliseconds) =>
        Describe(nowMilliseconds, Volatile.Read(ref _previous), Volatile.Read(ref _current));

    /// <summary>Clears both windows and starts a new one at <paramref name="nowMilliseconds"/>. Tests use it; the server never does.</summary>
    public static void Reset(long nowMilliseconds)
    {
        Volatile.Write(ref _previous, null);
        Volatile.Write(ref _current, new Window(nowMilliseconds));
    }

    private static GothwaitePvpLoadSnapshot Describe(long nowMilliseconds, params Window[] windows)
    {
        var counts = new long[KindCount];
        var microseconds = new long[KindCount];
        var maxMicroseconds = new long[KindCount];
        var buckets = new long[BucketUpperMilliseconds.Length + 1];
        long started = nowMilliseconds;
        foreach (Window window in windows)
        {
            if (window == null)
                continue;
            started = Math.Min(started, window.StartedMilliseconds);
            for (int kind = 0; kind < KindCount; kind++)
            {
                counts[kind] += Volatile.Read(ref window.Counts[kind]);
                microseconds[kind] += Volatile.Read(ref window.Microseconds[kind]);
                maxMicroseconds[kind] = Math.Max(maxMicroseconds[kind], Volatile.Read(ref window.MaxMicroseconds[kind]));
            }
            for (int bucket = 0; bucket < buckets.Length; bucket++)
                buckets[bucket] += Volatile.Read(ref window.Buckets[bucket]);
        }

        int turn = (int)GothwaitePvpLoadKind.Turn, scan = (int)GothwaitePvpLoadKind.Scan, former = (int)GothwaitePvpLoadKind.Former;
        double seconds = Math.Max(0, nowMilliseconds - started) / 1000d;
        long turns = counts[turn];
        double maxTurn = maxMicroseconds[turn] / 1000d;
        return new GothwaitePvpLoadSnapshot(
            seconds,
            turns,
            Average(microseconds[turn], turns),
            Percentile(buckets, turns, maxTurn, PercentileFraction),
            maxTurn,
            counts[scan],
            Average(microseconds[scan], counts[scan]),
            counts[former],
            Average(microseconds[former], counts[former]),
            maxMicroseconds[former] / 1000d);
    }

    private static double Average(long microseconds, long count) => count <= 0 ? 0 : microseconds / 1000d / count;

    /// <summary>One window of counts. Every field is updated with interlocked operations, so turns on many threads never lock.</summary>
    private sealed class Window
    {
        public readonly long StartedMilliseconds;
        public readonly long[] Counts = new long[Enum.GetValues<GothwaitePvpLoadKind>().Length];
        public readonly long[] Microseconds = new long[Enum.GetValues<GothwaitePvpLoadKind>().Length];
        public readonly long[] MaxMicroseconds = new long[Enum.GetValues<GothwaitePvpLoadKind>().Length];
        public readonly long[] Buckets = new long[BucketUpperMilliseconds.Length + 1];

        public Window(long startedMilliseconds) => StartedMilliseconds = startedMilliseconds;

        public void Add(GothwaitePvpLoadKind kind, double milliseconds)
        {
            int index = (int)kind;
            long micro = (long)Math.Round(Math.Max(0d, milliseconds) * 1000d);
            Interlocked.Increment(ref Counts[index]);
            Interlocked.Add(ref Microseconds[index], micro);
            long max = Volatile.Read(ref MaxMicroseconds[index]);
            while (micro > max)
            {
                long seen = Interlocked.CompareExchange(ref MaxMicroseconds[index], micro, max);
                if (seen == max)
                    break;
                max = seen;
            }
            if (kind == GothwaitePvpLoadKind.Turn)
                Interlocked.Increment(ref Buckets[BucketOf(milliseconds)]);
        }
    }
}

/// <summary>A timed scope of the load meter: the elapsed time of its kind is recorded on dispose.</summary>
public readonly struct GothwaitePvpLoadScope : IDisposable
{
    private readonly GothwaitePvpLoadKind _kind;
    private readonly long _started;

    internal GothwaitePvpLoadScope(GothwaitePvpLoadKind kind)
    {
        _kind = kind;
        _started = Stopwatch.GetTimestamp();
    }

    public void Dispose() =>
        GothwaitePvpLoadMeter.RecordMilliseconds(_kind, BotThinkProfiler.ToMilliseconds(Stopwatch.GetTimestamp() - _started));
}
