using System;
using System.Linq;
using System.Reflection;
using DOL.GS;
using DOL.GS.DailyQuest;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Pazz's Thidranki dailies on the open BG (spec 2026-10-11 §8): levels 1-50, coin reward still level x 2 gold.
    [TestFixture]
    public class UT_ThidrankiQuestLevels
    {
        private static readonly string[] Quests =
        {
            "ThidKeepCaptureAlb", "ThidKeepCaptureHib", "ThidKeepCaptureMid", "ThidKillQuestAlb", "ThidKillQuestHib", "ThidKillQuestMid"
        };

        private static int Constant(Type type, string name) =>
            (int)type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();

        [Test]
        public void ThidQuestsAcceptLevelOneToFifty()
        {
            Type[] types = typeof(ThidrankiDailies).Assembly.GetTypes().Where(type => Quests.Contains(type.Name)).ToArray();
            Assert.That(types.Select(type => type.Name), Is.EquivalentTo(Quests));
            Assert.Multiple(() =>
            {
                foreach (Type type in types)
                {
                    Assert.That(Constant(type, "minimumLevel"), Is.EqualTo(1), type.Name);
                    Assert.That(Constant(type, "maximumLevel"), Is.EqualTo(50), type.Name);
                }
                Assert.That(ThidrankiDailies.AcceptsLevel(1), Is.True);
                Assert.That(ThidrankiDailies.AcceptsLevel(24), Is.True);
                Assert.That(ThidrankiDailies.AcceptsLevel(50), Is.True, "a level-50 player can accept");
                Assert.That(ThidrankiDailies.AcceptsLevel(0), Is.False);
                Assert.That(ThidrankiDailies.AcceptsLevel(51), Is.False);
            });
        }

        [Test]
        public void CoinRewardScalesWithLevel()
        {
            Assert.Multiple(() =>
            {
                Assert.That(ThidrankiDailies.CoinReward(1), Is.EqualTo(Money.GetMoney(0, 0, 2, 0, 0)));
                Assert.That(ThidrankiDailies.CoinReward(20), Is.EqualTo(Money.GetMoney(0, 0, 40, 0, 0)), "the old level-20 reward");
                Assert.That(ThidrankiDailies.CoinReward(50), Is.EqualTo(Money.GetMoney(0, 0, 100, 0, 0)));
                Assert.That(ThidrankiDailies.KillCoinReward(30), Is.EqualTo(ThidrankiDailies.CoinReward(30) + Money.GetMoney(0, 0, 0, 32, 0)));
                long[] rewards = Enumerable.Range(1, 50).Select(level => ThidrankiDailies.CoinReward(level)).ToArray();
                Assert.That(rewards, Is.Ordered.Ascending);
                Assert.That(rewards.Distinct().Count(), Is.EqualTo(50));
            });
        }
    }
}
