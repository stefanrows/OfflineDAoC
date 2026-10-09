#!/usr/bin/env python3
"""Import one reviewed classic quest into a marked disposable SQLite copy."""

from __future__ import annotations

import argparse
import json
import os
import sqlite3
import stat
import tempfile
import uuid
from datetime import datetime, timezone
from pathlib import Path


RESOURCE = Path(__file__).with_name("classic_quest_pilot.json")
DISPOSABLE_MARKER = ".offline-daoc-disposable"
DISPOSABLE_MARKER_TEXT = "OfflineDAoC disposable quest-pilot database copy."
MANIFEST_SUFFIX = ".classic-quest-pilot-manifest.json"
EXPECTED_DEFINITIONS = {
    ("DataQuest", "ID"): 20054,
    ("ItemTemplate", "Id_nb"): "cq_pouch_of_seeds",
}
EXPECTED_RESOURCE_SHA256 = "012c5e1a05388f6c7cad8aee72868bfb3620cefdb8497d24fdd89d2de75e04ed"
KNOWN_INSTALL_PATHS = (
    Path("/mnt/d/Games/OfflineDAoC"),
    Path("D:/Games/OfflineDAoC"),
)
EXPECTED_SOURCE = {
    "tagCommit": "d337d184034f4c84e1e1748fda7f5b2deef135b8",
    "reviewedCommit": "c8b0dca0428666fbe2c6f68d0e4d0363b0d03caa",
    "databaseSha256": "0fff1a26913430a9c8f258484a1e2bae69102354c5e287d7d505d200dd3fdfe6",
}
ITEM_REFERENCE_COLUMNS = {
    "itemtemplateid",
    "itemtemplate_id",
    "itemtemplateid_nb",
    "itemtemplate_id_nb",
    "itemplate_id",
}


class PilotError(RuntimeError):
    pass


def quote_identifier(value: str) -> str:
    return '"' + value.replace('"', '""') + '"'


def open_readonly(path: Path) -> sqlite3.Connection:
    uri = path.resolve().as_uri() + "?mode=ro&immutable=1"
    connection = sqlite3.connect(uri, uri=True, timeout=2)
    connection.row_factory = sqlite3.Row
    connection.execute("PRAGMA query_only = ON")
    return connection


def load_resource() -> dict:
    if sha256_file(RESOURCE) != EXPECTED_RESOURCE_SHA256:
        raise PilotError("Pilot resource differs from the reviewed allowlist.")
    with RESOURCE.open("r", encoding="utf-8") as stream:
        spec = json.load(stream)

    definitions = spec.get("definitions")
    if spec.get("schemaVersion") != 1 or not isinstance(definitions, list) or len(definitions) != 2:
        raise PilotError("Pilot resource must contain exactly two definition rows.")

    seen = set()
    ordered = []
    for definition in definitions:
        table = definition.get("table")
        key = definition.get("key")
        if not isinstance(key, dict) or len(key) != 1:
            raise PilotError("Each definition must have one primary-key selector.")
        key_name, key_value = next(iter(key.items()))
        identity = (table, key_name)
        if identity in seen or EXPECTED_DEFINITIONS.get(identity) != key_value:
            raise PilotError("Pilot resource contains an unexpected or duplicate definition.")
        seen.add(identity)
        ordered.append(identity)

    if seen != set(EXPECTED_DEFINITIONS) or ordered != list(EXPECTED_DEFINITIONS):
        raise PilotError("Pilot resource does not match the two-row allowlist.")
    if any(spec["source"].get(key) != value for key, value in EXPECTED_SOURCE.items()):
        raise PilotError("Pilot resource source revision or hash differs from the reviewed release.")
    return spec


def sha256_file(path: Path) -> str:
    import hashlib

    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def require_clean_source(source: Path, spec: dict) -> None:
    if not source.is_file() or source.is_symlink():
        raise PilotError("Source must be the extracted clean v0.35b SQLite database file.")
    expected = spec["source"]["databaseSha256"]
    actual = sha256_file(source)
    if actual != expected:
        raise PilotError("Source database does not match the reviewed clean v0.35b artifact.")


