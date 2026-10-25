using DOL.Database;
using DOL.GS.Keeps;

namespace DOL.GS.ServerRules
{
	/// <summary>
	/// Rules of the classic realm-vs-realm world (save marker <c>Realm-1</c>).
	/// The server keeps GameType=PvP in both worlds, so this class derives from
	/// the fork's PvP rules and keeps every fork feature that hangs on them
	/// (keeps, open BG, battleground campaign, ToA hooks, bots). On top it
	/// restores the realm presentation and the realm-only social rules of the
	/// upstream Normal rules.
	///
	/// No <see cref="ServerRulesAttribute"/>: the attribute scan in
	/// <see cref="ScriptMgr.CreateServerRules(EGameServerType)"/> must keep
	/// returning <see cref="PvPServerRules"/> for GST_PvP. This class is chosen
	/// only by the world marker (<see cref="ScriptMgr.CreateServerRules(EGameServerType, WorldModelKind)"/>).
	///
	/// Friend and enemy come from <see cref="PvpCombatant.AreAllied"/>, which
	/// answers "same realm" in this world (R1b), so the inherited attack, heal
	/// and keep checks are realm based. Enemy players and bots show as in the
	/// upstream Normal rules: race name, realm rank title, no guild or title.
	/// </summary>
	public class RvrServerRules : PvPServerRules
	{
		public override string RulesDescription()
		{
			return "realm vs realm rules (Realm-1 world on the PvP rules)";
		}

		/// <summary>
		/// Colour handling 0, the upstream realm scheme: other-realm players red,
		/// own-realm NPCs friendly.
		/// </summary>
		public override byte GetColorHandling(GameClient client)
		{
			return 0;
		}

		/// <summary>
		/// The true realm of every living, as upstream sends it. An ally of the
		/// viewer (a companion of the viewer's realm whatever its class) shows the
		/// viewer's realm. A hostile actor or keep guard that shares the viewer's
		/// realm byte (a Frontier Wardens guard, a neutral battleground encounter
		/// bot) gets another realm so the client lets the viewer attack it.
		/// </summary>
		public override byte GetLivingRealm(GamePlayer player, GameLiving target)
		{
			if (player == null || target == null)
				return 0;

			if (target is GamePlayer playerTarget && playerTarget.Client?.Account?.PrivLevel > (uint)ePrivLevel.Player)
				return (byte)player.Realm;

			if (target is GameKeepGuard guard && guard.Realm == player.Realm && guard.Component?.Keep != null &&
				GameServer.KeepManager.IsEnemy(guard, player))
				return (byte)OtherRealm(player.Realm);

			if (PvpCombatant.IsPlayerShaped(target))
			{
				if (PvpCombatant.AreAllied(WorldModelKind.Realm, player, target))
					return (byte)player.Realm;

				if (target.Realm == player.Realm)
					return (byte)OtherRealm(player.Realm);
			}

			return (byte)target.Realm;
		}

		private static eRealm OtherRealm(eRealm realm) => realm == eRealm.Albion ? eRealm.Midgard : eRealm.Albion;

		public override bool IsAllowedToGroup(GamePlayer source, GamePlayer target, bool quiet)
		{
			if (source == null || target == null)
				return false;

			if (!WorldMode.AllowsRealmPair(WorldModelKind.Realm, source, target))
			{
				if (!quiet) MessageToLiving(source, "You can't group with a player from another realm!");
				return false;
			}

			return true;
		}

		public override bool IsAllowedToJoinGuild(GamePlayer source, Guild guild)
		{
			if (!base.IsAllowedToJoinGuild(source, guild))
				return false;

			if (source == null)
				return false;

			return guild == null || GuildAcceptsRealm(guild.Realm, source.Realm);
		}

		/// <summary>
		/// A realm-less guild (none is written by the RvR reset) stays open as in
		/// the Normal rules; every other guild takes its own realm only.
		/// </summary>
		public static bool GuildAcceptsRealm(eRealm guildRealm, eRealm memberRealm)
		{
			return guildRealm == eRealm.None || guildRealm == memberRealm;
		}

