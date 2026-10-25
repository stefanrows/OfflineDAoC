#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace OfflineDaoc.Configuration;

/// <summary>
/// The two worlds the launcher can create (docs/RVR_WORLDS.md). The numbers match
/// <c>DOL.GS.WorldModelKind</c> so server code can convert with a cast.
/// </summary>
public enum WorldSaveKind
{
    /// <summary>The Camlann full-PvP world, marker <c>Camlann-1</c>.</summary>
    Camlann = 1,

    /// <summary>The classic realm-vs-realm world, marker <c>Realm-1</c>; the default world.</summary>
    Realm = 2,
}

/// <summary>
/// Where each world keeps its save and the side files the server writes next to it. Shared by the
/// server and the launcher (linked into the launcher project), so both name every file the same way.
/// <para>
/// The RvR world keeps the historical file names: <c>runtime/data/opendaoc.sqlite3.db</c> and the
/// plain side-file names in <c>runtime/server</c>. The Camlann world uses
/// <c>runtime/data/opendaoc-camlann.sqlite3.db</c>, and every per-save side file gets the same
/// <c>-camlann</c> twin (<c>realm-event-records-camlann.sqlite3</c>, ...).
/// </para>
/// <para>
/// Side files are keyed by the save file the server opens, not by the marker inside it: the server
/// knows its connection string before it reads any marker (the Gothwaite switch is read before the
/// marker check), and a save that still sits at the default path keeps the side files it has today.
/// </para>
/// </summary>
public static class WorldSaveLayout
{
    public const string CamlannMarker = "Camlann-1";
    public const string RealmMarker = "Realm-1";

    public const string RealmDatabaseFileName = "opendaoc.sqlite3.db";
    public const string CamlannDatabaseFileName = "opendaoc-camlann.sqlite3.db";

    public const string RealmEventRecordsFileName = "realm-event-records.sqlite3";
    public const string OpenBgStatusFileName = "open-bg-status.json";
    public const string GothwaiteSwitchFileName = "gothwaite-pvp.json";
    public const string BotWorldFileName = "bot-world.json";
    public const string RvrWorldFileName = "rvr-world.json";

    private const string DatabaseStem = "opendaoc";
    private const string DatabaseExtension = ".sqlite3.db";

    /// <summary>
    /// World state the server writes per save: a world reset moves them to the backup with the save,
    /// and the fresh world starts without them.
    /// </summary>
    public static IReadOnlyList<string> PerSaveStateFiles { get; } =
        [RealmEventRecordsFileName, OpenBgStatusFileName, BotWorldFileName, RvrWorldFileName];

    /// <summary>
    /// Per-save settings: moved to the backup with the save as well. A fresh Camlann world gets a copy
    /// of the replaced Camlann world's settings back; a fresh RvR world starts without (Gothwaite off).
    /// </summary>
    public static IReadOnlyList<string> PerSaveSettingFiles { get; } = [GothwaiteSwitchFileName];

    /// <summary>The files SQLite keeps beside a database while it is open or after an unclean stop.</summary>
    public static IReadOnlyList<string> SqliteSidecarSuffixes { get; } = ["-wal", "-shm", "-journal"];

    public static IReadOnlyList<WorldSaveKind> Worlds { get; } = [WorldSaveKind.Realm, WorldSaveKind.Camlann];

    public static string Marker(WorldSaveKind world) => world switch
    {
        WorldSaveKind.Realm => RealmMarker,
        WorldSaveKind.Camlann => CamlannMarker,
        _ => throw new ArgumentOutOfRangeException(nameof(world), world, "Unknown world."),
    };

    /// <summary>The world a marker names, compared exactly as the server does; null for anything else.</summary>
    public static WorldSaveKind? FromMarker(string? marker) => marker switch
    {
        RealmMarker => WorldSaveKind.Realm,
        CamlannMarker => WorldSaveKind.Camlann,
        _ => null,
    };

    public static string DatabaseFileName(WorldSaveKind world) => world switch
    {
        WorldSaveKind.Realm => RealmDatabaseFileName,
        WorldSaveKind.Camlann => CamlannDatabaseFileName,
        _ => throw new ArgumentOutOfRangeException(nameof(world), world, "Unknown world."),
    };

