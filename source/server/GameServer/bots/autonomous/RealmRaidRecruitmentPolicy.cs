using System;

namespace DOL.GS;

public static class RealmRaidRecruitmentPolicy
{
    public const int MaximumBots = 300;
    public const int MaximumParties = (MaximumBots + 7) / 8;
    public const int AutonomousMinimumPresent = 200;
    public const int MinimumParties = AutonomousMinimumPresent / 8;
    public const long ForcedStagingMilliseconds = 45 * 60_000L;
    public const long AutonomousMinimumStagingMilliseconds = 15 * 60_000L;
    public const long AutonomousStagingLimitMilliseconds = 90 * 60_000L;
    public const long ForcedStagingLimitMilliseconds = 90 * 60_000L;
    public const long BattleMilliseconds = 4 * 60 * 60_000L;
    /// <summary>
    /// Camlann world: autonomous bots no longer open or join dragon and epic-dungeon raids on their
    /// own: level 50s spend that time in RvR. Raids the owner starts from the launcher's event
    /// controls still run. Port S3: the RvR world (Realm-1) runs upstream 0.38c, where level-50
    /// Group PvE parties open and join their realm's raids on their own.
    /// </summary>
    public static bool AutonomousRaidsEnabled => AutonomousRaidsRun(WorldMode.Current);

    public static bool AutonomousRaidsRun(WorldModelKind world) => world == WorldModelKind.Realm;

    /// <summary>The fork's raid calendar (scheduled sign-up raids) stays silent in both worlds.</summary>
    public static readonly bool ScheduledRaidsEnabled = false;
    public const double NewEventChance = .20;
    public const double JoinExistingChance = .95;

    public static bool Eligible(int level, bool autonomous, bool temporary, bool playerLed) =>
        level == 50 && autonomous && !temporary && !playerLed;

    public static bool CanOpenEvent(bool forced, bool activePveEvent, bool sameEncounterEvent) =>
        !sameEncounterEvent && (forced || !activePveEvent);

    /// <summary>
    /// Whether a rally may begin its assault. The RvR world (Realm-1) uses the upstream 0.38c rule
    /// (<see cref="UpstreamReady"/>); the Camlann world keeps the fork's 45-minute forced preparation.
    /// </summary>
    public static bool Ready(bool forced, long elapsed, int present, bool landed) =>
        WorldMode.IsRvr
            ? UpstreamReady(forced, elapsed, present, landed)
            : Ready(forced, elapsed, present, landed, AutonomousMinimumPresent,
                forced ? ForcedStagingMilliseconds : AutonomousMinimumStagingMilliseconds);

    // Upstream 0.38c (RvR world). An automatic rally that has waited most of its window with a nearly full
    // roster goes in rather than failing outright. Caer Sidi failed twice in one run with 196 and 186 of the
    // 200 required bots at its entrance.
    public const long AutonomousLateStartMilliseconds = 75 * 60_000L;
    public const int AutonomousLateStartMinimumPresent = 180;

    // Upstream 0.38c: forced raids (started by the player) go in as soon as 200 are staged and, for a dragon,
    // it has landed. They have no staging or battle time limit: they run until the encounter is defeated, the
    // player presses Stop event, or the server stops.
    public static bool UpstreamReady(bool forced, long elapsed, int present, bool landed) =>
        elapsed >= (forced ? 0 : AutonomousMinimumStagingMilliseconds) &&
        (present >= AutonomousMinimumPresent ||
         !forced && elapsed >= AutonomousLateStartMilliseconds && present >= AutonomousLateStartMinimumPresent) &&
        landed;

