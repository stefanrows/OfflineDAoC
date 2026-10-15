using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OfflineDaoc.Configuration;

namespace DOL.GS.Tests;

/// <summary>
/// The Gothwaite PvP performance pieces of stage 4 (docs/GOTHWAITE_PVP_MODE.md sections 6.8 and 8): the patrol-point cache (it returns
/// exactly the uncached points), the scan cadence (one scan per 1.5 s per bot on the turn clock), and the load meter (the histogram
/// percentile, the windows and the GOTH_PVP_LOAD line). Pure: no GameBot and no database.
/// </summary>
[TestFixture]
public class UT_GothwaitePvpPerformance
{
    [SetUp]
    public void SetUp()
    {
        GothwaitePvpMode.Reset();
        GothwaitePvpPatrolCache.Clear();
        GothwaitePvpLoadMeter.Reset(0);
    }

    [TearDown]
    public void TearDown()
    {
        GothwaitePvpMode.Reset();
        GothwaitePvpPatrolCache.Clear();
        GothwaitePvpLoadMeter.Reset(0);
    }

    private static void TurnOn(GothwaitePvpSettings settings = null) =>
        GothwaitePvpMode.Apply((settings ?? GothwaitePvpSettings.Defaults) with { Enabled = true });

    [TestCase(1)]
    [TestCase(19)]
    [TestCase(20)]
    [TestCase(50)]
    public void ThePatrolCacheReturnsThePointsOfTheUncachedMethod(int level)
    {
        TurnOn();
        GothwaitePvpSettings settings = GothwaitePvpMode.Settings;

        IReadOnlyList<GothwaitePvpSpot> cached = GothwaitePvpPatrolCache.PointsFor(GothwaitePvpMode.Arena, settings, level);

        Assert.That(cached.ToArray(), Is.EqualTo(GothwaitePvpMode.Arena.PatrolPointsFor(level, settings).ToArray()));
    }

