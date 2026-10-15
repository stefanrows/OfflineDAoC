using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>
/// Mode rules that existing code asks through one guarded call each (docs/GOTHWAITE_PVP_MODE.md
/// section 5.4). Every method answers "no change" while <see cref="GothwaitePvpMode.IsActive"/> is false.
/// </summary>
public static class GothwaitePvpRules
{
    /// <summary>The castle circle is a safe area for everyone, only while the mode is on (G3, G10).</summary>
    public static bool IsCastleSafe(GameLiving living) =>
        GothwaitePvpMode.IsActive && living != null && living.CurrentRegion != null &&
        GothwaitePvpMode.Arena.InCastle(living.CurrentRegionID, living.X, living.Y);

    /// <summary>
    /// A spell hit that lands after its cast (a bolt or archery shot in flight, a DoT tick) is checked against the castle again at impact
    /// (second live run, 2026-10-13): no PvP damage while the attacker or the target stands in the castle circle. Without it a bot hit
    /// outside could die at its castle landing after its release, or on its way in. Melee and bot bow shots already re-check at impact
    /// through the server rules. False with the mode off and for any fight that is not player-shaped against player-shaped (a pet counts
    /// as its owner), so monsters and guards are unchanged.
    /// </summary>
    public static bool BlocksSafeImpact(GameLiving attacker, GameLiving target) =>
        GothwaitePvpMode.IsActive && attacker != null && target != null &&
        BlocksSafeImpact(PvpCombatant.IsPlayerShaped(attacker) && PvpCombatant.IsPlayerShaped(target),
            IsCastleSafe(attacker), IsCastleSafe(target));

    /// <summary>The impact rule as a pure predicate: a player-shaped fight with either side inside the castle circle.</summary>
    public static bool BlocksSafeImpact(bool bothPlayerShaped, bool attackerInCastle, bool targetInCastle) =>
        bothPlayerShaped && (attackerInCastle || targetInCastle);

    /// <summary>Point form of <see cref="IsCastleSafe(GameLiving)"/>, for release points.</summary>
    public static bool IsCastleSafe(ushort regionId, int x, int y) =>
        GothwaitePvpMode.IsActive && GothwaitePvpMode.Arena.InCastle(regionId, x, y);

    /// <summary>G6: the sub-10 autonomous PvP protection does not apply to mode bots. A player's /safety is untouched.</summary>
    public static bool LiftsLowLevelProtection(GameLiving living) => GothwaitePvpMode.IsModeBot(living);

    /// <summary>G8: no constitution loss for mode bots.</summary>
    public static bool SkipsConstitutionLoss(GameLiving living) => GothwaitePvpMode.IsModeBot(living);

    /// <summary>G9 and section 5.4: no director, scheduler or alt trickle recruits world bots while the mode is on.</summary>
    public static bool SuppressesWorldRecruitment => GothwaitePvpMode.IsActive;

    /// <summary>A mode bot logs in and releases at a castle landing (section 6.1, 6.6). False outside the mode.</summary>
    public static bool TryCastleLanding(GameLiving living, out ushort regionId, out Point3D point)
    {
        regionId = 0;
        point = null;
        if (!GothwaitePvpMode.IsModeBot(living)) return false;
        GameBot bot = (GameBot)living;
        GothwaitePvpArena arena = GothwaitePvpMode.Arena;
        GothwaitePvpSpot landing = arena.CastleLanding(bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID);
        regionId = arena.RegionId;
        point = new Point3D(landing.X, landing.Y, landing.Z);
        return true;
    }
}
