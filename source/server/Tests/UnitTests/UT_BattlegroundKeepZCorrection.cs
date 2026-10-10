using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_BattlegroundKeepZCorrection
    {
        private static KeepSite Leirvik => BattlegroundKeepLayouts.FindSite(134);
        private static KeepSite Albion201 => BattlegroundKeepLayouts.PortalSites.First(site => site.KeepId == 201);
        private static KeepSite Hibernia203 => BattlegroundKeepLayouts.PortalSites.First(site => site.KeepId == 203);

        private static KeepRowZState Row(KeepSite site, int z) => new(true, site.Region, site.X, site.Y, z);

        private static IEnumerable<(KeepSite Site, int WrongZ)> Known() => new[]
        {
            (Leirvik, 14281), (Albion201, 7700), (Hibernia203, 8000),
        };

        [Test]
        public void SavedWrongZOfEachKnownKeepIsCorrected()
        {
            foreach ((KeepSite site, int wrongZ) in Known())
                Assert.That(BattlegroundKeepZPlanner.ShouldCorrect(site, wrongZ, Row(site, wrongZ)), Is.True, $"keep {site.KeepId}");
        }

        [Test]
        public void CorrectionIsIdempotentOnceTheRowHoldsTheSiteZ()
        {
            foreach ((KeepSite site, int wrongZ) in Known())
                Assert.That(BattlegroundKeepZPlanner.ShouldCorrect(site, wrongZ, Row(site, site.Z)), Is.False, $"keep {site.KeepId}");
        }

        [Test]
        public void AnyOtherSavedZIsLeftAlone()
        {
            foreach ((KeepSite site, int wrongZ) in Known())
            {
                Assert.Multiple(() =>
                {
                    Assert.That(BattlegroundKeepZPlanner.ShouldCorrect(site, wrongZ, Row(site, wrongZ + 1)), Is.False, $"keep {site.KeepId} +1");
                    Assert.That(BattlegroundKeepZPlanner.ShouldCorrect(site, wrongZ, Row(site, wrongZ - 1)), Is.False, $"keep {site.KeepId} -1");
                });
            }
        }

        [Test]
        public void OnlyTheSameKeepIsCorrected()
        {
            KeepSite site = Albion201;
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundKeepZPlanner.ShouldCorrect(site, 7700, Row(site, 7700) with { RowExists = false }), Is.False, "row missing");
                Assert.That(BattlegroundKeepZPlanner.ShouldCorrect(site, 7700, Row(site, 7700) with { Region = 238 }), Is.False, "region");
                Assert.That(BattlegroundKeepZPlanner.ShouldCorrect(site, 7700, Row(site, 7700) with { X = site.X + 1 }), Is.False, "x");
                Assert.That(BattlegroundKeepZPlanner.ShouldCorrect(site, 7700, Row(site, 7700) with { Y = site.Y - 1 }), Is.False, "y");
                Assert.That(BattlegroundKeepZPlanner.ShouldCorrect(null, 7700, Row(site, 7700)), Is.False, "site missing");
            });
        }

        [Test]
        public void NoCorrectionWhenTheSiteAlreadyMatchesTheWrongZ()
        {
            KeepSite site = new(237, 201, Albion201.Name, Albion201.X, Albion201.Y, 7700, 0, Albion201.Template, true);
            Assert.That(BattlegroundKeepZPlanner.ShouldCorrect(site, 7700, Row(site, 7700)), Is.False);
        }

        [Test]
        public void TheCorrectionTableNamesExactlyTheGroundCheckedKeeps()
        {
            var table = BattlegroundKeepZCorrection.Corrections.Select(correction => (correction.KeepId, correction.WrongZ)).ToList();
            Assert.That(table, Is.EqualTo(new[] { (134, 14281), (201, 7700), (203, 8000) }));
            foreach (KeepZCorrection correction in BattlegroundKeepZCorrection.Corrections)
            {
                KeepSite site = BattlegroundKeepLayouts.Sites.Concat(BattlegroundKeepLayouts.PortalSites)
                    .FirstOrDefault(candidate => candidate.KeepId == correction.KeepId);
                Assert.That(site, Is.Not.Null, $"keep {correction.KeepId} has a site");
                Assert.That(site.Z, Is.Not.EqualTo(correction.WrongZ), $"keep {correction.KeepId} moves");
            }
        }

        [Test]
        public void SiteZValuesMatchTheBuilderGroundSamples()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundKeepLayouts.FindSite(134).Z, Is.EqualTo(10976), "Leirvik keep 134");
                Assert.That(Albion201.Z, Is.EqualTo(8288), "Killaloe Albion portal keep 201");
                Assert.That(Hibernia203.Z, Is.EqualTo(8288), "Killaloe Hibernia portal keep 203");
                Assert.That(BattlegroundKeepLayouts.PortalSites.First(site => site.KeepId == 202).Z, Is.EqualTo(8288), "Midgard 202 is level and untouched");
                Assert.That(BattlegroundKeepLayouts.FindSite(138).Z, Is.EqualTo(8768), "Killaloe central keep 138 is untouched");
                Assert.That(BattlegroundKeepLayouts.FindSite(143).Z, Is.EqualTo(4071), "Thidranki 143 stays relaxed and untouched");
            });
        }
    }
}
