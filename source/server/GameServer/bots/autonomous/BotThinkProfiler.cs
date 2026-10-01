using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using DOL.Logging;

namespace DOL.GS;

/// <summary>Timed phases of a bot brain turn and of the reward work that
/// runs next to it. Phases are inclusive and may nest.</summary>
public enum BotThinkPhase
{
    Think,
    StuckWatchdog,
    FrontierThreat,
    WorldControllerTick,
    GroupCoordinatorPulse,
    CoordinatorWatchdogCheck,
    SelectCamp,
    ExecuteRvr,
    KeepTravel,
    TravelAcrossRegions,
    IssuePath,
    ZoneItineraryStep,
    RouteRecoverySearch,
    StableRouteFindBest,
    StableNetworkCache,
    CombatFsm,
    ClassBuffs,
    PetUpkeep,
    CompanionUpgrades,
    NavPathQuery,
    DeathRewards,
    CompanionGearGrant,
    SiegeJob,
    // Bug 56 round 2: finer phases for the RvR turn, the coordinator lock and
    // synchronous database access on a brain turn.
    /// <summary>ChooseRvrDestination: objective list, event choice, roam pick.</summary>
    RvrChooseDestination,
    /// <summary>FindRvrTarget: keep guards, gates and the lord.</summary>
    RvrKeepTarget,
    /// <summary>Frontier porter, medallion merchant and boarding.</summary>
    RvrFrontierTransport,
    /// <summary>Waiting to enter the RvR event layer lock.</summary>
    RvrEventLockWait,
    /// <summary>Waiting to enter the group coordinator lock.</summary>
    CoordinatorLockWait,
    /// <summary>Pulse work while holding the coordinator lock.</summary>
    CoordinatorSessionUpdate,
    /// <summary>Population-wide group maintenance (formation, backfill).</summary>
    CoordinatorMaintenance,
    /// <summary>A merchant list read from SQLite (first load or background refresh).</summary>
    MerchantItemsLoad,
    /// <summary>Opening a SQLite connection, including the wait for the write gate.
    /// Nested like NavPathQuery: the same milliseconds also count in the enclosing
    /// phase (for example MerchantItemsLoad or StableNetworkCache); do not add
    /// phase totals of the minute line together.</summary>
    DatabaseOpen,
    /// <summary>One slice of the stable-route first-leg corridor checks.</summary>
    StableRouteSearchSlice,
}

/// <summary>
/// Always-on, low-cost timing of bot brain turns (bug 56). Each phase adds two
/// timestamp reads. Once per minute one BOT_THINK_PROFILE line summarizes all
/// turns and phases, and up to five BOT_THINK_SLOW lines show the slowest
/// single turns with their own phase breakdown, so a long NpcService tick can
/// be traced to its cause on the live server.
/// </summary>
public static class BotThinkProfiler
{
    public const long LongThinkMilliseconds = 25;
    public const long SlowThinkMilliseconds = 100;
    public const long PublishIntervalMilliseconds = 60_000;
    public const int SlowExamplesPerWindow = 5;
    private const int TopPhases = 16; // round 2 added ten phases

    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
    private static readonly int PhaseCount = Enum.GetValues<BotThinkPhase>().Length;
    private static readonly string[] PhaseNames = Enum.GetNames<BotThinkPhase>();

    [ThreadStatic] private static long[] _turnTicks;
    [ThreadStatic] private static int[] _turnCalls;
    [ThreadStatic] private static int _turnDepth;

    private static Window _window = new(PhaseCount, Environment.TickCount64);
    private static long _nextPublishMilliseconds = Environment.TickCount64 + PublishIntervalMilliseconds;

    static BotThinkProfiler()
    {
        // Only opens on a brain thread count: the persistence and refresh
        // threads open connections all the time and would drown the signal.
        DOL.Database.Handlers.SqliteObjectDatabase.ConnectionOpenObserver =
            ticks => RecordInTurn(BotThinkPhase.DatabaseOpen, ticks);
    }

    /// <summary>Adds an already measured duration (Stopwatch ticks) to a phase.</summary>
    public static void Add(BotThinkPhase phase, long elapsedStopwatchTicks) => Record(phase, elapsedStopwatchTicks);

    /// <summary>Like <see cref="Add"/>, but only while this thread runs a brain turn.</summary>
    public static void RecordInTurn(BotThinkPhase phase, long elapsedStopwatchTicks)
    {
        if (_turnDepth > 0)
            Record(phase, elapsedStopwatchTicks);
    }

    /// <summary>True while this thread is inside a timed brain turn.</summary>
    public static bool InTurn => _turnDepth > 0;

    /// <summary>Enters <paramref name="gate"/> and adds any time spent waiting
    /// for it to <paramref name="waitPhase"/>. Exit with Monitor.Exit.</summary>
    public static void EnterMeasured(object gate, BotThinkPhase waitPhase)
    {
        if (Monitor.TryEnter(gate))
            return;
        long started = Stopwatch.GetTimestamp();
        Monitor.Enter(gate);
        Record(waitPhase, Stopwatch.GetTimestamp() - started);
    }

