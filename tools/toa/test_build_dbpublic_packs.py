#!/usr/bin/env python3
"""Focused checks for the db-public ToA dungeon/loot pack generator (WP1.1b).

Run with:
    python3 -m unittest discover -s tools/toa -p 'test_*.py'

A tiny fake db-public export is written to a temporary directory, the packs are
generated from it and applied to an in-memory SQLite database that carries our
column lists and keys. No game database is opened.
"""

from __future__ import annotations

import json
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import build_dbpublic_packs as gen  # noqa: E402
import probe_import as probe  # noqa: E402

PRIMARY_KEYS = {"Mob": "Mob_ID", "NpcTemplate": "NpcTemplate_ID", "NPCEquipment": "NPCEquipment_ID",
                "Faction": "ID", "LootTemplate": "LootTemplate_ID", "MobXLootTemplate": "MobXLootTemplate_ID"}


def our_schema(con: sqlite3.Connection) -> None:
    for table, cols in gen.SCHEMA.items():
        defs = []
        for name, kind, not_null, default in cols:
            sql_type = "INT" if kind == "int" else "TEXT"
            collate = " COLLATE NOCASE" if kind == "text" else ""
            defs.append(f'"{name}" {sql_type}{" NOT NULL" if not_null else ""} DEFAULT {default}{collate}')
        defs.append(f'PRIMARY KEY ("{PRIMARY_KEYS[table]}")')
        con.execute(f'CREATE TABLE "{table}" ({", ".join(defs)})')
    con.execute('CREATE UNIQUE INDEX U_Faction_Faction_ID ON Faction(Faction_ID)')


def mob(mob_id, name, region, x, y, z=100, **extra):
    row = {"Mob_ID": mob_id, "Name": name, "Region": region, "X": x, "Y": y, "Z": z, "Model": 900,
           "ClassType": "DOL.GS.GameNPC", "Level": 50, "NPCTemplateID": 0, "EquipmentTemplateID": None,
           "FactionID": 0, "MaxDistance": 3000, "OwnerID": None, "Brain": None, "PackageID": "Toa_Hib",
           "LastTimeRowUpdated": "2000-01-01 00:00:00", "ItemsListTemplateID": None}
    row.update(extra)
    return row


def template(tid, tpl_id, name, model="900", replace=1, equipment=None, faction=None):
    row = {"TemplateId": tid, "NpcTemplate_ID": tpl_id, "Name": name, "Model": model, "Level": "50",
           "Size": "50", "ClassType": "DOL.GS.GameNPC", "ReplaceMobValues": replace,
           "EquipmentTemplateID": equipment, "PackageID": "Public_DB", "LastTimeRowUpdated": "2000-01-01 00:00:00"}
    if faction is not None:
        row["FactionID"] = faction
    return row


def equip(eq_id, tid, slot):
    return {"NPCEquipment_ID": eq_id, "TemplateID": tid, "Slot": slot, "Model": 1, "Color": 0, "Effect": 0,
            "Extension": 0, "Emblem": 0, "LastTimeRowUpdated": "2000-01-01 00:00:00"}


