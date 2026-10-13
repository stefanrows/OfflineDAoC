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
    /// <summary>Where mode bots log in and release: next to the castle bindstones, inside the safe circle.</summary>
    public required IReadOnlyList<GothwaitePvpSpot> CastleLandings { get; init; }
    /// <summary>
    /// Castle exits: the points just outside the safe circle where a departing group walks to and holds (section 6.3).
    /// The first one is the exit a group uses.
    /// </summary>
    public IReadOnlyList<GothwaitePvpSpot> CastleApproaches { get; init; } = Array.Empty<GothwaitePvpSpot>();
    /// <summary>Roaming goals outside the castle; every one is proven reachable from the castle (stage 3).</summary>
    public IReadOnlyList<GothwaitePvpSpot> PatrolPoints { get; init; } = Array.Empty<GothwaitePvpSpot>();

    public static readonly GothwaitePvpArena Gothwaite = new()
    {
        Name = GothwaitePvpSettingsArena,
        RegionId = 51,
        // Area circle "Caer Gothwaite" (region 51), radius 4,000.
        CastleX = 534958,
        CastleY = 548813,
        SafeRadius = 4000,
        CastleLandings = new GothwaitePvpSpot[]
        {
            // Beside the two castle bindstones (about 535,000 / 548,790).
            new("castle bindstone", 535000, 548790, 4800),
            // The all-realms teleporter landing (AllRealmsTeleportFallbacks "1:Gothwaite").
            new("castle teleporter landing", 535158, 548589, 4800),
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

    /// <summary>The farthest a bot of this level may roam or chase from the castle centre (section 6.5).</summary>
    public static int EdgeFor(int level, GothwaitePvpSettings settings) =>
        level <= settings.LowBandMaxLevel ? settings.LowBandEdge : settings.Edge;

    /// <summary>The patrol points a bot of this level may use: those within its arena edge.</summary>
    public IReadOnlyList<GothwaitePvpSpot> PatrolPointsFor(int level, GothwaitePvpSettings settings)
    {
        double edge = EdgeFor(level, settings);
        var points = new List<GothwaitePvpSpot>();
        foreach (GothwaitePvpSpot point in PatrolPoints)
            if (CastleDistanceSquared(point.X, point.Y) <= edge * edge)
                points.Add(point);
        return points;
    }

    /// <summary>A castle landing for one bot, chosen and spread by its id so bots do not stack on one spot.</summary>
    public GothwaitePvpSpot CastleLanding(long botId)
    {
        ulong seed = unchecked((ulong)botId * 2654435761UL);
        GothwaitePvpSpot landing = CastleLandings[(int)(seed % (ulong)CastleLandings.Count)];
        int angle = (int)(seed / 7 % 360);
        int distance = 60 + (int)(seed / 11 % 140);
        double radians = angle * Math.PI / 180;
        return landing with
        {
            X = landing.X + (int)Math.Round(Math.Cos(radians) * distance),
            Y = landing.Y + (int)Math.Round(Math.Sin(radians) * distance),
        };
    }
}
