using System;
using System.Collections.Generic;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS.Keeps;
using DOL.GS.PacketHandler;
using DOL.Language;

namespace DOL.GS.ServerRules
{
	/// <summary>
	/// Set of rules for "PvP" server type.
	/// </summary>
	[ServerRules(EGameServerType.GST_PvP)]
	public class PvPServerRules : AbstractServerRules
	{
		public override string RulesDescription()
		{
			return "standard PvP server rules";
		}

		//release city
		//alb=26315, 21177, 8256, dir=0
		//mid=24664, 21402, 8759, dir=0
		//hib=15780, 22727, 7060, dir=0
		//0,"You will now release automatically to your home city in 8 more seconds!"

		//TODO: 2min immunity after release if killed by player

		/// <summary>
		/// TempProperty set if killed by player
		/// </summary>
		protected const string KILLED_BY_PLAYER_PROP = "PvP killed by player";

		public override void ImmunityExpiredCallback(GamePlayer player)
		{
			if (player.ObjectState != GameObject.eObjectState.Active) return;
			if (player.Client.IsPlaying == false) return;

			if (player.Level < m_safetyLevel && player.SafetyFlag)
				player.Out.SendMessage("Your temporary invulnerability timer has expired, but your /safety flag is still on.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
			else
				player.Out.SendMessage("Your temporary invulnerability timer has expired.", eChatType.CT_System, eChatLoc.CL_SystemWindow);

			return;
		}

		/// <summary>
		/// Level at which players safety flag has no effect
		/// </summary>
		protected int m_safetyLevel = 10;

		/// <summary>
		/// Invoked on Player death and deals out
		/// experience/realm points if needed
		/// </summary>
		/// <param name="killedPlayer">player that died</param>
		/// <param name="killer">killer</param>
		public override void OnPlayerKilled(GamePlayer killedPlayer, GameObject killer)
		{
			base.OnPlayerKilled(killedPlayer, killer);
			if (killer == null || killer is GamePlayer)
				killedPlayer.TempProperties.SetProperty(KILLED_BY_PLAYER_PROP, KILLED_BY_PLAYER_PROP);
			else
				killedPlayer.TempProperties.RemoveProperty(KILLED_BY_PLAYER_PROP);
		}

		public override void OnReleased(DOLEvent e, object sender, EventArgs args)
		{
			GamePlayer player = (GamePlayer)sender;
			if (player.TempProperties.GetProperty<string>(KILLED_BY_PLAYER_PROP) != null)
			{
				player.TempProperties.RemoveProperty(KILLED_BY_PLAYER_PROP);
				StartImmunityTimer(player, ServerProperties.Properties.TIMER_KILLED_BY_PLAYER * 1000);//When Killed by a Player
			}
			else
			{
				StartImmunityTimer(player, ServerProperties.Properties.TIMER_KILLED_BY_MOB * 1000);//When Killed by a Mob
			}
		}


		/// <summary>
		/// Should be called whenever a player teleports to a new location
		/// </summary>
		/// <param name="player"></param>
		/// <param name="source"></param>
		/// <param name="destination"></param>
		public override void OnPlayerTeleport(GamePlayer player, GameLocation source, DbTeleport destination)
		{
			// Since region change already starts an immunity timer we only want to do this if a player
			// is teleporting within the same region
			if (source.RegionID == destination.RegionID)
			{
				StartImmunityTimer(player, ServerProperties.Properties.TIMER_PVP_TELEPORT * 1000);
			}
		}


		public override bool IsAllowedToAttack(GameLiving attacker, GameLiving defender, bool quiet)
		{
			if (BotPvpCrowdControl.Protected(attacker, defender))
				return false;

			// Stable-ticket travel is a real, uninterrupted horse route. Neither
			// the rider nor nearby mobs may start combat until the route ends.
			if (attacker is GameBot { IsOnStableMasterRoute: true } ||
				defender is GameBot { IsOnStableMasterRoute: true })
				return false;

			if (!base.IsAllowedToAttack(attacker, defender, quiet))
				return false;

			// PvP decisions use the player-shaped owner. GetLivingOwner is
			// required here because GetPlayerOwner intentionally returns null for
			// pets controlled by a GameBot.
			GameLiving resolvedAttacker = PvpCombatant.Resolve(attacker);
			if (resolvedAttacker != null)
			{
				if (resolvedAttacker != attacker)
					quiet = true;
				attacker = resolvedAttacker;
			}

			GameLiving resolvedDefender = PvpCombatant.Resolve(defender);
			if (resolvedDefender != null)
				defender = resolvedDefender;

			// can't attack self
			if (attacker == defender)
			{
				if (quiet == false) MessageToLiving(attacker, "You can't attack yourself!");
				return false;
			}

			// Anyone outside the group, guild, or battlegroup is hostile. This
			// deliberately ignores realm, including for same-realm GameBots.
			if (PvpCombatant.IsPlayerShaped(attacker) && PvpCombatant.IsPlayerShaped(defender))
			{
				bool duel = attacker is GamePlayer playerAttacker && playerAttacker.IsDuelPartner(defender);
				if (!duel && PvpCombatant.AreAllied(attacker, defender))
				{
					if (!quiet) MessageToLiving(attacker, "You can't attack an allied player.");
					return false;
				}

				if (!duel)
				{
					// Safe regions
					if (PvpCombatant.IsSafeArea(attacker))
					{
						if (quiet == false) MessageToLiving(attacker, "You're currently in a safe zone, you can't attack other players here.");
						return false;
					}


					// Players with safety flag can not attack other players
					if (attacker is GamePlayer player && player.Level < m_safetyLevel && player.SafetyFlag)
					{
						if (quiet == false) MessageToLiving(attacker, "Your PvP safety flag is ON.");
						return false;
					}

					// Players with safety flag can not be attacked in safe regions
					if (defender is GamePlayer playerDefender && playerDefender.Level < m_safetyLevel && playerDefender.SafetyFlag)
					{
						if (!PvpCombatant.IsOldFrontier(playerDefender))
						{
							//"PLAYER has his safety flag on and is in a safe area, you can't attack him here."
							if (quiet == false) MessageToLiving(attacker, playerDefender.Name + " has " + playerDefender.GetPronoun(1, false) + " safety flag on and is in a safe area, you can't attack " + playerDefender.GetPronoun(2, false) + " here.");
							return false;
						}
					}
				}
			}

			if (attacker.Realm == 0 && defender.Realm == 0)
			{
				return FactionMgr.CanLivingAttack(attacker, defender);
			}

			//allow confused mobs to attack same realm
			if (attacker is GameNPC && (attacker as GameNPC).IsConfused && attacker.Realm == defender.Realm)
				return true;

			// "friendly" NPCs can't attack "friendly" players
			if (defender is GameNPC && !PvpCombatant.IsPlayerShaped(defender) && defender.Realm != 0 && attacker.Realm != 0 && defender is GameKeepGuard == false && defender is GameFont == false)
			{
				if (quiet == false) MessageToLiving(attacker, "You can't attack a friendly NPC!");
				return false;
			}
			// "friendly" NPCs can't be attacked by "friendly" players
			if (attacker is GameNPC && !PvpCombatant.IsPlayerShaped(attacker) && attacker.Realm != 0 && defender.Realm != 0 && attacker is GameKeepGuard == false)
			{
				return false;
			}

			#region Keep Guards
			//guard vs guard / npc
			if (attacker is GameKeepGuard)
			{
				if (defender is GameKeepGuard)
					return false;

				if (defender is GameNPC && (defender as GameNPC).Brain is IControlledBrain == false)
					return false;
			}

			//player vs guard
			if (defender is GameKeepGuard && PvpCombatant.IsPlayerShaped(attacker)
				&& GameServer.KeepManager.IsEnemy(defender as GameKeepGuard, attacker) == false)
			{
				if (quiet == false) MessageToLiving(attacker, "You can't attack a friendly NPC!");
				return false;
			}

			//guard vs player
			if (attacker is GameKeepGuard && PvpCombatant.IsPlayerShaped(defender)
				&& GameServer.KeepManager.IsEnemy(attacker as GameKeepGuard, defender) == false)
			{
				return false;
			}
			#endregion

			return true;
		}

		public override bool IsSameRealm(GameLiving source, GameLiving target, bool quiet)
		{
			if (source == null || target == null) 
				return false;

			GameLiving resolvedSource = PvpCombatant.Resolve(source);
			if (resolvedSource != null)
			{
				if (resolvedSource != source)
					quiet = true;
				source = resolvedSource;
			}

			GameLiving resolvedTarget = PvpCombatant.Resolve(target);
			if (resolvedTarget != null)
				target = resolvedTarget;

			if (source == target)
				return true;

			// clients with priv level > 1 are considered friendly by anyone
			if (target is GamePlayer && ((GamePlayer)target).Client.Account.PrivLevel > 1) return true;
			// checking as a gm, targets are considered friendly
			if (source is GamePlayer && ((GamePlayer)source).Client.Account.PrivLevel > 1) return true;

			// Neutral mobs can heal neutral mobs.
			if (source.Realm == 0 && target.Realm == 0) return true;

			// Same-realm player-shaped actors are hostile unless they are allied by
			// group, guild, or battlegroup.
			if (PvpCombatant.IsPlayerShaped(source) && PvpCombatant.IsPlayerShaped(target))
				return PvpCombatant.AreAllied(source, target);

			//keep guards
			if (source is GameKeepGuard && PvpCombatant.IsPlayerShaped(target))
			{
				if (!GameServer.KeepManager.IsEnemy(source as GameKeepGuard, target))
					return true;
			}

			if (target is GameKeepGuard && PvpCombatant.IsPlayerShaped(source))
			{
				if (!GameServer.KeepManager.IsEnemy(target as GameKeepGuard, source))
					return true;
			}

			//doors need special handling
			if (target is GameKeepDoor && PvpCombatant.IsPlayerShaped(source))
				return GameServer.KeepManager.IsEnemy(target as GameKeepDoor, source);

			if (source is GameKeepDoor && PvpCombatant.IsPlayerShaped(target))
				return GameServer.KeepManager.IsEnemy(source as GameKeepDoor, target);

			//components need special handling
			if (target is GameKeepComponent && PvpCombatant.IsPlayerShaped(source))
				return GameServer.KeepManager.IsEnemy(target as GameKeepComponent, source);

			//Peace flag NPCs are same realm
			if (target is GameNPC)
				if ((((GameNPC)target).Flags & GameNPC.eFlags.PEACE) != 0)
					return true;

			if (source is GameNPC)
				if ((((GameNPC)source).Flags & GameNPC.eFlags.PEACE) != 0)
					return true;

			if (source is GamePlayer && target is GameNPC && target.Realm != 0)
				return true;

			if (quiet == false) MessageToLiving(source, target.GetName(0, true) + " is not a member of your realm!");
			return false;
		}

		public override bool IsAllowedCharsInAllRealms(GameClient client)
		{
			return true;
		}

		public override bool IsAllowedToGroup(GamePlayer source, GamePlayer target, bool quiet)
		{
			return true;
		}

		public override bool IsAllowedToJoinGuild(GamePlayer source, Guild guild)
		{
			return true;
		}

		public override bool IsAllowedToTrade(GameLiving source, GameLiving target, bool quiet)
		{
			return true;
		}

		public override bool IsAllowedToUnderstand(GameLiving source, GamePlayer target)
		{
			return true;
		}

		/// <summary>
		/// Gets the server type color handling scheme
		/// 
		/// ColorHandling: this byte tells the client how to handle color for PC and NPC names (over the head) 
		/// 0: standard way, other realm PC appear red, our realm NPC appear light green 
		/// 1: standard PvP way, all PC appear red, all NPC appear with their level color 
		/// 2: Same realm livings are friendly, other realm livings are enemy; nearest friend/enemy buttons work
		/// 3: standard PvE way, all PC friendly, realm 0 NPC enemy rest NPC appear light green 
		/// 4: All NPC are enemy, all players are friendly; nearest friend button selects self, nearest enemy don't work at all
		/// </summary>
		/// <param name="client">The client asking for color handling</param>
		/// <returns>The color handling</returns>
		public override byte GetColorHandling(GameClient client)
		{
			return 1;
		}

		/// <summary>
		/// The client receives GameBots as NPCs. Give allied player-shaped
		/// actors the viewer's realm and spoof a different realm for same-realm
		/// hostiles so the client can present and target them as enemies.
		/// </summary>
		public override byte GetLivingRealm(GamePlayer player, GameLiving target)
		{
			if (player == null || target == null)
				return 0;

			if (target is GamePlayer playerTarget && playerTarget.Client.Account.PrivLevel > 1)
				return (byte)player.Realm;

			if (PvpCombatant.IsPlayerShaped(target))
			{
				if (PvpCombatant.AreAllied(player, target))
					return (byte)player.Realm;

				if (target.Realm == player.Realm)
					return (byte)(player.Realm == eRealm.Albion ? eRealm.Midgard : eRealm.Albion);
			}

			return (byte)target.Realm;
		}

		/// <summary>
		/// Formats player statistics.
		/// </summary>
		/// <param name="player">The player to read statistics from.</param>
		/// <returns>List of strings.</returns>
		public override IList<string> FormatPlayerStatistics(GamePlayer player)
		{
			var stat = new List<string>();

			int total = 0;
			#region Players Killed
			//only show if there is a kill [by Suncheck]
			if ((player.KillsAlbionPlayers + player.KillsMidgardPlayers + player.KillsHiberniaPlayers) > 0)
			{
				stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.Kill.Title"));
				if (player.KillsAlbionPlayers > 0) stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.Kill.AlbionPlayer") + ": " + player.KillsAlbionPlayers.ToString("N0"));
				if (player.KillsMidgardPlayers > 0) stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.Kill.MidgardPlayer") + ": " + player.KillsMidgardPlayers.ToString("N0"));
				if (player.KillsHiberniaPlayers > 0) stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.Kill.HiberniaPlayer") + ": " + player.KillsHiberniaPlayers.ToString("N0"));
				total = player.KillsMidgardPlayers + player.KillsAlbionPlayers + player.KillsHiberniaPlayers;
				stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.Kill.TotalPlayers") + ": " + total.ToString("N0"));
			}
			#endregion
			stat.Add(" ");
			#region Players Deathblows
			//only show if there is a kill [by Suncheck]
			if ((player.KillsAlbionDeathBlows + player.KillsMidgardDeathBlows + player.KillsHiberniaDeathBlows) > 0)
			{
				total = 0;
				if (player.KillsAlbionDeathBlows > 0) stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.Deathblows.AlbionPlayer") + ": " + player.KillsAlbionDeathBlows.ToString("N0"));
				if (player.KillsMidgardDeathBlows > 0) stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.Deathblows.MidgardPlayer") + ": " + player.KillsMidgardDeathBlows.ToString("N0"));
				if (player.KillsHiberniaDeathBlows > 0) stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.Deathblows.HiberniaPlayer") + ": " + player.KillsHiberniaDeathBlows.ToString("N0"));
				total = player.KillsMidgardDeathBlows + player.KillsAlbionDeathBlows + player.KillsHiberniaDeathBlows;
				stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.Deathblows.TotalPlayers") + ": " + total.ToString("N0"));
			}
			#endregion
			stat.Add(" ");
			#region Players Solo Kills
			//only show if there is a kill [by Suncheck]
			if ((player.KillsAlbionSolo + player.KillsMidgardSolo + player.KillsHiberniaSolo) > 0)
			{
				total = 0;
				if (player.KillsAlbionSolo > 0) stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.Solo.AlbionPlayer") + ": " + player.KillsAlbionSolo.ToString("N0"));
				if (player.KillsMidgardSolo > 0) stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.Solo.MidgardPlayer") + ": " + player.KillsMidgardSolo.ToString("N0"));
				if (player.KillsHiberniaSolo > 0) stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.Solo.HiberniaPlayer") + ": " + player.KillsHiberniaSolo.ToString("N0"));
				total = player.KillsMidgardSolo + player.KillsAlbionSolo + player.KillsHiberniaSolo;
				stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.Solo.TotalPlayers") + ": " + total.ToString("N0"));
			}
			#endregion
			stat.Add(" ");
			#region Keeps
			//only show if there is a capture [by Suncheck]
			if ((player.CapturedKeeps + player.CapturedTowers) > 0)
			{
				stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.Capture.Title"));
				//stat.Add("Relics Taken: " + player.RelicsTaken.ToString("N0"));
				//stat.Add("Albion Keeps Captured: " + player.CapturedAlbionKeeps.ToString("N0"));
				//stat.Add("Midgard Keeps Captured: " + player.CapturedMidgardKeeps.ToString("N0"));
				//stat.Add("Hibernia Keeps Captured: " + player.CapturedHiberniaKeeps.ToString("N0"));
				if (player.CapturedKeeps > 0) stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.Capture.Keeps") + ": " + player.CapturedKeeps.ToString("N0"));
				//stat.Add("Keep Lords Slain: " + player.KeepLordsSlain.ToString("N0"));
				//stat.Add("Albion Towers Captured: " + player.CapturedAlbionTowers.ToString("N0"));
				//stat.Add("Midgard Towers Captured: " + player.CapturedMidgardTowers.ToString("N0"));
				//stat.Add("Hibernia Towers Captured: " + player.CapturedHiberniaTowers.ToString("N0"));
				if (player.CapturedTowers > 0) stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.Capture.Towers") + ": " + player.CapturedTowers.ToString("N0"));
				//stat.Add("Tower Captains Slain: " + player.TowerCaptainsSlain.ToString("N0"));
				//stat.Add("Realm Guard Kills Albion: " + player.RealmGuardTotalKills.ToString("N0"));
				//stat.Add("Realm Guard Kills Midgard: " + player.RealmGuardTotalKills.ToString("N0"));
				//stat.Add("Realm Guard Kills Hibernia: " + player.RealmGuardTotalKills.ToString("N0"));
				//stat.Add("Total Realm Guard Kills: " + player.RealmGuardTotalKills.ToString("N0"));
			}
			#endregion
			stat.Add(" ");
			#region PvE
			//only show if there is a kill [by Suncheck]
			if ((player.KillsDragon + player.KillsEpicBoss + player.KillsLegion) > 0)
			{
				stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.PvE.Title"));
				if (player.KillsDragon > 0) stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.PvE.KillsDragon") + ": " + player.KillsDragon.ToString("N0"));
				if (player.KillsEpicBoss > 0) stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.PvE.KillsEpic") + ": " + player.KillsEpicBoss.ToString("N0"));
				if (player.KillsLegion > 0) stat.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "PlayerStatistic.PvE.KillsLegion") + ": " + player.KillsLegion.ToString("N0"));
			}
			#endregion

			return stat;
		}

		/// <summary>
		/// Reset the keep with special server rules handling
		/// </summary>
		/// <param name="lord">The lord that was killed</param>
		/// <param name="killer">The lord's killer</param>
		public override void ResetKeep(GuardLord lord, GameObject killer)
		{
			base.ResetKeep(lord, killer);
			eRealm realm = eRealm.None;

			// The realm shown by the legacy client is only cosmetic in Camlann.
			// Resolve GameBots and bot-owned pets to their player-shaped owner first.
			GameLiving combatant = PvpCombatant.Resolve(killer as GameLiving);
			if (combatant != null)
				realm = (eRealm)(combatant.Group?.LivingLeader?.Realm ?? combatant.Realm);

			lord.Component.Keep.Reset(realm);
		}
	}
}
