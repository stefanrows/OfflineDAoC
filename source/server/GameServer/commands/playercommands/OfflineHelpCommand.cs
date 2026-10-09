using System;

namespace DOL.GS.Commands
{
    [CmdAttribute("&offline", ePrivLevel.Player,
        "Everyday Offline DAoC help by topic",
        "/offline [basics|companions|travel|training|recovery|pvp]")]
    public sealed class OfflineHelpCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            if (client?.Player == null || IsSpammingCommand(client.Player, "offline"))
                return;

            if (args.Length > 2)
            {
                DisplaySyntax(client);
                return;
            }

            string topic = args.Length == 1 ? "basics" : args[1].ToLowerInvariant();
            string[] lines;
            switch (topic)
            {
                case "basics":
                case "help":
                    topic = "basics";
                    lines = new[]
                    {
                        "Start with /companions to open the Companion Manager.",
                        "Without its client extension, use /companions help for chat commands.",
                        "Use /train list to see your specialization names and available points.",
                        "Use /mobs to find monsters at your level; /mobs nearby searches your zone.",
                        "",
                        "Help topics: /offline companions, /offline travel, /offline training",
                        "/offline recovery, /offline pvp",
                        "/cmdhelp <command> shows the full syntax for a command.",
                    };
                    break;
                case "companions":
                    lines = new[]
                    {
                        "/companions opens your saved roster; /companions list lists its names.",
                        "/companions recruit <class> creates a free level-1 companion.",
                        "/companions invite <name> brings a saved companion into your party.",
                        "/companions status shows the condition of your roster and squads.",
                        "/companions help shows roster, tactics, training, squads and recovery commands.",
                        "/pull orders your companions and pets to engage your selected enemy.",
                        "/passive recalls companions and holds combat; /aggressive resumes assisting.",
                        "/spawn creates temporary helpers; use /raid 40 or /raid 80 first at level 50.",
                        "These orders control your companions, not autonomous world bots.",
                    };
                    break;
                case "travel":
                    lines = new[]
                    {
                        "/mobs lists accessible monsters at your level, with their locations.",
                        "/mobs <level> [page] searches a chosen level; /mobs nearby limits it to your zone.",
                        "/tele mob <exact monster name> teleports to a spawn.",
                        "Dungeon targets use the configured entrance approach where applicable.",
                        "/tele <playerbot name> teleports to an online autonomous bot.",
                        "/tc teleports to your capital's Realm Exchange when alive and off a horse route.",
                        "Ordinary stablemaster and porter routes remain available.",
                    };
                    break;
                case "training":
                    lines = new[]
                    {
                        "/train list shows your actual specialization names, levels and points anywhere.",
                        "If the client opens its trainer UI instead, use /trainline list.",
                        "Select a valid trainer for your class before spending points.",
                        "/train <line> <level> trains toward the requested specialization level.",
                        "Use the full command name shown by /train list when a prefix is ambiguous.",
                        "Training may stop early when you run out of points or reach your character level.",
                        "Companion training is separate: /companions build <name> lists their builds.",
                        "/companions help explains companion builds and manual training.",
                    };
                    break;
                case "recovery":
                    lines = new[]
                    {
                        "After death, /release uses the normal release destination.",
                        "/release bind returns to your bind point; /release city returns to your capital.",
                        "/release house uses your house bind, falling back to your normal bind if absent.",
                        "There is a short wait after death; repeating a queued release can cancel it.",
                        "/bind sets a bind point where binding is allowed.",
                        "A Healer NPC cures resurrection illness and offers paid recovery of lost Constitution.",
                        "/companions status helps find dead, benched or separated companions.",
                        "/companions reset [name] recreates companions beside you with saved progress and gear.",
                        "Reset can affect squads; /companions help recovery explains the recovery actions.",
                    };
                    break;
                case "pvp":
                    lines = new[]
                    {
                        "Camlann is a full-PvP world: realm alone does not make another player an ally.",
                        "Your group, guild, alliance and battlegroup determine allied relationships.",
                        "Capitals, housing and portal-keep hubs are sanctuaries.",
                        "Leveling towns and the Old Frontiers can be dangerous.",
                        "/safety shows your under-level-10 flag; it does not protect in Old Frontiers or battlegrounds.",
                        "/battleground opens brackets, contracts, siege funding and optional group matching.",
                        "/safety off permanently disables that flag; it cannot be turned back on.",
                        "/relics shows guild-owned keep and relic state.",
                        "/cmdhelp gc shows guild command syntax; /level is disabled.",
                    };
                    break;
                default:
                    DisplayMessage(client, "Unknown help topic. Use /offline basics, companions, travel, training, recovery or pvp.");
                    return;
            }

            client.Out.SendCustomTextWindow($"Offline DAoC - {topic}", lines);
        }
    }
}
