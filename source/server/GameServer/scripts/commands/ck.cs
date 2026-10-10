using DOL.GS.PacketHandler;
using System.Collections;
using System.Collections.Generic;
using DOL.Language;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;

namespace DOL.GS.Commands
{
	[CmdAttribute(
	   "&ck",
	   ePrivLevel.Player,
		 "Displays who owns the CK while in a battleground.", "/ck")]
	public class CkCommandHandler : AbstractCommandHandler, ICommandHandler
	{
		public void OnCommand(GameClient client, string[] args)
		{
			if (IsSpammingCommand(client.Player, "ck"))
				return;

			string bgName = client.Player.CurrentZone.Description;
			
			ushort region = client.Player.CurrentRegionID;
			// Campaign regions are named by the catalog; their caps are also registered at startup.
			bool campaign = BattlegroundCampaignCatalog.Find(region) != null;
			if (campaign || GameServer.KeepManager.GetBattleground(region) != null)
			{
				ICollection<AbstractGameKeep> keepList =
					GameServer.KeepManager.GetKeepsOfRegion(region);
				foreach (AbstractGameKeep keep in keepList)
				{
					ChatUtil.SendSystemMessage(client, KeepStringBuilder(keep, campaign));
				}
			}
			else
			{
				client.Out.SendMessage("You need to be in a battleground to use this command.", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
			}
		}
		private string KeepStringBuilder(AbstractGameKeep keep, bool campaign)
		{
			return BattlegroundKeepOwnership.Describe(keep.Name, keep.Realm, keep.Guild?.Name,
				keep.DBKeep?.LordDefeated == true, campaign) + "\n";
		}
		
	}
}
