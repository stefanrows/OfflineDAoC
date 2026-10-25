using System;
using System.Collections.Generic;

namespace DOL.GS;

/// <summary>
/// Travel and population rules of the two worlds (RvR package R2,
/// docs/RVR_TRAVEL_POPULATION.md). The Camlann world is open: every porter,
/// destination, broker and companion class serves every realm. The RvR world
/// (save marker <c>Realm-1</c>) binds them to the character's own realm, as on
/// live 1.65: own capital, own frontier passage, own Atlantis copy, own Realm
/// Exchange, companions of the own realm only. Every rule has a pure form with
/// a world parameter for the both-worlds tests.
/// </summary>
public static class RealmTravel
{
    public static readonly eRealm[] PlayableRealms = [eRealm.Albion, eRealm.Midgard, eRealm.Hibernia];

    public static bool IsPlayable(eRealm realm) => realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia;

    /// <summary>
    /// Whether something that belongs to <paramref name="target"/> (a destination, a
    /// porter, a broker, a companion class, a party member) is open to
    /// <paramref name="own"/>. Camlann: always. RvR: the same realm, or a realm-less
    /// (neutral) target.
    /// </summary>
    public static bool Allows(WorldModelKind world, eRealm own, eRealm target) =>
        world != WorldModelKind.Realm || !IsPlayable(target) || target == own;

    public static bool Allows(eRealm own, eRealm target) => Allows(WorldMode.Current, own, target);

    /// <summary>
    /// <see cref="Allows(eRealm, eRealm)"/> for an NPC service (porter, broker). Camlann never reads the NPC's realm,
    /// so the Camlann code path is unchanged.
    /// </summary>
    public static bool AllowsNpc(eRealm own, GameNPC service) =>
        !WorldMode.IsRvr || service == null || Allows(WorldModelKind.Realm, own, service.Realm);

    /// <summary>A service of <paramref name="serviceRealm"/> serves this player. Staff accounts are never refused.</summary>
    public static bool Serves(WorldModelKind world, GamePlayer player, eRealm serviceRealm) =>
        player != null && (IsStaff(player) || Allows(world, player.Realm, serviceRealm));

    public static bool Serves(GamePlayer player, eRealm serviceRealm) => Serves(WorldMode.Current, player, serviceRealm);

    /// <summary>The realms a menu offers to a character of <paramref name="own"/>: all three in Camlann, the own one in RvR.</summary>
    public static IReadOnlyList<eRealm> OfferedRealms(WorldModelKind world, eRealm own) =>
        world == WorldModelKind.Realm && IsPlayable(own) ? new[] { own } : PlayableRealms;

    public static IReadOnlyList<eRealm> OfferedRealms(eRealm own) => OfferedRealms(WorldMode.Current, own);

    public static string RealmName(eRealm realm) => realm switch
    {
        eRealm.Albion => "Albion",
        eRealm.Midgard => "Midgard",
        eRealm.Hibernia => "Hibernia",
        _ => realm.ToString(),
    };

    public static string CapitalName(eRealm realm) => realm switch
    {
        eRealm.Albion => "Camelot",
        eRealm.Midgard => "Jordheim",
        eRealm.Hibernia => "Tir na Nog",
        _ => string.Empty,
    };

    /// <summary>An enemy realm's porter, broker or frontier porter.</summary>
    public static string ServiceRefusal(eRealm serviceRealm) =>
        $"You are no citizen of {RealmName(serviceRealm)}. I will not serve an enemy of the realm.";

    /// <summary>A destination in another realm's lands.</summary>
    public static string DestinationRefusal(eRealm own, eRealm destination) =>
        $"The lands of {RealmName(destination)} are closed to the people of {RealmName(own)}. Choose a destination of your own realm.";

    /// <summary>A companion class of another realm.</summary>
    public static string CompanionRefusal(eRealm own) =>
        $"In the realm war only companions of {RealmName(own)} will follow you.";

    private static bool IsStaff(GamePlayer player) =>
        player.Client?.Account?.PrivLevel > (uint)ePrivLevel.Player;

    /// <summary>
    /// The realm a party member fights for, from the R1b alliance core
    /// (<see cref="DOL.GS.ServerRules.PvpCombatant.AllianceRealm"/>: companions and /spawn helpers take their
    /// owner's realm, battleground encounter bots their sponsor's or none). None for staff accounts, who may
    /// group with anyone, and for anything that is not player-shaped.
    /// </summary>
    public static eRealm PartyRealm(GameLiving living)
    {
        if (living is GamePlayer player && IsStaff(player))
            return eRealm.None;
        return DOL.GS.ServerRules.PvpCombatant.IsPlayerShaped(living)
            ? DOL.GS.ServerRules.PvpCombatant.AllianceRealm(living)
            : eRealm.None;
    }

    /// <summary>Two bots may share a party: always in Camlann, the same realm in RvR (bot formation filters).</summary>
    public static bool SharesParty(GameLiving first, GameLiving second)
    {
        if (!WorldMode.IsRvr || first == null || second == null)
            return true;
        eRealm a = PartyRealm(first), b = PartyRealm(second);
        return !IsPlayable(a) || !IsPlayable(b) || a == b;
    }

    /// <summary>
    /// RvR world: a party never mixes realms. A member joins only a group whose
    /// members fight for its realm (<see cref="PartyRealm"/>). This is the
    /// backstop under every formation path (pickup PvE, RvR pickup, warband
    /// backfill and merge, open BG units, companions); members without a realm
    /// (staff, neutral encounter bots) never block. Camlann: every party is allowed.
    /// </summary>
    public static bool PartyAccepts(WorldModelKind world, eRealm candidateRealm, IEnumerable<eRealm> memberRealms)
    {
        if (world != WorldModelKind.Realm || !IsPlayable(candidateRealm) || memberRealms == null)
            return true;
        foreach (eRealm member in memberRealms)
            if (IsPlayable(member) && member != candidateRealm)
                return false;
        return true;
    }

    public static bool PartyAccepts(GameLiving candidate, IEnumerable<GameLiving> members)
    {
        if (!WorldMode.IsRvr || candidate == null)
            return true;
        eRealm realm = PartyRealm(candidate);
        if (!IsPlayable(realm))
            return true;
        var realms = new List<eRealm>();
        if (members != null)
            foreach (GameLiving member in members)
                if (member != null && member != candidate)
                    realms.Add(PartyRealm(member));
        return PartyAccepts(WorldModelKind.Realm, realm, realms);
    }
}