def require_disposable_target(target: Path) -> Path:
    if target.is_symlink() or not target.is_file():
        raise PilotError("Target must be an existing regular SQLite file, not a symlink.")

    resolved = target.resolve(strict=True)
    for install_root in KNOWN_INSTALL_PATHS:
        if os.name == "nt" and install_root.drive == "":
            continue
        if os.name != "nt" and install_root.is_absolute() is False:
            continue
        try:
            resolved.relative_to(install_root.resolve(strict=False))
        except ValueError:
            continue
        raise PilotError("The known D:\\Games\\OfflineDAoC installation is never a valid migration target.")

    temp_root = Path(tempfile.gettempdir()).resolve(strict=True)
    try:
        resolved.relative_to(temp_root)
    except ValueError as exc:
        raise PilotError("Target must be inside the operating system temporary directory.") from exc

    marker = resolved.parent / DISPOSABLE_MARKER
    if marker.is_symlink():
        raise PilotError(f"{DISPOSABLE_MARKER} must be a regular confirmation file.")
    try:
        marker_text = marker.read_text(encoding="utf-8").strip()
    except OSError as exc:
        raise PilotError(f"Target directory needs a {DISPOSABLE_MARKER} confirmation file.") from exc
    if marker_text != DISPOSABLE_MARKER_TEXT:
        raise PilotError(f"{DISPOSABLE_MARKER} does not contain the exact disposable-copy confirmation.")

    if resolved.stat().st_nlink != 1:
        raise PilotError("Target must not be a hard link to another database path.")

    if resolved.name.endswith(("-wal", "-shm")):
        raise PilotError("Target must be the main database file.")
    for suffix in ("-wal", "-shm"):
        if Path(str(resolved) + suffix).exists():
            raise PilotError(f"Target has an active SQLite {suffix[1:]} sidecar; stop and close it first.")
    return resolved


def schema_columns(connection: sqlite3.Connection, table: str) -> list[sqlite3.Row]:
    return connection.execute(f"PRAGMA table_info({quote_identifier(table)})").fetchall()


def validate_schema(source: sqlite3.Connection, target: sqlite3.Connection, table: str) -> None:
    source_info = schema_columns(source, table)
    target_info = schema_columns(target, table)
    source_columns = [(row[1], row[2], row[5]) for row in source_info]
    target_columns = [(row[1], row[2], row[5]) for row in target_info]
    if not source_columns or source_columns != target_columns:
        raise PilotError(f"Target {table} schema differs from the verified source.")


def get_row(connection: sqlite3.Connection, table: str, key: dict) -> dict | None:
    key_name, key_value = next(iter(key.items()))
    row = connection.execute(
        f"SELECT * FROM {quote_identifier(table)} WHERE {quote_identifier(key_name)} = ?",
        (key_value,),
    ).fetchone()
    return dict(row) if row is not None else None


def validate_source_definition(source: sqlite3.Connection, definition: dict) -> dict:
    row = get_row(source, definition["table"], definition["key"])
    if row is None:
        raise PilotError(f"Verified source is missing {definition['table']} {definition['key']}.")
    for column, expected in definition["expectedSource"].items():
        if row.get(column) != expected:
            raise PilotError(f"Verified source definition failed its {column} assertion.")
    return row


def target_row(source_row: dict, definition: dict) -> dict:
    row = dict(source_row)
    row.update(definition.get("targetOverrides", {}))
    return row


def required_actors(connection: sqlite3.Connection, spec: dict, label: str) -> None:
    regions = {int(actor["region"]) for actor in spec["requiredActors"]}
    for region in regions:
        if connection.execute("SELECT 1 FROM Regions WHERE RegionID = ?", (region,)).fetchone() is None:
            raise PilotError(f"{label} is missing required quest region {region}.")
    for actor in spec["requiredActors"]:
        spawn = actor["spawn"]
        row = connection.execute(
            """SELECT COUNT(*) FROM Mob AS m
               JOIN NpcTemplate AS n ON n.TemplateId = m.NPCTemplateID
               WHERE lower(m.Name) = lower(?) AND m.Region = ?
                 AND m.X = ? AND m.Y = ? AND m.Z = ? AND m.Level = ? AND m.Realm = ?
                 AND n.TemplateId = ? AND lower(n.Name) = lower(?) AND n.Model = ?
                 AND n.Race = ? AND n.ClassType = ?""",
            (
                actor["name"], actor["region"], spawn["x"], spawn["y"], spawn["z"],
                spawn["level"], spawn["realm"], actor["templateId"], actor["name"],
                actor["templateModel"], actor["templateRace"], actor["templateClassType"],
            ),
        ).fetchone()
        if row is None or row[0] < 1:
            raise PilotError(f"{label} is missing the reviewed spawn/template for {actor['name']} in region {actor['region']}.")


