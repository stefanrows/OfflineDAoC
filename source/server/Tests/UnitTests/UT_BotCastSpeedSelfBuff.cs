using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_BotCastSpeedSelfBuff
    {
        [TestCase("DexterityBuff", "Realm", true)]
        [TestCase("DexterityQuicknessBuff", "Realm", true)]
        [TestCase("DexterityQuicknessBuff", "Group", true)]
        [TestCase("DexterityBuff", "Self", true)]
        [TestCase("StrengthBuff", "Realm", false)]
        [TestCase("AcuityBuff", "Realm", false)]
        [TestCase("DexterityBuff", "Pet", false)]
        public void OnlyDexterityBuffsTheCasterCanTakeCountAsCastSpeed(string type, string target, bool expected)
        {
            var spell = new Spell(new DbSpell { Type = type, Target = target, Value = 20, Duration = 1200 }, 20);
            Assert.That(BotCastSpeedSelfBuff.IsCastSpeedBuff(spell), Is.EqualTo(expected));
        }

        [Test]
        public void NoSpellIsNoCastSpeedBuff() =>
            Assert.That(BotCastSpeedSelfBuff.IsCastSpeedBuff(null), Is.False);
    }
}
