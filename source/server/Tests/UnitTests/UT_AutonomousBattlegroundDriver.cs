using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_AutonomousBattlegroundDriver
    {
        [Test]
        public void ClosedGateBlocksOnlyWhenInteractionCannotOpenItAndTheBotMayNotPass()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundDriver.IsRouteBlockingDoor(closed: true, openableByInteraction: false, friendlyKeepDoor: false), Is.True,
                    "Enemy portal gate: the native mover refuses it too");
                Assert.That(AutonomousBattlegroundDriver.IsRouteBlockingDoor(closed: true, openableByInteraction: false, friendlyKeepDoor: true), Is.False,
                    "A keep gate this bot may pass is walked through");
                Assert.That(AutonomousBattlegroundDriver.IsRouteBlockingDoor(closed: true, openableByInteraction: true, friendlyKeepDoor: false), Is.False,
                    "A door interaction can open");
                Assert.That(AutonomousBattlegroundDriver.IsRouteBlockingDoor(closed: false, openableByInteraction: false, friendlyKeepDoor: false), Is.False,
                    "An open gate never blocks");
            });
        }
    }
}
