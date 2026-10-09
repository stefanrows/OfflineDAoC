using NUnit.Framework;

namespace DOL.GS.Tests
{

    [TestFixture]
    public sealed class UT_AutonomousRouteThreatPolicy
    {
        [Test]
        public void OnlyCompleteNativePathsQualifyAsCorridors()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRouteThreatPolicy.IsCompleteCorridor(
                    new PathfindingResult(PathfindingStatus.PathFound, 1), 256), Is.True);
                Assert.That(AutonomousRouteThreatPolicy.IsCompleteCorridor(
                    new PathfindingResult(PathfindingStatus.PartialPathFound, 1), 256), Is.False);
                Assert.That(AutonomousRouteThreatPolicy.IsCompleteCorridor(
                    new PathfindingResult(PathfindingStatus.BufferTooSmall, 257), 256), Is.False);
                Assert.That(AutonomousRouteThreatPolicy.IsCompleteCorridor(
                    new PathfindingResult(PathfindingStatus.NoPathFound, 0), 256), Is.False);
            });
        }

        [Test]
        public void RouteResponsePullsManageableFightsAndDetoursDangerousPacks()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRouteThreatPolicy.Decide(ConColor.YELLOW, false, 1, false,
                    true, false, false, 0, 0), Is.EqualTo(RouteThreatAction.Pull));
                Assert.That(AutonomousRouteThreatPolicy.Decide(ConColor.ORANGE, false, 1, false,
                    true, true, false, 0, 0), Is.EqualTo(RouteThreatAction.Detour));
                Assert.That(AutonomousRouteThreatPolicy.Decide(ConColor.PURPLE, false, 1, false,
                    true, false, false, 0, 0), Is.EqualTo(RouteThreatAction.RejectCamp));
            });
        }

        [Test]
        public void ExhaustedPullsAndNestedDetoursCannotIgnoreAThreat()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRouteThreatPolicy.Decide(ConColor.YELLOW, false, 1, false,
                    true, true, false, AutonomousRouteThreatPolicy.MaximumPullAttemptsPerThreat, 0),
                    Is.EqualTo(RouteThreatAction.Detour));
                Assert.That(AutonomousRouteThreatPolicy.Decide(ConColor.PURPLE, false, 1, false,
                    false, true, true, 0, 0), Is.EqualTo(RouteThreatAction.RejectCamp));
                Assert.That(AutonomousRouteThreatPolicy.Decide(ConColor.PURPLE, false, 1, false,
                    false, false, false, 0, AutonomousRouteThreatPolicy.MaximumDetoursPerThreat),
                    Is.EqualTo(RouteThreatAction.RejectCamp));
            });
        }
    }

}
