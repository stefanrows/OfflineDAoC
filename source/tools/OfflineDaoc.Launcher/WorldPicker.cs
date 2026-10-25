using System.Data.SQLite;
using System.Text.Json;
using OfflineDaoc.Configuration;

namespace OfflineDaoc.Launcher;

/// <summary>How the launcher's world picker sees one world's save (docs/RVR_WORLDS.md, "World picker").</summary>
public enum WorldSaveState
{
    /// <summary>The save carries this world's marker; START SERVER starts it.</summary>
    Ready,

    /// <summary>No save file: the launcher offers "Create world" for it.</summary>
    Missing,

    /// <summary>A save without a world marker or with <c>Normal</c>: an old save; "Create world" builds the world from it.</summary>
    NotCreated,

    /// <summary>The file carries the other world's marker; "Create world" replaces it (the old save goes to the backups).</summary>
    OtherWorld,

    /// <summary>Any other marker. The launcher refuses it, as the server does.</summary>
    Unsupported,
}

public sealed record WorldSaveStatus(WorldSaveKind World, string Database, WorldSaveState State, string? Marker)
{
    /// <summary>The launcher can build a fresh world in this save's place (the replaced file goes to the backups).</summary>
    public bool CanCreate => State is WorldSaveState.Missing or WorldSaveState.NotCreated or WorldSaveState.OtherWorld;

    /// <summary>A save file of some kind exists, so "Reset world" has something to move to the backups.</summary>
    public bool HasSave => State != WorldSaveState.Missing;
}

/// <summary>The world a launcher window works on, and the save every launcher read and write uses.</summary>
public sealed record WorldSelection(WorldSaveKind World, string Database);

/// <summary>
/// The launcher's world picker (package R5b of docs/RVR_MODE_PLAN.md): which world the launcher shows on opening,
/// what state each world's save is in, the display texts, and the one-time move of a Camlann save that still sits
/// in the RvR world's file (installations from before the two-world layout).
/// </summary>
public static class WorldPicker
{
    /// <summary>The picker's entries, RvR first (the default world, owner decision 1).</summary>
    public static IReadOnlyList<WorldSaveKind> Worlds { get; } = [WorldSaveKind.Realm, WorldSaveKind.Camlann];

    /// <summary>The entry text in the open dropdown list.</summary>
    public static string PickerText(WorldSaveKind world) => world == WorldSaveKind.Realm ? "RvR (Realm vs Realm)" : "PvP (Camlann)";

    /// <summary>The closed dropdown's text, short enough for the 132-pixel header slot.</summary>
    public static string ShortText(WorldSaveKind world) => world == WorldSaveKind.Realm ? "World: RvR" : "World: PvP";

    /// <summary>The status line's world label.</summary>
    public static string StatusText(WorldSaveKind world) => world == WorldSaveKind.Realm ? "WORLD: RvR" : "WORLD: PvP (Camlann)";

    /// <summary>The world's name inside sentences.</summary>
    public static string Name(WorldSaveKind world) => world == WorldSaveKind.Realm ? "RvR world" : "PvP world (Camlann)";

    public static string WindowTitle(WorldSaveKind world) =>
        world == WorldSaveKind.Realm ? "Offline DAoC — RvR world" : "Offline DAoC — PvP world (Camlann)";

    /// <summary>The state of a world's own save file in an installation's runtime folder.</summary>
    public static WorldSaveStatus Inspect(string runtimeRoot, WorldSaveKind world) =>
        InspectDatabase(world, WorldFiles.For(runtimeRoot, world).Database);

    /// <summary>The state of a save file as the given world (an unreadable file counts as an unsupported marker).</summary>
    public static WorldSaveStatus InspectDatabase(WorldSaveKind world, string database)
    {
        if (!File.Exists(database))
            return new WorldSaveStatus(world, database, WorldSaveState.Missing, null);
        string? marker;
        try { marker = CamlannWorldReset.ReadWorldModel(database); }
        catch (Exception exception) when (exception is SQLiteException or IOException or UnauthorizedAccessException)
        {
            return new WorldSaveStatus(world, database, WorldSaveState.Unsupported, "unreadable save");
        }
        WorldSaveKind? marked = WorldSaveLayout.FromMarker(marker);
        WorldSaveState state = marked == world ? WorldSaveState.Ready
            : marked is not null ? WorldSaveState.OtherWorld
            : marker is null || string.Equals(marker, "Normal", StringComparison.OrdinalIgnoreCase) ? WorldSaveState.NotCreated
            : WorldSaveState.Unsupported;
        return new WorldSaveStatus(world, database, state, marker);
    }

