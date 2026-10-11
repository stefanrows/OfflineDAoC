using System.Reflection;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    // open BG (spec 2026-10-11 §2, §9): players and bots of level 1-50 enter through the porter's [open BG] line,
    // only while the open BG is switched on and zone 252 is ready; it never graduates anyone.
    [TestFixture]
    public class UT_OpenBattlegroundEntry
    {
        [TestCase(1)]
        [TestCase(12)]
        [TestCase(39)]
        [TestCase(50)]
        public void CanEnterOpenAcceptsLevelOneToFifty(int level)
        {
            Assert.That(OpenBattlegroundPolicy.CanEnterOpen(true, true, level), Is.True);
        }

        [Test]
        public void CanEnterOpenRefusesOutsideOneToFifty()
        {
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundPolicy.CanEnterOpen(true, true, 0), Is.False);
                Assert.That(OpenBattlegroundPolicy.CanEnterOpen(true, true, 51), Is.False);
            });
        }

        [Test]
        public void CanEnterOpenRefusesWhenDisabledOrNotReady()
        {
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundPolicy.CanEnterOpen(false, true, 30), Is.False, "switched off");
                Assert.That(OpenBattlegroundPolicy.CanEnterOpen(true, false, 30), Is.False, "zone 252 not ready");
                Assert.That(OpenBattlegroundPolicy.CanEnterOpen(false, false, 30), Is.False);
            });
        }

        [Test]
        public void UnavailableReasonNamesTheFirstMissingPiece()
        {
            // Review focus 2: enabled but no navmesh logs reason=no_navmesh (once per start).
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundPolicy.UnavailableReason(false, false, 0), Is.EqualTo("disabled"));
                Assert.That(OpenBattlegroundPolicy.UnavailableReason(true, false, 0), Is.EqualTo("no_navmesh"));
                Assert.That(OpenBattlegroundPolicy.UnavailableReason(true, true, 0), Is.EqualTo("no_landings"));
                Assert.That(OpenBattlegroundPolicy.UnavailableReason(true, true, 3), Is.Null);
            });
        }

        [Test]
        public void ZoneWithoutNavmeshIsNotReadyAndDoesNotThrow()
        {
            // No world is loaded in unit tests, so zone 252 has neither region nor mesh: the porter line stays hidden.
            BattlegroundDefinition open = BattlegroundCampaignCatalog.FindOpen();
            bool ready = true;
            Assert.DoesNotThrow(() => ready = BattlegroundCampaignPolicy.IsReady(open, out _));
            Assert.Multiple(() =>
            {
                Assert.That(ready, Is.False);
                Assert.That(OpenBattlegroundPolicy.CanEnterOpen(true, ready, 50), Is.False);
                Assert.That(OpenBattlegroundPolicy.HasNavmesh(open), Is.False);
            });
        }

        [Test]
        public void PorterShowsTheOpenLineOnlyWhenEnterable()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AllRealmsTeleporter.BuildTravelMenu(true), Does.Contain("[open BG]"));
                Assert.That(AllRealmsTeleporter.BuildTravelMenu(false), Does.Not.Contain("[open BG]"));
                Assert.That(AllRealmsTeleporter.BuildTravelMenu(), Is.EqualTo(AllRealmsTeleporter.BuildTravelMenu(false)), "default menu unchanged");
                Assert.That(AllRealmsTeleporter.BuildTravelMenu(false), Does.Contain("[Camelot] [Jordheim] [Tir na Nog]"));
            });
        }

        [Test]
        public void OpenDefinitionNeverGraduates()
        {
            BattlegroundDefinition open = BattlegroundCampaignCatalog.FindOpen();
            BattlegroundDefinition thidranki = BattlegroundCampaignCatalog.Find(238);
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundCampaignPolicy.ShouldGraduate(open, true, false), Is.False, "ineligible, still no graduation");
                Assert.That(BattlegroundCampaignPolicy.ShouldGraduate(open, false, false), Is.False, "campaign closed, still no tour end");
                Assert.That(BattlegroundCampaignPolicy.ShouldGraduate(thidranki, true, false), Is.True, "brackets graduate as before");
                Assert.That(BattlegroundCampaignPolicy.ShouldGraduate(thidranki, true, true), Is.False);
                Assert.That(BattlegroundCampaignPolicy.ShouldGraduate(null, true, false), Is.False);
                Assert.That(AutonomousBattlegroundParticipation.LeaveReasonFor(50, 120, true, false, false, open),
                    Is.Not.EqualTo(AutonomousBattlegroundParticipation.LeaveReason.Graduated), "a level-50 bot never graduates from it");
            });
        }

        [TestCase(nameof(OpenBattlegroundProperties.OPEN_BG_ENABLED), "open_bg_enabled", false)]
        [TestCase(nameof(OpenBattlegroundProperties.OPEN_BG_MAX_BOTS), "open_bg_max_bots", 72)]
        [TestCase(nameof(OpenBattlegroundProperties.OPEN_BG_GROUPS8), "open_bg_groups8", 1)]
        [TestCase(nameof(OpenBattlegroundProperties.OPEN_BG_GROUPS_SMALL), "open_bg_groups_small", 2)]
        [TestCase(nameof(OpenBattlegroundProperties.OPEN_BG_SOLOS), "open_bg_solos", 4)]
        [TestCase(nameof(OpenBattlegroundProperties.OPEN_BG_CYCLE_MIN_MINUTES), "open_bg_cycle_min_minutes", 15)]
        [TestCase(nameof(OpenBattlegroundProperties.OPEN_BG_CYCLE_MAX_MINUTES), "open_bg_cycle_max_minutes", 45)]
        [TestCase(nameof(OpenBattlegroundProperties.OPEN_BG_PLAYER_DRIFT), "open_bg_player_drift", true)]
        public void PropertiesDefaultToSpecValues(string field, string key, object expected)
        {
            FieldInfo info = typeof(OpenBattlegroundProperties).GetField(field, BindingFlags.Public | BindingFlags.Static);
            Assert.That(info, Is.Not.Null, field);
            var attribute = info.GetCustomAttribute<ServerPropertyAttribute>();
            Assert.Multiple(() =>
            {
                Assert.That(attribute, Is.Not.Null, field);
                Assert.That(attribute.Category, Is.EqualTo("pvp"));
                Assert.That(attribute.Key, Is.EqualTo(key));
                Assert.That(attribute.DefaultValue, Is.EqualTo(expected));
                Assert.That(info.GetValue(null), Is.EqualTo(expected), "the field starts at the same default");
            });
        }
    }
}
