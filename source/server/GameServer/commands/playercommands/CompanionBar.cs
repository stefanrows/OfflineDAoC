using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.Events;
using static DOL.GS.CompanionManagerProtocol;

namespace DOL.GS.Commands
{
    /// <summary>What a bare or on/off argument to /companions bar asks for.</summary>
    public enum CompanionBarArgument { Show, On, Off, Usage }

    /// <summary>
    /// Server side of the COMPANIONS window (Custom7). It shows while the account's setting is on and at least one
    /// owned companion is with the player, sends the tile and member-row labels, and turns tile clicks into the same
    /// commands the chat commands use. Clicks arrive through the Companion Manager's command path as controls
    /// 0xA8-0xBF and are routed here before the manager's view-token checks.
    /// </summary>
    public static class CompanionBar
    {
        public const string UsageText = "Usage: /companions bar [on|off]. Bare /companions bar shows the COMPANIONS window again after you closed it.";
        public const string OnMessage = "COMPANIONS window on. It appears while a companion is with you.";
        public const string OffMessage = "COMPANIONS window off. /companions bar on brings it back.";
        public const string DisabledMessage = "COMPANIONS window is off. /companions bar on turns it on.";
        public const string ShownMessage = "COMPANIONS window shown.";
        public const string NoCompanionsMessage = "No companions are with you; the window appears when one joins.";
        public const string GuidanceMessage = "No COMPANIONS window? It needs the updated Companion Manager client files. " +
                                              "Orders still work by command: /aggressive, /defensive, /passive, /pull.";
        public const string NoPullTargetMessage = "No companions are with you to pull.";
        public const string UnknownControlMessage = "That COMPANIONS control is not available.";
        public const string StayRefusalMessage = "Stay works in pet pull mode only: type /petpull on first.";
        public const string PetPullRefusalMessage = "Choose /aggressive or /defensive before enabling /petpull; AssistTrain follows your damage calls.";
        public const string NoPullFallbackMessage = "That target cannot be attacked, so nobody was sent.";

        private const int TickMilliseconds = 1000;
        private const int StartDelayMilliseconds = 2000;
        private const int GuidanceDelayMilliseconds = 3000;
        private const int SlotLimit = BarSlots;

        private sealed class Session
        {
            public bool Enabled;
            public bool Shown;
            public bool InitialStateSent;
            public bool ClientConfirmed;
            public bool GuidancePending;
            public ushort Region;
            public string Message = string.Empty;
            public readonly string[] SentLabels = new string[BarLabelCount];
            public ECSGameTimer Timer;
        }

