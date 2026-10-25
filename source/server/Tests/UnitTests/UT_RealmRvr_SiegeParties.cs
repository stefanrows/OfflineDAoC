// Upstream test (shadowofze/OfflineDAoC 0.38c, upstream/main 9da90910 source/server/Tests/UnitTests/UT_SiegeParties.cs) for the RvR world,
// carried in port session S2 (docs/RVR_PORT_MAP.md). It calls RealmRvrEventLayer, the upstream copy behind the
// AutonomousRvrEventLayer facade; the shared records stay on the facade. Local changes are marked "Fork:".
using System;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Siege recruits who answered alone are merged into one party that keeps their reservation (owner 2026-10-07).</summary>
[TestFixture, NonParallelizable]
public class UT_RealmRvr_SiegeParties
{
    [SetUp] public void ResetEvents() => RvrEventTestState.ClearRealm();
    [TearDown] public void ClearEvents() => RvrEventTestState.Clear();

    [Test]
    public void SoloRecruitsMergeIntoOneCommittedParty()
    {
        long now = GameLoop.GameLoopTime;
        string id = Guid.NewGuid().ToString();
        var target = new AutonomousRvrEventLayer.LiveObjective(id, "Party rally", AutonomousRvrEventLayer.Intent.AssaultKeep,
            eRealm.Hibernia, 200, 300000, 400000, 100, false, 0, 0, 10, 2);
        var opener = new AutonomousRvrEventLayer.Force(id + ":warband", eRealm.Albion, 8, 50, 2);
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(opener, [target], now, 0)?.TargetId, Is.EqualTo(id));
        foreach (long member in new long[] { 501, 502, 503 })
            Assert.That(RealmRvrEventLayer.ChooseOrJoin(new AutonomousRvrEventLayer.Force($"rvr-{member}", eRealm.Albion, 1, 50, 0,
                MemberIds: [member]), [target], now, 0)?.TargetId, Is.EqualTo(id));

        var roster = RealmRvrEventLayer.SoloSiegeRosters(now).Single(r => r.EventId == id && r.Realm == eRealm.Albion);
        Assert.That(roster.MemberIds, Is.EquivalentTo(new long[] { 501, 502, 503 }));

        Assert.That(RealmRvrEventLayer.MergeSoloForces(id, eRealm.Albion, [501, 502, 503], "albion-party-1"), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(RealmRvrEventLayer.IsForceCommitted("albion-party-1", now), Is.True);
            Assert.That(RealmRvrEventLayer.IsForceCommitted("rvr-501", now), Is.False);
            Assert.That(RealmRvrEventLayer.KeepPlan("albion-party-1", eRealm.Albion, now)?.TargetId, Is.EqualTo(id));
            Assert.That(RealmRvrEventLayer.SoloSiegeRosters(now).Any(r => r.EventId == id), Is.False);
            // a stale roster (a member left the event meanwhile) is refused, never half-merged
            Assert.That(RealmRvrEventLayer.MergeSoloForces(id, eRealm.Albion, [501, 999], "albion-party-2"), Is.False);
        });
    }
}
