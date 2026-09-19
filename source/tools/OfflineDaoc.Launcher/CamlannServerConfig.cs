using System.Text.RegularExpressions;

namespace OfflineDaoc.Launcher;

public static class CamlannServerConfig
{
    private static readonly Regex GameType = new(
        "(<GameType\\b[^>]*>)(.*?)(</GameType>)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public static bool EnsurePvP(string path)
    {
        if (!File.Exists(path))
            return true;

        string original = File.ReadAllText(path);
        Match match = GameType.Match(original);
        if (!match.Success)
            throw new InvalidOperationException("The server configuration has no GameType element; refusing to guess its format.");

        string updated = GameType.Replace(original, "$1PvP$3", 1);
        if (!string.Equals(original, updated, StringComparison.Ordinal))
            File.WriteAllText(path, updated);
        return true;
    }
}
