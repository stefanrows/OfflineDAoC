using System;

namespace DOL.GS;

/// <summary>Readiness for an army of existing guild parties; never creates troops or changes combat rules.</summary>
public static class AutonomousGuildAssault
{
    public const int MinimumAttackers = 8;
    public const int PartyQuorum = 6;
    public const int RallyRadius = 1400;
    public const int CampDistance = 9000;
    public const int MinimumKeepDistance = 7000;
    public const long GatherMilliseconds = 2 * 60_000;
    public const long MaximumGatherMilliseconds = 15 * 60_000;
    public const long MaximumTravelMilliseconds = 30 * 60_000;
    public const long ReportLifetime = 30_000;
    public const long ObservationLifetime = 90_000;

    // Choose once for a new assault, not every think: small guild sorties are
    // common, with larger planned expeditions too. Real opposition can always
    // raise this preference; a low roll never overrides the strength check.
    public static int PlannedParties(double roll) => roll < 0.45 ? 1 : roll < 0.80 ? 2 : 3;

    // A conservative initial policy, to be calibrated with actual simultaneous
    // combat attendance. Guards/doors cost a reserve; a defended keep needs
    // more than parity against the player-shaped defenders actually sighted.
    public static int RequiredAttackers(int observedDefenders, int livingGuards, int closedDoors, int plannedParties = 1) =>
        Math.Max(Math.Max(1, plannedParties) * MinimumAttackers, (int)Math.Ceiling(Math.Max(0, observedDefenders) * 1.5) +
            Math.Clamp((Math.Max(0, livingGuards) + 3) / 4 + Math.Max(0, closedDoors) * 2, 8, 24));

    /// <summary>A relaxed army (see <see cref="RelaxedRequired"/>) also needs
    /// fewer parties; never more than it planned.</summary>
    public static bool Ready(int parties, int attackers, int healers, int equippedOperators,
        int required, bool closedDoors, long gatheringMilliseconds, int plannedParties = 1,
        bool relaxed = false, int keepLevel = 0) =>
        gatheringMilliseconds >= GatherMilliseconds &&
        parties >= (relaxed ? Math.Min(Math.Max(1, plannedParties), RelaxedParties(keepLevel)) : Math.Max(1, plannedParties)) &&
        attackers >= required && healers >= (attackers + 7) / 8 && (!closedDoors || equippedOperators > 0);

    // Bug 75: guard count does not grow with keep level (28-30 at level 1 and
    // 5), so the formula above asks every keep for a full, unhurt party, and
    // 11+ troops (more than one party) once two doors are shut. In 2003 a level 1-2 keep fell to about
    // five players and a level 3-5 keep needed eight or more; guard levels
    // (52 at L1, 58 at L5, 66 at L10) are what makes a higher keep harder.
    // After five minutes of gathering the army settles for a keep-level floor;
    // sighted defenders are never relaxed and relaxing never asks for more.
    public const long RelaxAfterMilliseconds = 5 * 60_000;

    public static int KeepLevelFloor(int keepLevel) => keepLevel <= 3 ? 6 : keepLevel <= 6 ? 12 : 16;

    public static int RelaxedRequiredAttackers(int keepLevel, int observedDefenders) =>
        Math.Max(KeepLevelFloor(keepLevel), (int)Math.Ceiling(Math.Max(0, observedDefenders) * 1.5));

    public static int RelaxedRequired(int fullRequired, int keepLevel, int observedDefenders) =>
        Math.Min(fullRequired, RelaxedRequiredAttackers(keepLevel, observedDefenders));

    public static int RelaxedParties(int keepLevel) => (KeepLevelFloor(keepLevel) + 7) / 8;

    public static bool Relaxes(bool enabled, long gatheringMilliseconds) =>
        enabled && gatheringMilliseconds >= RelaxAfterMilliseconds;
}
