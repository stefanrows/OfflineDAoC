using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    // open BG (spec 2026-10-11 §3): Thidranki 252 is a catalog definition of kind Open. The level brackets never
    // route anyone into it; only FindOpen (the porter's [open BG] line) does.
    [TestFixture]
    public class UT_OpenBattlegroundCatalog
    {
        [TestCase(1)]
        [TestCase(20)]
        [TestCase(24)]
        [TestCase(45)]
        [TestCase(50)]
        public void ForLevelNeverReturnsTheOpenDefinition(int level)
        {
            BattlegroundDefinition definition = BattlegroundCampaignCatalog.ForLevel(level);
            if (definition == null) return;
            Assert.Multiple(() =>
            {
                Assert.That(definition.RegionId, Is.Not.EqualTo(252), "level " + level);
                Assert.That(definition.Kind, Is.EqualTo(BattlegroundDefinitionKind.Bracket), "level " + level);
            });
        }

        [Test]
        public void ForLevelKeepsTheTenBrackets()
        {
            // Stefan's brackets must route exactly as before; level 50 has no bracket.
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundCampaignCatalog.ForLevel(1).RegionId, Is.EqualTo(234));
                Assert.That(BattlegroundCampaignCatalog.ForLevel(22).RegionId, Is.EqualTo(238), "Thidranki bracket stays on 238");
                Assert.That(BattlegroundCampaignCatalog.ForLevel(49).RegionId, Is.EqualTo(165));
                Assert.That(BattlegroundCampaignCatalog.ForLevel(50), Is.Null, "a level-50 player has no bracket");
                Assert.That(BattlegroundCampaignCatalog.Definitions.Count(x => x.Kind == BattlegroundDefinitionKind.Bracket), Is.EqualTo(10));
            });
        }

        [Test]
        public void FindOpenReturnsThidranki252()
        {
            BattlegroundDefinition open = BattlegroundCampaignCatalog.FindOpen();
            Assert.Multiple(() =>
            {
                Assert.That(open, Is.Not.Null);
                Assert.That(open.RegionId, Is.EqualTo(252));
                Assert.That(open.ZoneId, Is.EqualTo(252));
                Assert.That(open.MinLevel, Is.EqualTo(1));
                Assert.That(open.MaxLevel, Is.EqualTo(50));
                Assert.That(open.MaxRealmLevel, Is.EqualTo(0), "no realm-rank limit");
                Assert.That(open.Kind, Is.EqualTo(BattlegroundDefinitionKind.Open));
                Assert.That(BattlegroundCampaignCatalog.Definitions.Count(x => x.Kind == BattlegroundDefinitionKind.Open), Is.EqualTo(1));
            });
        }

        [Test]
        public void Find252IsKindOpen()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundCampaignCatalog.Find(252)?.Kind, Is.EqualTo(BattlegroundDefinitionKind.Open));
                Assert.That(BattlegroundCampaignCatalog.Find(238)?.Kind, Is.EqualTo(BattlegroundDefinitionKind.Bracket));
                Assert.That(new BattlegroundDefinition(1, 1, "x", 1, 2, 0).Kind, Is.EqualTo(BattlegroundDefinitionKind.Bracket), "default kind");
            });
        }

        [Test]
        public void RegisterCapsMakes252LevelOneToFifty()
        {
            // The save's legacy row says Thidranki 20-24 below RR2L0. KeepManager.GetBattleground reads this list,
            // so release, zone-point and keep rules see the open BG's 1-50 cap; the row itself is never saved.
            var caps = new List<DbBattleground>
            {
                new() { RegionID = 252, MinLevel = 20, MaxLevel = 24, MaxRealmLevel = 10 },
                new() { RegionID = 253, MinLevel = 15, MaxLevel = 19, MaxRealmLevel = 2 },
            };
            BattlegroundCampaignCatalog.RegisterCaps(caps);
            DbBattleground thidranki = caps.Single(x => x.RegionID == 252);
            DbBattleground abermenai = caps.Single(x => x.RegionID == 253);
            Assert.Multiple(() =>
            {
                Assert.That(thidranki.MinLevel, Is.EqualTo(1));
                Assert.That(thidranki.MaxLevel, Is.EqualTo(50));
                Assert.That(thidranki.MaxRealmLevel, Is.EqualTo(0));
                Assert.That(abermenai.MinLevel, Is.EqualTo(15), "other legacy rows keep their bracket");
                Assert.That(abermenai.MaxLevel, Is.EqualTo(19));
            });
        }
    }
}
