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
using System.Collections.Generic;
using System.Reflection;
using DOL.Database;

namespace DOL.GS
{
	/// <summary>
	/// An artifact inside an inventory (author Aredhel, DOLSharp; TOA_PLAN WP3.1).
	///
	/// Item templates of artifacts carry ClassType <c>DOL.GS.InventoryArtifact</c>
	/// (tools/toa/sql/050-artifact-item-classtype.sql); GameInventoryItem.Create then
	/// builds this class on login and on every new copy.
	///
	/// Offline DAoC: upstream cloned the template and zeroed the locked bonuses in the
	/// clone. Our inventory row saves its ITWrapper relation, so a dirty, unpersisted
	/// template clone would be inserted over the shared ItemTemplate row (primary key
	/// Id_nb). The locks live in the getters instead: a bonus slot (Bonus1-10, SpellID,
	/// SpellID1, ProcSpellID, ProcSpellID1) reads 0 until the artifact reaches the
	/// slot's ArtifactBonus.Level. The level follows the stored Experience, so a reload
	/// keeps it, and a level change while the artifact is worn applies the newly
	/// unlocked stats to the wearer at once.
	/// </summary>
	public class InventoryArtifact : GameInventoryItem
	{
		private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

		private string m_artifactID;
		private int m_artifactLevel;
		private int[] m_levelRequirements;

		/// <summary>
		/// The player whose ItemBonus currently holds this item's bonuses: set between
		/// GamePlayer.OnItemEquipped and OnItemUnequipped (the active weapon only, for
		/// weapon slots).
		/// </summary>
		private GamePlayer m_bonusHolder;

		/// <summary>
		/// Create a new inventory artifact from an item template.
		/// </summary>
		public InventoryArtifact(DbItemTemplate template)
			: base(template)
		{
			Initialize();
		}

		/// <summary>
		/// Create a new inventory artifact from an existing inventory item (login, vault).
		/// </summary>
		public InventoryArtifact(DbInventoryItem item)
			: base(item)
		{
			if (item?.Template == null && log.IsErrorEnabled)
				log.Error($"Artifact: error loading artifact for owner {item?.OwnerID} holding item {item?.ITemplate_Id}");

			Initialize();
		}

		private void Initialize()
		{
			m_artifactID = ArtifactMgr.GetArtifactIDFromItemID(base.Id_nb);
			m_levelRequirements = ArtifactMgr.GetLevelRequirements(m_artifactID);
			m_artifactLevel = ArtifactMgr.GetLevelForExperience(base.Experience);
		}

		/// <summary>
		/// The ID of this artifact (Artifact.ArtifactID), or null if the item is in no
		/// ArtifactXItem row; then it behaves like a plain item.
		/// </summary>
		public string ArtifactID => m_artifactID;

		/// <summary>
		/// The level of this artifact (0-10), derived from <see cref="Experience"/>.
		/// </summary>
		public int ArtifactLevel => m_artifactLevel;

		/// <summary>
		/// Artifact experience, saved in the inventory row. Setting it moves the level and
		/// applies or removes the bonuses that change with it.
		/// </summary>
		public override long Experience
		{
			get => base.Experience;
			set
			{
				base.Experience = value;
				RefreshLevel();
			}
		}

		/// <summary>The level requirement of a bonus slot.</summary>
		public int GetLevelRequirement(DbArtifactBonus.ID slot)
		{
			int index = (int) slot;
			return m_levelRequirements == null || index < 0 || index >= m_levelRequirements.Length ? 0 : m_levelRequirements[index];
		}

		/// <summary>True if the bonus slot is active at the current level.</summary>
		public bool IsUnlocked(DbArtifactBonus.ID slot)
		{
			return m_levelRequirements == null || GetLevelRequirement(slot) <= m_artifactLevel;
		}

		/// <summary>True if a slot that carries a bonus or spell unlocks at exactly this level.</summary>
		public bool UnlocksAbilityAtLevel(int level)
		{
			if (level <= 0)
				return false;

			for (DbArtifactBonus.ID slot = DbArtifactBonus.ID.Min; slot <= DbArtifactBonus.ID.Max; ++slot)
			{
				if (GetLevelRequirement(slot) == level && SlotCarriesSomething(slot))
					return true;
			}

			return false;
		}

		/// <summary>
		/// The next level at which a slot that carries something unlocks, or 10.
		/// </summary>
		public int GainsNewAbilityAtLevel()
		{
			int next = ArtifactMgr.MAX_LEVEL;

			for (DbArtifactBonus.ID slot = DbArtifactBonus.ID.Min; slot <= DbArtifactBonus.ID.Max; ++slot)
			{
				int requirement = GetLevelRequirement(slot);

				if (requirement > m_artifactLevel && requirement < next && SlotCarriesSomething(slot))
					next = requirement;
			}

			return next;
		}

