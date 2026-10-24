#!/usr/bin/env python3
"""Load the converted gaheris ToA SQL into a disposable SQLite copy and measure it.

For every converted file (one transaction per file, committed at its end so
later files see earlier ones) this records statements total / succeeded /
failed, the first five distinct SQLite errors, rows loaded and rejected, the
column-list diff against PRAGMA table_info for every INSERT target, and
before/after row counts of every touched table. It then runs referential
sanity checks on the result (NpcTemplate, NPCEquipment, ClassType/Brain
names against source/server, ML spells against spell lines and spell
handlers, merchant items against item templates).

A failed multi-row INSERT is retried row by row (``--no-row-fallback`` turns
that off) so the report shows how many rows SQLite would accept; the strict
statement count is reported as well.

The probe refuses to touch a database under a ``playable`` directory or the
dev snapshot itself. Typical run (from the repository root):

    python3 tools/toa/probe_import.py \
        --fresh-from .toa-probe/snapshot.base.db --db .toa-probe/probe.db \
        --sql-dir tools/toa/sql --json .toa-probe/report.json
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import sqlite3
import sys
from collections import Counter, OrderedDict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import convert_mysql_to_sqlite as conv  # noqa: E402

ATLANTIS_REGION_GROUPS = OrderedDict([
    ("Albion Atlantis 30", [30]),
    ("Midgard Atlantis 73", [73]),
    ("Hibernia Atlantis 130", [130]),
    ("Haven/cities 70-72", [70, 71, 72]),
    ("Celestius 91", [91]),
    ("ToA dungeons Alb 45-47", [45, 46, 47]),
    ("ToA dungeons Mid 88-90, 93", [88, 89, 90, 93]),
    ("ToA dungeons Hib 145-147", [145, 146, 147]),
    ("ids 31-44 (zone ids)", list(range(31, 45))),
    ("ids 74-87 (zone ids)", list(range(74, 88))),
    ("ids 131-144 (zone ids)", list(range(131, 145))),
])
ML_LINES = ("Banelord", "Battlemaster", "Convoker", "Perfecter", "Sojourner", "Spymaster", "Stormlord", "Warlord")
PACKAGE_ID = "gaheris-atlantis"


def refuse_unsafe(db: Path) -> None:
    resolved = db.resolve()
    parts = {p.lower() for p in resolved.parts}
    if "playable" in parts or "db-snapshot" in parts:
        raise SystemExit(f"refusing to write {resolved}: probe databases must be disposable copies")


def norm_error(msg: str) -> str:
    return re.sub(r"\d+", "N", msg)


def table_columns(con: sqlite3.Connection, table: str) -> list[tuple]:
    return con.execute(f"PRAGMA table_info({conv.sqlite_ident(table)})").fetchall()


def resolve_table(con: sqlite3.Connection, name: str) -> str | None:
    row = con.execute("SELECT name FROM sqlite_master WHERE type='table' AND lower(name)=lower(?)", (name,)).fetchone()
    return row[0] if row else None


def count_rows(con: sqlite3.Connection, table: str) -> int:
    return con.execute(f"SELECT COUNT(*) FROM {conv.sqlite_ident(table)}").fetchone()[0]


class InsertShape:
    """Table, column list and per-row SQL of a converted (SQLite dialect) INSERT."""

    def __init__(self, sql: str):
        toks = conv.tokenize(sql, "sqlite")
        sig = [t for t in toks if t.significant]
        self.ok = False
        if not sig or sig[0].upper() != "INSERT":
            return
        k = 1
        prefix = ["INSERT"]
        while sig[k].upper() in ("OR", "IGNORE", "REPLACE", "ABORT", "FAIL", "ROLLBACK"):
            prefix.append(sig[k].text)
            k += 1
        if sig[k].upper() != "INTO":
            return
        k += 1
        self.table = conv.ident_name(sig[k]) or ""
        k += 1
        self.columns: list[str] = []
        if sig[k].text == "(":
            depth, j = 0, k
            while True:
                if sig[j].text == "(":
                    depth += 1
                elif sig[j].text == ")":
                    depth -= 1
                    if depth == 0:
                        break
                elif depth == 1 and sig[j].text != ",":
                    self.columns.append(conv.ident_name(sig[j]) or sig[j].text)
                j += 1
            k = j + 1
        self.is_values = sig[k].upper() == "VALUES"
        self.ok = True
        if not self.is_values:
            self.rows = []
            return
        # Re-split rows on the original token stream so each row keeps its text.
        start = toks.index(sig[k]) + 1
        rows, depth, cur, tail_start = [], 0, [], None
        for idx in range(start, len(toks)):
            t = toks[idx]
            if depth == 0 and t.significant and t.text != "(" and t.text != ",":
                tail_start = idx
                break
            if t.text == "(" and t.kind == "punct":
                depth += 1
            if depth > 0:
                cur.append(t)
            if t.text == ")" and t.kind == "punct":
                depth -= 1
                if depth == 0:
                    rows.append("".join(conv.render_default(x) if x.kind in ("string", "ident") else x.text for x in cur))
                    cur = []
        tail = ""
        if tail_start is not None:
            tail = "".join(x.text for x in toks[tail_start:] if x.kind != "semi")
        head = " ".join(prefix) + " INTO " + conv.sqlite_ident(self.table)
        if self.columns:
            head += " (" + ", ".join(conv.sqlite_ident(c) for c in self.columns) + ")"
        self.rows = [f"{head} VALUES {r} {tail}".rstrip() for r in rows]


def statement_kind(sql: str) -> tuple[str, str | None]:
    toks = [t for t in conv.tokenize(sql, "sqlite") if t.significant]
    if not toks:
        return "empty", None
    first = toks[0].upper()
    table = None
    try:
        if first == "INSERT":
            i = next(k for k, t in enumerate(toks) if t.upper() == "INTO")
            table = conv.ident_name(toks[i + 1])
        elif first == "UPDATE":
            i = 1
            while toks[i].upper() in ("OR", "IGNORE", "REPLACE"):
                i += 1
            table = conv.ident_name(toks[i])
        elif first == "DELETE":
            i = next(k for k, t in enumerate(toks) if t.upper() == "FROM")
            table = conv.ident_name(toks[i + 1])
    except (StopIteration, IndexError):
        table = None
    return first.lower(), table


def split_sql_file(text: str) -> list[str]:
    out = []
    for stmt in conv.split_statements(conv.tokenize(text, "sqlite")):
        if stmt.has_body:
            lead = stmt.sig()[0]
            out.append("".join(t.text for t in stmt.tokens[lead:]))
    return out


def probe_file(con: sqlite3.Connection, path: Path, row_fallback: bool) -> dict:
    statements = split_sql_file(path.read_text(encoding="utf-8"))
    res = {
        "file": path.name, "statements": len(statements), "succeeded": 0, "failed": 0,
        "by_kind": Counter(), "failed_by_kind": Counter(), "rows_loaded": 0, "rows_rejected": 0,
        "rows_changed_update_delete": 0, "errors": [], "error_counts": Counter(), "selects": [],
        "insert_shapes": {}, "touched_tables": [],
    }
    touched: "OrderedDict[str, None]" = OrderedDict()
    for sql in statements:
        kind, table = statement_kind(sql)
        if table:
            real = resolve_table(con, table) or table
            touched[real] = None
    before = {t: (count_rows(con, t) if resolve_table(con, t) else None) for t in touched}
    con.execute("BEGIN")
    for n, sql in enumerate(statements, 1):
        kind, table = statement_kind(sql)
        res["by_kind"][kind] += 1
        shape = InsertShape(sql) if kind == "insert" else None
        if shape is not None and shape.ok:
            key = f"{shape.table}|{','.join(c.lower() for c in shape.columns)}"
            entry = res["insert_shapes"].setdefault(key, {"table": shape.table, "columns": shape.columns,
                                                          "statements": 0, "failed": 0})
            entry["statements"] += 1
        con.execute("SAVEPOINT s")
        changes0 = con.total_changes
        try:
            cur = con.execute(sql)
            if kind == "select":
                rows = cur.fetchmany(25)
                res["selects"].append({"statement": n, "columns": [d[0] for d in cur.description or []],
                                       "rows": [list(r) for r in rows]})
            con.execute("RELEASE s")
            res["succeeded"] += 1
            delta = con.total_changes - changes0
            if kind == "insert":
                res["rows_loaded"] += delta
            else:
                res["rows_changed_update_delete"] += delta
        except sqlite3.Error as exc:
            con.execute("ROLLBACK TO s")
            con.execute("RELEASE s")
            res["failed"] += 1
            res["failed_by_kind"][kind] += 1
            msg = f"{type(exc).__name__}: {exc}"
            key = norm_error(msg)
            if res["error_counts"][key] == 0 and len(res["errors"]) < 5:
                res["errors"].append({"statement": n, "error": msg, "sql_head": sql[:240]})
            res["error_counts"][key] += 1
            if shape is not None and shape.ok:
                res["insert_shapes"][f"{shape.table}|{','.join(c.lower() for c in shape.columns)}"]["failed"] += 1
                if row_fallback and shape.is_values and len(shape.rows) > 0:
                    for row_sql in shape.rows:
                        con.execute("SAVEPOINT r")
                        c0 = con.total_changes
                        try:
                            con.execute(row_sql)
                            con.execute("RELEASE r")
                            res["rows_loaded"] += con.total_changes - c0
                        except sqlite3.Error as row_exc:
                            con.execute("ROLLBACK TO r")
                            con.execute("RELEASE r")
                            res["rows_rejected"] += 1
                            rkey = norm_error(f"{type(row_exc).__name__}: {row_exc}")
                            res["error_counts"]["row: " + rkey] += 1
                elif shape.is_values:
                    res["rows_rejected"] += len(shape.rows)
    con.execute("COMMIT")
    after = {t: (count_rows(con, t) if resolve_table(con, t) else None) for t in touched}
    res["touched_tables"] = [{"table": t, "before": before[t], "after": after[t]} for t in touched]
    for entry in res["insert_shapes"].values():
        real = resolve_table(con, entry["table"])
        if real is None:
            entry["table_missing"] = True
            continue
        info = table_columns(con, real)
        ours = {r[1].lower(): r for r in info}
        theirs = {c.lower() for c in entry["columns"]}
        entry["missing_in_our_table"] = [c for c in entry["columns"] if c.lower() not in ours]
        entry["ours_not_in_statement"] = [r[1] for r in info if r[1].lower() not in theirs]
        entry["ours_not_null_without_default_not_given"] = [
            r[1] for r in info if r[1].lower() not in theirs and r[3] and r[4] is None and not r[5]]
    res["by_kind"] = dict(res["by_kind"])
    res["failed_by_kind"] = dict(res["failed_by_kind"])
    res["error_counts"] = dict(res["error_counts"].most_common())
    return res


def source_types(source_root: Path) -> tuple[set[str], set[str], set[str]]:
    """Return (qualified class names, eSpellType members, eSpellType values with a [SpellHandler]) from C# source."""
    classes, spell_types, handled = set(), set(), set()
    handler_re = re.compile(r"\[SpellHandler(?:Attribute)?\(\s*eSpellType\.(\w+)")
    ns_re = re.compile(r"^\s*namespace\s+([\w.]+)", re.M)
    cls_re = re.compile(r"\b(?:class|struct)\s+(\w+)")
    for cs in source_root.rglob("*.cs"):
        try:
            text = cs.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
        text = re.sub(r"//[^\n]*", "", text)
        handled.update(m.group(1).lower() for m in handler_re.finditer(text))
        text = re.sub(r'@?"(?:[^"\\\n]|\\.)*"', '""', text)
        namespaces = [(m.start(), m.group(1)) for m in ns_re.finditer(text)]
        for m in cls_re.finditer(text):
            ns = ""
            for pos, name in namespaces:
                if pos < m.start():
                    ns = name
            classes.add(f"{ns}.{m.group(1)}" if ns else m.group(1))
    enum_file = next((p for p in source_root.rglob("*.cs") if "enum eSpellType" in p.read_text(errors="replace")), None)
    if enum_file is not None:
        text = enum_file.read_text(errors="replace")
        body = text[text.index("enum eSpellType"):]
        body = body[body.index("{") + 1:body.index("}")]
        body = re.sub(r"//[^\n]*", "", body)
        for item in body.split(","):
            name = item.strip().split("=")[0].strip()
            if re.fullmatch(r"\w+", name):
                spell_types.add(name.lower())
    return classes, spell_types, handled


