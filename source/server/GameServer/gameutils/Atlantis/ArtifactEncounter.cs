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
using DOL.Database;
using DOL.Events;

namespace DOL.GS.Quests.Atlantis
{
	/// <summary>
	/// Encounter credit for one artifact (author Aredhel, DOLSharp). A credit is a quest
	/// row that never shows in the journal: it lives only in the player's finished list
	/// and in the Quest table with Step -1.
	///
	/// Offline DAoC: one class serves every artifact; the artifact ID is a custom
	/// property, so our placeholder rows (EncounterID = template name) need no subclass.
	/// DOL-style subclasses per artifact still work: name one in Artifact.EncounterID.
	///
	/// Step -1 matters. DOL marked finished quests -1 and GamePlayer.RemoveEncounterCredit
	/// consumes only those; our AbstractQuest.FinishQuest writes -2, sends a journal
	/// remove for index 0 (the encounter was never in the journal) and refuses a second
	/// finished quest of the same type. FinishQuest is overridden accordingly.
	/// </summary>
	public class ArtifactEncounter : AbstractQuest
	{
		/// <summary>Custom property holding the artifact ID (Artifact.ArtifactID).</summary>
		public const string ARTIFACT_ID_PROPERTY = "artifactID";

		/// <summary>Quest step of a granted credit.</summary>
		public const int CREDIT_STEP = -1;

		public ArtifactEncounter(GamePlayer questingPlayer)
			: base(questingPlayer) { }

		public ArtifactEncounter(GamePlayer questingPlayer, int step)
			: base(questingPlayer, step) { }

		public ArtifactEncounter(GamePlayer questingPlayer, DbQuest dbQuest)
			: base(questingPlayer, dbQuest) { }

		/// <summary>
		/// The artifact this credit belongs to, or null for a DOL-style subclass that
		/// identifies its artifact by type alone.
		/// </summary>
		public string ArtifactID
		{
			get
			{
				try
				{
					return GetCustomProperty(ARTIFACT_ID_PROPERTY);
				}
				catch (Exception)
				{
					return null;
				}
			}
		}

		/// <summary>
		/// Creates and stores encounter credit. <paramref name="encounterType"/> may name a
		/// DOL-style ArtifactEncounter subclass (from Artifact.EncounterID); otherwise the
		/// generic class is used. Returns the credit, or null if it could not be created.
		/// </summary>
		public static ArtifactEncounter GrantCredit(GamePlayer player, string artifactID, Type encounterType = null)
		{
			if (player == null || string.IsNullOrEmpty(artifactID))
				return null;

			ArtifactEncounter credit;

			if (encounterType != null && typeof(ArtifactEncounter).IsAssignableFrom(encounterType) && encounterType != typeof(ArtifactEncounter))
			{
				// DOL's per-artifact stubs only have the (GamePlayer) and (GamePlayer, DbQuest) constructors.
				credit = encounterType.GetConstructor([typeof(GamePlayer), typeof(int)]) != null
					? Activator.CreateInstance(encounterType, player, CREDIT_STEP) as ArtifactEncounter
					: Activator.CreateInstance(encounterType, player) as ArtifactEncounter;
			}
			else
				credit = new ArtifactEncounter(player, CREDIT_STEP);

			if (credit == null)
				return null;

			credit.SetCustomProperty(ARTIFACT_ID_PROPERTY, artifactID);
			credit.FinishQuest();
			return credit;
		}

		/// <summary>
		/// Marks the credit as earned: Step -1, into the finished list, no journal packet
		/// unless it was (unexpectedly) in the journal.
		/// </summary>
		public override void FinishQuest()
		{
			if (Step != CREDIT_STEP)
				Step = CREDIT_STEP;

			if (m_questPlayer.QuestList.TryRemove(this, out byte index))
			{
				m_questPlayer.AvailableQuestIndexes.Enqueue(index);
				m_questPlayer.Out.SendQuestRemove(index);
			}

			if (!m_questPlayer.HasFinishedQuest(this))
				m_questPlayer.AddFinishedQuest(this);
		}

		public override bool CheckQuestQualification(GamePlayer player)
		{
			return player == null;
		}

		public override void Notify(DOLEvent e, object sender, EventArgs args)
		{
		}
	}
}
