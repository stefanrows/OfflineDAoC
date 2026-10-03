using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// The portal-keep muster of a freshly formed RvR group (owner, 2026-10-03:
/// "everyone meets as a group where you can port to, and walks on together
/// from there"). A 2003 group met at Castle Sauvage, Svasud Faste or Druim
/// Ligen, waited a few minutes for its stragglers and went out with whoever
/// stood there; latecomers caught up through the porter. Nobody is thrown out
/// for being late. Every wait is bounded, so one lost member cannot hold the
/// group at the keep, and a group that never came together goes back to
/// looking for a group at the keep instead of out alone. A quorum does not
/// leave while a missing member is expected within five minutes.
/// </summary>
public static class AutonomousRvrHubMuster
{
    public enum Decision { Wait, Depart, Disband }

    public readonly record struct Outcome(Decision Decision, string Reason);

    /// <summary>
    /// A missing member expected within this time is waited for, even when a
    /// quorum already stands at the keep (owner, 2026-10-03: otherwise many
    /// incomplete groups march off). The wait is bounded by the same time,
    /// counted from the moment the group could first have left.
    /// </summary>
    public const long ExpectedArrivalWindowMilliseconds = 5 * 60_000L;
    /// <summary>From here half the group may leave; with fewer than two present and nobody close, it disbands.</summary>
    public const long HalfGroupWaitMilliseconds = 10 * 60_000L;
    /// <summary>Hard end of the muster (and the RvR remote meetup window): two present leave, else disband.</summary>
    public const long MaximumWaitMilliseconds = 20 * 60_000L;
    /// <summary>Walk from a porter landing at the hub to a muster slot (landings lie 1,000-9,500 from the keep).</summary>
    public const int HubLandingWalk = 3_000;
    /// <summary>Time for the porter interaction and region transfer.</summary>
    public const long PortDelayMilliseconds = 10_000;

    /// <summary>Three quarters of the group, never fewer than two (8 -> 6), as for a siege force.</summary>
    public static int Quorum(int assigned) => AutonomousRvrSiegeMuster.Quorum(assigned);

    /// <summary>Half the group, rounded up, never fewer than two (8 -> 4, 5 -> 3).</summary>
    public static int HalfGroup(int assigned) => Math.Max(2, (assigned + 1) / 2);

    /// <summary>
    /// Enough of the group stands at the keep to leave, apart from members
    /// still expected: a three-quarter quorum, or half the group once the
    /// muster has run <see cref="HalfGroupWaitMilliseconds"/>.
    /// </summary>
    public static bool QuorumReady(int assigned, int present, long waitedMilliseconds) =>
        assigned >= 2 && (present >= Quorum(assigned) ||
            waitedMilliseconds >= HalfGroupWaitMilliseconds && present >= HalfGroup(assigned));

    /// <summary>
    /// Expected time until a missing member stands at its slot, or null when
    /// it is not expected soon: dead (it follows after its release), its
    /// route was proven unreachable, or it is in another region without a
    /// porter. In the hub region: its distance at its speed. Elsewhere: the
    /// walk to its porter, the transfer and the walk from the landing.
    /// </summary>
    public static long? EstimateArrivalMilliseconds(bool alive, bool routeHeld, bool sameRegion,
        double distanceToSlot, double distanceToPorter, double speed)
    {
        if (!alive || routeHeld)
            return null;
        double unitsPerSecond = Math.Max(50d, speed);
        if (sameRegion)
            return double.IsFinite(distanceToSlot) && distanceToSlot >= 0
                ? (long)Math.Ceiling(distanceToSlot / unitsPerSecond * 1000d) : null;
        if (!double.IsFinite(distanceToPorter) || distanceToPorter < 0)
            return null;
        return (long)Math.Ceiling((distanceToPorter + HubLandingWalk) / unitsPerSecond * 1000d) + PortDelayMilliseconds;
    }

    /// <summary>
    /// What a mustering group does now. <paramref name="present"/> members
    /// stand at their hub slot (the leader included); <paramref name="missingArrivals"/>
    /// holds one estimate per missing member (null: not expected soon).
    /// <paramref name="readyHeldMilliseconds"/> is how long <see cref="QuorumReady"/>
    /// has held. The group waits for its leader until the half-group mark;
    /// after that the caller hands the lead to a present member.
    /// Reasons: all, quorum_no_eta, eta_timeout, recruit_timeout, timeout, nobody_came, leader, waiting.
    /// </summary>
    public static Outcome DecideHubDeparture(int assigned, int present, IEnumerable<long?> missingArrivals,
        bool leaderPresent, bool recruiting, long waitedMilliseconds, long readyHeldMilliseconds)
    {
        long waited = Math.Max(0, waitedMilliseconds);
        present = Math.Max(0, present);
        if (assigned < 2)
            return new(Decision.Disband, "too_small");
        int expectedSoon = missingArrivals?.Count(eta => eta is long ms && ms >= 0 &&
            ms <= ExpectedArrivalWindowMilliseconds) ?? 0;
        if (waited >= MaximumWaitMilliseconds)
            return present >= 2 ? new(Decision.Depart, "timeout") : new(Decision.Disband, "timeout");
        if (waited >= HalfGroupWaitMilliseconds && present + expectedSoon < 2)
            return new(Decision.Disband, "nobody_came");
        Outcome outcome = new(Decision.Wait, "waiting");
        if (present >= assigned && !recruiting)
            outcome = new(Decision.Depart, "all");
        else if (QuorumReady(assigned, present, waited))
        {
            if (expectedSoon == 0 && !recruiting)
                outcome = new(Decision.Depart, "quorum_no_eta");
            else if (readyHeldMilliseconds >= ExpectedArrivalWindowMilliseconds)
                outcome = new(Decision.Depart, expectedSoon > 0 ? "eta_timeout" : "recruit_timeout");
        }
        // The march follows its leader: do not leave without it before the
        // half-group mark.
        if (outcome.Decision == Decision.Depart && !leaderPresent && waited < HalfGroupWaitMilliseconds)
            return new(Decision.Wait, "leader");
        return outcome;
    }

    /// <summary>Remote meetup window: 20 minutes for an RvR group, 45 for PvE.</summary>
    public static long RemoteMeetupTimeoutMilliseconds(eAutonomousObjectiveKind objectiveKind) =>
        objectiveKind == eAutonomousObjectiveKind.RvR
            ? MaximumWaitMilliseconds
            : AutonomousBotGroupCoordinator.RemoteMeetupTimeoutMilliseconds;
}