    public static string DisplayName(WorldSaveKind world) => world switch
    {
        WorldSaveKind.Realm => "Realm vs Realm",
        WorldSaveKind.Camlann => "PvP world (Camlann)",
        _ => throw new ArgumentOutOfRangeException(nameof(world), world, "Unknown world."),
    };

    /// <summary>The other world (for the seed of a world that has no save yet).</summary>
    public static WorldSaveKind Other(WorldSaveKind world) =>
        world == WorldSaveKind.Realm ? WorldSaveKind.Camlann : WorldSaveKind.Realm;

    /// <summary>
    /// The side-file suffix of a save: empty for <c>opendaoc.sqlite3.db</c> (and any name outside the
    /// <c>opendaoc-&lt;name&gt;.sqlite3.db</c> pattern), <c>-camlann</c> for <c>opendaoc-camlann.sqlite3.db</c>.
    /// </summary>
    public static string SideFileSuffix(string? databaseFileOrPath)
    {
        string name = Path.GetFileName(databaseFileOrPath ?? string.Empty);
        if (!name.EndsWith(DatabaseExtension, StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        string stem = name[..^DatabaseExtension.Length];
        if (!stem.StartsWith(DatabaseStem + "-", StringComparison.OrdinalIgnoreCase) || stem.Length == DatabaseStem.Length + 1)
            return string.Empty;
        return stem[DatabaseStem.Length..].ToLowerInvariant();
    }

    /// <summary>The per-save name of a side file: the suffix goes before the first dot.</summary>
    public static string SideFileName(string baseFileName, string? databaseFileOrPath)
    {
        if (string.IsNullOrWhiteSpace(baseFileName))
            throw new ArgumentException("A side-file name is required.", nameof(baseFileName));
        string suffix = SideFileSuffix(databaseFileOrPath);
        if (suffix.Length == 0)
            return baseFileName;
        int dot = baseFileName.IndexOf('.');
        return dot < 0 ? baseFileName + suffix : baseFileName[..dot] + suffix + baseFileName[dot..];
    }

    public static string SideFilePath(string? serverDirectory, string baseFileName, string? databaseFileOrPath) =>
        Path.Combine(serverDirectory ?? string.Empty, SideFileName(baseFileName, databaseFileOrPath));

    /// <summary>The <c>Data Source</c> value of an SQLite connection string, or null when it has none.</summary>
    public static string? DataSource(string? connectionString)
    {
        foreach (string part in (connectionString ?? string.Empty).Split(';'))
        {
            int equals = part.IndexOf('=');
            if (equals <= 0)
                continue;
            if (IsDataSourceKey(part[..equals]))
                return part[(equals + 1)..].Trim().Trim('"', '\'');
        }
        return null;
    }

    /// <summary>
    /// The connection string with the file name of its <c>Data Source</c> replaced, keeping the
    /// directory part and every other option exactly as written.
    /// </summary>
    public static string WithDatabaseFileName(string? connectionString, string databaseFileName)
    {
        if (string.IsNullOrWhiteSpace(databaseFileName) || databaseFileName.IndexOfAny(['/', '\\', ';', '"']) >= 0)
            throw new ArgumentException("A plain database file name is required.", nameof(databaseFileName));
        string[] parts = (connectionString ?? string.Empty).Split(';');
        for (int index = 0; index < parts.Length; index++)
        {
            int equals = parts[index].IndexOf('=');
            if (equals <= 0 || !IsDataSourceKey(parts[index][..equals]))
                continue;
            string value = parts[index][(equals + 1)..].Trim().Trim('"', '\'');
            int slash = Math.Max(value.LastIndexOf('/'), value.LastIndexOf('\\'));
            parts[index] = parts[index][..(equals + 1)] + (slash < 0 ? string.Empty : value[..(slash + 1)]) + databaseFileName;
            return string.Join(";", parts);
        }
        throw new InvalidOperationException("The connection string has no Data Source.");
    }

    private static bool IsDataSourceKey(string key)
    {
        string trimmed = key.Trim();
        return trimmed.Equals("Data Source", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("DataSource", StringComparison.OrdinalIgnoreCase);
    }
}
