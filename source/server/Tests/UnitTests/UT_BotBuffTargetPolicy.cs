using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using NUnit.Framework;
using Need = DOL.GS.BotBuffTargetPolicy.Need;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_BotBuffTargetPolicy
    {
        private static Spell Buff(string type, int concentration = 10) =>
            new(new DbSpell { Type = type, Target = "Realm", Value = 20, Concentration = (byte)concentration }, 20);

        private static Need Bot(string type, ICharacterClass cls, bool encumbered = false, int weapon = 0, int level = 50) =>
            BotBuffTargetPolicy.NeedOf(Buff(type), cls, false, encumbered, weapon, level);

        private static readonly ICharacterClass[] Tanks = [new ClassArmsman(), new ClassWarrior(), new ClassHero()];
        private static readonly ICharacterClass[] ListCasters = [new ClassWizard(), new ClassRunemaster(), new ClassEldritch()];
        private static readonly ICharacterClass[] NoAcuityHybrids = [new ClassCleric(), new ClassHealer(), new ClassDruid(), new ClassPaladin(), new ClassThane(), new ClassChampion()];
        private static readonly ICharacterClass[] Casters = [new ClassWizard(), new ClassRunemaster(), new ClassEldritch(), new ClassCleric(), new ClassDruid()];
        private static readonly ICharacterClass[] Fighters = [new ClassArmsman(), new ClassBerserker(), new ClassValewalker(), new ClassFriar(), new ClassThane()];

        [TestCaseSource(nameof(Tanks))]
        public void AcuityDoesNothingOnTanks(ICharacterClass tank) =>
            Assert.That(Bot("AcuityBuff", tank), Is.EqualTo(Need.None));

        [TestCaseSource(nameof(ListCasters))]
        public void ListCastersNeedAcuity(ICharacterClass member) =>
            Assert.That(Bot("AcuityBuff", member), Is.EqualTo(Need.Required));

        [TestCaseSource(nameof(NoAcuityHybrids))]
        public void AcuityDoesNothingOnHealersAndHybrids(ICharacterClass member) =>
            Assert.That(Bot("AcuityBuff", member), Is.EqualTo(Need.None));

        [TestCaseSource(nameof(Casters))]
        public void StrengthIsOptionalOnCasterBotsUnlessOverloaded(ICharacterClass caster)
        {
            Assert.That(Bot("StrengthBuff", caster), Is.EqualTo(Need.Optional));
            Assert.That(Bot("StrengthBuff", caster, encumbered: true), Is.EqualTo(Need.Required));
        }

        [TestCaseSource(nameof(Fighters))]
        public void FightersNeedStrength(ICharacterClass fighter) =>
            Assert.That(Bot("StrengthBuff", fighter), Is.EqualTo(Need.Required));

        [Test]
        public void HealerStrengthIsASpecQuestion()
        {
            Assert.That(Bot("StrengthBuff", new ClassCleric(), weapon: 5), Is.EqualTo(Need.Optional), "rejuvenation Cleric");
            Assert.That(Bot("StrengthBuff", new ClassCleric(), weapon: 30), Is.EqualTo(Need.Required), "battle Cleric");
            Assert.That(Bot("StrengthBuff", new ClassDruid(), weapon: 20, level: 40), Is.EqualTo(Need.Required));
            Assert.That(Bot("StrengthBuff", new ClassDruid(), weapon: 19, level: 40), Is.EqualTo(Need.Optional));
            Assert.That(Bot("StrengthBuff", new ClassWizard(), weapon: 50), Is.EqualTo(Need.Optional), "a weapon never makes a Wizard a fighter");
        }

        [Test]
        public void RealPlayersNeedEveryEffectiveBuff()
        {
            Assert.That(BotBuffTargetPolicy.NeedOf(Buff("StrengthBuff"), new ClassWizard(), true, false), Is.EqualTo(Need.Required));
            Assert.That(BotBuffTargetPolicy.NeedOf(Buff("AcuityBuff"), new ClassArmsman(), true, false), Is.EqualTo(Need.None),
                "acuity still does nothing on a tank");
        }

        [Test]
        public void EveryoneNeedsConstitutionAndSpecBuffs()
        {
            Assert.That(Bot("StrengthConstitutionBuff", new ClassWizard()), Is.EqualTo(Need.Required));
            Assert.That(Bot("ConstitutionBuff", new ClassCleric()), Is.EqualTo(Need.Required));
        }

        [Test]
        public void PetsAndUnknownTargetsTakeEverything() =>
            Assert.That(BotBuffTargetPolicy.NeedOf(Buff("AcuityBuff"), null, false, false), Is.EqualTo(Need.Required));
    }
}
