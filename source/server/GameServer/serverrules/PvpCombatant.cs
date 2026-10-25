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
		private const string OptionalSafetyRelinquished = "Autonomous PvP safety relinquished";

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
		/// The one alliance seam for player-shaped actors (humans, GameBots and
		/// their pets) in the current world. See the world overload.
		/// </summary>
		public static bool AreAllied(GameLiving first, GameLiving second) =>
			AreAllied(WorldMode.Current, first, second);

		/// <summary>
		/// Camlann allies are grouped, guilded, or in the same battlegroup. The
		/// dummy guild used by the legacy client pet hack is never an alliance.
		/// Temporary companions also remain loyal to their owner and the owner's
		/// current group while they exist.
		/// In the RvR world (Realm-1) the same realm is allied everywhere and
		/// another realm never is: group, guild, battlegroup and companion ties
		/// do not bridge realms (<see cref="AllianceRealm"/> decides).
		/// </summary>
		public static bool AreAllied(WorldModelKind world, GameLiving first, GameLiving second)
		{
			GameLiving a = Resolve(first);
			GameLiving b = Resolve(second);
			if (a == null || b == null)
				return false;

			if (a == b)
				return true;

			return world == WorldModelKind.Realm ? AreRealmAllies(a, b) : AreCamlannAllies(a, b);
		}

		/// <summary>
		/// RvR world: allied when both fight for the same realm. Two realm-less
		/// actors (unsponsored battleground encounter bots) are held together by
		/// their own group, guild and battlegroup as in Camlann, never by a realm.
		/// </summary>
		private static bool AreRealmAllies(GameLiving a, GameLiving b)
		{
			eRealm first = AllianceRealm(a);
			if (first != AllianceRealm(b))
				return false;
			return first != eRealm.None || AreCamlannAllies(a, b);
		}

		/// <summary>
		/// The realm an actor fights for in the RvR world. Pets resolve to their
		/// owner first. Persistent companions and /spawn helpers fight for their
		/// owner's realm, even if one of another realm exists (R2 stops creating
		/// them). A battleground encounter bot fights for its sponsor's realm, else
		/// for its real guild's realm, else for none (a neutral opponent); its class
		/// realm is only a model. Everyone else fights for their own realm.
		/// </summary>
		public static eRealm AllianceRealm(GameLiving living)
		{
			GameLiving actor = Resolve(living) ?? living;
			if (actor is GameBot bot)
			{
				if (bot.IsBattlegroundEncounterBot)
				{
					GameLiving sponsor = EncounterSponsor(bot);
					if (sponsor != null)
						return AllianceRealm(sponsor);
					Guild guild = bot.Guild;
					return IsRealGuild(guild) && IsPlayableRealm(guild.Realm) ? guild.Realm : eRealm.None;
				}

				if (bot.Owner != null)
					return bot.Owner.Realm;
			}

			return actor?.Realm ?? eRealm.None;
		}

		public static bool IsPlayableRealm(eRealm realm) =>
			realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia;

		private static bool AreCamlannAllies(GameLiving a, GameLiving b)
		{
			// Camp escorts inherit the sponsor's current alliances without taking
			// a slot in the player's group or becoming a persistent companion.
			GameLiving sponsorA = EncounterSponsor(a);
			GameLiving sponsorB = EncounterSponsor(b);
			if (sponsorA != null || sponsorB != null)
			{
				if (AreAllied(WorldModelKind.Camlann, sponsorA ?? a, sponsorB ?? b))
					return true;
			}

			if (a.Group != null && a.Group == b.Group && a.Group.IsInTheGroup(a) && a.Group.IsInTheGroup(b))
				return true;

			Guild firstGuild = GuildOf(a);
			Guild secondGuild = GuildOf(b);
			if (AreGuildIdsAllied(firstGuild?.GuildID, firstGuild?.Name, secondGuild?.GuildID, secondGuild?.Name))
				return true;

			BattleGroup firstBattleGroup = BattleGroupOf(a);
			BattleGroup secondBattleGroup = BattleGroupOf(b);
			if (firstBattleGroup != null && firstBattleGroup == secondBattleGroup)
				return true;

			if (a.CurrentRegionID == b.CurrentRegionID && a.CurrentRegionID == OpenBattlegroundMode.OpenBgRegion &&
				RealmFriendInOpenBg(OpenBattlegroundMode.Current, a.CurrentRegionID, a.Realm, b.Realm))
				return true;

			return CompanionProtects(a, b) || CompanionProtects(b, a);
		}

		/// <summary>Per realm mode: inside the open BG the same realm is a friend (classic DAoC); everywhere else Camlann.</summary>
		public static bool RealmFriendInOpenBg(OpenBgMode mode, ushort region, eRealm first, eRealm second) =>
			OpenBattlegroundMode.RealmsAreFriends(mode, region, first, second);

		private static GameLiving EncounterSponsor(GameLiving actor)
		{
			if (actor is not GameBot { IsBattlegroundEncounterBot: true } encounter)
				return null;
			GameLiving sponsor = Resolve(encounter.BattlegroundAllianceSponsor);
			return sponsor != null && sponsor is not GameBot { IsBattlegroundEncounterBot: true } &&
				sponsor.ObjectState == GameObject.eObjectState.Active &&
				sponsor.CurrentRegionID == encounter.CurrentRegionID ? sponsor : null;
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

			// Gothwaite PvP mode: the castle circle is safe while the mode is on (G3). Off, this is a no-op.
			if (GothwaitePvpRules.IsCastleSafe(living)) return true;

			if (SafeRegions.Contains(living.CurrentRegionID))
				return true;

			// Portal keeps are safe even though they sit inside Old Frontiers
			// zones. RvR world (spec 3.2): only for the keep's own realm, so an
			// intruder inside can be fought.
			if (living.CurrentZone != null && living.CurrentAreas.OfType<KeepArea>()
				.Any(area => area.Keep?.IsPortalKeep == true &&
					(!WorldMode.IsRvr || RealmPlaces.HubProtects(WorldModelKind.Realm, RealmPlaces.PortalKeepRealm(area.Keep), AllianceRealm(living)))))
				return true;

			// Camlann owner decision 7: the three border hubs are neutral safe
			// hubs like the capitals, for humans and bots alike, together with
			// the teleporter landings and bindstones outside their radius.
			// RvR world: a hub and its landing protect only the hub's realm.
			ushort regionId = living.CurrentRegionID;
			if (IsBorderHubRegion(regionId) &&
				(IsSafeBorderHub(regionId, living.X, living.Y) || IsSafeHubLanding(regionId, living.X, living.Y)) &&
				(!WorldMode.IsRvr || RealmPlaces.HubProtects(WorldModelKind.Realm, RealmPlaces.HubRealmAt(regionId, living.X, living.Y), AllianceRealm(living))))
				return true;

			// A missing zone is not a safe area. All other safe locations are
			// covered by the explicit region/portal-keep checks above.
			return false;
		}

		/// <summary>
		/// The attacker side of the safe-zone rule. Camlann: an attacker in any safe
		/// area cannot attack. RvR world (spec 3.2): capitals, housing and newbie
		/// dungeons disarm everyone; a portal keep, border hub or hub landing protects
		/// its own realm, and a defender it protects may fight only an intruder inside
		/// that same hub, never snipe from cover at someone outside it.
		/// </summary>
		public static bool BlocksAttackFromSafeArea(GameLiving attacker, GameLiving defender)
		{
			if (!WorldMode.IsRvr)
				return IsSafeArea(attacker);
			if (attacker == null)
				return false;
			if (IsSafeRegion(attacker.CurrentRegionID))
				return true;
			eRealm hub = HubRealmOf(attacker);
			if (!PvpCombatant.IsPlayableRealm(hub) || hub != AllianceRealm(attacker))
				return false; // not covered by an own hub: the open field rules apply
			return defender == null || defender.CurrentRegionID != attacker.CurrentRegionID || HubRealmOf(defender) != hub;
		}

		/// <summary>The realm of the portal keep, border hub or hub landing a living stands in; None elsewhere.</summary>
		public static eRealm HubRealmOf(GameLiving living)
		{
			if (living == null)
				return eRealm.None;
			if (living.CurrentZone != null)
			{
				AbstractGameKeep portal = living.CurrentAreas.OfType<KeepArea>().Select(area => area.Keep)
					.FirstOrDefault(keep => keep?.IsPortalKeep == true);
				if (portal != null)
					return RealmPlaces.PortalKeepRealm(portal);
			}
			ushort regionId = living.CurrentRegionID;
			return IsBorderHubRegion(regionId) &&
				(IsSafeBorderHub(regionId, living.X, living.Y) || IsSafeHubLanding(regionId, living.X, living.Y))
				? RealmPlaces.HubRealmAt(regionId, living.X, living.Y)
				: eRealm.None;
		}

        public static bool IsSafeReleasePoint(ushort regionId, Point3D point)
        {
            Region region = WorldMgr.GetRegion(regionId);
            return point != null && region != null && region.GetZone(point.X, point.Y) != null &&
                (GothwaitePvpRules.IsCastleSafe(regionId, point.X, point.Y) ||
                 IsSafeRegion(regionId) || IsSafeBorderHub(regionId, point.X, point.Y) ||
                 IsSafeHubLanding(regionId, point.X, point.Y) ||
                 region.GetAreasOfSpot(point).OfType<KeepArea>().Any(area => area.Keep?.IsPortalKeep == true));
        }

		public static bool IsSafeRegion(ushort regionId) => SafeRegions.Contains(regionId);

		/// <summary>
		/// Radius of the Castle Sauvage, Svasud Faste and Druim Ligen safe hubs.
		/// Centres and radius are the imported `area` rows of the same names
		/// (DOL.GS.Area+Circle, radius 3500), which AutonomousRvrStaging also
		/// uses as its staging anchors. The frontier outside stays open PvP.
		/// </summary>
		public const int SafeBorderHubRadius = 3500;

		private static readonly AutonomousRvrStaging.BorderKeep[] SafeBorderHubs =
			new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }
				.Select(realm => AutonomousRvrStaging.TryGetBorderKeep(realm, out var hub) ? hub : default)
				.Where(hub => hub.RegionId != 0).ToArray();

		private static bool IsBorderHubRegion(ushort regionId)
		{
			foreach (AutonomousRvrStaging.BorderKeep hub in SafeBorderHubs)
				if (hub.RegionId == regionId)
					return true;
			return false;
		}

		public static bool IsSafeBorderHub(ushort regionId, int x, int y)
		{
			foreach (AutonomousRvrStaging.BorderKeep hub in SafeBorderHubs)
			{
				if (hub.RegionId != regionId)
					continue;
				double dx = x - (double)hub.Position.X, dy = y - (double)hub.Position.Y;
				if (dx * dx + dy * dy <= (double)SafeBorderHubRadius * SafeBorderHubRadius)
					return true;
			}
			return false;
		}

		public readonly record struct SafeHubLanding(ushort RegionId, int X, int Y, int Radius, string Name);

		/// <summary>
		/// Camlann decision 7, option b (owner, 2026-09-28): a second safe
		/// circle around a hub's outer bindstones and the code-fallback
		/// teleporter landing next to them, both outside the 3,500-unit keep
		/// radius. The bindstones are the reason: in the shipped save the
		/// Teleport rows land players inside the hubs (Castle Sauvage
		/// 584151,477177, Svasud Faste 767242,669591); the landings below come
		/// only from AllRealmsTeleportFallbacks. Each centre is the midpoint of
		/// the fallback landing and the bindstones; the radius covers both plus
		/// the 750-unit bind radius. The road to the keep stays open PvP.
		/// Druim Ligen needs none: its teleporter destination (334342,419994)
		/// and its only bindstone (333220,420510) already lie inside the hub.
		/// </summary>
		public static readonly SafeHubLanding[] SafeHubLandings =
		{
			// Bindstones 584770,486230 and 584638,486320; fallback landing 583913,487012.
			new(1, 584340, 486620, 1500, "Castle Sauvage outer bindstones"),
			// Bindstone 764082,672416; fallback landing 765694,673509. About 4,000 units
			// from the keep centre, so this circle touches the hub circle.
			new(100, 764890, 672960, 1800, "Svasud Faste outer bindstone"),
		};

		public static bool IsSafeHubLanding(ushort regionId, int x, int y)
		{
			foreach (SafeHubLanding landing in SafeHubLandings)
			{
				if (landing.RegionId != regionId)
					continue;
				double dx = x - (double)landing.X, dy = y - (double)landing.Y;
				if (dx * dx + dy * dy <= (double)landing.Radius * landing.Radius)
					return true;
			}
			return false;
		}

		public static bool IsOldFrontier(GameLiving living) =>
			living?.CurrentZone?.IsOF == true;

		public static bool IsSafetyProtected(GamePlayer player, int safetyLevel = 10) =>
			player != null && !(BattlegroundCampaignPolicy.IsEnabled &&
                BattlegroundCampaignPolicy.IsEligible(player, BattlegroundCampaignCatalog.Find(player.CurrentRegionID))) &&
            IsSafetyProtected(player.Level, player.SafetyFlag, IsOldFrontier(player), safetyLevel);

		public static bool IsSafetyProtected(int level, bool safetyFlag, bool isOldFrontier, int safetyLevel = 10) =>
			safetyFlag && level < safetyLevel && !isOldFrontier;

		/// <summary>
		/// Autonomous world bots have no /safety command. Keep their sub-10
		/// protection even if an older RvR assignment left an opt-in flag behind.
		/// Player companions and temporary helpers follow their owner instead.
		/// </summary>
		public static bool IsSafetyProtected(GameLiving living, int safetyLevel = 10) => living switch
		{
			GamePlayer player => IsSafetyProtected(player, safetyLevel),
			GameBot { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false } bot =>
				bot.Level < safetyLevel && !GothwaitePvpRules.LiftsLowLevelProtection(bot),
			_ => false
		};

		/// <summary>Protect low-level autonomous actors and their controlled pets
		/// even when an attack path skips the normal server-rule target check.</summary>
		public static bool BlocksLowLevelAutonomousPvp(GameLiving attacker, GameLiving defender)
		{
			GameLiving first = Resolve(attacker);
			GameLiving second = Resolve(defender);
			return first != null && second != null && first != second &&
				(IsSafetyProtected(first) && first is GameBot ||
				 IsSafetyProtected(second) && second is GameBot);
		}

		/// <summary>
		/// Damage-time guard for every autonomous-PvP block that must hold even
		/// where an attack path skips the normal target check: sub-10 safety
		/// and the hub peace between same-realm autonomous world bots (wave 6
		/// band, wave 6b eight-minute departure clock; PvPServerRules checks
		/// the peace after its alliance check).
		/// TakeDamage and OnAttackedByEnemy ask it so a DoT, a projectile in
		/// flight or a bypassing path neither hurts nor starts a retaliation.
		/// Only TakeDamage passes <paramref name="countStray"/>, so one stopped
		/// hit is counted once.
		/// </summary>
		public static bool BlocksAutonomousPvp(GameLiving attacker, GameLiving defender, bool countStray = false)
		{
			if (BlocksLowLevelAutonomousPvp(attacker, defender))
				return true;
			if (!AutonomousHubDeparture.HubPeaceApplies(attacker, defender, out eRealm realm))
				return false;
			if (countStray)
				AutonomousHubDeparture.CountPeaceBlocked(realm, true);
			return true;
		}

		/// <summary>Uses the native player flag and records the equivalent explicit
		/// opt-in for autonomous actors, which otherwise never own a /safety flag.</summary>
		public static void RelinquishOptionalSafety(GameLiving living)
		{
			switch (Resolve(living))
			{
				case GamePlayer player:
					player.SafetyFlag = false;
					break;
				case GameBot bot:
					bot.TempProperties?.SetProperty(OptionalSafetyRelinquished, true);
					break;
			}
		}

		public static bool HasRelinquishedOptionalSafety(GameLiving living) => Resolve(living) switch
		{
			GamePlayer player => !player.SafetyFlag,
			GameBot bot => bot.TempProperties?.GetProperty<bool>(OptionalSafetyRelinquished, false) == true,
			_ => false
		};

		public static bool IsRealGuild(Guild guild) =>
			guild != null && guild.Name != DummyGuildName && !DOL.GS.Keeps.PvpKeepCampaign.IsGarrison(guild);

		public static bool AreGuildIdsAllied(string firstId, string firstName, string secondId, string secondName) =>
			!string.IsNullOrWhiteSpace(firstId) && !string.IsNullOrWhiteSpace(secondId) &&
			firstName != DummyGuildName && secondName != DummyGuildName && firstId == secondId;

		/// <summary>
		/// A player's battlegroup; a companion or /spawn helper fights under its
		/// owner's (2026-10-02: in a shared battlegroup the bots of one owner
		/// treated the other owner and his group as enemies).
		/// </summary>
		public static BattleGroup BattleGroupOf(GameLiving living)
		{
			BattleGroup own = living?.TempProperties?.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY);
			if (own != null || living is not GameBot bot)
				return own;
			GamePlayer owner = bot.Owner ?? bot.PlayerGroupLeader;
			return owner?.TempProperties?.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY);
		}

		public static Guild GuildOf(GameLiving living) => living switch
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
