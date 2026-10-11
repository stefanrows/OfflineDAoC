using System.Data.SQLite;
using System.Globalization;
using System.Text.Json.Serialization;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Read-only data behind the Camlann Herald window. The save is opened with
/// Read Only=True, so the Herald works while the server runs and never writes.
/// This file holds plain data only (no UI types): rankings, guild totals,
/// statistics and title holders are computed here so the window and the unit
/// tests share the same numbers.
/// </summary>
public static class HeraldData
{
    public const int Albion = 1;
    public const int Midgard = 2;
    public const int Hibernia = 3;

    /// <summary>
    /// Realm points needed for each realm level, copied from
    /// GamePlayer.REALMPOINTS_FOR_LEVEL on the server. Realm rank = level / 10 + 1.
    /// </summary>
    public static readonly long[] RealmPointsForLevel =
    [
        0, 0, 25, 125, 350, 750,
        1375, 2275, 3500, 5100, 7125, 9625,
        12650, 16250, 20475, 25375, 31000, 37400,
        44625, 52725, 61750, 71750, 82775, 94875,
        108100, 122500, 138125, 155025, 173250, 192850,
        213875, 236375, 260400, 286000, 313225, 342125,
        372750, 405150, 439375, 475475, 513500, 553500,
        595525, 639625, 685850, 734250, 784875, 837775,
        893000, 950600, 1010625, 1073125, 1138150, 1205750,
        1275975, 1348875, 1424500, 1502900, 1584125, 1668225,
        1755250, 1845250, 1938275, 2034375, 2133600, 2236000,
        2341625, 2450525, 2562750, 2678350, 2797375, 2919875,
        3045900, 3175500, 3308725, 3445625, 3586250, 3730650,
        3878875, 4030975, 4187000, 4347000, 4511025, 4679125,
        4851350, 5027750, 5208375, 5393275, 5582500, 5776100,
        5974125, 6176625, 6383650, 6595250, 6811475, 7032375,
        7258000, 7488400, 7723625, 7963725, 8208750, 9111713,
        10114001, 11226541, 12461460, 13832221, 15353765, 17042680,
        18917374, 20998286, 23308097, 25871988, 28717906, 31876876,
        35383333, 39275499, 43595804, 48391343, 53714390, 59622973,
        66181501, 73461466, 81542227, 90511872, 100468178, 111519678,
        123786843, 137403395, 152517769, 169294723, 187917143,
    ];

    /// <summary>
    /// Titles from scripts/titles/PvPKillsTitle.cs and RealmKeepTitle.cs. Each
    /// range is [Minimum, MaximumExclusive); null means no upper bound.
    /// </summary>
    public static readonly IReadOnlyList<HeraldTitle> Titles =
    [
        new("Bane of Albion", "Albion player kills", HeraldTitleMetric.AlbionKills, 2000, 25000),
        new("Scourge of Albion", "Albion player kills", HeraldTitleMetric.AlbionKills, 25000, 100000),
        new("Slayer of Albion", "Albion player kills", HeraldTitleMetric.AlbionKills, 100000, null),
        new("Bane of Midgard", "Midgard player kills", HeraldTitleMetric.MidgardKills, 2000, 25000),
        new("Scourge of Midgard", "Midgard player kills", HeraldTitleMetric.MidgardKills, 25000, 100000),
        new("Slayer of Midgard", "Midgard player kills", HeraldTitleMetric.MidgardKills, 100000, null),
        new("Bane of Hibernia", "Hibernia player kills", HeraldTitleMetric.HiberniaKills, 2000, 25000),
        new("Scourge of Hibernia", "Hibernia player kills", HeraldTitleMetric.HiberniaKills, 25000, 100000),
        new("Slayer of Hibernia", "Hibernia player kills", HeraldTitleMetric.HiberniaKills, 100000, null),
        new("Master Soldier", "All player kills", HeraldTitleMetric.AllPlayerKills, 2000, 25000),
        new("Master Enforcer", "All player kills", HeraldTitleMetric.AllPlayerKills, 25000, 100000),
        new("Master Assassine", "All player kills", HeraldTitleMetric.AllPlayerKills, 100000, null),
        new("Battle Enforcer", "Death blows", HeraldTitleMetric.DeathBlows, 2000, 25000),
        new("Battle Master", "Death blows", HeraldTitleMetric.DeathBlows, 25000, null),
        new("Lone Enforcer", "Solo kills", HeraldTitleMetric.SoloKills, 2000, 25000),
        new("Duel Master", "Solo kills", HeraldTitleMetric.SoloKills, 25000, null),
        new("Frontier Challenger", "Keeps captured", HeraldTitleMetric.CapturedKeeps, 10, 50),
        new("Frontier Vindicator", "Keeps captured", HeraldTitleMetric.CapturedKeeps, 50, 500),
        new("Frontier Protector", "Keeps captured", HeraldTitleMetric.CapturedKeeps, 500, null),
        new("Stronghold Soldier", "Towers captured", HeraldTitleMetric.CapturedTowers, 100, 1000),
        new("Stronghold Chief", "Towers captured", HeraldTitleMetric.CapturedTowers, 1000, null),
    ];

