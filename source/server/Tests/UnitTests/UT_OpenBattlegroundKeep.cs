using System;
using System.Linq;
using DOL.Database;
using DOL.GS.Keeps;
using NUnit.Framework;
using Native = DOL.GS.BattlegroundNativeKeepData;
using Wall = DOL.GS.BattlegroundKeepWallGuards;

namespace DOL.GS.Tests
{
    /// <summary>Thidranki Faste (region 252, keep 11): the open battleground's central keep is built like Murdaigean's native keep.</summary>
    [TestFixture]
    public class UT_OpenBattlegroundKeep
    {
        private static DbKeep SavedThidrankiRow() => new()
        {
            KeepID = 11, Name = "Thidranki Faste", Region = 252, X = 33089, Y = 38271, Z = 3720,
            Level = 1, BaseLevel = 26, CreateInfo = "Atlas BG",
        };

        [Test]
        public void NativeKeepDataCoversThidranki252()
        {
            NativeKeepFixture keep = Native.FindNativeKeep(252);
            Assert.That(keep, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(keep.KeepId, Is.EqualTo(11), "the saved row id stays");
                Assert.That(keep.Name, Is.EqualTo("Thidranki Faste"), "the saved name stays");
                Assert.That((keep.X, keep.Y, keep.Z), Is.EqualTo((33280, 38272, 3720)), "the client fixture nfrontkeep2.nif, not the row's 33089");
                Assert.That(keep.BaseLevel, Is.LessThan(100), "a central keep, not a portal keep");
                Assert.That(keep.SavedGarrison, Is.True, "the save carries its own lord and guards");
                Assert.That(Native.NativeTag(252), Is.EqualTo("offline-native-bg:252"));
            });

            // Murdaigean keeps its own historical path and values.
            NativeKeepFixture murdaigean = Native.FindNativeKeep(251);
            Assert.Multiple(() =>
            {
                Assert.That(murdaigean.KeepId, Is.EqualTo(Native.MurdaigeanKeepId));
                Assert.That(murdaigean.SavedGarrison, Is.False);
                Assert.That(Native.FindNativeKeep(238), Is.Null, "the rebuilt Thidranki bracket (238) is not a native keep");
            });
        }

        [Test]
        public void AdoptingTheSavedRowKeepsItsIdAndNameAndMovesItOntoTheFixture()
        {
            NativeKeepFixture fixture = Native.FindNativeKeep(252);
            DbKeep row = SavedThidrankiRow();
            Assert.That(Native.DecideAdoption(fixture, row), Is.EqualTo(Native.NativeKeepAdoption.Adopt));

            Native.Adopt(fixture, row);
            Assert.Multiple(() =>
            {
                Assert.That(row.KeepID, Is.EqualTo(11));
                Assert.That(row.Name, Is.EqualTo("Thidranki Faste"));
                Assert.That(row.BaseLevel, Is.EqualTo(26), "the saved base level stays");
                Assert.That((row.X, row.Y, row.Z), Is.EqualTo((33280, 38272, 3720)));
                Assert.That(row.CreateInfo, Is.EqualTo("offline-native-bg:252:pending"), "pending: the first keep start heals and closes its doors");
                Assert.That(Native.DecideAdoption(fixture, row), Is.EqualTo(Native.NativeKeepAdoption.Ready), "pending is already adopted");
            });

            row.CreateInfo = "offline-native-bg:252";
            Assert.That(Native.DecideAdoption(fixture, row), Is.EqualTo(Native.NativeKeepAdoption.Ready));
        }

        [Test]
        public void AdoptionRefusesAForeignOrPortalRowAndCreatesAMissingOne()
        {
            NativeKeepFixture fixture = Native.FindNativeKeep(252);
            DbKeep foreign = SavedThidrankiRow();
            foreign.Region = 251;
            DbKeep portal = SavedThidrankiRow();
            portal.BaseLevel = 255;
            DbKeep otherTag = SavedThidrankiRow();
            otherTag.CreateInfo = "offline-native-bg:2520";
            Assert.Multiple(() =>
            {
                Assert.That(Native.DecideAdoption(fixture, null), Is.EqualTo(Native.NativeKeepAdoption.Create));
                Assert.That(Native.DecideAdoption(fixture, foreign), Is.EqualTo(Native.NativeKeepAdoption.IdConflict));
                Assert.That(Native.DecideAdoption(fixture, portal), Is.EqualTo(Native.NativeKeepAdoption.IdConflict));
                Assert.That(Native.DecideAdoption(fixture, otherTag), Is.EqualTo(Native.NativeKeepAdoption.Adopt));
            });
        }

