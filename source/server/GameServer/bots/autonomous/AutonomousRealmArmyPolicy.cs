using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>
/// Tunables of the realm armies (package R4b, docs/RVR_ARMIES.md). Read only in the RvR world (save marker
/// <c>Realm-1</c>); the Camlann world never asks. The initial values are the defaults for code that runs before
/// (or without) the property loader, such as unit tests.
/// </summary>
public static class AutonomousRealmArmyProperties
{
    [ServerProperty("pvp", "rvr_army_neutral_keep_weight", "RvR world only: how strongly a realm army picks a neutral (Frontier Wardens) keep as its next siege target, against the enemy keep weight", 1.0)]
    public static double RVR_ARMY_NEUTRAL_KEEP_WEIGHT = 1.0;

    [ServerProperty("pvp", "rvr_army_enemy_keep_weight", "RvR world only: how strongly a realm army picks a keep held by an enemy realm as its next siege target, against the neutral keep weight", 1.5)]
    public static double RVR_ARMY_ENEMY_KEEP_WEIGHT = 1.5;

    [ServerProperty("pvp", "rvr_army_home_frontier_weight", "RvR world only: multiplier for a keep inside the army's own frontier (secure the home frontier first; an enemy-held keep there is a retake)", 2.0)]
    public static double RVR_ARMY_HOME_FRONTIER_WEIGHT = 2.0;

    [ServerProperty("pvp", "rvr_army_reserve_invader_percent", "RvR world only: extra share (percent) of a realm's warbands that stay home as a roaming reserve while enemies stand in its home frontier", 20)]
    public static int RVR_ARMY_RESERVE_INVADER_PERCENT = 20;

    [ServerProperty("pvp", "rvr_army_invader_min", "RvR world only: enemies (players, their companions, world bots) that must stand in a realm's home frontier before its reserve grows and stays home; fewer count as passers-by", 4)]
    public static int RVR_ARMY_INVADER_MIN = 4;

    [ServerProperty("pvp", "rvr_army_plan_minutes", "RvR world only: average game minutes a realm keeps its attack plan (single, double or opportunist) before it draws a new one (0.5x-1.5x)", 60)]
    public static int RVR_ARMY_PLAN_MINUTES = 60;

    [ServerProperty("pvp", "rvr_grudge_hunters_per_target", "RvR world only: at most this many warbands of one realm hunt the same grudge target at once (0 = no revenge trips)", 2)]
    public static int RVR_GRUDGE_HUNTERS_PER_TARGET = 2;
}

/// <summary>
/// Pure rules of the realm armies (package R4b). In the RvR world the autonomous event layer fights per realm:
/// an army is every warband of one realm at one siege, whatever their guilds; its siege slots, muster, defence and
/// third-realm response are the realm's. Every function takes its world or inputs explicitly so both worlds can be
/// tested without a server; the Camlann answers are the guild rules of before.
/// </summary>
public static class AutonomousRealmArmyPolicy
{
    /// <summary>Owner rule: at most this share (percent) of a realm's warbands stays out of the armies as reserve.</summary>
    public const int MaximumReservePercent = 60;

    /// <summary>The army a bot belongs to at a siege. Camlann: its guild (null without one). RvR: its realm.</summary>
    public static string ArmyKey(WorldModelKind world, string guildId, eRealm realm)
    {
        if (world == WorldModelKind.Realm)
            return RealmTravel.IsPlayable(realm) ? "realm:" + (int)realm : null;
        return string.IsNullOrEmpty(guildId) ? null : guildId;
    }

    /// <summary>Whether a bot marches in an army. Camlann: same guild. RvR: same realm, any guild or none.</summary>
    public static bool SameArmy<TGuild>(WorldModelKind world, TGuild armyGuild, eRealm armyRealm, TGuild botGuild, eRealm botRealm)
        where TGuild : class =>
        world == WorldModelKind.Realm
            ? RealmTravel.IsPlayable(armyRealm) && botRealm == armyRealm
            : botGuild != null && ReferenceEquals(botGuild, armyGuild);

    /// <summary>The frontier region a realm calls home (Albion 1, Midgard 100, Hibernia 200).</summary>
    public static ushort HomeFrontierRegion(eRealm realm) => AutonomousFrontierTransport.HomeRegion(realm);

    /// <summary>
    /// A keep a realm army may choose to besiege: never one of its own realm. Neutral (Frontier Wardens, realm
    /// None) and enemy-held keeps are open.
    /// </summary>
    public static bool IsArmyTarget(eRealm armyRealm, eRealm keepRealm) =>
        RealmTravel.IsPlayable(armyRealm) && keepRealm != armyRealm;

