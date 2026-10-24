#!/usr/bin/env python3
"""Focused checks for the gaheris MySQL -> SQLite converter and the import probe.

Run with:
    python3 -m unittest discover -s tools/toa -p 'test_*.py'

Everything runs against in-memory or temporary SQLite databases; no game
database is opened.
"""

from __future__ import annotations

import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import convert_mysql_to_sqlite as conv  # noqa: E402
import probe_import as probe  # noqa: E402


def schema(con: sqlite3.Connection) -> None:
    con.executescript(
        """
        CREATE TABLE Spell (SpellID INT NOT NULL DEFAULT 0, Name TEXT NOT NULL DEFAULT '',
                            Type TEXT NOT NULL DEFAULT '', Spell_ID VARCHAR(255) NOT NULL PRIMARY KEY);
        CREATE UNIQUE INDEX U_Spell_SpellID ON Spell(SpellID);
        CREATE TABLE LineXSpell (LineName TEXT NOT NULL DEFAULT '', SpellID INT NOT NULL DEFAULT 0,
                                 Level INT NOT NULL DEFAULT 0, LineXSpell_ID VARCHAR(255) NOT NULL PRIMARY KEY);
        CREATE TABLE Mob (Name TEXT NOT NULL DEFAULT '', Region INT NOT NULL DEFAULT 0,
                          ClassType TEXT, PackageID TEXT, Mob_ID VARCHAR(255) NOT NULL PRIMARY KEY);
        CREATE TABLE ZonePoint (Id INT NOT NULL DEFAULT 0, Realm INT NOT NULL DEFAULT 0,
                                ZonePoint_ID VARCHAR(255) NOT NULL PRIMARY KEY);
        """
    )


def convert(sql: str, drop=None) -> str:
    text, _report = conv.convert_text(sql, "test.sql", "literal", drop or {}, {})
    return text


class TokenizerTests(unittest.TestCase):
    def test_mysql_strings_and_escapes(self):
        toks = [t for t in conv.tokenize("SELECT 'it''s', 'a\\'b', \"dq\", `col`;") if t.significant]
        self.assertEqual([t.kind for t in toks], ["word", "string", "punct", "string", "punct", "string", "punct", "ident", "semi"])
        self.assertEqual(toks[1].value, "it's")
        self.assertEqual(toks[3].value, "a'b")
        self.assertEqual(toks[5].value, "dq")
        self.assertEqual(toks[7].value, "col")

    def test_semicolon_inside_string_does_not_split(self):
        stmts = [s for s in conv.split_statements(conv.tokenize("INSERT INTO t VALUES ('a;b'); DELETE FROM t;")) if s.has_body]
        self.assertEqual(len(stmts), 2)

    def test_comments_kept_and_hash_comment_converted(self):
        out = convert("-- keep me\n# hash comment\nDELETE FROM `mob` WHERE `Name` = 'x';\n")
        self.assertIn("-- keep me", out)
        self.assertIn("-- hash comment", out)
        self.assertIn('DELETE FROM "mob" WHERE "Name" = \'x\';', out)


