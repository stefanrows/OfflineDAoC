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
	/// Friend and enemy (<see cref="PvpCombatant.AreAllied"/>) are still the
	/// Camlann answer here; the same-realm alliance core is package R1b.
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
		/// The true realm of every living, as upstream sends it. Only a hostile
		/// keep guard that shares the viewer's realm still gets another realm,
		/// so the client lets the viewer attack it while keep hostility is
		/// guild based (realm keeps follow in R3a).
		/// </summary>
		public override byte GetLivingRealm(GamePlayer player, GameLiving target)
		{
			if (player == null || target == null)
				return 0;

			if (target is GamePlayer playerTarget && playerTarget.Client?.Account?.PrivLevel > (uint)ePrivLevel.Player)
				return (byte)player.Realm;

			if (target is GameKeepGuard guard && guard.Realm == player.Realm && guard.Component?.Keep != null &&
				GameServer.KeepManager.IsEnemy(guard, player))
				return (byte)(player.Realm == eRealm.Albion ? eRealm.Midgard : eRealm.Albion);

			return (byte)target.Realm;
		}

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

			return source.Realm == eRealm.None || source.Realm == target.Realm;
		}
	}
}
