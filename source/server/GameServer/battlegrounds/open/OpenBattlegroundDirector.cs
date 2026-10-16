using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;
using DOL.Logging;

namespace DOL.GS
{
    public enum OpenBgPhase { Quiet, Push, KeepFight, Retreat }

    /// <summary>One open BG cycle in game milliseconds (<see cref="GameLoop.GameLoopTime"/>) and its keep raid.</summary>
    public sealed record OpenBgCycle(long StartTick, int QuietMs, int PushMs, int FightMs, int RetreatMs, OpenBgRaid Raid)
    {
        public long TotalMs => (long)QuietMs + PushMs + FightMs + RetreatMs;
        public long EndTick => StartTick + TotalMs;
    }

    /// <summary>
    /// A roster unit inside the open BG as the raid pick sees it: its size, whether it is an 8-man, the guild of its
    /// leader (null: unguilded), its realm (geography only) and whether the keep counts it as a friend.
    /// </summary>
    public readonly record struct OpenBgRaidCandidate(int Unit, int Bots, bool Group8, string Guild, eRealm Realm, bool KeepFriend);

    /// <summary>
    /// One cycle's keep raid (owner decision 2026-10-11): several units of any realm and guild that attack the keep
    /// together as one battlegroup; each unit musters at its own realm's bridge. <paramref name="LeadUnit"/> (an 8-man
    /// where one could raid) gives the raid its <paramref name="LeadGuild"/>, named in the status line and credited when
    /// the lord lies defeated and unclaimed at the cycle's end. <paramref name="Bots"/> counts the raid's bots inside at
    /// its last change.
    /// </summary>
    public sealed record OpenBgRaid(IReadOnlyList<int> Units, int LeadUnit, int Bots, string LeadGuild, eRealm LeadRealm)
    {
        public static readonly OpenBgRaid None = new(Array.Empty<int>(), 0, 0, null, eRealm.None);
        public bool IsEmpty => Units == null || Units.Count == 0;
        public bool Contains(int unit) => !IsEmpty && Units.Contains(unit);

        /// <summary>The status and log text, e.g. "raid 3 units/18 bots, lead Ashen Concord"; "none" without a raid.</summary>
        public string Summary => IsEmpty ? "none" : $"raid {Units.Count} units/{Bots} bots, lead {LeadGuild ?? "unguilded"}";
    }

    /// <summary>
    /// The raid's battlegroup. From the push to the cycle's end every member of a raid unit that has entered the open BG
    /// and is not leaving it (alive or dead) carries one <see cref="BattleGroup"/> in its own battlegroup property
    /// (<see cref="RealmRaidBattleGroup"/>), so raid units of different realms and guilds are allies under
    /// <see cref="ServerRules.PvpCombatant.AreAllied"/>. Members that leave (relieved, unassigned) or whose unit left the
    /// raid are taken out on the next sync; <see cref="DetachAll"/> ends it (cycle end, a new raid, open BG off, server
    /// stop). Not thread-safe: the director calls it under its lock.
    /// </summary>
    public sealed class OpenBgRaidBattleGroup
    {
        private readonly HashSet<GameBot> _attached = new();

        /// <summary>The current battlegroup; null while nobody is attached.</summary>
        public BattleGroup BattleGroup { get; private set; }
        public int Count => _attached.Count;

        /// <summary>Attaches the raid's members (never in quiet), detaches the rest. Returns how many joined and left.</summary>
        public (int Added, int Removed) Sync(OpenBgPhase phase, OpenBgRaid raid, IEnumerable<AutonomousBattlegroundParticipation.OpenCrewMember> crew)
        {
            GameBot[] members = phase == OpenBgPhase.Quiet || raid == null || raid.IsEmpty || crew == null ? Array.Empty<GameBot>() :
                crew.Where(member => member.Bot != null && member.Entered && !member.Leaving && raid.Contains(member.Unit))
                    .Select(member => member.Bot).Distinct().ToArray();
            var keep = new HashSet<GameBot>(members);
            GameBot[] gone = _attached.Where(bot => !keep.Contains(bot)).ToArray();
            if (gone.Length > 0)
            {
                RealmRaidBattleGroup.Detach(BattleGroup, gone);
                _attached.ExceptWith(gone);
            }
            if (members.Length == 0)
            {
                if (_attached.Count == 0) BattleGroup = null;
                return (0, gone.Length);
            }
            BattleGroup ??= new BattleGroup();
            int added = RealmRaidBattleGroup.Attach(BattleGroup, members);
            _attached.UnionWith(members);
            // Raid mates already fighting each other stop now; target selection re-checks AreAllied on its own.
            if (added > 0) RealmRaidBattleGroup.DropFightsAgainstAllies(members);
            return (added, gone.Length);
        }

        /// <summary>Takes every attached bot out again; a battlegroup a bot joined since is left alone. Returns how many.</summary>
        public int DetachAll()
        {
            int count = _attached.Count;
            if (BattleGroup != null && count > 0) RealmRaidBattleGroup.Detach(BattleGroup, _attached.ToArray());
            _attached.Clear();
            BattleGroup = null;
            return count;
        }
    }

    /// <summary>
    /// A roster unit's current order. <paramref name="Point"/> is a resolved floor point (null: no point, the driver keeps
    /// its own priority). <paramref name="Muster"/> marks a raid unit still walking to its own bridge before the march.
    /// <paramref name="AtKeep"/> marks an assault or defend order whose point is the keep itself.
    /// </summary>
    public sealed record OpenBgOrder(int Unit, OpenBgRole Role, Point3D Point, bool Muster, bool AtKeep, long Cycle);

    /// <summary>The figures of the last OPEN_BG_FIELD line: units and bots outside their landing peace, of those inside.</summary>
    public sealed record OpenBgFieldFigures(int UnitsOut, int Units, int BotsOut, int Bots, int Pct);

    /// <summary>Metric log lines of the open BG (concept section 4).</summary>
    public static class OpenBgMetrics
    {
        private static OpenBgFieldFigures _lastField;

        public static int PercentOut(int botsOut, int bots) => bots == 0 ? 0 : (int)Math.Round(100.0 * botsOut / bots);

        public static string FieldLine(int unitsOut, int units, int botsOut, int bots) =>
            $"OPEN_BG_FIELD units_out={unitsOut}/{units} bots_out={botsOut}/{bots} pct={PercentOut(botsOut, bots)}";

        /// <summary>Remembers the figures of the OPEN_BG_FIELD line just logged (read by the status snapshot).</summary>
        public static void PublishField(int unitsOut, int units, int botsOut, int bots) =>
            Volatile.Write(ref _lastField, new OpenBgFieldFigures(unitsOut, units, botsOut, bots, PercentOut(botsOut, bots)));

        public static bool TryGetLastField(out OpenBgFieldFigures field)
        {
            field = Volatile.Read(ref _lastField);
            return field != null;
        }

        public static void ResetLastField() => Volatile.Write(ref _lastField, null);
    }