class ConversionTests(unittest.TestCase):
    def test_session_statements_become_comments(self):
        out = convert("SET NAMES utf8mb4;\nSET autocommit=0;\nCOMMIT;\n")
        for line in out.strip().splitlines():
            self.assertTrue(line.startswith("-- [MySQL session statement, not run]"), line)

    def test_on_duplicate_key_update_values(self):
        out = convert("INSERT INTO `spell` (`SpellID`, `Name`, `Spell_ID`) VALUES (1, 'A', 'x') "
                      "ON DUPLICATE KEY UPDATE `Name`=VALUES(`Name`);")
        self.assertIn('ON CONFLICT DO UPDATE SET "Name"=excluded."Name";', out)

    def test_on_duplicate_key_self_assign_is_do_nothing(self):
        out = convert("INSERT INTO zonepoint (ZonePoint_ID, Id) VALUES (UUID(), 1)\n"
                      "ON DUPLICATE KEY UPDATE `ZonePoint_ID` = `ZonePoint_ID`;")
        self.assertIn("ON CONFLICT DO NOTHING;", out)

    def test_uuid_literals_are_deterministic_and_distinct(self):
        sql = "INSERT INTO `mob` (`Mob_ID`, `Name`) VALUES (UUID(), 'a'), (UUID(), 'b');"
        first, second = convert(sql), convert(sql)
        self.assertEqual(first, second)
        self.assertNotIn("UUID()", first)
        ids = [t.value for t in conv.tokenize(first, "sqlite") if t.kind == "string" and len(t.value or "") == 36]
        self.assertEqual(len(set(ids)), 2)

    def test_uuid_expr_mode(self):
        text, _ = conv.convert_text("INSERT INTO t (id) VALUES (UUID());", "x.sql", "expr", {}, {})
        self.assertIn("randomblob", text)

    def test_insert_ignore_and_replace(self):
        self.assertIn("INSERT OR IGNORE INTO", convert("INSERT IGNORE INTO t (a) VALUES (1);"))
        self.assertIn("INSERT OR REPLACE INTO", convert("REPLACE INTO t (a) VALUES (1);"))
        self.assertIn("INSERT INTO", convert("INSERT t (a) VALUES (1);"))

    def test_booleans_and_functions(self):
        out = convert("UPDATE t SET a = TRUE, b = FALSE, c = NOW(), d = CONCAT('x', name), e = LCASE(name) WHERE 'true' = 'true';")
        self.assertIn("a = 1, b = 0, c = datetime('now'), d = ('x' || name), e = lower(name)", out)
        self.assertIn("WHERE 'true' = 'true'", out)

    def test_drop_column_from_insert_update_and_upsert(self):
        drop = {("linexspell", "packageid"): "test"}
        out = convert("INSERT INTO `linexspell` (`LineXSpell_ID`, `LineName`, `PackageID`, `SpellID`) VALUES "
                      "(UUID(), 'Banelord', 'gaheris', 7261),\n (UUID(), 'Warlord', 'gaheris', 7262)"
                      " ON DUPLICATE KEY UPDATE `PackageID`=VALUES(`PackageID`), `Level`=VALUES(`Level`);", drop)
        self.assertNotIn("PackageID", out)
        self.assertNotIn("gaheris", out)
        self.assertIn("'Banelord', 7261", out)
        self.assertIn('ON CONFLICT DO UPDATE SET "Level"=excluded."Level"', out)
        last = convert("INSERT INTO linexspell (LineName, PackageID) VALUES ('a', 'b');", drop)
        self.assertIn("(LineName) VALUES ('a')", last)
        upd = convert("UPDATE linexspell SET PackageID = 'x', Level = 2 WHERE SpellID = 1;", drop)
        self.assertIn("SET Level = 2 WHERE", upd)
        only = convert("UPDATE linexspell SET PackageID = 'x' WHERE SpellID = 1;", drop)
        self.assertTrue(all(line.startswith("-- [") for line in only.strip().splitlines()))

    def test_converted_sql_runs_and_upserts(self):
        con = sqlite3.connect(":memory:")
        schema(con)
        sql = (
            "SET NAMES utf8mb4;\n"
            "DELETE FROM `mob` WHERE `PackageID` = 'gaheris-atlantis';\n"
            "INSERT INTO `spell` (`SpellID`, `Name`, `Type`, `Spell_ID`) VALUES (7201, 'Old', 'X', 'ML_1')\n"
            "  ON DUPLICATE KEY UPDATE `Name`=VALUES(`Name`), `Type`=VALUES(`Type`);\n"
            "INSERT INTO `spell` (`SpellID`, `Name`, `Type`, `Spell_ID`) VALUES (7201, 'New', 'Y', 'ML_1')\n"
            "  ON DUPLICATE KEY UPDATE `Name`=VALUES(`Name`), `Type`=VALUES(`Type`);\n"
            "INSERT INTO `mob` (`Name`, `Region`, `PackageID`, `Mob_ID`) VALUES ('a', 73, 'gaheris-atlantis', UUID()),\n"
            "('b', 30, 'gaheris-atlantis', UUID());\n"
            "INSERT INTO zonepoint (ZonePoint_ID, Id, Realm) VALUES (UUID(), 116, 1) ON DUPLICATE KEY UPDATE `ZonePoint_ID` = `ZonePoint_ID`;\n"
            "COMMIT;\n"
        )
        text = convert(sql)
        con.executescript(text)
        con.executescript(text)  # deterministic ids: a re-run upserts / skips instead of duplicating
        self.assertEqual(con.execute("SELECT Name, Type FROM Spell").fetchall(), [("New", "Y")])
        self.assertEqual(con.execute("SELECT COUNT(*) FROM ZonePoint").fetchone()[0], 1)
        self.assertEqual(con.execute("SELECT COUNT(*) FROM Mob").fetchone()[0], 2)


class ProbeTests(unittest.TestCase):
    def test_insert_shape_rows_and_column_diff(self):
        sql = convert("INSERT INTO `mob` (`Name`, `Bogus`, `Mob_ID`) VALUES ('a', 1, UUID()), ('b', 2, UUID());")
        stmt = probe.split_sql_file(sql)[0]
        shape = probe.InsertShape(stmt)
        self.assertTrue(shape.ok and shape.is_values)
        self.assertEqual(shape.columns, ["Name", "Bogus", "Mob_ID"])
        self.assertEqual(len(shape.rows), 2)

    def test_probe_file_counts_rows_and_rejections(self):
        with tempfile.TemporaryDirectory() as tmp:
            db = Path(tmp) / "probe.db"
            con = sqlite3.connect(str(db), isolation_level=None)
            schema(con)
            con.execute("INSERT INTO Mob (Name, Region, Mob_ID) VALUES ('dup', 1, 'fixed')")
            sql = convert(
                "INSERT INTO `mob` (`Name`, `Region`, `PackageID`, `Mob_ID`) VALUES "
                "('a', 73, 'gaheris-atlantis', UUID()), ('dup', 30, 'gaheris-atlantis', 'fixed');\n"
                "INSERT INTO `mob` (`Name`, `Nope`, `Mob_ID`) VALUES ('c', 1, UUID());\n"
                "UPDATE `mob` SET `ClassType` = 'X' WHERE `Region` = 73;\n")
            path = Path(tmp) / "t.sql"
            path.write_text(sql, encoding="utf-8")
            res = probe.probe_file(con, path, row_fallback=True)
            self.assertEqual(res["statements"], 3)
            self.assertEqual(res["failed"], 2)
            self.assertEqual(res["rows_loaded"], 1)
            self.assertEqual(res["rows_rejected"], 2)
            self.assertEqual(res["rows_changed_update_delete"], 1)
            bogus = [s for s in res["insert_shapes"].values() if "Nope" in s["columns"]][0]
            self.assertEqual(bogus["missing_in_our_table"], ["Nope"])
            self.assertEqual(res["touched_tables"], [{"table": "Mob", "before": 1, "after": 2}])
            con.close()

    def test_refuses_live_paths(self):
        with self.assertRaises(SystemExit):
            probe.refuse_unsafe(Path("/x/playable/runtime/data/opendaoc.sqlite3.db"))
        with self.assertRaises(SystemExit):
            probe.refuse_unsafe(Path("/x/playable-dev/db-snapshot/opendaoc.snapshot.db"))


if __name__ == "__main__":
    unittest.main()
