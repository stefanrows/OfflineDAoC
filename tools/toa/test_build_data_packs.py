#!/usr/bin/env python3
"""Unit tests for build_data_packs.py. Run: python3 -m unittest discover -s tools/toa -p 'test_*.py'"""

import gzip
import hashlib
import json
import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import build_data_packs as bdp  # noqa: E402


def spell_file(marker):
    ids = list(bdp.ARTIFACT_SPELL_IDS) + [15001]
    rows = [f"('uuid-{i}',{i},1,1,'Spell {i}','Line one\nline two; it''s {marker}')" for i in ids]
    return (f'-- Source sha256: {"b" * 64}\n-- the sweep\n\n'
            f'DELETE FROM "spell" WHERE "SpellID" IN ({",".join(map(str, ids))});\n\n'
            'INSERT INTO "spell" ("Spell_ID","SpellID","ClientEffect","Icon","Name","Description") VALUES\n'
            + ',\n'.join(rows) + ';\n')


def write_inputs(sql_dir, marker='1'):
    for _, files, _, _ in bdp.PACKS:
        for name in files:
            text = (spell_file(marker) if name in bdp.ROW_FILTERS else
                    f'-- Source sha256: {"a" * 64}\n-- {name}\nINSERT INTO t VALUES ({marker});\n')
            (sql_dir / name).write_text(text, encoding='utf-8')


class BuildDataPacksTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        root = pathlib.Path(self.tmp.name)
        self.sql = root / 'sql'
        self.out = root / 'out'
        self.sql.mkdir()
        write_inputs(self.sql)

    def tearDown(self):
        self.tmp.cleanup()

    def snapshot(self):
        return {p.name: p.read_bytes() for p in sorted(self.out.iterdir())}

    def test_build_is_reproducible_and_hashes_uncompressed_text(self):
        manifest = bdp.build(self.sql, self.out)
        first = self.snapshot()
        bdp.build(self.sql, self.out)
        self.assertEqual(first, self.snapshot())

        self.assertEqual([p['name'] for p in manifest['packs']], [p[0] for p in bdp.PACKS])
        for entry in manifest['packs']:
            data = (self.out / entry['file']).read_bytes()
            if entry['file'].endswith('.gz'):
                data = gzip.decompress(data)
            self.assertEqual(hashlib.sha256(data).hexdigest(), entry['sha256'])
            self.assertNotIn(b'\r', data)
        mobs = next(p for p in manifest['packs'] if p['name'] == '020-atlantis-mobs')
        self.assertTrue(mobs['file'].endswith('.sql.gz'))
        spells = next(p for p in manifest['packs'] if p['name'] == '010-toa-ml-spells')
        self.assertEqual([s['file'] for s in spells['sources']],
                         ['12-master-levels.sql', '42-master-levels-gaps.sql', '43-master-levels-corrected.sql'])

    def test_artifact_spell_pack_keeps_only_the_listed_rows(self):
        manifest = bdp.build(self.sql, self.out)
        entry = next(p for p in manifest['packs'] if p['name'] == '012-toa-artifact-spells')
        text = (self.out / entry['file']).read_text(encoding='utf-8')
        self.assertEqual(text.count("('uuid-"), len(bdp.ARTIFACT_SPELL_IDS))
        self.assertNotIn("'uuid-15001'", text)
        self.assertIn('Line one\nline two; it\'\'s 1', text)
        delete = next(line for line in text.splitlines() if line.startswith('DELETE FROM "spell"'))
        self.assertNotIn('15001', delete)

    def test_changed_pack_is_refused_unless_allowed(self):
        bdp.build(self.sql, self.out)
        write_inputs(self.sql, marker='2')
        with self.assertRaises(SystemExit):
            bdp.build(self.sql, self.out)
        bdp.build(self.sql, self.out, allow_changed=True)

    def test_incomplete_statement_is_refused(self):
        (self.sql / '39-master-level-paths.sql').write_text('INSERT INTO t VALUES (1)\n', encoding='utf-8')
        with self.assertRaises(SystemExit):
            bdp.build(self.sql, self.out)

    def test_shipped_packs_match_the_converted_files(self):
        shipped = bdp.DEFAULT_OUT / 'manifest.json'
        if not shipped.is_file() or not (bdp.DEFAULT_SQL / '13-atlantis-mobs.sql').is_file():
            self.skipTest('needs the shipped packs and the regenerated (untracked) 13-atlantis-mobs.sql')
        rebuilt = bdp.build(bdp.DEFAULT_SQL, self.out)
        expected = json.loads(shipped.read_text(encoding='utf-8'))
        self.assertEqual(rebuilt, expected)


if __name__ == '__main__':
    unittest.main()
