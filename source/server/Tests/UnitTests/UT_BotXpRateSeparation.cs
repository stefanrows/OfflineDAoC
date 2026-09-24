using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Events;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture, NonParallelizable]
    public sealed class UT_BotXpRateSeparation
    {
        private sealed class RewardBot : GameBot
        {
            private RewardBot() : base((OfflineWorldBotRecord)null) { }
            public override byte Level { get; set; }
            public override int GetModified(eProperty property) => 0;
        }

        private double _playerRate;
        private double _botRate;
        private double _rvrRate;

        [SetUp]
        public void SaveRates()
        {
            _playerRate = Properties.XP_RATE;
            _botRate = Properties.BOT_XP_RATE;
            _rvrRate = Properties.RvR_XP_RATE;
        }

        [TearDown]
        public void RestoreRates()
        {
            Properties.XP_RATE = _playerRate;
            Properties.BOT_XP_RATE = _botRate;
            Properties.RvR_XP_RATE = _rvrRate;
        }

        [Test]
        public void AutonomousRateDoesNotReadOrModifyPlayerRate()
        {
            Properties.XP_RATE = 10;
            Properties.BOT_XP_RATE = 3;
            Properties.RvR_XP_RATE = 2;

            GameBot bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            MethodInfo scale = typeof(GameBot).GetMethod("ScaleExperience",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(scale.Invoke(bot, new object[] { 100L, false }), Is.EqualTo(300L));
            Assert.That(scale.Invoke(bot, new object[] { 100L, true }), Is.EqualTo(600L));
            Assert.That(Properties.XP_RATE, Is.EqualTo(10));
        }

        [TestCase(1, 100L)]
        [TestCase(2, 200L)]
        [TestCase(3, 300L)]
        [TestCase(5, 500L)]
        [TestCase(10, 1000L)]
        public void LauncherPresetsScaleAutonomousExperience(double rate, long expected)
        {
            Properties.BOT_XP_RATE = rate;
            Properties.RvR_XP_RATE = 1;
            GameBot bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            MethodInfo scale = typeof(GameBot).GetMethod("ScaleExperience",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(scale.Invoke(bot, new object[] { 100L, false }), Is.EqualTo(expected));
        }

        [TestCase(1, 100L)]
        [TestCase(10, 1000L)]
        public void TemporaryHelperUsesPlayerRate(double rate, long expected)
        {
            Properties.XP_RATE = rate;
            Properties.BOT_XP_RATE = 3;
            GameBot bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper))!.SetValue(bot, true);
            MethodInfo scale = typeof(GameBot).GetMethod("ScaleExperience",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(scale.Invoke(bot, new object[] { 100L, false }), Is.EqualTo(expected));
        }

        [TestCase(1, eXPSource.NPC)]
        [TestCase(10, eXPSource.NPC)]
        [TestCase(1, eXPSource.Player)]
        [TestCase(10, eXPSource.Player)]
        public void AutonomousKillAwardUsesBotRate(double rate, eXPSource source)
        {
            Properties.BOT_XP_RATE = rate;
            Properties.XP_RATE = 3;
            RewardBot bot = (RewardBot)RuntimeHelpers.GetUninitializedObject(typeof(RewardBot));
            bot.Level = 20;
            typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot))!.SetValue(bot, true);
            typeof(GameBot).GetProperty(nameof(GameBot.Experience))!.SetValue(bot, 1L);

            bot.GainExperience(new GainedExperienceEventArgs(
                100, 0, 0, 0, 0, 0, false, true, source));

            Assert.That(bot.Experience, Is.EqualTo(1 + (long)(100 * rate)));
        }
    }
}
