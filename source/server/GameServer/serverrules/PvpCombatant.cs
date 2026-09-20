using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS.Keeps;

namespace DOL.GS.ServerRules
{
	/// <summary>
	/// Resolves the player-shaped identity behind a human, GameBot, or controlled
	/// pet. Camlann uses this identity for hostility and presentation decisions;
	/// a bot must not fall back to the GameNPC realm rules merely because it is
	/// represented by an NPC on the wire.
	/// </summary>
	public static class PvpCombatant
	{
		private const string DummyGuildName = "DummyGuildToMakePetsUntargetable";

		private static readonly HashSet<ushort> SafeRegions = new()
		{
			10, 101, 201, // capitals
			2, 102, 202,  // housing
			21, 129, 221  // no-PvP newbie dungeons
		};

		/// <summary>
		/// Returns a GamePlayer or GameBot identity, walking controlled-pet
		/// ownership through GetLivingOwner so bot-owned pets are not lost.
		/// </summary>
		public static GameLiving Resolve(GameLiving living)
		{
			for (int depth = 0; living != null && depth < 32; depth++)
			{
				if (living is GamePlayer or GameBot)
					return living;

				if (living is not GameNPC npc || npc.Brain is not IControlledBrain controlled ||
					controlled.GetLivingOwner() is not GameLiving owner || owner == living)
					return null;

				living = owner;
			}

			return null;
		}

		public static bool IsPlayerShaped(GameLiving living) => Resolve(living) != null;

		/// <summary>
		/// Camlann allies are grouped, guilded, or in the same battlegroup. The
		/// dummy guild used by the legacy client pet hack is never an alliance.
		/// Temporary companions also remain loyal to their owner and the owner's
		/// current group while they exist.
		/// </summary>
		public static bool AreAllied(GameLiving first, GameLiving second)
		{
			GameLiving a = Resolve(first);
			GameLiving b = Resolve(second);
			if (a == null || b == null)
				return false;

			if (a == b)
				return true;

			if (a.Group != null && a.Group == b.Group && a.Group.IsInTheGroup(a) && a.Group.IsInTheGroup(b))
				return true;

			Guild firstGuild = GuildOf(a);
			Guild secondGuild = GuildOf(b);
			if (IsRealGuild(firstGuild) && firstGuild == secondGuild)
				return true;

			BattleGroup firstBattleGroup = a.TempProperties?.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY);
			BattleGroup secondBattleGroup = b.TempProperties?.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY);
			if (firstBattleGroup != null && firstBattleGroup == secondBattleGroup)
				return true;

			return CompanionProtects(a, b) || CompanionProtects(b, a);
		}

		public static bool IsInvulnerableToAttack(GameLiving living)
		{
			return Resolve(living) switch
			{
				GamePlayer player => player.IsInvulnerableToAttack,
				GameBot bot => bot.IsInvulnerableToAttack,
				_ => false
			};
		}

		public static bool IsEnteringWorld(GameLiving living)
		{
			return Resolve(living) is GamePlayer player &&
				player.Client.ClientState == GameClient.eClientState.WorldEnter;
		}

		public static bool IsSafeArea(GameLiving living)
		{
			if (living == null)
				return false;

			if (SafeRegions.Contains(living.CurrentRegionID))
				return true;

			// Portal keeps are safe even though they sit inside Old Frontiers
			// zones.
			if (living.CurrentZone != null && living.CurrentAreas.OfType<KeepArea>()
				.Any(area => area.Keep?.IsPortalKeep == true))
				return true;

			// A missing zone is not a safe area. All other safe locations are
			// covered by the explicit region/portal-keep checks above.
			return false;
		}

		public static bool IsOldFrontier(GameLiving living) =>
			living?.CurrentZone?.IsOF == true;

		private static bool IsRealGuild(Guild guild) =>
			guild != null && guild.Name != DummyGuildName;

		private static Guild GuildOf(GameLiving living) => living switch
		{
			GamePlayer player => player.Guild,
			GameBot bot => bot.Guild,
			_ => null
		};

		private static bool CompanionProtects(GameLiving companion, GameLiving target)
		{
			if (companion is not GameBot { IsTemporaryGroupHelper: true } helper)
				return false;

			GameLiving targetIdentity = Resolve(target);
			if (targetIdentity == null)
				return false;

			GamePlayer owner = helper.Owner;
			if (owner == targetIdentity || helper.PlayerGroupLeader == targetIdentity)
				return true;

			if (owner?.Group != null && owner.Group.IsInTheGroup(targetIdentity))
				return true;

			GamePlayer leader = helper.PlayerGroupLeader;
			if (leader?.Group != null && leader.Group.IsInTheGroup(targetIdentity))
				return true;

			return helper.ProtectsTemporaryCompanionMember(targetIdentity);
		}
	}
}
