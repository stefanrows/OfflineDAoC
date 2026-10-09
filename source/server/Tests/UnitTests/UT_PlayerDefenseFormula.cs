using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{

    [TestFixture, NonParallelizable]
    public class UT_PlayerDefenseFormula
    {
        private double _previousBlockCap;
        private double _previousEvadeCap;
        private double _previousParryCap;

        private static readonly FieldInfo LegacyEffectsField =
            typeof(GameLiving).GetField("m_effects", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo CreateLegacyEffects =
            typeof(GameLiving).GetMethod("CreateEffectsList", BindingFlags.Instance | BindingFlags.NonPublic);

        [SetUp]
        public void SetUpDefenseCaps()
        {
            _previousBlockCap = Properties.BLOCK_CAP;
            _previousEvadeCap = Properties.EVADE_CAP;
            _previousParryCap = Properties.PARRY_CAP;
            Properties.BLOCK_CAP = 1.0;
            Properties.EVADE_CAP = 1.0;
            Properties.PARRY_CAP = 0.5;
        }

        [TearDown]
        public void RestoreDefenseCaps()
        {
            Properties.BLOCK_CAP = _previousBlockCap;
            Properties.EVADE_CAP = _previousEvadeCap;
            Properties.PARRY_CAP = _previousParryCap;
        }

        [TestCase(false, true, eObjectType.SlashingWeapon)]
        [TestCase(true, false, eObjectType.SlashingWeapon)]
        [TestCase(true, true, eObjectType.Longbow)]
        public void BotCannotFallBackToNpcParryWhenEquipmentOrSpecIsIneligible(
            bool hasWeapon, bool hasSpec, eObjectType weaponType)
        {
            using var server = new EpicTestServerScope();
            DefenseBot defender = Bot(hasSpec);
            defender.Weapon = hasWeapon ? new DbInventoryItem { Object_Type = (int)weaponType } : null;

            Assert.That(defender.TryParry(Attack(Bot(), defender), null, 1), Is.Zero);
        }

        [TestCase(eRealm.Albion)]
        [TestCase(eRealm.Hibernia)]
        public void BotParryUsesPvpCapForSameRealmAndCrossRealmAttackers(eRealm attackerRealm)
        {
            using var server = new EpicTestServerScope();
            DefenseBot defender = Bot();
            defender.Identity = eRealm.Albion;
            DefenseBot attacker = Bot();
            attacker.Identity = attackerRealm;

            Assert.That(defender.TryParry(Attack(attacker, defender), null, 1),
                Is.EqualTo(Properties.PARRY_CAP));
        }

        [Test]
        public void BotRearParryIsRejectedButOrdinaryMonsterRetainsItsTemplatePath()
        {
            using var server = new EpicTestServerScope();
            DefenseBot bot = Bot();
            bot.Frontal = false;
            var monster = Monster();

            Assert.That(bot.TryParry(Attack(monster, bot), null, 1), Is.Zero);
            Assert.That(monster.TryParry(Attack(Bot(), monster), null, 1), Is.EqualTo(1));
        }

        [TestCase(false, eObjectType.Shield)]
        [TestCase(true, eObjectType.SlashingWeapon)]
        public void BotBlockRequiresAnEquippedShield(bool hasLeftWeapon, eObjectType leftWeaponType)
        {
            using var server = new EpicTestServerScope();
            DefenseBot defender = Bot();
            defender.AbilityKeys.Add(Abilities.Shield);
            defender.LeftWeapon = hasLeftWeapon ? Item(leftWeaponType, Slot.LEFTHAND) : null;

            Assert.That(defender.TryBlock(Attack(Bot(), defender), out _), Is.Zero);
        }

        [Test]
        public void BotBlockRequiresTheShieldAbility()
        {
            using var server = new EpicTestServerScope();
            DefenseBot defender = Bot();
            defender.LeftWeapon = Item(eObjectType.Shield, Slot.LEFTHAND);

            double chance = defender.TryBlock(Attack(Bot(), defender), out int shieldSize);

            Assert.That(chance, Is.Zero);
            Assert.That(shieldSize, Is.EqualTo(2));
        }

        [TestCase(Slot.RIGHTHAND, true)]
        [TestCase(Slot.TWOHAND, false)]
        public void BotBlockRequiresAOneHandedWeaponSetup(int weaponSlot, bool shouldBlock)
        {
            using var server = new EpicTestServerScope();
            DefenseBot defender = Bot();
            defender.AbilityKeys.Add(Abilities.Shield);
            defender.LeftWeapon = Item(eObjectType.Shield, Slot.LEFTHAND);
            defender.Weapon = Item(eObjectType.SlashingWeapon, weaponSlot);

            double chance = defender.TryBlock(Attack(Bot(), defender), out int shieldSize);

            Assert.That(chance, Is.EqualTo(shouldBlock ? Math.Min(1.0, Properties.BLOCK_CAP) : 0).Within(0.001));
            Assert.That(shieldSize, Is.EqualTo(2));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void BotMeleeBlockRequiresFrontalFacing(bool frontal)
        {
            using var server = new EpicTestServerScope();
            DefenseBot defender = Bot();
            defender.AbilityKeys.Add(Abilities.Shield);
            defender.LeftWeapon = Item(eObjectType.Shield, Slot.LEFTHAND);
            defender.Frontal = frontal;

            double chance = defender.TryBlock(Attack(Bot(), defender), out _);

            Assert.That(chance, Is.EqualTo(frontal ? Math.Min(1.0, Properties.BLOCK_CAP) : 0).Within(0.001));
        }

        [TestCase(50, 100, 0.5)]
        [TestCase(100, 0, 0)]
        public void BotMeleeBlockUsesShieldQualityAndCondition(int quality, int condition, double expected)
        {
            using var server = new EpicTestServerScope();
            DefenseBot defender = Bot();
            defender.AbilityKeys.Add(Abilities.Shield);
            defender.LeftWeapon = Item(eObjectType.Shield, Slot.LEFTHAND, quality, condition);

            Assert.That(defender.TryBlock(Attack(Bot(), defender), out _),
                Is.EqualTo(Math.Min(expected, Properties.BLOCK_CAP)).Within(0.001));
        }

        [TestCase(eRealm.Albion)]
        [TestCase(eRealm.Hibernia)]
        public void BotBlockUsesPvpCapForSameRealmAndCrossRealmAttackers(eRealm attackerRealm)
        {
            using var server = new EpicTestServerScope();
            double previousCap = Properties.BLOCK_CAP;
            Properties.BLOCK_CAP = 0.6;
            try
            {
                DefenseBot defender = Bot();
                defender.Identity = eRealm.Albion;
                defender.AbilityKeys.Add(Abilities.Shield);
                defender.LeftWeapon = Item(eObjectType.Shield, Slot.LEFTHAND);
                DefenseBot attacker = Bot();
                attacker.Identity = attackerRealm;

                AttackData attack = Attack(attacker, defender);
                Assert.That(defender.TryBlock(attack, out _), Is.EqualTo(0.6));
            }
            finally
            {
                Properties.BLOCK_CAP = previousCap;
            }
        }

        [Test]
        public void BotEvadeRequiresAnAbilityInsteadOfFallingThroughToNpcChance()
        {
            using var server = new EpicTestServerScope();
            DefenseBot defender = Bot();

            Assert.That(defender.TryEvade(Attack(Bot(), defender), null), Is.Zero);
        }

        [TestCase(Abilities.Evade, true)]
        [TestCase(Abilities.Evade, false)]
        [TestCase(Abilities.Advanced_Evade, false)]
        [TestCase(Abilities.Enhanced_Evade, false)]
        public void BotEvadeUsesFacingUnlessAdvancedOrEnhanced(string ability, bool frontal)
        {
            using var server = new EpicTestServerScope();
            DefenseBot defender = Bot();
            defender.AbilityKeys.Add(ability);
            defender.Frontal = frontal;
            bool ignoresFacing = ability is Abilities.Advanced_Evade or Abilities.Enhanced_Evade;
            double expected = frontal || ignoresFacing ? Math.Min(1.0, Properties.EVADE_CAP) : 0;

            Assert.That(defender.TryEvade(Attack(Bot(), defender), null), Is.EqualTo(expected).Within(0.001));
        }

        [TestCase(eRealm.Albion)]
        [TestCase(eRealm.Hibernia)]
        public void BotEvadeUsesPvpCapForSameRealmAndCrossRealmAttackers(eRealm attackerRealm)
        {
            using var server = new EpicTestServerScope();
            double previousCap = Properties.EVADE_CAP;
            Properties.EVADE_CAP = 0.4;
            try
            {
                DefenseBot defender = Bot();
                defender.Identity = eRealm.Albion;
                defender.AbilityKeys.Add(Abilities.Advanced_Evade);
                defender.Frontal = false;
                DefenseBot attacker = Bot();
                attacker.Identity = attackerRealm;

                Assert.That(defender.TryEvade(Attack(attacker, defender), null), Is.EqualTo(0.4));
            }
            finally
            {
                Properties.EVADE_CAP = previousCap;
            }
        }

        [Test]
        public void OrdinaryMonsterKeepsItsTemplateBlockAndEvadeChances()
        {
            using var server = new EpicTestServerScope();
            DefenseMonster monster = Monster(Item(eObjectType.SlashingWeapon, Slot.LEFTHAND));
            DefenseBot attacker = Bot();

            Assert.That(monster.TryBlock(Attack(attacker, monster), out int shieldSize), Is.EqualTo(1));
            Assert.That(shieldSize, Is.Zero);
            Assert.That(monster.TryEvade(Attack(attacker, monster), null), Is.EqualTo(1));
        }

        private static AttackData Attack(GameLiving attacker, GameLiving defender) => new()
        {
            Attacker = attacker,
            Target = defender,
            AttackType = AttackData.eAttackType.MeleeOneHand,
        };

        private static DefenseBot Bot(bool hasSpec = true)
        {
            var bot = (DefenseBot)RuntimeHelpers.GetUninitializedObject(typeof(DefenseBot));
            bot.ParrySpec = hasSpec;
            bot.Frontal = true;
            bot.AbilityKeys = new HashSet<string>();
            bot.Weapon = Item(eObjectType.SlashingWeapon, Slot.RIGHTHAND);
            LegacyEffectsField.SetValue(bot, CreateLegacyEffects.Invoke(bot, null));
            bot.effectListComponent = EffectListComponent.Create(bot);
            return bot;
        }

        private static DefenseMonster Monster(DbInventoryItem leftWeapon = null)
        {
            var monster = new DefenseMonster { Frontal = true, LeftWeapon = leftWeapon };
            monster.effectListComponent = EffectListComponent.Create(monster);
            return monster;
        }

        private static DbInventoryItem Item(eObjectType objectType, int itemType, int quality = 100,
            int condition = 100, int shieldSize = 2)
        {
            var template = new DbItemTemplate
            {
                Object_Type = (int)objectType,
                Item_Type = itemType,
                Type_Damage = shieldSize,
                Quality = quality,
                Condition = condition,
                MaxCondition = 100,
            };
            return new DbInventoryItem { Template = template, Condition = condition };
        }

        private sealed class DefenseBot : GameBot
        {
            private DefenseBot() : base((OfflineWorldBotRecord)null) { }
            public bool ParrySpec;
            public bool Frontal;
            public eRealm Identity;
            public DbInventoryItem Weapon;
            public DbInventoryItem LeftWeapon;
            public HashSet<string> AbilityKeys;
            public override eRealm Realm { get => Identity; set => Identity = value; }
            public override bool IsCrowdControlled => false;
            public override bool IsCasting => false;
            public override bool IsSitting { get => false; set { } }
            public override DbInventoryItem ActiveWeapon => Weapon;
            public override DbInventoryItem ActiveLeftWeapon => LeftWeapon;
            public override bool HasAbility(string keyName) => AbilityKeys.Contains(keyName);
            public override bool HasSpecialization(string keyName) => ParrySpec;
            public override int GetModified(eProperty property) => 1000;
            public override bool IsObjectInFront(GameObject target, double heading, int alwaysTrueRange = 32) => Frontal;
        }

        private sealed class DefenseMonster : GameNPC
        {
            public bool Frontal = true;
            public DbInventoryItem LeftWeapon;
            public override bool IsCrowdControlled => false;
            public override bool IsCasting => false;
            public override bool IsSitting { get => false; set { } }
            public override DbInventoryItem ActiveLeftWeapon => LeftWeapon;
            public override int GetModified(eProperty property) => 1000;
            public override bool IsObjectInFront(GameObject target, double heading, int alwaysTrueRange = 32) => Frontal;
        }
    }

}
