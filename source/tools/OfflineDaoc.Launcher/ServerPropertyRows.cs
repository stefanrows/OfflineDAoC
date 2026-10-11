using System.Data.SQLite;

namespace OfflineDaoc.Launcher;

/// <summary>One ServerProperty row the launcher writes: the description and default are used only for a new row.</summary>
public sealed record ServerPropertyRow(string Key, string Description, string DefaultValue, string Value);

/// <summary>
/// The launcher's server-property writer (XP/RP rates, open BG setup). Callers write only while the server is
/// stopped; the server reads the rows at its next start (or on /serverproperties refresh).
/// </summary>
public static class ServerPropertyRows
{
    /// <summary>
    /// Inserts or updates the rows in one transaction. An existing row keeps its category, description and default;
    /// only Value and LastTimeRowUpdated change. <paramref name="serverStopped"/>, when given, is checked again
    /// right before the commit.
    /// </summary>
    public static void Upsert(string database, string category, IReadOnlyList<ServerPropertyRow> rows, Func<bool>? serverStopped = null)
    {
        if (!File.Exists(database))
            throw new InvalidOperationException("The prepared world database is missing.");
        using var connection = new SQLiteConnection($"Data Source={database};Version=3;Pooling=False;Default Timeout=10");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // ServerProperty_ID has a unique index; the server leaves it NULL, so new
        // rows must too. Earlier launchers wrote '', which blocked a second new key.
        command.CommandText = "UPDATE ServerProperty SET ServerProperty_ID=NULL WHERE ServerProperty_ID='';";
        command.ExecuteNonQuery();
        command.CommandText = """
            INSERT INTO ServerProperty (Category, `Key`, Description, DefaultValue, Value, LastTimeRowUpdated, ServerProperty_ID)
            VALUES (@category, @key, @description, @default, @value, @updated, NULL)
            ON CONFLICT(`Key`) DO UPDATE SET
                Value=excluded.Value,
                LastTimeRowUpdated=excluded.LastTimeRowUpdated
            """;
        string updated = DateTime.UtcNow.ToString("O");
        foreach (ServerPropertyRow row in rows)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@category", category);
            command.Parameters.AddWithValue("@key", row.Key);
            command.Parameters.AddWithValue("@description", row.Description);
            command.Parameters.AddWithValue("@default", row.DefaultValue);
            command.Parameters.AddWithValue("@value", row.Value);
            command.Parameters.AddWithValue("@updated", updated);
            command.ExecuteNonQuery();
        }
        if (serverStopped != null && !serverStopped())
            throw new InvalidOperationException("The server started before the settings could be saved. Stop it and try again.");
        transaction.Commit();
    }

    /// <summary>The saved values of these keys (read-only). A missing database or table gives no rows.</summary>
    public static IReadOnlyList<(string key, string value)> Read(string database, IEnumerable<string> keys)
    {
        string[] wanted = keys.ToArray();
        if (wanted.Length == 0 || !File.Exists(database)) return [];
        using var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder
            { DataSource = database, Pooling = false, FailIfMissing = true, ReadOnly = true, DefaultTimeout = 5 }.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='ServerProperty'";
        if (Convert.ToInt64(command.ExecuteScalar()) == 0) return [];
        command.CommandText = "SELECT `Key`, Value FROM ServerProperty WHERE `Key` IN (" +
            string.Join(",", wanted.Select((_, index) => "@k" + index)) + ")";
        for (int index = 0; index < wanted.Length; index++)
            command.Parameters.AddWithValue("@k" + index, wanted[index]);
        var rows = new List<(string key, string value)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? string.Empty : Convert.ToString(reader.GetValue(1)) ?? string.Empty));
        return rows;
    }
}
