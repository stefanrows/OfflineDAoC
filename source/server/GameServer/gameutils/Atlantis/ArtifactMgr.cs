/*
 * DAWN OF LIGHT - The first free open source DAoC server emulator
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 *
 */
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.GS.Quests;
using DOL.GS.Quests.Atlantis;

namespace DOL.GS
{
	/// <summary>
	/// The artifact manager (Trials of Atlantis, TOA_PLAN WP3.1).
	///
	/// Ported from DOLSharp (author Aredhel) through the gaheris OpenDAoC overlay. Offline
	/// DAoC changes: the catalog is an immutable snapshot (no locks, no KeyNotFound on
	/// unknown IDs), experience comes from GamePlayerEvent.GainedExperience (WP2.0) filtered
	/// to kill sources, artifact levels derive from the item's stored experience, and
	/// encounter credit is keyed by artifact ID so our numeric placeholder rows
	/// (ScholarID 1001, EncounterID = template name) never need a quest type.
	/// See docs/TOA_ARTIFACTS.md.
	/// </summary>
	public static class ArtifactMgr
	{
		private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

		public enum Book { NoPage = 0x0, Page1 = 0x1, Page2 = 0x2, Page3 = 0x4, AllPages = 0x7 };

		/// <summary>Highest artifact level.</summary>
		public const int MAX_LEVEL = 10;

		/// <summary>
		/// What dye, effect and reskin NPCs say when handed an artifact. They rebuild the item
		/// as a new unique template, which would cut it loose from its artifact row, its stored
		/// level and its level gates.
		/// </summary>
		public const string REWORK_REFUSAL = "I dare not work on an artifact. Its power is bound to its form as it is.";

		/// <summary>Number of bonus slots an artifact level requirement can name (DbArtifactBonus.ID).</summary>
		public const int BONUS_SLOTS = DbArtifactBonus.ID.Max - DbArtifactBonus.ID.Min + 1;

		/// <summary>
		/// Immutable view of the three artifact tables. Replaced as a whole on (re)load.
		/// </summary>
		private sealed class Catalog
		{
			public static readonly Catalog Empty = new([], [], []);

			public readonly Dictionary<string, DbArtifact> Artifacts = new(StringComparer.Ordinal);
			public readonly Dictionary<string, List<DbArtifactXItem>> Versions = new(StringComparer.Ordinal);
			public readonly Dictionary<string, string> ArtifactByItem = new(StringComparer.OrdinalIgnoreCase);
			public readonly Dictionary<string, int[]> Requirements = new(StringComparer.Ordinal);

			/// <summary>Readable artifact names, filled on first use (template lookups).</summary>
			public readonly ConcurrentDictionary<string, string> Names = new(StringComparer.Ordinal);

			public Catalog(IEnumerable<DbArtifact> artifacts, IEnumerable<DbArtifactXItem> versions, IEnumerable<DbArtifactBonus> bonuses)
			{
				foreach (DbArtifact artifact in artifacts ?? [])
				{
					if (string.IsNullOrEmpty(artifact?.ArtifactID))
						continue;

					if (!Artifacts.TryAdd(artifact.ArtifactID, artifact) && log.IsWarnEnabled)
						log.Warn($"Duplicate artifact ID '{artifact.ArtifactID}' ignored");
				}

				foreach (DbArtifactXItem version in versions ?? [])
				{
					if (string.IsNullOrEmpty(version?.ArtifactID) || string.IsNullOrEmpty(version.ItemID))
						continue;

					if (!Versions.TryGetValue(version.ArtifactID, out List<DbArtifactXItem> list))
						Versions[version.ArtifactID] = list = [];

					list.Add(version);

					if (!ArtifactByItem.TryAdd(version.ItemID, version.ArtifactID) && log.IsWarnEnabled)
						log.Warn($"Artifact item '{version.ItemID}' belongs to more than one artifact; keeping '{ArtifactByItem[version.ItemID]}'");
				}

				foreach (DbArtifactBonus bonus in bonuses ?? [])
				{
					if (string.IsNullOrEmpty(bonus?.ArtifactID) || bonus.BonusID < 0 || bonus.BonusID >= BONUS_SLOTS)
						continue;

					if (!Requirements.TryGetValue(bonus.ArtifactID, out int[] requirements))
						Requirements[bonus.ArtifactID] = requirements = new int[BONUS_SLOTS];

					// A slot named twice keeps the higher requirement: never unlock early.
					requirements[bonus.BonusID] = Math.Max(requirements[bonus.BonusID], Math.Clamp(bonus.Level, 0, MAX_LEVEL));
				}
			}
		}

		private static volatile Catalog m_catalog = Catalog.Empty;

		#region Init / load

