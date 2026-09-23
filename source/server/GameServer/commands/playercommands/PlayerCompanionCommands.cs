using System;
using System.Linq;

namespace DOL.GS.Commands
{

    [CmdAttribute("&companions", ePrivLevel.Player,
        "Manage your persistent companion roster", "/companions [list | recruit <class> | recruit <realm> <class> | invite <name> | bench <name>]")]
    public sealed class PlayerCompanionCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;
            if (player == null)
                return;

            if (args.Length == 1 || args[1].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                ShowRoster(client, player);
                return;
            }

            string action = args[1].ToLowerInvariant();
            switch (action)
            {
                case "recruit":
                    Recruit(client, player, args);
                    break;
                case "invite":
                    UpdateCompanion(client, player, args, invite: true);
                    break;
                case "bench":
                    UpdateCompanion(client, player, args, invite: false);
                    break;
                default:
                    ShowUsage(client);
                    break;
            }
        }

        private void ShowRoster(GameClient client, GamePlayer player)
        {
            if (!PlayerCompanionRoster.TryGetRoster(player, out var records))
            {
                DisplayMessage(client, "Your companion roster could not be loaded. Try again later.");
                return;
            }

            DisplayMessage(client, $"Persistent companions: {records.Count}/{PlayerCompanionRoster.MaximumRosterSize}.");
            if (records.Count == 0)
            {
                DisplayMessage(client, "Your roster is empty. Recruit with /companions recruit <class>.");
                return;
            }

            foreach (var realmGroup in records
                         .OrderBy(record => record.Realm)
                         .ThenBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
                         .GroupBy(record => record.Realm))
            {
                DisplayMessage(client, $"{TemporaryGroupClassCatalog.RealmName((eRealm)realmGroup.Key)}:");
                foreach (PlayerCompanionRecord record in realmGroup)
                {
                    string state = record.IsActive ? "active" : "benched";
                    DisplayMessage(client, $"{record.Name}, level {record.Level} {(eCharacterClass)record.ClassId} ({state})");
                }
            }
        }

        private void Recruit(GameClient client, GamePlayer player, string[] args)
        {
            if (args.Length < 3)
            {
                DisplayMessage(client, "Use /companions recruit <class>, or /companions recruit <realm> <class>.");
                return;
            }

            string requestedClass = string.Join(' ', args.Skip(2));
            if (!TemporaryGroupClassCatalog.TryResolveForCompanion(player.Realm, requestedClass,
                    out eRealm realm, out eCharacterClass characterClass, out bool ambiguous))
            {
                DisplayMessage(client, ambiguous
                    ? $"'{requestedClass}' is used by more than one realm. Use /companions recruit <realm> <class>."
                    : $"'{requestedClass}' is not a supported Classic + SI class. Type /classes to see the catalog.");
                return;
            }

            PlayerCompanionRoster.TryRecruit(player, realm, characterClass, out _, out string message);
            DisplayMessage(client, message);
        }

        private void UpdateCompanion(GameClient client, GamePlayer player, string[] args, bool invite)
        {
            if (args.Length < 3)
            {
                DisplayMessage(client, invite
                    ? "Use /companions invite <name>. Type /companions list to see companion names."
                    : "Use /companions bench <name>. Type /companions list to see companion names.");
                return;
            }

            string companionName = string.Join(' ', args.Skip(2));
            string message;
            if (invite)
                PlayerCompanionRoster.TryInvite(player, companionName, out message);
            else
                PlayerCompanionRoster.TryBench(player, companionName, out message);
            DisplayMessage(client, message);
        }

        private void ShowUsage(GameClient client)
        {
            DisplayMessage(client, "Commands: /companions list | recruit <class> | recruit <realm> <class> | invite <name> | bench <name>.");
            DisplayMessage(client, "Recruitment is free, starts at level 1, and works anywhere. Type /classes for names grouped by realm.");
        }
    }
}