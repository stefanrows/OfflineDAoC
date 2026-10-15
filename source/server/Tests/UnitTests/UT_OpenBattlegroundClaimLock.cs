using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_OpenBattlegroundClaimLock
{
    [Test]
    public void OnlyTheLeadGuildClaimsFirstAndNoIdleRoleEverWalksToTheSteward()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBattlegroundDriver.ClaimAllowed(OpenBgRole.Assault, true, 5_000), Is.True, "lead guild inside the window");
            Assert.That(AutonomousBattlegroundDriver.ClaimAllowed(OpenBgRole.Assault, false, 5_000), Is.False, "a partner waits");
            Assert.That(AutonomousBattlegroundDriver.ClaimAllowed(OpenBgRole.Assault, false, 61_000), Is.True, "after the window anyone may");
            Assert.That(AutonomousBattlegroundDriver.ClaimAllowed(OpenBgRole.Defend, false, 0), Is.True, "the owner's defenders may re-claim");
            foreach (OpenBgRole idle in new[] { OpenBgRole.Roam, OpenBgRole.Lurk, OpenBgRole.BridgeWatch, OpenBgRole.Retreat })
                Assert.That(AutonomousBattlegroundDriver.ClaimAllowed(idle, true, 120_000), Is.False, idle + " never claims");
        });
    }
}