    /// <summary>
    /// The open BG's phase machine (spec 2026-10-11 §6): quiet, push, keep fight, retreat, one random 15-45 game-minute
    /// cycle after another, with a new keep raid each cycle: units of any realm that are not friends of the keep, the
    /// guilds with the fewest recent wins preferred (owner decision 2026-10-11: no realm alliance; group, guild and
    /// battlegroup as on the rest of the server). From the push on the raid is one battlegroup.
    /// Every 10 game seconds it reads the keep owner, advances the cycle and gives every roster unit inside a role and a
    /// point; <see cref="AutonomousBattlegroundDriver"/> reads the order for open-BG participants only. All state is in
    /// memory; nothing here touches a bot's level, gear, guild or realm points, and the director never releases a keep.
    /// </summary>
    public static class OpenBattlegroundDirector
    {
        public const int TickMilliseconds = 10_000;
        public const double QuietShare = 0.30, PushShare = 0.20, FightShare = 0.35, RetreatShare = 0.15;
        /// <summary>Each phase's share varies by up to this fraction of itself before the shares are renormalised.</summary>
        public const double ShareJitter = 0.05;
        public const int RecentCyclesKept = 6;
        /// <summary>A roaming unit rests this long at its point before it picks the next one.</summary>
        public const int PointRestMinMilliseconds = 30_000, PointRestMaxMilliseconds = 90_000;
        /// <summary>A lurking solo waits longer by its bridge, as a stealther would.</summary>
        public const int LurkRestMinMilliseconds = 120_000, LurkRestMaxMilliseconds = 300_000;
        /// <summary>A point not reached in this time is given up and replaced.</summary>
        public const int PointTimeoutMilliseconds = 8 * 60_000;
        public const int ArrivalDistance = 250;
        public const int UnreachableRepickMilliseconds = 30_000;
        /// <summary>The same assignment (an unreachable fixed point picked again) is logged at most this often per unit.</summary>
        public const int RoleRelogMilliseconds = 5 * 60_000;
        /// <summary>A raid unit has mustered once its leader is this close to its own bridge.</summary>
        public const int MusterDistance = 500;
        /// <summary>Humans this close to a portal keep landing stand in a sanctuary and draw no drift.</summary>
        public const int SanctuaryDistance = 2_500;
        /// <summary>A raid takes at least a third of the crew inside, and at least eight bots where that many may raid.</summary>
        public const int RaidCrewDivisor = 3, RaidMinimumBots = 8;
        // Floor search: bridges and lurk points are known to lie within 256 (spec), field points span the island and the river.
        private const float BridgeFloorRange = 256, LurkFloorRange = 512, FieldFloorRange = 1_024;
        private const long SlowTickMilliseconds = 50;
        private static readonly int[] FieldRadii = { 2_500, 3_500, 8_000, 11_000 };
        // Lurk draws per pick, each snapped to the floor and proved from the realm's landing.
        private const int LurkDraws = 6;
        private static readonly Logger Log = LoggerManager.Create(typeof(OpenBattlegroundDirector));

        // ---- pure rules ------------------------------------------------------------------------------------------

        public static bool IsPlayerRealm(eRealm realm) => realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia;

        /// <summary>
        /// A new cycle starting at tick 0 (the caller sets <see cref="OpenBgCycle.StartTick"/>): its total length is drawn
        /// in [min, max] game minutes, the four phases take 30/20/35/15 % of it, each varied by up to 5 % of itself. It has
        /// no raid yet: the raid forms when the push starts (<see cref="RaidForTick"/>).
        /// </summary>
        public static OpenBgCycle NewCycle(Random rng, int minMinutes, int maxMinutes)
        {
            rng ??= Random.Shared;
            int low = Math.Max(1, Math.Min(minMinutes, maxMinutes));
            int high = Math.Max(low, Math.Max(minMinutes, maxMinutes));
            long total = rng.NextInt64(low * 60_000L, high * 60_000L + 1);
            double[] shares = { QuietShare, PushShare, FightShare, RetreatShare };
            for (int i = 0; i < shares.Length; i++) shares[i] *= 1 + (rng.NextDouble() * 2 - 1) * ShareJitter;
            double sum = shares.Sum();
            int quiet = (int)Math.Round(total * shares[0] / sum);
            int push = (int)Math.Round(total * shares[1] / sum);
            int fight = (int)Math.Round(total * shares[2] / sum);
            int retreat = (int)(total - quiet - push - fight);
            return new OpenBgCycle(0, quiet, push, fight, retreat, OpenBgRaid.None);
        }

        /// <summary>
        /// The raid for this tick. Quiet: none (quiet has no raid roles). Push: formed at its start from the units inside,
        /// formed anew when the keep changed hands to a raid unit, and topped up while its bots inside stay below
        /// <see cref="RaidTargetBots"/> (units that entered later, a raid shrunk by reliefs). Keep fight and retreat: as it
        /// stands. <paramref name="reason"/> names a change for the log (null: unchanged); "keep_changed" means a new raid
        /// whose battlegroup starts empty.
        /// </summary>
        public static OpenBgRaid RaidForTick(Random rng, OpenBgPhase phase, OpenBgRaid raid, IReadOnlyList<OpenBgRaidCandidate> inside,
            IReadOnlyDictionary<string, int> recentWins, out string reason)
        {
            reason = null;
            raid ??= OpenBgRaid.None;
            if (phase == OpenBgPhase.Quiet)
            {
                if (!raid.IsEmpty) reason = "quiet";
                return OpenBgRaid.None;
            }
            if (phase != OpenBgPhase.Push) return raid;
            if (RaidNeedsRepick(raid, inside))
            {
                reason = "keep_changed";
                return PickRaid(rng, inside, recentWins);
            }
            OpenBgRaid next = ExtendRaid(rng, raid, inside, recentWins);
            if (!ReferenceEquals(next, raid))
                reason = raid.IsEmpty ? "push" : RaidBotsInside(raid, inside) == 0 ? "no_raid_inside" : "top_up";
            return next;
        }

        /// <summary>
        /// The bots a raid wants: a third of the crew inside (rounded up), at least <see cref="RaidMinimumBots"/>, never
        /// more than the bots that may raid at all.
        /// </summary>
        public static int RaidTargetBots(int crewInside, int eligibleBots) =>
            Math.Max(0, Math.Min(eligibleBots, Math.Max((crewInside + RaidCrewDivisor - 1) / RaidCrewDivisor, RaidMinimumBots)));

        /// <summary>
        /// A new raid: units inside that are not friends of the keep, of every realm (the realm plays no part), drawn one
        /// by one with the weight 1 / (1 + recent wins of the unit's guild)² until the raid reaches
        /// <see cref="RaidTargetBots"/>: a unit of a guild without recent wins is four times as likely as one of a guild
        /// with a win. An eligible 8-man is drawn first and leads; otherwise the first drawn unit leads. Empty when no unit
        /// may raid.
        /// </summary>
        public static OpenBgRaid PickRaid(Random rng, IReadOnlyList<OpenBgRaidCandidate> inside, IReadOnlyDictionary<string, int> recentWins) =>
            ExtendRaid(rng, OpenBgRaid.None, inside, recentWins);

