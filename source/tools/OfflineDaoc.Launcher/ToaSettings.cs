using System.Globalization;

namespace OfflineDaoc.Launcher;

/// <summary>
/// One Trials of Atlantis server property the launcher edits: its row key and category (as the server declares it),
/// the label, the range for numbers, the code default, the short note beside the control, the longer tooltip help and
/// the row description for a new row.
/// </summary>
public sealed record ToaField(string Name, string RowKey, string Category, string Label, int Min, int Max, int Default, bool IsSwitch,
    string Note, string Help, string Description);

/// <summary>
/// The Trials of Atlantis block of the launcher's XP Settings tab (docs/TOA_LAUNCHER.md): six existing server
/// properties. A missing or unreadable row means the server's code default; numbers are clamped to the launcher's
/// range. Each change writes only its own row, and only while the server is stopped; the server reads it at the
/// next start.
/// </summary>
public sealed record ToaSettings(int AtlantisPlvl, bool LootInAtlantis, int CapBonusChance, int MlXpPerLevel, int ArtifactXpRate, bool DataPacks)
{
    /// <summary>Privilege level that lets every player use the Atlantis porter lines.</summary>
    public const int AtlantisOpenPlvl = 1;

    /// <summary>Privilege level no account has: the Atlantis lines are closed to everyone.</summary>
    public const int AtlantisClosedPlvl = 4;

    /// <summary>Every player sees the porters' Atlantis lines (privilege level 1 or below).</summary>
    public bool AtlantisOpen => AtlantisPlvl <= AtlantisOpenPlvl;

    public static readonly ToaField[] Fields =
    [
        new("atlantis", "atlantis_teleport_plvl", "pve", "Atlantis open to players", 1, 4, AtlantisOpenPlvl, true,
            "Porters show the Atlantis lines to every player. Off closes them to all (level 4).",
            "Players see the porters' [Atlantis], [Atlantis Havens] and [Atlantis Dungeons] lines. Off: the lines are closed to every account, GMs included (level 4).",
            "Minimum account privilege level for the porters' Atlantis lines (1 = every player, 4 = nobody)."),
        new("loot", "toa_loot_in_atlantis", "atlas_rog", "ToA loot in Atlantis", 0, 1, 1, true,
            "Random drops from kills inside Atlantis are ToA items. Outside: unchanged.",
            "Random drops from kills inside Atlantis are Trials of Atlantis items with stronger bonuses. Loot outside Atlantis never changes.",
            "Random loot from kills inside Atlantis is a Trials of Atlantis item: 15 % stronger bonuses, each bonus a ToA cap bonus with the chance rog_toa_item_chance. Outside Atlantis nothing changes."),
        new("cap_chance", "rog_toa_item_chance", "atlas_rog", "ToA CAP-BONUS CHANCE", 0, 100, 25, false,
            "Chance in % per bonus of a ToA drop to be a cap bonus. Default 25.",
            "Chance in % that each bonus of a ToA drop is a cap bonus (stat cap, hit point cap, power pool). Default 25.",
            "Chance in % that a bonus of a Trials of Atlantis random item (loot from kills inside Atlantis) is a ToA cap bonus"),
        new("ml_xp", "ml_xp_per_level", "progression", "ML XP PER MOB LEVEL", 0, 100, 10, false,
            "Per level of a killed level 45+ NPC. Default 10; 0 turns ML kill XP off.",
            "Master Level XP per level of a killed NPC of level 45+. Default 10 (about 53 level 60 kills per Master Level); 0 turns kill ML XP off.",
            "Master Level experience per level of a killed NPC of level 45 or higher, paid to every player who earned the kill. " +
            "A Master Level costs 32,000, so 10 means about 53 kills of a level 60 creature per Master Level. 0 turns kill experience off."),
        new("artifact_xp", "artifact_xp_rate", "pve", "ARTIFACT XP RATE", 0, 10000, 350, false,
            "DOL default 350. Lower is faster (35 = ten times faster); 0 stops artifact XP.",
            "Divisor of artifact XP: DOL default 350, lower is faster (35 is ten times faster), 0 stops artifact XP.",
            "Adjust the rate at which all artifacts gain xp.  Higher numbers mean slower XP gain. XP / this = result"),
        new("data_packs", "world_data_packs_enabled", "world", "World data packs enabled", 0, 1, 1, true,
            "Applies new world data packs once per save at startup, after a backup.",
            "Applies new world data packs (Atlantis mobs, travel, loot) once per save at startup, after a database backup. Off: no pack runs.",
            "Apply pending world data packs from the server's data-packs folder to the database at startup. Each pack runs once per save, in one transaction, after a database backup."),
    ];

