using System;
using DOL.Database;

namespace DOL.GS;

/// <summary>
/// The fork has one supported world model. Keeping this contract in one place
/// prevents a missing local marker from silently loading an old Normal save.
/// </summary>
public static class CamlannWorldModel
{
    public const string MarkerKey = "WorldModel";
    public const string MarkerValue = "Camlann-1";

    public static bool IsSupported(EGameServerType serverType, DbOfflineLocalOption marker)
    {
        return serverType == EGameServerType.GST_PvP &&
               marker is not null &&
               string.Equals(marker.Value, MarkerValue, StringComparison.Ordinal);
    }
}
