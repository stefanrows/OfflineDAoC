using DOL.Database.Attributes;

namespace DOL.Database
{
	/// <summary>
	/// One named creature whose death earns encounter credit for an artifact (Trials of Atlantis,
	/// TOA_PLAN WP3.3, docs/TOA_ARTIFACTS.md "Encounter credit"). Shipped by world data pack 070;
	/// an artifact may name several creatures, a creature may serve several artifacts.
	/// </summary>
	[DataTable(TableName = "ArtifactEncounterTarget")]
	public class DbArtifactEncounterTarget : DataObject
	{
		/// <summary>Credit for the killer's group: every player who earned the kill.</summary>
		public const string MODE_GROUP = "group";

		/// <summary>Credit for the whole battlegroup of every player who earned the kill.</summary>
		public const string MODE_BATTLEGROUP = "battlegroup";

		private string m_artifactID = string.Empty;
		private string m_mobName = string.Empty;
		private int m_region;
		private string m_creditMode = MODE_GROUP;
		private string m_zone = string.Empty;

		/// <summary>The rows are world data from a pack; the server never writes them.</summary>
		public override bool AllowAdd
		{
			get { return false; }
			set { }
		}

		/// <summary>Artifact.ArtifactID the credit is for.</summary>
		[DataElement(AllowDbNull = false, Index = true)]
		public string ArtifactID
		{
			get { return m_artifactID; }
			set
			{
				Dirty = true;
				m_artifactID = value;
			}
		}

		/// <summary>Name of the creature, compared without regard to case.</summary>
		[DataElement(AllowDbNull = false, Index = true)]
		public string MobName
		{
			get { return m_mobName; }
			set
			{
				Dirty = true;
				m_mobName = value;
			}
		}

		/// <summary>Region the kill must happen in; 0 means any region.</summary>
		[DataElement(AllowDbNull = false)]
		public int Region
		{
			get { return m_region; }
			set
			{
				Dirty = true;
				m_region = value;
			}
		}

		/// <summary><see cref="MODE_GROUP"/> or <see cref="MODE_BATTLEGROUP"/>; anything else counts as group.</summary>
		[DataElement(AllowDbNull = false, Varchar = 20)]
		public string CreditMode
		{
			get { return m_creditMode; }
			set
			{
				Dirty = true;
				m_creditMode = value;
			}
		}

		/// <summary>
		/// Where the creature stands, for the player ("Mesothalassa", or "Oceanus Hesperos or Mesothalassa"
		/// for a creature in several zones). Text only; the credit does not depend on it.
		/// </summary>
		[DataElement(AllowDbNull = false, Varchar = 100)]
		public string Zone
		{
			get { return m_zone; }
			set
			{
				Dirty = true;
				m_zone = value;
			}
		}
	}
}
