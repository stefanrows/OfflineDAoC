using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Task 136: an RvR group's replacement leader is the member standing with the main body.</summary>
[TestFixture]
public sealed class UT_AutonomousMainBodyLeader
{
    private static AutonomousBotGroupCoordinator.MainBodyCandidate Bot(long id, int x, int y, ushort region = 163) =>
        new(id, region, x, y);

    [Test]
    public void StragglerFirstInTheListIsNotChosen()
    {
        var candidates = new[] { Bot(1, 60_000, 0), Bot(2, 0, 0), Bot(3, 500, 0), Bot(4, 0, 800) };
        int index = AutonomousBotGroupCoordinator.ChooseMainBodyLeader(candidates, null, out int near);
        Assert.That(candidates[index].Id, Is.Not.EqualTo(1));
        Assert.That(near, Is.EqualTo(2));
    }

    [Test]
    public void TieGoesToTheMemberNearestTheObjective()
    {
        // Ids 2 and 3 each have one comrade near; 3 stands closer to the keep.
        var candidates = new[] { Bot(2, 0, 0), Bot(3, 1_000, 0) };
        var objective = new AutonomousBotGroupCoordinator.MainBodyObjective(163, 20_000, 0);
        int index = AutonomousBotGroupCoordinator.ChooseMainBodyLeader(candidates, objective, out _);
        Assert.That(candidates[index].Id, Is.EqualTo(3));
    }

    [Test]
    public void FullTieWithoutObjectiveTakesTheLowestId()
    {
        var candidates = new[] { Bot(9, 0, 0), Bot(4, 500, 0), Bot(7, 0, 500) };
        int index = AutonomousBotGroupCoordinator.ChooseMainBodyLeader(candidates, null, out _);
        Assert.That(candidates[index].Id, Is.EqualTo(4));
    }

    [Test]
    public void MembersInAnotherRegionAreNotCounted()
    {
        // Bot 1 stands beside two members of another region; counted, it would win by id.
        var candidates = new[]
        {
            Bot(1, 0, 0, 163), Bot(2, 10, 0, 100), Bot(3, 20, 0, 100),
            Bot(5, 5_000, 0, 163), Bot(6, 5_100, 0, 163), Bot(7, 5_200, 0, 163),
        };
        int index = AutonomousBotGroupCoordinator.ChooseMainBodyLeader(candidates, null, out int near);
        Assert.That(candidates[index].Id, Is.EqualTo(5));
        Assert.That(near, Is.EqualTo(2));
    }

    [Test]
    public void NoCandidatesGivesNoLeader()
    {
        Assert.That(AutonomousBotGroupCoordinator.ChooseMainBodyLeader([], null, out _), Is.EqualTo(-1));
    }
}
