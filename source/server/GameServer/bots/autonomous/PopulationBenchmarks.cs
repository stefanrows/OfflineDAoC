using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OfflineDaoc.Configuration;

public sealed record PopulationBenchmarkSample(int Tier, int ActiveBots, int Cores,
    long AvailableMemoryBytes, long WorkingSetBytes, double TickP95Ms,
    double TickBudgetMs, DateTime RecordedUtc);

/// <summary>Local observed capacity; no personal saves or machine identifier.</summary>
public sealed class PopulationBenchmarks
{
    public const string FileName = "population-benchmarks.json";
    public List<PopulationBenchmarkSample> Samples { get; set; } = [];
    public static int MemoryTier(long bytes) => (int)Math.Round(bytes / (4d * 1073741824));

    public static PopulationBenchmarks Load(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<PopulationBenchmarks>(File.ReadAllText(path)) ?? new()
        : new();

    public void Record(PopulationBenchmarkSample sample)
    {
        if (sample.Tier is not (500 or 1000 or 1500) || sample.ActiveBots < sample.Tier ||
            sample.Cores <= 0 || sample.AvailableMemoryBytes <= 0 ||
            sample.WorkingSetBytes <= 0 || !double.IsFinite(sample.TickP95Ms) ||
            sample.TickP95Ms <= 0 || !double.IsFinite(sample.TickBudgetMs) || sample.TickBudgetMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(sample));
        Samples.RemoveAll(old => old.Tier == sample.Tier && old.Cores == sample.Cores &&
            MemoryTier(old.AvailableMemoryBytes) == MemoryTier(sample.AvailableMemoryBytes));
        Samples.Add(sample);
    }

    public int? Recommended(int cores, long availableMemoryBytes)
    {
        PopulationBenchmarkSample[] hardware = Samples
            .Where(sample => sample.Cores == cores &&
                MemoryTier(sample.AvailableMemoryBytes) == MemoryTier(availableMemoryBytes))
            .ToArray();
        if (new[] { 500, 1000, 1500 }.Any(tier => hardware.All(sample => sample.Tier != tier)))
            return null;
        int recommended = 0;
        foreach (int tier in new[] { 500, 1000, 1500 })
        {
            PopulationBenchmarkSample sample = hardware.Single(candidate => candidate.Tier == tier);
            if (sample.TickP95Ms > sample.TickBudgetMs * .8 ||
                sample.WorkingSetBytes > sample.AvailableMemoryBytes * .7)
                break;
            recommended = tier;
        }
        return recommended;
    }

    public void Save(string path)
    {
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, full, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
