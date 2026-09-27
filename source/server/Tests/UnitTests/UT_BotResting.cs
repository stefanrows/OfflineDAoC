using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_BotResting
    {
        [TestCase(11999, true)]
        [TestCase(12000, false)]
        [TestCase(12001, false)]
        public void BotCombatQuietWindowIsTwoSeconds(int now, bool blocked)
        {
            Assert.That(BotRestRecovery.RecentlyFought(now, 10000), Is.EqualTo(blocked));
        }

        [TestCase(0, 100, 10)] [TestCase(3, 100, 10)]
        [TestCase(20, 100, 20)] [TestCase(1, 19, 2)]
        public void BotFastRestIsTenPercentFloorWithoutNerfingStrongerNativeRegen(int native, int maximum, int expected)
        {
            Assert.That(BotRestRecovery.RecoveryAmount(native, maximum), Is.EqualTo(expected));
        }

        [TestCase(false, false, 6000)]
        [TestCase(true, false, 3000)]
        [TestCase(false, true, 14000)]
        [TestCase(true, true, 10000)]
        public void SharedPlayerAndBotHealthPowerTiming(bool sitting, bool combat, int interval)
        {
            Assert.That(ClassicRestRegeneration.HealthAndPowerInterval(sitting, combat), Is.EqualTo(interval));
        }

        [TestCase(false, false, false, 1)]
        [TestCase(true, false, false, 4)]
        [TestCase(false, true, false, 0)]
        [TestCase(true, true, false, 0)]
        [TestCase(false, false, true, 0)]
        [TestCase(true, false, true, 0)]
        [TestCase(false, true, true, 0)]
        [TestCase(true, true, true, 0)]
        public void SharedPlayerAndBotEnduranceBase(bool sitting, bool combat, bool moving, int amount)
        {
            Assert.That(ClassicRestRegeneration.BaseEndurancePerTick(sitting, combat, moving), Is.EqualTo(amount));
        }

        [Test]
        public void SeatedRecoveryContinuesThroughStartThresholdUntilFull()
        {
            bool sitting = false;
            for (byte percent = 20; percent < 100; percent++)
            {
                sitting = AutonomousRestPolicy.ShouldRest(true, false, false, sitting, percent, percent, percent, true);
                Assert.That(sitting, Is.True, $"Recovery interrupted at {percent}%");
            }
            Assert.That(AutonomousRestPolicy.ShouldRest(true, false, false, sitting, 100, 100, 100, true), Is.False);
        }

        [TestCase(false, false, false)] // Leader left / task or rest point changed.
        [TestCase(true, true, false)]   // Leader or bot moving.
        [TestCase(true, false, true)]   // Attacked while recovering.
        public void RestYieldsToTravelAndCombat(bool atRestPoint, bool moving, bool combat)
        {
            Assert.That(AutonomousRestPolicy.ShouldRest(atRestPoint, moving, combat, true, 40, 40, 40, true), Is.False);
        }

        [TestCase(69, 100, 100, true, true)]
        [TestCase(100, 44, 100, true, true)]
        [TestCase(100, 100, 34, false, true)]
        [TestCase(99, 99, 99, true, false)]
        [TestCase(100, 0, 100, false, false)]
        public void RestStartsOnlyForGenuineDeficits(byte hp, byte power, byte endurance, bool usesPower, bool expected)
        {
            Assert.That(AutonomousRestPolicy.ShouldRest(true, false, false, false, hp, power, endurance, usesPower), Is.EqualTo(expected));
        }

        [TestCase(100, 90, 100, true, true)]
        [TestCase(95, 100, 100, false, true)]
        [TestCase(100, 100, 99, false, true)]
        [TestCase(100, 50, 100, false, false)]
        [TestCase(100, 100, 100, true, false)]
        public void SittingLeaderStartsRestForAnyMissingPool(byte hp, byte power, byte endurance, bool usesPower, bool expected)
        {
            Assert.That(AutonomousRestPolicy.ShouldRest(true, false, false, false, hp, power, endurance, usesPower,
                leaderSitting: true), Is.EqualTo(expected));
        }

        [Test]
        public void SittingLeaderDoesNotOverrideTravelOrCombat()
        {
            Assert.That(AutonomousRestPolicy.ShouldRest(true, true, false, false, 100, 60, 100, true, leaderSitting: true), Is.False);
            Assert.That(AutonomousRestPolicy.ShouldRest(true, false, true, false, 100, 60, 100, true, leaderSitting: true), Is.False);
        }

        [Test]
        public void NonCasterDoesNotWaitForAPowerPoolItCannotHave()
        {
            Assert.That(AutonomousRestPolicy.ShouldRest(true, false, false, true, 100, 0, 100, false), Is.False);
        }

        [TestCase(99, 100, 100)]
        [TestCase(100, 99, 100)]
        [TestCase(100, 100, 99)]
        public void EveryUsedResourceMustFinishRecovery(byte hp, byte power, byte endurance)
        {
            Assert.That(AutonomousRestPolicy.ShouldRest(true, false, false, true, hp, power, endurance, true), Is.True);
        }

        [TestCase(99, 100, 100)]
        [TestCase(100, 99, 100)]
        [TestCase(100, 100, 99)]
        public void CompletedPullHasAResourceDeficitUntilEveryPoolIsFull(byte hp, byte power, byte endurance)
        {
            Assert.That(AutonomousRestPolicy.IsFullyRecovered(hp, power, endurance, true), Is.False);
        }

        [TestCase(true, false, true, false)]
        [TestCase(true, false, false, true)]
        [TestCase(false, true, true, true)]
        [TestCase(false, false, false, false)]
        public void OnlyMeaningfulTravelBlocksRest(
            bool botMoving, bool leaderMoving, bool ambientWanderMovement, bool expected)
        {
            Assert.That(AutonomousRestPolicy.MovementBlocksRest(
                botMoving, leaderMoving, ambientWanderMovement), Is.EqualTo(expected));
        }

        [Test]
        public void AmbientRoleplayMovementCannotInterruptRecovery()
        {
            bool moving = AutonomousRestPolicy.MovementBlocksRest(true, false, true);

            Assert.That(AutonomousRestPolicy.ShouldRest(
                true, moving, false, false, 40, 40, 40, true), Is.True);
        }

        [Test]
        public void TemporaryCompanionStartsRestForAnyMissingResourceAfterTwoQuietSeconds()
        {
            Assert.That(BotRestRecovery.ShouldTemporaryCompanionRest(
                true, true, false, false, 11_999, 10_000,
                999, 1000, 1000, 1000, 1000, 1000), Is.False);
            Assert.That(BotRestRecovery.ShouldTemporaryCompanionRest(
                true, true, false, false, 12_000, 10_000,
                999, 1000, 1000, 1000, 1000, 1000), Is.True);
            Assert.That(BotRestRecovery.ShouldTemporaryCompanionRest(
                true, true, false, false, 12_000, 10_000,
                1000, 1000, 999, 1000, 1000, 1000), Is.True);
            Assert.That(BotRestRecovery.ShouldTemporaryCompanionRest(
                true, true, false, false, 12_000, 10_000,
                1000, 1000, 1000, 1000, 999, 1000), Is.True);
        }

        [Test]
        public void CompanionCastActivityGetsItsOwnQuietWindowBeforeRest()
        {
            long leaderIdleSince = 10_000;
            long companionCastEnded = 11_500;
            long latest = BotRestRecovery.LatestRestActivityTick(leaderIdleSince, companionCastEnded);

            Assert.That(BotRestRecovery.ShouldTemporaryCompanionRest(
                true, true, false, false, 12_000, latest,
                900, 1000, 900, 1000, 900, 1000), Is.False,
                "A gap between casts is not a rest-mode trigger.");
            Assert.That(BotRestRecovery.ShouldTemporaryCompanionRest(
                true, true, false, false, 13_500, latest,
                900, 1000, 900, 1000, 900, 1000), Is.True,
                "Rest becomes eligible only after the companion itself is quiet for two seconds.");
        }

        [TestCase(false, true, false, false)]
        [TestCase(true, false, false, false)]
        [TestCase(true, true, true, false)]
        [TestCase(true, true, false, true)]
        public void TemporaryCompanionRestKeepsScopeMovementAndCombatGuards(
            bool temporary, bool atPlayer, bool moving, bool combat)
        {
            Assert.That(BotRestRecovery.ShouldTemporaryCompanionRest(
                temporary, atPlayer, moving, combat, 12_000, 10_000,
                500, 1000, 500, 1000, 500, 1000), Is.False);
        }

        [TestCase(true, false, 1000)]   // sitting, out of combat: fast rest like companions
        [TestCase(true, true, 10000)]   // sitting in combat stays classic
        [TestCase(false, false, 6000)]  // standing stays classic
        public void PlayerSittingOutOfCombatRestsFast(bool sitting, bool combat, int interval)
        {
            Assert.That(ClassicRestRegeneration.PlayerHealthAndPowerInterval(sitting, combat), Is.EqualTo(interval));
        }

        [Test]
        public void PlayerFastRestRefillsTenPercentPerTick()
        {
            Assert.That(ClassicRestRegeneration.PlayerRestAmount(6, 400, sitting: true, inCombat: false), Is.EqualTo(40));
            Assert.That(ClassicRestRegeneration.PlayerRestAmount(6, 400, sitting: true, inCombat: true), Is.EqualTo(6));
            Assert.That(ClassicRestRegeneration.PlayerRestAmount(6, 400, sitting: false, inCombat: false), Is.EqualTo(6));
            Assert.That(ClassicRestRegeneration.PlayerRestAmount(0, 400, sitting: true, inCombat: false), Is.EqualTo(0),
                "Suppressed regeneration (disease) stays suppressed.");
        }

        [TestCase(10 * 60 * 1000, false, true)]  // 10 min buff
        [TestCase(5 * 60 * 1000, false, true)]   // exactly 5 min
        [TestCase(60 * 1000, false, false)]      // 1 min buff is short
        [TestCase(0, true, true)]                // concentration buffs stay up
        public void LongBuffsStartAtFiveMinutes(int duration, bool concentration, bool expected)
        {
            Assert.That(BotBuffTimingPolicy.IsLongBuff(duration, concentration), Is.EqualTo(expected));
        }

        [TestCase(true, false, false, true)]    // long buff out of combat
        [TestCase(false, false, false, false)]  // short buff never out of combat
        [TestCase(false, true, true, true)]     // speed while the group travels
        [TestCase(true, true, false, false)]    // speed while standing
        public void OutOfCombatUpkeepOnlyForLongBuffsAndTravelSpeed(bool isLong, bool isSpeed, bool traveling, bool expected)
        {
            Assert.That(BotBuffTimingPolicy.MaintainOutOfCombat(isLong, isSpeed, traveling), Is.EqualTo(expected));
        }

        [TestCase(59_000, false, true)]
        [TestCase(61_000, false, false)]
        [TestCase(10_000, true, false)]   // concentration never expires
        [TestCase(0, false, false)]       // no timer: nothing to refresh
        public void LongBuffIsRefreshedInItsLastMinute(long remaining, bool concentration, bool expected)
        {
            Assert.That(BotBuffTimingPolicy.ExpiresSoon(remaining, concentration), Is.EqualTo(expected));
        }

        [Test]
        public void TemporaryCompanionDoesNotRestWhenAlreadyFull()
        {
            Assert.That(BotRestRecovery.ShouldTemporaryCompanionRest(
                true, true, false, false, 12_000, 10_000,
                1000, 1000, 1000, 1000, 1000, 1000), Is.False);
        }
    }
}
