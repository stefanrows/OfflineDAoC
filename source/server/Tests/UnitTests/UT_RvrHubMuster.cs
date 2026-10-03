using System;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// RvR portal-keep muster (owner, 2026-10-03): groups meet at their leader's
/// border hub, wait for members expected within five minutes, leave together
/// and never expel latecomers.
/// </summary>
[TestFixture]
public sealed class UT_RvrHubMuster
{
    private const long Minute = 60_000;
    private static readonly long?[] NoneMissing = Array.Empty<long?>();

    private static AutonomousRvrHubMuster.Outcome Decide(int assigned, int present, long?[] missing,
        long waited, long readyHeld = 0, bool leaderPresent = true, bool recruiting = false) =>
        AutonomousRvrHubMuster.DecideHubDeparture(assigned, present, missing, leaderPresent, recruiting, waited, readyHeld);

    // ---- Hub geometry ----------------------------------------------------

    [TestCase(eRealm.Albion, (ushort)1, "Castle Sauvage")]
    [TestCase(eRealm.Midgard, (ushort)100, "Svasud Faste")]
    [TestCase(eRealm.Hibernia, (ushort)200, "Druim Ligen")]
    public void EveryMusterAnchorLiesInsideItsHub(eRealm realm, ushort region, string name)
    {
        Assert.That(AutonomousRvrStaging.TryGetBorderKeep(realm, out AutonomousRvrStaging.BorderKeep keep), Is.True);
        foreach (Vector3 anchor in AutonomousRvrStaging.CandidateAnchors(keep, 12345))
        {
            Assert.That(AutonomousRvrStaging.TryGetHubAt(region, anchor, out var hub, out eRealm owner), Is.True);
            Assert.That(hub.Name, Is.EqualTo(name));
            Assert.That(owner, Is.EqualTo(realm));
        }
    }

    [Test]
    public void PointsOutsideAHubOrInAnotherRegionAreNoHub()
    {
        AutonomousRvrStaging.TryGetBorderKeep(eRealm.Albion, out var keep);
        Assert.That(AutonomousRvrStaging.TryGetHubAt(1, keep.Position + new Vector3(3_600, 0, 0), out _, out _), Is.False);
        Assert.That(AutonomousRvrStaging.TryGetHubAt(100, keep.Position, out _, out _), Is.False);
        Assert.That(AutonomousRvrStaging.TryGetHubAt(1, new(560467, 511652, 2344), out _, out _), Is.False,
            "Cotswold is a town, not a hub");
    }

    // ---- Departure -------------------------------------------------------

    [Test]
    public void EveryoneThereLeavesAtOnce() =>
        Assert.That(Decide(8, 8, NoneMissing, 30_000), Is.EqualTo(
            new AutonomousRvrHubMuster.Outcome(AutonomousRvrHubMuster.Decision.Depart, "all")));

    [Test]
    public void QuorumWaitsForAMemberExpectedWithinFiveMinutes()
    {
        long?[] missing = { 90_000, null };
        Assert.That(Decide(8, 6, missing, 2 * Minute, readyHeld: Minute).Decision,
            Is.EqualTo(AutonomousRvrHubMuster.Decision.Wait));
    }

    [Test]
    public void QuorumLeavesWhenNoMissingMemberIsExpectedSoon()
    {
        long?[] missing = { null, 6 * Minute };
        Assert.That(Decide(8, 6, missing, Minute), Is.EqualTo(
            new AutonomousRvrHubMuster.Outcome(AutonomousRvrHubMuster.Decision.Depart, "quorum_no_eta")));
    }

    [Test]
    public void WaitForAnExpectedMemberIsBoundedByFiveMinutes()
    {
        long?[] missing = { 60_000 };
        Assert.That(Decide(8, 7, missing, 6 * Minute, readyHeld: 5 * Minute - 1).Decision,
            Is.EqualTo(AutonomousRvrHubMuster.Decision.Wait));
        Assert.That(Decide(8, 7, missing, 6 * Minute, readyHeld: 5 * Minute), Is.EqualTo(
            new AutonomousRvrHubMuster.Outcome(AutonomousRvrHubMuster.Decision.Depart, "eta_timeout")));
    }

