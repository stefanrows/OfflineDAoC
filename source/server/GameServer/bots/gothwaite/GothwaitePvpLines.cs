using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// The Gothwaite PvP log lines (prefix GOTH_PVP_, docs/GOTHWAITE_PVP_MODE.md section 5.7). Each builder writes one line for
/// one event; none is written per tick. They are pure, so tests pin the format the owner greps for.
/// </summary>
public static class GothwaitePvpLines
{
    /// <summary>The id of a solo departure, which forms no group.</summary>
    public const string SoloId = "solo";

    public static string Formed(string id, GothwaitePvpShape shape, int size, int minLevel, int maxLevel,
        IEnumerable<string> realms, IEnumerable<string> names) =>
        $"GOTH_PVP_GROUP_FORMED group={id} shape={GothwaitePvpShapes.Name(shape)} size={size} levels={minLevel}-{maxLevel} " +
        $"realms={string.Join(",", realms ?? Enumerable.Empty<string>())} members=\"{string.Join(",", names ?? Enumerable.Empty<string>())}\"";

    public static string Depart(string id, string leader, int size) =>
        $"GOTH_PVP_DEPART group={id} leader=\"{leader}\" size={size}";

    public static string Arrive(string id, string leader, int size) =>
        $"GOTH_PVP_ARRIVE group={id} leader=\"{leader}\" size={size}";

    public static string Disbanded(string id, GothwaitePvpShape shape, int size, int ready, string reason) =>
        $"GOTH_PVP_GROUP_DISBANDED group={id} shape={GothwaitePvpShapes.Name(shape)} size={size} ready={ready} reason={reason}";

    public static string Dissolved(string id, GothwaitePvpShape shape, int size, string reason) =>
        $"GOTH_PVP_GROUP_DISSOLVED group={id} shape={GothwaitePvpShapes.Name(shape)} size={size} reason={reason}";

    public static string RestStart(string bot, int health, int power) =>
        $"GOTH_PVP_REST bot=\"{bot}\" health={health} power={power}";

    public static string RestEnd(string bot, string reason, int health, int power) =>
        $"GOTH_PVP_REST_END bot=\"{bot}\" reason={reason} health={health} power={power}";

    public static string Return(string bot, string reason, string detail = null) =>
        $"GOTH_PVP_RETURN bot=\"{bot}\" reason={reason}{(string.IsNullOrEmpty(detail) ? string.Empty : " " + detail)}";

    public static string RouteFailed(string bot, string goal, string reason) =>
        $"GOTH_PVP_ROUTE_FAILED bot=\"{bot}\" goal={goal} reason={reason}";

    /// <summary>A fight dropped by the chase limit (castle, edge or self_edge). Logged once per target.</summary>
    public static string Chase(string bot, string target, string reason) =>
        $"GOTH_PVP_CHASE_LIMIT bot=\"{bot}\" target=\"{target}\" reason={reason}";

    /// <summary>A stalled walk: the goal is refused and another picked (repick), or the bot goes back to the castle.</summary>
    public static string Stuck(string bot, string goal, string action) =>
        $"GOTH_PVP_STUCK bot=\"{bot}\" goal={goal} action={action}";

    /// <summary>A bot outside its arena edge walks back inside it. Logged once per excursion.</summary>
    public static string Edge(string bot, int distance, int edge) =>
        $"GOTH_PVP_EDGE bot=\"{bot}\" distance={distance} edge={edge}";

    /// <summary>A group passes to a new leader, when its leader returns or dies and another member is alive in the arena.</summary>
    public static string GroupLeader(string id, string leader, string previous, string reason) =>
        $"GOTH_PVP_GROUP_LEADER group={id} leader=\"{leader}\" previous=\"{previous}\" reason={reason}";

    /// <summary>A map position as the log writes it: x,y,z, or none for a goal that does not exist.</summary>
    public static string Spot(Point3D point) => point == null ? "none" : $"{point.X},{point.Y},{point.Z}";

    /// <summary>
    /// The GOTH_PVP_LOAD line of one finished minute (stage 4): the turns, the scans and the former ticks with their average, the turn
    /// p95 (an upper bound) and the maxima, and the mode bots online. Written once per window while the mode is on.
    /// </summary>
    public static string Load(GothwaitePvpLoadSnapshot load, int onlineBots) =>
        $"GOTH_PVP_LOAD windowS={Number(load.WindowSeconds, "0")} bots={onlineBots} turns={load.Turns} " +
        $"turnAvgMs={Number(load.TurnAverageMilliseconds, "0.000")} turnP95MsAtMost={Number(load.TurnP95Milliseconds, "0.00")} " +
        $"turnMaxMs={Number(load.TurnMaxMilliseconds, "0.00")} scans={load.Scans} scanAvgMs={Number(load.ScanAverageMilliseconds, "0.000")} " +
        $"formers={load.Formers} formerAvgMs={Number(load.FormerAverageMilliseconds, "0.00")} " +
        $"formerMaxMs={Number(load.FormerMaxMilliseconds, "0.00")}";

    /// <summary>A number with a fixed format in the invariant culture, so a German server writes the same decimal point as an English one.</summary>
    public static string Number(double value, string format) => value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
}
