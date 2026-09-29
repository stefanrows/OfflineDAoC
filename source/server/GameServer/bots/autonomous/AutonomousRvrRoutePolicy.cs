using System;
using System.Numerics;

namespace DOL.GS;

/// <summary>How a roaming RvR group gets to its next spot.</summary>
public enum RvrRouteVariant
{
    /// <summary>The shortest road: via-points straight ahead, as before.</summary>
    Road,
    /// <summary>One via-point well to the side of the leg ("over the hill").</summary>
    Flank,
    /// <summary>One via-point on the side away from the latest fighting.</summary>
    Cover,
    /// <summary>Leaving the own border hub on a random bearing, before the real route.</summary>
    HubFan,
}

/// <summary>
/// The navigation checks a via-point must pass, as delegates so the choice
/// itself stays a pure function. <see cref="Floor"/> returns the walkable
/// floor near a raw point within the given vertical search range, or null;
/// <see cref="SameZone"/> tells whether a point lies in the actor's zone;
/// <see cref="Corridor"/> whether a complete path connects two points.
/// </summary>
public sealed record RvrRouteProbe(
    Func<Vector3, float, Vector3?> Floor,
    Func<Vector3, bool> SameZone,
    Func<Vector3, Vector3, bool> Corridor,
    bool DestinationInZone);

public readonly record struct RvrRouteChoice(Vector3 Waypoint, RvrRouteVariant Variant, bool Fallback, float LegLength);

/// <summary>
/// Route variety for autonomous RvR groups (P3: you leave the door before you
/// hunt). A 2003 zerg walked the road, stealthers and small groups went over
/// the hills beside it, and groups leaving the portal keep spread out instead
/// of all taking one corridor. The variant is rolled once per destination.
/// </summary>
public static class AutonomousRvrRoutePolicy
{
    /// <summary>Legs shorter than this are walked directly, as before.</summary>
    public const float MinimumLegLength = 3_500;
    /// <summary>The Road via-point lies this far ahead on the straight line.</summary>
    public const float RoadStep = 2_400;
    public const float FlankMinimumOffset = 1_200;
    public const float FlankMaximumOffset = 2_400;
    public const double FlankMinimumFraction = 0.4;
    public const double FlankMaximumFraction = 0.6;
    /// <summary>A heat spot farther than this from the leg does not shape a Cover route.</summary>
    public const float CoverHeatRange = 6_000;
    /// <summary>The fan point lies this far beyond the edge of the safe
    /// circle the bot leaves: 4,500-6,000 from a keep centre (radius 3,500).</summary>
    public const float HubFanMinimumMargin = 1_000;
    public const float HubFanMaximumMargin = 2_500;
    public const float KeepSafeRadius = 3_500;
    public const float HubFanMinimumRadius = KeepSafeRadius + HubFanMinimumMargin;
    public const float HubFanMaximumRadius = KeepSafeRadius + HubFanMaximumMargin;
    /// <summary>Vertical search range for a fan point: hubs such as Svasud
    /// Faste sit on a rise well above the land around them.</summary>
    public const float HubFanFloorRange = 1_024;
    /// <summary>Bearings tried for the hub fan before falling back to Road.</summary>
    public const int HubFanAttempts = 4;
    /// <summary>
    /// The fan bearing lies within this many degrees of the destination's
    /// direction: groups leave by different sides of the hub, but nobody walks
    /// out of the back gate to go forward.
    /// </summary>
    public const double HubFanHalfArcDegrees = 120;
    /// <summary>Vertical search range for off-road via-points on hills.</summary>
    public const float OffRoadFloorRange = 512;
    private const float RoadFloorRange = 128;

    public static bool IsStealthDoctrine(RvrDoctrineKind kind) =>
        kind is RvrDoctrineKind.SoloAssassin or RvrDoctrineKind.StealthPack or RvrDoctrineKind.GankSquad;

    public static bool IsRoadDoctrine(RvrDoctrineKind kind) =>
        kind is RvrDoctrineKind.AssistTrain or RvrDoctrineKind.MeleeTrain or RvrDoctrineKind.KeepRaid;

    /// <summary>Road / Flank / Cover shares for a doctrine; a cautious leader
    /// (RiskTolerance below 40) moves 20 percentage points to Cover, taken from
    /// Road first.</summary>
    public static (double Road, double Flank, double Cover) Weights(RvrDoctrineKind kind, RvrLeaderTraits traits)
    {
        (double road, double flank, double cover) = IsStealthDoctrine(kind) ? (0.2, 0.4, 0.4) :
            IsRoadDoctrine(kind) ? (0.7, 0.2, 0.1) : (0.5, 0.3, 0.2);
        if (traits.RiskTolerance < 40)
        {
            double shift = 0.2;
            double fromRoad = Math.Min(road, shift);
            road -= fromRoad;
            double fromFlank = Math.Min(flank, shift - fromRoad);
            flank -= fromFlank;
            cover += fromRoad + fromFlank;
        }
        return (road, flank, cover);
    }

    /// <summary>The route variant for a roll in [0,1).</summary>
    public static RvrRouteVariant PickVariant(RvrDoctrineKind kind, RvrLeaderTraits traits, double roll)
    {
        (double road, double flank, _) = Weights(kind, traits);
        roll = Math.Clamp(roll, 0, 0.999999);
        if (roll < road) return RvrRouteVariant.Road;
        if (roll < road + flank) return RvrRouteVariant.Flank;
        return RvrRouteVariant.Cover;
    }