		/// <summary>
		/// Loads the artifact tables and starts listening for experience. Called once by
		/// GameServer.StartUp after the database and the server properties are ready, before
		/// any player can log in. Never blocks the boot: a load error leaves an empty catalog,
		/// so artifacts then behave like plain items.
		/// </summary>
		public static bool Init()
		{
			try
			{
				LoadArtifacts();
			}
			catch (Exception ex)
			{
				if (log.IsErrorEnabled)
					log.Error("Artifact tables could not be loaded; artifacts behave like plain items", ex);

				m_catalog = Catalog.Empty;
			}

			GameEventMgr.RemoveHandler(GamePlayerEvent.GainedExperience, new DOLEventHandler(PlayerGainedExperience));
			GameEventMgr.AddHandler(GamePlayerEvent.GainedExperience, new DOLEventHandler(PlayerGainedExperience));
			return true;
		}

		/// <summary>
		/// Load artifacts from the DB.
		/// </summary>
		public static int LoadArtifacts()
		{
			Load(GameServer.Database.SelectAllObjects<DbArtifact>(),
				GameServer.Database.SelectAllObjects<DbArtifactXItem>(),
				GameServer.Database.SelectAllObjects<DbArtifactBonus>());

			if (log.IsInfoEnabled)
				log.Info($"{m_catalog.Artifacts.Count} artifacts loaded ({m_catalog.ArtifactByItem.Count} artifact items)");

			return m_catalog.Artifacts.Count;
		}

		/// <summary>
		/// Replaces the catalog with the given rows. Used by LoadArtifacts and by tests; a
		/// data pack (WP3.1b) can call it after writing new rows.
		/// </summary>
		public static void Load(IEnumerable<DbArtifact> artifacts, IEnumerable<DbArtifactXItem> versions, IEnumerable<DbArtifactBonus> bonuses)
		{
			m_catalog = new Catalog(artifacts, versions, bonuses);
		}

		#endregion

		#region Lookup

		/// <summary>
		/// Find the matching artifact for the item template ID, or null.
		/// </summary>
		public static string GetArtifactIDFromItemID(string itemID)
		{
			if (string.IsNullOrEmpty(itemID))
				return null;

			return m_catalog.ArtifactByItem.TryGetValue(itemID, out string artifactID) ? artifactID : null;
		}

		/// <summary>
		/// True if the template is a version of an artifact (named by an ArtifactXItem row).
		/// </summary>
		public static bool IsArtifactTemplate(DbItemTemplate template)
		{
			return template != null && GetArtifactIDFromItemID(template.Id_nb) != null;
		}

		/// <summary>The artifact row, or null.</summary>
		public static DbArtifact GetArtifact(string artifactID)
		{
			if (string.IsNullOrEmpty(artifactID))
				return null;

			return m_catalog.Artifacts.TryGetValue(artifactID, out DbArtifact artifact) ? artifact : null;
		}

		/// <summary>
		/// Get all artifacts.
		/// </summary>
		public static List<DbArtifact> GetArtifacts()
		{
			return m_catalog.Artifacts.Values.ToList();
		}

		/// <summary>
		/// Find all artifacts from a particular zone. Our rows all carry Zone '0' today.
		/// </summary>
		public static List<DbArtifact> GetArtifacts(string zone)
		{
			if (zone == null)
				return [];

			return m_catalog.Artifacts.Values.Where(a => a.Zone == zone).ToList();
		}

		/// <summary>
		/// The artifact's reuse timer in seconds (Artifact.ReuseTimer). Our rows carry the
		/// placeholder 1 for 56 of 57 artifacts, so InventoryArtifact does not apply it; use
		/// spell recast delays instead.
		/// </summary>
		public static int GetReuseTimer(InventoryArtifact item)
		{
			return GetArtifact(item?.ArtifactID)?.ReuseTimer ?? 0;
		}

		/// <summary>
		/// Get a list of all scholars studying this artifact (ScholarID, ';'-separated). Our
		/// rows carry the numeric placeholder (the artifact ID), which matches no NPC name.
		/// </summary>
		public static string[] GetScholars(string artifactID)
		{
			DbArtifact artifact = GetArtifact(artifactID);
			return artifact == null ? null : Util.SplitCSV(artifact.ScholarID ?? string.Empty).ToArray();
		}

		/// <summary>
		/// Checks whether or not the item is an artifact.
		/// </summary>
		public static bool IsArtifact(DbInventoryItem item)
		{
			if (item == null)
				return false;

			if (item is InventoryArtifact)
				return true;

			return GetArtifactIDFromItemID(item.Id_nb) != null;
		}

		/// <summary>
		/// Every ArtifactXItem row of the artifact (all realms), ordered by numeric Version.
		/// Returns a fresh list.
		/// </summary>
		public static List<DbArtifactXItem> GetAllVersions(string artifactID)
		{
			if (string.IsNullOrEmpty(artifactID) || !m_catalog.Versions.TryGetValue(artifactID, out List<DbArtifactXItem> versions))
				return [];

			return versions.OrderBy(v => int.TryParse(v.Version, out int n) ? n : int.MaxValue)
				.ThenBy(v => v.Version, StringComparer.Ordinal).ToList();
		}

