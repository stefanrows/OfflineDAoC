using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture, NonParallelizable]
    public class UT_CompanionBuffCoverage
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        private PetTestLanguageScope _languageScope;
        private static readonly MethodInfo HasCoverageMethod = typeof(BotBrain).GetMethod(
            "HasOtherGroupMemberBuffCoverage", BindingFlags.NonPublic | BindingFlags.Static);
        private readonly List<GameLiving> _actors = new();
        private CompanionPolicyTestServerScope _serverScope;

        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public override byte Level => 50;
            public override bool IsAlive => true;
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override bool HasAbility(string keyName) => false;
        }

        private sealed class Player : GamePlayer
        {
            private Player() : base(null, null) { }
            public List<(SpellLine, List<Skill>)> UsableSpells = new();
            public override byte Level { get => 50; set { } }
            public override bool IsAlive => true;
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override List<(SpellLine, List<Skill>)> GetAllUsableListSpells(bool update = false) => UsableSpells;
        }

        private T Actor<T>() where T : GameLiving
        {
            T actor = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            actor.ObjectState = GameObject.eObjectState.Active;
            actor.effectListComponent = EffectListComponent.Create(actor);
            _actors.Add(actor);
            return actor;
        }

        private static Spell Buff(int id, string type, int level, int value, int damage = 0)
        {
            return new Spell(new DbSpell
            {
                SpellID = id,
                Name = "Buff coverage test",
                Type = type,
                Target = "Group",
                Range = 0,
                Radius = 1500,
                Duration = 600,
                Value = value,
                Damage = damage
            }, level);
        }

        private static Group GroupMembers(params GameLiving[] members)
        {
            Group group = new(members[0]);
            List<GameLiving> groupMembers = (List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden).GetValue(group);
            groupMembers.AddRange(members);
            foreach (GameLiving member in members)
                member.Group = group;
            return group;
        }

        private static bool HasCoverage(GameBot caster, GameLiving target, Spell requestedBuff) =>
            (bool)HasCoverageMethod.Invoke(null, new object[] { caster, target, requestedBuff });

        private static void AddActiveBuff(GameLiving target, GameLiving provider, Spell buff)
        {
            SpellLine line = new("coverage test", "coverage test", "", false);
            SpellHandler handler = new(provider, buff, line);
            ECSGameSpellEffect effect = new(new(target, 60_000, 1, handler));
            FieldInfo state = typeof(ECSGameEffect).GetField("_state", Hidden);
            state.SetValue(effect, Enum.Parse(state.FieldType, "Active"));

            Dictionary<eEffect, List<ECSGameEffect>> effects = (Dictionary<eEffect, List<ECSGameEffect>>)
                typeof(EffectListComponent).GetField("_effects", Hidden).GetValue(target.effectListComponent);
            if (!effects.TryGetValue(effect.EffectType, out List<ECSGameEffect> sameTypeEffects))
                effects[effect.EffectType] = sameTypeEffects = new();
            sameTypeEffects.Add(effect);
        }

        [SetUp]
        public void SetUp()
        {
            _languageScope = new PetTestLanguageScope();
            _serverScope = new CompanionPolicyTestServerScope();
        }

        [TearDown]
        public void TearDown()
        {
            _actors.Clear();
            _serverScope.Dispose();
            _languageScope.Dispose();
        }

        [Test]
        public void KnownPlayerBuffDoesNotCountUntilItIsApplied()
        {
            Bot companion = Actor<Bot>();
            Player player = Actor<Player>();
            Spell knownStrConBuff = Buff(88001, "StrengthConstitutionBuff", 40, 30);
            SpellLine specializationLine = new("test specialization", "test specialization", "", false);
            player.UsableSpells = new();
            player.UsableSpells.Add((specializationLine, new List<Skill> { knownStrConBuff }));
            typeof(GamePlayer).GetField("_usableSkills", Hidden).SetValue(player,
                new List<(Skill, Skill)> { (knownStrConBuff, specializationLine) });
            GroupMembers(companion, player);

            Spell requestedBaseBuff = Buff(88002, "StrengthBuff", 20, 12);
            Assert.That(HasCoverage(companion, companion, requestedBaseBuff), Is.False);
        }

        [Test]
        public void ActiveStrongerBuffFromAnotherCompanionCoversTheSameTarget()
        {
            Bot companion = Actor<Bot>();
            Bot provider = Actor<Bot>();
            GroupMembers(companion, provider);
            Spell appliedStrConBuff = Buff(88003, "StrengthConstitutionBuff", 40, 30);
            AddActiveBuff(companion, provider, appliedStrConBuff);

            Spell requestedBaseBuff = Buff(88004, "StrengthBuff", 20, 12);
            Assert.That(HasCoverage(companion, companion, requestedBaseBuff), Is.True);
        }

        [Test]
        public void ActiveStrongerBuffFromAPlayerCoversTheSameTarget()
        {
            Bot companion = Actor<Bot>();
            Player player = Actor<Player>();
            GroupMembers(companion, player);
            Spell appliedStrConBuff = Buff(88009, "StrengthConstitutionBuff", 40, 30);
            AddActiveBuff(companion, player, appliedStrConBuff);

            Spell requestedBaseBuff = Buff(88010, "StrengthBuff", 20, 12);
            Assert.That(HasCoverage(companion, companion, requestedBaseBuff), Is.True);
        }

        [Test]
        public void ActiveBuffsFromMultipleMembersCoverACompoundBuff()
        {
            Bot companion = Actor<Bot>();
            Bot strengthProvider = Actor<Bot>();
            Bot constitutionProvider = Actor<Bot>();
            GroupMembers(companion, strengthProvider, constitutionProvider);
            AddActiveBuff(companion, strengthProvider, Buff(88011, "StrengthBuff", 40, 30));
            AddActiveBuff(companion, constitutionProvider, Buff(88012, "ConstitutionBuff", 40, 30));

            Spell requestedBuff = Buff(88013, "StrengthConstitutionBuff", 20, 12);
            Assert.That(HasCoverage(companion, companion, requestedBuff), Is.True);
        }

        [Test]
        public void UnmappedBuffCoverageRequiresACompatibleFamilyAndRank()
        {
            Bot companion = Actor<Bot>();
            Bot provider = Actor<Bot>();
            GroupMembers(companion, provider);
            Spell requestedBuff = Buff(88005, "ArmorAbsorptionBuff", 20, 12, 5);

            AddActiveBuff(companion, provider, Buff(88006, "AblativeArmor", 40, 30, 10));
            Assert.That(HasCoverage(companion, companion, requestedBuff), Is.False,
                "A different unmapped buff family must not suppress the requested buff.");

            AddActiveBuff(companion, provider, Buff(88007, "ArmorAbsorptionBuff", 40, 30, 2));
            Assert.That(HasCoverage(companion, companion, requestedBuff), Is.False,
                "A higher Value does not cover a rank with stronger Damage.");

            AddActiveBuff(companion, provider, Buff(88008, "ArmorAbsorptionBuff", 10, 12, 5));
            Assert.That(HasCoverage(companion, companion, requestedBuff), Is.False,
                "The same strength from a lower-level rank does not count as an upgrade.");

            AddActiveBuff(companion, provider, Buff(88009, "ArmorAbsorptionBuff", 40, 30, 5));
            Assert.That(HasCoverage(companion, companion, requestedBuff), Is.True);
        }
    }
}
