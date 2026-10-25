using System.Linq;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>
/// Which realm a place belongs to in the RvR world (package R2,
/// docs/RVR_TRAVEL_POPULATION.md): a homeland zone or capital, a portal keep,
/// a border hub circle or a hub landing circle. Release and bind points use it
/// so a character of the RvR world only ever wakes in its own realm's lands;
/// the per-realm hub protection (<see cref="HubProtects"/>) is its other user.
/// Frontier fields, shared dungeons and realm-less regions belong to nobody.
/// The Camlann world never asks: every answer there is "open".
/// </summary>
public static class RealmPlaces
{
    /// <summary>The realm of the border hub circle (Castle Sauvage, Svasud Faste, Druim Ligen) or hub landing circle at a point, or None.</summary>
    public static eRealm HubRealmAt(ushort regionId, int x, int y)
    {
        foreach (eRealm realm in RealmTravel.PlayableRealms)
        {
            if (!AutonomousRvrStaging.TryGetBorderKeep(realm, out AutonomousRvrStaging.BorderKeep hub) || hub.RegionId != regionId)
                continue;
            double dx = x - (double)hub.Position.X, dy = y - (double)hub.Position.Y;
            if (dx * dx + dy * dy <= (double)PvpCombatant.SafeBorderHubRadius * PvpCombatant.SafeBorderHubRadius)
                return realm;
        }

        foreach (PvpCombatant.SafeHubLanding landing in PvpCombatant.SafeHubLandings)
        {
            if (landing.RegionId != regionId)
                continue;
            double dx = x - (double)landing.X, dy = y - (double)landing.Y;
            if (dx * dx + dy * dy <= (double)landing.Radius * landing.Radius)
                return BorderHubRealmOfRegion(regionId);
        }

        return eRealm.None;
    }

    /// <summary>The realm whose border hub stands in this frontier region (1 Albion, 100 Midgard, 200 Hibernia).</summary>
    public static eRealm BorderHubRealmOfRegion(ushort regionId)
    {
        foreach (eRealm realm in RealmTravel.PlayableRealms)
            if (AutonomousRvrStaging.TryGetBorderKeep(realm, out AutonomousRvrStaging.BorderKeep hub) && hub.RegionId == regionId)
                return realm;
        return eRealm.None;
    }

    /// <summary>A portal keep belongs to its original realm; the reset writes that realm, so either field works.</summary>
    public static eRealm PortalKeepRealm(AbstractGameKeep keep) =>
        keep == null ? eRealm.None : RealmTravel.IsPlayable(keep.OriginalRealm) ? keep.OriginalRealm : keep.Realm;

    /// <summary>
    /// The owner of a point. A homeland zone or capital first (the upstream bot boundary table), then a portal
    /// keep standing there, then a border hub or hub landing circle. None for open frontier and shared places.
    /// </summary>
    public static eRealm OwnerAt(ushort regionId, ushort zoneId, int x, int y, eRealm portalKeepRealm)
    {
        eRealm homeland = AutonomousWorldBotController.ProtectedRealm(regionId, zoneId);
        if (RealmTravel.IsPlayable(homeland))
            return homeland;
        if (RealmTravel.IsPlayable(portalKeepRealm))
            return portalKeepRealm;
        return HubRealmAt(regionId, x, y);
    }

    /// <summary>Pure rule: Camlann accepts every point; the RvR world only points that are not another realm's.</summary>
    public static bool IsOwnRealmPoint(WorldModelKind world, eRealm realm, ushort regionId, ushort zoneId, int x, int y,
        eRealm portalKeepRealm) =>
        world != WorldModelKind.Realm || !RealmTravel.IsPlayable(realm) ||
        RealmTravel.Allows(world, realm, OwnerAt(regionId, zoneId, x, y, portalKeepRealm));

    /// <summary>
    /// Live form for a bind or release point. A point the world cannot place (no region or zone) is left to the
    /// caller's own validation.
    /// </summary>
    public static bool IsOwnRealmPoint(eRealm realm, ushort regionId, int x, int y, int z)
    {
        if (!WorldMode.IsRvr)
            return true;
        Region region = WorldMgr.GetRegion(regionId);
        Zone zone = region?.GetZone(x, y);
        if (zone == null)
            return true;
        AbstractGameKeep portal = region.GetAreasOfSpot(new Point3D(x, y, z)).OfType<KeepArea>()
            .Select(area => area.Keep).FirstOrDefault(keep => keep?.IsPortalKeep == true);
        return IsOwnRealmPoint(WorldModelKind.Realm, realm, regionId, zone.ID, x, y, PortalKeepRealm(portal));
    }

    /// <summary>
    /// Safe hubs per realm (spec 3.2): in the RvR world a portal keep, border hub or landing circle protects only
    /// the realm it belongs to, so an intruder standing in it can be fought. Camlann: every hub protects everyone.
    /// This is the rule for the PvpCombatant.IsSafeArea hook (see RVR_TRAVEL_POPULATION.md, "Open").
    /// </summary>
    public static bool HubProtects(WorldModelKind world, eRealm hubRealm, eRealm actorRealm) =>
        world != WorldModelKind.Realm || !RealmTravel.IsPlayable(hubRealm) || hubRealm == actorRealm;
}