def unique_indexes(connection: sqlite3.Connection, table: str) -> list[list[str]]:
    indexes = connection.execute(f"PRAGMA index_list({quote_identifier(table)})").fetchall()
    result = []
    for index in indexes:
        if not index[2]:
            continue
        columns = connection.execute(
            f"PRAGMA index_info({quote_identifier(index[1])})"
        ).fetchall()
        names = [column[2] for column in columns]
        if names:
            result.append(names)
    return result


def check_unique_collisions(
    connection: sqlite3.Connection,
    table: str,
    row: dict,
    primary_key: dict,
) -> None:
    primary_column, primary_value = next(iter(primary_key.items()))
    for columns in unique_indexes(connection, table):
        values = [row.get(column) for column in columns]
        if any(value is None for value in values):
            continue
        clause = " AND ".join(f"{quote_identifier(column)} = ?" for column in columns)
        found = connection.execute(
            f"SELECT {quote_identifier(primary_column)} FROM {quote_identifier(table)} WHERE {clause}",
            values,
        ).fetchone()
        if found is not None and found[0] != primary_value:
            raise PilotError(f"A unique-key collision exists in {table}; no rows were changed.")


def row_matches(actual: dict, expected: dict, ignored: set[str] | None = None) -> bool:
    ignored = ignored or set()
    return all(actual.get(column) == value for column, value in expected.items() if column not in ignored)


def inspect_target(
    target: sqlite3.Connection,
    definitions: list[dict],
    source_rows: list[dict],
) -> tuple[list[tuple[dict, dict]], list[dict]]:
    plans = []
    missing = []
    for definition, source_row in zip(definitions, source_rows, strict=True):
        table = definition["table"]
        expected = target_row(source_row, definition)
        unique_column = definition.get("regenerateUniqueColumn")
        existing = get_row(target, table, definition["key"])
        ignored = {unique_column} if unique_column else set()
        if existing is not None:
            if not row_matches(existing, expected, ignored):
                raise PilotError(f"Target {table} key already exists with different data; no rows were changed.")
            plans.append((definition, expected))
            continue

        if unique_column:
            columns = {row[1] for row in schema_columns(target, table)}
            if unique_column not in columns:
                raise PilotError(f"Target {table} is missing unique column {unique_column}.")
            expected[unique_column] = str(uuid.uuid4())
        check_unique_collisions(target, table, expected, definition["key"])
        plans.append((definition, expected))
        missing.append(definition)
    return plans, missing


def validate_inputs(source_path: Path, target_path: Path, spec: dict) -> tuple[list[dict], list[tuple[dict, dict]], list[dict]]:
    require_clean_source(source_path, spec)
    if source_path.resolve() == target_path.resolve():
        raise PilotError("Source and target must be different files.")

    with open_readonly(source_path) as source, open_readonly(target_path) as target:
        for definition in spec["definitions"]:
            validate_schema(source, target, definition["table"])
        required_actors(source, spec, "Verified source")
        required_actors(target, spec, "Target")
        source_rows = [validate_source_definition(source, definition) for definition in spec["definitions"]]

        quest = source_rows[0]
        item_id = spec["definitions"][1]["key"]["Id_nb"]
        if item_id not in (quest["StepItemTemplates"] or "") or item_id not in (quest["CollectItemTemplate"] or ""):
            raise PilotError("Pilot quest does not reference its allowlisted item in both required steps.")

        plans, missing = inspect_target(target, spec["definitions"], source_rows)
    return source_rows, plans, missing


