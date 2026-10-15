using System.Linq;
using NUnit.Framework;
using OfflineDaoc.Configuration;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Pure tests of the Gothwaite PvP fight statistics (docs/GOTHWAITE_PVP_MODE.md section 8, stage 3): the level bands, the heat
    /// decay and radius, the ring buffer bound, the snapshot counts, the GOTH_PVP_KILL line, and the no-op paths. No GameBot and no
    /// database: the counting goes through the pure <see cref="GothwaitePvpStats.Record"/> entry point.
    /// </summary>
    [TestFixture]
    public class UT_GothwaitePvpStats
    {
        private const int CastleDistance = 5000;

        [SetUp]
        public void SetUp()
        {
            GothwaitePvpMode.Reset();
            GothwaitePvpStats.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            GothwaitePvpMode.Reset();
            GothwaitePvpStats.Reset();
        }

        private static void TurnOn() => GothwaitePvpMode.Apply(GothwaitePvpSettings.Defaults with { Enabled = true });

        private static GothwaitePvpFight Fight(GothwaitePvpKind killerKind, int killerLevel, GothwaitePvpKind victimKind, int victimLevel,
            bool playerKill, int x = 0, int y = 0) =>
            new(new GothwaitePvpFighter("Killer", killerLevel, killerKind), new GothwaitePvpFighter("Victim", victimLevel, victimKind),
                playerKill, x, y, CastleDistance);

        private static GothwaitePvpFight PlayerKillAt(int x, int y) =>
            Fight(GothwaitePvpKind.ModeBot, 10, GothwaitePvpKind.Player, 10, true, x, y);

        [TestCase(0, -1)]
        [TestCase(-5, -1)]
        [TestCase(1, 0)]
        [TestCase(9, 0)]
        [TestCase(10, 1)]
        [TestCase(19, 1)]
        [TestCase(20, 2)]
        [TestCase(29, 2)]
        [TestCase(30, 3)]
        [TestCase(39, 3)]
        [TestCase(40, 4)]
        [TestCase(50, 4)]
        [TestCase(51, 4)]
        public void TheBandFunctionSplitsTheLevelsIntoFiveBands(int level, int band)
        {
            Assert.That(GothwaitePvpStats.BandOf(level), Is.EqualTo(band));
        }

        [Test]
        public void TheBandLabelsAreTheFiveLevelRanges()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpStats.BandLabel(0), Is.EqualTo("1-9"));
                Assert.That(GothwaitePvpStats.BandLabel(1), Is.EqualTo("10-19"));
                Assert.That(GothwaitePvpStats.BandLabel(2), Is.EqualTo("20-29"));
                Assert.That(GothwaitePvpStats.BandLabel(3), Is.EqualTo("30-39"));
                Assert.That(GothwaitePvpStats.BandLabel(4), Is.EqualTo("40-50"));
                Assert.That(GothwaitePvpStats.BandLabel(5), Is.EqualTo(string.Empty));
            });
        }

        [Test]
        public void HeatWeightFallsLinearlyToZeroOverTenMinutes()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpStats.Weight(0, 0), Is.EqualTo(1.0).Within(1e-9));
                Assert.That(GothwaitePvpStats.Weight(0, 150_000), Is.EqualTo(0.75).Within(1e-9));
                Assert.That(GothwaitePvpStats.Weight(0, 300_000), Is.EqualTo(0.5).Within(1e-9));
                Assert.That(GothwaitePvpStats.Weight(0, 599_999), Is.GreaterThan(0));
                Assert.That(GothwaitePvpStats.Weight(0, 600_000), Is.EqualTo(0));
                Assert.That(GothwaitePvpStats.Weight(0, 900_000), Is.EqualTo(0));
                // A fight stamped after "now" counts as just now, not more than fully.
                Assert.That(GothwaitePvpStats.Weight(1_000, 0), Is.EqualTo(1.0).Within(1e-9));
            });
        }

        [Test]
        public void HeatDecaysWithTheAgeOfTheFight()
        {
            TurnOn();
            GothwaitePvpStats.Record(PlayerKillAt(0, 0), 0);
            var spot = new GothwaitePvpSpot("P", 0, 0, 0);

            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpStats.Heat(spot, 0), Is.EqualTo(1.0).Within(1e-9));
                Assert.That(GothwaitePvpStats.Heat(spot, 300_000), Is.EqualTo(0.5).Within(1e-9));
                Assert.That(GothwaitePvpStats.Heat(spot, 600_000), Is.EqualTo(0.0).Within(1e-9));
                Assert.That(GothwaitePvpStats.Heat(spot, 1_200_000), Is.EqualTo(0.0).Within(1e-9));
            });
        }

        [Test]
        public void HeatCountsOnlyFightsWithinTwoThousandFiveHundredUnits()
        {
            TurnOn();
            GothwaitePvpStats.Record(PlayerKillAt(0, 0), 0);

            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpStats.Heat(new GothwaitePvpSpot("edge", 2500, 0, 0), 0), Is.EqualTo(1.0).Within(1e-9));
                Assert.That(GothwaitePvpStats.Heat(new GothwaitePvpSpot("out", 2501, 0, 0), 0), Is.EqualTo(0.0).Within(1e-9));
                Assert.That(GothwaitePvpStats.Heat(new GothwaitePvpSpot("diagonal", 0, -2499, 0), 0), Is.EqualTo(1.0).Within(1e-9));
                Assert.That(GothwaitePvpStats.Heat(new GothwaitePvpSpot("far diagonal", 2000, 2000, 0), 0), Is.EqualTo(0.0).Within(1e-9));
            });
        }

        [Test]
        public void HeatSumsTheWeightsOfTheRecentFights()
        {
            TurnOn();
            GothwaitePvpStats.Record(PlayerKillAt(0, 0), 0);
            GothwaitePvpStats.Record(PlayerKillAt(100, 0), 300_000);

            Assert.That(GothwaitePvpStats.Heat(new GothwaitePvpSpot("P", 0, 0, 0), 300_000), Is.EqualTo(1.5).Within(1e-9));
        }

        [Test]
        public void TheRingBufferKeepsOnlyTheLastFiveHundredTwelveFights()
        {
            TurnOn();
            var near = new GothwaitePvpSpot("near", 0, 0, 0);
            GothwaitePvpStats.Record(PlayerKillAt(0, 0), 0);
            for (int index = 0; index < GothwaitePvpStats.RingCapacity - 1; index++)
                GothwaitePvpStats.Record(PlayerKillAt(10_000, 10_000), 0);

            Assert.That(GothwaitePvpStats.Heat(near, 0), Is.EqualTo(1.0).Within(1e-9),
                "The first fight is still in the buffer while it is not overwritten.");

            GothwaitePvpStats.Record(PlayerKillAt(10_000, 10_000), 0);

            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpStats.Heat(near, 0), Is.EqualTo(0.0).Within(1e-9),
                    "The 513th fight overwrites the oldest one, so the first fight no longer counts.");
                Assert.That(GothwaitePvpStats.Heat(new GothwaitePvpSpot("far", 10_000, 10_000, 0), 0),
                    Is.EqualTo(GothwaitePvpStats.RingCapacity).Within(1e-6));
            });
        }

        [Test]
        public void ResetClearsTheHeatHistory()
        {
            TurnOn();
            GothwaitePvpStats.Record(PlayerKillAt(0, 0), 0);

            GothwaitePvpStats.Reset();

            Assert.That(GothwaitePvpStats.Heat(new GothwaitePvpSpot("P", 0, 0, 0), 0), Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void TheSnapshotCountsKillsDeathsBandsAndConColours()
        {
            TurnOn();
            // Con from the killer's view: level 10 sees 1 as grey, 10 as yellow, 12 as red, 14 as purple (ConLevels, row 10).
            GothwaitePvpStats.Record(Fight(GothwaitePvpKind.ModeBot, 10, GothwaitePvpKind.Player, 12, true), 0); // red kill, band 1
            GothwaitePvpStats.Record(Fight(GothwaitePvpKind.ModeBot, 10, GothwaitePvpKind.Player, 1, true), 0);  // grey kill, band 1
            GothwaitePvpStats.Record(Fight(GothwaitePvpKind.Other, 5, GothwaitePvpKind.ModeBot, 35, false), 0); // a monster killed a mode bot
            GothwaitePvpStats.Record(Fight(GothwaitePvpKind.Player, 45, GothwaitePvpKind.ModeBot, 45, true), 0); // yellow kill, band 4

            GothwaitePvpStatsSnapshot snapshot = GothwaitePvpStats.Snapshot();

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.TotalKills, Is.EqualTo(3));
                Assert.That(snapshot.TotalDeaths, Is.EqualTo(4));
                Assert.That(snapshot.HeatWindowMilliseconds, Is.EqualTo(600_000));
                Assert.That(snapshot.Bands.Select(band => band.Label), Is.EqualTo(new[] { "1-9", "10-19", "20-29", "30-39", "40-50" }));
                Assert.That(snapshot.Bands.Select(band => band.Kills), Is.EqualTo(new[] { 0, 2, 0, 0, 1 }));
                Assert.That(snapshot.Bands.Select(band => band.Deaths), Is.EqualTo(new[] { 1, 1, 0, 1, 1 }));
                Assert.That(snapshot.KillsByCon.Count, Is.EqualTo(7));
                Assert.That(snapshot.KillsByCon[0], Is.EqualTo(new GothwaitePvpConCount(ConColor.GREY, 1)));
                Assert.That(snapshot.KillsByCon[3], Is.EqualTo(new GothwaitePvpConCount(ConColor.YELLOW, 1)));
                Assert.That(snapshot.KillsByCon[5], Is.EqualTo(new GothwaitePvpConCount(ConColor.RED, 1)));
                Assert.That(snapshot.KillsByCon[6], Is.EqualTo(new GothwaitePvpConCount(ConColor.PURPLE, 0)));
            });
        }

        [Test]
        public void TheSnapshotIsAReadOnlyCopy()
        {
            TurnOn();
            GothwaitePvpStatsSnapshot before = GothwaitePvpStats.Snapshot();

            GothwaitePvpStats.Record(PlayerKillAt(0, 0), 0);

            Assert.Multiple(() =>
            {
                Assert.That(before.TotalKills, Is.EqualTo(0));
                Assert.That(GothwaitePvpStats.Snapshot().TotalKills, Is.EqualTo(1));
            });
        }

        [Test]
        public void NothingCountsWhileTheModeIsOff()
        {
            GothwaitePvpStats.Record(PlayerKillAt(0, 0), 0);
            GothwaitePvpStats.RecordKill(null, null);

            GothwaitePvpStatsSnapshot snapshot = GothwaitePvpStats.Snapshot();

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.TotalKills, Is.EqualTo(0));
                Assert.That(snapshot.TotalDeaths, Is.EqualTo(0));
                Assert.That(GothwaitePvpStats.Heat(new GothwaitePvpSpot("P", 0, 0, 0), 0), Is.EqualTo(0.0).Within(1e-9));
            });
        }

        [Test]
        public void RecordKillDoesNothingWithNullsWhetherOrNotTheModeIsOn()
        {
            Assert.DoesNotThrow(() => GothwaitePvpStats.RecordKill(null, null));
            TurnOn();
            Assert.DoesNotThrow(() => GothwaitePvpStats.RecordKill(null, null));
            Assert.DoesNotThrow(() => GothwaitePvpStats.RecordKill(null, null));

            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpStats.Snapshot().TotalKills, Is.EqualTo(0));
                Assert.That(GothwaitePvpStats.Snapshot().TotalDeaths, Is.EqualTo(0));
            });
        }

        [Test]
        public void TheCountingRuleNeedsAnActiveModeAKillerAPlayerVictimAModeBotAndTheArena()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpStats.ShouldCount(false, true, true, true, true, true), Is.False, "mode off");
                Assert.That(GothwaitePvpStats.ShouldCount(true, false, true, true, true, true), Is.False, "no killer");
                Assert.That(GothwaitePvpStats.ShouldCount(true, true, false, true, true, true), Is.False, "victim is a pet or a monster");
                Assert.That(GothwaitePvpStats.ShouldCount(true, true, true, false, false, true), Is.False, "no mode bot in the fight");
                Assert.That(GothwaitePvpStats.ShouldCount(true, true, true, true, false, true), Is.True, "a mode bot killed");
                Assert.That(GothwaitePvpStats.ShouldCount(true, true, true, false, true, true), Is.True, "a mode bot died");
                Assert.That(GothwaitePvpStats.ShouldCount(true, true, true, true, true, false), Is.False, "outside the arena region");
                Assert.That(GothwaitePvpStats.ShouldCount(true, true, true, true, true, true), Is.True);
            });
        }

        [Test]
        public void TheKillLineNamesBothFightersTheConsAndThePlace()
        {
            var fight = new GothwaitePvpFight(new GothwaitePvpFighter("Ardent", 10, GothwaitePvpKind.ModeBot),
                new GothwaitePvpFighter("Brisk", 12, GothwaitePvpKind.Player), true, 100, -200, 5000.4);

            string line = GothwaitePvpStats.KillLine(fight, ConColor.RED, ConColor.GREEN);

            Assert.That(line, Is.EqualTo("GOTH_PVP_KILL killer=\"Ardent\" killer_level=10 killer_kind=mode_bot victim=\"Brisk\" " +
                "victim_level=12 victim_kind=player con_killer_to_victim=RED con_victim_to_killer=GREEN x=100 y=-200 distance=5000"));
        }

        [TestCase(GothwaitePvpKind.ModeBot, "mode_bot")]
        [TestCase(GothwaitePvpKind.Player, "player")]
        [TestCase(GothwaitePvpKind.Companion, "companion")]
        [TestCase(GothwaitePvpKind.Other, "other")]
        public void TheKindNamesAreTheLogWords(GothwaitePvpKind kind, string name)
        {
            Assert.That(GothwaitePvpStats.KindName(kind), Is.EqualTo(name));
        }

        [TestCase(10, 1, ConColor.GREY)]
        [TestCase(10, 7, ConColor.GREEN)]
        [TestCase(10, 10, ConColor.YELLOW)]
        [TestCase(10, 12, ConColor.RED)]
        [TestCase(10, 14, ConColor.PURPLE)]
        public void TheConOfAFighterIsTheServerConTable(int fromLevel, int toLevel, ConColor expected)
        {
            Assert.That(GothwaitePvpStats.ConOf(fromLevel, toLevel), Is.EqualTo(expected));
        }
    }

    /// <summary>
    /// The truth tables of the mode bot's self-defence and area rules (docs/GOTHWAITE_PVP_MODE.md section 6.2). The game-bound
    /// parts (the attacker tracker, the server rules, the radius scans) need a running world and are covered by the mode-off and
    /// live checks; these pure tables pin the decision.
    /// </summary>
    [TestFixture]
    public class UT_GothwaitePvpSelfDefenceTable
    {
        [Test]
        public void ANonModeBotIsNeverFiltered()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpSelfDefence.Allows(false, false, false), Is.True);
                Assert.That(GothwaitePvpSelfDefence.Allows(false, true, false), Is.True);
                Assert.That(GothwaitePvpSelfDefence.Allows(false, false, true), Is.True);
            });
        }

        [Test]
        public void AModeBotMayTakeALegalOpeningOrAnAttackerOnly()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpSelfDefence.Allows(true, true, false), Is.True, "a legal opening");
                Assert.That(GothwaitePvpSelfDefence.Allows(true, false, true), Is.True, "self-defence against an attacker");
                Assert.That(GothwaitePvpSelfDefence.Allows(true, true, true), Is.True);
                Assert.That(GothwaitePvpSelfDefence.Allows(true, false, false), Is.False, "grey, purple, a castle target or an idle monster");
            });
        }

        [Test]
        public void AnAreaSpellIsRefusedOnlyForAHarmfulModeCasterWithASparedBystander()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpSelfDefence.AreaRefused(true, true, true, true), Is.True);
                Assert.That(GothwaitePvpSelfDefence.AreaRefused(true, true, true, false), Is.False, "nobody spared in the area");
                Assert.That(GothwaitePvpSelfDefence.AreaRefused(false, true, true, true), Is.False, "not a mode caster");
                Assert.That(GothwaitePvpSelfDefence.AreaRefused(true, false, true, true), Is.False, "a helpful spell");
                Assert.That(GothwaitePvpSelfDefence.AreaRefused(true, true, false, true), Is.False, "a single-target spell");
            });
        }

        [Test]
        public void ANoteIsRecentForTenSecondsAndNoLonger()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpSelfDefence.IsRecent(0, 0), Is.True);
                Assert.That(GothwaitePvpSelfDefence.IsRecent(0, GothwaitePvpSelfDefence.NoteMilliseconds), Is.True);
                Assert.That(GothwaitePvpSelfDefence.IsRecent(0, GothwaitePvpSelfDefence.NoteMilliseconds + 1), Is.False);
            });
        }

        [Test]
        public void TheSelfDefenceHelpersKeepEverythingWithoutTheMode()
        {
            GothwaitePvpMode.Reset();
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(GothwaitePvpSelfDefence.MayFight(null, null), Is.True, "no bot, mode off: no change");
                    Assert.That(GothwaitePvpSelfDefence.Drops(null, null), Is.False);
                    Assert.That(GothwaitePvpSelfDefence.Keep(null, null), Is.Null);
                    Assert.That(GothwaitePvpSelfDefence.SparedAround(null, 2400), Is.Empty);
                    Assert.That(GothwaitePvpSelfDefence.RefusesCast(null, null, null), Is.False);
                });
            }
            finally
            {
                GothwaitePvpMode.Reset();
            }
        }
    }
}
