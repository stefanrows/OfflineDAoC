// Upstream test (shadowofze/OfflineDAoC 0.38c, upstream/main 9da90910 source/server/Tests/UnitTests/UT_AutonomousRvrEventLayer.cs) for the RvR world's event layer,
// carried in port session S1 (docs/RVR_PORT_MAP.md). It calls RealmRvrEventLayer, the upstream copy behind the
// AutonomousRvrEventLayer facade; the shared records stay on the facade. Local changes are marked "Fork:".
using System.Collections.Generic;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture, NonParallelizable]
public sealed class UT_RealmRvr_AutonomousRvrEventLayer
{
    [SetUp] public void ResetEvents() => RvrEventTestState.ClearRealm();
    [TearDown] public void ClearEvents() => RvrEventTestState.Clear();
    [Test]
    public void SmallForcesFavorHuntOrRoamWhileLargeHighLevelForcesCanAssault()
    {
        var small = new AutonomousRvrEventLayer.Force("small", eRealm.Albion, 3, 30, 1);
        var large = new AutonomousRvrEventLayer.Force("large", eRealm.Albion, 8, 50, 2);

        Assert.That(RealmRvrEventLayer.ChooseIntent(small, true, true, true, 0), Is.EqualTo(AutonomousRvrEventLayer.Intent.HuntEnemy));
        Assert.That(RealmRvrEventLayer.ChooseIntent(large, true, true, true, 0), Is.EqualTo(AutonomousRvrEventLayer.Intent.AssaultRelicKeep));
        Assert.That(RealmRvrEventLayer.MajorAssaultWeight(8, 50), Is.GreaterThan(RealmRvrEventLayer.MajorAssaultWeight(3, 30)));
    }

    [Test]
    public void SoloLowLevelAndUnsuppliedForcesCannotOpenKeepAssaults()
    {
        var solo = new AutonomousRvrEventLayer.Force("solo", eRealm.Albion, 1, 50, 0, true);
        var low = new AutonomousRvrEventLayer.Force("low", eRealm.Albion, 8, 34, 2, true);
        var unsupplied = new AutonomousRvrEventLayer.Force("poor", eRealm.Albion, 8, 50, 2, false);

        Assert.Multiple(() =>
        {
            Assert.That(RealmRvrEventLayer.ChooseIntent(solo, false, true, true, 0), Is.EqualTo(AutonomousRvrEventLayer.Intent.Roam));
            Assert.That(RealmRvrEventLayer.ChooseIntent(low, false, true, true, 0), Is.EqualTo(AutonomousRvrEventLayer.Intent.Roam));
            Assert.That(RealmRvrEventLayer.ChooseIntent(unsupplied, false, true, true, 0), Is.EqualTo(AutonomousRvrEventLayer.Intent.Roam));
        });
    }

    [Test]
    public void OpposingFrontierBotsAreEligibleOnlyWhenNormalServerCombatAllowsIt()
    {
        // Fork: the fork's IsEligible takes "enemy combatant" (PvpCombatant.AreAllied, by realm in the RvR world)
        // instead of the two realms; the realm pairs below are upstream's.
        static bool Enemy(eRealm actor, eRealm target) => actor != eRealm.None && target != eRealm.None && actor != target;
        Assert.That(AutonomousRvrTargetPolicy.IsEligible(Enemy(eRealm.Albion, eRealm.Midgard),
            true, true, true, false, true), Is.True);
        Assert.That(AutonomousRvrTargetPolicy.IsEligible(Enemy(eRealm.Albion, eRealm.Albion),
            true, true, true, false, true), Is.False);
        Assert.That(AutonomousRvrTargetPolicy.IsEligible(Enemy(eRealm.Albion, eRealm.Midgard),
            true, true, true, true, true), Is.False);
        Assert.That(AutonomousRvrTargetPolicy.IsEligible(Enemy(eRealm.Albion, eRealm.Midgard),
            true, true, true, false, false), Is.False);
    }

