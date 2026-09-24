using DOL.Database;
using DOL.Database.Attributes;

namespace DOL.GS
{

    [DataTable(TableName = "player_companions")]
    public sealed class PlayerCompanionRecord : DataObject
    {
        public PlayerCompanionRecord()
        {
            AllowAdd = true;
            AllowDelete = true;
        }

        [PrimaryKey]
        [DataElement(AllowDbNull = false, Varchar = 36)]
        public string CompanionId { get; set; } = string.Empty;

        [DataElement(AllowDbNull = false, Index = true, Varchar = 255)]
        public string OwnerCharacterId { get; set; } = string.Empty;

        [DataElement(AllowDbNull = false, Varchar = 64)]
        public string Name { get; set; } = string.Empty;

        [DataElement(AllowDbNull = false)]
        public int Realm { get; set; }

        [DataElement(AllowDbNull = false)]
        public int ClassId { get; set; }

        [DataElement(AllowDbNull = false)]
        public int RaceId { get; set; }

        [DataElement(AllowDbNull = false)]
        public int GenderId { get; set; }

        [DataElement(AllowDbNull = false)]
        public int Level { get; set; } = 1;

        [DataElement(AllowDbNull = false)]
        public long Experience { get; set; }

        [DataElement(AllowDbNull = false)]
        public string SerializedSpecs { get; set; } = string.Empty;

        [DataElement(AllowDbNull = false)]
        public string SerializedBuildPlan { get; set; } = string.Empty;

        [DataElement(AllowDbNull = false)]
        public int UnspentSpecPoints { get; set; }

        [DataElement(AllowDbNull = false)]
        public int LastTrainedLevel { get; set; } = 1;

        // Additive progression metadata. Empty plan and manual mode keep older
        // records on the established player-controlled training path.
        [DataElement(AllowDbNull = false, Varchar = 16)]
        public string TrainingMode { get; set; } = "manual";

        [DataElement(AllowDbNull = false, Varchar = 64)]
        public string TrainingPlanId { get; set; } = string.Empty;

        // Item IDs map to provenance/keep flags; equipped slots map to manual
        // locks. Keeping this on the companion record avoids changing Inventory.
        [DataElement(AllowDbNull = false)]
        public string SerializedEquipmentState { get; set; } = string.Empty;

        [DataElement(AllowDbNull = false)]
        public bool IsActive { get; set; }

        [DataElement(AllowDbNull = false)]
        public bool InventoryInitialized { get; set; }

        [DataElement(AllowDbNull = false, Varchar = 16)]
        public string RecruitType { get; set; } = "generated";

        [DataElement(AllowDbNull = false, Varchar = 128)]
        public string AuthoredRecruitKey { get; set; } = string.Empty;

        [DataElement(AllowDbNull = false, Varchar = 16)]
        public string PersonalityKey { get; set; } = string.Empty;

        [DataElement(AllowDbNull = false, Varchar = 16)]
        public string TacticalRole { get; set; } = string.Empty;

        [DataElement(AllowDbNull = false, Varchar = 16)]
        public string EngagementPreference { get; set; } = string.Empty;

        // Nullable so existing companion tables can add this preference without
        // assigning a new combat policy to old records. Empty/null resolves to Auto.
        [DataElement(AllowDbNull = true, Varchar = 16)]
        public string BombUsePreference { get; set; } = CompanionBombingPolicy.Auto;

        [DataElement(AllowDbNull = false)]
        public int AppearanceSize { get; set; }

        [DataElement(AllowDbNull = false)]
        public int StateVersion { get; set; } = 1;

        [DataElement(AllowDbNull = false, Varchar = 32)]
        public string CreatedUtc { get; set; } = string.Empty;

        [DataElement(AllowDbNull = false, Varchar = 32)]
        public string UpdatedUtc { get; set; } = string.Empty;
    }
}