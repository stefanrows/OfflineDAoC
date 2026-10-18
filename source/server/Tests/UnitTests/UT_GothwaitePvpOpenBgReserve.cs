using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_GothwaitePvpOpenBgReserve
    {
        [TearDown]
        public void TearDown()
        {
            GothwaitePvpMode.Reset();
        }

        private static List<(long BotId, int Realm, int Level, bool Retired)> Bots(int realm, int count, int level = 45, long start = 1) =>
            Enumerable.Range(0, count).Select(i => (start + i, realm, level, false)).ToList();

        [Test]
        public void ReserveSize_Follows_Formula()
        {
            Assert.That(GothwaitePvpOpenBgReserve.ReserveSize(20, 72), Is.EqualTo(50));
            Assert.That(GothwaitePvpOpenBgReserve.ReserveSize(20, 24), Is.EqualTo(20));
            Assert.That(GothwaitePvpOpenBgReserve.ReserveSize(0, 72), Is.EqualTo(0));
        }

        [Test]
        public void Order_Is_Deterministic_And_Spreads()
        {
            Assert.That(GothwaitePvpOpenBgReserve.Order(7), Is.EqualTo(GothwaitePvpOpenBgReserve.Order(7)));
            Assert.That(GothwaitePvpOpenBgReserve.Order(7), Is.Not.EqualTo(GothwaitePvpOpenBgReserve.Order(8)));
        }

        [Test]
        public void Choose_Is_Empty_When_Mode_Or_OpenBg_Off()
        {
            var bots = Bots(1, 100);
            Assert.That(GothwaitePvpOpenBgReserve.Choose(false, true, 40, 50, _ => 20, 72, bots), Is.Empty);
            Assert.That(GothwaitePvpOpenBgReserve.Choose(true, false, 40, 50, _ => 20, 72, bots), Is.Empty);
        }

        [Test]
        public void Choose_Filters_Realm_Band_And_Retired()
        {
            var bots = Bots(1, 20);
            bots.AddRange(Bots(4, 20, start: 100));
            bots.AddRange(Bots(2, 20, level: 30, start: 200));
            bots.AddRange(Enumerable.Range(0, 20).Select(i => ((long)300 + i, 3, 45, true)));
            var set = GothwaitePvpOpenBgReserve.Choose(true, true, 40, 50, _ => 20, 72, bots);
            Assert.That(set, Is.Not.Empty);
            Assert.That(set.All(id => id <= 20), Is.True);
        }

        [Test]
        public void Choose_Caps_At_30_Percent_Rounded_Up()
        {
            var set = GothwaitePvpOpenBgReserve.Choose(true, true, 40, 50, _ => 20, 72, Bots(1, 10));
            Assert.That(set.Count, Is.EqualTo(3));
        }

        [Test]
        public void Choose_Is_Order_Independent_And_Stable()
        {
            var bots = Bots(1, 60);
            bots.AddRange(Bots(2, 60, start: 100));
            var a = GothwaitePvpOpenBgReserve.Choose(true, true, 40, 50, _ => 6, 72, bots);
            var b = GothwaitePvpOpenBgReserve.Choose(true, true, 40, 50, _ => 6, 72, Enumerable.Reverse(bots));
            var c = GothwaitePvpOpenBgReserve.Choose(true, true, 40, 50, _ => 6, 72, bots);
            Assert.That(b.OrderBy(x => x), Is.EqualTo(a.OrderBy(x => x)));
            Assert.That(c.OrderBy(x => x), Is.EqualTo(a.OrderBy(x => x)));
        }

        [Test]
        public void Choose_Ignores_Bot_Outside_Band()
        {
            var bots = Bots(1, 60);
            var before = GothwaitePvpOpenBgReserve.Choose(true, true, 40, 50, _ => 6, 72, bots);
            bots.Add((999, 1, 20, false));
            Assert.That(GothwaitePvpOpenBgReserve.Choose(true, true, 40, 50, _ => 6, 72, bots).OrderBy(x => x), Is.EqualTo(before.OrderBy(x => x)));
        }

        [Test]
        public void Choose_Band_Edges_Are_Inclusive()
        {
            var bots = new List<(long BotId, int Realm, int Level, bool Retired)> { (1, 1, 40, false), (2, 1, 50, false), (3, 1, 39, false), (4, 1, 51, false) };
            var set = GothwaitePvpOpenBgReserve.Choose(true, true, 40, 50, _ => 20, 72, bots);
            Assert.That(set.OrderBy(x => x), Is.EqualTo(new long[] { 1, 2 }).Or.EqualTo(new long[] { 1 }).Or.EqualTo(new long[] { 2 }));
            var many = Bots(1, 10, level: 40).Concat(Bots(1, 10, level: 50, start: 100)).Concat(Bots(1, 10, level: 39, start: 200)).Concat(Bots(1, 10, level: 51, start: 300));
            var big = GothwaitePvpOpenBgReserve.Choose(true, true, 40, 50, _ => 20, 72, many);
            Assert.That(big.Count, Is.EqualTo(6));
            Assert.That(big.All(id => id < 200), Is.True);
        }

        [Test]
        public void Choose_Reserves_Per_Realm_Seats()
        {
            var bots = Bots(1, 100).Concat(Bots(2, 100, start: 1000)).ToList();
            var set = GothwaitePvpOpenBgReserve.Choose(true, true, 40, 50, r => r == 1 ? 4 : 20, 72, bots);
            Assert.That(set.Count(id => id < 1000), Is.EqualTo(10));
            Assert.That(set.Count(id => id >= 1000), Is.EqualTo(30));
        }

        [Test]
        public void ReservedWorldBot_Excludes_NonWorldBots_And_Uses_DatabaseId()
        {
            GothwaitePvpOpenBgReserve.Install(new HashSet<long> { 5 });
            Assert.That(GothwaitePvpOpenBgReserve.IsReservedWorldBot(true, true, false, false, false, 5), Is.True);
            Assert.That(GothwaitePvpOpenBgReserve.IsReservedWorldBot(true, true, false, false, false, 6), Is.False);
            Assert.That(GothwaitePvpOpenBgReserve.IsReservedWorldBot(false, true, false, false, false, 5), Is.False);
            Assert.That(GothwaitePvpOpenBgReserve.IsReservedWorldBot(true, true, true, false, false, 5), Is.False);
            Assert.That(GothwaitePvpOpenBgReserve.IsReservedWorldBot(true, true, false, true, false, 5), Is.False);
            Assert.That(GothwaitePvpOpenBgReserve.IsReservedWorldBot(true, true, false, false, true, 5), Is.False);
            Assert.That(GothwaitePvpOpenBgReserve.IsReservedWorldBot(true, false, false, false, false, 5), Is.False);
        }

        [Test]
        public void IsModeBot_Respects_Reserved()
        {
            Assert.That(GothwaitePvpMode.IsModeBot(true, true, false, false, false, reserved: true), Is.False);
            Assert.That(GothwaitePvpMode.IsModeBot(false, true, false, false, false, reserved: false), Is.False);
            Assert.That(GothwaitePvpMode.IsModeBot(true, true, false, false, false), Is.True);
        }

        [Test]
        public void OpenBgGuards_ModeOn_EmptySetSuppressesTheCrew_ReserveLetsReservedJoin()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpRules.SuppressesOpenBattlegroundCrew(true, 0), Is.True);
                Assert.That(GothwaitePvpRules.SuppressesOpenBattlegroundCrew(true, 5), Is.False);
                Assert.That(GothwaitePvpRules.MayJoinOpenBattleground(true, true), Is.True);
                Assert.That(GothwaitePvpRules.MayJoinOpenBattleground(true, false), Is.False);
                Assert.That(GothwaitePvpRules.RefusesOpenUnit(true, true), Is.False);
                Assert.That(GothwaitePvpRules.RefusesOpenUnit(true, false), Is.True, "a unit with a non-reserved member is refused");
            });
        }

        [Test]
        public void Install_And_Reset()
        {
            GothwaitePvpOpenBgReserve.Install(new HashSet<long> { 5 });
            Assert.That(GothwaitePvpOpenBgReserve.IsReserved(5), Is.True);
            GothwaitePvpMode.Reset();
            Assert.That(GothwaitePvpOpenBgReserve.Set, Is.Empty);
        }

        [Test]
        public void TheReserveKeepsSpeedClassesForTheGroups()
        {
            // 100 bots in realm 1, every tenth a speed class; seats 20 -> reserve 50.
            var bots = Enumerable.Range(1, 100).Select(i => ((long)i, 1, 45, false, i % 10 == 0 ? 2 : 0)).ToArray();
            var withSpeed = GothwaitePvpOpenBgReserve.Choose(true, true, 40, 50, _ => 20, 72, _ => 6, bots);
            int speed = withSpeed.Count(id => id % 10 == 0);
            Assert.Multiple(() =>
            {
                Assert.That(speed, Is.GreaterThanOrEqualTo(6), "six speed classes kept");
                Assert.That(withSpeed.Count, Is.EqualTo(GothwaitePvpOpenBgReserve.Choose(true, true, 40, 50, _ => 20, 72,
                    bots.Select(b => (b.Item1, b.Item2, b.Item3, b.Item4))).Count), "same reserve size");
                Assert.That(GothwaitePvpOpenBgReserve.SpeedWanted(1, 3), Is.EqualTo(6));
            });
        }

        [Test]
        public void TheReserveTakesPrimarySpeedBeforeSecondary()
        {
            // 100 bots: every tenth a primary speed class, every fifth (not tenth) a secondary one; six wanted.
            var bots = Enumerable.Range(1, 100).Select(i => ((long)i, 1, 45, false, i % 10 == 0 ? 2 : i % 5 == 0 ? 1 : 0)).ToArray();
            var three = Enumerable.Range(1, 100).Select(i => ((long)i, 1, 45, false, i <= 3 ? 2 : i % 5 == 0 ? 1 : 0)).ToArray();
            var set = GothwaitePvpOpenBgReserve.Choose(true, true, 40, 50, _ => 20, 72, _ => 6, bots);
            var fewPrimary = GothwaitePvpOpenBgReserve.Choose(true, true, 40, 50, _ => 20, 72, _ => 6, three);
            Assert.Multiple(() =>
            {
                Assert.That(set.Count(id => id % 10 == 0), Is.GreaterThanOrEqualTo(6), "primaries first");
                Assert.That(fewPrimary.Count(id => id <= 3), Is.EqualTo(3), "all three primaries");
                Assert.That(fewPrimary.Count(id => id % 5 == 0), Is.GreaterThanOrEqualTo(3), "secondaries fill the rest");
            });
        }

        private static GothwaitePvpOpenBgLiveReserve.Bot B(long id, int realm, int level = 50, int speed = 0, bool free = true, bool online = true,
            bool grouped = false) => new(id, realm, level, false, speed, online, free, grouped);

        [Test]
        public void TheLiveReserveGrowsWithFreeOnlineBandBots()
        {
            var bots = Enumerable.Range(1, 30).Select(i => B(i, 1, free: i != 3, online: i != 4, level: i == 5 ? 30 : 50)).ToList();
            var next = GothwaitePvpOpenBgLiveReserve.Rebalance(new HashSet<long>(), bots, 40, 50, _ => 10, _ => 0, out int added, out int removed);
            Assert.Multiple(() =>
            {
                Assert.That((added, removed, next.Count), Is.EqualTo((10, 0, 10)));
                Assert.That(next, Does.Not.Contain(3L), "in combat or in a battleground");
                Assert.That(next, Does.Not.Contain(4L), "offline");
                Assert.That(next, Does.Not.Contain(5L), "out of band");
            });
        }

        [Test]
        public void TheLiveReserveShrinksOnlyFreeBotsAndKeepsSpeed()
        {
            var bots = Enumerable.Range(1, 10).Select(i => B(i, 2, speed: i == 1 ? 2 : 0, free: i > 5)).ToList();
            var current = new HashSet<long>(Enumerable.Range(1, 10).Select(i => (long)i));
            var next = GothwaitePvpOpenBgLiveReserve.Rebalance(current, bots, 40, 50, _ => 4, _ => 0, out _, out int removed);
            Assert.Multiple(() =>
            {
                Assert.That(removed, Is.EqualTo(5), "only the five free bots may leave");
                Assert.That(next.Count(id => id <= 5), Is.EqualTo(5), "bots in the BG never leave");
            });
        }

        [Test]
        public void TheLiveReserveTakesSpeedFirstAndMovesAtMostFortyAPass()
        {
            var bots = Enumerable.Range(1, 200).Select(i => B(i, 3, speed: i % 50 == 0 ? 2 : 0)).ToList();
            var next = GothwaitePvpOpenBgLiveReserve.Rebalance(new HashSet<long>(), bots, 40, 50, _ => 100, _ => 4, out int added, out _);
            Assert.Multiple(() =>
            {
                Assert.That(added, Is.EqualTo(GothwaitePvpOpenBgLiveReserve.MaxMovesPerPass));
                Assert.That(next.Count(id => id % 50 == 0), Is.EqualTo(4), "all four speed classes first");
                Assert.That(GothwaitePvpOpenBgLiveReserve.Target(100, 300, 600), Is.EqualTo(160));
                Assert.That(GothwaitePvpOpenBgLiveReserve.Target(100, 300, 200), Is.EqualTo(120), "60 % of the band at most");
            });
        }
    }
}