    [Test]
    public void ThePatrolCacheKeepsOneListPerEdgeAndReusesIt()
    {
        TurnOn();
        GothwaitePvpSettings settings = GothwaitePvpMode.Settings;
        GothwaitePvpArena arena = GothwaitePvpMode.Arena;

        IReadOnlyList<GothwaitePvpSpot> low = GothwaitePvpPatrolCache.PointsFor(arena, settings, 1);
        IReadOnlyList<GothwaitePvpSpot> high = GothwaitePvpPatrolCache.PointsFor(arena, settings, 20);

        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpPatrolCache.PointsFor(arena, settings, 19), Is.SameAs(low), "level 19 shares the low band's edge");
            Assert.That(GothwaitePvpPatrolCache.PointsFor(arena, settings, 50), Is.SameAs(high), "level 50 shares the high band's edge");
            Assert.That(GothwaitePvpPatrolCache.PointsFor(arena, settings, 1), Is.SameAs(low), "the low list survives a high-band lookup");
            Assert.That(high, Is.Not.SameAs(low));
        });
    }

    [Test]
    public void ThePatrolCacheRebuildsWhenTheSettingsChange()
    {
        TurnOn();
        IReadOnlyList<GothwaitePvpSpot> before = GothwaitePvpPatrolCache.PointsFor(GothwaitePvpMode.Arena, GothwaitePvpMode.Settings, 19);
        GothwaitePvpSettings changed = GothwaitePvpSettings.Defaults with { Enabled = true, LowBandEdge = 12_000 };

        GothwaitePvpMode.Apply(changed);
        IReadOnlyList<GothwaitePvpSpot> after = GothwaitePvpPatrolCache.PointsFor(GothwaitePvpMode.Arena, GothwaitePvpMode.Settings, 19);

        Assert.Multiple(() =>
        {
            Assert.That(after, Is.Not.SameAs(before), "a new arena instance is a new cache entry");
            Assert.That(after.ToArray(), Is.EqualTo(GothwaitePvpMode.Arena.PatrolPointsFor(19, changed).ToArray()));
            Assert.That(after.Count, Is.LessThanOrEqualTo(before.Count), "a smaller low-band edge keeps no more points");
        });
    }

    [Test]
    public void TheScanIntervalIsOneAndAHalfSeconds()
    {
        Assert.That(AutonomousBattlegroundDriver.ScanMilliseconds, Is.EqualTo(1_500));
    }

    [Test]
    public void ABotScansOncePerIntervalOnTheTurnClock()
    {
        // Mode bots think every 750 ms: a bot is due on every second turn, and the next due time is set by each scan.
        long next = 0;
        var scans = new List<long>();
        for (long now = 0; now <= 60_000; now += 750)
        {
            if (now < next)
                continue;
            scans.Add(now);
            next = GothwaitePvpArenaRules.NextScanAfter(now);
        }

        Assert.Multiple(() =>
        {
            Assert.That(scans.Count, Is.EqualTo(41), "one scan at 0 s, 1.5 s, ... 60 s");
            Assert.That(scans.Zip(scans.Skip(1), (earlier, later) => later - earlier), Is.All.EqualTo(1_500));
        });
    }

    [TestCase(250)]
    [TestCase(750)]
    [TestCase(1_000)]
    public void NoTurnRateScansFasterThanTheInterval(int turnMilliseconds)
    {
        long next = 0;
        var scans = new List<long>();
        for (long now = 0; now <= 60_000; now += turnMilliseconds)
        {
            if (now < next)
                continue;
            scans.Add(now);
            next = GothwaitePvpArenaRules.NextScanAfter(now);
        }

        Assert.That(scans.Zip(scans.Skip(1), (earlier, later) => later - earlier), Is.All.GreaterThanOrEqualTo(1_500));
    }

    [Test]
    public void ThePercentileIsTheBucketBoundClampedToTheLargestTurn()
    {
        var buckets = new long[12];
        buckets[0] = 95;
        buckets[1] = 5;

        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpLoadMeter.Percentile(buckets, 100, 0.2, 0.95), Is.EqualTo(0.2), "the bound 0.25 is above the largest turn");
            Assert.That(GothwaitePvpLoadMeter.Percentile(buckets, 100, 3.0, 0.95), Is.EqualTo(0.25));
        });
    }

    [Test]
    public void ThePercentileOfTheLastBucketIsTheLargestTurnAndAnEmptyHistogramIsZero()
    {
        var overflow = new long[12];
        overflow[11] = 10;
        var empty = new long[12];

        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpLoadMeter.Percentile(overflow, 10, 1_500, 0.95), Is.EqualTo(1_500));
            Assert.That(GothwaitePvpLoadMeter.Percentile(empty, 0, 0, 0.95), Is.EqualTo(0));
        });
    }

    [Test]
    public void ATurnFallsInTheFirstBucketItDoesNotExceed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpLoadMeter.BucketOf(0.25), Is.EqualTo(0));
            Assert.That(GothwaitePvpLoadMeter.BucketOf(0.26), Is.EqualTo(1));
            Assert.That(GothwaitePvpLoadMeter.BucketOf(1.0), Is.EqualTo(2));
            Assert.That(GothwaitePvpLoadMeter.BucketOf(1_000), Is.EqualTo(10));
            Assert.That(GothwaitePvpLoadMeter.BucketOf(1_000.5), Is.EqualTo(11), "the last bucket holds every longer turn");
        });
    }

    [Test]
    public void TheMeterSnapshotGivesTheAverageTheP95TheMaximaAndTheScanAndFormerFigures()
    {
        for (int turn = 0; turn < 9; turn++)
            GothwaitePvpLoadMeter.RecordMilliseconds(GothwaitePvpLoadKind.Turn, 1.0);
        GothwaitePvpLoadMeter.RecordMilliseconds(GothwaitePvpLoadKind.Turn, 20.0);
        GothwaitePvpLoadMeter.RecordMilliseconds(GothwaitePvpLoadKind.Scan, 0.5);
        GothwaitePvpLoadMeter.RecordMilliseconds(GothwaitePvpLoadKind.Scan, 0.5);
        GothwaitePvpLoadMeter.RecordMilliseconds(GothwaitePvpLoadKind.Former, 3.0);

        GothwaitePvpLoadSnapshot snapshot = GothwaitePvpLoadMeter.Snapshot(30_000);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.WindowSeconds, Is.EqualTo(30).Within(1e-9));
            Assert.That(snapshot.Turns, Is.EqualTo(10));
            Assert.That(snapshot.TurnAverageMilliseconds, Is.EqualTo(2.9).Within(1e-6));
            Assert.That(snapshot.TurnP95Milliseconds, Is.EqualTo(20).Within(1e-9), "9 of 10 turns are at most 1 ms; the 10th is clamped to the 20 ms maximum");
            Assert.That(snapshot.TurnMaxMilliseconds, Is.EqualTo(20).Within(1e-9));
            Assert.That(snapshot.Scans, Is.EqualTo(2));
            Assert.That(snapshot.ScanAverageMilliseconds, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(snapshot.Formers, Is.EqualTo(1));
            Assert.That(snapshot.FormerAverageMilliseconds, Is.EqualTo(3).Within(1e-9));
            Assert.That(snapshot.FormerMaxMilliseconds, Is.EqualTo(3).Within(1e-9));
        });
    }

    [Test]
    public void AnEmptyMeterSaysNothingWasMeasured()
    {
        GothwaitePvpLoadSnapshot snapshot = GothwaitePvpLoadMeter.Snapshot(5_000);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Turns, Is.EqualTo(0));
            Assert.That(snapshot.TurnAverageMilliseconds, Is.EqualTo(0));
            Assert.That(snapshot.TurnP95Milliseconds, Is.EqualTo(0));
            Assert.That(snapshot.WindowSeconds, Is.EqualTo(5).Within(1e-9));
        });
    }

    [Test]
    public void TheWindowRotatesEachMinuteAndTheStatusCoversTheLastTwo()
    {
        GothwaitePvpLoadMeter.Reset(0);
        for (int turn = 0; turn < 3; turn++)
            GothwaitePvpLoadMeter.RecordMilliseconds(GothwaitePvpLoadKind.Turn, 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpLoadMeter.IsWindowDue(59_999), Is.False);
            Assert.That(GothwaitePvpLoadMeter.IsWindowDue(60_000), Is.True);
        });
        GothwaitePvpLoadSnapshot first = GothwaitePvpLoadMeter.FinishWindow(60_000);
        Assert.Multiple(() =>
        {
            Assert.That(first.Turns, Is.EqualTo(3));
            Assert.That(first.WindowSeconds, Is.EqualTo(60).Within(1e-9));
        });

        for (int turn = 0; turn < 2; turn++)
            GothwaitePvpLoadMeter.RecordMilliseconds(GothwaitePvpLoadKind.Turn, 1.0);
        Assert.That(GothwaitePvpLoadMeter.Snapshot(90_000).Turns, Is.EqualTo(5), "the finished minute and the current one");

        GothwaitePvpLoadMeter.FinishWindow(120_000);
        GothwaitePvpLoadMeter.RecordMilliseconds(GothwaitePvpLoadKind.Turn, 1.0);
        GothwaitePvpLoadSnapshot status = GothwaitePvpLoadMeter.Snapshot(130_000);
        Assert.Multiple(() =>
        {
            Assert.That(status.Turns, Is.EqualTo(3), "the first minute has aged out: the second minute and the current one remain");
            Assert.That(status.WindowSeconds, Is.EqualTo(70).Within(1e-9));
        });
    }

    [Test]
    public void ThePublishIsSilentAndReadsNoBotCountWhileTheWindowIsNotDue()
    {
        GothwaitePvpLoadMeter.Reset(0);

        Assert.DoesNotThrow(() => GothwaitePvpLoadMeter.PublishIfDue(1_000, () => throw new InvalidOperationException("read too early")));
    }

    [Test]
    public void TheMinuteLineCarriesTheWindowTheCountsAndTheOnlineBots()
    {
        var snapshot = new GothwaitePvpLoadSnapshot(60, 500, 1.25, 2.5, 40, 120, 0.3, 30, 0.5, 3.0);

        Assert.That(GothwaitePvpLines.Load(snapshot, 412), Is.EqualTo("GOTH_PVP_LOAD windowS=60 bots=412 turns=500 turnAvgMs=1.250 " +
            "turnP95MsAtMost=2.50 turnMaxMs=40.00 scans=120 scanAvgMs=0.300 formers=30 formerAvgMs=0.50 formerMaxMs=3.00"));
    }
}
