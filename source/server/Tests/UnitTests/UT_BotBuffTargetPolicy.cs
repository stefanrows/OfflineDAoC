using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_BotBuffTargetPolicy
    {
        private static Spell Buff(string type, int concentration = 10) =>
            new(new DbSpell { Type = type, Target = "Realm", Value = 20, Concentration = (byte)concentration }, 20);

        private static readonly ICharacterClass[] Tanks = [new ClassArmsman(), new ClassWarrior(), new ClassHero()];
        private static readonly ICharacterClass[] ListCasters = [new ClassWizard(), new ClassRunemaster(), new ClassEldritch()];
        private static readonly ICharacterClass[] NoAcuityHybrids = [new ClassCleric(), new ClassHealer(), new ClassDruid(), new ClassPaladin(), new ClassThane(), new ClassChampion()];
        private static readonly ICharacterClass[] Casters = [new ClassWizard(), new ClassRunemaster(), new ClassEldritch(), new ClassCleric(), new ClassDruid()];
        private static readonly ICharacterClass[] Fighters = [new ClassArmsman(), new ClassBerserker(), new ClassValewalker(), new ClassFriar(), new ClassThane()];

        [TestCaseSource(nameof(Tanks))]
        public void PowerlessTanksInEveryRealmTakeNoAcuity(ICharacterClass tank) =>
            Assert.That(BotBuffTargetPolicy.Wants(Buff("AcuityBuff"), tank, false, 100), Is.False);

        [TestCaseSource(nameof(ListCasters))]
        public void ListCastersTakeAcuity(ICharacterClass member) =>
            Assert.That(BotBuffTargetPolicy.Wants(Buff("AcuityBuff"), member, false, 0), Is.True);

        [TestCaseSource(nameof(NoAcuityHybrids))]
        public void HealersAndHybridsTakeNoAcuityBecauseTheServerIgnoresIt(ICharacterClass member) =>
            Assert.That(BotBuffTargetPolicy.Wants(Buff("AcuityBuff"), member, false, 100), Is.False);

        [TestCaseSource(nameof(Casters))]
        public void CastersTakeStrengthOnlyWhenOverloadedOrConcentrationIsSpare(ICharacterClass caster)
        {
            Spell strength = Buff("StrengthBuff", 10);
            Assert.That(BotBuffTargetPolicy.Wants(strength, caster, false, 9), Is.False, "too little concentration left");
            Assert.That(BotBuffTargetPolicy.Wants(strength, caster, false, 10), Is.True, "another buff still fits");
            Assert.That(BotBuffTargetPolicy.Wants(strength, caster, true, 0), Is.True, "overloaded");
        }

        [TestCaseSource(nameof(Fighters))]
        public void FightersAlwaysTakeStrength(ICharacterClass fighter) =>
            Assert.That(BotBuffTargetPolicy.Wants(Buff("StrengthBuff"), fighter, false, 0), Is.True);

        [Test]
        public void CastersStillTakeStrengthConstitution() =>
            Assert.That(BotBuffTargetPolicy.Wants(Buff("StrengthConstitutionBuff"), new ClassWizard(), false, 0), Is.True);

        [Test]
        public void PetsAndUnknownTargetsTakeEverything() =>
            Assert.That(BotBuffTargetPolicy.Wants(Buff("AcuityBuff"), null, false, 0), Is.True);
    }
}