        /// <summary>
        /// The raid topped up to <see cref="RaidTargetBots"/> with units inside that are neither in it nor friends of the
        /// keep, drawn as in <see cref="PickRaid"/> (an eligible 8-man first when the raid has none). Raid units no longer
        /// inside are dropped from it; the lead stays while its unit is inside. The same instance when nothing changes.
        /// </summary>
        public static OpenBgRaid ExtendRaid(Random rng, OpenBgRaid raid, IReadOnlyList<OpenBgRaidCandidate> inside, IReadOnlyDictionary<string, int> recentWins)
        {
            rng ??= Random.Shared;
            raid ??= OpenBgRaid.None;
            OpenBgRaidCandidate[] present = Present(inside);
            OpenBgRaidCandidate[] eligible = present.Where(unit => !unit.KeepFriend).ToArray();
            List<OpenBgRaidCandidate> staying = eligible.Where(unit => raid.Contains(unit.Unit)).ToList();
            int target = RaidTargetBots(present.Sum(unit => unit.Bots), eligible.Sum(unit => unit.Bots));
            int have = staying.Sum(unit => unit.Bots);
            List<OpenBgRaidCandidate> pool = eligible.Where(unit => !raid.Contains(unit.Unit)).ToList();
            if (have >= target || pool.Count == 0) return raid;
            var picked = new List<OpenBgRaidCandidate>();
            OpenBgRaidCandidate[] eights = staying.Any(unit => unit.Group8) ? Array.Empty<OpenBgRaidCandidate>() : pool.Where(unit => unit.Group8).ToArray();
            if (eights.Length > 0)
            {
                OpenBgRaidCandidate eight = WeightedUnit(rng, eights, recentWins);
                picked.Add(eight);
                pool.Remove(eight);
            }
            while (pool.Count > 0 && (picked.Count == 0 || have + picked.Sum(unit => unit.Bots) < target))
            {
                OpenBgRaidCandidate next = WeightedUnit(rng, pool, recentWins);
                picked.Add(next);
                pool.Remove(next);
            }
            OpenBgRaidCandidate leader = staying.Any(unit => unit.Unit == raid.LeadUnit) ? staying.First(unit => unit.Unit == raid.LeadUnit)
                : staying.Count > 0 ? staying.OrderByDescending(unit => unit.Bots).ThenBy(unit => unit.Unit).First() : picked[0];
            return new OpenBgRaid(staying.Concat(picked).Select(unit => unit.Unit).OrderBy(id => id).ToArray(), leader.Unit,
                have + picked.Sum(unit => unit.Bots), GuildKey(leader.Guild), leader.Realm);
        }

        /// <summary>The bots of the raid's units inside.</summary>
        public static int RaidBotsInside(OpenBgRaid raid, IReadOnlyList<OpenBgRaidCandidate> inside) =>
            raid == null ? 0 : Present(inside).Where(unit => raid.Contains(unit.Unit)).Sum(unit => unit.Bots);

        private static OpenBgRaidCandidate[] Present(IReadOnlyList<OpenBgRaidCandidate> inside) =>
            (inside ?? Array.Empty<OpenBgRaidCandidate>()).Where(unit => unit.Bots > 0)
                .GroupBy(unit => unit.Unit).Select(unit => unit.First()).ToArray();

