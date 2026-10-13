using System;
using System.Collections.Generic;

namespace DOL.GS;

/// <summary>One member of a group, in join order, as the leader choice sees it.</summary>
public readonly record struct GothwaitePvpPromotionCandidate(long BotId, bool Alive, bool InArena);

/// <summary>
/// The arena decisions of the Gothwaite PvP driver as pure functions (docs/GOTHWAITE_PVP_MODE.md sections 6.2, 6.4, 6.5 and
/// 8, stage 3): the patrol choice, the dwell, the return, chase-limit and stuck predicates, the edge margin and the leader
/// promotion. The driver reads the game and acts on the answers. Nothing here touches a game object, so the tests need no
/// bot and no database.
/// </summary>
public static class GothwaitePvpArenaRules
{
    public const string ReasonLowHealth = "low_health";
    public const string ReasonLowPower = "low_power";
    public const string ReasonLeaderLost = "leader_lost";
    public const string ReasonWiped = "wiped";
    public const string ChaseCastle = "castle";
    public const string ChaseEdge = "edge";
    public const string ChaseSelfEdge = "self_edge";
    public const string ChaseRing = "ring";
    public const string ActionRepick = "repick";
    public const string ActionCastle = "castle";

    /// <summary>
    /// The dwell at a patrol point after a visit; each visit draws its own length (inclusive range). Short, so groups keep cruising the
    /// island instead of standing at one point (second live run, 2026-10-13).
    /// </summary>
    public const int DwellMinMilliseconds = 6_000;
    public const int DwellMaxMilliseconds = 15_000;
    /// <summary>
    /// Heat above this counts as this much. Without a cap the hundreds of fights at the gate made the points next to it win nearly every
    /// pick, and the groups never left the gate area (second live run, 2026-10-13).
    /// </summary>
    public const double HeatCap = 3;
    /// <summary>
    /// The outbound leg (the walk from the gate to the first patrol point, which opens no fight) ends after this long (game time) even
    /// when the point was not reached, e.g. after long self-defence fights on the way.
    /// </summary>
    public const int OutboundMaxMilliseconds = 3 * 60_000;
    /// <summary>A patrol weight divides by 1 + distance / this: a slight preference for points that are near the bot.</summary>
    public const int DistanceScale = 10_000;
    /// <summary>A bot that is not fighting returns to the castle below this health (per cent).</summary>
    public const int ReturnHealthPercent = 35;
    /// <summary>A caster (MaxMana above zero) returns below this power (per cent). A fighter ignores power.</summary>
    public const int ReturnPowerPercent = 20;
    /// <summary>A bot beyond its edge by more than this drops its fight (the chase limit on the bot's own position).</summary>
    public const int ChaseSlack = 1_000;
    /// <summary>
    /// Goals are chosen this far inside the arena edge: a bot that arrives within the arrival distance of such a goal is still
    /// inside its edge, so arriving never leaves a bot outside.
    /// </summary>
    public const int EdgeMargin = AutonomousBattlegroundDriver.ArrivalDistance;
    /// <summary>A walk makes progress when it gets this much closer to its goal (a follower: when it moves this far).</summary>
    public const int MinimumProgress = 96;
    /// <summary>A walk without progress for this long (game time) has stalled.</summary>
    public const int ProgressWindowMilliseconds = 45_000;
    /// <summary>A patrol point that stalled is refused for this long.</summary>
    public const int UnreachableMilliseconds = 5 * 60_000;
    /// <summary>Consecutive stalls before a leader walks back to the castle (the third one sends it home).</summary>
    public const int ConsecutiveFailureLimit = 3;
    /// <summary>A living group member within this range of a follower counts as near it.</summary>
    public const int NearMemberRange = AutonomousBattlegroundDriver.OpponentRange;

