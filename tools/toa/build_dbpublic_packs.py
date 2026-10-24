#!/usr/bin/env python3
"""Build the db-public ToA dungeon and loot SQL packs (WP1.1b, docs/TOA_PLAN.md).

gaheris (tools/toa/sql/13-*) carries the Atlantis overworld, the Haven cities,
Aerus City and the Great Pyramid entrance, but none of the ToA dungeons, and no
loot rows for ToA mobs. This script reads the Eve-of-Darkness/db-public JSON
export (one file per table, ``data/*.json``) and writes three SQLite files in
our schema:

* ``030-toa-dungeon-mobs.sql``: Mob rows of the ToA dungeon regions;
* ``031-toa-dungeon-templates.sql``: the NpcTemplate, NPCEquipment and Faction
  rows those mobs reference;
* ``040-toa-loot.sql``: LootTemplate and MobXLootTemplate rows for the mob
  names of every Atlantis region.

Every statement is guarded so that applying a file twice, applying it after or
before gaheris, or applying it to a save that already holds some of the rows
changes nothing that exists:

* Mob: ``ON CONFLICT DO NOTHING`` on Mob_ID, plus no insert when a mob with the
  same name already stands in the region within 50 units (rows of this pack
  itself excepted, so the result does not depend on statement chunking);
* NpcTemplate: no insert when the TemplateId exists (the server's key);
* NPCEquipment: no insert when any row of that TemplateID exists, so a
  template is never mixed from two sources (a template is never split over two
  statements);
* Faction: ``ON CONFLICT DO NOTHING`` (ID and Faction_ID are unique);
* LootTemplate / MobXLootTemplate: no insert when the same
  (TemplateName, ItemTemplateID) / (MobName, LootTemplateName) pair exists.

Columns are mapped to our schema (``SCHEMA`` below, taken from PRAGMA
table_info of the dev snapshot): columns we do not have are dropped and
counted, columns db-public lacks or leaves NULL in a NOT NULL column get our
default. Mob_ID and every other primary key are kept, so the rows stay
traceable to db-public. Mob rows get PackageID ``dbpublic-toa-dungeons`` (undo:
``DELETE FROM Mob WHERE PackageID = 'dbpublic-toa-dungeons'``).

Rows whose ClassType has no class in source/server are kept only when the
server's plain GameNPC fallback is acceptable (ordinary monsters); NPCs that
need their class (boats, teleporters, merchants, trainers) are skipped and
listed.

Output is deterministic and LF. Typical run (repository root):

    python3 -I tools/toa/build_dbpublic_packs.py --src <db-public>/data \\
        --out tools/toa/sql --source-ref <db-public commit sha>
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from collections import Counter, OrderedDict
from pathlib import Path

GENERATOR_VERSION = 1
PACK_TAG = "dbpublic-toa-dungeons"
DUPLICATE_RADIUS = 50
MOB_CHUNK = 200
DEFAULT_CHUNK = 250

# Server region id -> label. Realm per copy follows the gaheris zone points
# (realm 1 jumps from Atlantis 73 into 78-90, realm 2 from 30 into 35-47,
# realm 3 from 130 into 135-147).
DUNGEON_REGIONS: "OrderedDict[int, str]" = OrderedDict([
    (35, "Cetus' Pit (Midgard)"), (36, "Sobekite Eternal (Midgard)"),
    (37, "Temple of Twilight (Midgard)"), (38, "region 38 (no Regions row)"),
    (39, "region 39 (no Regions row)"), (40, "Halls of Ma'ati (Midgard)"),
    (46, "Deep Volcanus (Midgard)"),
    (78, "Cetus' Pit (Albion)"), (79, "Sobekite Eternal (Albion)"),
    (80, "Temple of Twilight (Albion)"), (81, "region 81 (no Regions row)"),
    (82, "region 82 (no Regions row)"), (83, "Halls of Ma'ati (Albion)"),
    (89, "Deep Volcanus (Albion)"),
    (135, "Cetus' Pit (Hibernia)"), (136, "Sobekite Eternal (Hibernia)"),
    (137, "Temple of Twilight (Hibernia)"), (138, "region 138 (no Regions row)"),
    (139, "region 139 (no Regions row)"), (140, "Halls of Ma'ati (Hibernia)"),
    (146, "Deep Volcanus (Hibernia)"),
    (91, "Celestius"),
])
# The Great Pyramid (45/88/145) and Aerus City (47/90/147) are left out: every
# db-public mob there is already in gaheris file 13 (same name, same spot).

# Mob names from these regions select the loot templates of 040.
ATLANTIS_LOOT_REGIONS = sorted(set(range(30, 48)) | set(range(70, 74)) | set(range(73, 91))
                               | {91} | set(range(130, 148)))

# Our schema: (column, kind, not_null, default literal). kind: int | text.
SCHEMA: dict[str, list[tuple[str, str, bool, str]]] = {
    "Mob": [
        ("ClassType", "text", False, "NULL"), ("TranslationId", "text", False, "NULL"),
        ("Name", "text", True, "''"), ("Suffix", "text", False, "NULL"), ("Guild", "text", False, "NULL"),
        ("ExamineArticle", "text", False, "NULL"), ("MessageArticle", "text", False, "NULL"),
        ("X", "int", True, "0"), ("Y", "int", True, "0"), ("Z", "int", True, "0"),
        ("Speed", "int", True, "0"), ("Heading", "int", True, "0"), ("Region", "int", True, "0"),
        ("Model", "int", True, "0"), ("Size", "int", True, "0"), ("Strength", "int", True, "0"),
        ("Constitution", "int", True, "0"), ("Dexterity", "int", True, "0"), ("Quickness", "int", True, "0"),
        ("Intelligence", "int", True, "0"), ("Piety", "int", True, "0"), ("Empathy", "int", True, "0"),
        ("Charisma", "int", True, "0"), ("Level", "int", True, "0"), ("Realm", "int", True, "0"),
        ("EquipmentTemplateID", "text", False, "NULL"), ("ItemsListTemplateID", "text", False, "NULL"),
        ("NPCTemplateID", "int", True, "0"), ("Race", "int", True, "0"), ("Flags", "int", True, "0"),
        ("AggroLevel", "int", True, "0"), ("AggroRange", "int", True, "0"), ("MeleeDamageType", "int", True, "0"),
        ("RespawnInterval", "int", True, "0"), ("FactionID", "int", True, "0"), ("BodyType", "int", True, "0"),
        ("HouseNumber", "int", True, "0"), ("Brain", "text", False, "NULL"), ("PathID", "text", False, "NULL"),
        ("OwnerID", "text", True, "''"), ("RoamingRange", "int", True, "0"), ("IsCloakHoodUp", "int", True, "0"),
        ("Gender", "int", True, "0"), ("PackageID", "text", False, "NULL"),
        ("VisibleWeaponSlots", "int", True, "0"),
        ("LastTimeRowUpdated", "text", True, "'2000-01-01 00:00:00'"), ("Mob_ID", "text", True, "''"),
    ],
    "NpcTemplate": [
        ("TemplateId", "int", True, "0"), ("TranslationId", "text", False, "NULL"), ("Name", "text", True, "''"),
        ("Suffix", "text", False, "NULL"), ("ClassType", "text", True, "''"), ("GuildName", "text", False, "NULL"),
        ("ExamineArticle", "text", False, "NULL"), ("MessageArticle", "text", False, "NULL"),
        ("Model", "text", True, "''"), ("FactionID", "int", True, "0"), ("Gender", "int", True, "0"),
        ("Size", "text", True, "''"), ("Level", "text", True, "''"), ("MaxSpeed", "int", True, "0"),
        ("EquipmentTemplateID", "text", False, "NULL"), ("ItemsListTemplateID", "text", False, "NULL"),
        ("Flags", "int", True, "0"), ("MeleeDamageType", "int", True, "0"), ("ParryChance", "int", True, "0"),
        ("EvadeChance", "int", True, "0"), ("BlockChance", "int", True, "0"),
        ("LeftHandSwingChance", "int", True, "0"), ("Spells", "text", False, "NULL"),
        ("Styles", "text", False, "NULL"), ("Strength", "int", True, "0"), ("Constitution", "int", True, "0"),
        ("Dexterity", "int", True, "0"), ("Quickness", "int", True, "0"), ("Intelligence", "int", True, "0"),
        ("Piety", "int", True, "0"), ("Charisma", "int", True, "0"), ("Empathy", "int", True, "0"),
        ("Abilities", "text", False, "NULL"), ("AggroLevel", "int", True, "0"), ("AggroRange", "int", True, "0"),
        ("Race", "int", True, "0"), ("BodyType", "int", True, "0"), ("MaxDistance", "int", True, "0"),
        ("TetherRange", "int", True, "0"), ("VisibleWeaponSlots", "int", True, "0"),
        ("ReplaceMobValues", "int", True, "0"), ("PackageID", "text", False, "NULL"),
        ("LastTimeRowUpdated", "text", True, "'2000-01-01 00:00:00'"), ("NpcTemplate_ID", "text", True, "''"),
    ],
    "NPCEquipment": [
        ("TemplateID", "text", True, "''"), ("Slot", "int", True, "0"), ("Model", "int", True, "0"),
        ("Color", "int", True, "0"), ("Effect", "int", True, "0"), ("Extension", "int", True, "0"),
        ("Emblem", "int", True, "0"), ("LastTimeRowUpdated", "text", True, "'2000-01-01 00:00:00'"),
        ("NPCEquipment_ID", "text", True, "''"),
    ],
    "Faction": [
        ("ID", "int", True, "0"), ("Name", "text", False, "NULL"), ("BaseAggroLevel", "int", True, "0"),
        ("LastTimeRowUpdated", "text", True, "'2000-01-01 00:00:00'"), ("Faction_ID", "text", False, "NULL"),
    ],
    "LootTemplate": [
        ("TemplateName", "text", True, "''"), ("ItemTemplateID", "text", True, "''"), ("Chance", "int", True, "0"),
        ("Count", "int", True, "0"), ("LastTimeRowUpdated", "text", True, "'2000-01-01 00:00:00'"),
        ("LootTemplate_ID", "text", True, "''"),
    ],
    "MobXLootTemplate": [
        ("MobName", "text", True, "''"), ("LootTemplateName", "text", True, "''"), ("DropCount", "int", True, "0"),
        ("LastTimeRowUpdated", "text", True, "'2000-01-01 00:00:00'"), ("MobXLootTemplate_ID", "text", True, "''"),
    ],
}

# ClassType fallback: the server replaces an unknown ClassType by a plain GameNPC.
# That is fine for a monster, not for an NPC whose class is its function.
FUNCTIONAL_CLASS_RE = re.compile(r"teleport|porter|boat|taxi|merchant|trainer|healer|vault|registrar|"
                                 r"guard|scholar|recharger|enchanter|smith|hastener|stable|master", re.I)

INPUT_FILES = ("Mob.*.json", "NpcTemplate.json", "NPCEquipment.json", "Faction.json",
               "LootTemplate.json", "MobXLootTemplate.json")


# --------------------------------------------------------------------------- SQL helpers

def quote_ident(name: str) -> str:
    return '"' + name.replace('"', '""') + '"'


def sql_literal(value, kind: str) -> str:
    if value is None:
        return "NULL"
    if kind == "int":
        if isinstance(value, bool):
            return "1" if value else "0"
        if isinstance(value, int):
            return str(value)
        if isinstance(value, float) and value.is_integer():
            return str(int(value))
        if isinstance(value, str) and re.fullmatch(r"-?\d+", value.strip()):
            return str(int(value.strip()))
        raise ValueError(f"not an integer: {value!r}")
    text = value if isinstance(value, str) else str(value)
    return "'" + text.replace("'", "''") + "'"


class MappedTable:
    """Maps db-public rows onto one of our tables and records what it had to change."""

    def __init__(self, table: str):
        self.table = table
        self.columns = SCHEMA[table]
        self.names = [c[0] for c in self.columns]
        self.dropped: Counter = Counter()
        self.defaulted: Counter = Counter()

    def map(self, row: dict, overrides: dict | None = None) -> list[str]:
        overrides = overrides or {}
        ours = {n.lower() for n in self.names}
        for key in row:
            if key.lower() not in ours:
                self.dropped[key] += 1
        lower = {k.lower(): v for k, v in row.items()}
        out = []
        for name, kind, not_null, default in self.columns:
            if name in overrides:
                value = overrides[name]
            elif name.lower() in lower:
                value = lower[name.lower()]
            else:
                self.defaulted[name] += 1
                out.append(default)
                continue
            if value is None and not_null:
                self.defaulted[name] += 1
                out.append(default)
                continue
            out.append(sql_literal(value, kind))
        return out

    def column_ref(self, name: str) -> str:
        return f"v.column{self.names.index(name) + 1}"


def guarded_inserts(mapped: MappedTable, rows: list[list[str]], guard: str | None, chunk: int,
                    group_keys: list | None = None) -> list[str]:
    """INSERT ... SELECT FROM (VALUES ...) AS v [WHERE guard] ON CONFLICT DO NOTHING, chunked.

    ``group_keys`` (one per row) keeps rows with the same key in one statement.
    """
    statements = []
    cols = ", ".join(quote_ident(c) for c in mapped.names)
    select = ", ".join(f"column{i + 1}" for i in range(len(mapped.names)))
    where = f"\nWHERE {guard}" if guard else "\nWHERE 1"
    start = 0
    while start < len(rows):
        end = min(start + chunk, len(rows))
        if group_keys is not None:
            while end < len(rows) and group_keys[end] == group_keys[end - 1]:
                end += 1
        values = ",\n".join("(" + ", ".join(r) + ")" for r in rows[start:end])
        statements.append(f"INSERT INTO {quote_ident(mapped.table)} ({cols})\nSELECT {select} FROM (VALUES\n"
                          f"{values}\n) AS v{where}\nON CONFLICT DO NOTHING;")
        start = end
    return statements


# --------------------------------------------------------------------------- inputs

def mob_files(src: Path) -> list[Path]:
    files = sorted(src.glob("Mob.*.json"), key=lambda p: int(re.search(r"Mob\.(\d+)\.json$", p.name).group(1))
                   if re.search(r"Mob\.(\d+)\.json$", p.name) else 10 ** 6)
    if not files and (src / "Mob.json").exists():
        files = [src / "Mob.json"]
    if not files:
        raise SystemExit(f"no Mob.*.json in {src}")
    return files


def input_hashes(src: Path) -> "OrderedDict[str, str]":
    out: "OrderedDict[str, str]" = OrderedDict()
    for pattern in INPUT_FILES:
        for path in (mob_files(src) if pattern.startswith("Mob.") else [src / pattern]):
            if path.exists():
                out[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    return out


def load_json(path: Path) -> list[dict]:
    with open(path, encoding="utf-8") as fh:
        data = json.load(fh)
    if not isinstance(data, list):
        raise SystemExit(f"{path}: expected a JSON array")
    return data


def load_mobs(src: Path, regions: set[int]) -> list[dict]:
    out = []
    for path in mob_files(src):
        out.extend(r for r in load_json(path) if r.get("Region") in regions)
    return out


def source_classes(source_root: Path | None) -> set[str] | None:
    if source_root is None or not source_root.exists():
        return None
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import probe_import  # noqa: E402  (sibling module, scans the C# tree)
    classes, _, _ = probe_import.source_types(source_root)
    return {c.lower() for c in classes}


# --------------------------------------------------------------------------- packs

def class_verdict(row: dict, classes: set[str] | None) -> str:
    """keep | fallback | skip"""
    ctype = (row.get("ClassType") or "").strip()
    if not ctype or classes is None or ctype.lower() in classes:
        return "keep"
    if FUNCTIONAL_CLASS_RE.search(ctype) or (row.get("ItemsListTemplateID") or "").strip():
        return "skip"
    return "fallback"


def template_models(value) -> set[int]:
    """Model ids of an NpcTemplate Model value ("7;6;32-46")."""
    out: set[int] = set()
    for part in str(value or "").split(";"):
        part = part.strip()
        m = re.fullmatch(r"(\d+)\s*-\s*(\d+)", part)
        if m:
            lo, hi = sorted((int(m.group(1)), int(m.group(2))))
            out.update(range(lo, hi + 1))
        elif part.isdigit():
            out.add(int(part))
    return out


def broken_template_link(mob: dict, template: dict | None) -> bool:
    """True when the template would overwrite the mob with a different creature.

    A template with ReplaceMobValues replaces model, level and size at spawn. If the
    mob's own model is not among the template's models, db-public linked the mob to
    a same-named template of another creature (the Sobekite Eternal chess "Knight",
    model 993, points at the classic Albion knight template 12967).
    """
    if template is None or not int(template.get("ReplaceMobValues") or 0):
        return False
    models = template_models(template.get("Model"))
    return bool(models) and int(mob.get("Model") or 0) not in models


def build_mob_pack(mobs: list[dict], classes: set[str] | None,
                   templates: dict[int, dict] | None = None) -> tuple[list[str], dict, list[dict]]:
    mapped = MappedTable("Mob")
    templates = templates or {}
    kept, skipped, fallback = [], Counter(), Counter()
    cleared: Counter = Counter()
    seen_ids = set()
    duplicate_ids = 0
    for r in mobs:
        verdict = class_verdict(r, classes)
        if verdict == "skip":
            skipped[r.get("ClassType")] += 1
            continue
        if verdict == "fallback":
            fallback[r.get("ClassType")] += 1
        key = str(r["Mob_ID"]).lower()
        if key in seen_ids:
            duplicate_ids += 1
            continue
        seen_ids.add(key)
        kept.append(r)
    kept.sort(key=lambda r: (r["Region"], (r.get("Name") or "").lower(), str(r["Mob_ID"]).lower()))
    fixed = []
    for r in kept:
        tid = r.get("NPCTemplateID")
        if tid not in (None, 0, -1) and broken_template_link(r, templates.get(int(tid))):
            cleared[str(tid)] += 1
            r = dict(r, NPCTemplateID=0)
        fixed.append(r)
    kept = fixed
    rows = [mapped.map(r, {"PackageID": PACK_TAG}) for r in kept]
    c = mapped.column_ref
    guard = ("NOT EXISTS (SELECT 1 FROM \"Mob\" m WHERE m.\"Region\" = {reg} AND m.\"Name\" = {name} "
             "AND m.\"PackageID\" IS NOT '{tag}' AND (m.\"X\" - {x}) * (m.\"X\" - {x}) + (m.\"Y\" - {y}) * (m.\"Y\" - {y}) "
             "+ (m.\"Z\" - {z}) * (m.\"Z\" - {z}) <= {r2})").format(
        reg=c("Region"), name=c("Name"), tag=PACK_TAG, x=c("X"), y=c("Y"), z=c("Z"), r2=DUPLICATE_RADIUS ** 2)
    statements = guarded_inserts(mapped, rows, guard, MOB_CHUNK)
    per_region = Counter(r["Region"] for r in kept)
    stats = {
        "rows": len(rows), "statements": len(statements),
        "per_region": OrderedDict((str(k), per_region.get(k, 0)) for k in DUNGEON_REGIONS),
        "skipped_classtypes": dict(skipped.most_common()), "fallback_classtypes": dict(fallback.most_common()),
        "classtypes": dict(Counter(r.get("ClassType") for r in kept).most_common()),
        "duplicate_mob_ids_in_source": duplicate_ids,
        "template_links_cleared": dict(cleared),
        "columns_dropped": dict(mapped.dropped), "columns_defaulted": dict(mapped.defaulted),
    }
    return statements, stats, kept


def equipment_ids(value) -> set[str]:
    """NPCEquipment TemplateIDs named by an EquipmentTemplateID value.

    NpcTemplate rows may list several ids separated by ';' (the server picks one
    at random, GameNPC/Util.SplitCSV); entries with ':' are inline slot:model
    lists, not template ids.
    """
    if not value or not str(value).strip():
        return set()
    return {part.strip().lower() for part in str(value).split(";") if part.strip() and ":" not in part}


def templates_by_id(templates: list[dict]) -> dict[int, list[dict]]:
    by_id: dict[int, list[dict]] = {}
    for t in templates:
        by_id.setdefault(int(t["TemplateId"]), []).append(t)
    for rows in by_id.values():
        rows.sort(key=lambda t: str(t["NpcTemplate_ID"]).lower())
    return by_id


def build_template_pack(kept_mobs: list[dict], templates: list[dict], equipment: list[dict],
                        factions: list[dict]) -> tuple[list[str], dict]:
    by_id = templates_by_id(templates)
    wanted_ids = sorted({int(r["NPCTemplateID"]) for r in kept_mobs
                         if r.get("NPCTemplateID") not in (None, 0, -1)})
    tpl_rows, missing_ids, ambiguous = [], [], []
    for tid in wanted_ids:
        rows = by_id.get(tid, [])
        if not rows:
            missing_ids.append(tid)
            continue
        if len(rows) > 1:
            ambiguous.append(tid)
        tpl_rows.append(rows[0])
    equip_ids = set()
    for row in list(kept_mobs) + tpl_rows:
        equip_ids |= equipment_ids(row.get("EquipmentTemplateID"))
    eq_rows = sorted((e for e in equipment if str(e["TemplateID"]).lower() in equip_ids),
                     key=lambda e: (str(e["TemplateID"]).lower(), int(e["Slot"]), str(e["NPCEquipment_ID"]).lower()))
    found_equip = {str(e["TemplateID"]).lower() for e in eq_rows}
    faction_ids = {int(r["FactionID"]) for r in kept_mobs if r.get("FactionID")}
    faction_ids |= {int(t["FactionID"]) for t in tpl_rows if t.get("FactionID")}
    fac_rows = sorted((f for f in factions if int(f["ID"]) in faction_ids), key=lambda f: int(f["ID"]))

    statements = []
    m_tpl = MappedTable("NpcTemplate")
    statements += guarded_inserts(
        m_tpl, [m_tpl.map(t, {"PackageID": PACK_TAG}) for t in tpl_rows],
        f"NOT EXISTS (SELECT 1 FROM \"NpcTemplate\" t WHERE t.\"TemplateId\" = {m_tpl.column_ref('TemplateId')})",
        DEFAULT_CHUNK)
    m_eq = MappedTable("NPCEquipment")
    statements += guarded_inserts(
        m_eq, [m_eq.map(e) for e in eq_rows],
        f"NOT EXISTS (SELECT 1 FROM \"NPCEquipment\" e WHERE e.\"TemplateID\" = {m_eq.column_ref('TemplateID')})",
        DEFAULT_CHUNK, group_keys=[str(e["TemplateID"]).lower() for e in eq_rows])
    m_fac = MappedTable("Faction")
    statements += guarded_inserts(m_fac, [m_fac.map(f) for f in fac_rows], None, DEFAULT_CHUNK)
    stats = {
        "npctemplate_ids_referenced": len(wanted_ids), "npctemplate_rows": len(tpl_rows),
        "npctemplate_ids_not_in_source": missing_ids, "npctemplate_ids_ambiguous_in_source": ambiguous,
        "equipment_templates_referenced": len(equip_ids), "equipment_templates_found": len(found_equip),
        "equipment_templates_not_in_source": sorted(equip_ids - found_equip), "npcequipment_rows": len(eq_rows),
        "faction_ids_referenced": sorted(faction_ids), "faction_rows": len(fac_rows),
        "statements": len(statements),
        "columns_dropped": {"NpcTemplate": dict(m_tpl.dropped), "NPCEquipment": dict(m_eq.dropped),
                            "Faction": dict(m_fac.dropped)},
        "columns_defaulted": {"NpcTemplate": dict(m_tpl.defaulted), "NPCEquipment": dict(m_eq.defaulted),
                              "Faction": dict(m_fac.defaulted)},
    }
    return statements, stats


def build_loot_pack(atlantis_names: set[str], links: list[dict], loot: list[dict]) -> tuple[list[str], dict]:
    link_rows = sorted((m for m in links if (m.get("MobName") or "").lower() in atlantis_names),
                       key=lambda m: ((m["MobName"] or "").lower(), (m["LootTemplateName"] or "").lower(),
                                      str(m["MobXLootTemplate_ID"]).lower()))
    template_names = {(m["LootTemplateName"] or "").lower() for m in link_rows}
    # A LootTemplate named after a ToA mob drops even without a MobXLootTemplate row.
    template_names |= {n for n in atlantis_names if n}
    loot_rows = sorted((t for t in loot if (t.get("TemplateName") or "").lower() in template_names),
                       key=lambda t: ((t["TemplateName"] or "").lower(), (t["ItemTemplateID"] or "").lower(),
                                      str(t["LootTemplate_ID"]).lower()))
    m_loot = MappedTable("LootTemplate")
    m_link = MappedTable("MobXLootTemplate")
    statements = guarded_inserts(
        m_loot, [m_loot.map(t) for t in loot_rows],
        f"NOT EXISTS (SELECT 1 FROM \"LootTemplate\" t WHERE t.\"TemplateName\" = {m_loot.column_ref('TemplateName')} "
        f"AND t.\"ItemTemplateID\" = {m_loot.column_ref('ItemTemplateID')})", DEFAULT_CHUNK)
    statements += guarded_inserts(
        m_link, [m_link.map(m) for m in link_rows],
        f"NOT EXISTS (SELECT 1 FROM \"MobXLootTemplate\" x WHERE x.\"MobName\" = {m_link.column_ref('MobName')} "
        f"AND x.\"LootTemplateName\" = {m_link.column_ref('LootTemplateName')})", DEFAULT_CHUNK)
    with_loot = {(t["TemplateName"] or "").lower() for t in loot_rows}
    stats = {
        "atlantis_mob_names": len(atlantis_names),
        "mobxloottemplate_rows": len(link_rows),
        "loot_templates": len(with_loot),
        "linked_templates_without_loot_rows": sorted(
            {(m["LootTemplateName"] or "").lower() for m in link_rows} - with_loot),
        "loottemplate_rows": len(loot_rows),
        "distinct_items": len({(t["ItemTemplateID"] or "").lower() for t in loot_rows}),
        "chance": dict(sorted(Counter(int(t["Chance"]) for t in loot_rows).items())),
        "statements": len(statements),
    }
    return statements, stats


# --------------------------------------------------------------------------- output

def header(title: str, source_ref: str, hashes: "OrderedDict[str, str]", used: list[str], lines: list[str]) -> str:
    out = [f"-- {title}",
           f"-- Generated by tools/toa/build_dbpublic_packs.py (v{GENERATOR_VERSION}) from Eve-of-Darkness/db-public",
           f"-- data/*.json, ref {source_ref or 'unknown'}. Do not hand-edit; rerun the generator.",
           "-- SQLite dialect (UPSERT DO NOTHING without a conflict target: SQLite 3.24+). Every",
           "-- statement is guarded, so re-applying the file changes nothing. Run inside one transaction."]
    out += ["--"] + [f"-- {line}" for line in lines]
    out += ["--", "-- Inputs (sha256):"]
    out += [f"--   {name} {digest}" for name, digest in hashes.items() if name in used]
    return "\n".join(out) + "\n"


def write_sql(path: Path, head: str, statements: list[str]) -> None:
    text = head + "\n" + "\n\n".join(statements) + ("\n" if statements else "")
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)


def build(src: Path, out: Path, source_ref: str, source_root: Path | None) -> dict:
    hashes = input_hashes(src)
    mob_names_used = [p.name for p in mob_files(src)]
    loot_regions = set(ATLANTIS_LOOT_REGIONS)
    all_regions = set(DUNGEON_REGIONS) | loot_regions
    mobs_all = load_mobs(src, all_regions)
    dungeon_mobs = [r for r in mobs_all if r["Region"] in DUNGEON_REGIONS]
    atlantis_names = {(r.get("Name") or "").lower() for r in mobs_all if r["Region"] in loot_regions}
    classes = source_classes(source_root)

    templates = load_json(src / "NpcTemplate.json")
    first_template = {tid: rows[0] for tid, rows in templates_by_id(templates).items()}
    mob_statements, mob_stats, kept = build_mob_pack(dungeon_mobs, classes, first_template)
    tpl_statements, tpl_stats = build_template_pack(kept, templates,
                                                    load_json(src / "NPCEquipment.json"),
                                                    load_json(src / "Faction.json"))
    loot_statements, loot_stats = build_loot_pack(atlantis_names, load_json(src / "MobXLootTemplate.json"),
                                                  load_json(src / "LootTemplate.json"))

    regions_line = ", ".join(f"{k} {v}" for k, v in mob_stats["per_region"].items())
    write_sql(out / "030-toa-dungeon-mobs.sql", header(
        "030-toa-dungeon-mobs.sql: ToA dungeon spawns from db-public (WP1.1b)", source_ref, hashes, mob_names_used,
        [f"Mob rows: {mob_stats['rows']} in {mob_stats['statements']} statements, PackageID '{PACK_TAG}'.",
         f"Per region: {regions_line}.",
         "Regions: Cetus' Pit 35/78/135, Sobekite Eternal 36/79/136, Temple of Twilight 37/80/137,",
         "Halls of Ma'ati 40/83/140, Deep Volcanus 46/89/146, Celestius 91 (db-public has none of",
         "the last two). Great Pyramid and Aerus City come from gaheris file 13.",
         f"Skipped ClassTypes (no class, not a plain monster): {mob_stats['skipped_classtypes'] or 'none'}.",
         f"Columns dropped (not in our Mob table): {mob_stats['columns_dropped'] or 'none'}.",
         "NPCTemplateID set to 0 where the template would replace the mob by another creature",
         f"(ReplaceMobValues and the mob's model not in the template): {mob_stats['template_links_cleared'] or 'none'}.",
         f"Guard: Mob_ID, and no second mob of the same name within {DUPLICATE_RADIUS} units in the region.",
         f"Undo: DELETE FROM \"Mob\" WHERE \"PackageID\" = '{PACK_TAG}';",
         "NpcTemplate/NPCEquipment rows these mobs reference come with file 031 (rows are linked by",
         "value, so the order of 030 and 031 does not matter; the server needs both before start)."]),
        mob_statements)
    write_sql(out / "031-toa-dungeon-templates.sql", header(
        "031-toa-dungeon-templates.sql: templates referenced by the ToA dungeon spawns (WP1.1b)", source_ref, hashes,
        ["NpcTemplate.json", "NPCEquipment.json", "Faction.json"] + mob_names_used,
        [f"NpcTemplate rows: {tpl_stats['npctemplate_rows']} of {tpl_stats['npctemplate_ids_referenced']} referenced "
         "TemplateIds (inserted only where the TemplateId is missing; PackageID set to the pack tag).",
         f"NPCEquipment rows: {tpl_stats['npcequipment_rows']} for {tpl_stats['equipment_templates_found']} "
         "templates (inserted only where the TemplateID has no row at all).",
         f"Faction rows: {tpl_stats['faction_rows']} (referenced ids: {tpl_stats['faction_ids_referenced'] or 'none'}).",
         f"Undo (templates only): DELETE FROM \"NpcTemplate\" WHERE \"PackageID\" = '{PACK_TAG}';"]),
        tpl_statements)
    write_sql(out / "040-toa-loot.sql", header(
        "040-toa-loot.sql: db-public loot for ToA mob names (WP1.3 data, built with WP1.1b)", source_ref, hashes,
        ["LootTemplate.json", "MobXLootTemplate.json"] + mob_names_used,
        [f"LootTemplate rows: {loot_stats['loottemplate_rows']} in {loot_stats['loot_templates']} templates, "
         f"{loot_stats['distinct_items']} distinct items.",
         f"MobXLootTemplate rows: {loot_stats['mobxloottemplate_rows']} (inserted only where the pair is missing).",
         f"Mob names from regions {ATLANTIS_LOOT_REGIONS[0]}-{ATLANTIS_LOOT_REGIONS[-1]} (Atlantis, Havens, Aerus,",
         "Pyramid, dungeons, Celestius). Loot is keyed by mob name, so a same-named mob elsewhere drops it too.",
         "No ItemTemplate rows: the server resolves ItemTemplateID case-insensitively and every item",
         "already exists in the OpenDAoC item table (checked by tools/toa/probe_import.py)."]),
        loot_statements)
    return {"generator_version": GENERATOR_VERSION, "source_ref": source_ref, "inputs": hashes,
            "classes_checked": classes is not None,
            "mobs": mob_stats, "templates": tpl_stats, "loot": loot_stats}


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--src", required=True, type=Path, help="db-public data/ directory with the JSON tables")
    ap.add_argument("--out", required=True, type=Path, help="output directory (normally tools/toa/sql)")
    ap.add_argument("--source-ref", default="", help="db-public commit the JSON came from (header only)")
    ap.add_argument("--source-root", type=Path, default=Path(__file__).resolve().parents[2] / "source" / "server",
                    help="C# tree for the ClassType check; pass a missing path to skip the check")
    ap.add_argument("--report", type=Path, help="write the build statistics as JSON here")
    args = ap.parse_args(argv)
    report = build(args.src, args.out, args.source_ref, args.source_root)
    text = json.dumps(report, indent=2, default=list) + "\n"
    if args.report:
        with open(args.report, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(text)
    print(text, end="")
    return 0


if __name__ == "__main__":
    sys.exit(main())