    // Upstream 0.38c, 2026-10-16 (owner's session there): the automatic Tuscaran rally needed 84 minutes to
    // gather 200 at Hagall, so the one 90-minute staging clock left 6 minutes for the trip to the dungeon and it
    // failed with 93 there. A slow hub leaves after HubPatienceMilliseconds with HubEarlyDepartureMinimum present
    // (late arrivals join the expedition directly), and after leaving the raid always gets
    // PostDepartureStagingMilliseconds to stage.
    public const long HubPatienceMilliseconds = 30 * 60_000L;
    public const int HubEarlyDepartureMinimum = 120;
    public const long PostDepartureStagingMilliseconds = 45 * 60_000L;

    /// <summary>Upstream 0.38c hub departure: 200 present, or 120 after half an hour (elapsed 0 for forced raids).</summary>
    public static bool DepartHub(bool alreadyDeparted, int presentBots, long elapsed) =>
        alreadyDeparted || presentBots >= AutonomousMinimumPresent ||
        elapsed >= HubPatienceMilliseconds && presentBots >= HubEarlyDepartureMinimum;

    /// <summary>When an automatic rally's staging ends: 90 minutes after it opened, but never sooner than 45 minutes
    /// after its parties left the hub (departedTick 0 = not yet). Upstream 0.38c.</summary>
    public static long StagingDeadline(long created, long departedTick) =>
        Math.Max(created + AutonomousStagingLimitMilliseconds,
            departedTick > 0 ? departedTick + PostDepartureStagingMilliseconds : 0);

    /// <summary>Upstream 0.38c staging failure: automatic rallies only, after <see cref="StagingDeadline"/>.</summary>
    public static bool StagingExpired(bool forced, long now, long created, long departedTick, int present, bool landed) =>
        !forced && now >= StagingDeadline(created, departedTick) &&
        !(present >= AutonomousMinimumPresent && !landed);

    /// <summary>Upstream 0.38c: a forced raid has no battle time limit.</summary>
    public static bool BattleExpired(bool forced, long now, long deadline) => !forced && now >= deadline;

    /// <summary>A raid party that lost members (but still exists) takes replacements up to its size when it joined
    /// (upstream 0.38c).</summary>
    public static bool NeedsReplacement(int target, int members) => members > 0 && members < target;

    /// <summary>A raid with its own head count and preparation time (scheduled sign-up raids).</summary>
    public static bool Ready(bool forced, long elapsed, int present, bool landed, int minimumPresent, long minimumStaging) =>
        elapsed >= minimumStaging && present >= minimumPresent && landed;

    public static bool DepartHub(bool alreadyDeparted, int presentBots) =>
        DepartHub(alreadyDeparted, presentBots, AutonomousMinimumPresent);

    public static bool DepartHub(bool alreadyDeparted, int presentBots, int minimumPresent) =>
        alreadyDeparted || presentBots >= minimumPresent;

    /// <summary>
    /// Whether a rally's staging failed. The RvR world uses the upstream 0.38c rule (forced raids never
    /// expire); the Camlann world keeps the fork's 90-minute limit for both.
    /// </summary>
    public static bool StagingExpired(bool forced, long elapsed, int present = 0, bool landed = true) =>
        WorldMode.IsRvr
            ? !forced && elapsed >= AutonomousStagingLimitMilliseconds && !(present >= AutonomousMinimumPresent && !landed)
            : StagingExpired(forced, elapsed, present, landed, AutonomousMinimumPresent);

    public static bool StagingExpired(bool forced, long elapsed, int present, bool landed, int minimumPresent) =>
        elapsed >= (forced ? ForcedStagingLimitMilliseconds : AutonomousStagingLimitMilliseconds) &&
        !(present >= minimumPresent && !landed);

    // Scheduled sign-up raids (0.105.0): announced ahead, smaller head count.
    public const long ScheduledLeadMilliseconds = 25 * 60_000L;
    public const long ScheduledStagingMilliseconds = 10 * 60_000L;
    public const int ScheduledMinimumSignUps = 40;

    /// <summary>Head count that must actually arrive: most of those who signed up, never fewer than 24.</summary>
    public static int ScheduledMinimumPresent(int signedUp) => Math.Max(24, signedUp * 3 / 5);
}