def backup_database(target_path: Path) -> Path:
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    backup_path = target_path.with_name(f"{target_path.name}.pre-classic-quest-pilot.{stamp}.sqlite3")
    if backup_path.exists():
        raise PilotError("Named backup already exists; choose a fresh disposable copy.")

    source = sqlite3.connect(target_path, timeout=2)
    destination = sqlite3.connect(backup_path)
    try:
        source.backup(destination)
        destination.commit()
    except Exception:
        destination.close()
        source.close()
        backup_path.unlink(missing_ok=True)
        raise
    else:
        destination.close()
        source.close()
    backup_path.chmod(stat.S_IRUSR | stat.S_IRGRP | stat.S_IROTH)
    return backup_path


def write_manifest(path: Path, manifest: dict) -> None:
    fd, temporary_name = tempfile.mkstemp(prefix=f".{path.name}.", suffix=".tmp", dir=path.parent)
    temporary = Path(temporary_name)
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as stream:
            json.dump(manifest, stream, indent=2, sort_keys=True)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        if hasattr(os, "O_DIRECTORY"):
            directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
            try:
                os.fsync(directory)
            finally:
                os.close(directory)
    except Exception:
        temporary.unlink(missing_ok=True)
        raise


def manifest_path(target_path: Path) -> Path:
    return target_path.with_name(target_path.name + MANIFEST_SUFFIX)


def definition_identity(definition: dict) -> tuple[str, str, object]:
    key_name, key_value = next(iter(definition["key"].items()))
    return definition["table"], key_name, key_value


def validate_planned_rows(spec: dict, source_rows: list[dict], entries: object) -> list[dict]:
    if not isinstance(entries, list) or not entries:
        raise PilotError("Prepared manifest has no planned insertion rows.")

    definitions = {
        definition_identity(definition): (definition, source_row)
        for definition, source_row in zip(spec["definitions"], source_rows, strict=True)
    }
    seen = set()
    planned = []
    for entry in entries:
        if not isinstance(entry, dict) or not isinstance(entry.get("row"), dict):
            raise PilotError("Prepared manifest contains an invalid planned row.")
        key = entry.get("key")
        if not isinstance(entry.get("table"), str) or not isinstance(key, dict) or len(key) != 1:
            raise PilotError("Prepared manifest row has an invalid primary-key selector.")
        key_name, key_value = next(iter(key.items()))
        table = entry["table"]
        identity = (table, key_name, key_value)
        if identity not in definitions or identity in seen:
            raise PilotError("Prepared manifest contains an unexpected or duplicate row.")
        seen.add(identity)

        definition, source_row = definitions[identity]
        expected = target_row(source_row, definition)
        unique_column = definition.get("regenerateUniqueColumn")
        row = entry["row"]
        if unique_column:
            unique_value = row.get(unique_column)
            if not isinstance(unique_value, str):
                raise PilotError("Prepared item row has no regenerated unique key.")
            try:
                if str(uuid.UUID(unique_value)) != unique_value:
                    raise ValueError
            except ValueError as exc:
                raise PilotError("Prepared item row has an invalid regenerated unique key.") from exc
            expected[unique_column] = unique_value
        if set(row) != set(expected) or not row_matches(row, expected):
            raise PilotError("Prepared row differs from the reviewed source definition.")
        if row.get(key_name) != key_value:
            raise PilotError("Prepared row primary key differs from its allowlist entry.")
        planned.append({"table": table, "key": definition["key"], "row": row})

    return planned


def verified_manifest_backup(target_path: Path, manifest: dict) -> Path:
    backup_name = manifest.get("backup")
    if not isinstance(backup_name, str):
        raise PilotError("Prepared manifest has no named backup.")
    expected_prefix = target_path.name + ".pre-classic-quest-pilot."
    backup = target_path.with_name(backup_name)
    if (backup.parent != target_path.parent or not backup.name.startswith(expected_prefix)
            or backup.is_symlink() or not backup.is_file()):
        raise PilotError("Manifest backup path is invalid or missing.")
    if sha256_file(backup) != manifest.get("backupSha256"):
        raise PilotError("Named disposable-copy backup no longer matches its recorded hash.")
    return backup


def file_fingerprint(path: Path) -> tuple[int, str]:
    before = path.stat()
    digest = sha256_file(path)
    after = path.stat()
    if (before.st_mtime_ns, before.st_size) != (after.st_mtime_ns, after.st_size):
        raise PilotError("Disposable database changed while its file fingerprint was being read.")
    return after.st_mtime_ns, digest


