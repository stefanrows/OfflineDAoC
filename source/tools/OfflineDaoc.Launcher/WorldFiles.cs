using OfflineDaoc.Configuration;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Every file of one world in an installation (docs/RVR_WORLDS.md): the save in <c>runtime/data</c> and its per-save
/// side files in <c>runtime/server</c>, named by <see cref="WorldSaveLayout"/> exactly as the server names them.
/// </summary>
public sealed record WorldFiles(string Database, string ServerDirectory)
{
    /// <summary>The world of the save file, by its file name (null for a file outside the two world names).</summary>
    public WorldSaveKind? World => Path.GetFileName(Database) switch
    {
        WorldSaveLayout.RealmDatabaseFileName => WorldSaveKind.Realm,
        WorldSaveLayout.CamlannDatabaseFileName => WorldSaveKind.Camlann,
        _ => null,
    };

    public string EventRecords => Side(WorldSaveLayout.RealmEventRecordsFileName);
    public string OpenBgStatus => Side(WorldSaveLayout.OpenBgStatusFileName);
    public string GothwaiteSwitch => Side(WorldSaveLayout.GothwaiteSwitchFileName);
    public string BotWorld => Side(WorldSaveLayout.BotWorldFileName);
    public string RvrWorld => Side(WorldSaveLayout.RvrWorldFileName);

    /// <summary>World state beside the save (moved to the backup by a reset).</summary>
    public IReadOnlyList<string> StateFiles => WorldSaveLayout.PerSaveStateFiles.Select(Side).ToArray();

    /// <summary>World settings beside the save (moved to the backup by a reset; Camlann gets a copy back).</summary>
    public IReadOnlyList<string> SettingFiles => WorldSaveLayout.PerSaveSettingFiles.Select(Side).ToArray();

    /// <summary>A per-save side file of this world.</summary>
    public string Side(string baseFileName) => WorldSaveLayout.SideFilePath(ServerDirectory, baseFileName, Database);

    /// <summary>The files of a world in an installation's runtime folder (the launcher's own folder).</summary>
    public static WorldFiles For(string runtimeRoot, WorldSaveKind world) => new(
        Path.Combine(runtimeRoot, "data", WorldSaveLayout.DatabaseFileName(world)),
        Path.Combine(runtimeRoot, "server"));

    /// <summary>The files belonging to an arbitrary save path (side files keyed by its file name, as on the server).</summary>
    public static WorldFiles ForDatabase(string serverDirectory, string database) => new(database, serverDirectory);

    /// <summary>
    /// The installation's backup folder, <c>&lt;install&gt;-backups</c> beside the install root (the parent of
    /// <c>runtime</c>), as the deploy tooling uses: <c>C:\OfflineDAoC\playable-backups</c> for <c>C:\OfflineDAoC\playable</c>.
    /// </summary>
    public static string DefaultBackupRoot(string runtimeRoot)
    {
        string install = Path.GetDirectoryName(Path.GetFullPath(runtimeRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            ?? throw new InvalidOperationException("The runtime folder has no installation folder above it.");
        return install.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "-backups";
    }
}
