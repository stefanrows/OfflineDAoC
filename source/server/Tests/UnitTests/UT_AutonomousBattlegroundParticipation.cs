using System;
using System.Linq;
using DOL.GS.Scripts;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_AutonomousBattlegroundParticipation
    {
        private static readonly BattlegroundDefinition Thidranki = BattlegroundCampaignCatalog.Find(238);
        private static readonly BattlegroundDefinition Murdaigean = BattlegroundCampaignCatalog.Find(251);
        private static readonly BattlegroundDefinition ProvingGrounds = BattlegroundCampaignCatalog.Find(234);

        private static bool Candidate(int level, int realmLevel, bool rvrTour = true, ushort region = 1,
            bool playerLed = false, bool relic = false, BattlegroundDefinition definition = null) =>
            AutonomousBattlegroundParticipation.IsCandidate((byte)level, realmLevel, rvrTour, region, playerLed, relic,
                definition ?? Thidranki);

        [Test]
        public void ValidRvrTourBotOnAFrontierIsACandidateInEachHomeRegion()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Candidate(22, 3, region: 1), Is.True);
                Assert.That(Candidate(20, 0, region: 100), Is.True, "Bracket floor");
                Assert.That(Candidate(24, 14, region: 200), Is.True, "Bracket ceiling and last RR below the ceiling");
            });
        }

        [Test]
        public void OnlyHomeFrontierRegionsQualify()
        {
            foreach (ushort region in new ushort[] { 0, 2, 11, 165, 234, 238, 251 })
                Assert.That(Candidate(22, 3, region: region), Is.False, $"region {region}");
        }

        [Test]
        public void PlayerLedAndRelicCarriersAreNeverCandidates()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Candidate(22, 3, playerLed: true), Is.False, "Player-led group");
                Assert.That(Candidate(22, 3, relic: true), Is.False, "Relic carrier");
                Assert.That(Candidate(22, 3, rvrTour: false), Is.False, "Not on an RvR tour");
                Assert.That(Candidate(22, 3, definition: null), Is.False, "No bracket");
            });
        }

        [Test]
        public void LevelOutsideTheBracketIsNotACandidate()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Candidate(19, 0), Is.False, "Below the bracket");
                Assert.That(Candidate(25, 0), Is.False, "Above the bracket: graduated");
                Assert.That(Candidate(25, 0, definition: Murdaigean), Is.True, "The next bracket takes it");
            });
        }

        [Test]
        public void RealmRankCeilingGraduatesAndZeroMeansUnlimited()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Candidate(22, 14), Is.True, "One below the exclusive ceiling");
                Assert.That(Candidate(22, 15), Is.False, "At the exclusive ceiling");
                Assert.That(Candidate(1, 50, definition: ProvingGrounds), Is.True, "Proving Grounds has no ceiling");
            });
        }

        [Test]
        public void GraduationReasonsMatchTheBracketRules()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundParticipation.LeaveReasonFor(25, 0, true, false, false, Thidranki),
                    Is.EqualTo(AutonomousBattlegroundParticipation.LeaveReason.Graduated), "Level above the bracket");
                Assert.That(AutonomousBattlegroundParticipation.LeaveReasonFor(22, 15, true, false, false, Thidranki),
                    Is.EqualTo(AutonomousBattlegroundParticipation.LeaveReason.Graduated), "Realm Rank ceiling");
                Assert.That(AutonomousBattlegroundParticipation.LeaveReasonFor(22, 3, false, false, false, Thidranki),
                    Is.EqualTo(AutonomousBattlegroundParticipation.LeaveReason.TourEnded), "Tour ended");
                Assert.That(AutonomousBattlegroundParticipation.LeaveReasonFor(22, 3, true, true, false, Thidranki),
                    Is.EqualTo(AutonomousBattlegroundParticipation.LeaveReason.Unassigned), "Player-led");
                Assert.That(AutonomousBattlegroundParticipation.LeaveReasonFor(22, 3, true, false, false, Thidranki),
                    Is.Null, "Still eligible");
            });
        }

        [Test]
        public void DirectorCapSharesTheFortyActorPoolWithAutonomousParticipants()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundParticipation.DirectorCap(0), Is.EqualTo(24));
                Assert.That(AutonomousBattlegroundParticipation.DirectorCap(16), Is.EqualTo(24));
                Assert.That(AutonomousBattlegroundParticipation.DirectorCap(17), Is.EqualTo(23));
                Assert.That(AutonomousBattlegroundParticipation.DirectorCap(20), Is.EqualTo(20));
                Assert.That(AutonomousBattlegroundParticipation.DirectorCap(40), Is.EqualTo(0));
                Assert.That(AutonomousBattlegroundParticipation.DirectorCap(50), Is.EqualTo(0));
            });
        }

        [Test]
        public void EachMapAdmitsWholeGroupsWithinItsAutonomousLimit()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundParticipation.AdmitsGroup(1, 1, 23), Is.True, "Solo bot takes the last seat");
                Assert.That(AutonomousBattlegroundParticipation.AdmitsGroup(1, 1, 24), Is.False, "Map is full");
                Assert.That(AutonomousBattlegroundParticipation.AdmitsGroup(8, 8, 16), Is.True, "Group fills the map exactly");
                Assert.That(AutonomousBattlegroundParticipation.AdmitsGroup(3, 3, 22), Is.False, "Group would overflow");
                Assert.That(AutonomousBattlegroundParticipation.AdmitsGroup(3, 2, 0), Is.False, "One ineligible member blocks the whole group");
                Assert.That(AutonomousBattlegroundParticipation.AdmitsGroup(0, 0, 0), Is.False, "Empty group");
            });
        }

        [Test]
        public void LeaveReasonsUseTheLogVocabulary()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundParticipation.Describe(AutonomousBattlegroundParticipation.LeaveReason.Graduated), Is.EqualTo("graduated"));
                Assert.That(AutonomousBattlegroundParticipation.Describe(AutonomousBattlegroundParticipation.LeaveReason.TourEnded), Is.EqualTo("tour_ended"));
                Assert.That(AutonomousBattlegroundParticipation.Describe(AutonomousBattlegroundParticipation.LeaveReason.Unassigned), Is.EqualTo("unassigned"));
                Assert.That(AutonomousBattlegroundParticipation.Describe(AutonomousBattlegroundParticipation.LeaveReason.Stuck), Is.EqualTo("stuck"));
            });
        }

        [Test]
        public void PoolConstantsAndMedallionMatchTheDesign()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundParticipation.MaximumAutonomousPerMap, Is.EqualTo(24));
                Assert.That(AutonomousBattlegroundParticipation.SharedActorCap, Is.EqualTo(40));
                Assert.That(AutonomousBattlegroundParticipation.BattlegroundMedallionId, Is.EqualTo("battlegrounds_necklace"));
                Assert.That(AutonomousBattlegroundParticipation.IsFrontierRegion(1), Is.True);
                Assert.That(AutonomousBattlegroundParticipation.IsFrontierRegion(100), Is.True);
                Assert.That(AutonomousBattlegroundParticipation.IsFrontierRegion(200), Is.True);
                Assert.That(AutonomousBattlegroundParticipation.IsFrontierRegion(238), Is.False);
            });
        }

        [Test]
        public void UnregisteredActorsAreNeverParticipants()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundParticipation.IsParticipant(null), Is.False);
                Assert.That(AutonomousBattlegroundParticipation.IsAssigned(null), Is.False);
                Assert.That(AutonomousBattlegroundParticipation.IsLeaving(null), Is.False);
                Assert.That(AutonomousBattlegroundParticipation.PresentCount(238), Is.EqualTo(0));
            });
        }

        [Test]
        public void MerchantReachCoversTheMedallionSourceSearch()
        {
            Assert.That(AutonomousBattlegroundParticipation.MerchantReach, Is.EqualTo(3000));
        }

        [Test]
        public void FrontierMerchantOffsetsKeepTheMerchantAboutFiveHundredUnitsFromThePorterInOrder()
        {
            (int dx, int dy)[] offsets = FrontierMedallionMerchants.MerchantOffsets().ToArray();
            Assert.That(offsets, Has.Length.EqualTo(6));
            Assert.Multiple(() =>
            {
                Assert.That(offsets[0], Is.EqualTo((-373, -383)), "First choice");
                Assert.That(offsets[1], Is.EqualTo((373, -383)));
                Assert.That(offsets[2], Is.EqualTo((-373, 383)));
                Assert.That(offsets[3], Is.EqualTo((373, 383)));
                Assert.That(offsets[4], Is.EqualTo((-500, 0)));
                Assert.That(offsets[5], Is.EqualTo((500, 0)), "Last choice");
            });
            foreach ((int dx, int dy) in offsets)
            {
                double distance = Math.Sqrt((double)dx * dx + (double)dy * dy);
                Assert.That(distance, Is.InRange(400d, 600d), $"offset ({dx},{dy})");
            }
        }

        [Test]
        public void FrontierMerchantOffsetsAreDistinct()
        {
            (int dx, int dy)[] offsets = FrontierMedallionMerchants.MerchantOffsets().ToArray();
            Assert.That(offsets.Distinct().Count(), Is.EqualTo(offsets.Length));
        }
    }
}
