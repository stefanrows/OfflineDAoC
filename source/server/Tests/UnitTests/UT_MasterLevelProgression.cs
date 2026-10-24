using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.Database;
using DOL.Events;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.PlayerTitles;
using DOL.GS.Scripts;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Master Level progression (TOA_PLAN WP2.2): Arbiter group enrolment, kill experience for
    /// every earner on level 45+ NPCs, the 32,000 threshold, discipline exclusivity in the spec
    /// list, Demyphon's tokens and the class names the gaheris mob rows use. The class career and
    /// the Master Level spells are synthetic (class id 250); the dev database has no ML career rows.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_MasterLevelProgression
    {
        private const int ClassId = 250;
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();

        // Sojourner is left out of the synthetic career on purpose (NotInCareer).
        private static readonly string[] CareerPaths =
            { "Banelord", "Battlemaster", "Convoker", "Perfecter", "Spymaster", "Stormlord", "Warlord" };

        private GameServer _previousServer;
        private int _previousXpPerLevel;
        private readonly List<string> _addedSpecNames = new();
        private readonly List<string> _addedSpellLines = new();
        private bool _addedCareer;

        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
        }

        public class Recorder : DispatchProxy
        {
            public List<string> Messages = new();
            public int Windows;

            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == nameof(IPacketLib.SendMessage) && args.Length > 0 && args[0] is string text)
                    Messages.Add(text);
                else if (method.Name == nameof(IPacketLib.SendMasterLevelWindow))
                    Windows++;

                return method.ReturnType != typeof(void) && method.ReturnType.IsValueType
                    ? Activator.CreateInstance(method.ReturnType)
                    : null;
            }
        }

        public class ClassOf250 : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == "get_ID")
                    return ClassId;
                if (method.Name == "get_Name")
                    return "Tester";

                return method.ReturnType != typeof(void) && method.ReturnType.IsValueType
                    ? Activator.CreateInstance(method.ReturnType)
                    : null;
            }
        }

        private sealed class Player : GamePlayer
        {
            private Player() : base(null, null) { }
            public byte TestLevel;
            public bool Alive;
            public int TestX;
            public ushort Region;
            public bool Granted;
            public byte Line;
            public int Ml;
            public long MlXp;
            public int Saves;
            public int Refreshes;
            public IPacketLib Packets;
            public ICharacterClass Class;
            public string TestName;
            public override string Name { get => TestName; set => TestName = value; }
            public override byte Level { get => TestLevel; set { } }
            public override bool IsAlive => Alive;
            public override int X => TestX;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => Region; set { } }
            public override bool MLGranted { get => Granted; set => Granted = value; }
            public override byte MLLine { get => Line; set => Line = value; }
            public override int MLLevel { get => Ml; set => Ml = value; }
            public override long MLExperience { get => MlXp; set => MlXp = value; }
            public override IPacketLib Out => Packets;
            public override ICharacterClass CharacterClass => Class;
            public override void SaveIntoDatabase() => Saves++;
            public override void RefreshSpecDependantSkills(bool sendMessages) => Refreshes++;

            public override bool RemoveSpecialization(string specKeyName)
            {
                lock (_specializationLock)
                    return m_specialization.Remove(specKeyName);
            }

            protected override bool RemoveSpellLine(SpellLine line)
            {
                lock (_spellLinesListLock)
                    return m_spellLines.Remove(line);
            }

            public void GiveSpellLine(SpellLine line)
            {
                lock (_spellLinesListLock)
                    m_spellLines.Add(line);
            }

            public override void EnemyKilled(GameLiving enemy) { }

            private System.Collections.Concurrent.ConcurrentDictionary<DOL.GS.Quests.AbstractQuest, byte> _quests;
            public override System.Collections.Concurrent.ConcurrentDictionary<DOL.GS.Quests.AbstractQuest, byte> QuestList => _quests ??= new();

            public List<string> Messages => ((Recorder)(object)Packets).Messages;
        }

        private sealed class Mob : GameNPC
        {
            public byte TestLevel;
            public int TestX;
            public override byte Level { get => TestLevel; set { } }
            public override int X => TestX;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
        }

        /// <summary>A normally constructed NPC that keeps its corpse, so ProcessDeath runs end to end.</summary>
        private sealed class LiveMob : GameNPC
        {
            public override byte Level { get => 55; set { } }
            protected override bool RetainCorpseForResurrection => true;
        }

        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public override byte Level { get => 55; set { } }
        }

        [SetUp]
        public void Setup()
        {
            _previousServer = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            _previousXpPerLevel = MasterLevelProgression.ML_XP_PER_LEVEL;
            MasterLevelProgression.ML_XP_PER_LEVEL = 10;

            var byName = (Dictionary<string, Tuple<Type, string, ushort, int>>)typeof(SkillBase).GetField("m_specsByName", HiddenStatic).GetValue(null);
            var byClass = (Dictionary<int, Dictionary<string, int>>)typeof(SkillBase).GetField("m_specsByClass", HiddenStatic).GetValue(null);
            Assert.That(byClass.ContainsKey(ClassId), Is.False, "synthetic class id is free");

            var career = new Dictionary<string, int> { ["TestSlash"] = 1 };
            AddSpec(byName, "TestSlash", typeof(Specialization), 9001);

            for (int i = 0; i < MasterLevelProgression.Paths.Length; i++)
            {
                string key = MasterLevelProgression.Paths[i].Key;
                AddSpec(byName, key, typeof(LiveMasterLevelsSpecialization), 9100 + i);

                if (CareerPaths.Contains(key))
                    career[key] = 50;
            }

            byClass[ClassId] = career;
            _addedCareer = true;
        }

        [TearDown]
        public void Cleanup()
        {
            var byName = (Dictionary<string, Tuple<Type, string, ushort, int>>)typeof(SkillBase).GetField("m_specsByName", HiddenStatic).GetValue(null);
            var byClass = (Dictionary<int, Dictionary<string, int>>)typeof(SkillBase).GetField("m_specsByClass", HiddenStatic).GetValue(null);
            var lines = (Dictionary<string, List<Spell>>)typeof(SkillBase).GetField("m_lineSpells", HiddenStatic).GetValue(null);

            foreach (string key in _addedSpecNames)
                byName.Remove(key);
            foreach (string line in _addedSpellLines)
                lines.Remove(line);
            if (_addedCareer)
                byClass.Remove(ClassId);

            _addedSpecNames.Clear();
            _addedSpellLines.Clear();
            _addedCareer = false;
            MasterLevelProgression.ML_XP_PER_LEVEL = _previousXpPerLevel;
            GameServer.LoadTestDouble(_previousServer);
        }

        private void AddSpec(Dictionary<string, Tuple<Type, string, ushort, int>> byName, string key, Type type, int id)
        {
            if (byName.TryAdd(key, new Tuple<Type, string, ushort, int>(type, key, 0, id)))
                _addedSpecNames.Add(key);
        }

        private static void Field(Type type, object owner, string name, object value) =>
            type.GetField(name, Hidden).SetValue(owner, value);

        private static Player NewPlayer(string name, byte level = 50, int x = 0, bool enrolled = false)
        {
            var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            player.ObjectState = GameObject.eObjectState.Active;
            player.Name = name;
            player.TestLevel = level;
            player.Alive = true;
            player.TestX = x;
            player.Region = 1;
            player.Packets = DispatchProxy.Create<IPacketLib, Recorder>();
            player.Class = DispatchProxy.Create<ICharacterClass, ClassOf250>();
            Field(typeof(GamePlayer), player, "m_specialization", new Dictionary<string, Specialization>());
            Field(typeof(GamePlayer), player, "_specializationLock", new Lock());
            Field(typeof(GamePlayer), player, "m_spellLines", new List<SpellLine>());
            Field(typeof(GamePlayer), player, "_spellLinesListLock", new Lock());

            if (enrolled)
            {
                player.Granted = true;
                player.Ml = 1;
                player.Line = MasterLevelProgression.NO_PATH;
            }

            return player;
        }

        private static Mob NewMob(byte level, int x = 0)
        {
            var mob = (Mob)RuntimeHelpers.GetUninitializedObject(typeof(Mob));
            mob.ObjectState = GameObject.eObjectState.Active;
            mob.Name = "test mob";
            mob.TestLevel = level;
            mob.TestX = x;
            Field(typeof(GameNPC), mob, "m_brains", new System.Collections.ArrayList());
            Field(typeof(GameLiving), mob, "<TempProperties>k__BackingField", new PropertyCollection());
            return mob;
        }

        private static Group GroupOf(Player leader, params Player[] mates)
        {
            var group = new Group(leader);
            var members = (List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden).GetValue(group);

            foreach (Player member in new[] { leader }.Concat(mates))
            {
                members.Add(member);
                member.Group = group;
            }

            return group;
        }

        private static IEnumerable<string> MasterLevelSpecs(GamePlayer player) =>
            player.GetSpecList().Where(MasterLevelProgression.IsMasterLevelSpec).Select(spec => spec.KeyName);

        [Test]
        public void ArbiterEnrolsTheSpeakerAndEveryGroupMemberWithin2000Units()
        {
            Mob arbiter = NewMob(50);
            Player speaker = NewPlayer("Speaker");
            Player near = NewPlayer("Near", x: 1500);
            Player far = NewPlayer("Far", x: 3000);
            Player young = NewPlayer("Young", level: 30, x: 100);
            Player veteran = NewPlayer("Veteran", x: 100, enrolled: true);
            veteran.Line = 2;
            veteran.Ml = 4;
            GroupOf(speaker, near, far, young, veteran);

            List<GamePlayer> enrolled = MasterLevelProgression.EnrolCompany(speaker, arbiter);

            Assert.That(enrolled, Is.EquivalentTo(new GamePlayer[] { speaker, near }));

            foreach (Player player in new[] { speaker, near })
            {
                Assert.That(player.MLGranted, Is.True);
                Assert.That(player.MLLevel, Is.EqualTo(1), "enrolment starts at Master Level 1");
                Assert.That(player.MLLine, Is.EqualTo(MasterLevelProgression.NO_PATH), "no discipline yet");
                Assert.That(player.Saves, Is.EqualTo(1));
                Assert.That(MasterLevelProgression.PathOf(player), Is.Null);
            }

            Assert.That(far.MLGranted, Is.False, "3000 units away is not with you");
            Assert.That(young.MLGranted, Is.False, "the trials begin at level 40");
            Assert.That(veteran.MLLevel, Is.EqualTo(4), "an enrolled member keeps his standing");
            Assert.That(veteran.MLLine, Is.EqualTo(2));
        }

        [Test]
        public void ArbiterConversationEnrolsAndListsOnlyTheCareerDisciplines()
        {
            var arbiter = (MasterLevelArbiter)RuntimeHelpers.GetUninitializedObject(typeof(MasterLevelArbiter));
            Player player = NewPlayer("Solo");

            string text = arbiter.Conversation(player);

            Assert.That(player.MLGranted, Is.True);
            Assert.That(text, Does.Contain("You have begun the trials."));
            Assert.That(text, Does.Contain("[Convoker]"));
            Assert.That(text, Does.Not.Contain("[Sojourner]"), "only disciplines in the class career are offered");

            MasterLevelProgression.Choose(player, "Convoker");
            Assert.That(MasterLevelArbiter.PathList(player), Is.Empty, "no list once a discipline is walked");
        }

        [Test]
        public void Level45KillPaysEveryEnrolledEarnerAndNobodyElse()
        {
            Mob dead = NewMob(55);
            Player killer = NewPlayer("Killer", enrolled: true);
            Player helper = NewPlayer("Helper", enrolled: true);
            Player outsider = NewPlayer("Outsider");
            Player corpse = NewPlayer("Corpse", enrolled: true);
            corpse.Alive = false;
            Player distant = NewPlayer("Distant", x: WorldMgr.MAX_EXPFORKILL_DISTANCE + 500, enrolled: true);
            Player maxed = NewPlayer("Maxed", enrolled: true);
            maxed.Ml = GamePlayer.ML_MAX_LEVEL;

            var death = new DyingEventArgs(killer, new List<GamePlayer> { killer, helper, outsider, corpse, distant, maxed });
            MasterLevelProgression.OnDying(GameLivingEvent.Dying, dead, death);

            Assert.That(killer.MLExperience, Is.EqualTo(550), "level 55 x ml_xp_per_level 10");
            Assert.That(helper.MLExperience, Is.EqualTo(550), "not only the killing blow");
            Assert.That(killer.Messages, Has.Some.Contains("You gain 550 Master Level experience."));
            Assert.That(outsider.MLExperience, Is.Zero, "not on the path");
            Assert.That(corpse.MLExperience, Is.Zero, "dead earners get nothing");
            Assert.That(distant.MLExperience, Is.Zero, "out of experience range");
            Assert.That(maxed.MLExperience, Is.Zero, "Master Level 10 earns nothing more");
        }

        [Test]
        public void KillsBelowLevel45BotsAndSwitchedOffPropertyPayNothing()
        {
            Player player = NewPlayer("Player", enrolled: true);

            MasterLevelProgression.OnDying(GameLivingEvent.Dying, NewMob(44), new DyingEventArgs(player, new List<GamePlayer> { player }));
            Assert.That(player.MLExperience, Is.Zero, "level 44 is below the line");

            Assert.That(MasterLevelProgression.KillValue(NewMob(45)), Is.EqualTo(450), "level 45 counts");

            var bot = (Bot)RuntimeHelpers.GetUninitializedObject(typeof(Bot));
            Field(typeof(GameNPC), bot, "m_brains", new System.Collections.ArrayList());
            Assert.That(MasterLevelProgression.KillValue(bot), Is.Zero, "GameBot victims pay no Master Level experience");

            MasterLevelProgression.ML_XP_PER_LEVEL = 0;
            MasterLevelProgression.OnDying(GameLivingEvent.Dying, NewMob(60), new DyingEventArgs(player, new List<GamePlayer> { player }));
            Assert.That(player.MLExperience, Is.Zero, "ml_xp_per_level 0 turns kill experience off");
        }

        [Test]
        public void TheKillerAloneIsPaidWhenTheEventCarriesNoList()
        {
            Player player = NewPlayer("Player", enrolled: true);
            MasterLevelProgression.OnDying(GameLivingEvent.Dying, NewMob(50), new DyingEventArgs(player));
            Assert.That(player.MLExperience, Is.EqualTo(500));
        }

        [Test]
        public void A32000BarGrantsTheNextMasterLevelInTheField()
        {
            Player player = NewPlayer("Player", enrolled: true);
            player.MlXp = 31_000;

            Assert.That(MasterLevelProgression.AddExperience(player, 500), Is.Zero);
            Assert.That(player.MLLevel, Is.EqualTo(1));
            Assert.That(player.MLExperience, Is.EqualTo(31_500));

            Assert.That(MasterLevelProgression.AddExperience(player, 700), Is.EqualTo(1));
            Assert.That(player.MLLevel, Is.EqualTo(2));
            Assert.That(player.MLExperience, Is.EqualTo(200), "the overshoot carries over");
            Assert.That(player.Messages, Has.Some.Contains("You have reached Master Level 2!"));
            Assert.That(player.Refreshes, Is.EqualTo(1), "the Master Level line is refreshed");
            Assert.That(player.Saves, Is.EqualTo(1));

            Assert.That(MasterLevelProgression.AddExperience(player, 70_000), Is.EqualTo(2), "GM /player mlxp may pay several levels");
            Assert.That(player.MLLevel, Is.EqualTo(4));
            Assert.That(player.MLExperience, Is.EqualTo(6_200));

            Assert.That(MasterLevelProgression.AddExperience(player, -100_000), Is.Zero);
            Assert.That(player.MLLevel, Is.EqualTo(4), "negative experience never takes a level");
            Assert.That(player.MLExperience, Is.Zero);

            player.Ml = 9;
            Assert.That(MasterLevelProgression.AddExperience(player, 40_000), Is.EqualTo(1));
            Assert.That(player.MLLevel, Is.EqualTo(GamePlayer.ML_MAX_LEVEL));
            Assert.That(player.MLExperience, Is.Zero, "Master Level 10 holds no experience");
        }

        [Test]
        public void OnlyTheChosenDisciplineEntersTheSpecListAndIsExclusive()
        {
            Player player = NewPlayer("Player");

            player.LoadClassSpecializations(false);
            Assert.That(player.HasSpecialization("TestSlash"), Is.True, "ordinary career specs still load");
            Assert.That(MasterLevelSpecs(player), Is.Empty, "no Master Level line before the Arbiter");
            Assert.That(MasterLevelProgression.Choose(player, "Convoker"), Is.EqualTo(MasterLevelChoice.NotEnrolled));

            MasterLevelProgression.Enrol(player);
            player.LoadClassSpecializations(false);
            Assert.That(MasterLevelSpecs(player), Is.Empty, "enrolled without a discipline holds no line");

            Assert.That(MasterLevelProgression.Choose(player, "Sojourner"), Is.EqualTo(MasterLevelChoice.NotInCareer));
            Assert.That(MasterLevelProgression.Choose(player, "convoker"), Is.EqualTo(MasterLevelChoice.Chosen));
            Assert.That(player.MLLine, Is.EqualTo(MasterLevelProgression.PathIndex("Convoker")));
            Assert.That(MasterLevelProgression.Choose(player, "Convoker"), Is.EqualTo(MasterLevelChoice.AlreadyWalked));
            Assert.That(MasterLevelProgression.Choose(player, "Warlord"), Is.EqualTo(MasterLevelChoice.OtherPathTaken));
            Assert.That(MasterLevelProgression.PathOf(player), Is.EqualTo("Convoker"));

            player.LoadClassSpecializations(false);
            Assert.That(MasterLevelSpecs(player), Is.EqualTo(new[] { "Convoker" }));
            // RefreshSpecDependantSkills adds the line of the held spec; stand in for it here.
            player.GiveSpellLine(new SpellLine("Convoker", "Convoker", "Convoker", false));
            player.GiveSpellLine(new SpellLine("TestSlashLine", "Test Slash", "TestSlash", true));
            Assert.That(player.GetSpecList().Where(spec => spec.Trainable).Select(spec => spec.KeyName), Has.No.Member("Convoker"),
                "a Master Level line is never trainable with spec points");

            Assert.That(MasterLevelProgression.Respec(player), Is.True);
            player.LoadClassSpecializations(false);
            Assert.That(MasterLevelSpecs(player), Is.Empty, "the Star of Destiny takes the line away");
            Assert.That(player.GetSpellLine("Convoker"), Is.Null, "its spell line goes too, not only the spec");
            Assert.That(player.GetSpellLine("TestSlashLine"), Is.Not.Null, "ordinary lines stay");
            Assert.That(player.MLLevel, Is.EqualTo(1), "levels stay");

            Assert.That(MasterLevelProgression.Choose(player, "Warlord"), Is.EqualTo(MasterLevelChoice.Chosen));
            player.LoadClassSpecializations(false);
            Assert.That(MasterLevelSpecs(player), Is.EqualTo(new[] { "Warlord" }));

            player.TestLevel = 49;
            player.LoadClassSpecializations(false);
            Assert.That(MasterLevelSpecs(player), Is.Empty, "the career rows open the lines at level 50");
        }

        [Test]
        public void ASpecNamedLikeAMasterLevelLineIsGuardedWhateverItsImplementation()
        {
            Assert.That(MasterLevelProgression.IsMasterLevelSpec(new Specialization("Stormlord", "Stormlord", 0, 1)), Is.True);
            Assert.That(MasterLevelProgression.IsMasterLevelSpec(new Specialization("Slash", "Slash", 0, 1)), Is.False);
            Assert.That(MasterLevelProgression.PathIndex(" warlord "), Is.EqualTo(7));
            Assert.That(MasterLevelProgression.PathIndex("Champion"), Is.EqualTo(-1));
        }

        [Test]
        public void LearnedListsTheChosenLineUpToTheMasterLevel()
        {
            var lines = (Dictionary<string, List<Spell>>)typeof(SkillBase).GetField("m_lineSpells", HiddenStatic).GetValue(null);
            Assume.That(lines.ContainsKey("Convoker"), Is.False, "no real Convoker line loaded in this process");
            SkillBase.AddScriptedSpell("Convoker", new Spell(new DbSpell { Name = "Test Ward", Type = "Bladeturn", Target = "Self" }, 1));
            SkillBase.AddScriptedSpell("Convoker", new Spell(new DbSpell { Name = "Test Servant", Type = "SummonCommander", Target = "Self" }, 3));
            _addedSpellLines.Add("Convoker");

            Player player = NewPlayer("Player", enrolled: true);
            MasterLevelProgression.Choose(player, "Convoker");
            player.Ml = 2;

            Assert.That(MasterLevelProgression.Learned(player).Select(spell => spell.Name), Is.EqualTo(new[] { "Test Ward" }));
            Assert.That(MasterLevelProgression.Describe(player), Does.Contain("Test Ward (ML 1)").And.Contain("Discipline: Convoker."));
        }

        [Test]
        public void DemyphonRedeemsOnlyTheNextCreditAndTheStarFreesTheDiscipline()
        {
            Player player = NewPlayer("Player", enrolled: true);
            MasterLevelProgression.Choose(player, "Stormlord");

            string answer = MasterLevelsMerchant.Redeem(player, new DbInventoryItem { Id_nb = "master_level_credit_bptoken_5" }, out bool consumed);
            Assert.That(consumed, Is.False, "the credit for 5 does not fit Master Level 1");
            Assert.That(answer, Does.Contain("Master Level 2"));

            MasterLevelsMerchant.Redeem(player, new DbInventoryItem { Id_nb = "master_level_credit_bptoken_1" }, out consumed);
            Assert.That(consumed, Is.False, "Master Level 1 is already held");

            player.MlXp = 12_000;
            MasterLevelsMerchant.Redeem(player, new DbInventoryItem { Id_nb = "master_level_credit_bptoken_2" }, out consumed);
            Assert.That(consumed, Is.True);
            Assert.That(player.MLLevel, Is.EqualTo(2));
            Assert.That(player.MLExperience, Is.EqualTo(12_000), "a credit keeps the experience already earned");

            MasterLevelsMerchant.Respec(player, new DbInventoryItem { Id_nb = MasterLevelsMerchant.RESPEC_TOKEN }, out consumed);
            Assert.That(consumed, Is.True);
            Assert.That(player.MLLine, Is.EqualTo(MasterLevelProgression.NO_PATH));
            Assert.That(player.MLLevel, Is.EqualTo(2));

            MasterLevelsMerchant.Respec(player, new DbInventoryItem { Id_nb = MasterLevelsMerchant.RESPEC_TOKEN }, out consumed);
            Assert.That(consumed, Is.False, "no discipline to turn from");
        }

        [Test]
        public void NpcsTheKillRewardsRefusePayNoMasterLevelExperience()
        {
            var warder = new BattleWarderNpc { Name = "Battle Warder", Level = 70 };
            Assert.That(warder.RewardStatus, Is.EqualTo(GameNPC.RewardEligibility.DeniedInvalid));
            Assert.That(MasterLevelProgression.KillValue(warder), Is.Zero, "a Battle Warder is not worth 700 per recast");

            Mob charmed = NewMob(55);
            charmed.CurrentRegion = (Region)RuntimeHelpers.GetUninitializedObject(typeof(Region));
            charmed.TempProperties.SetProperty(GameNPC.CHARMED_TICK_PROP, GameLoop.GameLoopTime + 1);
            Assert.That(charmed.RewardStatus, Is.EqualTo(GameNPC.RewardEligibility.DeniedRecentlyCharmed));

            Player player = NewPlayer("Player", enrolled: true);
            MasterLevelProgression.OnDying(GameLivingEvent.Dying, charmed, new DyingEventArgs(player, new List<GamePlayer> { player }));
            Assert.That(player.MLExperience, Is.Zero, "a recently released charm pays nothing");

            Assert.That(MasterLevelProgression.KillValue(NewMob(55)), Is.EqualTo(550), "an ordinary NPC still pays");
        }

        [Test]
        public void ProcessDeathPaysEveryAttackerThroughTheRealDyingNotify()
        {
            var dead = new LiveMob { Name = "Atlantean" };
            Player first = NewPlayer("First", enrolled: true);
            Player second = NewPlayer("Second", enrolled: true);
            Player bystander = NewPlayer("Bystander", enrolled: true);
            first.Region = second.Region = bystander.Region = dead.CurrentRegionID;
            long expire = GameLoop.GameLoopTime + 60000;
            dead.attackComponent.AttackerTracker.AddOrUpdate(first, true, expire);
            dead.attackComponent.AttackerTracker.AddOrUpdate(second, false, expire);

            var handler = new DOLEventHandler(MasterLevelProgression.OnDying);
            GameEventMgr.AddHandler(GameLivingEvent.Dying, handler);

            try
            {
                dead.ProcessDeath(null);
            }
            finally
            {
                GameEventMgr.RemoveHandler(GameLivingEvent.Dying, handler);
            }

            Assert.That(first.MLExperience, Is.EqualTo(550));
            Assert.That(second.MLExperience, Is.EqualTo(550), "every attacker, not only the killing blow");
            Assert.That(bystander.MLExperience, Is.Zero, "no part in the kill");
        }

        [Test]
        public void TheMasterLevelTitleFollowsTheChosenDiscipline()
        {
            var title = (MasterlevelTitle)RuntimeHelpers.GetUninitializedObject(typeof(MasterlevelTitle));
            Player player = NewPlayer("Player", enrolled: true);
            int raised = 0;
            DOLEventHandler listener = (e, sender, args) => raised++;
            GameEventMgr.AddHandler(player, GamePlayerEvent.BecomeML, listener);

            try
            {
                Assert.That(title.IsSuitable(player), Is.False, "enrolled without a discipline: no title");
                Assert.That(title.GetValue(null, player), Is.Empty, "no \"!ML Title 0!\"");

                MasterLevelProgression.Choose(player, "Convoker");
                Assert.That(raised, Is.GreaterThan(0), "the choice raises BecomeML, so the title shows without a relog");
                Assert.That(title.IsSuitable(player), Is.True);
                Assert.That(title.GetValue(null, player), Is.EqualTo("!ML Title 3!"), "Titles.ML.Line3 is Convoker");

                player.TestLevel = 45;
                Assert.That(title.IsSuitable(player), Is.True, "the title follows the discipline, not the level-50 spec");

                int before = raised;
                MasterLevelProgression.Respec(player);
                Assert.That(raised, Is.GreaterThan(before), "the respec raises BecomeML so the title is dropped");
                Assert.That(title.IsSuitable(player), Is.False);
            }
            finally
            {
                GameEventMgr.RemoveHandler(player, GamePlayerEvent.BecomeML, listener);
            }
        }

        [Test]
        public void TheClassNamesInTheGaherisMobRowsResolve()
        {
            Assembly server = typeof(GameServer).Assembly;

            Type merchant = server.GetType("DOL.GS.Scripts.MasterLevelsMerchant");
            Assert.That(merchant, Is.Not.Null);
            Assert.That(typeof(GameBountyMerchant).IsAssignableFrom(merchant), Is.True, "sells for bounty points");

            Type gaheris = server.GetType("DOL.GS.Scripts.GaherisArbiter");
            Assert.That(gaheris, Is.Not.Null);
            Assert.That(typeof(MasterLevelArbiter).IsAssignableFrom(gaheris), Is.True);

            Type plain = server.GetType("DOL.GS.Arbiter");
            Assert.That(plain, Is.Not.Null);
            Assert.That(typeof(MasterLevelArbiter).IsAssignableFrom(plain), Is.False, "the plain Arbiter stays lore only");

            Assert.That(server.GetType("DOL.GS.HeadScholar"), Is.Not.Null);
            Assert.That(server.GetType("DOL.GS.Researcher"), Is.Not.Null);
        }
    }
}
