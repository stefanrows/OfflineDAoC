# tools/toa: Trials of Atlantis data tooling

Converter and import probe for the gaheris SQL migrations (MySQL dialect)
into our SQLite schema, and the db-public dungeon/loot pack generator.
Measured results: docs/TOA_IMPORT_PROBE.md, docs/TOA_DUNGEON_IMPORT.md.

- `convert_mysql_to_sqlite.py`: converts one or more gaheris `sql/*.sql`
  files; output goes to `tools/toa/sql/`. Deterministic (uuid5), so re-runs
  are byte-identical.
- `probe_import.py`: applies the converted files to a COPY of the DB and
  reports statements, rows, column diffs and referential checks. It refuses
  any database under `playable/` or `db-snapshot/`.
- `test_convert_mysql_to_sqlite.py`: unit tests for the converter.
- `sql/`: converted files. `13-atlantis-mobs.sql` (13 MB) is not tracked
  (the shipped pack 020 carries it gzipped); regenerate it from a gaheris clone:
- `build_dbpublic_packs.py`: builds `sql/030-toa-dungeon-mobs.sql`,
  `031-toa-dungeon-templates.sql` and `040-toa-loot.sql` from the
  Eve-of-Darkness/db-public JSON (ToA dungeon spawns gaheris lacks, and the
  ToA loot rows). Guarded inserts, re-application changes nothing. Numbers and
  commands: docs/TOA_DUNGEON_IMPORT.md. Tests: `test_build_dbpublic_packs.py`.
- `sql/`: converted files. `13-atlantis-mobs.sql` (11.6 MB) is not tracked;
  regenerate it from a gaheris clone:

```
git clone --depth 1 https://github.com/Pactor/gaheris.git /mnt/c/OfflineDAoC-ext/gaheris
# Converts all files; the tracked ones come out byte-identical, and
# conversion-report.json keeps every file (a single-file run would shrink it).
python3 -I tools/toa/convert_mysql_to_sqlite.py --src /mnt/c/OfflineDAoC-ext/gaheris/sql --out tools/toa/sql
```

- `build_data_packs.py`: assembles the shipped world data packs from `sql/`
  into `source/server/CoreServer/data-packs/` and writes `manifest.json`
  (docs/TOA_DATA_PACKS.md). Pack 020 is the gzipped file 13; pack 012 keeps
  only the 73 artifact rows of file 90 (`ARTIFACT_SPELL_IDS`). Packs 030, 031
  and 040 are the db-public files of the same names. Shipped packs are
  immutable; the builder refuses to change one.
- Hand-written pack sources in `sql/` (edit them only before their pack was
  ever released): `013-toa-artifact-spells-missing.sql` (the four artifact
  spells that exist nowhere, docs/TOA_ARTIFACT_SPELLS.md "WP3.0b"),
  `024-toa-arbiter-npcs.sql` (the Arbiter and Demyphon part of
  gaheris 35), `025-toa-atlantis-arena-fix.sql` (removes the three Atlantis
  fight-club Mordreds), `026-toa-travel-realms.sql` (Atlantis travel rows and
  haven porters, docs/TOA_TRAVEL.md), `027-toa-open-atlantis.sql` (opens
  Atlantis, ToA loot bonus chance), `041-toa-loot-caps.sql` (The Phoenix's loot cap) and
  `050-artifact-item-classtype.sql` (artifact templates load as
  `InventoryArtifact`), `070-toa-encounter-targets.sql` (artifact encounter
  credit targets) and `071-toa-encounter-spawns.sql` (the six missing
  encounter creatures; both docs/TOA_ARTIFACTS.md "Encounter credit").
- `test_build_data_packs.py`: unit tests for the pack builder.

Pack build step, after converting (13 must be regenerated first, see above):

```
python3 -I tools/toa/build_data_packs.py
python3 -I -m unittest discover -s tools/toa -p 'test_*.py'
```

Nothing here writes to the live install. The server applies the packs to a
save at its next start (docs/TOA_DATA_PACKS.md).