    public static string Label(RvrRouteVariant variant) => variant switch
    {
        RvrRouteVariant.Flank => "flank",
        RvrRouteVariant.Cover => "cover",
        RvrRouteVariant.HubFan => "hub_fan",
        _ => "road",
    };

    /// <summary>
    /// The next waypoint toward <paramref name="destination"/>. Road keeps the
    /// earlier behaviour (a straight-ahead point 2,400 out, no side offset);
    /// Flank and Cover add one via-point to the side; HubFan leaves a hub on a
    /// random bearing 4,500-6,000 from its centre. A via-point that fails the
    /// floor, zone or corridor checks falls back to Road, and Road falls back
    /// to the destination itself.
    /// </summary>
    public static RvrRouteChoice ChooseRoute(Vector3 start, Vector3 destination, RvrRouteVariant variant,
        RvrRouteProbe probe, Random random, Vector3? heat = null, Vector3? hubCentre = null,
        float hubSafeRadius = KeepSafeRadius)
    {
        Vector2 delta = new(destination.X - start.X, destination.Y - start.Y);
        float leg = delta.Length();
        if (probe == null || leg < MinimumLegLength)
            return new(destination, RvrRouteVariant.Road, variant != RvrRouteVariant.Road, leg);
        random ??= Random.Shared;
        Vector2 direction = delta / leg;
        Vector2 perpendicular = new(-direction.Y, direction.X);

        switch (variant)
        {
            case RvrRouteVariant.HubFan:
            {
                // A failed fan returns at once; the caller plans the rolled
                // route next, so no Road fallback is computed and discarded.
                if (!hubCentre.HasValue)
                    return new(destination, RvrRouteVariant.HubFan, true, leg);
                double baseAngle = Math.Atan2(direction.Y, direction.X);
                for (int attempt = 0; attempt < HubFanAttempts; attempt++)
                {
                    double angle = baseAngle + (random.NextDouble() * 2 - 1) * HubFanHalfArcDegrees * Math.PI / 180;
                    float radius = hubSafeRadius + HubFanMinimumMargin +
                        (float)random.NextDouble() * (HubFanMaximumMargin - HubFanMinimumMargin);
                    Vector2 point = new(hubCentre.Value.X + (float)Math.Cos(angle) * radius,
                        hubCentre.Value.Y + (float)Math.Sin(angle) * radius);
                    // Height guess toward the goal, like Flank; the wide probe finds the real floor.
                    float along = Math.Clamp(Vector2.Distance(new(start.X, start.Y), point) / leg, 0, 1);
                    Vector3 raw = new(point.X, point.Y, start.Z + (destination.Z - start.Z) * along);
                    if (TryVia(start, destination, raw, HubFanFloorRange, probe, out Vector3 via))
                        return new(via, RvrRouteVariant.HubFan, false, leg);
                }
                return new(destination, RvrRouteVariant.HubFan, true, leg);
            }
            case RvrRouteVariant.Flank:
            case RvrRouteVariant.Cover:
            {
                double fraction = FlankMinimumFraction + random.NextDouble() * (FlankMaximumFraction - FlankMinimumFraction);
                float offset = FlankMinimumOffset + (float)random.NextDouble() * (FlankMaximumOffset - FlankMinimumOffset);
                float side = random.NextDouble() < 0.5 ? -1 : 1;
                Vector2 anchor = new Vector2(start.X, start.Y) + direction * (float)(leg * fraction);
                if (variant == RvrRouteVariant.Cover && heat.HasValue &&
                    Vector2.Distance(anchor, new(heat.Value.X, heat.Value.Y)) <= CoverHeatRange)
                {
                    float heatSide = CoverSide(start, direction, heat.Value);
                    if (heatSide != 0) side = heatSide;
                }
                Vector2 point = anchor + perpendicular * side * offset;
                float z = start.Z + (destination.Z - start.Z) * (float)fraction;
                if (TryVia(start, destination, new(point.X, point.Y, z), OffRoadFloorRange, probe, out Vector3 via))
                    return new(via, variant, false, leg);
                break;
            }
        }

        // Road: today's straight-ahead point, now without a per-bot side offset.
        Vector3 road = start + new Vector3(direction.X * RoadStep, direction.Y * RoadStep, 0);
        bool fallback = variant != RvrRouteVariant.Road;
        return TryVia(start, destination, road, RoadFloorRange, probe, out Vector3 roadVia)
            ? new(roadVia, RvrRouteVariant.Road, fallback, leg)
            : new(destination, RvrRouteVariant.Road, true, leg);
    }

    /// <summary>+1 or -1: the side of the leg (along the left-hand
    /// perpendicular) away from the heat spot; 0 when it lies on the line.</summary>
    public static float CoverSide(Vector3 start, Vector2 direction, Vector3 heat)
    {
        Vector2 toHeat = new(heat.X - start.X, heat.Y - start.Y);
        float cross = direction.X * toHeat.Y - direction.Y * toHeat.X;
        return cross > 0 ? -1 : cross < 0 ? 1 : 0;
    }

    private static bool TryVia(Vector3 start, Vector3 destination, Vector3 raw, float floorRange,
        RvrRouteProbe probe, out Vector3 via)
    {
        via = destination;
        if (!probe.SameZone(raw)) return false;
        Vector3? floor = probe.Floor(raw, floorRange);
        if (!floor.HasValue || Math.Abs(floor.Value.Z - raw.Z) > Math.Max(256, floorRange) ||
            !probe.Corridor(start, floor.Value) ||
            probe.DestinationInZone && !probe.Corridor(floor.Value, destination))
            return false;
        via = floor.Value;
        return true;
    }
}
