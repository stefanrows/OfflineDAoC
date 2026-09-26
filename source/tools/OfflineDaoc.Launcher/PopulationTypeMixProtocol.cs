using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using OfflineDaoc.Configuration;

namespace OfflineDaoc.Launcher;

internal sealed record PopulationTypeMixRequest(
    string SessionId,
    string RequestId,
    DateTime CreatedUtc,
    PlayerTypeMix Mix);

internal sealed record PopulationTypeMixStatus
{
    public string SessionId { get; init; } = string.Empty;
    public string RequestId { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public DateTime UpdatedUtc { get; init; }
    public PlayerTypeMix Mix { get; init; } = new(25, 10, 30, 15, 12, 8);
    public int RosterCount { get; init; }
    public int[] CurrentCounts { get; init; } = new int[6];
    public int[] TargetCounts { get; init; } = new int[6];
    public int ActivePending { get; init; }
    public string Error { get; init; } = string.Empty;
}

internal static class PopulationTypeMixProtocol
{
    internal const string RequestFileName = "population-mix.request.json";
    internal const string StatusFileName = "population-mix.status.json";
    private const int MaximumStatusBytes = 16_384;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal static PopulationTypeMixStatus? ReadStatus(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumStatusBytes)
                return null;
            PopulationTypeMixStatus? status = JsonSerializer.Deserialize<PopulationTypeMixStatus>(
                File.ReadAllText(path), JsonOptions);
            if (status == null || string.IsNullOrWhiteSpace(status.SessionId) || string.IsNullOrWhiteSpace(status.State) ||
                !Guid.TryParse(status.RequestId, out _) || status.UpdatedUtc == default ||
                status.CurrentCounts?.Length != 6 || status.TargetCounts?.Length != 6 ||
                status.RosterCount < 0 || status.ActivePending < 0 ||
                status.CurrentCounts.Any(count => count < 0) || status.TargetCounts.Any(count => count < 0) ||
                !(status.State.Equals("pending", StringComparison.OrdinalIgnoreCase) ||
                  status.State.Equals("applied", StringComparison.OrdinalIgnoreCase) ||
                  status.State.Equals("failed", StringComparison.OrdinalIgnoreCase)))
                return null;

            if (!status.State.Equals("failed", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    new BotGoalSettings { Preset = PopulationPreset.Custom, Mix = status.Mix }.Validate();
                }
                catch (Exception) { return null; }
                if (status.CurrentCounts.Sum() != status.RosterCount ||
                    status.TargetCounts.Sum() != status.RosterCount || status.ActivePending > status.RosterCount)
                    return null;
            }
            return status;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    internal static PopulationTypeMixRequest WriteRequest(string path, string sessionId,
        PlayerTypeMix mix, DateTime createdUtc)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Fresh live server status is required to apply a mix.", nameof(sessionId));
        new BotGoalSettings { Preset = PopulationPreset.Custom, Mix = mix }.Validate();
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var request = new PopulationTypeMixRequest(sessionId, Guid.NewGuid().ToString("N"),
            createdUtc.ToUniversalTime(), mix);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(request, JsonOptions),
                new System.Text.UTF8Encoding(false));
            File.Move(temporary, path, true);
            return request;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
