using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using OfflineDaoc.Configuration;

namespace DOL.GS.Tests;

/// <summary>
/// The Gothwaite PvP status command's pure parts (docs/GOTHWAITE_PVP_MODE.md section 8, stage 4): the mode-off line, the header, the
/// level-band split by phase, the group census lines, the fight lines with the con colours, the load line and the culture-free numbers.
/// No GameBot, no database: the report is built from plain data, and the live capture is tested only with the mode off.
/// </summary>
[TestFixture]
public class UT_GothwaitePvpStatus
{
    private static readonly GothwaitePvpBotSample[] SampleBots =
    {
        new(1, GothwaitePvpBotPhase.CastleResting),
        new(5, GothwaitePvpBotPhase.CastleReady),
        new(12, GothwaitePvpBotPhase.Departing),
        new(15, GothwaitePvpBotPhase.Arena),
        new(25, GothwaitePvpBotPhase.Arena),
        new(35, GothwaitePvpBotPhase.Arena),
    };

    [SetUp]
    public void SetUp()
    {
        GothwaitePvpMode.Reset();
        GothwaitePvpStats.Reset();
        GothwaitePvpLoadMeter.Reset(0);
    }

    [TearDown]
    public void TearDown()
    {
        GothwaitePvpMode.Reset();
        GothwaitePvpStats.Reset();
        GothwaitePvpLoadMeter.Reset(0);
    }

    // The fixed report of the sample test: six mode bots, four groups (one forming), fights over thirty game minutes, two minutes of load.
    // Mode bots made four kills and died five times; all arena fights, players included, were seven kills and six deaths.
    private static GothwaitePvpStatusReport SampleReport(bool active = true) => new(
        active, 51, "gothwaite", 4000, SampleBots,
        new GothwaitePvpGroupCensus(1, 3, 1, new[] { 1, 2, 1, 0 }, 4, 12),
        new GothwaitePvpStatsSnapshot(0, 1_800_000, GothwaitePvpStats.HeatWindowMilliseconds, 7, 6,
            new[]
            {
                new GothwaitePvpBandCount("1-9", 2, 1),
                new GothwaitePvpBandCount("10-19", 3, 2),
                new GothwaitePvpBandCount("20-29", 1, 1),
                new GothwaitePvpBandCount("30-39", 1, 1),
                new GothwaitePvpBandCount("40-50", 0, 0),
            },
            new[]
            {
                new GothwaitePvpConCount(ConColor.GREY, 0),
                new GothwaitePvpConCount(ConColor.GREEN, 2),
                new GothwaitePvpConCount(ConColor.BLUE, 1),
                new GothwaitePvpConCount(ConColor.YELLOW, 3),
                new GothwaitePvpConCount(ConColor.ORANGE, 1),
                new GothwaitePvpConCount(ConColor.RED, 0),
                new GothwaitePvpConCount(ConColor.PURPLE, 0),
            })
        {
            ModeKills = 4,
            ModeDeaths = 5,
            ModeBands = new[]
            {
                new GothwaitePvpBandCount("1-9", 2, 1),
                new GothwaitePvpBandCount("10-19", 1, 2),
                new GothwaitePvpBandCount("20-29", 1, 1),
                new GothwaitePvpBandCount("30-39", 0, 1),
                new GothwaitePvpBandCount("40-50", 0, 0),
            },
            ModeKillsByCon = new[]
            {
                new GothwaitePvpConCount(ConColor.GREY, 0),
                new GothwaitePvpConCount(ConColor.GREEN, 1),
                new GothwaitePvpConCount(ConColor.BLUE, 1),
                new GothwaitePvpConCount(ConColor.YELLOW, 1),
                new GothwaitePvpConCount(ConColor.ORANGE, 1),
                new GothwaitePvpConCount(ConColor.RED, 0),
                new GothwaitePvpConCount(ConColor.PURPLE, 0),
            },
        },
        new GothwaitePvpLoadSnapshot(120, 1200, 1.25, 2.5, 40, 300, 0.3, 60, 0.5, 3.0));

    [Test]
    public void WithTheModeOffTheStatusIsOneLine()
    {
        IReadOnlyList<string> lines = GothwaitePvpStatus.Lines(SampleReport(active: false));

        Assert.That(lines, Is.EqualTo(new[] { "Gothwaite PvP mode is off." }));
    }

