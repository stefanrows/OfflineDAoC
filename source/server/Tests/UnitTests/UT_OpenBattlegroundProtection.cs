using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS;
using DOL.GS.PacketHandler;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // open BG low-level protection (spec 2026-10-11 §6) applied to the class brain's PvP crowd control: the mez sweep and
    // the healer area stun share the driver's target rule, so a crew bot spares a human below level 30 who did not attack.
    [TestFixture, NonParallelizable]
    public class UT_OpenBattlegroundProtection
    {
        private EpicTestServerScope _server;

        [SetUp]
        public void SetUp() => _server = new EpicTestServerScope();

        [TearDown]
        public void TearDown() => _server.Dispose();

        private sealed class TestPlayer : GamePlayer
        {
            public TestPlayer() : base(null, null) { }
            public int TestX;
            public override bool IsAlive => true;
            public override byte Level { get; set; } = 50;
            public override eRealm Realm { get; set; }
            public override GameClient Client { get; } = new BotDummyClient();
            public override ushort CurrentRegionID { get; set; } = 1;
            public override int X => TestX;
            public override int Y => 0;
            public override int Z => 0;
        }

        private sealed class TestBot : GameBot
        {
            public TestBot() : base((OfflineWorldBotRecord)null) { }
            public override bool IsAlive => true;
            public override byte Level { get; set; } = 50;
            public override eRealm Realm { get; set; }
        }

        [Test]
        public void ProtectedHumanIsSparedOnlyInsideTheOpenBattleground()
        {
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundRoles.MayEngageInOpenBattleground(true, true, 45, 22, attacked: false), Is.False,
                    "a level-22 bystander is neither mezzed nor stunned by the 40-50 crew");
                Assert.That(OpenBattlegroundRoles.MayEngageInOpenBattleground(true, true, 45, 22, attacked: true), Is.True,
                    "a lowbie who attacked the crew is fair game");
                Assert.That(OpenBattlegroundRoles.MayEngageInOpenBattleground(false, true, 45, 22, attacked: false), Is.True,
                    "brackets and the world keep their sweep");
                Assert.That(OpenBattlegroundRoles.MayEngageInOpenBattleground(true, false, 45, 22, attacked: false), Is.True,
                    "another autonomous bot is never protected");
                Assert.That(OpenBattlegroundRoles.MayEngageInOpenBattleground(true, true, 45, 30, attacked: false), Is.True);
                Assert.That(OpenBattlegroundRoles.MayEngageInOpenBattleground(true, true, 35, 22, attacked: false), Is.True,
                    "only the 40-50 crew holds back");
            });
        }

        [Test]
        public void CrewBotSparesAProtectedHumanAndItsCompanion()
        {
            TestBot crew = Crew(45);
            var lowbie = new TestPlayer { Realm = eRealm.Midgard, Level = 22 };
            TestBot companion = Bot(eRealm.Midgard, 22);
            SetGameBotProperty(companion, "Owner", lowbie);
            var veteran = new TestPlayer { Realm = eRealm.Midgard, Level = 35 };

            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundDriver.MayEngage(crew, lowbie, openBattlegroundRule: true), Is.False);
                Assert.That(AutonomousBattlegroundDriver.MayEngage(crew, companion, openBattlegroundRule: true), Is.False);
                Assert.That(AutonomousBattlegroundDriver.MayEngage(crew, veteran, openBattlegroundRule: true), Is.True);
                Assert.That(AutonomousBattlegroundDriver.MayEngage(crew, lowbie, openBattlegroundRule: false), Is.True,
                    "outside the open BG the sweep is unchanged");
            });
        }

        [Test]
        public void AttackByTheHumansGroupOrCompanionCountsAsTheHumansAttack()
        {
            var healer = new TestPlayer { Realm = eRealm.Midgard, Level = 22 };
            var groupMate = new TestPlayer { Realm = eRealm.Midgard, Level = 24 };
            TestBot companion = Bot(eRealm.Midgard, 22);
            SetGameBotProperty(companion, "Owner", healer);
            PutInGroup(healer, groupMate, companion);
            var stranger = new TestPlayer { Realm = eRealm.Midgard, Level = 50 };

            HashSet<GameLiving> side = AutonomousBattlegroundDriver.HumanSide(healer);
            Assert.Multiple(() =>
            {
                Assert.That(side, Is.EquivalentTo(new GameLiving[] { healer, groupMate, companion }));
                Assert.That(AutonomousBattlegroundDriver.AttackedBySide(new GameLiving[] { companion }, side), Is.True,
                    "a lowbie healer whose companion attacks the crew is fair game");
                Assert.That(AutonomousBattlegroundDriver.AttackedBySide(new GameLiving[] { groupMate }, side), Is.True);
                Assert.That(AutonomousBattlegroundDriver.AttackedBySide(new GameLiving[] { stranger }, side), Is.False);
                Assert.That(AutonomousBattlegroundDriver.HumanSide(companion), Does.Contain(healer), "a companion's owner is its side");
            });
        }

        [Test]
        public void AreaControlIsRefusedWhenASparedHumanStandsInItsRadius()
        {
            var center = new TestPlayer { Realm = eRealm.Midgard, TestX = 0 };
            var spared = new TestPlayer { Realm = eRealm.Midgard, Level = 22, TestX = 300 };
            GameLiving[] list = { spared };
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundDriver.CatchesSpared(list, center, 350), Is.True, "the area mez or stun would land on him");
                Assert.That(AutonomousBattlegroundDriver.CatchesSpared(list, center, 250), Is.False, "outside the radius");
                Assert.That(AutonomousBattlegroundDriver.CatchesSpared(list, center, 0), Is.False, "a single-target spell");
                Assert.That(AutonomousBattlegroundDriver.CatchesSpared(System.Array.Empty<GameLiving>(), center, 350), Is.False,
                    "nothing spared outside the open BG");
            });
        }

        private static TestBot Crew(byte level)
        {
            TestBot bot = Bot(eRealm.Albion, level);
            SetGameBotProperty(bot, "IsAutonomousWorldBot", true);
            return bot;
        }

        private static TestBot Bot(eRealm realm, byte level)
        {
            var bot = (TestBot)RuntimeHelpers.GetUninitializedObject(typeof(TestBot));
            typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, new ArrayList());
            var brain = new BotBrain { Body = bot };
            typeof(GameNPC).GetField("m_ownBrain", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, brain);
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(bot, new PropertyCollection());
            bot.Realm = realm;
            bot.Level = level;
            return bot;
        }

        private static void PutInGroup(params GameLiving[] members)
        {
            var group = new Group(members[0]);
            var groupMembers = (List<GameLiving>)typeof(Group)
                .GetField("_groupMembers", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(group);
            groupMembers.AddRange(members);
            foreach (GameLiving member in members)
                member.Group = group;
        }

        private static void SetGameBotProperty(GameBot bot, string property, object value) =>
            typeof(GameBot).GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(bot, value);
    }
}