    /// <summary>
    /// The world shown when this Windows user has not picked one for the installation yet: RvR when the RvR save
    /// exists or neither world exists; PvP when only a Camlann world exists (today's installs keep showing their
    /// world until the owner switches).
    /// </summary>
    public static WorldSaveKind DefaultWorld(string runtimeRoot)
    {
        if (Inspect(runtimeRoot, WorldSaveKind.Realm).State == WorldSaveState.Ready)
            return WorldSaveKind.Realm;
        if (Inspect(runtimeRoot, WorldSaveKind.Camlann).State == WorldSaveState.Ready || IsLegacyCamlannLayout(runtimeRoot))
            return WorldSaveKind.Camlann;
        return WorldSaveKind.Realm;
    }

    /// <summary>
    /// The world a launcher window opens on. While a server runs, the save it was started with (the file the server
    /// configuration names) decides, so the launcher never reads another world under a running server. Otherwise the
    /// user's remembered choice, else <see cref="DefaultWorld"/>.
    /// </summary>
    public static WorldSelection Resolve(string runtimeRoot, WorldSaveKind? remembered, string? runningDatabaseFileName)
    {
        if (!string.IsNullOrWhiteSpace(runningDatabaseFileName))
        {
            string running = Path.Combine(runtimeRoot, "data", Path.GetFileName(runningDatabaseFileName));
            WorldSaveKind? marked = null;
            try { marked = WorldSaveLayout.FromMarker(CamlannWorldReset.ReadWorldModel(running)); }
            catch (Exception exception) when (exception is SQLiteException or IOException or UnauthorizedAccessException) { }
            return new WorldSelection(marked ?? WorldFiles.ForDatabase(Path.Combine(runtimeRoot, "server"), running).World ?? WorldSaveKind.Realm, running);
        }
        WorldSaveKind world = remembered ?? DefaultWorld(runtimeRoot);
        return new WorldSelection(world, WorldFiles.For(runtimeRoot, world).Database);
    }

    /// <summary>
    /// True for an installation from before the two-world layout: the RvR world's file holds a Camlann world and the
    /// Camlann file does not exist yet. <see cref="RelocateLegacyCamlann"/> gives that world its own files.
    /// </summary>
    public static bool IsLegacyCamlannLayout(string runtimeRoot)
    {
        WorldFiles rvr = WorldFiles.For(runtimeRoot, WorldSaveKind.Realm);
        WorldFiles camlann = WorldFiles.For(runtimeRoot, WorldSaveKind.Camlann);
        if (File.Exists(camlann.Database) || WorldSaveLayout.SqliteSidecarSuffixes.Any(suffix => File.Exists(camlann.Database + suffix)))
            return false;
        return InspectDatabase(WorldSaveKind.Camlann, rvr.Database).State == WorldSaveState.Ready;
    }

    /// <summary>
    /// Moves (renames) a Camlann world that still sits in the RvR world's files to the Camlann files: the save, its
    /// -wal/-shm/-journal and every per-save side file. Nothing is copied or deleted, so nothing is lost; the RvR
    /// world can then be created beside it. Refused while the server runs or when a target file already exists
    /// (nothing moves). A failed move puts the files already moved back. Returns the new paths.
    /// </summary>
    public static IReadOnlyList<string> RelocateLegacyCamlann(string runtimeRoot, Func<bool> serverStopped, Action<string, string>? moveFile = null)
    {
        ArgumentNullException.ThrowIfNull(serverStopped);
        CamlannWorldReset.RequireStopped(serverStopped);
        if (!IsLegacyCamlannLayout(runtimeRoot))
            throw new InvalidOperationException("The RvR save file does not hold a PvP (Camlann) world that needs its own file.");

        List<(string From, string To)> plan = LegacyMoves(runtimeRoot).Where(move => File.Exists(move.From)).ToList();
        List<string> conflicts = plan.Where(move => File.Exists(move.To)).Select(move => move.To).ToList();
        if (conflicts.Count > 0)
            throw new InvalidOperationException("The PvP world's files already exist, so its save was not moved:\n" + string.Join("\n", conflicts));

        SQLiteConnection.ClearAllPools();
        CamlannWorldReset.RequireStopped(serverStopped);
        Action<string, string> move = moveFile ?? ((from, to) => File.Move(from, to));
        var moved = new List<(string From, string To)>();
        try
        {
            foreach ((string from, string to) in plan)
            {
                move(from, to);
                moved.Add((from, to));
            }
        }
        catch (Exception failure)
        {
            var stuck = new List<string>();
            foreach ((string from, string to) in Enumerable.Reverse(moved))
            {
                try { move(to, from); }
                catch (Exception) { stuck.Add($"{to} -> {from}"); }
            }
            string detail = stuck.Count == 0
                ? "Every file was put back."
                : "These files could not be put back; move them by hand:\n" + string.Join("\n", stuck);
            throw new IOException($"The PvP world's save could not be moved to its own file: {failure.Message}\n{detail}", failure);
        }
        return moved.Select(item => item.To).ToArray();
    }