    [Test]
    public void BelowQuorumWaitsThenHalfLeavesAfterTenMinutes()
    {
        long?[] missing = { null, null, null, null };
        Assert.That(Decide(8, 4, missing, 9 * Minute).Decision, Is.EqualTo(AutonomousRvrHubMuster.Decision.Wait));
        Assert.That(AutonomousRvrHubMuster.QuorumReady(8, 4, 10 * Minute), Is.True);
        Assert.That(Decide(8, 4, missing, 10 * Minute), Is.EqualTo(
            new AutonomousRvrHubMuster.Outcome(AutonomousRvrHubMuster.Decision.Depart, "quorum_no_eta")));
    }

    [Test]
    public void FewerThanTwoAfterTenMinutesDisbandUnlessSomeoneIsClose()
    {
        long?[] nobody = { null, null, null };
        Assert.That(Decide(4, 1, nobody, 10 * Minute), Is.EqualTo(
            new AutonomousRvrHubMuster.Outcome(AutonomousRvrHubMuster.Decision.Disband, "nobody_came")));
        long?[] oneClose = { 30_000, null, null };
        Assert.That(Decide(4, 1, oneClose, 10 * Minute).Decision, Is.EqualTo(AutonomousRvrHubMuster.Decision.Wait));
    }

    [Test]
    public void HardCapLeavesWithTwoAndDisbandsBelow()
    {
        long?[] missing = { 60_000, 60_000, 60_000, 60_000, 60_000, 60_000 };
        Assert.That(Decide(8, 2, missing, 20 * Minute), Is.EqualTo(
            new AutonomousRvrHubMuster.Outcome(AutonomousRvrHubMuster.Decision.Depart, "timeout")));
        Assert.That(Decide(8, 1, missing, 20 * Minute), Is.EqualTo(
            new AutonomousRvrHubMuster.Outcome(AutonomousRvrHubMuster.Decision.Disband, "timeout")));
        Assert.That(Decide(8, 3, missing, 19 * Minute).Decision, Is.EqualTo(AutonomousRvrHubMuster.Decision.Wait));
    }

    [Test]
    public void GroupWaitsForItsLeaderUntilTheHalfGroupMark()
    {
        Assert.That(Decide(4, 3, new long?[] { null }, 2 * Minute, leaderPresent: false),
            Is.EqualTo(new AutonomousRvrHubMuster.Outcome(AutonomousRvrHubMuster.Decision.Wait, "leader")));
        Assert.That(Decide(4, 3, new long?[] { null }, 10 * Minute, leaderPresent: false).Decision,
            Is.EqualTo(AutonomousRvrHubMuster.Decision.Depart), "the caller hands the lead to a present member");
    }

    [Test]
    public void RecruitingGroupHoldsUntilItsReadyWindowEnds()
    {
        Assert.That(Decide(3, 3, NoneMissing, Minute, readyHeld: Minute, recruiting: true).Decision,
            Is.EqualTo(AutonomousRvrHubMuster.Decision.Wait));
        Assert.That(Decide(3, 3, NoneMissing, 6 * Minute, readyHeld: 5 * Minute, recruiting: true).Decision,
            Is.EqualTo(AutonomousRvrHubMuster.Decision.Depart));
    }

    [Test]
    public void TwoPersonGroupNeedsBoth()
    {
        Assert.That(Decide(2, 1, new long?[] { null }, 5 * Minute).Decision, Is.EqualTo(AutonomousRvrHubMuster.Decision.Wait));
        Assert.That(Decide(2, 2, NoneMissing, 0).Decision, Is.EqualTo(AutonomousRvrHubMuster.Decision.Depart));
    }

    // ---- Arrival estimate ------------------------------------------------

    [Test]
    public void DeadOrBlockedMembersAreNotExpected()
    {
        Assert.That(AutonomousRvrHubMuster.EstimateArrivalMilliseconds(false, false, true, 100, double.NaN, 200), Is.Null);
        Assert.That(AutonomousRvrHubMuster.EstimateArrivalMilliseconds(true, true, true, 100, double.NaN, 200), Is.Null);
        Assert.That(AutonomousRvrHubMuster.EstimateArrivalMilliseconds(true, false, false, double.NaN, double.NaN, 200),
            Is.Null, "another region without a porter");
    }

    [Test]
    public void SameRegionEstimateIsDistanceOverSpeed() =>
        Assert.That(AutonomousRvrHubMuster.EstimateArrivalMilliseconds(true, false, true, 12_000, double.NaN, 200),
            Is.EqualTo(60_000));

