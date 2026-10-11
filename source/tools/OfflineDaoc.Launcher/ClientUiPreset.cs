using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Shares one character's quickbar macros as a preset. Spell slots are
/// class-specific and are never copied; every other line of the character
/// ini is kept exactly as the client wrote it.
/// </summary>
public static class ClientUiPreset
{
    public const string PresetFileName = "ui-preset.ini";

    private const string BackupSuffix = ".uipreset-backup";
    private const string CreatedSuffix = ".uipreset-created";
    private const string MacroSlotType = "52";
    private const string UserDatFileName = "user.dat";
    private static readonly string[] QuickbarSections = ["Quickbar", "Quickbar2", "Quickbar3"];
    private static readonly Regex CharacterFileName = new(@"^(?<name>.+)-[0-9]+\.ini$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex HotkeyKey = new(@"^Hotkey_[0-9]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string ProfileDirectory(string clientDirectory)
    {
        return ProfileDirectory(clientDirectory, DefaultProfilesRoot());
    }

    public static string ProfileDirectory(string clientDirectory, string profilesRoot)
    {
        return Path.Combine(profilesRoot, ClientDisplayPreferences.ResolveProfileName(clientDirectory));
    }

    public static IReadOnlyList<string> ListCharacterFiles(string profileDirectory)
    {
        if (!Directory.Exists(profileDirectory))
            return [];

        return new DirectoryInfo(profileDirectory)
            .EnumerateFiles()
            .Where(file => IsCharacterFileName(file.Name))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .ToList();
    }

    public static IReadOnlyList<string> Sanitize(IEnumerable<string> lines)
    {
        var result = new List<string>();
        bool inQuickbar = false;
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                inQuickbar = QuickbarSections.Contains(trimmed[1..^1].Trim(), StringComparer.OrdinalIgnoreCase);
            }
            else if (inQuickbar && IsSpellSlot(line))
            {
                continue;
            }

            result.Add(line);
        }
        return result;
    }

    public static void Save(string profileDirectory, string characterIniPath)
    {
        if (!File.Exists(characterIniPath))
            throw new FileNotFoundException("Character ini not found.", characterIniPath);

        string text = Encoding.Latin1.GetString(File.ReadAllBytes(characterIniPath));
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        IReadOnlyList<string> lines = Sanitize(text.Replace("\r\n", "\n").Split('\n'));

        Directory.CreateDirectory(profileDirectory);
        File.WriteAllBytes(PresetPath(profileDirectory), Encoding.Latin1.GetBytes(string.Join(newline, lines)));
    }

    public static bool HasPreset(string profileDirectory)
    {
        return File.Exists(PresetPath(profileDirectory));
    }

    public static void ApplyToCharacter(string profileDirectory, string characterIniPath)
    {
        string preset = PresetPath(profileDirectory);
        if (!File.Exists(preset))
            throw new InvalidOperationException("No UI preset has been saved for this profile.");

        BackupOriginal(characterIniPath, preset);
        File.Copy(preset, characterIniPath, overwrite: true);
    }

    public static int? ReadScreenWidth(string profileDirectory)
    {
        string userDat = Path.Combine(profileDirectory, UserDatFileName);
        if (!File.Exists(userDat))
            return null;

        foreach (string line in File.ReadLines(userDat))
        {
            int equals = line.IndexOf('=');
            if (equals < 0 || !line[..equals].Trim().Equals("screen_width", StringComparison.OrdinalIgnoreCase))
                continue;

            if (int.TryParse(line[(equals + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int width) && width > 0)
                return width;
        }
        return null;
    }

    public static string? InstallAsNewCharacterDefault(string clientDirectory, string profileDirectory)
    {
        string preset = PresetPath(profileDirectory);
        int? width = ReadScreenWidth(profileDirectory);
        if (!File.Exists(preset) || width is null)
            return null;

        string target = Path.Combine(clientDirectory, $"default{width.Value}.ini");
        if (!File.Exists(target))
            File.WriteAllBytes(target + CreatedSuffix, []);
        BackupOriginal(target, preset);
        File.Copy(preset, target, overwrite: true);
        return target;
    }

    public static void Clear(string clientDirectory, string profileDirectory)
    {
        if (Directory.Exists(clientDirectory))
        {
            foreach (string backup in Directory.EnumerateFiles(clientDirectory, "default*.ini" + BackupSuffix).ToList())
            {
                string original = backup[..^BackupSuffix.Length];
                File.Copy(backup, original, overwrite: true);
                File.Delete(backup);
            }

            foreach (string marker in Directory.EnumerateFiles(clientDirectory, "default*.ini" + CreatedSuffix).ToList())
            {
                File.Delete(marker[..^CreatedSuffix.Length]);
                File.Delete(marker);
            }
        }

        string preset = PresetPath(profileDirectory);
        if (File.Exists(preset))
            File.Delete(preset);
    }

    private static string PresetPath(string profileDirectory)
    {
        return Path.Combine(profileDirectory, PresetFileName);
    }

    private static string DefaultProfilesRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Electronic Arts",
            "Dark Age of Camelot");
    }

    private static bool IsCharacterFileName(string fileName)
    {
        Match match = CharacterFileName.Match(fileName);
        return match.Success && !match.Groups["name"].Value.StartsWith("default", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSpellSlot(string line)
    {
        int equals = line.IndexOf('=');
        if (equals < 0 || !HotkeyKey.IsMatch(line[..equals].Trim()))
            return false;

        string slotType = line[(equals + 1)..].Split(',')[0].Trim();
        return slotType != MacroSlotType;
    }

    // Keeps the first copy of a file the client wrote. A target that already
    // holds the preset is one we installed, so it is never backed up as "original".
    private static void BackupOriginal(string target, string preset)
    {
        string backup = target + BackupSuffix;
        if (File.Exists(target) && !File.Exists(backup) && !File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(preset)))
            File.Copy(target, backup);
    }
}