    [Test]
    public void TheLiveCaptureWithTheModeOffReadsNoGameObject()
    {
        GothwaitePvpMode.Reset();

        GothwaitePvpStatusReport report = GothwaitePvpStatus.Capture();

        Assert.Multiple(() =>
        {
            Assert.That(report.Active, Is.False);
            Assert.That(report.Bots, Is.Empty);
            Assert.That(report.Fights, Is.Null);
            Assert.That(report.Load, Is.Null);
            Assert.That(GothwaitePvpStatus.Lines(report), Is.EqualTo(new[] { "Gothwaite PvP mode is off." }));
        });
    }

    [Test]
    public void ALineOfTheStatusNamesTheArenaRegionAndRadius()
    {
        IReadOnlyList<string> lines = GothwaitePvpStatus.Lines(SampleReport());

        Assert.That(lines[0], Is.EqualTo("Gothwaite PvP mode: on. Arena gothwaite (region 51), castle safe radius 4000."));
    }

    [Test]
    public void TheBandSplitCountsEachPhaseInItsLevelBand()
    {
        GothwaitePvpBandPhases[] bands = GothwaitePvpStatus.CountBands(new[]
        {
            new GothwaitePvpBotSample(1, GothwaitePvpBotPhase.CastleResting),
            new GothwaitePvpBotSample(9, GothwaitePvpBotPhase.CastleReady),
            new GothwaitePvpBotSample(10, GothwaitePvpBotPhase.Departing),
            new GothwaitePvpBotSample(19, GothwaitePvpBotPhase.Arena),
            new GothwaitePvpBotSample(20, GothwaitePvpBotPhase.Arena),
            new GothwaitePvpBotSample(29, GothwaitePvpBotPhase.CastleResting),
            new GothwaitePvpBotSample(30, GothwaitePvpBotPhase.CastleReady),
            new GothwaitePvpBotSample(39, GothwaitePvpBotPhase.Departing),
            new GothwaitePvpBotSample(40, GothwaitePvpBotPhase.Arena),
            new GothwaitePvpBotSample(50, GothwaitePvpBotPhase.Arena),
            new GothwaitePvpBotSample(51, GothwaitePvpBotPhase.Arena),
        });

        Assert.Multiple(() =>
        {
            Assert.That(bands.Select(band => band.Label), Is.EqualTo(new[] { "1-9", "10-19", "20-29", "30-39", "40-50" }));
            Assert.That(bands[0], Is.EqualTo(new GothwaitePvpBandPhases("1-9", 2, 1, 1, 0, 0)));
            Assert.That(bands[1], Is.EqualTo(new GothwaitePvpBandPhases("10-19", 2, 0, 0, 1, 1)));
            Assert.That(bands[2], Is.EqualTo(new GothwaitePvpBandPhases("20-29", 2, 1, 0, 0, 1)));
            Assert.That(bands[3], Is.EqualTo(new GothwaitePvpBandPhases("30-39", 2, 0, 1, 1, 0)));
            Assert.That(bands[4], Is.EqualTo(new GothwaitePvpBandPhases("40-50", 3, 0, 0, 0, 3)), "level 51 is in the top band");
        });
    }

    [Test]
    public void ABotBelowLevelOneIsInNoBandRow()
    {
        GothwaitePvpBandPhases[] bands = GothwaitePvpStatus.CountBands(new[]
        {
            new GothwaitePvpBotSample(0, GothwaitePvpBotPhase.Arena),
            new GothwaitePvpBotSample(3, GothwaitePvpBotPhase.Arena),
        });

        Assert.Multiple(() =>
        {
            Assert.That(bands.Sum(band => band.Total), Is.EqualTo(1));
            Assert.That(bands[0].Arena, Is.EqualTo(1));
        });
    }

    [Test]
    public void TheBotsOnlineLineCountsEachPhaseAndNotesTheDeadBots()
    {
        IReadOnlyList<string> lines = GothwaitePvpStatus.Lines(SampleReport());

        Assert.That(lines[1], Is.EqualTo("Bots online in the mode: 6 (castle resting 1, castle ready 1, departing 1, arena 3). " +
            "A dead bot counts as castle resting until released."));
    }

    [Test]
    public void TheBandRowsFollowTheBotsLineInLevelOrder()
    {
        IReadOnlyList<string> lines = GothwaitePvpStatus.Lines(SampleReport());

        Assert.That(lines.Skip(2).Take(5), Is.EqualTo(new[]
        {
            "  Levels 1-9: 2 (castle resting 1, castle ready 1, departing 0, arena 0).",
            "  Levels 10-19: 2 (castle resting 0, castle ready 0, departing 1, arena 1).",
            "  Levels 20-29: 1 (castle resting 0, castle ready 0, departing 0, arena 1).",
            "  Levels 30-39: 1 (castle resting 0, castle ready 0, departing 0, arena 1).",
            "  Levels 40-50: 0 (castle resting 0, castle ready 0, departing 0, arena 0).",
        }));
    }

