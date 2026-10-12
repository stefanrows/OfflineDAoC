"""Offline unit tests for render_client_maps.py and the helpers in client_map_data.py.

Run from the repository root with:
    python3 -m unittest discover -s source/server/tools -p "test_render_client_maps.py"
No client, server, or game file is needed. Font-dependent tests are skipped when no
Georgia or Liberation font is installed.
"""

import json
import os
import sqlite3
import sys
import tempfile
import types
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import client_map_data as D  # noqa: E402
import render_client_maps as R  # noqa: E402


def _font_available():
    try:
        R.font('georgiab.ttf', 12)
        return True
    except (FileNotFoundError, OSError):
        return False


def _zone(zid, name, region, x0, y0, w=8, h=8, enabled='1', ztype='0'):
    return D.ZoneInfo(zid, {
        'name': name, 'region': str(region), 'enabled': enabled, 'type': ztype,
        'width': str(w), 'height': str(h),
        'region_offset_x': str(x0), 'region_offset_y': str(y0),
    }, {})


class RelicLabelTests(unittest.TestCase):
    def test_table(self):
        expected = {
            (1, 0): 'Scabbard Relic', (1, 1): 'Staff Relic',
            (2, 0): 'Hammer Relic', (2, 1): 'Horn Relic',
            (3, 0): 'Spear Relic', (3, 1): 'Cauldron Relic',
        }
        for key, label in expected.items():
            self.assertEqual(D.relic_label(*key), label)

    def test_unknown_pairs_are_none(self):
        self.assertIsNone(D.relic_label(0, 0))
        self.assertIsNone(D.relic_label(1, 2))
        self.assertIsNone(D.relic_label(4, 0))
        self.assertIsNone(D.relic_label(None, None))


class ExtraLabelTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.dir = self._tmp.name

    def tearDown(self):
        self._tmp.cleanup()

    def _write(self, doc):
        path = os.path.join(self.dir, 'labels.json')
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(doc, f)
        return path

    def test_valid_file(self):
        path = self._write({
            '_comment': 'ignored',
            'zones': {
                '101': [{'name': 'Tomte Camp', 'x': 160, 'y': 101, 'kind': 'place'},
                        {'name': ' Lake Tomte ', 'x': 0, 'y': 256, 'kind': 'water'}],
                '11': [],
            },
        })
        out = D.load_extra_labels(path)
        self.assertEqual(sorted(out), [11, 101])
        self.assertEqual(out[11], [])
        self.assertEqual(out[101][0], {'name': 'Tomte Camp', 'x': 160.0, 'y': 101.0, 'kind': 'place'})
        self.assertEqual(out[101][1]['name'], 'Lake Tomte')
        self.assertEqual(out[101][1]['kind'], 'water')

    def test_missing_file_gives_empty(self):
        self.assertEqual(D.load_extra_labels(os.path.join(self.dir, 'nope.json')), {})
        self.assertEqual(D.load_extra_labels(None), {})

    def test_bad_kind_raises_with_zone_and_entry(self):
        path = self._write({'zones': {'101': [{'name': 'X', 'x': 1, 'y': 1, 'kind': 'road'}]}})
        with self.assertRaises(ValueError) as cm:
            D.load_extra_labels(path)
        self.assertIn('zone 101', str(cm.exception))
        self.assertIn('entry 0', str(cm.exception))

    def test_out_of_range_raises(self):
        for x, y in [(257, 0), (-1, 10), (10, 300)]:
            path = self._write({'zones': {'7': [{'name': 'Y', 'x': x, 'y': y, 'kind': 'place'}]}})
            with self.assertRaises(ValueError) as cm:
                D.load_extra_labels(path)
            self.assertIn('zone 7', str(cm.exception))

    def test_empty_name_raises(self):
        for name in ['', '   ', None]:
            path = self._write({'zones': {'7': [{'name': name, 'x': 1, 'y': 1, 'kind': 'place'}]}})
            with self.assertRaises(ValueError):
                D.load_extra_labels(path)

    def test_boolean_coordinate_rejected(self):
        path = self._write({'zones': {'7': [{'name': 'Z', 'x': True, 'y': 1, 'kind': 'place'}]}})
        with self.assertRaises(ValueError):
            D.load_extra_labels(path)


