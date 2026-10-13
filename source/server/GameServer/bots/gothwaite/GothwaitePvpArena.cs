using System;
using System.Collections.Generic;
using OfflineDaoc.Configuration;

namespace DOL.GS;

/// <summary>One named spot of an arena (castle landing, harbor, patrol point).</summary>
public readonly record struct GothwaitePvpSpot(string Name, int X, int Y, int Z);

/// <summary>
/// An arena as data (docs/GOTHWAITE_PVP_MODE.md section 6.4): region, castle safe circle,
/// castle landings, and patrol points. Gothwaite is the first and only definition.
/// Measured from the installed world database on 2026-10-13 (stage 0).
/// </summary>
public sealed record GothwaitePvpArena
{
    public required string Name { get; init; }
    public required ushort RegionId { get; init; }
    public required int CastleX { get; init; }
    public required int CastleY { get; init; }
    public required int SafeRadius { get; init; }
    /// <summary>Where mode bots log in and release: navmesh-proven floor spots inside the safe circle, around the castle bindstones.</summary>
    public required IReadOnlyList<GothwaitePvpSpot> CastleLandings { get; init; }
    /// <summary>
    /// Castle exits: the points just outside the safe circle that a departing group walks to. A waypoint, not a post: the group does not hold
    /// there, the leader roams on once it is out of the gate (section 6.4). The first one is the exit a group uses.
    /// </summary>
    public IReadOnlyList<GothwaitePvpSpot> CastleApproaches { get; init; } = Array.Empty<GothwaitePvpSpot>();
    /// <summary>
    /// Patrol points outside the castle; every one is proven reachable from the castle (stage 3). Roaming uses only those beyond the roam floor
    /// (<see cref="PatrolPointsFor"/>); the points inside it are kept here as data.
    /// </summary>
    public IReadOnlyList<GothwaitePvpSpot> PatrolPoints { get; init; } = Array.Empty<GothwaitePvpSpot>();

    public static readonly GothwaitePvpArena Gothwaite = new()
    {
        Name = GothwaitePvpSettingsArena,
        RegionId = 51,
        // Area circle "Caer Gothwaite" (region 51), radius 4,000.
        CastleX = 534958,
        CastleY = 548813,
        SafeRadius = 4000,
        // Sixteen castle landings, proven on the installed navmesh on 2026-10-13 by the landing sampler in
        // UT_GothwaitePvpArenaInstalledNavigation (Windows Detour). Each floor snaps tight at the plateau (Z within 40 of 4,800), lies at least
        // 300 units inside the castle circle, and routes to the castle exit and to the first bindstone (CL01). They are picked farthest-point
        // from the 232 passing candidates of a 60-unit grid, 216 to 300 units apart. The earlier random 60-200 unit offsets around two spots were
        // not all on routable floor, and the login check moved bots that landed on one.
        CastleLandings = new GothwaitePvpSpot[]
        {
            new("CL01", 535000, 548790, 4800),
            new("CL02", 535360, 548190, 4800),
            new("CL03", 535540, 548730, 4800),
            new("CL04", 534820, 548310, 4832),
            new("CL05", 534580, 548910, 4800),
            new("CL06", 535120, 549210, 4800),
            new("CL07", 535180, 548490, 4800),
            new("CL08", 534700, 548610, 4800),
            new("CL09", 534820, 549150, 4800),
            new("CL10", 535300, 548910, 4800),
            new("CL11", 535540, 548430, 4800),
            new("CL12", 535060, 548190, 4802),
            new("CL13", 534940, 548550, 4800),
            new("CL14", 534820, 548910, 4800),
            new("CL15", 535000, 549030, 4800),
            new("CL16", 535300, 548670, 4800),
        },
        CastleApproaches = new GothwaitePvpSpot[]
        {
            // About 4,800 units north of the centre (y grows southward), just outside the 4,000 safe circle, in front of the
            // gate. Floor snapped on the installed navmesh (the plateau is at 4,800, the ground outside at 3,898).
            // The Z is an estimate: the installed mesh floor under this point is at Z 3,898 (installed-mesh probe 2026-10-13),
            // so the navigation snaps the goal to the floor and does not reject it for its height.
            new("castle approach N", 535020, 544051, 3898),
        },
        // Stage 0 candidates, snapped to the installed navmesh and each proven to route from the castle landing
        // (UT_GothwaitePvpArenaInstalledNavigation, 2026-10-13). GP02 is the castle exit itself and is left out.
        PatrolPoints = new GothwaitePvpSpot[]
        {
            new("GP01", 532069, 545280, 4643), // 4,564 from the centre
            new("GP03", 540020, 544020, 3169), // 6,971 from the centre
            new("GP04", 528538, 542032, 3379), // 9,338 from the centre
            new("GP05", 544020, 546020, 3162), // 9,483 from the centre
            new("GP06", 543020, 542020, 3125), // 10,542 from the centre
            new("GP07", 525020, 545020, 2915), // 10,637 from the centre
            new("GP08", 526020, 539020, 3188), // 13,259 from the centre
            new("GP09", 540020, 536020, 3732), // 13,758 from the centre
            new("GP10", 522020, 544020, 3168), // 13,797 from the centre
            new("GP11", 527651, 536998, 3169), // 13,892 from the centre
            new("GP12", 548020, 544020, 3168), // 13,914 from the centre
            new("GP13", 525020, 537020, 3125), // 15,422 from the centre
            new("GP14", 520020, 542020, 3324), // 16,410 from the centre
            new("GP15", 539020, 532020, 3524), // 17,277 from the centre
            new("GP16", 526020, 534020, 3044), // 17,284 from the centre
            new("GP17", 524020, 533020, 2853), // 19,211 from the centre
            new("GP18", 541020, 530020, 3827), // 19,747 from the centre
            new("GP19", 526122, 541138, 3168), // 11,704 from the centre
            new("GP20", 524020, 542020, 3168), // 12,876 from the centre
            new("GP21", 522020, 532020, 3023), // 21,199 from the centre
            new("GP22", 550020, 531020, 3124), // 23,312 from the centre
            new("GP23", 519020, 531020, 3306), // 23,887 from the centre
            new("GP24", 511020, 548020, 3138), // 23,951 from the centre
            new("GP25", 511020, 550020, 3168), // 23,968 from the centre
            new("GP26", 514020, 537020, 3305), // 24,031 from the centre
            new("GP27", 512020, 538020, 3240), // 25,350 from the centre
            new("GP28", 514020, 533020, 3161), // 26,226 from the centre
            new("GP29", 512020, 536020, 3263), // 26,264 from the centre
            new("GP30", 537990, 522470, 3350), // 26,517 from the centre
        },
    };

