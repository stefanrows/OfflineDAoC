using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_BattlegroundCampaignDirector
    {
        [Test]
        public void SponsorIsRestoredOnlyForARealUnexpiredGuild()
        {
            DateTime now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
            DateTime future = now.AddMinutes(30);
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundCampaignManager.ShouldRestoreSponsor("guild-id", true, future, now), Is.True);
                Assert.That(BattlegroundCampaignManager.ShouldRestoreSponsor(null, true, future, now), Is.False, "No guild id");
                Assert.That(BattlegroundCampaignManager.ShouldRestoreSponsor(string.Empty, true, future, now), Is.False, "Empty guild id");
                Assert.That(BattlegroundCampaignManager.ShouldRestoreSponsor("guild-id", false, future, now), Is.False, "Not a real guild");
                Assert.That(BattlegroundCampaignManager.ShouldRestoreSponsor("guild-id", true, now, now), Is.False, "Expiry equal to now");
                Assert.That(BattlegroundCampaignManager.ShouldRestoreSponsor("guild-id", true, now.AddMinutes(-1), now), Is.False, "Expired");
            });
        }

        [Test]
        public void CampTimersStartAtDistinctOffsetsForEveryDefinition()
        {
            var offsets = Enumerable.Range(0, 10).Select(BattlegroundCampaignManager.StaggerMs).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(offsets[0], Is.EqualTo(0));
                Assert.That(offsets.Distinct().Count(), Is.EqualTo(10));
                Assert.That(offsets.Zip(offsets.Skip(1), (a, b) => b - a).All(gap => gap >= 1_000), Is.True,
                    "Neighbouring campaigns stay more than one tick apart");
            });
        }

        [Test]
        public void NearestCampIsFoundInTwoDimensionsAndEmptyListIsMinusOne()
        {
            var camps = new List<Point3D> { new(0, 0, 9_000), new(4_000, 0, 0), new(0, 3_000, 0) };
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundCampaignManager.NearestIndex(camps, 3_500, 100), Is.EqualTo(1));
                Assert.That(BattlegroundCampaignManager.NearestIndex(camps, 10, 2_900), Is.EqualTo(2));
                Assert.That(BattlegroundCampaignManager.NearestIndex(camps, 10, 10), Is.EqualTo(0), "Z is not part of the distance");
                Assert.That(BattlegroundCampaignManager.NearestIndex(Array.Empty<Point3D>(), 0, 0), Is.EqualTo(-1));
            });
        }

        [Test]
        public void PatrolNeverStartsAtTheCampNearestItsParticipant()
        {
            Assert.That(BattlegroundCampaignManager.PatrolOriginIndex(0, 1, 7), Is.EqualTo(0));
            Assert.That(BattlegroundCampaignManager.PatrolOriginIndex(0, 0, 7), Is.EqualTo(0));
            for (int count = 2; count <= 4; count++)
                for (int nearest = 0; nearest < count; nearest++)
                    for (int rotation = -3; rotation <= 20; rotation++)
                    {
                        int origin = BattlegroundCampaignManager.PatrolOriginIndex(nearest, count, rotation);
                        Assert.That(origin, Is.InRange(0, count - 1));
                        Assert.That(origin, Is.Not.EqualTo(nearest), $"count={count} nearest={nearest} rotation={rotation}");
                    }
        }

        [Test]
        public void MurdaigeanNativeKeepRowIsAnOrdinaryBattlegroundKeep()
        {
            DbKeep row = BattlegroundNativeKeepData.MurdaigeanKeepRow();
            Assert.Multiple(() =>
            {
                Assert.That(row.KeepID, Is.EqualTo(BattlegroundNativeKeepData.MurdaigeanKeepId));
                Assert.That(BattlegroundNativeKeepData.MurdaigeanKeepId, Is.EqualTo(139));
                Assert.That(row.Region, Is.EqualTo((ushort)251));
                Assert.That(row.BaseLevel, Is.LessThan(100), "Not a portal keep");
                Assert.That(row.Name, Is.EqualTo("Murdaigean Keep"));
                Assert.That(row.X, Is.EqualTo(33280));
                Assert.That(row.Y, Is.EqualTo(38272));
                Assert.That(row.CreateInfo, Is.EqualTo("offline-native-bg:251:pending"));
            });
        }
    }
}
