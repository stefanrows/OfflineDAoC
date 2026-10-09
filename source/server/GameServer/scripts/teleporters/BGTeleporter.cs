using System;
using DOL.GS;
using DOL.Events;
using DOL.GS.PacketHandler;
using System.Reflection;

namespace DOL.GS.Scripts
{
    public class BGTeleporter : GameNPC
	{
		private static new readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        public override bool AddToWorld()
        {
            Model = 2026;
            Name = "BG TELEPORTER";
            Level = 50;
            Size = 60;
            Flags |= GameNPC.eFlags.PEACE;
            return base.AddToWorld();
        }
		public override bool Interact(GamePlayer player)
		{
			if (!base.Interact(player)) return false;
			TurnTo(player.X, player.Y);
            player.Out.SendMessage("Join the level-appropriate guild battleground: [Battlegrounds]. Use /battleground for status and objectives.",
                eChatType.CT_Say, eChatLoc.CL_PopupWindow);
			return true;
		}
        public override bool WhisperReceive(GameLiving source, string str)
        {
            if (!base.WhisperReceive(source, str) || source is not GamePlayer player) return false;
            if (!BattlegroundCampaignPolicy.TryEnter(player, out string reason))
                SendReply(player, reason);
            return true;
        }
		private void SendReply(GamePlayer target, string msg)
			{
				target.Client.Out.SendMessage(
					msg,
					eChatType.CT_Say,eChatLoc.CL_PopupWindow);
			}
		[ScriptLoadedEvent]
        public static void OnScriptCompiled(DOLEvent e, object sender, EventArgs args)
        {
            log.Info("BG Teleporter initialized: true");
        }	
    }
}