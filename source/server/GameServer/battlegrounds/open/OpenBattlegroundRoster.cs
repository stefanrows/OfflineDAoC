using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using DOL.GS.Keeps;
using DOL.GS.Scripts;
using DOL.GS.ServerRules;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Per-realm setup of the open BG crew and the hard cap over all realms (spec 2026-10-11 §5). <see cref="Groups8"/>,
    /// <see cref="GroupsSmall"/> and <see cref="Solos"/> are the global targets every realm uses unless it has its own
    /// (<see cref="Albion"/>, <see cref="Midgard"/>, <see cref="Hibernia"/>: the open_bg_units_* rows, owner decision
    /// 2026-10-11 14:50). The cap stays global.
    /// </summary>
    public sealed record OpenBgSetup(int Groups8, int GroupsSmall, int Solos, int MaxBots)
    {
        public OpenBgRealmTargets? Albion { get; init; }
        public OpenBgRealmTargets? Midgard { get; init; }
        public OpenBgRealmTargets? Hibernia { get; init; }

        /// <summary>The clamped server properties (open_bg_groups8, open_bg_groups_small, open_bg_solos, open_bg_max_bots and the realms' open_bg_units_*).</summary>
        public static OpenBgSetup FromProperties() =>
            new(OpenBattlegroundProperties.Groups8, OpenBattlegroundProperties.GroupsSmall, OpenBattlegroundProperties.Solos,
                OpenBattlegroundProperties.MaxBots)
            {
                Albion = OpenBattlegroundProperties.UnitsFor(eRealm.Albion),
                Midgard = OpenBattlegroundProperties.UnitsFor(eRealm.Midgard),
                Hibernia = OpenBattlegroundProperties.UnitsFor(eRealm.Hibernia),
            };

        /// <summary>A realm's own targets, or null when it uses the global ones.</summary>
        public OpenBgRealmTargets? OverrideFor(eRealm realm) => realm switch
        {
            eRealm.Albion => Albion,
            eRealm.Midgard => Midgard,
            eRealm.Hibernia => Hibernia,
            _ => null,
        };

        public bool IsOverridden(eRealm realm) => OverrideFor(realm) != null;

        /// <summary>The targets the roster plans a realm with: its own, else the global ones.</summary>
        public OpenBgRealmTargets TargetsFor(eRealm realm) =>
            OverrideFor(realm) ?? new OpenBgRealmTargets(Math.Max(0, Groups8), Math.Max(0, GroupsSmall), Math.Max(0, Solos));

        public int Groups8For(eRealm realm) => TargetsFor(realm).Groups8;
        public int SmallFor(eRealm realm) => TargetsFor(realm).Small;
        public int SolosFor(eRealm realm) => TargetsFor(realm).Solos;

        /// <summary>This setup with a realm's own targets set (null: back to the global ones).</summary>
        public OpenBgSetup WithOverride(eRealm realm, OpenBgRealmTargets? targets) => realm switch
        {
            eRealm.Albion => this with { Albion = targets },
            eRealm.Midgard => this with { Midgard = targets },
            eRealm.Hibernia => this with { Hibernia = targets },
            _ => this,
        };
    }

    /// <summary>
    /// One realm's crew targets: 8-man groups, small groups and solos, clamped like the global properties. The text
    /// form "8-man/small/solos" (e.g. "1/2/4") is what the open_bg_units_* rows, /bgsetup and the launcher store.
    /// </summary>
    public readonly record struct OpenBgRealmTargets(int Groups8, int Small, int Solos)
    {
        /// <summary>Each number inside its range (0-4, 0-6, 0-12).</summary>
        public static OpenBgRealmTargets Clamped(int groups8, int small, int solos) =>
            new(Math.Clamp(groups8, 0, OpenBattlegroundProperties.Groups8Max), Math.Clamp(small, 0, OpenBattlegroundProperties.GroupsSmallMax),
                Math.Clamp(solos, 0, OpenBattlegroundProperties.SolosMax));

        /// <summary>
        /// Three whole numbers separated by '/', each clamped to its range; false for empty or any other text (the realm
        /// then uses the global targets).
        /// </summary>
        public static bool TryParse(string text, out OpenBgRealmTargets targets)
        {
            targets = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Split('/');
            if (parts.Length != 3) return false;
            var values = new int[3];
            for (int index = 0; index < 3; index++)
                if (!int.TryParse(parts[index].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out values[index]))
                    return false;
            targets = Clamped(values[0], values[1], values[2]);
            return true;
        }

        /// <summary>The highest target of a kind (Groups8Max, GroupsSmallMax, SolosMax).</summary>
        public static int Max(OpenBgUnitKind kind) => kind switch
        {
            OpenBgUnitKind.Group8 => OpenBattlegroundProperties.Groups8Max,
            OpenBgUnitKind.Small => OpenBattlegroundProperties.GroupsSmallMax,
            _ => OpenBattlegroundProperties.SolosMax,
        };

        public int Count(OpenBgUnitKind kind) => kind switch
        {
            OpenBgUnitKind.Group8 => Groups8,
            OpenBgUnitKind.Small => Small,
            _ => Solos,
        };

        /// <summary>These targets with one kind set to <paramref name="value"/>, clamped.</summary>
        public OpenBgRealmTargets With(OpenBgUnitKind kind, int value) => kind switch
        {
            OpenBgUnitKind.Group8 => Clamped(value, Small, Solos),
            OpenBgUnitKind.Small => Clamped(Groups8, value, Solos),
            _ => Clamped(Groups8, Small, value),
        };

        /// <summary>The seats these targets book against the cap while planning (an 8-man 8, a small group 2, a solo 1).</summary>
        public int PlannedBots => Groups8 * OpenBattlegroundRoster.PlannedSize(OpenBgUnitKind.Group8) +
            Small * OpenBattlegroundRoster.PlannedSize(OpenBgUnitKind.Small) + Solos * OpenBattlegroundRoster.PlannedSize(OpenBgUnitKind.Solo);

        public string Format() => string.Create(CultureInfo.InvariantCulture, $"{Groups8}/{Small}/{Solos}");
    }

    public enum OpenBgUnitKind { Group8, Small, Solo }

    /// <summary>
    /// Why a level 40-50 world bot of a realm did not make the open BG crew's candidate pool, or why a pool candidate
    /// did not fit a group's leader (logged by <c>OPEN_BG_FORM_FAILED</c>).
    /// </summary>
    public enum OpenBgReject { KeepDefenseOrRaid, InGroup, Busy, Objective, Rest, Cooldown, FarFromPorter, OtherRegion, KeepOwnerSide, LevelBand }

    /// <summary>One roster unit of a realm: a group or a solo. Inside once a member has entered; leaving members are not part of it.</summary>
    public readonly record struct OpenBgUnit(int Id, OpenBgUnitKind Kind, int Size, bool Inside = false, long InsideSinceTick = 0);

    /// <summary>What a realm has in the open BG now. <paramref name="LeavingBots"/> still count against the cap, not against the setup.</summary>
    public sealed record RealmCount(eRealm Realm, IReadOnlyList<OpenBgUnit> Units, int LeavingBots = 0, bool ReliefDue = false)
    {
        public int Groups8 => Units.Count(unit => unit.Kind == OpenBgUnitKind.Group8);
        public int GroupsSmall => Units.Count(unit => unit.Kind == OpenBgUnitKind.Small);
        public int Solos => Units.Count(unit => unit.Kind == OpenBgUnitKind.Solo);
        public int Bots => Units.Sum(unit => unit.Size);
        public static RealmCount Empty(eRealm realm) => new(realm, Array.Empty<OpenBgUnit>());
    }

    public readonly record struct OpenBgAdmit(eRealm Realm, OpenBgUnitKind Kind);

    /// <summary>
    /// One realm's part of the plan: units to add and to relieve, and <paramref name="Refill"/>, the units still
    /// travelling that fell below their minimum and recruit replacements at their porter instead of being relieved.
    /// </summary>
    public sealed record OpenBgRealmPlan(eRealm Realm, int AddGroups8, int AddSmall, int AddSolos, IReadOnlyList<int> Relieve,
        IReadOnlyList<int> Refill = null);

    /// <summary>The roster's decision for one tick: relieved unit ids per realm and the admits in round-robin order.</summary>
    public sealed record OpenBgPlan(IReadOnlyList<OpenBgRealmPlan> Realms, IReadOnlyList<OpenBgAdmit> Admits, int Present, int Cap, bool CapBinds)
    {
        public OpenBgRealmPlan For(eRealm realm) => Realms.FirstOrDefault(plan => plan.Realm == realm);
    }

    /// <summary>One open-BG participation entry as the roster sees it (pure input for <see cref="OpenBattlegroundRoster.CountRealms"/>).</summary>
    public readonly record struct OpenRosterEntry(int Unit, eRealm Realm, OpenBgUnitKind Kind, BattlegroundDefinitionKind DefinitionKind,
        bool Entered, bool Leaving, long EnteredTick);

    /// <summary>
    /// The open BG's own bot crew (spec 2026-10-11 §5). Per realm it keeps the setup's 8-man groups, small groups
    /// (2-4) and solos from the 40-50 band of the autonomous world roster, never above the hard cap over all realms.
    /// Groups are formed before departure through the coordinator's RvR party path and muster at the medallion
    /// porter they board at; solos go alone. Every 20-30 game minutes one group or a quarter of the solos of a realm
    /// is relieved, walks to its portal keep and leaves; the roster refills from fresh candidates. Assignments live in
    /// memory only and never touch a bot's level, gear, guild or realm points. The bracket campaign is not touched:
    /// every entry the roster reads or writes has the Open definition and a roster unit id.
    /// </summary>
    public static class OpenBattlegroundRoster
    {
        public const int TickMilliseconds = 60_000;
        public const int LogIntervalMilliseconds = 5 * 60_000;
        // The crew level band (open_bg_band_min / open_bg_band_max, default 40-50), read live.
        public static int MinLevel => OpenBattlegroundProperties.BandMin;
        public static int MaxLevel => OpenBattlegroundProperties.BandMax;
        public const int Group8Size = 8;
        // An "8-man" short of candidates still forms with six; fewer waits for the next tick.
        public const int Group8Minimum = 6;
        public const int SmallGroupMin = 2;
        public const int SmallGroupMax = 4;
        public const int ReliefMinMinutes = 20;
        public const int ReliefMaxMinutes = 30;
        // A relieved bot rests before the roster may pick it again.
        public const long RestAfterReliefMilliseconds = 30 * 60_000L;
        // Group formation proves rendezvous slots on the navmesh; bound it per tick.
        public const int MaxGroupFormationsPerTick = 3;
        private const long SlowTickMilliseconds = 50;
        private static readonly eRealm[] RealmOrder = { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia };
        private static readonly Logger Log = LoggerManager.Create(typeof(OpenBattlegroundRoster));

        // ---- pure rules ------------------------------------------------------------------------------------------

        /// <summary>Seats an admit of this kind books against the cap while planning. A small group books its minimum;
        /// the runtime rolls its real size and never forms above the cap left.</summary>
        public static int PlannedSize(OpenBgUnitKind kind) => kind switch
        {
            OpenBgUnitKind.Group8 => Group8Size,
            OpenBgUnitKind.Small => SmallGroupMin,
            _ => 1,
        };

        /// <summary>The fewest members a unit of this kind fights with: six for an 8-man, two for a small group, one solo.</summary>
        public static int MinimumSize(OpenBgUnitKind kind) => kind switch
        {
            OpenBgUnitKind.Group8 => Group8Minimum,
            OpenBgUnitKind.Small => SmallGroupMin,
            _ => 1,
        };

        /// <summary>
        /// A group that lost members below its kind's minimum (an 8-man of three, a small group of one) no longer
        /// counts as that unit. An 8-man formed with six is whole.
        /// </summary>
        public static bool IsBelowMinimum(OpenBgUnit unit) => unit.Size < MinimumSize(unit.Kind);

        /// <summary>
        /// A unit inside below its minimum is relieved and replaced. A unit that never entered is refilled at its porter
        /// instead (live run 0.242.0: relieving it at once, median 18 s after forming, churned new units and stragglers).
        /// </summary>
        public static bool RelievedBelowMinimum(OpenBgUnit unit) => unit.Inside && IsBelowMinimum(unit);

        /// <summary>A unit still travelling below its minimum keeps its seat and recruits replacements at its porter.</summary>
        public static bool RefilledBelowMinimum(OpenBgUnit unit) => !unit.Inside && IsBelowMinimum(unit);

        /// <summary>
        /// Per realm: relieve one unit when its relief is due (the longest-serving group, or a quarter of the solos
        /// when a solo serves longest), relieve units above a lowered setup, relieve units above a lowered cap until the
        /// bots that stay fit under it, then add missing units in realm round-robin order (fewest bots first) while they
        /// fit under the cap. A group inside below its minimum (<see cref="RelievedBelowMinimum"/>) is relieved and
        /// counts as missing, so a new one is formed; a group still travelling below its minimum
        /// (<see cref="RefilledBelowMinimum"/>) is refilled instead, and its missing members book their seats before
        /// any new unit. Never throws for a cap below the setup. Each realm is planned with its own targets
        /// (<see cref="OpenBgSetup.TargetsFor"/>). <paramref name="forced"/> are units the player relieved by hand
        /// (<see cref="RequestRelief"/>): they go first, and the plan forms a replacement when the targets still want one.
        /// </summary>
        public static OpenBgPlan Plan(OpenBgSetup setup, IReadOnlyList<RealmCount> present, IReadOnlyCollection<int> forced = null)
        {
            setup ??= new OpenBgSetup(0, 0, 0, 0);
            present ??= Array.Empty<RealmCount>();
            forced ??= Array.Empty<int>();
            int cap = Math.Max(0, setup.MaxBots);
            int total = present.Sum(realm => realm.Bots + Math.Max(0, realm.LeavingBots));
            var needs = new Dictionary<eRealm, int[]>();
            var relieved = new Dictionary<eRealm, List<int>>();
            foreach (RealmCount realm in present)
            {
                OpenBgRealmTargets targets = setup.TargetsFor(realm.Realm);
                // A unit the player relieved by hand goes first.
                var relieve = realm.Units.Where(unit => forced.Contains(unit.Id)).Select(unit => unit.Id).ToList();
                // Counted by members, not units: a group shrunk below its minimum inside is replaced.
                relieve.AddRange(realm.Units.Where(unit => RelievedBelowMinimum(unit) && !relieve.Contains(unit.Id)).Select(unit => unit.Id));
                if (realm.ReliefDue) relieve.AddRange(ReliefPick(realm.Units.Where(unit => !relieve.Contains(unit.Id)).ToArray()));
                relieve.AddRange(SurplusPick(realm.Units, OpenBgUnitKind.Group8, targets.Groups8, relieve));
                relieve.AddRange(SurplusPick(realm.Units, OpenBgUnitKind.Small, targets.Small, relieve));
                relieve.AddRange(SurplusPick(realm.Units, OpenBgUnitKind.Solo, targets.Solos, relieve));
                relieved[realm.Realm] = relieve;
                int Remaining(OpenBgUnitKind kind, int wanted) =>
                    Math.Max(0, Math.Max(0, wanted) - realm.Units.Count(unit => unit.Kind == kind && !relieve.Contains(unit.Id)));
                needs[realm.Realm] = new[]
                {
                    Remaining(OpenBgUnitKind.Group8, targets.Groups8),
                    Remaining(OpenBgUnitKind.Small, targets.Small),
                    Remaining(OpenBgUnitKind.Solo, targets.Solos),
                };
            }
            CapSurplusPick(present, cap, relieved);
            var refills = present.ToDictionary(realm => realm.Realm, realm => (IReadOnlyList<int>)realm.Units
                .Where(unit => RefilledBelowMinimum(unit) && !relieved[realm.Realm].Contains(unit.Id))
                .Select(unit => unit.Id).ToArray());

            // Relieved bots stay until they have walked out, so they still hold their seats this tick. A refill's
            // missing members are booked before any new unit.
            int free = cap - total - present.Sum(realm => realm.Units.Where(unit => refills[realm.Realm].Contains(unit.Id))
                .Sum(unit => MinimumSize(unit.Kind) - unit.Size));
            var admits = new List<OpenBgAdmit>();
            var added = present.ToDictionary(realm => realm.Realm, _ => new int[3]);
            eRealm[] order = present.OrderBy(realm => realm.Bots + Math.Max(0, realm.LeavingBots))
                .ThenBy(realm => Array.IndexOf(RealmOrder, realm.Realm) is int index && index >= 0 ? index : int.MaxValue)
                .Select(realm => realm.Realm).ToArray();
            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (eRealm realm in order)
                {
                    int[] need = needs[realm];
                    for (int kind = 0; kind < need.Length; kind++)
                    {
                        if (need[kind] <= 0 || PlannedSize((OpenBgUnitKind)kind) > free) continue;
                        need[kind]--;
                        added[realm][kind]++;
                        free -= PlannedSize((OpenBgUnitKind)kind);
                        admits.Add(new OpenBgAdmit(realm, (OpenBgUnitKind)kind));
                        progress = true;
                        break;
                    }
                }
            }
            bool capBinds = needs.Values.Any(need => need.Any(count => count > 0));
            OpenBgRealmPlan[] realms = present.Select(realm => new OpenBgRealmPlan(realm.Realm, added[realm.Realm][0],
                added[realm.Realm][1], added[realm.Realm][2], relieved[realm.Realm], refills[realm.Realm])).ToArray();
            return new OpenBgPlan(realms, admits, total, cap, capBinds);
        }

        // The longest-serving unit inside goes: a group whole, or a quarter of the solos (at least one), oldest first.
        private static IEnumerable<int> ReliefPick(IReadOnlyList<OpenBgUnit> units)
        {
            OpenBgUnit[] inside = units.Where(unit => unit.Inside).OrderBy(unit => unit.InsideSinceTick).ThenBy(unit => unit.Id).ToArray();
            if (inside.Length == 0) return Array.Empty<int>();
            if (inside[0].Kind != OpenBgUnitKind.Solo) return new[] { inside[0].Id };
            int solos = units.Count(unit => unit.Kind == OpenBgUnitKind.Solo);
            int count = Math.Max(1, (int)Math.Ceiling(solos * 0.25));
            return inside.Where(unit => unit.Kind == OpenBgUnitKind.Solo).Take(count).Select(unit => unit.Id).ToArray();
        }

        // A lowered setup: the surplus goes, travelling units first, then the most recently entered.
        private static IEnumerable<int> SurplusPick(IReadOnlyList<OpenBgUnit> units, OpenBgUnitKind kind, int wanted, List<int> already)
        {
            OpenBgUnit[] kept = units.Where(unit => unit.Kind == kind && !already.Contains(unit.Id)).ToArray();
            int surplus = kept.Length - Math.Max(0, wanted);
            if (surplus <= 0) return Array.Empty<int>();
            return kept.OrderBy(unit => unit.Inside ? 1 : 0).ThenByDescending(unit => unit.InsideSinceTick).ThenByDescending(unit => unit.Id)
                .Take(surplus).Select(unit => unit.Id).ToArray();
        }

        // A lowered cap: while the bots that stay (neither relieved nor already leaving) exceed it, one more unit goes,
        // taken from the realm that keeps the most bots so the shed is spread over the realms; within that realm
        // travelling units first, then the most recently entered.
        private static void CapSurplusPick(IReadOnlyList<RealmCount> present, int cap, Dictionary<eRealm, List<int>> relieved)
        {
            int Kept(RealmCount realm) => realm.Units.Where(unit => !relieved[realm.Realm].Contains(unit.Id)).Sum(unit => unit.Size);
            while (present.Sum(Kept) > cap)
            {
                RealmCount realm = present.Where(count => Kept(count) > 0).OrderByDescending(Kept)
                    .ThenBy(count => Array.IndexOf(RealmOrder, count.Realm) is int index && index >= 0 ? index : int.MaxValue)
                    .FirstOrDefault();
                if (realm == null) return;
                OpenBgUnit unit = realm.Units.Where(candidate => !relieved[realm.Realm].Contains(candidate.Id))
                    .OrderBy(candidate => candidate.Inside ? 1 : 0).ThenByDescending(candidate => candidate.InsideSinceTick)
                    .ThenByDescending(candidate => candidate.Id).First();
                relieved[realm.Realm].Add(unit.Id);
            }
        }

        /// <summary>Realm counts from participation entries. Bracket entries and entries without a roster unit are ignored.</summary>
        public static IReadOnlyList<RealmCount> CountRealms(IEnumerable<OpenRosterEntry> entries, Func<eRealm, bool> reliefDue = null)
        {
            OpenRosterEntry[] open = (entries ?? Array.Empty<OpenRosterEntry>())
                .Where(entry => entry.DefinitionKind == BattlegroundDefinitionKind.Open && entry.Unit > 0).ToArray();
            var result = new List<RealmCount>();
            foreach (eRealm realm in RealmOrder)
            {
                OpenRosterEntry[] mine = open.Where(entry => entry.Realm == realm).ToArray();
                OpenBgUnit[] units = mine.Where(entry => !entry.Leaving).GroupBy(entry => entry.Unit)
                    .Select(unit =>
                    {
                        OpenRosterEntry[] inside = unit.Where(entry => entry.Entered).ToArray();
                        return new OpenBgUnit(unit.Key, unit.First().Kind, unit.Count(), inside.Length > 0,
                            inside.Length > 0 ? inside.Min(entry => entry.EnteredTick) : 0);
                    })
                    .OrderBy(unit => unit.Id).ToArray();
                result.Add(new RealmCount(realm, units, mine.Count(entry => entry.Leaving), reliefDue?.Invoke(realm) == true));
            }
            return result;
        }

        /// <summary>
        /// A crew candidate: an autonomous world bot inside the crew level band (default 40-50), not in a player-led group, carrying no relic, in no
        /// battleground, on a home frontier region (1, 100, 200), and either on an RvR tour or idle between tasks.
        /// </summary>
        public static bool IsCandidate(bool autonomousWorldBot, int level, bool playerLed, bool relic, bool inBattleground,
            ushort region, bool rvrTour, bool idle) =>
            autonomousWorldBot && level >= MinLevel && level <= MaxLevel && !playerLed && !relic && !inBattleground &&
            AutonomousBattlegroundParticipation.IsFrontierRegion(region) && CandidateRank(rvrTour, idle) >= 0;

        /// <summary>0 for a bot on an RvR tour, 1 for an idle bot, -1 for anything else (busy with PvE work).</summary>
        public static int CandidateRank(bool rvrTour, bool idle) => rvrTour ? 0 : idle ? 1 : -1;

        public static bool IsCandidate(GameBot bot) =>
            bot != null && bot.ObjectState == GameObject.eObjectState.Active && bot.IsAlive &&
            IsCandidate(bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper && !bot.IsPersistentPlayerCompanion,
                bot.Level, bot.IsPlayerLedGroup, GameRelic.IsPlayerCarryingRelic(bot),
                AutonomousBattlegroundParticipation.IsInAnyBattleground(bot), bot.CurrentRegionID,
                AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR), IsIdle(bot)) &&
            IsRecruitableNow(bot.IsAlive, bot.IsReturningAfterRelease, bot.CurrentZone?.IsDungeon == true,
                AutonomousActivityScheduler.IsPveBlocked(bot.PersistentRecord, WorldSimulationClock.UtcNow),
                bot.InCombat || bot.IsAttacking, bot.IsOnStableMasterRoute) &&
            !AutonomousRealmRaid.IsReserved(bot) && !AutonomousGuildKeepDefense.IsRecalled(bot);

        /// <summary>
        /// Live run 0.242.0: the roster recruited bots that could not reach the porter within the boarding wait. A
        /// candidate must be ready to walk there now: alive and not walking back after a release, outside any dungeon,
        /// not in a fight, not on a stable ride, and not behind the PvP wall (three PvP deaths: the coordinator takes
        /// such a bot out of every RvR party at once, which cost a unit its leader one second after forming).
        /// </summary>
        public static bool IsRecruitableNow(bool alive, bool returningAfterRelease, bool inDungeon, bool pvpWall, bool fighting,
            bool onStableRoute) =>
            alive && !returningAfterRelease && !inDungeon && !pvpWall && !fighting && !onStableRoute;

        /// <summary>Recruits stand within this distance of a medallion porter of their region (live run 0.242.0: up to 47,000 away).</summary>
        public const int PorterReach = 20_000;

        /// <summary>Whether a candidate stands near enough to its nearest medallion porter to be recruited.</summary>
        public static bool WithinPorterReach(double distanceToPorter) => distanceToPorter >= 0 && distanceToPorter <= PorterReach;

        /// <summary>
        /// A bot that left its unit before boarding (a straggler drop, a timeout, another party) is not recruited again
        /// for this long, in game time: Isaiselle was dropped and re-recruited four times within three minutes.
        /// </summary>
        public const long RecruitCooldownMilliseconds = 10 * 60_000L;

        /// <summary>The later of a running rest and a new one starting now; a rest never shrinks.</summary>
        public static long ExtendRest(long restingUntil, long now, long restMilliseconds) => Math.Max(restingUntil, now + restMilliseconds);

        /// <summary>
        /// The rest a relieved unit's members get before the roster may pick them again. A unit relieved because it fell
        /// below its minimum (stragglers, a keep-owner split) served for seconds; its members get only the
        /// <see cref="RecruitCooldownMilliseconds"/> so they can re-form (live run 0.246.0: the 30-minute rest after such
        /// reliefs drained the pool). A due, surplus or cap relief keeps the <see cref="RestAfterReliefMilliseconds"/>.
        /// </summary>
        public static long RestAfterRelief(bool belowMinimum) => belowMinimum ? RecruitCooldownMilliseconds : RestAfterReliefMilliseconds;

        /// <summary>
        /// The first reason a level 40-50 world bot on a home frontier is not a crew candidate, in this order: held for a
        /// guild keep defense or a realm raid, in a group, busy (dead, fighting, walking back, in a dungeon, on a stable
        /// ride, behind the PvP wall, carrying a relic), on PvE work, resting after a relief or cooling down after a drop,
        /// too far from a medallion porter. Null for a candidate.
        /// </summary>
        public static OpenBgReject? CandidateRejection(bool keepDefenseOrRaid, bool inGroup, bool busy, bool rvrTour, bool idle,
            bool resting, bool restAfterRelief, bool nearPorter)
        {
            if (keepDefenseOrRaid) return OpenBgReject.KeepDefenseOrRaid;
            if (inGroup) return OpenBgReject.InGroup;
            if (busy) return OpenBgReject.Busy;
            if (CandidateRank(rvrTour, idle) < 0) return OpenBgReject.Objective;
            if (resting) return restAfterRelief ? OpenBgReject.Rest : OpenBgReject.Cooldown;
            if (!nearPorter) return OpenBgReject.FarFromPorter;
            return null;
        }

        /// <summary>
        /// Why a pool candidate cannot join a group leader: another frontier region, the other side of the keep's owner
        /// guild, a level the leader's group does not take, or too far from the leader's porter. Null for a fit.
        /// </summary>
        public static OpenBgReject? MateRejection(bool sameRegion, bool sameKeepSide, bool levelsCompatible, bool withinPorterReach)
        {
            if (!sameRegion) return OpenBgReject.OtherRegion;
            if (!sameKeepSide) return OpenBgReject.KeepOwnerSide;
            if (!levelsCompatible) return OpenBgReject.LevelBand;
            if (!withinPorterReach) return OpenBgReject.FarFromPorter;
            return null;
        }

        /// <summary>
        /// The rejections that explain a group that could not form: with fewer RvR candidates in the pool than the group
        /// needs, the realm's pool rejections plus the idle pool bots (not on an RvR tour, so no group member); with
        /// enough candidates, why they did not fit the leader that came closest.
        /// </summary>
        public static IReadOnlyDictionary<OpenBgReject, int> FormFailureRejections(int rvrPool, int need, int idleInPool,
            IReadOnlyDictionary<OpenBgReject, int> poolStage, IReadOnlyDictionary<OpenBgReject, int> mateStage)
        {
            if (rvrPool >= need && mateStage != null && mateStage.Values.Any(count => count > 0)) return mateStage;
            var result = new Dictionary<OpenBgReject, int>();
            foreach (KeyValuePair<OpenBgReject, int> pair in poolStage ?? new Dictionary<OpenBgReject, int>())
                if (pair.Value > 0) result[pair.Key] = pair.Value;
            if (idleInPool > 0) result[OpenBgReject.Objective] = result.GetValueOrDefault(OpenBgReject.Objective) + idleInPool;
            return result;
        }

        /// <summary>The most frequent rejection (ties: the earlier reason), or null when nothing was rejected.</summary>
        public static OpenBgReject? DominantRejection(IReadOnlyDictionary<OpenBgReject, int> counts) =>
            (counts ?? new Dictionary<OpenBgReject, int>()).Where(pair => pair.Value > 0)
                .OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key)
                .Select(pair => (OpenBgReject?)pair.Key).FirstOrDefault();

        /// <summary>The log name of a rejection.</summary>
        public static string RejectionName(OpenBgReject? reject) => reject switch
        {
            OpenBgReject.KeepDefenseOrRaid => "keep_defense_or_raid",
            OpenBgReject.InGroup => "in_group",
            OpenBgReject.Busy => "busy",
            OpenBgReject.Objective => "objective",
            OpenBgReject.Rest => "rest",
            OpenBgReject.Cooldown => "cooldown",
            OpenBgReject.FarFromPorter => "far_from_porter",
            OpenBgReject.OtherRegion => "other_region",
            OpenBgReject.KeepOwnerSide => "keep_owner_side",
            OpenBgReject.LevelBand => "level_band",
            _ => "none",
        };

        /// <summary>
        /// The <c>OPEN_BG_FORM_FAILED</c> line: a realm's group of this kind could not form; <paramref name="need"/> is its
        /// minimum, <paramref name="pool"/> the RvR candidates left in the realm's pool, then the dominant rejection, all
        /// of them, and how many further failures since the last line it stands for.
        /// </summary>
        public static string FormFailedLine(eRealm realm, OpenBgUnitKind kind, int need, int pool, IReadOnlyDictionary<OpenBgReject, int> rejections,
            int repeats)
        {
            string all = string.Join(",", (rejections ?? new Dictionary<OpenBgReject, int>()).Where(pair => pair.Value > 0)
                .OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).Select(pair => $"{RejectionName(pair.Key)}:{pair.Value}"));
            return $"OPEN_BG_FORM_FAILED realm={RealmName(realm)} kind={Describe(kind)} need={need} pool={pool} " +
                $"reason={RejectionName(DominantRejection(rejections))} rejected=\"{all}\" repeats={repeats}";
        }

        /// <summary>The fewest game milliseconds between two <c>OPEN_BG_FORM_FAILED</c> lines of one realm and kind.</summary>
        public const long FormFailedLogMilliseconds = 5 * 60_000L;

        /// <summary>Whether a bot still rests (game time) and may not be recruited.</summary>
        public static bool IsResting(long restingUntil, long now) => now < restingUntil;

        /// <summary>
        /// Camlann keeps are friendly by guild (and its alliance), and the keep's friend check reads the whole group: one
        /// member of the owning guild makes every unit-mate a keep friend (live run 0.243.0: the realmless crew guild
        /// "Bandit Farmers" held keep 11 and had members in the attacking units of every realm, so they defended instead
        /// of assaulting). Whether a guild is the keep's owner or allied to it; false while no guild owns the keep.
        /// </summary>
        public static bool IsKeepOwnerGuild(string guildId, string allianceId, string keepOwnerGuildId, string keepOwnerAllianceId) =>
            !string.IsNullOrEmpty(keepOwnerGuildId) && !string.IsNullOrEmpty(guildId) &&
            (string.Equals(guildId, keepOwnerGuildId, StringComparison.OrdinalIgnoreCase) ||
             !string.IsNullOrEmpty(keepOwnerAllianceId) && string.Equals(allianceId, keepOwnerAllianceId, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// A candidate joins a unit only on the same side of the keep's owner guild as the unit's leader: owner-guild bots
        /// form their own units (the keep counts them as friends, so they defend it), everyone else never shares a group
        /// with them. Any candidate may join any unit while no guild owns the keep.
        /// </summary>
        public static bool MayJoinUnit(string candidateGuildId, string candidateAllianceId, string unitLeaderGuildId, string unitLeaderAllianceId,
            string keepOwnerGuildId, string keepOwnerAllianceId) =>
            IsKeepOwnerGuild(candidateGuildId, candidateAllianceId, keepOwnerGuildId, keepOwnerAllianceId) ==
            IsKeepOwnerGuild(unitLeaderGuildId, unitLeaderAllianceId, keepOwnerGuildId, keepOwnerAllianceId);

        /// <summary>The keep's owning guild and its alliance (empty while no guild owns it), and a member's guild ids.</summary>
        public readonly record struct GuildIds(string GuildId, string AllianceId);

        /// <summary>The candidates that may join the anchor's unit (<see cref="MayJoinUnit"/>), in their order.</summary>
        public static IEnumerable<T> UnitMates<T>(T anchor, IEnumerable<T> candidates, Func<T, GuildIds> guildOf, GuildIds keepOwner)
        {
            GuildIds lead = guildOf(anchor);
            return (candidates ?? Array.Empty<T>()).Where(candidate =>
            {
                GuildIds mine = guildOf(candidate);
                return MayJoinUnit(mine.GuildId, mine.AllianceId, lead.GuildId, lead.AllianceId, keepOwner.GuildId, keepOwner.AllianceId);
            });
        }

        /// <summary>
        /// The members a unit loses when the keep's owner changes under it: in a unit that mixes owner-guild members
        /// with others, the owner-guild members; nobody in a unit all of one side.
        /// </summary>
        public static T[] SplitForKeepOwner<T>(IReadOnlyList<T> members, Func<T, GuildIds> guildOf, GuildIds keepOwner)
        {
            if (members == null || members.Count < 2) return Array.Empty<T>();
            T[] owners = members.Where(member => guildOf(member) is GuildIds ids &&
                IsKeepOwnerGuild(ids.GuildId, ids.AllianceId, keepOwner.GuildId, keepOwner.AllianceId)).ToArray();
            return owners.Length > 0 && owners.Length < members.Count ? owners : Array.Empty<T>();
        }

        // Idle: alone and without a running PvE task (between tasks, or none assigned yet).
        private static bool IsIdle(GameBot bot) =>
            bot.Group == null && bot.PersistentRecord != null && !AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR) &&
            (AutonomousObjectiveAssignments.IsBetweenPveTasks(bot) || string.IsNullOrWhiteSpace(bot.PersistentRecord.ObjectiveAssignmentId));

        /// <summary>The size of a small group: 2, 3 or 4.</summary>
        public static int SmallGroupSize(Random random) => (random ?? Random.Shared).Next(SmallGroupMin, SmallGroupMax + 1);

        /// <summary>The relief delay of one draw, in game milliseconds: between the two bounds, fixed by the seed.</summary>
        public static long ReliefDelayMilliseconds(int minMinutes, int maxMinutes, int seed)
        {
            long low = Math.Max(0, Math.Min(minMinutes, maxMinutes)) * 60_000L;
            long high = Math.Max(0, Math.Max(minMinutes, maxMinutes)) * 60_000L;
            uint hash = unchecked((uint)seed * 2654435761u);
            hash ^= hash >> 16;
            hash = unchecked(hash * 2246822519u);
            hash ^= hash >> 13;
            return low + (high == low ? 0 : hash % (high - low + 1));
        }

        /// <summary>Whether a relief is due, in game time (<see cref="GameLoop.GameLoopTime"/>): 20-30 min scale with the world speed.</summary>
        public static bool DueForRelief(long assignedTick, long now, int minMin, int maxMin, int seed) =>
            now - assignedTick >= ReliefDelayMilliseconds(minMin, maxMin, seed);

        // ---- runtime -------------------------------------------------------------------------------------------

        private sealed class UnitInfo
        {
            public int Id;
            public eRealm Realm;
            public OpenBgUnitKind Kind;
            public Group Group;
            // The medallion porter the group musters and boards at, and the size it formed with (its refill target).
            public OFTeleporter Porter;
            public int FormedSize;
        }

        private sealed class ReliefClock
        {
            public long LastTick = -1;
            public int Seed;
        }

        private static readonly object Sync = new();
        private static readonly Dictionary<int, UnitInfo> Units = new();
        private static readonly Dictionary<eRealm, ReliefClock> Clocks = new();
        private static readonly Dictionary<GameBot, long> RestingUntil = new(ReferenceEqualityComparer.Instance);
        // Bots whose running rest is the long one after a relief (not a drop or below-minimum cooldown); only logged.
        private static readonly HashSet<GameBot> RestIsRelief = new(ReferenceEqualityComparer.Instance);
        private static readonly Dictionary<(eRealm, OpenBgUnitKind), (long NextTick, int Repeats)> FormFailedLog = new();
        // The last reason a unit of a realm and kind could not form, for the open BG window; cleared once one forms.
        private static readonly Dictionary<(eRealm, OpenBgUnitKind), (string Text, long Tick)> LastFormFailure = new();
        // Units the player relieved by hand in the open BG window or with /bgsetup relieve; the next tick relieves them first.
        private static readonly HashSet<int> PendingRelief = new();
        private static readonly Random Rng = new();
        private static int _nextUnit;
        private static long _nextLogTick;
        // Medallion porters per region. They are static spawns and the merchant scan walks every NPC of the
        // region, so the roster reads them at most once per five game minutes.
        private const long PorterCacheMilliseconds = 5 * 60_000L;
        private static readonly Dictionary<ushort, (long Tick, OFTeleporter[] Porters)> PorterCache = new();

        /// <summary>Called by the campaign manager every <see cref="TickMilliseconds"/> of game time for the Open definition only.</summary>
        public static void Tick(BattlegroundDefinition definition, long now)
        {
            if (definition?.Kind != BattlegroundDefinitionKind.Open) return;
            if (!OpenBattlegroundPolicy.IsEnabled)
            {
                // Participation sends every open entry out on its own pass; the roster forgets its units.
                Reset();
                return;
            }
            // Gothwaite PvP mode: no bot crew is formed, so the roster keeps no units (as when the open BG is off).
            if (GothwaitePvpRules.SuppressesWorldRecruitment)
            {
                Reset();
                return;
            }
            Stopwatch watch = Stopwatch.StartNew();
            OpenBgSetup setup = OpenBgSetup.FromProperties();
            AutonomousBattlegroundParticipation.OpenCrewMember[] crew = AutonomousBattlegroundParticipation.OpenCrew();
            GuildIds keepOwner = KeepOwnerOf(definition);
            // A unit the keep's owner changed under keeps no owner-guild member next to others; the split shows in the counts.
            if (SplitKeepOwnerUnits(crew, keepOwner, now) > 0) crew = AutonomousBattlegroundParticipation.OpenCrew();
            IReadOnlyList<RealmCount> counts;
            Group[] released;
            lock (Sync)
            {
                released = ForgetEmptyUnits(crew);
                foreach (AutonomousBattlegroundParticipation.OpenCrewMember member in crew)
                    if (!Units.ContainsKey(member.Unit))
                        Units[member.Unit] = new UnitInfo { Id = member.Unit, Realm = member.Bot.Realm, Kind = OpenBgUnitKind.Solo };
                counts = CountRealms(crew.Select(member => new OpenRosterEntry(member.Unit, Units[member.Unit].Realm,
                    Units[member.Unit].Kind, BattlegroundDefinitionKind.Open, member.Entered, member.Leaving, member.EnteredTick)),
                    realm => ReliefDue(realm, crew, now));
                foreach (GameBot bot in RestingUntil.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
                {
                    RestingUntil.Remove(bot);
                    RestIsRelief.Remove(bot);
                }
            }
            foreach (Group group in released)
                AutonomousBotGroupCoordinator.ReleaseOpenBattlegroundParty(group);

            int[] forced;
            lock (Sync)
            {
                forced = PendingRelief.ToArray();
                PendingRelief.Clear();
            }
            OpenBgPlan plan = Plan(setup, counts, forced);
            var belowMinimum = counts.SelectMany(count => count.Units).Where(RelievedBelowMinimum).Select(unit => unit.Id).ToHashSet();
            foreach (OpenBgRealmPlan realm in plan.Realms)
            {
                if (realm.Relieve.Count == 0) continue;
                foreach (int unit in realm.Relieve)
                {
                    bool manual = forced.Contains(unit);
                    bool shrunk = belowMinimum.Contains(unit);
                    // A hand relief gets the short cooldown, so its bots may come back soon.
                    long rest = RestAfterRelief(shrunk, manual);
                    GameBot[] relieved = AutonomousBattlegroundParticipation.RelieveOpenUnit(unit, now);
                    foreach (GameBot bot in relieved)
                        StartRest(bot, now, rest);
                    if (relieved.Length > 0)
                        Log.Info($"OPEN_BG_UNIT_RELIEVED realm={RealmName(realm.Realm)} unit={unit} size={relieved.Length} " +
                            $"reason={ReliefReason(manual, shrunk)} rest_min={rest / 60_000}");
                }
                // Only a due relief restarts the realm's clock; a lowered setup's surplus does not.
                if (counts.FirstOrDefault(count => count.Realm == realm.Realm)?.ReliefDue != true) continue;
                lock (Sync)
                {
                    ReliefClock clock = ClockOf(realm.Realm);
                    clock.LastTick = now;
                    clock.Seed++;
                }
            }
            bool refilling = plan.Realms.Any(realm => realm.Refill?.Count > 0);
            Dictionary<eRealm, Dictionary<OpenBgReject, int>> poolRejections = null;
            // A preset unit in Server PvP draws on all three realms (merged pool), so every realm's pool is built.
            eRealm[] poolRealms = OpenBattlegroundMuster.PoolsMerged(OpenBattlegroundMode.Current, OpenBattlegroundProperties.Presets.Length > 0)
                ? new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia } : Array.Empty<eRealm>();
            Dictionary<eRealm, List<GameBot>> pools = plan.Admits.Count > 0 || refilling
                ? CandidatePools(plan.Admits.Select(admit => admit.Realm)
                    .Concat(plan.Realms.Where(realm => realm.Refill?.Count > 0).Select(realm => realm.Realm)).Concat(poolRealms).Distinct().ToArray(), now,
                    out poolRejections)
                : null;
            if (refilling) RefillUnits(plan, setup, crew, pools, keepOwner, now);
            int formed = plan.Admits.Count > 0 ? FormUnits(plan, setup, now, pools, poolRejections, keepOwner) : 0;

            if (now >= _nextLogTick)
            {
                _nextLogTick = now + LogIntervalMilliseconds;
                LogRoster(setup, counts, plan);
            }
            if (watch.ElapsedMilliseconds > SlowTickMilliseconds)
                Log.Warn($"OPEN_BG_TICK_SLOW part=roster ms={watch.ElapsedMilliseconds} crew={crew.Length} admits={plan.Admits.Count} formed={formed}");
        }

        /// <summary>The kind a roster unit was formed as, or null for a unit the roster does not know (yet).</summary>
        public static OpenBgUnitKind? KindOf(int unit)
        {
            lock (Sync)
                return Units.TryGetValue(unit, out UnitInfo info) ? info.Kind : null;
        }

        /// <summary>Drops every unit and relief clock (open BG switched off or the server stopped).</summary>
        public static void Reset()
        {
            Group[] groups;
            lock (Sync)
            {
                groups = Units.Values.Select(unit => unit.Group).Where(group => group != null).ToArray();
                Units.Clear();
                Clocks.Clear();
                RestingUntil.Clear();
                RestIsRelief.Clear();
                FormFailedLog.Clear();
                LastFormFailure.Clear();
                PendingRelief.Clear();
                PorterCache.Clear();
                _nextLogTick = 0;
            }
            foreach (Group group in groups)
                AutonomousBotGroupCoordinator.ReleaseOpenBattlegroundParty(group);
        }

        // Callers hold Sync. Units with no entry left are gone; their groups may recruit again.
        private static Group[] ForgetEmptyUnits(AutonomousBattlegroundParticipation.OpenCrewMember[] crew)
        {
            var live = crew.Select(member => member.Unit).ToHashSet();
            UnitInfo[] gone = Units.Values.Where(unit => !live.Contains(unit.Id)).ToArray();
            foreach (UnitInfo unit in gone) Units.Remove(unit.Id);
            return gone.Select(unit => unit.Group).Where(group => group != null).ToArray();
        }

        // Callers hold Sync. A realm's clock starts when its first unit is inside and restarts at every relief.
        private static bool ReliefDue(eRealm realm, AutonomousBattlegroundParticipation.OpenCrewMember[] crew, long now)
        {
            ReliefClock clock = ClockOf(realm);
            if (clock.LastTick < 0)
            {
                if (crew.Any(member => member.Entered && !member.Leaving && Units.TryGetValue(member.Unit, out UnitInfo unit) && unit.Realm == realm))
                    clock.LastTick = now;
                return false;
            }
            return DueForRelief(clock.LastTick, now, ReliefMinMinutes, ReliefMaxMinutes, clock.Seed * 3 + (int)realm);
        }

        private static ReliefClock ClockOf(eRealm realm)
        {
            if (!Clocks.TryGetValue(realm, out ReliefClock clock)) Clocks[realm] = clock = new ReliefClock();
            return clock;
        }

        /// <summary>Forms the plan's admits in its order. Never above the cap: every unit's real size is checked against the seats left.</summary>
        private static int FormUnits(OpenBgPlan plan, OpenBgSetup setup, long now, Dictionary<eRealm, List<GameBot>> pools,
            Dictionary<eRealm, Dictionary<OpenBgReject, int>> poolRejections, GuildIds keepOwner)
        {
            int free = Math.Max(0, setup.MaxBots) - AutonomousBattlegroundParticipation.OpenCrewCount();
            if (free <= 0) return 0;
            int groupsFormed = 0, formed = 0;
            foreach (OpenBgAdmit admit in plan.Admits)
            {
                if (free <= 0) break;
                List<GameBot> pool = pools[admit.Realm];
                if (admit.Kind == OpenBgUnitKind.Solo)
                {
                    GameBot solo = pool.FirstOrDefault();
                    if (solo == null)
                    {
                        NoteFormFailed(admit, 1, pool, poolRejections?.GetValueOrDefault(admit.Realm), null, now, log: false);
                        continue;
                    }
                    pool.Remove(solo);
                    if (Register(admit, new[] { solo }, null, "solo", now)) { free--; formed++; }
                    continue;
                }
                if (groupsFormed >= MaxGroupFormationsPerTick) continue;
                int size = admit.Kind == OpenBgUnitKind.Group8 ? Math.Min(Group8Size, free) : Math.Min(SmallGroupSize(Rng), free);
                int minimum = admit.Kind == OpenBgUnitKind.Group8 ? Group8Minimum : SmallGroupMin;
                if (size < minimum) continue;
                // Merged pool (preset unit of this size, Server PvP): the admit's realm first, then the other realms.
                bool merged = OpenBattlegroundMuster.PoolsMerged(OpenBattlegroundMode.Current, PresetFor(size) != null);
                List<GameBot> pickFrom = merged
                    ? pool.Concat(pools.Where(entry => entry.Key != admit.Realm).SelectMany(entry => entry.Value)).ToList()
                    : pool;
                if (!TryPickGroup(pickFrom, size, minimum, now, keepOwner, merged, admit.Realm, out GameBot leader, out GameBot[] others, out OFTeleporter porter,
                        out Dictionary<OpenBgReject, int> mateRejections))
                {
                    NoteFormFailed(admit, minimum, pool, poolRejections?.GetValueOrDefault(admit.Realm), mateRejections, now);
                    continue;
                }
                groupsFormed++;
                foreach (List<GameBot> realmPool in pools.Values)
                {
                    realmPool.Remove(leader);
                    foreach (GameBot other in others) realmPool.Remove(other);
                }
                Vector3 muster = new(porter.X, porter.Y, porter.Z);
                if (!AutonomousBotGroupCoordinator.TryFormOpenBattlegroundParty(leader, others, muster, porter.Name,
                        out Group group, out string groupId, out string reason, ownPorterMembers: merged))
                {
                    Log.Info($"OPEN_BG_GROUP_FAILED realm={RealmName(admit.Realm)} kind={Describe(admit.Kind)} leader=\"{leader.Name}\" size={others.Length + 1} reason=\"{reason}\"");
                    continue;
                }
                GameBot[] members = group.GetMembersInTheGroup().OfType<GameBot>().ToArray();
                int crew = Math.Max(0, setup.MaxBots) - free;
                if (!AutonomousBattlegroundParticipation.AdmitsOpenUnit(members.Length, crew, setup.MaxBots) ||
                    !Register(admit, members, group, groupId, now, porter))
                {
                    // Cannot happen with the checks above; never leave a muster without a roster unit.
                    group.DisbandGroup();
                    continue;
                }
                if (PresetFor(size) != null) AdvancePreset(size);
                free -= members.Length;
                formed++;
            }
            return formed;
        }

        // An 8-man or small group of a realm could not form: one OPEN_BG_FORM_FAILED line per realm and kind every
        // FormFailedLogMilliseconds, counting the failures in between. Every failure (a solo's too, without a log
        // line) is kept for the open BG window as the realm's and kind's last form failure.
        private static void NoteFormFailed(OpenBgAdmit admit, int need, List<GameBot> pool, Dictionary<OpenBgReject, int> poolStage,
            Dictionary<OpenBgReject, int> mateStage, long now, bool log = true)
        {
            int rvr = admit.Kind == OpenBgUnitKind.Solo ? pool.Count
                : pool.Count(bot => AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR) && bot.Group == null);
            IReadOnlyDictionary<OpenBgReject, int> rejections = admit.Kind == OpenBgUnitKind.Solo
                ? FormFailureRejections(rvr, need, 0, poolStage, null)
                : FormFailureRejections(rvr, need, pool.Count - rvr, poolStage, mateStage);
            int repeats;
            RecordFormFailure(admit.Realm, admit.Kind, FormFailureText(admit.Kind, need, rvr, rejections), now);
            if (!log) return;
            lock (Sync)
            {
                var key = (admit.Realm, admit.Kind);
                (long next, int suppressed) = FormFailedLog.GetValueOrDefault(key);
                if (now < next)
                {
                    FormFailedLog[key] = (next, suppressed + 1);
                    return;
                }
                FormFailedLog[key] = (now + FormFailedLogMilliseconds, 0);
                repeats = suppressed;
            }
            Log.Info(FormFailedLine(admit.Realm, admit.Kind, need, rvr, rejections, repeats));
        }

        /// <summary>
        /// The window's text of a failed formation, e.g. "needs 6, 3 RvR candidates in the pool; far from a porter 5,
        /// resting after a relief 2". A solo counts every candidate, not only those on an RvR tour.
        /// </summary>
        public static string FormFailureText(OpenBgUnitKind kind, int need, int pool, IReadOnlyDictionary<OpenBgReject, int> rejections)
        {
            string reasons = string.Join(", ", (rejections ?? new Dictionary<OpenBgReject, int>()).Where(pair => pair.Value > 0)
                .OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).Take(4).Select(pair => $"{RejectionText(pair.Key)} {pair.Value}"));
            string candidates = kind == OpenBgUnitKind.Solo ? $"{pool} candidates" : $"{pool} RvR candidates";
            return $"needs {need}, {candidates} in the pool" + (reasons.Length == 0 ? $"; no level {MinLevel}-{MaxLevel} bot of the realm was free" : "; " + reasons);
        }

        /// <summary>A rejection in words for the open BG window.</summary>
        public static string RejectionText(OpenBgReject reject) => reject switch
        {
            OpenBgReject.KeepDefenseOrRaid => "held for a keep defense or raid",
            OpenBgReject.InGroup => "in a group",
            OpenBgReject.Busy => "busy (fighting, dead, dungeon, PvP wall)",
            OpenBgReject.Objective => "on PvE work",
            OpenBgReject.Rest => "resting after a relief",
            OpenBgReject.Cooldown => "cooling down after a drop",
            OpenBgReject.FarFromPorter => "far from a porter",
            OpenBgReject.OtherRegion => "in another frontier",
            OpenBgReject.KeepOwnerSide => "on the keep owner's side",
            OpenBgReject.LevelBand => "outside the leader's level band",
            _ => "other",
        };

        /// <summary>Keeps a realm's and kind's last form failure for the open BG window (game time).</summary>
        public static void RecordFormFailure(eRealm realm, OpenBgUnitKind kind, string text, long now)
        {
            lock (Sync)
                LastFormFailure[(realm, kind)] = (text, now);
        }

        /// <summary>A unit of this realm and kind formed: its last failure no longer explains anything.</summary>
        public static void ClearFormFailure(eRealm realm, OpenBgUnitKind kind)
        {
            lock (Sync)
                LastFormFailure.Remove((realm, kind));
        }

        /// <summary>The rest of a relieved unit's members: a hand relief or one below its minimum gets the short cooldown.</summary>
        public static long RestAfterRelief(bool belowMinimum, bool manual) => RestAfterRelief(belowMinimum || manual);

        /// <summary>The <c>OPEN_BG_UNIT_RELIEVED</c> reason: manual before below_minimum before scheduled (due, surplus or cap).</summary>
        public static string ReliefReason(bool manual, bool belowMinimum) => manual ? "manual" : belowMinimum ? "below_minimum" : "scheduled";

        /// <summary>
        /// The last time a unit of this realm and kind could not form (game time, <see cref="GameLoop.GameLoopTime"/>) and
        /// why. False when none failed since the last one formed.
        /// </summary>
        public static bool TryGetLastFormFailure(eRealm realm, OpenBgUnitKind kind, out string text, out long tick)
        {
            lock (Sync)
                if (LastFormFailure.TryGetValue((realm, kind), out var failure))
                {
                    text = failure.Text;
                    tick = failure.Tick;
                    return true;
                }
            text = null;
            tick = 0;
            return false;
        }

        /// <summary>
        /// Queues a unit for a hand relief (open BG window, /bgsetup relieve): the next roster tick relieves it first with
        /// the short <see cref="RecruitCooldownMilliseconds"/> rest and forms a replacement if the targets still want one.
        /// False for a unit the roster does not know.
        /// </summary>
        public static bool RequestRelief(int unit)
        {
            lock (Sync)
            {
                if (unit <= 0 || !Units.ContainsKey(unit)) return false;
                PendingRelief.Add(unit);
                return true;
            }
        }

        /// <summary>Whether a hand relief of this unit waits for the next roster tick.</summary>
        public static bool IsReliefPending(int unit)
        {
            lock (Sync)
                return PendingRelief.Contains(unit);
        }

        // Starts or extends a bot's rest; remembers whether the running rest is the long one after a relief.
        private static void StartRest(GameBot bot, long now, long restMilliseconds)
        {
            if (bot == null) return;
            lock (Sync)
            {
                long until = RestingUntil.GetValueOrDefault(bot);
                long extended = ExtendRest(until, now, restMilliseconds);
                if (extended != until) RestIsRelief.Remove(bot);
                if (extended != until && restMilliseconds >= RestAfterReliefMilliseconds) RestIsRelief.Add(bot);
                RestingUntil[bot] = extended;
            }
        }

        private static bool Register(OpenBgAdmit admit, GameBot[] members, Group group, string label, long now, OFTeleporter porter = null)
        {
            int id;
            lock (Sync) id = ++_nextUnit;
            if (!AutonomousBattlegroundParticipation.AssignOpenUnit(id, label, members, group, now)) return false;
            lock (Sync)
            {
                Units[id] = new UnitInfo { Id = id, Realm = admit.Realm, Kind = admit.Kind, Group = group, Porter = porter,
                    FormedSize = members.Length };
            }
            ClearFormFailure(admit.Realm, admit.Kind);
            Log.Info($"OPEN_BG_UNIT_ASSIGNED realm={RealmName(admit.Realm)} unit={id} kind={Describe(admit.Kind)} group=\"{label}\" size={members.Length} " +
                $"leader=\"{members[0].Name}\" members=\"{string.Join(",", members.Select(member => member.Name))}\"");
            return true;
        }

        /// <summary>
        /// Candidates per realm, best first: RvR tour before idle, then nearest to a medallion porter (per 1,000 units),
        /// level 50 first among those (their tour renews). Only bots alone, not resting after a relief or a drop, ready
        /// to walk now (<see cref="IsRecruitableNow"/>) and within <see cref="PorterReach"/> of a porter that sells the
        /// medallion.
        /// </summary>
        private static Dictionary<eRealm, List<GameBot>> CandidatePools(eRealm[] realms, long now,
            out Dictionary<eRealm, Dictionary<OpenBgReject, int>> rejections)
        {
            var pools = realms.ToDictionary(realm => realm, _ => new List<GameBot>());
            rejections = realms.ToDictionary(realm => realm, _ => new Dictionary<OpenBgReject, int>());
            var distance = new Dictionary<GameBot, long>(ReferenceEqualityComparer.Instance);
            Dictionary<GameBot, long> resting;
            HashSet<GameBot> restIsRelief;
            lock (Sync)
            {
                resting = new Dictionary<GameBot, long>(RestingUntil, ReferenceEqualityComparer.Instance);
                restIsRelief = new HashSet<GameBot>(RestIsRelief, ReferenceEqualityComparer.Instance);
            }
            foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
            {
                if (!pools.TryGetValue(bot.Realm, out List<GameBot> pool)) continue;
                bool isResting = resting.TryGetValue(bot, out long until) && IsResting(until, now);
                if (bot.Group != null || isResting || !IsCandidate(bot))
                {
                    if (InCrewBand(bot))
                        Count(rejections[bot.Realm], CandidateRejection(bot, isResting, restIsRelief.Contains(bot)) ?? OpenBgReject.Busy);
                    continue;
                }
                OFTeleporter[] porters = PortersIn(bot.CurrentRegionID, now);
                long nearest = porters.Length == 0 ? -1 : porters.Min(porter => (long)bot.GetDistanceTo(porter));
                if (!WithinPorterReach(nearest))
                {
                    Count(rejections[bot.Realm], OpenBgReject.FarFromPorter);
                    continue;
                }
                distance[bot] = nearest;
                pool.Add(bot);
            }
            foreach (List<GameBot> pool in pools.Values)
            {
                GameBot[] ordered = pool
                    .OrderBy(bot => AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR) ? 0 : 1)
                    .ThenBy(bot => distance[bot] / 1_000)
                    .ThenBy(bot => bot.Level >= 50 ? 0 : 1)
                    .ThenBy(bot => distance[bot])
                    .ThenBy(bot => bot.DatabaseID)
                    .ToArray();
                pool.Clear();
                pool.AddRange(ordered);
            }
            return pools;
        }

        // The bots whose rejection the FORM_FAILED line counts: crew-band autonomous world bots on a home frontier
        // outside every battleground, i.e. those the roster could recruit at all.
        private static bool InCrewBand(GameBot bot) =>
            bot.ObjectState == GameObject.eObjectState.Active && bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper &&
            !bot.IsPersistentPlayerCompanion && bot.Level >= MinLevel && bot.Level <= MaxLevel &&
            AutonomousBattlegroundParticipation.IsFrontierRegion(bot.CurrentRegionID) && !AutonomousBattlegroundParticipation.IsInAnyBattleground(bot);

        private static OpenBgReject? CandidateRejection(GameBot bot, bool resting, bool restAfterRelief) =>
            CandidateRejection(AutonomousRealmRaid.IsReserved(bot) || AutonomousGuildKeepDefense.IsRecalled(bot), bot.Group != null,
                bot.IsPlayerLedGroup || GameRelic.IsPlayerCarryingRelic(bot) ||
                !IsRecruitableNow(bot.IsAlive, bot.IsReturningAfterRelease, bot.CurrentZone?.IsDungeon == true,
                    AutonomousActivityScheduler.IsPveBlocked(bot.PersistentRecord, WorldSimulationClock.UtcNow),
                    bot.InCombat || bot.IsAttacking, bot.IsOnStableMasterRoute),
                AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR), IsIdle(bot), resting, restAfterRelief, true);

        private static void Count(Dictionary<OpenBgReject, int> counts, OpenBgReject reject) =>
            counts[reject] = counts.GetValueOrDefault(reject) + 1;

        /// <summary>Starts a bot's re-recruit cooldown (<see cref="RecruitCooldownMilliseconds"/>) after it left its unit before boarding.</summary>
        public static void NoteLeftBeforeEntry(GameBot bot, long now) => StartRest(bot, now, RecruitCooldownMilliseconds);

        /// <summary>
        /// Refills each unit the plan keeps although it fell below its minimum before boarding: replacements from the
        /// realm's pool join the unit's own group at its porter, up to the size it formed with and never above the cap.
        /// A unit whose party no longer exists cannot be refilled; it is relieved and a new unit forms in its place.
        /// Without enough candidates near the porter it waits for the next tick (the assignment timeout bounds it).
        /// </summary>
        private static void RefillUnits(OpenBgPlan plan, OpenBgSetup setup, AutonomousBattlegroundParticipation.OpenCrewMember[] crew,
            Dictionary<eRealm, List<GameBot>> pools, GuildIds keepOwner, long now)
        {
            int free = Math.Max(0, setup.MaxBots) - AutonomousBattlegroundParticipation.OpenCrewCount();
            foreach (OpenBgRealmPlan realm in plan.Realms)
                foreach (int unit in realm.Refill ?? Array.Empty<int>())
                {
                    UnitInfo info;
                    lock (Sync) Units.TryGetValue(unit, out info);
                    GameBot[] members = crew.Where(member => member.Unit == unit && !member.Leaving).Select(member => member.Bot).ToArray();
                    Group group = info?.Group;
                    if (info == null || group == null || group.MemberCount < 1 || !AutonomousBotGroupCoordinator.IsOpenBattlegroundParty(group) ||
                        info.Porter is not { ObjectState: GameObject.eObjectState.Active } porter)
                    {
                        foreach (GameBot bot in AutonomousBattlegroundParticipation.RelieveOpenUnit(unit, now))
                            StartRest(bot, now, RecruitCooldownMilliseconds);
                        continue;
                    }
                    int target = Math.Max(MinimumSize(info.Kind), info.Kind == OpenBgUnitKind.Group8 ? Group8Size : info.FormedSize);
                    int wanted = Math.Min(target - members.Length, free);
                    GameBot anchor = group.LivingLeader as GameBot ?? members.FirstOrDefault();
                    if (wanted < MinimumSize(info.Kind) - members.Length || anchor == null ||
                        pools == null || !pools.TryGetValue(realm.Realm, out List<GameBot> pool))
                        continue;
                    GameBot[] picks = UnitMates(anchor, pool, GuildIdsOf, keepOwner).Where(bot => bot.CurrentRegionID == porter.CurrentRegionID &&
                            AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR) &&
                            AutonomousBotGroupCoordinator.LevelsCompatible(anchor.Level, bot.Level) &&
                            WithinPorterReach(bot.GetDistanceTo(porter)))
                        .OrderBy(bot => bot.GetDistanceTo(porter)).Take(wanted).ToArray();
                    if (members.Length + picks.Length < MinimumSize(info.Kind)) continue;
                    GameBot[] added = AutonomousBotGroupCoordinator.TryRefillOpenBattlegroundParty(group, picks);
                    foreach (GameBot bot in added) pool.Remove(bot);
                    if (added.Length == 0) continue;
                    if (!AutonomousBattlegroundParticipation.AssignOpenUnit(unit, AutonomousBattlegroundParticipation.OpenUnitLabel(unit), added, group, now))
                    {
                        // Cannot happen (the pool holds only unregistered bots); never keep a member without a unit.
                        foreach (GameBot bot in added)
                            if (bot.Group == group) group.RemoveMember(bot);
                        continue;
                    }
                    free -= added.Length;
                    AutonomousBattlegroundParticipation.RestartOpenBoardingWait(unit);
                    Log.Info($"OPEN_BG_UNIT_REFILLED realm={RealmName(realm.Realm)} unit={unit} kind={Describe(info.Kind)} " +
                        $"size={members.Length + added.Length} added=\"{string.Join(",", added.Select(bot => bot.Name))}\" porter=\"{porter.Name}\"");
                }
        }

        /// <summary>
        /// A leader and its members from the RvR-tour part of the pool, all in the leader's region: the best-ranked
        /// leader (a claim-rank guild member preferred), its nearest medallion porter as the muster, a healer first
        /// (two for an 8-man), then the nearest level-compatible bots on the leader's side of the keep's owner guild
        /// (<see cref="MayJoinUnit"/>).
        /// </summary>
        // Units formed so far per unit size; PresetFor reads the counter (a peek, safe to call repeatedly for one
        // formation attempt) and AdvancePreset moves it once per unit that was actually registered.
        private static readonly long[] PresetOrdinals = new long[9];

        private static OpenBgArchetype PresetFor(int size) =>
            size < 0 || size >= PresetOrdinals.Length ? null
                : OpenBattlegroundArchetypes.PickForSize(OpenBattlegroundArchetypes.ParseMix(OpenBattlegroundProperties.Presets), size,
                    Interlocked.Read(ref PresetOrdinals[size]));

        private static void AdvancePreset(int size)
        {
            if (size >= 0 && size < PresetOrdinals.Length) Interlocked.Increment(ref PresetOrdinals[size]);
        }

        private static bool TryPickGroup(List<GameBot> pool, int size, int minimum, long now, GuildIds keepOwner, bool merged, eRealm admitRealm,
            out GameBot leader, out GameBot[] others, out OFTeleporter porter, out Dictionary<OpenBgReject, int> mateRejections)
        {
            leader = null;
            others = Array.Empty<GameBot>();
            porter = null;
            mateRejections = null;
            GameBot[] rvr = pool.Where(bot => AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR) && bot.Group == null).ToArray();
            if (rvr.Length < minimum) return false;
            int bestNear = -1;
            foreach (GameBot seed in rvr.Where(bot => OpenBattlegroundMuster.SeedAllowed(merged, bot.Realm, admitRealm)).OrderBy(bot => bot.Guild?.HasRank(bot, Guild.eRank.Claim) == true ? 0 : 1).Take(4))
            {
                OFTeleporter nearest = PortersIn(seed.CurrentRegionID, now).OrderBy(candidate => seed.GetDistanceTo(candidate)).FirstOrDefault();
                if (nearest == null) continue;
                GameBot[] near = UnitMates(seed, rvr, GuildIdsOf, keepOwner).Where(bot => bot != seed &&
                        AutonomousBotGroupCoordinator.LevelsCompatible(seed.Level, bot.Level) &&
                        OpenBattlegroundMuster.AcceptsMember(merged, bot.CurrentRegionID == seed.CurrentRegionID,
                            bot.CurrentRegionID == seed.CurrentRegionID && WithinPorterReach(bot.GetDistanceTo(nearest))))
                    .OrderBy(bot => bot.CurrentRegionID == seed.CurrentRegionID ? 0 : 1)
                    .ThenBy(bot => bot.CurrentRegionID == seed.CurrentRegionID ? bot.GetDistanceTo(nearest) : 0).ToArray();
                if (near.Length + 1 < minimum)
                {
                    // For the log: why the others did not fit the leader that came closest.
                    if (near.Length > bestNear)
                    {
                        bestNear = near.Length;
                        mateRejections = MateRejections(seed, rvr, nearest, keepOwner, merged);
                    }
                    continue;
                }
                GameBot[] picked = null;
                // Several presets may be configured: this unit's size picks among the archetypes of that size by their counts (PresetFor peeks; the counter advances when the unit is registered).
                OpenBgArchetype archetype = PresetFor(size);
                if (archetype != null && seed.CharacterClass != null && near.All(bot => bot.CharacterClass != null))
                {
                    // The seed leads and is candidate 0 (forced: it counts toward the slots); near is sorted by distance.
                    var classes = new List<eCharacterClass> { (eCharacterClass)seed.CharacterClass.ID };
                    classes.AddRange(near.Select(bot => (eCharacterClass)bot.CharacterClass.ID));
                    OpenBgFill fill = OpenBattlegroundArchetypes.Fill(archetype, classes, OpenBattlegroundMode.Current, 0);
                    picked = fill.PickedIndexes.Where(index => index > 0).Take(size - 1).Select(index => near[index - 1]).ToArray();
                    if (fill.MissedMinimums.Length > 0)
                        Log.Info($"OPEN_BG_ARCHETYPE_MISS archetype={archetype.Key} missed={string.Join(",", fill.MissedMinimums)}");
                }
                if (picked == null)
                {
                    int healersWanted = (size >= 6 ? 2 : 1) - (IsHealer(seed) ? 1 : 0);
                    GameBot[] healers = near.Where(IsHealer).Take(Math.Max(0, healersWanted)).ToArray();
                    picked = healers.Concat(near.Where(bot => !healers.Contains(bot)).Take(size - 1 - healers.Length)).ToArray();
                }
                leader = seed;
                others = picked;
                porter = nearest;
                return true;
            }
            return false;
        }

        private static Dictionary<OpenBgReject, int> MateRejections(GameBot seed, GameBot[] rvr, OFTeleporter porter, GuildIds keepOwner, bool merged)
        {
            var counts = new Dictionary<OpenBgReject, int>();
            var sameSide = UnitMates(seed, rvr, GuildIdsOf, keepOwner).ToHashSet(ReferenceEqualityComparer.Instance);
            foreach (GameBot bot in rvr)
            {
                if (bot == seed) continue;
                bool sameRegion = bot.CurrentRegionID == seed.CurrentRegionID;
                bool accepted = OpenBattlegroundMuster.AcceptsMember(merged, sameRegion, sameRegion && WithinPorterReach(bot.GetDistanceTo(porter)));
                OpenBgReject? reject = MateRejection(sameRegion || merged, sameSide.Contains(bot),
                    AutonomousBotGroupCoordinator.LevelsCompatible(seed.Level, bot.Level), accepted);
                if (reject is OpenBgReject value) Count(counts, value);
            }
            return counts;
        }

        /// <summary>
        /// The guild that owns the open BG's keep by the keep's friend check (<c>KeepManager.IsEnemy</c>):
        /// a real guild on a PvP (Camlann) server, with its alliance. Empty for an unclaimed keep, the garrison, or a
        /// realm server, where the keep's friends are not a guild.
        /// </summary>
        private static GuildIds KeepOwnerOf(BattlegroundDefinition definition)
        {
            if (GameServer.Instance?.Configuration?.ServerType != EGameServerType.GST_PvP) return default;
            AbstractGameKeep keep = BattlegroundCampaignCatalog.CentralKeep(definition);
            return keep?.Guild != null && PvpCombatant.IsRealGuild(keep.Guild) ? IdsOf(keep.Guild) : default;
        }

        private static GuildIds GuildIdsOf(GameBot bot) => IdsOf(bot?.Guild);

        private static GuildIds IdsOf(Guild guild) =>
            guild == null ? default : new GuildIds(guild.GuildID, guild.alliance == null ? null : guild.alliance.DbAlliance?.ObjectId ?? guild.AllianceId);

        /// <summary>
        /// Splits every unit that mixes members of the keep's owner guild with others (the keep changed hands under it):
        /// the owner-guild members leave the unit and its group with the <see cref="RecruitCooldownMilliseconds"/> rest,
        /// the others stay, and the roster refills or replaces a unit left below its minimum as usual. Returns the
        /// number of units split.
        /// </summary>
        private static int SplitKeepOwnerUnits(AutonomousBattlegroundParticipation.OpenCrewMember[] crew, GuildIds keepOwner, long now)
        {
            if (string.IsNullOrEmpty(keepOwner.GuildId)) return 0;
            int split = 0;
            foreach (IGrouping<int, AutonomousBattlegroundParticipation.OpenCrewMember> unit in crew.Where(member => !member.Leaving).GroupBy(member => member.Unit))
            {
                GameBot[] members = unit.Select(member => member.Bot).Where(bot => bot != null).ToArray();
                GameBot[] owners = SplitForKeepOwner(members, GuildIdsOf, keepOwner);
                if (owners.Length == 0) continue;
                GameBot[] relieved = AutonomousBattlegroundParticipation.RelieveOpenMembers(unit.Key, owners, now);
                if (relieved.Length == 0) continue;
                foreach (GameBot bot in relieved)
                    StartRest(bot, now, RecruitCooldownMilliseconds);
                split++;
                Log.Info($"OPEN_BG_UNIT_SPLIT unit={unit.Key} reason=keep_owner_guild guild=\"{relieved[0].Guild?.Name}\" " +
                    $"members=\"{string.Join(",", relieved.Select(bot => bot.Name))}\" remaining={members.Length - relieved.Length}");
            }
            return split;
        }

        // Active porters with a medallion merchant in reach, cached per region (see PorterCacheMilliseconds).
        private static OFTeleporter[] PortersIn(ushort region, long now)
        {
            lock (Sync)
                if (PorterCache.TryGetValue(region, out var cached) && now - cached.Tick < PorterCacheMilliseconds &&
                    cached.Porters.All(porter => porter.ObjectState == GameObject.eObjectState.Active))
                    return cached.Porters;
            OFTeleporter[] porters = AutonomousBattlegroundParticipation.SourcePorters(region);
            lock (Sync) PorterCache[region] = (now, porters);
            return porters;
        }

        /// <summary>The kind of a roster unit, read by the /bgsetup status page. False for a unit the roster no longer tracks.</summary>
        public static bool TryGetUnitKind(int unit, out OpenBgUnitKind kind)
        {
            lock (Sync)
                if (Units.TryGetValue(unit, out UnitInfo info))
                {
                    kind = info.Kind;
                    return true;
                }
            kind = OpenBgUnitKind.Solo;
            return false;
        }

        /// <summary>The short name of a unit kind as the roster logs it: group8, small or solo.</summary>
        public static string KindName(OpenBgUnitKind kind) => Describe(kind);

        private static readonly HashSet<eCharacterClass> RosterHealers = new()
        { eCharacterClass.Cleric, eCharacterClass.Healer, eCharacterClass.Druid, eCharacterClass.Friar, eCharacterClass.Shaman, eCharacterClass.Warden };

        /// <summary>The roster's healer test: a Paladin is no healer (<see cref="BotPartyRoles.IsHealingClass"/> stays for companions).</summary>
        public static bool IsRosterHealer(eCharacterClass characterClass) => RosterHealers.Contains(characterClass);

        private static bool IsHealer(GameBot bot) =>
            bot.CharacterClass != null && IsRosterHealer((eCharacterClass)bot.CharacterClass.ID);

        private static string Describe(OpenBgUnitKind kind) => kind switch
        {
            OpenBgUnitKind.Group8 => "group8",
            OpenBgUnitKind.Small => "small",
            _ => "solo",
        };

        // eRealm.Albion shares its value with _FirstPlayerRealm, so ToString() prints the alias.
        public static string RealmName(eRealm realm) => realm switch
        {
            eRealm.Albion => "Albion",
            eRealm.Midgard => "Midgard",
            eRealm.Hibernia => "Hibernia",
            _ => realm.ToString(),
        };

        private static void LogRoster(OpenBgSetup setup, IReadOnlyList<RealmCount> counts, OpenBgPlan plan)
        {
            foreach (RealmCount realm in counts)
            {
                int inside = realm.Units.Where(unit => unit.Inside).Sum(unit => unit.Size);
                OpenBgRealmTargets targets = setup.TargetsFor(realm.Realm);
                Log.Info($"OPEN_BG_ROSTER realm={RealmName(realm.Realm)} groups8={realm.Groups8}/{targets.Groups8} small={realm.GroupsSmall}/{targets.Small} " +
                    $"solos={realm.Solos}/{targets.Solos} own_setup={setup.IsOverridden(realm.Realm)} present={inside} assigned={realm.Bots} leaving={realm.LeavingBots} " +
                    $"total={plan.Present} cap={plan.Cap} cap_binds={plan.CapBinds}");
            }
        }
    }
}
