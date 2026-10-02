using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_MeleeHybridHealer
    {
        // A melee hybrid in a Healer or Buffer role still fights (bug 77,
        // Paladin added 2026-10-02: it only chanted and never attacked).
        [TestCase(eCharacterClass.Friar)]
        [TestCase(eCharacterClass.Warden)]
        [TestCase(eCharacterClass.Paladin)]
        public void MeleeHybridsFightInSupportRoles(eCharacterClass characterClass) =>
            Assert.That(BotPartyRoles.IsMeleeHybridHealer(characterClass), Is.True);

        [TestCase(eCharacterClass.Cleric)]
        [TestCase(eCharacterClass.Druid)]
        [TestCase(eCharacterClass.Healer)]
        public void PureHealersStaySupport(eCharacterClass characterClass) =>
            Assert.That(BotPartyRoles.IsMeleeHybridHealer(characterClass), Is.False);
    }
}
