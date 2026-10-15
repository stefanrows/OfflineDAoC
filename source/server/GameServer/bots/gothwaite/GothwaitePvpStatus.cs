using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>Where a mode bot is, as the status command counts it: resting or ready in the castle, departing, or in the arena.</summary>
public enum GothwaitePvpBotPhase { CastleResting, CastleReady, Departing, Arena }

/// <summary>One mode bot as the status sees it: its level and its phase.</summary>
public readonly record struct GothwaitePvpBotSample(int Level, GothwaitePvpBotPhase Phase);

/// <summary>The mode bots of one level band, by phase.</summary>
public readonly record struct GothwaitePvpBandPhases(string Label, int Total, int CastleResting, int CastleReady, int Departing, int Arena);

/// <summary>
/// The castle former's groups at one moment: the open groups still gathering (with their members), the bots waiting in the pool,
/// the departed groups by shape (index: <see cref="GothwaitePvpShape"/>), and the members of the groups of two or more, forming and
/// departed alike, for their average size.
/// </summary>
public sealed record GothwaitePvpGroupCensus(int Forming, int FormingMembers, int Waiting, IReadOnlyList<int> DepartedByShape,
    int MultiGroups, int MultiMembers)
{
    public int Departed => DepartedByShape.Sum();

    /// <summary>The average size of the groups of two or more (zero when there is none).</summary>
    public double AverageSize => MultiGroups == 0 ? 0 : (double)MultiMembers / MultiGroups;

    public static GothwaitePvpGroupCensus Empty { get; } = new(0, 0, 0, new int[4], 0, 0);
}

/// <summary>
/// Everything the status command prints (docs/GOTHWAITE_PVP_MODE.md section 8, stage 4). Fights and load are null while the mode is off.
/// </summary>
public sealed record GothwaitePvpStatusReport(bool Active, ushort Region, string Arena, int SafeRadius,
    IReadOnlyList<GothwaitePvpBotSample> Bots, GothwaitePvpGroupCensus Groups, GothwaitePvpStatsSnapshot Fights,
    GothwaitePvpLoadSnapshot Load);

/// <summary>
/// The Gothwaite PvP status (stage 4): the bots online in the mode by level band and phase, the castle groups, the fights with
/// their kills and deaths by band and by con colour, and the load the mode's own meter measured. <see cref="Capture"/> reads the
/// live state (lock-light, once per command); <see cref="Lines"/> and <see cref="CountBands"/> are pure and tested.
/// </summary>
public static class GothwaitePvpStatus
{
    /// <summary>The only line the status prints while the mode is off.</summary>
    public const string OffLine = "Gothwaite PvP mode is off.";

    private const int BandRows = 5;
    private const int PhaseCount = 4;

    /// <summary>
    /// The mode bots of each level band, by phase (pure). A level below 1 falls in no band and is left out of the rows.
    /// </summary>
    public static GothwaitePvpBandPhases[] CountBands(IEnumerable<GothwaitePvpBotSample> bots)
    {
        var totals = new int[BandRows];
        var phases = new int[BandRows, PhaseCount];
        foreach (GothwaitePvpBotSample bot in bots)
        {
            int band = GothwaitePvpStats.BandOf(bot.Level);
            if (band < 0)
                continue;
            totals[band]++;
            phases[band, (int)bot.Phase]++;
        }
        var rows = new GothwaitePvpBandPhases[BandRows];
        for (int band = 0; band < BandRows; band++)
            rows[band] = new GothwaitePvpBandPhases(GothwaitePvpStats.BandLabel(band), totals[band],
                phases[band, (int)GothwaitePvpBotPhase.CastleResting], phases[band, (int)GothwaitePvpBotPhase.CastleReady],
                phases[band, (int)GothwaitePvpBotPhase.Departing], phases[band, (int)GothwaitePvpBotPhase.Arena]);
        return rows;
    }

    /// <summary>The mode bots online now: the autonomous world bots of the registry that the mode drives. Read once per call.</summary>
    public static int CountOnline() => AutonomousBotRegistry.Snapshot().Count(bot => GothwaitePvpMode.IsModeBot(bot));

    /// <summary>
    /// The live report. With the mode off it reads nothing but the settings. With the mode on it reads the registry (a copy of the
    /// active bots), each bot's driver phase, the former's census, the fight statistics and the load meter.
    /// </summary>
    public static GothwaitePvpStatusReport Capture()
    {
        GothwaitePvpArena arena = GothwaitePvpMode.Arena;
        if (!GothwaitePvpMode.IsActive)
            return new GothwaitePvpStatusReport(false, arena.RegionId, arena.Name, arena.SafeRadius,
                Array.Empty<GothwaitePvpBotSample>(), GothwaitePvpGroupCensus.Empty, null, null);
        var bots = new List<GothwaitePvpBotSample>();
        foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
            if (GothwaitePvpMode.IsModeBot(bot))
                bots.Add(new GothwaitePvpBotSample(bot.Level, GothwaitePvpDriver.PhaseOf(bot)));
        return new GothwaitePvpStatusReport(true, arena.RegionId, arena.Name, arena.SafeRadius, bots,
            GothwaitePvpGroupFormer.Census(), GothwaitePvpStats.Snapshot(),
            GothwaitePvpLoadMeter.Snapshot(Environment.TickCount64));
    }

