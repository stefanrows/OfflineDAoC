using System;
using System.Linq;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;

namespace DOL.GS.Quests.Atlantis
{
	/// <summary>
	/// The artifact a scholar awakens for a player (TOA_PLAN WP3.1b), data-driven: one class
	/// for every artifact, the artifact ID in a custom property, as <see cref="ArtifactEncounter"/>.
	///
	/// Two states, both stored as one Quest row:
	/// <list type="bullet">
	/// <item>Step <see cref="CHOOSING_STEP"/>: the scholar holds the player's book and waits for
	/// the version (Cloth, Plate, Slashing, ...). Shown in the journal; survives a relog.</item>
	/// <item>Step <see cref="AWAKENED_STEP"/>: the artifact was handed over. Kept only in the
	/// finished list; <see cref="ArtifactMgr.HasFinishedArtifactQuest"/> reads it, so the
	/// player never gets the same artifact twice and never buys its credit again.</item>
	/// </list>
	/// DOLSharp had one ArtifactQuest subclass per artifact, found by the type name in
	/// Artifact.QuestID; our rows carry template names there (docs/TOA_ARTIFACTS.md).
	/// </summary>
	public class ArtifactQuest : AbstractQuest
	{
		/// <summary>Custom property holding the artifact ID (Artifact.ArtifactID).</summary>
		public const string ARTIFACT_ID_PROPERTY = "artifactID";

		/// <summary>Custom property holding the item template that was handed out.</summary>
		public const string ITEM_PROPERTY = "itemID";

		/// <summary>Custom property holding a page template of the book the scholar keeps (to give it back on abort).</summary>
		public const string BOOK_TEMPLATE_PROPERTY = "bookTemplate";

		/// <summary>Waiting for the player to name a version.</summary>
		public const int CHOOSING_STEP = 1;

		/// <summary>Artifact handed over (the finished step of our AbstractQuest).</summary>
		public const int AWAKENED_STEP = -2;

		public ArtifactQuest(GamePlayer questingPlayer)
			: base(questingPlayer) { }

		public ArtifactQuest(GamePlayer questingPlayer, int step)
			: base(questingPlayer, step) { }

		public ArtifactQuest(GamePlayer questingPlayer, DbQuest dbQuest)
			: base(questingPlayer, dbQuest) { }

		/// <summary>The artifact this quest belongs to.</summary>
		public string ArtifactID => Property(ARTIFACT_ID_PROPERTY);

		/// <summary>The item template handed out, once awakened.</summary>
		public string ItemID => Property(ITEM_PROPERTY);

		/// <summary>A page template of the book the scholar keeps while the player chooses.</summary>
		public string BookTemplateID => Property(BOOK_TEMPLATE_PROPERTY);

		public bool IsAwakened => Step < 0;

		public override string Name => "Awakening " + ArtifactMgr.GetArtifactName(ArtifactID);

		public override string Description
		{
			get
			{
				if (Step != CHOOSING_STEP)
					return base.Description;

				string artifact = ArtifactMgr.GetArtifactName(ArtifactID);
				string options = string.Join(", ", ArtifactAwakening.GetChoices(QuestPlayer, ArtifactID).Select(c => "[" + c.Label + "]"));
				string held = ArtifactMgr.HasScrollSet(ArtifactID) ? $"A scholar keeps your book of {artifact}. " : string.Empty;
				return $"{held}Tell a scholar which form {artifact} should take: {options}.";
			}
		}

		public override int Level => ArtifactAwakening.MIN_LEVEL;

		/// <summary>
		/// Starts the version choice: the scholar has taken the book. Returns null if the player
		/// is already choosing another artifact.
		/// </summary>
		public static ArtifactQuest StartChoice(GamePlayer player, string artifactID, string bookTemplateID)
		{
			if (player == null || string.IsNullOrEmpty(artifactID) || Pending(player) != null)
				return null;

			var quest = new ArtifactQuest(player, CHOOSING_STEP);
			quest.SetCustomProperty(ARTIFACT_ID_PROPERTY, artifactID);

			if (!string.IsNullOrEmpty(bookTemplateID))
				quest.SetCustomProperty(BOOK_TEMPLATE_PROPERTY, bookTemplateID);

			if (!player.AddQuest(quest))
			{
				quest.DeleteFromDatabase();
				return null;
			}

			return quest;
		}

		/// <summary>The player's open version choice, or null.</summary>
		public static ArtifactQuest Pending(GamePlayer player)
		{
			return player?.ActiveQuests().OfType<ArtifactQuest>().FirstOrDefault(q => !q.IsAwakened);
		}

		/// <summary>
		/// Records the hand-over: the pending choice moves to the finished list, or a new
		/// finished row is written when the artifact had only one version for the player.
		/// </summary>
		public static ArtifactQuest RecordAwakened(GamePlayer player, string artifactID, string itemID, ArtifactQuest pending = null)
		{
			if (player == null || string.IsNullOrEmpty(artifactID))
				return null;

			ArtifactQuest quest = pending;

			if (quest == null || quest.ArtifactID != artifactID)
			{
				quest = new ArtifactQuest(player, AWAKENED_STEP);
				quest.SetCustomProperty(ARTIFACT_ID_PROPERTY, artifactID);
			}

			if (!string.IsNullOrEmpty(itemID))
				quest.SetCustomProperty(ITEM_PROPERTY, itemID);

			quest.FinishQuest();
			return quest;
		}

		/// <summary>
		/// Step -2, out of the journal, into the finished list. Unlike AbstractQuest.FinishQuest
		/// a second finished ArtifactQuest (another artifact) is kept, and a quest that never
		/// was in the journal sends no journal packet.
		/// </summary>
		public override void FinishQuest()
		{
			bool wasActive = m_questPlayer.QuestList.TryRemove(this, out byte index);

			if (wasActive)
				m_questPlayer.AvailableQuestIndexes.Enqueue(index);

			// Out of QuestList first: the Step setter's journal update then finds no index and sends nothing.
			if (Step != AWAKENED_STEP)
				Step = AWAKENED_STEP;

			if (wasActive)
				m_questPlayer.Out.SendQuestRemove(index);

			if (!m_questPlayer.HasFinishedQuest(this))
				m_questPlayer.AddFinishedQuest(this);
		}

		public override void OnQuestAssigned(GamePlayer player)
		{
			player.Out.SendMessage($"You begin the awakening of {ArtifactMgr.GetArtifactName(ArtifactID)}.", eChatType.CT_System, eChatLoc.CL_ChatWindow);
		}

		/// <summary>Abandoning the choice gives the book back (or drops it if the backpack is full).</summary>
		public override void AbortQuest()
		{
			if (!IsAwakened)
				ArtifactAwakening.ReturnBook(QuestPlayer, ArtifactID, BookTemplateID);

			base.AbortQuest();
		}

		public override bool CheckQuestQualification(GamePlayer player)
		{
			// Never offered through the quest-giver path; scholars start it.
			return false;
		}

		public override void Notify(DOLEvent e, object sender, EventArgs args)
		{
		}

		private string Property(string key)
		{
			try
			{
				return GetCustomProperty(key);
			}
			catch (Exception)
			{
				return null;
			}
		}
	}
}
