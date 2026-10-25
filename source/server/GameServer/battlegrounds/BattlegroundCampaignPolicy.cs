using System;
using System.Collections.Generic;
using System.Numerics;
using DOL.GS.PacketHandler;
using DOL.GS.ServerRules;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    public static class BattlegroundCampaignPolicy
    {
        private const string CampKey = "BattlegroundCampaign.Camp";
        public static bool IsEnabled => Properties.BATTLEGROUND_CAMPAIGN_ENABLED;
        public static bool IsEligible(GameLiving actor, BattlegroundDefinition definition)
        {
            if (actor == null || definition == null || actor.Level < definition.MinLevel || actor.Level > definition.MaxLevel) return false;
            int realmLevel = actor switch { GamePlayer human => human.RealmLevel, IGamePlayer bot => bot.RealmLevel, _ => 0 };
            return definition.MaxRealmLevel == 0 || realmLevel < definition.MaxRealmLevel;
        }

        public static bool IsReady(BattlegroundDefinition definition, out string reason)
        {
            reason = null;
            if (definition == null) { reason = "No battleground covers your level."; return false; }
            Zone zone = WorldMgr.GetZone(definition.ZoneId);
            if (WorldMgr.GetRegion(definition.RegionId) == null || zone == null ||
                !PathfindingProvider.Instance.IsAvailable || !PathfindingProvider.Instance.HasNavmesh(zone))
            { reason = definition.Name + " is awaiting its native navigation mesh."; return false; }
            GameLocation[] landings = BattlegroundCampaignCatalog.GetLandings(definition);
            if (landings.Length == 0)
            { reason = definition.Name + " has no verified native arrival camp."; return false; }
            foreach (GameLocation landing in landings)
            {
                Vector3 point = new(landing.X, landing.Y, landing.Z);
                if (WorldMgr.GetRegion(definition.RegionId).GetZone(landing.X, landing.Y) != zone ||
                    !PathfindingProvider.Instance.TrySnapToMesh(zone, ref point, 100) || Math.Abs(point.Z - landing.Z) > 100)
                { reason = definition.Name + " is awaiting verified arrival-camp navigation."; return false; }
            }
            return true;
        }

        public static bool CanEnter(BattlegroundDefinition definition, GameLiving actor, out string reason)
        {
            reason = null;
            // The open BG answers through its own switch and level range (review follow-up: the zone gate
            // and AddToWorld call CanEnter, so a switched-off open BG must refuse here too).
            if (definition != null && definition.Kind == BattlegroundDefinitionKind.Open)
                return CanEnterOpenBattleground(definition, actor, out reason);
            if (!IsEnabled) { reason = "The battleground campaign is closed."; return false; }
            if (!IsEligible(actor, definition)) { reason = "Your level or Realm Rank is outside this battleground's bracket."; return false; }
            return IsReady(definition, out reason);
        }

        // The open BG: no bracket, levels 1-50, its own switch on top of the campaign switch.
        private static bool CanEnterOpenBattleground(BattlegroundDefinition definition, GameLiving actor, out string reason)
        {
            reason = null;
            if (!OpenBattlegroundPolicy.IsEnabled || definition == null) { reason = "The open battleground is closed."; return false; }
            if (!IsReady(definition, out reason)) return false;
            if (!OpenBattlegroundPolicy.CanEnterOpen(true, true, actor.Level))
            { reason = "The open battleground takes levels " + OpenBattlegroundPolicy.MinLevel + " to " + OpenBattlegroundPolicy.MaxLevel + "."; return false; }
            return true;
        }

        /// <summary>
        /// Index of the landing a battleground entrant uses (bug 147). A group member lands where its group leader
        /// lands (owner decision 2026-10-11; players already did through the leader's hash). A leader or solo bot
        /// lands at the portal keep of its own realm; no such landing falls back to <paramref name="fallback"/>.
        /// <paramref name="leaderLanding"/> is the leader's landing when it is fixed already (a player's hash camp),
        /// null when the leader's realm decides. <paramref name="landingRealms"/> is the realm of each landing, in landing order.
        /// </summary>
        public static int ChooseLanding(eRealm realm, int? leaderLanding, IReadOnlyList<eRealm> landingRealms, int fallback)
        {
            if (leaderLanding.HasValue) return leaderLanding.Value;
            if (landingRealms != null && realm != eRealm.None)
                for (int i = 0; i < landingRealms.Count; i++)
                    if (landingRealms[i] == realm) return i;
            return fallback;
        }

        /// <summary>
        /// A player-led group keeps its hash camp in Camlann; in the RvR world every realm, players included,
        /// lands at its own realm's portal keep (null lets <see cref="ChooseLanding"/> pick the realm's landing).
        /// </summary>
        public static int? LeaderLanding(WorldModelKind world, bool playerLeader, int hashed) =>
            playerLeader && world != WorldModelKind.Realm ? hashed : null;

        public static GameLocation GetLanding(GameLiving actor, BattlegroundDefinition definition)
        {
            GameLocation[] landings = BattlegroundCampaignCatalog.GetLandings(definition);
            if (landings.Length == 0) return null;
            GameLiving leader = actor.Group?.LivingLeader ?? actor;
            string identity = PvpCombatant.GuildOf(leader)?.GuildID ?? leader.Name ?? "";
            // Stable guild/group camp assignment, independent of realm identity.
            uint hash = 2166136261;
            foreach (char c in identity) hash = (hash ^ c) * 16777619;
            int hashed = (int)(hash % (uint)landings.Length);
            // A player leader keeps its hash camp; a bot leader (or a solo bot) lands at its realm's own portal keep,
            // in the Server PvP open BG at the keep its roster unit was balanced to.
            eRealm realm = leader.Realm;
            if (definition.Kind == BattlegroundDefinitionKind.Open && leader is GameBot leaderBot)
            {
                int unit = AutonomousBattlegroundParticipation.OpenUnitOf(leaderBot);
                if (unit == 0 && actor is GameBot actorBot) unit = AutonomousBattlegroundParticipation.OpenUnitOf(actorBot);
                if (unit != 0) realm = OpenBattlegroundRoster.LandingSideOf(unit, realm);
            }
            int index = ChooseLanding(realm, LeaderLanding(WorldMode.Current, leader is GamePlayer, hashed),
                BattlegroundCampaignCatalog.GetLandingRealms(definition, landings.Length), hashed);
            if (index < 0 || index >= landings.Length) index = hashed;
            actor.TempProperties.SetProperty(CampKey, index);
            return landings[index];
        }

        public static bool TryEnter(GamePlayer player, out string reason)
        {
            BattlegroundDefinition definition = BattlegroundCampaignCatalog.ForLevel(player.Level);
            if (!CanEnter(definition, player, out reason)) return false;
            return MovePlayerIn(player, definition, out reason);
        }

        /// <summary>The porter's [open BG] line: the same checks, landing and move as <see cref="TryEnter"/> without a bracket.</summary>
        public static bool TryEnterOpen(GamePlayer player, out string reason)
        {
            reason = null;
            if (player == null) { reason = "No battleground participant."; return false; }
            BattlegroundDefinition definition = BattlegroundCampaignCatalog.FindOpen();
            if (!CanEnterOpenBattleground(definition, player, out reason)) return false;
            return MovePlayerIn(player, definition, out reason);
        }

        private static bool MovePlayerIn(GamePlayer player, BattlegroundDefinition definition, out string reason)
        {
            reason = null;
            if (!player.IsAlive || player.InCombat || GameRelic.IsPlayerCarryingRelic(player))
            { reason = "You must be alive, out of combat and carrying no relic to enter."; return false; }
            if (BattlegroundCampaignCatalog.Find((ushort)player.BindRegion) != null)
            { reason = "Bind outside the battleground before entering."; return false; }
            GameLocation landing = GetLanding(player, definition);
            if (landing == null) { reason = "The battleground has no arrival camp."; return false; }
            if (!player.MoveTo(landing.RegionID, landing.X, landing.Y, landing.Z, landing.Heading))
            { reason = "The battleground transfer could not be completed."; return false; }
            return true;
        }

        public static bool TryEnterBot(GameBot bot, out string reason) => TryEnterBot(bot, out reason, out _);

        // Autonomous participants use the admission, landing and move of TryEnter.
        // Bots keep no bind point, so the bind-outside check does not apply to them.
        public static bool TryEnterBot(GameBot bot, out string reason, out ushort destinationRegion)
        {
            reason = null;
            destinationRegion = 0;
            if (bot == null) { reason = "No battleground participant."; return false; }
            BattlegroundDefinition definition = BattlegroundCampaignCatalog.ForLevel(bot.Level);
            if (!CanEnter(definition, bot, out reason)) return false;
            return MoveBotIn(bot, definition, out reason, out destinationRegion);
        }

        public static bool TryEnterBotOpen(GameBot bot, out string reason) => TryEnterBotOpen(bot, out reason, out _);

        /// <summary>An open-BG crew bot boards like <see cref="TryEnterBot(GameBot, out string, out ushort)"/>, without a bracket.</summary>
        public static bool TryEnterBotOpen(GameBot bot, out string reason, out ushort destinationRegion)
        {
            reason = null;
            destinationRegion = 0;
            if (bot == null) { reason = "No battleground participant."; return false; }
            BattlegroundDefinition definition = BattlegroundCampaignCatalog.FindOpen();
            if (!CanEnterOpenBattleground(definition, bot, out reason)) return false;
            return MoveBotIn(bot, definition, out reason, out destinationRegion);
        }

        private static bool MoveBotIn(GameBot bot, BattlegroundDefinition definition, out string reason, out ushort destinationRegion)
        {
            reason = null;
            destinationRegion = 0;
            if (!bot.IsAlive || bot.InCombat || GameRelic.IsPlayerCarryingRelic(bot))
            { reason = "The bot must be alive, out of combat and carrying no relic to enter."; return false; }
            GameLocation landing = GetLanding(bot, definition);
            if (landing == null) { reason = "The battleground has no arrival camp."; return false; }
            StopForTransfer(bot);
            if (!bot.MoveTo(landing.RegionID, landing.X, landing.Y, landing.Z, landing.Heading))
            { reason = "The battleground transfer could not be completed."; return false; }
            destinationRegion = landing.RegionID;
            SettleTransfer(bot);
            return true;
        }

        // The mover must not carry an old route, stable ticket or follow order into the new region.
        // AutonomousFrontierTransport stops the same way before its cross-region MoveTo.
        private static void StopForTransfer(GameBot bot)
        {
            bot.StopMovingOnPath();
            bot.StopMoving();
        }

        // After a real transfer the bot must belong to the battleground driver again: no stable
        // route, a fresh path plan, a brain due to think now, and the watchdog's movement clock
        // restarted. The participation clock is restarted by its caller.
        private static void SettleTransfer(GameBot bot)
        {
            if (bot.IsOnStableMasterRoute) bot.CompleteStableMasterRoute();
            bot.ForcePathReplot();
            if (bot.Brain is { } brain)
            {
                brain.Start();
                brain.NextThinkTick = GameLoop.GameLoopTime;
            }
            AutonomousStuckWatchdog.MarkProgress(bot, eAutonomousProgressKind.Movement);
            AutonomousBotStatusPersistence.Queue(bot, true);
        }

        // The bot's equivalent of /battleground leave. Bots keep no bind point, so the
        // outside destination is the realm capital, spread as the release fallback spreads it.
        public static bool TryExitBot(GameBot bot, out string reason)
        {
            reason = null;
            if (bot == null || BattlegroundCampaignCatalog.Find(bot.CurrentRegionID) == null)
            { reason = "The bot is not in a campaign battleground."; return false; }
            if (!bot.IsAlive || bot.InCombat) { reason = "The bot must be alive and out of combat to leave."; return false; }
            if (GameRelic.IsPlayerCarryingRelic(bot)) { reason = "Return the relic before leaving."; return false; }
            AutonomousStuckWatchdog.CapitalLocation capital = AutonomousStuckWatchdog.SpreadAround(
                AutonomousStuckWatchdog.SafeCapitalFor(bot.Realm), bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID);
            StopForTransfer(bot);
            if (!bot.MoveTo(capital.RegionId, capital.X, capital.Y, capital.Z, capital.Heading))
            { reason = "The outside destination is unavailable."; return false; }
            SettleTransfer(bot);
            return true;
        }

        public static bool TryLeave(GamePlayer player, out string reason)
        {
            reason = null;
            if (BattlegroundCampaignCatalog.Find(player.CurrentRegionID) == null)
            { reason = "You are not in a campaign battleground."; return false; }
            if (!player.IsAlive || player.InCombat) { reason = "You must be alive and out of combat to leave."; return false; }
            if (GameRelic.IsPlayerCarryingRelic(player)) { reason = "Return the relic before leaving."; return false; }
            return ExitToBind(player, out reason);
        }

        public static bool ExitToBind(GamePlayer player, out string reason)
        {
            reason = null;
            if (BattlegroundCampaignCatalog.Find((ushort)player.BindRegion) != null)
            { reason = "Your bind is inside a battleground; bind outside before entering."; return false; }
            if (!player.MoveTo((ushort)player.BindRegion, player.BindXpos, player.BindYpos, player.BindZpos, (ushort)player.BindHeading))
            { reason = "Your outside bind destination is unavailable."; return false; }
            return true;
        }

        /// <summary>
        /// Whether a player leaves a battleground for its bracket (graduation or a closed campaign).
        /// The open BG takes levels 1-50 for good: nobody graduates and no tour ends there.
        /// </summary>
        public static bool ShouldGraduate(BattlegroundDefinition definition, bool enabled, bool eligible) =>
            definition != null && definition.Kind != BattlegroundDefinitionKind.Open && !(enabled && eligible);

        public static void CheckProgress(GamePlayer player)
        {
            BattlegroundDefinition definition = BattlegroundCampaignCatalog.Find(player.CurrentRegionID);
            if (!ShouldGraduate(definition, IsEnabled, IsEligible(player, definition))) return;
            if (player.IsAlive && ExitToBind(player, out string reason))
                player.Out.SendMessage("You have graduated from this battleground. Your next eligible bracket is available through /battleground.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }
}
