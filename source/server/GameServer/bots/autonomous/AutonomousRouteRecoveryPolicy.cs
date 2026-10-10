using System.Numerics;

namespace DOL.GS;

public static class AutonomousRouteRecoveryPolicy
{
    // A movement callback may describe the route that was just replaced.
    // Retain intermediate/seam failures while the intent is unchanged, but
    // never charge an obsolete destination against a freshly selected route.
    public static bool AppliesToCurrentDestination(bool destinationChanged, Vector3 failed, Vector3 current) =>
        !destinationChanged || Vector3.DistanceSquared(failed, current) <= 96 * 96;

    // Bug 88: a rejected dungeon or outdoor rival-hunt ("local-pvp-*") roaming
    // spot must not stay the chosen destination. The rejection (30 minutes) is
    // recorded under the camp id, but RvR roaming keeps its own destination.
    // Keep and relic objectives are re-published every tick by the shared RvR
    // plan, so they are deliberately not covered here.
    public static bool ShouldDropRejectedRvrDestination(
        string destinationId, bool isDungeon, System.Collections.Generic.IReadOnlyDictionary<string, long> rejectedUntil,
        long nowTick) =>
        destinationId != null && (isDungeon || destinationId.StartsWith("local-pvp-", System.StringComparison.Ordinal)) &&
        rejectedUntil.TryGetValue(destinationId, out long until) && until > nowTick;

    public const int MaximumLocalAttempts = 3;
    public const float ForwardProgressRequired = 240f;
    public const long RepeatedFailureWindowMilliseconds = 10 * 60_000L;
    public const float RepeatedFailureRadius = 384f;
    public const int FailuresBeforeSafeRelocation = 3;
    public const long ImmediateRouteFailureCooldownMilliseconds = 30_000;

    public static bool HasMeaningfulForwardProgress(float baselineDistance, float currentDistance) =>
        baselineDistance >= 0 && baselineDistance - currentDistance >= ForwardProgressRequired;

    public static bool ShouldAbandon(int attemptedRecoveries) =>
        attemptedRecoveries > MaximumLocalAttempts;

    public static bool ShouldRetainMovementOrder(bool isMoving, long nowTick, long retryAfterTick) =>
        isMoving || nowTick < retryAfterTick;

    // A collision-stopped actor reports IsMoving=false. Route ownership is
    // established by the controller before this policy is called, so elapsed
    // position time—not the movement flag—is the reliable stall signal.
    public static bool ShouldRecoverActiveRouteStall(long lastProgressTick, long nowTick, long threshold) =>
        lastProgressTick > 0 && nowTick - lastProgressTick >= threshold;

    public static long ReplanDelayMilliseconds(bool emptyLiveCamp, long actorKey) =>
        (emptyLiveCamp ? 2_500 : ImmediateRouteFailureCooldownMilliseconds) +
        System.Math.Abs(actorKey % 1_500);

    // A pocket escape that lands on the actor's own floor is not an escape: the
    // actor never leaves the pocket, but the move would be reported as success,
    // reset the failure budget and repeat on every replan (bug 132).
    public const float MinimumPocketEscapeDistance = 32f;

    public static bool IsRealPocketEscape(Vector3 current, Vector3 escape) =>
        Vector3.DistanceSquared(current, escape) > MinimumPocketEscapeDistance * MinimumPocketEscapeDistance;

    public static bool IsSameRepeatedFailurePocket(ushort previousRegion, ushort currentRegion,
        Vector3 previous, Vector3 current, long elapsedMilliseconds) =>
        previousRegion == currentRegion && elapsedMilliseconds >= 0 &&
        elapsedMilliseconds <= RepeatedFailureWindowMilliseconds &&
        Vector2.DistanceSquared(new(previous.X, previous.Y), new(current.X, current.Y)) <=
        RepeatedFailureRadius * RepeatedFailureRadius;

    // Detour has a bounded search budget. A useful partial corridor is not a
    // collision: continue once from its reached end, never retry a zero-progress island.
    public static bool CanContinuePartial(PathfindingStatus status, Vector3 origin, Vector3 current) =>
        status == PathfindingStatus.PartialPathFound &&
        Vector3.DistanceSquared(origin, current) >= ForwardProgressRequired * ForwardProgressRequired;
}
