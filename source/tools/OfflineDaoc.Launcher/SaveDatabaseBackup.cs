namespace OfflineDaoc.Launcher;

/// <summary>A plain file copy of the stopped save database, for changes that move many saved bots.</summary>
public static class SaveDatabaseBackup
{
    /// <summary>
    /// Copies the database into a new <c>&lt;prefix&gt;-&lt;UTC stamp&gt;-&lt;id&gt;</c> folder under
    /// <paramref name="backupRoot"/> and returns that folder. Refuses a missing database or a
    /// non-empty WAL, which may hold uncheckpointed game data (the server did not stop cleanly).
    /// </summary>
    public static string Create(string database, string backupRoot, string prefix)
    {
        if (!File.Exists(database))
            throw new FileNotFoundException("The save database was not found.", database);
        string wal = database + "-wal";
        if (File.Exists(wal) && new FileInfo(wal).Length > 0)
            throw new InvalidOperationException("The database still has an uncheckpointed SQLite WAL. Stop the server cleanly and retry.");
        string directory = Path.Combine(backupRoot, $"{prefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.Copy(database, Path.Combine(directory, Path.GetFileName(database)), overwrite: false);
        return directory;
    }
}
