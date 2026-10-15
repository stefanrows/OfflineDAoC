using DOL.Database;
using DOL.Database.Attributes;

namespace DOL.GS;

/// <summary>
/// The home of one autonomous world bot before the Gothwaite PvP mode moved it: region, zone, position, bind, camp
/// and itinerary copied from <see cref="OfflineWorldBotRecord"/>. One row per bot (docs/GOTHWAITE_PVP_MODE.md 6.7).
/// The table is additive; the object database creates it at start. Progress is never stored or restored here.
/// </summary>
[DataTable(TableName = "offline_gothwaite_pvp_home")]
public sealed class GothwaitePvpHomeRecord : DataObject
{
    [PrimaryKey(AutoIncrement = true)] public long HomeId { get; set; }
    [DataElement(AllowDbNull = false, Unique = true)] public long BotId { get; set; }
    [DataElement(AllowDbNull = false)] public int RegionId { get; set; }
    [DataElement(AllowDbNull = false)] public int ZoneId { get; set; }
    [DataElement(AllowDbNull = true)] public string ZoneName { get; set; }
    [DataElement(AllowDbNull = false)] public int X { get; set; }
    [DataElement(AllowDbNull = false)] public int Y { get; set; }
    [DataElement(AllowDbNull = false)] public int Z { get; set; }
    [DataElement(AllowDbNull = false)] public int BindRegionId { get; set; }
    [DataElement(AllowDbNull = false)] public int BindX { get; set; }
    [DataElement(AllowDbNull = false)] public int BindY { get; set; }
    [DataElement(AllowDbNull = false)] public int BindZ { get; set; }
    [DataElement(AllowDbNull = false)] public string CurrentCampId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string ItineraryJson { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string SnapshotUtc { get; set; } = string.Empty;
}
