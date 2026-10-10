using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_BattlegroundPortalKeepLayouts
    {
        private static readonly ushort[] ServerBuiltRegions = { 234, 235, 236, 237, 238, 240, 241, 242 };
        private static readonly int[] CentralAndNativeKeepIds = { 1, 2, 3, 41, 42, 43, 132, 134, 138, 139, 140, 141, 142, 143, 144 };

        private static KeepSite Hibernian900 => BattlegroundKeepLayouts.PortalSites.First(site => site.KeepId == 900);

        [Test]
        public void EveryServerBuiltRegionHasThreePortalKeepsAndNoOthers()
        {
            var sites = BattlegroundKeepLayouts.PortalSites;
            Assert.Multiple(() =>
            {
                foreach (ushort region in ServerBuiltRegions)
                    Assert.That(sites.Count(site => site.Region == region), Is.EqualTo(3), $"region {region}");
                Assert.That(sites.Count, Is.EqualTo(ServerBuiltRegions.Length * 3));
                Assert.That(sites.Select(site => site.KeepId).Distinct().Count(), Is.EqualTo(sites.Count), "keep ids are unique");
                Assert.That(sites.Select(site => site.KeepId).Intersect(CentralAndNativeKeepIds), Is.Empty,
                    "no portal keep reuses a central, native or Murdaigean id");
            });
        }

        [Test]
        public void EverySiteUsesTheRingTemplateAndAnExistingRow()
        {
            foreach (KeepSite site in BattlegroundKeepLayouts.PortalSites)
            {
                Assert.That(site.Template, Is.EqualTo(BattlegroundKeepLayouts.PortalTemplate), $"keep {site.KeepId}");
                Assert.That(site.ExistingRow, Is.True, $"keep {site.KeepId} is a saved row, never created here");
                Assert.That(site.Name, Is.Not.Empty, $"keep {site.KeepId}");
            }
        }

        [Test]
        public void NativePortalKeepsAreCathalAndMurdaigeanOnly()
        {
            var native = BattlegroundKeepLayouts.PortalNativeKeeps.Select(keep => (keep.Region, keep.KeepId))
                .OrderBy(keep => keep.Region).ThenBy(keep => keep.KeepId).ToList();
            Assert.That(native, Is.EqualTo(new (ushort Region, int KeepId)[]
            {
                (165, 1), (165, 2), (165, 3), (251, 41), (251, 42), (251, 43),
            }));
            var built = BattlegroundKeepLayouts.PortalSites.Select(site => (site.Region, site.KeepId)).ToList();
            Assert.That(built.Intersect(native), Is.Empty, "a native portal keep is never server-built");
        }

        [Test]
        public void RingTemplateIsTheClosedEightComponentLayout()
        {
            var ring = BattlegroundKeepLayouts.Template(BattlegroundKeepLayouts.PortalTemplate)
                .Select(spec => (spec.Id, spec.Skin, X: BattlegroundKeepLayouts.SignedOffset(spec.X),
                    Y: BattlegroundKeepLayouts.SignedOffset(spec.Y), spec.Heading)).ToList();
            var expected = new (int Id, int Skin, int X, int Y, int Heading)[]
            {
                (0, 0, -2, -2, 0),
                (1, 9, -2, -1, 1),
                (2, 9, -2, 2, 1),
                (3, 9, -3, 3, 0),
                (4, 9, -1, 3, 0),
                (5, 9, 0, 3, 0),
                (6, 9, 3, 1, 1),
                (7, 9, 3, 2, 1),
            };
            Assert.Multiple(() =>
            {
                Assert.That(ring, Is.EqualTo(expected));
                Assert.That(ring, Has.Count.EqualTo(8));
                Assert.That(ring.Count(part => part.Skin == 0), Is.EqualTo(1), "one gate, the only passage");
                Assert.That(ring.Count(part => part.Skin == 4), Is.EqualTo(0), "no corner tower: it cannot sit inside the clearance rules");
                Assert.That(ring.Min(part => Math.Max(Math.Abs(part.X), Math.Abs(part.Y))), Is.GreaterThanOrEqualTo(2),
                    "no component origin within one cell of the landing centre");
            });
        }

        [Test]
        public void MeasuredClearancesApplyOnlyToTheMeasuredRing()
        {
            // Recorded from the raster footprints of exactly this template (offline scratch tool, not in the repo).
            // The measurements are valid only while the template matches the key below; re-measure after any change.
            const string MeasuredKey = "0,0,-2,-2,0;1,9,-2,-1,1;2,9,-2,2,1;3,9,-3,3,0;4,9,-1,3,0;5,9,0,3,0;6,9,3,1,1;7,9,3,2,1";
            var recorded = new (string Name, int Clearance)[]
            {
                ("Siegemaster Wyllam (keep 388)", 232),
                ("Master Eldritch B (keep 897)", 264),
                ("Master Eldritch A (keep 897)", 361),
                ("Siegemaster Camey (keep 897)", 392),
                ("Siegemaster Ordrand (keep 641)", 414),
                ("Siegemaster Sigfreid (keep 641)", 447),
                ("Siegemaster Salendar (keep 385)", 454),
                ("Scryer Idora (keep 637)", 499),
                ("Champion (keep 897)", 791),
            };
            string key = string.Join(";", BattlegroundKeepLayouts.Template(BattlegroundKeepLayouts.PortalTemplate).Select(spec =>
                $"{spec.Id},{spec.Skin},{BattlegroundKeepLayouts.SignedOffset(spec.X)},{BattlegroundKeepLayouts.SignedOffset(spec.Y)},{spec.Heading}"));
            Assert.Multiple(() =>
            {
                Assert.That(key, Is.EqualTo(MeasuredKey), "the clearances below were measured on this layout");
                foreach ((string name, int clearance) in recorded)
                    Assert.That(clearance, Is.GreaterThanOrEqualTo(200), name);
            });
        }

        [Test]
        public void EmptyRowsWithOldSkinsAreBuilt()
        {
            KeepSite site = Hibernian900;
            Assert.Multiple(() =>
            {
                foreach (byte skinType in new byte[] { 0, 1 })
                {
                    PortalKeepDecision decision = BattlegroundPortalKeepPlanner.Decide(site, Row(site, skinType: skinType));
                    Assert.That(decision.Build, Is.True, $"skin type {skinType}");
                    Assert.That(decision.Reason, Is.Empty);
                }
            });
        }

        [Test]
        public void EveryOtherRowIsSkippedWithItsReason()
        {
            KeepSite site = Hibernian900;
            Assert.Multiple(() =>
            {
                Assert.That(Reason(site, Row(site) with { RowExists = false }), Is.EqualTo("row_missing"));
                Assert.That(Reason(site, Row(site) with { Region = 240 }), Is.EqualTo("id_conflict"));
                Assert.That(Reason(site, Row(site) with { BaseLevel = 50 }), Is.EqualTo("not_portal_row"));
                Assert.That(Reason(site, Row(site, skinType: 2)), Is.EqualTo("skin_type_unsupported"), "new skins");
                Assert.That(Reason(site, Row(site, skinType: 99)), Is.EqualTo("skin_type_unsupported"), "relic");
                Assert.That(Reason(site, Row(site) with { X = site.X + 1 }), Is.EqualTo("site_mismatch"));
                Assert.That(Reason(site, Row(site) with { Y = site.Y - 1 }), Is.EqualTo("site_mismatch"));
                Assert.That(Reason(site, Row(site) with { Z = site.Z + 10 }), Is.EqualTo("site_mismatch"));
                Assert.That(Reason(site, Row(site) with { Heading = (site.Heading + 1) % 360 }), Is.EqualTo("site_mismatch"));
                Assert.That(Reason(site, Row(site) with { ComponentCount = 1 }), Is.EqualTo("has_components"));
                Assert.That(Reason(site, Row(site) with { ComponentCount = 8 }), Is.EqualTo("has_components"));
                Assert.That(Reason(new KeepSite(241, 900, "Hibernia Portal Keep", site.X, site.Y, site.Z, site.Heading, "NoSuchTemplate", true),
                    Row(site)), Is.EqualTo("template_missing"));
                Assert.That(BattlegroundPortalKeepPlanner.Decide(null, Row(site)).Reason, Is.EqualTo("site_missing"));
            });
        }

        [Test]
        public void SharedJsonMatchesThePortalTablesAndTheRing()
        {
            JObject json = LoadSharedJson();
            Assert.Multiple(() =>
            {
                Assert.That(SiteRows((JArray)json["portalKeepSites"]), Is.EqualTo(SiteRows(BattlegroundKeepLayouts.PortalSites)));
                var jsonNative = ((JArray)json["portalNativeKeeps"]).Select(keep => ((int)keep["region"], (int)keep["keepId"])).ToList();
                var csharpNative = BattlegroundKeepLayouts.PortalNativeKeeps.Select(keep => ((int)keep.Region, keep.KeepId)).ToList();
                Assert.That(jsonNative, Is.EqualTo(csharpNative));
                var jsonRing = ((JArray)json["templates"][BattlegroundKeepLayouts.PortalTemplate]).Select(component => (
                    Id: (int)component["id"], Skin: (int)component["skin"], X: (int)component["x"],
                    Y: (int)component["y"], Heading: (int)component["heading"])).ToList();
                var csharpRing = BattlegroundKeepLayouts.Template(BattlegroundKeepLayouts.PortalTemplate).Select(spec => (
                    spec.Id, spec.Skin, spec.X, spec.Y, spec.Heading)).ToList();
                Assert.That(jsonRing, Is.EqualTo(csharpRing));
            });
        }

        private static PortalKeepRowState Row(KeepSite site, byte skinType = 0) =>
            new(true, site.Region, 100, skinType, site.X, site.Y, site.Z, site.Heading, 0);

        private static string Reason(KeepSite site, PortalKeepRowState row) => BattlegroundPortalKeepPlanner.Decide(site, row).Reason;

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