    /// <summary>A <c>using</c>-scoped lock that measures its wait.</summary>
    public readonly struct MeasuredLock : IDisposable
    {
        private readonly object _gate;

        internal MeasuredLock(object gate) => _gate = gate;

        public void Dispose() => Monitor.Exit(_gate);
    }

    /// <summary>Use as <c>using (BotThinkProfiler.Lock(gate, phase))</c> in place
    /// of <c>lock (gate)</c>; same monitor, same reentrancy.</summary>
    public static MeasuredLock Lock(object gate, BotThinkPhase waitPhase)
    {
        EnterMeasured(gate, waitPhase);
        return new MeasuredLock(gate);
    }

    public readonly record struct SlowTurn(string Bot, string Kind, long Milliseconds, int Interval, string Phases);

    public sealed class Window
    {
        public readonly long StartedMilliseconds;
        public readonly long[] Ticks;
        public readonly long[] Calls;
        public readonly long[] MaxTicks;
        public long Thinks, LongThinks, SlowThinks, VerySlowThinks;
        public readonly List<SlowTurn> Slowest = new();

        public Window(int phases, long started)
        {
            StartedMilliseconds = started;
            Ticks = new long[phases];
            Calls = new long[phases];
            MaxTicks = new long[phases];
        }

        public double TotalMilliseconds(BotThinkPhase phase) => ToMilliseconds(Ticks[(int)phase]);
        public long CallCount(BotThinkPhase phase) => Calls[(int)phase];
        public double MaxMilliseconds(BotThinkPhase phase) => ToMilliseconds(MaxTicks[(int)phase]);
    }

    public readonly struct Scope : IDisposable
    {
        private readonly BotThinkPhase _phase;
        private readonly long _started;

        internal Scope(BotThinkPhase phase)
        {
            _phase = phase;
            _started = Stopwatch.GetTimestamp();
        }

        public void Dispose() => Record(_phase, Stopwatch.GetTimestamp() - _started);
    }

    public readonly struct TurnScope : IDisposable
    {
        private readonly GameNPC _body;
        private readonly long _started;
        private readonly bool _outermost;

        internal TurnScope(GameNPC body)
        {
            _body = body;
            _outermost = _turnDepth++ == 0;
            if (_outermost)
            {
                _turnTicks ??= new long[PhaseCount];
                _turnCalls ??= new int[PhaseCount];
                Array.Clear(_turnTicks);
                Array.Clear(_turnCalls);
            }
            _started = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            long elapsed = Stopwatch.GetTimestamp() - _started;
            Record(BotThinkPhase.Think, elapsed);
            _turnDepth = Math.Max(0, _turnDepth - 1);
            if (_outermost)
                CompleteTurn(_body, elapsed);
        }
    }

    /// <summary>Times one phase. Use with <c>using</c>.</summary>
    public static Scope Measure(BotThinkPhase phase) => new(phase);

    /// <summary>Times a whole brain turn and collects its phase breakdown.</summary>
    public static TurnScope Turn(GameNPC body) => new(body);

    public static double ToMilliseconds(long stopwatchTicks) => stopwatchTicks * 1000d / Stopwatch.Frequency;

    private static long FromMilliseconds(long milliseconds) => milliseconds * Stopwatch.Frequency / 1000;

    private static void Record(BotThinkPhase phase, long elapsedTicks)
    {
        if (elapsedTicks < 0)
            elapsedTicks = 0;
        int index = (int)phase;
        Window window = Volatile.Read(ref _window);
        Interlocked.Add(ref window.Ticks[index], elapsedTicks);
        Interlocked.Increment(ref window.Calls[index]);
        long max = Volatile.Read(ref window.MaxTicks[index]);
        while (elapsedTicks > max)
        {
            long seen = Interlocked.CompareExchange(ref window.MaxTicks[index], elapsedTicks, max);
            if (seen == max)
                break;
            max = seen;
        }

        if (_turnDepth > 0 && _turnTicks != null)
        {
            _turnTicks[index] += elapsedTicks;
            _turnCalls[index]++;
        }
    }

    private static void CompleteTurn(GameNPC body, long elapsedTicks)
    {
        Window window = Volatile.Read(ref _window);
        Interlocked.Increment(ref window.Thinks);
        if (elapsedTicks < FromMilliseconds(LongThinkMilliseconds))
            return;
        Interlocked.Increment(ref window.LongThinks);
        if (elapsedTicks < FromMilliseconds(SlowThinkMilliseconds))
            return;
        Interlocked.Increment(ref window.SlowThinks);
        if (elapsedTicks >= FromMilliseconds(1_000))
            Interlocked.Increment(ref window.VerySlowThinks);

        long milliseconds = (long)ToMilliseconds(elapsedTicks);
        lock (window.Slowest)
        {
            if (window.Slowest.Count >= SlowExamplesPerWindow &&
                window.Slowest.Min(turn => turn.Milliseconds) >= milliseconds)
                return;
        }

        var turn = new SlowTurn(body?.Name ?? "?", KindOf(body), milliseconds,
            body?.Brain?.ThinkInterval ?? 0, DescribeTurnPhases());
        lock (window.Slowest)
        {
            window.Slowest.Add(turn);
            if (window.Slowest.Count > SlowExamplesPerWindow)
                window.Slowest.Remove(window.Slowest.MinBy(entry => entry.Milliseconds));
        }
    }

