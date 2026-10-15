using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DOL.GS.PacketHandler;
using static DOL.GS.CompanionManagerProtocol;

namespace DOL.GS.Commands
{
    /// <summary>The server state the Custom8 transport needs from a window session.</summary>
    public interface ICompanionWindowState
    {
        /// <summary>The label texts the client was last sent; a render sends only the labels that differ.</summary>
        string[] SentLabels { get; }
        /// <summary>The patched client answered (ControlReady or any click).</summary>
        bool ClientConfirmed { get; set; }
        bool GuidancePending { get; set; }
    }

    /// <summary>Which manager drives a player's Custom8 window.</summary>
    public enum CompanionWindowKind { None, Companions, OpenBg }

    /// <summary>
    /// One Custom8 window per client: the Companion Manager and the open BG manager share it. Opening one claims the
    /// window and closes the other's session; a click (<c>/companions ui</c>) goes to the manager that owns it.
    /// Keys are players (any object in tests); the registry holds them weakly.
    /// </summary>
    public static class CompanionWindowOwner
    {
        private sealed class Holder
        {
            public CompanionWindowKind Kind;
        }

        private static readonly ConditionalWeakTable<object, Holder> Owners = new();

        /// <summary>The manager that owns the window, or None.</summary>
        public static CompanionWindowKind Current(object player) =>
            player != null && Owners.TryGetValue(player, out Holder holder) ? holder.Kind : CompanionWindowKind.None;

        /// <summary>
        /// Gives the window to <paramref name="kind"/>. When another manager owned it, <paramref name="closePrevious"/> is
        /// called with that manager so it can drop its session. Returns the previous owner.
        /// </summary>
        public static CompanionWindowKind Claim(object player, CompanionWindowKind kind, Action<CompanionWindowKind> closePrevious = null)
        {
            if (player == null)
                return CompanionWindowKind.None;
            Holder holder = Owners.GetValue(player, _ => new Holder());
            CompanionWindowKind previous;
            lock (holder)
            {
                previous = holder.Kind;
                holder.Kind = kind;
            }
            if (previous != CompanionWindowKind.None && previous != kind)
                closePrevious?.Invoke(previous);
            return previous;
        }

        /// <summary>Frees the window when <paramref name="kind"/> still owns it; another owner is left alone.</summary>
        public static void Release(object player, CompanionWindowKind kind)
        {
            if (player == null || !Owners.TryGetValue(player, out Holder holder))
                return;
            lock (holder)
                if (holder.Kind == kind)
                    holder.Kind = CompanionWindowKind.None;
        }

        /// <summary>Whether a <c>/companions ui</c> click belongs to the open BG manager; anything else keeps the Companion Manager's handling.</summary>
        public static bool RoutesToOpenBg(object player) => Current(player) == CompanionWindowKind.OpenBg;
    }

    /// <summary>
    /// The Custom8 packet transport shared by the Companion Manager and the open BG manager: fixed 128-byte DebugMode
    /// bodies (<see cref="CompanionManagerProtocol.BuildBody"/>), label diffing against what the client was last sent,
    /// the view token, show and hide, and the one-line guidance when an unpatched client never answers.
    /// </summary>
    public static class CompanionWindowTransport
    {
        public const int GuidanceDelayMilliseconds = 3000;

        /// <summary>The label indexes whose text differs from what the client was last sent.</summary>
        public static IReadOnlyList<int> ChangedLabels(string[] sent, string[] labels)
        {
            var changed = new List<int>();
            for (int index = 0; index < labels.Length; index++)
                if (!string.Equals(sent[index], labels[index], StringComparison.Ordinal))
                    changed.Add(index);
            return changed;
        }

        /// <summary>
        /// Sends the labels that changed (and records them as sent), then the view token when a click could now resolve
        /// differently, then shows the window when asked.
        /// </summary>
        public static void Publish(GamePlayer player, string[] sent, string[] labels, bool sendToken, ushort revision, bool show)
        {
            foreach (int index in ChangedLabels(sent, labels))
            {
                Send(player, OpLabel, index, labels[index]);
                sent[index] = labels[index];
            }
            if (sendToken)
                Send(player, OpToken, 0, FormatToken(revision));
            if (show)
                Send(player, OpShow);
        }

        public static void Hide(GamePlayer player) => Send(player, OpHide);

        /// <summary>
        /// After <see cref="GuidanceDelayMilliseconds"/>, calls <paramref name="guidance"/> once when the client has not
        /// answered and <paramref name="stillOpen"/> holds: an unpatched client gets one line of command guidance.
        /// </summary>
        public static void ScheduleGuidance(GamePlayer player, ICompanionWindowState state, Func<bool> stillOpen, Action guidance)
        {
            if (state.ClientConfirmed || state.GuidancePending || player.Client?.ClientState != GameClient.eClientState.Playing)
                return;
            state.GuidancePending = true;
            _ = new ECSGameTimer(player, _ =>
            {
                state.GuidancePending = false;
                if (!state.ClientConfirmed && stillOpen() && player.Client?.ClientState == GameClient.eClientState.Playing)
                    guidance();
                return 0;
            }, GuidanceDelayMilliseconds);
        }

        public static void Tell(GamePlayer player, string message) =>
            player.Out?.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);

        public static void Send(GamePlayer player, byte operation, int index = 0, string text = "")
        {
            if (player.Client?.ClientState != GameClient.eClientState.Playing)
                return;
            // Fixed 128-byte body; the native parser validates marker, version,
            // operation, label index, and the terminator before use.
            byte[] body = BuildBody(operation, index, text);
            using var packet = PooledObjectFactory.GetForTick<GSTCPPacketOut>().Init((byte)eServerPackets.DebugMode);
            packet.Write(body, 0, body.Length);
            player.Out.SendTCP(packet);
        }
    }
}