def verify_backup_has_no_planned_rows(backup_path: Path, planned_rows: list[dict]) -> None:
    with open_readonly(backup_path) as backup:
        for entry in planned_rows:
            if get_row(backup, entry["table"], entry["key"]) is not None:
                raise PilotError("Prepared backup already contains a planned row; ownership is ambiguous.")


def read_manifest(path: Path) -> dict:
    if path.is_symlink():
        raise PilotError("Pilot manifest must not be a symlink.")
    try:
        manifest = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise PilotError("Existing pilot manifest is unreadable; inspect the disposable copy first.") from exc
    if not isinstance(manifest, dict):
        raise PilotError("Pilot manifest must be a JSON object.")
    return manifest


def lock_target(target_path: Path) -> sqlite3.Connection:
    for suffix in ("-wal", "-shm"):
        if Path(str(target_path) + suffix).exists():
            raise PilotError(f"Target has an active SQLite {suffix[1:]} sidecar; stop and close it first.")
    connection = sqlite3.connect(target_path, timeout=1, isolation_level=None)
    connection.row_factory = sqlite3.Row
    try:
        mode = connection.execute("PRAGMA journal_mode").fetchone()[0]
        if str(mode).lower() != "delete":
            raise PilotError("Disposable target must use SQLite DELETE journaling; WAL mode is refused.")
        connection.execute("PRAGMA busy_timeout = 1000")
        connection.execute("PRAGMA foreign_keys = ON")
        connection.execute("BEGIN EXCLUSIVE")
    except Exception:
        connection.close()
        raise
    return connection