		private bool SlotCarriesSomething(DbArtifactBonus.ID slot)
		{
			DbItemTemplate template = Template;

			if (slot <= DbArtifactBonus.ID.MaxStat)
				return template.GetBonusType(slot) != 0 && template.GetBonusAmount(slot) != 0;

			return template.GetBonusType(slot) != 0;
		}

		/// <summary>
		/// Re-derives the level from the stored experience. If the artifact is worn, the
		/// stat slots that unlock (or lock again, after a GM lowers the experience) are
		/// pushed into the wearer's item bonuses, exactly mirroring what equip/unequip add
		/// and remove through the gated getters.
		/// </summary>
		private void RefreshLevel()
		{
			if (m_levelRequirements == null)
				return;

			int oldLevel = m_artifactLevel;
			int newLevel = ArtifactMgr.GetLevelForExperience(base.Experience);

			if (oldLevel == newLevel)
				return;

			List<(eProperty Property, int Delta)> changes = [];
			DbItemTemplate template = Template;

			for (DbArtifactBonus.ID slot = DbArtifactBonus.ID.MinStat; slot <= DbArtifactBonus.ID.MaxStat; ++slot)
			{
				int requirement = GetLevelRequirement(slot);
				bool wasActive = requirement <= oldLevel;
				bool isActive = requirement <= newLevel;

				if (wasActive == isActive)
					continue;

				int type = template.GetBonusType(slot);
				int amount = template.GetBonusAmount(slot);

				if (type != 0 && amount != 0)
					changes.Add(((eProperty) type, isActive ? amount : -amount));
			}

			m_artifactLevel = newLevel;

			GamePlayer wearer = m_bonusHolder;

			if (wearer == null)
				return;

			foreach ((eProperty property, int delta) in changes)
				wearer.ApplyItemBonusChange(property, delta);
		}

		public override void OnEquipped(GamePlayer player)
		{
			base.OnEquipped(player);
			m_bonusHolder = player;
		}

		public override void OnUnEquipped(GamePlayer player)
		{
			base.OnUnEquipped(player);

			if (m_bonusHolder == player)
				m_bonusHolder = null;
		}

		/// <summary>
		/// Repair cost for this artifact in the current state (DOL: one hundredth of the
		/// missing condition).
		/// </summary>
		public override long RepairCost => (MaxCondition - Condition) / 100;

		#region Gated bonus slots

		private int Gate(DbArtifactBonus.ID slot, int value) => IsUnlocked(slot) ? value : 0;

		public override int Bonus1 { get => Gate(DbArtifactBonus.ID.Bonus1, base.Bonus1); set => base.Bonus1 = value; }
		public override int Bonus2 { get => Gate(DbArtifactBonus.ID.Bonus2, base.Bonus2); set => base.Bonus2 = value; }
		public override int Bonus3 { get => Gate(DbArtifactBonus.ID.Bonus3, base.Bonus3); set => base.Bonus3 = value; }
		public override int Bonus4 { get => Gate(DbArtifactBonus.ID.Bonus4, base.Bonus4); set => base.Bonus4 = value; }
		public override int Bonus5 { get => Gate(DbArtifactBonus.ID.Bonus5, base.Bonus5); set => base.Bonus5 = value; }
		public override int Bonus6 { get => Gate(DbArtifactBonus.ID.Bonus6, base.Bonus6); set => base.Bonus6 = value; }
		public override int Bonus7 { get => Gate(DbArtifactBonus.ID.Bonus7, base.Bonus7); set => base.Bonus7 = value; }
		public override int Bonus8 { get => Gate(DbArtifactBonus.ID.Bonus8, base.Bonus8); set => base.Bonus8 = value; }
		public override int Bonus9 { get => Gate(DbArtifactBonus.ID.Bonus9, base.Bonus9); set => base.Bonus9 = value; }
		public override int Bonus10 { get => Gate(DbArtifactBonus.ID.Bonus10, base.Bonus10); set => base.Bonus10 = value; }

