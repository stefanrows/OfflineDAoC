using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_BattlegroundHoldReward
    {
        private const long Minute = 60_000;

        [Test]
        public void NothingIsDueWithoutAHeldKeep()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundCampaignManager.HoldRewardDue(0, 0, 0), Is.False, "No holder ever recorded");
                Assert.That(BattlegroundCampaignManager.HoldRewardDue(0, 0, 60 * Minute), Is.False, "Keep lost (HeldSince cleared)");
                Assert.That(BattlegroundCampaignManager.HoldRewardDue(0, 30 * Minute, 60 * Minute), Is.False, "Lost keep keeps no interval");
            });
        }

        [Test]
        public void JustCapturedKeepIsNotDue()
        {
            long captured = 5_000_000;
            Assert.That(BattlegroundCampaignManager.HoldRewardDue(captured, 0, captured), Is.False);
        }

        [Test]
        public void TenMinutesIsDueAndNineMinutesFiftyNineSecondsIsNot()
        {
            long heldSince = 1_000_000;
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundCampaignManager.HoldRewardDue(heldSince, 0, heldSince + 10 * Minute - 1), Is.False, "9:59.999");
                Assert.That(BattlegroundCampaignManager.HoldRewardDue(heldSince, 0, heldSince + 10 * Minute), Is.True, "10:00");
                Assert.That(BattlegroundCampaignManager.HoldRewardDue(heldSince, 0, heldSince + 25 * Minute), Is.True, "Overdue is still due");
            });
        }

        [Test]
        public void IntervalRestartsFromThePayoutNotFromTheCapture()
        {
            long heldSince = 1_000_000;
            long payout = heldSince + 10 * Minute;
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundCampaignManager.HoldRewardDue(heldSince, payout, payout + 9 * Minute + 59_999), Is.False, "Second interval at 9:59");
                Assert.That(BattlegroundCampaignManager.HoldRewardDue(heldSince, payout, payout + 10 * Minute), Is.True, "Second interval at 10:00");
            });
        }

        [Test]
        public void ExperienceIsTwoPercentOfTheLevelSpanAndRoundsDown()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundCampaignManager.HoldRewardExperience(0), Is.EqualTo(0));
                Assert.That(BattlegroundCampaignManager.HoldRewardExperience(100_000), Is.EqualTo(2_000));
                Assert.That(BattlegroundCampaignManager.HoldRewardExperience(1_234), Is.EqualTo(24), "Integer division");
                Assert.That(BattlegroundCampaignManager.HoldRewardExperience(49_999), Is.EqualTo(999), "Rounds down");
            });
        }
    }
}
