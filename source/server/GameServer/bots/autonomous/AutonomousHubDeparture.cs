using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.GS.ServerRules;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Departure truce (owner decision 2026-09-29, principle P3: you leave the
/// door before you hunt). Groups of one realm leave the same border hub one
/// after another; under Camlann rules they are hostile to each other and used
/// to fight at the edge of the safe radius. A bot that is still "departing"
/// does not open a fight on a same-realm autonomous bot that is departing too.
/// Retaliation always stays allowed, and humans, companions and other realms
/// are not affected.
///
/// "Departing" = an autonomous RvR world bot outside its own realm's safe hub
/// which left it less than 180 seconds ago and is at most 2,500 units beyond
/// the edge of the safe circle it left: the keep circle (radius 3,500, so
/// 6,000 from the keep centre) or the hub's outer bindstone landing (Castle
/// Sauvage's lies about 9,100 units from its keep). The crossing is observed
/// on the bot's own AI pulse and on every truce check; a bot never seen inside
/// the hub since the server started is not departing.
/// </summary>
public static class AutonomousHubDeparture
{
    /// <summary>Departure band beyond the edge of the safe circle left.</summary>
    public const int DepartureBand = 2_500;
    /// <summary>Outer edge of the band around a keep circle.</summary>
    public const int DepartureRadius = PvpCombatant.SafeBorderHubRadius + DepartureBand;

    /// <summary>One safe circle of a realm's border hub: the keep circle or an
    /// outer bindstone landing.</summary>
    public readonly record struct SafeAnchor(ushort RegionId, Vector2 Centre, float Radius, string HubName);

    /// <summary>The own hub's safe circle containing the point, if any.</summary>
    public static bool TryGetSafeAnchor(eRealm realm, ushort regionId, float x, float y, out SafeAnchor anchor)
    {
        anchor = default;
        if (!AutonomousRvrStaging.TryGetBorderKeep(realm, out AutonomousRvrStaging.BorderKeep hub) || hub.RegionId != regionId)
            return false;
        Vector2 point = new(x, y);
        Vector2 keep = new(hub.Position.X, hub.Position.Y);
        if (Vector2.Distance(point, keep) <= PvpCombatant.SafeBorderHubRadius)
        {
            anchor = new(regionId, keep, PvpCombatant.SafeBorderHubRadius, hub.Name);
            return true;
        }
        foreach (PvpCombatant.SafeHubLanding landing in PvpCombatant.SafeHubLandings)
        {
            Vector2 centre = new(landing.X, landing.Y);
            if (landing.RegionId == regionId && Vector2.Distance(point, centre) <= landing.Radius)
            {
                anchor = new(regionId, centre, landing.Radius, hub.Name);
                return true;
            }
        }
        return false;
    }
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(180);
    public static readonly TimeSpan LogThrottle = TimeSpan.FromSeconds(60);

    /// <summary>Whether a bot was last seen inside the hub, which safe circle
    /// it left, and when.</summary>
    public sealed class Track
    {
        public SafeAnchor? Inside;
        public SafeAnchor? LeftFrom;
        public DateTime? LeftUtc;

        /// <summary>Records one observation (the safe circle the bot stands
        /// in, or null); returns the current leave time and circle.</summary>
        public (DateTime? LeftUtc, SafeAnchor? From) Observe(SafeAnchor? inside, DateTime nowUtc)
        {
            lock (this)
            {
                if (inside.HasValue)
                {
                    Inside = inside;
                    LeftFrom = null;
                    LeftUtc = null;
                }
                else if (Inside.HasValue)
                {
                    LeftFrom = Inside;
                    LeftUtc = nowUtc;
                    Inside = null;
                }
                return (LeftUtc, LeftFrom);
            }
        }
    }

    private static readonly ConditionalWeakTable<GameBot, Track> Tracks = new();
    private static readonly ConditionalWeakTable<GameBot, StrongBox<DateTime>> TruceLogged = new();
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousHubDeparture));

    /// <summary>Pure departing rule; <paramref name="distance"/> is measured
    /// from the centre of the safe circle the bot left.</summary>
    public static bool IsDeparting(bool insideSafeHub, double distance, float safeRadius, DateTime? leftUtc, DateTime nowUtc) =>
        !insideSafeHub && distance > safeRadius && distance <= safeRadius + DepartureBand &&
        leftUtc.HasValue && nowUtc >= leftUtc.Value && nowUtc - leftUtc.Value < Window;

    /// <summary>
    /// Pure truce rule: both sides are departing from the same hub, the
    /// attacker is not answering an attack, and both are same-realm
    /// autonomous RvR bots.
    /// </summary>
    public static bool TruceApplies(bool bothAutonomousRvr, bool sameRealm, bool sameHub,
        bool attackerDeparting, bool targetDeparting, bool retaliation) =>
        bothAutonomousRvr && sameRealm && sameHub && attackerDeparting && targetDeparting && !retaliation;

    private static bool Applies(GameBot bot) => bot is { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false,
        IsPersistentPlayerCompanion: false, IsPlayerLedGroup: false } &&
        AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR);

    /// <summary>Records the bot's hub crossing; call from its AI pulse.</summary>
    public static void Observe(GameBot bot, DateTime nowUtc) => IsDeparting(bot, nowUtc, out _);

    public static bool IsDeparting(GameBot bot, DateTime nowUtc) => IsDeparting(bot, nowUtc, out _);

    public static bool IsDeparting(GameBot bot, DateTime nowUtc, out SafeAnchor from)
    {
        from = default;
        if (!Applies(bot) || !AutonomousRvrStaging.TryGetBorderKeep(bot.Realm, out AutonomousRvrStaging.BorderKeep hub) ||
            hub.RegionId != bot.CurrentRegionID)
            return false;
        bool inside = TryGetSafeAnchor(bot.Realm, bot.CurrentRegionID, bot.X, bot.Y, out SafeAnchor here);
        (DateTime? left, SafeAnchor? leftFrom) = Tracks.GetOrCreateValue(bot).Observe(inside ? here : null, nowUtc);
        if (!leftFrom.HasValue)
            return false;
        from = leftFrom.Value;
        return IsDeparting(inside, Vector2.Distance(from.Centre, new(bot.X, bot.Y)), from.Radius, left, nowUtc);
    }

    /// <summary>
    /// Whether <paramref name="attacker"/> must not open a fight on
    /// <paramref name="target"/> because both are leaving the same hub. Logs
    /// <c>RVR_HUB_TRUCE</c> at most once per attacker per minute.
    /// </summary>
    public static bool TruceApplies(GameBot attacker, GameLiving target, DateTime nowUtc)
    {
        if (!Applies(attacker) || PvpCombatant.Resolve(target) is not GameBot other || !Applies(other) ||
            other.Realm != attacker.Realm)
            return false;
        if (!IsDeparting(attacker, nowUtc, out SafeAnchor hub) ||
            !IsDeparting(other, nowUtc, out SafeAnchor otherHub))
            return false;
        bool retaliation = BotPvpCrowdControl.IsInFightWith(attacker, target, null);
        // Same hub, whichever of its safe circles each side left.
        bool sameHub = hub.RegionId == otherHub.RegionId && hub.HubName == otherHub.HubName;
        if (!TruceApplies(true, true, sameHub, true, true, retaliation))
            return false;
        StrongBox<DateTime> logged = TruceLogged.GetOrCreateValue(attacker);
        bool write;
        lock (logged)
        {
            write = nowUtc - logged.Value >= LogThrottle;
            if (write) logged.Value = nowUtc;
        }
        if (write && Log.IsInfoEnabled)
            Log.Info($"RVR_HUB_TRUCE bot=\"{attacker.Name}\" target=\"{other.Name}\" hub=\"{hub.HubName}\"");
        return true;
    }
}