		public override bool IsAllowedToTrade(GameLiving source, GameLiving target, bool quiet)
		{
			if (source == null || target == null)
				return false;

			// Peace flag NPCs can trade with everyone.
			if (target is GameNPC targetNpc && (targetNpc.Flags & GameNPC.eFlags.PEACE) != 0)
				return true;
			if (source is GameNPC sourceNpc && (sourceNpc.Flags & GameNPC.eFlags.PEACE) != 0)
				return true;

			if (!WorldMode.AllowsRealmPair(WorldModelKind.Realm, source, target))
			{
				if (!quiet) MessageToLiving(source, "You can't trade with enemy realm!");
				return false;
			}

			return true;
		}

		/// <summary>
		/// Other realms' speech is garbled, as in the upstream Normal rules.
		/// </summary>
		public override bool IsAllowedToUnderstand(GameLiving source, GamePlayer target)
		{
			if (source == null || target == null)
				return false;

			if (source.CurrentRegionID == 27)
				return true;

			if (source is GamePlayer sourcePlayer && sourcePlayer.Client?.Account?.PrivLevel > (uint)ePrivLevel.Player)
				return true;
			if (target.Client?.Account?.PrivLevel > (uint)ePrivLevel.Player)
				return true;

			if (source is GameNPC npc && (npc.Flags & GameNPC.eFlags.PEACE) != 0)
				return true;

			if (source.Realm == eRealm.None)
				return true;

			// A companion or /spawn helper speaks its owner's tongue.
			return PvpCombatant.AllianceRealm(source) == PvpCombatant.AllianceRealm(target);
		}

		/// <summary>Bind stones of another realm refuse, as in the upstream Normal rules.</summary>
		public override bool IsAllowedToBind(GamePlayer player, DbBindPoint point)
		{
			if (player == null || point == null)
				return false;
			return point.Realm == 0 || player.Realm == (eRealm)point.Realm;
		}

		/// <summary>Only the own realm's recipes (and realm-less ones while the
		/// allow_craft_norealm_items property is on), as in the upstream Normal rules.</summary>
		public override bool IsAllowedToCraft(GamePlayer player, DbItemTemplate item)
		{
			if (player == null || item == null)
				return false;
			return player.Realm == (eRealm)item.Realm || (item.Realm == 0 && ServerProperties.Properties.ALLOW_CRAFT_NOREALM_ITEMS);
		}

		// ---- enemy presentation, as in the upstream Normal rules -------------

		public override string GetPlayerName(GamePlayer source, GamePlayer target)
		{
			if (IsSameRealm(source, target, true))
				return target.Name;

			return source.RaceToTranslatedName(target.Race, target.Gender);
		}

		public override string GetPlayerLastName(GamePlayer source, GamePlayer target)
		{
			if (IsSameRealm(source, target, true))
				return target.LastName;

			return target.RealmRankTitle(source.Client.Account.Language);
		}

		public override string GetPlayerGuildName(GamePlayer source, GamePlayer target)
		{
			if (IsSameRealm(source, target, true))
				return target.GuildName;

			return string.Empty;
		}

		public override string GetPlayerTitle(GamePlayer source, GamePlayer target)
		{
			if (IsSameRealm(source, target, true))
				return target.CurrentTitle.GetValue(source, target);

			return string.Empty;
		}

		/// <summary>
		/// A GameBot reaches the client as an NPC; an enemy one shows like an
		/// enemy player of its realm: its race name instead of its name. Allied
		/// bots and every other NPC keep their name.
		/// </summary>
		public override string GetNPCName(GamePlayer source, GameNPC target)
		{
			if (target is GameBot bot && source != null && PvpCombatant.IsPlayerShaped(bot) &&
				!PvpCombatant.AreAllied(WorldModelKind.Realm, source, bot))
				return source.RaceToTranslatedName(bot.Race, bot.Gender);

			return target.Name;
		}

		/// <summary>An enemy bot shows no guild, like an enemy player.</summary>
		public override string GetNPCGuildName(GamePlayer source, GameNPC target)
		{
			if (target is GameBot bot && source != null && PvpCombatant.IsPlayerShaped(bot) &&
				!PvpCombatant.AreAllied(WorldModelKind.Realm, source, bot))
				return string.Empty;

			return target.GuildName;
		}
	}
}