    /// <summary>The server's code defaults: Atlantis open, ToA loot on, 25 %, 10 ML XP per level, rate 350, packs on.</summary>
    public static ToaSettings Defaults => new(AtlantisOpenPlvl, true, 25, 10, 350, true);

    public static ToaField Field(string name) => Fields.First(field => field.Name == name);

    public static int Clamp(string name, int value)
    {
        ToaField field = Field(name);
        return Math.Clamp(value, field.Min, field.Max);
    }

    /// <summary>A number field's value by name (cap_chance, ml_xp, artifact_xp).</summary>
    public int NumberOf(string name) => name switch
    {
        "cap_chance" => CapBonusChance,
        "ml_xp" => MlXpPerLevel,
        "artifact_xp" => ArtifactXpRate,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Not a ToA number field."),
    };

    /// <summary>A switch field's value by name (atlantis, loot, data_packs).</summary>
    public bool SwitchOf(string name) => name switch
    {
        "atlantis" => AtlantisOpen,
        "loot" => LootInAtlantis,
        "data_packs" => DataPacks,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Not a ToA switch field."),
    };

    /// <summary>
    /// Settings from saved rows. A missing or unreadable row shows the code default. The server creates a missing row
    /// with the code default; an unreadable value fails its parse and the server keeps the field's current value
    /// (mostly 0 or false, ml_xp_per_level 10), so such a row should be saved again from the launcher.
    /// numbers outside the launcher's range are clamped for display only (nothing is written back).
    /// </summary>
    public static ToaSettings FromRows(IEnumerable<(string key, string value)>? rows)
    {
        var saved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in rows ?? [])
            if (!string.IsNullOrWhiteSpace(key)) saved[key.Trim()] = value ?? string.Empty;
        // The server converts with Convert.ChangeType: bool.Parse for switches, int.Parse for numbers.
        bool ReadSwitch(string name) =>
            saved.TryGetValue(Field(name).RowKey, out string? text) && bool.TryParse(text.Trim(), out bool on) ? on : Field(name).Default == 1;
        int ReadNumber(string name) =>
            Clamp(name, saved.TryGetValue(Field(name).RowKey, out string? text) &&
                int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : Field(name).Default);
        return new ToaSettings(ReadNumber("atlantis"), ReadSwitch("loot"), ReadNumber("cap_chance"), ReadNumber("ml_xp"),
            ReadNumber("artifact_xp"), ReadSwitch("data_packs"));
    }

    /// <summary>Every row key of the block, in display order.</summary>
    public static IEnumerable<string> RowKeys => Fields.Select(item => item.RowKey);

    /// <summary>Reads the six rows of the save (read-only; a missing table or row gives the code default).</summary>
    public static ToaSettings Load(string database) => FromRows(ServerPropertyRows.Read(database, RowKeys));

    /// <summary>
    /// The row one control writes: the Atlantis switch as privilege level 1 (open) or 4 (closed), other switches as
    /// "True"/"False" like the server stores them, numbers clamped.
    /// </summary>
    public static ServerPropertyRow SwitchRow(string name, bool on)
    {
        ToaField field = Field(name);
        if (!field.IsSwitch) throw new ArgumentOutOfRangeException(nameof(name), name, "Not a ToA switch field.");
        string value = name == "atlantis" ? Number(on ? AtlantisOpenPlvl : AtlantisClosedPlvl) : Switch(on);
        return new ServerPropertyRow(field.RowKey, field.Description, DefaultText(field), value);
    }

    public static ServerPropertyRow NumberRow(string name, int value)
    {
        ToaField field = Field(name);
        if (field.IsSwitch) throw new ArgumentOutOfRangeException(nameof(name), name, "Not a ToA number field.");
        return new ServerPropertyRow(field.RowKey, field.Description, DefaultText(field), Number(Clamp(name, value)));
    }

    /// <summary>Writes one row in the field's own category. Only while the server is stopped (checked again before the commit).</summary>
    public static void Save(string database, string name, ServerPropertyRow row, Func<bool> serverStopped)
    {
        if (!serverStopped()) throw new InvalidOperationException("Stop the server before changing Trials of Atlantis settings.");
        ServerPropertyRows.Upsert(database, Field(name).Category, [row], serverStopped);
    }

    private static string DefaultText(ToaField field) =>
        field.Name == "atlantis" ? Number(field.Default) : field.IsSwitch ? Switch(field.Default == 1) : Number(field.Default);

    private static string Switch(bool on) => on ? "True" : "False";
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
