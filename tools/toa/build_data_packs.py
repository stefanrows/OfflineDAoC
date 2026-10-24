#!/usr/bin/env python3
"""Build the shipped world data packs from the converted gaheris SQL.

The server applies every pack in ``source/server/CoreServer/data-packs`` once
per save at startup (docs/TOA_DATA_PACKS.md). This script assembles those packs
from ``tools/toa/sql`` (the converter output) and writes ``manifest.json``.

* A pack is one or more converted files concatenated in the listed order.
* A pack marked ``gzip`` is written as ``NNN-name.sql.gz`` (gzip with mtime 0
  and no file name, so the bytes are reproducible).
* The manifest ``sha256`` is the hash of the uncompressed SQL text, so it
  does not depend on the compressor.

Shipped packs are immutable: once a save has applied a pack, the server never
re-runs it, even when its hash changes. A data fix therefore ships as a new
pack with a higher number, never as an edit of an old one. The builder refuses
to change an existing pack unless ``--allow-changed`` is given (use that only
before the pack was ever released).

Run from the repository root:

    python3 -I tools/toa/convert_mysql_to_sqlite.py --src <gaheris>/sql --out tools/toa/sql
    python3 -I tools/toa/build_data_packs.py
"""

import argparse
import gzip
import hashlib
import io
import json
import pathlib
import re
import sys

REPO = pathlib.Path(__file__).resolve().parents[2]
DEFAULT_SQL = REPO / 'tools/toa/sql'
DEFAULT_OUT = REPO / 'source/server/CoreServer/data-packs'

# (name, source files in order, gzip, description)
PACKS = [
    ('010-toa-ml-spells',
     ['12-master-levels.sql', '42-master-levels-gaps.sql', '43-master-levels-corrected.sql'],
     False,
     'Master Level spells and their eight spell lines (gaheris 12, 42, 43): 73 of 80 ranks.'),
    ('011-toa-ml-paths',
     ['39-master-level-paths.sql'],
     False,
     'The eight Master Level paths in every class career at level 50 (gaheris 39). '
     'They stay hidden until the Arbiter grants Master Levels.'),
    ('012-toa-artifact-spells',
     ['90-spells.sql'],
     False,
     'Artifact spells from gaheris 90, filtered: the 68 spell IDs our artifact items reference '
     'and lack, plus the 5 spells they chain to. 5 referenced IDs exist nowhere.'),
    ('020-atlantis-mobs',
     ['13-atlantis-mobs.sql'],
     True,
     'Atlantis population from db-public via gaheris 13: 41,727 mobs tagged '
     "PackageID 'gaheris-atlantis' (overworld, Haven cities, Aerus, pyramid entrance)."),
    ('021-atlantis-travel',
     ['14-atlantis-travel.sql'],
     False,
     'Atlantis teleport destinations (gaheris 14): 13 rows of type gaheris.'),
    ('022-atlantis-zonepoints',
     ['29-atlantis-zonepoints.sql'],
     False,
     'Atlantis zone points (gaheris 29): 100 rows, existing ones kept.'),
    ('023-artifact-scroll-binders',
     ['36-artifact-scrolls.sql'],
     False,
     'Artifact scroll binder merchants (gaheris 36): 162 merchant rows, scroll prices, Zosyne and Mnosus.'),
    # Pack set 2. 024, 025, 041 and 050 are hand-written; 030, 031 and 040 come from
    # build_dbpublic_packs.py (db-public, docs/TOA_DUNGEON_IMPORT.md).
    ('024-toa-arbiter-npcs',
     ['024-toa-arbiter-npcs.sql'],
     False,
     'The Master Level NPCs of gaheris 35: the three Arbiters become DOL.GS.MasterLevelArbiter, '
     'Demyphon in region 72 a Master Level credit merchant.'),
    ('025-toa-atlantis-arena-fix',
     ['025-toa-atlantis-arena-fix.sql'],
     False,
     'Removes the three Atlas fight-club Mordreds (DOL.GS.LordOfBattle) from Albion Atlantis, '
     'regions 73 and 80; the ones in 334 and 339 stay.'),
    # WP1.2/1.3 travel and loot (docs/TOA_TRAVEL.md), hand-written.
    ('026-toa-travel-realms',
     ['026-toa-travel-realms.sql'],
     False,
     'Atlantis travel per realm: 33 Teleport rows of type atlantis (ruins city, four havens, six dungeon '
     'entrances in each realm copy), ten haven porters, and the Oceanus channelers that were plain NPCs become porters.'),
    ('027-toa-open-atlantis',
     ['027-toa-open-atlantis.sql'],
     False,
     'Opens Atlantis to players (atlantis_teleport_plvl 4 or 2 -> 1) and sets rog_toa_item_chance 0 -> 25, '
     'the ToA bonus chance of random loot inside Atlantis.'),
    ('030-toa-dungeon-mobs',
     ['030-toa-dungeon-mobs.sql'],
     False,
     'ToA dungeon spawns from db-public: 2,135 mobs tagged PackageID dbpublic-toa-dungeons '
     "(Cetus' Pit, Sobekite Eternal, Temple of Twilight, Halls of Ma'ati)."),
    ('031-toa-dungeon-templates',
     ['031-toa-dungeon-templates.sql'],
     False,
     'NPC and equipment templates of the dungeon spawns (db-public), inserted only where missing.'),
    ('040-toa-loot',
     ['040-toa-loot.sql'],
     False,
     'ToA loot from db-public: 2,106 LootTemplate rows in 363 templates and 364 MobXLootTemplate '
     'links, inserted only where missing.'),
    ('041-toa-loot-caps',
     ['041-toa-loot-caps.sql'],
     False,
     'Caps the by-name loot template of The Phoenix at one random item per kill.'),
    ('050-artifact-item-classtype',
     ['050-artifact-item-classtype.sql'],
     False,
     'Artifact item templates load as DOL.GS.InventoryArtifact: the 197 templates named by '
     'ArtifactXItem that have no class type.'),
]