    /// <summary>
    /// Weight of a keep in the army's target draw: a neutral or an enemy keep with its tunable weight, times the
    /// home frontier multiplier when the keep stands in the army's own frontier. An own-realm keep weighs 0.
    /// </summary>
    public static double KeepWeight(eRealm armyRealm, eRealm keepRealm, ushort keepRegion,
        double neutralWeight, double enemyWeight, double homeFrontierWeight)
    {
        if (!IsArmyTarget(armyRealm, keepRealm))
            return 0;
        double weight = Math.Max(0, RealmTravel.IsPlayable(keepRealm) ? enemyWeight : neutralWeight);
        if (keepRegion != 0 && keepRegion == HomeFrontierRegion(armyRealm))
            weight *= Math.Max(0, homeFrontierWeight);
        return weight;
    }

    /// <summary>The live weight with the server's tunables.</summary>
    public static double KeepWeight(eRealm armyRealm, eRealm keepRealm, ushort keepRegion) =>
        KeepWeight(armyRealm, keepRealm, keepRegion, AutonomousRealmArmyProperties.RVR_ARMY_NEUTRAL_KEEP_WEIGHT,
            AutonomousRealmArmyProperties.RVR_ARMY_ENEMY_KEEP_WEIGHT, AutonomousRealmArmyProperties.RVR_ARMY_HOME_FRONTIER_WEIGHT);

    /// <summary>
    /// Weighted draw among <paramref name="candidates"/>: candidates not yet in <paramref name="used"/> first (as
    /// the Camlann draw, so a realm works through the frontier rather than hitting one keep again and again), then
    /// by <paramref name="weight"/>. Candidates of weight 0 are never drawn. <paramref name="roll"/> is in [0, 1).
    /// </summary>
    public static T ChooseWeighted<T>(IEnumerable<T> candidates, Func<T, double> weight, Func<T, bool> used, double roll)
        where T : class
    {
        var eligible = (candidates ?? Array.Empty<T>()).Select(candidate => (candidate, weight: Math.Max(0, weight(candidate))))
            .Where(entry => entry.weight > 0 && double.IsFinite(entry.weight)).ToArray();
        if (eligible.Length == 0) return null;
        var fresh = eligible.Where(entry => used == null || !used(entry.candidate)).ToArray();
        if (fresh.Length == 0) fresh = eligible;
        double total = fresh.Sum(entry => entry.weight);
        double pick = Math.Clamp(roll, 0d, Math.BitDecrement(1d)) * total;
        foreach (var entry in fresh)
        {
            if (pick < entry.weight) return entry.candidate;
            pick -= entry.weight;
        }
        return fresh[^1].candidate;
    }

    /// <summary>Own sieges a realm runs at once under its plan: single 1, double 2, opportunist 1.</summary>
    public static int MaximumOwnSieges(OpenBgAttackPlan plan) => plan == OpenBgAttackPlan.Double ? 2 : 1;

    /// <summary>
    /// An opportunist realm opens a siege of its own only while no other realm runs one; otherwise it waits for
    /// another realm's keep fight to join as the third realm. Single and double plans open freely up to their count.
    /// </summary>
    public static bool MayOpenOwnSiege(OpenBgAttackPlan plan, int ownRunning, int otherRealmsRunning) =>
        ownRunning < MaximumOwnSieges(plan) && (plan != OpenBgAttackPlan.Opportunist || otherRealmsRunning == 0);

    /// <summary>
    /// A warband of a realm that is neither attacker nor defender contests a siege once the keep fight is on (an
    /// attacker reached the walls). Opportunists come with any formed group of two or more; other plans send only
    /// whole warbands of eight. Before the fight nobody intervenes.
    /// </summary>
    public static bool MayContestAsThirdRealm(OpenBgAttackPlan plan, int memberCount, bool keepFightOn) =>
        keepFightOn && memberCount >= (plan == OpenBgAttackPlan.Opportunist ? 2 : 8);

    /// <summary>The third realm comes with at most half the attacker cap (upstream rule).</summary>
    public static int ThirdRealmCapacity(int attackerCapacity) => Math.Max(1, attackerCapacity / 2);

    /// <summary>
    /// Enough enemies stand in the home frontier to count as an invasion: at least <paramref name="minimum"/>
    /// (never fewer than one), so a single passing enemy does not keep the reserve home for good.
    /// </summary>
    public static bool InvadersAtHome(int invaders, int minimum) => invaders >= Math.Max(1, minimum);

    /// <summary>
    /// The share (percent) of a realm's warbands kept home as a roaming reserve: the leader's temperament share
    /// (roamer 40, hybrid 25, keep warrior 5, others 15), plus <paramref name="invaderBonus"/> while an invasion
    /// (<see cref="InvadersAtHome"/>) is counted in the home frontier; never above <see cref="MaximumReservePercent"/>.
    /// </summary>
    public static int ReservePercent(int temperamentPercent, int invadersAtHome, int invaderBonus, int invaderMinimum) =>
        Math.Clamp(Math.Max(0, temperamentPercent) +
            (InvadersAtHome(invadersAtHome, invaderMinimum) ? Math.Max(0, invaderBonus) : 0), 0, MaximumReservePercent);

