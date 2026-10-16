using System;
using System.Collections.Generic;
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

    /// <summary>
    /// The manager packet transport of the Companion Manager (Custom8) and the OPEN BG window (Custom5): fixed 128-byte DebugMode
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
        /// The packets of one publish, in order: the labels that changed (recorded as sent; wire index
        /// <paramref name="labelBase"/> + array index), then the view token, then show. Pure apart from updating
        /// <paramref name="sent"/>. The defaults are the Custom8 window's (label base 0, token op 4, show op 2).
        /// </summary>
        public static IReadOnlyList<(byte Operation, int Index, string Text)> Plan(string[] sent, string[] labels, bool sendToken, ushort revision,
            bool show, int labelBase = 0, byte tokenOp = OpToken, byte showOp = OpShow)
        {
            var steps = new List<(byte Operation, int Index, string Text)>();
            foreach (int index in ChangedLabels(sent, labels))
            {
                steps.Add((OpLabel, labelBase + index, labels[index]));
                sent[index] = labels[index];
            }
            if (sendToken)
                steps.Add((tokenOp, 0, FormatToken(revision)));
            if (show)
                steps.Add((showOp, 0, string.Empty));
            return steps;
        }

        /// <summary>
        /// Sends the labels that changed (and records them as sent), then the view token when a click could now resolve
        /// differently, then shows the window when asked. Existing Custom8 callers keep the defaults; the OPEN BG window
        /// (Custom5) passes its label base 620 and its operations 12 (token) and 10 (show).
        /// </summary>
        public static void Publish(GamePlayer player, string[] sent, string[] labels, bool sendToken, ushort revision, bool show,
            int labelBase = 0, byte tokenOp = OpToken, byte showOp = OpShow)
        {
            foreach ((byte operation, int index, string text) in Plan(sent, labels, sendToken, revision, show, labelBase, tokenOp, showOp))
                Send(player, operation, index, text);
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
