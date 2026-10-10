using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DOL.GS.Keeps;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_BattlegroundKeepLayouts
    {
        private static readonly ushort[] CampaignRegions = { 234, 235, 236, 237, 238, 240, 241, 242 };
        private static readonly string[] Templates = { "TBG40_44", "ClaimBG5_9", "CaerClaret", "CKBG15_19", "CKBG20_24", "CKBG30_34", "CKBG35_39", "CKBG40_44" };
        private static readonly int[] ReservedKeepIds = { 132, 134, 138, 139 };

        [Test]
        public void EveryCampaignRegionHasExactlyOneKeepSiteAndNoneIsBlocked()
        {
            var placed = BattlegroundKeepLayouts.Sites.Select(site => site.Region).ToList();
            Assert.Multiple(() =>
            {
                foreach (ushort region in CampaignRegions)
                    Assert.That(placed.Count(value => value == region), Is.EqualTo(1), $"region {region}");
                Assert.That(placed.Count, Is.EqualTo(CampaignRegions.Length));
                Assert.That(BattlegroundKeepLayouts.BlockedSites, Is.Empty, "Thidranki has a relaxed site (variance 183, max 200)");
            });
        }

        [Test]
        public void EveryTemplateUsesOnlyPartsBelowSkinTwenty()
        {
            foreach (string template in Templates)
            {
                var skins = BattlegroundKeepLayouts.Template(template).Select(spec => spec.Skin).ToList();
                Assert.That(skins, Is.Not.Empty, template);
                Assert.That(skins, Has.All.InRange(0, 19), template);
            }
        }

        [Test]
        public void ComponentIdsAreUniqueAndStartAtZeroInEveryTemplate()
        {
            foreach (string template in Templates)
            {
                var ids = BattlegroundKeepLayouts.Template(template).Select(spec => spec.Id).ToList();
                Assert.Multiple(() =>
                {
                    Assert.That(ids.First(), Is.EqualTo(0), template);
                    Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count), $"{template} ids unique");
                    Assert.That(ids.Max(), Is.EqualTo(ids.Count - 1), $"{template} ids contiguous");
                });
            }
        }

        [Test]
        public void TemplateComponentCountsMatchTheFastcreateLayouts()
        {
            var expected = new Dictionary<string, int>
            {
                ["TBG40_44"] = 1, ["ClaimBG5_9"] = 16, ["CaerClaret"] = 11, ["CKBG15_19"] = 20,
                ["CKBG20_24"] = 22, ["CKBG30_34"] = 21, ["CKBG35_39"] = 22, ["CKBG40_44"] = 25,
            };
            foreach (var pair in expected)
                Assert.That(BattlegroundKeepLayouts.Template(pair.Key).Count, Is.EqualTo(pair.Value), pair.Key);
            Assert.That(BattlegroundKeepLayouts.Template("NoSuchTemplate"), Is.Empty);
        }

        [Test]
        public void FullKeepTemplatesHaveAGateAndTheSingleTowerHasATower()
        {
            Assert.Multiple(() =>
            {
                foreach (string template in Templates.Where(name => name != "ClaimBG5_9" && name != "TBG40_44"))
                {
                    var skins = BattlegroundKeepLayouts.Template(template).Select(spec => spec.Skin).ToList();
                    Assert.That(skins, Does.Contain(0).Or.Contain(BattlegroundKeepLayouts.TowerSkin), template);
                }
                var tower = BattlegroundKeepLayouts.Template("TBG40_44").Select(spec => spec.Skin).ToList();
                Assert.That(BattlegroundKeepLayouts.IsTower(tower), Is.True, "TBG40_44 is one tower");
                Assert.That(BattlegroundKeepLayouts.IsTower(new[] { 0 }), Is.False, "A gate alone is not a tower");
            });
        }

        // Deviation from the owner plan: Lion's Den (ClaimBG5_9) has no gate or tower skin in the
        // native fastcreate layout, and no KeepPosition door rows for its skins. It therefore gets no
        // blocking door and no gated lord. This test pins that fact so a later change is deliberate.
        [Test]
        public void LionsDenTemplateHasNoDoorSkinSoItsLordStaysUnspawned()
        {
            var skins = BattlegroundKeepLayouts.Template("ClaimBG5_9").Select(spec => spec.Skin).ToList();
            Assert.That(skins, Has.None.EqualTo(0));
            Assert.That(skins, Has.None.EqualTo(BattlegroundKeepLayouts.TowerSkin));
        }

        [Test]
        public void NewKeepIdsAreUniqueBelowTwoFiftySixAndAvoidExistingRows()
        {
            var newIds = BattlegroundKeepLayouts.Sites.Where(site => !site.ExistingRow).Select(site => site.KeepId).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(newIds.Count, Is.EqualTo(5));
                Assert.That(newIds.Distinct().Count(), Is.EqualTo(newIds.Count));
                Assert.That(newIds, Has.All.LessThan(256));
                foreach (int reserved in ReservedKeepIds)
                    Assert.That(newIds, Does.Not.Contain(reserved));
            });
        }

        [Test]
        public void ExistingSitesKeepTheirStoredKeepRows()
        {
            var existing = BattlegroundKeepLayouts.Sites.Where(site => site.ExistingRow).ToDictionary(site => site.KeepId);
            Assert.Multiple(() =>
            {
                Assert.That(existing.Keys.OrderBy(id => id).ToArray(), Is.EqualTo(new[] { 132, 134, 138 }));
                Assert.That(existing[138].Region, Is.EqualTo((ushort)237));
                Assert.That(existing[132].Region, Is.EqualTo((ushort)241));
                Assert.That(existing[134].Region, Is.EqualTo((ushort)242));
            });
        }

        [Test]
        public void PlacedNewSitesHaveSearchedCoordinatesNotPlaceholders()
        {
            foreach (KeepSite site in BattlegroundKeepLayouts.Sites.Where(site => !site.ExistingRow))
            {
                Assert.That(site.X == 0 && site.Y == 0, Is.False, $"keep {site.KeepId} has a placeholder position");
                Assert.That(site.Heading, Is.EqualTo(0), $"keep {site.KeepId} is a new keep");
            }
        }

        [Test]
        public void SignedOffsetsReadRawComponentBytes()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundKeepLayouts.SignedOffset(253), Is.EqualTo(-3));
                Assert.That(BattlegroundKeepLayouts.SignedOffset(255), Is.EqualTo(-1));
                Assert.That(BattlegroundKeepLayouts.SignedOffset(7), Is.EqualTo(7));
                Assert.That(BattlegroundKeepLayouts.SignedOffset(128), Is.EqualTo(-128));
            });
        }

        [Test]
        public void SearchRadiusScalesWithTheFootprintAndStaysInsideTheKeepArea()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundKeepLayouts.SearchRadius("TBG40_44"), Is.EqualTo(4 * 148 + 200));
                Assert.That(BattlegroundKeepLayouts.SearchRadius("CKBG40_44"), Is.EqualTo(13 * 148 + 200));
                foreach (string template in Templates)
                    Assert.That(BattlegroundKeepLayouts.SearchRadius(template), Is.LessThan(3000), $"{template} inside KeepArea radius");
            });
        }

        [Test]
        public void PlausibleOffsetsRejectTheCorruptNativeRows()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GameKeepComponent.IsPlausibleOffset(155181, -93037), Is.False);
                Assert.That(GameKeepComponent.IsPlausibleOffset(-433701, 574589), Is.False);
                Assert.That(GameKeepComponent.IsPlausibleOffset(4097, 0), Is.False);
                Assert.That(GameKeepComponent.IsPlausibleOffset(0, -4097), Is.False);
                Assert.That(GameKeepComponent.IsPlausibleOffset(4096, -4096), Is.True);
                Assert.That(GameKeepComponent.IsPlausibleOffset(593, -478), Is.True);
            });
        }

        [Test]
        public void SharedJsonMatchesTheCSharpSiteAndTemplateTables()
        {
            JObject json = LoadSharedJson();
            Assert.That(SiteRows((JArray)json["sites"]), Is.EqualTo(SiteRows(BattlegroundKeepLayouts.Sites)));
            Assert.That(SiteRows((JArray)json["blockedSites"]), Is.EqualTo(SiteRows(BattlegroundKeepLayouts.BlockedSites)));

            foreach (string template in Templates)
            {
                var jsonComponents = ((JArray)json["templates"][template]).Select(component => (
                    Id: (int)component["id"], Skin: (int)component["skin"], X: (int)component["x"],
                    Y: (int)component["y"], Heading: (int)component["heading"])).ToList();
                var csharpComponents = BattlegroundKeepLayouts.Template(template).Select(spec => (
                    spec.Id, spec.Skin, spec.X, spec.Y, spec.Heading)).ToList();
                Assert.That(jsonComponents, Is.EqualTo(csharpComponents), template);
            }
        }

        [Test]
        public void OnlyTheCorruptDoorRowsFailThePlausibilityCheck()
        {
            JObject json = LoadSharedJson();
            var doors = ((JArray)json["doorOffsets"]).ToList();
            var implausible = doors
                .Where(row => !GameKeepComponent.IsPlausibleOffset((int)row["xOff"], (int)row["yOff"]))
                .Select(row => (string)row["templateId"])
                .ToList();
            Assert.Multiple(() =>
            {
                Assert.That(doors, Has.Count.EqualTo(8));
                Assert.That(doors.Count(row => (int)row["skin"] == 10), Is.EqualTo(4));
                Assert.That(implausible, Has.Count.EqualTo(2));
                Assert.That(implausible, Has.All.Matches<string>(id => !string.IsNullOrEmpty(id)));
            });
        }

        private static List<(int Region, int KeepId, string Name, int X, int Y, int Z, int Heading, string Template, bool ExistingRow)> SiteRows(JArray rows) =>
            rows.Select(site => ((int)site["region"], (int)site["keepId"], (string)site["name"],
                (int)site["x"], (int)site["y"], (int)site["z"], (int)site["heading"],
                (string)site["template"], (bool)site["existingRow"])).ToList();

        private static List<(int Region, int KeepId, string Name, int X, int Y, int Z, int Heading, string Template, bool ExistingRow)> SiteRows(IEnumerable<KeepSite> rows) =>
            rows.Select(site => ((int)site.Region, site.KeepId, site.Name, site.X, site.Y, site.Z, site.Heading,
                site.Template, site.ExistingRow)).ToList();

        private static JObject LoadSharedJson() =>
            JObject.Parse(File.ReadAllText(FindRepoFile(Path.Combine("tools", "dev", "battleground-keeps.json"))));

        private static string FindRepoFile(string relativePath)
        {
            for (DirectoryInfo directory = new(TestContext.CurrentContext.TestDirectory); directory != null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate)) return candidate;
            }
            Assert.Fail($"Repository file not found from the test directory: {relativePath}");
            return null;
        }
    }
}