SOURCE_SHA = re.compile(r'^-- Source sha256: ([0-9a-f]{64})$', re.M)

# gaheris 90-spells.sql is its whole expansion-class spell sweep: 1,886 rows, deleted
# by SpellID and re-inserted. Our save lacks 1,885 of them and has 15001 ("Mind
# Agony") where gaheris puts "Accursed Scourge". Only the artifact spells ship:
#   * the 68 SpellIDs that our 197 artifact item templates (ArtifactXItem ->
#     ItemTemplate SpellID, SpellID1, ProcSpellID, ProcSpellID1, PoisonSpellID)
#     reference and our Spell table lacks (dev snapshot, 2026-10-24);
#   * 5 spells those chain to that we lack as well: 25618 (OffensiveProc Value of
#     30196) and its SubSpellID 25619, 30138 (SubSpellID of the Scarab curse 30136),
#     30160 (UnyeldingProc Value of 30162), 30198 (SubSpellID of 30110).
# Spell 24 is left out: StarsProc2 (30170) reads Value as a stat amount, not a spell.
ARTIFACT_SPELL_IDS = (
    25618, 25619, 29996, 30100, 30101, 30102, 30103, 30109, 30110, 30111, 30112, 30113, 30114,
    30116, 30117, 30120, 30121, 30122, 30123, 30124, 30131, 30132, 30134, 30135, 30136, 30137,
    30138, 30139, 30142, 30143, 30144, 30145, 30147, 30148, 30149, 30151, 30152, 30154, 30160,
    30161, 30162, 30165, 30166, 30170, 30171, 30172, 30173, 30174, 30175, 30177, 30178, 30184,
    30186, 30187, 30193, 30195, 30196, 30198, 30199, 30200, 30201, 30202, 30203, 30204, 30205,
    30206, 32153, 32175, 32230, 32232, 40008, 61371, 61372,
)

SPELL_ROW = re.compile(r"^\('(?:[^']|'')*',(\d+),")


def split_value_tuples(values):
    """Split the text after VALUES into its top-level (...) tuples; quote-aware, rows may span lines."""
    tuples, depth, start, i, in_quote = [], 0, None, 0, False
    while i < len(values):
        ch = values[i]
        if in_quote:
            if ch == "'":
                if i + 1 < len(values) and values[i + 1] == "'":
                    i += 1
                else:
                    in_quote = False
        elif ch == "'":
            in_quote = True
        elif ch == '(':
            if depth == 0:
                start = i
            depth += 1
        elif ch == ')':
            depth -= 1
            if depth == 0:
                tuples.append(values[start:i + 1])
        elif ch == ';' and depth == 0:
            return tuples, values[i + 1:]
        i += 1
    sys.exit('unterminated INSERT ... VALUES')


def filter_spell_rows(text, file_name, spell_ids):
    """Keep only the given SpellIDs of a converted DELETE-by-SpellID + INSERT spell file."""
    delete_at = text.find('\nDELETE FROM "spell" WHERE "SpellID" IN (')
    insert_at = text.find('\nINSERT INTO "spell" (')
    if delete_at < 0 or insert_at < 0 or text.count('\nINSERT INTO ') != 1:
        sys.exit(f'{file_name}: expected one DELETE by SpellID and one multi-row INSERT INTO "spell".')
    values_at = text.index(' VALUES', insert_at) + len(' VALUES')
    insert_head = text[insert_at + 1:values_at]
    tuples, rest = split_value_tuples(text[values_at:])
    if rest.strip(' \n'):
        sys.exit(f'{file_name}: unexpected text after the INSERT.')
    wanted = set(spell_ids)
    kept, seen = [], set()
    for row in tuples:
        match = SPELL_ROW.match(row)
        if not match:
            sys.exit(f'{file_name}: cannot read the SpellID of row {row[:80]}')
        spell_id = int(match.group(1))
        if spell_id in wanted:
            if spell_id in seen:
                sys.exit(f'{file_name}: SpellID {spell_id} appears twice.')
            seen.add(spell_id)
            kept.append(row)
    if seen != wanted:
        sys.exit(f'{file_name}: SpellIDs not found: {sorted(wanted - seen)}')
    header = text[:delete_at + 1]
    ids = ','.join(str(i) for i in sorted(wanted))
    return (header +
            f'-- Filtered by tools/toa/build_data_packs.py: {len(kept)} of {len(tuples)} rows (ARTIFACT_SPELL_IDS).\n'
            '-- The rest of the sweep is not artifact data and does not ship. None of these\n'
            '-- SpellIDs exist in the save before this pack, so the DELETE removes nothing of ours.\n\n'
            f'DELETE FROM "spell" WHERE "SpellID" IN ({ids});\n\n'
            + insert_head + '\n' + ',\n'.join(kept) + ';\n')


