// Bug 179 (docs/BUGS.md): the open battleground driver's follow pace clamped the leader's speed to [50, own top speed];
// a snared member's top speed is below 50, Math.Clamp threw ("'50' cannot be greater than 2") and the bot was removed.
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
public sealed class UT_Bug179FollowPace
{
    [TestCase(191, 2, ExpectedResult = 2, TestName = "A member snared to 2 walks at 2 instead of crashing")]
    [TestCase(191, 41, ExpectedResult = 41, TestName = "A member slowed to 41 walks at its top speed")]
    [TestCase(30, 40, ExpectedResult = 40, TestName = "A member slowed to 40 behind a slow leader: its top speed (the floor of 50 capped)")]
    [TestCase(191, 191, ExpectedResult = 191, TestName = "Equal speed: the leader's pace")]
    [TestCase(10, 191, ExpectedResult = 50, TestName = "A crawling leader: at least 50")]
    [TestCase(300, 191, ExpectedResult = 191, TestName = "A faster leader: the member's top speed")]
    public int FollowPaceStaysWithinTheMembersTopSpeed(int leaderSpeed, int maxSpeed) =>
        AutonomousBattlegroundDriver.FollowPace(leaderSpeed, maxSpeed);
}
