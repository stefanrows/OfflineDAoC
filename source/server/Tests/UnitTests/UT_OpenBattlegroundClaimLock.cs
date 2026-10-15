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
            // While the lord lies defeated the roles code gives the whole raid the Defend role: the window applies to it as to Assault.
            Assert.That(AutonomousBattlegroundDriver.ClaimAllowed(OpenBgRole.Defend, true, 5_000), Is.True, "the lead guild's defend unit inside the window");
            Assert.That(AutonomousBattlegroundDriver.ClaimAllowed(OpenBgRole.Defend, false, 5_000), Is.False, "a partner's defend unit waits");
            Assert.That(AutonomousBattlegroundDriver.ClaimAllowed(OpenBgRole.Defend, false, 61_000), Is.True, "after the window anyone may");
            foreach (OpenBgRole idle in new[] { OpenBgRole.Roam, OpenBgRole.Lurk, OpenBgRole.BridgeWatch, OpenBgRole.Retreat })
                Assert.That(AutonomousBattlegroundDriver.ClaimAllowed(idle, true, 120_000), Is.False, idle + " never claims");
        });
    }
}
