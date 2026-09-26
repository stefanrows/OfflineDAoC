using System;
using System.Collections;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.Language;

namespace DOL.GS
{
	public class GameHastener : GameNPC
	{
		public GameHastener() : base() { }
		public GameHastener(INpcTemplate template) : base(template) { }

		public const int SPEEDOFTHEREALMID = 2430;

		public static void CastSpeedOfTheRealm(GameNPC sourceNpc, GamePlayer player)
		{
			if (!GameServer.ServerRules.IsSameRealm(sourceNpc, player, true))
			{
				SendSpeedBlockMessage(player, "GameHastener.SpeedBlockedRealm");
				return;
			}

			if (player.InCombat)
			{
				SendSpeedBlockMessage(player, "GameHastener.SpeedBlockedCombat");
				return;
			}

			Spell spell = SkillBase.GetSpellByID(SPEEDOFTHEREALMID);
			if (spell != null)
				GameNPCHelper.CastSpellOnOwnerAndPets(sourceNpc, player, spell,
					SkillBase.GetSpellLine(GlobalSpellsLines.Realm_Spells), false);
		}

		internal static void SendSpeedBlockMessage(GamePlayer player, string translationKey)
		{
			player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, translationKey),
				eChatType.CT_SpellResisted, eChatLoc.CL_SystemWindow);
		}

		public override bool Interact(GamePlayer player)
		{
			if (player.Client.Account.PrivLevel == 1 && !IsWithinRadius(player, WorldMgr.INTERACT_DISTANCE))
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "GameObject.Interact.TooFarAway", GetName(0, true)), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				Notify(GameObjectEvent.InteractFailed, this, new InteractEventArgs(player));
				return false;
			}

			if (!base.Interact(player))
				return false;

			// Just give out speed without asking.
			CastSpeedOfTheRealm(this, player);

			if (player.CurrentRegion.IsCapitalCity)
				SayTo(player, string.Format("{0} {1}. {2}",
					LanguageMgr.GetTranslation(player.Client.Account.Language, "GameHastener.Greeting"),
					player.CharacterClass.Name,
					LanguageMgr.GetTranslation(player.Client.Account.Language, "GameHastener.CityMovementOffer")));
					// LanguageMgr.GetTranslation(player.Client.Account.Language, "GameHastener.StrengthOffer")));
			else if (IsShroudedIslesStartZone(player.CurrentZone.ID))
				SayTo(player, string.Format("{0} {1}. {2}",
					LanguageMgr.GetTranslation(player.Client.Account.Language, "GameHastener.Greeting"),
					player.CharacterClass.Name,
					LanguageMgr.GetTranslation(player.Client.Account.Language, "GameHastener.CityMovementOffer")));
			else if(!player.CurrentRegion.IsRvR)//default message outside of RvR
				SayTo(player, string.Format("{0} {1}. {2}",
					LanguageMgr.GetTranslation(player.Client.Account.Language, "GameHastener.Greeting"),
					player.CharacterClass.Name,
					LanguageMgr.GetTranslation(player.Client.Account.Language, "GameHastener.DefaultMovementOffer")));
			return true;
		}

		public override bool WhisperReceive(GameLiving source, string str)
		{
			if (base.WhisperReceive(source, str))
			{
				GamePlayer player = source as GamePlayer;
				if (player == null)
					return false;

				if (GameServer.ServerRules.IsSameRealm(this, player, true))
				{
					switch (str.ToLower())
					{
						case "movement":
							CastSpeedOfTheRealm(this, player);
							break;
						// disabled until we figure out how to disable it on port outside of capital cities
						// case "strength":
						// 	if (player.CurrentRegion.IsCapitalCity)
						// 	{
						// 		TargetObject = player;
						// 		CastSpell(SkillBase.GetSpellByID(STROFTHEREALMID), SkillBase.GetSpellLine(GlobalSpellsLines.Mob_Spells),false);
						// 	}
						// 	break;
					}
				}
				else if (str.Equals("movement", StringComparison.OrdinalIgnoreCase))
					SendSpeedBlockMessage(player, "GameHastener.SpeedBlockedRealm");

				return true;
			}

			return false;
		}

		public override IList GetExamineMessages(GamePlayer player)
		{
			IList list = new ArrayList();
			list.Add(string.Format("You examine {0}. {1} is {2}.", GetName(0, false), GetPronoun(0, true), GetAggroLevelString(player, false)));
			return list;
		}

		private bool IsShroudedIslesStartZone(int zoneID)
		{
			switch (zoneID)
			{
				case 51: //Isle of Glass
				case 151: //Aegir's Landing
				case 181: //Domnann
					return true;
			}
			return false;
		}
	}
}
