using System;
using System.Collections.Generic;
using DOL.Database;

namespace DOL.GS;

/// <summary>
/// Pure policy seams for Darkness Falls. Live entrance authority remains in
/// DFEnterJumpPoint; these helpers make its keep-control and local-PvP rules
/// independently testable.
/// </summary>
public static class AutonomousDarknessFallsPolicy
{
    public const ushort RegionId = 249;

    public static bool CanEnter(
        eRealm realm,
        eRealm currentOwner,
        eRealm previousOwner,
        long nowTick,
        long lastSwapTick,
        long gracePeriod,
        bool allowAllRealms,
        bool normalServer)
    {
        if (!normalServer || allowAllRealms)
            return true;
        if (realm == eRealm.None)
            return false;
        if (realm == previousOwner && lastSwapTick + Math.Max(0, gracePeriod) >= nowTick)
            return true;
        return realm == currentOwner;
    }

    public static bool CanUseRegionEdge(eRealm realm, ushort sourceRegion, ushort targetRegion, Func<eRealm, bool> canEnter)
    {
        if (targetRegion != RegionId || sourceRegion == RegionId)
            return true;
        return canEnter?.Invoke(realm) == true;
    }

    /// <summary>The "no route" value of the controller's travel estimates.</summary>
    public const double NoRouteTravelMinutes = 9999;

    // Entrance sources whose floor is an isolated navmesh patch: a complete
    // Detour search from it reaches neither the surrounding road nor the
    // nearest keep (zone point 87 in Connacht: no path to the Connacht road
    // or Druim Ligen on the installed zone207.nav, 2026-10-10). Bots sent
    // there never arrive; the same realm's other entrances land in the same
    // Darkness Falls hall. Keyed to the authored source position, so a moved
    // or rebuilt zone point is no longer excluded.
    private static readonly (ushort Region, int X, int Y)[] IsolatedEntranceSources =
    [
        (200, 325269, 433985),
    ];

    /// <summary>A Darkness Falls entrance that autonomous routes must not use (see IsolatedEntranceSources).</summary>
    public static bool IsIsolatedEntrance(DbZonePoint point)
    {
        if (point == null || point.TargetRegion != RegionId || point.SourceRegion == RegionId)
            return false;
        foreach ((ushort region, int x, int y) in IsolatedEntranceSources)
        {
            if (point.SourceRegion == region && Math.Abs(point.SourceX - x) <= 64 && Math.Abs(point.SourceY - y) <= 64)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Whether camp planning may offer a camp. It already checks that the
    /// camp's region is reachable at all; a Darkness Falls room is entered
    /// only through its proven entrance landings, and each realm reaches only
    /// its own, so a DF camp also needs a real crossing (bug 78 remainder:
    /// bots drew rooms behind another realm's entrance and abandoned them
    /// with "No legal region route"). The answer depends only on the room's
    /// entrance set, so one planning pass computes it once per set.
    /// </summary>
    public static bool HasCampRoute(ushort currentRegion, ushort campRegion, int entranceGroup,
        IDictionary<int, bool> routeByEntranceGroup, Func<double> estimateTravelMinutes)
    {
        if (campRegion != RegionId || currentRegion == RegionId)
            return true;
        if (routeByEntranceGroup != null && routeByEntranceGroup.TryGetValue(entranceGroup, out bool known))
            return known;
        bool route = estimateTravelMinutes != null && estimateTravelMinutes() < NoRouteTravelMinutes;
        if (routeByEntranceGroup != null)
            routeByEntranceGroup[entranceGroup] = route;
        return route;
    }

    public static bool CanEngageLocalOpponent(
        bool enemyCombatant,
        ushort attackerRegion,
        ushort targetRegion,
        bool targetAlive,
        bool allowedByServerRules) =>
        attackerRegion == RegionId && targetRegion == RegionId && targetAlive && allowedByServerRules &&
        enemyCombatant;
}
