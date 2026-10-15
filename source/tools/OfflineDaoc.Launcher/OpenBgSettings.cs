using System.Globalization;
using System.Text.Json;

namespace OfflineDaoc.Launcher;

/// <summary>One open BG setting: its server property row, range, default and the one-line help of the launcher tab.</summary>
public sealed record OpenBgField(string Name, string RowKey, string Label, int Min, int Max, int Default, string Help, string Description)
{
    public bool IsSwitch => Name is "enabled" or "drift";
}

/// <summary>One realm's own crew row: open_bg_units_&lt;realm&gt;, text "8-man/small/solos" or empty for the global values.</summary>
public sealed record OpenBgUnitsField(string Name, string RowKey, string Label, string Help, string Description);

/// <summary>One text row of the open BG: mode, landing peace or the presets list, with the value the server uses when the row is empty.</summary>
public sealed record OpenBgTextField(string Name, string RowKey, string Label, string DefaultText, string Help, string Description);

/// <summary>
/// The open BG setup (Thidranki 252, spec 2026-10-11 §7) as the launcher edits it: the same open_bg_* server property
/// rows and ranges as the in-game /bgsetup command, eight numbers and switches plus each realm's own crew
/// (owner decision 2026-10-11 14:50). Values are clamped on the way in and out; an unreadable realm crew means the
/// global values, as on the server.
/// </summary>
public sealed record OpenBgSettings(bool Enabled, int Groups8, int Small, int Solos, int MaxBots, int CycleMin, int CycleMax, bool Drift)
{
    public const string Category = "pvp";

    /// <summary>Albion's own crew, "8-man/small/solos" such as "1/2/4"; empty for the global values.</summary>
    public string AlbionUnits { get; init; } = string.Empty;
    public string MidgardUnits { get; init; } = string.Empty;
    public string HiberniaUnits { get; init; } = string.Empty;

    /// <summary>Crew level band of the world bots that may crew the open BG (1-50); an old config without the rows is 40-50.</summary>
    public int BandMin { get; init; } = 40;
    public int BandMax { get; init; } = 50;

    /// <summary>The friend rule: "serverpvp" (Camlann rule, mixed crews) or "perrealm" (same realm is a friend in region 252).</summary>
    public string Mode { get; init; } = "serverpvp";

    /// <summary>Landing peace: "all", "realm" or empty (by mode).</summary>
    public string LandingPeace { get; init; } = string.Empty;

    /// <summary>Group presets as "std8:2,caster8:1"; empty means the group counts as before.</summary>
    public string Presets { get; init; } = string.Empty;

    public static readonly OpenBgTextField[] TextFields =
    [
        new("mode", "open_bg_mode", "Mode", "serverpvp",
            "Server PvP: friends are group, guild and battlegroup, crews mixed across realms. Per realm: the same realm is a friend inside region 252, crews per realm.",
            "Open BG mode: serverpvp (Camlann rule) or perrealm (classic DAoC rule inside region 252)"),
        new("peace", "open_bg_landing_peace", "Landing peace", string.Empty,
            "Whose landing peace holds at a landing circle. Auto: all realms in Server PvP, only the landing's own realm in Per realm.",
            "Open BG landing peace: all, realm, or empty (by mode)"),
        new("presets", "open_bg_presets", "Group presets", string.Empty,
            "Role presets as name:count, e.g. std8:2,caster8:1,raid8:1,stealth4:1,small:2,duo:0 (std8, caster8, raid8, stealth4, small, duo). Archetypes are matched by unit size; units of the same size cycle through the listed archetypes in proportion to their counts; the 8-man/small/solo targets still decide how many units exist; a size without a listed archetype is unchanged. Empty: the group counts as before.",
            "Open BG group presets as name:count, e.g. std8:2,caster8:1,small:2 (empty: the 8-man/small/solo counts as before)"),
    ];

