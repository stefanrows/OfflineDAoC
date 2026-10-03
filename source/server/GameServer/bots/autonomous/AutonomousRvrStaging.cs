using System;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// Classic-frontier warbands assemble inside their own safe border keep before
/// entering the frontier. These are fixed realm contracts, not random PvE town
/// choices. The coordinator still projects and validates the point against the
/// installed navmesh before using it.
/// </summary>
public static class AutonomousRvrStaging
{
    public readonly record struct BorderKeep(ushort RegionId, Vector3 Position, string Name);

    public static bool TryGetBorderKeep(eRealm realm, out BorderKeep keep)
    {
        keep = realm switch
        {
            eRealm.Albion => new(1, new(585085, 477504, 2600), "Castle Sauvage"),
            eRealm.Midgard => new(100, new(766235, 669173, 5736), "Svasud Faste"),
            eRealm.Hibernia => new(200, new(333229, 419539, 5336), "Druim Ligen"),
            _ => default,
        };
        return keep.RegionId != 0;
    }

    /// <summary>Radius of a border hub's safe circle (the imported area rows).</summary>
    public const int HubRadius = 3_500;

    /// <summary>
    /// The border hub whose safe circle contains this point, and the realm it
    /// belongs to. Hubs are neutral on Camlann, so a cross-realm warband can
    /// muster at its leader's hub.
    /// </summary>
    public static bool TryGetHubAt(ushort region, Vector3 point, out BorderKeep hub, out eRealm realm)
    {
        foreach (eRealm candidate in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            if (!TryGetBorderKeep(candidate, out BorderKeep keep) || keep.RegionId != region)
                continue;
            float dx = point.X - keep.Position.X, dy = point.Y - keep.Position.Y;
            if (dx * dx + dy * dy > (float)HubRadius * HubRadius)
                continue;
            hub = keep;
            realm = candidate;
            return true;
        }
        hub = default;
        realm = eRealm.None;
        return false;
    }

    /// <summary>
    /// Imported area centers can sit inside keep geometry rather than on a
    /// walkable courtyard polygon. Probe a deterministic set wholly inside the
    /// 3,500-unit safe-area radius; the coordinator still requires a connected
    /// floor and valid formation slots before accepting one.
    /// </summary>
    public static IEnumerable<Vector3> CandidateAnchors(BorderKeep keep, long formationKey = 0)
    {
        int offset = (int)(unchecked((ulong)formationKey) % 12);
        for (int ring = 0; ring < 4; ring++)
        for (int step = 0; step < 12; step++)
        {
            int radius = 600 * (1 + (ring + (int)(unchecked((ulong)formationKey) / 12 % 4)) % 4);
            double angle = Math.PI * 2d * ((step + offset) % 12) / 12d;
            yield return keep.Position + new Vector3(
                (float)(Math.Cos(angle) * radius),
                (float)(Math.Sin(angle) * radius), 0);
        }
        yield return keep.Position;
    }

    /// <summary>Compatibility policy for callers requesting frontier PvP release.</summary>
    public static bool TryFrontierPvpRelease(eRealm realm, bool autonomousRvr, bool pvpDeath, bool diedInFrontier,
        out ushort region, out Point3D point)
    {
        region = 0; point = null;
        if (!autonomousRvr || !pvpDeath || !diedInFrontier || !TryGetBorderKeep(realm, out BorderKeep hub)) return false;
        int x = (int)hub.Position.X, y = (int)hub.Position.Y;
        Point3D bind = BotReleaseBindPoints.Nearest(hub.RegionId, x, y, realm);
        region = hub.RegionId;
        point = bind != null && DOL.GS.ServerRules.PvpCombatant.IsSafeBorderHub(hub.RegionId, bind.X, bind.Y)
            ? bind : new Point3D(x, y, (int)hub.Position.Z);
        return true;
    }

    /// <summary>RvR recovery is at an own-realm sanctuary regardless of killing blow.</summary>
    public static bool TrySafeRvrRelease(eRealm realm, bool autonomousRvr, out ushort region, out Point3D point)
    {
        region = 0;
        point = null;
        if (!autonomousRvr || !TryGetBorderKeep(realm, out BorderKeep hub))
            return false;
        int x = (int)hub.Position.X, y = (int)hub.Position.Y;
        Point3D bind = BotReleaseBindPoints.Nearest(hub.RegionId, x, y, realm, safeOnly: true);
        region = hub.RegionId;
        point = bind ?? BotReleaseBindPoints.Resolve(PathfindingProvider.Instance,
            WorldMgr.GetRegion(region)?.GetZone(x, y), hub.Position);
        return DOL.GS.ServerRules.PvpCombatant.IsSafeReleasePoint(region, point);
    }

    /// <summary>Every formed warband member may acquire a local RvR target;
    /// PvE parties retain their single tank-or-leader puller.</summary>
    public static bool UsesIndependentCombatActors(eAutonomousObjectiveKind objectiveKind) =>
        objectiveKind == eAutonomousObjectiveKind.RvR;

    public static int RollWarbandSize(int maximumSize, double roll)
        => CamlannPopulationTuning.RollWarbandSize(maximumSize, roll);

    /// <summary>
    /// Spreads a warband deterministically across the closest visible enemies.
    /// Limiting the window to party size keeps the force converged instead of
    /// sending one member after a distant target.
    /// </summary>
    public static int TargetIndex(long actorKey, int candidateCount, int warbandSize)
    {
        if (candidateCount <= 1)
            return 0;
        int window = Math.Min(candidateCount, Math.Max(2, warbandSize));
        ulong positive = unchecked((ulong)actorKey);
        return (int)(positive % (uint)window);
    }
}
