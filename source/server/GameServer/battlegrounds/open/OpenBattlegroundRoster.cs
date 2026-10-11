using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using DOL.GS.Scripts;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>Per-realm setup of the open BG crew and the hard cap over all realms (spec 2026-10-11 §5).</summary>
    public sealed record OpenBgSetup(int Groups8, int GroupsSmall, int Solos, int MaxBots)
    {
        /// <summary>The clamped server properties (open_bg_groups8, open_bg_groups_small, open_bg_solos, open_bg_max_bots).</summary>
        public static OpenBgSetup FromProperties() =>
            new(OpenBattlegroundProperties.Groups8, OpenBattlegroundProperties.GroupsSmall, OpenBattlegroundProperties.Solos,
                OpenBattlegroundProperties.MaxBots);
    }

    public enum OpenBgUnitKind { Group8, Small, Solo }

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

    public sealed record OpenBgRealmPlan(eRealm Realm, int AddGroups8, int AddSmall, int AddSolos, IReadOnlyList<int> Relieve);

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
        public const int MinLevel = 40;
        public const int MaxLevel = 50;
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

        /// <summary>
        /// Per realm: relieve one unit when its relief is due (the longest-serving group, or a quarter of the solos
        /// when a solo serves longest), relieve units above a lowered setup, then add missing units in realm
        /// round-robin order (fewest bots first) while they fit under the cap. Never throws for a cap below the setup.
        /// </summary>
        public static OpenBgPlan Plan(OpenBgSetup setup, IReadOnlyList<RealmCount> present)
        {
            setup ??= new OpenBgSetup(0, 0, 0, 0);
            present ??= Array.Empty<RealmCount>();
            int cap = Math.Max(0, setup.MaxBots);
            int total = present.Sum(realm => realm.Bots + Math.Max(0, realm.LeavingBots));
            var needs = new Dictionary<eRealm, int[]>();
            var relieved = new Dictionary<eRealm, List<int>>();
            foreach (RealmCount realm in present)
            {
                var relieve = new List<int>();
                if (realm.ReliefDue) relieve.AddRange(ReliefPick(realm.Units));
                relieve.AddRange(SurplusPick(realm.Units, OpenBgUnitKind.Group8, setup.Groups8, relieve));
                relieve.AddRange(SurplusPick(realm.Units, OpenBgUnitKind.Small, setup.GroupsSmall, relieve));
                relieve.AddRange(SurplusPick(realm.Units, OpenBgUnitKind.Solo, setup.Solos, relieve));
                relieved[realm.Realm] = relieve;
                int Remaining(OpenBgUnitKind kind, int wanted) =>
                    Math.Max(0, Math.Max(0, wanted) - realm.Units.Count(unit => unit.Kind == kind && !relieve.Contains(unit.Id)));
                needs[realm.Realm] = new[]
                {
                    Remaining(OpenBgUnitKind.Group8, setup.Groups8),
                    Remaining(OpenBgUnitKind.Small, setup.GroupsSmall),
                    Remaining(OpenBgUnitKind.Solo, setup.Solos),
                };
            }

            // Relieved bots stay until they have walked out, so they still hold their seats this tick.
            int free = cap - total;
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
                added[realm.Realm][1], added[realm.Realm][2], relieved[realm.Realm])).ToArray();
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
        /// A crew candidate: an autonomous world bot of level 40-50, not in a player-led group, carrying no relic, in no
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
            !AutonomousRealmRaid.IsReserved(bot) && !AutonomousGuildKeepDefense.IsRecalled(bot);

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
            Stopwatch watch = Stopwatch.StartNew();
            OpenBgSetup setup = OpenBgSetup.FromProperties();
            AutonomousBattlegroundParticipation.OpenCrewMember[] crew = AutonomousBattlegroundParticipation.OpenCrew();
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
                    RestingUntil.Remove(bot);
            }
            foreach (Group group in released)
                AutonomousBotGroupCoordinator.ReleaseOpenBattlegroundParty(group);

            OpenBgPlan plan = Plan(setup, counts);
            foreach (OpenBgRealmPlan realm in plan.Realms)
            {
                if (realm.Relieve.Count == 0) continue;
                foreach (int unit in realm.Relieve)
                    foreach (GameBot bot in AutonomousBattlegroundParticipation.RelieveOpenUnit(unit, now))
                        lock (Sync) RestingUntil[bot] = now + RestAfterReliefMilliseconds;
                // Only a due relief restarts the realm's clock; a lowered setup's surplus does not.
                if (counts.FirstOrDefault(count => count.Realm == realm.Realm)?.ReliefDue != true) continue;
                lock (Sync)
                {
                    ReliefClock clock = ClockOf(realm.Realm);
                    clock.LastTick = now;
                    clock.Seed++;
                }
            }
            int formed = plan.Admits.Count > 0 ? FormUnits(plan, setup, now) : 0;

            if (now >= _nextLogTick)
            {
                _nextLogTick = now + LogIntervalMilliseconds;
                LogRoster(setup, counts, plan);
            }
            if (watch.ElapsedMilliseconds > SlowTickMilliseconds)
                Log.Warn($"OPEN_BG_TICK_SLOW part=roster ms={watch.ElapsedMilliseconds} crew={crew.Length} admits={plan.Admits.Count} formed={formed}");
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
        private static int FormUnits(OpenBgPlan plan, OpenBgSetup setup, long now)
        {
            int free = Math.Max(0, setup.MaxBots) - AutonomousBattlegroundParticipation.OpenCrewCount();
            if (free <= 0) return 0;
            Dictionary<eRealm, List<GameBot>> pools = CandidatePools(plan.Admits.Select(admit => admit.Realm).Distinct().ToArray(), now);
            int groupsFormed = 0, formed = 0;
            foreach (OpenBgAdmit admit in plan.Admits)
            {
                if (free <= 0) break;
                List<GameBot> pool = pools[admit.Realm];
                if (admit.Kind == OpenBgUnitKind.Solo)
                {
                    GameBot solo = pool.FirstOrDefault();
                    if (solo == null) continue;
                    pool.Remove(solo);
                    if (Register(admit, new[] { solo }, null, "solo", now)) { free--; formed++; }
                    continue;
                }
                if (groupsFormed >= MaxGroupFormationsPerTick) continue;
                int size = admit.Kind == OpenBgUnitKind.Group8 ? Math.Min(Group8Size, free) : Math.Min(SmallGroupSize(Rng), free);
                int minimum = admit.Kind == OpenBgUnitKind.Group8 ? Group8Minimum : SmallGroupMin;
                if (size < minimum) continue;
                if (!TryPickGroup(pool, size, minimum, now, out GameBot leader, out GameBot[] others, out OFTeleporter porter)) continue;
                groupsFormed++;
                pool.Remove(leader);
                foreach (GameBot other in others) pool.Remove(other);
                Vector3 muster = new(porter.X, porter.Y, porter.Z);
                if (!AutonomousBotGroupCoordinator.TryFormOpenBattlegroundParty(leader, others, muster, porter.Name,
                        out Group group, out string groupId, out string reason))
                {
                    Log.Info($"OPEN_BG_GROUP_FAILED realm={admit.Realm} kind={Describe(admit.Kind)} leader=\"{leader.Name}\" size={others.Length + 1} reason=\"{reason}\"");
                    continue;
                }
                GameBot[] members = group.GetMembersInTheGroup().OfType<GameBot>().ToArray();
                int crew = Math.Max(0, setup.MaxBots) - free;
                if (!AutonomousBattlegroundParticipation.AdmitsOpenUnit(members.Length, crew, setup.MaxBots) ||
                    !Register(admit, members, group, groupId, now))
                {
                    // Cannot happen with the checks above; never leave a muster without a roster unit.
                    group.DisbandGroup();
                    continue;
                }
                free -= members.Length;
                formed++;
            }
            return formed;
        }

        private static bool Register(OpenBgAdmit admit, GameBot[] members, Group group, string label, long now)
        {
            int id;
            lock (Sync) id = ++_nextUnit;
            if (!AutonomousBattlegroundParticipation.AssignOpenUnit(id, label, members, group, now)) return false;
            lock (Sync) Units[id] = new UnitInfo { Id = id, Realm = admit.Realm, Kind = admit.Kind, Group = group };
            Log.Info($"OPEN_BG_UNIT_ASSIGNED realm={admit.Realm} unit={id} kind={Describe(admit.Kind)} group=\"{label}\" size={members.Length} " +
                $"leader=\"{members[0].Name}\" members=\"{string.Join(",", members.Select(member => member.Name))}\"");
            return true;
        }

        /// <summary>
        /// Candidates per realm, best first: RvR tour before idle, level 50 first (their tour renews), then nearest to a
        /// medallion porter. Only bots alone, not resting after a relief, and standing where a porter sells the medallion.
        /// </summary>
        private static Dictionary<eRealm, List<GameBot>> CandidatePools(eRealm[] realms, long now)
        {
            var pools = realms.ToDictionary(realm => realm, _ => new List<GameBot>());
            HashSet<GameBot> resting;
            lock (Sync) resting = new HashSet<GameBot>(RestingUntil.Keys, ReferenceEqualityComparer.Instance);
            foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
            {
                if (!pools.TryGetValue(bot.Realm, out List<GameBot> pool) || bot.Group != null || resting.Contains(bot) || !IsCandidate(bot)) continue;
                if (PortersIn(bot.CurrentRegionID, now).Length == 0) continue;
                pool.Add(bot);
            }
            foreach (List<GameBot> pool in pools.Values)
            {
                GameBot[] ordered = pool
                    .OrderBy(bot => AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR) ? 0 : 1)
                    .ThenBy(bot => bot.Level >= 50 ? 0 : 1)
                    .ThenBy(bot => PortersIn(bot.CurrentRegionID, now).Min(porter => (long)bot.GetDistanceTo(porter)))
                    .ThenBy(bot => bot.DatabaseID)
                    .ToArray();
                pool.Clear();
                pool.AddRange(ordered);
            }
            return pools;
        }

        /// <summary>
        /// A leader and its members from the RvR-tour part of the pool, all in the leader's region: the best-ranked
        /// leader (a claim-rank guild member preferred), its nearest medallion porter as the muster, a healer first
        /// (two for an 8-man), then the nearest level-compatible bots.
        /// </summary>
        private static bool TryPickGroup(List<GameBot> pool, int size, int minimum, long now,
            out GameBot leader, out GameBot[] others, out OFTeleporter porter)
        {
            leader = null;
            others = Array.Empty<GameBot>();
            porter = null;
            GameBot[] rvr = pool.Where(bot => AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR) && bot.Group == null).ToArray();
            if (rvr.Length < minimum) return false;
            foreach (GameBot seed in rvr.OrderBy(bot => bot.Guild?.HasRank(bot, Guild.eRank.Claim) == true ? 0 : 1).Take(4))
            {
                OFTeleporter nearest = PortersIn(seed.CurrentRegionID, now).OrderBy(candidate => seed.GetDistanceTo(candidate)).FirstOrDefault();
                if (nearest == null) continue;
                GameBot[] near = rvr.Where(bot => bot != seed && bot.CurrentRegionID == seed.CurrentRegionID &&
                        AutonomousBotGroupCoordinator.LevelsCompatible(seed.Level, bot.Level))
                    .OrderBy(bot => bot.GetDistanceTo(nearest)).ToArray();
                if (near.Length + 1 < minimum) continue;
                int healersWanted = (size >= 6 ? 2 : 1) - (IsHealer(seed) ? 1 : 0);
                GameBot[] healers = near.Where(IsHealer).Take(Math.Max(0, healersWanted)).ToArray();
                GameBot[] picked = healers.Concat(near.Where(bot => !healers.Contains(bot)).Take(size - 1 - healers.Length)).ToArray();
                leader = seed;
                others = picked;
                porter = nearest;
                return true;
            }
            return false;
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

        private static bool IsHealer(GameBot bot) =>
            bot.CharacterClass != null && BotPartyRoles.IsHealingClass((eCharacterClass)bot.CharacterClass.ID);

        private static string Describe(OpenBgUnitKind kind) => kind switch
        {
            OpenBgUnitKind.Group8 => "group8",
            OpenBgUnitKind.Small => "small",
            _ => "solo",
        };

        private static void LogRoster(OpenBgSetup setup, IReadOnlyList<RealmCount> counts, OpenBgPlan plan)
        {
            foreach (RealmCount realm in counts)
            {
                int inside = realm.Units.Where(unit => unit.Inside).Sum(unit => unit.Size);
                Log.Info($"OPEN_BG_ROSTER realm={realm.Realm} groups8={realm.Groups8}/{setup.Groups8} small={realm.GroupsSmall}/{setup.GroupsSmall} " +
                    $"solos={realm.Solos}/{setup.Solos} present={inside} assigned={realm.Bots} leaving={realm.LeavingBots} " +
                    $"total={plan.Present} cap={plan.Cap} cap_binds={plan.CapBinds}");
            }
        }
    }
}
