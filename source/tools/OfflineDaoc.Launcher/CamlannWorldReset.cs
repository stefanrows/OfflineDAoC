using System.Data.SQLite;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Performs the one-time conversion from the shared Normal save shape to the
/// fork's fresh Camlann world. The operation is deliberately launcher-owned so
/// the server can never decide to destroy a save while it is starting.
/// </summary>
public static class CamlannWorldReset
{
    public const string WorldModelKey = "WorldModel";
    public const string WorldModelValue = "Camlann-1";
    public const string ResetUtcKey = "CamlannWorldResetUtc";
    public const string DummyGuildName = "DummyGuildToMakePetsUntargetable";

    public sealed record Result(
        bool AlreadyApplied,
        int Characters,
        int Bots,
        int Guilds,
        int Keeps,
        int Relics,
        int EventRecords,
        string Backup,
        DateTime UpdatedUtc);

    private sealed record RelicHome(int RelicId, int OriginalRealm, int RelicType, int Region, int X, int Y, int Z, int Heading);

    public static string? ReadWorldModel(string database)
    {
        if (!File.Exists(database))
            return null;

        using var connection = Open(database, true);
        if (!TableExists(connection, "offline_local_options"))
            return null;

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM offline_local_options WHERE Key=@key LIMIT 1";
        command.Parameters.AddWithValue("@key", WorldModelKey);
        return command.ExecuteScalar()?.ToString();
    }

    public static Result Apply(
        string database,
        string backupDirectory,
        string localAccount,
        string? eventRecordsPath,
        Func<bool> serverStopped)
    {
        if (!File.Exists(database))
            throw new FileNotFoundException("World database is missing.", database);
        if (string.IsNullOrWhiteSpace(localAccount))
            throw new ArgumentException("The local account name is required.", nameof(localAccount));

        string? existingWorldModel = ReadWorldModel(database);
        if (string.Equals(existingWorldModel, WorldModelValue, StringComparison.Ordinal))
            return new(true, 0, 0, 0, 0, 0, 0, string.Empty, DateTime.MinValue);
        if (existingWorldModel is not null && !string.Equals(existingWorldModel, "Normal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"This save has an unsupported world marker: {existingWorldModel}.");

        RequireStopped(serverStopped);
        EnsureNoSQLiteSidecars(database);
        if (!string.IsNullOrWhiteSpace(eventRecordsPath))
            EnsureNoSQLiteSidecars(eventRecordsPath);

        DateTime now = DateTime.UtcNow;
        string backup = CreateBackup(database, backupDirectory, eventRecordsPath);
        int eventRecords = CountEventRecords(eventRecordsPath);

        int characters;
        int bots;
        int guilds;
        int keeps;
        int relics;

        using (var connection = Open(database, false))
        using (var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable))
        {
            EnsureLocalOptionsTable(connection, transaction);

            characters = Count(connection, transaction, "DOLCharacters");
            bots = Count(connection, transaction, "offline_world_bots");
            guilds = Count(connection, transaction, "Guild", "GuildName<>@dummy", ("@dummy", DummyGuildName));
            keeps = Count(connection, transaction, "Keep");
            relics = Count(connection, transaction, "Relic");

            RequireLocalAccount(connection, transaction, localAccount);
            DeletePlayerInventory(connection, transaction);
            foreach (string table in new[]
            {
                "DOLCharactersXCustomParam", "DOLCharactersBackupXCustomParam", "CharacterXDataQuest",
                "CharacterXMasterLevel", "CharacterXOneTimeDrop", "FactionAggroLevel", "PlayerXEffect",
                "Quest", "Task", "TimeXLevel", "SinglePermission", "DOLCharacters", "DOLCharactersBackup",
                "PlayerInfo", "PlayerBoats", "CraftedItem", "ItemUnique"
            })
                DeleteAll(connection, transaction, table);

            foreach (string table in new[]
            {
                "bot_settings", "bot_profiles", "offline_bot_commands", "offline_world_bots",
                "offline_auction_escrow", "offline_auction_ledger", "offline_auction_listings",
                "realm_exchange_sales"
            })
                DeleteAll(connection, transaction, table);

            foreach (string table in new[] { "AccountXCrafting", "AccountXMoney", "AccountXCustomParam" })
                DeleteAll(connection, transaction, table);
            DeleteExceptLocalAccount(connection, transaction, localAccount);

            foreach (string table in new[] { "GuildRank", "GuildAlliance" })
                DeleteAllExceptDummyGuild(connection, transaction, table);
            DeleteGuildsExceptDummy(connection, transaction);

            ResetKeeps(connection, transaction);
            ResetRelics(connection, transaction);

            foreach (string table in new[] { "KeepCaptureLog", "News", "serverstats" })
                DeleteAll(connection, transaction, table);

            UpsertOption(connection, transaction, WorldModelKey, WorldModelValue);
            UpsertOption(connection, transaction, ResetUtcKey, now.ToString("O"));
            RequireStopped(serverStopped);
            transaction.Commit();
        }

        if (!string.IsNullOrWhiteSpace(eventRecordsPath))
            ClearEventRecords(eventRecordsPath);

        return new(false, characters, bots, guilds, keeps, relics, eventRecords, backup, now);
    }

    private static SQLiteConnection Open(string database, bool readOnly)
    {
        var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder
        {
            DataSource = database,
            ReadOnly = readOnly,
            Pooling = false,
            FailIfMissing = readOnly,
            DefaultTimeout = 10,
        }.ConnectionString);
        connection.Open();
        return connection;
    }

