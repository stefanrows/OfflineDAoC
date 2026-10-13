using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>
/// Who a mode bot may open a fight on (docs/GOTHWAITE_PVP_MODE.md 6.2, 6.4, 6.5). The driver's scan, a follower's assist
/// and the class brain's own target choice all ask <see cref="MayOpen"/>, so the rule holds whichever code picks the target.
/// Self-defence (a fight someone else opened) is not filtered here.
/// </summary>
public static class GothwaitePvpTargetRules
{
    /// <summary>
    /// A legal opening for a mode bot: the target is alive, player-shaped (a player, a companion or another autonomous bot,
    /// or a pet of one), cons green to red to the bot (G4: never grey or purple), stands outside the castle circle and
    /// inside the bot's arena edge, neither the bot nor the target stands inside the no-fight ring (section 6.2), is not an
    /// alliance mate, and the server rules allow the attack (group and guild mates are refused there). Monsters are never
    /// opened on. A bot on its outbound leg from the gate opens nothing (second live run, 2026-10-13). False for any bot that is
    /// not a mode bot.
    /// </summary>
    public static bool MayOpen(GameBot bot, GameLiving target)
    {
        if (!GothwaitePvpMode.IsModeBot(bot) || target == null || target == bot || !target.IsAlive)
            return false;
        if (GothwaitePvpDriver.IsOutbound(bot))
            return false;
        if (PvpCombatant.Resolve(target) is not GameLiving master || !PvpCombatant.IsPlayerShaped(master))
            return false;
        if (!ConAllows(bot, target) || master != target && !ConAllows(bot, master))
            return false;
        if (!InArena(bot, target))
            return false;
        if (!RingAllowsOpening(GothwaitePvpMode.Arena, GothwaitePvpMode.Settings.GateRing,
                bot.CurrentRegionID, bot.X, bot.Y, target.CurrentRegionID, target.X, target.Y))
            return false;
        if (AllianceMates(bot, master))
            return false;
        return BattlegroundCombatTargets.IsLegal(bot, target, bot.CurrentZone);
    }

    /// <summary>
    /// The no-fight ring of section 6.2 as a pure predicate: no fight is opened while the bot or the target stands inside the castle
    /// circle widened by <paramref name="gateRing"/> units. Beyond the ring on both sides, the ring allows the opening.
    /// </summary>
    public static bool RingAllowsOpening(GothwaitePvpArena arena, int gateRing, ushort botRegion, int botX, int botY,
        ushort targetRegion, int targetX, int targetY) =>
        !arena.InGateRing(botRegion, botX, botY, gateRing) && !arena.InGateRing(targetRegion, targetX, targetY, gateRing);

    /// <summary>True when the object stands inside the no-fight ring of the mode's arena (the mode's gate ring setting).</summary>
    public static bool InGateRing(GameObject point) =>
        point != null && GothwaitePvpMode.Arena.InGateRing(point.CurrentRegionID, point.X, point.Y, GothwaitePvpMode.Settings.GateRing);

    // G4 for one living: green to red. A pet is judged by its owner's con too, so a grey or purple player's pet is no opening.
    private static bool ConAllows(GameBot bot, GameLiving living) =>
        GothwaitePvpCastleRules.ConMayOpen(ConLevels.GetConColor(ConLevels.GetConLevel(bot.Level, living.Level)));

    /// <summary>The point is in the arena for this bot: its region, outside the castle circle, within its level's edge.</summary>
    public static bool InArena(GameBot bot, GameObject point)
    {
        GothwaitePvpArena arena = GothwaitePvpMode.Arena;
        if (bot == null || point == null || point.CurrentRegionID != arena.RegionId)
            return false;
        if (arena.InCastle(point.CurrentRegionID, point.X, point.Y))
            return false;
        double edge = GothwaitePvpArena.EdgeFor(bot.Level, GothwaitePvpMode.Settings);
        return arena.CastleDistanceSquared(point.X, point.Y) <= edge * edge;
    }

    /// <summary>
    /// Section 1: alliance mates are friends in the mode. Camlann's server rules already spare group and guild mates;
    /// they do not check alliances, so the mode does it for its own openings only.
    /// </summary>
    public static bool AllianceMates(GameLiving first, GameLiving second)
    {
        Guild a = PvpCombatant.GuildOf(first), b = PvpCombatant.GuildOf(second);
        if (a == null || b == null || a == b)
            return false;
        return a.alliance != null && a.alliance == b.alliance;
    }
}
