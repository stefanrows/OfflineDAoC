using System.Collections.Generic;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

// Bug 88: an unreachable outdoor rival hunt is rejected for 30 minutes, but
// RvR roaming kept the rejected destination (only dungeons were dropped), so
// the same bot re-walked into the same unreachable camp every ~40 seconds.
[TestFixture]
public class UT_RivalHuntRejection
{
    private const string Camp = "local-pvp-capnbry-source-empty:181:73:79:gorge shriller";
    private const long Now = 1_000_000;
    private const long Window = 30 * 60_000;

    [Test]
    public void RejectedOutdoorRivalHuntIsDroppedForTheWholeWindowAndOnlyThen()
    {
        var rejected = new Dictionary<string, long> { [Camp] = Now + Window };
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRouteRecoveryPolicy.ShouldDropRejectedRvrDestination(Camp, false, rejected, Now), Is.True);
            Assert.That(AutonomousRouteRecoveryPolicy.ShouldDropRejectedRvrDestination(Camp, false, rejected, Now + Window - 1), Is.True);
            Assert.That(AutonomousRouteRecoveryPolicy.ShouldDropRejectedRvrDestination(Camp, false, rejected, Now + Window), Is.False,
                "The rejection expires after 30 minutes");
        });
    }

    [Test]
    public void OtherDestinationsAreNotDropped()
    {
        var rejected = new Dictionary<string, long>
        {
            [Camp] = Now + Window,
            ["rvr-keep-5"] = Now + Window,
            ["dungeon-camp"] = Now + Window,
        };
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRouteRecoveryPolicy.ShouldDropRejectedRvrDestination("local-pvp-other", false, rejected, Now), Is.False,
                "A different camp is unaffected");
            Assert.That(AutonomousRouteRecoveryPolicy.ShouldDropRejectedRvrDestination("rvr-keep-5", false, rejected, Now), Is.False,
                "Keep objectives are re-published by the shared RvR plan and are not dropped here");
            Assert.That(AutonomousRouteRecoveryPolicy.ShouldDropRejectedRvrDestination("dungeon-camp", true, rejected, Now), Is.True,
                "Rejected dungeon destinations are still dropped");
            Assert.That(AutonomousRouteRecoveryPolicy.ShouldDropRejectedRvrDestination(null, false, rejected, Now), Is.False);
        });
    }
}
