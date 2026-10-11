using System.Globalization;
using System.Text.Json;

namespace OfflineDaoc.Launcher;

/// <summary>One open BG setting: its server property row, range, default and the one-line help of the launcher tab.</summary>
public sealed record OpenBgField(string Name, string RowKey, string Label, int Min, int Max, int Default, string Help, string Description)
{
    public bool IsSwitch => Name is "enabled" or "drift";
}

/// <summary>
/// The open BG setup (Thidranki 252, spec 2026-10-11 §7) as the launcher edits it: the same eight open_bg_* server
/// property rows and ranges as the in-game /bgsetup command. Values are clamped on the way in and out.
/// </summary>
public sealed record OpenBgSettings(bool Enabled, int Groups8, int Small, int Solos, int MaxBots, int CycleMin, int CycleMax, bool Drift)
{
    public const string Category = "pvp";

    public static readonly OpenBgField[] Fields =
    [
        new("enabled", "open_bg_enabled", "Open BG on", 0, 1, 0,
            "Opens Thidranki 252 for levels 1-50 through the porters' [open BG] line and sends the bot crew; needs the battleground campaign and zone 252's navmesh.",
            "Open the permanently open battleground (Thidranki 252, levels 1-50) through the porters' [open BG] line; it also needs the battleground campaign and zone 252's navmesh"),
        new("groups8", "open_bg_groups8", "8-man groups per realm", 0, 4, 1,
            "Full eight-player bot groups per realm (0-4); they hold bridges, roam and storm the keep.",
            "Full 8-man bot groups per realm in the open BG (0-4)"),
        new("small", "open_bg_groups_small", "Small groups per realm", 0, 6, 2,
            "Bot groups of two to four per realm (0-6); the size is rolled when the group forms.",
            "Small bot groups (2-4 members) per realm in the open BG (0-6)"),
        new("solos", "open_bg_solos", "Solos per realm", 0, 12, 4,
            "Solo roamers and stealthers per realm (0-12); they lurk near the bridges.",
            "Solo bots (roamers and stealthers) per realm in the open BG (0-12)"),
        new("max_bots", "open_bg_max_bots", "Bot cap, all realms", 8, 120, 72,
            "Hard limit of bots inside, all realms together (8-120); the setup above stops filling at this cap.",
            "Most autonomous bots in the open BG at once, all realms together (8-120)"),
        new("cycle_min", "open_bg_cycle_min_minutes", "Shortest cycle (min)", 5, 120, 15,
            "Shortest quiet - push - keep fight - retreat cycle in game minutes (5-120, not above the longest).",
            "Shortest open BG cycle quiet - push - keep fight - retreat, in minutes (5-120, not above the maximum)"),
        new("cycle_max", "open_bg_cycle_max_minutes", "Longest cycle (min)", 5, 120, 45,
            "Longest cycle in game minutes (5-120, not below the shortest); a faster world speed shortens it in real time.",
            "Longest open BG cycle, in minutes (5-120, not below the minimum)"),
        new("drift", "open_bg_player_drift", "Player drift", 0, 1, 1,
            "Roamers and solos prefer field points within sight of a player; the keep cycle runs on regardless.",
            "Open BG roamers and solos prefer field points within sight of a player; the keep cycle runs independently"),
    ];

    public static OpenBgSettings Defaults => new(false, 1, 2, 4, 72, 15, 45, true);

    public static OpenBgField Field(string name) => Fields.First(field => field.Name == name);

    /// <summary>Every number inside its range; the longest cycle never below the shortest.</summary>
    public OpenBgSettings Clamped()
    {
        int min = Clamp("cycle_min", CycleMin);
        return this with
        {
            Groups8 = Clamp("groups8", Groups8),
            Small = Clamp("small", Small),
            Solos = Clamp("solos", Solos),
            MaxBots = Clamp("max_bots", MaxBots),
            CycleMin = min,
            CycleMax = Math.Max(min, Clamp("cycle_max", CycleMax)),
        };
    }

    public static int Clamp(string name, int value)
    {
        OpenBgField field = Field(name);
        return Math.Clamp(value, field.Min, field.Max);
    }

    /// <summary>The eight rows (row key, stored value) in field order, clamped; switches as "True"/"False" like the server writes them.</summary>
    public static IReadOnlyList<(string key, string value)> ToRows(OpenBgSettings settings)
    {
        OpenBgSettings value = (settings ?? Defaults).Clamped();
        return Fields.Select(field => (field.RowKey, field.Name switch
        {
            "enabled" => Switch(value.Enabled),
            "groups8" => Number(value.Groups8),
            "small" => Number(value.Small),
            "solos" => Number(value.Solos),
            "max_bots" => Number(value.MaxBots),
            "cycle_min" => Number(value.CycleMin),
            "cycle_max" => Number(value.CycleMax),
            _ => Switch(value.Drift),
        })).ToArray();
    }

