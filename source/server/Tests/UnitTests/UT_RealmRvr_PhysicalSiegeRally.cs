// Upstream test (shadowofze/OfflineDAoC 0.38c, upstream/main 9da90910 source/server/Tests/UnitTests/UT_PhysicalSiegeRally.cs) for the RvR world,
// carried in port session S2 (docs/RVR_PORT_MAP.md). It calls RealmRvrEventLayer, the upstream copy behind the
// AutonomousRvrEventLayer facade; the shared records stay on the facade. Local changes are marked "Fork:".
using System;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture, NonParallelizable]
public class UT_RealmRvr_PhysicalSiegeRally
{
    [SetUp] public void ResetEvents() => RvrEventTestState.ClearRealm();
    [TearDown] public void ClearEvents() => RvrEventTestState.Clear();
    private static (AutonomousRvrEventLayer.LiveObjective Target, AutonomousRvrEventLayer.Force[] Forces) Open(bool relic, long now, int groups = 2)
    {
        string id = Guid.NewGuid().ToString();
        var target = new AutonomousRvrEventLayer.LiveObjective(id, "Physical rally",
            relic ? AutonomousRvrEventLayer.Intent.AssaultRelicKeep : AutonomousRvrEventLayer.Intent.AssaultKeep,
            eRealm.Hibernia, 200, 300000, 400000, 100, relic, 0, 0, 10, 2);
        var forces = Enumerable.Range(0, groups).Select(i => new AutonomousRvrEventLayer.Force(
            id + ":" + i, eRealm.Albion, 8, 50, 2)).ToArray();
        foreach (var force in forces)
            Assert.That(RealmRvrEventLayer.ChooseOrJoin(force, [target], now, 0)?.TargetId, Is.EqualTo(id));
        return (target, forces);
    }
    private static void Attend(AutonomousRvrEventLayer.LiveObjective target, AutonomousRvrEventLayer.Force[] forces, int count, long now)
    {
        for (int i = 0; i < count; i++)
            RealmRvrEventLayer.ReportAttendance(target.Id, forces[i / 8].GroupId, eRealm.Albion, 1000 + i, true, now);
    }

