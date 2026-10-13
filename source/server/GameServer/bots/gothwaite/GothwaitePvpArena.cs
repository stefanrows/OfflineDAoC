using System;
using System.Collections.Generic;

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
    };

    private const string GothwaitePvpSettingsArena = "gothwaite";

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