    /// <summary>The mode as stored: "perrealm" or "serverpvp" (anything else is the server's default).</summary>
    public static string NormalizeMode(string? text) =>
        string.Equals(text?.Trim(), "perrealm", StringComparison.OrdinalIgnoreCase) ? "perrealm" : "serverpvp";

    /// <summary>The landing peace as stored: "all", "realm" or "" (auto).</summary>
    public static string NormalizePeace(string? text) => text?.Trim().ToLowerInvariant() switch { "all" => "all", "realm" => "realm", _ => string.Empty };

    /// <summary>
    /// A presets list as stored: empty or "off" gives "", otherwise every entry must be name:count (a name of letters and
    /// digits, a whole count) and is joined by commas without spaces. False for any other text. Which names exist is the
    /// server's catalog; it skips an unknown name.
    /// </summary>
    public static bool TryNormalizePresets(string? text, out string normalized)
    {
        normalized = string.Empty;
        string trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Equals("off", StringComparison.OrdinalIgnoreCase)) return true;
        var entries = new List<string>();
        foreach (string part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] pair = part.Split(':', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || pair[0].Length == 0 || !pair[0].All(char.IsLetterOrDigit) ||
                !int.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out int count))
                return false;
            entries.Add(string.Create(CultureInfo.InvariantCulture, $"{pair[0].ToLowerInvariant()}:{count}"));
        }
        if (entries.Count == 0) return false;
        normalized = string.Join(",", entries);
        return true;
    }

    private static string NormalizePresets(string? text) => TryNormalizePresets(text, out string normalized) ? normalized : string.Empty;

    public static readonly OpenBgUnitsField[] UnitFields =
    [
        new("albion", "open_bg_units_albion", "Albion units",
            "Albion's own crew as 8-man/small/solos, e.g. 1/2/4 (0-4, 0-6, 0-12). Empty: the three per-realm values above.",
            "Albion's open BG crew as 8-man/small/solos, e.g. 1/2/4 (empty: open_bg_groups8, open_bg_groups_small and open_bg_solos)"),
        new("midgard", "open_bg_units_midgard", "Midgard units",
            "Midgard's own crew as 8-man/small/solos, e.g. 1/2/4 (0-4, 0-6, 0-12). Empty: the three per-realm values above.",
            "Midgard's open BG crew as 8-man/small/solos, e.g. 1/2/4 (empty: open_bg_groups8, open_bg_groups_small and open_bg_solos)"),
        new("hibernia", "open_bg_units_hibernia", "Hibernia units",
            "Hibernia's own crew as 8-man/small/solos, e.g. 1/2/4 (0-4, 0-6, 0-12). Empty: the three per-realm values above.",
            "Hibernia's open BG crew as 8-man/small/solos, e.g. 1/2/4 (empty: open_bg_groups8, open_bg_groups_small and open_bg_solos)"),
    ];

    /// <summary>A realm's units text by field name (albion, midgard, hibernia).</summary>
    public string UnitsOf(string name) => name switch
    {
        "albion" => AlbionUnits,
        "midgard" => MidgardUnits,
        "hibernia" => HiberniaUnits,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown realm units field."),
    };

    /// <summary>These settings with one realm's units text.</summary>
    public OpenBgSettings WithUnits(string name, string text) => name switch
    {
        "albion" => this with { AlbionUnits = text ?? string.Empty },
        "midgard" => this with { MidgardUnits = text ?? string.Empty },
        "hibernia" => this with { HiberniaUnits = text ?? string.Empty },
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown realm units field."),
    };

    /// <summary>
    /// A realm crew as stored: empty (or "global") gives "", three whole numbers separated by '/' give them clamped
    /// ("9/1/2" gives "4/1/2"). False for any other text.
    /// </summary>
    public static bool TryNormalizeUnits(string? text, out string normalized)
    {
        normalized = string.Empty;
        string trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Equals("global", StringComparison.OrdinalIgnoreCase)) return true;
        string[] parts = trimmed.Split('/');
        if (parts.Length != 3) return false;
        var values = new int[3];
        for (int index = 0; index < 3; index++)
            if (!int.TryParse(parts[index].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out values[index]))
                return false;
        normalized = string.Create(CultureInfo.InvariantCulture,
            $"{Clamp("groups8", values[0])}/{Clamp("small", values[1])}/{Clamp("solos", values[2])}");
        return true;
    }

    // Unreadable text means the global values, as the server reads it.
    private static string NormalizeUnits(string? text) => TryNormalizeUnits(text, out string normalized) ? normalized : string.Empty;

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
        new("band_min", "open_bg_band_min", "Crew level from", 1, 50, 40,
            "Lowest level of the world bots that may crew the open BG (1-50, not above the highest).",
            "Open BG crew level band, lowest level of the world bots that may crew it (1-50)"),
        new("band_max", "open_bg_band_max", "Crew level to", 1, 50, 50,
            "Highest level of the world bots that may crew the open BG (1-50, not below the lowest).",
            "Open BG crew level band, highest level of the world bots that may crew it (1-50)"),
    ];

    public static OpenBgSettings Defaults => new(false, 1, 2, 4, 72, 15, 45, true);

    public static OpenBgField Field(string name) => Fields.First(field => field.Name == name);

    /// <summary>Every number inside its range; the longest cycle never below the shortest.</summary>
    public OpenBgSettings Clamped()
    {
        int min = Clamp("cycle_min", CycleMin);
        int bandMin = Clamp("band_min", BandMin), bandMax = Clamp("band_max", BandMax);
        // Like the server: a minimum above the maximum is the default 40-50.
        if (bandMin > bandMax) (bandMin, bandMax) = (Field("band_min").Default, Field("band_max").Default);
        return this with
        {
            BandMin = bandMin,
            BandMax = bandMax,
            Groups8 = Clamp("groups8", Groups8),
            Small = Clamp("small", Small),
            Solos = Clamp("solos", Solos),
            MaxBots = Clamp("max_bots", MaxBots),
            CycleMin = min,
            CycleMax = Math.Max(min, Clamp("cycle_max", CycleMax)),
            AlbionUnits = NormalizeUnits(AlbionUnits),
            MidgardUnits = NormalizeUnits(MidgardUnits),
            HiberniaUnits = NormalizeUnits(HiberniaUnits),
            Mode = NormalizeMode(Mode),
            LandingPeace = NormalizePeace(LandingPeace),
            Presets = NormalizePresets(Presets),
        };
    }

    public static int Clamp(string name, int value)
    {
        OpenBgField field = Field(name);
        return Math.Clamp(value, field.Min, field.Max);
    }

    /// <summary>
    /// The sixteen rows (row key, stored value): the ten fields in order, the realms' crews, then mode, landing peace and presets; clamped, switches as
    /// "True"/"False" like the server writes them.
    /// </summary>
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
            "band_min" => Number(value.BandMin),
            "band_max" => Number(value.BandMax),
            _ => Switch(value.Drift),
        })).Concat(UnitFields.Select(field => (field.RowKey, value.UnitsOf(field.Name))))
            .Concat(TextFields.Select(field => (field.RowKey, value.TextOf(field.Name)))).ToArray();
    }

    /// <summary>A text field's stored value by field name (mode, peace, presets).</summary>
    public string TextOf(string name) => name switch
    {
        "mode" => Mode,
        "peace" => LandingPeace,
        "presets" => Presets,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown open BG text field."),
    };

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
            ReadSwitch("drift", defaults.Drift))
        {
            BandMin = ReadNumber("band_min", defaults.BandMin),
            BandMax = ReadNumber("band_max", defaults.BandMax),
            AlbionUnits = saved.GetValueOrDefault("open_bg_units_albion") ?? string.Empty,
            MidgardUnits = saved.GetValueOrDefault("open_bg_units_midgard") ?? string.Empty,
            HiberniaUnits = saved.GetValueOrDefault("open_bg_units_hibernia") ?? string.Empty,
            Mode = saved.GetValueOrDefault("open_bg_mode") ?? string.Empty,
            LandingPeace = saved.GetValueOrDefault("open_bg_landing_peace") ?? string.Empty,
            Presets = saved.GetValueOrDefault("open_bg_presets") ?? string.Empty,
        }.Clamped();
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
        FromRows(ServerPropertyRows.Read(database, Fields.Select(field => field.RowKey).Concat(UnitFields.Select(field => field.RowKey)).Concat(TextFields.Select(field => field.RowKey))));

    /// <summary>Writes all fourteen rows through the launcher's server-property writer. Only while the server is stopped.</summary>
    public static void Save(string database, OpenBgSettings settings, Func<bool> serverStopped)
    {
        if (!serverStopped()) throw new InvalidOperationException("Stop the server before saving the open BG setup; while it runs, use /bgsetup in game.");
        IReadOnlyList<(string key, string value)> rows = ToRows(settings);
        ServerPropertyRow[] numbers = Fields.Select((field, index) =>
            new ServerPropertyRow(field.RowKey, field.Description, DefaultText(field), rows[index].value)).ToArray();
        ServerPropertyRow[] realms = UnitFields.Select((field, index) =>
            new ServerPropertyRow(field.RowKey, field.Description, string.Empty, rows[Fields.Length + index].value)).ToArray();
        ServerPropertyRow[] texts = TextFields.Select((field, index) =>
            new ServerPropertyRow(field.RowKey, field.Description, field.DefaultText, rows[Fields.Length + UnitFields.Length + index].value)).ToArray();
        ServerPropertyRows.Upsert(database, Category, numbers.Concat(realms).Concat(texts).ToArray(), serverStopped);
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
            var lines = new List<string>
            {
                $"Live ({Math.Max(0, (int)age.TotalSeconds)} s ago): open BG {state}. Phase: {phase}.",
                $"Keep 11: {Text(root, "KeepOwner") ?? "unknown"}.",
                $"Bots inside: {inside} (travelling {bots.Count(bot => bot.State == "travelling")}, leaving {bots.Count(bot => bot.State == "leaving")}), cap {cap}. " +
                $"Players inside: {players}. World speed {speed.ToString("0.0", CultureInfo.InvariantCulture)}x.",
            };
            lines.AddRange(RealmLines(root));
            return string.Join(Environment.NewLine, lines);
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    // Per realm: present/target units, whether the realm has its own setup, and its last form failures.
    private static IEnumerable<string> RealmLines(JsonElement root)
    {
        if (!root.TryGetProperty("Realms", out JsonElement realms) || realms.ValueKind != JsonValueKind.Array) yield break;
        foreach (JsonElement realm in realms.EnumerateArray())
        {
            string line = $"{Text(realm, "Realm") ?? "?"}: 8-man {Number(realm, "Groups8Present")}/{Number(realm, "Groups8")}, " +
                $"small {Number(realm, "SmallPresent")}/{Number(realm, "Small")}, solo {Number(realm, "SolosPresent")}/{Number(realm, "Solos")}" +
                (Bool(realm, "OwnSetup") ? " (own setup)" : string.Empty) + ".";
            if (realm.TryGetProperty("FormFailures", out JsonElement failures) && failures.ValueKind == JsonValueKind.Array)
                foreach (JsonElement failure in failures.EnumerateArray())
                    line += $" Last {KindLabel(Text(failure, "Kind"))} form failure ({Number(failure, "AgeSeconds") / 60} game min ago): {Text(failure, "Text") ?? "?"}.";
            yield return line;
        }
    }

    private static string KindLabel(string? kind) => kind switch { "group8" => "8-man", null => "?", _ => kind };

    private static int Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number) ? number : 0;

    private static bool Bool(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString() : null;
}
