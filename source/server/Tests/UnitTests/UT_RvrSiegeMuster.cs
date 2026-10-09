using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// Bug 75: an attacking warband musters on its leader, marches together through
/// one frontier passage, and the siege's idle clocks count from its departure.
/// Every wait is bounded, so no member can hold a siege hostage.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_RvrSiegeMuster
{
    private EpicTestServerScope _server;
    private const long Minute = 60_000;

    [SetUp]
    public void SetUp()
    {
        _server = new EpicTestServerScope();
        DOL.GS.Tests.RvrEventTestState.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        DOL.GS.Tests.RvrEventTestState.Clear();
        _server.Dispose();
    }

    private static System.Collections.Generic.Dictionary<string, int> Bucket(string targetId, string name)
    {
        var events = (System.Collections.IDictionary)typeof(AutonomousRvrEventLayer)
            .GetField("Events", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        object active = events[targetId];
        return (System.Collections.Generic.Dictionary<string, int>)active.GetType().GetField(name).GetValue(active);
    }

    private static AutonomousRvrEventLayer.LiveObjective OpenSiege(string id, long start)
    {
        var target = new AutonomousRvrEventLayer.LiveObjective(id, "Blendrake Faste",
            AutonomousRvrEventLayer.Intent.AssaultKeep, eRealm.Midgard, 100, 100_000, 100_000, 0, false, 0, 0, 4, 2);
        Assert.That(AutonomousRvrEventLayer.ForceStart(target, eRealm.Hibernia, start, out string reason), Is.True, reason);
        return target;
    }

    private static void Sweep(long now) => AutonomousRvrEventLayer.TryConsumeRelease("sweep-only", now, out _);

    // ---- Pure policy -----------------------------------------------------

    [TestCase(8, 6)] [TestCase(6, 5)] [TestCase(4, 3)] [TestCase(3, 3)] [TestCase(2, 2)] [TestCase(1, 1)]
    public void QuorumIsThreeQuartersOfTheWarband(int assigned, int quorum) =>
        Assert.That(AutonomousRvrSiegeMuster.Quorum(assigned), Is.EqualTo(quorum));

    [Test]
    public void ForceDepartsWhenEveryLivingMemberIsPresent()
    {
        Assert.That(AutonomousRvrSiegeMuster.Decide(8, 8, 8, 0, 0), Is.EqualTo(AutonomousRvrSiegeMuster.Decision.Depart));
        Assert.That(AutonomousRvrSiegeMuster.Decide(7, 7, 8, 0, 0), Is.EqualTo(AutonomousRvrSiegeMuster.Decision.Depart),
            "one member dead and not waited for");
    }

    [Test]
    public void ForceWaitsForStragglersOnlyForABoundedTime()
    {
        // Six of eight are present, two are on their way.
        Assert.That(AutonomousRvrSiegeMuster.Decide(8, 6, 8, 0, Minute), Is.EqualTo(AutonomousRvrSiegeMuster.Decision.Wait));
        Assert.That(AutonomousRvrSiegeMuster.Decide(8, 6, 8, 0, AutonomousRvrSiegeMuster.QuorumWaitMilliseconds),
            Is.EqualTo(AutonomousRvrSiegeMuster.Decision.Depart), "a quorum leaves after the quorum wait");
        // Five of eight never make a quorum, but the muster is not endless either.
        Assert.That(AutonomousRvrSiegeMuster.Decide(8, 5, 8, 0, 9 * Minute), Is.EqualTo(AutonomousRvrSiegeMuster.Decision.Wait));
        Assert.That(AutonomousRvrSiegeMuster.Decide(8, 5, 8, 0, AutonomousRvrSiegeMuster.MaximumWaitMilliseconds),
            Is.EqualTo(AutonomousRvrSiegeMuster.Decision.Depart), "half the warband goes at the hard end");
    }

    [Test]
    public void ForceThatNeverGathersFails()
    {
        Assert.That(AutonomousRvrSiegeMuster.Decide(8, 2, 8, 0, AutonomousRvrSiegeMuster.MaximumWaitMilliseconds),
            Is.EqualTo(AutonomousRvrSiegeMuster.Decision.Fail));
        Assert.That(AutonomousRvrSiegeMuster.Decide(3, 3, 8, 0, AutonomousRvrSiegeMuster.MaximumWaitMilliseconds),
            Is.EqualTo(AutonomousRvrSiegeMuster.Decision.Fail), "five dead, three left is not a siege");
        Assert.That(AutonomousRvrSiegeMuster.Decide(8, 0, 8, 0, 20 * Minute), Is.EqualTo(AutonomousRvrSiegeMuster.Decision.Fail));
    }

    [Test]
    public void ForceAlreadyNearTheKeepSkipsTheMuster()
    {
        Assert.That(AutonomousRvrSiegeMuster.CanSkipMuster(true, 9_000), Is.True);
        Assert.That(AutonomousRvrSiegeMuster.CanSkipMuster(true, 40_000), Is.False);
        Assert.That(AutonomousRvrSiegeMuster.CanSkipMuster(false, 100), Is.False, "another region: it must port first");
    }

    [Test]
    public void MarchingMembersStayOnTheLeaderAndAReleasedMemberRejoinsIt()
    {
        // Leader still on the road: everybody follows, near or far.
        Assert.That(AutonomousRvrSiegeMuster.FollowsLeader(false, 100, 100, 200), Is.True);
        Assert.That(AutonomousRvrSiegeMuster.FollowsLeader(false, 200, 100, double.PositiveInfinity), Is.True);
        // Leader at the walls: members beside it fight, a member far away or in another region rejoins.
        Assert.That(AutonomousRvrSiegeMuster.FollowsLeader(true, 100, 100, 800), Is.False);
        Assert.That(AutonomousRvrSiegeMuster.FollowsLeader(true, 100, 100, 6_000), Is.True);
        Assert.That(AutonomousRvrSiegeMuster.FollowsLeader(true, 200, 100, double.PositiveInfinity), Is.True);
        Assert.That(AutonomousRvrSiegeMuster.LeaderAtKeep(100, new Vector2(100_000, 102_000), 100, new Vector2(100_000, 100_000)), Is.True);
        Assert.That(AutonomousRvrSiegeMuster.LeaderAtKeep(1, new Vector2(100_000, 100_000), 100, new Vector2(100_000, 100_000)), Is.False);
    }

    [Test]
    public void IdleClocksStandStillWhileMusteringAndCountFromDeparture()
    {
        Assert.That(AutonomousRvrSiegeMuster.IdleClocksRun(true), Is.False);
        Assert.That(AutonomousRvrSiegeMuster.IdleClocksRun(false), Is.True);
        Assert.That(AutonomousRvrSiegeMuster.ClockOrigin(1_000, 9_000), Is.EqualTo(9_000));
        Assert.That(AutonomousRvrSiegeMuster.ClockOrigin(12_000, 9_000), Is.EqualTo(12_000), "later activity is never moved back");
    }

    [Test]
    public void KeepRouteBlockGrowsWithRepeatedFailuresToEightHours()
    {
        Assert.That(AutonomousRvrSiegeMuster.RouteBlockMilliseconds(1), Is.EqualTo(60 * Minute));
        Assert.That(AutonomousRvrSiegeMuster.RouteBlockMilliseconds(2), Is.EqualTo(120 * Minute));
        Assert.That(AutonomousRvrSiegeMuster.RouteBlockMilliseconds(3), Is.EqualTo(240 * Minute));
        Assert.That(AutonomousRvrSiegeMuster.RouteBlockMilliseconds(4), Is.EqualTo(480 * Minute));
        Assert.That(AutonomousRvrSiegeMuster.RouteBlockMilliseconds(9), Is.EqualTo(480 * Minute));
    }

    // ---- One passage for the whole warband --------------------------------

    [Test]
    public void MixedRealmWarbandUsesOnePassageThroughOnePorter()
    {
        eRealm[] members = [eRealm.Albion, eRealm.Hibernia, eRealm.Midgard, eRealm.Albion];
        const eRealm leader = eRealm.Albion;
        // Every member picks its passage with the force's realm, at the same porter.
        var passages = members.Select(own => AutonomousFrontierTransport.ChoosePassage(
            AutonomousFrontierTransport.ForcePassageRealm(own, leader, true), 1, 100, _ => true)).ToArray();

        Assert.That(passages.Select(p => (p.Medallion, p.Location.Name)).Distinct().Count(), Is.EqualTo(1));
        Assert.That(passages[0].Location.Name, Is.EqualTo("Odin Alb"));
        // Before, each realm chose its own: three different landings.
        var alone = members.Select(own => AutonomousFrontierTransport.ChoosePassage(own, 1, 100, _ => true).Location.Name).Distinct().ToArray();
        Assert.That(alone, Is.EquivalentTo(new[] { "Odin Alb", "Odin Hib", "Home Mid" }));
    }

    [Test]
    public void MembersMayBoardTheirForcesPassageOnlyForRvr()
    {
        var passage = AutonomousFrontierTransport.Destination(eRealm.Albion, 100);
        Assert.That(AutonomousFrontierTransport.CanBoardForObjective(eRealm.Midgard, eRealm.Albion, eAutonomousObjectiveKind.RvR, passage), Is.True);
        Assert.That(AutonomousFrontierTransport.CanBoardForObjective(eRealm.Hibernia, eRealm.Albion, eAutonomousObjectiveKind.RvR, passage), Is.True);
        // Not for PvE, and never a medallion that no member's realm could use.
        Assert.That(AutonomousFrontierTransport.CanBoardForObjective(eRealm.Midgard, eRealm.Albion, eAutonomousObjectiveKind.GroupPve, passage), Is.False);
        Assert.That(AutonomousFrontierTransport.CanBoardForObjective(eRealm.Midgard, eRealm.Midgard, eAutonomousObjectiveKind.RvR, passage), Is.False);
        Assert.That(AutonomousFrontierTransport.CanBoardForObjective(eRealm.Midgard, eRealm.Albion, eAutonomousObjectiveKind.RvR,
            new AutonomousFrontierTransport.Passage(100, "keep_necklace", passage.Location)), Is.False);
    }

    [Test]
    public void SoloBotsAndPveKeepTheirOwnRealmPassage()
    {
        Assert.That(AutonomousFrontierTransport.ForcePassageRealm(eRealm.Midgard, eRealm.Albion, false), Is.EqualTo(eRealm.Midgard));
        Assert.That(AutonomousFrontierTransport.ForcePassageRealm(eRealm.Midgard, null, true), Is.EqualTo(eRealm.Midgard));
        Assert.That(AutonomousFrontierTransport.ForcePassageRealm(eRealm.Midgard, eRealm.None, true), Is.EqualTo(eRealm.Midgard));
    }

    [Test]
    public void OnlyTheSamePassageBoardsTogether()
    {
        var alb = AutonomousFrontierTransport.Destination(eRealm.Albion, 100);
        var hib = AutonomousFrontierTransport.Destination(eRealm.Hibernia, 100);
        Assert.That(AutonomousFrontierTransport.SamePassage(alb, AutonomousFrontierTransport.Destination(eRealm.Albion, 100)), Is.True);
        Assert.That(AutonomousFrontierTransport.SamePassage(alb, hib), Is.False, "same medallion, different landing");
        Assert.That(AutonomousFrontierTransport.SamePassage(alb, null), Is.False);
    }

    // ---- Event layer: muster, departure, clocks ----------------------------

    [Test]
    public void OnlyAttackingWarbandsOfAStartedSiegeMuster()
    {
        const long start = 200_000_000;
        var target = OpenSiege("rvr-keep-9101", start);
        Bucket(target.Id, "Attackers").Add("attackers", 8);
        Bucket(target.Id, "Defenders").Add("defenders", 8);
        Bucket(target.Id, "ThirdRealm").Add("third", 8);

        Assert.That(AutonomousRvrEventLayer.MusterPhaseOf("attackers"), Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Mustering));
        Assert.That(AutonomousRvrEventLayer.MusterPhaseOf("third"), Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Mustering));
        Assert.That(AutonomousRvrEventLayer.MusterPhaseOf("defenders"), Is.EqualTo(AutonomousRvrSiegeMuster.Phase.None));
        Assert.That(AutonomousRvrEventLayer.MusterPhaseOf("stranger"), Is.EqualTo(AutonomousRvrSiegeMuster.Phase.None));
        Assert.That(AutonomousRvrEventLayer.ReportMuster("defenders", 8, 8, 8, false, start), Is.EqualTo(AutonomousRvrSiegeMuster.Phase.None));
    }

    [Test]
    public void GatheredForceWaitsForSupplyTripAndReportsSupplyFailureSeparately()
    {
        const long start = 205_000_000;
        var target = OpenSiege("rvr-keep-9110", start);
        Bucket(target.Id, "Attackers").Add("supplied", 8);
        Bucket(target.Id, "Attackers").Add("unsupplied", 8);
        Assert.That(AutonomousRvrEventLayer.ReportMuster("supplied", 8, 8, 8, true, start, suppliesReady: false),
            Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Mustering));
        Assert.That(AutonomousRvrEventLayer.ReportMuster("supplied", 8, 8, 8, true, start + 30_000, suppliesReady: true),
            Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Marching));
        Assert.That(AutonomousRvrEventLayer.ReportMuster("unsupplied", 8, 8, 8, false, start, suppliesReady: false),
            Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Mustering));
        Assert.That(AutonomousRvrEventLayer.ReportMuster("unsupplied", 8, 8, 8, false,
            start + AutonomousRvrSiegeMuster.MaximumWaitMilliseconds, suppliesReady: false),
            Is.EqualTo(AutonomousRvrSiegeMuster.Phase.None));
        Assert.That(AutonomousRvrEventLayer.TryConsumeRelease("unsupplied", start + AutonomousRvrSiegeMuster.MaximumWaitMilliseconds,
            out string reason), Is.True);
        Assert.That(reason, Is.EqualTo("Siege supply trip did not finish before departure"));
        Assert.That(AutonomousRvrEventLayer.MusterPhaseOf("supplied"), Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Marching));
    }

    [Test]
    public void FullFirstReportWaitsForPreparationWithoutExtendingTheMusterDeadline()
    {
        const long start = 208_000_000;
        var target = OpenSiege("rvr-keep-9111", start);
        Bucket(target.Id, "Attackers").Add("full", 8);
        Assert.That(AutonomousRvrEventLayer.ReportMuster("full", 8, 8, 8, false, start),
            Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Mustering));
        Assert.That(AutonomousRvrEventLayer.ReportMuster("full", 8, 8, 8, false,
            start + AutonomousRvrSiegeMuster.PreparationWindowMilliseconds - 1),
            Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Mustering));
        Assert.That(AutonomousRvrEventLayer.ReportMuster("full", 8, 8, 8, false,
            start + AutonomousRvrSiegeMuster.PreparationWindowMilliseconds),
            Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Marching));
    }

    [Test]
    public void GatheredForceDeparts_AndStaysDeparted()
    {
        const long start = 210_000_000;
        var target = OpenSiege("rvr-keep-9102", start);
        Bucket(target.Id, "Attackers").Add("wb", 8);

        Assert.That(AutonomousRvrEventLayer.ReportMuster("wb", 8, 3, 8, false, start), Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Mustering));
        Assert.That(AutonomousRvrEventLayer.ReportMuster("wb", 8, 8, 8, false, start + 30_000), Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Marching));
        // Members drifting apart on the road do not send it back to the muster.
        Assert.That(AutonomousRvrEventLayer.ReportMuster("wb", 8, 2, 8, false, start + 5 * Minute), Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Marching));
        Assert.That(AutonomousRvrEventLayer.MusterPhaseOf("wb"), Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Marching));
        Assert.That(AutonomousRvrEventLayer.MusterCounts(target.Id), Is.EqualTo((0, 1)));
    }

    [Test]
    public void ForceNearTheKeepWaitsForARealQuorumAndPreparation()
    {
        const long start = 215_000_000;
        var target = OpenSiege("rvr-keep-9103", start);
        Bucket(target.Id, "Attackers").Add("near", 8);
        Assert.That(AutonomousRvrEventLayer.ReportMuster("near", 8, 1, 8, true, start), Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Mustering),
            "A leader near the keep cannot skip the missing warband's muster");
        Assert.That(AutonomousRvrEventLayer.ReportMuster("near", 8, 6, 8, true, start + 1), Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Mustering),
            "Supply preparation must get a turn before even a nearby quorum departs");
        Assert.That(AutonomousRvrEventLayer.ReportMuster("near", 8, 6, 8, true,
            start + AutonomousRvrSiegeMuster.PreparationWindowMilliseconds), Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Marching));
    }

    [Test]
    public void SiegeCountsItsIdleClocksFromDepartureNotFromOpening()
    {
        const long start = 220_000_000;
        var target = OpenSiege("rvr-keep-9104", start);
        Bucket(target.Id, "Attackers").Add("late", 8);

        // The force only begins to muster ten minutes after the siege opened. Five of eight
        // are with the leader: below the quorum, so it waits for the rest of its ten minutes.
        Assert.That(AutonomousRvrEventLayer.ReportMuster("late", 8, 5, 8, false, start + 10 * Minute),
            Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Mustering));
        Sweep(start + 16 * Minute);
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.True,
            "sixteen minutes after opening, but the warband is still mustering: the fifteen-minute rule stands still");
        Assert.That(AutonomousRvrEventLayer.ReportMuster("late", 8, 5, 8, false, start + 19 * Minute),
            Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Mustering));
        Assert.That(AutonomousRvrEventLayer.ReportMuster("late", 8, 5, 8, false, start + 20 * Minute),
            Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Marching), "half the warband leaves at the hard end");

        Sweep(start + 20 * Minute + 14 * Minute);
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.True, "fourteen minutes after departure");
        Sweep(start + 20 * Minute + 16 * Minute);
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.False, "the clock ran from the departure");
    }

    [Test]
    public void WarbandThatNeverMustersEndsTheSiegeWithAClearReason()
    {
        const long start = 230_000_000;
        var target = OpenSiege("rvr-keep-9105", start);
        Bucket(target.Id, "Attackers").Add("scattered", 8);

        Assert.That(AutonomousRvrEventLayer.ReportMuster("scattered", 8, 2, 8, false, start), Is.EqualTo(AutonomousRvrSiegeMuster.Phase.Mustering));
        Assert.That(AutonomousRvrEventLayer.ReportMuster("scattered", 8, 2, 8, false, start + AutonomousRvrSiegeMuster.MaximumWaitMilliseconds),
            Is.EqualTo(AutonomousRvrSiegeMuster.Phase.None));

        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.False);
        Assert.That(AutonomousRvrEventLayer.TryConsumeRelease("scattered", start + 11 * Minute, out string reason), Is.True);
        Assert.That(reason, Is.EqualTo("Rally failed: the warband never mustered"));
        Assert.That(AutonomousRvrEventLayer.IsAbandoned("scattered", target.Id, start + 11 * Minute), Is.True,
            "the force does not reopen the same keep at once");
    }

    [Test]
    public void MusterWhoseLeaderStoppedReportingIsJudgedAfterTheGracePeriod()
    {
        const long start = 240_000_000;
        var target = OpenSiege("rvr-keep-9106", start);
        Bucket(target.Id, "Attackers").Add("leaderless", 8);
        AutonomousRvrEventLayer.ReportMuster("leaderless", 8, 4, 8, false, start);

        Sweep(start + AutonomousRvrSiegeMuster.MaximumWaitMilliseconds + AutonomousRvrSiegeMuster.UnreportedGraceMilliseconds - 1);
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.True);
        Sweep(start + AutonomousRvrSiegeMuster.MaximumWaitMilliseconds + AutonomousRvrSiegeMuster.UnreportedGraceMilliseconds);
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.False);
    }

    [Test]
    public void AFailedForceLeavesTheSiegeToTheOtherAttackers()
    {
        const long start = 250_000_000;
        var target = OpenSiege("rvr-keep-9107", start);
        Bucket(target.Id, "Attackers").Add("strong", 8);
        Bucket(target.Id, "Attackers").Add("weak", 8);
        AutonomousRvrEventLayer.ReportMuster("strong", 8, 8, 8, false, start);
        AutonomousRvrEventLayer.ReportMuster("strong", 8, 8, 8, false,
            start + AutonomousRvrSiegeMuster.PreparationWindowMilliseconds);
        AutonomousRvrEventLayer.ReportMuster("weak", 8, 1, 8, false, start);

        Assert.That(AutonomousRvrEventLayer.ReportMuster("weak", 8, 1, 8, false, start + 11 * Minute), Is.EqualTo(AutonomousRvrSiegeMuster.Phase.None));
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.True, "the other force still marches");
        Assert.That(Bucket(target.Id, "Attackers").ContainsKey("weak"), Is.False);
        Assert.That(Bucket(target.Id, "Attackers").ContainsKey("strong"), Is.True);
    }

    [Test]
    public void RemovingAForceClearsItsMuster()
    {
        const long start = 260_000_000;
        var target = OpenSiege("rvr-keep-9108", start);
        Bucket(target.Id, "Attackers").Add("gone", 8);
        AutonomousRvrEventLayer.ReportMuster("gone", 8, 3, 8, false, start);
        Assert.That(AutonomousRvrEventLayer.MusterCounts(target.Id), Is.EqualTo((1, 0)));
        AutonomousRvrEventLayer.RemoveForce("gone");
        Assert.That(AutonomousRvrEventLayer.MusterCounts(target.Id), Is.EqualTo((0, 0)));
    }

    // ---- The opener skips keeps that could not be reached -------------------

    private static AutonomousRvrEventLayer.Force WholeGuildWarband(string id) =>
        new(id, eRealm.Albion, 8, 50, 1, true, false, [1, 2, 3, 4, 5, 6, 7, 8], 50, "Stone Ward", true, true);

    private static AutonomousRvrEventLayer.LiveObjective Keep(string id) =>
        new(id, "Keep " + id, AutonomousRvrEventLayer.Intent.AssaultKeep, eRealm.Midgard, 100, 100_000, 100_000, 0,
            false, 0, 0, 4, 2);

    [Test]
    public void OpenerSkipsAKeepWhoseExteriorRouteFailed_AndReturnsToItLater()
    {
        const long start = 270_000_000;
        var unreachable = Keep("rvr-keep-51");
        Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(WholeGuildWarband("wb-a"), [unreachable], start, 0)?.TargetId,
            Is.EqualTo("rvr-keep-51"), "control: without a failure the keep is chosen");
        DOL.GS.Tests.RvrEventTestState.Clear();

        AutonomousRvrEventLayer.NoteKeepRouteFailure(unreachable.Id, start);
        Assert.That(AutonomousRvrEventLayer.IsKeepRouteBlocked(unreachable.Id, start + 30 * Minute), Is.True);
        Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(WholeGuildWarband("wb-b"), [unreachable], start + 30 * Minute, 0), Is.Null,
            "the only keep is known to be unreachable");
        var reachable = Keep("rvr-keep-53");
        Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(WholeGuildWarband("wb-c"), [unreachable, reachable], start + 31 * Minute, 0)?.TargetId,
            Is.EqualTo("rvr-keep-53"));

        Assert.That(AutonomousRvrEventLayer.IsKeepRouteBlocked(unreachable.Id, start + 61 * Minute), Is.False, "an hour later it may be tried again");
    }

    [Test]
    public void RepeatedRouteFailuresDoubleTheBlock_ButOneForcesReportsCountOnce()
    {
        const long start = 280_000_000;
        AutonomousRvrEventLayer.NoteKeepRouteFailure("rvr-keep-57", start);
        AutonomousRvrEventLayer.NoteKeepRouteFailure("rvr-keep-57", start + 1_000); // second member of the same force
        Assert.That(AutonomousRvrEventLayer.IsKeepRouteBlocked("rvr-keep-57", start + 61 * Minute), Is.False, "still the first, one-hour block");
        AutonomousRvrEventLayer.NoteKeepRouteFailure("rvr-keep-57", start + 62 * Minute);
        Assert.That(AutonomousRvrEventLayer.IsKeepRouteBlocked("rvr-keep-57", start + 62 * Minute + 90 * Minute), Is.True, "second strike: two hours");
        Assert.That(AutonomousRvrEventLayer.IsKeepRouteBlocked("rvr-keep-57", start + 62 * Minute + 121 * Minute), Is.False);
    }

    [Test]
    public void AnAttackerReachingTheWallsClearsTheRouteBlock()
    {
        const long start = 290_000_000;
        var target = OpenSiege("rvr-keep-9109", start);
        Bucket(target.Id, "Attackers").Add("arrived", 8);
        AutonomousRvrEventLayer.NoteKeepRouteFailure(target.Id, start);
        Assert.That(AutonomousRvrEventLayer.IsKeepRouteBlocked(target.Id, start + 1), Is.True);
        AutonomousRvrEventLayer.ReportMarch(target.Id, "arrived", 1, 100, new Vector3(100_000, 101_000, 0), false, start + 1);
        Assert.That(AutonomousRvrEventLayer.IsKeepRouteBlocked(target.Id, start + 2), Is.False);
    }

    [Test]
    public void AForceThatLeavesTheSiegeTakesItsMusterAndItsClockFreezeWithIt()
    {
        const long start = 300_000_000;
        var target = OpenSiege("rvr-keep-9110", start);
        Bucket(target.Id, "Attackers").Add("switcher", 8);
        AutonomousRvrEventLayer.ReportMuster("switcher", 8, 3, 8, false, start);
        Assert.That(AutonomousRvrEventLayer.MusterCounts(target.Id), Is.EqualTo((1, 0)));

        // The warband picks a roaming plan instead (for instance a pickup member drops its level).
        var roam = new AutonomousRvrEventLayer.LiveObjective("rvr-camp-1", "patrol", AutonomousRvrEventLayer.Intent.Roam,
            eRealm.None, 100, 1, 1, 0, false, 0, 0, 0, 0);
        var lowLevel = new AutonomousRvrEventLayer.Force("switcher", eRealm.Albion, 8, 40, 1, true, false, [1], 40);
        AutonomousRvrEventLayer.ChooseOrJoin(lowLevel, [roam], start + Minute, 0);
        Assert.That(Bucket(target.Id, "Attackers").ContainsKey("switcher"), Is.False);
        Assert.That(AutonomousRvrEventLayer.MusterCounts(target.Id), Is.EqualTo((0, 0)), "the muster left with the force");

        // Even a muster left behind cannot release the force from its new task or freeze the clocks.
        Sweep(start + 20 * Minute);
        Assert.That(AutonomousRvrEventLayer.TryConsumeRelease("switcher", start + 20 * Minute, out string reason), Is.False, reason);
    }

    [Test]
    public void StaleMusterOfAForceNoLongerAttackingIsDroppedWithoutARelease()
    {
        const long start = 310_000_000;
        var target = OpenSiege("rvr-keep-9111", start);
        Bucket(target.Id, "Attackers").Add("stale", 8);
        AutonomousRvrEventLayer.ReportMuster("stale", 8, 3, 8, false, start);
        Bucket(target.Id, "Attackers").Remove("stale"); // left without going through the cleanup paths

        Sweep(start + 13 * Minute);
        Assert.That(AutonomousRvrEventLayer.TryConsumeRelease("stale", start + 13 * Minute, out _), Is.False);
        Assert.That(AutonomousRvrEventLayer.IsAbandoned("stale", target.Id, start + 13 * Minute), Is.False);
        Assert.That(AutonomousRvrEventLayer.MusterCounts(target.Id), Is.EqualTo((0, 0)));
    }
}
