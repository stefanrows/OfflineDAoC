using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    public class UT_CompanionSquadFormation
    {
        [Test]
        public void ValidSquadIndexIsOneThroughFive()
        {
            Assert.That(CompanionSquadFormation.IsValidSquadIndex(0), Is.False);
            for (int index = 1; index <= CompanionSquadFormation.MaxSquadCount; index++)
                Assert.That(CompanionSquadFormation.IsValidSquadIndex(index), Is.True, index.ToString());
            Assert.That(CompanionSquadFormation.IsValidSquadIndex(CompanionSquadFormation.MaxSquadCount + 1), Is.False);
            Assert.That(CompanionSquadFormation.IsValidSquadIndex(-1), Is.False);
        }

        [Test]
        public void LeaderFollowBandFansOutBehindTheOwnerWithinTwoHundredToFourHundred()
        {
            int previousMax = 0;
            for (int index = 1; index <= CompanionSquadFormation.MaxSquadCount; index++)
            {
                (int min, int max) = CompanionSquadFormation.LeaderFollowBand(index);
                Assert.That(min, Is.InRange(200, 400), $"squad {index} min");
                Assert.That(max, Is.InRange(200, 400), $"squad {index} max");
                Assert.That(max, Is.GreaterThan(min), $"squad {index} band width");
                Assert.That(min, Is.GreaterThanOrEqualTo(previousMax), $"squad {index} should not overlap the previous squad");
                previousMax = max;
            }
        }

        [Test]
        public void InvalidSquadIndexFallsBackToTheOrdinaryCompanionFollowDistance()
        {
            Assert.That(CompanionSquadFormation.LeaderFollowBand(0),
                Is.EqualTo((BotManager.FOLLOW_DISTANCE, BotManager.MAX_FOLLOW_DISTANCE)));
            Assert.That(CompanionSquadFormation.LeaderFollowBand(99),
                Is.EqualTo((BotManager.FOLLOW_DISTANCE, BotManager.MAX_FOLLOW_DISTANCE)));
        }

        [Test]
        public void ExistingRecordsDefaultToNoSquadSameAsTheOwnersOwnGroup()
        {
            // Additive schema (task 42): every pre-existing PlayerCompanionRecord row
            // loads with SquadIndex 0 and IsSquadLeader false, so it keeps behaving
            // exactly like it did before squads existed.
            var record = new PlayerCompanionRecord();
            Assert.That(record.SquadIndex, Is.Zero);
            Assert.That(record.IsSquadLeader, Is.False);
        }
    }
}
