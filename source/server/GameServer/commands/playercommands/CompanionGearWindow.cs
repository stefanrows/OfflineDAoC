using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.Database;
using static DOL.GS.CompanionManagerProtocol;

namespace DOL.GS.Commands
{
    /// <summary>
    /// Server side of the COMPANION GEAR window (Custom6, TASKS 129, docs/COMPANION_PAPERDOLL_RE.md): one active
    /// companion's 19 worn slots and 40-slot backpack with drag and drop, opened by /companions gear and by the
    /// Companion Manager's [Open inventory]. The slots are PersistentCompanionInventoryView's house-vault client slots
    /// and every move goes through that view. The labels are the title, the backpack count, the last reply, the worn
    /// captions and a "*" on each item that stays with the companion. The client answers every show with
    /// <see cref="CompanionManagerProtocol.ControlGearReady"/>; a client that never answers (a manager client older
    /// than 0.276.0) gets the stock House Vault list instead, three seconds after its first open and at once after that.
    /// </summary>
    public static class CompanionGearWindow
    {
        public const string UsageText = "Usage: /companions gear [name] opens a companion's gear window; " +
                                        "/companions gear off closes it.";
        public const string NoCompanionMessage = "Invite a companion first; the gear window opens only for an active companion.";
        public const string ClosedMessage = "Companion gear window closed.";
        public const string FallbackMessage = "This client has no COMPANION GEAR window yet, so the House Vault list opened " +
                                              "instead. Update the Companion Manager client files to get it.";
        public const string MarkText = "*";
        public const int BackpackSlots = 40;
        private const string OffArgument = "off";
        private const int GuidanceDelayMilliseconds = 3000;

        /// <summary>Captions under the worn slots, in <see cref="PersistentCompanionGear.SheetSlots"/> order.</summary>
        public static readonly string[] WornCaptions =
        {
            "Helm", "Chest", "Arms", "Hands", "Legs", "Feet", "Cloak", "Neck", "Jewel", "Belt",
            "L Wrist", "R Wrist", "L Ring", "R Ring", "R Hand", "L Hand", "2 Hand", "Ranged", "Mythic",
        };

        private sealed class Session
        {
            public bool ClientConfirmed;
            public bool ClientMissing;
            public string Message = string.Empty;
            public readonly string[] SentLabels = new string[GearLabelCount];
        }

        private static readonly ConditionalWeakTable<GamePlayer, Session> Sessions = new();

        /// <summary>Handles the words after "gear": [name] opens, "off" closes.</summary>
        public static string Command(GamePlayer player, IEnumerable<string> arguments)
        {
            if (player == null)
                return UsageText;
            List<string> words = (arguments ?? Array.Empty<string>()).Where(word => !string.IsNullOrWhiteSpace(word)).ToList();
            if (words.Count == 1 && words[0].Equals(OffArgument, StringComparison.OrdinalIgnoreCase))
                return Close(player);
            if (!TryChooseCompanion(player, string.Join(' ', words), out GameBot companion, out string message))
                return message;
            TryOpen(player, companion.PlayerCompanionRecord.CompanionId, out _, out message);
            return message;
        }

        /// <summary>
        /// Opens the gear window for an active companion; <paramref name="message"/> says what opened. Without a
        /// confirmed client the stock House Vault list follows after three seconds unless the client answers first.
        /// </summary>
        public static bool TryOpen(GamePlayer player, string companionId, out PersistentCompanionInventoryView view,
            out string message)
        {
            view = null;
            if (player == null || !PlayerCompanionRoster.TryGetActiveCompanionById(player, companionId, out GameBot companion))
            {
                message = "Invite that companion first; the gear window opens only for an active companion.";
                return false;
            }
            Session session = Sessions.GetValue(player, _ => new Session());
            session.Message = string.Empty;
            view = new PersistentCompanionInventoryView(player, companionId, gearWindow: true, stockWindow: session.ClientMissing);
            view.Open();
            if (!session.ClientConfirmed && !session.ClientMissing)
                ScheduleFallback(player, session, view);
            message = session.ClientMissing
                ? $"{companion.Name}'s inventory is open: worn slots 1-19, backpack 21-60. Drag from your bag onto a worn slot to equip."
                : $"{companion.Name}'s gear window is open. Click one of your items, then a slot; click a filled slot to pick its item up.";
            return true;
        }

