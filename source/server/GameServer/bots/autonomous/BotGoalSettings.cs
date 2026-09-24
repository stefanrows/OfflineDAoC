using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OfflineDaoc.Configuration;

public enum PopulationPreset { Camlann2003, Peaceful, Bloodbath, KeepWars, Custom }
public enum PopulationDanger { Mild, Authentic, FullCamlann }
public enum PopulationWorldShape { FreshLaunch, Established }

public sealed record PlayerTypeMix(int Leveler, int Casual, int Hybrid, int Hunter, int Roamer, int KeepWarrior)
{
    public int Total => Leveler + Casual + Hybrid + Hunter + Roamer + KeepWarrior;
    public int[] Values => [Leveler, Casual, Hybrid, Hunter, Roamer, KeepWarrior];
}

public sealed record BotGoalLoadResult(BotGoalSettings Settings, bool MigratedFromV1,
    PopulationPreset MappedPreset);

// Shared by server and launcher. The file changes only while the server is stopped.
public sealed record BotGoalSettings
{
    public const string FileName = "bot-goals.json";
    public int Version { get; init; } = 2;
    public PopulationPreset Preset { get; init; } = PopulationPreset.Camlann2003;
    public required PlayerTypeMix Mix { get; init; }
    public PopulationDanger Danger { get; init; } = PopulationDanger.Authentic;
    public PopulationWorldShape WorldShape { get; init; } = PopulationWorldShape.FreshLaunch;
    public int AltJoinIntervalHours { get; init; } = 72;
    public int AltRosterCap { get; init; } = 5000;

    public static BotGoalSettings Defaults => ForPreset(PopulationPreset.Camlann2003);

    public static BotGoalSettings ForPreset(PopulationPreset preset) => preset switch
    {
        PopulationPreset.Camlann2003 => new() { Preset = preset, Mix = new(25, 10, 30, 15, 12, 8), Danger = PopulationDanger.Authentic },
        PopulationPreset.Peaceful => new() { Preset = preset, Mix = new(45, 20, 25, 3, 5, 2), Danger = PopulationDanger.Mild },
        PopulationPreset.Bloodbath => new() { Preset = preset, Mix = new(10, 5, 25, 30, 20, 10), Danger = PopulationDanger.FullCamlann },
        PopulationPreset.KeepWars => new() { Preset = preset, Mix = new(15, 5, 25, 10, 20, 25), Danger = PopulationDanger.Authentic },
        _ => throw new ArgumentOutOfRangeException(nameof(preset), "Custom requires an explicit type mix."),
    };

    public void Validate()
    {
        if (Version != 2 || !Enum.IsDefined(Preset) || !Enum.IsDefined(Danger) || !Enum.IsDefined(WorldShape))
            throw new InvalidDataException("Unsupported server population settings.");
        if (Mix == null || Mix.Values.Any(value => value < 0 || value > 100) || Mix.Total != 100)
            throw new InvalidDataException("Player-type percentages must be 0–100 and total exactly 100%.");
        if (AltJoinIntervalHours is < 0 or > 720 || AltRosterCap is < 0 or > 100000)
            throw new InvalidDataException("Alt join interval must be 0–720 hours and roster cap 0–100,000.");
        if (Preset != PopulationPreset.Custom)
        {
            BotGoalSettings standard = ForPreset(Preset);
            if (Mix != standard.Mix || Danger != standard.Danger)
                throw new InvalidDataException("Choose Custom before changing a preset's type mix or danger.");
        }
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        IgnoreReadOnlyProperties = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static BotGoalSettings Load(string path) => LoadDetailed(path).Settings;

    public static BotGoalLoadResult LoadDetailed(string path)
    {
        if (!File.Exists(path)) return new(Defaults, false, PopulationPreset.Camlann2003);
        string content = File.ReadAllText(path);
        using JsonDocument document = JsonDocument.Parse(content);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("Version", out JsonElement version) ||
            !version.TryGetInt32(out int number))
            throw new InvalidDataException("Server population settings have no valid version.");
        if (number == 1)
        {
            LegacyGoals legacy = JsonSerializer.Deserialize<LegacyGoals>(content, Json)
                ?? throw new InvalidDataException("Legacy bot goals are empty.");
            legacy.Validate();
            PopulationPreset mapped = NearestPreset(legacy);
            return new(ForPreset(mapped), true, mapped);
        }
        if (number != 2) throw new InvalidDataException("Unsupported server population settings version.");
        BotGoalSettings settings = JsonSerializer.Deserialize<BotGoalSettings>(content, Json)
            ?? throw new InvalidDataException("Server population settings are empty.");
        settings.Validate();
        return new(settings, false, settings.Preset);
    }

    public void Save(string path, Func<bool> serverStopped)
    {
        Validate();
        if (!serverStopped()) throw new InvalidOperationException("Stop the server before saving population settings.");
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, this, Json);
                file.Flush(true);
            }
            if (!serverStopped()) throw new InvalidOperationException("The server started; population settings were not saved.");
            File.Move(temporary, fullPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // v1 had goal percentages by level bracket. The launcher displays this
    // approximate preset mapping before replacing the file.
    private static PopulationPreset NearestPreset(LegacyGoals old)
    {
        (PopulationPreset Preset, LegacyWeights[] Rows)[] profiles =
        [
            (PopulationPreset.Camlann2003, [new(45, 40, 15), new(30, 45, 25), new(15, 35, 50)]),
            (PopulationPreset.Peaceful, [new(60, 37, 3), new(55, 40, 5), new(35, 50, 15)]),
            (PopulationPreset.Bloodbath, [new(30, 35, 35), new(15, 25, 60), new(5, 15, 80)]),
            (PopulationPreset.KeepWars, [new(40, 40, 20), new(25, 35, 40), new(10, 25, 65)]),
        ];
        LegacyWeights[] actual = [old.Levels1To19!, old.Levels20To49!, old.Level50!];
        return profiles.OrderBy(profile => Enumerable.Range(0, 3).Sum(index =>
                Math.Abs(actual[index].SoloPve - profile.Rows[index].SoloPve) +
                Math.Abs(actual[index].GroupPve - profile.Rows[index].GroupPve) +
                2 * Math.Abs(actual[index].RvR - profile.Rows[index].RvR)))
            .ThenBy(profile => profile.Preset).First().Preset;
    }

    private sealed record LegacyWeights(int SoloPve, int GroupPve, int RvR)
    {
        public void Validate()
        {
            if (SoloPve < 0 || GroupPve < 0 || RvR < 0 || SoloPve + GroupPve + RvR != 100)
                throw new InvalidDataException("Legacy bot goal percentages are invalid.");
        }
    }

    private sealed record LegacyGoals
    {
        public int Version { get; init; }
        public LegacyWeights? Levels1To19 { get; init; }
        public LegacyWeights? Levels20To49 { get; init; }
        public LegacyWeights? Level50 { get; init; }
        public void Validate()
        {
            if (Version != 1 || Levels1To19 == null || Levels20To49 == null || Level50 == null)
                throw new InvalidDataException("Legacy bot goals are incomplete.");
            Levels1To19.Validate(); Levels20To49.Validate(); Level50.Validate();
        }
    }
}
