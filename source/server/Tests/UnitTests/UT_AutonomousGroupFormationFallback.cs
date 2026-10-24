using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>A follower whose slot is unreachable or over a zone border trails the leader instead of standing.</summary>
    public class UT_AutonomousGroupFormationFallback
    {
        private const long Now = 100_000;

        [Test]
        public void ReachableSameZoneSlotIsUnchanged()
        {
            long until = AutonomousGroupMotion.NextTrailUntil(0, false, Now);
            Assert.That(until, Is.EqualTo(0));
            Assert.That(AutonomousGroupMotion.ChooseAim(AutonomousGroupMotion.TrailActive(until, Now), false, false, false),
                Is.EqualTo(AutonomousGroupMotion.FollowAim.Slot));
        }

        [Test]
        public void UnreachableSlotTrailsLeaderForThreeSecondsThenReturnsToSlot()
        {
            long until = AutonomousGroupMotion.NextTrailUntil(0, true, Now);
            Assert.That(until, Is.EqualTo(Now + 3_000));
            Assert.That(AutonomousGroupMotion.ChooseAim(AutonomousGroupMotion.TrailActive(until, Now + 100), false, false, false),
                Is.EqualTo(AutonomousGroupMotion.FollowAim.Trail));
            Assert.That(AutonomousGroupMotion.ChooseAim(AutonomousGroupMotion.TrailActive(until, Now + 2_999), false, false, false),
                Is.EqualTo(AutonomousGroupMotion.FollowAim.Trail));
            // A later quiet check keeps the running spell and does not extend it.
            Assert.That(AutonomousGroupMotion.NextTrailUntil(until, false, Now + 2_000), Is.EqualTo(until));
            Assert.That(AutonomousGroupMotion.ChooseAim(AutonomousGroupMotion.TrailActive(until, Now + 3_000), false, false, false),
                Is.EqualTo(AutonomousGroupMotion.FollowAim.Slot));
        }

        [Test]
        public void SlotInAnotherZoneAimsBehindTheLeaderOrAtTheLeader()
        {
            // The leader (and the point behind it) is on the follower's side: aim at the trail point.
            Assert.That(AutonomousGroupMotion.ChooseAim(false, true, false, false),
                Is.EqualTo(AutonomousGroupMotion.FollowAim.Trail));
            // The leader is on the follower's side but 64 units behind it is over the border: aim at the leader.
            Assert.That(AutonomousGroupMotion.ChooseAim(false, true, true, false),
                Is.EqualTo(AutonomousGroupMotion.FollowAim.Leader));
        }

        [Test]
        public void LeaderAcrossTheBorderIsLeftToTheZoneItinerary()
        {
            // The point behind the leader is still on the follower's side: walk there.
            Assert.That(AutonomousGroupMotion.ChooseAim(false, true, false, true),
                Is.EqualTo(AutonomousGroupMotion.FollowAim.Trail));
            // Both over the border: no straight walk across it, the controller routes the crossing.
            Assert.That(AutonomousGroupMotion.ChooseAim(false, true, true, true),
                Is.EqualTo(AutonomousGroupMotion.FollowAim.CrossZone));
            Assert.That(AutonomousGroupMotion.ChooseAim(true, false, true, true),
                Is.EqualTo(AutonomousGroupMotion.FollowAim.CrossZone));
        }

        [Test]
        public void TrailPointLiesBehindTheLeaderAlongItsHeading()
        {
            Vector3 point = AutonomousGroupMotion.TrailPoint(new Vector3(1000, 2000, 50), new Vector2(0, 100));
            Assert.That(point.X, Is.EqualTo(1000).Within(0.01));
            Assert.That(point.Y, Is.EqualTo(2000 - AutonomousGroupMotion.TrailLeaderDistance).Within(0.01));
            Assert.That(point.Z, Is.EqualTo(50));
            Vector3 diagonal = AutonomousGroupMotion.TrailPoint(new Vector3(0, 0, 0), new Vector2(100, 100));
            Assert.That(Vector2.Distance(Vector2.Zero, new Vector2(diagonal.X, diagonal.Y)),
                Is.EqualTo(AutonomousGroupMotion.TrailLeaderDistance).Within(0.01));
            Assert.That(AutonomousGroupMotion.TrailPoint(new Vector3(5, 6, 7), Vector2.Zero), Is.EqualTo(new Vector3(5, 6, 7)));
        }
    }
}