    private static void RequireStopped(Func<bool> serverStopped)
    {
        if (!serverStopped())
            throw new InvalidOperationException("Stop the server completely before creating the new world.");
    }

    private static void EnsureNoSQLiteSidecars(string? database)
    {
        if (string.IsNullOrWhiteSpace(database))
            return;
        // Opening a stopped WAL-mode database for the marker read can recreate
        // an empty -shm file. A non-empty WAL is the unsafe condition because
        // it may contain uncheckpointed game data.
        string wal = database + "-wal";
        if (File.Exists(wal) && new FileInfo(wal).Length > 0)
            throw new InvalidOperationException("The database still has an uncheckpointed SQLite WAL. Stop the server cleanly and retry.");
    }

    private static string CreateBackup(string database, string backupDirectory, string? eventRecordsPath)
    {
        string directory = Path.Combine(backupDirectory, $"camlann-reset-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.Copy(database, Path.Combine(directory, Path.GetFileName(database)), overwrite: false);
        if (!string.IsNullOrWhiteSpace(eventRecordsPath) && File.Exists(eventRecordsPath))
            File.Copy(eventRecordsPath, Path.Combine(directory, Path.GetFileName(eventRecordsPath)), overwrite: false);
        return directory;
    }

    private static bool TableExists(SQLiteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@table LIMIT 1";
        command.Parameters.AddWithValue("@table", table);
        return command.ExecuteScalar() is not null;
    }

    private static int Count(SQLiteConnection connection, SQLiteTransaction transaction, string table, string? predicate = null, params (string Name, object Value)[] parameters)
    {
        if (!TableExists(connection, table))
            return 0;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM [{table}]" + (predicate is null ? string.Empty : " WHERE " + predicate);
        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void EnsureLocalOptionsTable(SQLiteConnection connection, SQLiteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "CREATE TABLE IF NOT EXISTS offline_local_options (Key TEXT PRIMARY KEY,Value TEXT NOT NULL)";
        command.ExecuteNonQuery();
    }

    private static void DeletePlayerInventory(SQLiteConnection connection, SQLiteTransaction transaction)
    {
        if (!TableExists(connection, "Inventory"))
            return;

        var characterTables = new List<string>();
        if (TableExists(connection, "DOLCharacters"))
            characterTables.Add("SELECT DOLCharacters_ID FROM DOLCharacters");
        if (TableExists(connection, "DOLCharactersBackup"))
            characterTables.Add("SELECT DOLCharacters_ID FROM DOLCharactersBackup");

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        string characterOwners = characterTables.Count == 0 ? "SELECT NULL WHERE 0" : string.Join(" UNION ", characterTables);
        command.CommandText = $"DELETE FROM Inventory WHERE OwnerID IN ({characterOwners}) OR OwnerID LIKE 'offlinebot:%'";
        command.ExecuteNonQuery();
    }

    private static void DeleteAll(SQLiteConnection connection, SQLiteTransaction transaction, string table)
    {
        if (!TableExists(connection, table))
            return;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM [{table}]";
        command.ExecuteNonQuery();
    }

    private static void DeleteExceptLocalAccount(SQLiteConnection connection, SQLiteTransaction transaction, string localAccount)
    {
        if (!TableExists(connection, "Account"))
            return;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM Account WHERE Name<>@account";
        command.Parameters.AddWithValue("@account", localAccount);
        command.ExecuteNonQuery();
    }

    private static void RequireLocalAccount(SQLiteConnection connection, SQLiteTransaction transaction, string localAccount)
    {
        if (!TableExists(connection, "Account"))
            throw new InvalidOperationException("The account table is missing; the new world was not created.");

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM Account WHERE Name=@account";
        command.Parameters.AddWithValue("@account", localAccount);
        if (Convert.ToInt32(command.ExecuteScalar()) != 1)
            throw new InvalidOperationException($"The local account '{localAccount}' is missing; the new world was not created.");
    }

    private static void DeleteAllExceptDummyGuild(SQLiteConnection connection, SQLiteTransaction transaction, string table)
    {
        if (!TableExists(connection, table))
            return;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = table.Equals("GuildRank", StringComparison.OrdinalIgnoreCase)
            ? "DELETE FROM GuildRank WHERE GuildID NOT IN (SELECT GuildID FROM Guild WHERE GuildName=@dummy)"
            : "DELETE FROM GuildAlliance";
        command.Parameters.AddWithValue("@dummy", DummyGuildName);
        command.ExecuteNonQuery();
    }

    private static void DeleteGuildsExceptDummy(SQLiteConnection connection, SQLiteTransaction transaction)
    {
        if (!TableExists(connection, "Guild"))
            return;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM Guild WHERE GuildName<>@dummy";
        command.Parameters.AddWithValue("@dummy", DummyGuildName);
        command.ExecuteNonQuery();
    }

    private static void ResetKeeps(SQLiteConnection connection, SQLiteTransaction transaction)
    {
        if (!TableExists(connection, "Keep"))
            throw new InvalidOperationException("Keep data is missing; the new world was not created.");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE [Keep] SET Realm=0, ClaimedGuildName=''";
        command.ExecuteNonQuery();
    }

    private static void ResetRelics(SQLiteConnection connection, SQLiteTransaction transaction)
    {
        if (!TableExists(connection, "Relic") || !TableExists(connection, "WorldObject"))
            throw new InvalidOperationException("Relic or shrine data is missing; the new world was not created.");

        var homes = new Dictionary<int, (int Region, int X, int Y, int Z, int Heading)>();
        using (var pads = connection.CreateCommand())
        {
            pads.Transaction = transaction;
            pads.CommandText = "SELECT Emblem,Region,X,Y,Z,Heading FROM WorldObject WHERE ClassType='DOL.GS.GameRelicPad'";
            using var reader = pads.ExecuteReader();
            while (reader.Read())
            {
                int emblem = Convert.ToInt32(reader[0]);
                if (!homes.TryAdd(emblem, (Convert.ToInt32(reader[1]), Convert.ToInt32(reader[2]), Convert.ToInt32(reader[3]), Convert.ToInt32(reader[4]), Convert.ToInt32(reader[5]))))
                    throw new InvalidOperationException($"Relic shrine {emblem} is duplicated; the new world was not created.");
            }
        }

        var relics = new List<RelicHome>();
        using (var rows = connection.CreateCommand())
        {
            rows.Transaction = transaction;
            rows.CommandText = "SELECT RelicID,OriginalRealm,relicType FROM Relic";
            using var reader = rows.ExecuteReader();
            while (reader.Read())
            {
                int originalRealm = Convert.ToInt32(reader[1]);
                int relicType = Convert.ToInt32(reader[2]);
                int emblem = originalRealm + 10 * relicType;
                if (!homes.TryGetValue(emblem, out var home))
                    throw new InvalidOperationException($"Relic shrine {emblem} is missing or ambiguous; the new world was not created.");
                relics.Add(new(Convert.ToInt32(reader[0]), originalRealm, relicType, home.Region, home.X, home.Y, home.Z, home.Heading));
            }
        }

        if (relics.Count != 6 || relics.Select(relic => (relic.OriginalRealm, relic.RelicType)).Distinct().Count() != 6)
            throw new InvalidOperationException("Expected the six classic relics; the new world was not created.");

        foreach (RelicHome relic in relics)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE Relic SET Realm=@realm,LastRealm=@realm,LastCaptureDate=NULL,Region=@region,X=@x,Y=@y,Z=@z,Heading=@heading WHERE RelicID=@id";
            command.Parameters.AddWithValue("@realm", relic.OriginalRealm);
            command.Parameters.AddWithValue("@region", relic.Region);
            command.Parameters.AddWithValue("@x", relic.X);
            command.Parameters.AddWithValue("@y", relic.Y);
            command.Parameters.AddWithValue("@z", relic.Z);
            command.Parameters.AddWithValue("@heading", relic.Heading);
            command.Parameters.AddWithValue("@id", relic.RelicId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("A relic changed during reset; the new world was not created.");
        }
    }

    private static void UpsertOption(SQLiteConnection connection, SQLiteTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO offline_local_options(Key,Value) VALUES (@key,@value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value";
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value);
        command.ExecuteNonQuery();
    }

    private static int CountEventRecords(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return 0;
        using var connection = Open(path, true);
        if (!TableExists(connection, "Events"))
            return 0;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Events";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void ClearEventRecords(string path)
    {
        if (!File.Exists(path))
            return;
        using var connection = Open(path, false);
        if (!TableExists(connection, "Events"))
            return;
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM Events";
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
