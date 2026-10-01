using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    public class UT_CompanionFollowPolicy
    {
        [Test]
        public void CatchupIsSmoothMonotonicAndBoundedAtTwentyPercent()
        {
            int last = 0;
            for (int distance = 0; distance <= 4000; distance++)
            {
                int speed = CompanionFollowPolicy.CalculateSpeed(191, 390, distance);
                Assert.That(speed, Is.InRange(390, 468));
                Assert.That(speed, Is.GreaterThanOrEqualTo(last));
                if (distance > 0) Assert.That(speed - last, Is.LessThanOrEqualTo(1));
                last = speed;
            }
            Assert.That(CompanionFollowPolicy.CalculateSpeed(0, 390, 3000), Is.Zero);
            Assert.That(CompanionFollowPolicy.CalculateSpeed(500, 390, 0), Is.EqualTo(500));
            Assert.That(CompanionFollowPolicy.CalculateSpeed(191, 390, 300), Is.EqualTo(468),
                "A follower one short song-speed run behind needs the bounded full catch-up pace.");
        }

        [Test]
        public void FasterFollowerClosesAPathDelayWithoutTeleportingOrOvershooting()
        {
            double leader = 500, follower = 0;
            for (int tick = 0; tick < 1200; tick++)
            {
                leader += 390 * 0.1;
                double slot = leader - 140;
                double gap = slot - follower;
                follower += System.Math.Min(gap, CompanionFollowPolicy.CalculateSpeed(191, 390, gap) * 0.1);
                Assert.That(follower, Is.LessThanOrEqualTo(slot));
            }
            Assert.That(leader - 140 - follower, Is.LessThan(80));
        }

        [Test]
        public void StandingStillFarFromTheLeaderIsAStallOnlyAfterTheFullWindow()
        {
            var state = new CompanionFollowPolicy.State();
            Vector3 stuck = new(0, 0, 400);
            float far = CompanionFollowPolicy.LeftBehindDistance + 100;
            Assert.That(state.Stalled(stuck, far, true, 1000), Is.False, "the window starts at the first far sighting");
            Assert.That(state.Stalled(stuck + new Vector3(10, 0, 0), far, true,
                1000 + CompanionFollowPolicy.StallMilliseconds - 1), Is.False);
            Assert.That(state.Stalled(stuck + new Vector3(10, 0, 0), far, true,
                1000 + CompanionFollowPolicy.StallMilliseconds), Is.True);
        }

        [Test]
        public void ACompanionThatKeepsWalkingOrIsNearOrIneligibleIsNeverStalled()
        {
            float far = CompanionFollowPolicy.LeftBehindDistance + 100;
            var walking = new CompanionFollowPolicy.State();
            // Trailing a running leader: far the whole time, but always moving.
            for (int i = 0; i < 60; i++)
                Assert.That(walking.Stalled(new(i * 120, 0, 0), far, true, 1000 + i * 1000), Is.False);

            var near = new CompanionFollowPolicy.State();
            for (int i = 0; i < 60; i++)
                Assert.That(near.Stalled(Vector3.Zero, CompanionFollowPolicy.LeftBehindDistance, true, 1000 + i * 1000), Is.False);

            var fighting = new CompanionFollowPolicy.State();
            for (int i = 0; i < 60; i++)
                Assert.That(fighting.Stalled(Vector3.Zero, far, false, 1000 + i * 1000), Is.False);
        }

        [Test]
        public void StallWindowRestartsAfterCombatOrWhenTheCompanionMovesOn()
        {
            float far = CompanionFollowPolicy.LeftBehindDistance + 100;
            var state = new CompanionFollowPolicy.State();
            Assert.That(state.Stalled(Vector3.Zero, far, true, 1000), Is.False);
            Assert.That(state.Stalled(Vector3.Zero, far, false, 4000), Is.False, "combat resets the window");
            Assert.That(state.Stalled(Vector3.Zero, far, true, 4500), Is.False);
            Assert.That(state.Stalled(Vector3.Zero, far, true, 8000), Is.False, "only 3.5 s since combat ended");
            Assert.That(state.Stalled(Vector3.Zero, far, true, 8500), Is.True);
            Assert.That(state.Stalled(new(500, 0, 0), far, true, 8600), Is.False, "moved well past the radius");
            Assert.That(state.Stalled(new(500, 0, 0), far, true, 12599), Is.False);
            Assert.That(state.Stalled(new(500, 0, 0), far, true, 12600), Is.True);
        }

        [Test]
        public void BuffWaitReleasesAfterStopAndResetsOnMovementOrPositionChange()
        {
            var state = new CompanionFollowPolicy.State();
            Assert.That(state.Observe(Vector3.Zero, true, 1000), Is.True);
            Assert.That(state.Observe(Vector3.Zero, false, 1300), Is.True);
            Assert.That(state.Observe(Vector3.Zero, false, 1600), Is.False);
            Assert.That(state.Observe(new(20, 0, 0), false, 1700), Is.True);
            Assert.That(state.Observe(new(20, 0, 0), false, 2300), Is.False);
            for (int i = 0; i < 100; i++)
            {
                Assert.That(state.Observe(new(20, 0, 0), true, 3000 + i * 1000), Is.True);
                Assert.That(state.Observe(new(20, 0, 0), false, 3600 + i * 1000), Is.False);
            }
        }

        [Test]
        public void PredictionIsShortBoundedAndTracksActualMotionNotFacing()
        {
            Assert.That(CompanionFollowPolicy.Predict(Vector3.Zero, new(0, 390, 0), 390), Is.EqualTo(new Vector3(0, 195, 0))); // half a second ahead (0.102.0)
            Assert.That(CompanionFollowPolicy.Predict(Vector3.Zero, new(10000, 0, 0), 390), Is.EqualTo(new Vector3(195, 0, 0)));
            Assert.That(CompanionFollowPolicy.Predict(Vector3.Zero, new(10000, 0, 0), 3000), Is.EqualTo(new Vector3(200, 0, 0)));
            Assert.That(CompanionFollowPolicy.Predict(new(1, 2, 3), Vector3.Zero, 390), Is.EqualTo(new Vector3(1, 2, 3)));
        }
    }
}