    /// <summary>
    /// The status as chat lines (pure). With the mode off it is the single <see cref="OffLine"/>. Numbers use the invariant culture,
    /// so a German server prints the same figures as an English one.
    /// </summary>
    public static IReadOnlyList<string> Lines(GothwaitePvpStatusReport report)
    {
        if (report == null || !report.Active)
            return new[] { OffLine };

        var lines = new List<string>();
        IReadOnlyList<GothwaitePvpBotSample> bots = report.Bots ?? Array.Empty<GothwaitePvpBotSample>();
        int resting = 0, ready = 0, departing = 0, arena = 0;
        foreach (GothwaitePvpBotSample bot in bots)
        {
            switch (bot.Phase)
            {
                case GothwaitePvpBotPhase.CastleReady: ready++; break;
                case GothwaitePvpBotPhase.Departing: departing++; break;
                case GothwaitePvpBotPhase.Arena: arena++; break;
                default: resting++; break;
            }
        }
        lines.Add($"Gothwaite PvP mode: on. Arena {report.Arena} (region {report.Region}), castle safe radius {report.SafeRadius}.");
        lines.Add($"Bots online in the mode: {bots.Count} (castle resting {resting}, castle ready {ready}, departing {departing}, " +
            $"arena {arena}). A dead bot counts as castle resting until released.");
        foreach (GothwaitePvpBandPhases band in CountBands(bots))
            lines.Add($"  Levels {band.Label}: {band.Total} (castle resting {band.CastleResting}, castle ready {band.CastleReady}, " +
                $"departing {band.Departing}, arena {band.Arena}).");

        GothwaitePvpGroupCensus groups = report.Groups ?? GothwaitePvpGroupCensus.Empty;
        lines.Add($"Groups: forming {groups.Forming} ({groups.FormingMembers} members), departed {groups.Departed} " +
            $"(solo {Count(groups, 0)}, duo {Count(groups, 1)}, small {Count(groups, 2)}, eight {Count(groups, 3)}), " +
            $"average size {GothwaitePvpLines.Number(groups.AverageSize, "0.0")} for groups of two or more, " +
            $"castle pool waiting {groups.Waiting}.");

        GothwaitePvpStatsSnapshot fights = report.Fights;
        if (fights != null)
        {
            double minutes = Math.Max(0, fights.AsOfGameTime - fights.WindowStartGameTime) / 60_000d;
            lines.Add($"Fights since server start (game time {GothwaitePvpLines.Number(minutes, "0.0")} min): " +
                $"mode bot kills {fights.ModeKills}, mode bot deaths {fights.ModeDeaths}.");
            foreach (GothwaitePvpBandCount band in fights.ModeBands)
                lines.Add($"  Levels {band.Label}: kills {band.Kills}, deaths {band.Deaths}.");
            if (fights.ModeKillsByCon.Count > 0)
                lines.Add("Mode bot kills by con (incl. self-defence): " +
                    string.Join(", ", fights.ModeKillsByCon.Select(con => $"{con.Color} {con.Kills}")) + ".");
            lines.Add($"All arena fights with a mode bot on either side (players included): kills {fights.TotalKills}, " +
                $"deaths {fights.TotalDeaths}.");
            GothwaitePvpTacticsCount tactics = fights.Tactics;
            lines.Add($"Tactics: tanks took the lead {tactics.MainAssists}, openings refused (party too big) {tactics.RefusedOpenings}, " +
                $"retreats {tactics.Retreats}, regroups {tactics.Regroups}, adds {tactics.Adds}, stealth openings {tactics.Ambushes}.");
        }

        lines.Add(LoadLine(report.Load, bots.Count));
        return lines;
    }

    private static int Count(GothwaitePvpGroupCensus groups, int shape) =>
        shape < groups.DepartedByShape.Count ? groups.DepartedByShape[shape] : 0;

    private static string LoadLine(GothwaitePvpLoadSnapshot load, int online)
    {
        if (load == null || load.Turns <= 0)
            return $"Load at {online} bots: no mode turns measured yet.";
        return $"Load at {online} bots over the last {GothwaitePvpLines.Number(load.WindowSeconds, "0")} s (real time): " +
            $"turn avg {GothwaitePvpLines.Number(load.TurnAverageMilliseconds, "0.00")} ms, " +
            $"p95 <= {GothwaitePvpLines.Number(load.TurnP95Milliseconds, "0.00")} ms, " +
            $"max {GothwaitePvpLines.Number(load.TurnMaxMilliseconds, "0.00")} ms ({load.Turns} turns); " +
            $"scan avg {GothwaitePvpLines.Number(load.ScanAverageMilliseconds, "0.00")} ms ({load.Scans}); " +
            $"castle former avg {GothwaitePvpLines.Number(load.FormerAverageMilliseconds, "0.00")} ms, " +
            $"max {GothwaitePvpLines.Number(load.FormerMaxMilliseconds, "0.00")} ms ({load.Formers} ticks).";
    }
}