        [Test]
        public void SavedLordIsCheckedOnHisOwnFloorOnlyInThidranki()
        {
            NativeKeepFixture thidranki = Native.FindNativeKeep(252);
            NativeKeepFixture murdaigean = Native.FindNativeKeep(251);
            Assert.Multiple(() =>
            {
                Assert.That(Native.GarrisonFloorReach(thidranki, 11, lord: true), Is.EqualTo(Native.LordFloorReach));
                Assert.That(Native.LordFloorReach, Is.LessThan(Native.GarrisonFloorSearch));
                Assert.That(Native.GarrisonFloorReach(thidranki, 11, lord: false), Is.EqualTo(Native.GarrisonFloorSearch), "retainers keep the 0.235.0 reach");
                Assert.That(Native.GarrisonFloorReach(murdaigean, 139, lord: true), Is.EqualTo(Native.GarrisonFloorSearch), "Murdaigean unchanged");
                Assert.That(Native.GarrisonFloorReach(null, 5, lord: true), Is.EqualTo(Native.GarrisonFloorSearch), "server-built keeps unchanged");
                Assert.That(Native.GarrisonFloorReach(thidranki, 12, lord: true), Is.EqualTo(Native.GarrisonFloorSearch), "only the fixture's own keep");
            });
        }

        [Test]
        public void WallGuardsSkipAKeepWithSavedOutsideGuards()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Wall.HasOwnWallGuards(18), Is.True);
                Assert.That(Wall.HasOwnWallGuards(Wall.OwnWallGuardThreshold), Is.True);
                Assert.That(Wall.HasOwnWallGuards(Wall.OwnWallGuardThreshold - 1), Is.False);
                Assert.That(Wall.HasOwnWallGuards(0), Is.False);
                Assert.That(Wall.OwnWallGuardThreshold, Is.EqualTo(10));
            });

            // Keep 11's saved roster: 8 fighters, 6 static casters, 3 static archers, the lord and a hastener.
            const string keep = "Thidranki Faste";
            var roster = Enumerable.Repeat((typeof(GuardFighter), "new mob"), 8)
                .Concat(Enumerable.Repeat((typeof(GuardStaticCaster), "new mob"), 6))
                .Concat(Enumerable.Repeat((typeof(GuardStaticArcher), "Hunter"), 3))
                .Append((typeof(GuardLord), "new mob"))
                .Append((typeof(FrontierHastener), "new mob"))
                .ToList();
            int outside = roster.Count(member => Wall.IsSavedOutsideGuard(member.Item1, member.Item2, string.Empty, keep));
            Assert.Multiple(() =>
            {
                Assert.That(outside, Is.EqualTo(17), "lord and hastener are not wall guards");
                Assert.That(Wall.HasOwnWallGuards(outside), Is.True, "no Molvik copy for keep 11");
                Assert.That(Wall.IsSavedOutsideGuard(typeof(WallPostArcher), "Thidranki Faste Wall Guard", string.Empty, keep), Is.False, "runtime posts are not saved guards");
                Assert.That(Wall.IsSavedOutsideGuard(typeof(GuardFighter), "Thidranki Faste Guard", string.Empty, keep), Is.False, "a garrison row is not a wall guard");
                Assert.That(Wall.IsSavedOutsideGuard(typeof(GuardHealer), "new mob", string.Empty, keep), Is.True);
                Assert.That(Wall.IsSavedOutsideGuard(typeof(GuardMerchant), "Merchant", string.Empty, keep), Is.False);
            });
        }

        [Test]
        public void PortalKeepsOf252AreNative()
        {
            var native = BattlegroundKeepLayouts.PortalNativeKeeps.Where(keep => keep.Region == 252).Select(keep => keep.KeepId).OrderBy(id => id).ToList();
            Assert.That(native, Is.EqualTo(new[] { 12, 13, 14 }));
            Assert.That(BattlegroundKeepLayouts.PortalSites.Any(site => site.Region == 252), Is.False, "a native portal keep is never server-built");
        }

        [Test]
        public void OpenBgKeepGuardsAreBuiltFromFrontierBaseLevel()
        {
            BattlegroundDefinition open = BattlegroundCampaignCatalog.Find(252);
            BattlegroundDefinition bracket = BattlegroundCampaignCatalog.Find(238);
            byte savedBase = SavedThidrankiRow().BaseLevel;
            double multiplier = 1.6;
            byte openBase = AbstractGameKeep.GuardKeepBaseLevel(savedBase, open.Kind);
            Assert.Multiple(() =>
            {
                Assert.That(open.Kind, Is.EqualTo(BattlegroundDefinitionKind.Open));
                Assert.That(openBase, Is.EqualTo(50), "guards use the frontier base level");
                Assert.That(AbstractGameKeep.GuardKeepBaseLevel(savedBase, bracket.Kind), Is.EqualTo(savedBase), "a bracket keep is unchanged");
                Assert.That(AbstractGameKeep.GuardKeepBaseLevel(savedBase, null), Is.EqualTo(savedBase), "a frontier keep is unchanged");
                // A GameKeep guard is base + 1, the lord base + (base / 10 + 1) * 2, at keep level 1 with the 1.6 multiplier.
                Assert.That(AbstractGameKeep.GuardLevelFor((byte)(openBase + 1), 1, multiplier), Is.EqualTo(52));
                Assert.That(AbstractGameKeep.GuardLevelFor((byte)(openBase + (openBase / 10 + 1) * 2), 1, multiplier), Is.EqualTo(63));
                Assert.That(AbstractGameKeep.GuardLevelFor((byte)(savedBase + 1), 1, multiplier), Is.EqualTo(28), "the saved base level gave the gray guards");
            });
        }
    }
}