    /// <summary>
    /// How long after its launch a released party keeps its permission while it walks from the muster to the keep.
    /// Camlann: five minutes (the forward camp lies 9,000 units out). RvR: the realm muster can stand a whole
    /// frontier away, so the walk is estimated from its distance (see <see cref="AutonomousRvrSiegeMuster.MarchAllowanceMilliseconds"/>).
    /// </summary>
    public static long ReleaseHoldMilliseconds(WorldModelKind world, double campToKeepDistance) =>
        world == WorldModelKind.Realm
            ? AutonomousRvrSiegeMuster.MarchAllowanceMilliseconds(campToKeepDistance, CamlannReleaseHoldMilliseconds)
            : CamlannReleaseHoldMilliseconds;

    public const long CamlannReleaseHoldMilliseconds = 5 * 60_000L;

    // Each realm's two classic border keeps (safe-area centres; upstream AutonomousRvrStaging.BorderKeeps).
    private static readonly AutonomousRvrStaging.BorderKeep[] AlbionKeeps =
        [new(1, new(585085, 477504, 2600), "Castle Sauvage"), new(1, new(528451, 358290, 8320), "Snowdonia Fortress")];
    private static readonly AutonomousRvrStaging.BorderKeep[] MidgardKeeps =
        [new(100, new(766235, 669173, 5736), "Svasud Faste"), new(100, new(704022, 738009, 5704), "Vindsaul Faste")];
    private static readonly AutonomousRvrStaging.BorderKeep[] HiberniaKeeps =
        [new(200, new(333229, 419539, 5336), "Druim Ligen"), new(200, new(421166, 484998, 1976), "Druim Cain")];

    /// <summary>A realm's two border keeps in its home frontier.</summary>
    public static IReadOnlyList<AutonomousRvrStaging.BorderKeep> BorderKeeps(eRealm realm) => realm switch
    {
        eRealm.Albion => AlbionKeeps,
        eRealm.Midgard => MidgardKeeps,
        eRealm.Hibernia => HiberniaKeeps,
        _ => Array.Empty<AutonomousRvrStaging.BorderKeep>(),
    };

    /// <summary>
    /// Where a realm army musters for a keep (upstream <c>TryGetSiegeMuster</c>): in an enemy frontier at the
    /// realm's own portal outpost there (the frontier porter's landing, e.g. "Odin Alb" for Albion in Midgard);
    /// in its home frontier at the nearer of its two border keeps. False for any other region (no muster known).
    /// </summary>
    public static bool TryGetRealmMuster(eRealm realm, ushort targetRegion, Vector2 keep, out AutonomousRvrStaging.BorderKeep muster)
    {
        muster = default;
        if (!RealmTravel.IsPlayable(realm) || targetRegion == 0) return false;
        if (targetRegion == HomeFrontierRegion(realm))
        {
            muster = BorderKeeps(realm).Where(candidate => candidate.RegionId == targetRegion)
                .OrderBy(candidate => Vector2.DistanceSquared(new(candidate.Position.X, candidate.Position.Y), keep))
                .FirstOrDefault();
            return muster.RegionId != 0;
        }
        AutonomousFrontierTransport.Passage passage = AutonomousFrontierTransport.Destination(realm, targetRegion);
        if (passage == null || passage.Medallion == "home_necklace" || passage.Region != targetRegion) return false;
        muster = new(passage.Region, new(passage.Location.X, passage.Location.Y, passage.Location.Z),
            "the " + GlobalConstants.RealmToName(realm) + " portal outpost");
        return true;
    }

    /// <summary>The realm muster stands far enough from the keep to be a muster, not a siege camp in the guards' sight.</summary>
    public static bool IsUsableMuster(AutonomousRvrStaging.BorderKeep muster, ushort keepRegion, Vector2 keep) =>
        muster.RegionId != 0 && muster.RegionId == keepRegion &&
        Vector2.DistanceSquared(new(muster.Position.X, muster.Position.Y), keep) >=
            (float)AutonomousGuildAssault.MinimumKeepDistance * AutonomousGuildAssault.MinimumKeepDistance;

    /// <summary>Game minutes a realm keeps its drawn attack plan: half to one and a half times the tunable.</summary>
    public static long PlanMilliseconds(int averageMinutes, double roll) =>
        (long)(Math.Max(1, averageMinutes) * 60_000L * (0.5 + Math.Clamp(roll, 0d, 1d)));

    /// <summary>The cap on revenge trips: a warband may hunt a grudge target while fewer than the cap of its realm hunt it.</summary>
    public static bool HasGrudgeRoom(int huntersOnTarget, bool alreadyHunting, int cap) =>
        alreadyHunting || huntersOnTarget < Math.Max(0, cap);
}