        /// <summary>The ready control: the client has the window. Every label is sent again.</summary>
        public static void HandleClientControl(GamePlayer player)
        {
            if (player == null)
                return;
            Session session = Sessions.GetValue(player, _ => new Session());
            session.ClientConfirmed = true;
            session.ClientMissing = false;
            Array.Clear(session.SentLabels);
            if (player.ActiveInventoryObject is PersistentCompanionInventoryView { IsGearWindow: true } view)
                view.Refresh();
        }

        /// <summary>The reply shown in the window until the next move or open.</summary>
        public static void SetMessage(GamePlayer player, string message)
        {
            if (player != null)
                Sessions.GetValue(player, _ => new Session()).Message = message ?? string.Empty;
        }

        /// <summary>Sends the labels that changed for the view's companion and client inventory.</summary>
        public static void Render(GamePlayer player, string companionName, IReadOnlyDictionary<int, DbInventoryItem> slots,
            Func<DbInventoryItem, bool> staysWithCompanion)
        {
            if (player == null)
                return;
            Session session = Sessions.GetValue(player, _ => new Session());
            string[] labels = BuildLabels(companionName, slots, staysWithCompanion, session.Message);
            for (int index = 0; index < labels.Length; index++)
            {
                if (string.Equals(session.SentLabels[index], labels[index], StringComparison.Ordinal))
                    continue;
                CompanionManager.Send(player, OpLabel, GearLabelBase + index, labels[index]);
                session.SentLabels[index] = labels[index];
            }
        }

        /// <summary>
        /// The <see cref="CompanionManagerProtocol.GearLabelCount"/> label values, from label GearLabelBase on.
        /// <paramref name="slots"/> holds the item at each gear slot (0-18 worn, 19-58 backpack); missing keys are empty.
        /// </summary>
        public static string[] BuildLabels(string companionName, IReadOnlyDictionary<int, DbInventoryItem> slots,
            Func<DbInventoryItem, bool> staysWithCompanion, string message)
        {
            var labels = new string[GearLabelCount];
            labels[GearLabelTitle - GearLabelBase] = Fit($"COMPANION GEAR - {Sanitize(companionName)}", GearWidthTitle);
            int backpack = slots?.Count(entry => entry.Key >= GearWornSlots && entry.Key < GearSlots && entry.Value != null) ?? 0;
            labels[GearLabelBackpack - GearLabelBase] = $"Backpack {backpack}/{BackpackSlots}";
            IReadOnlyList<string> lines = CompanionManagerSession.Wrap(Sanitize(message), GearWidthMessage);
            for (int line = 0; line < GearMessageLines; line++)
            {
                string text = line < lines.Count ? lines[line] : string.Empty;
                if (line == GearMessageLines - 1 && lines.Count > GearMessageLines)
                    text = Fit(text + " " + lines[line + 1], GearWidthMessage);
                labels[GearLabelMessageBase - GearLabelBase + line] = text;
            }
            for (int index = 0; index < GearWornSlots; index++)
                labels[GearLabelCaptionBase - GearLabelBase + index] = WornCaptions[index];
            for (int index = 0; index < GearSlots; index++)
            {
                DbInventoryItem item = slots != null && slots.TryGetValue(index, out DbInventoryItem found) ? found : null;
                labels[GearLabelMarkBase - GearLabelBase + index] =
                    item != null && staysWithCompanion != null && staysWithCompanion(item) ? MarkText : string.Empty;
            }
            return labels;
        }

        /// <summary>The House Vault list replaced the gear window; tell the player once per open.</summary>
        private static void ScheduleFallback(GamePlayer player, Session session, PersistentCompanionInventoryView view)
        {
            if (player.Client?.ClientState != GameClient.eClientState.Playing)
                return;
            _ = new ECSGameTimer(player, _ =>
            {
                if (!session.ClientConfirmed && ReferenceEquals(player.ActiveInventoryObject, view) &&
                    player.Client?.ClientState == GameClient.eClientState.Playing)
                {
                    session.ClientMissing = true;
                    view.ShowStockWindow();
                    CompanionManager.Tell(player, FallbackMessage);
                }
                return 0;
            }, GuidanceDelayMilliseconds);
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
                : $"Name the companion: /companions gear <name>. With you: {string.Join(", ", active.Select(bot => bot.Name))}.";
            return false;
        }

        private static string Close(GamePlayer player)
        {
            // Removing the view's observer hides the window; without an open view the hide is sent directly.
            if (player.ActiveInventoryObject is PersistentCompanionInventoryView { IsGearWindow: true } view)
            {
                view.RemoveObserver(player);
                player.ActiveInventoryObject = null;
            }
            else
                CompanionManager.Send(player, OpGearHide);
            return ClosedMessage;
        }
    }
}
