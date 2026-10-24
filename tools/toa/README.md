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
  only the 73 artifact rows of file 90 (`ARTIFACT_SPELL_IDS`). Shipped packs
  are immutable; the builder refuses to change one.
- `test_build_data_packs.py`: unit tests for the pack builder.

Pack build step, after converting (13 must be regenerated first, see above):

```
python3 -I tools/toa/build_data_packs.py
python3 -I -m unittest discover -s tools/toa -p 'test_*.py'
```

Nothing here writes to the live install. The server applies the packs to a
save at its next start (docs/TOA_DATA_PACKS.md).
