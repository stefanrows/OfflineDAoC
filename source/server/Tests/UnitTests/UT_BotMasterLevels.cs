using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.Database;
using DOL.Database.Handlers;
using DOL.Events;
using DOL.GS;
using DOL.GS.PacketHandler;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Master Levels for companions and autonomous world bots (TOA_PLAN WP2.4): companion
    /// enrolment with the owner, the discipline by role, Master Level experience from a shared
    /// kill, the world bot roll, the new record columns, and that only the chosen line loads.
    /// The class career and the Master Level spells are synthetic (class id 251).
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_BotMasterLevels
    {
        private const int CareerClassId = 251;
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private static readonly string[] MasterLevelColumns = { "MLLevel", "MLExperience", "MLLine" };

        private GameServer _previousServer;
        private int _previousXpPerLevel;
        private string _previousDistribution;
        private readonly List<string> _addedSpecNames = new();
        private readonly List<string> _addedSpellLines = new();
        private bool _addedCareer;

        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
            protected override DOL.GS.ServerRules.IServerRules ServerRulesImpl => new DOL.GS.ServerRules.NormalServerRules();
        }

        public class Recorder : DispatchProxy
        {
            public List<string> Messages = new();

            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == nameof(IPacketLib.SendMessage) && args.Length > 0 && args[0] is string text)
                    Messages.Add(text);

                return method.ReturnType != typeof(void) && method.ReturnType.IsValueType
                    ? Activator.CreateInstance(method.ReturnType)
                    : null;
            }
        }

        public class ClassProxy : DispatchProxy
        {
            public int Id;

            public static ICharacterClass For(int id)
            {
                ICharacterClass proxy = Create<ICharacterClass, ClassProxy>();
                ((ClassProxy)(object)proxy).Id = id;
                return proxy;
            }

            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == "get_ID")
                    return Id;
                if (method.Name == "get_Name")
                    return "Tester";

                return method.ReturnType != typeof(void) && method.ReturnType.IsValueType
                    ? Activator.CreateInstance(method.ReturnType)
                    : null;
            }
        }

        /// <summary>The companion owner: a player the Arbiter can enrol.</summary>
        private sealed class Owner : GamePlayer
        {
            private Owner() : base(null, null) { }
            public byte TestLevel;
            public int TestX;
            public bool Dead;
            public bool Granted;
            public byte Line;
            public int Ml;
            public long MlXp;
            public IPacketLib Packets;
            public string TestName;
            public override string Name { get => TestName; set => TestName = value; }
            public override byte Level { get => TestLevel; set { } }
            public override bool IsAlive => !Dead;
            public override int X => TestX;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override bool MLGranted { get => Granted; set => Granted = value; }
            public override byte MLLine { get => Line; set => Line = value; }
            public override int MLLevel { get => Ml; set => Ml = value; }
            public override long MLExperience { get => MlXp; set => MlXp = value; }
            public override IPacketLib Out => Packets;
            public override ICharacterClass CharacterClass => ClassProxy.For(CareerClassId);
            public override void SaveIntoDatabase() { }
            public override void RefreshSpecDependantSkills(bool sendMessages) { }
            public override void EnemyKilled(GameLiving enemy) { }
            private System.Collections.Concurrent.ConcurrentDictionary<DOL.GS.Quests.AbstractQuest, byte> _quests;
            public override System.Collections.Concurrent.ConcurrentDictionary<DOL.GS.Quests.AbstractQuest, byte> QuestList => _quests ??= new();
            public List<string> Messages => ((Recorder)(object)Packets).Messages;
        }

        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public byte TestLevel;
            public int TestX;
            public bool Alive;
            public string TestName;
            public ICharacterClass Class;
            public int Saves;
            public int Refreshes;
            public int TestHealth;
            public eRealm TestRealm;
            public override int Health { get => TestHealth; set => TestHealth = value; }
            public override eRealm Realm { get => TestRealm; set => TestRealm = value; }

            public override int ChangeHealth(GameObject changeSource, eHealthChangeType healthChangeType, int changeAmount)
            {
                TestHealth += changeAmount;
                return changeAmount;
            }

            public override byte Level { get => TestLevel; set => TestLevel = value; }
            public override string Name { get => TestName; set => TestName = value; }
            public override bool IsAlive => Alive;
            public override int X => TestX;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override ICharacterClass CharacterClass => Class;
            protected override void QueueMasterLevelSave() => Saves++;

            // The real refresh also adds abilities and styles; the spec list is what these tests read.
            public override void RefreshSpecDependantSkills(bool sendMessages)
            {
                Refreshes++;
                LoadClassSpecializations(sendMessages);
            }

            public void GiveSpellLine(SpellLine line)
            {
                lock (m_spellLines)
                    m_spellLines.Add(line);
            }
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

        [SetUp]
        public void Setup()
        {
            _previousServer = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            _previousXpPerLevel = MasterLevelProgression.ML_XP_PER_LEVEL;
            MasterLevelProgression.ML_XP_PER_LEVEL = 10;
            _previousDistribution = BotMasterLevels.BOT_ML_DISTRIBUTION;
            BotMasterLevels.BOT_ML_DISTRIBUTION = BotMasterLevels.DEFAULT_DISTRIBUTION;

            var byName = (Dictionary<string, Tuple<Type, string, ushort, int>>)typeof(SkillBase).GetField("m_specsByName", HiddenStatic).GetValue(null);
            var byClass = (Dictionary<int, Dictionary<string, int>>)typeof(SkillBase).GetField("m_specsByClass", HiddenStatic).GetValue(null);
            Assert.That(byClass.ContainsKey(CareerClassId), Is.False, "synthetic class id is free");

            var career = new Dictionary<string, int> { ["BotTestSlash"] = 1 };
            AddSpec(byName, "BotTestSlash", typeof(Specialization), 9201);

            for (int i = 0; i < MasterLevelProgression.Paths.Length; i++)
            {
                string key = MasterLevelProgression.Paths[i].Key;
                AddSpec(byName, key, typeof(LiveMasterLevelsSpecialization), 9300 + i);
                career[key] = 50;
            }

            byClass[CareerClassId] = career;
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
                byClass.Remove(CareerClassId);

            _addedSpecNames.Clear();
            _addedSpellLines.Clear();
            _addedCareer = false;
            MasterLevelProgression.ML_XP_PER_LEVEL = _previousXpPerLevel;
            BotMasterLevels.BOT_ML_DISTRIBUTION = _previousDistribution;
            GameServer.LoadTestDouble(_previousServer);
        }

        private void AddSpec(Dictionary<string, Tuple<Type, string, ushort, int>> byName, string key, Type type, int id)
        {
            if (byName.TryAdd(key, new Tuple<Type, string, ushort, int>(type, key, 0, id)))
                _addedSpecNames.Add(key);
        }

        private static void Field(Type type, object owner, string name, object value) =>
            type.GetField(name, Hidden | BindingFlags.Public).SetValue(owner, value);

        private static void Property(object owner, string name, object value) =>
            typeof(GameBot).GetProperty(name).SetValue(owner, value);

        private static Owner NewOwner(string name, byte level = 50, int x = 0, bool enrolled = false)
        {
            var owner = (Owner)RuntimeHelpers.GetUninitializedObject(typeof(Owner));
            owner.ObjectState = GameObject.eObjectState.Active;
            owner.TestName = name;
            owner.TestLevel = level;
            owner.TestX = x;
            owner.Packets = DispatchProxy.Create<IPacketLib, Recorder>();
            Field(typeof(GamePlayer), owner, "m_specialization", new Dictionary<string, Specialization>());
            Field(typeof(GamePlayer), owner, "_specializationLock", new Lock());
            Field(typeof(GamePlayer), owner, "m_spellLines", new List<SpellLine>());
            Field(typeof(GamePlayer), owner, "_spellLinesListLock", new Lock());

            if (enrolled)
            {
                owner.Granted = true;
                owner.Ml = 1;
                owner.Line = MasterLevelProgression.NO_PATH;
            }

            return owner;
        }

        private static Bot NewBot(string name, eCharacterClass characterClass, byte level = 50, int x = 0,
            Owner owner = null, bool world = false, bool helper = false, string role = null, int classId = 0)
        {
            var bot = (Bot)RuntimeHelpers.GetUninitializedObject(typeof(Bot));
            bot.TestName = name;
            bot.TestLevel = level;
            bot.TestX = x;
            bot.Alive = true;
            bot.Class = ClassProxy.For(classId > 0 ? classId : (int)characterClass);
            Field(typeof(GameLiving), bot, "<TempProperties>k__BackingField", new PropertyCollection());
            Field(typeof(GameNPC), bot, "m_brains", new ArrayList());
            Field(typeof(GameBot), bot, "m_specialization", new Dictionary<string, Specialization>());
            Field(typeof(GameBot), bot, "m_spellLines", new List<SpellLine>());
            Property(bot, nameof(GameBot.IsAutonomousWorldBot), world);
            Property(bot, nameof(GameBot.IsTemporaryGroupHelper), helper);

            if (owner != null)
            {
                Property(bot, nameof(GameBot.Owner), owner);

                if (!helper)
                    Field(typeof(GameBot), bot, "<PlayerCompanionRecord>k__BackingField",
                        new PlayerCompanionRecord { CompanionId = Guid.NewGuid().ToString(), TacticalRole = role ?? string.Empty });
            }

            bot.ObjectState = GameObject.eObjectState.Active;
            return bot;
        }

        private static Mob NewMob(byte level, int x = 0)
        {
            var mob = (Mob)RuntimeHelpers.GetUninitializedObject(typeof(Mob));
            mob.ObjectState = GameObject.eObjectState.Active;
            mob.Name = "test mob";
            mob.TestLevel = level;
            mob.TestX = x;
            Field(typeof(GameNPC), mob, "m_brains", new ArrayList());
            Field(typeof(GameLiving), mob, "<TempProperties>k__BackingField", new PropertyCollection());
            Field(typeof(GameLiving), mob, "m_xpGainers", new Dictionary<GameLiving, double>());
            Field(typeof(GameLiving), mob, "XpGainersLock", new Lock());
            return mob;
        }

        private static Group GroupOf(GameLiving leader, params GameLiving[] mates)
        {
            var group = new Group(leader);
            var members = (List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden).GetValue(group);

            foreach (GameLiving member in new[] { leader }.Concat(mates))
            {
                members.Add(member);
                member.Group = group;
            }

            return group;
        }

        private static void Join(Group group, GameLiving member)
        {
            ((List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden).GetValue(group)).Add(member);
            member.Group = group;
        }

        private static IEnumerable<string> MasterLevelSpecs(GameBot bot) =>
            bot.GetSpecList().Where(MasterLevelProgression.IsMasterLevelSpec).Select(spec => spec.KeyName);

        private void AddLineSpell(string line, string type, int rank, int damageType = 0, string target = "Realm")
        {
            if (!_addedSpellLines.Contains(line))
            {
                var lines = (Dictionary<string, List<Spell>>)typeof(SkillBase).GetField("m_lineSpells", HiddenStatic).GetValue(null);
                Assume.That(lines.ContainsKey(line), Is.False, $"no real {line} line loaded in this process");
                _addedSpellLines.Add(line);
            }

            SkillBase.AddScriptedSpell(line, new Spell(new DbSpell
            {
                SpellID = 95000 + _addedSpellLines.IndexOf(line) * 20 + rank,
                Name = $"{line} {rank}",
                Type = type,
                Target = target,
                DamageType = damageType,
                Radius = 750,
            }, rank));
        }

        // ------------------------------------------------------------------ discipline by role

        [TestCase(eCharacterClass.Armsman, null, "Warlord")]
        [TestCase(eCharacterClass.Warrior, "tank", "Warlord")]
        [TestCase(eCharacterClass.Hero, null, "Warlord")]
        [TestCase(eCharacterClass.Paladin, "tank", "Warlord")]
        [TestCase(eCharacterClass.Paladin, "healer", "Perfecter")]
        [TestCase(eCharacterClass.Berserker, null, "Battlemaster")]
        [TestCase(eCharacterClass.Berserker, "healer", "Battlemaster")]
        [TestCase(eCharacterClass.Blademaster, "attacker", "Battlemaster")]
        [TestCase(eCharacterClass.Cleric, null, "Perfecter")]
        [TestCase(eCharacterClass.Healer, "healer", "Perfecter")]
        [TestCase(eCharacterClass.Shaman, null, "Perfecter")]
        [TestCase(eCharacterClass.Theurgist, null, "Convoker")]
        [TestCase(eCharacterClass.Spiritmaster, null, "Convoker")]
        [TestCase(eCharacterClass.Animist, null, "Convoker")]
        [TestCase(eCharacterClass.Wizard, null, "Stormlord")]
        [TestCase(eCharacterClass.Runemaster, null, "Stormlord")]
        [TestCase(eCharacterClass.Eldritch, null, "Stormlord")]
        [TestCase(eCharacterClass.Infiltrator, null, "Spymaster")]
        [TestCase(eCharacterClass.Hunter, null, "Spymaster")]
        [TestCase(eCharacterClass.Nightshade, null, "Spymaster")]
        public void TheDisciplineFollowsTheRole(eCharacterClass characterClass, string role, string expected)
        {
            Assert.That(BotMasterLevels.DisciplineFor(characterClass, role), Is.EqualTo(expected));
        }

        [Test]
        public void EveryAdvancedClassIsInTheDisciplineTable()
        {
            var baseClasses = new HashSet<eCharacterClass>
            {
                eCharacterClass.Unknown, eCharacterClass.Acolyte, eCharacterClass.AlbionRogue, eCharacterClass.Disciple,
                eCharacterClass.Elementalist, eCharacterClass.Fighter, eCharacterClass.Forester, eCharacterClass.Guardian,
                eCharacterClass.Mage, eCharacterClass.Magician, eCharacterClass.MidgardRogue, eCharacterClass.Mystic,
                eCharacterClass.Naturalist, eCharacterClass.Seer, eCharacterClass.Stalker, eCharacterClass.Viking,
            };

            foreach (eCharacterClass characterClass in Enum.GetValues<eCharacterClass>().Where(cls => !baseClasses.Contains(cls)))
            {
                Assert.That(BotMasterLevels.IsInDisciplineTable(characterClass), Is.True, $"{characterClass} has a discipline");
                Assert.That(MasterLevelProgression.PathIndex(BotMasterLevels.DisciplineFor(characterClass)), Is.GreaterThanOrEqualTo(0));
            }
        }

        // ------------------------------------------------------------------ companion enrolment

        [Test]
        public void TheArbiterEnrolsTheCompanionsInTheGroupWithTheirOwner()
        {
            Mob arbiter = NewMob(50);
            Owner owner = NewOwner("Owner");
            Bot tank = NewBot("Tank", eCharacterClass.Armsman, x: 500, owner: owner);
            Bot healer = NewBot("Healer", eCharacterClass.Paladin, x: 600, owner: owner, role: "healer");
            Bot young = NewBot("Young", eCharacterClass.Cleric, level: 35, x: 100, owner: owner);
            Bot far = NewBot("Far", eCharacterClass.Wizard, x: 3000, owner: owner);
            Bot helper = NewBot("Helper", eCharacterClass.Cleric, x: 100, owner: owner, helper: true);
            Owner novice = NewOwner("Novice", level: 30, x: 100);
            Bot novicesPal = NewBot("Pal", eCharacterClass.Cleric, x: 100, owner: novice);
            GroupOf(owner, tank, healer, young, far, helper, novice, novicesPal);

            List<GamePlayer> players = MasterLevelProgression.EnrolCompany(owner, arbiter);

            Assert.That(players, Is.EqualTo(new GamePlayer[] { owner }), "companions are not in the player list");
            Assert.That(tank.MLLevel, Is.EqualTo(1), "a companion starts at Master Level 1, like its owner");
            Assert.That(tank.MasterLevelPath, Is.EqualTo("Warlord"), "tank: Warlord");
            Assert.That(healer.MasterLevelPath, Is.EqualTo("Perfecter"), "a Paladin played as healer walks Perfecter");
            Assert.That(tank.Saves, Is.EqualTo(1));
            Assert.That(tank.Refreshes, Is.EqualTo(1), "the line is refreshed at once");
            Assert.That(young.IsOnMasterLevelPath, Is.False, "the trials begin at level 40");
            Assert.That(far.IsOnMasterLevelPath, Is.False, "3000 units from the Arbiter");
            Assert.That(helper.IsOnMasterLevelPath, Is.False, "a /spawn helper never holds Master Levels");
            Assert.That(novicesPal.IsOnMasterLevelPath, Is.False, "an owner who is not enrolled takes no companion along");
            Assert.That(owner.Messages, Has.Some.EqualTo("Your companions begin the trials with you: Tank (Warlord), Healer (Perfecter)."));

            tank.MLLevel = 4;
            Assert.That(BotMasterLevels.EnrolCompanion(tank), Is.False, "a second visit keeps the standing");
            Assert.That(tank.MLLevel, Is.EqualTo(4));

            Bot recruit = NewBot("Recruit", eCharacterClass.Wizard, x: 100, owner: owner);
            Join(owner.Group, recruit);
            MasterLevelProgression.EnrolCompany(owner, arbiter);
            Assert.That(recruit.MasterLevelPath, Is.EqualTo("Stormlord"), "a companion recruited later joins on the next visit");
        }

        // ------------------------------------------------------------------ experience

        [Test]
        public void ACompanionEarnsFromTheSameKillAsItsOwner()
        {
            Owner owner = NewOwner("Owner", enrolled: true);
            Bot near = NewBot("Near", eCharacterClass.Armsman, x: 300, owner: owner);
            Bot distant = NewBot("Distant", eCharacterClass.Cleric, x: WorldMgr.MAX_EXPFORKILL_DISTANCE + 500, owner: owner);
            Bot dead = NewBot("Dead", eCharacterClass.Cleric, x: 100, owner: owner);
            Bot offPath = NewBot("OffPath", eCharacterClass.Cleric, x: 100, owner: owner);
            near.SetMasterLevelState(1, 31_800, "Warlord");
            distant.SetMasterLevelState(1, 0, "Perfecter");
            dead.SetMasterLevelState(1, 0, "Perfecter");
            dead.Alive = false;
            GroupOf(owner, near, distant, dead, offPath);

            Mob mob = NewMob(55);
            MasterLevelProgression.OnDying(GameLivingEvent.Dying, mob, new DyingEventArgs(owner, new List<GamePlayer> { owner }));

            Assert.That(owner.MLExperience, Is.EqualTo(550));
            Assert.That(near.MLLevel, Is.EqualTo(2), "31,800 + 550 fills the 32,000 bar");
            Assert.That(near.MLExperience, Is.EqualTo(350), "the overshoot carries over");
            Assert.That(near.Refreshes, Is.EqualTo(1), "the new rank's abilities load at once");
            Assert.That(near.Saves, Is.EqualTo(1));
            Assert.That(owner.Messages, Has.Some.EqualTo("Near has reached Master Level 2!"));
            Assert.That(distant.MLExperience, Is.Zero, "out of experience range");
            Assert.That(dead.MLExperience, Is.Zero, "a dead companion earns nothing");
            Assert.That(offPath.IsOnMasterLevelPath, Is.False, "a kill never enrols");

            MasterLevelProgression.OnDying(GameLivingEvent.Dying, NewMob(44), new DyingEventArgs(owner, new List<GamePlayer> { owner }));
            Assert.That(near.MLExperience, Is.EqualTo(350), "level 44 pays nothing, the same rule as for players");
        }

        [Test]
        public void ACompanionEarnsOnlyWhenItsOwnerEarnedTheKill()
        {
            Mob mob = NewMob(50);
            Owner owner = NewOwner("Owner", enrolled: true);
            Bot companion = NewBot("Companion", eCharacterClass.Armsman, x: 100, owner: owner);
            companion.SetMasterLevelState(1, 0, "Warlord");
            GroupOf(owner, companion);
            var death = new DyingEventArgs(owner, new List<GamePlayer> { owner });

            owner.Dead = true;
            MasterLevelProgression.OnDying(GameLivingEvent.Dying, mob, death);
            Assert.That(companion.MLExperience, Is.Zero, "a dead owner earns nothing, so his companion earns nothing");

            owner.Dead = false;
            owner.TestX = WorldMgr.MAX_EXPFORKILL_DISTANCE + 500;
            MasterLevelProgression.OnDying(GameLivingEvent.Dying, mob, death);
            Assert.That(companion.MLExperience, Is.Zero, "nor does an owner out of experience range");

            owner.TestX = 0;
            owner.Ml = GamePlayer.ML_MAX_LEVEL;
            MasterLevelProgression.OnDying(GameLivingEvent.Dying, mob, death);
            Assert.That(owner.MLExperience, Is.Zero, "Master Level 10 earns nothing more");
            Assert.That(companion.MLExperience, Is.EqualTo(500), "but his companion keeps earning");
        }

        [Test]
        public void DefendingMartyrCostsTheCasterOnceHoweverManyItHeals()
        {
            Bot warlord = NewBot("Warlord", eCharacterClass.Armsman, world: true);
            warlord.TestHealth = 1_000;
            warlord.TestRealm = eRealm.Albion;
            var martyr = new Spell(new DbSpell { Name = "Defending Martyr", Type = "PBAEHeal", Target = "Realm", DamageType = 1, Value = 90, Radius = 750 }, 8);
            var handler = new DOL.GS.Spells.PBAEHealHandler(warlord, martyr, new SpellLine("Warlord", "Warlord", "Warlord", false));

            for (int i = 0; i < 4; i++)
            {
                Bot mate = NewBot("Mate" + i, eCharacterClass.Cleric, world: true);
                mate.TestHealth = 100;
                mate.TestRealm = eRealm.Albion;
                handler.OnDirectEffect(mate);
            }

            Assert.That(warlord.TestHealth, Is.EqualTo(1_000), "no price per healed target");
            Assert.That(handler.PayMartyrCost(), Is.EqualTo(900));
            Assert.That(warlord.TestHealth, Is.EqualTo(100), "90% once per cast");

            var battlecry = new Spell(new DbSpell { Name = "Bolstering Battlecry", Type = "PBAEHeal", Target = "Realm", DamageType = 0, Value = 10, Radius = 750 }, 2);
            Assert.That(new DOL.GS.Spells.PBAEHealHandler(warlord, battlecry, null).PayMartyrCost(), Is.Zero, "the battle cry costs no health");
        }

        [Test]
        public void WorldBotsThatFoughtAndTheirGroupEarnFromTheKill()
        {
            Bot fighter = NewBot("Fighter", eCharacterClass.Warrior, x: 100, world: true);
            Bot mate = NewBot("Mate", eCharacterClass.Healer, x: 200, world: true);
            Bot stranger = NewBot("Stranger", eCharacterClass.Healer, x: 200, world: true);
            fighter.SetMasterLevelState(3, 1_000, "Warlord");
            mate.SetMasterLevelState(0, 0, "Perfecter");
            stranger.SetMasterLevelState(0, 0, "Perfecter");
            GroupOf(fighter, mate);

            Mob mob = NewMob(50);
            mob.XPGainers[fighter] = 400;
            MasterLevelProgression.OnDying(GameLivingEvent.Dying, mob, new DyingEventArgs(fighter));

            Assert.That(fighter.MLExperience, Is.EqualTo(1_500), "level 50 x 10");
            Assert.That(mate.MLExperience, Is.EqualTo(500), "a group member in range is paid too");
            Assert.That(stranger.MLExperience, Is.Zero, "no part in the kill");
            Assert.That(fighter.Saves, Is.EqualTo(1), "progress is saved with the bot's state");

            fighter.MLExperience = 31_900;
            MasterLevelProgression.OnDying(GameLivingEvent.Dying, mob, new DyingEventArgs(fighter));
            Assert.That(fighter.MLLevel, Is.EqualTo(4));
            Assert.That(fighter.MLExperience, Is.EqualTo(400));

            fighter.SetMasterLevelState(GamePlayer.ML_MAX_LEVEL, 0, "Warlord");
            Assert.That(BotMasterLevels.AddExperience(fighter, 50_000), Is.Zero);
            Assert.That(fighter.MLExperience, Is.Zero, "Master Level 10 holds no experience");
        }

        // ------------------------------------------------------------------ world bot roll

        [Test]
        public void TheWorldBotRollStaysWithinItsLevelBounds()
        {
            double[] samples = Enumerable.Range(0, 200).Select(i => i / 200.0).ToArray();

            Assert.That(samples.Select(s => BotMasterLevels.RollLevel(39, s)), Has.All.EqualTo(-1), "no Master Level below 40");
            Assert.That(samples.Select(s => BotMasterLevels.RollLevel(40, s)), Has.All.EqualTo(0));
            Assert.That(samples.Select(s => BotMasterLevels.RollLevel(42, s)), Has.All.EqualTo(0));
            Assert.That(samples.Select(s => BotMasterLevels.RollLevel(45, s)), Has.All.InRange(0, 1));
            Assert.That(samples.Select(s => BotMasterLevels.RollLevel(49, s)), Has.All.InRange(0, 3));
            Assert.That(samples.Select(s => BotMasterLevels.RollLevel(50, s)), Has.All.InRange(0, GamePlayer.ML_MAX_LEVEL));
            Assert.That(BotMasterLevels.RollLevel(50, 0), Is.Zero);
            Assert.That(BotMasterLevels.RollLevel(50, 0.99999), Is.EqualTo(GamePlayer.ML_MAX_LEVEL));
        }

        [Test]
        public void MostLevel50WorldBotsAreMasterLevel0To3AndFew7To10()
        {
            const int count = 10_000;
            int[] rolled = Enumerable.Range(0, count).Select(i => BotMasterLevels.RollLevel(50, (i + 0.5) / count)).ToArray();

            double low = rolled.Count(level => level <= 3) / (double)count;
            double high = rolled.Count(level => level >= 7) / (double)count;

            Assert.That(low, Is.GreaterThanOrEqualTo(0.6), "most 50s are Master Level 0-3");
            Assert.That(high, Is.InRange(0.05, 0.15), "a few are Master Level 7-10");
        }

        [Test]
        public void AnInvalidDistributionFallsBackToTheDefault()
        {
            int[] fallback = BotMasterLevels.Weights(BotMasterLevels.DEFAULT_DISTRIBUTION);

            Assert.That(BotMasterLevels.Weights("1,2,3"), Is.EqualTo(fallback), "eleven weights or none");
            Assert.That(BotMasterLevels.Weights("1,1,1,1,1,1,1,1,1,1,-1"), Is.EqualTo(fallback), "no negative weights");
            Assert.That(BotMasterLevels.Weights("0,0,0,0,0,0,0,0,0,0,0"), Is.EqualTo(fallback), "not all zero");
            Assert.That(BotMasterLevels.Weights("0,0,0,0,0,0,0,0,0,0,5"), Is.EqualTo(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5 }));

            BotMasterLevels.BOT_ML_DISTRIBUTION = "0,0,0,0,0,0,0,0,0,0,5";
            Assert.That(BotMasterLevels.RollLevel(50, 0.1), Is.EqualTo(10), "the property is read on every roll");
            Assert.That(BotMasterLevels.RollLevel(49, 0.1), Is.EqualTo(3), "the level cap still holds");
        }

        [Test]
        public void AWorldBotRollsOnceOnLoadAndKeepsItsRoll()
        {
            Bot wizard = NewBot("Wizard", eCharacterClass.Wizard, world: true);
            var record = new OfflineWorldBotRecord { Name = "Wizard", Level = 50 };

            wizard.RestoreMasterLevelState(record);

            Assert.That(wizard.MasterLevelPath, Is.EqualTo("Stormlord"), "a nuker walks Stormlord");
            Assert.That(wizard.MLLevel, Is.InRange(0, GamePlayer.ML_MAX_LEVEL));
            Assert.That(record.MLLine, Is.EqualTo("Stormlord"), "the roll goes into the record");
            Assert.That(record.MLLevel, Is.EqualTo(wizard.MLLevel));
            Assert.That(wizard.AutonomousStateDirty, Is.True, "and is saved with the next snapshot");

            record.MLLevel = 6;
            record.MLExperience = 1_234;
            Bot reloaded = NewBot("Wizard", eCharacterClass.Wizard, world: true);
            reloaded.RestoreMasterLevelState(record);
            Assert.That(reloaded.MLLevel, Is.EqualTo(6), "a saved Master Level is never rolled again");
            Assert.That(reloaded.MLExperience, Is.EqualTo(1_234));

            Assert.That(BotMasterLevels.TryRollWorldBot(NewBot("Young", eCharacterClass.Wizard, level: 39, world: true), () => 0.5), Is.False);
            Assert.That(BotMasterLevels.TryRollWorldBot(NewBot("Companion", eCharacterClass.Wizard, owner: NewOwner("O")), () => 0.5), Is.False,
                "companions are enrolled by the Arbiter, not rolled");
            Assert.That(BotMasterLevels.TryRollWorldBot(NewBot("Helper", eCharacterClass.Wizard, world: true, helper: true), () => 0.5), Is.False);
            Assert.That(BotMasterLevels.TryRollWorldBot(reloaded, () => 0.99), Is.False, "already on the path");
        }

        // ------------------------------------------------------------------ persistence

        [Test]
        public void TheMasterLevelStateRoundTripsThroughBothRecords()
        {
            Owner owner = NewOwner("Owner");
            Bot companion = NewBot("Companion", eCharacterClass.Cleric, owner: owner);
            companion.SetMasterLevelState(7, 12_345, "perfecter");
            var companionRecord = new PlayerCompanionRecord();
            companion.CopyMasterLevelStateTo(companionRecord);
            Assert.That((companionRecord.MLLevel, companionRecord.MLExperience, companionRecord.MLLine), Is.EqualTo((7, 12_345L, "Perfecter")));

            Bot restored = NewBot("Companion", eCharacterClass.Cleric, owner: owner);
            restored.RestoreMasterLevelState(companionRecord);
            Assert.That((restored.MLLevel, restored.MLExperience, restored.MasterLevelPath), Is.EqualTo((7, 12_345L, "Perfecter")));
            Assert.That(restored.MLLine, Is.EqualTo(MasterLevelProgression.PathIndex("Perfecter")));

            restored.RestoreMasterLevelState(new PlayerCompanionRecord { MLLevel = 0, MLLine = null });
            Assert.That(restored.IsOnMasterLevelPath, Is.False, "an old row (null discipline) is off the path");
            Assert.That(restored.MLLine, Is.EqualTo(MasterLevelProgression.NO_PATH));

            restored.RestoreMasterLevelState(new PlayerCompanionRecord { MLLevel = 99, MLExperience = 5, MLLine = "Warlord" });
            Assert.That(restored.MLLevel, Is.EqualTo(GamePlayer.ML_MAX_LEVEL), "ranks are clamped");
            Assert.That(restored.MLExperience, Is.Zero, "Master Level 10 holds no experience");

            restored.RestoreMasterLevelState(new PlayerCompanionRecord { MLLevel = 3, MLLine = "Champion" });
            Assert.That(restored.IsOnMasterLevelPath, Is.False, "an unknown discipline is no discipline");
            Assert.That(restored.MLLevel, Is.Zero);

            Bot world = NewBot("World", eCharacterClass.Warrior, world: true);
            world.SetMasterLevelState(2, 99, "Warlord");
            var worldRecord = new OfflineWorldBotRecord();
            world.CopyMasterLevelStateTo(worldRecord);
            Bot worldAgain = NewBot("World", eCharacterClass.Warrior, world: true);
            worldAgain.RestoreMasterLevelState(worldRecord);
            Assert.That((worldAgain.MLLevel, worldAgain.MLExperience, worldAgain.MasterLevelPath), Is.EqualTo((2, 99L, "Warlord")));
        }

        [Test]
        public void LegacyCompanionAndWorldBotTablesGainTheMasterLevelColumns()
        {
            string path = TemporaryPath();
            try
            {
                new SqliteObjectDatabase(ConnectionString(path)).RegisterDataObject(typeof(PlayerCompanionRecord));
                new SqliteObjectDatabase(ConnectionString(path)).RegisterDataObject(typeof(OfflineWorldBotRecord));
                DropColumns(path, "player_companions");
                DropColumns(path, "offline_world_bots");
                ExecuteSql(path, "INSERT INTO `player_companions` (`CompanionId`, `Name`, `Level`, `Experience`) " +
                                 "VALUES ('legacy-companion', 'Old Companion', 50, 9001)");
                ExecuteSql(path, "INSERT INTO `offline_world_bots` (`Name`, `Realm`, `Level`, `RealmPoints`) VALUES ('Old Bot', 2, 50, 900)");

                SqliteObjectDatabase migrated = new(ConnectionString(path));
                migrated.RegisterDataObject(typeof(PlayerCompanionRecord));
                migrated.RegisterDataObject(typeof(OfflineWorldBotRecord));
                PlayerCompanionRecord companion = migrated.SelectObjects<PlayerCompanionRecord>(
                    DB.Column(nameof(PlayerCompanionRecord.CompanionId)).IsEqualTo("legacy-companion")).Single();
                OfflineWorldBotRecord world = migrated.SelectObjects<OfflineWorldBotRecord>(
                    DB.Column(nameof(OfflineWorldBotRecord.Name)).IsEqualTo("Old Bot")).Single();

                Assert.That(companion.Experience, Is.EqualTo(9001), "the old row keeps its data");
                Assert.That((companion.MLLevel, companion.MLExperience), Is.EqualTo((0, 0L)));
                Assert.That(string.IsNullOrEmpty(companion.MLLine), Is.True, "an old companion is off the path, not a Banelord");
                Assert.That(world.RealmPoints, Is.EqualTo(900));
                Assert.That(string.IsNullOrEmpty(world.MLLine), Is.True);

                companion.MLLevel = 4;
                companion.MLExperience = 777;
                companion.MLLine = "Warlord";
                companion.Dirty = true;
                world.MLLevel = 9;
                world.MLLine = "Spymaster";
                world.Dirty = true;
                Assert.That(migrated.SaveObject(companion), Is.True);
                Assert.That(migrated.SaveObject(world), Is.True);

                SqliteObjectDatabase restarted = new(ConnectionString(path));
                restarted.RegisterDataObject(typeof(PlayerCompanionRecord));
                restarted.RegisterDataObject(typeof(OfflineWorldBotRecord));
                PlayerCompanionRecord reloaded = restarted.SelectObjects<PlayerCompanionRecord>(
                    DB.Column(nameof(PlayerCompanionRecord.CompanionId)).IsEqualTo("legacy-companion")).Single();
                OfflineWorldBotRecord worldReloaded = restarted.SelectObjects<OfflineWorldBotRecord>(
                    DB.Column(nameof(OfflineWorldBotRecord.Name)).IsEqualTo("Old Bot")).Single();

                Assert.That((reloaded.MLLevel, reloaded.MLExperience, reloaded.MLLine), Is.EqualTo((4, 777L, "Warlord")));
                Assert.That((worldReloaded.MLLevel, worldReloaded.MLLine), Is.EqualTo((9, "Spymaster")));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        // ------------------------------------------------------------------ the chosen line only

        [Test]
        public void OnlyTheChosenLineLoadsAndOnlyItsRanksAreHeld()
        {
            AddLineSpell("Warlord", "PBAEHeal", 2);
            AddLineSpell("Warlord", "CoweringBellow", 3, target: "Enemy");
            AddLineSpell("Warlord", "Critical", 5);
            AddLineSpell("Warlord", "PBAEHeal", 8, damageType: 1);
            AddLineSpell("Perfecter", "CureDisease", 1, target: "Group");

            Bot bot = NewBot("Bot", eCharacterClass.Armsman, world: true, classId: CareerClassId);

            bot.LoadClassSpecializations(false);
            Assert.That(bot.HasSpecialization("BotTestSlash"), Is.True, "ordinary career specs still load");
            Assert.That(MasterLevelSpecs(bot), Is.Empty, "off the path: no Master Level line");

            bot.SetMasterLevelState(0, 0, "Warlord");
            bot.LoadClassSpecializations(false);
            Assert.That(MasterLevelSpecs(bot), Is.Empty, "Master Level 0 holds no line yet");

            bot.SetMasterLevelState(3, 0, "Warlord");
            bot.RefreshMasterLevelSkills();
            Assert.That(MasterLevelSpecs(bot), Is.EqualTo(new[] { "Warlord" }), "only the chosen discipline");
            Assert.That(bot.GetSpecList().Where(spec => spec.Trainable).Select(spec => spec.KeyName), Has.No.Member("Warlord"),
                "a Master Level line is never a trainable bot spec");
            Assert.That(bot.MasterLevelSpells.Select(spell => spell.Level), Is.EqualTo(new[] { 2, 3 }), "ranks up to Master Level 3");
            Assert.That(bot.MasterLevelCombatSpells.Select(spell => spell.SpellType), Is.EqualTo(new[] { eSpellType.PBAEHeal }),
                "Cowering Bellow is held but never pressed by the AI");

            bot.SetMasterLevelState(10, 0, "Warlord");
            bot.RefreshMasterLevelSkills();
            Assert.That(bot.MasterLevelCombatSpells.Select(spell => spell.Level), Is.EqualTo(new[] { 2, 5 }),
                "Defending Martyr (90% of the caster's health) is never pressed");

            bot.GiveSpellLine(new SpellLine("Warlord", "Warlord", "Warlord", false));
            bot.SetMasterLevelState(1, 0, "Perfecter");
            bot.RefreshMasterLevelSkills();
            Assert.That(MasterLevelSpecs(bot), Is.EqualTo(new[] { "Perfecter" }), "a changed discipline drops the old line");
            Assert.That(bot.GetSpellLine("Warlord"), Is.Null, "and its spell line");
            Assert.That(bot.MasterLevelCombatSpells.Select(spell => spell.SpellType), Is.EqualTo(new[] { eSpellType.CureDisease }));

            bot.TestLevel = 49;
            bot.RefreshMasterLevelSkills();
            Assert.That(MasterLevelSpecs(bot), Is.Empty, "the career rows open the lines at level 50");
            Assert.That(bot.MasterLevelSpells, Is.Empty);
        }

        [Test]
        public void TheMasterLevelSpecReadsTheBotsRank()
        {
            Bot bot = NewBot("Bot", eCharacterClass.Armsman, world: true);
            bot.SetMasterLevelState(6, 0, "Warlord");
            var spec = new LiveMasterLevelsSpecialization("Warlord", "Warlord", 0, 1);

            Assert.That(spec.GetSpecLevelForLiving(bot), Is.EqualTo(6), "bots hold their Master Level like players");
            Assert.That(spec.GetSpecLevelForLiving(NewMob(50)), Is.Zero, "ordinary NPCs hold none");
        }

        [Test]
        public void TheAiPressesOnlyAbilitiesItCanJudge()
        {
            Spell Make(string type, string target = "Realm", int damageType = 0) =>
                new(new DbSpell { Name = type, Type = type, Target = target, DamageType = damageType }, 1);

            Assert.That(BotMasterLevels.UseOf(Make("Critical")), Is.EqualTo(BotMasterLevelUse.FightBuff));
            Assert.That(BotMasterLevels.UseOf(Make("FatigueConsumptionBuff")), Is.EqualTo(BotMasterLevelUse.FightBuff));
            Assert.That(BotMasterLevels.UseOf(Make("MLABSBuff")), Is.EqualTo(BotMasterLevelUse.FightBuff));
            Assert.That(BotMasterLevels.UseOf(Make("PBAEHeal")), Is.EqualTo(BotMasterLevelUse.HealBurst));
            Assert.That(BotMasterLevels.UseOf(Make("PBAEHeal", damageType: 1)), Is.EqualTo(BotMasterLevelUse.None), "Defending Martyr");
            Assert.That(BotMasterLevels.UseOf(Make("MLEndudrain", "Enemy")), Is.EqualTo(BotMasterLevelUse.EnduranceDrain));
            Assert.That(BotMasterLevels.UseOf(Make("MLManadrain", "Enemy")), Is.EqualTo(BotMasterLevelUse.PowerDrain));
            Assert.That(BotMasterLevels.UseOf(Make("CureDisease", "Group")), Is.EqualTo(BotMasterLevelUse.CureDisease));
            Assert.That(BotMasterLevels.UseOf(Make("CureDisease", "Realm")), Is.EqualTo(BotMasterLevelUse.None), "only the group cure is Perfecter's");
            Assert.That(BotMasterLevels.UseOf(Make("CureNearsight", "Group")), Is.EqualTo(BotMasterLevelUse.CureNearsight));

            // Crystal Titan and Dazzling Array are cast since WP2.6 (UT_BotMasterLevelCasting).
            foreach (string held in new[] { "Banespike", "FOH", "Groupport", "BlanketOfCamouflage", "Grapple", "ThrowWeapon" })
                Assert.That(BotMasterLevels.UseOf(Make(held)), Is.EqualTo(BotMasterLevelUse.None), held);

            Assert.That(BotMasterLevels.ReactionChance(BotMasterLevelUse.FightBuff), Is.InRange(1, 99), "not on every think");
        }

        [Test]
        public void TheBotBarIsThePlayerBar()
        {
            var player = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));

            for (int level = 1; level < GamePlayer.ML_MAX_LEVEL; level++)
                Assert.That(player.GetMLExperienceForLevel(level), Is.EqualTo(BotMasterLevels.EXPERIENCE_PER_LEVEL));
        }

        private static string ConnectionString(string path) => $"Data Source={path};Version=3;Pooling=False;";

        private static void DropColumns(string path, string table)
        {
            using var connection = new SQLiteConnection(ConnectionString(path));
            connection.Open();

            foreach (string column in MasterLevelColumns)
            {
                using SQLiteCommand command = connection.CreateCommand();
                command.CommandText = $"ALTER TABLE `{table}` DROP COLUMN `{column}`";
                command.ExecuteNonQuery();
            }
        }

        private static void ExecuteSql(string path, string sql)
        {
            using var connection = new SQLiteConnection(ConnectionString(path));
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static string TemporaryPath() => Path.Combine(Path.GetTempPath(),
            "daoc-bot-ml-" + Guid.NewGuid().ToString("N") + ".sqlite3");
    }
}
