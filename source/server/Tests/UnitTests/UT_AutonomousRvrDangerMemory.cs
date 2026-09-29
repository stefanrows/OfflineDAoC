using System;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Wave 2 of livelier RvR bots (C2, principles P5 and P7): a crew
/// remembers for about an hour where it lost people; careful leaders stay
/// away, bold ones come back only bigger, and a revenge trip waits for enough
/// people.</summary>
[TestFixture]
public sealed class UT_AutonomousRvrDangerMemory
{
    private static readonly DateTime Start = new(2026, 9, 29, 20, 0, 0, DateTimeKind.Utc);
    private static readonly RvrLeaderTraits Cautious = new(50, 30, 50);
    private static readonly RvrLeaderTraits Bold = new(80, 60, 50);
    private static readonly RvrLeaderTraits Ordinary = new(50, 55, 50);

    private static string NewKey() => "test:" + Guid.NewGuid();

    [Test]
    public void MemoryFadesLinearlyToNothingAtSixtyMinutes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrDangerMemory.Freshness(Start, Start), Is.EqualTo(1));
            Assert.That(AutonomousRvrDangerMemory.Freshness(Start, Start.AddMinutes(30)), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(AutonomousRvrDangerMemory.Freshness(Start, Start.AddMinutes(60)), Is.EqualTo(0));
            Assert.That(AutonomousRvrDangerMemory.Intensity(2, 0, Start, Start.AddMinutes(60)), Is.EqualTo(0));
        });

        string key = NewKey();
        AutonomousRvrDangerMemory.Record(key, 163, new(10_000, 20_000, 0), 6, true, Start, out _);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrDangerMemory.At(key, 163, 10_100, 20_100, Start.AddMinutes(59)), Is.Not.Null);
            Assert.That(AutonomousRvrDangerMemory.At(key, 163, 10_100, 20_100, Start.AddMinutes(60)), Is.Null,
                "a loss is forgotten after an hour");
            Assert.That(AutonomousRvrDangerMemory.LostGroupSize(key, Start.AddMinutes(60)), Is.EqualTo(0));
        });
    }

    [Test]
    public void CautiousLeaderAvoidsTheLossPlace()
    {
        double one = AutonomousRvrDangerMemory.DangerFactor(Cautious, 0.1, 8, 6, 1);
        double four = AutonomousRvrDangerMemory.DangerFactor(Cautious, 0.1, 8, 6, 4);
        double byDoctrine = AutonomousRvrDangerMemory.DangerFactor(Ordinary, 0.3, 8, 6, 1);
        Assert.Multiple(() =>
        {
            Assert.That(one, Is.EqualTo(0.5).Within(1e-9), "one fresh loss halves the pull, even with more people");
            Assert.That(four, Is.EqualTo(0.2).Within(1e-9), "four losses: hardly ever back within the hour");
            Assert.That(byDoctrine, Is.LessThan(1), "a doctrine that retreats early is also careful");
            Assert.That(AutonomousRvrDangerMemory.DangerFactor(Cautious, 0.1, 8, 6, 0.5), Is.InRange(0.5, 1),
                "an old memory fades toward normal");
            Assert.That(AutonomousRvrDangerMemory.DangerFactor(Cautious, 0.1, 8, 6, 0), Is.EqualTo(1));
        });
    }

    [Test]
    public void BoldLeaderReturnsOnlyWhenBigger()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrDangerMemory.DangerFactor(Bold, 0.1, 7, 6, 1), Is.EqualTo(1.3).Within(1e-9),
                "one more member than the group that died: back for revenge");
            Assert.That(AutonomousRvrDangerMemory.DangerFactor(Bold, 0.1, 6, 6, 1), Is.EqualTo(0.7).Within(1e-9),
                "the same size stays away");
            Assert.That(AutonomousRvrDangerMemory.DangerFactor(Bold, 0.1, 4, 6, 1), Is.LessThan(1));
            Assert.That(AutonomousRvrDangerMemory.DangerFactor(Ordinary, 0.1, 8, 6, 1), Is.EqualTo(0.6).Within(1e-9),
                "everyone else keeps a plain distance");
        });
    }

    [Test]
    public void DeathsInOneCellMergeAndKeepTheBiggestGroup()
    {
        string key = NewKey();
        AutonomousRvrDangerMemory.Record(key, 163, new(3_100, 4_600, 0), 5, true, Start, out bool firstLog);
        AutonomousRvrDangerMemory.Danger second = AutonomousRvrDangerMemory.Record(key, 163, new(4_400, 5_900, 0), 8, true,
            Start.AddSeconds(20), out bool secondLog);
        AutonomousRvrDangerMemory.Danger retreat = AutonomousRvrDangerMemory.Record(key, 163, new(3_200, 4_700, 0), 3, false,
            Start.AddSeconds(90), out bool thirdLog);
        Assert.Multiple(() =>
        {
            Assert.That(second.Cell, Is.EqualTo(new AutonomousRvrDangerMemory.CellKey(163, 2, 3)));
            Assert.That(second.Deaths, Is.EqualTo(2), "two deaths in one 1,500 cell are one place");
            Assert.That(second.GroupSize, Is.EqualTo(8));
            Assert.That(retreat.Retreats, Is.EqualTo(1));
            Assert.That(retreat.Intensity, Is.EqualTo(2.5).Within(1e-9), "a retreat weighs half a death");
            Assert.That(firstLog, Is.True);
            Assert.That(secondLog, Is.False, "the log line is throttled to once a minute per cell");
            Assert.That(thirdLog, Is.True);
            Assert.That(AutonomousRvrDangerMemory.At(key, 163, 6_100, 4_600, Start), Is.Null, "the next cell is clean");
            Assert.That(AutonomousRvrDangerMemory.At(key, 164, 3_100, 4_600, Start), Is.Null, "other region, other place");
            Assert.That(AutonomousRvrDangerMemory.LostGroupSize(key, Start.AddMinutes(2)), Is.EqualTo(8));
        });
    }

    [Test]
    public void EachCrewKeepsAtMostTwentyFourPlacesDroppingTheOldest()
    {
        string key = NewKey();
        for (int i = 0; i < AutonomousRvrDangerMemory.MaximumCellsPerKey + 3; i++)
            AutonomousRvrDangerMemory.Record(key, 163, new(i * 3_000, 0, 0), 4, true, Start.AddSeconds(i), out _);
        DateTime now = Start.AddMinutes(1);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrDangerMemory.At(key, 163, 0, 0, now), Is.Null, "the oldest place was dropped");
            Assert.That(AutonomousRvrDangerMemory.At(key, 163, 2 * 3_000, 0, now), Is.Null);
            Assert.That(AutonomousRvrDangerMemory.At(key, 163, 3 * 3_000, 0, now), Is.Not.Null);
            Assert.That(AutonomousRvrDangerMemory.At(key, 163, 26 * 3_000, 0, now), Is.Not.Null);
        });
    }

    [Test]
    public void RevengeWaitsForTwoThirdsOfTheLostGroup()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrDangerMemory.AllowsRevenge(4, 6), Is.True, "4 of 6 is two thirds");
            Assert.That(AutonomousRvrDangerMemory.AllowsRevenge(3, 6), Is.False, "half the lost group roams instead");
            Assert.That(AutonomousRvrDangerMemory.AllowsRevenge(5, 8), Is.False);
            Assert.That(AutonomousRvrDangerMemory.AllowsRevenge(6, 8), Is.True);
            Assert.That(AutonomousRvrDangerMemory.AllowsRevenge(1, 0), Is.True, "no remembered loss, no gate");
        });
    }

    [Test]
    public void WorstNearbyCellSteersCoverUnlessHeatIsFresher()
    {
        string key = NewKey();
        AutonomousRvrDangerMemory.Record(key, 163, new(10_000, 10_000, 0), 6, true, Start, out _);
        AutonomousRvrDangerMemory.Record(key, 163, new(12_000, 10_000, 0), 6, true, Start, out _);
        AutonomousRvrDangerMemory.Record(key, 163, new(12_000, 10_000, 0), 6, true, Start, out _);
        AutonomousRvrDangerMemory.Danger? worst = AutonomousRvrDangerMemory.Worst(key, 163, new(11_000, 10_000),
            AutonomousRvrRoutePolicy.CoverHeatRange, Start.AddMinutes(5));
        Assert.That(worst, Is.Not.Null);
        Assert.That(worst.Value.Deaths, Is.EqualTo(2), "the cell with two deaths is the worst");
        Assert.That(AutonomousRvrDangerMemory.Worst(key, 163, new(40_000, 10_000), AutonomousRvrRoutePolicy.CoverHeatRange,
            Start.AddMinutes(5)), Is.Null, "farther than 6,000 does not shape the route");

        Vector3 heat = new(0, 0, 0), danger = new(5, 5, 0);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrDangerMemory.CoverThreat(null, 0, danger, TimeSpan.FromMinutes(30)), Is.EqualTo(danger),
                "without heat the remembered loss decides the side");
            Assert.That(AutonomousRvrDangerMemory.CoverThreat(heat, 0.9, danger, TimeSpan.FromMinutes(10)), Is.EqualTo(heat),
                "a fight 1.2 minutes old is fresher than a 10-minute-old loss");
            Assert.That(AutonomousRvrDangerMemory.CoverThreat(heat, 0.1, danger, TimeSpan.FromMinutes(2)), Is.EqualTo(danger),
                "a fresh loss beats an old fight");
            Assert.That(AutonomousRvrDangerMemory.CoverThreat(heat, 0.5, null, TimeSpan.MaxValue), Is.EqualTo(heat));
        });
    }
}
