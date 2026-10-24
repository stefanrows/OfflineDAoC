using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.Quests;

namespace DOL.GS
{
	/// <summary>
	/// Player helpers the DOLSharp artifact code expects (formerly gaheris ArtifactCompat).
	///
	/// gaheris's <c>player.QuestList()</c> extension collides with our
	/// <see cref="GamePlayer.QuestList"/> property (a ConcurrentDictionary; the call would
	/// not compile), so the active-quest snapshot is <see cref="ActiveQuests"/> here.
	/// Port Scholar, ArtifactScholar and ArtifactTurnInQuest (WP3.1b) against these names.
	/// </summary>
	public static class ArtifactPlayerExtensions
	{
		/// <summary>
		/// DOLSharp had this on GamePlayer: whether any version of the artifact exists
		/// that this class and realm could be given.
		/// </summary>
		public static bool CanReceiveArtifact(this GamePlayer player, string artifactID)
		{
			if (player?.CharacterClass == null || string.IsNullOrEmpty(artifactID))
				return false;

			Dictionary<string, DbItemTemplate> versions = ArtifactMgr.GetArtifactVersions(
				artifactID, (eCharacterClass) player.CharacterClass.ID, player.Realm);

			return versions != null && versions.Count > 0;
		}

		/// <summary>
		/// DOLSharp's CharacterClass.GetSalutation(Gender): the class name a scholar greets
		/// the player by. Ours has no gendered names, so this is the class name.
		/// </summary>
		public static string Salutation(this GamePlayer player)
		{
			return player?.CharacterClass?.Name ?? "traveller";
		}

		/// <summary>
		/// Snapshot of the player's active quests (DOLSharp: the QuestList property; gaheris:
		/// the QuestList() extension). Removing from the returned list changes nothing; use
		/// <c>player.QuestList.TryRemove</c> as AbstractQuest.FinishQuest does.
		/// </summary>
		public static List<AbstractQuest> ActiveQuests(this GamePlayer player)
		{
			return player != null ? player.QuestList.Keys.ToList() : [];
		}

		/// <summary>
		/// Snapshot of the player's finished quests, including encounter credits.
		/// </summary>
		public static List<AbstractQuest> FinishedQuests(this GamePlayer player)
		{
			return player != null ? player.GetFinishedQuests() : [];
		}
	}
}
