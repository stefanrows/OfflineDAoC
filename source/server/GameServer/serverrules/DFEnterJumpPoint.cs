using System;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;
using JNogueira.Discord.WebhookClient;

namespace DOL.GS.ServerRules
{
	/// <summary>
	/// Handles DF entrance jump point allowing only one realm to enter on Normal server type.
	/// </summary>
	public class DFEnterJumpPoint : IJumpPointHandler
	{
		/// <summary>
		/// Shared entrance authority for client players and server-controlled
		/// characters. Characters already inside Darkness Falls are not evicted
		/// when control changes; this method only answers whether a new entry is
		/// legal at this instant.
		/// </summary>
		public static bool CanRealmEnter(eRealm realm) =>
			CanRealmEnter(WorldMode.RulesBranch(GameServer.Instance.Configuration.ServerType),
				ServerProperties.Properties.ALLOW_ALL_REALMS_DF, realm, DarknessFallOwner, PreviousOwner,
				LastRealmSwapTick + GracePeriod >= GameLoop.GameLoopTime);

		/// <summary>
		/// The entrance rule for a server-type branch: open to every realm except
		/// on the realm (Normal) branch, which the RvR world takes (R3a). There
		/// only the owner enters, and the previous owner during its grace period.
		/// </summary>
		public static bool CanRealmEnter(EGameServerType rulesBranch, bool allowAllRealms, eRealm realm,
			eRealm owner, eRealm previousOwner, bool inGracePeriod)
		{
			if (rulesBranch != EGameServerType.GST_Normal)
				return true;
			if (allowAllRealms)
				return true;
			if (realm == previousOwner && inGracePeriod)
				return true;
			return realm != eRealm.None && realm == owner;
		}

