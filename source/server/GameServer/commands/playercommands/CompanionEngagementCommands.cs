using System.Linq;
using DOL.AI.Brain;

namespace DOL.GS.Commands
{
    [CmdAttribute("&defensive", ePrivLevel.Player, "Companions wait for enemies to approach you", "/defensive")]
    public class CompanionDefensiveCommand : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            CompanionEngagementMode.Set(client.Player, true);
            if (client.Player.Group != null)
                foreach (GameBot bot in client.Player.Group.GetMembersInTheGroup().OfType<GameBot>())
                    if (bot.PlayerGroupLeader == client.Player && bot.Brain is BotBrain brain)
                        brain.EnforceCompanionEngagementRange();
            DisplayMessage(client, "Companions: DEFENSIVE. They engage threats near you and return when left far behind. Use /passive to regroup or /companions group default for saved preferences.");
        }
    }
    [CmdAttribute("&aggressive", ePrivLevel.Player, "Restore normal companion assisting (default)", "/aggressive")]
    public class CompanionAggressiveCommand : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            CompanionEngagementMode.Set(client.Player, false);
            DisplayMessage(client, "Companions: AGGRESSIVE. They assist your attacks but return when left far behind. Use /passive to regroup or /companions group default for saved preferences.");
        }
    }

    [CmdAttribute("&passive", ePrivLevel.Player, "Recall companions and hold combat until another mode is chosen", "/passive")]
    public class CompanionPassiveCommand : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            CompanionEngagementMode.Set(client.Player, eCompanionEngagementMode.Passive);
            PlayerLedPullCoordinator.CancelForLeader(client.Player);
            if (client.Player.Group != null)
                foreach (GameBot bot in client.Player.Group.GetMembersInTheGroup().OfType<GameBot>())
                    if (bot.PlayerGroupLeader == client.Player && bot.Brain is BotBrain brain)
                        brain.RegroupWithLeader();
            DisplayMessage(client, "Companions: PASSIVE. They drop combat and return to you; they will not attack until you choose /defensive, /aggressive, or /companions group default.");
        }
    }
}
