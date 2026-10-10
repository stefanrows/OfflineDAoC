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
        // The catalog has no realm-rank ceilings; this bracket keeps one to exercise the ceiling rule.
        private static readonly BattlegroundDefinition CappedThidranki = new(238, 238, "Thidranki", 20, 24, 15);

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
                Assert.That(Candidate(22, 14, definition: CappedThidranki), Is.True, "One below the exclusive ceiling");
                Assert.That(Candidate(22, 15, definition: CappedThidranki), Is.False, "At the exclusive ceiling");
                Assert.That(Candidate(1, 50, definition: ProvingGrounds), Is.True, "Proving Grounds has no ceiling");
            });
        }

        [Test]
        public void NoCampaignBracketHasARealmRankCeiling()
        {
            Assert.Multiple(() =>
            {
                foreach (BattlegroundDefinition definition in BattlegroundCampaignCatalog.Definitions)
                    Assert.That(definition.MaxRealmLevel, Is.EqualTo(0), definition.Name);
                Assert.That(Candidate(22, 120), Is.True, "Thidranki takes any realm rank");
                Assert.That(AutonomousBattlegroundParticipation.LeaveReasonFor(22, 120, true, false, false, Thidranki),
                    Is.Not.EqualTo(AutonomousBattlegroundParticipation.LeaveReason.Graduated), "Realm rank never graduates");
            });
        }

        [Test]
        public void GraduationReasonsMatchTheBracketRules()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundParticipation.LeaveReasonFor(25, 0, true, false, false, Thidranki),
                    Is.EqualTo(AutonomousBattlegroundParticipation.LeaveReason.Graduated), "Level above the bracket");
                Assert.That(AutonomousBattlegroundParticipation.LeaveReasonFor(22, 15, true, false, false, CappedThidranki),
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
        public void StuckNeedsFiveGameMinutesNinetyWallSecondsAndADriverTurn()
        {
            const long fiveGameMinutes = 300_000;
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundParticipation.IsStuck(fiveGameMinutes + 1, 0, 90_001, 0, hadTurn: true), Is.True,
                    "Both clocks elapsed after a turn");
                Assert.That(AutonomousBattlegroundParticipation.IsStuck(fiveGameMinutes, 0, 900_000, 0, hadTurn: true), Is.False,
                    "The game clock must pass five minutes, not reach them");
                Assert.That(AutonomousBattlegroundParticipation.IsStuck(fiveGameMinutes + 1, 0, 89_999, 0, hadTurn: true), Is.False,
                    "A fast world speed cannot eject a bot inside ninety wall-clock seconds");
                Assert.That(AutonomousBattlegroundParticipation.IsStuck(fiveGameMinutes + 1, 0, 90_000, 0, hadTurn: true), Is.True,
                    "The wall-clock floor is inclusive");
                Assert.That(AutonomousBattlegroundParticipation.IsStuck(fiveGameMinutes + 1, 0, 90_001, 0, hadTurn: false), Is.False,
                    "Without a driver turn the bot is no_turn, never stuck");
            });
        }

        [Test]
        public void NoTurnUsesTheSameTwoClockWindowWithoutAnyDriverTurn()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundParticipation.IsNoTurn(300_001, 0, 90_000, 0, hadTurn: false), Is.True);
                Assert.That(AutonomousBattlegroundParticipation.IsNoTurn(300_001, 0, 89_000, 0, hadTurn: false), Is.False,
                    "Not yet ninety wall-clock seconds");
                Assert.That(AutonomousBattlegroundParticipation.IsNoTurn(300_000, 0, 120_000, 0, hadTurn: false), Is.False,
                    "Not yet five game minutes");
                Assert.That(AutonomousBattlegroundParticipation.IsNoTurn(300_001, 0, 90_000, 0, hadTurn: true), Is.False,
                    "A participant that has turned is judged by IsStuck");
            });
        }

        [Test]
        public void NoTurnIsItsOwnLeaveReason()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundParticipation.Describe(AutonomousBattlegroundParticipation.LeaveReason.NoTurn), Is.EqualTo("no_turn"));
                Assert.That(AutonomousBattlegroundParticipation.Describe(AutonomousBattlegroundParticipation.LeaveReason.Stuck), Is.EqualTo("stuck"));
            });
        }

        [Test]
        public void MerchantReachIsMeasuredInTheTwoDimensionalPlane()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundParticipation.IsWithinReach2D(3000, 0, 3000), Is.True, "On the reach circle");
                Assert.That(AutonomousBattlegroundParticipation.IsWithinReach2D(2121, 2121, 3000), Is.True, "Diagonal inside the circle");
                Assert.That(AutonomousBattlegroundParticipation.IsWithinReach2D(2200, 2200, 3000), Is.False, "Diagonal outside the circle");
                Assert.That(AutonomousBattlegroundParticipation.IsWithinReach2D(-373, -383, 3000), Is.True, "A frontier merchant offset");
            });
        }

        [Test]
        public void SeederDuplicateMatchesOnlyItsOwnExactRow()
        {
            Assert.Multiple(() =>
            {
                Assert.That(FrontierMedallionMerchants.SeederClassType, Is.EqualTo(typeof(OFMerchant).FullName));
                Assert.That(FrontierMedallionMerchants.IsSeederDuplicate("DOL.GS.Scripts.OFMerchant", "Gwulla", 100, "OFMerchant_Mid"), Is.True);
                Assert.That(FrontierMedallionMerchants.IsSeederDuplicate("DOL.GS.GameMerchant", "Gwulla", 100, "OFMerchant_Mid"), Is.False,
                    "The native seller is never a duplicate");
                Assert.That(FrontierMedallionMerchants.IsSeederDuplicate("DOL.GS.Scripts.OFMerchant", "Gwulla", 101, "OFMerchant_Mid"), Is.False);
                Assert.That(FrontierMedallionMerchants.IsSeederDuplicate("DOL.GS.Scripts.OFMerchant", "Gwulla", 100, "OFMerchant_Other"), Is.False);
                Assert.That(FrontierMedallionMerchants.IsSeederDuplicate("DOL.GS.Scripts.OFMerchant", "Other", 100, "OFMerchant_Mid"), Is.False);
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
        public void ProgressCountsAsRecentOnlyInsideTheStuckWallWindow()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundParticipation.IsRecentProgress(89_999, 0), Is.True, "Inside the window");
                Assert.That(AutonomousBattlegroundParticipation.IsRecentProgress(90_000, 0), Is.False, "Same threshold as IsStuck");
                Assert.That(AutonomousBattlegroundParticipation.IsRecentProgress(90_000, 0),
                    Is.EqualTo(!AutonomousBattlegroundParticipation.IsStuck(300_001, 0, 90_000, 0, hadTurn: true)),
                    "Recent progress is the complement of the wall test that makes a participant stuck");
            });
        }

        [Test]
        public void BattlegroundLandingReleaseDoesNotStartTheReturnWalk()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundParticipation.StartsReleaseReturnWalk(returnToParty: true, battlegroundLanding: true),
                    Is.False, "The driver leads a participant released at its landing; no walk back to the death spot");
                Assert.That(AutonomousBattlegroundParticipation.StartsReleaseReturnWalk(returnToParty: true, battlegroundLanding: false),
                    Is.True, "An ordinary world release still walks back to the party");
                Assert.That(AutonomousBattlegroundParticipation.StartsReleaseReturnWalk(returnToParty: false, battlegroundLanding: false),
                    Is.False, "A disbanded party has nobody to return to");
            });
        }

        [Test]
        public void ProgressWithdrawsOnlyAStuckLeaveMark()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundParticipation.AfterProgress(AutonomousBattlegroundParticipation.LeaveReason.Stuck),
                    Is.Null, "A bot that fights again after its stuck mark stays");
                Assert.That(AutonomousBattlegroundParticipation.AfterProgress(null), Is.Null);
                foreach (var reason in new[]
                         {
                             AutonomousBattlegroundParticipation.LeaveReason.Graduated,
                             AutonomousBattlegroundParticipation.LeaveReason.TourEnded,
                             AutonomousBattlegroundParticipation.LeaveReason.Unassigned,
                             AutonomousBattlegroundParticipation.LeaveReason.NoTurn,
                         })
                    Assert.That(AutonomousBattlegroundParticipation.AfterProgress(reason), Is.EqualTo(reason), reason.ToString());
            });
        }

        [Test]
        public void FrontierMerchantOffsetsAreDistinct()
        {
            (int dx, int dy)[] offsets = FrontierMedallionMerchants.MerchantOffsets().ToArray();
            Assert.That(offsets.Distinct().Count(), Is.EqualTo(offsets.Length));
        }
    }
}