        private static readonly ConditionalWeakTable<GamePlayer, Session> Sessions = new();

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs arguments) =>
            GameEventMgr.AddHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnGameEntered));

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs arguments) =>
            GameEventMgr.RemoveHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnGameEntered));

        private static void OnGameEntered(DOLEvent e, object sender, EventArgs arguments)
        {
            // Bots have no window; only a real client gets the bar.
            if (sender is GamePlayer player && player.Client is not BotDummyClient)
                Start(player);
        }

        /// <summary>Creates the player's session and its one-second reconcile timer, if it does not exist yet.</summary>
        public static void Start(GamePlayer player)
        {
            if (player != null)
                GetOrCreate(player);
        }

        /// <summary>The account's setting, as the session holds it.</summary>
        public static bool IsEnabled(GamePlayer player) => player != null && GetOrCreate(player).Enabled;

        /// <summary>True for the controls the COMPANIONS window owns, so the manager must not treat them as its own.</summary>
        public static bool IsBarControl(int control) => control >= BarControlFirst && control < BarControlLimit;

        /// <summary>Maps an argument after "/companions bar": empty shows, "on" and "off" set the setting.</summary>
        public static CompanionBarArgument ParseArgument(string argument)
        {
            string text = argument?.Trim();
            if (string.IsNullOrEmpty(text))
                return CompanionBarArgument.Show;
            if (string.Equals(text, "on", StringComparison.OrdinalIgnoreCase))
                return CompanionBarArgument.On;
            if (string.Equals(text, "off", StringComparison.OrdinalIgnoreCase))
                return CompanionBarArgument.Off;
            return CompanionBarArgument.Usage;
        }

        /// <summary>Runs one /companions bar subcommand and returns the reply to print.</summary>
        public static string Command(GamePlayer player, string argument)
        {
            if (player == null)
                return UsageText;
            return ParseArgument(argument) switch
            {
                CompanionBarArgument.On => SetEnabled(player, true),
                CompanionBarArgument.Off => SetEnabled(player, false),
                CompanionBarArgument.Show => ShowNow(player),
                _ => UsageText,
            };
        }

        /// <summary>Saves the account's setting and shows or hides the window at once.</summary>
        public static string SetEnabled(GamePlayer player, bool enabled)
        {
            if (player == null)
                return UsageText;
            CompanionBarPreferences.Save(player, enabled);
            Session session = GetOrCreate(player);
            session.Enabled = enabled;
            if (IsLive(player))
                Reconcile(player, session);
            return enabled ? OnMessage : OffMessage;
        }

        /// <summary>
        /// Bare /companions bar: shows the window again after it was closed with its X. If the client never answers
        /// the show within three seconds, one line explains the client update.
        /// </summary>
        private static string ShowNow(GamePlayer player)
        {
            Session session = GetOrCreate(player);
            if (!session.Enabled)
                return DisabledMessage;
            if (OwnedCompanions(player).Count == 0)
                return NoCompanionsMessage;
            session.Shown = false;
            if (IsLive(player))
                Reconcile(player, session);
            ScheduleGuidance(player, session);
            return ShownMessage;
        }

        /// <summary>
        /// Companions the owner has with him: his own group plus his squads, persistent or temporary, never autonomous
        /// world bots. The same predicate the manager's group detail uses.
        /// </summary>
        public static List<GameBot> OwnedCompanions(GamePlayer player)
        {
            if (player == null)
                return new List<GameBot>();
            return CompanionSquads.OwnerForceBots(player)
                .Where(bot => (bot.PlayerGroupLeader ?? bot.Owner) == player &&
                              (bot.IsPersistentPlayerCompanion || bot.IsTemporaryGroupHelper) && !bot.IsAutonomousWorldBot)
                .Distinct()
                .ToList();
        }

        /// <summary>Tile states for the current state of the player's group orders, pet pull, stay and owned companions.</summary>
        public static CompanionBarTileState[] ComputeTiles(GamePlayer player, int ownedCount)
        {
            bool ordered = CompanionEngagementMode.TryGetGroupOrder(player, out eCompanionEngagementMode order);
            return CompanionBarView.ComputeTiles(ordered, order, CompanionPetPull.IsModeOn(player),
                CompanionPetPull.IsStaying(player), CompanionAssistTrain.Active(player), ownedCount > 0);
        }

        /// <summary>Builds the view from live state. Member rows follow the group window's member order.</summary>
        public static CompanionBarView BuildView(GamePlayer player, string message, IReadOnlyCollection<GameBot> owned)
        {
            var view = new CompanionBarView { Message = message ?? string.Empty };
            CompanionBarTileState[] tiles = ComputeTiles(player, owned.Count);
            Array.Copy(tiles, view.Tiles, tiles.Length);

            bool ordered = CompanionEngagementMode.TryGetGroupOrder(player, out eCompanionEngagementMode order);
            int inGroup = owned.Count(bot => player.Group?.IsInTheGroup(bot) == true);
            view.Status = StatusText(OrderLabel(ordered, order), inGroup, owned.Count - inGroup);

            IEnumerable<GameLiving> rows = player.Group?.GetMembersInTheGroup() ?? Enumerable.Empty<GameLiving>();
            if (player.Group?.IsCompanionRaid == true)
                rows = rows.Where(member => member.GroupIndex < 8);
            GameLiving[] members = rows.Take(SlotLimit).ToArray();
            for (int slot = 0; slot < members.Length; slot++)
            {
                GameLiving member = members[slot];
                bool isOwned = member is GameBot bot && owned.Contains(bot);
                eCompanionEngagementMode effective = isOwned
                    ? CompanionEngagementMode.Effective(member) : eCompanionEngagementMode.Aggressive;
                view.Slots[slot] = CompanionBarView.DescribeSlot(isOwned, member.IsAlive,
                    member.CurrentRegionID == player.CurrentRegionID, effective);
            }
            return view;
        }

        /// <summary>The status line: "{order} | {n} with you", plus " | {m} in squads" when some are in squads.</summary>
        public static string StatusText(string orderLabel, int withYou, int inSquads)
        {
            string text = $"{orderLabel} | {withYou} with you";
            return inSquads > 0 ? $"{text} | {inSquads} in squads" : text;
        }

        /// <summary>"Own stances" when no group order is set; otherwise the order's name, "Assist train" for AssistTrain.</summary>
        public static string OrderLabel(bool ordered, eCompanionEngagementMode order) =>
            !ordered ? "Own stances" : order == eCompanionEngagementMode.AssistTrain ? "Assist train" : order.ToString();

        /// <summary>The first sentence of a command reply, short enough for the one-line message label.</summary>
        public static string ShortMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            int end = text.IndexOf(". ", StringComparison.Ordinal);
            return end > 0 ? text[..(end + 1)] : text.Trim();
        }

        /// <summary>
        /// A click from the window (control 0xA8-0xBF). The ready control arrives once the client has shown the window;
        /// every other control runs its tile. Only PULL and refusals are echoed to chat; stances and camp stay quiet.
        /// </summary>
        public static void HandleClientControl(GamePlayer player, int control)
        {
            if (player == null || !IsBarControl(control))
                return;
            Session session = GetOrCreate(player);
            session.ClientConfirmed = true;
            if (control == BarControlReady)
            {
                Array.Clear(session.SentLabels);
                Render(player, session);
                return;
            }

            int tile = control - BarControlFirst;
            List<GameBot> owned = OwnedCompanions(player);
            CompanionBarTileState state = tile < BarTiles ? ComputeTiles(player, owned.Count)[tile] : CompanionBarTileState.Disabled;
            (string message, bool echo) = tile < BarTiles ? ApplyTile(player, tile, state) : (UnknownControlMessage, true);
            session.Message = message;
            if (echo && !string.IsNullOrEmpty(message) && IsLive(player))
                CompanionManager.Tell(player, message);
            if (IsLive(player))
                Render(player, session);
        }

        private static (string Message, bool Echo) ApplyTile(GamePlayer player, int tile, CompanionBarTileState state)
        {
            if (state == CompanionBarTileState.Disabled)
                return tile switch
                {
                    BarControlStay - BarControlFirst => (StayRefusalMessage, true),
                    BarControlPetPull - BarControlFirst => (PetPullRefusalMessage, true),
                    BarControlPull - BarControlFirst => (NoPullTargetMessage, true),
                    _ => (UnknownControlMessage, true),
                };

            switch (tile + BarControlFirst)
            {
                case BarControlAggressive:
                    return (ShortMessage(CompanionGroupOrders.Aggressive(player)), false);
                case BarControlDefensive:
                    return (ShortMessage(CompanionGroupOrders.Defensive(player)), false);
                case BarControlPassive:
                    return (ShortMessage(CompanionGroupOrders.Passive(player)), false);
                case BarControlAssist:
                    return (ShortMessage(CompanionGroupOrders.AssistTrain(player)), false);
                case BarControlDefault:
                    return (ShortMessage(CompanionGroupOrders.UseSavedStances(player)), false);
                case BarControlFollow:
                    return (ShortMessage(CompanionPetPull.SetStay(player, false)), false);
                case BarControlStay:
                {
                    string reply = CompanionPetPull.SetStay(player, true);
                    // A refused stay leaves no stay on; that reply is worth a chat line.
                    return CompanionPetPull.IsStaying(player) ? (ShortMessage(reply), false) : (reply, true);
                }
                case BarControlPetPull:
                {
                    bool wanted = !CompanionPetPull.IsModeOn(player);
                    string reply = CompanionPetPull.SetMode(player, wanted);
                    return CompanionPetPull.IsModeOn(player) == wanted ? (ShortMessage(reply), false) : (reply, true);
                }
                case BarControlPull:
                {
                    // A null reply means the attack rules already told the player why; keep it out of chat.
                    string reply = PullGroupCommandHandler.Order(player, "click PULL");
                    return reply == null ? (NoPullFallbackMessage, false) : (reply, true);
                }
                case BarControlManager:
                    CompanionManager.OpenGroupOrders(player);
                    return (string.Empty, false);
                default:
                    return (UnknownControlMessage, true);
            }
        }

        private static Session GetOrCreate(GamePlayer player)
        {
            return Sessions.GetValue(player, p =>
            {
                var session = new Session { Enabled = CompanionBarPreferences.Load(p) };
                session.Timer = new ECSGameTimer(p) { Callback = _ => Tick(p, session) };
                session.Timer.Start(StartDelayMilliseconds);
                return session;
            });
        }

        private static int Tick(GamePlayer player, Session session)
        {
            GameClient client = player.Client;
            if (client == null || player.ObjectState == GameObject.eObjectState.Deleted ||
                client.ClientState is GameClient.eClientState.Linkdead or GameClient.eClientState.Disconnected or
                    GameClient.eClientState.CharScreen or GameClient.eClientState.NotConnected)
            {
                session.Timer?.Stop();
                Sessions.Remove(player);
                return 0;
            }
            if (!IsLive(player))
                return TickMilliseconds;
            Reconcile(player, session);
            return TickMilliseconds;
        }

        private static bool IsLive(GamePlayer player) =>
            player.ObjectState == GameObject.eObjectState.Active && player.Client?.ClientState == GameClient.eClientState.Playing;

        /// <summary>Shows or hides the window to match the setting and owned companions; sends labels once confirmed.</summary>
        private static void Reconcile(GamePlayer player, Session session)
        {
            if (session.Region != player.CurrentRegionID)
            {
                session.Region = player.CurrentRegionID;
                Array.Clear(session.SentLabels);
            }
            bool want = session.Enabled && OwnedCompanions(player).Count > 0;
            if (want && !session.Shown)
            {
                session.Shown = true;
                CompanionManager.Send(player, OpBarShow);
            }
            else if (!want && (session.Shown || !session.InitialStateSent))
            {
                session.Shown = false;
                CompanionManager.Send(player, OpBarHide);
            }
            session.InitialStateSent = true;
            if (session.ClientConfirmed && session.Shown)
                Render(player, session);
        }

        /// <summary>Sends the labels that differ from what the client already has.</summary>
        private static void Render(GamePlayer player, Session session)
        {
            string[] labels = BuildView(player, session.Message, OwnedCompanions(player)).ToLabels();
            for (int index = 0; index < labels.Length; index++)
            {
                if (string.Equals(session.SentLabels[index], labels[index], StringComparison.Ordinal))
                    continue;
                CompanionManager.Send(player, OpLabel, BarLabelBase + index, labels[index]);
                session.SentLabels[index] = labels[index];
            }
        }

        private static void ScheduleGuidance(GamePlayer player, Session session)
        {
            if (session.ClientConfirmed || session.GuidancePending || !IsLive(player))
                return;
            session.GuidancePending = true;
            _ = new ECSGameTimer(player, _ =>
            {
                session.GuidancePending = false;
                if (!session.ClientConfirmed && Sessions.TryGetValue(player, out Session current) &&
                    ReferenceEquals(current, session) && IsLive(player))
                    CompanionManager.Tell(player, GuidanceMessage);
                return 0;
            }, GuidanceDelayMilliseconds);
        }
    }
}
