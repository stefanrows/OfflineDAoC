using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_PerformerSpeedSong
    {
        private static Spell Song(int group, string type = "SpeedEnhancement") =>
            new(new DbSpell { Type = type, Target = "Group", Frequency = 60, SpellGroup = group }, 20);

        [TestCase(1101)] // Minstrel
        [TestCase(3608)] // Skald
        [TestCase(5151)] // Bard
        public void EveryRealmsPerformerSpeedRunsBesideOtherSongs(int group) =>
            Assert.That(EffectListComponent.IgnoresOtherPulseSpells(Song(group)), Is.True);

        [Test]
        public void OtherSongsAndChantsStillReplaceEachOther()
        {
            Assert.That(EffectListComponent.IgnoresOtherPulseSpells(Song(5131)), Is.False, "Warden speed chant");
            Assert.That(EffectListComponent.IgnoresOtherPulseSpells(Song(0, "EnduranceRegenBuff")), Is.False);
            Assert.That(EffectListComponent.IgnoresOtherPulseSpells(null), Is.False);
        }
    }
}