def apply_migration(
    target_path: Path,
    spec: dict,
    source_rows: list[dict],
    plans: list[tuple[dict, dict]],
    missing: list[dict],
) -> None:
    sidecar = manifest_path(target_path)
    current = None
    if sidecar.exists() or sidecar.is_symlink():
        current = read_manifest(sidecar)
        if current.get("migrationId") != spec["migrationId"]:
            raise PilotError("A different migration owns this disposable target's manifest.")
        if current.get("status") == "applied":
            verified_manifest_backup(target_path, current)
            print("Pilot migration already applied; target rows were preflighted and match.")
            return
        if current.get("status") == "rolled_back":
            raise PilotError("This disposable target already has a rolled-back pilot; use a fresh copy for another run.")
        if current.get("status") != "prepared":
            raise PilotError("Manifest status is not resumable; inspect the disposable copy first.")

    if current is None and not missing:
        print("Both pilot rows already match; no database definitions were inserted.")
        return

    if current is None:
        missing_identities = {definition_identity(item) for item in missing}
        planned_rows = [
            {"table": definition["table"], "key": definition["key"], "row": expected}
            for definition, expected in plans
            if definition_identity(definition) in missing_identities
        ]
        pre_apply_mtime = target_path.stat().st_mtime_ns
        backup_path = backup_database(target_path)
        try:
            if target_path.stat().st_mtime_ns != pre_apply_mtime:
                raise PilotError("Target changed while the named backup was being made.")
            verify_backup_has_no_planned_rows(backup_path, planned_rows)
            current = {
                "migrationId": spec["migrationId"],
                "status": "prepared",
                "sourceSha256": spec["source"]["databaseSha256"],
                "inserted": [{"table": item["table"], "key": item["key"]} for item in planned_rows],
                "plannedRows": planned_rows,
                "backup": backup_path.name,
                "backupSha256": sha256_file(backup_path),
                "targetMode": stat.S_IMODE(target_path.stat().st_mode),
                "preApplyTargetMtimeNs": pre_apply_mtime,
                "preparedAtUtc": datetime.now(timezone.utc).isoformat(),
            }
            write_manifest(sidecar, current)
        except Exception:
            if not sidecar.exists() and backup_path.exists():
                backup_path.chmod(stat.S_IRUSR | stat.S_IWUSR)
                backup_path.unlink(missing_ok=True)
            raise

    planned_rows = validate_planned_rows(spec, source_rows, current.get("plannedRows"))
    expected_inserted = [{"table": item["table"], "key": item["key"]} for item in planned_rows]
    if current.get("sourceSha256") != spec["source"]["databaseSha256"] or current.get("inserted") != expected_inserted:
        raise PilotError("Prepared ownership metadata differs from the reviewed source or planned rows.")
    backup_path = verified_manifest_backup(target_path, current)
    verify_backup_has_no_planned_rows(backup_path, planned_rows)
    planned_identities = {
        (entry["table"], next(iter(entry["key"].items()))[0], next(iter(entry["key"].items()))[1])
        for entry in planned_rows
    }
    if {definition_identity(item) for item in missing} not in (planned_identities, set()):
        raise PilotError("Current missing-row set differs from the prepared ownership manifest.")

    present = []
    with open_readonly(target_path) as target:
        for entry in planned_rows:
            actual = get_row(target, entry["table"], entry["key"])
            if actual is None:
                present.append(False)
            elif row_matches(actual, entry["row"]):
                present.append(True)
            else:
                raise PilotError("A prepared row now exists with different data; ownership is ambiguous.")
    if all(present):
        current["status"] = "applied"
        current["rollbackAllowed"] = False
        current["rollbackDisabledReason"] = "Recovered after commit; unrelated post-commit edits cannot be ruled out."
        current["targetMtimeNs"], current["targetSha256"] = file_fingerprint(target_path)
        current["appliedAtUtc"] = datetime.now(timezone.utc).isoformat()
        write_manifest(sidecar, current)
        print("Committed pilot rows recovered. Automatic rollback is disabled because later database edits cannot be ruled out; retain the named backup.")
        return
    if any(present):
        raise PilotError("Only part of the prepared transaction is present; inspect the disposable copy.")
    if target_path.stat().st_mtime_ns != current.get("preApplyTargetMtimeNs"):
        raise PilotError("Target changed after preparation; refusing to apply over later disposable-copy edits.")

    connection = lock_target(target_path)
    try:
        if target_path.stat().st_mtime_ns != current.get("preApplyTargetMtimeNs"):
            raise PilotError("Target changed after preparation; no rows were inserted.")
        required_actors(connection, spec, "Target")
        for entry in planned_rows:
            table = entry["table"]
            key = entry["key"]
            expected = entry["row"]
            if get_row(connection, table, key) is not None:
                raise PilotError(f"Target {table} changed after preparation; no rows were inserted.")
            check_unique_collisions(connection, table, expected, key)
            columns = list(expected)
            values = [expected[column] for column in columns]
            placeholders = ", ".join("?" for _ in columns)
            column_list = ", ".join(quote_identifier(column) for column in columns)
            connection.execute(
                f"INSERT INTO {quote_identifier(table)} ({column_list}) VALUES ({placeholders})",
                values,
            )

        for entry in planned_rows:
            actual = get_row(connection, entry["table"], entry["key"])
            if actual is None or not row_matches(actual, entry["row"]):
                raise PilotError(f"Post-insert verification failed for {entry['table']}.")
        connection.commit()
    except Exception:
        if connection.in_transaction:
            connection.rollback()
        raise
    finally:
        connection.close()

    current["status"] = "applied"
    current["rollbackAllowed"] = True
    current.pop("rollbackDisabledReason", None)
    current["targetMtimeNs"], current["targetSha256"] = file_fingerprint(target_path)
    current["targetSha256"] = sha256_file(target_path)
    current["appliedAtUtc"] = datetime.now(timezone.utc).isoformat()
    write_manifest(sidecar, current)
    print(f"Pilot rows inserted into disposable copy. Backup: {backup_path.name}")


def referenced_rows(target: sqlite3.Connection, table: str, column: str, value: object) -> int:
    columns = {row[1] for row in schema_columns(target, table)}
    if column not in columns:
        return 0
    result = target.execute(
        f"SELECT COUNT(*) FROM {quote_identifier(table)} WHERE {quote_identifier(column)} = ?",
        (value,),
    ).fetchone()
    return int(result[0])


