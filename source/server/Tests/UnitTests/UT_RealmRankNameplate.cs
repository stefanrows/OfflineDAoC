using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Realm rank nameplates (presentation only): "Name | RR 2L5" on autonomous bots (show_bot_realm_rank_nameplates,
    // on by default) and persistent companions (show_realm_rank_nameplates, off by default, which also gives players
    // "| RR 2L5" after an empty last name). Stored names never change, and the caps keep the strings
    // inside the lengths the client copies into fixed buffers (45 for NPC names, 31 for player last names).
    [TestFixture, NonParallelizable]
    public sealed class UT_RealmRankNameplate
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private bool _previous;
        private bool _previousBots;

        [SetUp]
        public void SetUp()
        {
            _previous = DOL.GS.ServerProperties.Properties.SHOW_REALM_RANK_NAMEPLATES;
            _previousBots = DOL.GS.ServerProperties.Properties.SHOW_BOT_REALM_RANK_NAMEPLATES;
        }

        [TearDown]
        public void TearDown()
        {
            DOL.GS.ServerProperties.Properties.SHOW_REALM_RANK_NAMEPLATES = _previous;
            DOL.GS.ServerProperties.Properties.SHOW_BOT_REALM_RANK_NAMEPLATES = _previousBots;
        }

        [TestCase(0, "RR 1L0")]
        [TestCase(9, "RR 1L9")]
        [TestCase(10, "RR 2L0")]
        [TestCase(15, "RR 2L5")]
        [TestCase(110, "RR 12L0")]
        [TestCase(-4, "RR 1L0")]
        public void FormatReadsTheRankAndTheLevelWithinIt(int realmLevel, string expected)
        {
            Assert.That(RealmRankNameplate.Format(realmLevel), Is.EqualTo(expected));
        }

        [Test]
        public void PropertyDefaultsOffAndSitsInThePvpCategory()
        {
            FieldInfo field = typeof(DOL.GS.ServerProperties.Properties).GetField("SHOW_REALM_RANK_NAMEPLATES",
                BindingFlags.Public | BindingFlags.Static);
            ServerPropertyAttribute attribute = field.GetCustomAttribute<ServerPropertyAttribute>();
            Assert.Multiple(() =>
            {
                Assert.That(attribute.Category, Is.EqualTo("pvp"));
                Assert.That(attribute.Key, Is.EqualTo("show_realm_rank_nameplates"));
                Assert.That(attribute.DefaultValue, Is.EqualTo(false));
            });
        }

        [Test]
        public void AutonomousBotPropertyDefaultsOnAndSitsInThePvpCategory()
        {
            FieldInfo field = typeof(DOL.GS.ServerProperties.Properties).GetField("SHOW_BOT_REALM_RANK_NAMEPLATES",
                BindingFlags.Public | BindingFlags.Static);
            ServerPropertyAttribute attribute = field.GetCustomAttribute<ServerPropertyAttribute>();
            Assert.Multiple(() =>
            {
                Assert.That(attribute.Category, Is.EqualTo("pvp"));
                Assert.That(attribute.Key, Is.EqualTo("show_bot_realm_rank_nameplates"));
                Assert.That(attribute.DefaultValue, Is.EqualTo(true));
            });
        }

        [Test]
        public void KindRuleGivesTheRankOnlyToAutonomousBotsAndPersistentCompanions()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RealmRankNameplate.IsNameplateBotKind(true, false, false, false), Is.True, "autonomous world bot");
                Assert.That(RealmRankNameplate.IsNameplateBotKind(false, true, false, false), Is.True, "persistent companion");
                Assert.That(RealmRankNameplate.IsNameplateBotKind(false, false, true, false), Is.False, "temporary /spawn helper");
                Assert.That(RealmRankNameplate.IsNameplateBotKind(false, false, false, true), Is.False, "battleground encounter actor");
                Assert.That(RealmRankNameplate.IsNameplateBotKind(false, false, false, false), Is.False, "ordinary NPC");
                Assert.That(RealmRankNameplate.IsNameplateBotKind(true, true, true, false), Is.False, "a helper never gets the rank");
            });
        }

        [Test]
        public void ComposeNpcNameAddsTheRankWhenItFitsTheFortyFiveCharacterCap()
        {
            string fills = new string('A', 36); // 36 + " | RR 2L5" (9 characters) is exactly 45
            Assert.Multiple(() =>
            {
                Assert.That(RealmRankNameplate.ComposeNpcName("Dairoinlan", 15), Is.EqualTo("Dairoinlan | RR 2L5"));
                Assert.That(RealmRankNameplate.ComposeNpcName(fills, 15), Is.EqualTo(fills + " | RR 2L5"));
                Assert.That(RealmRankNameplate.ComposeNpcName(fills + "A", 15), Is.EqualTo(fills + "A"),
                    "46 characters would overflow the cap, so the plain name is kept and never cut");
                Assert.That(RealmRankNameplate.ComposeNpcName("", 15), Is.EqualTo(""));
            });
        }

        [Test]
        public void ComposePlayerLastNameUsesThePipeWhenThereIsNoLastName()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RealmRankNameplate.ComposePlayerLastName("", 15), Is.EqualTo("| RR 2L5"));
                Assert.That(RealmRankNameplate.ComposePlayerLastName(null, 15), Is.EqualTo("| RR 2L5"));
                Assert.That(RealmRankNameplate.ComposePlayerLastName("Stormbringer", 15), Is.EqualTo("Stormbringer | RR 2L5"));
            });
        }

        [Test]
        public void ComposePlayerLastNameKeepsThePlainValueWhenItWouldExceedThirtyOne()
        {
            string fits = new string('L', 22); // 22 + 9 = 31
            string overflows = new string('L', 23); // 23 + 9 = 32
            Assert.Multiple(() =>
            {
                Assert.That(RealmRankNameplate.ComposePlayerLastName(fits, 15), Is.EqualTo(fits + " | RR 2L5"));
                Assert.That(RealmRankNameplate.ComposePlayerLastName(overflows, 15), Is.EqualTo(overflows));
            });
        }

        [Test]
        public void AutonomousBotsFollowOnlyTheBotSetting()
        {
            GameBot bot = UninitializedBot(autonomous: true, companion: false, helper: false, encounter: false, realmLevel: 15);

            DOL.GS.ServerProperties.Properties.SHOW_BOT_REALM_RANK_NAMEPLATES = true;
            DOL.GS.ServerProperties.Properties.SHOW_REALM_RANK_NAMEPLATES = false;
            string on = RealmRankNameplate.NpcName(bot, "Dairoinlan");
            DOL.GS.ServerProperties.Properties.SHOW_BOT_REALM_RANK_NAMEPLATES = false;
            DOL.GS.ServerProperties.Properties.SHOW_REALM_RANK_NAMEPLATES = true;
            string off = RealmRankNameplate.NpcName(bot, "Dairoinlan");

            Assert.Multiple(() =>
            {
                Assert.That(on, Is.EqualTo("Dairoinlan | RR 2L5"));
                Assert.That(off, Is.EqualTo("Dairoinlan"), "bot setting off keeps the packet text exactly as before");
                Assert.That(bot.RealmLevel, Is.EqualTo(15), "the stored realm level is never changed by the label");
            });
        }

        [Test]
        public void PersistentCompanionsFollowOnlyTheCompanionAndPlayerSetting()
        {
            GameBot companion = UninitializedBot(autonomous: false, companion: true, helper: false, encounter: false, realmLevel: 3);

            DOL.GS.ServerProperties.Properties.SHOW_REALM_RANK_NAMEPLATES = true;
            DOL.GS.ServerProperties.Properties.SHOW_BOT_REALM_RANK_NAMEPLATES = false;
            string on = RealmRankNameplate.NpcName(companion, "Ysolde");
            DOL.GS.ServerProperties.Properties.SHOW_REALM_RANK_NAMEPLATES = false;
            DOL.GS.ServerProperties.Properties.SHOW_BOT_REALM_RANK_NAMEPLATES = true;
            string off = RealmRankNameplate.NpcName(companion, "Ysolde");

            Assert.Multiple(() =>
            {
                Assert.That(on, Is.EqualTo("Ysolde | RR 1L3"));
                Assert.That(off, Is.EqualTo("Ysolde"));
            });
        }

        [Test]
        public void TemporaryHelpersBattlegroundActorsAndOrdinaryNpcsKeepThePlainName()
        {
            DOL.GS.ServerProperties.Properties.SHOW_REALM_RANK_NAMEPLATES = true;
            DOL.GS.ServerProperties.Properties.SHOW_BOT_REALM_RANK_NAMEPLATES = true;
            GameBot helper = UninitializedBot(autonomous: false, companion: false, helper: true, encounter: false, realmLevel: 15);
            GameBot encounter = UninitializedBot(autonomous: false, companion: false, helper: false, encounter: true, realmLevel: 15);
            var ordinary = new GameNPC();

            Assert.Multiple(() =>
            {
                Assert.That(RealmRankNameplate.NpcName(helper, "Helper"), Is.EqualTo("Helper"));
                Assert.That(RealmRankNameplate.NpcName(encounter, "Opponent"), Is.EqualTo("Opponent"));
                Assert.That(RealmRankNameplate.NpcName(ordinary, "Gate Guard"), Is.EqualTo("Gate Guard"));
                Assert.That(RealmRankNameplate.NpcName(null, "Nobody"), Is.EqualTo("Nobody"));
            });
        }

        [Test]
        public void EligibleBotNamesThatWouldOverflowKeepThePlainName()
        {
            DOL.GS.ServerProperties.Properties.SHOW_BOT_REALM_RANK_NAMEPLATES = true;
            GameBot bot = UninitializedBot(autonomous: true, companion: false, helper: false, encounter: false, realmLevel: 110);
            string longName = new string('B', 37);
            Assert.That(RealmRankNameplate.NpcName(bot, longName), Is.EqualTo(longName));
        }

        [Test]
        public void PlayerLastNameReadsTheRealmLevelOfThePlayerAndNeverChangesStoredValues()
        {
            GamePlayer player = UninitializedPlayer(realmLevel: 15);

            DOL.GS.ServerProperties.Properties.SHOW_REALM_RANK_NAMEPLATES = true;
            string withName = RealmRankNameplate.PlayerLastName(player, "Stormbringer");
            string withoutName = RealmRankNameplate.PlayerLastName(player, string.Empty);
            DOL.GS.ServerProperties.Properties.SHOW_REALM_RANK_NAMEPLATES = false;
            string off = RealmRankNameplate.PlayerLastName(player, "Stormbringer");

            Assert.Multiple(() =>
            {
                Assert.That(withName, Is.EqualTo("Stormbringer | RR 2L5"));
                Assert.That(withoutName, Is.EqualTo("| RR 2L5"));
                Assert.That(off, Is.EqualTo("Stormbringer"));
                Assert.That(player.RealmLevel, Is.EqualTo(15));
                Assert.That(RealmRankNameplate.PlayerLastName(null, "Stormbringer"), Is.EqualTo("Stormbringer"));
            });
        }

        // Builds a GameBot without its constructors (no database, no owner), with only the bot-kind flags and the
        // realm level set. The helper reads nothing else.
        private static GameBot UninitializedBot(bool autonomous, bool companion, bool helper, bool encounter, int realmLevel)
        {
            var bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            SetBackingField(bot, nameof(GameBot.IsAutonomousWorldBot), autonomous);
            SetBackingField(bot, nameof(GameBot.IsTemporaryGroupHelper), helper);
            SetBackingField(bot, nameof(GameBot.IsBattlegroundEncounterBot), encounter);
            SetBackingField(bot, nameof(GameBot.RealmLevel), realmLevel);
            if (companion)
                SetBackingField(bot, nameof(GameBot.PlayerCompanionRecord), new PlayerCompanionRecord());
            return bot;
        }

        private static GamePlayer UninitializedPlayer(int realmLevel)
        {
            var player = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
            var character = new DbCoreCharacter { RealmLevel = realmLevel };
            typeof(GamePlayer).GetField("m_dbCharacter", Hidden).SetValue(player, character);
            return player;
        }

        private static void SetBackingField(object target, string property, object value)
        {
            FieldInfo field = target.GetType().GetField($"<{property}>k__BackingField", Hidden);
            Assert.That(field, Is.Not.Null, property);
            field.SetValue(target, value);
        }
    }
}
