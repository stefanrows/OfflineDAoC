using System.Numerics;

namespace DOL.GS;

/// <summary>
/// Where an autonomous world bot may go. Both worlds keep battleground regions
/// for admitted campaign participants only. The Camlann world opens every
/// homeland to every realm (Tier 2). The RvR world returns to the upstream
/// rule (RVR_TRAVEL_POPULATION.md): no bot enters another realm's homeland or
/// capital; frontier zones, shared dungeons and realm-less regions stay open.
/// </summary>
public static class AutonomousRealmBoundary
{
    public static bool IsBattlegroundRegion(ushort region) => region is 250 or 252 or 253 || BattlegroundCampaignCatalog.Find(region) != null;

    public static bool Allows(eRealm realm, ushort region, ushort zone) =>
        Allows(WorldMode.Current, realm, region, zone);

    /// <summary>Pure form for the both-worlds tests.</summary>
    public static bool Allows(WorldModelKind world, eRealm realm, ushort region, ushort zone)
    {
        if (IsBattlegroundRegion(region))
            return false;
        return world != WorldModelKind.Realm || AllowsHomeland(realm, region, zone);
    }

    /// <summary>The upstream homeland rule: a protected zone admits only its own realm.</summary>
    public static bool AllowsHomeland(eRealm realm, ushort region, ushort zone)
    {
        eRealm owner = AutonomousWorldBotController.ProtectedRealm(region, zone);
        return owner == eRealm.None || owner == realm;
    }

    /// <summary>A destination point (porter landing): its zone must admit the realm. Unknown regions pass.</summary>
    public static bool AllowsPoint(eRealm realm, ushort region, int x, int y)
    {
        if (!WorldMode.IsRvr) return true;
        Zone zone = WorldMgr.GetRegion(region)?.GetZone(x, y);
        return zone == null || AllowsHomeland(realm, region, zone.ID);
    }

    public static bool Allows(GameNPC actor, Vector3 point)
    {
        if (actor is not GameBot { IsAutonomousWorldBot: true } bot) return true;
        if (IsBattlegroundRegion(bot.CurrentRegionID))
            // Only admitted campaign participants may stand in a battleground.
            return AutonomousBattlegroundParticipation.IsParticipant(bot);
        if (!WorldMode.IsRvr) return true;
        eRealm regionalOwner = AutonomousWorldBotController.ProtectedRealm(bot.CurrentRegionID, ushort.MaxValue);
        // Own homeland and shared regions need no coordinate lookup on movement ticks.
        if (regionalOwner == eRealm.None || regionalOwner == bot.Realm) return true;
        Zone zone = bot.CurrentRegion?.GetZone((int)point.X, (int)point.Y);
        if (zone != null && AllowsHomeland(bot.Realm, bot.CurrentRegionID, zone.ID)) return true;
        // A bot that already stands in an enemy homeland (an old save, a forced move) may walk on, so it can leave;
        // the login repair sends such saves home.
        Zone here = bot.CurrentZone;
        return here != null && !AllowsHomeland(bot.Realm, bot.CurrentRegionID, here.ID);
    }
}