    [TestCase(false)] [TestCase(true)]
    public void EveryRealmConvergesWithoutRequiringPhysicalStaging(bool relic)
    {
        long now = GameLoop.GameLoopTime;
        int attackers = relic ? 170 : 108;
        var (target, forces) = Open(relic, now, (attackers + 7) / 8);
        Attend(target, forces, attackers, now + 1);
        Assert.That(RealmRvrEventLayer.IsRallying(forces[0].GroupId, now + 1), Is.False);
        long ready = now + RealmEventPolicy.EarliestAssaultMilliseconds;
        Attend(target, forces, attackers, ready);
        Assert.That(RealmRvrEventLayer.IsBattleForce(forces[0].GroupId, ready), Is.True, "Opposing attendance cannot hold attackers at a camp.");
        AddDefenders(target, relic ? 96 : 64, ready + 1);
        Assert.That(RealmRvrEventLayer.IsBattleForce(forces[0].GroupId, ready + 1), Is.True);
        Assert.That(RealmRvrEventLayer.GetRallyOrder(forces[0].GroupId, eRealm.Albion, ready + 1), Is.Null);
        var defender = forces[0] with { GroupId = "defender", Realm = eRealm.Hibernia };
        var third = forces[0] with { GroupId = "third", Realm = eRealm.Midgard };
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(defender, [target], ready + 2, 0)?.TargetId, Is.EqualTo(target.Id));
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(third, [target], ready + 2, 0)?.TargetId, Is.EqualTo(target.Id));
        target = target with { UnderAttack = true };
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(defender, [target], ready + 3, 0)?.TargetId, Is.EqualTo(target.Id));
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(third, [target], ready + 3, 0)?.TargetId, Is.EqualTo(target.Id));
        Assert.That(RealmRvrEventLayer.KeepPlan(defender.GroupId, defender.Realm, ready + 3)?.Intent,
            Is.EqualTo(AutonomousRvrEventLayer.Intent.DefendEvent));
        Assert.That(RealmRvrEventLayer.KeepPlan(third.GroupId, third.Realm, ready + 3)?.Intent,
            Is.EqualTo(relic ? AutonomousRvrEventLayer.Intent.AssaultRelicKeep : AutonomousRvrEventLayer.Intent.AssaultKeep),
            "Third faction independently attacks/captures; it never defends the current owner");
        RealmRvrEventLayer.ReportAttendance(target.Id, forces[0].GroupId, eRealm.Albion, 1000, false, ready + 4);
        Assert.That(RealmRvrEventLayer.IsBattleForce(forces[0].GroupId, now + RealmRvrEventLayer.BattleLifetimeMilliseconds - 1), Is.True);
        Assert.That(RealmRvrEventLayer.TryConsumeRelease(forces[0].GroupId, ready + 1 + RealmRvrEventLayer.BattleLifetimeMilliseconds, out string reason), Is.True);
        Assert.That(reason, Does.Contain("defended"));
    }

    [Test]
    public void AnArmyDrivenOffAfterReachingTheKeepEndsTheSiegeAsDefended()
    {
        // Run 2026-10-07: Midgard was wiped at Caer Erasleigh, yet the event held Albion's defenders for four hours.
        long now = GameLoop.GameLoopTime;
        var (target, forces) = Open(false, now, 14);
        long ready = now + RealmEventPolicy.EarliestAssaultMilliseconds;
        Attend(target, forces, 108, ready);
        Assert.That(RealmRvrEventLayer.IsBattleForce(forces[0].GroupId, ready), Is.True);
        Assert.That(RealmRvrEventLayer.GetRallyOrder(forces[0].GroupId, eRealm.Albion, ready + 1), Is.Null);
        long gone = ready + RealmRvrEventLayer.RepelMilliseconds + 30_000;
        RealmRvrEventLayer.GetRallyOrder(forces[0].GroupId, eRealm.Albion, gone);
        Assert.That(RealmRvrEventLayer.TryConsumeRelease(forces[0].GroupId, gone, out string reason), Is.True);
        Assert.That(reason, Does.Contain("Siege defended").And.Contain("no attacker"));
    }

    [TestCase(false)] [TestCase(true)]
    public void ContinuousAssaultExpiresAfterFourHoursEvenIfNobodyArrives(bool relic)
    {
        long now = GameLoop.GameLoopTime;
        var (target, forces) = Open(relic, now);
        long duration = RealmRvrEventLayer.Snapshot().Single().RemainingMilliseconds;
        Assert.That(duration, Is.EqualTo(RealmRvrEventLayer.BattleLifetimeMilliseconds));
        long deadline = now + duration;
        Attend(target, forces, 8, deadline - 1);
        Assert.That(RealmRvrEventLayer.IsBattleForce(forces[0].GroupId, deadline - 1), Is.True);
        foreach (var force in forces)
        {
            Assert.That(RealmRvrEventLayer.TryConsumeRelease(force.GroupId, deadline, out string reason), Is.True);
            Assert.That(reason, Does.Contain("battle timer expired"));
            Assert.That(RealmRvrEventLayer.IsBattleForce(force.GroupId, deadline), Is.False);
        }
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(forces[1], [target], deadline + 1, 1), Is.Null);
        RealmRvrEventLayer.EndTarget(target.Id, deadline + 2);
        foreach (var force in forces) Assert.That(RealmRvrEventLayer.IsForceCommitted(force.GroupId, deadline + 3), Is.False);
    }

    [Test]
    public void MissingLocalCandidateDoesNotCancelACommittedOrOtherRealmEvent()
    {
        long now = GameLoop.GameLoopTime;
        var (target, forces) = Open(false, now);
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(forces[0], [], now + 1, 1)?.TargetId, Is.EqualTo(target.Id));
        var independent = target with { Id = target.Id + "-other", OwningRealm = eRealm.Albion };
        var otherRealm = forces[0] with { GroupId = "independent", Realm = eRealm.Midgard };
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(otherRealm, [independent], now + 2, 0), Is.Null);
        Assert.That(RealmRvrEventLayer.IsForceCommitted(forces[0].GroupId, now + 2), Is.True);
    }

    [Test]
    public void RelicEscortInheritsReactingRealmsAndRemainingBattleClock()
    {
        long now = GameLoop.GameLoopTime;
        var (target, forces) = Open(true, now, 22);
        long ready = now + RealmEventPolicy.EarliestAssaultMilliseconds;
        Attend(target, forces, 170, ready);
        AddDefenders(target, 96, ready);
        var reactors = new[] { eRealm.Midgard, eRealm.Hibernia }.Select(r => forces[0] with { GroupId = target.Id + r, Realm = r }).ToArray();
        foreach (var force in reactors)
            Assert.That(RealmRvrEventLayer.ChooseOrJoin(force, [target with { UnderAttack = true }], ready + 1, 0)?.TargetId, Is.EqualTo(target.Id));
        string carrier = target.Id + "-carrier";
        RealmRvrEventLayer.TransferToRelicCarrier(target.Id, carrier, ready + 60_000);
        Assert.That(RealmRvrEventLayer.CarrierRemainingMilliseconds(carrier, ready + 60_000), Is.EqualTo(RealmRvrEventLayer.BattleLifetimeMilliseconds - (ready - now) - 60_000));
        foreach (var force in forces.Concat(reactors)) Assert.That(RealmRvrEventLayer.IsForceCommitted(force.GroupId, ready + 60_001), Is.True);
        foreach (var force in forces.Concat(reactors))
            Assert.That(RealmRvrEventLayer.ChooseOrJoin(force, [], ready + 60_001, 0)?.TargetId, Is.EqualTo(carrier),
                "A partial route snapshot cannot disband the moving relic escort/interceptors.");
        RealmRvrEventLayer.EndTarget(carrier, ready + 60_002);
        foreach (var force in forces.Concat(reactors)) Assert.That(RealmRvrEventLayer.TryConsumeRelease(force.GroupId, ready + 60_003, out _), Is.True);
    }

    [TestCase(false)] [TestCase(true)]
    public void PendingEventFillsBeforeNewOneAndLateReinforcementsRespectCaps(bool relic)
    {
        long now = GameLoop.GameLoopTime;
        int cap = relic ? RealmRvrEventLayer.RelicAssaultCap : RealmRvrEventLayer.OrdinaryAssaultCap;
        var (target, forces) = Open(relic, now, cap / 8);
        var other = target with { Id = target.Id + "-other" };
        var roam = target with { Id = target.Id + "-roam", Kind = AutonomousRvrEventLayer.Intent.Roam };
        var extra = forces[0] with { GroupId = "extra" };
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(extra, [target, other, roam], now + 1, 0)?.TargetId, Is.EqualTo(roam.Id));
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(extra with { GroupId = "reserve", RoamingReserve = true }, [target, other, roam], now + 2, 0)?.TargetId, Is.EqualTo(roam.Id));
        RealmRvrEventLayer.RemoveForce(forces[^1].GroupId);
        long ready = now + RealmEventPolicy.EarliestAssaultMilliseconds;
        Attend(target, forces, cap - 8, ready);
        AddDefenders(target, relic ? 96 : 64, ready);
        Assert.That(RealmRvrEventLayer.IsBattleForce(forces[0].GroupId, ready), Is.True);
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(extra, [target], ready + 1, 0)?.TargetId, Is.EqualTo(target.Id));
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(extra with { GroupId = "over-cap" }, [target], ready + 2, 0), Is.Null);
    }

    private static void AddDefenders(AutonomousRvrEventLayer.LiveObjective target, int count, long now)
    {
        for (int group = 0; group < (count + 7) / 8; group++)
        {
            var force = new AutonomousRvrEventLayer.Force(target.Id + "-def-" + group, target.OwningRealm, 8, 50, 2);
            Assert.That(RealmRvrEventLayer.ChooseOrJoin(force, [target], now, 0)?.TargetId, Is.EqualTo(target.Id));
            for (int member = 0; member < 8 && group * 8 + member < count; member++)
                RealmRvrEventLayer.ReportAttendance(target.Id, force.GroupId, force.Realm, 2000 + group * 8 + member, true, now);
        }
    }
    [Test]
    public void RelicsRemainRarerAndPveIntermissionBlocksNewRvr()
    {
        var force = new AutonomousRvrEventLayer.Force("weight", eRealm.Albion, 8, 50, 2);
        Assert.That(Enumerable.Range(0, 100).Count(i => RealmRvrEventLayer.ChooseIntent(force, false, true, true, i / 100d) == AutonomousRvrEventLayer.Intent.AssaultRelicKeep), Is.EqualTo(15));
        // Fork: upstream's level-50 goal split (RollLevelFiftyObjective, BotGoalSettings 20/40/40) is not ported; the
        // fork's player types choose the task (AutonomousActivityScheduler), so only the relic weight is checked here.
        var record = new OfflineWorldBotRecord { Level = 50, ObjectiveRvrEligibleUtc = AutonomousObjectiveAssignments.PveCompletionRequired };
        Assert.That(AutonomousObjectiveAssignments.IsRvrEligible(record, DateTime.UtcNow), Is.False);
    }
}