    /// <summary>
    /// Every (RvR file, Camlann file) pair the move of a legacy Camlann world renames, whether present or not. The
    /// save's -wal/-shm/-journal go first and the save itself after them, so the save never sits under its new name
    /// without its sidecars; the side files follow.
    /// </summary>
    public static IEnumerable<(string From, string To)> LegacyMoves(string runtimeRoot)
    {
        List<(string From, string To)> pairs = WorldReset.ReplacedFiles(WorldFiles.For(runtimeRoot, WorldSaveKind.Realm))
            .Zip(WorldReset.ReplacedFiles(WorldFiles.For(runtimeRoot, WorldSaveKind.Camlann))).ToList();
        (string From, string To) save = pairs[0];
        pairs.RemoveAt(0);
        pairs.Insert(WorldSaveLayout.SqliteSidecarSuffixes.Count, save);
        return pairs;
    }

    /// <summary>
    /// The confirmation text before a world is created or reset. It names the world, where the new save is built from,
    /// and the backup folder the replaced files move to (<see cref="WorldReset"/> names it the same way).
    /// </summary>
    public static string CreationPrompt(WorldSaveStatus status, string seed, string backupRoot)
    {
        string world = Name(status.World);
        string other = Name(WorldSaveLayout.Other(status.World));
        string tag = status.World == WorldSaveKind.Realm ? "rvr" : "camlann";
        string start = status.World == WorldSaveKind.Realm
            ? "The frontier keeps start owned by their original realm at their original level, the portal and relic keeps with their realm, the relics are home, and 600 bots per realm join single-realm guilds."
            : "Keeps start neutral, the relics are home, and 600 bots per realm join mixed guilds, as in the PvP world today.";
        string what = status.HasSave
            ? $"Reset the {world}?\n\nThe {world} starts over: new characters, companions, bots and guilds. Your account stays, as a normal player."
            : $"Create a fresh {world}?\n\nIt starts with no characters. Your account is kept, as a normal player.";
        string from = string.Equals(Path.GetFullPath(seed), Path.GetFullPath(status.Database), StringComparison.OrdinalIgnoreCase)
            ? "The world data comes from the current save."
            : $"The world data is copied from {Path.GetFileName(seed)}; that save is only read.";
        string untouched = status.State == WorldSaveState.OtherWorld
            ? $"This file holds a {other}: that world moves to the backups with the file. The {other}'s own save, " +
              $"{WorldSaveLayout.DatabaseFileName(WorldSaveLayout.Other(status.World))}, is not changed."
            : $"The {other} is not changed.";
        string moved = status.HasSave
            ? $"The old save and its side files move to:\n{Path.Combine(backupRoot, $"<date-time>-{tag}-world-reset")}\nNothing is deleted."
            : "No save is replaced.";
        return $"{what}\n{start}\n{from}\n{untouched}\n\n{moved}\n\nThe server stays stopped until this finishes. Continue?";
    }
}

/// <summary>
/// The world each Windows user last picked per installation, beside the Join Friend profile in
/// <c>%LOCALAPPDATA%\OfflineDAoC</c>. Keyed by the installation's runtime folder, so two installs keep their own
/// choice. A missing or unreadable file means "not picked yet".
/// </summary>
public static class WorldChoiceStore
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OfflineDAoC", "world-choice.json");

    public static WorldSaveKind? Load(string path, string runtimeRoot)
    {
        Dictionary<string, string> installs = Read(path);
        return installs.TryGetValue(InstallKey(runtimeRoot), out string? stored) &&
               Enum.TryParse(stored, ignoreCase: false, out WorldSaveKind world) && Enum.IsDefined(world)
            ? world
            : null;
    }

    public static void Save(string path, string runtimeRoot, WorldSaveKind world)
    {
        if (!Enum.IsDefined(world))
            throw new ArgumentOutOfRangeException(nameof(world), world, "Unknown world.");
        Dictionary<string, string> installs = Read(path);
        installs[InstallKey(runtimeRoot)] = world.ToString();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new StoredChoices { Installs = installs },
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    /// <summary>The installation key: the full runtime folder path, case-insensitive as Windows paths are.</summary>
    public static string InstallKey(string runtimeRoot) =>
        Path.GetFullPath(runtimeRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();

    private static Dictionary<string, string> Read(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new Dictionary<string, string>(StringComparer.Ordinal);
            StoredChoices? stored = JsonSerializer.Deserialize<StoredChoices>(File.ReadAllText(path));
            return new Dictionary<string, string>(stored?.Installs ?? [], StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private sealed class StoredChoices
    {
        public Dictionary<string, string>? Installs { get; set; }
    }
}
