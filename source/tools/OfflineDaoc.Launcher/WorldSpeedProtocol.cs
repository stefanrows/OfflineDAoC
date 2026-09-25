using System.Data.SQLite;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OfflineDaoc.Launcher;

internal sealed record WorldSpeedRequest(
    string SessionId,
    string RequestId,
    DateTime CreatedUtc,
    int Multiplier);

internal sealed record WorldSpeedStatus
{
    public string SessionId { get; init; } = string.Empty;
    public DateTime UpdatedUtc { get; init; }
    public DateTime SimulatedUtc { get; init; }
    public int SelectedMultiplier { get; init; }
    public int EffectiveMultiplier { get; init; }
    public double AchievedMultiplier { get; init; }
    public int ConnectedClients { get; init; }
    public double TickP95Ms { get; init; }
    public string? Error { get; init; }
}

internal sealed record WorldSimulationCheckpoint
{
    public int Version { get; init; }
    public DateTime SimulationUtc { get; init; }
    public DateTime WallUtc { get; init; }
}

internal sealed record WorldSimulationClockRead(
    WorldSimulationCheckpoint? Checkpoint,
    bool KeyPresent,
    string? Error);

internal static class WorldSpeedProtocol
{
    internal const string RequestFileName = "world-speed.request.json";
    internal const string StatusFileName = "world-speed.status.json";
    internal static readonly TimeSpan StatusMaxAge = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    internal static WorldSpeedStatus? ReadFreshStatus(string path, DateTime nowUtc, out string? unavailableReason)
    {
        unavailableReason = null;
        try
        {
            if (!File.Exists(path))
            {
                unavailableReason = "Waiting for live server status.";
                return null;
            }

            string json = File.ReadAllText(path);
            if (json.Length > 32_768)
            {
                unavailableReason = "World speed status is invalid.";
                return null;
            }

            WorldSpeedStatus? status = JsonSerializer.Deserialize<WorldSpeedStatus>(json, JsonOptions);
            if (status is null || string.IsNullOrWhiteSpace(status.SessionId) ||
                status.UpdatedUtc == default || status.SimulatedUtc == default ||
                !IsValidMultiplier(status.SelectedMultiplier) || !IsValidMultiplier(status.EffectiveMultiplier) ||
                status.ConnectedClients < 0 || !double.IsFinite(status.AchievedMultiplier) ||
                status.AchievedMultiplier < 0 || status.AchievedMultiplier > 3.05 ||
                !double.IsFinite(status.TickP95Ms) || status.TickP95Ms < 0)
            {
                unavailableReason = "World speed status is invalid.";
                return null;
            }

            DateTime updatedUtc = status.UpdatedUtc.ToUniversalTime();
            TimeSpan age = nowUtc.ToUniversalTime() - updatedUtc;
            if (age < TimeSpan.FromSeconds(-5) || age > StatusMaxAge)
            {
                unavailableReason = "Live world speed status is unavailable.";
                return null;
            }

            // Normalize timestamps before they are used for clock arithmetic.
            return status with
            {
                UpdatedUtc = updatedUtc,
                SimulatedUtc = status.SimulatedUtc.ToUniversalTime(),
                Error = string.IsNullOrWhiteSpace(status.Error) ? null : status.Error.Trim(),
            };
        }
        catch (IOException)
        {
            unavailableReason = "Live world speed status is unavailable.";
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            unavailableReason = "Unable to read live world speed status.";
            return null;
        }
        catch (JsonException)
        {
            unavailableReason = "World speed status is invalid.";
            return null;
        }
    }

    internal static void WriteRequest(string path, string sessionId, int multiplier, DateTime createdUtc)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("A live server session is required.", nameof(sessionId));
        if (!IsValidMultiplier(multiplier))
            throw new ArgumentOutOfRangeException(nameof(multiplier), "World speed must be 1×, 2×, or 3×.");

        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("The request path must have a parent directory.", nameof(path));
        Directory.CreateDirectory(directory);
        string temporaryPath = path + ".tmp";
        var request = new WorldSpeedRequest(sessionId, Guid.NewGuid().ToString("N"), createdUtc.ToUniversalTime(), multiplier);
        try
        {
            string json = JsonSerializer.Serialize(request, JsonOptions);
            File.WriteAllText(temporaryPath, json, new System.Text.UTF8Encoding(false));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    internal static bool IsValidMultiplier(int multiplier) => multiplier is 1 or 2 or 3;

    internal static DateTime? AdvanceLiveClock(WorldSpeedStatus? status, DateTime nowUtc)
    {
        if (status is null)
            return null;
        TimeSpan elapsed = nowUtc.ToUniversalTime() - status.UpdatedUtc.ToUniversalTime();
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        if (elapsed > StatusMaxAge)
            return null;
        long ticks = (long)Math.Round(elapsed.Ticks * Math.Clamp(status.AchievedMultiplier, 0d, 3d));
        return status.SimulatedUtc.ToUniversalTime().AddTicks(ticks);
    }
}

internal static class WorldSimulationClock
{
    internal const string OptionKey = "WorldSimulationClock";

    internal static WorldSimulationClockRead ReadCheckpoint(SQLiteConnection connection)
    {
        if (!TableExists(connection, "offline_local_options"))
            return new WorldSimulationClockRead(null, false, null);
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Value FROM offline_local_options WHERE Key=@key LIMIT 1";
            command.Parameters.AddWithValue("@key", OptionKey);
            object? value = command.ExecuteScalar();
            if (value is null || value == DBNull.Value)
                return new WorldSimulationClockRead(null, false, null);
            WorldSimulationCheckpoint? checkpoint = ParseCheckpoint(value.ToString());
            return checkpoint is null
                ? new WorldSimulationClockRead(null, true, "The saved simulation clock is invalid.")
                : new WorldSimulationClockRead(checkpoint, true, null);
        }
        catch (SQLiteException)
        {
            return new WorldSimulationClockRead(null, false, "Unable to read the saved simulation clock.");
        }
    }

    internal static WorldSimulationCheckpoint? ParseCheckpoint(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 8_192)
            return null;
        try
        {
            WorldSimulationCheckpoint? checkpoint = JsonSerializer.Deserialize<WorldSimulationCheckpoint>(json);
            if (checkpoint is not { Version: 1 } || checkpoint.SimulationUtc == default || checkpoint.WallUtc == default)
                return null;
            return checkpoint with
            {
                SimulationUtc = checkpoint.SimulationUtc.ToUniversalTime(),
                WallUtc = checkpoint.WallUtc.ToUniversalTime(),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static DateTime? AdvanceStoppedClock(WorldSimulationClockRead? savedClock, DateTime nowUtc)
    {
        DateTime wallNow = nowUtc.ToUniversalTime();
        if (savedClock is null || savedClock.Error is not null)
            return null;
        if (!savedClock.KeyPresent)
            return wallNow;
        WorldSimulationCheckpoint? checkpoint = savedClock.Checkpoint;
        if (checkpoint is null)
            return null;
        TimeSpan offlineElapsed = wallNow - checkpoint.WallUtc.ToUniversalTime();
        if (offlineElapsed < TimeSpan.Zero)
            offlineElapsed = TimeSpan.Zero;
        return checkpoint.SimulationUtc.ToUniversalTime().Add(offlineElapsed);
    }

    private static bool TableExists(SQLiteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@name LIMIT 1";
        command.Parameters.AddWithValue("@name", name);
        return command.ExecuteScalar() is not null;
    }
}