    /// <summary>Two-dimensional distance (the ground plane; heights of goals are estimates).</summary>
    public static double Distance2D(double ax, double ay, double bx, double by)
    {
        double dx = ax - bx, dy = ay - by;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>The edge a bot chooses its goals inside: its arena edge less <see cref="EdgeMargin"/>.</summary>
    public static int GoalEdge(int edge) => edge - EdgeMargin;

    /// <summary>True when a point lies beyond the edge (the castle distance is strictly greater).</summary>
    public static bool OutsideEdge(double castleDistance, int edge) => castleDistance > edge;

    /// <summary>
    /// The weight of one patrol point: heat raises it (recent fights, 1 + heat, heat capped at <see cref="HeatCap"/>) and distance lowers
    /// it slightly (divided by 1 + distance / <see cref="DistanceScale"/>). Negative or unknown heat counts as none.
    /// </summary>
    public static double PatrolWeight(double heat, double distance)
    {
        if (!(heat > 0))
            heat = 0;
        else if (heat > HeatCap)
            heat = HeatCap;
        if (!(distance > 0))
            distance = 0;
        return (1 + heat) / (1 + distance / DistanceScale);
    }

    /// <summary>
    /// The weight of one patrol point for a pick. The first pick after a group arrives outside the castle gate ignores heat and distance
    /// (every allowed point weighs 1), so the groups spread over the arena instead of all going back to the last fights. Later picks use
    /// <see cref="PatrolWeight"/>.
    /// </summary>
    public static double RoamWeight(bool firstPick, double heat, double distance) => firstPick ? 1 : PatrolWeight(heat, distance);

    /// <summary>
    /// <see cref="RoamWeight(bool, double, double)"/> divided by 1 + the number of other groups and solos already heading to or standing at
    /// the point (<paramref name="claims"/>), so the groups spread over the whole island instead of meeting at one point. Negative claims
    /// count as none.
    /// </summary>
    public static double RoamWeight(bool firstPick, double heat, double distance, int claims) =>
        RoamWeight(firstPick, heat, distance) / (1 + Math.Max(0, claims));

    /// <summary>
    /// The outbound leg is over (second live run, 2026-10-13): the leader reached the patrol point it picked at the gate, or the leg has
    /// lasted <see cref="OutboundMaxMilliseconds"/>. Until then the group opens no fight, so it does not stop to fight the next group out
    /// of the gate just beyond the no-fight ring.
    /// </summary>
    public static bool OutboundOver(bool arrivedAtGoal, long now, long since) => arrivedAtGoal || now - since >= OutboundMaxMilliseconds;

    /// <summary>
    /// A fight dropped at the castle or the no-fight ring sends an arrived leader on a new outbound leg to a freshly picked point; a fight
    /// dropped at the edge sends it to the nearest point inside its edge (pure). Walking to the nearest point after a ring drop kept the
    /// groups next to the gate.
    /// </summary>
    public static bool DropSendsOutbound(string chaseReason) => chaseReason == ChaseRing || chaseReason == ChaseCastle;

    /// <summary>
    /// The next patrol point: a weighted random draw among the points inside <paramref name="goalEdge"/>, skipping the point
    /// the bot has just left (<paramref name="last"/>) and any point <paramref name="usable"/> refuses. Null when none is left.
    /// The draw uses only <paramref name="random"/>, so a seed gives the same sequence.
    /// </summary>
    public static GothwaitePvpSpot? ChoosePatrolPoint(GothwaitePvpArena arena, int goalEdge, IReadOnlyList<GothwaitePvpSpot> points,
        GothwaitePvpSpot? last, Func<GothwaitePvpSpot, bool> usable, Func<GothwaitePvpSpot, double> weightOf, Random random)
    {
        if (arena == null || points == null || random == null)
            return null;
        var candidates = new List<(GothwaitePvpSpot Spot, double Weight)>();
        double total = 0;
        foreach (GothwaitePvpSpot point in points)
        {
            if (last is { } previous && point == previous)
                continue;
            if (arena.CastleDistance(point.X, point.Y) > goalEdge)
                continue;
            if (usable != null && !usable(point))
                continue;
            double weight = weightOf == null ? 1 : weightOf(point);
            if (!(weight > 0) || double.IsInfinity(weight))
                continue;
            candidates.Add((point, weight));
            total += weight;
        }
        if (candidates.Count == 0)
            return null;
        double roll = random.NextDouble() * total;
        foreach ((GothwaitePvpSpot spot, double weight) in candidates)
        {
            if (roll < weight)
                return spot;
            roll -= weight;
        }
        return candidates[^1].Spot;
    }

    /// <summary>The nearest point inside <paramref name="goalEdge"/> that <paramref name="usable"/> accepts. Ties keep the first.</summary>
    public static GothwaitePvpSpot? NearestPoint(GothwaitePvpArena arena, int goalEdge, IReadOnlyList<GothwaitePvpSpot> points,
        int x, int y, Func<GothwaitePvpSpot, bool> usable)
    {
        if (arena == null || points == null)
            return null;
        GothwaitePvpSpot? best = null;
        double bestDistance = double.PositiveInfinity;
        foreach (GothwaitePvpSpot point in points)
        {
            if (arena.CastleDistance(point.X, point.Y) > goalEdge || usable != null && !usable(point))
                continue;
            double distance = Distance2D(x, y, point.X, point.Y);
            if (distance < bestDistance)
            {
                best = point;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>
    /// The walk-back point of a bot outside its edge: the nearest usable point inside its goal edge, or, when none is left
    /// there, the nearest usable point inside the edge itself.
    /// </summary>
    public static GothwaitePvpSpot? WalkBackPoint(GothwaitePvpArena arena, int edge, IReadOnlyList<GothwaitePvpSpot> points,
        int x, int y, Func<GothwaitePvpSpot, bool> usable) =>
        NearestPoint(arena, GoalEdge(edge), points, x, y, usable) ?? NearestPoint(arena, edge, points, x, y, usable);

    /// <summary>A dwell length for one visit, drawn from the random source (inclusive of both ends).</summary>
    public static int DwellMilliseconds(Random random) => random.Next(DwellMinMilliseconds, DwellMaxMilliseconds + 1);

    /// <summary>
    /// Why a bot that is not fighting goes back to the castle, in the order of the design: low health, low power (casters
    /// only), a lost leader, a wiped group. Null when none applies.
    /// </summary>
    public static string ReturnReason(int healthPercent, int powerPercent, bool usesPower, bool leaderLost, bool wiped)
    {
        if (healthPercent < ReturnHealthPercent)
            return ReasonLowHealth;
        if (usesPower && powerPercent < ReturnPowerPercent)
            return ReasonLowPower;
        if (leaderLost)
            return ReasonLeaderLost;
        return wiped ? ReasonWiped : null;
    }

    /// <summary>A follower has lost its group when its leader is not present (dead, released, or out of the arena) and no other living member is near it.</summary>
    public static bool FollowerLost(bool leaderPresent, int aliveNearOthers) => !leaderPresent && aliveNearOthers == 0;

    /// <summary>A bot in a group (not a solo) is wiped when no other member is still alive.</summary>
    public static bool Wiped(bool solo, int aliveOthers) => !solo && aliveOthers == 0;

    /// <summary>
    /// The chase limit (section 6.4): a fight is dropped when the target stands in the castle circle ("castle"), when it is
    /// beyond the bot's edge ("edge"), or when the bot itself is beyond its edge plus <see cref="ChaseSlack"/> ("self_edge").
    /// Only geometry decides. The target's con does not, so a grey or purple attacker that hits the bot is not dropped for
    /// its con. Null while the fight may go on.
    /// </summary>
    public static string ChaseLimitReason(bool targetInCastle, double targetCastleDistance, double botCastleDistance, int edge)
    {
        if (targetInCastle)
            return ChaseCastle;
        if (OutsideEdge(targetCastleDistance, edge))
            return ChaseEdge;
        return OutsideEdge(botCastleDistance, edge + ChaseSlack) ? ChaseSelfEdge : null;
    }

    /// <summary>
    /// The no-fight ring part of the chase limit (section 6.2): a fight that is still open is dropped once the target or the bot stands
    /// inside the ring, unless the target attacked the bot's side (self-defence is fought back, as in the design). Null while the fight may go on.
    /// </summary>
    public static string GateRingReason(bool targetInRing, bool botInRing, bool targetAttackedUs) =>
        (targetInRing || botInRing) && !targetAttackedUs ? ChaseRing : null;

    /// <summary>
    /// The walk out of the castle is done (section 6.4, live-run fix 2026-10-13): the leader is within the arrival distance of the castle exit, or it is already
    /// outside the no-fight ring on its way. The exit is a waypoint; the leader does not hold there.
    /// </summary>
    public static bool GateWalkDone(bool arrivedAtExit, bool outsideGateRing) => arrivedAtExit || outsideGateRing;

    /// <summary>
    /// Whether a bot scans for a legal opening this turn: the scan is due, the bot is not returning, it is not outside its own
    /// edge (edge keeping comes first, since a fight opened from there would be dropped at once), and it is not inside the no-fight ring
    /// (section 6.2). A leader or a solo always scans; a follower scans only while its leader has no legal target.
    /// </summary>
    public static bool MayScan(bool due, bool returning, bool outsideOwnEdge, bool isLeader, bool leaderHasLegalTarget, bool inGateRing = false) =>
        due && !returning && !outsideOwnEdge && !inGateRing && (isLeader || !leaderHasLegalTarget);

    /// <summary>A walk made progress when the bot is now at least <see cref="MinimumProgress"/> closer than the reference distance.</summary>
    public static bool MadeProgress(double distance, double reference) => distance <= reference - MinimumProgress;

    /// <summary>A follower made progress when it moved at least <see cref="MinimumProgress"/> from its reference point.</summary>
    public static bool MovedEnough(double moved) => moved >= MinimumProgress;

    /// <summary>The progress window has run out: <see cref="ProgressWindowMilliseconds"/> passed since the last progress.</summary>
    public static bool ProgressWindowElapsed(long now, long since) => now - since >= ProgressWindowMilliseconds;

    /// <summary>
    /// What a stall does: a patrol goal is marked unreachable and another is picked for the first two consecutive stalls; the
    /// third sends the bot back to the castle. A bot with no patrol goal (the exit, a follower) goes back at once.
    /// </summary>
    public static string StuckAction(int consecutiveFailures, bool canRepick) =>
        canRepick && consecutiveFailures < ConsecutiveFailureLimit ? ActionRepick : ActionCastle;

    /// <summary>The first member, in join order, who is alive and in the arena; -1 when none is. The leader is not in the list.</summary>
    public static int FirstAliveInArena(IReadOnlyList<GothwaitePvpPromotionCandidate> members)
    {
        for (int index = 0; members != null && index < members.Count; index++)
            if (members[index].Alive && members[index].InArena)
                return index;
        return -1;
    }

    /// <summary>
    /// A bot's first scan is offset by a value drawn from its id within one scan cycle, so the bots of one zone do not all scan
    /// on the same tick.
    /// </summary>
    public static int ScanOffsetMilliseconds(long botId)
    {
        ulong seed = unchecked((ulong)botId * 2654435761UL);
        return (int)((seed >> 9) % (ulong)AutonomousBattlegroundDriver.ScanMilliseconds);
    }

    /// <summary>
    /// The next due time of a bot's opponent scan after a scan at <paramref name="now"/>: one scan interval later (1.5 s). The driver
    /// sets it on every scan it runs, so a bot scans at most once per interval however often its turn comes (stage 4 cadence check).
    /// </summary>
    public static long NextScanAfter(long now) => now + AutonomousBattlegroundDriver.ScanMilliseconds;
}