def sanity(con: sqlite3.Connection, source_root: Path) -> dict:
    out: dict = {}
    classes, spell_types, handled = source_types(source_root)
    classes_lower = {c.lower() for c in classes}
    mob = "Mob"
    q = lambda sql, *a: con.execute(sql, a).fetchall()  # noqa: E731
    out["mobs_loaded"] = q(f"SELECT COUNT(*) FROM {mob} WHERE PackageID=?", PACKAGE_ID)[0][0]
    out["npctemplate_refs"] = q(
        f"SELECT COUNT(*), COUNT(DISTINCT m.NPCTemplateID) FROM {mob} m WHERE m.PackageID=? AND m.NPCTemplateID NOT IN (0,-1) "
        "AND m.NPCTemplateID IS NOT NULL", PACKAGE_ID)[0]
    out["npctemplate_missing"] = q(
        f"SELECT COUNT(*), COUNT(DISTINCT m.NPCTemplateID) FROM {mob} m WHERE m.PackageID=? AND m.NPCTemplateID NOT IN (0,-1) "
        "AND m.NPCTemplateID IS NOT NULL AND NOT EXISTS (SELECT 1 FROM NpcTemplate t WHERE t.TemplateId=m.NPCTemplateID)",
        PACKAGE_ID)[0]
    out["npctemplate_missing_ids"] = [r[0] for r in q(
        f"SELECT DISTINCT m.NPCTemplateID FROM {mob} m WHERE m.PackageID=? AND m.NPCTemplateID NOT IN (0,-1) "
        "AND m.NPCTemplateID IS NOT NULL AND NOT EXISTS (SELECT 1 FROM NpcTemplate t WHERE t.TemplateId=m.NPCTemplateID) "
        "ORDER BY 1 LIMIT 20", PACKAGE_ID)]
    out["equipment_refs"] = q(
        f"SELECT COUNT(*), COUNT(DISTINCT EquipmentTemplateID) FROM {mob} WHERE PackageID=? "
        "AND EquipmentTemplateID IS NOT NULL AND EquipmentTemplateID<>''", PACKAGE_ID)[0]
    out["equipment_missing"] = q(
        f"SELECT COUNT(*), COUNT(DISTINCT m.EquipmentTemplateID) FROM {mob} m WHERE m.PackageID=? "
        "AND m.EquipmentTemplateID IS NOT NULL AND m.EquipmentTemplateID<>'' "
        "AND NOT EXISTS (SELECT 1 FROM NPCEquipment e WHERE e.TemplateID=m.EquipmentTemplateID)", PACKAGE_ID)[0]
    out["equipment_missing_ids"] = [r[0] for r in q(
        f"SELECT DISTINCT m.EquipmentTemplateID FROM {mob} m WHERE m.PackageID=? "
        "AND m.EquipmentTemplateID IS NOT NULL AND m.EquipmentTemplateID<>'' "
        "AND NOT EXISTS (SELECT 1 FROM NPCEquipment e WHERE e.TemplateID=m.EquipmentTemplateID) ORDER BY 1 LIMIT 20",
        PACKAGE_ID)]
    out["faction_missing"] = q(
        f"SELECT COUNT(*), COUNT(DISTINCT m.FactionID) FROM {mob} m WHERE m.PackageID=? AND m.FactionID<>0 "
        "AND NOT EXISTS (SELECT 1 FROM Faction f WHERE f.ID=m.FactionID)", PACKAGE_ID)[0] \
        if any(c[1].lower() == "id" for c in table_columns(con, "Faction")) else "Faction has no ID column"
    out["itemslist_missing"] = [list(r) for r in q(
        f"SELECT m.ItemsListTemplateID, COUNT(*) FROM {mob} m WHERE m.Region IN "
        "(30,45,46,47,70,71,72,73,88,89,90,91,93,130,145,146,147) AND m.ItemsListTemplateID IS NOT NULL "
        "AND m.ItemsListTemplateID<>'' AND NOT EXISTS (SELECT 1 FROM MerchantItem i WHERE i.ItemListID=m.ItemsListTemplateID) "
        "GROUP BY 1 ORDER BY 2 DESC")]
    out["path_missing"] = q(
        f"SELECT COUNT(*), COUNT(DISTINCT m.PathID) FROM {mob} m WHERE m.PackageID=? AND m.PathID IS NOT NULL AND m.PathID<>'' "
        "AND NOT EXISTS (SELECT 1 FROM Path p WHERE p.PathID=m.PathID)", PACKAGE_ID)[0]
    atl = "(30,45,46,47,70,71,72,73,88,89,90,91,93,130,145,146,147)"
    ctypes = q(f"SELECT COALESCE(ClassType,''), COUNT(*) FROM {mob} WHERE Region IN {atl} GROUP BY 1 ORDER BY 2 DESC")
    out["classtypes"] = [[c, n, (c == "" or c.lower() in classes_lower)] for c, n in ctypes]
    out["classtypes_missing"] = [[c, n] for c, n, ok in out["classtypes"] if not ok]
    brains = q(f"SELECT COALESCE(Brain,''), COUNT(*) FROM {mob} WHERE Region IN {atl} GROUP BY 1 ORDER BY 2 DESC")
    out["brains"] = [[b, n, (b == "" or b.lower() in classes_lower)] for b, n in brains]
    # Master Levels
    lines = ",".join("'" + ln + "'" for ln in ML_LINES)
    out["ml_linexspell"] = q(f"SELECT COUNT(*) FROM LineXSpell WHERE LineName IN ({lines})")[0][0]
    out["ml_join_spell"] = q(
        f"SELECT COUNT(*) FROM LineXSpell l JOIN Spell s ON s.SpellID=l.SpellID WHERE l.LineName IN ({lines})")[0][0]
    out["ml_join_spell_and_line"] = q(
        f"SELECT COUNT(*) FROM LineXSpell l JOIN Spell s ON s.SpellID=l.SpellID JOIN SpellLine sl ON sl.KeyName=l.LineName "
        f"WHERE l.LineName IN ({lines})")[0][0]
    out["ml_per_line"] = [list(r) for r in q(
        f"SELECT l.LineName, COUNT(*), GROUP_CONCAT(l.Level) FROM (SELECT * FROM LineXSpell WHERE LineName IN ({lines}) "
        "ORDER BY LineName, Level) l GROUP BY l.LineName ORDER BY 1")]
    ml_types = q(
        f"SELECT s.Type, COUNT(*) FROM LineXSpell l JOIN Spell s ON s.SpellID=l.SpellID WHERE l.LineName IN ({lines}) "
        "GROUP BY 1 ORDER BY 1")
    out["ml_spell_types_unknown"] = [[t, n] for t, n in ml_types if (t or "").lower() not in spell_types]
    out["ml_spell_types_known"] = sum(n for t, n in ml_types if (t or "").lower() in spell_types)
    out["ml_spell_types_without_handler"] = [[t, n] for t, n in ml_types if (t or "").lower() not in handled]
    out["ml_distinct_spell_types"] = len(ml_types)
    out["ml_orphan_spells"] = q(
        "SELECT COUNT(*) FROM Spell s WHERE s.SpellID BETWEEN 7201 AND 7328 "
        "AND NOT EXISTS (SELECT 1 FROM LineXSpell l WHERE l.SpellID=s.SpellID)")[0][0]
    out["ml_classxspec"] = q(
        f"SELECT COUNT(*), COUNT(DISTINCT ClassID) FROM ClassXSpecialization WHERE SpecKeyName IN ({lines})")[0]
    out["ml_specialization_rows"] = q(f"SELECT COUNT(*) FROM Specialization WHERE KeyName IN ({lines})")[0][0]
    # Merchants and travel
    out["merchant_items_missing_template"] = [list(r) for r in q(
        "SELECT i.ItemListID, COUNT(*) FROM MerchantItem i WHERE i.ItemListID LIKE 'artifact_scrolls%' "
        "AND NOT EXISTS (SELECT 1 FROM ItemTemplate t WHERE t.Id_nb=i.ItemTemplateID) GROUP BY 1")]
    out["merchant_items_scrolls"] = [list(r) for r in q(
        "SELECT ItemListID, COUNT(*) FROM MerchantItem WHERE ItemListID LIKE 'artifact_scrolls%' GROUP BY 1")]
    out["zonepoint_dup_id_realm"] = q(
        "SELECT COUNT(*) FROM (SELECT Id, Realm FROM ZonePoint GROUP BY Id, Realm HAVING COUNT(*)>1)")[0][0]
    out["teleport_gaheris"] = q("SELECT COUNT(*) FROM Teleport WHERE Type='gaheris'")[0][0]
    toa = ("(SELECT RegionID FROM Regions WHERE Expansion >= 2)")
    out["zonepoint_toa_targets"] = [list(r) for r in q(
        "SELECT z.TargetRegion, COUNT(*), (SELECT COUNT(*) FROM Mob m WHERE m.Region=z.TargetRegion), "
        "EXISTS (SELECT 1 FROM Regions r WHERE r.RegionID=z.TargetRegion) FROM ZonePoint z "
        f"WHERE z.TargetRegion IN {toa} OR z.SourceRegion IN {toa} GROUP BY 1 ORDER BY 1")]
    out["teleport_targets_unknown_region"] = q(
        "SELECT COUNT(*) FROM Teleport t WHERE t.Type='gaheris' AND NOT EXISTS "
        "(SELECT 1 FROM Regions r WHERE r.RegionID=t.RegionID)")[0][0]
    out["named_npcs"] = [list(r) for r in q(
        f"SELECT Name, Region, ClassType, ItemsListTemplateID FROM {mob} WHERE Name IN "
        "('Arbiter','Aphaestia','Zosyne','Mnosus','Demyphon') ORDER BY Name, Region")]
    out["portal_npcs_gaheris_teleporter"] = q(
        f"SELECT COUNT(*) FROM {mob} WHERE ClassType='DOL.GS.Scripts.GaherisTeleporter'")[0][0]
    # Type affinity drift in loaded mob rows (MySQL would have cast silently)
    drift = {}
    for cid, name, ctype, *_ in table_columns(con, "Mob"):
        if "INT" in (ctype or "").upper():
            n = q(f"SELECT COUNT(*) FROM {mob} WHERE PackageID=? AND typeof({conv.sqlite_ident(name)}) NOT IN ('integer','null')",
                  PACKAGE_ID)[0][0]
            if n:
                drift[name] = n
    out["mob_int_columns_not_integer"] = drift
    return out