def fake_dbpublic(root: Path) -> None:
    mobs0 = [
        mob("m1", "Sobekite pulverizer", 36, 1000, 1000, NPCTemplateID=500, EquipmentTemplateID="eqA"),
        mob("m2", "Sobekite pulverizer", 36, 5000, 5000),
        mob("m3", "near twin", 37, 2000, 2000),         # a same-named mob already stands 30 units away
        mob("m4", "far twin", 37, 3000, 3000),          # a same-named mob already stands 60 units away
        mob("m5", "Knight", 36, 4000, 4000, Model=993, NPCTemplateID=600),  # broken template link
        mob("m6", "overworld mob", 30, 100, 100),       # Atlantis overworld: loot only, no spawn
        mob("m7", "Aerus mob", 47, 100, 100),           # Aerus City is gaheris territory
    ]
    mobs1 = [
        mob("m8", "Boat to Nowhere", 40, 100, 100, ClassType="DOL.GS.NoSuchTaxiBoat"),
        mob("m9", "odd beast", 40, 200, 200, ClassType="DOL.GS.NoSuchBeast"),
        mob("m10", "Cetus", 35, 300, 300, Level="73"),
        mob("classic", "Sobekite pulverizer", 1, 1, 1),  # outside every ToA region
    ]
    tables = {
        "Mob.0.json": mobs0, "Mob.1.json": mobs1,
        "NpcTemplate.json": [template(500, "tpl500", "Sobekite pulverizer", equipment="eqA;eqB;0:12|13", faction=7),
                             template(600, "tpl600", "Knight", model="7;32-46"),
                             template(999, "tpl999", "unused")],
        "NPCEquipment.json": [equip("e1", "eqA", 10), equip("e2", "eqA", 11), equip("e3", "eqB", 10),
                              equip("e4", "eqZ", 10)],
        "Faction.json": [{"ID": 7, "Name": "Sobekites", "BaseAggroLevel": 100, "Faction_ID": "F7",
                          "LastTimeRowUpdated": "2000-01-01 00:00:00"},
                         {"ID": 8, "Name": "unused", "BaseAggroLevel": 100, "Faction_ID": "F8",
                          "LastTimeRowUpdated": "2000-01-01 00:00:00"}],
        "MobXLootTemplate.json": [
            {"MobXLootTemplate_ID": "x1", "MobName": "sobekite pulverizer", "LootTemplateName": "sobekite pulverizer",
             "DropCount": 2, "LastTimeRowUpdated": "2000-01-01 00:00:00"},
            {"MobXLootTemplate_ID": "x2", "MobName": "overworld mob", "LootTemplateName": "overworld loot",
             "DropCount": 1, "LastTimeRowUpdated": "2000-01-01 00:00:00"},
            {"MobXLootTemplate_ID": "x3", "MobName": "classic only", "LootTemplateName": "classic only",
             "DropCount": 1, "LastTimeRowUpdated": "2000-01-01 00:00:00"}],
        "LootTemplate.json": [
            {"LootTemplate_ID": "l1", "TemplateName": "sobekite pulverizer", "ItemTemplateID": "Spear_of_kings",
             "Chance": 15, "Count": 0, "LastTimeRowUpdated": "2000-01-01 00:00:00"},
            {"LootTemplate_ID": "l2", "TemplateName": "overworld loot", "ItemTemplateID": "it2",
             "Chance": 20, "Count": 1, "LastTimeRowUpdated": "2000-01-01 00:00:00"},
            {"LootTemplate_ID": "l3", "TemplateName": "aerus mob", "ItemTemplateID": "it3",
             "Chance": 15, "Count": 0, "LastTimeRowUpdated": "2000-01-01 00:00:00"},
            {"LootTemplate_ID": "l4", "TemplateName": "classic only", "ItemTemplateID": "it4",
             "Chance": 15, "Count": 0, "LastTimeRowUpdated": "2000-01-01 00:00:00"}],
    }
    for name, rows in tables.items():
        (root / name).write_text(json.dumps(rows), encoding="utf-8")


def apply(con: sqlite3.Connection, path: Path) -> int:
    before = con.total_changes
    for stmt in probe.split_sql_file(path.read_text(encoding="utf-8")):
        con.execute(stmt)
    return con.total_changes - before


