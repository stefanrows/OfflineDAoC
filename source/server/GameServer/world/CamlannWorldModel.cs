using System;
using DOL.Database;

namespace DOL.GS;

/// <summary>
/// The worlds a save can declare in its <c>offline_local_options.WorldModel</c>
/// marker. A save without a known marker is never started.
/// </summary>
public enum WorldModelKind
{
    Unsupported = 0,

    /// <summary>The Camlann full-PvP world (marker <c>Camlann-1</c>).</summary>
    Camlann = 1,

    /// <summary>The classic realm-vs-realm world (marker <c>Realm-1</c>).</summary>
    Realm = 2,
}

/// <summary>
/// The fork supports two world models, both on GameType=PvP. Keeping this
/// contract in one place prevents a missing local marker from silently loading
/// an old Normal save, and keeps the marker values the launcher writes and the
/// values the server accepts in one list.
/// </summary>
public static class CamlannWorldModel
{
    public const string MarkerKey = "WorldModel";
    public const string MarkerValue = "Camlann-1";
    public const string CamlannMarkerValue = MarkerValue;
    public const string RealmMarkerValue = "Realm-1";

    /// <summary>
    /// The world a save declares, or <see cref="WorldModelKind.Unsupported"/>
    /// for a missing marker, an unknown marker or a server type other than PvP.
    /// Marker values are compared exactly, as the launcher writes them.
    /// </summary>
    public static WorldModelKind Parse(EGameServerType serverType, DbOfflineLocalOption marker)
    {
        if (serverType != EGameServerType.GST_PvP)
            return WorldModelKind.Unsupported;

        return ParseMarker(marker?.Value);
    }

    public static WorldModelKind ParseMarker(string value)
    {
        if (string.Equals(value, CamlannMarkerValue, StringComparison.Ordinal))
            return WorldModelKind.Camlann;
        if (string.Equals(value, RealmMarkerValue, StringComparison.Ordinal))
            return WorldModelKind.Realm;
        return WorldModelKind.Unsupported;
    }

    public static string MarkerValueFor(WorldModelKind world) => world switch
    {
        WorldModelKind.Camlann => CamlannMarkerValue,
        WorldModelKind.Realm => RealmMarkerValue,
        _ => null,
    };

    public static bool IsSupported(EGameServerType serverType, DbOfflineLocalOption marker)
    {
        return Parse(serverType, marker) != WorldModelKind.Unsupported;
    }
}
