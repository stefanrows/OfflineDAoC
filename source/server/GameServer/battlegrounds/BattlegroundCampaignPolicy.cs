using System;
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
            if (!IsEnabled) { reason = "The battleground campaign is closed."; return false; }
            if (!IsEligible(actor, definition)) { reason = "Your level or Realm Rank is outside this battleground's bracket."; return false; }
            return IsReady(definition, out reason);
        }

        public static GameLocation GetLanding(GameLiving actor, BattlegroundDefinition definition)
        {
            GameLocation[] landings = BattlegroundCampaignCatalog.GetLandings(definition);
            if (landings.Length == 0) return null;
            GameLiving leader = actor.Group?.LivingLeader ?? actor;
            string identity = PvpCombatant.GuildOf(leader)?.GuildID ?? leader.Name ?? "";
            // Stable guild/group camp assignment, independent of realm identity.
            uint hash = 2166136261;
            foreach (char c in identity) hash = (hash ^ c) * 16777619;
            int index = (int)(hash % (uint)landings.Length);
            actor.TempProperties.SetProperty(CampKey, index);
            return landings[index];
        }

        public static bool TryEnter(GamePlayer player, out string reason)
        {
            BattlegroundDefinition definition = BattlegroundCampaignCatalog.ForLevel(player.Level);
            if (!CanEnter(definition, player, out reason)) return false;
            if (!player.IsAlive || player.InCombat || GameRelic.IsPlayerCarryingRelic(player))
            { reason = "You must be alive, out of combat and carrying no relic to enter."; return false; }
            if (BattlegroundCampaignCatalog.Find((ushort)player.BindRegion) != null)
            { reason = "Bind outside the battleground before entering."; return false; }
            GameLocation landing = GetLanding(player, definition);
            if (!player.MoveTo(landing.RegionID, landing.X, landing.Y, landing.Z, landing.Heading))
            { reason = "The battleground transfer could not be completed."; return false; }
            return true;
        }

        // Autonomous participants use the admission, landing and move of TryEnter.
        // Bots keep no bind point, so the bind-outside check does not apply to them.
        public static bool TryEnterBot(GameBot bot, out string reason)
        {
            reason = null;
            if (bot == null) { reason = "No battleground participant."; return false; }
            BattlegroundDefinition definition = BattlegroundCampaignCatalog.ForLevel(bot.Level);
            if (!CanEnter(definition, bot, out reason)) return false;
            if (!bot.IsAlive || bot.InCombat || GameRelic.IsPlayerCarryingRelic(bot))
            { reason = "The bot must be alive, out of combat and carrying no relic to enter."; return false; }
            GameLocation landing = GetLanding(bot, definition);
            if (landing == null) { reason = "The battleground has no arrival camp."; return false; }
            if (!bot.MoveTo(landing.RegionID, landing.X, landing.Y, landing.Z, landing.Heading))
            { reason = "The battleground transfer could not be completed."; return false; }
            return true;
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
            if (!bot.MoveTo(capital.RegionId, capital.X, capital.Y, capital.Z, capital.Heading))
            { reason = "The outside destination is unavailable."; return false; }
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

        public static void CheckProgress(GamePlayer player)
        {
            BattlegroundDefinition definition = BattlegroundCampaignCatalog.Find(player.CurrentRegionID);
            if (definition == null || IsEnabled && IsEligible(player, definition)) return;
            if (player.IsAlive && ExitToBind(player, out string reason))
                player.Out.SendMessage("You have graduated from this battleground. Your next eligible bracket is available through /battleground.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }
}