def regions(con: sqlite3.Connection) -> dict:
    out = OrderedDict()
    for label, ids in ATLANTIS_REGION_GROUPS.items():
        marks = ",".join("?" * len(ids))
        total = con.execute(f"SELECT COUNT(*) FROM Mob WHERE Region IN ({marks})", ids).fetchone()[0]
        new = con.execute(f"SELECT COUNT(*) FROM Mob WHERE Region IN ({marks}) AND PackageID=?", ids + [PACKAGE_ID]).fetchone()[0]
        out[label] = {"total": total, "gaheris": new}
    return out


DBPUBLIC_PACKAGE_ID = "dbpublic-toa-dungeons"
# ToA dungeon copies per realm (realm from the gaheris zone points: realm 1 jumps
# from 73 into 78-90, realm 2 from 30 into 35-47, realm 3 from 130 into 135-147).
TOA_DUNGEON_REGIONS = OrderedDict([
    (35, "Cetus' Pit (Mid)"), (78, "Cetus' Pit (Alb)"), (135, "Cetus' Pit (Hib)"),
    (36, "Sobekite Eternal (Mid)"), (79, "Sobekite Eternal (Alb)"), (136, "Sobekite Eternal (Hib)"),
    (37, "Temple of Twilight (Mid)"), (80, "Temple of Twilight (Alb)"), (137, "Temple of Twilight (Hib)"),
    (40, "Halls of Ma'ati (Mid)"), (83, "Halls of Ma'ati (Alb)"), (140, "Halls of Ma'ati (Hib)"),
    (46, "Deep Volcanus (Mid)"), (89, "Deep Volcanus (Alb)"), (146, "Deep Volcanus (Hib)"),
    (45, "Great Pyramid (Mid)"), (88, "Great Pyramid (Alb)"), (145, "Great Pyramid (Hib)"),
    (47, "Aerus City (Mid)"), (90, "Aerus City (Alb)"), (147, "Aerus City (Hib)"),
    (91, "Celestius"),
])
ATLANTIS_ALL_REGIONS = sorted(set(range(30, 48)) | set(range(70, 91)) | {91} | set(range(130, 148)))