class ZonePoiRelicTests(unittest.TestCase):
    """zone_pois() on a small temporary world database with only the columns it reads."""

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.path = os.path.join(self._tmp.name, 'world.sqlite3.db')
        self.with_relic = True

    def tearDown(self):
        self._tmp.cleanup()

    def _build(self, relic_rows, keep_rows):
        con = sqlite3.connect(self.path)
        con.executescript(
            'CREATE TABLE Area (Description TEXT, X INT, Y INT, Radius INT, Region INT, ClassType TEXT);'
            'CREATE TABLE BindPoint (X INT, Y INT, Region INT);'
            'CREATE TABLE Keep (Name TEXT, X INT, Y INT, Region INT);'
            'CREATE TABLE Mob (ClassType TEXT, Name TEXT, X INT, Y INT, Region INT);'
            'CREATE TABLE ZonePoint (SourceX INT, SourceY INT, SourceRegion INT, TargetRegion INT, ClassType TEXT);'
            'CREATE TABLE JumpPoint (Name TEXT, Region INT, Xpos INT, Ypos INT);'
            'CREATE TABLE Teleport (TeleportID TEXT, RegionID INT, X INT, Y INT);')
        if self.with_relic:
            con.execute('CREATE TABLE Relic (X INT, Y INT, Region INT, OriginalRealm INT, relicType INT)')
            con.executemany('INSERT INTO Relic VALUES (?, ?, 111, ?, ?)', relic_rows)
        con.executemany('INSERT INTO Keep VALUES (?, ?, ?, 111)', keep_rows)
        con.commit()
        con.close()

    def _pois(self):
        db = D.WorldDb(self.path)
        try:
            zinfo = types.SimpleNamespace(region=111, origin_loc=(0, 0), extent_loc=(65536, 65536))
            data = db.zone_pois(zinfo, lambda tr: ('overworld', 'Other'))
        finally:
            db.close()
        return {p['name']: p for p in data['pois'] if p['type'] == 'keep'}

    def test_relic_attaches_to_nearest_keep_within_range(self):
        self._build(
            relic_rows=[(1050, 1020, 2, 0), (9000, 9000, 1, 0)],
            keep_rows=[('Mjollner Faste', 1000, 1000), ('Far Keep', 5000, 5000), ('', 1010, 1010)],
        )
        keeps = self._pois()
        self.assertEqual(keeps['Mjollner Faste']['relic'], 'Hammer Relic')
        self.assertNotIn('relic', keeps['Far Keep'])
        self.assertNotIn('', keeps)          # empty names are skipped

    def test_relic_outside_range_is_not_attached(self):
        self._build(relic_rows=[(4500, 1000, 2, 0)], keep_rows=[('Mjollner Faste', 1000, 1000)])
        self.assertNotIn('relic', self._pois()['Mjollner Faste'])

    def test_unknown_relic_type_is_ignored(self):
        self._build(relic_rows=[(1050, 1020, 2, 9)], keep_rows=[('Mjollner Faste', 1000, 1000)])
        self.assertNotIn('relic', self._pois()['Mjollner Faste'])

    def test_missing_relic_table_is_fine(self):
        self.with_relic = False
        self._build(relic_rows=[], keep_rows=[('Mjollner Faste', 1000, 1000)])
        keeps = self._pois()
        self.assertEqual(keeps['Mjollner Faste']['name'], 'Mjollner Faste')
        self.assertNotIn('relic', keeps['Mjollner Faste'])


class NeighbourNameTests(unittest.TestCase):
    def test_dummy_and_duplicate_names_dropped(self):
        # zone 152 sits at sectors (8, 8); each neighbour touches one of its edges
        zones = {
            152: _zone(152, 'DUMMY ZONE', 152, 8, 8),       # the zone itself
            1: _zone(1, 'Dummy Zone', 152, 16, 8),           # east, filtered as dummy
            2: _zone(2, 'Svasud Faste', 152, 8, 16),         # south
            3: _zone(3, 'Svasud Faste', 152, 8, 16),         # south again: listed once
            4: _zone(4, '', 152, 8, 0),                      # north but unnamed
            5: _zone(5, 'Lake Tomte', 152, 0, 8),            # west
        }
        names = R._neighbour_names(zones, 152)
        self.assertEqual(names['south'], ['Svasud Faste'])
        self.assertEqual(names['north'], [])
        self.assertEqual(names['east'], [])
        self.assertEqual(names['west'], ['Lake Tomte'])
        for side_names in names.values():
            for n in side_names:
                self.assertNotIn('dummy', n.lower())

    def test_dummy_only_edge_is_empty(self):
        zones = {
            10: _zone(10, 'Real Zone', 10, 0, 0),
            11: _zone(11, 'Dummy Zone', 10, 8, 0),           # east edge, dummy
        }
        names = R._neighbour_names(zones, 10)
        self.assertEqual(names['east'], [])
        self.assertEqual(names['west'], [])


class IconCandidateTests(unittest.TestCase):
    def test_second_ring_follows_the_first(self):
        cands = R._icon_candidates(100.0, 100.0)
        self.assertEqual(cands[0], (100.0, 100.0))
        self.assertIn((114.0, 100.0), cands)
        self.assertIn((100.0, 86.0), cands)
        self.assertLess(cands.index((109.0, 100.0)), cands.index((114.0, 100.0)))


@unittest.skipUnless(_font_available(), 'no Georgia or Liberation font installed')
class TerrainFontTests(unittest.TestCase):
    def test_wrap_splits_long_names_near_middle(self):
        from PIL import Image
        cv = R.Canvas(512)
        cv.img = Image.new('RGBA', (cv.S, cv.S), (0, 0, 0, 0))
        fnt = R.font('georgiab.ttf', int(round(12.0 * cv.u)))
        self.assertEqual(R._wrap_label(cv, 'Hib', fnt, 1.8), ['Hib'])
        lines = R._wrap_label(cv, 'The Long Named Border Keep Ruins', fnt, 1.8)
        self.assertEqual(len(lines), 2)
        self.assertEqual(' '.join(lines), 'The Long Named Border Keep Ruins')
        self.assertTrue(all(lines))


if __name__ == '__main__':
    unittest.main()
