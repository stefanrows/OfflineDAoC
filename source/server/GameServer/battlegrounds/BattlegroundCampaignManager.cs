using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using DOL.Database;
using DOL.Events;
using DOL.GS.Keeps;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using DOL.GS.ServerRules;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>Bounded, physical guild/group encounters. No realm-side assignment or simulated captures.</summary>
    public static class BattlegroundCampaignManager
    {
        public const string TokenTemplateId = "offline_bg_siege_token";
        public const int FundingThreshold = 20;
        private const int CaptainRespawnMinutes = 5;
        private const int MaximumActorsPerRegion = 24;
        private const int ParticipantChaseDistance = 400;
        private const int OccupiedSquadDelayMs = 15_000;
        private const long SlowTickMilliseconds = 50;
        private static readonly Logger Log = LoggerManager.Create(typeof(BattlegroundCampaignManager));
        private static readonly object Gate = new();
        private static readonly Dictionary<ushort, Campaign> Campaigns = new();
        private static DbItemTemplate _token;
        private static bool _running;
        private static long Now => WorldSimulationClock.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;

        private sealed class Campaign
        {
            internal BattlegroundDefinition Definition;
            internal AbstractGameKeep Keep;
            internal readonly List<Camp> Camps = new();
            internal readonly List<Encounter> Encounters = new();
            internal readonly HashSet<string> LordParticipants = new(StringComparer.Ordinal);
            internal readonly Dictionary<string, long> KillCredits = new(StringComparer.Ordinal);
            internal long NextSquadAt;
            internal long NextAmbushAt;
            internal ECSGameTimer Timer;
            internal int Rotation;
            internal long LordSpawnTick;
            internal bool HasMonsterObjectives;
            internal int LastHumans;
            internal readonly Dictionary<string, string> SquadSkipReasons = new(StringComparer.Ordinal);
        }
        private sealed class Camp
        {
            internal Campaign Campaign;
            internal int Index;
            internal Point3D Position;
            internal BattlegroundCampCommander Commander;
            internal GameBot Captain;
            internal Guild Guild;
            internal GamePlayer GroupSponsor;
            internal GamePlayer AllianceSponsor;
            internal Group SponsorGroup;
            internal DbBattlegroundCampaignCamp Row;
            internal int Tokens;
            internal long RespawnAt;
            internal long SponsorExpiresAt;
            internal Encounter Assault;
        }
        private sealed class Encounter
        {
            internal readonly List<GameBot> Members = new();
            internal long ExpiresAt;
            internal bool Siege;
            internal Camp Camp;
            internal Point3D Goal;
            internal long NextGoalAt;
            internal byte Level;
            internal GameLiving Participant;
        }

        [GameServerStartedEvent]
        public static void Start(DOLEvent e, object sender, EventArgs args)
        {
            if (!BattlegroundCampaignPolicy.IsEnabled) return;
            lock (Gate)
            {
                if (_running) return;
                _running = true;
                try
                {
                    EnsureToken();
                    foreach (BattlegroundDefinition definition in BattlegroundCampaignCatalog.Definitions)
                        Initialize(definition);
                    GameEventMgr.AddHandler(GameLivingEvent.EnemyKilled, new DOLEventHandler(OnEnemyKilled));
                }
                catch (Exception exception)
                {
                    Log.Error("BATTLEGROUND_CAMPAIGN_START_FAILED", exception);
                    Stop(e, sender, args);
                }
            }
        }

        [GameServerStoppedEvent]
        public static void Stop(DOLEvent e, object sender, EventArgs args)
        {
            lock (Gate)
            {
                _running = false;
                GameEventMgr.RemoveHandler(GameLivingEvent.EnemyKilled, new DOLEventHandler(OnEnemyKilled));
                foreach (Campaign campaign in Campaigns.Values)
                {
                    campaign.Timer?.Stop();
                    foreach (Encounter encounter in campaign.Encounters) Cleanup(encounter);
                    foreach (Camp camp in campaign.Camps)
                    {
                        camp.Captain?.Delete();
                        camp.Commander?.Delete();
                    }
                }
                Campaigns.Clear();
            }
        }

        private static void EnsureToken()
        {
            _token = GameServer.Database.FindObjectByKey<DbItemTemplate>(TokenTemplateId);
            if (_token != null) return;
            _token = new DbItemTemplate
            {
                Id_nb = TokenTemplateId, Name = "Battleground siege token", Model = 499,
                Object_Type = 0, Item_Type = 0, Level = 1, Weight = 0, MaxCount = 100,
                IsTradable = false, IsDropable = false, IsPickable = false,
                CanDropAsLoot = false, IsIndestructible = false, Price = 0
            };
            if (!GameServer.Database.AddObject(_token)) throw new InvalidOperationException("Cannot save battleground token template.");
        }

        // Restores a funded camp only for a real guild sponsor. Unsponsored camps used to
        // restore an expiry too, so every camp reset in the same tick after a restart.
        public static bool ShouldRestoreSponsor(string guildId, bool realGuild, DateTime expiresAt, DateTime now) =>
            realGuild && !string.IsNullOrEmpty(guildId) && expiresAt > now;

        // Nine campaign timers would otherwise start, and fire squads and captains, together.
        public static int StaggerMs(int index) => index * 1_100;

        public static int NearestIndex(IReadOnlyList<Point3D> camps, int x, int y)
        {
            int nearest = -1;
            long best = long.MaxValue;
            for (int i = 0; i < camps.Count; i++)
            {
                long dx = camps[i].X - x, dy = camps[i].Y - y;
                long distance = dx * dx + dy * dy;
                if (distance >= best) continue;
                best = distance;
                nearest = i;
            }
            return nearest;
        }

        // A patrol always walks in from a camp other than the one nearest its participant.
        public static int PatrolOriginIndex(int nearest, int count, int rotation)
        {
            if (count <= 1) return 0;
            int offset = 1 + Math.Abs(rotation % (count - 1));
            return ((nearest < 0 ? 0 : nearest) + offset) % count;
        }

        private static int DefinitionIndex(BattlegroundDefinition definition) =>
            Math.Max(0, BattlegroundCampaignCatalog.Definitions.ToList().FindIndex(candidate => candidate.RegionId == definition.RegionId));

        private static void Initialize(BattlegroundDefinition definition)
        {
            AbstractGameKeep keep = BattlegroundCampaignCatalog.CentralKeep(definition);
            GameLocation[] landings = BattlegroundCampaignCatalog.GetLandings(definition);
            if (!BattlegroundCampaignPolicy.IsReady(definition, out _) || landings.Length == 0) return;
            if (keep != null) BattlegroundNativeKeepData.EnsureDoors(definition, keep);
            Campaign campaign = new() { Definition = definition, Keep = keep, HasMonsterObjectives =
                WorldMgr.GetNPCsFromRegion(definition.RegionId).Any(npc => npc is not GameBot && npc is not GameKeepGuard &&
                    (npc.Flags & GameNPC.eFlags.PEACE) == 0 && npc.RewardStatus == GameNPC.RewardEligibility.Eligible) };
            long now = Now;
            int stagger = StaggerMs(DefinitionIndex(definition));
            campaign.NextSquadAt = now + 60_000 + stagger;
            campaign.NextAmbushAt = now + 180_000 + stagger;
            Campaigns.Add(definition.RegionId, campaign);
            // Camp anchors start from actual portal landings, then prove a native
            // walking route to a point outside every portal safe area.
            for (int i = 0; i < Math.Min(3, landings.Length); i++)
            {
                GameLocation landing = landings[i];
                if (!TryCampAnchor(definition, landing, keep, landings[(i + 1) % landings.Length], out Point3D anchor)) continue;
                Camp camp = new() { Campaign = campaign, Index = i, Position = anchor };
                camp.Row = GameServer.Database.SelectObject<DbBattlegroundCampaignCamp>(DB.Column("CampKey").IsEqualTo($"{definition.RegionId}:{i}"));
                if (camp.Row == null)
                {
                    camp.Row = new DbBattlegroundCampaignCamp { CampKey = $"{definition.RegionId}:{i}" };
                    GameServer.Database.AddObject(camp.Row);
                }
                Guild sponsor = GuildMgr.GetGuildByGuildID(camp.Row.SponsorGuildId);
                if (ShouldRestoreSponsor(camp.Row.SponsorGuildId, PvpCombatant.IsRealGuild(sponsor), camp.Row.ExpiresAt, WorldSimulationClock.UtcNow))
                {
                    camp.Guild = sponsor;
                    camp.Tokens = Math.Clamp(camp.Row.Tokens, 0, FundingThreshold - 1);
                    camp.SponsorExpiresAt = now + (long)(camp.Row.ExpiresAt - WorldSimulationClock.UtcNow).TotalMilliseconds;
                }
                if (camp.Guild == null) SaveFunding(camp, 0);
                camp.Commander = new BattlegroundCampCommander(definition.RegionId, new GameLocation("Camp", definition.RegionId, anchor.X, anchor.Y, anchor.Z), i);
                if (!camp.Commander.AddToWorld()) continue;
                campaign.Camps.Add(camp);
                if (camp.Row.CaptainRespawnAt > WorldSimulationClock.UtcNow)
                    camp.RespawnAt = now + (long)(camp.Row.CaptainRespawnAt - WorldSimulationClock.UtcNow).TotalMilliseconds;
                else SpawnCaptain(camp);
            }
            if (campaign.Camps.Count == 0)
            {
                Campaigns.Remove(definition.RegionId);
                return;
            }
            if (keep != null && BattlegroundNativeKeepData.IsOfflineKeep(keep))
            {
                // A failed keep must not stop the campaign start for every map.
                try { BattlegroundNativeKeepData.EnsureGarrison(definition, keep, campaign.Camps.Select(camp => camp.Position).ToArray(), BattlegroundNativeKeepData.GarrisonSearchRadius(keep)); }
                catch (Exception exception) { Log.Error($"BATTLEGROUND_NATIVE_KEEP_FAILED region={definition.RegionId}", exception); }
            }
            campaign.Timer = new ECSGameTimer(campaign.Camps[0].Commander, _ => Tick(campaign), 1000 + stagger);
            Log.Info($"BATTLEGROUND_CAMPAIGN_READY region={definition.RegionId} camps={campaign.Camps.Count} keep={(keep == null ? "none" : keep.KeepID.ToString())} lord={NativeLordReady(campaign)} doors={keep?.Doors.Count ?? 0} monsters={campaign.HasMonsterObjectives}");
        }

        private static int Tick(Campaign campaign)
        {
            lock (Gate)
            {
                if (!_running) return 0;
                if (!BattlegroundCampaignPolicy.IsEnabled)
                {
                    Stop(GameServerEvent.Stopped, null, EventArgs.Empty);
                    return 0;
                }
                Stopwatch total = Stopwatch.StartNew();
                Stopwatch phase = Stopwatch.StartNew();
                long captainsMs = 0, encountersMs = 0, squadsMs = 0;
                try
                {
                    long now = Now;
                    GuardLord liveLord = campaign.Keep?.Guards.Values.OfType<GuardLord>().FirstOrDefault(lord => lord.IsAlive && lord.ObjectState == GameObject.eObjectState.Active);
                    if (liveLord != null && liveLord.SpawnTick != campaign.LordSpawnTick)
                    { campaign.LordParticipants.Clear(); campaign.LordSpawnTick = liveLord.SpawnTick; }
                    // At most one captain per campaign per tick; the other due camps wait for later ticks.
                    bool captainSpawned = false;
                    foreach (Camp camp in campaign.Camps)
                    {
                        if (camp.Captain != null && (!camp.Captain.IsAlive || camp.Captain.ObjectState != GameObject.eObjectState.Active))
                        {
                            camp.Captain.Delete();
                            camp.Captain = null;
                            camp.RespawnAt = now + CaptainRespawnMinutes * 60_000;
                            camp.Row.CaptainRespawnAt = WorldSimulationClock.UtcNow.AddMinutes(CaptainRespawnMinutes);
                            GameServer.Database.SaveObject(camp.Row);
                        }
                        if (camp.Captain == null && now >= camp.RespawnAt && !captainSpawned)
                        {
                            captainSpawned = true;
                            SpawnCaptain(camp);
                        }
                        if ((camp.SponsorExpiresAt > 0 && now >= camp.SponsorExpiresAt) ||
                            (camp.GroupSponsor != null && camp.GroupSponsor.Group != camp.SponsorGroup))
                            ResetSponsor(camp);
                    }
                    captainsMs = phase.ElapsedMilliseconds;
                    phase.Restart();
                    foreach (Encounter encounter in campaign.Encounters.ToArray())
                    {
                        foreach (GameBot dead in encounter.Members.Where(member => member.ObjectState != GameObject.eObjectState.Active || !member.IsAlive).ToArray())
                        { dead.Delete(); encounter.Members.Remove(dead); }
                        if (now >= encounter.ExpiresAt || encounter.Members.Count == 0)
                        {
                            Cleanup(encounter);
                            campaign.Encounters.Remove(encounter);
                            if (encounter.Camp != null) encounter.Camp.Assault = null;
                            continue;
                        }
                        if (now < encounter.NextGoalAt) continue;
                        encounter.NextGoalAt = now + 5000;
                        // Siege approaches are unchanged. Patrols and ambushes re-path only when
                        // their live participant has moved away from the current goal.
                        if (!encounter.Siege && !RefreshParticipantGoal(campaign, encounter)) continue;
                        foreach (GameBot member in encounter.Members)
                        {
                            Point3D approach = null;
                            GameLiving target = encounter.Siege ? SiegeTarget(campaign, member, out approach) : null;
                            Point3D goal = target == null ? encounter.Goal : approach;
                            BattlegroundEncounterActor.SetGoal(member, goal, target);
                        }
                    }
                    encountersMs = phase.ElapsedMilliseconds;
                    phase.Restart();
                    int humans = CountHumans(campaign);
                    if ((humans > 0) != (campaign.LastHumans > 0))
                        Log.Info($"BATTLEGROUND_OCCUPANCY region={campaign.Definition.RegionId} humans={humans}");
                    if (humans > 0 && campaign.LastHumans == 0)
                        campaign.NextSquadAt = Math.Min(campaign.NextSquadAt, now + OccupiedSquadDelayMs);
                    campaign.LastHumans = humans;
                    if (now >= campaign.NextSquadAt)
                    {
                        campaign.NextSquadAt = now + 120_000;
                        SpawnSquad(campaign, false);
                    }
                    if (now >= campaign.NextAmbushAt)
                    {
                        campaign.NextAmbushAt = now + 5 * 60_000;
                        SpawnSquad(campaign, true);
                    }
                    if (campaign.KillCredits.Count > 2048)
                        foreach (string key in campaign.KillCredits.Where(pair => now - pair.Value > 300_000).Select(pair => pair.Key).ToArray())
                            campaign.KillCredits.Remove(key);
                    if (campaign.KillCredits.Count > 4096)
                        foreach (string key in campaign.KillCredits.OrderBy(pair => pair.Value).Take(campaign.KillCredits.Count - 4096).Select(pair => pair.Key).ToArray()) campaign.KillCredits.Remove(key);
                    squadsMs = phase.ElapsedMilliseconds;
                }
                catch (Exception exception) { Log.Error($"BATTLEGROUND_CAMPAIGN_TICK_FAILED region={campaign.Definition.RegionId}", exception); }
                if (total.ElapsedMilliseconds > SlowTickMilliseconds)
                    Log.Warn($"BATTLEGROUND_TICK_SLOW region={campaign.Definition.RegionId} ms={total.ElapsedMilliseconds} captains_ms={captainsMs} encounters_ms={encountersMs} squads_ms={squadsMs} actors={ActorCount(campaign)}");
                return 1000;
            }
        }

        private static void SpawnCaptain(Camp camp)
        {
            if (ActorCount(camp.Campaign) >= MaximumActorsPerRegion) return;
            camp.Captain = BattlegroundEncounterActor.Spawn(camp.Campaign.Definition.RegionId,
                camp.Position.X, camp.Position.Y, camp.Position.Z, (byte)eCharacterClass.Armsman,
                (byte)camp.Campaign.Definition.MaxLevel, $"{camp.Campaign.Definition.Name} Camp Captain", camp.Guild);
            if (camp.Captain == null) { camp.RespawnAt = Now + 60_000; return; }
            if (camp.AllianceSponsor != null) BattlegroundEncounterActor.SetAllianceSponsor(camp.Captain, camp.AllianceSponsor);
            if (camp.Guild == null && camp.GroupSponsor == null) camp.Captain.Flags |= GameNPC.eFlags.PEACE;
            BattlegroundEncounterActor.SetGoal(camp.Captain, camp.Position);
            camp.Row.CaptainRespawnAt = DateTime.MinValue;
            GameServer.Database.SaveObject(camp.Row);
        }

        private static int ActorCount(Campaign campaign) => campaign.Encounters.Sum(encounter => encounter.Members.Count) + campaign.Camps.Count(camp => camp.Captain != null);

        private static GameLiving SiegeTarget(Campaign campaign, GameBot member, out Point3D approach)
        {
            approach = null;
            if (campaign.Keep == null || !GameServer.KeepManager.IsEnemy(campaign.Keep, member)) return null;
            GameKeepDoor[] doors = campaign.Keep.Doors.Values
                .Where(candidate => candidate.IsAlive && candidate.State == eDoorState.Closed && GameServer.ServerRules.IsAllowedToAttack(member, candidate, true))
                .OrderBy(candidate => member.GetDistanceTo(candidate)).ToArray();
            foreach (GameKeepDoor door in doors)
                if (TryApproach(member, door, out approach)) return door;
            // A remaining closed hostile door always precedes the lord, even if
            // navigation cannot prove its approach. Never bypass it by attacking upstairs.
            if (doors.Length > 0) return null;
            GuardLord lord = campaign.Keep.Guards.Values.OfType<GuardLord>()
                .FirstOrDefault(candidate => candidate.IsAlive && candidate.ObjectState == GameObject.eObjectState.Active && GameServer.ServerRules.IsAllowedToAttack(member, candidate, true));
            return lord != null && TryApproach(member, lord, out approach) ? lord : null;
        }

        private static bool TryApproach(GameBot member, GameLiving target, out Point3D approach)
        {
            approach = null;
            var nav = PathfindingProvider.Instance;
            Zone zone = member.CurrentZone;
            if (zone == null || target.CurrentZone != zone || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;
            Span<WrappedPathfindingNode> nodes = stackalloc WrappedPathfindingNode[512];
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4;
                Vector3 candidate = new(target.X + (float)(Math.Cos(angle) * 180), target.Y + (float)(Math.Sin(angle) * 180), target.Z);
                if (!nav.TrySnapToMesh(zone, ref candidate, 100) || Math.Abs(candidate.Z - target.Z) > 100) continue;
                if (nav.GetPathStraight(zone, new(member.X, member.Y, member.Z), candidate, nav.BlockingDoorAvoidanceFilters, nodes).Status != PathfindingStatus.PathFound) continue;
                approach = new Point3D(candidate.X, candidate.Y, candidate.Z);
                return true;
            }
            return false;
        }

        private static bool TryCampAnchor(BattlegroundDefinition definition, GameLocation landing, AbstractGameKeep keep, GameLocation alternate, out Point3D anchor)
        {
            anchor = null;
            Region region = WorldMgr.GetRegion(definition.RegionId);
            Zone zone = region?.GetZone(landing.X, landing.Y);
            var nav = PathfindingProvider.Instance;
            if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;
            Vector3 start = new(landing.X, landing.Y, landing.Z);
            if (!nav.TrySnapToMesh(zone, ref start, 100)) return false;
            // Match the portal keep by its area: a snapped landing never equals the keep's exact centre.
            AbstractGameKeep portal = region.GetAreasOfSpot(new Point3D(landing.X, landing.Y, landing.Z)).OfType<KeepArea>()
                .Select(area => area.Keep).FirstOrDefault(candidate => candidate?.IsPortalKeep == true);
            int radius = (portal?.Area as Area.Circle)?.Radius ?? 4000;
            double direction = Math.Atan2((keep?.Y ?? alternate.Y) - landing.Y, (keep?.X ?? alternate.X) - landing.X);
            if (FindCampAnchor(region, zone, nav, landing, start, direction, radius, nav.BlockingDoorAvoidanceFilters, out anchor)) return true;
            // Bug 122: the native portal keep's closed gates are excluded by the blocking-door
            // filter, which leaves no route. Only when such a closed gate exists, accept the
            // ordinary route to a point proved outside every portal area.
            int closedDoors = portal?.Doors.Values.Count(door => door.State == eDoorState.Closed) ?? 0;
            if (closedDoors > 0 && FindCampAnchor(region, zone, nav, landing, start, direction, radius, nav.DefaultFilters, out anchor))
            {
                Log.Info($"BATTLEGROUND_CAMP_DOOR_ROUTE region={definition.RegionId} landing={landing.Name} doors={closedDoors}");
                return true;
            }
            Log.Warn($"BATTLEGROUND_CAMP_UNAVAILABLE region={definition.RegionId} landing={landing.Name} reason=no_proved_outside_camp");
            return false;
        }

        private static bool FindCampAnchor(Region region, Zone zone, IPathfindingMgr nav, GameLocation landing, Vector3 start, double direction, int radius, EDtPolyFlags[] filters, out Point3D anchor)
        {
            anchor = null;
            Span<WrappedPathfindingNode> nodes = stackalloc WrappedPathfindingNode[512];
            foreach (int offset in new[] { 0, 1, -1, 2, -2, 3, -3, 4 })
            {
                double angle = direction + offset * Math.PI / 4;
                Vector3 candidate = new(landing.X + (float)(Math.Cos(angle) * (radius + 500)), landing.Y + (float)(Math.Sin(angle) * (radius + 500)), landing.Z);
                if (region.GetZone((int)candidate.X, (int)candidate.Y) != zone) continue;
                Vector3? floor = nav.GetClosestPoint(zone, candidate, 128, 128, 2048, nav.DefaultFilters);
                if (!floor.HasValue || Vector2.Distance(new(candidate.X, candidate.Y), new(floor.Value.X, floor.Value.Y)) > 128) continue;
                candidate = floor.Value;
                Point3D point = new(candidate.X, candidate.Y, candidate.Z);
                if (region.GetAreasOfSpot(point).OfType<KeepArea>().Any(area => area.Keep?.IsPortalKeep == true)) continue;
                if (nav.GetPathStraight(zone, start, candidate, filters, nodes).Status != PathfindingStatus.PathFound) continue;
                anchor = point;
                return true;
            }
            return false;
        }

        private static GameLiving[] LocalParticipants(Campaign campaign)
        {
            Point3D origin = campaign.Camps[0].Position;
            return WorldMgr.GetPlayersCloseToSpot(campaign.Definition.RegionId, origin.X, origin.Y, origin.Z, ushort.MaxValue)
                .Cast<GameLiving>().Concat(AutonomousBotRegistry.Snapshot().Where(bot => bot.IsAutonomousWorldBot && bot.CurrentRegionID == campaign.Definition.RegionId))
                .Where(actor => actor.IsAlive && actor.ObjectState == GameObject.eObjectState.Active && IsEligible(actor)).Distinct().ToArray();
        }

        private static int CountHumans(Campaign campaign)
        {
            Point3D origin = campaign.Camps[0].Position;
            return WorldMgr.GetPlayersCloseToSpot(campaign.Definition.RegionId, origin.X, origin.Y, origin.Z, ushort.MaxValue).Count;
        }

        // Sanctuaries and portal-keep interiors are never a patrol goal, so landed players are not chased inside.
        private static bool OutsideSanctuary(GameLiving participant, Point3D point)
        {
            if (PvpCombatant.IsSafeArea(participant)) return false;
            Region region = WorldMgr.GetRegion(participant.CurrentRegionID);
            return region != null && !region.GetAreasOfSpot(point).OfType<KeepArea>().Any(area => area.Keep?.IsPortalKeep == true);
        }

        private static bool RefreshParticipantGoal(Campaign campaign, Encounter encounter)
        {
            GameLiving participant = encounter.Participant;
            if (participant == null || !participant.IsAlive || participant.ObjectState != GameObject.eObjectState.Active ||
                participant.CurrentRegionID != campaign.Definition.RegionId || participant.GetDistanceTo(encounter.Goal) <= ParticipantChaseDistance) return false;
            Point3D target = new(participant);
            if (!OutsideSanctuary(participant, target)) return false;
            encounter.Goal = target;
            return true;
        }

        private static void LogSquadSkipped(Campaign campaign, string kind, string reason, int participants)
        {
            if (campaign.SquadSkipReasons.TryGetValue(kind, out string previous) && previous == reason) return;
            campaign.SquadSkipReasons[kind] = reason;
            Log.Info($"BATTLEGROUND_SQUAD_SKIPPED region={campaign.Definition.RegionId} kind={kind} reason={reason} participants={participants} actors={ActorCount(campaign)}/{MaximumActorsPerRegion}");
        }

        private static string FormatPoint(Point3D point) => $"{point.X},{point.Y},{point.Z}";

        private static void SpawnSquad(Campaign campaign, bool ambush)
        {
            string kind = ambush ? "ambush" : "patrol";
            GameLiving[] participants = LocalParticipants(campaign);
            if (participants.Length == 0) { LogSquadSkipped(campaign, kind, "no_participants", 0); return; }
            GameLiving participant = participants[campaign.Rotation++ % participants.Length];
            if (ambush)
            {
                participant = participants.FirstOrDefault(actor => !PvpCombatant.IsSafeArea(actor) && !PvpCombatant.IsInvulnerableToAttack(actor) &&
                    (actor is not GamePlayer human || human.IsDoingQuest(typeof(BattlegroundCampaignQuest)) is BattlegroundCampaignQuest quest && quest.Region == campaign.Definition.RegionId));
                if (participant == null) { LogSquadSkipped(campaign, kind, "no_ambush_target", participants.Length); return; }
            }
            int localParty = participant.Group?.GetMembersInTheGroup().Count(member => member.CurrentRegionID == campaign.Definition.RegionId && IsEligible(member)) ?? 1;
            int size = Math.Clamp(localParty, 1, 8);
            if (ActorCount(campaign) + size > MaximumActorsPerRegion) { LogSquadSkipped(campaign, kind, "actor_cap", participants.Length); return; }
            // Effective current allies count against a guild's local presence.
            // Rotate ties, and select actual underrepresented autonomous guilds.
            GameBot[] representatives = AutonomousBotRegistry.Snapshot()
                .Where(bot => bot.IsAutonomousWorldBot && PvpCombatant.IsRealGuild(bot.Guild))
                .GroupBy(bot => bot.Guild.GuildID).Select(group => group.First()).Take(15).ToArray();
            Guild guild = null;
            if (representatives.Length > 0)
            {
                GameBot candidate = representatives.OrderBy(bot => participants.Count(member => PvpCombatant.AreAllied(bot, member)) +
                        campaign.Encounters.Sum(encounter => encounter.Members.Count(member => member.Guild == bot.Guild)))
                    .ThenBy(bot => (Array.IndexOf(representatives, bot) + campaign.Rotation) % representatives.Length)
                    .FirstOrDefault(bot => !PvpCombatant.AreAllied(bot, participant));
                if (candidate != null) guild = candidate.Guild;
            }
            // With no autonomous guild available, a real transient Group is
            // hostile to outsiders and allied internally; no guild is fabricated.
            List<Point3D> camps = campaign.Camps.Select(camp => camp.Position).ToList();
            int nearest = NearestIndex(camps, participant.X, participant.Y);
            Point3D participantPoint = new(participant);
            bool targetParticipant = ambush || OutsideSanctuary(participant, participantPoint);
            Point3D goal = targetParticipant ? participantPoint : new Point3D(camps[nearest]);
            Camp origin = campaign.Camps[ambush ? nearest : PatrolOriginIndex(nearest, camps.Count, campaign.Rotation)];
            string name = ambush ? "Ambush patrol" : "Battleground patrol";
            Encounter encounter = new() { Goal = goal, Participant = participant, Level = (byte)Math.Clamp(participant.Level, campaign.Definition.MinLevel, campaign.Definition.MaxLevel),
                ExpiresAt = Now + (ambush ? 6 : 10) * 60_000 };
            SpawnParty(campaign, encounter, origin.Position, guild, null, name, size);
            // A participant point with no proved route must not leave the patrol empty.
            if (encounter.Members.Count == 0 && targetParticipant && !ambush)
            {
                encounter.Goal = new Point3D(camps[nearest]);
                SpawnParty(campaign, encounter, origin.Position, guild, null, name, size);
            }
            if (encounter.Members.Count == 0) { LogSquadSkipped(campaign, kind, "route_or_spawn_failed", participants.Length); return; }
            campaign.Encounters.Add(encounter);
            campaign.SquadSkipReasons.Remove(kind);
            Log.Info($"BATTLEGROUND_SQUAD_SPAWNED region={campaign.Definition.RegionId} kind={kind} members={encounter.Members.Count}/{size} level={encounter.Level} guild={guild?.Name ?? "none"} origin=camp{origin.Index + 1} goal={FormatPoint(encounter.Goal)} target={participant.Name ?? "none"} actors={ActorCount(campaign)}/{MaximumActorsPerRegion}");
        }

        private static void SpawnParty(Campaign campaign, Encounter encounter, Point3D origin, Guild guild, GamePlayer sponsor, string name, int size)
        {
            Group group = null;
            byte[] classes = { (byte)eCharacterClass.Armsman, (byte)eCharacterClass.Cleric, (byte)eCharacterClass.Sorcerer, (byte)eCharacterClass.Mercenary };
            for (int i = 0; i < size && ActorCount(campaign) + encounter.Members.Count < MaximumActorsPerRegion; i++)
            {
                GameBot actor = BattlegroundEncounterActor.Spawn(campaign.Definition.RegionId, origin.X, origin.Y, origin.Z,
                    classes[i % classes.Length], encounter.Level == 0 ? (byte)campaign.Definition.MaxLevel : encounter.Level, name, guild, group);
                if (actor == null) continue;
                if (sponsor != null) BattlegroundEncounterActor.SetAllianceSponsor(actor, sponsor);
                Point3D goal = encounter.Goal;
                GameLiving target = null;
                if (encounter.Siege) target = SiegeTarget(campaign, actor, out goal);
                if (goal == null || !BattlegroundEncounterActor.SetGoal(actor, goal, target)) { actor.Delete(); continue; }
                if (group == null) { group = new Group(actor); group.AddMember(actor); }
                encounter.Members.Add(actor);
            }
        }

        private static void Cleanup(Encounter encounter)
        {
            foreach (GameBot member in encounter.Members.ToArray()) member.Delete();
            encounter.Members.Clear();
        }

        public static void ShowStatus(GamePlayer player)
        {
            lock (Gate)
            {
                if (!TryCampaign(player, out Campaign campaign, out string reason)) { Say(player, reason); return; }
                BattlegroundCampaignQuest quest = player.IsDoingQuest(typeof(BattlegroundCampaignQuest)) as BattlegroundCampaignQuest;
                Say(player, $"{campaign.Definition.Name}: {ActorCount(campaign)}/{MaximumActorsPerRegion} encounter actors; siege funding {FundingThreshold} tokens per assault. Tokens are personal and earned from monster kills and commander contracts.");
                if (!campaign.HasMonsterObjectives) Say(player, "No native hunting monsters are loaded here; the monster contract is unavailable. Hostile-player and encounter-squad contracts remain available.");
                Say(player, "Patrols match local party sizes from one to eight, at the active participant's level; larger parties include healing and crowd control. Encounters stop being scheduled when the map is empty.");
                if (campaign.Keep == null) Say(player, "This map has no native central keep. Available field contracts, patrols and ambushes remain active; keep contracts and siege funding are unavailable.");
                else if (!NativeLordReady(campaign)) Say(player, "This map has native keep scenery but no loaded native keep lord; capture contracts and siege funding are unavailable.");
                else Say(player, $"Native siege objectives: {campaign.Keep.Doors.Count} actual doors and a loaded keep lord. Existing closed doors precede the lord; when no actual closed door exists the assault approaches the native lord directly.");
                if (quest != null) Say(player, quest.Description);
                foreach (Camp camp in campaign.Camps)
                    Say(player, $"Camp {camp.Index + 1}: {camp.Tokens}/{FundingThreshold}, {(CaptainAlive(camp) ? "captain alive" : "captain defeated; turn-ins closed")}, sponsor {camp.Guild?.Name ?? (camp.GroupSponsor != null ? "local group" : "open")}, commander at {camp.Position.X}, {camp.Position.Y}.");
            }
        }

        public static void AcceptObjectives(GamePlayer player)
        {
            lock (Gate)
            {
                if (!TryCampaign(player, out Campaign campaign, out string reason)) { Say(player, reason); return; }
                Camp camp = NearbyCamp(campaign, player);
                if (camp == null || !CanUse(camp, player, out reason)) { Say(player, reason ?? "Approach a living camp commander and captain."); return; }
                BattlegroundCampaignQuest quest = player.IsDoingQuest(typeof(BattlegroundCampaignQuest)) as BattlegroundCampaignQuest;
                if (quest != null && quest.Region == player.CurrentRegionID) { Say(player, quest.Description); return; }
                if (quest == null)
                {
                    quest = new BattlegroundCampaignQuest(player);
                    if (!player.AddQuest(quest)) { quest.DeleteFromDatabase(); Say(player, "Your quest journal cannot accept this contract."); return; }
                }
                quest.AssignRegion(player.CurrentRegionID);
                Say(player, quest.Description);
            }
        }

        public static void TurnInObjectives(GamePlayer player)
        {
            lock (Gate)
            {
                if (!TryCampaign(player, out Campaign campaign, out string reason)) { Say(player, reason); return; }
                Camp camp = NearbyCamp(campaign, player);
                if (camp == null || !CanUse(camp, player, out reason)) { Say(player, reason ?? "Approach a living camp commander and captain."); return; }
                BattlegroundCampaignQuest quest = player.IsDoingQuest(typeof(BattlegroundCampaignQuest)) as BattlegroundCampaignQuest;
                if (quest?.Region != player.CurrentRegionID) { Say(player, "Accept this battleground's field contracts first."); return; }
                int tokens = quest.TurnIn();
                Say(player, tokens > 0 ? $"Contracts completed: {tokens} siege tokens. Completed contracts are ready to repeat." : "No completed contract could be rewarded. Complete an objective and leave backpack space.");
            }
        }

        public static bool Contribute(GamePlayer player, int amount, out string message)
        {
            lock (Gate)
            {
                if (!TryCampaign(player, out Campaign campaign, out message)) return false;
                Camp camp = NearbyCamp(campaign, player);
                if (camp == null) { message = "Approach the camp commander to contribute."; return false; }
                if (!CanUse(camp, player, out message)) return false;
                if (!NativeLordReady(campaign)) { message = "This battleground lacks a loaded native central keep lord; siege funding and capture contracts are unavailable. Available field contracts and patrols remain active."; return false; }
                if (camp.Assault != null) { message = "This camp already has an active assault."; return false; }
                if (player.Guild == null && player.Group == null) { message = "A guild or group must sponsor this camp."; return false; }
                if (amount <= 0 || amount > FundingThreshold - camp.Tokens) { message = $"Contribute between 1 and {FundingThreshold - camp.Tokens} tokens."; return false; }
                Guild previousGuild = camp.Guild;
                GamePlayer previousSponsor = camp.GroupSponsor;
                Group previousGroup = camp.SponsorGroup;
                // One stack per transaction. Holding the camp and native inventory
                // gates makes concurrent offers linearizable; never trust a handed
                // item reference or consume tokens from a vault/trade window.
                lock (player.Inventory.Lock)
                {
                    DbInventoryItem item = player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)
                        .FirstOrDefault(candidate => candidate.Id_nb == TokenTemplateId && candidate.Count >= amount && candidate.OwnerID == player.InternalID);
                    if (item == null || !ReferenceEquals(player.Inventory.GetItem((eInventorySlot)item.SlotPosition), item) || !player.Inventory.RemoveCountFromStack(item, amount))
                    {
                        message = "Keep enough siege tokens together in one backpack stack.";
                        return false;
                    }
                    if (camp.Guild == null && camp.GroupSponsor == null)
                    {
                        camp.Guild = PvpCombatant.IsRealGuild(player.Guild) ? player.Guild : null;
                        if (camp.Guild == null) { camp.GroupSponsor = player; camp.SponsorGroup = player.Group; }
                    }
                    int previous = camp.Tokens;
                    try { SaveFunding(camp, previous + amount); }
                    catch (Exception exception)
                    {
                        Log.Error("BATTLEGROUND_FUNDING_SAVE_FAILED", exception);
                        camp.Tokens = previous;
                        camp.Guild = previousGuild; camp.GroupSponsor = previousSponsor; camp.SponsorGroup = previousGroup;
                        camp.Row.SponsorGuildId = previousGuild?.GuildID ?? string.Empty;
                        camp.Row.Tokens = previousGuild == null ? 0 : previous;
                        GameServer.Database.SaveObject(camp.Row);
                        GiveTokens(player, amount);
                        message = "The camp could not record your contribution; tokens were returned.";
                        return false;
                    }
                    camp.SponsorExpiresAt = Now + 30 * 60_000;
                    camp.AllianceSponsor ??= player;
                    camp.Captain.Guild = camp.Guild;
                    if (camp.AllianceSponsor != null) BattlegroundEncounterActor.SetAllianceSponsor(camp.Captain, camp.AllianceSponsor);
                    camp.Captain.Flags &= ~GameNPC.eFlags.PEACE;
                    if (camp.Tokens >= FundingThreshold)
                    {
                        // Consume the persisted threshold before creating actors. A
                        // restart cannot replay a funded assault or duplicate tokens.
                        try { SaveFunding(camp, 0); }
                        catch (Exception exception)
                        {
                            Log.Error("BATTLEGROUND_ASSAULT_FUNDING_SAVE_FAILED", exception);
                            camp.Tokens = FundingThreshold - amount;
                            camp.Row.Tokens = camp.Guild == null ? 0 : camp.Tokens;
                            GameServer.Database.SaveObject(camp.Row);
                            GiveTokens(player, amount);
                            message = "The assault could not record its funding; your last contribution was returned.";
                            return false;
                        }
                        Encounter assault = new() { Camp = camp, Siege = true, Level = (byte)Math.Clamp(player.Level, campaign.Definition.MinLevel, campaign.Definition.MaxLevel), Goal = new Point3D(campaign.Keep.X, campaign.Keep.Y, campaign.Keep.Z), ExpiresAt = Now + 10 * 60_000 };
                        SpawnParty(campaign, assault, camp.Position, camp.Guild, camp.AllianceSponsor, "Camp siege escort", 4);
                        if (assault.Members.Count == 0)
                        {
                            SaveFunding(camp, FundingThreshold - amount);
                            GiveTokens(player, amount);
                            message = "The assault could not find a safe route or actor slot; your last contribution was returned.";
                            return false;
                        }
                        Log.Info($"BATTLEGROUND_SQUAD_SPAWNED region={campaign.Definition.RegionId} kind=siege members={assault.Members.Count}/4 level={assault.Level} guild={camp.Guild?.Name ?? "none"} origin=camp{camp.Index + 1} goal={FormatPoint(assault.Goal)} target=none actors={ActorCount(campaign)}/{MaximumActorsPerRegion}");
                        camp.Assault = assault;
                        campaign.Encounters.Add(assault);
                        message = "The funded assault is marching to the real keep doors, then its lord. Claim the defeated keep at its steward.";
                    }
                    else message = $"Camp funding: {camp.Tokens}/{FundingThreshold} tokens.";
                    return true;
                }
            }
        }

        private static void SaveFunding(Camp camp, int tokens)
        {
            camp.Tokens = Math.Clamp(tokens, 0, FundingThreshold);
            // Session groups are deliberately never persisted as stale group IDs.
            camp.Row.SponsorGuildId = camp.Guild?.GuildID ?? string.Empty;
            camp.Row.Tokens = camp.Guild == null ? 0 : camp.Tokens;
            camp.Row.ExpiresAt = WorldSimulationClock.UtcNow.AddMinutes(30);
            if (!GameServer.Database.SaveObject(camp.Row)) throw new InvalidOperationException("Camp funding was not saved.");
        }

        private static void ResetSponsor(Camp camp)
        {
            camp.Guild = null;
            camp.GroupSponsor = null;
            camp.AllianceSponsor = null;
            camp.SponsorGroup = null;
            camp.SponsorExpiresAt = 0;
            SaveFunding(camp, 0);
            camp.Captain?.Delete();
            camp.Captain = null;
            camp.RespawnAt = Now + CaptainRespawnMinutes * 60_000;
        }

        private static bool CaptainAlive(Camp camp) => camp.Captain?.IsAlive == true && camp.Captain.ObjectState == GameObject.eObjectState.Active;
        private static Camp NearbyCamp(Campaign campaign, GamePlayer player) => campaign.Camps.FirstOrDefault(camp => player.IsWithinRadius(camp.Commander, WorldMgr.INTERACT_DISTANCE));
        private static bool CanUse(Camp camp, GamePlayer player, out string reason)
        {
            reason = null;
            if (!player.IsAlive || !player.IsWithinRadius(camp.Commander, WorldMgr.INTERACT_DISTANCE)) reason = "Stay alive beside the camp commander.";
            else if (!CaptainAlive(camp)) reason = "The camp captain is dead. Contributions and contract turn-ins reopen after its respawn.";
            else if (camp.Guild != null && player.Guild != camp.Guild && !PvpCombatant.AreAllied(camp.Captain, player)) reason = "This camp currently belongs to a hostile guild.";
            else if (camp.GroupSponsor != null && !PvpCombatant.AreAllied(camp.GroupSponsor, player)) reason = "This camp currently belongs to a hostile group.";
            return reason == null;
        }

        private static bool TryCampaign(GamePlayer player, out Campaign campaign, out string reason)
        {
            campaign = null;
            reason = "Battleground campaign activity is unavailable here.";
            BattlegroundDefinition definition = player == null ? null : BattlegroundCampaignCatalog.Find(player.CurrentRegionID);
            if (definition == null || !BattlegroundCampaignPolicy.CanEnter(definition, player, out reason)) return false;
            if (!_running || !Campaigns.TryGetValue(player.CurrentRegionID, out campaign)) { reason = "This battleground's camps are not ready."; return false; }
            return true;
        }

        private static void OnEnemyKilled(DOLEvent e, object sender, EventArgs args)
        {
            if (sender is not GamePlayer player || args is not EnemyKilledEventArgs killed || !IsEligible(player)) return;
            lock (Gate)
            {
                if (!_running || !Campaigns.TryGetValue(player.CurrentRegionID, out Campaign campaign)) return;
                GameLiving target = killed.Target;
                if (target == null || target.CurrentRegionID != player.CurrentRegionID || !player.IsAlive || !player.IsWithinRadius(target, WorldMgr.MAX_EXPFORKILL_DISTANCE)) return;
                bool combatant = PvpCombatant.IsPlayerShaped(target);
                if (combatant && (PvpCombatant.AreAllied(player, target) || player.GetConLevel(target) < -3)) return;
                if (target is GamePlayer victim && victim.DeathTime > 0 && victim.DeathTime + Properties.RP_WORTH_SECONDS > victim.PlayedTime) return;
                string key = $"{player.InternalID}:{target.ObjectID}:{target.SpawnTick}:{(target is GamePlayer body ? body.DeathTime : 0)}";
                // Player bodies can be reused after resurrection without a new spawn
                // tick; their previous DeathTime is stable through the native
                // EnemyKilled fan-out and updates afterwards.
                long now = Now;
                if (campaign.KillCredits.ContainsKey(key)) return;
                campaign.KillCredits[key] = now;
                BattlegroundCampaignQuest quest = player.IsDoingQuest(typeof(BattlegroundCampaignQuest)) as BattlegroundCampaignQuest;
                if (target is GuardLord lord && lord.Component?.Keep == campaign.Keep)
                    campaign.LordParticipants.Add(player.InternalID);
                if (combatant)
                {
                    if (quest?.Region == player.CurrentRegionID) quest.Credit("enemies");
                    return; // Camlann PvP kills remain XP/RP only; no inventory loot.
                }
                if (target is not GameNPC npc || npc is GameKeepGuard || npc is BattlegroundCampCommander ||
                    npc.RewardStatus != GameNPC.RewardEligibility.Eligible || player.GetConLevel(npc) < -3) return;
                if (campaign.HasMonsterObjectives && quest?.Region == player.CurrentRegionID) quest.Credit("monsters");
                GiveTokens(player, 1);
            }
        }

        public static void OnKeepClaimed(AbstractGameKeep keep, GameLiving claimer)
        {
            lock (Gate)
            {
                if (!_running || keep == null || claimer == null || !Campaigns.TryGetValue((ushort)keep.Region, out Campaign campaign) || campaign.Keep != keep) return;
                var members = WorldMgr.GetPlayersCloseToSpot((ushort)keep.Region, keep.X, keep.Y, keep.Z, WorldMgr.MAX_EXPFORKILL_DISTANCE)
                    .Cast<GameLiving>().Concat(claimer.Group?.GetMembersInTheGroup() ?? new List<GameLiving> { claimer }).Distinct();
                foreach (GamePlayer player in members.OfType<GamePlayer>())
                {
                    if (!player.IsAlive || !IsEligible(player) || player.CurrentRegionID != (ushort)keep.Region ||
                        keep.Area?.IsContaining(player, false) != true || !PvpCombatant.AreAllied(claimer, player) || !campaign.LordParticipants.Contains(player.InternalID)) continue;
                    if (player.IsDoingQuest(typeof(BattlegroundCampaignQuest)) is BattlegroundCampaignQuest quest && quest.Region == (ushort)keep.Region)
                        quest.Credit("captures");
                }
                campaign.LordParticipants.Clear();
            }
        }

        private static bool NativeLordReady(Campaign campaign) => campaign.Keep != null && campaign.Keep.Guards.Values.OfType<GuardLord>().Any();

        public static bool HasCaptureObjectives(ushort region)
        {
            lock (Gate) return Campaigns.TryGetValue(region, out Campaign campaign) && NativeLordReady(campaign);
        }

        public static bool HasMonsterObjectives(ushort region)
        {
            lock (Gate) return Campaigns.TryGetValue(region, out Campaign campaign) && campaign.HasMonsterObjectives;
        }

        private static bool IsEligible(GameLiving player) => BattlegroundCampaignPolicy.IsEnabled &&
            BattlegroundCampaignPolicy.IsEligible(player, BattlegroundCampaignCatalog.Find(player.CurrentRegionID));

        internal static bool GiveTokens(GamePlayer player, int count)
        {
            if (_token == null || count <= 0) return false;
            lock (player.Inventory.Lock)
            {
                return player.Inventory.AddTemplate(new GameInventoryItem(_token), count, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            }
        }
        internal static void Say(GamePlayer player, string message) => player?.Out.SendMessage(message ?? "Battleground activity is unavailable.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
    }

    public sealed class BattlegroundCampCommander : GameNPC
    {
        public BattlegroundCampCommander(ushort region, GameLocation landing, int camp)
        {
            Name = $"Battleground Camp {camp + 1} Commander";
            GuildName = "Field contracts and siege funding";
            Realm = eRealm.None;
            Model = 40;
            Level = 50;
            Flags = eFlags.PEACE;
            CurrentRegionID = region;
            X = landing.X; Y = landing.Y; Z = landing.Z; Heading = landing.Heading;
        }
        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player)) return false;
            BattlegroundCampaignManager.ShowStatus(player);
            SayTo(player, "[Contracts] records local objectives. [Rewards] turns in completed contracts. [Contribute] gives one siege token; twenty fund a physical keep assault. Kill a hostile captain to interrupt funding and turn-ins until it respawns.");
            return true;
        }
        public override bool WhisperReceive(GameLiving source, string text)
        {
            if (!base.WhisperReceive(source, text) || source is not GamePlayer player) return false;
            switch (text.ToLowerInvariant())
            {
                case "contracts": BattlegroundCampaignManager.AcceptObjectives(player); break;
                case "rewards": BattlegroundCampaignManager.TurnInObjectives(player); break;
                case "contribute":
                    BattlegroundCampaignManager.Contribute(player, 1, out string message);
                    BattlegroundCampaignManager.Say(player, message);
                    break;
            }
            return true;
        }
    }
}
