using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS.Commands
{
    /// <summary>
    /// /companions gearprobe: opens one active companion's inventory view with the Custom6 paperdoll probe (TASKS 129,
    /// docs/COMPANION_PAPERDOLL_RE.md). Research only. The probe window has four slots: the helm, the chest and the first
    /// two backpack slots (view positions 1, 2, 21, 22); every move goes through PersistentCompanionInventoryView exactly
    /// as from the House Vault window. "plain" opens the view without the stock House Vault window.
    /// </summary>
    public static class CompanionPaperdollProbe
    {
        public const string UsageText = "Usage: /companions gearprobe [name] [plain] or /companions gearprobe off. " +
                                        "Research probe for the companion character window; it needs the probe client files.";
        public const string NoCompanionMessage = "Invite a companion first; the gear probe opens only for an active companion.";
        public const string ClosedMessage = "Gear probe closed.";
        private const string PlainArgument = "plain";
        private const string OffArgument = "off";

        /// <summary>Handles the words after "gearprobe".</summary>
        public static string Command(GamePlayer player, IEnumerable<string> arguments)
        {
            if (player == null)
                return UsageText;
            List<string> words = (arguments ?? Array.Empty<string>()).Where(word => !string.IsNullOrWhiteSpace(word)).ToList();
            if (words.Count == 1 && words[0].Equals(OffArgument, StringComparison.OrdinalIgnoreCase))
                return Close(player);
            bool plain = words.Count > 0 && words[^1].Equals(PlainArgument, StringComparison.OrdinalIgnoreCase);
            if (plain)
                words.RemoveAt(words.Count - 1);
            if (!TryChooseCompanion(player, string.Join(' ', words), out GameBot companion, out string message))
                return message;

            var view = new PersistentCompanionInventoryView(player, companion.PlayerCompanionRecord.CompanionId,
                paperdoll: true, stockWindow: !plain);
            view.Open();
            return $"Companion gear window open for {companion.Name}: 19 worn slots and the 40-slot backpack" +
                   (plain ? ". " : " (the House Vault window opens too). ") +
                   "Click one of your items, then a slot; click a filled slot to pick its item up.";
        }

        private static bool TryChooseCompanion(GamePlayer player, string name, out GameBot companion, out string message)
        {
            message = null;
            companion = null;
            if (name.Length > 0)
            {
                if (PlayerCompanionRoster.TryGetActiveCompanion(player, name, out companion) && companion.IsPersistentPlayerCompanion)
                    return true;
                message = $"No active companion named {name}. Invite it first.";
                return false;
            }
            List<GameBot> active = PlayerCompanionRoster.GetActiveCompanions(player)
                .Where(candidate => candidate.IsPersistentPlayerCompanion)
                .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (active.Count == 1)
            {
                companion = active[0];
                return true;
            }
            message = active.Count == 0
                ? NoCompanionMessage
                : $"Name the companion: /companions gearprobe <name>. With you: {string.Join(", ", active.Select(bot => bot.Name))}.";
            return false;
        }

        private static string Close(GamePlayer player)
        {
            CompanionManager.Send(player, CompanionManagerProtocol.OpPaperdollHide);
            if (player.ActiveInventoryObject is PersistentCompanionInventoryView { IsPaperdoll: true } view)
            {
                view.RemoveObserver(player);
                player.ActiveInventoryObject = null;
            }
            return ClosedMessage;
        }
    }
}