    private const string GothwaitePvpSettingsArena = "gothwaite";

    /// <summary>The gap a roaming goal keeps beyond the no-fight ring (section 6.2), so no goal is reached inside the ring.</summary>
    public const int RoamMargin = 500;
    /// <summary>How far a bot may stand from its castle landing: a few units, so the bots of one landing do not stand on one tile.</summary>
    public const int LandingSpread = 15;

    /// <summary>The castle exit a departing group walks to (the first approach).</summary>
    public GothwaitePvpSpot CastleExit => CastleApproaches[0];

    /// <summary>Squared 2-D distance from the castle centre, in doubles (int math overflows at these coordinates).</summary>
    public double CastleDistanceSquared(int x, int y)
    {
        double dx = x - (double)CastleX, dy = y - (double)CastleY;
        return dx * dx + dy * dy;
    }

    public double CastleDistance(int x, int y) => Math.Sqrt(CastleDistanceSquared(x, y));

    /// <summary>Inside the castle safe circle of this arena's region. Z is ignored.</summary>
    public bool InCastle(ushort regionId, int x, int y) =>
        regionId == RegionId && CastleDistanceSquared(x, y) <= (double)SafeRadius * SafeRadius;

    /// <summary>
    /// Inside the no-fight ring (section 6.2): the castle circle widened by <paramref name="gateRing"/> units. The ring is about the
    /// castle centre, so Z is ignored, and only this arena's region counts.
    /// </summary>
    public bool InGateRing(ushort regionId, int x, int y, int gateRing) =>
        regionId == RegionId && CastleDistanceSquared(x, y) <= (double)(SafeRadius + gateRing) * (SafeRadius + gateRing);

    /// <summary>
    /// The nearest a roaming goal may lie to the castle centre: the outer edge of the no-fight ring plus <see cref="RoamMargin"/>. A bot that
    /// reaches a goal within its arrival distance is then still outside the ring.
    /// </summary>
    public int MinimumRoamDistance(int gateRing) => SafeRadius + gateRing + RoamMargin;

    /// <summary>The farthest a bot of this level may roam or chase from the castle centre (section 6.5).</summary>
    public static int EdgeFor(int level, GothwaitePvpSettings settings) =>
        level <= settings.LowBandMaxLevel ? settings.LowBandEdge : settings.Edge;

    /// <summary>
    /// The patrol points a bot of this level may use as roaming goals: those within its arena edge and beyond the roam floor
    /// (<see cref="MinimumRoamDistance"/>), so no goal lies in the no-fight ring. The points inside the floor stay in
    /// <see cref="PatrolPoints"/> as data.
    /// </summary>
    public IReadOnlyList<GothwaitePvpSpot> PatrolPointsFor(int level, GothwaitePvpSettings settings)
    {
        double edge = EdgeFor(level, settings);
        double floor = MinimumRoamDistance(settings.GateRing);
        var points = new List<GothwaitePvpSpot>();
        foreach (GothwaitePvpSpot point in PatrolPoints)
        {
            double distance = CastleDistanceSquared(point.X, point.Y);
            if (distance <= edge * edge && distance > floor * floor)
                points.Add(point);
        }
        return points;
    }

    /// <summary>
    /// A castle landing for one bot: one of <see cref="CastleLandings"/>, chosen by the bot's id, and at most <see cref="LandingSpread"/>
    /// units from it. The landings are proven on the navmesh (live-run fix 2026-10-13), so a login there is a valid floor.
    /// </summary>
    public GothwaitePvpSpot CastleLanding(long botId)
    {
        ulong seed = unchecked((ulong)botId * 2654435761UL);
        GothwaitePvpSpot landing = CastleLandings[(int)(seed % (ulong)CastleLandings.Count)];
        int angle = (int)(seed / 7 % 360);
        int distance = (int)(seed / 11 % (ulong)(LandingSpread + 1));
        double radians = angle * Math.PI / 180;
        // The int cast truncates, so the offset never exceeds the spread, not even by rounding.
        return landing with
        {
            X = landing.X + (int)(Math.Cos(radians) * distance),
            Y = landing.Y + (int)(Math.Sin(radians) * distance),
        };
    }
}
