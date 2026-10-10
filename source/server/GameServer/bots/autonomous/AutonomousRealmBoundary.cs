using System.Numerics;

namespace DOL.GS;

public static class AutonomousRealmBoundary
{
    public static bool IsBattlegroundRegion(ushort region) => region is 250 or 252 or 253 || BattlegroundCampaignCatalog.Find(region) != null;

    public static bool Allows(eRealm realm, ushort region, ushort zone)
    {
        return !IsBattlegroundRegion(region);
    }

    public static bool Allows(GameNPC actor, Vector3 point)
    {
        if (actor is not GameBot { IsAutonomousWorldBot: true } bot) return true;
        if (!IsBattlegroundRegion(bot.CurrentRegionID)) return true;
        // Only admitted campaign participants may stand in a battleground.
        return AutonomousBattlegroundParticipation.IsParticipant(bot);
    }
}
