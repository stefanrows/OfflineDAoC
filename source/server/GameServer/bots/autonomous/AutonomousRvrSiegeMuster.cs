using System;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// Muster-and-march policy of an attacking warband (bug 75). A keep siege used
/// to open with "each assigned bot converges without formation staging": every
/// member walked to the keep alone, from wherever it stood, by its own porter,
/// and died alone to the guards (0 captures, 0 ram hits in 36 sieges). A 2003
/// group met at one place, went out together and stayed together. The force
/// now musters on its leader, departs once enough of it is present, and
/// marches behind the leader. Every wait here is bounded, so a member that
/// cannot arrive (stuck, dead, riding a horse) can never hold the siege hostage.
/// </summary>
public static class AutonomousRvrSiegeMuster
{
    public enum Phase { None, Mustering, Marching }
    public enum Decision { Wait, Depart, Fail }

    /// <summary>Members this close to the leader (same region) count as mustered.</summary>
    public const int PresentRadius = 1_000;
    /// <summary>A quorum (6 of 8) departs once this long has passed since the muster began.</summary>
    public const long QuorumWaitMilliseconds = 4 * 60_000L;
    /// <summary>
    /// The muster's hard end: half the warband departs, less than that means the
    /// force never came together. Game time; at world speed 3x this is about
    /// three real minutes.
    /// </summary>
    public const long MaximumWaitMilliseconds = 10 * 60_000L;
    /// <summary>An unreported muster (its leader dead or gone) is judged this much after the maximum.</summary>
    public const long UnreportedGraceMilliseconds = 2 * 60_000L;
    /// <summary>A force whose leader already stands this close to the keep in its region skips the muster.</summary>
    public const int SkipMusterDistance = 14_000;
    /// <summary>The leader counts as arrived at the keep this close to it; followers then fight on their own.</summary>
    public const int KeepArrivalRadius = 3_500;
    /// <summary>A follower farther than this from the arrived leader rejoins it instead of walking alone.</summary>
    public const int RejoinRadius = 2_500;
    /// <summary>How long a keep whose exterior route failed stays off the automatic opener's list (first strike).</summary>
    public const long KeepRouteBlockMilliseconds = 60 * 60_000L;
    public const long KeepRouteBlockMaximumMilliseconds = 8 * 60 * 60_000L;

    /// <summary>Three quarters of the warband, never fewer than two (8 -> 6).</summary>
    public static int Quorum(int assigned) =>
        assigned <= 2 ? Math.Max(1, assigned) : Math.Max(2, (int)Math.Ceiling(assigned * 0.75));

    /// <summary>What the force does after this long of mustering.</summary>
    public static Decision Decide(int alive, int present, int assigned, long startedTick, long nowTick)
    {
        long waited = Math.Max(0, nowTick - startedTick);
        if (present >= Quorum(assigned) && (present >= alive || waited >= QuorumWaitMilliseconds))
            return Decision.Depart;
        if (waited >= MaximumWaitMilliseconds)
            return present >= Math.Max(2, assigned / 2) ? Decision.Depart : Decision.Fail;
        return Decision.Wait;
    }

    /// <summary>A force already standing in the keep's region near the keep needs no muster.</summary>
    public static bool CanSkipMuster(bool sameRegionAsKeep, double distanceToKeep) =>
        sameRegionAsKeep && distanceToKeep <= SkipMusterDistance;

    public static bool LeaderAtKeep(ushort leaderRegion, Vector2 leader, ushort keepRegion, Vector2 keep) =>
        leaderRegion == keepRegion && Vector2.Distance(leader, keep) <= KeepArrivalRadius;

    /// <summary>
    /// Whether a follower of a departed force stays on its leader. On the way
    /// (leader not yet at the keep) every member follows, so nobody walks the
    /// road alone; once the leader stands at the keep only a member that is
    /// far from it (a released, returning one) rejoins, and the rest fight.
    /// </summary>
    public static bool FollowsLeader(bool leaderAtKeep, ushort botRegion, ushort leaderRegion, double distanceToLeader) =>
        !leaderAtKeep || botRegion != leaderRegion || distanceToLeader > RejoinRadius;

    /// <summary>
    /// The clocks of an automatic siege (fifteen minutes without progress,
    /// forty-five without an attacker at the walls) count from the force's
    /// departure from its muster, not from the siege's opening, and stand still
    /// while an attacking force is still mustering.
    /// </summary>
    public static bool IdleClocksRun(bool anyForceMustering) => !anyForceMustering;

    /// <summary>The tick the idle and absence clocks count from.</summary>
    public static long ClockOrigin(long lastActivityTick, long departureTick) => Math.Max(lastActivityTick, departureTick);

    /// <summary>How long a keep stays off the opener's list after its n-th consecutive route failure (1h, 2h, 4h, 8h).</summary>
    public static long RouteBlockMilliseconds(int consecutiveFailures) =>
        Math.Min(KeepRouteBlockMaximumMilliseconds,
            KeepRouteBlockMilliseconds << Math.Clamp(consecutiveFailures - 1, 0, 3));
}