    /// <summary>Level bands for the distribution chart, highest first.</summary>
    public static readonly IReadOnlyList<HeraldLevelBandDefinition> LevelBands =
    [
        new("50", 50, int.MaxValue),
        new("45–49", 45, 49),
        new("40–44", 40, 44),
        new("35–39", 35, 39),
        new("30–34", 30, 34),
        new("25–29", 25, 29),
        new("20–24", 20, 24),
        new("15–19", 15, 19),
        new("10–14", 10, 14),
        new("5–9", 5, 9),
        new("1–4", 1, 4),
    ];

    private static readonly string[] CombatColumns =
    [
        "KillsAlbionPlayers", "KillsMidgardPlayers", "KillsHiberniaPlayers",
        "KillsAlbionDeathBlows", "KillsMidgardDeathBlows", "KillsHiberniaDeathBlows",
        "KillsAlbionSolo", "KillsMidgardSolo", "KillsHiberniaSolo",
        "DeathsPvP",
    ];

    /// <summary>Class names for the ids used by the 1.65 realms (same table as MainForm).</summary>
    public static string ClassName(int classId) => classId switch
    {
        1 => "Paladin", 2 => "Armsman", 3 => "Scout", 4 => "Minstrel",
        5 => "Theurgist", 6 => "Cleric", 7 => "Wizard", 8 => "Sorcerer",
        9 => "Infiltrator", 10 => "Friar", 11 => "Mercenary", 12 => "Necromancer",
        13 => "Cabalist", 19 => "Reaver",
        21 => "Thane", 22 => "Warrior", 23 => "Shadowblade", 24 => "Skald",
        25 => "Hunter", 26 => "Healer", 27 => "Spiritmaster", 28 => "Shaman",
        29 => "Runemaster", 30 => "Bonedancer", 31 => "Berserker", 32 => "Savage",
        40 => "Eldritch", 41 => "Enchanter", 42 => "Mentalist", 43 => "Blademaster",
        44 => "Hero", 45 => "Champion", 46 => "Warden", 47 => "Druid",
        48 => "Bard", 49 => "Nightshade", 50 => "Ranger", 55 => "Animist",
        56 => "Valewalker",
        _ => $"Class {classId}",
    };

    public static string RealmName(int realm) => realm switch
    {
        Albion => "Albion",
        Midgard => "Midgard",
        Hibernia => "Hibernia",
        _ => "—",
    };

    /// <summary>
    /// Realm level for a realm point total, the same walk as
    /// GamePlayer.CalculateRealmLevelFromRPs: 0 RP is level 0, 25 RP is level 2.
    /// </summary>
    public static int RealmLevelFromRealmPoints(long realmPoints)
    {
        if (realmPoints <= 0)
            return 0;

        int level;
        for (level = RealmPointsForLevel.Length - 1; level > 0; level--)
        {
            if (RealmPointsForLevel[level] <= realmPoints)
                break;
        }

        return level;
    }

    /// <summary>Rank text in the in-game form: realm level 12 is "RR 2L2".</summary>
    public static string RealmRankText(int realmLevel)
    {
        int level = Math.Max(0, realmLevel);
        return $"RR {level / 10 + 1}L{level % 10}";
    }

    public static string RealmRankFromRealmPoints(long realmPoints) =>
        RealmRankText(RealmLevelFromRealmPoints(realmPoints));

    /// <summary>
    /// Reads the save at <paramref name="databasePath"/>. A missing file gives an
    /// empty snapshot and creates nothing. Missing tables or columns are tolerated
    /// and reported in <see cref="HeraldSnapshot.Coverage"/>.
    /// </summary>
    public static HeraldSnapshot Load(string databasePath)
    {
        DateTime generated = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
            return HeraldSnapshot.Empty(generated);

        using var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;Read Only=True;Pooling=False;Default Timeout=5");
        connection.Open();
        SchemaInfo schema = SchemaInfo.Read(connection,
            "DOLCharacters", "Account", "offline_world_bots", "player_companions", "Guild", "Keep", "Relic");

        List<HeraldGuild> guilds = ReadGuilds(connection, schema);
        Dictionary<string, string> guildNames = GuildNameIndex(guilds);
        List<HeraldCharacter> characters = ReadPlayers(connection, schema, guildNames);
        bool gamebotsRead = schema.HasTable("offline_world_bots") && schema.HasColumn("offline_world_bots", "Name");
        characters.AddRange(ReadGamebots(connection, schema, guildNames));
        bool companionsRead = schema.HasTable("player_companions") && schema.HasColumn("player_companions", "Name");
        characters.AddRange(ReadCompanions(connection, schema, guildNames));
        List<HeraldKeep> keeps = ReadKeeps(connection, schema);
        List<HeraldRelic> relics = ReadRelics(connection, schema);

        var coverage = new HeraldCoverage(
            Players: schema.HasTable("DOLCharacters") && schema.HasColumn("DOLCharacters", "Name"),
            Gamebots: gamebotsRead,
            Companions: companionsRead,
            GamebotKillCounters: gamebotsRead && schema.HasColumns("offline_world_bots", CombatColumns),
            CompanionKillCounters: companionsRead && schema.HasColumns("player_companions", CombatColumns),
            Guilds: schema.HasTable("Guild") && schema.HasColumn("Guild", "GuildID"),
            Keeps: schema.HasTable("Keep") && schema.HasColumn("Keep", "KeepID"),
            Relics: schema.HasTable("Relic") && schema.HasColumn("Relic", "RelicID"),
            Accounts: schema.HasTable("Account") && schema.HasColumn("Account", "PrivLevel"),
            CapturedCounters: schema.HasColumn("DOLCharacters", "CapturedKeeps") && schema.HasColumn("DOLCharacters", "CapturedTowers"));

        return new HeraldSnapshot(generated, true, characters, guilds, keeps, relics, coverage);
    }

