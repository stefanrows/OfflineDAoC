using System.Text.RegularExpressions;
using OfflineDaoc.Configuration;

namespace OfflineDaoc.Launcher;

/// <summary>
/// The launcher's writer for the two serverconfig.xml values a world needs (docs/RVR_WORLDS.md): GameType stays
/// PvP for both worlds, and <c>DBConnectionString</c> names the chosen world's save file. Only those two element
/// texts change; every other byte, including line endings, stays as written.
/// </summary>
public static class CamlannServerConfig
{
    private static readonly Regex GameType = new(
        "(<GameType\\b[^>]*>)(.*?)(</GameType>)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex ConnectionString = new(
        "(<DBConnectionString\\b[^>]*>)(.*?)(</DBConnectionString>)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex DatabaseType = new(
        "<DBType\\b[^>]*>(.*?)</DBType>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public static bool EnsurePvP(string path)
    {
        if (!File.Exists(path))
            return true;

        string original = File.ReadAllText(path);
        string updated = PinPvP(original);
        if (!string.Equals(original, updated, StringComparison.Ordinal))
            File.WriteAllText(path, updated);
        return true;
    }

    /// <summary>Pins GameType to PvP and points the connection string at the world's save file.</summary>
    public static bool EnsureWorld(string path, WorldSaveKind world) =>
        EnsureWorld(path, WorldSaveLayout.DatabaseFileName(world));

    /// <summary>
    /// Pins GameType to PvP and replaces the file name of the connection string's Data Source, keeping its folder
    /// and options. A missing configuration is left to the server's defaults, as <see cref="EnsurePvP"/> does.
    /// </summary>
    public static bool EnsureWorld(string path, string databaseFileName)
    {
        if (!File.Exists(path))
            return true;

        string original = File.ReadAllText(path);
        string updated = SelectDatabase(PinPvP(original), databaseFileName);
        if (!string.Equals(original, updated, StringComparison.Ordinal))
            File.WriteAllText(path, updated);
        return true;
    }

    /// <summary>The save file the configuration names (file name only), or null when it names none.</summary>
    public static string? ReadDatabaseFileName(string path)
    {
        if (!File.Exists(path))
            return null;
        Match match = ConnectionString.Match(File.ReadAllText(path));
        string? source = match.Success ? WorldSaveLayout.DataSource(match.Groups[2].Value) : null;
        return string.IsNullOrWhiteSpace(source) ? null : Path.GetFileName(source);
    }

    private static string PinPvP(string original)
    {
        Match match = GameType.Match(original);
        if (!match.Success)
            throw new InvalidOperationException("The server configuration has no GameType element; refusing to guess its format.");
        return GameType.Replace(original, "$1PvP$3", 1);
    }

    private static string SelectDatabase(string original, string databaseFileName)
    {
        Match type = DatabaseType.Match(original);
        if (type.Success && !type.Groups[1].Value.Trim().Equals("SQLITE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only an SQLite save can be selected per world; the server configuration names another database type.");
        Match match = ConnectionString.Match(original);
        if (!match.Success)
            throw new InvalidOperationException("The server configuration has no DBConnectionString element; refusing to guess its format.");
        string connection = WorldSaveLayout.WithDatabaseFileName(match.Groups[2].Value, databaseFileName);
        return original[..match.Groups[2].Index] + connection + original[(match.Groups[2].Index + match.Groups[2].Length)..];
    }
}
