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
    /// Server side of the COMPANIONS window (Custom7). It shows while the account's setting is on and the player
    /// is in a group or has an owned companion with him, sends the tile and member-row labels, and turns tile and
    /// row clicks into the same commands the chat commands use. Clicks arrive through the Companion Manager's
    /// command path as controls 0xA8-0xBF and are routed here before the manager's view-token checks.
    /// </summary>
    public static class CompanionBar
    {
        public const string UsageText = "Usage: /companions bar [on|off]. Bare /companions bar shows the COMPANIONS window again after you closed it.";
        public const string OnMessage = "COMPANIONS window on. It appears while you are grouped or a companion is with you.";
        public const string OffMessage = "COMPANIONS window off. /companions bar on brings it back.";
        public const string DisabledMessage = "COMPANIONS window is off. /companions bar on turns it on.";
        public const string ShownMessage = "COMPANIONS window shown.";
        public const string NoCompanionsMessage = "You are not grouped and no companion is with you; the window appears when one joins.";
        public const string GuidanceMessage = "No COMPANIONS window? It needs the updated Companion Manager client files. " +
                                              "Orders still work by command: /aggressive, /defensive, /passive, /pull.";
        public const string NoPullTargetMessage = "No companions are with you to pull.";
        public const string UnknownControlMessage = "That COMPANIONS control is not available.";
        public const string StayRefusalMessage = "Stay works in pet pull mode only: type /petpull on first.";
        public const string PetPullRefusalMessage = "Choose /aggressive or /defensive before enabling /petpull; AssistTrain follows your damage calls.";
        public const string NoPullFallbackMessage = "That target cannot be attacked, so nobody was sent.";
        public const string NotGroupedMessage = "You are not in a group.";
        public const string NotLeaderMessage = "You are not the leader of your group.";
        public const string KickNoTargetMessage = "Click a member's row first, then Kick.";
        public const string KickSelfMessage = "Use Leave to leave the group yourself.";
        public const string LeaderNoTargetMessage = "Click a player's row first, then Leader.";
        public const string LeaderNotPlayerMessage = "Only a player can lead the group.";
        public const string LeaderAlreadyMessage = "You are the group leader already.";
        public const string LeaveConfirmMessage = "Click Leave again within 10 seconds to leave the group.";
        public const string LeftGroupMessage = "You left the group.";
        public const string EmptyRowMessage = "That row is empty.";
        public const string GrindStoppedMessage = "Grind mode stopped.";

        private const int TickMilliseconds = 1000;
        private const int StartDelayMilliseconds = 2000;
        private const int GuidanceDelayMilliseconds = 3000;
        private const int SlotLimit = BarSlots;
        private static readonly TimeSpan LeaveConfirmWindow = TimeSpan.FromSeconds(10);

        private sealed class Session
        {
            public bool Enabled;
            public bool Shown;
            public bool InitialStateSent;
            public bool ClientConfirmed;
            public bool GuidancePending;
            public ushort Region;
            public string Message = string.Empty;
            public DateTime LeaveConfirmUntil = DateTime.MinValue;
            public readonly string[] SentLabels = new string[BarLabelCount];
            public ECSGameTimer Timer;

            public bool LeavePending(DateTime now) => LeaveConfirmUntil > now;
        }

        private static readonly ConditionalWeakTable<GamePlayer, Session> Sessions = new();

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs arguments)
        {
            GameEventMgr.AddHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnGameEntered));
            GameEventMgr.AddHandler(GamePlayerEvent.RegionChanged, new DOLEventHandler(OnRegionChanged));
        }

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs arguments)
        {
            GameEventMgr.RemoveHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnGameEntered));
            GameEventMgr.RemoveHandler(GamePlayerEvent.RegionChanged, new DOLEventHandler(OnRegionChanged));
        }

        private static void OnGameEntered(DOLEvent e, object sender, EventArgs arguments)
        {
            // Bots have no window; only a real client gets the bar.
            if (sender is GamePlayer player && player.Client is not BotDummyClient)
                Start(player);
        }

        /// <summary>
        /// Every zone load rebuilds the client's UI: the native init hook clears the window's click flag, so the
        /// server must show the window again (op 5 sets the flag and sends the ready control) and resend all labels.
        /// The next one-second tick does this once the player is live in the new region.
        /// </summary>
        private static void OnRegionChanged(DOLEvent e, object sender, EventArgs arguments)
        {
            if (sender is GamePlayer player && player.Client is not BotDummyClient && Sessions.TryGetValue(player, out Session session))
                ResetClientState(session);
        }

        private static void ResetClientState(Session session)
        {
            session.Shown = false;
            session.InitialStateSent = false;
            Array.Clear(session.SentLabels);
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

        /// <summary>True for a member row's stance click; the row is control - BarControlSlotBase.</summary>
        public static bool IsSlotControl(int control) => control >= BarControlSlotBase && control < BarControlSlotBase + BarSlots;

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
            if (!WantsWindow(player))
                return NoCompanionsMessage;
            session.Shown = false;
            if (IsLive(player))
                Reconcile(player, session);
            ScheduleGuidance(player, session);
            return ShownMessage;
        }

        /// <summary>The window has something to show while the player is grouped or an owned companion is with him.</summary>
        public static bool WantsWindow(GamePlayer player) =>
            player != null && (player.Group != null || OwnedCompanions(player).Count > 0);

        /// <summary>
        /// Companions the owner has with him: his own group plus his squads, persistent or temporary, never autonomous
        /// world bots. The same predicate the manager's group detail uses.
        /// </summary>
        public static List<GameBot> OwnedCompanions(GamePlayer player)
        {
            if (player == null)
                return new List<GameBot>();
            return CompanionSquads.OwnerForceBots(player)
                .Where(bot => IsOwned(bot, player))
                .Distinct()
                .ToList();
        }

        private static bool IsOwned(GameBot bot, GamePlayer player) =>
            (bot.PlayerGroupLeader ?? bot.Owner) == player && IsCompanion(bot);

        /// <summary>A player's companion or temporary helper, whoever the owner is; never an autonomous world bot.</summary>
        private static bool IsCompanion(GameBot bot) =>
            (bot.IsPersistentPlayerCompanion || bot.IsTemporaryGroupHelper) && !bot.IsAutonomousWorldBot &&
            (bot.PlayerGroupLeader ?? bot.Owner) != null;

        /// <summary>Tile states for the current state of the player's group orders, pet pull, stay, group and owned companions.</summary>
        public static CompanionBarTileState[] ComputeTiles(GamePlayer player, int ownedCount, bool leavePending = false)
        {
            bool ordered = CompanionEngagementMode.TryGetGroupOrder(player, out eCompanionEngagementMode order);
            Group group = player.Group;
            return CompanionBarView.ComputeTiles(ordered, order, CompanionPetPull.IsModeOn(player),
                CompanionPetPull.IsStaying(player), CompanionAssistTrain.Active(player), ownedCount > 0,
                grouped: group != null, leads: group != null && group.Leader == player && group.MemberCount >= 2,
                leavePending: leavePending, grinding: PlayerCompanionGrind.IsActive(player));
        }

        /// <summary>The group members by window row: the group's slot order, which the native adapters also use.</summary>
        public static GameLiving[] MembersBySlot(GamePlayer player)
        {
            var members = new GameLiving[SlotLimit];
            Group group = player?.Group;
            if (group == null)
                return members;
            foreach (GameLiving member in group.GetMembersInTheGroup())
            {
                int slot = member.GroupIndex;
                if (slot >= 0 && slot < SlotLimit && members[slot] == null)
                    members[slot] = member;
            }
            return members;
        }

        /// <summary>Builds the view from live state. Member rows follow the group window's member order.</summary>
        public static CompanionBarView BuildView(GamePlayer player, string message, IReadOnlyCollection<GameBot> owned,
            bool leavePending = false)
        {
            var view = new CompanionBarView { Message = message ?? string.Empty };
            CompanionBarTileState[] tiles = ComputeTiles(player, owned.Count, leavePending);
            Array.Copy(tiles, view.Tiles, tiles.Length);
            if (PlayerCompanionGrind.IsActive(player))
                view.TileLabels[BarControlGrind - BarControlFirst] = CompanionBarView.GrindActiveText;

            bool ordered = CompanionEngagementMode.TryGetGroupOrder(player, out eCompanionEngagementMode order);
            Group group = player.Group;
            int inGroup = owned.Count(bot => group?.IsInTheGroup(bot) == true);
            view.Status = StatusText(OrderLabel(ordered, order), inGroup, owned.Count - inGroup, group?.MemberCount ?? 0);

            GameLiving[] members = MembersBySlot(player);
            GameLiving leader = group?.LivingLeader;
            for (int slot = 0; slot < members.Length; slot++)
            {
                GameLiving member = members[slot];
                if (member == null)
                    continue;
                view.Leaders[slot] = leader != null && member == leader;
                // Any companion in the group shows its stance, also another player's: Effective resolves the
                // companion's own leader. Only the owner's rows react to clicks (ApplyStanceClick).
                bool isCompanion = member is GameBot bot && (owned.Contains(bot) || IsCompanion(bot));
                eCompanionEngagementMode effective = isCompanion
                    ? CompanionEngagementMode.Effective(member) : eCompanionEngagementMode.Aggressive;
                view.Slots[slot] = CompanionBarView.DescribeSlot(isCompanion, member.IsAlive,
                    member.CurrentRegionID == player.CurrentRegionID, effective);
            }
            return view;
        }

        /// <summary>
        /// The status line: "{order} | {n} with you", plus " | {m} in squads" when some are in squads. A group
        /// without companions reads "Group of {size} | no companions".
        /// </summary>
        public static string StatusText(string orderLabel, int withYou, int inSquads, int groupSize)
        {
            if (withYou == 0 && inSquads == 0 && groupSize > 0)
                return $"Group of {groupSize} | no companions";
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
        /// a tile control runs its tile and a row control cycles that member's saved stance. Only PULL and refusals
        /// are echoed to chat; stances, camp and group changes stay quiet (the group itself reports joins and leaves).
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

            DateTime now = DateTime.UtcNow;
            bool leavePending = session.LeavePending(now);
            List<GameBot> owned = OwnedCompanions(player);
            (string message, bool echo) = (UnknownControlMessage, true);
            int tile = control - BarControlFirst;
            if (tile < BarTiles)
            {
                CompanionBarTileState state = ComputeTiles(player, owned.Count, leavePending)[tile];
                (message, echo) = ApplyTile(player, session, tile, state, now);
            }
            else if (IsSlotControl(control))
            {
                (message, echo) = ApplyStanceClick(player, control - BarControlSlotBase);
            }
            if (control != BarControlLeave)
                session.LeaveConfirmUntil = DateTime.MinValue;
            session.Message = message;
            if (echo && !string.IsNullOrEmpty(message) && IsLive(player))
                CompanionManager.Tell(player, message);
            if (IsLive(player))
                Render(player, session);
        }

        private static (string Message, bool Echo) ApplyTile(GamePlayer player, Session session, int tile,
            CompanionBarTileState state, DateTime now)
        {
            if (state == CompanionBarTileState.Disabled)
                return tile switch
                {
                    BarControlStay - BarControlFirst => (StayRefusalMessage, true),
                    BarControlPetPull - BarControlFirst => (PetPullRefusalMessage, true),
                    BarControlPull - BarControlFirst => (NoPullTargetMessage, true),
                    BarControlKick - BarControlFirst => (NotGroupedMessage, true),
                    BarControlLeave - BarControlFirst => (NotGroupedMessage, true),
                    BarControlLeader - BarControlFirst => (player.Group == null || player.Group.MemberCount < 2
                        ? NotGroupedMessage : NotLeaderMessage, true),
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
                case BarControlKick:
                    return Kick(player);
                case BarControlLeader:
                    return MakeLeader(player);
                case BarControlLeave:
                {
                    if (session.LeavePending(now))
                    {
                        session.LeaveConfirmUntil = DateTime.MinValue;
                        LeaveGroup(player);
                        return (LeftGroupMessage, false);
                    }
                    session.LeaveConfirmUntil = now + LeaveConfirmWindow;
                    return (LeaveConfirmMessage, false);
                }
                case BarControlGrind:
                {
                    if (PlayerCompanionGrind.IsActive(player))
                    {
                        // Stop tells the player in chat itself; only the window line is set here.
                        PlayerCompanionGrind.Stop(player, "cancelled by you");
                        return (GrindStoppedMessage, false);
                    }
                    bool started = PlayerCompanionGrind.TryStart(player, out string reply);
                    return (ShortMessage(reply), !started);
                }
                default:
                    return (UnknownControlMessage, true);
            }
        }

        /// <summary>
        /// Kick acts on the targeted group member (a row click targets it): an owned persistent companion is benched
        /// through the roster, an owned temporary helper is dismissed, and any other member is removed the way
        /// /disband &lt;name&gt; does, which needs the leader.
        /// </summary>
        private static (string Message, bool Echo) Kick(GamePlayer player)
        {
            Group group = player.Group;
            if (group == null)
                return (NotGroupedMessage, true);
            if (player.TargetObject is not GameLiving target || target.Group != group)
                return (KickNoTargetMessage, true);
            if (target == player)
                return (KickSelfMessage, true);
            if (target is GameBot bot && IsOwned(bot, player))
            {
                if (bot.PlayerCompanionRecord is { } record)
                {
                    bool benched = PlayerCompanionRoster.TryBench(player, record.CompanionId, out string reply);
                    return (ShortMessage(reply), !benched);
                }
                group.RemoveMember(bot);
                bot.Delete();
                return ($"{bot.Name} dismissed.", false);
            }
            if (group.Leader != player)
                return (NotLeaderMessage, true);
            group.RemoveMember(target);
            return ($"{target.Name} removed from the group.", false);
        }

        /// <summary>Leader hands the group to the targeted player, like /makeleader on a target.</summary>
        private static (string Message, bool Echo) MakeLeader(GamePlayer player)
        {
            Group group = player.Group;
            if (group == null || group.MemberCount < 2)
                return (NotGroupedMessage, true);
            if (group.Leader != player)
                return (NotLeaderMessage, true);
            if (player.TargetObject is not GameLiving target || target.Group != group)
                return (LeaderNoTargetMessage, true);
            if (target == player)
                return (LeaderAlreadyMessage, true);
            if (target is not GamePlayer next)
                return (LeaderNotPlayerMessage, true);
            group.MakeLeader(next);
            return ($"{next.Name} leads the group now.", false);
        }

        /// <summary>Leave does what bare /disband does: closes a companion raid, dismisses temporary helpers, leaves.</summary>
        private static void LeaveGroup(GamePlayer player)
        {
            Group group = player.Group;
            if (group == null)
                return;
            CompanionRaid.Close(player);
            GameBot[] helpers = group.GetMembersInTheGroup().OfType<GameBot>()
                .Where(bot => bot.IsTemporaryGroupHelper && bot.Owner == player)
                .ToArray();
            foreach (GameBot helper in helpers)
                helper.Delete();
            player.Group?.RemoveMember(player);
        }

        /// <summary>
        /// A click on a row's stance column cycles that companion's saved stance (aggressive, defensive, passive).
        /// A group order keeps overriding it, and the reply says so.
        /// </summary>
        private static (string Message, bool Echo) ApplyStanceClick(GamePlayer player, int slot)
        {
            GameLiving member = slot >= 0 && slot < SlotLimit ? MembersBySlot(player)[slot] : null;
            if (member == null)
                return (EmptyRowMessage, false);
            if (member is not GameBot bot || !IsOwned(bot, player))
                return ($"{member.Name} is not your companion.", false);
            if (bot.PlayerCompanionRecord is not { } record)
                return ($"{bot.Name} is a temporary helper; the group order sets its stance.", false);
            string next = CompanionBarView.NextSavedStance(record.EngagementPreference);
            bool changed = PlayerCompanionRoster.TrySetTactics(player, record.CompanionId, "stance", next, out string reply);
            if (changed && CompanionEngagementMode.TryGetGroupOrder(player, out eCompanionEngagementMode order))
                reply = $"{ShortMessage(reply)} Group order {OrderLabel(true, order)} overrides it.";
            return (changed ? reply : ShortMessage(reply), !changed);
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

        /// <summary>Shows or hides the window to match the setting, the group and owned companions; sends labels once confirmed.</summary>
        private static void Reconcile(GamePlayer player, Session session)
        {
            if (session.Region != player.CurrentRegionID)
            {
                session.Region = player.CurrentRegionID;
                ResetClientState(session);
            }
            bool want = session.Enabled && WantsWindow(player);
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
            string[] labels = BuildView(player, session.Message, OwnedCompanions(player), session.LeavePending(DateTime.UtcNow)).ToLabels();
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
