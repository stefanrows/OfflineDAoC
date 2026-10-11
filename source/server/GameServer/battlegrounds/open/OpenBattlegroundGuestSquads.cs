using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>One live guest squad as the planner sees it. <paramref name="EmptySince"/> is 0 while a member lives.</summary>
    public sealed record GuestSquad(int Id, eRealm Realm, int Level, int Size, int Alive, bool InCombat, long LastLowHumanAt,
        long EmptySince = 0);

    public readonly record struct GuestSpawn(eRealm Realm, int Level, int Size);

    /// <param name="Reason">left (5 game minutes after the last human under 40), wiped (every member dead), level (the
    /// lowest human changed by more than the spread while the squad was out of combat).</param>
    public readonly record struct GuestDespawn(int Id, eRealm Realm, int Level, int Size, string Reason);

    /// <summary>The planner's decision for one tick.</summary>
    public sealed record GuestPlan(bool LowHumanInside, int LowestLevel, IReadOnlyList<GuestSpawn> Spawns,
        IReadOnlyList<GuestDespawn> Despawns);

    /// <summary>A guest squad as the /bgsetup window and the launcher see it.</summary>
    public sealed record OpenBgGuestStatus(string Realm, int Level, int Size, int Alive, string Target);

    /// <summary>
    /// Guest squads of the open BG (spec 2026-10-11 §2 "level gap", §8). The fixed crew is 40-50 and never changes
    /// level, so while a human under 40 is inside, each realm gets one non-persistent squad of 3-5 encounter actors on
    /// the lowest such human's level (±3). A squad starts at its realm's landing (the camp anchor just outside its own
    /// portal keep), walks into the human's half of the map and roams there; it goes for a human it comes near, the way
    /// a small 2003 roaming group would, instead of homing in from across the map. The squads are ordinary
    /// <see cref="BattlegroundEncounterActor"/> bots: never saved, no loot, no experience or realm points
    /// (<see cref="GameBot.SuppressLootAndProgress"/>), and hostile to everyone outside their own group (Camlann rules),
    /// so the three squads also fight each other. They despawn 5 game minutes after the last human under 40 left.
    /// Every timer runs in game time (<see cref="GameLoop.GameLoopTime"/>).
    /// </summary>
    public static class OpenBattlegroundGuestSquads
    {
        public const int TickMilliseconds = 30_000;
        /// <summary>Humans below this level get guest squads; the campaign's own squads never scale below it.</summary>
        public const int LevelLimit = 40;
        public const int LevelSpread = 3;
        public const int MinSize = 3;
        public const int MaxSize = 5;
        public const long LingerMilliseconds = 5 * 60_000;
        /// <summary>A wiped squad's realm waits this long before its next squad, so a squad is no farm.</summary>
        public const long RespawnMilliseconds = 2 * 60_000;
        /// <summary>A squad does not appear while a human under 40 stands this close to its landing.</summary>
        public const int SpawnClearance = 2_500;
        /// <summary>A squad goes straight for a human it comes this close to (outside sanctuaries).</summary>
        public const int ChaseRange = 2_500;
        public const int RoamMin = 800;
        public const int RoamMax = 3_500;
        /// <summary>Roam points keep this far from the central keep and its garrison.</summary>
        public const int KeepClearance = 1_800;
        public const int ArriveRadius = 400;
        /// <summary>A human resting in a sanctuary (his portal keep) is hunted this far out toward the central keep.</summary>
        public const int SanctuaryOffset = 4_000;

        public static readonly eRealm[] Realms = { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia };

        // Tank, healer, caster, damage per realm; a fifth member is a second tank.
        private static readonly IReadOnlyDictionary<eRealm, eCharacterClass[]> ClassSets = new Dictionary<eRealm, eCharacterClass[]>
        {
            [eRealm.Albion] = new[] { eCharacterClass.Armsman, eCharacterClass.Cleric, eCharacterClass.Wizard, eCharacterClass.Mercenary },
            [eRealm.Midgard] = new[] { eCharacterClass.Warrior, eCharacterClass.Healer, eCharacterClass.Runemaster, eCharacterClass.Berserker },
            [eRealm.Hibernia] = new[] { eCharacterClass.Hero, eCharacterClass.Druid, eCharacterClass.Eldritch, eCharacterClass.Blademaster },
        };

        private static readonly Logger Log = LoggerManager.Create(typeof(OpenBattlegroundGuestSquads));
        private static readonly object Sync = new();
        private static readonly List<Squad> Squads = new();
        private static readonly Dictionary<eRealm, string> SkipReasons = new();
        private static readonly Random Rng = new();
        private static IReadOnlyDictionary<eRealm, Point3D> _origins;
        private static int _nextId;

        private sealed class Squad
        {
            internal int Id;
            internal eRealm Realm;
            internal int Level;
            internal int Size;
            internal readonly List<GameBot> Members = new();
            internal long LastLowHumanAt;
            internal long EmptySince;
            internal Point3D Goal;
            internal Point3D LastHumanPoint;
            internal string Target;
        }

        // ---- pure rules -------------------------------------------------------------------------------------------

        public static bool IsGuestLevel(int level) => level >= 1 && level < LevelLimit;

        /// <summary>The level of a new squad: the lowest human under 40, ±3, within 1-50.</summary>
        public static int SquadLevel(int lowestHuman, Random rng) =>
            Math.Clamp(lowestHuman + rng.Next(-LevelSpread, LevelSpread + 1), 1, OpenBattlegroundPolicy.MaxLevel);

        public static int SquadSize(Random rng) => rng.Next(MinSize, MaxSize + 1);

        /// <summary>The class of member <paramref name="index"/> of a squad of <paramref name="realm"/>; the class sets the actor's realm.</summary>
        public static byte ClassFor(eRealm realm, int index)
        {
            eCharacterClass[] set = ClassSets.TryGetValue(realm, out eCharacterClass[] classes) ? classes : ClassSets[eRealm.Albion];
            return (byte)set[Math.Abs(index) % set.Length];
        }

        public static string NameFor(eRealm realm) => GlobalConstants.RealmToName(realm) + " skirmisher";

        /// <summary>
        /// The level a campaign patrol, ambush or siege squad of <paramref name="definition"/> gets for a participant of
        /// <paramref name="level"/>. Brackets clamp to their range as before; in the open BG the campaign's squads never
        /// go below 40, so the guest squads are the only squads on a low human's level.
        /// </summary>
        public static byte CampaignSquadLevel(BattlegroundDefinition definition, int level) =>
            definition?.Kind == BattlegroundDefinitionKind.Open
                ? (byte)Math.Clamp(level, LevelLimit, definition.MaxLevel)
                : (byte)Math.Clamp(level, definition?.MinLevel ?? 1, definition?.MaxLevel ?? OpenBattlegroundPolicy.MaxLevel);

        /// <summary>
        /// Whether a campaign patrol or ambush may pick this target. Brackets: anyone, as before. Open BG: humans of 40 or more
        /// (those under 40 are the guest squads') and never a crew bot: the level-50 patrols hunted the crew at its own arrival
        /// camps (live run 0.242.0, 32 squads in 22 minutes).
        /// </summary>
        public static bool CampaignSquadMayTarget(BattlegroundDefinition definition, bool isHuman, bool isCrewBot, int level) =>
            definition?.Kind != BattlegroundDefinitionKind.Open || (isHuman ? !IsGuestLevel(level) : !isCrewBot);

        /// <summary>open BG: a campaign squad's origin and goal keep this far (2D) from every arrival camp.</summary>
        public const int ArrivalCampClearance = 2_000;

        /// <summary>Brackets: any point. Open BG: a point farther than <see cref="ArrivalCampClearance"/> from every arrival camp.</summary>
        public static bool CampaignSquadPointAllowed(BattlegroundDefinition definition, Point3D point, IEnumerable<Point3D> arrivalCamps) =>
            definition?.Kind != BattlegroundDefinitionKind.Open ||
            point != null && (arrivalCamps ?? Array.Empty<Point3D>()).All(camp => camp == null || Distance2D(camp, point) > ArrivalCampClearance);

        /// <summary>
        /// Where a campaign squad starts. Brackets: its camp. Open BG: out in the field from the camp toward the central keep
        /// (<see cref="SanctuaryOffset"/>, at most half way), on the keep approach instead of at the crews' arrival camp.
        /// </summary>
        public static Point3D CampaignSquadOrigin(BattlegroundDefinition definition, Point3D camp, Point3D keep) =>
            definition?.Kind == BattlegroundDefinitionKind.Open ? HuntAnchor(camp, true, keep) : camp;

        /// <summary>
        /// One tick's decision. A realm with no squad gets one while a human under 40 is inside; a wiped squad is
        /// replaced <see cref="RespawnMilliseconds"/> after its last member died; an idle squad whose level is more than
        /// the spread off the lowest human is replaced; with no human under 40 inside, every squad goes
        /// <see cref="LingerMilliseconds"/> after it last saw one (a wiped one at once). The caller keeps
        /// <see cref="GuestSquad.LastLowHumanAt"/> current.
        /// </summary>
        public static GuestPlan Plan(IReadOnlyList<(eRealm realm, int level)> humansInside, IReadOnlyList<GuestSquad> active, long now,
            Random rng = null)
        {
            rng ??= Random.Shared;
            humansInside ??= Array.Empty<(eRealm, int)>();
            active ??= Array.Empty<GuestSquad>();
            int lowest = humansInside.Where(human => IsGuestLevel(human.level)).Select(human => human.level).DefaultIfEmpty(0).Min();
            bool low = lowest > 0;
            var despawns = new List<GuestDespawn>();
            var remaining = new List<GuestSquad>();
            foreach (GuestSquad squad in active)
            {
                string reason;
                if (!low)
                    reason = squad.Alive <= 0 ? "wiped" : now - squad.LastLowHumanAt >= LingerMilliseconds ? "left" : null;
                else if (squad.Alive <= 0)
                    reason = now - squad.EmptySince >= RespawnMilliseconds ? "wiped" : null;
                else
                    reason = !squad.InCombat && Math.Abs(squad.Level - lowest) > LevelSpread ? "level" : null;
                if (reason == null) remaining.Add(squad);
                else despawns.Add(new GuestDespawn(squad.Id, squad.Realm, squad.Level, squad.Size, reason));
            }
            var spawns = new List<GuestSpawn>();
            if (low)
                foreach (eRealm realm in Realms)
                    if (!remaining.Any(squad => squad.Realm == realm))
                        spawns.Add(new GuestSpawn(realm, SquadLevel(lowest, rng), SquadSize(rng)));
            return new GuestPlan(low, lowest, spawns, despawns);
        }

        /// <summary>The realm's own landing for each realm: the camp anchor outside its portal keep, else the landing itself.</summary>
        public static IReadOnlyDictionary<eRealm, Point3D> MergeOrigins(IReadOnlyList<eRealm> landingRealms, IReadOnlyList<Point3D> landings,
            IEnumerable<(int Index, Point3D Anchor)> camps)
        {
            var origins = new Dictionary<eRealm, Point3D>();
            if (landingRealms == null) return origins;
            foreach ((int index, Point3D anchor) in camps ?? Array.Empty<(int, Point3D)>())
                if (anchor != null && index >= 0 && index < landingRealms.Count && Realms.Contains(landingRealms[index]))
                    origins.TryAdd(landingRealms[index], anchor);
            for (int i = 0; i < landingRealms.Count && landings != null && i < landings.Count; i++)
                if (landings[i] != null && Realms.Contains(landingRealms[i]))
                    origins.TryAdd(landingRealms[i], landings[i]);
            return origins;
        }

        /// <summary>No human under 40 stands within <see cref="SpawnClearance"/> of the origin (no squad appears on top of one).</summary>
        public static bool OriginClear(Point3D origin, IEnumerable<Point3D> lowHumans) =>
            origin != null && (lowHumans ?? Array.Empty<Point3D>()).All(human => Distance2D(origin, human) > SpawnClearance);

        /// <summary>
        /// Whether <paramref name="point"/> lies in the human's half of the map: the side of the line through the central
        /// keep, square to the keep-human direction, that holds the human. Without a keep every point counts.
        /// </summary>
        public static bool InHumansHalf(Point3D keep, Point3D human, Point3D point)
        {
            if (keep == null || human == null || point == null) return true;
            long hx = human.X - keep.X, hy = human.Y - keep.Y, px = point.X - keep.X, py = point.Y - keep.Y;
            return hx * px + hy * py >= 0;
        }

        /// <summary>A roam point <see cref="RoamMin"/>-<see cref="RoamMax"/> from the human, in the human's half, clear of the keep.</summary>
        public static Point3D RoamPoint(Point3D human, Point3D keep, Random rng)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                double angle = rng.NextDouble() * Math.PI * 2;
                double distance = RoamMin + rng.NextDouble() * (RoamMax - RoamMin);
                var candidate = new Point3D((int)(human.X + Math.Cos(angle) * distance), (int)(human.Y + Math.Sin(angle) * distance), human.Z);
                if (InHumansHalf(keep, human, candidate) && (keep == null || Distance2D(candidate, keep) >= KeepClearance))
                    return candidate;
            }
            // Away from the keep through the human: always in the human's half.
            double dx = keep == null ? 1 : human.X - keep.X, dy = keep == null ? 0 : human.Y - keep.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1) { dx = 1; dy = 0; length = 1; }
            return new Point3D((int)(human.X + dx / length * RoamMin), (int)(human.Y + dy / length * RoamMin), human.Z);
        }

        /// <summary>
        /// Goal candidates in order of preference from <paramref name="from"/>: near the human two roam points first;
        /// far away legs of 6,000, 3,500 and 1,500 toward the human first (a long route may not prove), then a roam point.
        /// </summary>
        public static IReadOnlyList<Point3D> GoalCandidates(Point3D from, Point3D human, Point3D keep, Random rng)
        {
            var candidates = new List<Point3D>();
            double distance = Distance2D(from, human);
            bool near = distance <= RoamMax * 2;
            if (near)
            {
                candidates.Add(RoamPoint(human, keep, rng));
                candidates.Add(RoamPoint(human, keep, rng));
            }
            foreach (int leg in new[] { 6_000, 3_500, 1_500 })
                if (distance > leg + RoamMin)
                    candidates.Add(new Point3D((int)(from.X + (human.X - from.X) * leg / distance),
                        (int)(from.Y + (human.Y - from.Y) * leg / distance), from.Z + (int)((human.Z - from.Z) * leg / distance)));
            if (!near) candidates.Add(RoamPoint(human, keep, rng));
            return candidates;
        }

        /// <summary>
        /// Where a squad hunts for a human: around the human himself, or, while he rests in a sanctuary, a point up to
        /// <see cref="SanctuaryOffset"/> out toward the central keep (at most half way), so nobody camps a portal keep's door.
        /// </summary>
        public static Point3D HuntAnchor(Point3D human, bool inSanctuary, Point3D keep)
        {
            if (!inSanctuary || keep == null) return human;
            double distance = Distance2D(human, keep);
            if (distance < 1) return human;
            double step = Math.Min(SanctuaryOffset, distance / 2);
            return new Point3D((int)(human.X + (keep.X - human.X) * step / distance), (int)(human.Y + (keep.Y - human.Y) * step / distance),
                human.Z + (int)((keep.Z - human.Z) * step / distance));
        }

        /// <summary>A squad picks a new goal once it arrived or its goal no longer lies near the human it hunts.</summary>
        public static bool NeedsNewGoal(Point3D position, Point3D goal, Point3D human) =>
            goal == null || Distance2D(position, goal) <= ArriveRadius || (human != null && Distance2D(goal, human) > RoamMax + 1_000);

        public static double Distance2D(Point3D a, Point3D b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // ---- runtime ----------------------------------------------------------------------------------------------

        /// <summary>The live squads for the status page.</summary>
        public static OpenBgGuestStatus[] Status()
        {
            lock (Sync)
                return Squads.Select(squad => new OpenBgGuestStatus(GlobalConstants.RealmToName(squad.Realm), squad.Level, squad.Size,
                    squad.Members.Count(member => member.IsAlive), squad.Target)).ToArray();
        }

        /// <summary>Deletes every guest actor (server stop, campaign stop). Nothing was ever saved.</summary>
        public static void Reset()
        {
            lock (Sync)
            {
                foreach (Squad squad in Squads) Remove(squad, "stopped");
                Squads.Clear();
                SkipReasons.Clear();
                _origins = null;
            }
        }

        /// <summary>
        /// The 30-second guest pass for the open BG. <paramref name="camps"/> are the campaign's camp anchors by landing
        /// index; <paramref name="keep"/> is the central keep (null tolerated).
        /// </summary>
        public static void Tick(BattlegroundDefinition definition, IEnumerable<(int Index, Point3D Anchor)> camps, AbstractGameKeep keep, long now)
        {
            if (definition?.Kind != BattlegroundDefinitionKind.Open) return;
            if (!OpenBattlegroundPolicy.IsEnabled)
            {
                lock (Sync)
                {
                    foreach (Squad squad in Squads) Remove(squad, "disabled");
                    Squads.Clear();
                }
                return;
            }
            Region region = WorldMgr.GetRegion(definition.RegionId);
            if (region == null) return;
            GamePlayer[] humans = ClientService.Instance.GetPlayersOfRegion(region)
                .Where(player => player.ObjectState == GameObject.eObjectState.Active && player.CurrentRegionID == definition.RegionId).ToArray();
            GamePlayer[] low = humans.Where(player => IsGuestLevel(player.Level)).ToArray();
            Point3D keepPoint = keep == null ? null : new Point3D(keep.X, keep.Y, keep.Z);

            lock (Sync)
            {
                foreach (Squad squad in Squads)
                {
                    foreach (GameBot gone in squad.Members.Where(member => !member.IsAlive || member.ObjectState != GameObject.eObjectState.Active).ToArray())
                    {
                        gone.Delete();
                        squad.Members.Remove(gone);
                    }
                    if (squad.Members.Count == 0 && squad.EmptySince == 0) squad.EmptySince = Math.Max(1, now);
                    if (low.Length > 0) squad.LastLowHumanAt = now;
                }

                GuestPlan plan = Plan(humans.Select(player => (player.Realm, (int)player.Level)).ToArray(),
                    Squads.Select(squad => new GuestSquad(squad.Id, squad.Realm, squad.Level, squad.Size, squad.Members.Count,
                        squad.Members.Any(member => member.InCombat), squad.LastLowHumanAt, squad.EmptySince)).ToArray(), now, Rng);

                foreach (GuestDespawn despawn in plan.Despawns)
                {
                    Squad squad = Squads.FirstOrDefault(candidate => candidate.Id == despawn.Id);
                    if (squad == null) continue;
                    Remove(squad, despawn.Reason);
                    Squads.Remove(squad);
                }

                if (plan.Spawns.Count > 0)
                {
                    _origins ??= Origins(definition, camps);
                    Point3D[] lowPoints = low.Select(player => new Point3D(player)).ToArray();
                    foreach (GuestSpawn spawn in plan.Spawns)
                        TrySpawn(definition, region, spawn, lowPoints, low, keepPoint, now);
                }

                foreach (Squad squad in Squads)
                    Steer(squad, region, low, keepPoint);
            }
        }

        private static IReadOnlyDictionary<eRealm, Point3D> Origins(BattlegroundDefinition definition, IEnumerable<(int Index, Point3D Anchor)> camps)
        {
            GameLocation[] landings = BattlegroundCampaignCatalog.GetLandings(definition);
            IReadOnlyList<eRealm> realms = BattlegroundCampaignCatalog.GetLandingRealms(definition, landings.Length);
            return MergeOrigins(realms, landings.Select(landing => new Point3D(landing.X, landing.Y, landing.Z)).ToArray(), camps);
        }

        private static void TrySpawn(BattlegroundDefinition definition, Region region, GuestSpawn spawn, Point3D[] lowPoints,
            GamePlayer[] low, Point3D keep, long now)
        {
            if (_origins == null || !_origins.TryGetValue(spawn.Realm, out Point3D origin))
            {
                Skip(spawn, "no_origin");
                return;
            }
            if (!OriginClear(origin, lowPoints))
            {
                Skip(spawn, "origin_busy");
                return;
            }
            GamePlayer human = low.OrderBy(player => Distance2D(origin, new Point3D(player))).FirstOrDefault();
            if (human == null) return;
            // Prove a first goal before any actor exists, as the campaign's squads do.
            Point3D goal = null;
            string failure = null;
            Point3D hunt = HuntAnchor(new Point3D(human), PvpCombatant.IsSafeArea(human), keep);
            foreach (Point3D candidate in GoalCandidates(origin, hunt, keep, Rng))
            {
                Point3D floor = Floor(region, candidate);
                if (floor == null || InPortalKeep(region, floor)) { failure ??= "goal_floor"; continue; }
                string route = BattlegroundEncounterActor.RouteFailure(definition.RegionId, origin, floor);
                if (route == null) { goal = floor; break; }
                failure = "route:" + route;
            }
            if (goal == null)
            {
                Skip(spawn, failure ?? "route:none");
                return;
            }

            var squad = new Squad { Id = ++_nextId, Realm = spawn.Realm, Level = spawn.Level, Size = spawn.Size, LastLowHumanAt = now,
                Goal = goal, LastHumanPoint = hunt, Target = human.Name };
            Group group = null;
            string spawnFailure = null;
            for (int i = 0; i < spawn.Size; i++)
            {
                GameBot actor = BattlegroundEncounterActor.TrySpawn(definition.RegionId, origin.X, origin.Y, origin.Z,
                    ClassFor(spawn.Realm, i), (byte)spawn.Level, NameFor(spawn.Realm), null, group, out string actorFailure);
                if (actor == null)
                {
                    spawnFailure = actorFailure;
                    if (squad.Members.Count == 0) break;
                    continue;
                }
                if (group == null) { group = new Group(actor); group.AddMember(actor); }
                squad.Members.Add(actor);
                BattlegroundEncounterActor.SetGoal(actor, goal);
            }
            if (squad.Members.Count == 0)
            {
                Skip(spawn, "spawn:" + (spawnFailure ?? "none"));
                return;
            }
            Squads.Add(squad);
            SkipReasons.Remove(spawn.Realm);
            Log.Info($"OPEN_BG_GUEST realm={GlobalConstants.RealmToName(spawn.Realm)} level={spawn.Level} size={squad.Members.Count} action=spawn " +
                $"planned={spawn.Size} origin={Format(origin)} goal={Format(goal)} target={human.Name} target_level={human.Level}");
        }

        // Once in a while a squad looks for its human: straight at one it is near, else a roam point in the human's half.
        private static void Steer(Squad squad, Region region, GamePlayer[] low, Point3D keep)
        {
            GameBot leader = squad.Members.FirstOrDefault(member => member.IsAlive && member.ObjectState == GameObject.eObjectState.Active);
            if (leader == null || leader.InCombat) return;
            Point3D from = new(leader);
            // Prefer a human out in the field; one resting in his portal keep is hunted from a distance.
            GamePlayer human = low.Where(player => player.IsAlive).OrderBy(player => PvpCombatant.IsSafeArea(player))
                .ThenBy(player => Distance2D(from, new Point3D(player))).FirstOrDefault();
            bool safe = human != null && PvpCombatant.IsSafeArea(human);
            if (human != null)
            {
                squad.LastHumanPoint = HuntAnchor(new Point3D(human), safe, keep);
                squad.Target = human.Name;
            }
            Point3D anchor = squad.LastHumanPoint;
            if (anchor == null) return;
            if (human != null && !safe && Distance2D(from, anchor) <= ChaseRange)
            {
                foreach (GameBot member in squad.Members) BattlegroundEncounterActor.SetGoal(member, anchor, human);
                squad.Goal = anchor;
                return;
            }
            if (!NeedsNewGoal(from, squad.Goal, anchor)) return;
            foreach (Point3D candidate in GoalCandidates(from, anchor, keep, Rng))
            {
                Point3D floor = Floor(region, candidate);
                if (floor == null || InPortalKeep(region, floor)) continue;
                if (BattlegroundEncounterActor.RouteFailure(region.ID, from, floor) != null) continue;
                foreach (GameBot member in squad.Members) BattlegroundEncounterActor.SetGoal(member, floor);
                squad.Goal = floor;
                return;
            }
        }

        internal static Point3D Floor(Region region, Point3D point)
        {
            Zone zone = region.GetZone(point.X, point.Y);
            var nav = PathfindingProvider.Instance;
            if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return null;
            Vector3? floor = nav.GetClosestPoint(zone, new Vector3(point.X, point.Y, point.Z), 256, 256, 2048, nav.DefaultFilters);
            return floor.HasValue ? new Point3D(floor.Value.X, floor.Value.Y, floor.Value.Z) : null;
        }

        private static bool InPortalKeep(Region region, Point3D point) =>
            region.GetAreasOfSpot(point).OfType<KeepArea>().Any(area => area.Keep?.IsPortalKeep == true);

        private static void Remove(Squad squad, string reason)
        {
            foreach (GameBot member in squad.Members.ToArray()) member.Delete();
            squad.Members.Clear();
            Log.Info($"OPEN_BG_GUEST realm={GlobalConstants.RealmToName(squad.Realm)} level={squad.Level} size={squad.Size} action=despawn reason={reason}");
        }

        private static void Skip(GuestSpawn spawn, string reason)
        {
            if (SkipReasons.TryGetValue(spawn.Realm, out string previous) && previous == reason) return;
            SkipReasons[spawn.Realm] = reason;
            Log.Info($"OPEN_BG_GUEST realm={GlobalConstants.RealmToName(spawn.Realm)} level={spawn.Level} size={spawn.Size} action=skip reason={reason}");
        }

        private static string Format(Point3D point) => $"{point.X},{point.Y},{point.Z}";
    }
}
