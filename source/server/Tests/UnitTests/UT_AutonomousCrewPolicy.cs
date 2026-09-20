using System.Collections.Generic;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_AutonomousCrewPolicy
{
    [Test]
    public void LoginBalancerUsesCrewLoadWithoutRealmInput()
    {
        var candidates = new[]
        {
            new AutonomousCrewLoginBalancer.Candidate(10, "crew-a"),
            new AutonomousCrewLoginBalancer.Candidate(20, "crew-b"),
        };
        var activeAndPending = new Dictionary<string, int>
        {
            ["crew-a"] = 3,
            ["crew-b"] = 0,
        };

        Assert.That(AutonomousCrewLoginBalancer.SelectNext(candidates, activeAndPending), Is.EqualTo(20));
    }

    [Test]
    public void LoginBalancerTreatsMissingIdentityAsOneUnassignedCrew()
    {
        var candidates = new[]
        {
            new AutonomousCrewLoginBalancer.Candidate(10, string.Empty),
            new AutonomousCrewLoginBalancer.Candidate(20, string.Empty),
        };

        Assert.That(AutonomousCrewLoginBalancer.SelectNext(candidates, new Dictionary<string, int>()), Is.EqualTo(10));
    }

    [Test]
    public void CrewNamesStayUniqueAcrossTheFirstGeneratedBatch()
    {
        string[] names =
        {
            AutonomousCrewManager.NameForOrdinal(0),
            AutonomousCrewManager.NameForOrdinal(1),
            AutonomousCrewManager.NameForOrdinal(AutonomousCrewManager.MaximumCrewSize),
        };

        Assert.Multiple(() =>
        {
            Assert.That(names[0], Does.StartWith(AutonomousCrewManager.CrewNamePrefix));
            Assert.That(names[0], Is.Not.EqualTo(names[1]));
            Assert.That(names[1], Is.Not.EqualTo(names[2]));
        });
    }

    [Test]
    public void NewBotRecordsDefaultToOrdinaryCrewRank()
    {
        var record = new OfflineWorldBotRecord();

        Assert.Multiple(() =>
        {
            Assert.That(record.GuildId, Is.Empty);
            Assert.That(record.GuildRank, Is.EqualTo(9));
        });
    }
}
