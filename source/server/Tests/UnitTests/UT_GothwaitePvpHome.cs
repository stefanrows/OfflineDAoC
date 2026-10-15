using System;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// The Gothwaite PvP home table: which bots get a home row, which rows go back to their bot at mode-off, and what
    /// restore changes (docs/GOTHWAITE_PVP_MODE.md 6.7). Pure planner and copy rules only: no database, no GameBot.
    /// </summary>
    [TestFixture]
    public class UT_GothwaitePvpHome
    {
        private const string Utc = "2026-10-13T08:00:00.0000000Z";

        private static OfflineWorldBotRecord Bot(long id, int region = 1, int x = 100, int y = 200, int z = 300, bool retired = false) =>
            new()
            {
                BotId = id,
                Name = "Bot" + id,
                Realm = 1,
                ClassId = 6,
                RaceId = 1,
                Level = 12,
                Experience = 5000,
                RealmPoints = 800,
                MoneyCopper = 4321,
                RegionId = region,
                ZoneId = 3,
                ZoneName = "Zone3",
                X = x,
                Y = y,
                Z = z,
                BindRegionId = 2,
                BindX = 11,
                BindY = 22,
                BindZ = 33,
                CurrentCampId = "camp-7",
                ItineraryJson = "[\"stop-a\"]",
                IsRetired = retired,
            };

        private static GothwaitePvpHomeRecord Home(long homeId, long botId, int region = 1, int x = 1000) => new()
        {
            HomeId = homeId,
            BotId = botId,
            RegionId = region,
            ZoneId = 1,
            ZoneName = "Cotswold",
            X = x,
            Y = 2000,
            Z = 3000,
            BindRegionId = 2,
            BindX = 4,
            BindY = 5,
            BindZ = 6,
            CurrentCampId = "home-camp",
            ItineraryJson = "[\"home\"]",
            SnapshotUtc = Utc,
        };

        [Test]
        public void ModeOnStoresAHomeOnlyForANonRetiredBotThatHasNone()
        {
            var bots = new[] { Bot(1), Bot(2), Bot(3, retired: true) };
            var homes = new[] { Home(10, 1) };

            GothwaitePvpHomePlan plan = GothwaitePvpHomeStore.Plan(true, bots, homes);

            Assert.Multiple(() =>
            {
                Assert.That(plan.ToSnapshot.Select(bot => bot.BotId), Is.EqualTo(new long[] { 2 }));
                Assert.That(plan.Kept, Is.EqualTo(1));
                Assert.That(plan.ToRestore, Is.Empty);
                Assert.That(plan.Orphans, Is.Empty);
            });
        }

        [Test]
        public void ModeOnNeverOverwritesAnExistingHome()
        {
            var bots = new[] { Bot(1, region: 51, x: 534900, y: 548790, z: 4800) };
            var stored = Home(10, 1, region: 1, x: 1000);

            GothwaitePvpHomePlan plan = GothwaitePvpHomeStore.Plan(true, bots, new[] { stored });

            Assert.Multiple(() =>
            {
                Assert.That(plan.ToSnapshot, Is.Empty, "the stored home already holds the pre-mode place");
                Assert.That(plan.Kept, Is.EqualTo(1));
                Assert.That(stored.RegionId, Is.EqualTo(1));
                Assert.That(stored.X, Is.EqualTo(1000));
            });
        }

        [Test]
        public void ModeOnKeepsHomesWhoseBotIsGone()
        {
            var homes = new[] { Home(10, 99) };

            GothwaitePvpHomePlan plan = GothwaitePvpHomeStore.Plan(true, new[] { Bot(1) }, homes);

            Assert.Multiple(() =>
            {
                Assert.That(plan.Orphans, Is.Empty, "orphans are only deleted at mode-off");
                Assert.That(plan.ToRestore, Is.Empty);
                Assert.That(plan.ToSnapshot.Select(bot => bot.BotId), Is.EqualTo(new long[] { 1 }));
                Assert.That(plan.Kept, Is.EqualTo(1), "every stored row is left alone while the mode is on");
            });
        }

        [Test]
        public void ModeOffRestoresEveryHomeWhoseBotExistsAndListsTheOrphans()
        {
            var bots = new[] { Bot(1), Bot(2) };
            var homes = new[] { Home(10, 1), Home(11, 3), Home(12, 2) };

            GothwaitePvpHomePlan plan = GothwaitePvpHomeStore.Plan(false, bots, homes);

            Assert.Multiple(() =>
            {
                Assert.That(plan.ToRestore.Select(pair => pair.Record.BotId), Is.EqualTo(new long[] { 1, 2 }));
                Assert.That(plan.ToRestore.Select(pair => pair.Home.HomeId), Is.EqualTo(new long[] { 10, 12 }));
                Assert.That(plan.Orphans.Select(home => home.HomeId), Is.EqualTo(new long[] { 11 }));
                Assert.That(plan.ToSnapshot, Is.Empty);
                Assert.That(plan.Kept, Is.EqualTo(0));
            });
        }

        [Test]
        public void ModeOffRestoresHomesOfRetiredBotsToo()
        {
            GothwaitePvpHomePlan plan = GothwaitePvpHomeStore.Plan(false, new[] { Bot(1, retired: true) }, new[] { Home(10, 1) });

            Assert.That(plan.ToRestore.Select(pair => pair.Record.BotId), Is.EqualTo(new long[] { 1 }));
        }

        [Test]
        public void ModeOffAdoptsTheLowestHomeOfADuplicateAndDeletesTheRest()
        {
            var homes = new[] { Home(15, 1), Home(14, 1) };

            GothwaitePvpHomePlan plan = GothwaitePvpHomeStore.Plan(false, new[] { Bot(1) }, homes);

            Assert.Multiple(() =>
            {
                Assert.That(plan.ToRestore.Select(pair => pair.Home.HomeId), Is.EqualTo(new long[] { 14 }));
                Assert.That(plan.Orphans.Select(home => home.HomeId), Is.EqualTo(new long[] { 15 }));
            });
        }

        [Test]
        public void SnapshotCopiesTheHomeValuesOfTheBot()
        {
            OfflineWorldBotRecord bot = Bot(7, region: 51, x: 534900, y: 548790, z: 4800);

            GothwaitePvpHomeRecord home = GothwaitePvpHomeStore.Snapshot(bot, Utc);

            Assert.Multiple(() =>
            {
                Assert.That(home.BotId, Is.EqualTo(7));
                Assert.That(home.RegionId, Is.EqualTo(51));
                Assert.That(home.ZoneId, Is.EqualTo(3));
                Assert.That(home.ZoneName, Is.EqualTo("Zone3"));
                Assert.That(home.X, Is.EqualTo(534900));
                Assert.That(home.Y, Is.EqualTo(548790));
                Assert.That(home.Z, Is.EqualTo(4800));
                Assert.That(home.BindRegionId, Is.EqualTo(2));
                Assert.That(home.BindX, Is.EqualTo(11));
                Assert.That(home.BindY, Is.EqualTo(22));
                Assert.That(home.BindZ, Is.EqualTo(33));
                Assert.That(home.CurrentCampId, Is.EqualTo("camp-7"));
                Assert.That(home.ItineraryJson, Is.EqualTo("[\"stop-a\"]"));
                Assert.That(home.SnapshotUtc, Is.EqualTo(Utc));
            });
        }

        [Test]
        public void RestoreCopiesPositionBindCampAndItineraryAndKeepsProgress()
        {
            OfflineWorldBotRecord record = Bot(1, region: 51, x: 534900, y: 548790, z: 4800);
            record.Level = 20;
            record.Experience = 99_999;
            record.RealmPoints = 4_444;
            record.MoneyCopper = 777;
            record.Name = "Kept name";

            GothwaitePvpHomeStore.Restore(Home(10, 1, region: 1, x: 1000), record);

            Assert.Multiple(() =>
            {
                Assert.That(record.RegionId, Is.EqualTo(1));
                Assert.That(record.ZoneId, Is.EqualTo(1));
                Assert.That(record.ZoneName, Is.EqualTo("Cotswold"));
                Assert.That(record.X, Is.EqualTo(1000));
                Assert.That(record.Y, Is.EqualTo(2000));
                Assert.That(record.Z, Is.EqualTo(3000));
                Assert.That(record.BindRegionId, Is.EqualTo(2));
                Assert.That(record.BindX, Is.EqualTo(4));
                Assert.That(record.BindY, Is.EqualTo(5));
                Assert.That(record.BindZ, Is.EqualTo(6));
                Assert.That(record.CurrentCampId, Is.EqualTo("home-camp"));
                Assert.That(record.ItineraryJson, Is.EqualTo("[\"home\"]"));

                Assert.That(record.Level, Is.EqualTo(20), "level is never restored");
                Assert.That(record.Experience, Is.EqualTo(99_999), "experience is never restored");
                Assert.That(record.RealmPoints, Is.EqualTo(4_444), "realm points are never restored");
                Assert.That(record.MoneyCopper, Is.EqualTo(777), "money is never restored");
                Assert.That(record.Name, Is.EqualTo("Kept name"));
            });
        }

        [Test]
        public void SnapshotThenRestoreReturnsTheBotToItsPreModePlace()
        {
            OfflineWorldBotRecord record = Bot(1, region: 1, x: 100, y: 200, z: 300);
            GothwaitePvpHomeRecord home = GothwaitePvpHomeStore.Snapshot(record, Utc);

            // The mode moves the bot and its bind, and the normal save writes those values into the record.
            record.RegionId = 51;
            record.X = 534900;
            record.Y = 548790;
            record.Z = 4800;
            record.BindX = 535000;
            record.BindY = 548790;
            GothwaitePvpHomeStore.Restore(home, record);

            Assert.Multiple(() =>
            {
                Assert.That(record.RegionId, Is.EqualTo(1));
                Assert.That(record.X, Is.EqualTo(100));
                Assert.That(record.Y, Is.EqualTo(200));
                Assert.That(record.Z, Is.EqualTo(300));
                Assert.That(record.BindX, Is.EqualTo(11));
                Assert.That(record.BindY, Is.EqualTo(22));
            });
        }

        [Test]
        public void EmptyInputsPlanNothing()
        {
            GothwaitePvpHomePlan active = GothwaitePvpHomeStore.Plan(true, Array.Empty<OfflineWorldBotRecord>(),
                Array.Empty<GothwaitePvpHomeRecord>());
            GothwaitePvpHomePlan off = GothwaitePvpHomeStore.Plan(false, Array.Empty<OfflineWorldBotRecord>(),
                Array.Empty<GothwaitePvpHomeRecord>());

            Assert.Multiple(() =>
            {
                Assert.That(active.ToSnapshot, Is.Empty);
                Assert.That(active.Kept, Is.EqualTo(0));
                Assert.That(off.ToRestore, Is.Empty);
                Assert.That(off.Orphans, Is.Empty);
            });
        }

        [Test]
        public void Plan_Is_Independent_Of_The_Reserved_Set()
        {
            var bots = new[] { Bot(1), Bot(2) };
            try
            {
                GothwaitePvpOpenBgReserve.Install(new System.Collections.Generic.HashSet<long>());
                var without = GothwaitePvpHomeStore.Plan(true, bots, Array.Empty<GothwaitePvpHomeRecord>());
                GothwaitePvpOpenBgReserve.Install(new System.Collections.Generic.HashSet<long> { 1 });
                var with = GothwaitePvpHomeStore.Plan(true, bots, Array.Empty<GothwaitePvpHomeRecord>());
                Assert.That(with.ToSnapshot.Select(b => b.BotId), Is.EqualTo(without.ToSnapshot.Select(b => b.BotId)));
            }
            finally
            {
                GothwaitePvpOpenBgReserve.Install(null);
            }
        }
    }
}
