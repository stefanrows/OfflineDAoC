using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DOL.GS.DataPacks
{
    /// <summary>One line of data-packs/manifest.json.</summary>
    public sealed class WorldDataPackManifestEntry
    {
        public string Name { get; init; }
        public string File { get; init; }
        public string Sha256 { get; init; }
        public string Description { get; init; }
    }

    public enum WorldDataPackState
    {
        /// <summary>Listed, valid, not applied to this save yet.</summary>
        Pending,
        /// <summary>Applied to this save with the shipped hash.</summary>
        Applied,
        /// <summary>Applied to this save, but the shipped file now has another hash. Never re-run.</summary>
        AppliedDifferentHash,
        /// <summary>The file's hash differs from the manifest. Refused.</summary>
        HashMismatch,
        /// <summary>The manifest names a file that is not there. Refused.</summary>
        MissingFile,
        /// <summary>The file cannot be read or contains a forbidden statement. Refused.</summary>
        Invalid,
        /// <summary>A statement failed; the pack was rolled back.</summary>
        Failed,
    }

    public sealed class WorldDataPackResult
    {
        public string Name { get; init; }
        public WorldDataPackState State { get; init; }
        public int Statements { get; init; }
        public long Rows { get; init; }
        public string AppliedSha256 { get; init; }
        public string AppliedUtc { get; init; }
        public TimeSpan Elapsed { get; init; }
        public string Message { get; init; }
        /// <summary>True only for a pack this call applied (not for one applied in an earlier run).</summary>
        public bool AppliedNow { get; init; }
        /// <summary>Whether the pack file is in the folder (set by every status call).</summary>
        public bool FilePresent { get; init; }
        /// <summary>A failed pack with the same sha that this run skipped instead of retrying.</summary>
        public bool SkippedAfterFailure { get; init; }
    }

    public sealed class WorldDataPackRun
    {
        public List<WorldDataPackResult> Results { get; } = new();
        public List<string> Warnings { get; } = new();
        /// <summary>Backup taken before the first pack of this run, or null when nothing was applied.</summary>
        public string BackupPath { get; set; }
        /// <summary>Older data pack backups this run deleted (only the newest <see cref="WorldDataPackApplier.BackupsKept"/> stay).</summary>
        public List<string> PrunedBackups { get; } = new();
        /// <summary>SQLite journal mode of the save, read before anything is applied.</summary>
        public string JournalMode { get; set; }
        /// <summary>True when the journal mode does not allow a safe rollback and could not be raised; nothing was applied.</summary>
        public bool RefusedJournalMode { get; set; }
        /// <summary>
        /// Journal mode the applier's own connections used for this run because the save's mode (<see cref="JournalMode"/>)
        /// was OFF or MEMORY; null when the save's mode was used unchanged.
        /// </summary>
        public string RaisedJournalMode { get; set; }
        /// <summary>Keys changed in the watched (pre-cached) tables, per table name.</summary>
        public Dictionary<string, HashSet<string>> ChangedKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int AppliedCount => Results.Count(r => r.AppliedNow);
        public int FailedCount => Results.Count(r => r.State is WorldDataPackState.Failed or WorldDataPackState.HashMismatch
            or WorldDataPackState.MissingFile or WorldDataPackState.Invalid);
    }

    /// <summary>A pre-cached table whose changed primary keys the applier reports, so the caller can refresh the cache.</summary>
    public sealed record WorldDataPackCacheWatch(string Table, string KeyColumn);

    /// <summary>
    /// Applies world data packs (SQLite SQL files listed in data-packs/manifest.json) to a SQLite save, once each.
    /// A pack runs in one transaction; the marker table <see cref="MarkerTable"/> records it in the same
    /// transaction, so a pack is either fully applied and recorded or not applied at all. Before the first pack of
    /// a run the whole database is copied next to itself (online backup API). See docs/TOA_DATA_PACKS.md.
    /// This class does no logging and needs no running server; <see cref="WorldDataPacks"/> wires it in.
    /// </summary>
    public sealed class WorldDataPackApplier
    {
        public const string MarkerTable = "offline_data_packs";
        public const string ManifestFileName = "manifest.json";
        public const string BackupInfix = ".bak-datapacks-";
        public const string FailureTable = "offline_data_pack_failures";
        public const int BackupsKept = 3;
        /// <summary>Rollback journal mode the applier's own connections switch to when the save's mode is OFF or MEMORY.</summary>
        public const string RollbackJournalMode = "truncate";
        private const string WriteProbeTable = "offline_data_pack_write_probe";

        private static readonly Regex NamePattern = new("^[0-9]{3}-[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant);
        private static readonly Regex ShaPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
        private static readonly HashSet<string> ForbiddenFirstWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "BEGIN", "COMMIT", "END", "ROLLBACK", "SAVEPOINT", "RELEASE", "ATTACH", "DETACH", "VACUUM", "PRAGMA", "REINDEX",
        };

        private readonly string _connectionString;
        private readonly IReadOnlyList<WorldDataPackCacheWatch> _cacheWatches;
        private readonly Func<DateTime> _utcNow;
        /// <summary>Set for one run when the save's journal mode had to be raised; every connection <see cref="Open"/> makes then uses it.</summary>
        private string _runJournalMode;

        public WorldDataPackApplier(string sqliteConnectionString, string packDirectory,
            IEnumerable<WorldDataPackCacheWatch> cacheWatches = null, Func<DateTime> utcNow = null)
        {
            var builder = new SQLiteConnectionStringBuilder(sqliteConnectionString);
            if (string.IsNullOrWhiteSpace(builder.DataSource))
                throw new ArgumentException("The SQLite connection string has no Data Source.", nameof(sqliteConnectionString));

            DatabasePath = Path.GetFullPath(builder.DataSource);
            builder.DataSource = DatabasePath;
            builder.Pooling = false;
            builder.FailIfMissing = true;
            if (builder.DefaultTimeout < 60)
                builder.DefaultTimeout = 60;
            _connectionString = builder.ToString();
            PackDirectory = Path.GetFullPath(packDirectory);
            _cacheWatches = (cacheWatches ?? Array.Empty<WorldDataPackCacheWatch>()).ToArray();
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public string DatabasePath { get; }
        public string PackDirectory { get; }

        /// <summary>Called with a short progress line (backup, pack start). Optional.</summary>
        public Action<string> Progress { get; set; }

        #region Manifest

        /// <summary>Reads and validates the manifest. Throws <see cref="InvalidDataException"/> for a broken manifest.</summary>
        public IReadOnlyList<WorldDataPackManifestEntry> ReadManifest()
        {
            string path = Path.Combine(PackDirectory, ManifestFileName);
            if (!System.IO.File.Exists(path))
                throw new FileNotFoundException("No data pack manifest.", path);

            using JsonDocument document = JsonDocument.Parse(System.IO.File.ReadAllBytes(path));
            if (!document.RootElement.TryGetProperty("packs", out JsonElement packs) || packs.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("manifest.json has no \"packs\" array.");

            var entries = new List<WorldDataPackManifestEntry>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement pack in packs.EnumerateArray())
            {
                var entry = new WorldDataPackManifestEntry
                {
                    Name = GetString(pack, "name"),
                    File = GetString(pack, "file"),
                    Sha256 = GetString(pack, "sha256")?.ToLowerInvariant(),
                    Description = GetString(pack, "description") ?? string.Empty,
                };
                if (entry.Name == null || !NamePattern.IsMatch(entry.Name))
                    throw new InvalidDataException($"manifest.json: invalid pack name '{entry.Name}' (expected NNN-name).");
                if (entry.File != entry.Name + ".sql" && entry.File != entry.Name + ".sql.gz")
                    throw new InvalidDataException($"manifest.json: pack {entry.Name} must use file {entry.Name}.sql or {entry.Name}.sql.gz.");
                if (entry.Sha256 == null || !ShaPattern.IsMatch(entry.Sha256))
                    throw new InvalidDataException($"manifest.json: pack {entry.Name} has no valid sha256.");
                if (!names.Add(entry.Name))
                    throw new InvalidDataException($"manifest.json: pack {entry.Name} is listed twice.");
                entries.Add(entry);
            }

            return entries.OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
        }

        private static string GetString(JsonElement element, string property) =>
            element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        /// <summary>Pack files in the folder that the manifest does not list (they are never applied).</summary>
        public IReadOnlyList<string> UnlistedFiles(IReadOnlyList<WorldDataPackManifestEntry> manifest)
        {
            var listed = new HashSet<string>(manifest.Select(e => e.File), StringComparer.OrdinalIgnoreCase);
            return Directory.EnumerateFiles(PackDirectory)
                .Select(Path.GetFileName)
                .Where(f => (f.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".sql.gz", StringComparison.OrdinalIgnoreCase))
                            && !listed.Contains(f))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
        }

        #endregion

        #region Status

        /// <summary>The state of every manifest pack for this save. Reads only; validates hashes of pending packs.</summary>
        public IReadOnlyList<WorldDataPackResult> GetStatus() => Status(validateFiles: true);

        /// <summary>
        /// The state of every manifest pack from the manifest, the marker table and the failure table only: pack files are
        /// checked for presence but not read, decompressed or hashed (cheap enough for a GM command on a running server).
        /// </summary>
        public IReadOnlyList<WorldDataPackResult> GetQuickStatus() => Status(validateFiles: false);

        private IReadOnlyList<WorldDataPackResult> Status(bool validateFiles)
        {
            IReadOnlyList<WorldDataPackManifestEntry> manifest = ReadManifest();
            Dictionary<string, AppliedRecord> applied;
            Dictionary<string, FailureRecord> failures;
            using (SQLiteConnection connection = Open())
            {
                applied = ReadApplied(connection);
                failures = ReadFailures(connection);
            }

            var results = new List<WorldDataPackResult>();
            foreach (WorldDataPackManifestEntry entry in manifest)
            {
                bool present = System.IO.File.Exists(Path.Combine(PackDirectory, entry.File));
                if (applied.TryGetValue(entry.Name, out AppliedRecord record))
                {
                    WorldDataPackResult done = AppliedResult(entry, record);
                    results.Add(new WorldDataPackResult
                    {
                        Name = done.Name, State = done.State, Rows = done.Rows, AppliedSha256 = done.AppliedSha256,
                        AppliedUtc = done.AppliedUtc, Message = done.Message, FilePresent = present,
                    });
                    continue;
                }

                if (failures.TryGetValue(entry.Name, out FailureRecord failure) &&
                    string.Equals(failure.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(FailedBeforeResult(entry, failure));
                    continue;
                }

                if (!validateFiles)
                {
                    results.Add(new WorldDataPackResult
                    {
                        Name = entry.Name,
                        State = present ? WorldDataPackState.Pending : WorldDataPackState.MissingFile,
                        FilePresent = present,
                        Message = present ? entry.Description : $"file {entry.File} is missing",
                    });
                    continue;
                }

                PreparedPack prepared = Prepare(entry);
                results.Add(prepared.Refusal ?? new WorldDataPackResult
                {
                    Name = entry.Name,
                    State = WorldDataPackState.Pending,
                    Statements = prepared.Statements.Count,
                    FilePresent = true,
                    Message = entry.Description,
                });
            }

            return results;
        }

        private static WorldDataPackResult AppliedResult(WorldDataPackManifestEntry entry, AppliedRecord record)
        {
            bool same = string.Equals(record.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase);
            return new WorldDataPackResult
            {
                Name = entry.Name,
                State = same ? WorldDataPackState.Applied : WorldDataPackState.AppliedDifferentHash,
                Rows = record.Rows,
                AppliedSha256 = record.Sha256,
                AppliedUtc = record.AppliedUtc,
                Message = same
                    ? $"applied {record.AppliedUtc}"
                    : $"applied {record.AppliedUtc} with sha {Short(record.Sha256)}, shipped file is {Short(entry.Sha256)}; not re-run (ship a fix as a new pack)",
            };
        }

        #endregion

        #region Apply

        /// <summary>Applies every pending pack in name order. Never throws for a single pack's failure.</summary>
        public WorldDataPackRun ApplyPending() => Run(null);

        /// <summary>Applies one named pack if it is pending.</summary>
        public WorldDataPackRun Apply(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A pack name is required.", nameof(name));
            return Run(name.Trim());
        }

        private WorldDataPackRun Run(string onlyName)
        {
            try
            {
                return RunPacks(onlyName);
            }
            finally
            {
                // A raised journal mode is for one run only; status calls use the connection string as given.
                _runJournalMode = null;
            }
        }

        private WorldDataPackRun RunPacks(string onlyName)
        {
            var run = new WorldDataPackRun();
            IReadOnlyList<WorldDataPackManifestEntry> manifest = ReadManifest();
            foreach (string unlisted in UnlistedFiles(manifest))
                run.Warnings.Add($"{unlisted} is not listed in manifest.json and is ignored.");

            IEnumerable<WorldDataPackManifestEntry> selected = manifest;
            if (onlyName != null)
            {
                selected = manifest.Where(e => string.Equals(e.Name, onlyName, StringComparison.OrdinalIgnoreCase)).ToList();
                if (!selected.Any())
                {
                    run.Warnings.Add($"No pack named {onlyName} in manifest.json.");
                    return run;
                }
            }

            Dictionary<string, AppliedRecord> applied;
            Dictionary<string, FailureRecord> failures;
            _runJournalMode = null;
            using (SQLiteConnection connection = Open())
            {
                // Rollback and crash recovery are undefined without a rollback journal or WAL.
                run.JournalMode = Convert.ToString(Scalar(connection, "PRAGMA journal_mode"), CultureInfo.InvariantCulture)?.ToLowerInvariant();
                if (run.JournalMode is "off" or "memory")
                {
                    // The OpenDAoC default and the setup tool connect with Journal Mode=Off. For a database that is not in
                    // WAL mode the journal mode is a setting of the connection, not of the file: "PRAGMA journal_mode ...
                    // sets the journal mode for databases associated with the current database connection", and "if a
                    // process sets PRAGMA journal_mode=TRUNCATE and then closes and reopens the database will come back up
                    // in the default rollback mode of DELETE" (sqlite.org/pragma.html#pragma_journal_mode,
                    // sqlite.org/wal.html#persistence_of_wal_mode). So the applier's own connections can use a rollback
                    // journal while the server's connections stay OFF. If the process dies mid-pack, the -journal file is
                    // hot and "before reading from a database file, SQLite always checks to see if that database file has
                    // a hot journal ... then the journal is rolled back before the file is read" (sqlite.org/lockingv3.html,
                    // section 4.1), whatever journal mode the next connection uses. WAL is never used here: it is
                    // persistent and would change the mode of the server's own connections.
                    if (!TryRaiseJournalMode(connection, out string reason))
                    {
                        run.RefusedJournalMode = true;
                        run.Warnings.Add($"journal mode is {run.JournalMode.ToUpperInvariant()} and could not be raised to " +
                                         $"{RollbackJournalMode.ToUpperInvariant()} ({reason}); no data pack is applied because a failed " +
                                         "pack could not be rolled back safely. Use Journal Mode=WAL (or Delete) in the DB connection string.");
                        return run;
                    }

                    run.RaisedJournalMode = RollbackJournalMode;
                    _runJournalMode = RollbackJournalMode;
                }

                EnsureMarkerTable(connection);
                applied = ReadApplied(connection);
                failures = ReadFailures(connection);
            }

            bool explicitRetry = onlyName != null;
            foreach (WorldDataPackManifestEntry entry in selected)
            {
                if (applied.TryGetValue(entry.Name, out AppliedRecord record))
                {
                    run.Results.Add(AppliedResult(entry, record));
                    continue;
                }

                if (!explicitRetry && failures.TryGetValue(entry.Name, out FailureRecord failure) &&
                    string.Equals(failure.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    // A pack that failed at SQL level fails the same way again; retrying it on every start
                    // would only add a full database backup per start.
                    run.Results.Add(FailedBeforeResult(entry, failure));
                    continue;
                }

                PreparedPack prepared = Prepare(entry);
                if (prepared.Refusal != null)
                {
                    run.Results.Add(prepared.Refusal);
                    continue;
                }

                if (run.BackupPath == null)
                {
                    try
                    {
                        run.BackupPath = Backup();
                        PruneBackups(run);
                    }
                    catch (Exception e)
                    {
                        // Never write a pack without a backup of the save.
                        run.Results.Add(new WorldDataPackResult
                        {
                            Name = entry.Name,
                            State = WorldDataPackState.Failed,
                            Message = $"database backup failed, no pack applied: {e.Message}",
                        });
                        return run;
                    }
                }

                run.Results.Add(ApplyPrepared(entry, prepared, run));
            }

            return run;
        }

        private WorldDataPackResult ApplyPrepared(WorldDataPackManifestEntry entry, PreparedPack prepared, WorldDataPackRun run)
        {
            Progress?.Invoke($"Applying world data pack {entry.Name} ({prepared.Statements.Count} statements)...");
            DateTime started = _utcNow();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            int index = 0;
            long rows = 0;
            using SQLiteConnection connection = Open();
            using SQLiteTransaction transaction = connection.BeginTransaction();
            try
            {
                List<WorldDataPackCacheWatch> watches = InstallWatches(connection, transaction);

                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    for (index = 0; index < prepared.Statements.Count; index++)
                    {
                        command.CommandText = prepared.Statements[index];
                        int affected = command.ExecuteNonQuery();
                        if (affected > 0)
                            rows += affected;
                    }
                }

                Dictionary<string, HashSet<string>> changed = CollectWatches(connection, transaction, watches);
                string appliedUtc = started.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                using (SQLiteCommand marker = connection.CreateCommand())
                {
                    marker.Transaction = transaction;
                    marker.CommandText = $"INSERT INTO \"{MarkerTable}\" (Name, Sha256, AppliedUtc, Rows) VALUES (@name, @sha, @utc, @rows)";
                    marker.Parameters.AddWithValue("@name", entry.Name);
                    marker.Parameters.AddWithValue("@sha", entry.Sha256);
                    marker.Parameters.AddWithValue("@utc", appliedUtc);
                    marker.Parameters.AddWithValue("@rows", rows);
                    marker.ExecuteNonQuery();
                }

                using (SQLiteCommand clear = connection.CreateCommand())
                {
                    clear.Transaction = transaction;
                    clear.CommandText = $"DELETE FROM \"{FailureTable}\" WHERE Name = @name";
                    clear.Parameters.AddWithValue("@name", entry.Name);
                    clear.ExecuteNonQuery();
                }

                transaction.Commit();
                foreach ((string table, HashSet<string> keys) in changed)
                {
                    if (!run.ChangedKeys.TryGetValue(table, out HashSet<string> all))
                        run.ChangedKeys[table] = all = new HashSet<string>(StringComparer.Ordinal);
                    all.UnionWith(keys);
                }

                return new WorldDataPackResult
                {
                    Name = entry.Name,
                    State = WorldDataPackState.Applied,
                    Statements = prepared.Statements.Count,
                    Rows = rows,
                    AppliedSha256 = entry.Sha256,
                    AppliedUtc = appliedUtc,
                    Elapsed = stopwatch.Elapsed,
                    AppliedNow = true,
                    Message = $"applied: {prepared.Statements.Count} statements, {rows} rows",
                };
            }
            catch (Exception e)
            {
                try
                {
                    transaction.Rollback();
                }
                catch
                {
                    // Disposing the transaction rolls back as well.
                }

                string statement = index < prepared.Statements.Count ? Excerpt(prepared.Statements[index]) : "(marker)";
                string message = $"rolled back; statement {index + 1} of {prepared.Statements.Count} failed: {e.Message} [{statement}]";
                try
                {
                    RecordFailure(connection, entry, started, message);
                }
                catch (Exception recordError)
                {
                    message += $" (failure not recorded, it will be retried at the next start: {recordError.Message})";
                }

                return new WorldDataPackResult
                {
                    Name = entry.Name,
                    State = WorldDataPackState.Failed,
                    Statements = prepared.Statements.Count,
                    Elapsed = stopwatch.Elapsed,
                    FilePresent = true,
                    Message = message,
                };
            }
        }

        private List<WorldDataPackCacheWatch> InstallWatches(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            var installed = new List<WorldDataPackCacheWatch>();
            if (_cacheWatches.Count == 0)
                return installed;

            Execute(connection, transaction, "CREATE TEMP TABLE IF NOT EXISTS offline_data_pack_changes (TableName TEXT NOT NULL, KeyValue TEXT)");
            Execute(connection, transaction, "DELETE FROM temp.offline_data_pack_changes");
            for (int i = 0; i < _cacheWatches.Count; i++)
            {
                WorldDataPackCacheWatch watch = _cacheWatches[i];
                if (!TableExists(connection, transaction, watch.Table))
                    continue;

                string table = QuoteIdentifier(watch.Table);
                string key = QuoteIdentifier(watch.KeyColumn);
                string name = QuoteLiteral(watch.Table);
                Execute(connection, transaction,
                    $"CREATE TEMP TRIGGER offline_data_pack_ins_{i} AFTER INSERT ON main.{table} BEGIN " +
                    $"INSERT INTO offline_data_pack_changes VALUES ({name}, NEW.{key}); END");
                Execute(connection, transaction,
                    $"CREATE TEMP TRIGGER offline_data_pack_upd_{i} AFTER UPDATE ON main.{table} BEGIN " +
                    $"INSERT INTO offline_data_pack_changes VALUES ({name}, NEW.{key}); " +
                    $"INSERT INTO offline_data_pack_changes SELECT {name}, OLD.{key} WHERE OLD.{key} IS NOT NEW.{key}; END");
                Execute(connection, transaction,
                    $"CREATE TEMP TRIGGER offline_data_pack_del_{i} AFTER DELETE ON main.{table} BEGIN " +
                    $"INSERT INTO offline_data_pack_changes VALUES ({name}, OLD.{key}); END");
                installed.Add(watch);
            }

            return installed;
        }

        private Dictionary<string, HashSet<string>> CollectWatches(SQLiteConnection connection, SQLiteTransaction transaction,
            List<WorldDataPackCacheWatch> installed)
        {
            var changed = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            if (installed.Count == 0)
                return changed;

            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT DISTINCT TableName, KeyValue FROM temp.offline_data_pack_changes WHERE KeyValue IS NOT NULL";
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string table = reader.GetString(0);
                    if (!changed.TryGetValue(table, out HashSet<string> keys))
                        changed[table] = keys = new HashSet<string>(StringComparer.Ordinal);
                    keys.Add(Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture));
                }
            }

            for (int i = 0; i < _cacheWatches.Count; i++)
            {
                Execute(connection, transaction, $"DROP TRIGGER IF EXISTS temp.offline_data_pack_ins_{i}");
                Execute(connection, transaction, $"DROP TRIGGER IF EXISTS temp.offline_data_pack_upd_{i}");
                Execute(connection, transaction, $"DROP TRIGGER IF EXISTS temp.offline_data_pack_del_{i}");
            }

            Execute(connection, transaction, "DROP TABLE IF EXISTS temp.offline_data_pack_changes");
            return changed;
        }

        /// <summary>Copies the whole database next to itself with SQLite's online backup API (consistent with WAL).</summary>
        private string Backup()
        {
            string stamp = _utcNow().ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string path = DatabasePath + BackupInfix + stamp;
            for (int suffix = 2; System.IO.File.Exists(path); suffix++)
                path = DatabasePath + BackupInfix + stamp + "-" + suffix.ToString(CultureInfo.InvariantCulture);

            Progress?.Invoke($"Backing up the database to {path} before applying world data packs...");
            try
            {
                using SQLiteConnection source = Open();
                using (var destination = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
                {
                    destination.Open();
                    source.BackupDatabase(destination, "main", "main", -1, null, 0);
                    destination.Close();
                }

                if (!System.IO.File.Exists(path) || new FileInfo(path).Length == 0)
                    throw new IOException($"backup file {path} was not written");
            }
            catch
            {
                // Never leave a partial backup behind that looks like a good one.
                SQLiteConnection.ClearAllPools();
                foreach (string partial in new[] { path, path + "-journal", path + "-wal", path + "-shm" })
                {
                    try
                    {
                        if (System.IO.File.Exists(partial))
                            System.IO.File.Delete(partial);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }

                throw;
            }

            return path;
        }

        /// <summary>Deletes all but the newest <see cref="BackupsKept"/> data pack backups of this database.</summary>
        private void PruneBackups(WorldDataPackRun run)
        {
            string directory = Path.GetDirectoryName(DatabasePath);
            string prefix = Path.GetFileName(DatabasePath) + BackupInfix;
            List<FileInfo> backups = new DirectoryInfo(directory).EnumerateFiles(prefix + "*")
                .Where(f => !f.Name.EndsWith("-journal", StringComparison.Ordinal) && !f.Name.EndsWith("-wal", StringComparison.Ordinal) &&
                            !f.Name.EndsWith("-shm", StringComparison.Ordinal))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ThenByDescending(f => f.Name, StringComparer.Ordinal)
                .ToList();
            foreach (FileInfo old in backups.Skip(BackupsKept))
            {
                try
                {
                    old.Delete();
                    run.PrunedBackups.Add(old.FullName);
                }
                catch (IOException e)
                {
                    run.Warnings.Add($"could not delete old backup {old.FullName}: {e.Message}");
                }
                catch (UnauthorizedAccessException e)
                {
                    run.Warnings.Add($"could not delete old backup {old.FullName}: {e.Message}");
                }
            }
        }

        #endregion

        #region Pack reading and statement splitting

        private sealed class PreparedPack
        {
            public List<string> Statements { get; init; } = new();
            public WorldDataPackResult Refusal { get; init; }
        }

        private PreparedPack Prepare(WorldDataPackManifestEntry entry)
        {
            string path = Path.Combine(PackDirectory, entry.File);
            if (!System.IO.File.Exists(path))
                return Refuse(entry, WorldDataPackState.MissingFile, $"file {entry.File} is missing");

            string text;
            string sha;
            try
            {
                text = ReadPackText(path, out sha);
            }
            catch (Exception e)
            {
                return Refuse(entry, WorldDataPackState.Invalid, $"cannot read {entry.File}: {e.Message}");
            }

            if (!string.Equals(sha, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                return Refuse(entry, WorldDataPackState.HashMismatch,
                    $"sha256 of {entry.File} is {Short(sha)}, manifest says {Short(entry.Sha256)}; refused");

            List<string> statements;
            try
            {
                statements = SplitStatements(text).ToList();
            }
            catch (FormatException e)
            {
                return Refuse(entry, WorldDataPackState.Invalid, e.Message);
            }

            for (int i = 0; i < statements.Count; i++)
            {
                string forbidden = ForbiddenReason(statements[i]);
                if (forbidden != null)
                    return Refuse(entry, WorldDataPackState.Invalid,
                        $"statement {i + 1} is not allowed in a data pack ({forbidden}): [{Excerpt(statements[i])}]");
            }

            if (statements.Count == 0)
                return Refuse(entry, WorldDataPackState.Invalid, "the pack has no statements");

            return new PreparedPack { Statements = statements };
        }

        private static PreparedPack Refuse(WorldDataPackManifestEntry entry, WorldDataPackState state, string message) =>
            new() { Refusal = new WorldDataPackResult { Name = entry.Name, State = state, Message = message } };

        /// <summary>Reads a .sql or .sql.gz pack as UTF-8 and returns the sha256 of the uncompressed bytes.</summary>
        public static string ReadPackText(string path, out string sha256)
        {
            byte[] bytes;
            if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                using FileStream file = System.IO.File.OpenRead(path);
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                using var memory = new MemoryStream();
                gzip.CopyTo(memory);
                bytes = memory.ToArray();
            }
            else
            {
                bytes = System.IO.File.ReadAllBytes(path);
            }

            sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿');
        }

        /// <summary>
        /// Splits SQLite text into statements on top-level semicolons. Understands '...' strings, "..." and `...`
        /// and [...] identifiers, -- and /* */ comments. Comment-only fragments are dropped. Trigger bodies
        /// (BEGIN ... END) are not supported; such packs are refused by <see cref="ForbiddenReason"/>.
        /// </summary>
        public static IEnumerable<string> SplitStatements(string sql)
        {
            var current = new StringBuilder();
            bool hasCode = false;
            int i = 0;
            while (i < sql.Length)
            {
                char c = sql[i];
                if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
                {
                    int end = sql.IndexOf('\n', i);
                    end = end < 0 ? sql.Length : end + 1;
                    current.Append(sql, i, end - i);
                    i = end;
                    continue;
                }

                if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                {
                    int end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0)
                        throw new FormatException("unterminated /* comment");
                    current.Append(sql, i, end + 2 - i);
                    i = end + 2;
                    continue;
                }

                if (c is '\'' or '"' or '`' or '[')
                {
                    char close = c == '[' ? ']' : c;
                    int j = i + 1;
                    while (true)
                    {
                        if (j >= sql.Length)
                            throw new FormatException($"unterminated {c} quote near [{Excerpt(sql.Substring(i))}]");
                        if (sql[j] == close)
                        {
                            if (close != ']' && j + 1 < sql.Length && sql[j + 1] == close)
                            {
                                j += 2;
                                continue;
                            }

                            break;
                        }

                        j++;
                    }

                    current.Append(sql, i, j + 1 - i);
                    hasCode = true;
                    i = j + 1;
                    continue;
                }

                if (c == ';')
                {
                    if (hasCode)
                        yield return current.ToString().Trim();
                    current.Clear();
                    hasCode = false;
                    i++;
                    continue;
                }

                if (!char.IsWhiteSpace(c))
                    hasCode = true;
                current.Append(c);
                i++;
            }

            if (hasCode)
                yield return current.ToString().Trim();
        }

        /// <summary>Why a statement may not run inside a pack's transaction, or null when it may.</summary>
        public static string ForbiddenReason(string statement)
        {
            List<string> words = LeadingWords(statement, 3);
            if (words.Count == 0)
                return null;
            if (ForbiddenFirstWords.Contains(words[0]))
                return words[0].ToUpperInvariant() + " would break the pack's own transaction";
            if (words[0].Equals("CREATE", StringComparison.OrdinalIgnoreCase) &&
                words.Skip(1).Any(w => w.Equals("TRIGGER", StringComparison.OrdinalIgnoreCase)))
                return "CREATE TRIGGER";
            if (statement.IndexOf(MarkerTable, StringComparison.OrdinalIgnoreCase) >= 0 ||
                statement.IndexOf(FailureTable, StringComparison.OrdinalIgnoreCase) >= 0)
                return "packs must not touch the marker tables";
            return null;
        }

        private static List<string> LeadingWords(string statement, int count)
        {
            var words = new List<string>();
            int i = 0;
            while (i < statement.Length && words.Count < count)
            {
                char c = statement[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (c == '-' && i + 1 < statement.Length && statement[i + 1] == '-')
                {
                    int end = statement.IndexOf('\n', i);
                    i = end < 0 ? statement.Length : end + 1;
                    continue;
                }

                if (c == '/' && i + 1 < statement.Length && statement[i + 1] == '*')
                {
                    int end = statement.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? statement.Length : end + 2;
                    continue;
                }

                int start = i;
                while (i < statement.Length && (char.IsLetterOrDigit(statement[i]) || statement[i] == '_'))
                    i++;
                if (i == start)
                    break;
                words.Add(statement.Substring(start, i - start));
            }

            return words;
        }

        #endregion

        #region SQLite helpers

        private sealed record AppliedRecord(string Sha256, string AppliedUtc, long Rows);

        private sealed record FailureRecord(string Sha256, string FailedUtc, string Message);

        private static WorldDataPackResult FailedBeforeResult(WorldDataPackManifestEntry entry, FailureRecord failure) => new()
        {
            Name = entry.Name,
            State = WorldDataPackState.Failed,
            SkippedAfterFailure = true,
            FilePresent = true,
            Message = $"failed on {failure.FailedUtc} and is skipped until '/datapacks apply {entry.Name}' or a new pack file: {failure.Message}",
        };

        private static Dictionary<string, FailureRecord> ReadFailures(SQLiteConnection connection)
        {
            var failures = new Dictionary<string, FailureRecord>(StringComparer.OrdinalIgnoreCase);
            if (!TableExists(connection, null, FailureTable))
                return failures;

            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT Name, Sha256, FailedUtc, Message FROM \"{FailureTable}\"";
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                failures[reader.GetString(0)] = new FailureRecord(
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    reader.IsDBNull(3) ? string.Empty : reader.GetString(3));
            }

            return failures;
        }

        private static void RecordFailure(SQLiteConnection connection, WorldDataPackManifestEntry entry, DateTime failedUtc, string message)
        {
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO \"{FailureTable}\" (Name, Sha256, FailedUtc, Message) VALUES (@name, @sha, @utc, @message) " +
                                  "ON CONFLICT(Name) DO UPDATE SET Sha256 = excluded.Sha256, FailedUtc = excluded.FailedUtc, Message = excluded.Message";
            command.Parameters.AddWithValue("@name", entry.Name);
            command.Parameters.AddWithValue("@sha", entry.Sha256);
            command.Parameters.AddWithValue("@utc", failedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@message", message.Length <= 1000 ? message : message.Substring(0, 1000));
            command.ExecuteNonQuery();
        }

        private static object Scalar(SQLiteConnection connection, string sql)
        {
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar();
        }

        private SQLiteConnection Open()
        {
            var connection = new SQLiteConnection(_connectionString);
            connection.Open();
            if (_runJournalMode == null)
                return connection;

            try
            {
                string mode = SetJournalMode(connection, _runJournalMode);
                if (mode != _runJournalMode)
                    throw new InvalidOperationException($"SQLite kept journal mode {mode} instead of {_runJournalMode} for the data pack run.");
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Switches this connection to <see cref="RollbackJournalMode"/> and checks that it can write. False (with a reason)
        /// when SQLite keeps the old mode (it returns the mode in effect) or the database is read-only.
        /// </summary>
        private static bool TryRaiseJournalMode(SQLiteConnection connection, out string reason)
        {
            string mode;
            try
            {
                mode = SetJournalMode(connection, RollbackJournalMode);
            }
            catch (SQLiteException e)
            {
                reason = e.Message;
                return false;
            }

            if (mode != RollbackJournalMode)
            {
                reason = $"SQLite kept {mode?.ToUpperInvariant() ?? "no mode"}";
                return false;
            }

            // A read-only file accepts the pragma (nothing is written for it), and SQLite turns BEGIN IMMEDIATE on a
            // read-only database into a read transaction. So write for real and roll it back: this fails when the file is
            // read-only or the rollback journal cannot be created next to it, and leaves nothing behind otherwise.
            try
            {
                using SQLiteTransaction probe = connection.BeginTransaction();
                Execute(connection, probe, $"CREATE TABLE \"{WriteProbeTable}\" (Id INTEGER)");
                probe.Rollback();
            }
            catch (SQLiteException e)
            {
                reason = $"the database is not writable: {e.Message}";
                return false;
            }

            reason = null;
            return true;
        }

        private static string SetJournalMode(SQLiteConnection connection, string mode) =>
            Convert.ToString(Scalar(connection, "PRAGMA journal_mode=" + mode.ToUpperInvariant()), CultureInfo.InvariantCulture)?.ToLowerInvariant();

        private static void EnsureMarkerTable(SQLiteConnection connection)
        {
            Execute(connection, null,
                $"CREATE TABLE IF NOT EXISTS \"{MarkerTable}\" (Name TEXT NOT NULL PRIMARY KEY, Sha256 TEXT NOT NULL, " +
                "AppliedUtc TEXT NOT NULL, Rows INTEGER NOT NULL DEFAULT 0)");
            Execute(connection, null,
                $"CREATE TABLE IF NOT EXISTS \"{FailureTable}\" (Name TEXT NOT NULL PRIMARY KEY, Sha256 TEXT NOT NULL, " +
                "FailedUtc TEXT NOT NULL, Message TEXT)");
        }

        private static Dictionary<string, AppliedRecord> ReadApplied(SQLiteConnection connection)
        {
            var applied = new Dictionary<string, AppliedRecord>(StringComparer.OrdinalIgnoreCase);
            if (!TableExists(connection, null, MarkerTable))
                return applied;

            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT Name, Sha256, AppliedUtc, Rows FROM \"{MarkerTable}\"";
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                applied[reader.GetString(0)] = new AppliedRecord(
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    reader.IsDBNull(3) ? 0 : reader.GetInt64(3));
            }

            return applied;
        }

        private static bool TableExists(SQLiteConnection connection, SQLiteTransaction transaction, string table)
        {
            using SQLiteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM main.sqlite_master WHERE type = 'table' AND name = @name COLLATE NOCASE";
            command.Parameters.AddWithValue("@name", table);
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
        }

        private static void Execute(SQLiteConnection connection, SQLiteTransaction transaction, string sql)
        {
            using SQLiteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static string QuoteIdentifier(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

        private static string QuoteLiteral(string value) => "'" + value.Replace("'", "''") + "'";

        private static string Short(string sha) => string.IsNullOrEmpty(sha) ? "(none)" : sha.Substring(0, Math.Min(12, sha.Length));

        private static string Excerpt(string statement)
        {
            string flat = Regex.Replace(statement, @"\s+", " ").Trim();
            return flat.Length <= 160 ? flat : flat.Substring(0, 160) + "...";
        }

        #endregion
    }
}
