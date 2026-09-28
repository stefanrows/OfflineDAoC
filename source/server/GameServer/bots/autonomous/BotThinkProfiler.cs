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
    private const int TopPhases = 12;

    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
    private static readonly int PhaseCount = Enum.GetValues<BotThinkPhase>().Length;
    private static readonly string[] PhaseNames = Enum.GetNames<BotThinkPhase>();

    [ThreadStatic] private static long[] _turnTicks;
    [ThreadStatic] private static int[] _turnCalls;
    [ThreadStatic] private static int _turnDepth;

    private static Window _window = new(PhaseCount, Environment.TickCount64);
    private static long _nextPublishMilliseconds = Environment.TickCount64 + PublishIntervalMilliseconds;

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