    /// <summary>Settings from saved rows: a missing or unreadable row gives that field's default; numbers are clamped.</summary>
    public static OpenBgSettings FromRows(IEnumerable<(string key, string value)>? rows)
    {
        var saved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in rows ?? [])
            if (!string.IsNullOrWhiteSpace(key)) saved[key.Trim()] = value ?? string.Empty;
        OpenBgSettings defaults = Defaults;
        bool ReadSwitch(string name, bool fallback) =>
            saved.TryGetValue(Field(name).RowKey, out string? text) && TryParseSwitch(text, out bool on) ? on : fallback;
        int ReadNumber(string name, int fallback) =>
            saved.TryGetValue(Field(name).RowKey, out string? text) &&
            int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : fallback;
        return new OpenBgSettings(
            ReadSwitch("enabled", defaults.Enabled),
            ReadNumber("groups8", defaults.Groups8),
            ReadNumber("small", defaults.Small),
            ReadNumber("solos", defaults.Solos),
            ReadNumber("max_bots", defaults.MaxBots),
            ReadNumber("cycle_min", defaults.CycleMin),
            ReadNumber("cycle_max", defaults.CycleMax),
            ReadSwitch("drift", defaults.Drift)).Clamped();
    }

    public static bool TryParseSwitch(string? text, out bool on)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "true" or "1" or "on" or "yes": on = true; return true;
            case "false" or "0" or "off" or "no": on = false; return true;
            default: on = false; return false;
        }
    }

    /// <summary>Reads the open_bg_* rows of the save (read-only; missing table or rows give the defaults).</summary>
    public static OpenBgSettings Load(string database) =>
        FromRows(ServerPropertyRows.Read(database, Fields.Select(field => field.RowKey)));

    /// <summary>Writes all eight rows through the launcher's server-property writer. Only while the server is stopped.</summary>
    public static void Save(string database, OpenBgSettings settings, Func<bool> serverStopped)
    {
        if (!serverStopped()) throw new InvalidOperationException("Stop the server before saving the open BG setup; while it runs, use /bgsetup in game.");
        IReadOnlyList<(string key, string value)> rows = ToRows(settings);
        ServerPropertyRows.Upsert(database, Category, Fields.Select((field, index) =>
            new ServerPropertyRow(field.RowKey, field.Description, DefaultText(field), rows[index].value)).ToArray(), serverStopped);
    }

    private static string DefaultText(OpenBgField field) => field.IsSwitch ? Switch(field.Default == 1) : Number(field.Default);
    private static string Switch(bool on) => on ? "True" : "False";
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The live open BG state for the launcher tab, read from open-bg-status.json that the running server writes
/// every 15 seconds (the same snapshot /bgsetup shows in game).
/// </summary>
public static class OpenBgLiveStatus
{
    public const string FileName = "open-bg-status.json";
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);
    public const string Fallback = "Server running: see /bgsetup in game.";

    /// <summary>The summary lines of a fresh running snapshot, or null when the file is missing, stale or unreadable.</summary>
    public static string? Read(string path, DateTime nowUtc)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return Describe(reader.ReadToEnd(), nowUtc);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static string? Describe(string json, DateTime nowUtc)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("UpdatedUtc", out JsonElement updatedElement) || updatedElement.ValueKind != JsonValueKind.String ||
                !updatedElement.TryGetDateTimeOffset(out DateTimeOffset updated))
                return null;
            TimeSpan age = nowUtc - updated.UtcDateTime;
            if (age > MaxAge || age < TimeSpan.FromSeconds(-30) || !Bool(root, "Running")) return null;

            string state = !Bool(root, "CampaignEnabled") ? "closed (the battleground campaign is off)"
                : !Bool(root, "OpenEnabled") ? "off"
                : !Bool(root, "Ready") ? "on, NOT ready: " + (Text(root, "NotReadyReason") ?? "unknown")
                : "on and ready";
            string phase = Text(root, "Phase") ?? "n/a";
            string? attacker = Text(root, "Attacker");
            if (attacker != null) phase += ", attacker " + attacker;
            var bots = root.TryGetProperty("Bots", out JsonElement botArray) && botArray.ValueKind == JsonValueKind.Array
                ? botArray.EnumerateArray().Select(bot => (Realm: Text(bot, "Realm") ?? "?", State: Text(bot, "State") ?? "?")).ToArray()
                : [];
            string[] realms = ["Albion", "Midgard", "Hibernia"];
            string inside = string.Join(", ", realms.Select(realm => $"{realm} {bots.Count(bot => bot.Realm == realm && bot.State == "inside")}"));
            int players = root.TryGetProperty("Players", out JsonElement playerArray) && playerArray.ValueKind == JsonValueKind.Array
                ? playerArray.GetArrayLength() : 0;
            double speed = root.TryGetProperty("AchievedSpeed", out JsonElement speedElement) && speedElement.TryGetDouble(out double achieved) ? achieved : 0;
            int cap = root.TryGetProperty("Cap", out JsonElement capElement) && capElement.TryGetInt32(out int capValue) ? capValue : 0;
            return string.Join(Environment.NewLine,
                $"Live ({Math.Max(0, (int)age.TotalSeconds)} s ago): open BG {state}. Phase: {phase}.",
                $"Keep 11: {Text(root, "KeepOwner") ?? "unknown"}.",
                $"Bots inside: {inside} (travelling {bots.Count(bot => bot.State == "travelling")}, leaving {bots.Count(bot => bot.State == "leaving")}), cap {cap}. " +
                $"Players inside: {players}. World speed {speed.ToString("0.0", CultureInfo.InvariantCulture)}x.");
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static bool Bool(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString() : null;
}