    /// <summary>The value a character is ranked by. Null means the counter is not recorded.</summary>
    public static long? MetricValue(HeraldCharacter character, HeraldMetric metric) => metric switch
    {
        HeraldMetric.Kills => character.Combat.Kills,
        HeraldMetric.Deaths => character.Combat.Deaths,
        HeraldMetric.DeathBlows => character.Combat.DeathBlows,
        HeraldMetric.SoloKills => character.Combat.Solo,
        _ => character.RealmPoints,
    };

    public static string TypeTag(HeraldCharacter character) => character.Kind switch
    {
        HeraldKind.Gamebot => "Gamebot",
        HeraldKind.Companion => "Companion",
        _ => character.IsGameMaster ? "GM" : "Player",
    };

    /// <summary>
    /// Ranked characters. Characters hidden by /statsanon or /anonymous are left
    /// out, as the server's own top statistics do. Unrecorded counters sort last.
    /// </summary>
    public static IReadOnlyList<HeraldRankRow> Rankings(HeraldSnapshot snapshot, HeraldRankQuery query)
    {
        IEnumerable<HeraldCharacter> rows = snapshot.Characters.Where(character => !character.IsHidden);
        rows = query.Kind switch
        {
            HeraldKindFilter.Players => rows.Where(character => character.Kind == HeraldKind.Player),
            HeraldKindFilter.Gamebots => rows.Where(character => character.Kind == HeraldKind.Gamebot),
            HeraldKindFilter.Companions => rows.Where(character => character.Kind == HeraldKind.Companion),
            _ => rows,
        };
        if (query.Realm is Albion or Midgard or Hibernia)
            rows = rows.Where(character => character.Realm == query.Realm);
        if (!string.IsNullOrWhiteSpace(query.ClassName))
            rows = rows.Where(character => string.Equals(character.ClassName, query.ClassName, StringComparison.OrdinalIgnoreCase));
        string nameFilter = (query.NameFilter ?? string.Empty).Trim();
        if (nameFilter.Length > 0)
            rows = rows.Where(character => character.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase));

