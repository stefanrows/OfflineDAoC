using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// Bug 56: bot brain turns stalled the NPC service. These tests pin the cost
/// bounds of the fixed paths without changing their decisions.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_BotThinkCostBounds
{
    private sealed class FakeClock
    {
        public long Now;
        public long Read() => Now;
    }

    // Deterministic stand-in geometry: a wall blocks some directions, the
    // surface is shortened by an obstacle, and only some side steps have an
    // onward corridor.
    private sealed class Geometry
    {
        private readonly int _seed;
        public readonly FakeClock Clock = new();
        public int CorridorChecks;
        public int CornerChecks;
        public long CorridorCostMilliseconds;
        public bool CornerFound;

        public Geometry(int seed) => _seed = seed;

        public Vector3? Corner()
        {
            CornerChecks++;
            Clock.Now += 1;
            return CornerFound ? new Vector3(1, 2, 3) : null;
        }

        public Vector3? MoveAlongGround(Vector3 raw)
        {
            int bucket = Math.Abs(((int)raw.X * 31 + (int)raw.Y * 17 + _seed) % 7);
            if (bucket == 0)
                return null;
            float shorten = bucket switch { 1 => 0.3f, 2 => 0.6f, _ => 1f };
            return new Vector3(raw.X * shorten, raw.Y * shorten, raw.Z);
        }

        public bool LineOfSight(Vector3 from, Vector3 to) =>
            Math.Abs(((int)to.X * 13 + (int)to.Y * 7 + _seed) % 5) != 0;

        public bool OnwardCorridor(Vector3 surface)
        {
            CorridorChecks++;
            Clock.Now += CorridorCostMilliseconds;
            return Math.Abs(((int)surface.X * 3 + (int)surface.Y * 11 + _seed) % 3) != 0;
        }
    }

    // The single-turn search as it was before bug 56, kept as the oracle.
    private static bool ReferenceSearch(Geometry geometry, Vector3 current, Vector3 destination, int attempt,
        out Vector3 recovery)
    {
        recovery = default;
        Vector3? corner = geometry.Corner();
        if (corner.HasValue)
        {
            recovery = corner.Value;
            return true;
        }

        Vector2 forward = new(destination.X - current.X, destination.Y - current.Y);
        forward = forward.LengthSquared() < 1f ? new(0, 1) : Vector2.Normalize(forward);
        int[][] angleOrders =
        [
            [90, -90, 135, -135, 180, 45, -45],
            [-90, 90, -135, 135, 180, -45, 45],
            [135, -135, 90, -90, 180, 45, -45],
        ];
        int[] angles = angleOrders[Math.Clamp(attempt - 1, 0, angleOrders.Length - 1)];
        float bestScore = float.MinValue;
        foreach (float radius in new[] { 220f, 340f, 460f })
        {
            foreach (int degrees in angles)
            {
                float radians = degrees * MathF.PI / 180f;
                Vector2 direction = new(
                    forward.X * MathF.Cos(radians) - forward.Y * MathF.Sin(radians),
                    forward.X * MathF.Sin(radians) + forward.Y * MathF.Cos(radians));
                Vector3 raw = new(current.X + direction.X * radius, current.Y + direction.Y * radius, current.Z);
                Vector3? surface = geometry.MoveAlongGround(raw);
                if (!surface.HasValue || Vector3.DistanceSquared(current, surface.Value) < 80 * 80 ||
                    !geometry.LineOfSight(current, surface.Value))
                    continue;
                if (!geometry.OnwardCorridor(surface.Value))
                    continue;
                Vector2 achieved = new(surface.Value.X - current.X, surface.Value.Y - current.Y);
                float lateral = MathF.Abs(forward.X * achieved.Y - forward.Y * achieved.X);
                float clearance = achieved.Length();
                float score = lateral * 2f + clearance;
                if (score <= bestScore)
                    continue;
                bestScore = score;
                recovery = surface.Value;
            }
        }
        return bestScore > float.MinValue;
    }

    private static AutonomousLocalRecoverySearch NewSearch(Geometry geometry, Vector3 current, Vector3 destination,
        int attempt) =>
        new(current, destination, attempt, geometry.Corner, geometry.MoveAlongGround, geometry.LineOfSight,
            geometry.OnwardCorridor, geometry.Clock.Read);

    [Test]
    public void SlicedRecoverySearchChoosesTheSameSideStepAsTheSingleTurnSearch()
    {
        int compared = 0, found = 0;
        foreach (int seed in Enumerable.Range(0, 40))
        foreach (int attempt in new[] { 1, 2, 3 })
        foreach (long cost in new long[] { 0, 3, 20 })
        {
            Vector3 current = new(1000 + seed * 37, 2000 - seed * 11, 50);
            Vector3 destination = new(current.X + 900 - seed * 41, current.Y + 300 + seed * 23, 60);

            var reference = new Geometry(seed) { CorridorCostMilliseconds = cost };
            bool expectedFound = ReferenceSearch(reference, current, destination, attempt, out Vector3 expected);

            var geometry = new Geometry(seed) { CorridorCostMilliseconds = cost };
            AutonomousLocalRecoverySearch search = NewSearch(geometry, current, destination, attempt);
            AutonomousLocalRecoverySearch.Outcome outcome;
            Vector3 actual;
            int calls = 0;
            do
            {
                outcome = search.Continue(out actual);
                calls++;
            } while (outcome == AutonomousLocalRecoverySearch.Outcome.Pending);

            Assert.That(outcome == AutonomousLocalRecoverySearch.Outcome.Found, Is.EqualTo(expectedFound),
                $"seed {seed} attempt {attempt} cost {cost}");
            if (expectedFound)
            {
                Assert.That(actual, Is.EqualTo(expected), $"seed {seed} attempt {attempt} cost {cost}");
                found++;
            }
            Assert.That(geometry.CorridorChecks, Is.EqualTo(reference.CorridorChecks),
                "the sliced search must do exactly the same corridor work in total");
            Assert.That(calls, Is.LessThanOrEqualTo(AutonomousLocalRecoverySearch.CandidateCount + 1));
            compared++;
        }
        Assert.That(compared, Is.EqualTo(360));
        Assert.That(found, Is.GreaterThan(100), "the fake geometry must exercise real side steps");
    }

    [Test]
    public void SlowCorridorChecksAreSpreadAcrossBrainTurns()
    {
        // Live 2026-09-28: a failing corridor check costs tens of milliseconds.
        // Before the fix all 21 ran in one brain turn (100-300 ms stalls).
        var geometry = new Geometry(7) { CorridorCostMilliseconds = 20 };
        AutonomousLocalRecoverySearch search = NewSearch(geometry, new(5000, 5000, 0), new(9000, 5200, 0), 1);
        var perTurn = new List<int>();
        var perTurnMilliseconds = new List<long>();
        AutonomousLocalRecoverySearch.Outcome outcome;
        do
        {
            int before = geometry.CorridorChecks;
            long started = geometry.Clock.Now;
            outcome = search.Continue(out _);
            perTurn.Add(geometry.CorridorChecks - before);
            perTurnMilliseconds.Add(geometry.Clock.Now - started);
        } while (outcome == AutonomousLocalRecoverySearch.Outcome.Pending);

        Assert.That(geometry.CorridorChecks, Is.GreaterThan(3), "the scenario must need several slow checks");
        Assert.That(perTurn.Max(), Is.EqualTo(1), "one slow corridor check per brain turn at most");
        Assert.That(perTurnMilliseconds.Max(),
            Is.LessThanOrEqualTo(AutonomousLocalRecoverySearch.SliceMilliseconds + 20 + 1),
            "a turn spends at most its slice plus one check");
        Assert.That(search.Slices, Is.EqualTo(perTurn.Count));
    }

    [Test]
    public void CheapChecksStillFinishInOneTurn()
    {
        var geometry = new Geometry(3);
        AutonomousLocalRecoverySearch search = NewSearch(geometry, new(0, 0, 0), new(1000, 0, 0), 2);
        AutonomousLocalRecoverySearch.Outcome outcome = search.Continue(out _);
        Assert.That(outcome, Is.Not.EqualTo(AutonomousLocalRecoverySearch.Outcome.Pending));
        Assert.That(search.EvaluatedCandidates, Is.EqualTo(AutonomousLocalRecoverySearch.CandidateCount));
    }

    [Test]
    public void CorridorCornerEndsTheSearchBeforeAnySideStep()
    {
        var geometry = new Geometry(1) { CornerFound = true, CorridorCostMilliseconds = 50 };
        AutonomousLocalRecoverySearch search = NewSearch(geometry, new(0, 0, 0), new(1000, 0, 0), 1);
        Assert.That(search.Continue(out Vector3 recovery), Is.EqualTo(AutonomousLocalRecoverySearch.Outcome.Found));
        Assert.That(recovery, Is.EqualTo(new Vector3(1, 2, 3)));
        Assert.That(geometry.CorridorChecks, Is.Zero);
    }

    [Test]
    public void PendingSearchBelongsOnlyToTheStalledPositionAndRoute()
    {
        var geometry = new Geometry(1);
        AutonomousLocalRecoverySearch search = NewSearch(geometry, new(100, 100, 0), new(5000, 100, 0), 1);
        Assert.That(search.IsFor(new(110, 110, 0), new(5050, 100, 0)), Is.True);
        Assert.That(search.IsFor(new(200, 100, 0), new(5000, 100, 0)), Is.False, "the bot moved away");
        Assert.That(search.IsFor(new(100, 100, 0), new(5200, 100, 0)), Is.False, "the route changed");
    }

    [Test]
    public void RefreshingCacheBuildsOnceWithinItsLifetime()
    {
        var cache = new AutonomousRefreshingCache<int, string>(TimeSpan.FromMinutes(30));
        int builds = 0;
        Assert.That(cache.Get(1, 0, () => $"v{++builds}"), Is.EqualTo("v1"));
        Assert.That(cache.Get(1, 29 * 60_000, () => $"v{++builds}"), Is.EqualTo("v1"));
        Assert.That(builds, Is.EqualTo(1));
        Assert.That(cache.Get(1, 30 * 60_000, () => $"v{++builds}"), Is.EqualTo("v2"));
    }

    [Test]
    public void ExpiredEntryIsRefreshedByOneCallerWhileOthersKeepTheOldValue()
    {
        var cache = new AutonomousRefreshingCache<int, string>(TimeSpan.FromSeconds(1));
        cache.Get(1, 0, () => "old");
        using var refreshStarted = new ManualResetEventSlim();
        using var releaseRefresh = new ManualResetEventSlim();
        Task<string> refresher = Task.Run(() => cache.Get(1, 5_000, () =>
        {
            refreshStarted.Set();
            releaseRefresh.Wait(TimeSpan.FromSeconds(10));
            return "new";
        }));
        Assert.That(refreshStarted.Wait(TimeSpan.FromSeconds(10)), Is.True);

        // Another brain thread must not wait for the rebuild.
        int otherBuilds = 0;
        Task<string> other = Task.Run(() => cache.Get(1, 5_001, () => { otherBuilds++; return "other"; }));
        Assert.That(other.Wait(TimeSpan.FromSeconds(2)), Is.True, "a concurrent reader blocked on the rebuild");
        Assert.That(other.Result, Is.EqualTo("old"));
        Assert.That(otherBuilds, Is.Zero);

        releaseRefresh.Set();
        Assert.That(refresher.Result, Is.EqualTo("new"));
        Assert.That(cache.Get(1, 5_002, () => "unused"), Is.EqualTo("new"));
    }

    [Test]
    public void FirstBuildOfOneKeyDoesNotBlockAnotherKey()
    {
        var cache = new AutonomousRefreshingCache<int, string>(TimeSpan.FromMinutes(30));
        using var buildStarted = new ManualResetEventSlim();
        using var releaseBuild = new ManualResetEventSlim();
        Task<string> slow = Task.Run(() => cache.Get(1, 0, () =>
        {
            buildStarted.Set();
            releaseBuild.Wait(TimeSpan.FromSeconds(10));
            return "region 1";
        }));
        Assert.That(buildStarted.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Task<string> other = Task.Run(() => cache.Get(2, 0, () => "region 2"));
        Assert.That(other.Wait(TimeSpan.FromSeconds(2)), Is.True, "a rebuild blocked an unrelated region");
        Assert.That(other.Result, Is.EqualTo("region 2"));
        releaseBuild.Set();
        Assert.That(slow.Result, Is.EqualTo("region 1"));
    }

    [Test]
    public void FailedRefreshKeepsServingThePreviousValue()
    {
        var cache = new AutonomousRefreshingCache<int, string>(TimeSpan.FromSeconds(1));
        cache.Get(1, 0, () => "old");
        Assert.That(cache.Get(1, 2_000, () => throw new InvalidOperationException("db busy")), Is.EqualTo("old"));
    }

    [Test]
    public void ProfilerReportsPhasesAndTheSlowestTurnWithItsBreakdown()
    {
        BotThinkProfiler.Swap(0);
        using (BotThinkProfiler.Turn(null))
        {
            using (BotThinkProfiler.Measure(BotThinkPhase.RouteRecoverySearch))
            using (BotThinkProfiler.Measure(BotThinkPhase.NavPathQuery))
                SpinFor(TimeSpan.FromMilliseconds(BotThinkProfiler.SlowThinkMilliseconds + 20));
            using (BotThinkProfiler.Measure(BotThinkPhase.NavPathQuery)) { }
        }
        using (BotThinkProfiler.Turn(null)) { }
        // Phases outside a brain turn (for example the reaper) still count.
        using (BotThinkProfiler.Measure(BotThinkPhase.DeathRewards)) { }

        BotThinkProfiler.Window window = BotThinkProfiler.Swap(60_000);
        Assert.That(window.Thinks, Is.EqualTo(2));
        Assert.That(window.SlowThinks, Is.EqualTo(1));
        Assert.That(window.CallCount(BotThinkPhase.NavPathQuery), Is.EqualTo(2));
        Assert.That(window.CallCount(BotThinkPhase.DeathRewards), Is.EqualTo(1));
        Assert.That(window.TotalMilliseconds(BotThinkPhase.RouteRecoverySearch),
            Is.GreaterThanOrEqualTo(BotThinkProfiler.SlowThinkMilliseconds));

        string[] lines = BotThinkProfiler.Describe(window, 60_000).ToArray();
        Assert.That(lines[0], Does.StartWith("BOT_THINK_PROFILE windowS=60 thinks=2 "));
        Assert.That(lines[0], Does.Contain("over100ms=1"));
        Assert.That(lines[0], Does.Contain("RouteRecoverySearch="));
        Assert.That(lines, Has.Length.EqualTo(2));
        Assert.That(lines[1], Does.StartWith("BOT_THINK_SLOW bot=\"?\" kind=Npc "));
        Assert.That(lines[1], Does.Contain("NavPathQuery="));
        Assert.That(lines[1], Does.Contain("ms/2x"), "the slow turn lists both of its path queries");
    }

    [Test]
    public void DisabledSkillKeysCompareLikeTheDefaultKeyValuePairEquality()
    {
        var comparer = GameLiving.DisabledSkillKeyComparer.Instance;
        var pairs = new[]
        {
            new KeyValuePair<int, Type>(1, typeof(string)),
            new KeyValuePair<int, Type>(1, typeof(string)),
            new KeyValuePair<int, Type>(1, typeof(object)),
            new KeyValuePair<int, Type>(2, typeof(string)),
            new KeyValuePair<int, Type>(2, null),
            new KeyValuePair<int, Type>(2, null),
        };
        foreach (var left in pairs)
        foreach (var right in pairs)
        {
            Assert.That(comparer.Equals(left, right), Is.EqualTo(left.Equals(right)), $"{left} vs {right}");
            if (left.Equals(right))
                Assert.That(comparer.GetHashCode(left), Is.EqualTo(comparer.GetHashCode(right)));
        }
    }

    private static void SpinFor(TimeSpan duration)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed < duration)
            Thread.SpinWait(100);
    }
}