ROW_FILTERS = {'90-spells.sql': lambda text, name: filter_spell_rows(text, name, ARTIFACT_SPELL_IDS)}


def last_code_line(text):
    for line in reversed(text.splitlines()):
        stripped = line.strip()
        if stripped and not stripped.startswith('--'):
            return stripped
    return ''


def pack_text(name, description, sql_dir, files):
    out = io.StringIO()
    out.write(f'-- World data pack {name}\n')
    out.write(f'-- {description}\n')
    out.write('-- Built by tools/toa/build_data_packs.py from tools/toa/sql. Do not hand-edit:\n')
    out.write('-- the server applies a pack once per save, so a fix ships as a new pack.\n')
    out.write('-- Applied by the server in one transaction (docs/TOA_DATA_PACKS.md).\n')
    sources = []
    for file_name in files:
        path = sql_dir / file_name
        if not path.is_file():
            sys.exit(f'Missing converted file {path}; run convert_mysql_to_sqlite.py first.')
        text = path.read_bytes().decode('utf-8')
        if '\r' in text:
            sys.exit(f'{path} has CR characters; converted files must be LF.')
        tail = last_code_line(text)
        if tail and not tail.endswith(';'):
            sys.exit(f'{path} does not end with a complete statement: {tail[:80]}')
        match = SOURCE_SHA.search(text)
        if file_name in ROW_FILTERS:
            text = ROW_FILTERS[file_name](text, file_name)
        sources.append({'file': file_name, 'gaherisSha256': match.group(1) if match else None})
        out.write(f'\n-- ======== part: {file_name} ========\n\n')
        out.write(text if text.endswith('\n') else text + '\n')
    return out.getvalue(), sources


def gzip_bytes(data):
    buffer = io.BytesIO()
    with gzip.GzipFile(filename='', mode='wb', fileobj=buffer, compresslevel=9, mtime=0) as gz:
        gz.write(data)
    return buffer.getvalue()


def build(sql_dir, out_dir, allow_changed=False):
    out_dir.mkdir(parents=True, exist_ok=True)
    manifest_path = out_dir / 'manifest.json'
    previous = {}
    if manifest_path.is_file():
        for entry in json.loads(manifest_path.read_text(encoding='utf-8')).get('packs', []):
            previous[entry['name']] = entry['sha256']

    entries = []
    for name, files, compress, description in PACKS:
        text, sources = pack_text(name, description, sql_dir, files)
        data = text.encode('utf-8')
        digest = hashlib.sha256(data).hexdigest()
        if name in previous and previous[name] != digest and not allow_changed:
            sys.exit(f'Pack {name} would change (sha {previous[name][:12]} -> {digest[:12]}). '
                     'Shipped packs are immutable; add a new pack or pass --allow-changed '
                     'if this pack was never released.')
        file_name = name + ('.sql.gz' if compress else '.sql')
        payload = gzip_bytes(data) if compress else data
        target = out_dir / file_name
        if not target.is_file() or target.read_bytes() != payload:
            target.write_bytes(payload)
        stale = out_dir / (name + ('.sql' if compress else '.sql.gz'))
        if stale.exists():
            stale.unlink()
        entries.append({'name': name, 'file': file_name, 'sha256': digest,
                        'description': description, 'sources': sources})
        print(f'{file_name}: {len(data):,} bytes SQL, {len(payload):,} bytes shipped, sha256 {digest[:12]}')

    manifest = {'format': 1,
                'note': 'World data packs, applied once per save at server start. See docs/TOA_DATA_PACKS.md.',
                'packs': entries}
    manifest_path.write_bytes((json.dumps(manifest, indent=2) + '\n').encode('utf-8'))
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    parser.add_argument('--sql-dir', default=str(DEFAULT_SQL))
    parser.add_argument('--out', default=str(DEFAULT_OUT))
    parser.add_argument('--allow-changed', action='store_true',
                        help='rewrite a pack whose content changed (only for never-released packs)')
    args = parser.parse_args()
    build(pathlib.Path(args.sql_dir), pathlib.Path(args.out), args.allow_changed)


if __name__ == '__main__':
    main()
