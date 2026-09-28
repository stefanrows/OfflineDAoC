using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// Bug 56 round 2: merchant lists and the stable network refresh off the brain
/// turn, the stable-route corridor checks run in slices, and lock waits are
/// timed. Decisions stay those of the former one-turn code.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_BotThinkCostBoundsRound2
{
    private readonly List<Action> _queued = new();

    [SetUp]
    public void QueueBackgroundWork()
    {
        _queued.Clear();
        BackgroundCacheRefresh.Executor = work => _queued.Add(work);
    }

    [TearDown]
    public void RestoreBackgroundWorker() => BackgroundCacheRefresh.Executor = BackgroundCacheRefresh.DefaultExecutor;

    private void RunQueued()
    {
        Action[] work = _queued.ToArray();
        _queued.Clear();
        foreach (Action action in work)
            action();
    }

    // ---- merchant lists and other refreshed values -------------------------

    [Test]
    public void RefreshedValueLoadsOnceAndRefreshesOnlyAfterItsLifetime()
    {
        int loads = 0;
        var value = new BackgroundRefreshedValue<string>(() => $"v{++loads}", 300_000, 30_000);
        Assert.That(value.Get(0), Is.EqualTo("v1"), "the first load is synchronous");
        Assert.That(value.Get(299_999), Is.EqualTo("v1"));
        Assert.That(loads, Is.EqualTo(1));
        Assert.That(_queued, Is.Empty);

        Assert.That(value.Get(300_000), Is.EqualTo("v1"), "an expired value is still served");
        Assert.That(loads, Is.EqualTo(1), "the caller never runs the reload itself");
        Assert.That(_queued, Has.Count.EqualTo(1));
        Assert.That(value.Get(300_001), Is.EqualTo("v1"));
        Assert.That(_queued, Has.Count.EqualTo(1), "one refresh at a time");

        RunQueued();
        Assert.That(loads, Is.EqualTo(2));
        Assert.That(value.Get(300_002), Is.EqualTo("v2"));
        Assert.That(value.Get(599_999), Is.EqualTo("v2"));
        Assert.That(_queued, Is.Empty, "the lifetime restarts at the refresh request");
    }

    [Test]
    public void FailedRefreshKeepsTheListAndRetriesAfterThirtySeconds()
    {
        bool fail = false;
        int loads = 0;
        var value = new BackgroundRefreshedValue<string>(() =>
        {
            loads++;
            if (fail) throw new InvalidOperationException("database busy");
            return $"v{loads}";
        }, 300_000, 30_000);
        value.Get(0);
        fail = true;
        value.Get(300_000);
        Assert.Throws<InvalidOperationException>(RunQueued, "the worker logs the failure");
        Assert.That(value.Get(300_001), Is.EqualTo("v1"));
        Assert.That(value.IsRefreshing, Is.False);
        Assert.That(_queued, Is.Empty, "no retry storm before the retry delay");

        fail = false;
        Assert.That(value.Get(330_000), Is.EqualTo("v1"));
        RunQueued();
        Assert.That(value.Get(330_001), Is.EqualTo("v3"));
    }

    [Test]
    public void AFailedFirstLoadIsRetriedByTheNextCaller()
    {
        int loads = 0;
        var value = new BackgroundRefreshedValue<string>(() =>
        {
            if (++loads == 1) throw new InvalidOperationException("no database yet");
            return "loaded";
        }, 300_000, 30_000);
        Assert.Throws<InvalidOperationException>(() => value.Get(0));
        Assert.That(value.HasValue, Is.False);
        Assert.That(value.Get(1), Is.EqualTo("loaded"));
    }

    [Test]
    public void AnExpiredListNeverBlocksABrainTurnOnTheRealWorker()
    {
        BackgroundCacheRefresh.Executor = BackgroundCacheRefresh.DefaultExecutor;
        using var release = new ManualResetEventSlim();
        using var refreshed = new ManualResetEventSlim();
        int loads = 0;
        var value = new BackgroundRefreshedValue<string>(() =>
        {
            if (Interlocked.Increment(ref loads) == 1) return "old";
            // A SELECT queued behind pending saves on the old hard disk.
            release.Wait(TimeSpan.FromSeconds(10));
            refreshed.Set();
            return "new";
        }, 1_000, 1_000);
        value.Get(0);

        var watch = Stopwatch.StartNew();
        string seen = value.Get(5_000);
        watch.Stop();
        Assert.That(seen, Is.EqualTo("old"));
        Assert.That(watch.ElapsedMilliseconds, Is.LessThan(250), "the caller waited for the database");

        release.Set();
        Assert.That(refreshed.Wait(TimeSpan.FromSeconds(10)), Is.True);
        SpinUntil(() => !value.IsRefreshing);
        Assert.That(value.Get(5_001), Is.EqualTo("new"));
    }

    [Test]
    public void MerchantListWithoutADatabaseListStillReturnsScriptedItems()
    {
        var merchant = new MerchantTradeItems(null);
        var template = new DOL.Database.DbItemTemplate { Id_nb = "test_ticket", Name = "test ticket" };
        Assert.That(merchant.AddTradeItem(0, eMerchantWindowSlot.FirstInPage, template), Is.True);
        IDictionary items = merchant.GetAllItems();
        Assert.That(items.Count, Is.EqualTo(1));
        Assert.That(items[0], Is.SameAs(template));
        Assert.That(_queued, Is.Empty, "an unexpired list starts no refresh");
    }

    // ---- stable network cache ----------------------------------------------

    [Test]
    public void ExpiredStableNetworkIsRebuiltOffTheBrainTurn()
    {
        var cache = new AutonomousRefreshingCache<int, string>(TimeSpan.FromMinutes(30), refreshInBackground: true);
        int builds = 0;
        Assert.That(cache.Get(1, 0, () => $"v{++builds}"), Is.EqualTo("v1"), "the first build is synchronous");

        int callerThread = Environment.CurrentManagedThreadId;
        int buildThread = -1;
        Assert.That(cache.Get(1, 30 * 60_000, () =>
        {
            buildThread = Environment.CurrentManagedThreadId;
            return $"v{++builds}";
        }), Is.EqualTo("v1"), "the brain turn keeps the previous network");
        Assert.That(builds, Is.EqualTo(1));
        Assert.That(cache.Get(1, 30 * 60_000 + 1, () => $"v{++builds}"), Is.EqualTo("v1"));
        Assert.That(_queued, Has.Count.EqualTo(1), "one rebuild at a time");

        Task.Run(RunQueued).Wait();
        Assert.That(buildThread, Is.Not.EqualTo(callerThread));
        Assert.That(cache.Get(1, 30 * 60_000 + 2, () => "unused"), Is.EqualTo("v2"));
        Assert.That(cache.Get(1, 60 * 60_000 - 1, () => "unused"), Is.EqualTo("v2"));
    }

    [Test]
    public void FailedBackgroundRebuildKeepsTheOldNetworkAndRetries()
    {
        var cache = new AutonomousRefreshingCache<int, string>(TimeSpan.FromMinutes(30), refreshInBackground: true);
        cache.Get(1, 0, () => "old");
        Assert.That(cache.Get(1, 30 * 60_000, () => throw new InvalidOperationException("db busy")), Is.EqualTo("old"));
        Assert.Throws<InvalidOperationException>(RunQueued);
        Assert.That(cache.Get(1, 30 * 60_000 + 29_999, () => "unused"), Is.EqualTo("old"));
        Assert.That(_queued, Is.Empty);
        Assert.That(cache.Get(1, 30 * 60_000 + 30_000, () => "new"), Is.EqualTo("old"));
        RunQueued();
        Assert.That(cache.Get(1, 30 * 60_000 + 30_001, () => "unused"), Is.EqualTo("new"));
    }

    [Test]
    public void BackgroundRebuildNeverBlocksACallerBeyondAFewMilliseconds()
    {
        BackgroundCacheRefresh.Executor = BackgroundCacheRefresh.DefaultExecutor;
        var cache = new AutonomousRefreshingCache<int, string>(TimeSpan.FromSeconds(1), refreshInBackground: true);
        cache.Get(1, 0, () => "old");
        using var release = new ManualResetEventSlim();
        var watch = Stopwatch.StartNew();
        string seen = cache.Get(1, 5_000, () =>
        {
            release.Wait(TimeSpan.FromSeconds(10)); // the ~1 s rebuild of 2026-09-28
            return "new";
        });
        watch.Stop();
        Assert.That(seen, Is.EqualTo("old"));
        Assert.That(watch.ElapsedMilliseconds, Is.LessThan(250));
        release.Set();
        SpinUntil(() => cache.Get(1, 5_001, () => "unused") == "new");
    }

    // ---- stable-route first-leg corridor checks ----------------------------

    private sealed class Legs
    {
        public long Now;
        public long CheckCostMilliseconds;
        public int Checks;
        public readonly bool[] SameZone;
        public readonly bool[] Corridor;
        public readonly HashSet<int> PreExcluded;
        public readonly AutonomousStableRoutePlanner.LegMetric[] Metrics;

        public Legs(int seed, int count)
        {
            var random = new Random(seed);
            SameZone = Enumerable.Range(0, count).Select(_ => random.Next(4) != 0).ToArray();
            Corridor = Enumerable.Range(0, count).Select(_ => random.Next(3) != 0).ToArray();
            PreExcluded = Enumerable.Range(0, count).Where(_ => random.Next(6) == 0).ToHashSet();
            Metrics = Enumerable.Range(0, count).Select(index => new AutonomousStableRoutePlanner.LegMetric(index,
                random.Next(0, 40_000), random.Next(0, 40_000), random.Next(0, 60_000), random.Next(0, 60_000),
                random.Next(20, 400), random.Next(0, 3) * 50)).ToArray();
        }

        public bool Check(int index)
        {
            Checks++;
            Now += CheckCostMilliseconds;
            return Corridor[index];
        }

        public bool Needs(int index) => SameZone[index] && !PreExcluded.Contains(index);
    }

    // The one-turn loop of FindBest before round 2, kept as the oracle: it
    // proved every same-zone leg and excluded those without a corridor.
    private static HashSet<int> ReferenceExclusions(Legs legs)
    {
        var excluded = new HashSet<int>(legs.PreExcluded);
        for (int index = 0; index < legs.SameZone.Length; index++)
        {
            bool complete = !legs.SameZone[index] || legs.Corridor[index];
            if (!AutonomousStableRoutePlanner.CanUseAsFirstBoardingLeg(legs.SameZone[index], complete))
                excluded.Add(index);
        }
        return excluded;
    }

    [Test]
    public void SlicedCorridorScanChoosesTheSameFirstLegAsTheOneTurnLoop()
    {
        int compared = 0, horses = 0;
        foreach (int seed in Enumerable.Range(0, 60))
        foreach (long cost in new long[] { 0, 3, 30 })
        {
            int count = 1 + seed % 23;
            var reference = new Legs(seed, count);
            AutonomousStableRoutePlanner.RouteDecision? expected = AutonomousStableRoutePlanner.ChooseFirstLeg(
                1_000, 2_000, 50_000, 45_000, 191, 200, reference.Metrics, ReferenceExclusions(reference));

            var legs = new Legs(seed, count) { CheckCostMilliseconds = cost };
            var scan = new AutonomousFirstLegCorridorScan(count, legs.Needs, legs.Check, 8, () => legs.Now);
            int turns = 0;
            while (scan.Continue() == AutonomousFirstLegCorridorScan.Outcome.Pending)
                Assert.That(++turns, Is.LessThan(count + 1), "a scan always finishes");
            var excluded = new HashSet<int>(legs.PreExcluded);
            excluded.UnionWith(scan.Blocked);
            AutonomousStableRoutePlanner.RouteDecision? actual = AutonomousStableRoutePlanner.ChooseFirstLeg(
                1_000, 2_000, 50_000, 45_000, 191, 200, legs.Metrics, excluded);

            Assert.That(actual, Is.EqualTo(expected), $"seed {seed} cost {cost}");
            Assert.That(legs.Checks, Is.EqualTo(Enumerable.Range(0, count).Count(legs.Needs)),
                "every leg that needs proof is checked exactly once");
            if (expected.HasValue) horses++;
            compared++;
        }
        Assert.That(compared, Is.EqualTo(180));
        Assert.That(horses, Is.GreaterThan(30), "the fake network must choose real horses");
    }

    [Test]
    public void SlowCorridorChecksAreSpreadOverTurnsWithOneSlicePerTurn()
    {
        // Live 2026-09-28 (SSD run): 8 corridor checks took 216 ms in one turn.
        var legs = new Legs(11, 20) { CheckCostMilliseconds = 27 };
        for (int index = 0; index < 20; index++)
            legs.SameZone[index] = true;
        legs.PreExcluded.Clear();
        var scan = new AutonomousFirstLegCorridorScan(20, legs.Needs, legs.Check,
            AutonomousStableRoutePlanner.Search.SliceMilliseconds, () => legs.Now);
        var perTurn = new List<long>();
        AutonomousFirstLegCorridorScan.Outcome outcome;
        do
        {
            long started = legs.Now;
            outcome = scan.Continue();
            perTurn.Add(legs.Now - started);
        } while (outcome == AutonomousFirstLegCorridorScan.Outcome.Pending);

        Assert.That(perTurn.Max(), Is.LessThanOrEqualTo(AutonomousStableRoutePlanner.Search.SliceMilliseconds + 27),
            "a turn spends at most its slice plus one check");
        Assert.That(scan.Slices, Is.EqualTo(20));
        Assert.That(scan.Checks, Is.EqualTo(20));
    }

    [Test]
    public void PendingStableSearchBelongsOnlyToItsStartGoalAndRegion()
    {
        var origin = new System.Numerics.Vector2(10_000, 20_000);
        var goal = new System.Numerics.Vector3(60_000, 40_000, 100);
        bool Valid(long age, ushort region, float x, float y, System.Numerics.Vector3 target) =>
            AutonomousStableRoutePlanner.Search.IsStillValid(51, origin, goal, age, region,
                new(x, y, 0), target);

        Assert.That(Valid(250, 51, 10_300, 20_200, goal), Is.True, "a bot that kept walking a little");
        Assert.That(Valid(250, 51, 11_100, 20_000, goal), Is.False,
            "a combat pause and resume far from the start snapshot starts over");
        Assert.That(Valid(250, 51, 10_000, 20_000, goal + new System.Numerics.Vector3(600, 0, 0)), Is.False,
            "the goal moved");
        Assert.That(Valid(250, 52, 10_000, 20_000, goal), Is.False, "another region");
        Assert.That(Valid(AutonomousStableRoutePlanner.Search.MaximumAgeMilliseconds + 1, 51, 10_000, 20_000, goal),
            Is.False, "too old");
    }

    [Test]
    public void CheapCorridorChecksStillFinishInOneTurn()
    {
        var legs = new Legs(5, 40);
        var scan = new AutonomousFirstLegCorridorScan(40, legs.Needs, legs.Check,
            AutonomousStableRoutePlanner.Search.SliceMilliseconds, () => legs.Now);
        Assert.That(scan.Continue(), Is.EqualTo(AutonomousFirstLegCorridorScan.Outcome.Done));
        Assert.That(scan.Slices, Is.EqualTo(1));
    }

    [Test]
    public void AlreadyExcludedLegsNeedNoCorridorProof()
    {
        // Excluding a leg twice cannot change the choice, so its proof is skipped.
        var legs = new Legs(3, 12);
        for (int index = 0; index < 12; index++)
            legs.SameZone[index] = true;
        legs.PreExcluded.Clear();
        legs.PreExcluded.UnionWith([0, 1, 2, 3]);
        var scan = new AutonomousFirstLegCorridorScan(12, legs.Needs, legs.Check, long.MaxValue, () => legs.Now);
        Assert.That(scan.Continue(), Is.EqualTo(AutonomousFirstLegCorridorScan.Outcome.Done));
        Assert.That(legs.Checks, Is.EqualTo(8));
    }

    // ---- lock waits and database opens in the profile ----------------------

    [Test]
    public void MeasuredLockTimesOnlyTheWaitAndBehavesLikeALock()
    {
        object gate = new();
        BotThinkProfiler.Swap(0);
        using (BotThinkProfiler.Lock(gate, BotThinkPhase.CoordinatorLockWait))
        using (BotThinkProfiler.Lock(gate, BotThinkPhase.CoordinatorLockWait)) // reentrant like lock
            Assert.That(Monitor.IsEntered(gate), Is.True);
        Assert.That(Monitor.IsEntered(gate), Is.False);
        Assert.That(BotThinkProfiler.Swap(0).CallCount(BotThinkPhase.CoordinatorLockWait), Is.Zero,
            "an uncontended lock records no wait");

        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task holder = Task.Run(() =>
        {
            lock (gate)
            {
                held.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }
        });
        Assert.That(held.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Task.Delay(60).ContinueWith(_ => release.Set());
        using (BotThinkProfiler.Lock(gate, BotThinkPhase.RvrEventLockWait)) { }
        holder.Wait();
        BotThinkProfiler.Window window = BotThinkProfiler.Swap(60_000);
        Assert.That(window.CallCount(BotThinkPhase.RvrEventLockWait), Is.EqualTo(1));
        Assert.That(window.TotalMilliseconds(BotThinkPhase.RvrEventLockWait), Is.GreaterThanOrEqualTo(40));
    }

    [Test]
    public void DatabaseOpensCountOnlyInsideABrainTurn()
    {
        BotThinkProfiler.Swap(0);
        long tenMilliseconds = Stopwatch.Frequency / 100;
        // The profiler wires itself to SQLite connection opens.
        Assert.That(DOL.Database.Handlers.SqliteObjectDatabase.ConnectionOpenObserver, Is.Not.Null);
        DOL.Database.Handlers.SqliteObjectDatabase.ConnectionOpenObserver(tenMilliseconds);
        Assert.That(BotThinkProfiler.InTurn, Is.False);
        using (BotThinkProfiler.Turn(null))
        {
            Assert.That(BotThinkProfiler.InTurn, Is.True);
            DOL.Database.Handlers.SqliteObjectDatabase.ConnectionOpenObserver(tenMilliseconds);
        }
        BotThinkProfiler.Window window = BotThinkProfiler.Swap(60_000);
        Assert.That(window.CallCount(BotThinkPhase.DatabaseOpen), Is.EqualTo(1),
            "persistence and refresh threads do not count");
        Assert.That(window.TotalMilliseconds(BotThinkPhase.DatabaseOpen), Is.EqualTo(10).Within(0.5));
    }

    [Test]
    public void NewPhasesAppearInTheMinuteProfileByName()
    {
        BotThinkProfiler.Swap(0);
        foreach (BotThinkPhase phase in new[]
                 {
                     BotThinkPhase.RvrChooseDestination, BotThinkPhase.RvrKeepTarget,
                     BotThinkPhase.RvrFrontierTransport, BotThinkPhase.CoordinatorSessionUpdate,
                     BotThinkPhase.MerchantItemsLoad, BotThinkPhase.StableRouteSearchSlice,
                 })
            using (BotThinkProfiler.Measure(phase)) { }
        string line = BotThinkProfiler.Describe(BotThinkProfiler.Swap(60_000), 60_000).First();
        foreach (string name in new[] { "RvrChooseDestination=", "RvrKeepTarget=", "RvrFrontierTransport=",
                     "CoordinatorSessionUpdate=", "MerchantItemsLoad=", "StableRouteSearchSlice=" })
            Assert.That(line, Does.Contain(name));
    }

    private static void SpinUntil(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)), "background refresh did not finish");
            Thread.Sleep(5);
        }
    }
}
