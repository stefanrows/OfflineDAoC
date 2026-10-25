namespace DOL.GS;

/// <summary>
/// The fork's "livelier RvR" waves (observe, group pause and retreat, stealth loop, speed habits, doctrine and support
/// offense, LFG waits, hub muster, danger memory, mob avoidance, route variants, fight heat) belong to the Camlann world.
/// The RvR world (save marker <c>Realm-1</c>) runs the original upstream bot behaviour (owner 2026-10-25 16:10: "RvR wie
/// im Original, nichts Neues erfinden"; port session S2, docs/RVR_PORT_MAP.md section 3), so the waves are off there.
/// Battleground participants (the open battleground and the fork's bracket campaign) keep their own fork crews in both
/// worlds until S3 settles the battlegrounds (map collision 4f). The code of every wave stays; only its entry points ask
/// this switch.
/// </summary>
public static class AutonomousRvrWaves
{
    /// <summary>The waves run for this bot: always in Camlann; in the RvR world only for battleground participants.</summary>
    public static bool ActiveFor(GameLiving living) =>
        WorldMode.Current != WorldModelKind.Realm || living is GameBot bot &&
        (BattlegroundCampaignCatalog.Find(bot.CurrentRegionID) != null || AutonomousBattlegroundParticipation.IsAssigned(bot));

    public static bool ActiveFor(WorldModelKind world, bool battlegroundParticipant) =>
        world != WorldModelKind.Realm || battlegroundParticipant;

    /// <summary>The waves run in this world at all (no single bot to ask).</summary>
    public static bool Active => WorldMode.Current != WorldModelKind.Realm;
}
