using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    /// <summary>
    /// /ml: Master Level, experience toward the next level, discipline and the arts held
    /// (TOA_PLAN WP2.2). Read-only; the Arbiter and Demyphon change Master Level state.
    /// </summary>
    [CmdAttribute(
        "&ml",
        ePrivLevel.Player,
        "Show your Master Level, Master Level experience and discipline",
        "/ml")]
    public class MasterLevelCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;

            if (player == null)
                return;

            foreach (string line in MasterLevelProgression.Describe(player).Split('\n'))
                client.Out.SendMessage(line, eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }
}
