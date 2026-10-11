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

    /// <summary>One open BG cycle in game milliseconds (<see cref="GameLoop.GameLoopTime"/>) and its attacking realm.</summary>
    public sealed record OpenBgCycle(long StartTick, int QuietMs, int PushMs, int FightMs, int RetreatMs, eRealm Attacker)
    {
        public long TotalMs => (long)QuietMs + PushMs + FightMs + RetreatMs;
        public long EndTick => StartTick + TotalMs;
    }

    /// <summary>
    /// A roster unit's current order. <paramref name="Point"/> is a resolved floor point (null: no point, the driver keeps
    /// its own priority). <paramref name="Muster"/> marks an attacker still walking to its bridge before the march.
    /// <paramref name="AtKeep"/> marks an assault or defend order whose point is the keep itself.
    /// </summary>
    public sealed record OpenBgOrder(int Unit, OpenBgRole Role, Point3D Point, bool Muster, bool AtKeep, long Cycle);

    /// <summary>
    /// The open BG's phase machine (spec 2026-10-11 §6): quiet, push, keep fight, retreat, one random 15-45 game-minute
    /// cycle after another, with a new attacking realm each cycle (never the keep owner, fewest recent wins preferred).
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
        /// <summary>An attacker has mustered once its leader is this close to its own bridge.</summary>
        public const int MusterDistance = 500;
        /// <summary>The third realm in a keep fight, and defenders the keep's guards would shoot, fight this close to the keep.</summary>
        public const int NearKeepDistance = 3_600;
        /// <summary>Humans this close to a portal keep landing stand in a sanctuary and draw no drift.</summary>
        public const int SanctuaryDistance = 2_500;
        // Floor search: bridges and lurk points are known to lie within 256 (spec), field points span the island and the river.
        private const float BridgeFloorRange = 256, LurkFloorRange = 512, FieldFloorRange = 1_024;
        private const long SlowTickMilliseconds = 50;
        private static readonly eRealm[] PlayerRealms = { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia };
        private static readonly int[] FieldRadii = { 2_500, 3_500, 8_000, 11_000 };
        private static readonly Logger Log = LoggerManager.Create(typeof(OpenBattlegroundDirector));

        // ---- pure rules ------------------------------------------------------------------------------------------

        public static bool IsPlayerRealm(eRealm realm) => realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia;

        /// <summary>
        /// A new cycle starting at tick 0 (the caller sets <see cref="OpenBgCycle.StartTick"/>): its total length is drawn
        /// in [min, max] game minutes, the four phases take 30/20/35/15 % of it, each varied by up to 5 % of itself, and the
        /// attacker is a realm other than the keep owner, the one with the fewest recent wins most likely.
        /// </summary>
        public static OpenBgCycle NewCycle(Random rng, int minMinutes, int maxMinutes, eRealm keepOwner, IReadOnlyDictionary<eRealm, int> recentWins)
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
            return new OpenBgCycle(0, quiet, push, fight, retreat, PickAttacker(rng, keepOwner, recentWins));
        }

        /// <summary>
        /// A realm other than the keep owner, weighted 1 / (1 + recent wins)²: a realm without recent wins is four times as
        /// likely as one with a win and sixteen times as likely as one with three. Never the owner; any realm when unowned.
        /// </summary>
        public static eRealm PickAttacker(Random rng, eRealm keepOwner, IReadOnlyDictionary<eRealm, int> recentWins)
        {
            rng ??= Random.Shared;
            eRealm[] candidates = PlayerRealms.Where(realm => realm != keepOwner).ToArray();
            double[] weights = candidates.Select(realm =>
            {
                int wins = recentWins != null && recentWins.TryGetValue(realm, out int count) ? Math.Max(0, count) : 0;
                return 1.0 / ((1.0 + wins) * (1.0 + wins));
            }).ToArray();
            double roll = rng.NextDouble() * weights.Sum();
            for (int i = 0; i < candidates.Length; i++)
            {
                roll -= weights[i];
                if (roll < 0) return candidates[i];
            }
            return candidates[^1];
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

        /// <summary>The owner the roles see: after the lord fell in the keep fight or retreat, the attacker holds the field until a claim.</summary>
        public static eRealm RoleOwner(eRealm owner, bool lordDefeatedUnclaimed, OpenBgPhase phase, eRealm attacker) =>
            lordDefeatedUnclaimed && phase is OpenBgPhase.KeepFight or OpenBgPhase.Retreat && IsPlayerRealm(attacker) ? attacker : owner;

        /// <summary>The realm that won a finished cycle: the attacker if it took the keep, else the realm that held it, else nobody.</summary>
        public static eRealm CycleWinner(eRealm ownerAtStart, eRealm ownerAtEnd, bool lordDefeatedUnclaimed, eRealm attacker)
        {
            if (lordDefeatedUnclaimed || ownerAtEnd == attacker && ownerAtEnd != ownerAtStart) return IsPlayerRealm(attacker) ? attacker : eRealm.None;
            return IsPlayerRealm(ownerAtEnd) ? ownerAtEnd : eRealm.None;
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
            public eRealm Attacker = eRealm.None;
            public eRealm Owner = eRealm.None;
            public long Cycle;
            public Dictionary<int, OpenBgOrder> Orders = new();
        }

        private static readonly object Sync = new();
        private static readonly Random Rng = new();
        private static readonly Dictionary<int, UnitState> UnitStates = new();
        private static readonly Queue<eRealm> RecentWinners = new();
        private static readonly HashSet<string> UnresolvedLogged = new(StringComparer.Ordinal);
        private static MapData _map;
        private static OpenBgCycle _cycle;
        private static long _cycleIndex;
        private static eRealm _ownerAtStart;
        private static OpenBgPhase _loggedPhase;
        private static eRealm _loggedOwner;
        private static eRealm _loggedAttacker;
        private static bool _logged;
        private static Snapshot _snapshot = Snapshot.Empty;

        public static OpenBgPhase Phase => Volatile.Read(ref _snapshot).Phase;
        public static eRealm Attacker => Volatile.Read(ref _snapshot).Attacker;
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

        /// <summary>The driver found no route to the order's point: the next tick gives the unit another one.</summary>
        public static void ReportUnreachable(int unit, Point3D point)
        {
            lock (Sync)
                if (UnitStates.TryGetValue(unit, out UnitState state) && ReferenceEquals(state.Point, point)) state.Unreachable = true;
        }

        /// <summary>Phase, attacker and seconds left in the cycle, for the status window (lock-free).</summary>
        public static OpenBgPhaseStatus PhaseStatus()
        {
            OpenBgCycle cycle = Volatile.Read(ref _cycle);
            long left = cycle == null ? 0 : Math.Max(0, (cycle.EndTick - GameLoop.GameLoopTime) / 1000);
            return new OpenBgPhaseStatus(Phase.ToString(), Attacker.ToString(), (int)left);
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
            Stopwatch watch = Stopwatch.StartNew();
            AbstractGameKeep keep = BattlegroundCampaignCatalog.CentralKeep(definition);
            bool realGuild = keep?.Guild != null && PvpCombatant.IsRealGuild(keep.Guild);
            eRealm owner = keep == null ? eRealm.None :
                OwnerRealm(realGuild, PvpKeepCampaign.IsGarrison(keep.Guild), keep.Guild?.Realm ?? eRealm.None, keep.Realm);
            bool lordDown = keep?.Guild == null && keep?.DBKeep?.LordDefeated == true;
            int units;
            lock (Sync)
            {
                _map ??= BuildMap(definition, keep);
                AdvanceCycle(now, owner, lordDown);
                units = UpdateOrders(definition, keep, owner, lordDown, now);
            }
            if (watch.ElapsedMilliseconds > SlowTickMilliseconds)
                Log.Warn($"OPEN_BG_TICK_SLOW part=director ms={watch.ElapsedMilliseconds} units={units}");
        }

        /// <summary>Forgets the cycle, the map and every order (open BG switched off or the server stopped).</summary>
        public static void Reset()
        {
            lock (Sync)
            {
                UnitStates.Clear();
                RecentWinners.Clear();
                UnresolvedLogged.Clear();
                _map = null;
                _cycle = null;
                _cycleIndex = 0;
                _logged = false;
                Volatile.Write(ref _snapshot, Snapshot.Empty);
            }
        }

        // Callers hold Sync.
        private static void AdvanceCycle(long now, eRealm owner, bool lordDown)
        {
            if (_cycle == null || now >= _cycle.EndTick)
            {
                if (_cycle != null)
                {
                    RecentWinners.Enqueue(CycleWinner(_ownerAtStart, owner, lordDown, _cycle.Attacker));
                    while (RecentWinners.Count > RecentCyclesKept) RecentWinners.Dequeue();
                }
                _cycle = NewCycle(Rng, OpenBattlegroundProperties.CycleMinMinutes, OpenBattlegroundProperties.CycleMaxMinutes, owner, RecentWins())
                    with { StartTick = now };
                _cycleIndex++;
                _ownerAtStart = owner;
            }
            OpenBgPhase phase = PhaseAt(_cycle, now);
            // The keep changed hands to the chosen attacker before the fight began: another realm attacks this cycle.
            if (owner != eRealm.None && owner == _cycle.Attacker && phase is OpenBgPhase.Quiet or OpenBgPhase.Push)
                _cycle = _cycle with { Attacker = PickAttacker(Rng, owner, RecentWins()) };
            if (_logged && phase == _loggedPhase && owner == _loggedOwner && _cycle.Attacker == _loggedAttacker) return;
            _logged = true;
            _loggedPhase = phase;
            _loggedOwner = owner;
            _loggedAttacker = _cycle.Attacker;
            Log.Info($"OPEN_BG_PHASE phase={Describe(phase)} attacker={_cycle.Attacker} cycle_s={_cycle.TotalMs / 1000} owner={owner} " +
                $"cycle={_cycleIndex} lord_down={lordDown} speed={OfflineWorldSpeedControl.AchievedMultiplier:0.0}");
        }

        private static Dictionary<eRealm, int> RecentWins() =>
            RecentWinners.Where(IsPlayerRealm).GroupBy(realm => realm).ToDictionary(group => group.Key, group => group.Count());

        // Callers hold Sync. Returns the number of units with an order.
        private static int UpdateOrders(BattlegroundDefinition definition, AbstractGameKeep keep, eRealm owner, bool lordDown, long now)
        {
            OpenBgPhase phase = PhaseAt(_cycle, now);
            eRealm attacker = _cycle.Attacker;
            eRealm roleOwner = RoleOwner(owner, lordDown, phase, attacker);
            AutonomousBattlegroundParticipation.OpenCrewMember[] inside = AutonomousBattlegroundParticipation.OpenCrew()
                .Where(member => member.Entered && !member.Leaving && member.Bot != null && member.Bot.CurrentRegionID == definition.RegionId)
                .ToArray();
            var orders = new Dictionary<int, OpenBgOrder>();
            Point3D[] humans = null;
            var units = inside.GroupBy(member => member.Unit).Select(unit => (Id: unit.Key, Members: unit.ToArray(), Leader: LeaderOf(unit.ToArray())))
                .Where(unit => unit.Leader != null).OrderBy(unit => unit.Id).ToArray();
            var groupIndex = new Dictionary<eRealm, int>();
            foreach (var unit in units)
            {
                GameBot leader = unit.Leader;
                eRealm realm = leader.Realm;
                bool grouped = OpenBattlegroundRoster.KindOf(unit.Id) is OpenBgUnitKind kind ? kind != OpenBgUnitKind.Solo : unit.Members.Length > 1;
                int index = 0;
                if (grouped)
                {
                    groupIndex.TryGetValue(realm, out index);
                    groupIndex[realm] = index + 1;
                }
                var ctx = new OpenBgContext(phase, attacker, roleOwner, realm, grouped ? Math.Max(2, unit.Members.Length) : 1, true);
                OpenBgRole role = OpenBattlegroundRoles.Assign(ctx, index);
                // Camlann: keep friendship is the claiming guild's. A unit the keep counts as a friend never assaults it.
                bool keepEnemy = keep != null && GameServer.KeepManager.IsEnemy(keep, leader);
                role = OpenBattlegroundRoles.ForKeepFriendship(role, keep != null, keepEnemy);

                if (!UnitStates.TryGetValue(unit.Id, out UnitState state)) UnitStates[unit.Id] = state = new UnitState();
                if (state.Point != null && state.ArrivedAt == 0 &&
                    leader.GetDistanceTo(state.Point) <= (state.Muster ? MusterDistance : ArrivalDistance)) state.ArrivedAt = now;
                bool roaming = role is OpenBgRole.Roam or OpenBgRole.Lurk;
                // An unreachable point is replaced, but not more often than every 30 game seconds.
                bool repick = state.Point == null || role != state.Role || state.Unreachable && now - state.PointSince >= UnreachableRepickMilliseconds ||
                    roaming && (state.ArrivedAt > 0 && now - state.ArrivedAt >= state.RestMilliseconds || state.ArrivedAt == 0 && now - state.PointSince >= PointTimeoutMilliseconds);
                // An attacker in the push musters at its own bridge first, then marches on the keep.
                bool muster = role == OpenBgRole.Assault && phase == OpenBgPhase.Push && state.MusteredCycle != _cycleIndex;
                if (muster && state.Muster && state.ArrivedAt > 0) { state.MusteredCycle = _cycleIndex; muster = false; }
                if (role == OpenBgRole.Assault && muster != state.Muster) repick = true;
                if (repick)
                {
                    humans ??= FieldHumans(definition);
                    Choose(state, unit.Members[0].Group, leader, unit.Members.Length, role, realm, phase, roleOwner, attacker, keepEnemy, muster, humans, now);
                }
                if (state.Point != null)
                    orders[unit.Id] = new OpenBgOrder(unit.Id, state.Role, state.Point, state.Muster, state.AtKeep, _cycleIndex);
            }
            foreach (int gone in UnitStates.Keys.Where(id => units.All(unit => unit.Id != id)).ToArray()) UnitStates.Remove(gone);
            Volatile.Write(ref _snapshot, new Snapshot { Phase = phase, Attacker = attacker, Owner = owner, Cycle = _cycleIndex, Orders = orders });
            return orders.Count;
        }

        // The unit's group leader when it is inside, else its longest-serving member.
        private static GameBot LeaderOf(AutonomousBattlegroundParticipation.OpenCrewMember[] members)
        {
            GameBot leader = members.Select(member => member.Bot).FirstOrDefault(bot => bot.Group != null && bot.Group.LivingLeader == bot);
            return leader ?? members.OrderBy(member => member.EnteredTick).Select(member => member.Bot).FirstOrDefault();
        }

        // Callers hold Sync. Picks the point for a (new) role, applies the once-per-cycle drift and logs the assignment.
        private static void Choose(UnitState state, string group, GameBot leader, int size, OpenBgRole role, eRealm realm, OpenBgPhase phase,
            eRealm owner, eRealm attacker, bool keepEnemy, bool muster, Point3D[] humans, long now)
        {
            MapData map = _map;
            Point3D[] nearKeep = map.Field.Where(point => OpenBattlegroundRoles.Distance2D(point, map.Keep) <= NearKeepDistance).ToArray();
            Point3D point;
            bool atKeep = false;
            string bridge = null;
            switch (role)
            {
                case OpenBgRole.Assault when muster:
                case OpenBgRole.BridgeWatch:
                case OpenBgRole.Retreat:
                    point = OpenBattlegroundRoles.RolePoint(OpenBgRole.BridgeWatch, realm, map.Bridges, map.Keep, map.Field, Rng);
                    bridge = OpenBattlegroundRoles.OwnBridge(realm, map.Bridges)?.Name;
                    break;
                case OpenBgRole.Assault:
                    // A group too small for the doors skirmishes near the keep; the siege needs four or more.
                    point = size >= AutonomousBattlegroundDriver.AssaultGroupMinimum ? null
                        : OpenBattlegroundRoles.RolePoint(OpenBgRole.Roam, realm, map.Bridges, map.Keep, nearKeep, Rng);
                    atKeep = point == null;
                    point ??= map.Keep;
                    break;
                case OpenBgRole.Defend:
                    // The keep's friends hold the keep; same-realm strangers its guards would shoot hold the approaches.
                    atKeep = !keepEnemy;
                    point = atKeep ? map.Keep : OpenBattlegroundRoles.RolePoint(OpenBgRole.Roam, realm, map.Bridges, map.Keep, nearKeep, Rng) ?? map.Keep;
                    break;
                case OpenBgRole.Lurk:
                    point = LurkPoint(leader.CurrentRegion, realm, map);
                    break;
                default:
                    // The third realm picks its fights near the keep while it is fought over.
                    bool third = phase == OpenBgPhase.KeepFight && realm != owner && realm != attacker;
                    point = OpenBattlegroundRoles.RolePoint(OpenBgRole.Roam, realm, map.Bridges, map.Keep, third ? nearKeep : map.Field, Rng) ??
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
                $"realm={realm} phase={Describe(phase)}{(muster ? " muster=true" : "")}{(bridge != null ? $" bridge={bridge}" : "")}{(drifted ? " drift=true" : "")}";
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

        // A lurk point with a floor; a few draws, then the nearest camp to the realm's bridge (logged once).
        private static Point3D LurkPoint(Region region, eRealm realm, MapData map)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Point3D raw = OpenBattlegroundRoles.RolePoint(OpenBgRole.Lurk, realm, map.Bridges, map.Keep, map.Field, Rng);
                if (raw != null && TryFloor(region?.GetZone(raw.X, raw.Y), raw, LurkFloorRange, out Point3D floor))
                    return floor;
            }
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
            Log.Info($"OPEN_BG_MAP region={definition.RegionId} bridges={string.Join(",", bridges.Select(bridge => $"{bridge.Name}:{bridge.Point.X},{bridge.Point.Y},{bridge.Point.Z}"))} " +
                $"field={field.Count} camps={camps.Length} landings={landings.Length}");
            return new MapData { Bridges = bridges.ToArray(), Keep = keepPoint, Field = field.ToArray(), Camps = camps, Landings = landings };
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