        /// <summary>The recent wins of a guild (case-insensitive); none for an unguilded unit.</summary>
        public static int WinsOf(string guild, IReadOnlyDictionary<string, int> recentWins)
        {
            string key = GuildKey(guild);
            if (key == null || recentWins == null) return 0;
            foreach (KeyValuePair<string, int> entry in recentWins)
                if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)) return Math.Max(0, entry.Value);
            return 0;
        }

        private static OpenBgRaidCandidate WeightedUnit(Random rng, IReadOnlyList<OpenBgRaidCandidate> units, IReadOnlyDictionary<string, int> recentWins)
        {
            double[] weights = units.Select(unit =>
            {
                int wins = WinsOf(unit.Guild, recentWins);
                return 1.0 / ((1.0 + wins) * (1.0 + wins));
            }).ToArray();
            double roll = rng.NextDouble() * weights.Sum();
            for (int i = 0; i < units.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0) return units[i];
            }
            return units[^1];
        }

        private static string GuildKey(string guild) => string.IsNullOrWhiteSpace(guild) ? null : guild.Trim();

        /// <summary>
        /// Whether the raid is formed anew in the push: a raid unit inside became a friend of the keep (the keep changed
        /// hands to it before the fight). A raid without units inside is topped up instead (<see cref="ExtendRaid"/>).
        /// </summary>
        public static bool RaidNeedsRepick(OpenBgRaid raid, IReadOnlyList<OpenBgRaidCandidate> inside) =>
            raid != null && !raid.IsEmpty && Present(inside).Any(unit => unit.KeepFriend && raid.Contains(unit.Unit));

        /// <summary>
        /// The raid took the keep: in the keep fight or the retreat a raid unit is now a friend of the keep (its guild
        /// claimed it). The raid's other units stop assaulting a keep their raid holds and hold its approaches
        /// (<see cref="OpenBattlegroundRoles.RaidPartnerHolds"/>); its claiming guild defends it.
        /// </summary>
        public static bool RaidTookKeep(OpenBgPhase phase, OpenBgRaid raid, IReadOnlyList<OpenBgRaidCandidate> inside) =>
            phase is OpenBgPhase.KeepFight or OpenBgPhase.Retreat && raid != null && !raid.IsEmpty &&
            (inside ?? Array.Empty<OpenBgRaidCandidate>()).Any(unit => unit.KeepFriend && raid.Contains(unit.Unit));

        /// <summary>
        /// A keep that changed hands to a raid unit ends the fight (owner decision 2026-10-15: about one change per cycle,
        /// not one every few game minutes): in the push and the keep fight the cycle goes on in the retreat; quiet and
        /// retreat stay as they are.
        /// </summary>
        public static OpenBgPhase PhaseAfterKeepChange(OpenBgPhase current, bool keepChangedToRaidUnit) =>
            keepChangedToRaidUnit && current is OpenBgPhase.Push or OpenBgPhase.KeepFight ? OpenBgPhase.Retreat : current;

        /// <summary>
        /// The cycle with its phases before the retreat ended at <paramref name="now"/>: start and retreat length stay, so
        /// <see cref="PhaseAt"/> answers retreat from now on and the cycle ends one retreat later.
        /// </summary>
        public static OpenBgCycle CycleRetreatingAt(OpenBgCycle cycle, long now)
        {
            long left = Math.Max(0, now - cycle.StartTick);
            int quiet = (int)Math.Min(cycle.QuietMs, left);
            left -= quiet;
            int push = (int)Math.Min(cycle.PushMs, left);
            left -= push;
            int fight = (int)Math.Min(cycle.FightMs, left);
            return cycle with { QuietMs = quiet, PushMs = push, FightMs = fight };
        }

        /// <summary>The phase at a game tick: quiet before and at the start, retreat from its start on (also past the end).</summary>
        public static OpenBgPhase PhaseAt(OpenBgCycle cycle, long now)
        {
            if (cycle == null) return OpenBgPhase.Quiet;
            long elapsed = now - cycle.StartTick;
            if (elapsed < cycle.QuietMs) return OpenBgPhase.Quiet;
            elapsed -= cycle.QuietMs;
            if (elapsed < cycle.PushMs) return OpenBgPhase.Push;
            elapsed -= cycle.PushMs;
            return elapsed < cycle.FightMs ? OpenBgPhase.KeepFight : OpenBgPhase.Retreat;
        }

        /// <summary>
        /// The keep owner's realm: the realm of a real claiming guild, human or bot (its keep realm if the guild has none);
        /// None while the Frontier Wardens garrison holds it or nobody has claimed it after the lord fell.
        /// </summary>
        public static eRealm OwnerRealm(bool realGuild, bool garrison, eRealm guildRealm, eRealm keepRealm)
        {
            if (!realGuild || garrison) return eRealm.None;
            return IsPlayerRealm(guildRealm) ? guildRealm : IsPlayerRealm(keepRealm) ? keepRealm : eRealm.None;
        }

        /// <summary>The keep's claiming guild by name: a real guild, human or bot; null for the Frontier Wardens garrison or nobody.</summary>
        public static string OwnerGuild(bool realGuild, bool garrison, string guildName) =>
            realGuild && !garrison ? GuildKey(guildName) : null;

        /// <summary>
        /// The guild that won a finished cycle: the guild holding the keep at the cycle's end (a raid guild that took it,
        /// or the guild that held it); the raid's lead guild when the lord lies defeated and unclaimed; else nobody (the
        /// garrison held it).
        /// </summary>
        public static string CycleWinner(string ownerGuildAtEnd, bool lordDefeatedUnclaimed, string raidLeadGuild)
        {
            if (lordDefeatedUnclaimed) return GuildKey(raidLeadGuild);
            return GuildKey(ownerGuildAtEnd);
        }

        public static string Describe(OpenBgPhase phase) => phase switch
        {
            OpenBgPhase.Quiet => "quiet",
            OpenBgPhase.Push => "push",
            OpenBgPhase.KeepFight => "keep_fight",
            _ => "retreat",
        };

        // ---- runtime -------------------------------------------------------------------------------------------

        private sealed class MapData
        {
            public BridgePoint[] Bridges;
            public Point3D Keep;
            public Point3D[] Field;
            public Point3D[] Camps;
            public Point3D[] Landings;
            // Each realm's own landing (its portal keep), the start of the lurk point proof.
            public Dictionary<eRealm, Point3D> LandingByRealm = new();
        }

        private sealed class UnitState
        {
            public OpenBgRole Role;
            public Point3D Point;
            public bool AtKeep;
            public bool Muster;
            public long PointSince;
            public long ArrivedAt;
            public long RestMilliseconds;
            public long DriftCycle = -1;
            public long MusteredCycle = -1;
            public bool Unreachable;
            public string LoggedLine;
            public long LoggedAt;
        }

        private sealed class Snapshot
        {
            public static readonly Snapshot Empty = new();
            public OpenBgPhase Phase = OpenBgPhase.Quiet;
            public OpenBgRaid Raid = OpenBgRaid.None;
            public eRealm Owner = eRealm.None;
            public long Cycle;
            public Dictionary<int, OpenBgOrder> Orders = new();
        }

        // A roster unit inside the open BG this tick: its members there, its leader, its kind and the keep's view of it.
        private sealed class InsideUnit
        {
            public int Id;
            public AutonomousBattlegroundParticipation.OpenCrewMember[] Members;
            public GameBot Leader;
            public bool Grouped;
            public bool KeepEnemy;
            public OpenBgRaidCandidate Candidate;
        }

        private static readonly object Sync = new();
        private static readonly Random Rng = new();
        private static readonly Dictionary<int, UnitState> UnitStates = new();
        // The guild that won each recent cycle (null: nobody, the garrison held the keep).
        private static readonly Queue<string> RecentWinners = new();
        private static readonly HashSet<string> UnresolvedLogged = new(StringComparer.Ordinal);
        // Lurk spots a unit found no route to in this cycle; no lurk point is picked near them again before the next cycle.
        private static readonly List<Point3D> DeadLurkSpots = new();
        // The raid's battlegroup, from the push to the cycle's end.
        private static readonly OpenBgRaidBattleGroup RaidBattleGroup = new();
        private static MapData _map;
        private static OpenBgCycle _cycle;
        private static long _cycleIndex;
        private static OpenBgPhase _loggedPhase;
        private static string _loggedOwner;
        private static OpenBgRaid _loggedRaid;
        private static bool _logged;
        private static Snapshot _snapshot = Snapshot.Empty;
        private static long _fieldLoggedAt;
        private const long FieldLogMilliseconds = 60_000;

        public static OpenBgPhase Phase => Volatile.Read(ref _snapshot).Phase;
        /// <summary>This cycle's keep raid (empty before the first tick or when no unit may raid).</summary>
        public static OpenBgRaid Raid => Volatile.Read(ref _snapshot).Raid;
        public static eRealm KeepOwnerRealm => Volatile.Read(ref _snapshot).Owner;
        /// <summary>Counts cycles since the start; 0 before the first tick.</summary>
        public static long CycleNumber => Volatile.Read(ref _snapshot).Cycle;

        /// <summary>The order of the bot's roster unit. False for bracket participants and units without an order yet.</summary>
        public static bool TryGetOrder(GameBot bot, out OpenBgOrder order)
        {
            order = null;
            int unit = AutonomousBattlegroundParticipation.OpenUnitOf(bot);
            return unit > 0 && Volatile.Read(ref _snapshot).Orders.TryGetValue(unit, out order) && order != null;
        }

        /// <summary>The current role of a roster unit inside (the open BG window). False for a unit without an order yet.</summary>
        public static bool TryGetUnitRole(int unit, out OpenBgRole role)
        {
            role = OpenBgRole.Roam;
            if (unit <= 0 || !Volatile.Read(ref _snapshot).Orders.TryGetValue(unit, out OpenBgOrder order) || order == null) return false;
            role = order.Role;
            return true;
        }

        /// <summary>The driver found no route to the order's point: the next tick gives the unit another one.</summary>
        public static void ReportUnreachable(int unit, Point3D point)
        {
            lock (Sync)
                if (UnitStates.TryGetValue(unit, out UnitState state) && ReferenceEquals(state.Point, point))
                {
                    state.Unreachable = true;
                    // A dead-end lurk spot is dropped after its first failure for the rest of the cycle.
                    if (state.Role == OpenBgRole.Lurk && point != null) DeadLurkSpots.Add(point);
                }
        }

        /// <summary>Phase, the raid's summary (the status file's "Attacker") and seconds left in the cycle, for the status window (lock-free).</summary>
        public static OpenBgPhaseStatus PhaseStatus()
        {
            OpenBgCycle cycle = Volatile.Read(ref _cycle);
            long left = cycle == null ? 0 : Math.Max(0, (cycle.EndTick - GameLoop.GameLoopTime) / 1000);
            return new OpenBgPhaseStatus(Phase.ToString(), Raid.Summary, (int)left);
        }

        /// <summary>Called by the campaign manager every <see cref="TickMilliseconds"/> of game time for the Open definition only.</summary>
        public static void Tick(BattlegroundDefinition definition, long now)
        {
            if (definition?.Kind != BattlegroundDefinitionKind.Open) return;
            if (!OpenBattlegroundPolicy.IsEnabled)
            {
                Reset();
                return;
            }
            // The /bgsetup window and the launcher status file read the phase through this hook.
            OpenBattlegroundStatus.PhaseSource ??= PhaseStatus;
            // Gothwaite PvP mode: no bot crew while no bot is reserved for it, so no cycle, raid or order is computed (section 5.4).
            if (GothwaitePvpRules.SuppressesOpenBattlegroundCrew(GothwaitePvpMode.IsActive, GothwaitePvpOpenBgReserve.Set.Count))
            {
                Reset();
                return;
            }
            Stopwatch watch = Stopwatch.StartNew();
            AbstractGameKeep keep = BattlegroundCampaignCatalog.CentralKeep(definition);
            bool realGuild = keep?.Guild != null && PvpCombatant.IsRealGuild(keep.Guild);
            bool garrison = keep != null && PvpKeepCampaign.IsGarrison(keep.Guild);
            eRealm owner = keep == null ? eRealm.None :
                OwnerRealm(realGuild, garrison, keep.Guild?.Realm ?? eRealm.None, keep.Realm);
            string ownerGuild = keep == null ? null : OwnerGuild(realGuild, garrison, keep.Guild?.Name);
            bool lordDown = keep?.Guild == null && keep?.DBKeep?.LordDefeated == true;
            AutonomousBattlegroundParticipation.OpenCrewMember[] crew = AutonomousBattlegroundParticipation.OpenCrew();
            int units;
            lock (Sync)
            {
                _map ??= BuildMap(definition, keep);
                InsideUnit[] inside = InsideUnits(definition, keep, ownerGuild, crew);
                AdvanceCycle(now, owner, ownerGuild, lordDown, inside);
                units = UpdateOrders(definition, keep, owner, lordDown, inside, crew, now);
                LogField(inside, now);
            }
            if (watch.ElapsedMilliseconds > SlowTickMilliseconds)
                Log.Warn($"OPEN_BG_TICK_SLOW part=director ms={watch.ElapsedMilliseconds} units={units}");
        }

        // Callers hold Sync. Once per 60 game seconds: how much of the roster stands out in the field, i.e. has a field order
        // (Roam, Assault, Defend, BridgeWatch) and is farther than the landing peace radius from its own realm's landing.
        // Per-bot cost is a position test against at most a few landings; nothing is allocated.
        private static void LogField(InsideUnit[] inside, long now)
        {
            if (now - _fieldLoggedAt < FieldLogMilliseconds) return;
            _fieldLoggedAt = now;
            var landings = AutonomousHubDeparture.OpenBgLandings();
            Dictionary<int, OpenBgOrder> orders = Volatile.Read(ref _snapshot).Orders;
            int unitsOut = 0, bots = 0, botsOut = 0;
            foreach (InsideUnit unit in inside)
            {
                bots += unit.Members.Length;
                if (!orders.TryGetValue(unit.Id, out OpenBgOrder order) || order == null) continue;
                if (order.Role is not (OpenBgRole.Roam or OpenBgRole.Assault or OpenBgRole.Defend or OpenBgRole.BridgeWatch)) continue;
                int outCount = 0;
                foreach (AutonomousBattlegroundParticipation.OpenCrewMember member in unit.Members)
                {
                    GameBot bot = member.Bot;
                    if (!AutonomousHubDeparture.NearOwnLanding(bot.CurrentRegionID, bot.X, bot.Y, OpenBattlegroundRoster.LandingSideOf(unit.Id, bot.Realm),
                            AutonomousHubDeparture.OpenBgLandingPeaceRadius, landings)) outCount++;
                }
                if (outCount == 0) continue;
                unitsOut++;
                botsOut += outCount;
            }
            Log.Info(OpenBgMetrics.FieldLine(unitsOut, inside.Length, botsOut, bots));
            OpenBgMetrics.PublishField(unitsOut, inside.Length, botsOut, bots);
        }

        /// <summary>Forgets the cycle, the map and every order (open BG switched off or the server stopped).</summary>
        public static void Reset()
        {
            lock (Sync)
            {
                DetachRaid();
                UnitStates.Clear();
                RecentWinners.Clear();
                UnresolvedLogged.Clear();
                DeadLurkSpots.Clear();
                _map = null;
                _cycle = null;
                _cycleIndex = 0;
                _logged = false;
                Volatile.Write(ref _snapshot, Snapshot.Empty);
            }
        }

        // Callers hold Sync. The roster units inside region 252, each with its leader and the keep's view of it. Only the
        // claiming guild (or its alliance, group check included) makes a unit a keep friend here; the "anyone while the
        // lord lies defeated" friendship of the keep rule never keeps a unit out of the raid.
        private static InsideUnit[] InsideUnits(BattlegroundDefinition definition, AbstractGameKeep keep, string ownerGuild,
            AutonomousBattlegroundParticipation.OpenCrewMember[] crew)
        {
            var units = new List<InsideUnit>();
            foreach (IGrouping<int, AutonomousBattlegroundParticipation.OpenCrewMember> group in crew
                .Where(member => member.Entered && !member.Leaving && member.Bot != null && member.Bot.CurrentRegionID == definition.RegionId)
                .GroupBy(member => member.Unit).OrderBy(group => group.Key))
            {
                AutonomousBattlegroundParticipation.OpenCrewMember[] members = group.ToArray();
                GameBot leader = LeaderOf(members);
                if (leader == null) continue;
                OpenBgUnitKind? kind = OpenBattlegroundRoster.KindOf(group.Key);
                bool keepEnemy = keep != null && GameServer.KeepManager.IsEnemy(keep, leader);
                units.Add(new InsideUnit
                {
                    Id = group.Key,
                    Members = members,
                    Leader = leader,
                    Grouped = kind is OpenBgUnitKind known ? known != OpenBgUnitKind.Solo : members.Length > 1,
                    KeepEnemy = keepEnemy,
                    Candidate = new OpenBgRaidCandidate(group.Key, members.Length,
                        kind is OpenBgUnitKind size ? size == OpenBgUnitKind.Group8 : members.Length >= OpenBattlegroundRoster.Group8Minimum,
                        leader.Guild?.Name, leader.Realm, keep != null && ownerGuild != null && !keepEnemy),
                });
            }
            return units.ToArray();
        }

        // Callers hold Sync.
        private static void AdvanceCycle(long now, eRealm owner, string ownerGuild, bool lordDown, InsideUnit[] inside)
        {
            OpenBgRaidCandidate[] candidates = inside.Select(unit => unit.Candidate).ToArray();
            if (_cycle == null || now >= _cycle.EndTick)
            {
                if (_cycle != null)
                {
                    RecentWinners.Enqueue(CycleWinner(ownerGuild, lordDown, _cycle.Raid.LeadGuild));
                    while (RecentWinners.Count > RecentCyclesKept) RecentWinners.Dequeue();
                }
                DetachRaid();
                _cycle = NewCycle(Rng, OpenBattlegroundProperties.CycleMinMinutes, OpenBattlegroundProperties.CycleMaxMinutes) with { StartTick = now };
                _cycleIndex++;
                DeadLurkSpots.Clear();
            }
            OpenBgPhase phase = PhaseAt(_cycle, now);
            // A keep that changed hands to a raid unit ends the fight: no new raid, the cycle goes on in the retreat (its
            // orders: the raid's claiming guild defends, the rest hold the approaches or leave as in any retreat).
            if (PhaseAfterKeepChange(phase, RaidNeedsRepick(_cycle.Raid, candidates)) != phase)
            {
                Log.Info($"OPEN_BG_KEEP_CHANGED cycle={_cycleIndex} from={Describe(phase)} owner_guild=\"{ownerGuild ?? "none"}\" lead=\"{_cycle.Raid.LeadGuild ?? "unguilded"}\"");
                _cycle = CycleRetreatingAt(_cycle, now);
                phase = PhaseAt(_cycle, now);
            }
            // The raid forms when the push starts and is topped up through the push (late entries, reliefs).
            OpenBgRaid raid = RaidForTick(Rng, phase, _cycle.Raid, candidates, RecentWins(), out string reason);
            if (reason != null)
            {
                // A raid formed anew starts a new battlegroup; a top-up only adds members on the next sync.
                if (reason is "keep_changed" or "quiet") DetachRaid();
                _cycle = _cycle with { Raid = raid };
                LogRaid(reason, candidates);
            }
            if (_logged && phase == _loggedPhase && string.Equals(ownerGuild, _loggedOwner, StringComparison.Ordinal) &&
                ReferenceEquals(_cycle.Raid, _loggedRaid)) return;
            _logged = true;
            _loggedPhase = phase;
            _loggedOwner = ownerGuild;
            _loggedRaid = _cycle.Raid;
            Log.Info($"OPEN_BG_PHASE phase={Describe(phase)} attacker=\"{_cycle.Raid.Summary}\" cycle_s={_cycle.TotalMs / 1000} owner={owner} " +
                $"owner_guild=\"{ownerGuild ?? "none"}\" cycle={_cycleIndex} lord_down={lordDown} speed={OfflineWorldSpeedControl.AchievedMultiplier:0.0}");
        }

        // Callers hold Sync. One line per raid change: its units, bots, lead guild and its units per realm (each musters at
        // its own realm's bridge).
        private static void LogRaid(string reason, OpenBgRaidCandidate[] inside)
        {
            OpenBgRaid raid = _cycle.Raid;
            string realms = string.Join(",", inside.Where(unit => raid.Contains(unit.Unit)).GroupBy(unit => unit.Realm).OrderBy(group => group.Key)
                .Select(group => $"{group.Key}:{group.Count()}"));
            Log.Info($"OPEN_BG_RAID cycle={_cycleIndex} units={(raid.IsEmpty ? "none" : string.Join(",", raid.Units))} bots={raid.Bots} " +
                $"lead=\"{(raid.IsEmpty ? "none" : raid.LeadGuild ?? "unguilded")}\" lead_unit={raid.LeadUnit} " +
                $"realms={(realms.Length == 0 ? "none" : realms)} reason={reason}");
        }

        private static Dictionary<string, int> RecentWins() =>
            RecentWinners.Where(guild => guild != null).GroupBy(guild => guild, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        // Callers hold Sync. Returns the number of units with an order.
        private static int UpdateOrders(BattlegroundDefinition definition, AbstractGameKeep keep, eRealm owner, bool lordDown, InsideUnit[] units,
            AutonomousBattlegroundParticipation.OpenCrewMember[] crew, long now)
        {
            OpenBgPhase phase = PhaseAt(_cycle, now);
            OpenBgRaid raid = _cycle.Raid;
            // A raid that took the keep stops assaulting it: its claiming guild defends it, its partners hold the approaches.
            bool raidTookKeep = RaidTookKeep(phase, raid, units.Select(unit => unit.Candidate).ToArray());
            var orders = new Dictionary<int, OpenBgOrder>();
            Point3D[] humans = null;
            var groupIndex = new Dictionary<eRealm, int>();
            // Without a field point far from the keep a bystander keeps its point instead of re-picking every tick.
            bool bystanderRoamExists = OpenBattlegroundRoles.FieldAwayFromKeep(_map.Field, _map.Bridges, _map.Keep, OpenBattlegroundRoles.BystanderKeepDistance).Length > 0;
            foreach (InsideUnit unit in units)
            {
                GameBot leader = unit.Leader;
                // Server PvP: the realm of the keep the unit landed at, so it musters and lurks on that side.
                eRealm realm = OpenBattlegroundRoster.LandingSideOf(unit.Id, leader.Realm);
                int index = 0;
                if (unit.Grouped)
                {
                    groupIndex.TryGetValue(realm, out index);
                    groupIndex[realm] = index + 1;
                }
                bool inRaid = !raidTookKeep && raid.Contains(unit.Id);
                bool friend = OpenBattlegroundRoles.ActsAsKeepFriend(unit.Candidate.KeepFriend, inRaid, lordDown, phase);
                var ctx = new OpenBgContext(phase, inRaid, friend, realm, unit.Grouped ? Math.Max(2, unit.Members.Length) : 1, true);
                OpenBgRole role = OpenBattlegroundRoles.Assign(ctx, index);
                // Camlann: keep friendship is the claiming guild's (group check included). A raid unit the keep does not count
                // as an enemy (the lord lies defeated and nobody claimed it yet) goes to the keep to claim it instead.
                OpenBgRole assigned = role;
                role = OpenBattlegroundRoles.ForKeepFriendship(role, keep != null, unit.KeepEnemy);
                bool keepFriend = friend || role != assigned;
                // The raid's other units once a raid guild claimed the keep: the keep's guards still count them as enemies,
                // so they hold the approaches outside guard reach instead of walking off as bystanders.
                bool partner = OpenBattlegroundRoles.RaidPartnerHolds(raidTookKeep, raid.Contains(unit.Id), friend);
                if (partner) role = OpenBgRole.Defend;
                bool bystander = !partner && OpenBattlegroundRoles.IsBystanderInFight(phase, inRaid, friend);

                if (!UnitStates.TryGetValue(unit.Id, out UnitState state)) UnitStates[unit.Id] = state = new UnitState();
                if (state.Point != null && state.ArrivedAt == 0 &&
                    leader.GetDistanceTo(state.Point) <= (state.Muster ? MusterDistance : ArrivalDistance)) state.ArrivedAt = now;
                bool roaming = role is OpenBgRole.Roam or OpenBgRole.Lurk;
                // An unreachable point is replaced, but not more often than every 30 game seconds.
                bool repick = state.Point == null || role != state.Role || state.Unreachable && now - state.PointSince >= UnreachableRepickMilliseconds ||
                    roaming && (state.ArrivedAt > 0 && now - state.ArrivedAt >= state.RestMilliseconds || state.ArrivedAt == 0 && now - state.PointSince >= PointTimeoutMilliseconds);
                // A raid unit in the push musters at its own realm's bridge first, then marches on the keep, where the
                // four-at-the-gate rule gathers the raid; never in the keep fight.
                bool muster = OpenBattlegroundRoles.NeedsMuster(role, phase, state.MusteredCycle == _cycleIndex);
                if (muster && state.Muster && state.ArrivedAt > 0) { state.MusteredCycle = _cycleIndex; muster = false; }
                if (role == OpenBgRole.Assault && muster != state.Muster) repick = true;
                // A bystander still roaming toward the keep from the push leaves it when the keep fight begins.
                if (role == OpenBgRole.Roam && state.Point != null && bystanderRoamExists && bystander &&
                    OpenBattlegroundRoles.Distance2D(state.Point, _map.Keep) < OpenBattlegroundRoles.BystanderKeepDistance) repick = true;
                if (repick)
                {
                    humans ??= FieldHumans(definition);
                    Choose(state, unit.Members[0].Group, leader, role, realm, phase, bystander, unit.KeepEnemy || partner, keepFriend, inRaid,
                        partner, muster, humans, now);
                }
                if (state.Point != null)
                    orders[unit.Id] = new OpenBgOrder(unit.Id, state.Role, state.Point, state.Muster, state.AtKeep, _cycleIndex);
            }
            foreach (int gone in UnitStates.Keys.Where(id => units.All(unit => unit.Id != id)).ToArray()) UnitStates.Remove(gone);
            (int added, int removed) = RaidBattleGroup.Sync(phase, raid, crew);
            if (added > 0)
                Log.Info($"OPEN_BG_RAID_BATTLEGROUP cycle={_cycleIndex} added={added} members={RaidBattleGroup.Count} " +
                    $"lead=\"{raid.LeadGuild ?? "unguilded"}\" left={removed}");
            Volatile.Write(ref _snapshot, new Snapshot { Phase = phase, Raid = raid, Owner = owner, Cycle = _cycleIndex, Orders = orders });
            return orders.Count;
        }

        // Callers hold Sync. Ends the raid's battlegroup (cycle end, a raid formed anew, open BG off, server stop).
        private static void DetachRaid() => RaidBattleGroup.DetachAll();

        // The unit's group leader when it is inside, else its longest-serving member.
        private static GameBot LeaderOf(AutonomousBattlegroundParticipation.OpenCrewMember[] members)
        {
            GameBot leader = members.Select(member => member.Bot).FirstOrDefault(bot => bot.Group != null && bot.Group.LivingLeader == bot);
            return leader ?? members.OrderBy(member => member.EnteredTick).Select(member => member.Bot).FirstOrDefault();
        }

        // Callers hold Sync. Picks the point for a (new) role, applies the once-per-cycle drift and logs the assignment.
        private static void Choose(UnitState state, string group, GameBot leader, OpenBgRole role, eRealm realm, OpenBgPhase phase,
            bool bystander, bool keepEnemy, bool keepFriend, bool inRaid, bool raidPartner, bool muster, Point3D[] humans, long now)
        {
            MapData map = _map;
            // Bystanders keep away from the keep while it is fought over: the keep fight is the raid against the keep's friends.
            Point3D point;
            bool atKeep = false;
            string bridge = null;
            switch (role)
            {
                // A raid unit musters at its own realm's bridge (no walk across the keep island to a foreign one).
                case OpenBgRole.Assault when muster:
                case OpenBgRole.BridgeWatch:
                case OpenBgRole.Retreat:
                    point = OpenBattlegroundRoles.RolePoint(OpenBgRole.BridgeWatch, realm, map.Bridges, map.Keep, map.Field, Rng);
                    bridge = OpenBattlegroundRoles.OwnBridge(realm, map.Bridges)?.Name;
                    break;
                case OpenBgRole.Assault:
                    // Every assaulting unit, whatever its size, goes to the keep: the driver marches it to the outer gate and
                    // sieges once four allied attackers stand there (live run 0.243.0: units under four got a skirmish point
                    // 2,500 off the keep and never reached the gate march).
                    point = map.Keep;
                    atKeep = true;
                    break;
                case OpenBgRole.Defend:
                    // The keep's friends hold the keep; a defender its guards would still shoot (a raid partner of the
                    // claiming guild) holds the approaches outside guard reach, never the keep itself.
                    atKeep = !keepEnemy;
                    point = atKeep ? map.Keep : OpenBattlegroundRoles.RolePoint(OpenBgRole.Roam, realm, map.Bridges, map.Keep,
                            OpenBattlegroundRoles.ApproachPoints(map.Field, map.Keep), Rng) ??
                        OpenBattlegroundRoles.RolePoint(OpenBgRole.Roam, realm, map.Bridges, map.Keep,
                            OpenBattlegroundRoles.FieldAwayFromKeep(map.Field, map.Bridges, map.Keep, OpenBattlegroundRoles.ApproachMinDistance), Rng) ??
                        OpenBattlegroundRoles.RolePoint(OpenBgRole.BridgeWatch, realm, map.Bridges, map.Keep, map.Field, Rng);
                    break;
                case OpenBgRole.Lurk:
                    point = LurkPoint(leader.CurrentRegion, realm, map);
                    break;
                default:
                    point = (bystander ? OpenBattlegroundRoles.RolePoint(OpenBgRole.Roam, realm, map.Bridges, map.Keep,
                            OpenBattlegroundRoles.FieldAwayFromKeep(map.Field, map.Bridges, map.Keep, OpenBattlegroundRoles.BystanderKeepDistance), Rng) : null) ??
                        OpenBattlegroundRoles.RolePoint(OpenBgRole.Roam, realm, map.Bridges, map.Keep, map.Field, Rng) ?? Nearest(map.Camps, map.Keep);
                    break;
            }
            bool drifted = false;
            if (role is OpenBgRole.Roam or OpenBgRole.Lurk && point != null && OpenBattlegroundProperties.OPEN_BG_PLAYER_DRIFT &&
                state.DriftCycle != _cycleIndex && humans.Length > 0)
            {
                state.DriftCycle = _cycleIndex;
                Point3D drift = OpenBattlegroundRoles.DriftedPoint(point, humans, true, Rng);
                Point3D snapped = drift != null && !ReferenceEquals(drift, point) &&
                    TryFloor(leader.CurrentRegion?.GetZone(drift.X, drift.Y), drift, FieldFloorRange, out Point3D floor) ? floor : null;
                // Drift never pulls a bystander back into the keep fight.
                if (bystander && snapped != null && OpenBattlegroundRoles.Distance2D(snapped, map.Keep) < OpenBattlegroundRoles.BystanderKeepDistance) snapped = null;
                // The snap can move the drift point onto a player, and a failed drift can leave the unit's own point there:
                // either way it takes a point clear of every human instead.
                point = OpenBattlegroundRoles.AfterDrift(point, snapped, humans,
                    () => UndriftedPoint(leader.CurrentRegion, role, realm, map, humans), out drifted);
            }
            state.Role = role;
            state.Point = point;
            state.AtKeep = atKeep;
            state.Muster = muster;
            state.PointSince = now;
            state.ArrivedAt = 0;
            state.Unreachable = false;
            state.RestMilliseconds = role == OpenBgRole.Lurk
                ? Rng.Next(LurkRestMinMilliseconds, LurkRestMaxMilliseconds + 1)
                : Rng.Next(PointRestMinMilliseconds, PointRestMaxMilliseconds + 1);
            string where = point == null ? "none" : $"{point.X},{point.Y},{point.Z}";
            string line = $"OPEN_BG_ROLE bot=\"{leader.Name}\" group=\"{group ?? "solo"}\" role={OpenBattlegroundRoles.Describe(role)} point={where} " +
                $"realm={realm} phase={Describe(phase)}{(muster ? " muster=true" : "")}{(bridge != null ? $" bridge={bridge}" : "")}{(drifted ? " drift=true" : "")}" +
                $"{(keepFriend ? " keep_friend=true" : "")}{(inRaid ? " raid=true" : "")}{(raidPartner ? " raid_partner=true" : "")}";
            // An unreachable bridge or keep is picked again every 30 s; the unchanged assignment is logged once per 5 game minutes.
            if (LogsRoleAgain(line == state.LoggedLine, state.LoggedAt, now))
            {
                Log.Info(line);
                state.LoggedLine = line;
                state.LoggedAt = now;
            }
        }

        /// <summary>A changed assignment is always logged; the same one again only after <see cref="RoleRelogMilliseconds"/>.</summary>
        public static bool LogsRoleAgain(bool sameAssignment, long loggedAt, long now) =>
            !sameAssignment || now - loggedAt >= RoleRelogMilliseconds;

        // Callers hold Sync. A point for a drifting unit whose own point lies within 800 of a human: a lurk spot clear of
        // humans for a lurker (a few draws), else a field point away from the bridges, clear of humans where one is.
        private static Point3D UndriftedPoint(Region region, OpenBgRole role, eRealm realm, MapData map, Point3D[] humans)
        {
            if (role == OpenBgRole.Lurk)
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    Point3D lurk = LurkPoint(region, realm, map);
                    if (OpenBattlegroundRoles.ClearOfHumans(lurk, humans)) return lurk;
                }
            Point3D[] field = OpenBattlegroundRoles.FieldAwayFromBridges(map.Field, map.Bridges);
            return OpenBattlegroundRoles.PointClearOfHumans(field.Length > 0 ? field : map.Field, humans, Rng);
        }

        // Callers hold Sync. A lurk point on the walkable floor that a route from the realm's landing reaches, never near a
        // spot that failed this cycle; a few draws, then the nearest camp to the realm's bridge (logged once).
        private static Point3D LurkPoint(Region region, eRealm realm, MapData map)
        {
            map.LandingByRealm.TryGetValue(realm, out Point3D landing);
            Point3D picked = OpenBattlegroundRoles.PickLurkPoint(
                () => OpenBattlegroundRoles.RolePoint(OpenBgRole.Lurk, realm, map.Bridges, map.Keep, map.Field, Rng),
                raw => TryFloor(region?.GetZone(raw.X, raw.Y), raw, LurkFloorRange, out Point3D floor) ? floor : null,
                floor => ProvenFromLanding(region, landing, floor), DeadLurkSpots, LurkDraws);
            if (picked != null) return picked;
            Point3D bridge = OpenBattlegroundRoles.OwnBridge(realm, map.Bridges)?.Point ?? map.Keep;
            LogUnresolvedOnce($"lurk:{realm}", $"OPEN_BG_ROLE_POINT_UNRESOLVED kind=lurk realm={realm} fallback=camp");
            return Nearest(map.Camps, bridge) ?? bridge;
        }

        // Humans in the field: alive, in the region, outside the portal keep sanctuaries.
        private static Point3D[] FieldHumans(BattlegroundDefinition definition)
        {
            Region region = WorldMgr.GetRegion(definition.RegionId);
            if (region == null) return Array.Empty<Point3D>();
            return ClientService.Instance.GetPlayersOfRegion(region)
                .Where(player => player.IsAlive && player.ObjectState == GameObject.eObjectState.Active)
                .Select(player => new Point3D(player.X, player.Y, player.Z))
                .Where(point => _map.Landings.All(landing => OpenBattlegroundRoles.Distance2D(point, landing) > SanctuaryDistance))
                .ToArray();
        }

        // The lurk proof: chained default-filter legs (the portal keep's own gate is passable) from the landing reach the floor.
        // Without a landing or a navmesh nothing can be proved, and the snapped floor stands as before.
        private static bool ProvenFromLanding(Region region, Point3D landing, Point3D floor)
        {
            var nav = PathfindingProvider.Instance;
            Zone zone = landing == null ? null : region?.GetZone(landing.X, landing.Y);
            if (zone == null || floor == null || region.GetZone(floor.X, floor.Y) != zone || !nav.IsAvailable || !nav.HasNavmesh(zone)) return true;
            Vector3 from = new(landing.X, landing.Y, landing.Z);
            Vector3 goal = new(floor.X, floor.Y, floor.Z);
            WrappedPathfindingNode[] nodes = System.Buffers.ArrayPool<WrappedPathfindingNode>.Shared.Rent(512);
            try
            {
                return AutonomousBattlegroundDriver.ChainRoute(from, goal, AutonomousBattlegroundDriver.MaxRouteLegs,
                    start => AutonomousBattlegroundDriver.QueryLeg(nav, zone, start, goal, nav.DefaultFilters, nodes), out _) ==
                    AutonomousBattlegroundDriver.RouteChain.Reached;
            }
            finally
            {
                System.Buffers.ArrayPool<WrappedPathfindingNode>.Shared.Return(nodes);
            }
        }

        // Callers hold Sync. Bridges, keep, field points and landings, resolved to navmesh floors once per start.
        private static MapData BuildMap(BattlegroundDefinition definition, AbstractGameKeep keep)
        {
            Region region = WorldMgr.GetRegion(definition.RegionId);
            Point3D keepPoint = keep != null ? new Point3D(keep.X, keep.Y, keep.Z) : new Point3D(33280, 38272, 3720);
            Point3D[] camps = BattlegroundCampaignManager.CampPositions(definition.RegionId);
            var bridges = new List<BridgePoint>();
            foreach (BridgePoint bridge in OpenBattlegroundRoles.ThidrankiBridges)
            {
                if (TryFloor(region?.GetZone(bridge.Point.X, bridge.Point.Y), bridge.Point, BridgeFloorRange, out Point3D floor))
                {
                    bridges.Add(bridge with { Point = floor });
                    continue;
                }
                Point3D camp = Nearest(camps, bridge.Point);
                LogUnresolvedOnce($"bridge:{bridge.Name}", $"OPEN_BG_ROLE_POINT_UNRESOLVED kind=bridge bridge={bridge.Name} fallback={(camp == null ? "fixture" : "camp")}");
                bridges.Add(camp == null ? bridge : bridge with { Point = camp });
            }
            var field = new List<Point3D>(camps);
            for (int ring = 0; ring < FieldRadii.Length; ring++)
                for (int step = 0; step < 8; step++)
                {
                    // Alternate rings are turned by half a step, so the points do not line up on eight spokes.
                    int radius = FieldRadii[ring];
                    double angle = step * Math.PI / 4 + (ring % 2) * Math.PI / 8;
                    var raw = new Point3D((int)(keepPoint.X + Math.Cos(angle) * radius), (int)(keepPoint.Y + Math.Sin(angle) * radius), keepPoint.Z);
                    if (TryFloor(region?.GetZone(raw.X, raw.Y), raw, FieldFloorRange, out Point3D floor)) field.Add(floor);
                }
            Point3D[] landings = BattlegroundCampaignCatalog.GetLandings(definition).Select(landing => new Point3D(landing.X, landing.Y, landing.Z)).ToArray();
            IReadOnlyList<eRealm> landingRealms = BattlegroundCampaignCatalog.GetLandingRealms(definition, landings.Length);
            var landingByRealm = new Dictionary<eRealm, Point3D>();
            for (int i = 0; i < landingRealms.Count; i++)
                if (IsPlayerRealm(landingRealms[i])) landingByRealm.TryAdd(landingRealms[i], landings[i]);
            Log.Info($"OPEN_BG_MAP region={definition.RegionId} bridges={string.Join(",", bridges.Select(bridge => $"{bridge.Name}:{bridge.Point.X},{bridge.Point.Y},{bridge.Point.Z}"))} " +
                $"field={field.Count} camps={camps.Length} landings={landings.Length}");
            return new MapData
            {
                Bridges = bridges.ToArray(), Keep = keepPoint, Field = field.ToArray(), Camps = camps, Landings = landings, LandingByRealm = landingByRealm,
            };
        }

        // A navmesh floor within a few units of the point's XY and within range of its height.
        private static bool TryFloor(Zone zone, Point3D point, float zRange, out Point3D floor)
        {
            floor = null;
            var nav = PathfindingProvider.Instance;
            if (zone == null || point == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;
            Vector3? found = nav.GetClosestPoint(zone, new Vector3(point.X, point.Y, point.Z), 64, 64, zRange, nav.DefaultFilters);
            if (!found.HasValue || !float.IsFinite(found.Value.Z) || Math.Abs(found.Value.Z - point.Z) > zRange) return false;
            floor = new Point3D((int)Math.Round(found.Value.X), (int)Math.Round(found.Value.Y), (int)Math.Round(found.Value.Z));
            return true;
        }

        private static Point3D Nearest(Point3D[] points, Point3D origin) =>
            points == null || origin == null ? null : points.Where(point => point != null).OrderBy(point => OpenBattlegroundRoles.Distance2D(point, origin)).FirstOrDefault();

        // Callers hold Sync.
        private static void LogUnresolvedOnce(string key, string line)
        {
            if (UnresolvedLogged.Add(key)) Log.Warn(line);
        }
    }
}