    [Test]
    public void ForcedSiegeTakesTheArmyOfAnUnstartedAutomaticSiege()
    {
        // Run 7 (2026-10-14): a forced relic siege got no attackers; the realm's warbands were committed to an
        // automatic keep siege still mustering.
        RealmRvrEventLayer.StagedAssault = true;
        var keep = new AutonomousRvrEventLayer.LiveObjective("keep-auto", "Dun Crauchon", AutonomousRvrEventLayer.Intent.AssaultKeep,
            eRealm.Hibernia, 163, 100, 100, 0, false, 0, 0, 2, 2);
        var relic = new AutonomousRvrEventLayer.LiveObjective("relic-forced", "Dun Dagda", AutonomousRvrEventLayer.Intent.AssaultRelicKeep,
            eRealm.Hibernia, 163, 300, 300, 0, true, 0, 0, 2, 2);
        var force = new AutonomousRvrEventLayer.Force("a", eRealm.Midgard, 8, 50, 2);
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(force, new[] { keep }, 1, 0).TargetId, Is.EqualTo("keep-auto"));

        Assert.That(RealmRvrEventLayer.ForceStart(relic, eRealm.Midgard, 2, out _), Is.True);

        Assert.That(System.Linq.Enumerable.Select(RealmRvrEventLayer.Snapshot(), e => e.TargetId), Is.EquivalentTo(new[] { "relic-forced" }));
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(force, new[] { keep, relic }, 3, 0).TargetId, Is.EqualTo("relic-forced"));
    }

    [Test]
    public void MusteringArmyIsAnEventForceBeforeItsBattle()
    {
        // Run 8: group rendezvous held a mustering relic army because only a started battle exempted it.
        RealmRvrEventLayer.StagedAssault = true;
        var keep = new AutonomousRvrEventLayer.LiveObjective("keep-muster", "Dun Crauchon", AutonomousRvrEventLayer.Intent.AssaultKeep,
            eRealm.Hibernia, 163, 100, 100, 0, false, 0, 0, 2, 2);
        var force = new AutonomousRvrEventLayer.Force("m", eRealm.Midgard, 8, 50, 2);
        RealmRvrEventLayer.ChooseOrJoin(force, new[] { keep }, 1, 0);
        Assert.That(RealmRvrEventLayer.IsBattleForce("m", 2), Is.False);
        Assert.That(RealmRvrEventLayer.IsEventForce("m", 2), Is.True);
        Assert.That(RealmRvrEventLayer.IsEventForce("other", 2), Is.False);
    }

    [Test]
    public void KeepAssaultIsSharedButParticipationIsCapped()
    {
        var keep = new AutonomousRvrEventLayer.LiveObjective("keep-1", "Dun Crauchon", AutonomousRvrEventLayer.Intent.AssaultKeep,
            eRealm.Hibernia, 163, 100, 100, 0, false, 0, 0, 2, 2);
        var first = new AutonomousRvrEventLayer.Force("a", eRealm.Albion, 8, 50, 2);
        var second = new AutonomousRvrEventLayer.Force("b", eRealm.Albion, 8, 50, 2);

        AutonomousRvrEventLayer.Plan opened = RealmRvrEventLayer.ChooseOrJoin(first, new[] { keep }, 1, 0);
        AutonomousRvrEventLayer.Plan joined = RealmRvrEventLayer.ChooseOrJoin(second, new[] { keep }, 2, 0);

        Assert.That(opened.IsSharedEvent, Is.True);
        Assert.That(joined.TargetId, Is.EqualTo("keep-1"));
    }

    [TestCase(49, 49)] [TestCase(50, 49)]
    public void UnderFiftyOrMixedLevelForceCannotJoinKeepOrRelicEvents(int average, int minimum)
    {
        var keep = new AutonomousRvrEventLayer.LiveObjective("keep-50", "Test keep", AutonomousRvrEventLayer.Intent.AssaultKeep,
            eRealm.Hibernia, 163, 100, 100, 0, false, 0, 0, 2, 2);
        var carrier = new AutonomousRvrEventLayer.LiveObjective("carrier-50", "Relic carrier", AutonomousRvrEventLayer.Intent.HuntEnemy,
            eRealm.Hibernia, 163, 100, 100, 0, false, 1, 0, 0, 0, true);
        var roam = new AutonomousRvrEventLayer.LiveObjective("roam-50", "Patrol", AutonomousRvrEventLayer.Intent.Roam,
            eRealm.Hibernia, 163, 200, 200, 0, false, 0, 0, 0, 0);
        var force = new AutonomousRvrEventLayer.Force("mixed-level", eRealm.Albion, 8, average, 2, MinimumMemberLevel: minimum);
        var plan = RealmRvrEventLayer.ChooseOrJoin(force, new[] { keep, carrier, roam }, 100, 0);
        Assert.That(plan.IsSharedEvent, Is.False);
        Assert.That(plan.TargetId, Is.EqualTo("roam-50"));
    }

    [Test]
    public void ActiveEventAttractsForcesButKeepsAHighRollRoamingReserve()
    {
        var keep = new AutonomousRvrEventLayer.LiveObjective("keep-reserve", "Dun Bolg", AutonomousRvrEventLayer.Intent.AssaultKeep,
            eRealm.Hibernia, 163, 100, 100, 0, false, 0, 0, 2, 2);
        var roam = new AutonomousRvrEventLayer.LiveObjective("roam-reserve", "Bolg frontier", AutonomousRvrEventLayer.Intent.Roam,
            eRealm.Hibernia, 163, 200, 200, 0, false, 0, 0, 0, 0);
        var opener = new AutonomousRvrEventLayer.Force("reserve-open", eRealm.Albion, 8, 50, 2);
        var joiner = new AutonomousRvrEventLayer.Force("reserve-join", eRealm.Albion, 8, 50, 2);
        var decliner = new AutonomousRvrEventLayer.Force("reserve-decline", eRealm.Albion, 2, 20, 0);

        Assert.That(RealmRvrEventLayer.ChooseOrJoin(opener, new[] { keep, roam }, 100, 0).IsSharedEvent, Is.True);
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(joiner, new[] { keep, roam }, 101, 0).TargetId, Is.EqualTo("keep-reserve"));
        AutonomousRvrEventLayer.Plan declined = RealmRvrEventLayer.ChooseOrJoin(decliner, new[] { keep, roam }, 102, 1);

        Assert.Multiple(() =>
        {
            Assert.That(RealmRvrEventLayer.ShouldJoinActiveEvent(joiner, 8, RealmRvrEventLayer.OrdinaryAssaultCap, false, false, 0), Is.True);
            Assert.That(RealmRvrEventLayer.ShouldJoinActiveEvent(decliner, 16, RealmRvrEventLayer.OrdinaryAssaultCap, false, false, 1), Is.False);
            Assert.That(declined.IsSharedEvent, Is.False);
            Assert.That(declined.TargetId, Is.EqualTo("roam-reserve"));
        });
    }

    [Test]
    public void CarrierEventAllowsAllThreeRealmsButCapsEachRealm()
    {
        var carrier = new AutonomousRvrEventLayer.LiveObjective("carrier-1", "Strength relic carrier", AutonomousRvrEventLayer.Intent.HuntEnemy,
            eRealm.Hibernia, 163, 100, 100, 0, false, 1, 0, 0, 0, true);
        var escort = new AutonomousRvrEventLayer.Force("hib", eRealm.Hibernia, 6, 50, 1);
        var alb = new AutonomousRvrEventLayer.Force("alb", eRealm.Albion, 6, 50, 1);
        var mid = new AutonomousRvrEventLayer.Force("mid", eRealm.Midgard, 6, 50, 1);

        Assert.That(RealmRvrEventLayer.ChooseOrJoin(escort, new[] { carrier }, 10, 0).Intent, Is.EqualTo(AutonomousRvrEventLayer.Intent.DefendEvent));
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(alb, new[] { carrier }, 11, 0).Intent, Is.EqualTo(AutonomousRvrEventLayer.Intent.HuntEnemy));
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(mid, new[] { carrier }, 12, 0).Intent, Is.EqualTo(AutonomousRvrEventLayer.Intent.HuntEnemy));
    }

    [Test]
    public void AuthoritativeZonePointEdgesRequireDistinctPersistedEndpoints()
    {
        Assert.That(AutonomousWorldBotController.IsAuthoritativeZonePointEdge(new DbZonePoint { SourceRegion = 163, TargetRegion = 161 }), Is.True);
        Assert.That(AutonomousWorldBotController.IsAuthoritativeZonePointEdge(new DbZonePoint { SourceRegion = 163, TargetRegion = 163 }), Is.False);
        Assert.That(AutonomousWorldBotController.IsAuthoritativeZonePointEdge(new DbZonePoint { SourceRegion = 0, TargetRegion = 161 }), Is.False);
    }
}
