using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DOL.Database;
using DOL.Database.Handlers;
using DOL.GS;
using DOL.GS.DataPacks;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>World data packs (docs/TOA_DATA_PACKS.md): the applier on temporary SQLite files, and the shipped packs.</summary>
    [TestFixture, NonParallelizable]
    public class UT_WorldDataPacks
    {
        private string _root;
        private string _db;
        private string _packs;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "offline-daoc-datapacks-" + Guid.NewGuid().ToString("N"));
            _packs = Path.Combine(_root, "data-packs");
            Directory.CreateDirectory(_packs);
            _db = Path.Combine(_root, "save.sqlite3.db");
            Exec("CREATE TABLE thing (Id TEXT PRIMARY KEY, Value INTEGER NOT NULL)");
        }

        [TearDown]
        public void TearDown()
        {
            SQLiteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        #region Applier

        [Test]
        public void ApplyPending_AppliesEachPackOnce_AndRecordsIt()
        {
            WriteManifest(Pack("010-first", "INSERT INTO thing VALUES ('a', 1);\nINSERT INTO thing VALUES ('b', 2);"));

            WorldDataPackRun first = Applier().ApplyPending();
            Assert.That(first.Results.Single().State, Is.EqualTo(WorldDataPackState.Applied));
            Assert.That(first.Results.Single().AppliedNow, Is.True);
            Assert.That(first.Results.Single().Rows, Is.EqualTo(2));
            Assert.That(Scalar("SELECT COUNT(*) FROM thing"), Is.EqualTo(2));
            Assert.That(Scalar($"SELECT Rows FROM {WorldDataPackApplier.MarkerTable} WHERE Name = '010-first'"), Is.EqualTo(2));

            WorldDataPackRun second = Applier().ApplyPending();
            Assert.That(second.AppliedCount, Is.Zero);
            Assert.That(second.Results.Single().State, Is.EqualTo(WorldDataPackState.Applied));
            Assert.That(second.BackupPath, Is.Null, "no backup when nothing is pending");
            Assert.That(Scalar("SELECT COUNT(*) FROM thing"), Is.EqualTo(2), "a pack runs once per save");
        }

        [Test]
        public void ApplyPending_ShaMismatch_RefusesAndLeavesDatabaseUntouched()
        {
            WorldDataPackManifestEntry entry = Pack("010-tampered", "INSERT INTO thing VALUES ('a', 1);");
            File.AppendAllText(Path.Combine(_packs, entry.File), "INSERT INTO thing VALUES ('evil', 666);\n");
            WriteManifest(entry);

            WorldDataPackRun run = Applier().ApplyPending();

            Assert.That(run.Results.Single().State, Is.EqualTo(WorldDataPackState.HashMismatch));
            Assert.That(run.BackupPath, Is.Null, "a refused pack needs no backup");
            Assert.That(Scalar("SELECT COUNT(*) FROM thing"), Is.Zero);
            Assert.That(Scalar($"SELECT COUNT(*) FROM {WorldDataPackApplier.MarkerTable}"), Is.Zero);
        }

        [Test]
        public void ApplyPending_FailingStatement_RollsBackWholePack_AndLaterPacksStillApply()
        {
            WriteManifest(
                Pack("010-broken", "INSERT INTO thing VALUES ('a', 1);\nUPDATE thing SET Value = 5;\nINSERT INTO missing_table VALUES (1);"),
                Pack("020-good", "INSERT INTO thing VALUES ('z', 26);"));

            WorldDataPackRun run = Applier().ApplyPending();

            WorldDataPackResult broken = run.Results.Single(r => r.Name == "010-broken");
            Assert.That(broken.State, Is.EqualTo(WorldDataPackState.Failed));
            Assert.That(broken.Message, Does.Contain("statement 3 of 3").And.Contain("missing_table"));
            Assert.That(run.Results.Single(r => r.Name == "020-good").State, Is.EqualTo(WorldDataPackState.Applied));
            Assert.That(Scalar("SELECT COUNT(*) FROM thing WHERE Id = 'a'"), Is.Zero, "the failed pack left no row behind");
            Assert.That(Scalar("SELECT Value FROM thing WHERE Id = 'z'"), Is.EqualTo(26));
            Assert.That(Scalar($"SELECT COUNT(*) FROM {WorldDataPackApplier.MarkerTable} WHERE Name = '010-broken'"), Is.Zero);

            // The failure is recorded: the next start skips the pack (no retry, no backup); the good one is not re-run.
            WorldDataPackRun rerun = Applier().ApplyPending();
            Assert.That(rerun.Results.Single(r => r.Name == "010-broken").State, Is.EqualTo(WorldDataPackState.Failed));
            Assert.That(rerun.Results.Single(r => r.Name == "010-broken").SkippedAfterFailure, Is.True);
            Assert.That(rerun.Results.Single(r => r.Name == "020-good").AppliedNow, Is.False);
        }

        [Test]
        public void FailedPack_IsNotRetriedAtTheNextStart_AndTakesNoSecondBackup()
        {
            WriteManifest(Pack("010-broken", "INSERT INTO thing VALUES ('a', 1);\nINSERT INTO missing_table VALUES (1);"));

            WorldDataPackRun first = Applier().ApplyPending();
            Assert.That(first.Results.Single().State, Is.EqualTo(WorldDataPackState.Failed));
            Assert.That(first.BackupPath, Is.Not.Null);
            Assert.That(Scalar($"SELECT COUNT(*) FROM {WorldDataPackApplier.FailureTable} WHERE Name = '010-broken'"), Is.EqualTo(1));

            WorldDataPackRun second = Applier().ApplyPending();
            Assert.That(second.Results.Single().SkippedAfterFailure, Is.True);
            Assert.That(second.Results.Single().Message, Does.Contain("/datapacks apply 010-broken"));
            Assert.That(second.BackupPath, Is.Null, "a known failure triggers no backup");
            Assert.That(Backups(), Has.Length.EqualTo(1));
            Assert.That(Applier().GetQuickStatus().Single().SkippedAfterFailure, Is.True);

            // An explicit apply retries it.
            WorldDataPackRun retry = Applier().Apply("010-broken");
            Assert.That(retry.Results.Single().State, Is.EqualTo(WorldDataPackState.Failed));
            Assert.That(retry.Results.Single().SkippedAfterFailure, Is.False);
            Assert.That(retry.BackupPath, Is.Not.Null);

            // A new pack file (new sha) is tried again at the next start, and success clears the failure.
            WriteManifest(Pack("010-broken", "INSERT INTO thing VALUES ('a', 1);"));
            WorldDataPackRun fixedRun = Applier().ApplyPending();
            Assert.That(fixedRun.Results.Single().AppliedNow, Is.True);
            Assert.That(Scalar($"SELECT COUNT(*) FROM {WorldDataPackApplier.FailureTable}"), Is.Zero);
            Assert.That(Scalar("SELECT COUNT(*) FROM thing"), Is.EqualTo(1));
        }

        [TestCase("Off")]
        [TestCase("Memory")]
        public void JournalModeWithoutSafeRollback_IsRaisedForTheRun_AndPacksApply(string journalMode)
        {
            WriteManifest(Pack("010-a", "INSERT INTO thing VALUES ('a', 1);"));

            WorldDataPackRun run = new WorldDataPackApplier($"Data Source={_db};Version=3;Journal Mode={journalMode}", _packs).ApplyPending();

            Assert.That(run.RefusedJournalMode, Is.False);
            Assert.That(run.JournalMode, Is.EqualTo(journalMode.ToLowerInvariant()));
            Assert.That(run.RaisedJournalMode, Is.EqualTo(WorldDataPackApplier.RollbackJournalMode));
            Assert.That(run.Warnings, Is.Empty);
            Assert.That(run.Results.Single().AppliedNow, Is.True);
            Assert.That(run.BackupPath, Is.Not.Null);
            Assert.That(Scalar("SELECT COUNT(*) FROM thing"), Is.EqualTo(1));
            Assert.That(Scalar($"SELECT COUNT(*) FROM {WorldDataPackApplier.MarkerTable} WHERE Name = '010-a'"), Is.EqualTo(1));

            // The raised mode is not persistent: the file is not WAL, and a server connection still gets its own mode.
            Assert.That(ScalarObject("PRAGMA journal_mode"), Is.EqualTo("delete"));
            Assert.That(JournalModeOf($"Data Source={_db};Version=3;Pooling=False;Journal Mode={journalMode}"),
                Is.EqualTo(journalMode.ToLowerInvariant()));

            WorldDataPackRun second = new WorldDataPackApplier($"Data Source={_db};Version=3;Journal Mode={journalMode}", _packs).ApplyPending();
            Assert.That(second.AppliedCount, Is.Zero);
            Assert.That(second.BackupPath, Is.Null);
        }

        [Test]
        public void JournalModeOff_ReadOnlySave_CannotBeRaised_AndStillRefuses()
        {
            WriteManifest(Pack("010-a", "INSERT INTO thing VALUES ('a', 1);"));
            File.SetAttributes(_db, File.GetAttributes(_db) | FileAttributes.ReadOnly);
            WorldDataPackRun run;
            try
            {
                run = new WorldDataPackApplier($"Data Source={_db};Version=3;Journal Mode=Off", _packs).ApplyPending();
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                File.SetAttributes(_db, File.GetAttributes(_db) & ~FileAttributes.ReadOnly);
            }

            Assert.That(run.RefusedJournalMode, Is.True);
            Assert.That(run.JournalMode, Is.EqualTo("off"));
            Assert.That(run.RaisedJournalMode, Is.Null);
            Assert.That(run.Warnings.Single(), Does.Contain("journal mode").And.Contain("could not be raised").And.Contain("not writable"));
            Assert.That(run.Results, Is.Empty);
            Assert.That(run.BackupPath, Is.Null);
            Assert.That(Backups(), Is.Empty);
            Assert.That(Scalar("SELECT COUNT(*) FROM thing"), Is.Zero);
            Assert.That(Scalar($"SELECT COUNT(*) FROM sqlite_master WHERE name = '{WorldDataPackApplier.MarkerTable}'"), Is.Zero);
        }

        [Test]
        public void JournalModeOff_RaisedForTheRun_FailingLargePackRollsBackFully()
        {
            Exec("INSERT INTO thing VALUES ('keep', 7)");
            // Far more than the 2 MB page cache, so SQLite writes pages into the database file before the failure;
            // only a real rollback journal can undo those (with Journal Mode=Off the rollback is undefined).
            const string bulk = "INSERT INTO thing SELECT 'r' || x, x FROM " +
                                "(WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c WHERE x < 200000) SELECT x FROM c);";
            WriteManifest(
                Pack("010-broken", bulk + "\nUPDATE thing SET Value = -1;\nINSERT INTO missing_table VALUES (1);"),
                Pack("020-good", "INSERT INTO thing VALUES ('z', 26);"));

            WorldDataPackRun run = new WorldDataPackApplier($"Data Source={_db};Version=3;Journal Mode=Off", _packs).ApplyPending();

            Assert.That(run.RaisedJournalMode, Is.EqualTo(WorldDataPackApplier.RollbackJournalMode));
            WorldDataPackResult broken = run.Results.Single(r => r.Name == "010-broken");
            Assert.That(broken.State, Is.EqualTo(WorldDataPackState.Failed));
            Assert.That(broken.Message, Does.Contain("statement 3 of 3").And.Contain("missing_table"));
            Assert.That(run.Results.Single(r => r.Name == "020-good").AppliedNow, Is.True);

            Assert.That(Strings("SELECT Id || '=' || Value FROM thing ORDER BY Id"), Is.EqualTo(new[] { "keep=7", "z=26" }));
            Assert.That(ScalarObject("PRAGMA integrity_check"), Is.EqualTo("ok"));
            Assert.That(Scalar($"SELECT COUNT(*) FROM {WorldDataPackApplier.MarkerTable} WHERE Name = '010-broken'"), Is.Zero);
            Assert.That(Scalar($"SELECT COUNT(*) FROM {WorldDataPackApplier.FailureTable} WHERE Name = '010-broken'"), Is.EqualTo(1));
        }

        [Test]
        public void RaisedJournal_HotJournalOfAnInterruptedPack_IsRolledBackByAJournalOffConnection()
        {
            // The safety argument for raising only the applier's connections: if the process dies mid-pack, the server's
            // next connection (Journal Mode=Off) still rolls the hot journal back before it reads.
            Exec("INSERT INTO thing VALUES ('keep', 7)");
            string crashDb = Path.Combine(_root, "crashed.sqlite3.db");
            using (var connection = new SQLiteConnection($"Data Source={_db};Version=3;Pooling=False;Journal Mode=Off"))
            {
                connection.Open();
                using (SQLiteCommand pragma = connection.CreateCommand())
                {
                    pragma.CommandText = "PRAGMA journal_mode=TRUNCATE";
                    Assert.That(pragma.ExecuteScalar(), Is.EqualTo("truncate"));
                    pragma.CommandText = "PRAGMA cache_size=10";
                    pragma.ExecuteNonQuery();
                }

                using SQLiteTransaction transaction = connection.BeginTransaction();
                using (SQLiteCommand insert = connection.CreateCommand())
                {
                    insert.Transaction = transaction;
                    insert.CommandText = "INSERT INTO thing SELECT 'r' || x, x FROM " +
                                         "(WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c WHERE x < 20000) SELECT x FROM c)";
                    insert.ExecuteNonQuery();
                    insert.CommandText = "UPDATE thing SET Value = -1 WHERE Id = 'keep'";
                    insert.ExecuteNonQuery();
                }

                // "Crash": copy the database and its journal as they are on disk in the middle of the transaction.
                CopyShared(_db, crashDb);
                CopyShared(_db + "-journal", crashDb + "-journal");
                transaction.Rollback();
            }

            Assert.That(new FileInfo(crashDb + "-journal").Length, Is.GreaterThan(512), "the copied journal is hot");
            Assert.That(new FileInfo(crashDb).Length, Is.GreaterThan(new FileInfo(_db).Length), "spilled pages reached the copied file");

            using var reader = new SQLiteConnection($"Data Source={crashDb};Version=3;Pooling=False;Journal Mode=Off");
            reader.Open();
            using SQLiteCommand query = reader.CreateCommand();
            query.CommandText = "SELECT Id || '=' || Value FROM thing";
            Assert.That(query.ExecuteScalar(), Is.EqualTo("keep=7"));
            query.CommandText = "SELECT COUNT(*) FROM thing";
            Assert.That(query.ExecuteScalar(), Is.EqualTo(1L));
            query.CommandText = "PRAGMA integrity_check";
            Assert.That(query.ExecuteScalar(), Is.EqualTo("ok"));
        }

        [Test]
        public void Backups_KeepOnlyTheNewestThree()
        {
            string prefix = _db + WorldDataPackApplier.BackupInfix;
            for (int i = 0; i < 4; i++)
            {
                string old = prefix + "2026010" + i + "-000000";
                File.WriteAllText(old, "old backup " + i);
                File.SetLastWriteTimeUtc(old, new DateTime(2026, 1, 1 + i, 0, 0, 0, DateTimeKind.Utc));
            }

            WriteManifest(Pack("010-a", "INSERT INTO thing VALUES ('a', 1);"));
            WorldDataPackRun run = Applier().ApplyPending();

            string[] left = Backups();
            Assert.That(left, Has.Length.EqualTo(WorldDataPackApplier.BackupsKept));
            Assert.That(left, Does.Contain(run.BackupPath));
            Assert.That(left, Does.Contain(prefix + "20260103-000000").And.Contain(prefix + "20260102-000000"));
            Assert.That(run.PrunedBackups, Has.Count.EqualTo(2));
        }

        [Test]
        public void QuickStatus_ReadsNoPackFiles()
        {
            WorldDataPackManifestEntry tampered = Pack("010-tampered", "INSERT INTO thing VALUES ('a', 1);");
            File.AppendAllText(Path.Combine(_packs, tampered.File), "-- changed\n");
            WorldDataPackManifestEntry missing = Pack("020-missing", "INSERT INTO thing VALUES ('b', 1);");
            File.Delete(Path.Combine(_packs, missing.File));
            WriteManifest(tampered, missing);

            IReadOnlyList<WorldDataPackResult> quick = Applier().GetQuickStatus();
            Assert.That(quick.Single(s => s.Name == "010-tampered").State, Is.EqualTo(WorldDataPackState.Pending), "not hashed");
            Assert.That(quick.Single(s => s.Name == "010-tampered").FilePresent, Is.True);
            Assert.That(quick.Single(s => s.Name == "020-missing").State, Is.EqualTo(WorldDataPackState.MissingFile));
            Assert.That(quick.Single(s => s.Name == "020-missing").FilePresent, Is.False);
            Assert.That(Applier().GetStatus().Single(s => s.Name == "010-tampered").State, Is.EqualTo(WorldDataPackState.HashMismatch));
        }

        [Test]
        public void ApplyPending_TakesOneBackupBeforeTheFirstPack_HoldingThePreviousState()
        {
            Exec("INSERT INTO thing VALUES ('old', 1)");
            WriteManifest(Pack("010-a", "INSERT INTO thing VALUES ('a', 1);"), Pack("011-b", "INSERT INTO thing VALUES ('b', 1);"));

            WorldDataPackRun run = Applier().ApplyPending();

            Assert.That(run.AppliedCount, Is.EqualTo(2));
            Assert.That(run.BackupPath, Is.Not.Null);
            Assert.That(Path.GetDirectoryName(run.BackupPath), Is.EqualTo(Path.GetDirectoryName(_db)));
            Assert.That(Path.GetFileName(run.BackupPath), Does.StartWith(Path.GetFileName(_db) + WorldDataPackApplier.BackupInfix));
            string[] backups = Directory.GetFiles(_root, Path.GetFileName(_db) + WorldDataPackApplier.BackupInfix + "*");
            Assert.That(backups, Has.Length.EqualTo(1), "one backup per run, not per pack");
            using var backup = new SQLiteConnection($"Data Source={run.BackupPath};Pooling=False;FailIfMissing=True");
            backup.Open();
            using SQLiteCommand count = backup.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM thing";
            Assert.That(Convert.ToInt64(count.ExecuteScalar()), Is.EqualTo(1), "the backup holds the state before any pack");
        }

        [Test]
        public void GzipPack_Applies_AndItsHashIsTheUncompressedText()
        {
            string sql = "INSERT INTO thing VALUES ('gz', 7);\n";
            byte[] bytes = Encoding.UTF8.GetBytes(sql);
            using (FileStream file = File.Create(Path.Combine(_packs, "010-zipped.sql.gz")))
            using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
                gzip.Write(bytes);
            WriteManifest(new WorldDataPackManifestEntry { Name = "010-zipped", File = "010-zipped.sql.gz", Sha256 = Sha(bytes) });

            WorldDataPackRun run = Applier().ApplyPending();

            Assert.That(run.Results.Single().State, Is.EqualTo(WorldDataPackState.Applied));
            Assert.That(Scalar("SELECT Value FROM thing WHERE Id = 'gz'"), Is.EqualTo(7));
        }

        [TestCase("INSERT INTO thing VALUES ('a', 1);\nCOMMIT;")]
        [TestCase("BEGIN TRANSACTION;\nINSERT INTO thing VALUES ('a', 1);")]
        [TestCase("PRAGMA foreign_keys = OFF;")]
        [TestCase("CREATE TRIGGER t AFTER INSERT ON thing BEGIN SELECT 1; END;")]
        [TestCase("DELETE FROM offline_data_packs;")]
        [TestCase("DELETE FROM offline_data_pack_failures;")]
        public void ForbiddenStatement_RefusesThePack(string sql)
        {
            WriteManifest(Pack("010-forbidden", sql));

            WorldDataPackRun run = Applier().ApplyPending();

            Assert.That(run.Results.Single().State, Is.EqualTo(WorldDataPackState.Invalid));
            Assert.That(Scalar("SELECT COUNT(*) FROM thing"), Is.Zero);
        }

        [Test]
        public void AppliedPackWhoseFileChanged_IsNotRerun()
        {
            WriteManifest(Pack("010-once", "INSERT INTO thing VALUES ('a', 1);"));
            Applier().ApplyPending();
            WriteManifest(Pack("010-once", "INSERT INTO thing VALUES ('b', 2);"));

            WorldDataPackRun run = Applier().ApplyPending();

            Assert.That(run.Results.Single().State, Is.EqualTo(WorldDataPackState.AppliedDifferentHash));
            Assert.That(run.AppliedCount, Is.Zero);
            Assert.That(Scalar("SELECT COUNT(*) FROM thing WHERE Id = 'b'"), Is.Zero);
        }

        [Test]
        public void ApplyByName_AppliesOnlyThatPack_AndStatusShowsBoth()
        {
            WriteManifest(Pack("010-a", "INSERT INTO thing VALUES ('a', 1);"), Pack("020-b", "INSERT INTO thing VALUES ('b', 2);"));

            Assert.That(Applier().GetStatus().Select(s => s.State), Is.All.EqualTo(WorldDataPackState.Pending));
            WorldDataPackRun run = Applier().Apply("020-b");
            Assert.That(run.Results.Select(r => r.Name), Is.EqualTo(new[] { "020-b" }));

            IReadOnlyList<WorldDataPackResult> status = Applier().GetStatus();
            Assert.That(status.Single(s => s.Name == "010-a").State, Is.EqualTo(WorldDataPackState.Pending));
            Assert.That(status.Single(s => s.Name == "020-b").State, Is.EqualTo(WorldDataPackState.Applied));
            Assert.That(Applier().Apply("999-none").Warnings.Single(), Does.Contain("No pack named"));
        }

        [Test]
        public void UnlistedPackFile_IsIgnoredWithAWarning()
        {
            WriteManifest(Pack("010-a", "INSERT INTO thing VALUES ('a', 1);"));
            File.WriteAllText(Path.Combine(_packs, "099-stray.sql"), "INSERT INTO thing VALUES ('stray', 1);");

            WorldDataPackRun run = Applier().ApplyPending();

            Assert.That(run.Warnings.Single(), Does.Contain("099-stray.sql"));
            Assert.That(Scalar("SELECT COUNT(*) FROM thing WHERE Id = 'stray'"), Is.Zero);
        }

        [Test]
        public void CacheWatch_ReportsInsertedUpdatedAndDeletedKeys()
        {
            Exec("INSERT INTO thing VALUES ('keep', 1), ('change', 1), ('drop', 1)");
            WriteManifest(Pack("010-cache",
                "INSERT INTO thing VALUES ('new', 1);\nUPDATE thing SET Value = 2 WHERE Id = 'change';\nDELETE FROM thing WHERE Id = 'drop';"));

            WorldDataPackRun run = Applier(new WorldDataPackCacheWatch("thing", "Id"), new WorldDataPackCacheWatch("absent", "Id"))
                .ApplyPending();

            Assert.That(run.Results.Single().State, Is.EqualTo(WorldDataPackState.Applied));
            Assert.That(run.ChangedKeys["thing"], Is.EquivalentTo(new[] { "new", "change", "drop" }));
        }

        [Test]
        public void SplitStatements_RespectsQuotesAndComments()
        {
            const string sql = "-- leading; comment\n" +
                               "INSERT INTO t VALUES ('Typhon''s; Reach', \"a;b\", `c;d`, [e;f]);\n" +
                               "/* block ; comment */ UPDATE t SET x = 1;\n" +
                               "-- trailing comment only;\n";

            List<string> statements = WorldDataPackApplier.SplitStatements(sql).ToList();

            Assert.That(statements, Has.Count.EqualTo(2));
            Assert.That(statements[0], Does.Contain("'Typhon''s; Reach'").And.Contain("[e;f]"));
            Assert.That(statements[1], Does.EndWith("UPDATE t SET x = 1"));
            Assert.Throws<FormatException>(() => WorldDataPackApplier.SplitStatements("INSERT INTO t VALUES ('open").ToList());
        }

        [Test]
        public void RuntimeSqlite_SupportsUpsertWithoutConflictTarget()
        {
            // The converted gaheris files use ON CONFLICT DO UPDATE/NOTHING without a target (SQLite 3.35+).
            string version = (string)ScalarObject("SELECT sqlite_version()");
            TestContext.Out.WriteLine($"System.Data.SQLite runtime SQLite {version}");
            Version parsed = Version.Parse(version);
            Assert.That(parsed, Is.GreaterThanOrEqualTo(new Version(3, 35, 0)));

            Exec("INSERT INTO thing VALUES ('u', 1) ON CONFLICT DO UPDATE SET Value = excluded.Value");
            Exec("INSERT INTO thing VALUES ('u', 2) ON CONFLICT DO UPDATE SET Value = excluded.Value");
            Exec("INSERT INTO thing VALUES ('u', 3) ON CONFLICT DO NOTHING");
            Assert.That(Scalar("SELECT Value FROM thing WHERE Id = 'u'"), Is.EqualTo(2));
        }

        #endregion

        #region Master Level specs

        [Test]
        public void MasterLevelSpecs_AreUntrainableCareerSpecs()
        {
            // Pack 011 puts the eight ML paths into every class career at level 50. GamePlayer.LoadClassSpecializations
            // only adds them when MLGranted and MLLevel >= 1, GameBot skips them, and /train and the trainer window list
            // only Trainable specs; this pins the type facts those paths rely on.
            var spec = new LiveMasterLevelsSpecialization("Banelord", "Banelord", 0, 150);
            Assert.That(spec, Is.InstanceOf<IMasterLevelsSpecialization>());
            Assert.That(spec.Trainable, Is.False);
        }

        #endregion

        #region Shipped packs

        [Test]
        public void ShippedPacks_ManifestHashesMatch_AndNoForbiddenStatements()
        {
            string directory = ShippedPackDirectory();
            var applier = new WorldDataPackApplier($"Data Source={_db}", directory);
            IReadOnlyList<WorldDataPackManifestEntry> manifest = applier.ReadManifest();

            Assert.That(manifest.Select(e => e.Name), Is.EqualTo(new[]
            {
                "010-toa-ml-spells", "011-toa-ml-paths", "012-toa-artifact-spells", "020-atlantis-mobs", "021-atlantis-travel",
                "022-atlantis-zonepoints", "023-artifact-scroll-binders", "024-toa-arbiter-npcs", "025-toa-atlantis-arena-fix",
                "026-toa-travel-realms", "027-toa-open-atlantis", "030-toa-dungeon-mobs", "031-toa-dungeon-templates", "040-toa-loot",
                "041-toa-loot-caps", "050-artifact-item-classtype",
            }));
            Assert.That(applier.UnlistedFiles(manifest), Is.Empty);
            Assert.That(applier.GetStatus().Select(s => s.State), Is.All.EqualTo(WorldDataPackState.Pending),
                "every shipped pack reads, matches its manifest hash and has only allowed statements");
            Assert.That(manifest.Single(e => e.Name == "020-atlantis-mobs").File, Does.EndWith(".sql.gz"));
        }

        [Test]
        public void ShippedPacks_ApplyOnTheServerSchema()
        {
            // The schema the server itself creates from the DataObject definitions, empty.
            var database = new SqliteObjectDatabase($"Data Source={_db};Version=3;Pooling=False;Journal Mode=WAL;");
            foreach (Type table in new[]
                     {
                         typeof(DbSpell), typeof(DbLineXSpell), typeof(DbClassXSpecialization), typeof(DbMob), typeof(DbTeleport),
                         typeof(DbZonePoint), typeof(DbItemTemplate), typeof(DbMerchantItem), typeof(DbNpcTemplate), typeof(DbNpcEquipment),
                         typeof(DbLootTemplate), typeof(DbMobXLootTemplate), typeof(DbArtifactXItem), typeof(DbServerProperty),
                     })
                database.RegisterDataObject(table);

            WorldDataPackRun run = new WorldDataPackApplier($"Data Source={_db};Journal Mode=WAL", ShippedPackDirectory(),
                WorldDataPacks.CacheWatches).ApplyPending();

            foreach (WorldDataPackResult result in run.Results)
                TestContext.Out.WriteLine($"{result.Name}: {result.State}, {result.Rows} rows, {result.Elapsed.TotalSeconds:0.0} s - {result.Message}");
            Assert.That(run.Results.Select(r => r.State), Is.All.EqualTo(WorldDataPackState.Applied));
            Assert.That(Scalar("SELECT COUNT(*) FROM Mob WHERE PackageID = 'gaheris-atlantis'"), Is.EqualTo(41727));
            Assert.That(Scalar("SELECT COUNT(*) FROM LineXSpell"), Is.EqualTo(73));
            Assert.That(Scalar("SELECT COUNT(*) FROM Teleport WHERE Type = 'gaheris'"), Is.EqualTo(13));
            Assert.That(Scalar("SELECT COUNT(*) FROM ZonePoint"), Is.EqualTo(100));
            Assert.That(Scalar("SELECT COUNT(*) FROM MerchantItem"), Is.EqualTo(162));
            Assert.That(Scalar("SELECT COUNT(*) FROM Mob WHERE Name = 'Arbiter' AND ClassType = 'DOL.GS.MasterLevelArbiter'"), Is.EqualTo(3));
            Assert.That(Scalar("SELECT COUNT(*) FROM Mob WHERE PackageID = 'dbpublic-toa-dungeons'"), Is.EqualTo(2135));
            Assert.That(Scalar("SELECT COUNT(*) FROM NpcTemplate"), Is.EqualTo(119));
            Assert.That(Scalar("SELECT COUNT(*) FROM NPCEquipment"), Is.EqualTo(827));
            Assert.That(Scalar("SELECT COUNT(*) FROM LootTemplate"), Is.EqualTo(2106));
            Assert.That(Scalar("SELECT COUNT(*) FROM MobXLootTemplate"), Is.EqualTo(364 + 1), "the 040 links and the Phoenix cap");
            AssertAtlantisTravelRows();
            Assert.That(Scalar("SELECT COUNT(*) FROM Mob WHERE PackageID = 'toa-travel'"), Is.EqualTo(10), "026: the haven porters");

            WorldDataPackRun again = new WorldDataPackApplier($"Data Source={_db}", ShippedPackDirectory()).ApplyPending();
            Assert.That(again.AppliedCount, Is.Zero);
        }

        [Test]
        public void ShippedPacks_ApplyOnTheDevSnapshot()
        {
            string snapshot = FindSnapshot();
            if (snapshot == null)
                Assert.Ignore("No repaired dev snapshot (.toa-probe/snapshot.clean.db or OFFLINE_DAOC_TOA_SNAPSHOT); see docs/TOA_DATA_PACKS.md.");

            File.Delete(_db);
            File.Copy(snapshot, _db);
            long mobs = Scalar("SELECT COUNT(*) FROM Mob");
            long lootRows = Scalar("SELECT COUNT(*) FROM LootTemplate");
            long classSpecs = Scalar("SELECT COUNT(*) FROM ClassXSpecialization");
            long zonePoints = Scalar("SELECT COUNT(*) FROM ZonePoint");
            string spell15001 = (string)ScalarObject("SELECT Name FROM Spell WHERE SpellID = 15001");
            long spells = Scalar("SELECT COUNT(*) FROM Spell");
            var stopwatch = Stopwatch.StartNew();

            // The live install's connection options (serverconfig.xml), so WAL and foreign keys match the real save.
            WorldDataPackRun run = new WorldDataPackApplier(
                    $"Data Source={_db};Version=3;Pooling=False;Journal Mode=WAL;Synchronous=Normal;Foreign Keys=True;Default Timeout=60",
                    ShippedPackDirectory(), WorldDataPacks.CacheWatches)
                .ApplyPending();

            TestContext.Out.WriteLine($"Snapshot {snapshot}, total {stopwatch.Elapsed.TotalSeconds:0.0} s, backup {run.BackupPath}");
            foreach (WorldDataPackResult result in run.Results)
                TestContext.Out.WriteLine($"{result.Name}: {result.State}, {result.Statements} statements, {result.Rows} rows, " +
                                          $"{result.Elapsed.TotalSeconds:0.0} s - {result.Message}");
            foreach ((string table, HashSet<string> keys) in run.ChangedKeys)
                TestContext.Out.WriteLine($"cache refresh: {keys.Count} {table} keys");

            Assert.That(run.Results.Select(r => r.State), Is.All.EqualTo(WorldDataPackState.Applied));
            Assert.That(run.BackupPath, Is.Not.Null);
            Assert.That(Scalar("SELECT COUNT(*) FROM Mob") - mobs, Is.EqualTo(41727 + 2135 - 3 + 10),
                "020 and 030 add, 025 removes three, 026 adds ten porters");
            Assert.That(Scalar("SELECT COUNT(*) FROM ClassXSpecialization") - classSpecs, Is.EqualTo(496));
            Assert.That(Scalar("SELECT COUNT(*) FROM ZonePoint") - zonePoints, Is.EqualTo(100));
            Assert.That(Scalar("SELECT COUNT(*) FROM Spell") - spells, Is.EqualTo(73 + 73), "73 ML and 73 artifact spells");
            Assert.That(Scalar("SELECT COUNT(*) FROM LineXSpell WHERE LineName IN ('Banelord','Battlemaster','Convoker','Perfecter'," +
                               "'Sojourner','Spymaster','Stormlord','Warlord')"), Is.EqualTo(73));
            Assert.That(Scalar("SELECT COUNT(*) FROM ItemTemplate WHERE Price = 300 AND Id_nb LIKE '%of_3'"), Is.GreaterThanOrEqualTo(150));
            Assert.That(run.ChangedKeys.ContainsKey("ItemTemplate"), Is.True, "the scroll price update is reported for the cache refresh");

            // Pack 012 (with 7206 "Purify Vision" from pack 010, the Erinys Charm use spell): every spell our artifact
            // items reference resolves, except four that exist nowhere.
            const string artifactSpellIds =
                "SELECT DISTINCT v FROM (" +
                "SELECT i.SpellID v FROM ItemTemplate i JOIN ArtifactXItem a ON a.ItemID = i.Id_nb UNION ALL " +
                "SELECT i.SpellID1 FROM ItemTemplate i JOIN ArtifactXItem a ON a.ItemID = i.Id_nb UNION ALL " +
                "SELECT i.ProcSpellID FROM ItemTemplate i JOIN ArtifactXItem a ON a.ItemID = i.Id_nb UNION ALL " +
                "SELECT i.ProcSpellID1 FROM ItemTemplate i JOIN ArtifactXItem a ON a.ItemID = i.Id_nb UNION ALL " +
                "SELECT i.PoisonSpellID FROM ItemTemplate i JOIN ArtifactXItem a ON a.ItemID = i.Id_nb) WHERE v > 0";
            Assert.That(Scalar($"SELECT COUNT(*) FROM ({artifactSpellIds})"), Is.EqualTo(85));
            Assert.That(Ids($"SELECT v FROM ({artifactSpellIds}) WHERE v NOT IN (SELECT SpellID FROM Spell) ORDER BY v"),
                Is.EqualTo(new long[] { 13135, 30115, 30125, 32223 }));
            Assert.That((string)ScalarObject("SELECT Type FROM Spell WHERE SpellID = 7206"), Is.EqualTo("CureNearsight"));
            Assert.That((string)ScalarObject("SELECT Name FROM Spell WHERE SpellID = 15001"), Is.EqualTo(spell15001),
                "pack 012 ships only artifact rows; gaheris' replacement of 15001 does not ship");

            // Pack set 2.
            Assert.That(Scalar("SELECT COUNT(*) FROM Mob WHERE Name = 'Arbiter' AND ClassType = 'DOL.GS.MasterLevelArbiter'"), Is.EqualTo(3),
                "024: the three Atlantis Arbiters enrol");
            Assert.That(Scalar("SELECT COUNT(*) FROM Mob WHERE Name = 'Arbiter' AND ClassType <> 'DOL.GS.MasterLevelArbiter'"), Is.Zero);
            Assert.That(Scalar("SELECT COUNT(*) FROM Mob WHERE Name = 'Demyphon' AND ClassType = 'DOL.GS.Scripts.MasterLevelsMerchant' " +
                               "AND ItemsListTemplateID = 'Master Level Credits'"), Is.EqualTo(3), "024: Demyphon sells in 70, 71 and 72");
            Assert.That(Scalar("SELECT COUNT(*) FROM Mob WHERE ClassType = 'DOL.GS.LordOfBattle' AND Region IN (73, 80)"), Is.Zero,
                "025: no fight-club Mordred in Albion Atlantis");
            Assert.That(Ids("SELECT Region FROM Mob WHERE ClassType = 'DOL.GS.LordOfBattle' ORDER BY Region"), Is.EqualTo(new long[] { 334, 339 }));
            Assert.That(Scalar("SELECT COUNT(*) FROM Mob WHERE PackageID = 'dbpublic-toa-dungeons'"), Is.EqualTo(2135), "030");
            Assert.That(Scalar("SELECT COUNT(*) FROM LootTemplate") - lootRows, Is.EqualTo(2106), "040");
            Assert.That(Scalar("SELECT COUNT(*) FROM MobXLootTemplate WHERE MobName = 'the phoenix' AND LootTemplateName = 'the phoenix' " +
                               "AND DropCount = 1"), Is.EqualTo(1), "041: the Phoenix cap");
            Assert.That(Scalar("SELECT COUNT(*) FROM LootTemplate WHERE TemplateName = 'the phoenix'"), Is.EqualTo(23));
            Assert.That(Scalar("SELECT COUNT(*) FROM ItemTemplate WHERE ClassType = 'DOL.GS.InventoryArtifact'"), Is.EqualTo(197), "050");
            Assert.That(Scalar("SELECT COUNT(*) FROM ItemTemplate WHERE ClassType = 'DOL.GS.InventoryArtifact' " +
                               "AND Id_nb NOT IN (SELECT ItemID FROM ArtifactXItem)"), Is.Zero);
            List<string> artifactTemplates = Strings("SELECT Id_nb FROM ItemTemplate WHERE ClassType = 'DOL.GS.InventoryArtifact'");
            Assert.That(artifactTemplates.Where(id => !run.ChangedKeys["ItemTemplate"].Contains(id)), Is.Empty,
                "050 is reported for the ItemTemplate cache refresh");

            // Packs 026 and 027 (WP1.2/1.3, docs/TOA_TRAVEL.md).
            AssertAtlantisTravelRows();
            Assert.That(Scalar("SELECT COUNT(*) FROM Mob WHERE PackageID = 'toa-travel' AND Flags & 16 = 16 AND AggroLevel = 0"),
                Is.EqualTo(10), "026: ten peaceful porters");
            Assert.That(Scalar("SELECT COUNT(*) FROM Mob WHERE PackageID = 'toa-travel' AND Region IN (70, 71, 72)"), Is.EqualTo(3),
                "026: one porter per ruins city");
            Assert.That(Strings("SELECT Region || ':' || ClassType FROM Mob WHERE Name LIKE 'Channeler %' AND Region IN (73, 130) " +
                                "AND PackageID = 'gaheris-atlantis' ORDER BY Region, Name"),
                Is.EqualTo(new[]
                {
                    "73:DOL.GS.AlbionTeleporter", "73:DOL.GS.AlbionTeleporter",
                    "130:DOL.GS.HiberniaTeleporter", "130:DOL.GS.HiberniaTeleporter",
                }), "026: Khol, Raifaol, Hialysam and Lana are porters");
            Assert.That(Scalar("SELECT COUNT(*) FROM Mob WHERE Region = 130 AND Name IN ('Channeler Hialysam', 'Channeler Lana') " +
                               "AND NPCTemplateID = -1 AND Flags & 16 = 16"), Is.EqualTo(2), "026: their template no longer drops the peace flag");
            Assert.That((string)ScalarObject("SELECT Value FROM ServerProperty WHERE \"Key\" = 'atlantis_teleport_plvl'"), Is.EqualTo("1"),
                "027 opens Atlantis (the snapshot has the setup value 4)");
            Assert.That((string)ScalarObject("SELECT Value FROM ServerProperty WHERE \"Key\" = 'rog_toa_item_chance'"), Is.EqualTo("25"));
            Assert.That(run.ChangedKeys["ServerProperty"], Is.EquivalentTo(new[] { "atlantis_teleport_plvl", "rog_toa_item_chance" }),
                "027 is reported, so the server reloads the properties on this boot");
            long teleports = Scalar("SELECT COUNT(*) FROM Teleport");
            long allMobs = Scalar("SELECT COUNT(*) FROM Mob");
            ExecuteCountingChanges(File.ReadAllText(Path.Combine(ShippedPackDirectory(), "026-toa-travel-realms.sql")));
            Assert.That(Scalar("SELECT COUNT(*) FROM Teleport"), Is.EqualTo(teleports), "026 applied a second time keeps its rows");
            Assert.That(Scalar("SELECT COUNT(*) FROM Mob"), Is.EqualTo(allMobs));

            // The pack set 2 files and 027 are idempotent: a second run of their statements changes nothing.
            foreach (string name in new[]
                     {
                         "024-toa-arbiter-npcs", "025-toa-atlantis-arena-fix", "027-toa-open-atlantis", "030-toa-dungeon-mobs",
                         "031-toa-dungeon-templates", "040-toa-loot", "041-toa-loot-caps", "050-artifact-item-classtype",
                     })
                Assert.That(ExecuteCountingChanges(File.ReadAllText(Path.Combine(ShippedPackDirectory(), name + ".sql"))), Is.Zero,
                    name + " applied a second time");

            Assert.That(ScalarObject("PRAGMA quick_check"), Is.EqualTo("ok"));
        }

        #endregion

        #region Helpers

        // Pack 026: every Atlantis destination exactly once per realm, in that realm's copy (DOL.GS.Tests.UT_AtlantisTravel.ExpectedRegion).
        private void AssertAtlantisTravelRows()
        {
            Assert.That(Scalar("SELECT COUNT(*) FROM Teleport WHERE Type = 'atlantis'"), Is.EqualTo(33));
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            foreach (string destination in AtlantisTravel.Destinations)
            {
                string where = $"Type = 'atlantis' AND Realm = {(int)realm} AND TeleportID = '{destination.Replace("'", "''")}'";
                Assert.That(Scalar($"SELECT COUNT(*) FROM Teleport WHERE {where}"), Is.EqualTo(1), $"{realm} {destination}");
                Assert.That(Scalar($"SELECT RegionID FROM Teleport WHERE {where}"),
                    Is.EqualTo(DOL.GS.Tests.UT_AtlantisTravel.ExpectedRegion(realm, destination)), $"{realm} {destination}");
            }
        }

        private WorldDataPackApplier Applier(params WorldDataPackCacheWatch[] watches) =>
            new($"Data Source={_db};Version=3;Journal Mode=WAL", _packs, watches);

        private WorldDataPackManifestEntry Pack(string name, string sql)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(sql + "\n");
            File.WriteAllBytes(Path.Combine(_packs, name + ".sql"), bytes);
            return new WorldDataPackManifestEntry { Name = name, File = name + ".sql", Sha256 = Sha(bytes), Description = "test pack" };
        }

        private void WriteManifest(params WorldDataPackManifestEntry[] entries)
        {
            var manifest = new
            {
                format = 1,
                packs = entries.Select(e => new { name = e.Name, file = e.File, sha256 = e.Sha256, description = e.Description ?? string.Empty }),
            };
            File.WriteAllText(Path.Combine(_packs, WorldDataPackApplier.ManifestFileName), JsonSerializer.Serialize(manifest));
        }

        private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        private void Exec(string sql)
        {
            using var connection = new SQLiteConnection($"Data Source={_db};Version=3;Pooling=False");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private long Scalar(string sql) => Convert.ToInt64(ScalarObject(sql));

        private static string JournalModeOf(string connectionString)
        {
            using var connection = new SQLiteConnection(connectionString);
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode";
            return Convert.ToString(command.ExecuteScalar())?.ToLowerInvariant();
        }

        /// <summary>Copies a file that SQLite holds open (it shares read and write).</summary>
        private static void CopyShared(string source, string destination)
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
            input.CopyTo(output);
        }

        private string[] Backups() =>
            Directory.GetFiles(_root, Path.GetFileName(_db) + WorldDataPackApplier.BackupInfix + "*").OrderBy(f => f, StringComparer.Ordinal).ToArray();

        private long[] Ids(string sql)
        {
            using var connection = new SQLiteConnection($"Data Source={_db};Version=3;Pooling=False");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            var ids = new List<long>();
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                ids.Add(reader.GetInt64(0));
            return ids.ToArray();
        }

        private List<string> Strings(string sql)
        {
            using var connection = new SQLiteConnection($"Data Source={_db};Version=3;Pooling=False");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            var values = new List<string>();
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                values.Add(reader.GetString(0));
            return values;
        }

        /// <summary>Runs a pack's statements in one transaction that is rolled back, and returns the rows they changed.</summary>
        private long ExecuteCountingChanges(string sql)
        {
            using var connection = new SQLiteConnection($"Data Source={_db};Version=3;Pooling=False");
            connection.Open();
            using SQLiteTransaction transaction = connection.BeginTransaction();
            long changed = 0;
            foreach (string statement in WorldDataPackApplier.SplitStatements(sql))
            {
                using SQLiteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = statement;
                changed += Math.Max(0, command.ExecuteNonQuery());
            }

            transaction.Rollback();
            return changed;
        }

        private object ScalarObject(string sql)
        {
            using var connection = new SQLiteConnection($"Data Source={_db};Version=3;Pooling=False");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar();
        }

        private static string ShippedPackDirectory()
        {
            for (DirectoryInfo directory = new(TestContext.CurrentContext.TestDirectory); directory != null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, "CoreServer", WorldDataPacks.FolderName);
                if (File.Exists(Path.Combine(candidate, WorldDataPackApplier.ManifestFileName)))
                    return candidate;
                candidate = Path.Combine(directory.FullName, "source", "server", "CoreServer", WorldDataPacks.FolderName);
                if (File.Exists(Path.Combine(candidate, WorldDataPackApplier.ManifestFileName)))
                    return candidate;
            }

            Assert.Fail("source/server/CoreServer/data-packs/manifest.json not found above the test directory.");
            return null;
        }

        private static string FindSnapshot()
        {
            string configured = Environment.GetEnvironmentVariable("OFFLINE_DAOC_TOA_SNAPSHOT");
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
                return configured;

            for (DirectoryInfo directory = new(TestContext.CurrentContext.TestDirectory); directory != null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, ".toa-probe", "snapshot.clean.db");
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        #endregion
    }
}