    [Test]
    public void TheGroupLineCountsTheFormingGroupsTheDepartedShapesAndTheAverageSize()
    {
        IReadOnlyList<string> lines = GothwaitePvpStatus.Lines(SampleReport());

        Assert.That(lines[7], Is.EqualTo("Groups: forming 1 (3 members), departed 4 (solo 1, duo 2, small 1, eight 0), " +
            "average size 3.0 for groups of two or more, castle pool waiting 1."));
    }

    [Test]
    public void TheAverageSizeIsZeroWithoutAGroupOfTwoOrMore()
    {
        var census = new GothwaitePvpGroupCensus(0, 0, 4, new[] { 3, 0, 0, 0 }, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(census.AverageSize, Is.EqualTo(0));
            Assert.That(census.Departed, Is.EqualTo(3));
            Assert.That(GothwaitePvpStatus.Lines(new GothwaitePvpStatusReport(true, 51, "gothwaite", 4000, Array.Empty<GothwaitePvpBotSample>(),
                census, SampleReport().Fights, SampleReport().Load))[7],
                Is.EqualTo("Groups: forming 0 (0 members), departed 3 (solo 3, duo 0, small 0, eight 0), average size 0.0 for groups of two or more, " +
                    "castle pool waiting 4."));
        });
    }

    [Test]
    public void TheFightLinesGiveTheKillsDeathsAndBandsOverTheWindow()
    {
        IReadOnlyList<string> lines = GothwaitePvpStatus.Lines(SampleReport());

        Assert.Multiple(() =>
        {
            Assert.That(lines[8], Is.EqualTo("Fights since server start (game time 30.0 min): mode bot kills 4, mode bot deaths 5."));
            Assert.That(lines[9], Is.EqualTo("  Levels 1-9: kills 2, deaths 1."));
            Assert.That(lines[10], Is.EqualTo("  Levels 10-19: kills 1, deaths 2."));
            Assert.That(lines[13], Is.EqualTo("  Levels 40-50: kills 0, deaths 0."));
        });
    }

    [Test]
    public void TheConLineLabelsEverySevenColoursOfTheModeBotKillsIncludingGreyAndPurple()
    {
        IReadOnlyList<string> lines = GothwaitePvpStatus.Lines(SampleReport());

        Assert.That(lines[14], Is.EqualTo("Mode bot kills by con (incl. self-defence): GREY 0, GREEN 1, BLUE 1, YELLOW 1, ORANGE 1, RED 0, PURPLE 0."));
    }

    [Test]
    public void TheAllFightsLineCountsThePlayersTheModeBotsKilledToo()
    {
        IReadOnlyList<string> lines = GothwaitePvpStatus.Lines(SampleReport());

        Assert.That(lines[15], Is.EqualTo("All arena fights with a mode bot on either side (players included): kills 7, deaths 6."));
    }

    [Test]
    public void APlayersKillOfAModeBotIsNoModeBotKill()
    {
        GothwaitePvpMode.Apply(GothwaitePvpSettings.Defaults with { Enabled = true });
        try
        {
            // A real player (not bound by the con rule) kills a mode bot a grey colour away: a fight, a death of the mode bot, no mode kill.
            GothwaitePvpStats.Record(new GothwaitePvpFight(new GothwaitePvpFighter("Player", 1, GothwaitePvpKind.Player),
                new GothwaitePvpFighter("Bot", 10, GothwaitePvpKind.ModeBot), true, 0, 0, 5000), 0);
            GothwaitePvpStatsSnapshot snapshot = GothwaitePvpStats.Snapshot();

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.TotalKills, Is.EqualTo(1), "every player-shaped killer is a kill in the stage 3 totals");
                Assert.That(snapshot.ModeKills, Is.EqualTo(0));
                Assert.That(snapshot.ModeDeaths, Is.EqualTo(1));
                Assert.That(snapshot.ModeKillsByCon.Single(con => con.Color == ConColor.GREY).Kills, Is.EqualTo(0),
                    "a grey player kill never reaches the mode bot table");
            });
        }
        finally
        {
            GothwaitePvpMode.Reset();
            GothwaitePvpStats.Reset();
        }
    }

    [Test]
    public void AModeBotsKillIsCountedInTheModeTableByItsKillerBandAndCon()
    {
        GothwaitePvpMode.Apply(GothwaitePvpSettings.Defaults with { Enabled = true });
        try
        {
            // Level 10 sees level 1 as grey: a mode bot's grey kill (self-defence) is labelled, not hidden.
            GothwaitePvpStats.Record(new GothwaitePvpFight(new GothwaitePvpFighter("Bot", 10, GothwaitePvpKind.ModeBot),
                new GothwaitePvpFighter("Brisk", 1, GothwaitePvpKind.Player), true, 0, 0, 5000), 0);
            GothwaitePvpStatsSnapshot snapshot = GothwaitePvpStats.Snapshot();

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.ModeKills, Is.EqualTo(1));
                Assert.That(snapshot.ModeBands.Select(band => band.Kills), Is.EqualTo(new[] { 0, 1, 0, 0, 0 }));
                Assert.That(snapshot.ModeKillsByCon.Single(con => con.Color == ConColor.GREY).Kills, Is.EqualTo(1));
            });
        }
        finally
        {
            GothwaitePvpMode.Reset();
            GothwaitePvpStats.Reset();
        }
    }

    [Test]
    public void TheLoadLineGivesTheAverageTheP95TheMaximaAndTheOnlineCount()
    {
        IReadOnlyList<string> lines = GothwaitePvpStatus.Lines(SampleReport());

        Assert.That(lines.Last(), Is.EqualTo("Load at 6 bots over the last 120 s (real time): turn avg 1.25 ms, p95 <= 2.50 ms, " +
            "max 40.00 ms (1200 turns); scan avg 0.30 ms (300); castle former avg 0.50 ms, max 3.00 ms (60 ticks)."));
    }

    [Test]
    public void TheLoadLineSaysSoBeforeAnyTurnIsMeasured()
    {
        GothwaitePvpStatusReport report = SampleReport() with { Load = new GothwaitePvpLoadSnapshot(0, 0, 0, 0, 0, 0, 0, 0, 0, 0) };

        Assert.That(GothwaitePvpStatus.Lines(report).Last(), Is.EqualTo("Load at 6 bots: no mode turns measured yet."));
    }

    [Test]
    public void TheNumbersStayInTheInvariantCulture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            IReadOnlyList<string> lines = GothwaitePvpStatus.Lines(SampleReport());

            Assert.Multiple(() =>
            {
                Assert.That(lines.Last(), Does.Contain("turn avg 1.25 ms"));
                Assert.That(lines.Last(), Does.Not.Contain("1,25"));
                Assert.That(lines[7], Does.Contain("average size 3.0 "));
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Test]
    public void ASampleReportFormatsEveryLineInOrder()
    {
        IReadOnlyList<string> lines = GothwaitePvpStatus.Lines(SampleReport());

        Assert.That(lines, Is.EqualTo(new[]
        {
            "Gothwaite PvP mode: on. Arena gothwaite (region 51), castle safe radius 4000.",
            "Bots online in the mode: 6 (castle resting 1, castle ready 1, departing 1, arena 3). A dead bot counts as castle resting until released.",
            "  Levels 1-9: 2 (castle resting 1, castle ready 1, departing 0, arena 0).",
            "  Levels 10-19: 2 (castle resting 0, castle ready 0, departing 1, arena 1).",
            "  Levels 20-29: 1 (castle resting 0, castle ready 0, departing 0, arena 1).",
            "  Levels 30-39: 1 (castle resting 0, castle ready 0, departing 0, arena 1).",
            "  Levels 40-50: 0 (castle resting 0, castle ready 0, departing 0, arena 0).",
            "Groups: forming 1 (3 members), departed 4 (solo 1, duo 2, small 1, eight 0), average size 3.0 for groups of two or more, castle pool waiting 1.",
            "Fights since server start (game time 30.0 min): mode bot kills 4, mode bot deaths 5.",
            "  Levels 1-9: kills 2, deaths 1.",
            "  Levels 10-19: kills 1, deaths 2.",
            "  Levels 20-29: kills 1, deaths 1.",
            "  Levels 30-39: kills 0, deaths 1.",
            "  Levels 40-50: kills 0, deaths 0.",
            "Mode bot kills by con (incl. self-defence): GREY 0, GREEN 1, BLUE 1, YELLOW 1, ORANGE 1, RED 0, PURPLE 0.",
            "All arena fights with a mode bot on either side (players included): kills 7, deaths 6.",
            "Load at 6 bots over the last 120 s (real time): turn avg 1.25 ms, p95 <= 2.50 ms, max 40.00 ms (1200 turns); scan avg 0.30 ms (300); castle former avg 0.50 ms, max 3.00 ms (60 ticks).",
        }));
    }
}
