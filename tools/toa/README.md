# tools/toa: Trials of Atlantis data tooling

Converter and import probe for the gaheris SQL migrations (MySQL dialect)
into our SQLite schema. Measured results: docs/TOA_IMPORT_PROBE.md.

- `convert_mysql_to_sqlite.py`: converts one or more gaheris `sql/*.sql`
  files; output goes to `tools/toa/sql/`. Deterministic (uuid5), so re-runs
  are byte-identical.
- `probe_import.py`: applies the converted files to a COPY of the DB and
  reports statements, rows, column diffs and referential checks. It refuses
  any database under `playable/` or `db-snapshot/`.
- `test_convert_mysql_to_sqlite.py`: unit tests for the converter.
- `sql/`: converted files. `13-atlantis-mobs.sql` (11.6 MB) is not tracked;
  regenerate it from a gaheris clone:

```
git clone --depth 1 https://github.com/Pactor/gaheris.git /mnt/c/OfflineDAoC-ext/gaheris
python3 -I tools/toa/convert_mysql_to_sqlite.py /mnt/c/OfflineDAoC-ext/gaheris/sql/13-atlantis-mobs.sql
```

Nothing here writes to the live install. Loading into a playable database is
WP1.1 in docs/TOA_PLAN.md.