def ensure_no_live_references(target: sqlite3.Connection, spec: dict) -> None:
    quest_id = spec["definitions"][0]["key"]["ID"]
    item_id = spec["definitions"][1]["key"]["Id_nb"]
    for table, column in (("CharacterXDataQuest", "DataQuestID"), ("Inventory", "ITemplate_Id")):
        if column not in {row[1] for row in schema_columns(target, table)}:
            raise PilotError(f"Rollback cannot verify references because {table}.{column} is unavailable.")
    progress = referenced_rows(target, "CharacterXDataQuest", "DataQuestID", quest_id)
    inventory = referenced_rows(target, "Inventory", "ITemplate_Id", item_id)
    if progress or inventory:
        raise PilotError("Rollback refused: pilot progress or item references exist; nothing was removed.")

    for table_row in target.execute("SELECT name FROM sqlite_master WHERE type = 'table'"):
        table = str(table_row[0])
        if table in {"Inventory", "ItemTemplate"} or table.startswith("sqlite_"):
            continue
        columns = {row[1] for row in schema_columns(target, table)}
        for column in columns:
            if column.lower() not in ITEM_REFERENCE_COLUMNS:
                continue
            count = referenced_rows(target, table, column, item_id)
            if count:
                raise PilotError(f"Rollback refused: {table} references the pilot item; nothing was removed.")


def present_planned_rows(connection: sqlite3.Connection, planned_rows: list[dict]) -> list[bool]:
    present = []
    for entry in planned_rows:
        actual = get_row(connection, entry["table"], entry["key"])
        if actual is None:
            present.append(False)
        elif row_matches(actual, entry["row"]):
            present.append(True)
        else:
            raise PilotError("An owned pilot row differs from its manifest; nothing was changed.")
    return present


def rollback_migration(
    target_path: Path,
    spec: dict,
    source_rows: list[dict],
) -> None:
    sidecar = manifest_path(target_path)
    manifest = read_manifest(sidecar)
    if manifest.get("migrationId") != spec["migrationId"]:
        raise PilotError("Manifest does not belong to this pilot migration.")
    if manifest.get("status") == "rolled_back":
        print("Pilot migration is already rolled back; no database rows were changed.")
        return
    if manifest.get("status") not in {"applied", "rollback_prepared"}:
        raise PilotError("Manifest does not describe an applied or resumable rollback.")
    if manifest.get("status") == "applied" and manifest.get("rollbackAllowed") is not True:
        reason = manifest.get("rollbackDisabledReason", "post-commit database changes cannot be ruled out")
        raise PilotError(f"Automatic rollback is disabled: {reason} Retain the named backup and use a fresh disposable copy.")
    if not manifest.get("inserted"):
        raise PilotError("This migration inserted no definitions; rollback is not available.")
    if manifest.get("sourceSha256") != spec["source"]["databaseSha256"]:
        raise PilotError("Manifest source hash differs from the reviewed release.")

    backup = verified_manifest_backup(target_path, manifest)
    planned_rows = validate_planned_rows(spec, source_rows, manifest.get("plannedRows"))
    expected_inserted = [{"table": item["table"], "key": item["key"]} for item in planned_rows]
    if manifest.get("inserted") != expected_inserted:
        raise PilotError("Manifest ownership list differs from its planned rows.")
    verify_backup_has_no_planned_rows(backup, planned_rows)

    with open_readonly(target_path) as target:
        present = present_planned_rows(target, planned_rows)
    if manifest.get("status") == "rollback_prepared":
        if not any(present):
            lock = lock_target(target_path)
            try:
                if any(present_planned_rows(lock, planned_rows)):
                    raise PilotError("Pilot rows reappeared during rollback recovery; no changes were made.")
                manifest["status"] = "rolled_back"
                manifest["targetMtimeNs"], manifest["targetSha256"] = file_fingerprint(target_path)
                manifest["rolledBackAtUtc"] = datetime.now(timezone.utc).isoformat()
                write_manifest(sidecar, manifest)
                lock.commit()
            finally:
                lock.close()
            print("Previously committed rollback recovered from its manifest.")
            return
        if not all(present):
            raise PilotError("Only part of the rollback transaction is present; inspect the disposable copy.")
        if target_path.stat().st_mtime_ns != manifest.get("rollbackTargetMtimeNs"):
            raise PilotError("Target changed during the interrupted rollback; no rows were removed.")
    elif not all(present):
        raise PilotError("Owned pilot rows are missing or changed; refusing rollback.")
    else:
        mtime, digest = file_fingerprint(target_path)
        if mtime != manifest.get("targetMtimeNs") or digest != manifest.get("targetSha256"):
            raise PilotError("Target changed after migration; no rows were removed.")

    lock = lock_target(target_path)
    try:
        expected_mtime = manifest.get("rollbackTargetMtimeNs") if manifest.get("status") == "rollback_prepared" else manifest.get("targetMtimeNs")
        expected_digest = manifest.get("rollbackTargetSha256") if manifest.get("status") == "rollback_prepared" else manifest.get("targetSha256")
        current_mtime, current_digest = file_fingerprint(target_path)
        if current_mtime != expected_mtime or current_digest != expected_digest:
            raise PilotError("Target changed before the exclusive rollback lock; no rows were removed.")
        current_rows = present_planned_rows(lock, planned_rows)
        if not all(current_rows):
            raise PilotError("Owned pilot rows changed before rollback; no rows were removed.")
        ensure_no_live_references(lock, spec)

        if manifest.get("status") == "applied":
            manifest["status"] = "rollback_prepared"
            manifest["rollbackTargetMtimeNs"] = expected_mtime
            manifest["rollbackTargetSha256"] = expected_digest
            manifest["rollbackPreparedAtUtc"] = datetime.now(timezone.utc).isoformat()
            write_manifest(sidecar, manifest)

        for entry in planned_rows:
            key_name, key_value = next(iter(entry["key"].items()))
            cursor = lock.execute(
                f"DELETE FROM {quote_identifier(entry['table'])} WHERE {quote_identifier(key_name)} = ?",
                (key_value,),
            )
            if cursor.rowcount != 1:
                raise PilotError(f"Rollback could not remove exactly one owned {entry['table']} row.")
        if any(present_planned_rows(lock, planned_rows)):
            raise PilotError("Rollback verification found a pilot row still present.")
        lock.commit()
    except Exception:
        if lock.in_transaction:
            lock.rollback()
        raise
    finally:
        lock.close()

    manifest["status"] = "rolled_back"
    manifest["targetMtimeNs"], manifest["targetSha256"] = file_fingerprint(target_path)
    manifest["rolledBackAtUtc"] = datetime.now(timezone.utc).isoformat()
    write_manifest(sidecar, manifest)
    print(f"Pilot rows removed from the disposable copy; immutable backup retained: {backup.name}")


