using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>Bug 141: /ck and /battleground list show keep owners in the campaign battlegrounds.</summary>
    [TestFixture]
    public class UT_BattlegroundKeepOwnership
    {
        [Test]
        public void EveryCampaignRegionHasABattlegroundCapWithoutDatabaseRows()
        {
            // /ck passes its gate when KeepManager.GetBattleground(region) is non-null. Startup registers
            // these caps from the catalog, so campaign regions never depend on Battleground table rows.
            var caps = new List<DbBattleground>();
            BattlegroundCampaignCatalog.RegisterCaps(caps);
            foreach (BattlegroundDefinition definition in BattlegroundCampaignCatalog.Definitions)
                Assert.That(caps.Any(cap => cap.RegionID == definition.RegionId), Is.True, definition.Name);
        }

        [Test]
        public void KeepLineKeepsLegacyTextAndMarksOnlyCampaignLordDefeatedUnclaimedKeeps()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundKeepOwnership.Describe("Thidranki Keep", eRealm.Hibernia, "Stone Ward", false, true),
                    Is.EqualTo("Thidranki Keep: Hibernia (Stone Ward)"), "Guild shown");
                Assert.That(BattlegroundKeepOwnership.Describe("Thidranki Keep", eRealm.Hibernia, "Stone Ward", true, true),
                    Is.EqualTo("Thidranki Keep: Hibernia (Stone Ward)"), "Guild wins over lord defeated");
                Assert.That(BattlegroundKeepOwnership.Describe("Lion's Den Keep", eRealm.Albion, null, true, true),
                    Is.EqualTo("Lion's Den Keep: Albion - lord defeated, unclaimed"), "Campaign lord defeated");
                Assert.That(BattlegroundKeepOwnership.Describe("Lion's Den Keep", eRealm.Albion, null, false, true),
                    Is.EqualTo("Lion's Den Keep: Albion"), "Campaign unclaimed, lord alive");
                Assert.That(BattlegroundKeepOwnership.Describe("Old Keep", eRealm.Midgard, null, true, false),
                    Is.EqualTo("Old Keep: Midgard"), "Legacy battleground output is unchanged");
            });
        }

        [Test]
        public void OwnerIsGuildThenLordDefeatedThenUnclaimed()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundKeepOwnership.Owner("Stone Ward", true), Is.EqualTo("Stone Ward"));
                Assert.That(BattlegroundKeepOwnership.Owner(null, true), Is.EqualTo("lord defeated, unclaimed"));
                Assert.That(BattlegroundKeepOwnership.Owner(null, false), Is.EqualTo("unclaimed"));
                Assert.That(BattlegroundKeepOwnership.CentralLine("Thidranki Keep", null, true),
                    Is.EqualTo("Owner of Thidranki Keep: lord defeated, unclaimed."));
            });
        }
    }
}
