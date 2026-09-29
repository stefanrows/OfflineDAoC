using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
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
    // ---- Hub-band peace (wave 6, owner decision 2026-09-29) ----
    //
    // The departure truce above only filtered opportunity picks. Live 0.146.0
    // logs showed 770 of 1,847 bot PvP deaths in one cell outside Svasud
    // Faste, 98 % Mid killed by Mid, because self-defence, assist, pets, AoE
    // splash and the cc-sweep never asked it. The peace therefore sits in the
    // attack permission itself (PvpCombatant.BlocksAutonomousPvp, called by
    // PvPServerRules.IsAllowedToAttack and the damage-time guards): two
    // same-realm autonomous world bots may not fight at all while either of
    // them stands inside its own realm's hub band.

    /// <summary>Hub band around the keep centre: safe radius + departure band.</summary>
    public const int HubBandRadius = DepartureRadius;

    /// <summary>Per-realm band circles (index = (int)eRealm, 1..3): the keep
    /// circle at 6,000 and each outer landing at its radius + 2,500. Built once;
    /// the hub data are code constants.</summary>
    private static readonly SafeAnchor[][] HubBands = BuildHubBands();

    private static SafeAnchor[][] BuildHubBands()
    {
        var bands = new SafeAnchor[4][];
        for (int realm = 0; realm < bands.Length; realm++)
        {
            bands[realm] = Array.Empty<SafeAnchor>();
            if (!AutonomousRvrStaging.TryGetBorderKeep((eRealm)realm, out AutonomousRvrStaging.BorderKeep hub))
                continue;
            var circles = new System.Collections.Generic.List<SafeAnchor>
            {
                new(hub.RegionId, new(hub.Position.X, hub.Position.Y), HubBandRadius, hub.Name)
            };
            foreach (PvpCombatant.SafeHubLanding landing in PvpCombatant.SafeHubLandings)
                if (landing.RegionId == hub.RegionId)
                    circles.Add(new(hub.RegionId, new(landing.X, landing.Y), landing.Radius + DepartureBand, hub.Name));
            bands[realm] = circles.ToArray();
        }
        return bands;
    }

    /// <summary>The band circles of a realm's own hub (empty for none).</summary>
    public static SafeAnchor[] HubBandOf(eRealm realm) =>
        (int)realm is >= 1 and <= 3 ? HubBands[(int)realm] : Array.Empty<SafeAnchor>();

    /// <summary>Pure band test against injected circles; no allocation.</summary>
    public static bool InHubBand(SafeAnchor[] band, ushort regionId, float x, float y)
    {
        if (band == null)
            return false;
        for (int i = 0; i < band.Length; i++)
        {
            SafeAnchor circle = band[i];
            if (circle.RegionId != regionId)
                continue;
            float dx = x - circle.Centre.X, dy = y - circle.Centre.Y;
            if (dx * dx + dy * dy <= circle.Radius * circle.Radius)
                return true;
        }
        return false;
    }

    /// <summary>Whether a point lies in the hub band of the realm's own border hub.</summary>
    public static bool InHubBand(eRealm realm, ushort regionId, float x, float y) =>
        InHubBand(HubBandOf(realm), regionId, x, y);

    /// <summary>Whether the living stands in its own realm's hub band.</summary>
    public static bool InHubBand(GameLiving living)
    {
        if (living == null)
            return false;
        // Region first: most actors are nowhere near a hub region, and the
        // position is only read when a band circle could contain it.
        SafeAnchor[] band = HubBandOf(living.Realm);
        ushort regionId = living.CurrentRegionID;
        for (int i = 0; i < band.Length; i++)
            if (band[i].RegionId == regionId)
                return InHubBand(band, regionId, living.X, living.Y);
        return false;
    }

    /// <summary>
    /// Pure peace rule: both sides are autonomous world bots of one realm and
    /// at least one of them stands in that realm's hub band. There is no
    /// retaliation exception: an opener must not pull two groups into a fight.
    /// </summary>
    public static bool HubPeaceApplies(bool bothAutonomousWorldBots, bool sameRealm, bool attackerInBand, bool targetInBand) =>
        bothAutonomousWorldBots && sameRealm && (attackerInBand || targetInBand);

    /// <summary>A free autonomous world bot: not a companion, helper or part
    /// of a human-led group, whose fights stay the human's decision.</summary>
    private static bool IsWorldBot(GameLiving living) => living is GameBot
    {
        IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false, IsPersistentPlayerCompanion: false,
        IsPlayerLedGroup: false
    };

    /// <summary>
    /// Hub-band peace between the player-shaped identities behind
    /// <paramref name="attacker"/> and <paramref name="target"/> (pets resolve
    /// to their owner). Cheap: type and realm checks first, then at most two
    /// squared distances per side.
    /// </summary>
    public static bool HubPeaceApplies(GameLiving attacker, GameLiving target) =>
        HubPeaceApplies(attacker, target, out _);

    public static bool HubPeaceApplies(GameLiving attacker, GameLiving target, out eRealm realm)
    {
        realm = eRealm.None;
        GameLiving first = PvpCombatant.Resolve(attacker);
        if (!IsWorldBot(first))
            return false;
        GameLiving second = PvpCombatant.Resolve(target);
        if (second == first || !IsWorldBot(second) || first.Realm != second.Realm)
            return false;
        realm = first.Realm;
        return HubPeaceApplies(true, true, InHubBand(first), InHubBand(second));
    }

    // Counters for RVR_HUB_PEACE, index = (int)eRealm. Each sits on its own
    // cache line so parallel brain ticks at different hubs do not contend.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 64)]
    private struct PaddedCounter
    {
        [System.Runtime.InteropServices.FieldOffset(0)] public int Value;
    }
    private static readonly PaddedCounter[] PeaceBlocked = new PaddedCounter[4];
    private static readonly PaddedCounter[] PeaceStray = new PaddedCounter[4];
    private static long _nextPeaceLogTick;
    public const long PeaceLogIntervalMilliseconds = 300_000;

    /// <summary>Counts one attack permission refused by the peace.</summary>
    public static void CountPeaceBlocked(eRealm realm, bool damageTime)
    {
        int index = (int)realm;
        if (index is >= 1 and <= 3)
            Interlocked.Increment(ref (damageTime ? PeaceStray : PeaceBlocked)[index].Value);
    }

    /// <summary>
    /// Returns and resets the counters as the RVR_HUB_PEACE line. "blocked"
    /// counts attack-permission checks refused between non-allied bots
    /// (target scans, swings, spell targets, AoE splash); "stray" counts hits
    /// stopped in TakeDamage because they bypassed the permission check or
    /// were already in flight.
    /// </summary>
    public static string DrainPeaceLine()
    {
        int alb = Interlocked.Exchange(ref PeaceBlocked[1].Value, 0), mid = Interlocked.Exchange(ref PeaceBlocked[2].Value, 0),
            hib = Interlocked.Exchange(ref PeaceBlocked[3].Value, 0);
        int stray = Interlocked.Exchange(ref PeaceStray[1].Value, 0) + Interlocked.Exchange(ref PeaceStray[2].Value, 0) +
            Interlocked.Exchange(ref PeaceStray[3].Value, 0);
        return $"RVR_HUB_PEACE window_s={PeaceLogIntervalMilliseconds / 1000} blocked={alb + mid + hib} " +
            $"by_hub=Svasud:{mid},Sauvage:{alb},Druim:{hib} stray={stray}";
    }

    /// <summary>Writes RVR_HUB_PEACE once per five minutes.</summary>
    public static void LogPeace(long nowTick)
    {
        long next = Interlocked.Read(ref _nextPeaceLogTick);
        if (next == 0)
        {
            // First call only arms the window so the line covers a full 300 s.
            Interlocked.CompareExchange(ref _nextPeaceLogTick, nowTick + PeaceLogIntervalMilliseconds, 0);
            return;
        }
        if (nowTick < next || Interlocked.CompareExchange(ref _nextPeaceLogTick, nowTick + PeaceLogIntervalMilliseconds, next) != next)
            return;
        string line = DrainPeaceLine();
        if (Log.IsInfoEnabled)
            Log.Info(line);
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
