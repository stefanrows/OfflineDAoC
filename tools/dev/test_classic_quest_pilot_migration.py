#!/usr/bin/env python3
"""Focused disposable-copy regression checks for the classic quest pilot.

Run with:
    OFFLINE_DAOC_QUEST_PILOT_SOURCE=/path/to/clean/runtime/data/opendaoc.sqlite3.db \
        python3 tools/dev/test_classic_quest_pilot_migration.py

The fixture is the pinned clean v0.35b release database. Every mutation is
performed on a temporary SQLite backup carrying the utility's disposable
marker; the release fixture itself is opened read-only.
"""

from __future__ import annotations

import hashlib
import importlib.util
import json
import os
import sqlite3
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


HERE = Path(__file__).resolve().parent
SCRIPT = HERE / "migrate_classic_quest_pilot.py"
SOURCE_VALUE = os.environ.get("OFFLINE_DAOC_QUEST_PILOT_SOURCE")
if not SOURCE_VALUE:
    raise RuntimeError(
        "Set OFFLINE_DAOC_QUEST_PILOT_SOURCE to the extracted clean v0.35b "
        "runtime/data/opendaoc.sqlite3.db fixture."
    )
SOURCE = Path(SOURCE_VALUE).expanduser().resolve()
EXPECTED_SOURCE_SHA256 = "0fff1a26913430a9c8f258484a1e2bae69102354c5e287d7d505d200dd3fdfe6"
SPEC = importlib.util.spec_from_file_location("classic_quest_pilot_migration", SCRIPT)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError("Unable to load the migration utility for focused checks.")
MIGRATION = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = MIGRATION
SPEC.loader.exec_module(MIGRATION)


def source_hash() -> str:
    digest = hashlib.sha256()
    with SOURCE.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def connect(path: Path) -> sqlite3.Connection:
    connection = sqlite3.connect(path, timeout=3)
    connection.row_factory = sqlite3.Row
    return connection


class DisposableMigrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        if not SOURCE.is_file():
            raise RuntimeError(f"Pinned clean fixture is unavailable: {SOURCE}")
        cls.source_sha256 = source_hash()
        if cls.source_sha256 != EXPECTED_SOURCE_SHA256:
            raise RuntimeError("Pinned clean fixture hash does not match the reviewed v0.35b release.")
        MIGRATION.require_clean_source(SOURCE, MIGRATION.load_resource())

    @classmethod
    def tearDownClass(cls) -> None:
        if source_hash() != cls.source_sha256:
            raise AssertionError("The pinned clean release fixture changed during disposable-copy checks.")

    def setUp(self) -> None:
        self.temp_context = tempfile.TemporaryDirectory(prefix="offlinedaoc-quest-pilot-test-")
        self.addCleanup(self.temp_context.cleanup)
        self.directory = Path(self.temp_context.name)
        (self.directory / MIGRATION.DISPOSABLE_MARKER).write_text(
            MIGRATION.DISPOSABLE_MARKER_TEXT + "\n", encoding="utf-8"
        )
        self.target = self.directory / "pilot.sqlite3.db"
        self.copy_clean_source(self.target)
        self.spec = MIGRATION.load_resource()

    @staticmethod
    def copy_clean_source(destination: Path) -> None:
        source = MIGRATION.open_readonly(SOURCE)
        target = sqlite3.connect(destination)
        try:
            source.backup(target)
        finally:
            target.close()
            source.close()

    def seed_missing_pilot_rows(self, target: Path | None = None) -> None:
        target = target or self.target
        connection = connect(target)
        try:
            quest = connection.execute("DELETE FROM DataQuest WHERE ID = 20054")
            item = connection.execute("DELETE FROM ItemTemplate WHERE Id_nb = ?", ("cq_pouch_of_seeds",))
            self.assertEqual(quest.rowcount, 1)
            self.assertEqual(item.rowcount, 1)
            connection.commit()
        finally:
            connection.close()

    def add_unrelated_sentinels(self, target: Path | None = None) -> tuple[int, str]:
        target = target or self.target
        connection = connect(target)
        try:
            unrelated_quest = connection.execute(
                "SELECT ID FROM DataQuest WHERE ID <> 20054 ORDER BY ID LIMIT 1"
            ).fetchone()[0]
            unrelated_item = connection.execute(
                "SELECT Id_nb FROM ItemTemplate WHERE Id_nb <> ? ORDER BY Id_nb LIMIT 1",
                ("cq_pouch_of_seeds",),
            ).fetchone()[0]
            connection.execute(
                "INSERT INTO CharacterXDataQuest (ID, Character_ID, DataQuestID, Step, Count) "
                "VALUES (?, ?, ?, 2, 3)",
                (910001, "synthetic-quest-pilot-owner", unrelated_quest),
            )
            connection.execute(
                "INSERT INTO Inventory (OwnerID, OwnerLot, ITemplate_Id, Inventory_ID) "
                "VALUES (?, 0, ?, ?)",
                ("synthetic-quest-pilot-owner", unrelated_item, "synthetic-unrelated-inventory-row"),
            )
            connection.execute(
                "CREATE TABLE PilotSyntheticSentinel "
                "(ID INTEGER PRIMARY KEY, Progress TEXT NOT NULL, Currency INTEGER NOT NULL)"
            )
            connection.execute(
                "INSERT INTO PilotSyntheticSentinel (ID, Progress, Currency) VALUES (1, ?, ?)",
                ("unrelated-progress", 987654321),
            )
            connection.commit()
            return int(unrelated_quest), str(unrelated_item)
        finally:
            connection.close()

    def invoke(self, *arguments: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [sys.executable, str(SCRIPT), "--source", str(SOURCE), "--target", str(self.target), *arguments],
            text=True,
            capture_output=True,
            timeout=180,
            check=False,
        )

    def test_dry_run_apply_rerun_and_owned_row_rollback(self) -> None:
        self.seed_missing_pilot_rows()
        unrelated_quest, unrelated_item = self.add_unrelated_sentinels()
        target_before = MIGRATION.sha256_file(self.target)

        dry_run = self.invoke()
        self.assertEqual(dry_run.returncode, 0, dry_run.stdout + dry_run.stderr)
        self.assertIn("Dry run only", dry_run.stdout)
        self.assertEqual(MIGRATION.sha256_file(self.target), target_before)
        self.assertFalse(MIGRATION.manifest_path(self.target).exists())

        before = connect(self.target)
        try:
            before_counts = {
                table: before.execute(f'SELECT COUNT(*) FROM "{table}"').fetchone()[0]
                for table in ("DataQuest", "ItemTemplate")
            }
            sequence_before = before.execute(
                "SELECT seq FROM sqlite_sequence WHERE name = 'DataQuest'"
            ).fetchone()
            sequence_before = 0 if sequence_before is None else int(sequence_before[0])
        finally:
            before.close()

        applied = self.invoke("--apply")
        self.assertEqual(applied.returncode, 0, applied.stdout + applied.stderr)
        self.assertIn("Pilot rows inserted", applied.stdout)
        manifest_path = MIGRATION.manifest_path(self.target)
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        self.assertEqual(manifest["status"], "applied")
        self.assertTrue(manifest["rollbackAllowed"])
        self.assertEqual(len(manifest["inserted"]), 2)
        backups = list(self.directory.glob("*.pre-classic-quest-pilot.*.sqlite3"))
        self.assertEqual(len(backups), 1)
        backup_name = backups[0].name

        applied_connection = connect(self.target)
        try:
            self.assertEqual(
                applied_connection.execute("SELECT COUNT(*) FROM DataQuest").fetchone()[0],
                before_counts["DataQuest"] + 1,
            )
            self.assertEqual(
                applied_connection.execute("SELECT COUNT(*) FROM ItemTemplate").fetchone()[0],
                before_counts["ItemTemplate"] + 1,
            )
            sequence_applied = applied_connection.execute(
                "SELECT seq FROM sqlite_sequence WHERE name = 'DataQuest'"
            ).fetchone()
            sequence_applied = 0 if sequence_applied is None else int(sequence_applied[0])
            self.assertGreaterEqual(sequence_applied, sequence_before)
        finally:
            applied_connection.close()

        rerun = self.invoke("--apply")
        self.assertEqual(rerun.returncode, 0, rerun.stdout + rerun.stderr)
        self.assertIn("already applied", rerun.stdout)
        rerun_manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        self.assertEqual(rerun_manifest["inserted"], manifest["inserted"])
        self.assertEqual(rerun_manifest["backup"], backup_name)
        self.assertEqual(len(list(self.directory.glob("*.pre-classic-quest-pilot.*.sqlite3"))), 1)

        rollback = self.invoke("--rollback")
        self.assertEqual(rollback.returncode, 0, rollback.stdout + rollback.stderr)
        self.assertIn("Pilot rows removed", rollback.stdout)
        final = connect(self.target)
        try:
            self.assertIsNone(final.execute("SELECT 1 FROM DataQuest WHERE ID = 20054").fetchone())
            self.assertIsNone(
                final.execute("SELECT 1 FROM ItemTemplate WHERE Id_nb = ?", ("cq_pouch_of_seeds",)).fetchone()
            )
            self.assertEqual(
                tuple(final.execute(
                    "SELECT Progress, Currency FROM PilotSyntheticSentinel WHERE ID = 1"
                ).fetchone()),
                ("unrelated-progress", 987654321),
            )
            self.assertEqual(
                tuple(final.execute(
                    "SELECT DataQuestID, Step, Count FROM CharacterXDataQuest WHERE ID = 910001"
                ).fetchone()),
                (unrelated_quest, 2, 3),
            )
            self.assertEqual(
                final.execute(
                    "SELECT ITemplate_Id FROM Inventory WHERE Inventory_ID = ?",
                    ("synthetic-unrelated-inventory-row",),
                ).fetchone()[0],
                unrelated_item,
            )
            sequence_after = final.execute(
                "SELECT seq FROM sqlite_sequence WHERE name = 'DataQuest'"
            ).fetchone()
            sequence_after = 0 if sequence_after is None else int(sequence_after[0])
            max_id = int(final.execute("SELECT COALESCE(MAX(ID), 0) FROM DataQuest").fetchone()[0])
            self.assertGreaterEqual(sequence_after, sequence_applied)
            self.assertGreaterEqual(sequence_after, max_id)
        finally:
            final.close()
        final_manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        self.assertEqual(final_manifest["status"], "rolled_back")
        self.assertTrue(backups[0].exists())

    def test_matching_preexisting_quest_is_not_owned_or_removed(self) -> None:
        connection = connect(self.target)
        try:
            connection.execute("UPDATE DataQuest SET ClassType = '' WHERE ID = 20054")
            item = connection.execute(
                "DELETE FROM ItemTemplate WHERE Id_nb = ?", ("cq_pouch_of_seeds",)
            )
            self.assertEqual(item.rowcount, 1)
            connection.commit()
            preexisting_quest = MIGRATION.get_row(connection, "DataQuest", {"ID": 20054})
        finally:
            connection.close()
        self.assertEqual(preexisting_quest["ClassType"], "")

        applied = self.invoke("--apply")
        self.assertEqual(applied.returncode, 0, applied.stdout + applied.stderr)
        manifest_path = MIGRATION.manifest_path(self.target)
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        self.assertEqual(manifest["status"], "applied")
        self.assertEqual(manifest["inserted"], [{"table": "ItemTemplate", "key": {"Id_nb": "cq_pouch_of_seeds"}}])

        rollback = self.invoke("--rollback")
        self.assertEqual(rollback.returncode, 0, rollback.stdout + rollback.stderr)
        connection = connect(self.target)
        try:
            self.assertEqual(
                MIGRATION.get_row(connection, "DataQuest", {"ID": 20054}),
                preexisting_quest,
            )
            self.assertEqual(
                connection.execute(
                    "SELECT COUNT(*) FROM ItemTemplate WHERE Id_nb = ?", ("cq_pouch_of_seeds",)
                ).fetchone()[0],
                0,
            )
        finally:
            connection.close()
        final_manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        self.assertEqual(final_manifest["status"], "rolled_back")
        self.assertEqual(final_manifest["inserted"], manifest["inserted"])

    def test_rollback_manifest_failures_leave_safe_recovery_state(self) -> None:
        self.seed_missing_pilot_rows()
        self.add_unrelated_sentinels()
        source_rows, plans, missing = MIGRATION.validate_inputs(SOURCE, self.target, self.spec)
        MIGRATION.apply_migration(self.target, self.spec, source_rows, plans, missing)
        applied_hash = MIGRATION.sha256_file(self.target)
        manifest_path = MIGRATION.manifest_path(self.target)
        applied_manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        backup_path = self.directory / applied_manifest["backup"]
        self.assertTrue(backup_path.is_file())

        original_write = MIGRATION.write_manifest

        def fail_before_rollback_preparation(path: Path, manifest: dict) -> None:
            if manifest.get("status") == "rollback_prepared":
                raise OSError("injected rollback-preparation manifest failure")
            original_write(path, manifest)

        with mock.patch.object(MIGRATION, "write_manifest", side_effect=fail_before_rollback_preparation):
            with self.assertRaisesRegex(OSError, "injected rollback-preparation manifest failure"):
                MIGRATION.rollback_migration(self.target, self.spec, source_rows)
        self.assertEqual(MIGRATION.sha256_file(self.target), applied_hash)
        still_applied = json.loads(manifest_path.read_text(encoding="utf-8"))
        self.assertEqual(still_applied["status"], "applied")
        self.assertEqual(still_applied["inserted"], applied_manifest["inserted"])
        self.assertTrue(backup_path.is_file())

        def fail_after_rollback_commit(path: Path, manifest: dict) -> None:
            if manifest.get("status") == "rolled_back":
                raise OSError("injected post-delete manifest failure")
            original_write(path, manifest)

        with mock.patch.object(MIGRATION, "write_manifest", side_effect=fail_after_rollback_commit):
            with self.assertRaisesRegex(OSError, "injected post-delete manifest failure"):
                MIGRATION.rollback_migration(self.target, self.spec, source_rows)
        interrupted = json.loads(manifest_path.read_text(encoding="utf-8"))
        self.assertEqual(interrupted["status"], "rollback_prepared")
        self.assertEqual(interrupted["inserted"], applied_manifest["inserted"])
        self.assertTrue(backup_path.is_file())

        recovered = self.invoke("--rollback")
        self.assertEqual(recovered.returncode, 0, recovered.stdout + recovered.stderr)
        self.assertIn("Previously committed rollback recovered", recovered.stdout)
        finalized = json.loads(manifest_path.read_text(encoding="utf-8"))
        self.assertEqual(finalized["status"], "rolled_back")
        self.assertEqual(finalized["inserted"], applied_manifest["inserted"])
        self.assertTrue(backup_path.is_file())
        self.assertEqual(MIGRATION.sha256_file(backup_path), applied_manifest["backupSha256"])

        connection = connect(self.target)
        try:
            self.assertIsNone(connection.execute("SELECT 1 FROM DataQuest WHERE ID = 20054").fetchone())
            self.assertIsNone(
                connection.execute(
                    "SELECT 1 FROM ItemTemplate WHERE Id_nb = ?", ("cq_pouch_of_seeds",)
                ).fetchone()
            )
            self.assertEqual(
                tuple(connection.execute(
                    "SELECT Progress, Currency FROM PilotSyntheticSentinel WHERE ID = 1"
                ).fetchone()),
                ("unrelated-progress", 987654321),
            )
            self.assertIsNotNone(
                connection.execute("SELECT 1 FROM CharacterXDataQuest WHERE ID = 910001").fetchone()
            )
            self.assertIsNotNone(
                connection.execute(
                    "SELECT 1 FROM Inventory WHERE Inventory_ID = ?",
                    ("synthetic-unrelated-inventory-row",),
                ).fetchone()
            )
        finally:
            connection.close()

    def test_collision_and_live_reference_refusals_are_non_destructive(self) -> None:
        self.seed_missing_pilot_rows()
        connection = connect(self.target)
        try:
            connection.execute(
                "INSERT INTO ItemTemplate (Id_nb, Name, ItemTemplate_ID) VALUES (?, ?, ?)",
                ("cq_pouch_of_seeds", "synthetic conflicting row", "synthetic-collision-template-id"),
            )
            connection.commit()
        finally:
            connection.close()
        collision_before = MIGRATION.sha256_file(self.target)
        collision = self.invoke("--apply")
        self.assertEqual(collision.returncode, 2, collision.stdout + collision.stderr)
        self.assertIn("different data", collision.stdout)
        self.assertEqual(MIGRATION.sha256_file(self.target), collision_before)
        self.assertFalse(MIGRATION.manifest_path(self.target).exists())
        self.assertEqual(list(self.directory.glob("*.pre-classic-quest-pilot.*.sqlite3")), [])

        connection = connect(self.target)
        try:
            connection.execute("DELETE FROM ItemTemplate WHERE Id_nb = ?", ("cq_pouch_of_seeds",))
            connection.execute(
                "INSERT INTO CharacterXDataQuest (ID, Character_ID, DataQuestID, Step, Count) "
                "VALUES (?, ?, ?, 1, 0)",
                (910002, "synthetic-pilot-progress-owner", 20054),
            )
            connection.execute(
                "INSERT INTO Inventory (OwnerID, OwnerLot, ITemplate_Id, Inventory_ID) "
                "VALUES (?, 0, ?, ?)",
                ("synthetic-pilot-item-owner", "cq_pouch_of_seeds", "synthetic-pilot-item-reference"),
            )
            connection.commit()
        finally:
            connection.close()

        applied = self.invoke("--apply")
        self.assertEqual(applied.returncode, 0, applied.stdout + applied.stderr)
        applied_hash = MIGRATION.sha256_file(self.target)
        rollback = self.invoke("--rollback")
        self.assertEqual(rollback.returncode, 2, rollback.stdout + rollback.stderr)
        self.assertIn("progress or item references exist", rollback.stdout)
        self.assertEqual(MIGRATION.sha256_file(self.target), applied_hash)
        connection = connect(self.target)
        try:
            self.assertIsNotNone(connection.execute("SELECT 1 FROM DataQuest WHERE ID = 20054").fetchone())
            self.assertIsNotNone(
                connection.execute(
                    "SELECT 1 FROM ItemTemplate WHERE Id_nb = ?", ("cq_pouch_of_seeds",)
                ).fetchone()
            )
            self.assertIsNotNone(connection.execute("SELECT 1 FROM CharacterXDataQuest WHERE ID = 910002").fetchone())
            self.assertIsNotNone(
                connection.execute(
                    "SELECT 1 FROM Inventory WHERE Inventory_ID = ?", ("synthetic-pilot-item-reference",)
                ).fetchone()
            )
        finally:
            connection.close()

    def test_manifest_interruptions_recover_without_unsafe_rollback(self) -> None:
        self.seed_missing_pilot_rows()
        source_rows, plans, missing = MIGRATION.validate_inputs(SOURCE, self.target, self.spec)
        original_hash = MIGRATION.sha256_file(self.target)
        original_write = MIGRATION.write_manifest

        def fail_before_preparation(path: Path, manifest: dict) -> None:
            if manifest.get("status") == "prepared":
                raise OSError("injected prepared-manifest failure")
            original_write(path, manifest)

        with mock.patch.object(MIGRATION, "write_manifest", side_effect=fail_before_preparation):
            with self.assertRaisesRegex(OSError, "injected prepared-manifest failure"):
                MIGRATION.apply_migration(self.target, self.spec, source_rows, plans, missing)
        self.assertEqual(MIGRATION.sha256_file(self.target), original_hash)
        self.assertFalse(MIGRATION.manifest_path(self.target).exists())
        self.assertEqual(list(self.directory.glob("*.pre-classic-quest-pilot.*.sqlite3")), [])

        with mock.patch.object(MIGRATION, "lock_target", side_effect=MIGRATION.PilotError("injected pre-commit interruption")):
            with self.assertRaisesRegex(MIGRATION.PilotError, "injected pre-commit interruption"):
                MIGRATION.apply_migration(self.target, self.spec, source_rows, plans, missing)
        prepared = json.loads(MIGRATION.manifest_path(self.target).read_text(encoding="utf-8"))
        self.assertEqual(prepared["status"], "prepared")
        self.assertEqual(MIGRATION.sha256_file(self.target), original_hash)
        self.assertTrue((self.directory / prepared["backup"]).is_file())

        def fail_after_commit(path: Path, manifest: dict) -> None:
            if manifest.get("status") == "applied":
                raise OSError("injected post-commit manifest failure")
            original_write(path, manifest)

        with mock.patch.object(MIGRATION, "write_manifest", side_effect=fail_after_commit):
            with self.assertRaisesRegex(OSError, "injected post-commit manifest failure"):
                MIGRATION.apply_migration(self.target, self.spec, source_rows, plans, missing)
        still_prepared = json.loads(MIGRATION.manifest_path(self.target).read_text(encoding="utf-8"))
        self.assertEqual(still_prepared["status"], "prepared")
        committed_hash = MIGRATION.sha256_file(self.target)
        connection = connect(self.target)
        try:
            self.assertIsNotNone(connection.execute("SELECT 1 FROM DataQuest WHERE ID = 20054").fetchone())
            self.assertIsNotNone(
                connection.execute(
                    "SELECT 1 FROM ItemTemplate WHERE Id_nb = ?", ("cq_pouch_of_seeds",)
                ).fetchone()
            )
        finally:
            connection.close()

        source_rows, plans, missing = MIGRATION.validate_inputs(SOURCE, self.target, self.spec)
        MIGRATION.apply_migration(self.target, self.spec, source_rows, plans, missing)
        recovered = json.loads(MIGRATION.manifest_path(self.target).read_text(encoding="utf-8"))
        self.assertEqual(recovered["status"], "applied")
        self.assertFalse(recovered["rollbackAllowed"])
        self.assertIn("unrelated post-commit edits cannot be ruled out", recovered["rollbackDisabledReason"])
        self.assertEqual(MIGRATION.sha256_file(self.target), committed_hash)
        with self.assertRaisesRegex(MIGRATION.PilotError, "Automatic rollback is disabled"):
            MIGRATION.rollback_migration(self.target, self.spec, source_rows)
        self.assertEqual(MIGRATION.sha256_file(self.target), committed_hash)
        self.assertTrue((self.directory / recovered["backup"]).is_file())


class TargetGuardTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_context = tempfile.TemporaryDirectory(prefix="offlinedaoc-quest-pilot-guards-")
        self.addCleanup(self.temp_context.cleanup)
        self.directory = Path(self.temp_context.name)
        (self.directory / MIGRATION.DISPOSABLE_MARKER).write_text(
            MIGRATION.DISPOSABLE_MARKER_TEXT + "\n", encoding="utf-8"
        )

    def make_small_database(self, path: Path) -> Path:
        path.parent.mkdir(parents=True, exist_ok=True)
        connection = sqlite3.connect(path)
        try:
            connection.execute("CREATE TABLE GuardSentinel (ID INTEGER PRIMARY KEY, Value TEXT)")
            connection.execute("INSERT INTO GuardSentinel (ID, Value) VALUES (1, 'synthetic')")
            connection.commit()
        finally:
            connection.close()
        return path

    def test_marker_install_symlink_hardlink_and_sidecar_guards(self) -> None:
        target = self.make_small_database(self.directory / "guard.sqlite3")
        MIGRATION.require_disposable_target(target)

        marker = self.directory / MIGRATION.DISPOSABLE_MARKER
        marker.unlink()
        with self.assertRaisesRegex(MIGRATION.PilotError, "needs a .offline-daoc-disposable"):
            MIGRATION.require_disposable_target(target)
        marker.write_text("not the exact marker\n", encoding="utf-8")
        with self.assertRaisesRegex(MIGRATION.PilotError, "does not contain the exact"):
            MIGRATION.require_disposable_target(target)
        marker.write_text(MIGRATION.DISPOSABLE_MARKER_TEXT + "\n", encoding="utf-8")

        symlink = self.directory / "symlink.sqlite3"
        symlink.symlink_to(target)
        with self.assertRaisesRegex(MIGRATION.PilotError, "not a symlink"):
            MIGRATION.require_disposable_target(symlink)

        hardlink = self.directory / "hardlink.sqlite3"
        os.link(target, hardlink)
        with self.assertRaisesRegex(MIGRATION.PilotError, "must not be a hard link"):
            MIGRATION.require_disposable_target(hardlink)
        hardlink.unlink()

        sidecar = Path(str(target) + "-wal")
        sidecar.write_bytes(b"synthetic sidecar guard fixture")
        with self.assertRaisesRegex(MIGRATION.PilotError, "active SQLite wal sidecar"):
            MIGRATION.require_disposable_target(target)
        sidecar.unlink()

        fake_install = self.directory / "D" / "Games" / "OfflineDAoC"
        fake_target = self.make_small_database(fake_install / "runtime" / "data" / "opendaoc.sqlite3.db")
        (fake_target.parent / MIGRATION.DISPOSABLE_MARKER).write_text(
            MIGRATION.DISPOSABLE_MARKER_TEXT + "\n", encoding="utf-8"
        )
        with mock.patch.object(MIGRATION, "KNOWN_INSTALL_PATHS", (fake_install,)):
            with self.assertRaisesRegex(MIGRATION.PilotError, "known D:"):
                MIGRATION.require_disposable_target(fake_target)

    def test_wal_journal_and_running_exclusive_handle_are_refused(self) -> None:
        wal_target = self.make_small_database(self.directory / "wal.sqlite3")
        connection = sqlite3.connect(wal_target)
        try:
            mode = connection.execute("PRAGMA journal_mode = WAL").fetchone()[0]
            self.assertEqual(str(mode).lower(), "wal")
        finally:
            connection.close()
        with self.assertRaises(MIGRATION.PilotError):
            MIGRATION.lock_target(wal_target)

        locked_target = self.make_small_database(self.directory / "locked.sqlite3")
        holder = sqlite3.connect(locked_target, timeout=1, isolation_level=None)
        try:
            holder.execute("BEGIN EXCLUSIVE")
            with self.assertRaises(sqlite3.OperationalError):
                MIGRATION.lock_target(locked_target)
        finally:
            holder.rollback()
            holder.close()


if __name__ == "__main__":
    unittest.main(verbosity=2)
