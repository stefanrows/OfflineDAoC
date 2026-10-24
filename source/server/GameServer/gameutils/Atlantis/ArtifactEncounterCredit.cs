using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
	/// <summary>How far an encounter's credit reaches.</summary>
	public enum ArtifactCreditMode
	{
		/// <summary>Every player who earned the kill: the attackers and their group members in range.</summary>
		Group,
		/// <summary>As <see cref="Group"/>, plus every member of those players' battlegroups in range.</summary>
		Battlegroup,
	}

	/// <summary>
	/// One mapped creature: the artifact its death unlocks, the region it must die in (0 = any), for
	/// whom, and the zone it stands in (text for the player, may be empty).
	/// </summary>
	public sealed record ArtifactEncounterTarget(string ArtifactID, string MobName, int Region, ArtifactCreditMode Mode, string Zone = "");

	/// <summary>
	/// Encounter credit from kills (Trials of Atlantis, TOA_PLAN WP3.3, docs/TOA_ARTIFACTS.md
	/// "Encounter credit"). The table ArtifactEncounterTarget (world data pack 070) maps an artifact
	/// to the named creatures that guard it. When one of them dies a death that pays experience,
	/// every player who earned the kill receives the artifact's encounter credit through
	/// <see cref="ArtifactMgr.GrantArtifactCredit"/>, which refuses a player who already holds the
	/// credit, has awakened the artifact or has no version of it for the class. A battlegroup
	/// encounter also credits the battlegroup members of those players who stand in range.
	///
	/// Live, a few encounters were scripted set pieces (a chest, a flask carried past the taurs).
	/// Here every one is a kill: the creature DOLSharp's artifact quest names, or a named creature
	/// of the artifact's region where it names none. Aphaestia's credit tokens stay as the
	/// alternative. Companions and world bots are GameNPCs, never in the earners: only players earn.
	/// </summary>
	public static class ArtifactEncounterCredit
	{
		private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

		private sealed class Map
		{
			public static readonly Map Empty = new([]);

			public readonly Dictionary<string, List<ArtifactEncounterTarget>> ByMob = new(StringComparer.OrdinalIgnoreCase);
			public readonly Dictionary<string, List<ArtifactEncounterTarget>> ByArtifact = new(StringComparer.Ordinal);
			public readonly int Count;

			public Map(IEnumerable<ArtifactEncounterTarget> targets)
			{
				foreach (ArtifactEncounterTarget target in targets)
				{
					if (target == null || string.IsNullOrWhiteSpace(target.ArtifactID) || string.IsNullOrWhiteSpace(target.MobName))
						continue;

					string mob = target.MobName.Trim();
					ArtifactEncounterTarget clean = target with { ArtifactID = target.ArtifactID.Trim(), MobName = mob, Zone = target.Zone?.Trim() ?? string.Empty };

					if (!ByMob.TryGetValue(mob, out List<ArtifactEncounterTarget> forMob))
						ByMob[mob] = forMob = [];

					if (forMob.Any(t => t.ArtifactID == clean.ArtifactID && t.Region == clean.Region))
						continue;

					forMob.Add(clean);

					if (!ByArtifact.TryGetValue(clean.ArtifactID, out List<ArtifactEncounterTarget> forArtifact))
						ByArtifact[clean.ArtifactID] = forArtifact = [];

					forArtifact.Add(clean);
					Count++;
				}
			}
		}

		private static volatile Map m_map = Map.Empty;

		/// <summary>
		/// Two guardians of one artifact dying in the same tick (Phoebus' Harp has three) must not both
		/// pass the "already holds the credit" check for the same player.
		/// </summary>
		private static readonly object GrantLock = new();

		/// <summary>Number of mapped creature/artifact pairs.</summary>
		public static int Count => m_map.Count;

		#region Load

		/// <summary>
		/// Loads the mapping and listens for deaths. Called by <see cref="ArtifactMgr.Init"/> after the
		/// artifact catalog. A load error leaves an empty mapping; credit tokens still work.
		/// </summary>
		public static void Init()
		{
			try
			{
				int loaded = Load(GameServer.Database.SelectAllObjects<DbArtifactEncounterTarget>());

				if (log.IsInfoEnabled)
					log.Info($"{loaded} artifact encounter targets loaded ({m_map.ByArtifact.Count} artifacts)");

				foreach (string unknown in m_map.ByArtifact.Keys.Where(id => ArtifactMgr.GetArtifact(id) == null))
				{
					if (log.IsWarnEnabled)
						log.Warn($"Artifact encounter targets name the unknown artifact '{unknown}'; they grant nothing");
				}
			}
			catch (Exception ex)
			{
				if (log.IsErrorEnabled)
					log.Error("Artifact encounter targets could not be loaded; encounter credit comes from Aphaestia's tokens only", ex);

				m_map = Map.Empty;
			}

			GameEventMgr.RemoveHandler(GameLivingEvent.Dying, new DOLEventHandler(OnDying));
			GameEventMgr.AddHandler(GameLivingEvent.Dying, new DOLEventHandler(OnDying));
		}

		/// <summary>Replaces the mapping with these rows (the table, or a test). Returns the pairs kept.</summary>
		public static int Load(IEnumerable<DbArtifactEncounterTarget> rows)
		{
			m_map = new Map((rows ?? []).Where(row => row != null)
				.Select(row => new ArtifactEncounterTarget(row.ArtifactID, row.MobName, row.Region, ParseMode(row.CreditMode), row.Zone)));
			return m_map.Count;
		}

		/// <summary>"battlegroup" (any case) is <see cref="ArtifactCreditMode.Battlegroup"/>; everything else is a group encounter.</summary>
		public static ArtifactCreditMode ParseMode(string mode)
		{
			return string.Equals(mode?.Trim(), DbArtifactEncounterTarget.MODE_BATTLEGROUP, StringComparison.OrdinalIgnoreCase)
				? ArtifactCreditMode.Battlegroup
				: ArtifactCreditMode.Group;
		}

		#endregion

		#region Lookup

		/// <summary>The targets this creature's death serves in this region (region 0 rows serve every region).</summary>
		public static List<ArtifactEncounterTarget> TargetsFor(string mobName, int region)
		{
			if (string.IsNullOrWhiteSpace(mobName) || !m_map.ByMob.TryGetValue(mobName.Trim(), out List<ArtifactEncounterTarget> targets))
				return [];

			return targets.Where(t => t.Region == 0 || t.Region == region).ToList();
		}

		/// <summary>The creatures that guard this artifact, in table order; empty if none is mapped.</summary>
		public static List<ArtifactEncounterTarget> TargetsOf(string artifactID)
		{
			return !string.IsNullOrEmpty(artifactID) && m_map.ByArtifact.TryGetValue(artifactID, out List<ArtifactEncounterTarget> targets)
				? [.. targets]
				: [];
		}

		/// <summary>
		/// The guardians and where they stand, for a scholar's hint, or null if none is mapped:
		/// "Minkhat in Mesothalassa", "Hallea, Kosma or Zoea in Mesothalassa", "the protector of light
		/// in Oceanus Boreal", "Landrine in Stygian Delta or Kertom in Arbor Glen". A zone the table
		/// leaves empty reads "in Atlantis".
		/// </summary>
		public static string DescribeGuardians(string artifactID)
		{
			List<ArtifactEncounterTarget> targets = TargetsOf(artifactID)
				.GroupBy(t => t.MobName, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

			if (targets.Count == 0)
				return null;

			// One phrase per zone, in table order: the names of that zone, then "in <zone>".
			var phrases = targets
				.GroupBy(t => string.IsNullOrWhiteSpace(t.Zone) ? "Atlantis" : t.Zone, StringComparer.OrdinalIgnoreCase)
				.Select(zone => JoinOr(zone.Select(t => WithArticle(t.MobName)).ToList()) + " in " + zone.Key)
				.ToList();

			return JoinOr(phrases);
		}

		/// <summary>
		/// The name as a sentence uses it (GameObject.GetName with the definite article): a proper name
		/// as it stands ("Minkhat"), a common one with "the" ("the protector of light").
		/// </summary>
		public static string WithArticle(string name, bool firstLetterUppercase = false)
		{
			if (string.IsNullOrWhiteSpace(name))
				return string.Empty;

			name = name.Trim();

			if (char.IsUpper(name[0]))
				return name;

			return (firstLetterUppercase ? "The " : "the ") + name;
		}

		private static string JoinOr(List<string> parts)
		{
			return parts.Count switch
			{
				0 => string.Empty,
				1 => parts[0],
				_ => string.Join(", ", parts.Take(parts.Count - 1)) + " or " + parts[^1],
			};
		}

		#endregion

		#region Credit

		/// <summary>
		/// Whether this death can earn credit at all: an NPC that is not a companion, bot or pet and
		/// whose death pays experience (the same gate as AbstractServerRules.OnNpcKilled).
		/// </summary>
		public static bool IsCreditKill(GameNPC dead)
		{
			if (dead == null || dead is GameBot || dead.Brain is IControlledBrain)
				return false;

			return dead.RewardStatus == GameNPC.RewardEligibility.Eligible;
		}

		/// <summary>
		/// The players who earn credit for this kill: the Dying event's players (attackers and their
		/// group members in range; the killer if the list is missing), plus for a battlegroup
		/// encounter every member of their battlegroups. Each one alive, in the world, in the region and
		/// within experience range of the body. Companions and bots are never in the list.
		/// </summary>
		public static List<GamePlayer> Earners(GameNPC dead, DyingEventArgs death, ArtifactCreditMode mode)
		{
			var earners = new List<GamePlayer>();
			var seen = new HashSet<GamePlayer>();

			IEnumerable<GamePlayer> candidates = death?.PlayerKillers is { Count: > 0 } killers
				? killers
				: death?.Killer is GamePlayer killer ? [killer] : [];

			foreach (GamePlayer player in candidates.ToList())
				Consider(player);

			if (mode == ArtifactCreditMode.Battlegroup)
			{
				foreach (GamePlayer player in earners.ToList())
				{
					BattleGroup battlegroup = player.TempProperties?.GetProperty<BattleGroup>(BattleGroup.BATTLEGROUP_PROPERTY);

					if (battlegroup == null)
						continue;

					// A copy taken under the members lock; companions ride in a separate list and
					// are never members.
					foreach (GamePlayer member in battlegroup.GetPlayerMembers())
						Consider(member);
				}
			}

			return earners;

			void Consider(GamePlayer player)
			{
				if (player == null || !seen.Add(player))
					return;

				if (player.ObjectState != GameObject.eObjectState.Active || !player.IsAlive)
					return;

				if (dead != null && !player.IsWithinRadius(dead, WorldMgr.MAX_EXPFORKILL_DISTANCE))
					return;

				earners.Add(player);
			}
		}

		/// <summary>
		/// Grants every artifact this kill unlocks to every earner. Returns the (player, artifact)
		/// pairs that received credit; players who already hold it or have awakened it are skipped.
		/// </summary>
		public static List<(GamePlayer Player, string ArtifactID)> AwardKill(GameNPC dead, DyingEventArgs death)
		{
			var granted = new List<(GamePlayer, string)>();

			if (!IsCreditKill(dead))
				return granted;

			List<ArtifactEncounterTarget> targets = TargetsFor(dead.Name, dead.CurrentRegionID);

			foreach (ArtifactEncounterTarget target in targets)
			{
				foreach (GamePlayer player in Earners(dead, death, target.Mode))
				{
					bool earned;

					lock (GrantLock)
						earned = ArtifactMgr.GrantArtifactCredit(player, target.ArtifactID);

					if (!earned)
						continue;

					string artifact = ArtifactMgr.GetArtifactName(target.ArtifactID);
					string next = ArtifactMgr.HasScrollSet(target.ArtifactID)
						? "Bring its story to a scholar to awaken it."
						: "An Artifact Scholar will awaken it on this credit alone.";
					string where = ZoneOfDeath(dead, target);
					player.Out?.SendMessage($"With {WithArticle(dead.Name)} defeated{(where == null ? string.Empty : " in " + where)}, " +
						$"you have earned the encounter credit for {artifact}. {next}", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
					granted.Add((player, target.ArtifactID));
				}
			}

			return granted;
		}

		/// <summary>
		/// The zone the creature died in: the world's zone at the body, else the table's zone if it
		/// names exactly one, else null.
		/// </summary>
		private static string ZoneOfDeath(GameNPC dead, ArtifactEncounterTarget target)
		{
			string zone = dead.CurrentZone?.Description;

			if (!string.IsNullOrWhiteSpace(zone))
				return zone;

			zone = target.Zone;
			return string.IsNullOrWhiteSpace(zone) || zone.Contains(" or ") || zone.Contains(',') ? null : zone;
		}

		/// <summary>Global Dying listener: credit for the deaths of mapped creatures.</summary>
		public static void OnDying(DOLEvent e, object sender, EventArgs arguments)
		{
			if (sender is not GameNPC dead || arguments is not DyingEventArgs death || m_map.Count == 0)
				return;

			if (!m_map.ByMob.ContainsKey(dead.Name ?? string.Empty))
				return;

			try
			{
				AwardKill(dead, death);
			}
			catch (Exception ex)
			{
				// Encounter credit is never worth losing a kill over.
				if (log.IsErrorEnabled)
					log.Error($"Artifact encounter credit for the death of {dead.Name} failed", ex);
			}
		}

		#endregion
	}
}
