using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Live 0.241.0: a level-50 hunter whose warband roster broke kept a Group
    /// reference to a group that no longer listed it. ChooseLowLevelPvpDestination
    /// averaged an empty party and threw "Sequence contains no elements" on every
    /// plan (eight times in thirteen minutes). The party falls back to the bot.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_LocalRivalHuntParty
    {
        private sealed class HuntBot : GameBot
        {
            private HuntBot() : base((OfflineWorldBotRecord)null) { }
            public bool Alive;
            public override bool IsAlive => Alive;
            public override byte Level { get; set; }
        }

        private static HuntBot Bot(byte level, bool alive = true)
        {
            var bot = (HuntBot)RuntimeHelpers.GetUninitializedObject(typeof(HuntBot));
            bot.Level = level;
            bot.Alive = alive;
            return bot;
        }

        private static List<GameLiving> Members(Group group) => (List<GameLiving>)typeof(Group)
            .GetField("_groupMembers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(group);

        [Test]
        public void BotPointingAtAGroupThatNoLongerListsItHuntsAsItself()
        {
            HuntBot bot = Bot(50);
            bot.Group = new Group(bot); // stale reference: the member list is empty

            GameBot[] party = AutonomousWorldBotController.LivingPartyOrSelf(bot);

            Assert.That(party, Is.EqualTo(new GameBot[] { bot }));
            Assert.That(party.Average(member => member.Level), Is.EqualTo(50));
        }

        [Test]
        public void BotOutsideAGroupOfOnlyDeadBotsHuntsAsItself()
        {
            HuntBot bot = Bot(50);
            HuntBot dead = Bot(40, alive: false);
            var group = new Group(dead);
            Members(group).Add(dead);
            dead.Group = group;
            bot.Group = group;

            Assert.That(AutonomousWorldBotController.LivingPartyOrSelf(bot), Is.EqualTo(new GameBot[] { bot }));
        }

        [Test]
        public void ValidGroupStillAveragesItsLivingMembers()
        {
            HuntBot bot = Bot(50);
            HuntBot mate = Bot(40);
            HuntBot dead = Bot(10, alive: false);
            var group = new Group(bot);
            Members(group).AddRange([bot, mate, dead]);
            bot.Group = mate.Group = dead.Group = group;

            GameBot[] party = AutonomousWorldBotController.LivingPartyOrSelf(bot);

            Assert.That(party, Is.EqualTo(new GameBot[] { bot, mate }));
            Assert.That(party.Average(member => member.Level), Is.EqualTo(45));
        }

        [Test]
        public void UngroupedBotIsItsOwnParty() =>
            Assert.That(AutonomousWorldBotController.LivingPartyOrSelf(Bot(20)), Has.Length.EqualTo(1));
    }
}