		/// <summary>
		/// The name players know the artifact by: the item name of its first version that has a
		/// template ("Alvarus's Leggings"), else the Credit value with spaces, else the ID. Our
		/// rows carry no readable name of their own (TOA_PLAN WP3.4).
		/// </summary>
		public static string GetArtifactName(string artifactID)
		{
			DbArtifact artifact = GetArtifact(artifactID);

			if (artifact == null)
				return artifactID ?? string.Empty;

			Catalog catalog = m_catalog;
			return catalog.Names.GetOrAdd(artifactID, id =>
			{
				foreach (DbArtifactXItem version in GetAllVersions(id))
				{
					try
					{
						string name = GameServer.Database?.FindObjectByKey<DbItemTemplate>(version.ItemID)?.Name;

						if (!string.IsNullOrWhiteSpace(name))
							return name;
					}
					catch (Exception)
					{
						// No database (tests, tools): fall through to the credit name.
					}
				}

				return string.IsNullOrWhiteSpace(artifact.Credit) ? id : artifact.Credit.Replace('_', ' ');
			});
		}

		/// <summary>
		/// Whether the artifact is earned with a book of three scrolls. The scroll wiring pack
		/// (051) clears BookID for the three artifacts without a scroll set (Flask, Bruiser,
		/// Shield of Khaos); an Artifact Scholar hands those out on encounter credit alone.
		/// </summary>
		public static bool HasScrollSet(string artifactID)
		{
			return !string.IsNullOrWhiteSpace(GetArtifact(artifactID)?.BookID);
		}

		/// <summary>
		/// A text column of the Artifact row if it holds readable text. Our rows carry the
		/// numeric placeholder (the artifact ID, "1001") in every message column; those, and
		/// empty values, give <paramref name="fallback"/>.
		/// </summary>
		public static string ReadableText(string stored, string fallback)
		{
			if (string.IsNullOrWhiteSpace(stored) || stored.Trim().All(char.IsDigit))
				return fallback;

			return stored;
		}

		#endregion

		#region Experience / level

		private static readonly long[] m_xpForLevel =
		{
			0,				// xp to level 0
			50000000,		// xp to level 1
			100000000,		// xp to level 2
			150000000,		// xp to level 3
			200000000,		// xp to level 4
			250000000,		// xp to level 5
			300000000,		// xp to level 6
			350000000,		// xp to level 7
			400000000,		// xp to level 8
			450000000,		// xp to level 9
			500000000		// xp to level 10
		};

		/// <summary>Total artifact experience needed for <paramref name="level"/> (0-10).</summary>
		public static long GetExperienceForLevel(int level)
		{
			return m_xpForLevel[Math.Clamp(level, 0, MAX_LEVEL)];
		}

		/// <summary>
		/// Artifact level for a total amount of artifact experience.
		/// </summary>
		public static int GetLevelForExperience(long experience)
		{
			for (int level = MAX_LEVEL; level > 0; --level)
			{
				if (experience >= m_xpForLevel[level])
					return level;
			}

			return 0;
		}

		/// <summary>
		/// Determine artifact level from total XP.
		/// </summary>
		public static int GetCurrentLevel(InventoryArtifact item)
		{
			return item == null ? 0 : GetLevelForExperience(item.Experience);
		}

		/// <summary>
		/// Calculate the XP gained towards the next level (in percent).
		/// </summary>
		public static int GetXPGainedForLevel(InventoryArtifact item)
		{
			if (item == null)
				return 0;

			int level = GetCurrentLevel(item);

			if (level >= MAX_LEVEL)
				return 0;

			double xpGained = item.Experience - m_xpForLevel[level];
			double xpNeeded = m_xpForLevel[level + 1] - m_xpForLevel[level];
			return (int) (xpGained * 100 / xpNeeded);
		}

		/// <summary>
		/// Level requirement per bonus slot (index = DbArtifactBonus.ID). A slot without an
		/// ArtifactBonus row is a level 0 bonus. Returns a fresh copy.
		/// </summary>
		public static int[] GetLevelRequirements(string artifactID)
		{
			int[] requirements = new int[BONUS_SLOTS];

			if (!string.IsNullOrEmpty(artifactID) && m_catalog.Requirements.TryGetValue(artifactID, out int[] known))
				Array.Copy(known, requirements, BONUS_SLOTS);

			return requirements;
		}

		/// <summary>
		/// What this artifact gains XP from. Live had a source per artifact; DOL and we use
		/// one rule for all of them.
		/// </summary>
		public static string GetEarnsXP(InventoryArtifact item)
		{
			return "Slaying enemies and monsters found anywhere.";
		}

		/// <summary>
		/// True for experience that comes from a kill: NPC kills (companion kills are
		/// booked to the owner) and player kills (real players and autonomous bots).
		/// Quests, missions, tasks, praying, GM grants and zone bonuses do not count.
		/// </summary>
		public static bool IsKillExperience(eXPSource source)
		{
			return source is eXPSource.NPC or eXPSource.Player;
		}

