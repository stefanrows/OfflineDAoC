using System;
using System.Collections.Generic;
using System.Text;

namespace DOL.GS;

/// <summary>
/// Five-minute <c>MOVE_PACE</c> summary of the speed autonomous world bots
/// are actually told to run: percentiles of the commanded speed of moving
/// bots (one sample per bot every few seconds), split by role (leader,
/// follower, solo) and by flags (stealthed, under a third of health,
/// snared), plus how many leaders looped instead of standing still.
/// Fixed histograms; no per-sample allocation.
/// </summary>
public static class AutonomousMovePace
{
    public const long WindowMilliseconds = 300_000;
    public const long SampleIntervalMilliseconds = 5_000;
    public const int BinWidth = 10;
    public const int BinCount = 41; // 0..400+, the last bin collects everything faster

    public enum Bucket { Leader, Follower, Solo, Stealthed, Hurt, Snared }
    private static readonly string[] BucketNames = ["leader", "follower", "solo", "stealthed", "hurt", "snared"];

    private static readonly object Sync = new();
    private static readonly int[][] Bins = CreateBins();
    private static readonly int[] Counts = new int[BucketNames.Length];
    private static readonly HashSet<long> LoopingLeaders = [];
    private static int _loopStarts;
    private static long _windowStart;

    private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(AutonomousMovePace));

    private static int[][] CreateBins()
    {
        var bins = new int[BucketNames.Length][];
        for (int i = 0; i < bins.Length; i++)
            bins[i] = new int[BinCount];
        return bins;
    }

    // ------------------------------------------------------------ pure rules

    public static int BinOf(int speed) => Math.Clamp(speed / BinWidth, 0, BinCount - 1);

    /// <summary>
    /// The <paramref name="quantile"/> of a speed histogram, as the lower edge
    /// of the bin that holds it; 0 for an empty histogram.
    /// </summary>
    public static int Percentile(int[] bins, int count, double quantile)
    {
        if (bins == null || count <= 0)
            return 0;
        int rank = Math.Max(1, (int)Math.Ceiling(Math.Clamp(quantile, 0, 1) * count));
        int seen = 0;
        for (int i = 0; i < bins.Length; i++)
        {
            seen += bins[i];
            if (seen >= rank)
                return i * BinWidth;
        }
        return (bins.Length - 1) * BinWidth;
    }

    /// <summary>The role of a bot for the summary.</summary>
    public static Bucket RoleOf(bool grouped, bool leader) =>
        !grouped ? Bucket.Solo : leader ? Bucket.Leader : Bucket.Follower;

    // ----------------------------------------------------------- live glue

    /// <summary>One sample of a moving autonomous world bot, at most every five seconds per bot.</summary>
    public static void Sample(GameBot bot, ref long nextSampleTick)
    {
        long now = GameLoop.GameLoopTime;
        if (now < nextSampleTick)
            return;
        nextSampleTick = now + SampleIntervalMilliseconds;
        if (bot?.IsAutonomousWorldBot != true || !bot.IsAlive || !bot.IsMoving)
        {
            FlushIfDue(now);
            return;
        }
        int speed = bot.CurrentSpeed;
        bool grouped = bot.Group != null && bot.Group.MemberCount > 1;
        Bucket role = RoleOf(grouped, grouped && bot.Group.LivingLeader == bot);
        bool stealthed = bot.IsStealthed;
        bool hurt = bot.HealthPercent < 33;
        bool snared = bot.BuffBonusMultCategory1.Get((int)eProperty.MaxSpeed) < 1 ||
            bot.effectListComponent?.ContainsEffectForEffectType(eEffect.MovementSpeedDebuff) == true;
        int bin = BinOf(speed);
        lock (Sync)
        {
            Add(role, bin);
            if (stealthed) Add(Bucket.Stealthed, bin);
            if (hurt) Add(Bucket.Hurt, bin);
            if (snared) Add(Bucket.Snared, bin);
            FlushIfDueLocked(now);
        }
    }

    public static void LoopStarted()
    {
        lock (Sync)
            _loopStarts++;
    }

    public static void NoteLooping(GameBot leader)
    {
        long key = leader.DatabaseID > 0 ? leader.DatabaseID : leader.ObjectID;
        lock (Sync)
            LoopingLeaders.Add(key);
    }

    private static void Add(Bucket bucket, int bin)
    {
        Bins[(int)bucket][bin]++;
        Counts[(int)bucket]++;
    }

    private static void FlushIfDue(long now)
    {
        lock (Sync)
            FlushIfDueLocked(now);
    }

    private static void FlushIfDueLocked(long now)
    {
        if (_windowStart == 0)
            _windowStart = now;
        if (now - _windowStart < WindowMilliseconds)
            return;
        if (Log.IsInfoEnabled)
            Log.Info(Format((now - _windowStart) / 1000));
        for (int i = 0; i < Bins.Length; i++)
        {
            Array.Clear(Bins[i]);
            Counts[i] = 0;
        }
        LoopingLeaders.Clear();
        _loopStarts = 0;
        _windowStart = now;
    }

    private static string Format(long windowSeconds)
    {
        var line = new StringBuilder("MOVE_PACE window_s=").Append(windowSeconds);
        for (int i = 0; i < BucketNames.Length; i++)
            line.Append(' ').Append(BucketNames[i]).Append("=n").Append(Counts[i])
                .Append(":p10=").Append(Percentile(Bins[i], Counts[i], 0.1))
                .Append(",p50=").Append(Percentile(Bins[i], Counts[i], 0.5))
                .Append(",p90=").Append(Percentile(Bins[i], Counts[i], 0.9));
        return line.Append(" looping_leaders=").Append(LoopingLeaders.Count)
            .Append(" loop_starts=").Append(_loopStarts).ToString();
    }
}