    [Test]
    public void RemoteEstimateAddsPorterTransferAndLandingWalk()
    {
        long? eta = AutonomousRvrHubMuster.EstimateArrivalMilliseconds(true, false, false, double.NaN, 3_000, 200);
        Assert.That(eta, Is.EqualTo((3_000 + AutonomousRvrHubMuster.HubLandingWalk) / 200 * 1000 +
            AutonomousRvrHubMuster.PortDelayMilliseconds));
        Assert.That(eta, Is.LessThan(AutonomousRvrHubMuster.ExpectedArrivalWindowMilliseconds));
        Assert.That(AutonomousRvrHubMuster.EstimateArrivalMilliseconds(true, false, false, double.NaN, 80_000, 200),
            Is.GreaterThan(AutonomousRvrHubMuster.ExpectedArrivalWindowMilliseconds));
    }

    // ---- Disband before departure ----------------------------------------

    [Test]
    public void OnlyAMusterThatNeverLeftReturnsToHubLfg()
    {
        Assert.That(AutonomousObjectiveAssignments.ReturnsToHubLfg(eAutonomousObjectiveKind.RvR, taskStarted: false), Is.True);
        Assert.That(AutonomousObjectiveAssignments.ReturnsToHubLfg(eAutonomousObjectiveKind.RvR, taskStarted: true), Is.False,
            "a group that went out ends its tour as before");
        Assert.That(AutonomousObjectiveAssignments.ReturnsToHubLfg(eAutonomousObjectiveKind.GroupPve, taskStarted: false), Is.False);
    }

    [Test]
    public void LevellingBotKeepsItsRvrAssignmentAfterADisbandedMuster()
    {
        var record = new OfflineWorldBotRecord
        {
            ObjectiveKind = nameof(eAutonomousObjectiveKind.RvR),
            ObjectiveAssignmentId = "rvr-tour-1",
            ObjectiveRvrEligibleUtc = string.Empty,
            CurrentCampId = "frontier",
            TravelDestination = "Castle Sauvage",
        };
        GameBot bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
        typeof(GameBot).GetField("<IsAutonomousWorldBot>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(bot, true);
        typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord))!.SetValue(bot, record);
        // Level 0 here, i.e. below 50: BeginSoloAfterGroupTask would end the tour and owe PvE.
        Assert.That(AutonomousObjectiveAssignments.RvrTourRenews(bot.Level), Is.False);

        AutonomousObjectiveAssignments.ReturnToHubLfgAfterMuster(bot, "Too few members met at Castle Sauvage");

        Assert.That(record.ObjectiveKind, Is.EqualTo(nameof(eAutonomousObjectiveKind.RvR)));
        Assert.That(record.ObjectiveAssignmentId, Is.EqualTo("rvr-tour-1"), "no new task, no town break");
        Assert.That(record.ObjectiveRvrEligibleUtc, Is.Not.EqualTo(AutonomousObjectiveAssignments.PveCompletionRequired));
        Assert.That(record.CurrentCampId, Is.Empty);
        Assert.That(record.ObjectivePhase, Does.Contain("Castle Sauvage"));
    }

    // ---- Windows and attendance ------------------------------------------

    [Test]
    public void RvrRemoteMeetupWindowIsTwentyMinutesPveUnchanged()
    {
        Assert.That(AutonomousRvrHubMuster.RemoteMeetupTimeoutMilliseconds(eAutonomousObjectiveKind.RvR),
            Is.EqualTo(20 * Minute));
        Assert.That(AutonomousRvrHubMuster.RemoteMeetupTimeoutMilliseconds(eAutonomousObjectiveKind.GroupPve),
            Is.EqualTo(45 * Minute));
    }

    [Test]
    public void RestartMakesAnArrivedMemberTravelAgainWithoutTouchingOthers()
    {
        var attendance = new AutonomousRendezvousAttendance();
        DateTime utc = new(2026, 10, 3, 20, 0, 0, DateTimeKind.Utc);
        attendance.Rebase(new long[] { 1, 2 }, 0, utc, 20 * Minute);
        attendance.Observe(1, 1_000, true);
        attendance.Observe(2, 1_000, true);
        attendance.Restart(1, 2 * Minute, utc, 10 * Minute);
        Assert.That(attendance.HasArrived(1), Is.False);
        Assert.That(attendance.HasArrived(2), Is.True);
        Assert.That(attendance.DeadlineUtc(1), Is.EqualTo(utc.AddMinutes(10)));
        Assert.That(attendance.Observe(1, 12 * Minute - 1, false), Is.False);
    }
}