		/// <summary>
		/// The amount of experience a kill is worth to an artifact before its own rate:
		/// base + camp + group + outpost bonus, as in DOL. The arguments are the ones
		/// GamePlayer.GainExperience received, before the server XP rates, so the
		/// <c>xp_rate</c> property does not speed up artifacts; <c>artifact_xp_rate</c> does.
		/// </summary>
		public static long GetKillExperience(GainedExperienceEventArgs xpArgs)
		{
			if (xpArgs == null || !IsKillExperience(xpArgs.XPSource))
				return 0;

			long amount = xpArgs.ExpBase + xpArgs.ExpCampBonus + xpArgs.ExpGroupBonus + xpArgs.ExpOutpostBonus;
			return Math.Max(0, amount);
		}

		/// <summary>
		/// Raised by GamePlayer.GainExperience (GamePlayerEvent.GainedExperience). Every
		/// equipped artifact gains experience.
		/// </summary>
		public static void PlayerGainedExperience(DOLEvent e, object sender, EventArgs args)
		{
			if (sender is not GamePlayer player || args is not GainedExperienceEventArgs xpArgs)
				return;

			try
			{
				long xpAmount = GetKillExperience(xpArgs);

				if (xpAmount <= 0 || player.IsPraying)
					return;

				foreach (InventoryArtifact artifact in GetEquippedArtifacts(player))
					ArtifactGainedExperience(player, artifact, xpAmount);
			}
			catch (Exception ex)
			{
				// An artifact is never worth losing a kill reward over.
				if (log.IsErrorEnabled)
					log.Error($"Artifact experience for {player.Name} failed", ex);
			}
		}

		/// <summary>
		/// Every artifact the player has equipped (paperdoll slots, including the weapon
		/// slots that are not the active weapon, as in DOL).
		/// </summary>
		public static List<InventoryArtifact> GetEquippedArtifacts(GamePlayer player)
		{
			ICollection<DbInventoryItem> equipped = player?.Inventory?.EquippedItems;
			return equipped == null ? [] : equipped.OfType<InventoryArtifact>().ToList();
		}

		/// <summary>
		/// Artifact experience for one kill reward: <c>xp * Artifact.XPRate / artifact_xp_rate</c>
		/// (our rows: XPRate 5, property 350, so one seventieth), plus the guild artifact
		/// buff. Returns 0 when the property or the artifact rate is 0 or below.
		/// </summary>
		public static long CalculateArtifactExperience(GamePlayer player, InventoryArtifact item, long xpAmount)
		{
			DbArtifact artifact = GetArtifact(item?.ArtifactID);
			int serverRate = ServerProperties.Properties.ARTIFACT_XP_RATE;

			if (artifact == null || xpAmount <= 0 || artifact.XPRate <= 0 || serverRate <= 0)
				return 0;

			if (player?.Guild != null && player.Guild.BonusType == Guild.eBonusType.ArtifactXP)
				xpAmount += (long) (xpAmount * ServerProperties.Properties.GUILD_BUFF_ARTIFACT_XP * .01);

			return xpAmount * artifact.XPRate / serverRate;
		}