		public override int Bonus1Type { get => Gate(DbArtifactBonus.ID.Bonus1, base.Bonus1Type); set => base.Bonus1Type = value; }
		public override int Bonus2Type { get => Gate(DbArtifactBonus.ID.Bonus2, base.Bonus2Type); set => base.Bonus2Type = value; }
		public override int Bonus3Type { get => Gate(DbArtifactBonus.ID.Bonus3, base.Bonus3Type); set => base.Bonus3Type = value; }
		public override int Bonus4Type { get => Gate(DbArtifactBonus.ID.Bonus4, base.Bonus4Type); set => base.Bonus4Type = value; }
		public override int Bonus5Type { get => Gate(DbArtifactBonus.ID.Bonus5, base.Bonus5Type); set => base.Bonus5Type = value; }
		public override int Bonus6Type { get => Gate(DbArtifactBonus.ID.Bonus6, base.Bonus6Type); set => base.Bonus6Type = value; }
		public override int Bonus7Type { get => Gate(DbArtifactBonus.ID.Bonus7, base.Bonus7Type); set => base.Bonus7Type = value; }
		public override int Bonus8Type { get => Gate(DbArtifactBonus.ID.Bonus8, base.Bonus8Type); set => base.Bonus8Type = value; }
		public override int Bonus9Type { get => Gate(DbArtifactBonus.ID.Bonus9, base.Bonus9Type); set => base.Bonus9Type = value; }
		public override int Bonus10Type { get => Gate(DbArtifactBonus.ID.Bonus10, base.Bonus10Type); set => base.Bonus10Type = value; }

		public override int SpellID { get => Gate(DbArtifactBonus.ID.Spell, base.SpellID); set => base.SpellID = value; }
		public override int SpellID1 { get => Gate(DbArtifactBonus.ID.Spell1, base.SpellID1); set => base.SpellID1 = value; }
		public override int ProcSpellID { get => Gate(DbArtifactBonus.ID.ProcSpell, base.ProcSpellID); set => base.ProcSpellID = value; }
		public override int ProcSpellID1 { get => Gate(DbArtifactBonus.ID.ProcSpell1, base.ProcSpellID1); set => base.ProcSpellID1 = value; }

		#endregion

		#region Delve

		/// <summary>
		/// Artifact delve: level and progress, the abilities still locked, then the normal
		/// item delve (which shows only the unlocked bonuses and spells).
		/// </summary>
		public override void Delve(List<string> delve, GamePlayer player)
		{
			if (player == null)
				return;

			if (m_artifactID != null)
			{
				if (m_artifactLevel < ArtifactMgr.MAX_LEVEL)
				{
					delve.Add($"Artifact (Current level: {m_artifactLevel})");
					delve.Add($"- {ArtifactMgr.GetXPGainedForLevel(this)}% exp earned towards level {m_artifactLevel + 1}");

					int next = GainsNewAbilityAtLevel();

					if (next > m_artifactLevel)
						delve.Add($"- Artifact will gain new abilities at level {next}");

					delve.Add($"(Earns exp: {ArtifactMgr.GetEarnsXP(this)})");
				}
				else
				{
					delve.Add("Artifact:");
					delve.Add("Current level: 10");
				}

				WriteLockedAbilities(delve);
				delve.Add(" ");
			}

			base.Delve(delve, player);
		}

		private void WriteLockedAbilities(List<string> delve)
		{
			List<(int Level, string Text)> locked = [];
			DbItemTemplate template = Template;

			for (DbArtifactBonus.ID slot = DbArtifactBonus.ID.Min; slot <= DbArtifactBonus.ID.Max; ++slot)
			{
				int requirement = GetLevelRequirement(slot);

				if (requirement <= m_artifactLevel || !SlotCarriesSomething(slot))
					continue;

				if (slot <= DbArtifactBonus.ID.MaxStat)
				{
					int amount = template.GetBonusAmount(slot);
					string name = SkillBase.GetPropertyName((eProperty) template.GetBonusType(slot));
					locked.Add((requirement, $"- [L{requirement}]: {name}: {amount.ToString("+0;-0;0")}"));
				}
				else
				{
					int spellID = template.GetBonusType(slot);
					string spellName = SkillBase.GetSpellByID(spellID)?.Name ?? $"spell {spellID}";
					string kind = slot is DbArtifactBonus.ID.ProcSpell or DbArtifactBonus.ID.ProcSpell1 ? "Proc" : "Magical ability";
					locked.Add((requirement, $"- [L{requirement}]: {kind}: {spellName}"));
				}
			}

			if (locked.Count == 0)
				return;

			locked.Sort((a, b) => a.Level.CompareTo(b.Level));
			delve.Add(" ");
			delve.Add("Locked abilities:");

			foreach ((_, string text) in locked)
				delve.Add(text);
		}

		/// <summary>
		/// GM delve: artifact state, then the stock technical block.
		/// </summary>
		public override void WriteTechnicalInfo(List<string> delve, GameClient client)
		{
			delve.Add(" ");
			delve.Add("--- Artifact information ---");
			delve.Add("  Artifact ID: " + (m_artifactID ?? "(none)"));
			delve.Add("        Level: " + m_artifactLevel);
			delve.Add("   Experience: " + Experience);
			base.WriteTechnicalInfo(delve, client);
		}

		#endregion
	}
}