        List<HeraldCharacter> ordered = rows
            .OrderByDescending(character => MetricValue(character, query.Metric) ?? -1)
            .ThenByDescending(character => character.RealmPoints)
            .ThenBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        IEnumerable<HeraldCharacter> limited = query.Limit > 0 ? ordered.Take(query.Limit) : ordered;
        return limited.Select((character, index) => new HeraldRankRow(
            index + 1,
            character.Name,
            TypeTag(character),
            RealmName(character.Realm),
            character.ClassName,
            character.Level,
            character.GuildName,
            character.RealmPoints,
            character.RealmRank,
            character.Combat.Kills,
            character.Combat.Deaths,
            character.Combat.DeathBlows,
            character.Combat.Solo,
            character)).ToList();
    }

    /// <summary>
    /// Guilds with at least one visible member or one claimed keep. Members are
    /// counted across players, gamebots and companions by GuildID.
    /// </summary>
    public static IReadOnlyList<HeraldGuildRow> GuildRows(HeraldSnapshot snapshot)
    {
        Dictionary<string, List<HeraldCharacter>> members = snapshot.Characters
            .Where(character => !character.IsHidden && character.GuildId.Length > 0)
            .GroupBy(character => character.GuildId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var rows = new List<HeraldGuildRow>();
        foreach (HeraldGuild guild in snapshot.Guilds)
        {
            members.TryGetValue(guild.GuildId, out List<HeraldCharacter>? list);
            list ??= [];
            int keeps = snapshot.Keeps.Count(keep => keep.ClaimedGuildName.Length > 0 &&
                string.Equals(keep.ClaimedGuildName, guild.Name, StringComparison.OrdinalIgnoreCase));
            if (list.Count == 0 && keeps == 0)
                continue;
            rows.Add(new HeraldGuildRow(
                guild.Name,
                RealmName(guild.Realm),
                list.Count,
                list.Count(character => character.Kind == HeraldKind.Player),
                list.Count(character => character.Kind == HeraldKind.Gamebot),
                list.Count(character => character.Kind == HeraldKind.Companion),
                list.Sum(character => character.RealmPoints),
                guild.RealmPoints,
                keeps));
        }

        return rows
            .OrderByDescending(row => row.MemberRealmPoints)
            .ThenByDescending(row => row.Members)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<HeraldKeepRow> KeepRows(HeraldSnapshot snapshot) => snapshot.Keeps
        .OrderBy(keep => (keep.Realm is Albion or Midgard or Hibernia) ? keep.Realm : 4)
        .ThenByDescending(keep => keep.Level)
        .ThenBy(keep => keep.Name, StringComparer.OrdinalIgnoreCase)
        .Select(keep => new HeraldKeepRow(
            keep.KeepId,
            keep.Name,
            keep.Realm,
            (keep.Realm is Albion or Midgard or Hibernia) ? RealmName(keep.Realm) : "Unclaimed",
            RealmName(keep.OriginalRealm),
            keep.Level,
            keep.ClaimedGuildName.Length == 0 ? "—" : keep.ClaimedGuildName))
        .ToList();

    public static IReadOnlyList<HeraldRelicRow> RelicRows(HeraldSnapshot snapshot)
    {
        Dictionary<int, string> keepNames = snapshot.Keeps
            .GroupBy(keep => keep.KeepId)
            .ToDictionary(group => group.Key, group => group.First().Name);
        return snapshot.Relics
            .OrderBy(relic => relic.RelicId)
            .Select(relic => new HeraldRelicRow(
                relic.RelicId,
                relic.RelicType switch { 0 => "Strength", 1 => "Magic", _ => "Unknown" },
                RealmName(relic.OriginalRealm),
                RealmName(relic.Realm),
                relic.Realm,
                keepNames.TryGetValue(relic.KeepId, out string? keepName) ? keepName : "—",
                relic.LastCaptureDate is DateTime captured
                    ? captured.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                    : "—"))
            .ToList();
    }

    public static long? TitleValue(HeraldCharacter character, HeraldTitleMetric metric) => metric switch
    {
        HeraldTitleMetric.AlbionKills => character.Combat.KillsAlbion,
        HeraldTitleMetric.MidgardKills => character.Combat.KillsMidgard,
        HeraldTitleMetric.HiberniaKills => character.Combat.KillsHibernia,
        HeraldTitleMetric.AllPlayerKills => character.Combat.Kills,
        HeraldTitleMetric.DeathBlows => character.Combat.DeathBlows,
        HeraldTitleMetric.SoloKills => character.Combat.Solo,
        HeraldTitleMetric.CapturedKeeps => (long?)character.CapturedKeeps,
        HeraldTitleMetric.CapturedTowers => (long?)character.CapturedTowers,
        _ => null,
    };

    /// <summary>
    /// Holders of one title among visible characters. Titles are granted to real
    /// players in game, so the UI defaults to <see cref="HeraldKindFilter.Players"/>.
    /// </summary>
    public static IReadOnlyList<HeraldTitleHolder> TitleHolders(HeraldSnapshot snapshot, HeraldTitle title,
        HeraldKindFilter kinds, int realm)
    {
        var holders = new List<HeraldTitleHolder>();
        foreach (HeraldCharacter character in snapshot.Characters)
        {
            if (character.IsHidden)
                continue;
            if (kinds == HeraldKindFilter.Players && character.Kind != HeraldKind.Player)
                continue;
            if ((realm is Albion or Midgard or Hibernia) && character.Realm != realm)
                continue;
            if (TitleValue(character, title.Metric) is not long value || !title.Contains(value))
                continue;
            holders.Add(new HeraldTitleHolder(
                character.Name,
                TypeTag(character),
                RealmName(character.Realm),
                character.ClassName,
                character.Level,
                character.GuildName,
                value,
                character.RealmPoints,
                character.RealmRank));
        }

        return holders
            .OrderByDescending(holder => holder.Value)
            .ThenByDescending(holder => holder.RealmPoints)
            .ThenBy(holder => holder.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Visible class names, grouped by realm, for the class filter.</summary>
    public static IReadOnlyList<string> ClassNames(HeraldSnapshot snapshot) => snapshot.Characters
        .Where(character => !character.IsHidden)
        .GroupBy(character => character.ClassName, StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group.Min(character => character.Realm))
        .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.Key)
        .ToList();

    public static HeraldStatistics Statistics(HeraldSnapshot snapshot)
    {
        List<HeraldCharacter> visible = snapshot.Characters.Where(character => !character.IsHidden).ToList();
        int[] realms = [Albion, Midgard, Hibernia];
        List<HeraldRealmBalance> balance = realms.Select(realm =>
        {
            List<HeraldCharacter> inRealm = visible.Where(character => character.Realm == realm).ToList();
            return new HeraldRealmBalance(
                realm,
                RealmName(realm),
                inRealm.Count,
                inRealm.Sum(character => character.RealmPoints),
                snapshot.Keeps.Count(keep => keep.Realm == realm),
                inRealm.Count(character => character.Level >= 50));
        }).ToList();
        List<HeraldRealmClasses> classes = realms.Select(realm =>
        {
            List<HeraldCharacter> inRealm = visible.Where(character => character.Realm == realm).ToList();
            List<HeraldClassCount> counts = inRealm
                .GroupBy(character => character.ClassName, StringComparer.OrdinalIgnoreCase)
                .Select(group => new HeraldClassCount(group.Key, group.Count()))
                .OrderByDescending(count => count.Count)
                .ThenBy(count => count.ClassName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new HeraldRealmClasses(realm, RealmName(realm), inRealm.Count, counts);
        }).ToList();
        List<HeraldLevelBand> levels = LevelBands.Select(band => new HeraldLevelBand(
            band.Label,
            visible.Count(character => character.Level >= band.Minimum && character.Level <= band.Maximum))).ToList();

        return new HeraldStatistics(
            visible.Count,
            visible.Count(character => character.Kind == HeraldKind.Player),
            visible.Count(character => character.Kind == HeraldKind.Gamebot),
            visible.Count(character => character.Kind == HeraldKind.Companion),
            snapshot.Characters.Count(character => character.IsHidden),
            visible.Sum(character => character.RealmPoints),
            balance,
            classes,
            levels);
    }

    private static Dictionary<string, string> GuildNameIndex(IEnumerable<HeraldGuild> guilds)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (HeraldGuild guild in guilds)
            names.TryAdd(guild.GuildId, guild.Name);
        return names;
    }

    private static List<HeraldGuild> ReadGuilds(SQLiteConnection connection, SchemaInfo schema)
    {
        const string table = "Guild";
        var rows = new List<HeraldGuild>();
        if (!schema.HasTable(table) || !schema.HasColumn(table, "GuildID") || !schema.HasColumn(table, "GuildName"))
            return rows;

        string sql = $"SELECT {Field(schema, table, "g", "GuildID")}, {Field(schema, table, "g", "GuildName")}, " +
                     $"{Field(schema, table, "g", "Realm")}, {Field(schema, table, "g", "RealmPoints")} FROM [{table}] g";
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string guildId = ReadText(reader, 0);
            if (guildId.Length == 0)
                continue;
            rows.Add(new HeraldGuild(guildId, ReadText(reader, 1), ReadInt(reader, 2) ?? 0, ReadLong(reader, 3) ?? 0));
        }

        return rows;
    }

    private static List<HeraldCharacter> ReadPlayers(SQLiteConnection connection, SchemaInfo schema,
        IReadOnlyDictionary<string, string> guildNames)
    {
        const string table = "DOLCharacters";
        var rows = new List<HeraldCharacter>();
        if (!schema.HasTable(table) || !schema.HasColumn(table, "Name"))
            return rows;

        bool hasAccounts = schema.HasTable("Account") && schema.HasColumn("Account", "Name") && schema.HasColumn("Account", "PrivLevel");
        string join = hasAccounts ? "LEFT JOIN [Account] a ON a.[Name] = c.[AccountName]" : string.Empty;
        string sql = "SELECT " + string.Join(",",
            Field(schema, table, "c", "DOLCharacters_ID"),
            "c.[Name]",
            Field(schema, table, "c", "AccountName"),
            Field(schema, table, "c", "Level"),
            Field(schema, table, "c", "Class"),
            Field(schema, table, "c", "Realm"),
            Field(schema, table, "c", "GuildID"),
            Field(schema, table, "c", "RealmPoints"),
            Field(schema, table, "c", "RealmLevel"),
            CombatSql(schema, table, "c"),
            Field(schema, table, "c", "CapturedKeeps"),
            Field(schema, table, "c", "CapturedTowers"),
            Field(schema, table, "c", "IgnoreStatistics"),
            Field(schema, table, "c", "IsAnonymous"),
            hasAccounts ? "a.[PrivLevel]" : "NULL")
            + $" FROM [{table}] c {join}";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string name = ReadText(reader, 1);
            if (name.Length == 0)
                continue;
            string id = ReadText(reader, 0);
            int classId = ReadInt(reader, 4) ?? 0;
            long realmPoints = ReadLong(reader, 7) ?? 0;
            string guildId = ReadText(reader, 6);
            rows.Add(new HeraldCharacter(
                Id: "player:" + (id.Length > 0 ? id : name),
                Kind: HeraldKind.Player,
                Name: name,
                Realm: ReadInt(reader, 5) ?? 0,
                ClassId: classId,
                ClassName: ClassName(classId),
                Level: ReadInt(reader, 3) ?? 0,
                GuildId: guildId,
                GuildName: GuildNameFor(guildNames, guildId),
                RealmPoints: realmPoints,
                RealmLevel: ReadInt(reader, 8) ?? RealmLevelFromRealmPoints(realmPoints),
                IsGameMaster: (ReadLong(reader, 23) ?? 0) > 1,
                IsHidden: (ReadLong(reader, 21) ?? 0) != 0 || (ReadLong(reader, 22) ?? 0) != 0,
                OwnerName: string.Empty,
                OwnerMissing: false,
                CapturedKeeps: ReadInt(reader, 19),
                CapturedTowers: ReadInt(reader, 20),
                Combat: ReadCombat(reader, 9)));
        }

        return rows;
    }

    private static List<HeraldCharacter> ReadGamebots(SQLiteConnection connection, SchemaInfo schema,
        IReadOnlyDictionary<string, string> guildNames)
    {
        const string table = "offline_world_bots";
        var rows = new List<HeraldCharacter>();
        if (!schema.HasTable(table) || !schema.HasColumn(table, "Name"))
            return rows;

        string where = schema.HasColumn(table, "IsRetired") ? "WHERE COALESCE(b.[IsRetired], 0) = 0" : string.Empty;
        string sql = "SELECT " + string.Join(", ",
            Field(schema, table, "b", "BotId"),
            "b.[Name]",
            Field(schema, table, "b", "Realm"),
            Field(schema, table, "b", "ClassId"),
            Field(schema, table, "b", "ClassName"),
            Field(schema, table, "b", "Level"),
            Field(schema, table, "b", "GuildId"),
            Field(schema, table, "b", "RealmPoints"),
            CombatSql(schema, table, "b"))
            + $" FROM [{table}] b {where}";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string name = ReadText(reader, 1);
            if (name.Length == 0)
                continue;
            int classId = ReadInt(reader, 3) ?? 0;
            long realmPoints = ReadLong(reader, 7) ?? 0;
            string guildId = ReadText(reader, 6);
            rows.Add(new HeraldCharacter(
                Id: "gamebot:" + (ReadLong(reader, 0)?.ToString(CultureInfo.InvariantCulture) ?? name),
                Kind: HeraldKind.Gamebot,
                Name: name,
                Realm: ReadInt(reader, 2) ?? 0,
                ClassId: classId,
                ClassName: ResolveClassName(classId, ReadText(reader, 4)),
                Level: ReadInt(reader, 5) ?? 0,
                GuildId: guildId,
                GuildName: GuildNameFor(guildNames, guildId),
                RealmPoints: realmPoints,
                RealmLevel: RealmLevelFromRealmPoints(realmPoints),
                IsGameMaster: false,
                IsHidden: false,
                OwnerName: string.Empty,
                OwnerMissing: false,
                CapturedKeeps: null,
                CapturedTowers: null,
                Combat: ReadCombat(reader, 8)));
        }

        return rows;
    }

    private static List<HeraldCharacter> ReadCompanions(SQLiteConnection connection, SchemaInfo schema,
        IReadOnlyDictionary<string, string> guildNames)
    {
        const string table = "player_companions";
        var rows = new List<HeraldCharacter>();
        if (!schema.HasTable(table) || !schema.HasColumn(table, "Name"))
            return rows;

        bool hasOwners = schema.HasTable("DOLCharacters") && schema.HasColumn("DOLCharacters", "DOLCharacters_ID")
            && schema.HasColumn("DOLCharacters", "Name") && schema.HasColumn(table, "OwnerCharacterId");
        string ownerName = hasOwners
            ? "(SELECT o.[Name] FROM [DOLCharacters] o WHERE o.[DOLCharacters_ID] = pc.[OwnerCharacterId] LIMIT 1)"
            : "NULL";
        string sql = "SELECT " + string.Join(", ",
            Field(schema, table, "pc", "CompanionId"),
            "pc.[Name]",
            Field(schema, table, "pc", "Realm"),
            Field(schema, table, "pc", "ClassId"),
            Field(schema, table, "pc", "Level"),
            Field(schema, table, "pc", "GuildId"),
            Field(schema, table, "pc", "RealmPoints"),
            CombatSql(schema, table, "pc"),
            ownerName)
            + $" FROM [{table}] pc";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string name = ReadText(reader, 1);
            if (name.Length == 0)
                continue;
            int classId = ReadInt(reader, 3) ?? 0;
            long realmPoints = ReadLong(reader, 6) ?? 0;
            string guildId = ReadText(reader, 5);
            string owner = ReadText(reader, 17);
            rows.Add(new HeraldCharacter(
                Id: "companion:" + ReadText(reader, 0),
                Kind: HeraldKind.Companion,
                Name: name,
                Realm: ReadInt(reader, 2) ?? 0,
                ClassId: classId,
                ClassName: ResolveClassName(classId, string.Empty),
                Level: ReadInt(reader, 4) ?? 0,
                GuildId: guildId,
                GuildName: GuildNameFor(guildNames, guildId),
                RealmPoints: realmPoints,
                RealmLevel: RealmLevelFromRealmPoints(realmPoints),
                IsGameMaster: false,
                IsHidden: false,
                OwnerName: owner,
                OwnerMissing: hasOwners && reader.IsDBNull(17),
                CapturedKeeps: null,
                CapturedTowers: null,
                Combat: ReadCombat(reader, 7)));
        }

        return rows;
    }

    private static List<HeraldKeep> ReadKeeps(SQLiteConnection connection, SchemaInfo schema)
    {
        const string table = "Keep";
        var rows = new List<HeraldKeep>();
        if (!schema.HasTable(table) || !schema.HasColumn(table, "KeepID"))
            return rows;

        string sql = "SELECT " + string.Join(", ",
            Field(schema, table, "k", "KeepID"),
            Field(schema, table, "k", "Name"),
            Field(schema, table, "k", "Realm"),
            Field(schema, table, "k", "OriginalRealm"),
            Field(schema, table, "k", "Level"),
            Field(schema, table, "k", "ClaimedGuildName"))
            + $" FROM [{table}] k";
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new HeraldKeep(
                ReadInt(reader, 0) ?? 0,
                ReadText(reader, 1),
                ReadInt(reader, 2) ?? 0,
                ReadInt(reader, 3) ?? 0,
                ReadInt(reader, 4) ?? 0,
                ReadText(reader, 5)));
        }

        return rows;
    }

    private static List<HeraldRelic> ReadRelics(SQLiteConnection connection, SchemaInfo schema)
    {
        const string table = "Relic";
        var rows = new List<HeraldRelic>();
        if (!schema.HasTable(table) || !schema.HasColumn(table, "RelicID"))
            return rows;

        string sql = "SELECT " + string.Join(", ",
            Field(schema, table, "r", "RelicID"),
            Field(schema, table, "r", "relicType"),
            Field(schema, table, "r", "Realm"),
            Field(schema, table, "r", "OriginalRealm"),
            Field(schema, table, "r", "KeepID"),
            Field(schema, table, "r", "LastCaptureDate"))
            + $" FROM [{table}] r";
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new HeraldRelic(
                ReadInt(reader, 0) ?? 0,
                ReadInt(reader, 1) ?? -1,
                ReadInt(reader, 2) ?? 0,
                ReadInt(reader, 3) ?? 0,
                ReadInt(reader, 4) ?? 0,
                ReadDate(reader, 5)));
        }

        return rows;
    }

    private static string GuildNameFor(IReadOnlyDictionary<string, string> guildNames, string guildId) =>
        guildId.Length > 0 && guildNames.TryGetValue(guildId, out string? name) ? name : string.Empty;

    private static string ResolveClassName(int classId, string recordedName)
    {
        string known = ClassName(classId);
        if (!known.StartsWith("Class ", StringComparison.Ordinal))
            return known;
        return recordedName.Length > 0 ? recordedName : known;
    }

    private static string Field(SchemaInfo schema, string table, string alias, string column) =>
        schema.HasColumn(table, column) ? $"{alias}.[{column}]" : "NULL";

    private static string CombatSql(SchemaInfo schema, string table, string alias) =>
        string.Join(", ", CombatColumns.Select(column => Field(schema, table, alias, column)));

    private static HeraldCombat ReadCombat(SQLiteDataReader reader, int start) => new(
        ReadLong(reader, start),
        ReadLong(reader, start + 1),
        ReadLong(reader, start + 2),
        ReadLong(reader, start + 3),
        ReadLong(reader, start + 4),
        ReadLong(reader, start + 5),
        ReadLong(reader, start + 6),
        ReadLong(reader, start + 7),
        ReadLong(reader, start + 8),
        ReadLong(reader, start + 9));

    private static long? ReadLong(SQLiteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;
        object value = reader.GetValue(ordinal);
        return value switch
        {
            long integer => integer,
            int integer => integer,
            double number => (long)number,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) => parsed,
            _ => null,
        };
    }

    private static int? ReadInt(SQLiteDataReader reader, int ordinal) =>
        ReadLong(reader, ordinal) is long value ? (int)value : null;

    private static string ReadText(SQLiteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;

    private static DateTime? ReadDate(SQLiteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;
        object value = reader.GetValue(ordinal);
        if (value is DateTime date)
            return date;
        return DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime parsed) ? parsed : null;
    }

    /// <summary>Which optional parts of the save were present when the snapshot was read.</summary>
    private sealed class SchemaInfo
    {
        private readonly Dictionary<string, HashSet<string>> _columns = new(StringComparer.OrdinalIgnoreCase);

        public static SchemaInfo Read(SQLiteConnection connection, params string[] tables)
        {
            var schema = new SchemaInfo();
            foreach (string table in tables)
            {
                using (var existence = connection.CreateCommand())
                {
                    existence.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = @name COLLATE NOCASE";
                    existence.Parameters.AddWithValue("@name", table);
                    if (existence.ExecuteScalar() is not string name)
                        continue;

                    var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using var pragma = connection.CreateCommand();
                    pragma.CommandText = $"PRAGMA table_info([{name}])";
                    using var reader = pragma.ExecuteReader();
                    while (reader.Read())
                        columns.Add(reader.GetString(1));
                    schema._columns[name] = columns;
                }
            }

            return schema;
        }

        public bool HasTable(string table) => _columns.ContainsKey(table);

        public bool HasColumn(string table, string column) =>
            _columns.TryGetValue(table, out HashSet<string>? columns) && columns.Contains(column);

        public bool HasColumns(string table, IEnumerable<string> columns) => columns.All(column => HasColumn(table, column));
    }
}

