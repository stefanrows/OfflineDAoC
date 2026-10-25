using System;
using System.Collections.Generic;
using System.Linq;
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

    // RvR world (upstream 0.38c, port session S1): the siege army's muster and the defense rally's fallback.

    /// <summary>
    /// Where a realm's siege army musters for a target in another region: its own guarded portal
    /// outpost inside that frontier (the frontier teleporter's arrival point), else its border keep.
    /// Run 11: Midgard mustered at Svasud Faste, crossed, and walked 105k-145k units from the Hadrian's
    /// Wall arrival to Castle Myrddin; most were still on the road at the rally deadline.
    /// </summary>
    public static bool TryGetSiegeMuster(eRealm realm, ushort targetRegion, out BorderKeep muster)
    {
        var passage = AutonomousFrontierTransport.Destination(realm, targetRegion);
        if (passage != null && passage.Medallion != "home_necklace")
        {
            Zone zone = WorldMgr.GetRegion(passage.Region)?.GetZone(passage.Location.X, passage.Location.Y);
            muster = new(passage.Region, new(passage.Location.X, passage.Location.Y, passage.Location.Z),
                $"the portal outpost in {zone?.Description ?? "the frontier"}");
            return true;
        }
        return TryGetBorderKeep(realm, out muster);
    }

    // Each realm's two classic border keeps (safe-area centres from the Area table). Owner 2026-10-07: bots only
    // ever entered the frontier through the first one; the second was never offered.
    private static readonly BorderKeep[] AlbionKeeps =
        [new(1, new(585085, 477504, 2600), "Castle Sauvage"), new(1, new(528451, 358290, 8320), "Snowdonia Fortress")];
    private static readonly BorderKeep[] MidgardKeeps =
        [new(100, new(766235, 669173, 5736), "Svasud Faste"), new(100, new(704022, 738009, 5704), "Vindsaul Faste")];
    private static readonly BorderKeep[] HiberniaKeeps =
        [new(200, new(333229, 419539, 5336), "Druim Ligen"), new(200, new(421166, 484998, 1976), "Druim Cain")];

    public static IReadOnlyList<BorderKeep> BorderKeeps(eRealm realm) => realm switch
    {
        eRealm.Albion => AlbionKeeps,
        eRealm.Midgard => MidgardKeeps,
        eRealm.Hibernia => HiberniaKeeps,
        _ => [],
    };

    /// <summary>The border keep nearest a point in its region (shared by a whole army), else the first one.</summary>
    public static bool TryGetBorderKeepNear(eRealm realm, ushort regionId, Vector2 point, out BorderKeep keep)
    {
        var keeps = BorderKeeps(realm);
        keep = keeps.Where(k => k.RegionId == regionId).OrderBy(k => Vector2.DistanceSquared(new(k.Position.X, k.Position.Y), point))
            .FirstOrDefault();
        if (keep.RegionId == 0) keep = keeps.FirstOrDefault();
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