def main() -> int:
    parser = argparse.ArgumentParser(
        description=__doc__,
        epilog=(
            "Disposable-only: source must be the clean, hash-verified v0.35b release DB and is opened read-only. "
            "Target must be a closed copy under the OS temp directory, with no WAL/SHM sidecars and a regular "
            f"{DISPOSABLE_MARKER} file containing exactly: {DISPOSABLE_MARKER_TEXT} "
            "The known D:\\Games\\OfflineDAoC installation is always refused. Default mode is dry-run; "
            "no database change occurs unless --apply or --rollback is explicit. Rollback removes only manifest-owned rows from "
            "the disposable copy under an exclusive transaction and retains the named backup."
        ),
    )
    parser.add_argument("--source", type=Path, required=True, help="Extracted clean v0.35b runtime/data/opendaoc.sqlite3.db")
    parser.add_argument("--target", type=Path, required=True, help="Existing disposable SQLite copy inside the system temp directory")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--apply", action="store_true", help="Insert missing allowlisted rows after backup and collision checks")
    mode.add_argument("--rollback", action="store_true", help="Remove owned pilot rows from this disposable copy")
    args = parser.parse_args()

    try:
        spec = load_resource()
        if args.source.is_symlink():
            raise PilotError("Source must not be a symlink to another database file.")
        source_path = args.source.resolve(strict=True)
        target_path = require_disposable_target(args.target)
        source_rows, plans, missing = validate_inputs(source_path, target_path, spec)
        if args.rollback:
            rollback_migration(target_path, spec, source_rows)
            return 0

        if args.apply:
            apply_migration(target_path, spec, source_rows, plans, missing)
        elif missing:
            names = ", ".join(f"{definition['table']} {definition['key']}" for definition in missing)
            print(f"Dry run only. Would add {names}; no files or database rows were changed.")
        else:
            print("Dry run only. Both allowlisted rows already match; no files or database rows were changed.")
        return 0
    except (PilotError, OSError, sqlite3.Error, KeyError, TypeError, ValueError) as exc:
        print(f"Pilot migration stopped: {exc}")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
