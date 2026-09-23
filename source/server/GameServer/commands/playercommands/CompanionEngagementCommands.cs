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
                    if (CompanionEngagementMode.DefensiveLeader(bot) == client.Player && bot.Brain is BotBrain brain)
                        brain.EnforceCompanionEngagementRange();
            DisplayMessage(client, "Companions: DEFENSIVE group order. Helpers and persistent companions hold near you; direct /pull and your attacks still take precedence. Use /companions group default for individual preferences.");
        }
    }
    [CmdAttribute("&aggressive", ePrivLevel.Player, "Restore normal companion assisting (default)", "/aggressive")]
    public class CompanionAggressiveCommand : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            CompanionEngagementMode.Set(client.Player, false);
            DisplayMessage(client, "Companions: AGGRESSIVE group order. Helpers and persistent companions assist normally. Use /companions group default for individual preferences.");
        }
    }
}
