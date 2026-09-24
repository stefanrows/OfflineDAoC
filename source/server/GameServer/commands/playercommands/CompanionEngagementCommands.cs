using System.Linq;
using DOL.AI.Brain;

namespace DOL.GS.Commands
{
    /// <summary>Group orders shared by the chat commands and the Companion Manager.</summary>
    public static class CompanionGroupOrders
    {
        public static string Defensive(GamePlayer player)
        {
            CompanionEngagementMode.Set(player, true);
            if (player.Group != null)
                foreach (GameBot bot in player.Group.GetMembersInTheGroup().OfType<GameBot>())
                    if (bot.PlayerGroupLeader == player && bot.Brain is BotBrain brain)
                        brain.EnforceCompanionEngagementRange();
            return "Companions: DEFENSIVE. They engage threats near you and return when left far behind. Use /passive to regroup or /companions group default for saved preferences.";
        }

        public static string Aggressive(GamePlayer player)
        {
            CompanionEngagementMode.Set(player, false);
            return "Companions: AGGRESSIVE. They assist your attacks but return when left far behind. Use /passive to regroup or /companions group default for saved preferences.";
        }

        public static string Passive(GamePlayer player)
        {
            CompanionEngagementMode.Set(player, eCompanionEngagementMode.Passive);
            PlayerLedPullCoordinator.CancelForLeader(player);
            if (player.Group != null)
                foreach (GameBot bot in player.Group.GetMembersInTheGroup().OfType<GameBot>())
                    if (bot.PlayerGroupLeader == player && bot.Brain is BotBrain brain)
                        brain.RegroupWithLeader();
            return "Companions: PASSIVE. They drop combat and return to you; they will not attack until you choose /defensive, /aggressive, or /companions group default.";
        }

        public static string UseSavedStances(GamePlayer player)
        {
            CompanionEngagementMode.ClearGroupOrder(player);
            return "Group stance override cleared; each persistent companion uses their own preference.";
        }
    }

    [CmdAttribute("&defensive", ePrivLevel.Player, "Companions wait for enemies to approach you", "/defensive")]
    public class CompanionDefensiveCommand : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args) =>
            DisplayMessage(client, CompanionGroupOrders.Defensive(client.Player));
    }
    [CmdAttribute("&aggressive", ePrivLevel.Player, "Restore normal companion assisting (default)", "/aggressive")]
    public class CompanionAggressiveCommand : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args) =>
            DisplayMessage(client, CompanionGroupOrders.Aggressive(client.Player));
    }

    [CmdAttribute("&passive", ePrivLevel.Player, "Recall companions and hold combat until another mode is chosen", "/passive")]
    public class CompanionPassiveCommand : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args) =>
            DisplayMessage(client, CompanionGroupOrders.Passive(client.Player));
    }
}
