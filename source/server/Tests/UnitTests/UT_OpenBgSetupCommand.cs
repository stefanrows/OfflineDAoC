using System;
using System.Linq;
using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    // open BG /bgsetup (spec 2026-10-11 §7): the pure parser of /bgsetup <key> <value> and the status window text.
    [TestFixture]
    public class UT_OpenBgSetupCommand
    {
        private static (bool ok, string key, string value, string error) Parse(string key, string value, int cycleMin = 15, int cycleMax = 45) =>
            OpenBgSetupParser.Parse(new[] { "&bgsetup", key, value }, cycleMin, cycleMax);

        [TestCase("enabled", "on", "open_bg_enabled", "True")]
        [TestCase("enabled", "off", "open_bg_enabled", "False")]
        [TestCase("groups8", "2", "open_bg_groups8", "2")]
        [TestCase("small", "6", "open_bg_groups_small", "6")]
        [TestCase("solos", "0", "open_bg_solos", "0")]
        [TestCase("max_bots", "120", "open_bg_max_bots", "120")]
        [TestCase("cycle_min", "5", "open_bg_cycle_min_minutes", "5")]
        [TestCase("cycle_max", "120", "open_bg_cycle_max_minutes", "120")]
        [TestCase("drift", "false", "open_bg_player_drift", "False")]
        [TestCase("DRIFT", "Yes", "open_bg_player_drift", "True")]
        [TestCase("open_bg_solos", "12", "open_bg_solos", "12")]
        public void ParseAcceptsEveryKey(string key, string value, string rowKey, string stored)
        {
            var result = Parse(key, value);
            Assert.Multiple(() =>
            {
                Assert.That(result.ok, Is.True, result.error);
                Assert.That(result.key, Is.EqualTo(rowKey));
                Assert.That(result.value, Is.EqualTo(stored));
                Assert.That(result.error, Is.Null);
            });
        }

        [Test]
        public void EveryPropertyRowHasAKey()
        {
            // The eight open_bg_* properties of OpenBattlegroundProperties are exactly the eight /bgsetup rows.
            string[] rows = typeof(OpenBattlegroundProperties).GetFields()
                .Select(field => field.GetCustomAttributes(typeof(DOL.GS.ServerProperties.ServerPropertyAttribute), false)
                    .Cast<DOL.GS.ServerProperties.ServerPropertyAttribute>().FirstOrDefault()?.Key)
                .Where(key => key != null).OrderBy(key => key).ToArray();
            Assert.That(OpenBgSetupParser.Keys.Select(key => key.RowKey).OrderBy(key => key), Is.EqualTo(rows));
        }

        [TestCase("groups8", "5")]
        [TestCase("groups8", "-1")]
        [TestCase("small", "7")]
        [TestCase("solos", "13")]
        [TestCase("max_bots", "7")]
        [TestCase("max_bots", "121")]
        [TestCase("cycle_min", "4")]
        [TestCase("cycle_max", "121")]
        [TestCase("solos", "four")]
        [TestCase("enabled", "maybe")]
        public void ParseRejectsOutOfRange(string key, string value)
        {
            var result = Parse(key, value);
            Assert.Multiple(() =>
            {
                Assert.That(result.ok, Is.False);
                Assert.That(result.value, Is.Null);
                Assert.That(result.error, Does.Contain(key));
            });
        }

        [Test]
        public void ParseRejectsUnknownKeyAndWrongArgumentCount()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Parse("speed", "3").ok, Is.False);
                Assert.That(OpenBgSetupParser.Parse(new[] { "&bgsetup", "solos" }, 15, 45).ok, Is.False);
                Assert.That(OpenBgSetupParser.Parse(new[] { "&bgsetup", "solos", "1", "2" }, 15, 45).ok, Is.False);
                Assert.That(OpenBgSetupParser.Parse(null, 15, 45).error, Is.EqualTo(OpenBgSetupParser.Usage));
            });
        }

        [Test]
        public void ParseRejectsCycleMinAboveMax()
        {
            Assert.Multiple(() =>
            {
                // Compared against the current other bound.
                Assert.That(Parse("cycle_min", "50", cycleMin: 15, cycleMax: 45).ok, Is.False);
                Assert.That(Parse("cycle_min", "50", cycleMin: 15, cycleMax: 45).error, Does.Contain("cycle_max 45"));
                Assert.That(Parse("cycle_max", "10", cycleMin: 15, cycleMax: 45).ok, Is.False);
                Assert.That(Parse("cycle_min", "45", cycleMin: 15, cycleMax: 45).ok, Is.True, "min equal to max is allowed");
                Assert.That(Parse("cycle_max", "15", cycleMin: 15, cycleMax: 45).ok, Is.True, "max equal to min is allowed");
                Assert.That(Parse("cycle_max", "100", cycleMin: 60, cycleMax: 90).ok, Is.True);
            });
        }

        private static OpenBgStatusSnapshot Snapshot(OpenBgBotStatus[] bots, OpenBgPlayerStatus[] players = null, string phase = null) =>
            new(new DateTime(2026, 10, 11, 3, 0, 0, DateTimeKind.Utc), true, true, true, true, null,
                phase ?? OpenBgStatusSnapshot.PhaseNotAvailable, null, 0, "Thidranki Faste: Midgard (Northmen)", 19.6, 20, 72,
                players ?? Array.Empty<OpenBgPlayerStatus>(), bots);

        private static readonly OpenBgSetupValues Defaults = new(true, 1, 2, 4, 72, 15, 45, true);

        [Test]
        public void StatusPageShowsLiveStateAndCrewPerRealm()
        {
            OpenBgBotStatus[] bots =
            {
                new("Albion", "Aldwyn", 50, "Cleric", 3, "small", "Aldwyn", "inside", true),
                new("Albion", "Brannoc", 47, "Armsman", 3, "small", "Aldwyn", "inside", false),
                new("Albion", "Cerys", 44, "Infiltrator", 4, "solo", null, "travelling", true),
                new("Midgard", "Hrolf", 50, "Healer", 7, "group8", "Hrolf", "leaving", true),
                new("Hibernia", "Aine", 48, "Ranger", 9, "solo", null, "inside", true),
            };
            var pages = OpenBgSetupPages.Pages(Snapshot(bots, new[] { new OpenBgPlayerStatus("Albion", "Ked", 50) }), Defaults);
            string live = string.Join("\n", pages[0]);
            Assert.Multiple(() =>
            {
                Assert.That(pages, Has.Count.EqualTo(2), "a small crew fits page 1; page 2 is the setup");
                Assert.That(live, Does.Contain("on and ready"));
                Assert.That(live, Does.Contain("Phase: n/a (stage 2)"));
                Assert.That(live, Does.Contain("Keep 11: Thidranki Faste: Midgard (Northmen)"));
                Assert.That(live, Does.Contain("achieved 19.6x (effective 20x)"));
                Assert.That(live, Does.Contain("Crew: 3 inside, 1 travelling, 1 leaving, 0 outside; cap 72"));
                Assert.That(live, Does.Contain("Players inside (1): Ked 50 Albion"));
                Assert.That(live, Does.Contain("Albion: 2 inside, 1 travelling; units 0 group8, 1 small, 1 solo."));
                Assert.That(live, Does.Contain("#3 small, leader Aldwyn: Aldwyn 50 Cleric, Brannoc 47 Armsman dead"));
                Assert.That(live, Does.Contain("#4 solo [travelling]: Cerys 44 Infiltrator"));
                Assert.That(live, Does.Contain("#7 group8, leader Hrolf [leaving]: Hrolf 50 Healer"));
                Assert.That(live, Does.Contain("#9 solo: Aine 48 Ranger"));
                Assert.That(live.IndexOf("Albion:", StringComparison.Ordinal), Is.LessThan(live.IndexOf("Midgard:", StringComparison.Ordinal)));
                Assert.That(string.Join("\n", pages[1]), Does.Contain("groups8 = 1 (0-4)"));
                Assert.That(string.Join("\n", pages[1]), Does.Contain("cycle_max = 45 (5-120)"));
                Assert.That(string.Join("\n", pages[1]), Does.Contain("enabled = True (on|off)"));
            });
        }

        [Test]
        public void StatusPageShowsTheDirectorPhaseWhenItExists()
        {
            OpenBgStatusSnapshot status = Snapshot(Array.Empty<OpenBgBotStatus>(), phase: "Push") with { Attacker = "Hibernia", PhaseSecondsLeft = 125 };
            string live = string.Join("\n", OpenBgSetupPages.Pages(status, Defaults)[0]);
            Assert.That(live, Does.Contain("Phase: Push, attacker Hibernia, 2m 5s left."));
            Assert.That(live, Does.Contain("Midgard: no crew."));
        }

        [Test]
        public void StatusPagesSplitALargeCrewWithinThePacketBudget()
        {
            string[] realms = { "Albion", "Midgard", "Hibernia" };
            OpenBgBotStatus[] bots = Enumerable.Range(0, 120).Select(index => new OpenBgBotStatus(realms[index % 3],
                "Botname" + index.ToString("000"), 40 + index % 11, "Paladin", 1 + index / 8, index < 96 ? "group8" : "solo",
                "Botname" + (index / 8 * 8).ToString("000"), "inside", true)).ToArray();
            var pages = OpenBgSetupPages.Pages(Snapshot(bots), Defaults);
            Assert.Multiple(() =>
            {
                Assert.That(pages.Count, Is.GreaterThan(2));
                Assert.That(pages[1][0], Does.StartWith("Change with /bgsetup"), "page 2 stays the setup");
                foreach (var page in pages)
                    Assert.That(page.Sum(OpenBgSetupPages.LineCost), Is.LessThanOrEqualTo(OpenBgSetupPages.PageBudgetBytes));
                string all = string.Join("\n", pages.SelectMany(page => page));
                foreach (OpenBgBotStatus bot in bots)
                    Assert.That(all, Does.Contain(bot.Name + " " + bot.Level), bot.Name);
                Assert.That(pages.Last().Last(), Does.StartWith($"Page {pages.Count}/{pages.Count}"));
            });
        }

        [Test]
        public void SetupPageShowsTheClampedValueInUse()
        {
            string setup = string.Join("\n", OpenBgSetupPages.SetupLines(Defaults with { MaxBots = 500 }));
            Assert.That(setup, Does.Contain("max_bots = 500 (8-120): most bots inside, all realms together. In use: 120."));
        }

        [Test]
        public void ChatLinesListEveryKey()
        {
            var lines = OpenBgSetupPages.ChatLines(Defaults);
            foreach (OpenBgSetupKey key in OpenBgSetupParser.Keys)
                Assert.That(lines, Has.Some.Contains($"{key.Name} = "), key.Name);
        }

        [Test]
        public void CrewStateAndKindFallback()
        {
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundStatus.StateOf(entered: false, leaving: false, inRegion: false), Is.EqualTo("travelling"));
                Assert.That(OpenBattlegroundStatus.StateOf(true, false, true), Is.EqualTo("inside"));
                Assert.That(OpenBattlegroundStatus.StateOf(true, false, false), Is.EqualTo("outside"));
                Assert.That(OpenBattlegroundStatus.StateOf(true, true, true), Is.EqualTo("leaving"));
                Assert.That(OpenBattlegroundStatus.KindFromSize(1), Is.EqualTo("solo"));
                Assert.That(OpenBattlegroundStatus.KindFromSize(3), Is.EqualTo("small"));
                Assert.That(OpenBattlegroundStatus.KindFromSize(7), Is.EqualTo("group8"));
                Assert.That(OpenBattlegroundStatus.GameIntervalFor(15_000, 20), Is.EqualTo(300_000));
                Assert.That(OpenBattlegroundStatus.GameIntervalFor(15_000, 0), Is.EqualTo(15_000));
            });
        }
    }
}