class HelperTests(unittest.TestCase):
    def test_sql_literal(self):
        self.assertEqual(gen.sql_literal(None, "int"), "NULL")
        self.assertEqual(gen.sql_literal("73", "int"), "73")
        self.assertEqual(gen.sql_literal(True, "int"), "1")
        self.assertEqual(gen.sql_literal("Ka'a", "text"), "'Ka''a'")
        self.assertEqual(gen.sql_literal(606, "text"), "'606'")
        with self.assertRaises(ValueError):
            gen.sql_literal("10-11", "int")

    def test_mapping_drops_unknown_and_defaults_not_null(self):
        m = gen.MappedTable("Mob")
        values = dict(zip(m.names, m.map(mob("a", "x", 36, 1, 2), {"PackageID": "tag"})))
        self.assertNotIn("MaxDistance", values)
        self.assertEqual(m.dropped["MaxDistance"], 1)
        self.assertEqual(values["OwnerID"], "''")      # NULL in a NOT NULL column -> our default
        self.assertEqual(values["Brain"], "NULL")      # nullable column keeps NULL
        self.assertEqual(values["PackageID"], "'tag'")
        self.assertEqual(values["Speed"], "0")         # absent in the source row -> default
        self.assertEqual(m.defaulted["OwnerID"], 1)

    def test_equipment_ids(self):
        self.assertEqual(gen.equipment_ids("A;b; ;0:12|13"), {"a", "b"})
        self.assertEqual(gen.equipment_ids(None), set())

    def test_template_models_and_broken_link(self):
        self.assertEqual(gen.template_models("7;32-34;x"), {7, 32, 33, 34})
        knight = template(600, "t", "Knight", model="7;32-46")
        self.assertTrue(gen.broken_template_link({"Model": 993}, knight))
        self.assertFalse(gen.broken_template_link({"Model": 40}, knight))
        self.assertFalse(gen.broken_template_link({"Model": 993}, dict(knight, ReplaceMobValues=0)))
        self.assertFalse(gen.broken_template_link({"Model": 993}, None))

    def test_class_verdict(self):
        classes = {"dol.gs.gamenpc"}
        self.assertEqual(gen.class_verdict({"ClassType": "DOL.GS.GameNPC"}, classes), "keep")
        self.assertEqual(gen.class_verdict({"ClassType": "DOL.GS.Beast"}, classes), "fallback")
        self.assertEqual(gen.class_verdict({"ClassType": "DOL.GS.GameTaxiBoat"}, classes), "skip")
        self.assertEqual(gen.class_verdict({"ClassType": "DOL.GS.Odd", "ItemsListTemplateID": "list"}, classes),
                         "skip")
        self.assertEqual(gen.class_verdict({"ClassType": "DOL.GS.Anything"}, None), "keep")


class BuildTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        self.src, self.out = root / "data", root / "out"
        self.src.mkdir()
        fake_dbpublic(self.src)
        self.classes_file = root / "src"
        (self.classes_file / "GameServer").mkdir(parents=True)
        (self.classes_file / "GameServer" / "npc.cs").write_text(
            "namespace DOL.GS { public class GameNPC {} }\n", encoding="utf-8")
        self.report = gen.build(self.src, self.out, "testref", self.classes_file)
        self.con = sqlite3.connect(":memory:", isolation_level=None)
        our_schema(self.con)
        ins = 'INSERT INTO "{}" ({}) VALUES ({})'
        # A gaheris mob 30 units from "near twin", one 60 units from "far twin".
        for mid, name, x in (("g1", "near twin", 2030), ("g2", "far twin", 3060)):
            self.con.execute(ins.format("Mob", "Mob_ID, Name, Region, X, Y, Z, PackageID", "?,?,?,?,?,?,?"),
                             (mid, name, 37, x, 2000 if name == "near twin" else 3000, 100, "gaheris-atlantis"))
        # Our save already has template 500 under another key and one slot of eqB.
        self.con.execute(ins.format("NpcTemplate", "TemplateId, Name, NpcTemplate_ID", "?,?,?"), (500, "ours", "own"))
        self.con.execute(ins.format("NPCEquipment", "TemplateID, Slot, NPCEquipment_ID", "?,?,?"), ("eqB", 22, "own"))
        self.con.execute(ins.format("MobXLootTemplate", "MobName, LootTemplateName, MobXLootTemplate_ID", "?,?,?"),
                         ("Sobekite Pulverizer", "Sobekite Pulverizer", "ours-link"))

    def tearDown(self):
        self.con.close()
        self.tmp.cleanup()

    def apply_all(self):
        return {name: apply(self.con, self.out / name) for name in
                ("031-toa-dungeon-templates.sql", "030-toa-dungeon-mobs.sql", "040-toa-loot.sql")}

    def test_mob_pack_regions_classes_and_guards(self):
        stats = self.report["mobs"]
        self.assertEqual(stats["skipped_classtypes"], {"DOL.GS.NoSuchTaxiBoat": 1})
        self.assertEqual(stats["fallback_classtypes"], {"DOL.GS.NoSuchBeast": 1})
        self.assertEqual(stats["template_links_cleared"], {"600": 1})
        self.assertEqual(stats["rows"], 7)                  # m1-m5, m9, m10
        self.apply_all()
        names = {r[0] for r in self.con.execute("SELECT Mob_ID FROM Mob WHERE PackageID=?", (gen.PACK_TAG,))}
        self.assertEqual(names, {"m1", "m2", "m4", "m5", "m9", "m10"})   # m3 is a duplicate of g1
        self.assertEqual(self.con.execute("SELECT NPCTemplateID FROM Mob WHERE Mob_ID='m5'").fetchone()[0], 0)
        self.assertEqual(self.con.execute("SELECT Level FROM Mob WHERE Mob_ID='m10'").fetchone()[0], 73)

    def test_templates_keyed_by_server_ids(self):
        stats = self.report["templates"]
        self.assertEqual(stats["npctemplate_rows"], 1)       # 600 was cleared, 999 unused
        self.assertEqual(stats["faction_ids_referenced"], [7])
        counts = self.apply_all()
        self.assertEqual(counts["031-toa-dungeon-templates.sql"], 3)  # eqA two slots + faction 7
        self.assertEqual(self.con.execute("SELECT Name FROM NpcTemplate WHERE TemplateId=500").fetchall(), [("ours",)])
        self.assertEqual(self.con.execute("SELECT COUNT(*) FROM NPCEquipment WHERE TemplateID='eqA'").fetchone()[0], 2)
        self.assertEqual(self.con.execute("SELECT Slot FROM NPCEquipment WHERE TemplateID='eqB'").fetchall(), [(22,)])
        self.assertEqual(self.con.execute("SELECT COUNT(*) FROM NPCEquipment WHERE TemplateID='eqZ'").fetchone()[0], 0)
        self.assertEqual(self.con.execute("SELECT ID FROM Faction").fetchall(), [(7,)])

    def test_loot_by_atlantis_names(self):
        stats = self.report["loot"]
        self.assertEqual(stats["loottemplate_rows"], 3)      # pulverizer, overworld loot, aerus mob (by name)
        self.assertEqual(stats["mobxloottemplate_rows"], 2)
        counts = self.apply_all()
        self.assertEqual(counts["040-toa-loot.sql"], 4)      # the pulverizer link exists in another case
        self.assertEqual(self.con.execute("SELECT COUNT(*) FROM LootTemplate WHERE TemplateName='classic only'")
                         .fetchone()[0], 0)
        self.assertEqual(self.con.execute("SELECT COUNT(*) FROM MobXLootTemplate WHERE lower(MobName)="
                                          "'sobekite pulverizer'").fetchone()[0], 1)

    def test_reapplication_changes_nothing(self):
        self.apply_all()
        again = self.apply_all()
        self.assertEqual(again, {k: 0 for k in again})

    def test_output_is_deterministic_lf(self):
        first = {p.name: p.read_bytes() for p in self.out.glob("*.sql")}
        gen.build(self.src, self.out, "testref", self.classes_file)
        second = {p.name: p.read_bytes() for p in self.out.glob("*.sql")}
        self.assertEqual(first, second)
        for data in first.values():
            self.assertNotIn(b"\r", data)

    def test_equipment_template_never_split_across_statements(self):
        rows = [["'t1'"], ["'t1'"], ["'t2'"]]
        m = gen.MappedTable("Faction")
        m.names = ["x"]
        stmts = gen.guarded_inserts(m, rows, None, 1, group_keys=["t1", "t1", "t2"])
        self.assertEqual(len(stmts), 2)
        self.assertEqual(stmts[0].count("'t1'"), 2)

    def test_probe_pack_sanity(self):
        self.apply_all()
        for ddl in ("CREATE TABLE Path (PathID TEXT)", "CREATE TABLE MerchantItem (ItemListID TEXT)",
                    "CREATE TABLE ItemTemplate (Id_nb TEXT COLLATE NOCASE PRIMARY KEY)"):
            self.con.execute(ddl)
        self.con.execute("INSERT INTO ItemTemplate VALUES ('Spear_of_Kings'), ('it2')")
        # gaheris placed the Aerus mob: its by-name template 'aerus mob' now counts, and its item is missing.
        self.con.execute("INSERT INTO Mob (Mob_ID, Name, Region, PackageID) VALUES ('g3', 'Aerus mob', 47, 'gaheris-atlantis')")
        out = probe.pack_sanity(self.con, self.classes_file)
        self.assertEqual(out["mobs_loaded"], 6)
        self.assertEqual(out["duplicates_same_name_within_radius"], [])
        self.assertEqual(out["toa_loot_items_unresolved"], ["it3"])
        self.assertEqual(out["toa_loot_items_case_only"], 1)
        self.assertEqual([c for c, n, ok in out["classtypes"] if not ok], ["DOL.GS.NoSuchBeast"])


if __name__ == "__main__":
    unittest.main()