    private static string KindOf(GameNPC body) => body switch
    {
        GameBot { IsPersistentPlayerCompanion: true } => "Companion",
        GameBot { IsTemporaryGroupHelper: true } => "Helper",
        GameBot { IsAutonomousWorldBot: true } => "WorldBot",
        GameBot => "Bot",
        _ => "Npc",
    };

    private static string DescribeTurnPhases()
    {
        var builder = new StringBuilder();
        foreach (int index in Enumerable.Range(0, PhaseCount)
                     .Where(index => index != (int)BotThinkPhase.Think && _turnCalls[index] > 0)
                     .OrderByDescending(index => _turnTicks[index])
                     .Take(6))
        {
            if (builder.Length > 0)
                builder.Append(';');
            builder.Append(PhaseNames[index]).Append('=')
                .Append(ToMilliseconds(_turnTicks[index]).ToString("0"))
                .Append("ms/").Append(_turnCalls[index]).Append('x');
        }
        return builder.Length == 0 ? "none" : builder.ToString();
    }

    /// <summary>Called once per NpcService tick by the game loop thread.</summary>
    public static void PublishIfDue(long nowMilliseconds)
    {
        long due = Interlocked.Read(ref _nextPublishMilliseconds);
        if (nowMilliseconds < due ||
            Interlocked.CompareExchange(ref _nextPublishMilliseconds, nowMilliseconds + PublishIntervalMilliseconds, due) != due)
            return;
        Window finished = Swap(nowMilliseconds);
        try
        {
            if (Log.IsInfoEnabled && (finished.Thinks > 0 || finished.Calls.Any(count => count > 0)))
                foreach (string line in Describe(finished, nowMilliseconds))
                    Log.Info(line);
            if (Log.IsInfoEnabled && BattleGroupLoadReport.Describe() is string battleGroupLoad)
                Log.Info(battleGroupLoad);
        }
        catch (Exception exception)
        {
            Log.Warn($"BOT_THINK_PROFILE could not be written: {exception.Message}");
        }
    }

    /// <summary>Starts a new window and returns the previous one.</summary>
    public static Window Swap(long nowMilliseconds) =>
        Interlocked.Exchange(ref _window, new Window(PhaseCount, nowMilliseconds));

    public static IEnumerable<string> Describe(Window window, long nowMilliseconds)
    {
        double seconds = Math.Max(0.001, (nowMilliseconds - window.StartedMilliseconds) / 1000d);
        double thinkMs = window.TotalMilliseconds(BotThinkPhase.Think);
        long thinks = Interlocked.Read(ref window.Thinks);
        var phases = new StringBuilder();
        foreach (int index in Enumerable.Range(0, PhaseCount)
                     .Where(index => index != (int)BotThinkPhase.Think && window.Calls[index] > 0)
                     .OrderByDescending(index => window.Ticks[index])
                     .Take(TopPhases))
        {
            if (phases.Length > 0)
                phases.Append(';');
            phases.Append(PhaseNames[index]).Append('=')
                .Append(ToMilliseconds(window.Ticks[index]).ToString("0")).Append("ms/")
                .Append(window.Calls[index]).Append("x/max")
                .Append(ToMilliseconds(window.MaxTicks[index]).ToString("0")).Append("ms");
        }
        yield return $"BOT_THINK_PROFILE windowS={seconds:0} thinks={thinks} thinkMs={thinkMs:0} " +
                     $"avgMs={(thinks == 0 ? 0 : thinkMs / thinks):0.00} over25ms={Interlocked.Read(ref window.LongThinks)} " +
                     $"over100ms={Interlocked.Read(ref window.SlowThinks)} over1000ms={Interlocked.Read(ref window.VerySlowThinks)} " +
                     $"maxMs={window.MaxMilliseconds(BotThinkPhase.Think):0} phases=\"{phases}\"";
        SlowTurn[] slowest;
        lock (window.Slowest)
            slowest = window.Slowest.OrderByDescending(turn => turn.Milliseconds).ToArray();
        foreach (SlowTurn turn in slowest)
            yield return $"BOT_THINK_SLOW bot=\"{turn.Bot}\" kind={turn.Kind} ms={turn.Milliseconds} " +
                         $"interval={turn.Interval} phases=\"{turn.Phases}\"";
    }
}
