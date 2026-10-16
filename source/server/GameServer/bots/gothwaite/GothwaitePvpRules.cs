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

    /// <summary>
    /// The repeat-kill timer (RP_WORTH_SECONDS) is lifted for realm points and experience while the mode is on and the
    /// victim died in the arena region. Loot drops and party gear keep the timer. The rest of the world is unchanged.
    /// </summary>
    public static bool LiftsRepeatKillTimer(GameLiving victim) =>
        victim != null && LiftsRepeatKillTimer(GothwaitePvpMode.IsActive, victim.CurrentRegionID, GothwaitePvpMode.Arena.RegionId);

    /// <summary>Pure form of <see cref="LiftsRepeatKillTimer(GameLiving)"/> for tests.</summary>
    public static bool LiftsRepeatKillTimer(bool active, ushort victimRegion, ushort arenaRegion) =>
        active && victimRegion == arenaRegion;

    /// <summary>G8: no constitution loss for mode bots.</summary>
    public static bool SkipsConstitutionLoss(GameLiving living) => GothwaitePvpMode.IsModeBot(living);

    /// <summary>G9 and section 5.4: no director, scheduler or alt trickle recruits world bots while the mode is on.</summary>
    public static bool SuppressesWorldRecruitment => GothwaitePvpMode.IsActive;

    /// <summary>The open BG crew is off only while the mode is on and no bot is reserved for it (docs/superpowers/specs/2026-10-15-open-bg-gothwaite-design.md, section 3).</summary>
    public static bool SuppressesOpenBattlegroundCrew(bool active, int reservedCount) => active && reservedCount == 0;

    /// <summary>The open BG may recruit a bot: always with the mode off, with the mode on only a reserved one.</summary>
    public static bool MayJoinOpenBattleground(bool active, bool reserved) => !active || reserved;

    /// <summary>Live form: the bot's reservation is read through <see cref="GothwaitePvpOpenBgReserve.IsOpenBgReserved"/>.</summary>
    public static bool MayJoinOpenBattleground(GameLiving bot) =>
        MayJoinOpenBattleground(GothwaitePvpMode.IsActive, GothwaitePvpMode.IsActive && GothwaitePvpOpenBgReserve.IsOpenBgReserved(bot));

    /// <summary>An open BG unit is refused while the mode is on and any member is not reserved.</summary>
    public static bool RefusesOpenUnit(bool active, bool allMembersReserved) => active && !allMembersReserved;

    /// <summary>Live form of <see cref="RefusesOpenUnit(bool, bool)"/> over the unit's members.</summary>
    public static bool RefusesOpenUnit(System.Collections.Generic.IEnumerable<GameBot> members) =>
        RefusesOpenUnit(GothwaitePvpMode.IsActive,
            !GothwaitePvpMode.IsActive || (members != null && System.Linq.Enumerable.All(members, GothwaitePvpOpenBgReserve.IsOpenBgReserved)));

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
