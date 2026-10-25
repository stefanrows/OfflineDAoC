using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using OfflineDaoc.Configuration;

namespace DOL.UnitTests;

/// <summary>
/// The world-aware bot guild seeding rule (docs/RVR_WORLDS.md): Camlann plans one mixed pool as before, the RvR world
/// plans one pool per realm so every bot guild is single-realm.
/// </summary>
[TestFixture]
public sealed class UT_AutonomousGuildSeeding
{
    private static IEnumerable<eRealm> Roster(int perRealm) =>
        Enumerable.Range(0, perRealm * 3).Select(index => (eRealm)(1 + index % 3));

    private static AutonomousCrewManager.AssignmentCandidate[] Candidates(int perRealm) =>
        Enumerable.Range(1, perRealm * 3).Select(id => new AutonomousCrewManager.AssignmentCandidate(
            id, (eRealm)(1 + (id - 1) % 3), 1 + (id * 7) % 50,
            id % 3 == 0 ? eCharacterClass.Cleric : id % 3 == 1 ? eCharacterClass.Armsman : eCharacterClass.Wizard,
            string.Empty)).ToArray();

    [Test]
    public void CamlannPlansOneMixedPoolOfFifteenGuildsAsBefore()
    {
        AutonomousGuildSeeding.GuildPool[] pools = AutonomousGuildSeeding.PlanPools(WorldModelKind.Camlann, Roster(600));

        Assert.That(pools, Has.Length.EqualTo(1));
        Assert.That(pools[0], Is.EqualTo(new AutonomousGuildSeeding.GuildPool(eRealm.None, 1800, 15, 0)));
        Assert.That(AutonomousGuildSeeding.GuildRealm(WorldModelKind.Camlann, pools[0]), Is.EqualTo(eRealm.None));
        Assert.That(AutonomousGuildSeeding.MaximumManagedGuilds(WorldModelKind.Camlann, pools), Is.EqualTo(15));
        Assert.That(AutonomousGuildSeeding.MayJoin(WorldModelKind.Camlann, eRealm.Albion, eRealm.None), Is.True);
        Assert.That(AutonomousGuildSeeding.GuildServesPool(WorldModelKind.Camlann, eRealm.Midgard, pools[0]), Is.True);
    }