		/// <summary>
		/// RvR world (R3a): the realm holding the most frontier keeps owns
		/// Darkness Falls (towers, portal and relic keeps do not count, as
		/// <see cref="Keeps.IKeepManager.GetKeepCountByRealm"/>). The owner keeps it
		/// until another realm holds strictly more; of two realms tied above the
		/// owner the one that just took a keep wins. The Frontier Wardens' neutral
		/// keeps belong to no realm, so a world that starts with every keep
		/// neutral has no owner until the first realm captures a keep.
		/// </summary>
		public static eRealm NextOwner(eRealm owner, eRealm taker, int albion, int midgard, int hibernia)
		{
			int Count(eRealm realm) => realm switch
			{
				eRealm.Albion => albion,
				eRealm.Midgard => midgard,
				eRealm.Hibernia => hibernia,
				_ => 0
			};

			eRealm best = Count(owner) > 0 ? owner : eRealm.None;
			int bestCount = Count(best);
			foreach (eRealm candidate in new[] { taker, eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
			{
				if (candidate is not (eRealm.Albion or eRealm.Midgard or eRealm.Hibernia))
					continue;
				if (Count(candidate) > bestCount)
				{
					best = candidate;
					bestCount = Count(candidate);
				}
			}
			return best;
		}

		/// <summary>
		/// Decides whether player can jump to the target point.
		/// All messages with reasons must be sent here.
		/// Can change destination too.
		/// </summary>
		/// <param name="targetPoint">The jump destination</param>
		/// <param name="player">The jumping player</param>
		/// <returns>True if allowed</returns>
		public bool IsAllowedToJump(DbZonePoint targetPoint, GamePlayer player)
		{
            if (player.Client.Account.PrivLevel > 1)
            {
                return true;
            }
			return CanRealmEnter(player.Realm);
		}

		public static eRealm DarknessFallOwner = eRealm.None;
		public static eRealm PreviousOwner = eRealm.None;

		public static long GracePeriod = 900 * 1000; //15 mins
		public static long LastRealmSwapTick = 0;
		
		/// <summary>
		/// initialize the darkness fall entrance system
		/// </summary>
		/// <param name="e"></param>
		/// <param name="sender"></param>
		/// <param name="args"></param>
		[ScriptLoadedEvent]
		public static void OnScriptLoaded(DOLEvent e, object sender, EventArgs args)
		{
			CheckDFOwner();
			GameEventMgr.AddHandler(KeepEvent.KeepTaken,new DOLEventHandler(OnKeepTaken));
		}

		/// <summary>
		/// check if a realm have more keep at start
		/// to know the DF owner
		/// </summary>
		
		private static void CheckDFOwner()
		{
			int albcount = GameServer.KeepManager.GetKeepCountByRealm(eRealm.Albion);
			int midcount = GameServer.KeepManager.GetKeepCountByRealm(eRealm.Midgard);
			int hibcount = GameServer.KeepManager.GetKeepCountByRealm(eRealm.Hibernia);

			// RvR world (R3a): the same rule as every capture, so a restart keeps the owner the counts give.
			if (WorldMode.IsRvr)
			{
				DarknessFallOwner = NextOwner(eRealm.None, eRealm.None, albcount, midcount, hibcount);
				return;
			}
			
			if (albcount > midcount && albcount > hibcount)
			{
				DarknessFallOwner = eRealm.Albion;
			}
			if (midcount > albcount && midcount > hibcount)
			{
				DarknessFallOwner = eRealm.Midgard;
			}
			if (hibcount > midcount && hibcount > albcount)
			{
				DarknessFallOwner = eRealm.Hibernia;
			}
		}



		/// <summary>
		/// when  keep is taken it check if the realm which take gain the control of DF
		/// </summary>
		/// <param name="e"></param>
		/// <param name="sender"></param>
		/// <param name="arguments"></param>
		public static void OnKeepTaken(DOLEvent e, object sender, EventArgs arguments)
		{
			KeepEventArgs args = arguments as KeepEventArgs;
			eRealm realm = (eRealm) args.Keep.Realm ;
			if (WorldMode.IsRvr)
			{
				OnRvrKeepTaken(realm);
				return;
			}
			if (realm != DarknessFallOwner )
			{
				// TODO send message to chat and discord web hook
				string oldDFOwner = GlobalConstants.RealmToName(DarknessFallOwner);
				int currentDFOwnerTowerCount = GameServer.KeepManager.GetKeepCountByRealm(DarknessFallOwner);
				int challengerOwnerTowerCount = GameServer.KeepManager.GetKeepCountByRealm(realm);
				if (currentDFOwnerTowerCount < challengerOwnerTowerCount)
                {
					PreviousOwner = DarknessFallOwner;
					LastRealmSwapTick = GameLoop.GameLoopTime;
					DarknessFallOwner = realm;
				}
					
				string realmName = string.Empty;

				string messageDFGetControl = string.Format("The forces of {0} have gained access to Darkness Falls!", GlobalConstants.RealmToName(realm));
				string messageDFLostControl = string.Format("{0} will lose access to Darkness Falls in 15 minutes!", oldDFOwner);

				if (oldDFOwner != GlobalConstants.RealmToName(DarknessFallOwner))
				{ 
					BroadcastMessage(messageDFLostControl, eRealm.None);
					BroadcastMessage(messageDFGetControl, eRealm.None);
				}
			}
		}
		
		private static void OnRvrKeepTaken(eRealm taker)
		{
			eRealm next = NextOwner(DarknessFallOwner, taker,
				GameServer.KeepManager.GetKeepCountByRealm(eRealm.Albion),
				GameServer.KeepManager.GetKeepCountByRealm(eRealm.Midgard),
				GameServer.KeepManager.GetKeepCountByRealm(eRealm.Hibernia));
			if (next == DarknessFallOwner)
				return;

			eRealm old = DarknessFallOwner;
			PreviousOwner = old;
			LastRealmSwapTick = GameLoop.GameLoopTime;
			DarknessFallOwner = next;
			if (old != eRealm.None)
				BroadcastMessage(string.Format("{0} will lose access to Darkness Falls in 15 minutes!", GlobalConstants.RealmToName(old)), eRealm.None);
			if (next != eRealm.None)
				BroadcastMessage(string.Format("The forces of {0} have gained access to Darkness Falls!", GlobalConstants.RealmToName(next)), eRealm.None);
		}

		/// <summary>
		/// Method to broadcast messages, if eRealm.None all can see,
		/// else only the right realm can see
		/// </summary>
		/// <param name="message">The message</param>
		/// <param name="realm">The realm</param>
		public static void BroadcastMessage(string message, eRealm realm)
		{
			foreach (GamePlayer player in ClientService.Instance.GetPlayersOfRealm(realm))
				player.Out.SendMessage(message, eChatType.CT_Important, eChatLoc.CL_SystemWindow);

			if (DiscordClientManager.TryGetClient(WebhookType.RvR, out var discordClient))
			{
				// Create your DiscordMessage with all parameters of your message.
				var discordMessage = new DiscordMessage(
					"",
					username: "RvR",
					avatarUrl: "",
					tts: false,
					embeds: new[]
					{
						new DiscordMessageEmbed(
							author: new DiscordMessageEmbedAuthor("Darkness Falls"),
							color: 0,
							description: message
						)
					}
				);

				discordClient.SendToDiscordAsync(discordMessage);
			}
		}

		public static void SetDFOwner(GamePlayer p, eRealm NewDFOwner)
		{
			if (DarknessFallOwner != NewDFOwner)
			{
				foreach (GamePlayer otherPlayer in ClientService.Instance.GetPlayersOfRegion(WorldMgr.GetRegion(249)))
				{
					if (otherPlayer.Realm == DarknessFallOwner)
						otherPlayer.Out.SendSoundEffect(217, 0, 0, 0, 0, 0);
					else if (otherPlayer.Realm == NewDFOwner)
						otherPlayer.Out.SendSoundEffect(216, 0, 0, 0, 0, 0);
				}

				DarknessFallOwner = NewDFOwner;
				p.Out.SendMessage(string.Format("New DF Owner set to {0}", GlobalConstants.RealmToName(NewDFOwner)), eChatType.CT_System, eChatLoc.CL_SystemWindow);
			}
			else
				p.Out.SendMessage(string.Format("DF Owner is already set to {0}", GlobalConstants.RealmToName(NewDFOwner)), eChatType.CT_System, eChatLoc.CL_SystemWindow);
		}
	}
}
