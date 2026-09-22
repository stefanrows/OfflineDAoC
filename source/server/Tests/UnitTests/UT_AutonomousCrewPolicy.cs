using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_AutonomousCrewPolicy
{
    [TestCase(0, 0)] [TestCase(1, 1)] [TestCase(2, 2)] [TestCase(3, 3)]
    [TestCase(56, 3)] [TestCase(57, 6)] [TestCase(280, 15)] [TestCase(10_000, 15)]
    public void ManagedGuildCountUsesCappedTripletsWithoutExceedingBots(int bots, int expected) =>
        Assert.That(AutonomousCrewManager.DesiredManagedGuildCount(bots), Is.EqualTo(expected));

    [Test]
    public void OneTripletHasOneTwoFourWeightsAndExactWeightedSizes()
    {
        var targets = AutonomousCrewManager.BuildTargets(new[] { "small", "medium", "large" }, 56);
        Assert.That(targets.Select(target => target.Weight), Is.EqualTo(new[] { 1, 2, 4 }));
        Assert.That(targets.Select(target => target.TargetSize), Is.EqualTo(new[] { 8, 16, 32 }));
    }

    [Test]
    public void DeterministicAssignmentsMixRealmsAndNeverReshuffleFixedMembers()
    {
        var targets = AutonomousCrewManager.BuildTargets(new[] { "a", "b", "c" }, 56);
        var candidates = Enumerable.Range(1, 56).Select(id => new AutonomousCrewManager.AssignmentCandidate(
            id, (eRealm)(1 + (id - 1) % 3), 1 + (id * 7) % 50,
            id % 3 == 0 ? eCharacterClass.Cleric : id % 3 == 1 ? eCharacterClass.Armsman : eCharacterClass.Wizard,
            string.Empty)).ToArray();
        var first = AutonomousCrewManager.PlanAssignments([], candidates, targets);
        var repeated = AutonomousCrewManager.PlanAssignments([], candidates, targets);

        Assert.That(repeated, Is.EquivalentTo(first));
        foreach (var target in targets)
        {
            long[] members = first.Where(pair => pair.Value == target.GuildId).Select(pair => pair.Key).ToArray();
            Assert.That(members, Has.Length.EqualTo(target.TargetSize));
            Assert.That(candidates.Where(candidate => members.Contains(candidate.BotId)).Select(candidate => candidate.Realm).Distinct().Count(),
                Is.EqualTo(3));
        }

        var fixedMembers = candidates.Select(candidate => candidate with { GuildId = first[candidate.BotId] }).ToArray();
        var addition = new AutonomousCrewManager.AssignmentCandidate(57, eRealm.Hibernia, 12, eCharacterClass.Druid, string.Empty);
        var incremental = AutonomousCrewManager.PlanAssignments(fixedMembers, new[] { addition },
            AutonomousCrewManager.BuildTargets(new[] { "a", "b", "c" }, 57));
        Assert.That(incremental.Keys, Is.EqualTo(new[] { 57L }));
    }

    [Test]
    public void PlayerAndHumanOccupiedGuildsAreProtectedAndMappingHelpersAreIdempotent()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousCrewManager.IsManagedMembership(string.Empty, false, false), Is.True);
            Assert.That(AutonomousCrewManager.IsManagedMembership("player", false, false), Is.False);
            Assert.That(AutonomousCrewManager.IsManagedMembership("generated", true, true), Is.False);
            Assert.That(AutonomousCrewManager.IsManagedMembership("generated", true, false), Is.True);
            Assert.That(AutonomousCrewManager.MappedKeepOwner("old", "old", "survivor"), Is.EqualTo("survivor"));
            Assert.That(AutonomousCrewManager.MappedKeepOwner("survivor", "old", "survivor"), Is.EqualTo("survivor"));
            Assert.That(AutonomousCrewManager.MappingNeedsCompletion("Planned"), Is.True);
            Assert.That(AutonomousCrewManager.MappingNeedsCompletion("Completed"), Is.False);
            Assert.That(AutonomousCrewManager.MappingNeedsCompletion("Completed", sourceStillExists: true), Is.True);
        });
    }

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
    public void LoginBalancerPrefersCompatibleCohortAndMissingRole()
    {
        var candidates = new[]
        {
            new AutonomousCrewLoginBalancer.Candidate(10, "crew", 30, 1, eCharacterClass.Wizard),
            new AutonomousCrewLoginBalancer.Candidate(11, "crew", 31, 1, eCharacterClass.Cleric),
            new AutonomousCrewLoginBalancer.Candidate(12, "crew", 45, 2, eCharacterClass.Armsman),
        };
        var active = new[] { new AutonomousCrewLoginBalancer.Candidate(1, "crew", 30, 1, eCharacterClass.Armsman) };
        Assert.That(AutonomousCrewLoginBalancer.SelectNext(candidates,
            new Dictionary<string, int> { ["crew"] = 1 }, active), Is.EqualTo(11));
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