/// <summary>A character from the save: a real player, an autonomous gamebot or a persistent companion.</summary>
public sealed record HeraldCharacter(
    string Id,
    HeraldKind Kind,
    string Name,
    int Realm,
    int ClassId,
    string ClassName,
    int Level,
    string GuildId,
    string GuildName,
    long RealmPoints,
    int RealmLevel,
    bool IsGameMaster,
    bool IsHidden,
    string OwnerName,
    bool OwnerMissing,
    int? CapturedKeeps,
    int? CapturedTowers,
    HeraldCombat Combat)
{
    /// <summary>In-game rank text, for example "RR 3L3".</summary>
    public string RealmRank => HeraldData.RealmRankText(RealmLevel);
}

/// <summary>
/// Kill, death blow, solo and death counters. Null means the save does not record
/// that counter for this row yet: the UI shows it as a dash and sums count it as 0.
/// </summary>
public sealed record HeraldCombat(
    long? KillsAlbion,
    long? KillsMidgard,
    long? KillsHibernia,
    long? DeathBlowsAlbion,
    long? DeathBlowsMidgard,
    long? DeathBlowsHibernia,
    long? SoloAlbion,
    long? SoloMidgard,
    long? SoloHibernia,
    long? Deaths)
{
    /// <summary>All player kills across realms; null only when no realm counter is recorded.</summary>
    public long? Kills => Total(KillsAlbion, KillsMidgard, KillsHibernia);

    public long? DeathBlows => Total(DeathBlowsAlbion, DeathBlowsMidgard, DeathBlowsHibernia);

    public long? Solo => Total(SoloAlbion, SoloMidgard, SoloHibernia);

    public static long? Total(long? albion, long? midgard, long? hibernia) =>
        albion is null && midgard is null && hibernia is null
            ? null
            : (albion ?? 0) + (midgard ?? 0) + (hibernia ?? 0);
}