def dungeon_regions(con: sqlite3.Connection, package: str = DBPUBLIC_PACKAGE_ID) -> dict:
    """Mobs per ToA dungeon region, split by origin (this pack, gaheris, everything else)."""
    out = OrderedDict()
    for region, label in TOA_DUNGEON_REGIONS.items():
        row = con.execute(
            "SELECT COUNT(*), SUM(PackageID=?), SUM(PackageID=?) FROM Mob WHERE Region=?",
            (package, PACKAGE_ID, region)).fetchone()
        total, pack, gaheris = row[0], row[1] or 0, row[2] or 0
        out[f"{region} {label}"] = {"total": total, "dbpublic": pack, "gaheris": gaheris,
                                    "other": total - pack - gaheris}
    return out


def pack_sanity(con: sqlite3.Connection, source_root: Path, package: str = DBPUBLIC_PACKAGE_ID,
                radius: int = 50) -> dict:
    """Referential and duplicate checks for the db-public dungeon and loot packs (030/031/040)."""
    q = lambda sql, *a: con.execute(sql, a).fetchall()  # noqa: E731
    out: dict = {}
    classes, _, _ = source_types(source_root)
    classes_lower = {c.lower() for c in classes}
    out["mobs_loaded"] = q("SELECT COUNT(*) FROM Mob WHERE PackageID=?", package)[0][0]
    out["mobs_per_region"] = [list(r) for r in q(
        "SELECT Region, COUNT(*) FROM Mob WHERE PackageID=? GROUP BY 1 ORDER BY 1", package)]
    out["duplicates_same_name_within_radius"] = [list(r) for r in q(
        "SELECT o.PackageID, COUNT(*) FROM Mob m JOIN Mob o ON o.Region=m.Region AND o.Name=m.Name "
        "AND o.Mob_ID<>m.Mob_ID AND o.PackageID IS NOT m.PackageID "
        "AND (o.X-m.X)*(o.X-m.X)+(o.Y-m.Y)*(o.Y-m.Y)+(o.Z-m.Z)*(o.Z-m.Z) <= ? "
        "WHERE m.PackageID=? GROUP BY 1", radius * radius, package)]
    out["gaheris_mobs_in_pack_regions"] = q(
        "SELECT COUNT(*) FROM Mob WHERE PackageID=? AND Region IN (SELECT DISTINCT Region FROM Mob WHERE PackageID=?)",
        PACKAGE_ID, package)[0][0]
    out["npctemplate_missing"] = [list(r) for r in q(
        "SELECT m.NPCTemplateID, COUNT(*) FROM Mob m WHERE m.PackageID=? AND m.NPCTemplateID NOT IN (0,-1) "
        "AND NOT EXISTS (SELECT 1 FROM NpcTemplate t WHERE t.TemplateId=m.NPCTemplateID) GROUP BY 1", package)]
    out["npctemplate_pack_rows"] = q("SELECT COUNT(*) FROM NpcTemplate WHERE PackageID=?", package)[0][0]
    out["npctemplate_duplicate_templateid"] = q(
        "SELECT COUNT(*) FROM (SELECT TemplateId FROM NpcTemplate GROUP BY TemplateId HAVING COUNT(*)>1 "
        "AND SUM(PackageID=?)>0)", package)[0][0]
    out["mob_equipment_missing"] = [list(r) for r in q(
        "SELECT m.EquipmentTemplateID, COUNT(*) FROM Mob m WHERE m.PackageID=? AND COALESCE(m.EquipmentTemplateID,'')<>'' "
        "AND NOT EXISTS (SELECT 1 FROM NPCEquipment e WHERE e.TemplateID=m.EquipmentTemplateID) GROUP BY 1", package)]
    tpl_equipment = set()
    for (value,) in q("SELECT DISTINCT t.EquipmentTemplateID FROM NpcTemplate t WHERE t.TemplateId IN "
                      "(SELECT NPCTemplateID FROM Mob WHERE PackageID=?) AND COALESCE(t.EquipmentTemplateID,'')<>''",
                      package):
        tpl_equipment |= {p.strip() for p in value.split(";") if p.strip() and ":" not in p}
    out["template_equipment_ids"] = len(tpl_equipment)
    out["template_equipment_missing"] = sorted(
        e for e in tpl_equipment if not q("SELECT 1 FROM NPCEquipment WHERE TemplateID=? LIMIT 1", e))
    out["faction_missing"] = q(
        "SELECT COUNT(*) FROM Mob m WHERE m.PackageID=? AND m.FactionID<>0 "
        "AND NOT EXISTS (SELECT 1 FROM Faction f WHERE f.ID=m.FactionID)", package)[0][0]
    out["path_missing"] = q(
        "SELECT COUNT(*) FROM Mob m WHERE m.PackageID=? AND COALESCE(m.PathID,'')<>'' "
        "AND NOT EXISTS (SELECT 1 FROM Path p WHERE p.PathID=m.PathID)", package)[0][0]
    out["itemslist_missing"] = q(
        "SELECT COUNT(*) FROM Mob m WHERE m.PackageID=? AND COALESCE(m.ItemsListTemplateID,'')<>'' "
        "AND NOT EXISTS (SELECT 1 FROM MerchantItem i WHERE i.ItemListID=m.ItemsListTemplateID)", package)[0][0]
    ctypes = q("SELECT COALESCE(ClassType,''), COUNT(*) FROM Mob WHERE PackageID=? GROUP BY 1 ORDER BY 2 DESC", package)
    out["classtypes"] = [[c, n, (c == "" or c.lower() in classes_lower)] for c, n in ctypes]
    brains = q("SELECT COALESCE(Brain,''), COUNT(*) FROM Mob WHERE PackageID=? GROUP BY 1 ORDER BY 2 DESC", package)
    out["brains"] = [[b, n, (b == "" or b.lower() in classes_lower)] for b, n in brains]
    # Loot (040): names of every mob standing in an Atlantis region after the import.
    regs = ",".join(str(r) for r in ATLANTIS_ALL_REGIONS)
    con.execute("DROP TABLE IF EXISTS temp.toa_names")
    con.execute(f"CREATE TEMP TABLE toa_names AS SELECT DISTINCT lower(Name) AS n FROM Mob WHERE Region IN ({regs})")
    out["toa_mob_names"] = q("SELECT COUNT(*) FROM temp.toa_names")[0][0]
    out["toa_names_with_link"] = q(
        "SELECT COUNT(DISTINCT lower(x.MobName)) FROM MobXLootTemplate x WHERE lower(x.MobName) IN (SELECT n FROM temp.toa_names)")[0][0]
    out["toa_linked_templates_with_loot_rows"] = q(
        "SELECT COUNT(DISTINCT lower(x.LootTemplateName)) FROM MobXLootTemplate x WHERE lower(x.MobName) IN "
        "(SELECT n FROM temp.toa_names) AND EXISTS (SELECT 1 FROM LootTemplate t WHERE t.TemplateName=x.LootTemplateName)")[0][0]
    out["toa_linked_templates_without_loot_rows"] = [r[0] for r in q(
        "SELECT DISTINCT lower(x.LootTemplateName) FROM MobXLootTemplate x WHERE lower(x.MobName) IN "
        "(SELECT n FROM temp.toa_names) AND NOT EXISTS (SELECT 1 FROM LootTemplate t WHERE t.TemplateName=x.LootTemplateName) "
        "ORDER BY 1")]
    out["toa_loot_rows"] = q(
        "SELECT COUNT(*) FROM LootTemplate t WHERE lower(t.TemplateName) IN (SELECT n FROM temp.toa_names) OR "
        "lower(t.TemplateName) IN (SELECT lower(x.LootTemplateName) FROM MobXLootTemplate x WHERE lower(x.MobName) IN "
        "(SELECT n FROM temp.toa_names))")[0][0]
    out["toa_loot_items_unresolved"] = [r[0] for r in q(
        "SELECT DISTINCT t.ItemTemplateID FROM LootTemplate t WHERE (lower(t.TemplateName) IN (SELECT n FROM temp.toa_names) "
        "OR lower(t.TemplateName) IN (SELECT lower(x.LootTemplateName) FROM MobXLootTemplate x WHERE lower(x.MobName) IN "
        "(SELECT n FROM temp.toa_names))) AND NOT EXISTS (SELECT 1 FROM ItemTemplate i WHERE i.Id_nb=t.ItemTemplateID) "
        "ORDER BY 1")]
    out["toa_loot_items_case_only"] = q(
        "SELECT COUNT(DISTINCT t.ItemTemplateID) FROM LootTemplate t JOIN ItemTemplate i ON i.Id_nb=t.ItemTemplateID "
        "WHERE i.Id_nb<>t.ItemTemplateID COLLATE BINARY AND lower(t.TemplateName) IN (SELECT lower(x.LootTemplateName) "
        "FROM MobXLootTemplate x WHERE lower(x.MobName) IN (SELECT n FROM temp.toa_names))")[0][0]
    # Mobs outside Atlantis that share a name with a ToA mob that has loot rows (they drop it too).
    out["loot_names_also_outside_atlantis"] = [list(r) for r in q(
        f"SELECT Name, Region, COUNT(*) FROM Mob WHERE Region NOT IN ({regs}) AND lower(Name) IN "
        "(SELECT lower(x.MobName) FROM MobXLootTemplate x WHERE lower(x.MobName) IN (SELECT n FROM temp.toa_names) "
        "AND EXISTS (SELECT 1 FROM LootTemplate t WHERE t.TemplateName=x.LootTemplateName) "
        "UNION SELECT lower(t.TemplateName) FROM LootTemplate t WHERE lower(t.TemplateName) IN (SELECT n FROM temp.toa_names)) "
        "GROUP BY 1, 2 ORDER BY 1")]
    con.execute("DROP TABLE temp.toa_names")
    return out


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--db", required=True, type=Path, help="disposable probe database (overwritten with --fresh-from)")
    ap.add_argument("--fresh-from", type=Path, help="copy this database over --db before probing")
    ap.add_argument("--sql-dir", required=True, type=Path, help="directory with converted .sql files")
    ap.add_argument("--source-root", type=Path, default=Path(__file__).resolve().parents[2] / "source" / "server")
    ap.add_argument("--json", type=Path, help="write the full report here")
    ap.add_argument("--no-row-fallback", action="store_true", help="do not retry failed multi-row INSERTs row by row")
    ap.add_argument("--dbpublic-package", default=DBPUBLIC_PACKAGE_ID,
                    help="PackageID of the db-public dungeon pack (030/031/040) for the dungeon checks")
    ap.add_argument("files", nargs="*", help="file names in --sql-dir, applied in the order given; a name may "
                    "repeat (re-application check). Default: every *.sql, sorted")
    args = ap.parse_args(argv)
    refuse_unsafe(args.db)
    if args.fresh_from:
        if args.fresh_from.resolve() == args.db.resolve():
            raise SystemExit("--fresh-from and --db must differ")
        for suffix in ("-wal", "-shm", "-journal"):
            Path(str(args.db) + suffix).unlink(missing_ok=True)
        shutil.copyfile(args.fresh_from, args.db)
    con = sqlite3.connect(str(args.db), isolation_level=None)
    con.execute("PRAGMA foreign_keys=OFF")
    files = [args.sql_dir / f for f in args.files] if args.files else sorted(args.sql_dir.glob("*.sql"))
    report = {"sqlite_version": sqlite3.sqlite_version, "regions_before": regions(con),
              "dungeon_regions_before": dungeon_regions(con, args.dbpublic_package), "files": []}
    for path in files:
        res = probe_file(con, path, not args.no_row_fallback)
        report["files"].append(res)
        print(f"{res['file']}: {res['succeeded']}/{res['statements']} ok, {res['failed']} failed; "
              f"rows loaded {res['rows_loaded']}, rejected {res['rows_rejected']}, "
              f"updated/deleted {res['rows_changed_update_delete']}")
        for e in res["errors"]:
            print(f"    #{e['statement']}: {e['error']}")
    report["regions_after"] = regions(con)
    report["sanity"] = sanity(con, args.source_root)
    report["dungeon_regions_after"] = dungeon_regions(con, args.dbpublic_package)
    report["dbpublic_sanity"] = pack_sanity(con, args.source_root, args.dbpublic_package)
    con.close()
    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        with open(args.json, "w", encoding="utf-8", newline="\n") as fh:
            json.dump(report, fh, indent=2, default=list)
            fh.write("\n")
    print(json.dumps({"regions_after": report["regions_after"], "sanity": {k: v for k, v in report["sanity"].items()
                                                                           if k not in ("classtypes", "brains")},
                      "dungeon_regions_after": report["dungeon_regions_after"],
                      "dbpublic_sanity": report["dbpublic_sanity"]},
                     indent=1, default=list))
    return 0


if __name__ == "__main__":
    sys.exit(main())
