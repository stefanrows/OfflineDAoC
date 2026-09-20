using System.Numerics;

namespace DOL.GS;

public static class AutonomousRealmBoundary
{
    public static bool IsBattlegroundRegion(ushort region) => region is 165 or 250 or 251 or 252 or 253;

    public static bool Allows(eRealm realm, ushort region, ushort zone)
    {
        return !IsBattlegroundRegion(region);
    }

    public static bool Allows(GameNPC actor, Vector3 point)
    {
        if (actor is not GameBot { IsAutonomousWorldBot: true } bot) return true;
        return !IsBattlegroundRegion(bot.CurrentRegionID);
    }
}
