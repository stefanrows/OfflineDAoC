using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OfflineDaoc.Configuration;

/// <summary>Group shape mix of the Gothwaite PvP castle group former, in percent (total 100).</summary>
public sealed record GothwaitePvpGroupShares(int Solo, int Duo, int Small, int Eight)
{
    public int Total => Solo + Duo + Small + Eight;
}

/// <summary>
/// The Gothwaite PvP world mode switch (docs/GOTHWAITE_PVP_MODE.md section 7). Shared by
/// server and launcher. The launcher writes it while the server is stopped; the server
/// reads it once at start. A missing file means the mode is off.
/// </summary>
public sealed record GothwaitePvpSettings
{
    public const string FileName = "gothwaite-pvp.json";
    public const string GothwaiteArena = "gothwaite";

    public int Version { get; init; } = 1;
    public bool Enabled { get; init; }
    public string Arena { get; init; } = GothwaiteArena;
    public int SafeRadius { get; init; } = 4000;
    public int LowBandMaxLevel { get; init; } = 19;
    public int LowBandEdge { get; init; } = 20000;
    public int Edge { get; init; } = 30000;
    public GothwaitePvpGroupShares GroupShares { get; init; } = new(15, 15, 35, 35);
    public int LevelBand { get; init; } = 5;
    public int RestPercent { get; init; } = 80;
    /// <summary>
    /// The no-fight ring around the castle, in units beyond the safe circle (docs/GOTHWAITE_PVP_MODE.md section 6.2). A mode bot opens
    /// no fight with a target, or from a position, inside SafeRadius + GateRing, so the castle gate does not become a camp.
    /// </summary>
    public int GateRing { get; init; } = 1500;

    public static GothwaitePvpSettings Defaults => new();

    public void Validate()
    {
        if (Version != 1)
            throw new InvalidDataException("Unsupported Gothwaite PvP settings version.");
        if (!string.Equals(Arena, GothwaiteArena, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unknown Gothwaite PvP arena.");
        if (SafeRadius is < 1000 or > 8000)
            throw new InvalidDataException("The castle safe radius must be 1,000–8,000.");
        if (LowBandMaxLevel is < 1 or > 49 || LowBandEdge < SafeRadius + 1000 || Edge < LowBandEdge || Edge > 60000)
            throw new InvalidDataException("Arena edges must grow outward from the castle: safe radius < low-band edge <= edge <= 60,000.");
        if (GroupShares == null || GroupShares.Solo < 0 || GroupShares.Duo < 0 || GroupShares.Small < 0 ||
            GroupShares.Eight < 0 || GroupShares.Total != 100)
            throw new InvalidDataException("Group shares must be 0–100 and total exactly 100%.");
        if (LevelBand is < 1 or > 25)
            throw new InvalidDataException("The group level band must be 1–25.");
        if (RestPercent is < 10 or > 100)
            throw new InvalidDataException("The castle rest level must be 10–100%.");
        if (GateRing is < 0 or > 5000)
            throw new InvalidDataException("The gate ring must be 0–5,000 units.");
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        IgnoreReadOnlyProperties = true,
    };

    public static GothwaitePvpSettings Load(string path)
    {
        if (!File.Exists(path)) return Defaults;
        GothwaitePvpSettings settings = JsonSerializer.Deserialize<GothwaitePvpSettings>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Gothwaite PvP settings are empty.");
        settings.Validate();
        return settings;
    }

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    /// <summary>Writes the file atomically; refused unless the server is stopped before and at commit.</summary>
    public void Save(string path, Func<bool> serverStopped)
    {
        Validate();
        if (!serverStopped()) throw new InvalidOperationException("Stop the server before changing the world mode.");
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
            if (!serverStopped())
                throw new InvalidOperationException("The server started; the world mode was not saved.");
            File.Move(temporary, fullPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
