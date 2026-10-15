using NUnit.Framework;

namespace DOL.GS.Tests
{
    // Configurable crew level band of the open BG (open_bg_band_min / open_bg_band_max, default 40 / 50).
    [TestFixture]
    public class UT_OpenBattlegroundBand
    {
        [TestCase(40, 50, 40, 50)]
        [TestCase(1, 50, 1, 50)]
        [TestCase(0, 50, 1, 50)]
        [TestCase(-7, 50, 1, 50)]
        [TestCase(40, 51, 40, 50)]
        [TestCase(30, 30, 30, 30)]
        [TestCase(20, 35, 20, 35)]
        [TestCase(45, 30, 40, 50)]
        [TestCase(51, 60, 50, 50)]
        public void ResolveBandClampsAndFallsBackWhenInverted(int min, int max, int expectedMin, int expectedMax)
        {
            (int resolvedMin, int resolvedMax) = OpenBattlegroundProperties.ResolveBand(min, max);
            Assert.That((resolvedMin, resolvedMax), Is.EqualTo((expectedMin, expectedMax)));
        }

        [Test]
        public void DefaultsAreTheCurrentCrewBand()
        {
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundProperties.BandMin, Is.EqualTo(40));
                Assert.That(OpenBattlegroundProperties.BandMax, Is.EqualTo(50));
                Assert.That(OpenBattlegroundRoster.MinLevel, Is.EqualTo(40));
                Assert.That(OpenBattlegroundRoster.MaxLevel, Is.EqualTo(50));
            });
        }

        [Test]
        public void TheCrewFollowsTheConfiguredBand()
        {
            int min = OpenBattlegroundProperties.OPEN_BG_BAND_MIN, max = OpenBattlegroundProperties.OPEN_BG_BAND_MAX;
            try
            {
                OpenBattlegroundProperties.OPEN_BG_BAND_MIN = 20;
                OpenBattlegroundProperties.OPEN_BG_BAND_MAX = 30;
                Assert.Multiple(() =>
                {
                    Assert.That(OpenBattlegroundRoster.IsCandidate(true, 25, false, false, false, 1, true, false), Is.True);
                    Assert.That(OpenBattlegroundRoster.IsCandidate(true, 45, false, false, false, 1, true, false), Is.False);
                    Assert.That(OpenBattlegroundRoles.MayHuntHuman(25, 10, false), Is.False, "a crew bot at the band minimum spares a low human");
                    Assert.That(OpenBattlegroundRoles.MayHuntHuman(19, 10, false), Is.True);
                });
            }
            finally
            {
                OpenBattlegroundProperties.OPEN_BG_BAND_MIN = min;
                OpenBattlegroundProperties.OPEN_BG_BAND_MAX = max;
            }
        }
    }
}
