using System;
using DOL.Database.Attributes;

namespace DOL.Database
{
    /// <summary>Additive camp funding; existing characters and world saves remain compatible.</summary>
    [DataTable(TableName = "BattlegroundCampaignCamp")]
    public class DbBattlegroundCampaignCamp : DataObject
    {
        private string _key = string.Empty;
        private string _guildId = string.Empty;
        private int _tokens;
        private DateTime _expires;
        private DateTime _captainRespawn;

        [DataElement(AllowDbNull = false, Unique = true)]
        public string CampKey { get => _key; set { _key = value; Dirty = true; } }
        [DataElement(AllowDbNull = false)]
        public string SponsorGuildId { get => _guildId; set { _guildId = value; Dirty = true; } }
        [DataElement(AllowDbNull = false)]
        public int Tokens { get => _tokens; set { _tokens = value; Dirty = true; } }
        [DataElement(AllowDbNull = false)]
        public DateTime ExpiresAt { get => _expires; set { _expires = value; Dirty = true; } }
        [DataElement(AllowDbNull = false)]
        public DateTime CaptainRespawnAt { get => _captainRespawn; set { _captainRespawn = value; Dirty = true; } }
    }
}