public sealed record HeraldGuild(string GuildId, string Name, int Realm, long RealmPoints);

public sealed record HeraldKeep(int KeepId, string Name, int Realm, int OriginalRealm, int Level, string ClaimedGuildName);

public sealed record HeraldRelic(int RelicId, int RelicType, int Realm, int OriginalRealm, int KeepId, DateTime? LastCaptureDate);

/// <summary>Which optional sources the snapshot found in the save. Used for footnotes, never for errors.</summary>
public sealed record HeraldCoverage(
    bool Players,
    bool Gamebots,
    bool Companions,
    bool GamebotKillCounters,
    bool CompanionKillCounters,
    bool Guilds,
    bool Keeps,
    bool Relics,
    bool Accounts,
    bool CapturedCounters)
{
    public static readonly HeraldCoverage None = new(false, false, false, false, false, false, false, false, false, false);
}

/// <summary>Plain snapshot of the save for the Herald. Serializes to JSON and holds no UI types.</summary>
public sealed record HeraldSnapshot(
    DateTime GeneratedUtc,
    bool DatabaseFound,
    IReadOnlyList<HeraldCharacter> Characters,
    IReadOnlyList<HeraldGuild> Guilds,
    IReadOnlyList<HeraldKeep> Keeps,
    IReadOnlyList<HeraldRelic> Relics,
    HeraldCoverage Coverage)
{
    public static HeraldSnapshot Empty(DateTime generatedUtc) =>
        new(generatedUtc, false, [], [], [], [], HeraldCoverage.None);
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HeraldKind { Player, Gamebot, Companion }

public enum HeraldKindFilter { All, Players, Gamebots, Companions }

public enum HeraldMetric { RealmPoints, Kills, Deaths, DeathBlows, SoloKills }

public enum HeraldTitleMetric { AlbionKills, MidgardKills, HiberniaKills, AllPlayerKills, DeathBlows, SoloKills, CapturedKeeps, CapturedTowers }

/// <summary>Query for the Rankings view. Limit 0 means every match.</summary>
public sealed record HeraldRankQuery(
    HeraldKindFilter Kind = HeraldKindFilter.All,
    int Realm = 0,
    HeraldMetric Metric = HeraldMetric.RealmPoints,
    int Limit = 100,
    string ClassName = "",
    string NameFilter = "");

public sealed record HeraldRankRow(
    int Position,
    string Name,
    string Type,
    string Realm,
    string ClassName,
    int Level,
    string Guild,
    long RealmPoints,
    string Rank,
    long? Kills,
    long? Deaths,
    long? DeathBlows,
    long? Solo,
    HeraldCharacter Character);

public sealed record HeraldGuildRow(
    string Name,
    string Realm,
    int Members,
    int Players,
    int Gamebots,
    int Companions,
    long MemberRealmPoints,
    long GuildRealmPoints,
    int KeepsClaimed);

public sealed record HeraldKeepRow(int KeepId, string Name, int OwnerRealm, string Owner, string OriginalRealm, int Level, string ClaimedBy);

public sealed record HeraldRelicRow(int RelicId, string Type, string OriginalRealm, string CurrentRealm, int CurrentRealmId, string Keep, string LastCaptured);

public sealed record HeraldTitle(string Name, string Group, HeraldTitleMetric Metric, long Minimum, long? MaximumExclusive)
{
    public bool Contains(long value) => value >= Minimum && (MaximumExclusive is not long maximum || value < maximum);

    /// <summary>Range text such as "2,000–24,999" or "100,000+".</summary>
    public string Threshold => MaximumExclusive is long maximum
        ? $"{Minimum.ToString("N0", CultureInfo.InvariantCulture)}–{(maximum - 1).ToString("N0", CultureInfo.InvariantCulture)}"
        : $"{Minimum.ToString("N0", CultureInfo.InvariantCulture)}+";
}

public sealed record HeraldTitleHolder(string Name, string Type, string Realm, string ClassName, int Level, string Guild, long Value, long RealmPoints, string Rank);

public sealed record HeraldLevelBandDefinition(string Label, int Minimum, int Maximum);

public sealed record HeraldLevelBand(string Label, int Count);

public sealed record HeraldRealmBalance(int Realm, string Name, int Characters, long RealmPoints, int KeepsOwned, int Level50);

public sealed record HeraldClassCount(string ClassName, int Count);

public sealed record HeraldRealmClasses(int Realm, string Name, int Total, IReadOnlyList<HeraldClassCount> Classes);

public sealed record HeraldStatistics(
    int Visible,
    int Players,
    int Gamebots,
    int Companions,
    int Hidden,
    long RealmPoints,
    IReadOnlyList<HeraldRealmBalance> Realms,
    IReadOnlyList<HeraldRealmClasses> ClassesByRealm,
    IReadOnlyList<HeraldLevelBand> Levels);