		/// <summary>
		/// Called when an artifact has gained experience. The level follows the stored
		/// experience; InventoryArtifact applies newly unlocked bonuses to the wearer.
		/// </summary>
		public static void ArtifactGainedExperience(GamePlayer player, InventoryArtifact item, long xpAmount)
		{
			if (player == null || item == null)
				return;

			long maxExperience = m_xpForLevel[MAX_LEVEL];

			if (item.Experience >= maxExperience)
				return;

			long gain = CalculateArtifactExperience(player, item, xpAmount);

			if (gain <= 0)
				return;

			if (player.Guild != null && player.Guild.BonusType == Guild.eBonusType.ArtifactXP)
				player.Out.SendMessage($"Your {item.Name} gains additional experience due to your guild's buff!", eChatType.CT_Important, eChatLoc.CL_SystemWindow);

			int oldLevel = item.ArtifactLevel;
			item.Experience = Math.Min(maxExperience, item.Experience + gain);
			player.Out.SendMessage($"Your {item.Name} has gained experience.", eChatType.CT_Important, eChatLoc.CL_SystemWindow);

			for (int level = oldLevel + 1; level <= item.ArtifactLevel; level++)
			{
				player.Out.SendMessage($"Your {item.Name} has gained a level!", eChatType.CT_Important, eChatLoc.CL_SystemWindow);

				if (item.UnlocksAbilityAtLevel(level))
					player.Out.SendMessage($"Your {item.Name} has gained a new ability!", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
			}
		}

		#endregion

		#region Artifact versions

		/// <summary>
		/// Get a list of all versions for this artifact.
		/// </summary>
		private static List<DbArtifactXItem> GetArtifactVersions(string artifactID, eRealm realm)
		{
			if (string.IsNullOrEmpty(artifactID) || !m_catalog.Versions.TryGetValue(artifactID, out List<DbArtifactXItem> allVersions))
				return [];

			return allVersions.Where(v => v.Realm == 0 || v.Realm == (int) realm).ToList();
		}

		/// <summary>
		/// All item templates of this artifact that this class may use, keyed by
		/// ArtifactXItem.Version (ours: "0".."15"; DOL: "dmg;wep;stat").
		/// </summary>
		public static Dictionary<string, DbItemTemplate> GetArtifactVersions(string artifactID, eCharacterClass charClass, eRealm realm)
		{
			if (artifactID == null)
				return null;

			Dictionary<string, DbItemTemplate> classVersions = new();

			foreach (DbArtifactXItem version in GetArtifactVersions(artifactID, realm))
			{
				DbItemTemplate itemTemplate = GameServer.Database.FindObjectByKey<DbItemTemplate>(version.ItemID);

				if (itemTemplate == null)
				{
					if (log.IsWarnEnabled)
						log.Warn($"Artifact item template '{version.ItemID}' is missing");

					continue;
				}

				foreach (string classID in Util.SplitCSV(itemTemplate.AllowedClasses ?? string.Empty, true))
				{
					if (int.TryParse(classID, out int id) && id == (int) charClass)
					{
						classVersions.TryAdd(version.Version ?? string.Empty, itemTemplate);
						break;
					}
				}
			}

			return classVersions;
		}

		#endregion

		#region Encounters & quests

		/// <summary>
		/// Get the quest type from the quest type string, or null. Our rows carry template
		/// names here (EncounterID = QuestID = "Alvarus_Leggings"); they resolve to no type.
		/// </summary>
		public static Type GetQuestType(string questTypeString)
		{
			if (string.IsNullOrWhiteSpace(questTypeString))
				return null;

			try
			{
				foreach (Assembly asm in ScriptMgr.Scripts)
				{
					Type scriptType = asm.GetType(questTypeString, false);

					if (scriptType != null)
						return typeof(AbstractQuest).IsAssignableFrom(scriptType) ? scriptType : null;
				}

				Type questType = typeof(GameServer).Assembly.GetType(questTypeString, false);
				return questType != null && typeof(AbstractQuest).IsAssignableFrom(questType) ? questType : null;
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>
		/// Get the quest type for the encounter from the artifact ID, or null.
		/// </summary>
		public static Type GetEncounterType(string artifactID)
		{
			return GetQuestType(GetArtifact(artifactID)?.EncounterID);
		}

		/// <summary>
		/// Grant bounty point credit for an artifact. Accepts the artifact's Credit value
		/// or the credit item ID (<c>lower(Credit) + "_credit"</c>, as our 82 credit items
		/// are named).
		/// </summary>
		public static bool GrantArtifactBountyCredit(GamePlayer player, string bountyCredit)
		{
			string artifactID = GetArtifactIDFromCredit(bountyCredit);
			return artifactID != null && GrantArtifactCredit(player, artifactID);
		}

		/// <summary>
		/// The artifact whose Credit matches <paramref name="credit"/> (ignoring case and an
		/// optional "_credit" suffix), or null.
		/// </summary>
		public static string GetArtifactIDFromCredit(string credit)
		{
			if (string.IsNullOrEmpty(credit))
				return null;

			const string suffix = "_credit";
			string bare = credit.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? credit[..^suffix.Length] : credit;

			foreach (DbArtifact artifact in m_catalog.Artifacts.Values)
			{
				if (string.Equals(artifact.Credit, credit, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(artifact.Credit, bare, StringComparison.OrdinalIgnoreCase))
					return artifact.ArtifactID;
			}

			return null;
		}

		/// <summary>
		/// Grant encounter credit for an artifact: the step a player must reach before a
		/// scholar will take the book (WP3.1b) and what an encounter (WP3.3) awards.
		/// Refused when the artifact is unknown, no version fits the player's class and
		/// realm, the player already holds the credit, or a DOL-style artifact quest type
		/// named in QuestID is already finished.
		/// </summary>
		public static bool GrantArtifactCredit(GamePlayer player, string artifactID)
		{
			if (player == null || GetArtifact(artifactID) == null)
				return false;

			if (!player.CanReceiveArtifact(artifactID))
				return false;

			if (HasEncounterCredit(player, artifactID) || HasFinishedArtifactQuest(player, artifactID))
				return false;

			return ArtifactEncounter.GrantCredit(player, artifactID, GetEncounterType(artifactID)) != null;
		}

		/// <summary>
		/// Whether the player holds encounter credit for this artifact: a finished
		/// ArtifactEncounter carrying the artifact ID, or a finished quest of the DOL-style
		/// encounter type named in EncounterID.
		/// </summary>
		public static bool HasEncounterCredit(GamePlayer player, string artifactID)
		{
			if (player == null || string.IsNullOrEmpty(artifactID))
				return false;

			foreach (AbstractQuest quest in player.GetFinishedQuests())
			{
				if (quest is ArtifactEncounter encounter && encounter.ArtifactID == artifactID)
					return true;
			}

			Type encounterType = GetEncounterType(artifactID);
			return encounterType != null && !typeof(ArtifactEncounter).IsAssignableFrom(encounterType) && player.HasFinishedQuest(encounterType) > 0;
		}

		/// <summary>
		/// Removes the player's encounter credit for this artifact (for example when a
		/// scholar hands it over, WP3.1b). Returns true if a credit was removed.
		/// </summary>
		public static bool ConsumeEncounterCredit(GamePlayer player, string artifactID)
		{
			if (player == null || string.IsNullOrEmpty(artifactID))
				return false;

			bool removed = false;

			player.RemoveFinishedQuests(quest =>
			{
				if (removed || quest is not ArtifactEncounter encounter || encounter.ArtifactID != artifactID)
					return false;

				removed = true;
				return true;
			});

			if (!removed)
			{
				Type encounterType = GetEncounterType(artifactID);

				if (encounterType != null && !typeof(ArtifactEncounter).IsAssignableFrom(encounterType))
					removed = player.RemoveEncounterCredit(encounterType);
			}

			return removed;
		}

		/// <summary>
		/// True once a scholar has awakened this artifact for the player: a finished
		/// <see cref="ArtifactQuest"/> carrying the artifact ID (WP3.1b), or, for DOL
		/// compatibility, a finished quest of the type named in QuestID.
		/// </summary>
		public static bool HasFinishedArtifactQuest(GamePlayer player, string artifactID)
		{
			if (player == null || string.IsNullOrEmpty(artifactID))
				return false;

			foreach (AbstractQuest quest in player.GetFinishedQuests())
			{
				if (quest is ArtifactQuest awakened && awakened.ArtifactID == artifactID)
					return true;
			}

			Type questType = GetQuestType(GetArtifact(artifactID)?.QuestID);
			return questType != null && !typeof(ArtifactQuest).IsAssignableFrom(questType) && player.HasFinishedQuest(questType) > 0;
		}

		#endregion

		#region Scrolls & books

		/// <summary>
		/// Whether the item has the shape of an artifact scroll or book: a backpack item
		/// (Item_Type 40), generic (our scroll templates, Object_Type 0) or magical (DOL and
		/// gaheris migration 37 use 41). The name decides which page it is.
		/// </summary>
		private static bool IsPaper(DbInventoryItem item)
		{
			return item != null &&
				item.Item_Type == (int) eInventorySlot.FirstBackpack &&
				(item.Object_Type == (int) eObjectType.Magical || item.Object_Type == (int) eObjectType.GenericItem);
		}

		/// <summary>
		/// Find the matching artifact for this book.
		/// </summary>
		public static string GetArtifactID(string bookID)
		{
			if (string.IsNullOrEmpty(bookID))
				return null;

			return m_catalog.Artifacts.Values.FirstOrDefault(a => a.BookID == bookID)?.ArtifactID;
		}

		/// <summary>
		/// Check whether these 2 items can be combined.
		/// </summary>
		public static bool CanCombine(DbInventoryItem item1, DbInventoryItem item2)
		{
			string artifactID1 = null;
			Book pageNumbers1 = GetPageNumbers(item1, ref artifactID1);
			if (pageNumbers1 == Book.NoPage || pageNumbers1 == Book.AllPages)
				return false;

			string artifactID2 = null;
			Book pageNumbers2 = GetPageNumbers(item2, ref artifactID2);
			if (pageNumbers2 == Book.NoPage || pageNumbers2 == Book.AllPages)
				return false;

			return artifactID1 == artifactID2 && (pageNumbers1 & pageNumbers2) == Book.NoPage;
		}

		/// <summary>
		/// Check which scroll pages are in this item. <paramref name="artifactID"/> is set
		/// only on a match (upstream left it at the last artifact scanned). Artifacts without
		/// a scroll set never match.
		/// </summary>
		public static Book GetPageNumbers(DbInventoryItem item, ref string artifactID)
		{
			if (!IsPaper(item) || string.IsNullOrEmpty(item.Name))
				return Book.NoPage;

			string name = item.Name;

			foreach (DbArtifact artifact in m_catalog.Artifacts.Values)
			{
				if (string.IsNullOrWhiteSpace(artifact.BookID))
					continue;

				Book pages = Book.NoPage;

				if (name == artifact.Scroll1)
					pages = Book.Page1;
				else if (name == artifact.Scroll2)
					pages = Book.Page2;
				else if (name == artifact.Scroll3)
					pages = Book.Page3;
				else if (name == artifact.Scroll12)
					pages = Book.Page1 | Book.Page2;
				else if (name == artifact.Scroll13)
					pages = Book.Page1 | Book.Page3;
				else if (name == artifact.Scroll23)
					pages = Book.Page2 | Book.Page3;
				else if (name == artifact.BookID)
					pages = Book.AllPages;

				if (pages != Book.NoPage)
				{
					artifactID = artifact.ArtifactID;
					return pages;
				}
			}

			return Book.NoPage;
		}

		/// <summary>
		/// Find all artifacts that this player carries.
		/// </summary>
		public static List<string> GetArtifacts(GamePlayer player)
		{
			List<string> artifacts = [];

			if (player?.Inventory == null)
				return artifacts;

			foreach (DbInventoryItem item in player.Inventory.AllItems)
			{
				string artifactID = GetArtifactIDFromItemID(item.Id_nb);

				if (artifactID != null)
					artifacts.Add(artifactID);
			}

			return artifacts;
		}

		/// <summary>
		/// Get the artifact for this scroll/book item.
		/// </summary>
		public static DbArtifact GetArtifact(DbInventoryItem item)
		{
			string artifactID = null;
			return GetPageNumbers(item, ref artifactID) == Book.NoPage ? null : GetArtifact(artifactID);
		}

		/// <summary>
		/// Whether or not the player has the complete book for this artifact in the backpack.
		/// </summary>
		public static bool HasBook(GamePlayer player, string artifactID)
		{
			string bookID = GetArtifact(artifactID)?.BookID;

			if (player?.Inventory == null || string.IsNullOrWhiteSpace(bookID))
				return false;

			foreach (DbInventoryItem item in player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
			{
				if (IsPaper(item) && item.Name == bookID)
					return true;
			}

			return false;
		}

		/// <summary>
		/// Whether or not the item is an artifact scroll.
		/// </summary>
		public static bool IsArtifactScroll(DbInventoryItem item)
		{
			string artifactID = null;
			Book pageNumbers = GetPageNumbers(item, ref artifactID);
			return pageNumbers != Book.NoPage && pageNumbers != Book.AllPages;
		}

		/// <summary>
		/// Combine 2 scrolls into the pair or the book. The new item is a unique copy of the
		/// generic template "artifact_scroll" when the save has one (DOL, gaheris migration 37),
		/// else of the first scroll's own template, renamed after the Artifact row.
		/// </summary>
		public static WorldInventoryItem CombineScrolls(DbInventoryItem scroll1, DbInventoryItem scroll2, ref bool combinesToBook)
		{
			if (!CanCombine(scroll1, scroll2))
				return null;

			string artifactID = null;
			Book combinedPages = GetPageNumbers(scroll1, ref artifactID) | GetPageNumbers(scroll2, ref artifactID);

			combinesToBook = combinedPages == Book.AllPages;
			return CreatePages(artifactID, combinedPages, FindPageTemplate(artifactID, scroll1, scroll2));
		}

		/// <summary>
		/// A real (non-unique) scroll template of the artifact to build pairs and books from,
		/// so a book can always be rebuilt (a version choice that is abandoned gives it back):
		/// the template of the first item that has one, else the page template a combined
		/// unique was copied from ("page_template#id"), else the template named like Scroll1-3.
		/// Null if none exists.
		/// </summary>
		public static DbItemTemplate FindPageTemplate(string artifactID, params DbInventoryItem[] items)
		{
			foreach (DbInventoryItem item in items ?? [])
			{
				if (item?.Template != null && item.Template is not DbItemUnique)
					return item.Template;
			}

			try
			{
				foreach (DbInventoryItem item in items ?? [])
				{
					string id = item?.Id_nb;
					int separator = id?.IndexOf(DbItemUnique.UNIQUE_SEPARATOR, StringComparison.Ordinal) ?? -1;

					if (separator > 0)
					{
						DbItemTemplate template = GameServer.Database?.FindObjectByKey<DbItemTemplate>(id[..separator]);

						if (template != null && template is not DbItemUnique)
							return template;
					}
				}

				DbArtifact artifact = GetArtifact(artifactID);

				if (artifact == null || string.IsNullOrWhiteSpace(artifact.BookID))
					return null;

				foreach (string name in new[] { artifact.Scroll1, artifact.Scroll2, artifact.Scroll3 })
				{
					if (string.IsNullOrWhiteSpace(name))
						continue;

					DbItemTemplate template = GameServer.Database?.SelectObjects<DbItemTemplate>(DB.Column("Name").IsEqualTo(name))?
						.FirstOrDefault(t => t.Item_Type == (int) eInventorySlot.FirstBackpack && !t.IsStackable);

					if (template != null)
						return template;
				}
			}
			catch (Exception ex)
			{
				if (log.IsWarnEnabled)
					log.Warn($"Scroll template lookup for artifact {artifactID} failed", ex);
			}

			return null;
		}

		/// <summary>
		/// Backpack drop of one scroll onto another (GamePlayerInventory.CombineItems): both
		/// scrolls of one artifact, no page twice. Replaces them with the combined item in the
		/// target slot and tells the player. Returns false, touching nothing, for anything
		/// else, so the normal move/swap goes on.
		/// </summary>
		public static bool TryCombineScrolls(GamePlayer player, DbInventoryItem fromItem, DbInventoryItem toItem)
		{
			IGameInventory inventory = player?.Inventory;

			if (inventory == null || fromItem == null || toItem == null || fromItem == toItem)
				return false;

			if (!IsBackpackSlot(fromItem.SlotPosition) || !IsBackpackSlot(toItem.SlotPosition) || !CanCombine(fromItem, toItem))
				return false;

			bool combinesToBook = false;
			WorldInventoryItem combined = CombineScrolls(fromItem, toItem, ref combinesToBook);

			if (combined?.Item == null)
				return false;

			var targetSlot = (eInventorySlot) toItem.SlotPosition;
			var sourceSlot = (eInventorySlot) fromItem.SlotPosition;

			lock (inventory.Lock)
			{
				if (!inventory.RemoveItem(fromItem))
					return false;

				if (!inventory.RemoveItem(toItem))
				{
					inventory.AddItem(sourceSlot, fromItem);
					return false;
				}

				if (!inventory.AddItem(targetSlot, combined.Item))
				{
					// Should not happen: the target slot was just freed. Give both scrolls back.
					inventory.AddItem(sourceSlot, fromItem);
					inventory.AddItem(targetSlot, toItem);
					return false;
				}
			}

			InventoryLogging.LogInventoryAction(player, "(ARTIFACT;scrolls)", eInventoryActionType.Other, fromItem.Template, fromItem.Count);
			InventoryLogging.LogInventoryAction(player, "(ARTIFACT;scrolls)", eInventoryActionType.Other, toItem.Template, toItem.Count);
			InventoryLogging.LogInventoryAction("(ARTIFACT;scrolls)", player, eInventoryActionType.Other, combined.Item.Template, 1);

			DbArtifact artifact = GetArtifact(combined.Item);
			string artifactName = GetArtifactName(artifact?.ArtifactID);
			string message = combinesToBook
				? ReadableText(artifact?.MessageCombineBook,
					$"The pages fall into place: you now hold {combined.Item.Name}. A scholar in Atlantis can awaken {artifactName} with it.")
				: ReadableText(artifact?.MessageCombineScrolls,
					$"You combine the pages into {combined.Item.Name}. One part of the story of {artifactName} is still missing.");

			player.Out?.SendMessage(message, eChatType.CT_Important, eChatLoc.CL_SystemWindow);
			return true;
		}

		private static bool IsBackpackSlot(int slot)
		{
			return slot >= (int) eInventorySlot.FirstBackpack && slot <= (int) eInventorySlot.LastBackpack;
		}

		/// <summary>
		/// Create a scroll or book containing the given page numbers, from the generic template
		/// "artifact_scroll" if the save has one, else from <paramref name="pageTemplate"/>
		/// (a scroll page of the same artifact). Null if neither exists.
		/// </summary>
		public static WorldInventoryItem CreatePages(string artifactID, Book pageNumbers, DbItemTemplate pageTemplate = null)
		{
			DbArtifact artifact = GetArtifact(artifactID);

			if (artifact == null || pageNumbers == Book.NoPage)
				return null;

			DbItemTemplate template = null;

			try
			{
				template = GameServer.Database?.FindObjectByKey<DbItemTemplate>("artifact_scroll");
			}
			catch (Exception)
			{
				// No database: use the page template.
			}

			template ??= pageTemplate;

			if (template == null || template.IsStackable)
				return null;

			WorldInventoryItem scroll = WorldInventoryItem.CreateUniqueFromTemplate(template);

			if (scroll == null)
				return null;

			string scrollTitle = null;
			int scrollModel = 499;
			short gold = 4;

			switch (pageNumbers)
			{
				case Book.Page1:
					scrollTitle = artifact.Scroll1;
					scrollModel = artifact.ScrollModel1;
					gold = 2;
					break;
				case Book.Page2:
					scrollTitle = artifact.Scroll2;
					scrollModel = artifact.ScrollModel1;
					gold = 3;
					break;
				case Book.Page3:
					scrollTitle = artifact.Scroll3;
					scrollModel = artifact.ScrollModel1;
					break;
				case Book.Page1 | Book.Page2:
					scrollTitle = artifact.Scroll12;
					scrollModel = artifact.ScrollModel2;
					break;
				case Book.Page1 | Book.Page3:
					scrollTitle = artifact.Scroll13;
					scrollModel = artifact.ScrollModel2;
					break;
				case Book.Page2 | Book.Page3:
					scrollTitle = artifact.Scroll23;
					scrollModel = artifact.ScrollModel2;
					break;
				case Book.AllPages:
					scrollTitle = artifact.BookID;
					scrollModel = artifact.BookModel;
					gold = 5;
					break;
			}

			if (string.IsNullOrWhiteSpace(scrollTitle))
				return null;

			if (scrollModel <= 0)
				scrollModel = pageNumbers == Book.AllPages ? 500 : 499;

			scroll.Name = scrollTitle;
			scroll.Item.Name = scrollTitle;
			scroll.Model = (ushort) scrollModel;
			scroll.Item.Model = (ushort) scrollModel;

			// Correct for possible errors in generic scroll template (artifact_scroll)
			scroll.Item.Price = Money.GetMoney(0, 0, gold, 0, 0);
			scroll.Item.IsDropable = true;
			scroll.Item.IsPickable = true;
			scroll.Item.IsTradable = true;

			return scroll;
		}

		/// <summary>
		/// Create a scroll from a particular book.
		/// </summary>
		/// <param name="artifactID">The artifact's ID.</param>
		/// <param name="pageNumber">Scroll page number (1-3).</param>
		/// <returns>An item that can be picked up by a player (or null).</returns>
		public static WorldInventoryItem CreateScroll(string artifactID, int pageNumber)
		{
			return pageNumber switch
			{
				1 => CreatePages(artifactID, Book.Page1),
				2 => CreatePages(artifactID, Book.Page2),
				3 => CreatePages(artifactID, Book.Page3),
				_ => null,
			};
		}

		#endregion
	}
}
