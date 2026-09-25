using System;
using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using OfflineDaoc.Configuration;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_AutonomousPopulationShape
{
    [Test]
    public void EstablishedHundredBotBatchHasRoadmapLevelBandsAndServerXp()
    {
        int[] levels = AutonomousPopulationShape.NewLevels(PopulationWorldShape.Established, 100, new Random(27));
        Assert.Multiple(() =>
        {
            Assert.That(levels.Count(level => level is >= 1 and <= 9), Is.EqualTo(15));
            Assert.That(levels.Count(level => level is >= 10 and <= 19), Is.EqualTo(15));
            Assert.That(levels.Count(level => level is >= 20 and <= 34), Is.EqualTo(20));
            Assert.That(levels.Count(level => level is >= 35 and <= 49), Is.EqualTo(25));
            Assert.That(levels.Count(level => level == 50), Is.EqualTo(25));
            Assert.That(levels.Distinct().Count(), Is.GreaterThan(10));
        });
        foreach (int level in levels)
            Assert.That(AutonomousPopulationShape.ExperienceForLevel(level),
                Is.EqualTo(GamePlayer.GetExperienceAmountForLevel(level - 1)));
        Assert.That(AutonomousPopulationShape.RealmPointsForNewLevelFifty(new Random(1)), Is.GreaterThan(0));
    }

    [Test]
    public void FreshLaunchAndAltIntervalRespectRosterCap()
    {
        Assert.That(AutonomousPopulationShape.NewLevels(PopulationWorldShape.FreshLaunch, 100),
            Is.All.EqualTo(1));
        DateTime now = DateTime.UtcNow;
        BotGoalSettings settings = BotGoalSettings.Defaults;
        Assert.That(AutonomousAltTrickle.IsDue(settings, 100, now, now.AddMinutes(-1)), Is.True);
        Assert.That(AutonomousAltTrickle.IsDue(settings, 5000, now, now.AddMinutes(-1)), Is.False);
        Assert.That(AutonomousAltTrickle.IsDue(settings, 100, now, now.AddMinutes(1)), Is.False);
        Assert.That(AutonomousAltTrickle.IsDue(settings with { AltJoinIntervalHours = 0 },
            100, now, now.AddMinutes(-1)), Is.False);
        var generated = new OfflineWorldBotRecord { Level = 50, Experience =
            AutonomousPopulationShape.ExperienceForLevel(50), Activity = "Queued at randomized starting location" };
        Assert.That(AutonomousPopulationController.ShouldFillNewBotVitals(generated), Is.True);
        generated.LastSavedUtc = now.ToString("O");
        Assert.That(AutonomousPopulationController.ShouldFillNewBotVitals(generated), Is.False);
    }

    [Test]
    public void ThreeTimesSpawnCapacityKeepsTenThousandBotRampOnSchedule()
    {
        const int rosterSize = 10_000;
        int earlyTarget = (int)Math.Ceiling(rosterSize * AutonomousPopulationRamp.EarlyPopulationFraction);
        double earlyRatePerSimulationSecond = earlyTarget / (AutonomousPopulationRamp.EarlyRampMinutes * 60d);
        double laterRatePerSimulationSecond = (rosterSize - earlyTarget) / ((15 - AutonomousPopulationRamp.EarlyRampMinutes) * 60d);
        int requiredAtThreeTimes = (int)Math.Ceiling(Math.Max(earlyRatePerSimulationSecond, laterRatePerSimulationSecond) *
            3d * AutonomousPopulationController.PopulationPollIntervalMilliseconds / 1000d);

        Assert.That(AutonomousPopulationController.MaximumSpawnEnqueuePerPoll, Is.GreaterThanOrEqualTo(requiredAtThreeTimes));
    }

    [Test]
    public void RecommendationRequiresThreeStableSamplesForSameHardware()
    {
        string folder = Path.Combine(Path.GetTempPath(), "population-benchmark-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, PopulationBenchmarks.FileName);
        try
        {
            var benchmarks = new PopulationBenchmarks();
            foreach (int tier in new[] { 500, 1000, 1500 })
            {
                benchmarks.Record(new(tier, tier, 8, 16L << 30, tier * 1000000L,
                    tier == 1500 ? 90 : 25, 100, DateTime.UtcNow));
                if (tier < 1500) Assert.That(benchmarks.Recommended(8, 16L << 30), Is.Null);
            }
            benchmarks.Save(path);
            Assert.That(PopulationBenchmarks.Load(path).Recommended(8, 16L << 30), Is.EqualTo(1000));
            Assert.That(benchmarks.Recommended(8, (16L << 30) - (256L << 20)), Is.EqualTo(1000));
            Assert.That(benchmarks.Recommended(4, 16L << 30), Is.Null);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
