using System;
using System.Collections.Generic;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    [CmdAttribute("&battleground", new[] { "&bgs", "&battlegrounds" }, ePrivLevel.Player,
        "Guild and group battleground campaign",
        "/battleground [list|join|leave|status|quests|turnin|contribute <count>|lfg [xp|pvp|off]]")]
    public sealed class BattlegroundCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            if (client?.Player == null || IsSpammingCommand(client.Player, "battleground")) return;
            GamePlayer player = client.Player;
            string action = args.Length > 1 ? args[1].ToLowerInvariant() : "help";
            string message;
            switch (action)
            {
                case "list" when args.Length == 2:
                    var lines = new List<string> { "Guild, group and battlegroup alliances apply in every bracket.",
                        "Realm identity does not assign a team. Join means consenting to PvP below level 10.", "" };
                    foreach (BattlegroundDefinition definition in BattlegroundCampaignCatalog.Definitions)
                    {
                        bool ready = BattlegroundCampaignPolicy.IsReady(definition, out string reason);
                        string rank = definition.MaxRealmLevel == 0 ? "no RP ceiling" : $"below RR{definition.MaxRealmLevel / 10 + 1}L{definition.MaxRealmLevel % 10}";
                        lines.Add($"{definition.Name}: levels {definition.MinLevel}-{definition.MaxLevel}, {rank}.");
                        if (!ready) lines.Add(reason);
                        var central = BattlegroundCampaignCatalog.CentralKeep(definition);
                        lines.Add("  " + (central == null ? "Owner of central keep: none registered." :
                            BattlegroundKeepOwnership.CentralLine(central.Name, central.Guild?.Name, central.DBKeep?.LordDefeated == true)));
                    }
                    if (!BattlegroundCampaignPolicy.IsEnabled) lines.Add("The campaign is currently closed.");
                    client.Out.SendCustomTextWindow("Battleground brackets", lines);
                    return;
                case "join" when args.Length == 2:
                    if (BattlegroundCampaignPolicy.TryEnter(player, out message))
                        message = "You have entered the battleground. Use /battleground quests to accept objectives.";
                    break;
                case "leave" when args.Length == 2:
                    if (BattlegroundCampaignPolicy.TryLeave(player, out message))
                        message = "You have returned to your outside bind point.";
                    break;
                case "status" when args.Length == 2:
                    BattlegroundCampaignManager.ShowStatus(player);
                    return;
                case "quests" when args.Length == 2:
                    BattlegroundCampaignManager.AcceptObjectives(player);
                    return;
                case "turnin" when args.Length == 2:
                    BattlegroundCampaignManager.TurnInObjectives(player);
                    return;
                case "contribute" when args.Length == 3 && int.TryParse(args[2], out int count) && count > 0:
                    BattlegroundCampaignManager.Contribute(player, count, out message);
                    break;
                case "lfg" when args.Length <= 3:
                    BattlegroundGroupFinder.Set(player, args.Length == 3 ? args[2] : "pvp", out message);
                    break;
                case "help":
                    client.Out.SendCustomTextWindow("Battleground campaign", new[]
                    {
                        "/battleground list - see the ten level brackets and their readiness.",
                        "/battleground join - enter your level bracket; leave returns to your outside bind.",
                        "Your current guild, group and battlegroup remain your alliances.",
                        "Below level 10, joining allows PvP here; your saved /safety flag is retained.",
                        "Portal keeps are safe arrival areas. Campaign camps outside them can be contested.",
                        "/battleground status - see objectives, camp funding and active encounters.",
                        "/battleground quests - accept the current repeatable contracts.",
                        "/battleground turnin - collect completed contracts beside a camp commander.",
                        "/battleground contribute <count> - donate siege tokens beside a live captain.",
                        "A funded camp sends a physical assault against the central keep, where present.",
                        "/battleground lfg xp|pvp|off - opt in to automatic grouping every 30 seconds.",
                        "/LFxp and /LFrvr are shortcuts; append off to leave the queue.",
                        "Normal /bg commands still manage battlegroups. /bgs is the campaign shortcut."
                    });
                    return;
                default:
                    DisplaySyntax(client);
                    return;
            }
            if (!string.IsNullOrEmpty(message)) client.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }

    [CmdAttribute("&lfxp", ePrivLevel.Player, "Opt in to battleground XP grouping", "/LFxp [off]")]
    public sealed class BattlegroundXpGroupCommand : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args) => BattlegroundGroupFinder.Command(client, args, "xp");
    }

    [CmdAttribute("&lfrvr", ePrivLevel.Player, "Opt in to battleground PvP grouping", "/LFrvr [off]")]
    public sealed class BattlegroundPvpGroupCommand : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args) => BattlegroundGroupFinder.Command(client, args, "pvp");
    }
}