    [Test]
    public void RvrPlansFifteenSingleRealmGuildsPerRealmWithFixedOrdinals()
    {
        AutonomousGuildSeeding.GuildPool[] pools = AutonomousGuildSeeding.PlanPools(WorldModelKind.Realm, Roster(600));

        Assert.That(pools, Is.EqualTo(new[]
        {
            new AutonomousGuildSeeding.GuildPool(eRealm.Albion, 600, 15, 0),
            new AutonomousGuildSeeding.GuildPool(eRealm.Midgard, 600, 15, 15),
            new AutonomousGuildSeeding.GuildPool(eRealm.Hibernia, 600, 15, 30),
        }));
        Assert.That(pools.Select(pool => AutonomousGuildSeeding.GuildRealm(WorldModelKind.Realm, pool)),
            Is.EqualTo(new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }));
        Assert.That(AutonomousGuildSeeding.MaximumManagedGuilds(WorldModelKind.Realm, pools), Is.EqualTo(45));
        Assert.That(AutonomousGuildSeeding.MayJoin(WorldModelKind.Realm, eRealm.Albion, eRealm.Midgard), Is.False);
        Assert.That(AutonomousGuildSeeding.MayJoin(WorldModelKind.Realm, eRealm.Hibernia, eRealm.Hibernia), Is.True);
        Assert.That(AutonomousGuildSeeding.GuildServesPool(WorldModelKind.Realm, eRealm.None, pools[0]), Is.False,
            "an old mixed Camlann guild serves no RvR realm pool");
    }

    [Test]
    public void RvrPoolSizeFollowsEachRealmsRosterAndAnEmptyRealmHasNoPool()
    {
        eRealm[] roster = Enumerable.Repeat(eRealm.Albion, 56).Concat(Enumerable.Repeat(eRealm.Hibernia, 57)).ToArray();

        AutonomousGuildSeeding.GuildPool[] pools = AutonomousGuildSeeding.PlanPools(WorldModelKind.Realm, roster);

        Assert.That(pools.Select(pool => (pool.Realm, pool.GuildCount, pool.FirstOrdinal)),
            Is.EqualTo(new[] { (eRealm.Albion, 3, 0), (eRealm.Hibernia, 6, 30) }));
        Assert.That(AutonomousGuildSeeding.PlanPools(WorldModelKind.Realm, []), Is.Empty);
    }

    [Test]
    public void RvrAssignmentsNeverMixRealmsAndFillEveryTarget()
    {
        AutonomousCrewManager.AssignmentCandidate[] candidates = Candidates(56);
        AutonomousGuildSeeding.GuildPool[] pools = AutonomousGuildSeeding.PlanPools(WorldModelKind.Realm, candidates.Select(c => c.Realm));
        var plan = pools.Select(pool => (pool, AutonomousCrewManager.BuildTargets(
            Enumerable.Range(0, pool.GuildCount).Select(index => $"{pool.Realm}-{index}").ToArray(), pool.BotCount))).ToArray();

        Dictionary<long, string> assignments = AutonomousGuildSeeding.PlanAssignments(WorldModelKind.Realm, plan, [], candidates);

        Assert.That(assignments.Keys, Is.EquivalentTo(candidates.Select(candidate => candidate.BotId)));
        foreach (AutonomousCrewManager.AssignmentCandidate candidate in candidates)
            Assert.That(assignments[candidate.BotId], Does.StartWith(candidate.Realm + "-"), $"bot {candidate.BotId} stays in its realm");
        foreach (AutonomousCrewManager.GuildTarget target in plan.SelectMany(entry => entry.Item2))
            Assert.That(assignments.Count(pair => pair.Value == target.GuildId), Is.EqualTo(target.TargetSize));
        Assert.That(AutonomousGuildSeeding.PlanAssignments(WorldModelKind.Realm, plan, [], candidates), Is.EquivalentTo(assignments),
            "deterministic");
    }

    [Test]
    public void CamlannAssignmentsAreExactlyTheCrewManagersMixedPlan()
    {
        AutonomousCrewManager.AssignmentCandidate[] candidates = Candidates(19);
        AutonomousGuildSeeding.GuildPool[] pools = AutonomousGuildSeeding.PlanPools(WorldModelKind.Camlann, candidates.Select(c => c.Realm));
        AutonomousCrewManager.GuildTarget[] targets = AutonomousCrewManager.BuildTargets(new[] { "a", "b", "c" }, pools[0].BotCount);

        Dictionary<long, string> seeded = AutonomousGuildSeeding.PlanAssignments(WorldModelKind.Camlann, [(pools[0], targets)], [], candidates);

        Assert.That(seeded, Is.EquivalentTo(AutonomousCrewManager.PlanAssignments([], candidates, targets)));
        Assert.That(seeded.Values.Distinct().Select(guild => candidates.Where(c => seeded[c.BotId] == guild).Select(c => c.Realm).Distinct().Count()),
            Is.All.EqualTo(3), "Camlann guilds stay mixed");
    }

    [Test]
    public void FortyFiveRvrGuildNamesAreUnique()
    {
        PlayerTypeMix mix = BotGoalSettings.Defaults.Mix;
        string[] names = Enumerable.Range(0, 45)
            .Select(ordinal => AutonomousCrewManager.NameForOrdinal(ordinal, AutonomousBotIdentity.CharterForOrdinal(ordinal, mix)))
            .ToArray();

        Assert.That(names.Distinct().Count(), Is.EqualTo(45));
    }
}
