# Trials of Atlantis: SQL import probe (WP0.3)

Probe only. Nothing here touches the live install or its database. Package
WP0.3 of [TOA_PLAN.md](TOA_PLAN.md); background in
[TOA_RESEARCH.md](TOA_RESEARCH.md). Measured 2026-10-24.

## Result

- **All ten gaheris migrations load into our SQLite schema.** After one
  converter rule, 385 of 385 statements succeed and no row is rejected.
- **The only schema blocker was one column.** Our `LineXSpell` has no
  `PackageID`. gaheris uses that column only as a row tag, so the converter
  drops it.
- **The real gaps are content, not SQL.** Seven NPC classes the rows name do
  not exist in our server. Six ToA dungeon regions stay empty. Seven Master
  Level ranks have no spell.

## Inputs

| Input | Detail |
|---|---|
| gaheris migrations (read only) | `/mnt/c/OfflineDAoC-ext/gaheris/sql/` files 12, 13, 14, 28, 29, 35, 36, 39, 42, 43. Each converted file records its source sha256 in its header |
| Dev DB snapshot (read only) | `playable-dev/db-snapshot/opendaoc.snapshot.db`, last mob change 2026-10-18 |
| Probe base | `.toa-probe/snapshot.clean.db`, a repaired copy of the snapshot (see below) |
| Probe DB | `.toa-probe/probe.db`, recreated from the base on every run |
| SQLite | Python sqlite3 3.53.1 in WSL |

`.toa-probe/` is excluded through `.git/info/exclude`, not `.gitignore`.

### The dev snapshot is damaged

`PRAGMA quick_check` on the snapshot itself reports a short freelist and rows
out of order in the fork table `offline_guild_grudges`. The first probe run
failed on COMMIT with "database disk image is malformed". The snapshot was
probably copied while the live server was writing.

The probe base is repaired, not edited:

1. `VACUUM INTO` the copy, which rebuilds the freelist.
2. Reinsert the 222 `offline_guild_grudges` rows in rowid order.
3. Confirm that `quick_check` returns `ok` and that every table keeps its row
   count.

No ToA table is involved. Whoever refreshes the snapshot next should check
`quick_check` on the new copy. The same damage would break any write to it.

## Per file (final run)

"Rows in" counts inserted rows. "Changed" counts rows that UPDATE or DELETE
touched.

| File | Statements ok | Rows in | Changed | Tables, before → after |
|---|---|---|---|---|
| 12-master-levels | 132 / 132 | 128 | 0 | Spell 4,152 → 4,216; LineXSpell 3,718 → 3,782 |
| 13-atlantis-mobs | 87 / 87 | 41,727 | 0 | Mob 60,505 → 102,232 |
| 14-atlantis-travel | 15 / 15 | 13 | 0 | Teleport 71 → 84 |
| 28-atlantis-portals | 1 / 1 | 0 | 58 | Mob (ClassType of portal NPCs) |
| 29-atlantis-zonepoints | 100 / 100 | 100 | 0 | ZonePoint 169 → 269 |
| 35-atlantis-npcs | 6 / 6 | 12 | 9 | MerchantItem 52,341 → 52,353; Mob |
| 36-artifact-scrolls | 5 / 5 | 162 | 178 | MerchantItem 52,353 → 52,503; ItemTemplate prices; Mob |
| 39-master-level-paths | 2 / 2 | 496 | 0 | ClassXSpecialization 396 → 892 |
| 42-master-levels-gaps | 27 / 27 | 18 | 0 | Spell 4,216 → 4,225; LineXSpell 3,782 → 3,791 |
| 43-master-levels-corrected | 10 / 10 | 9 | 17 | Spell names; LineXSpell rank moves |

The statement counts leave out the 13 MySQL session statements (`SET NAMES`,
`SET autocommit`, `COMMIT`). The converter keeps those as comments only.

Detail for files 35 and 36:

- **File 36, changed rows:** 162 scroll prices set to 300, 12 merchant rows
  from file 35 replaced, and 4 binder NPCs re-pointed.
- **File 36, merchant rows:** all 162 scroll rows resolve to an existing
  `ItemTemplate`.
- **File 35, changed rows:** three Arbiters, Aphaestia and Demyphon in region
  72, and Zosyne and Mnosus in two regions each.

## Rounds

| Round | Rule set | Failed statements | Rejected rows |
|---|---|---|---|
| 0 | none | 74: every `LineXSpell` insert in 12 (64), 42 (9) and 43 (1) | 82 |
| 1 | drop `LineXSpell.PackageID` | 0 | 0 |

The round 0 error was `table linexspell has no column named PackageID`.
`DbLineXSpell` has no such column and nothing reads it. gaheris's own DELETEs
key on `LineName` and `SpellID`, not on the tag. No round 2 was needed.

## Atlantis mobs per region

Region ids are the server's region ids from our `Regions` table. The lists
30-44, 73-87 and 130-144 in the work order are mostly zone ids. Where a
dungeon has its own region, it is listed under that dungeon below.

| Region | Before | After |
|---|---|---|
| Atlantis, Albion copy (30) | 0 | 11,146 |
| Atlantis, Midgard copy (73) | 20 | 11,185 |
| Atlantis, Hibernia copy (130) | 8 | 14,563 |
| Haven cities 70-72 | 0 | 3,945 |
| Aerus City 47 / 90 / 147 | 14 / 14 / 14 | 315 / 305 / 311 |
| Great Pyramid 45 / 88 / 145 | 0 | 9 each |
| Cetus' Pit, Sobekite Eternal, Temple of Twilight, Halls of Ma'ati, Deep Volcanus (35, 36, 37, 40, 46, 78, 79, 80, 83, 89, 135, 136, 137, 140, 146) | 0 (one mob in 80) | unchanged |
| Celestius 91 | 0 | 0 |

The ToA dungeons and Celestius stay empty. File 29 adds zone points into
them, so a player would arrive in an empty dungeon. gaheris imported only
the Atlantis overworld, the cities, Aerus and the pyramid entrance.
db-public holds about 3,051 dungeon mobs (TOA_RESEARCH.md). Filling the
dungeons is a separate import for WP1.1 or WP0.1.

The server allows 30,000 objects per region (`region_max_objects`).
Hibernia's Atlantis copy reaches 14,563 mobs. That fits, but it is the
largest region we would load.

## Referential sanity (after the final run)

| Check | Result |
|---|---|
| Mobs loaded with `PackageID='gaheris-atlantis'` | 41,727 |
| `NPCTemplateID` references (33,146 rows, 1,204 ids) missing from `NpcTemplate` | 0 |
| `EquipmentTemplateID` references (9,475 rows, 3,248 ids) missing from `NPCEquipment` | 2 rows, both Kraken (regions 30, 73), id `'0'` |
| `FactionID`, `PathID` references missing | 0, 0 |
| `ItemsListTemplateID` with no merchant rows | 1: Dockmistress Vanoran (73) carries a boat path name there |
| Brain names that do not resolve | 1: Kraken (73), Brain `' '` |
| ML `LineXSpell` rows | 73, all joining `Spell` and `SpellLine` |
| ML ranks per line | Banelord, Convoker and Stormlord 10; Battlemaster, Perfecter and Warlord 9; Sojourner and Spymaster 8 |
| ML spell types (68 distinct) without a `[SpellHandler]` in our source | 0 |
| ML spells without a line | 0 |
| ML specialisations in class careers | 496 rows, 8 for each of 62 class ids |
| ZonePoint duplicates on (Id, Realm) | 0 |
| gaheris teleports into unknown regions | 0 |
| Mob integer columns holding non-integers | 0 |
| Loaded mobs that share name and region with one of the 70 mobs already in ToA regions | 0 |

The missing ML ranks are Battlemaster 10, Perfecter 4, Sojourner 1 and 3,
Spymaster 1 and 3, and Warlord 1. gaheris's file 43 names all seven and
leaves them out because no handler exists.

### ClassType names with no class in source/server

The server falls back to a plain `GameNPC` for an unknown ClassType and logs
one error line per region (`Region.cs`, "GameNPC used instead"). The rows
still load. The NPC just does nothing on click.

| ClassType | Rows in ToA regions | Set by | Notes |
|---|---|---|---|
| `DOL.GS.Scripts.GaherisTeleporter` | 58 | file 28 | gaheris script. The portal priests were inert GameNPCs before, so nothing is lost |
| `DOL.GS.Scholar` | 26 | file 13 | our `gameobjects/Atlantis/Scholar.cs` is fully commented out. Comes back with WP3.1 |
| `DOL.GS.Scripts.Recharger` | 13 | file 13 | our class is `DOL.GS.Recharger`. These rows carry Guild `Recharger`, so the guild-script lookup already finds it. The same name is used outside Atlantis today |
| `DOL.GS.ArtifactScholar` | 5 | file 13 | commented out in our tree (WP3.1) |
| `DOL.GS.Scripts.MasterLevelsMerchant` | 3 | 13 (2) + 35 (1) | gaheris script (WP2.2) |
| `DOL.GS.Scripts.GaherisArbiter` | 3 | file 35 | gaheris script (WP2.2) |
| `DOL.GS.ArtifactCreditMerchant` | 3 | 13 (2) + 35 (1) | gaheris script (WP3.1) |

File 35 makes three NPCs worse until the gaheris scripts exist:

- **The three Arbiters** move from our existing `DOL.GS.Arbiter` to
  `GaherisArbiter`.
- **Aphaestia in region 72** moves from `GameBountyMerchant` to
  `ArtifactCreditMerchant`.
- **Demyphon in region 72** moves from `GameAtlanteanGlassMerchant` to
  `MasterLevelsMerchant`.

Without the scripts, all of them fall back to a plain GameNPC. WP1.1 should
therefore load 13, 14, 29 and 36 first. File 35 and the ClassType half of
file 28 should wait until WP2.2 and WP3.1 bring the classes.

## Remaining blockers and what each needs

| Blocker | Needs |
|---|---|
| Seven missing NPC classes (table above) | Port the gaheris scripts (WP2.2, WP3.1), or hold back 28 and 35 until then |
| Empty ToA dungeons and Celestius | A db-public import of the dungeon spawns (WP0.1, WP1.1); gaheris has none |
| Seven ML ranks without spells | Handler code first, as gaheris file 43 says (WP2.3) |
| Kraken: Brain `' '`, equipment `'0'` | Harmless: the server logs one error and uses the default brain. Clean it up in WP1.1 if wanted |
| Dockmistress Vanoran: path name in `ItemsListTemplateID` | Harmless (boat stable master, empty list). Clean it up in WP1.1 if wanted |
| `atlantis_teleport_plvl` is 4 in this snapshot (research said 2) | WP1.2 sets it to 1 |
| ML specs in all 62 class careers at level 50 | Untested here; WP2.1 checks `/train` and the career loop on a level 50 character |
| Damaged dev snapshot | Refresh the snapshot while the server is stopped, then run `quick_check` |

## Conversion rules (tools/toa/convert_mysql_to_sqlite.py)

- **Identifiers and strings.** Backtick identifiers become double-quoted
  identifiers. MySQL double-quoted strings become single-quoted strings.
  Backslash escapes are decoded and re-quoted. The inputs have none.
- **Upserts.** `ON DUPLICATE KEY UPDATE c = VALUES(c)` becomes
  `ON CONFLICT DO UPDATE SET c = excluded.c`. A clause that only assigns a
  column to itself becomes `ON CONFLICT DO NOTHING` (the 100 zone points).
  An upsert without a conflict target needs SQLite 3.35 or newer. The
  server's System.Data.SQLite 1.0.119 bundles a newer SQLite according to its
  release notes. That was not verified locally.
- **UUIDs.** `UUID()` becomes a deterministic UUID literal, a uuid5 of file
  name, statement number and occurrence. A re-run therefore produces the
  same ids. `DELETE` + `INSERT` files stay idempotent, and the zone points
  are skipped instead of duplicated. `--uuid-mode expr` emits a random
  SQLite expression instead.
- **Other rewrites.** `INSERT IGNORE` and `REPLACE INTO` become
  `INSERT OR IGNORE` and `INSERT OR REPLACE`. `TRUE`/`FALSE` become 1/0, and
  NOW, CONCAT, IF, LCASE, UCASE, GREATEST and LEAST are mapped. The inputs
  use only `UUID()` and `VALUES()`.
- **Session statements.** `SET`, `COMMIT`, `START TRANSACTION` and
  `LOCK TABLES` are kept as comments only. The caller owns the transaction.
- **Schema rules.** `DROP_COLUMN_RULES` holds one rule:
  `LineXSpell.PackageID`, added in round 1. `--rules-round N` replays a
  round.

Comments, statement order and row order are preserved. The converted files
are LF and carry a header with source hash, counts, applied rules and
warnings.

## Reproduce

From the worktree root (`/mnt/c/OfflineDAoC-wt-toa`). Writes happen only
under `.toa-probe/`. The probe refuses any database path under `playable` or
`db-snapshot`.

```bash
mkdir -p .toa-probe
grep -qx '.toa-probe/' "$(git rev-parse --git-path info/exclude)" || \
  echo '.toa-probe/' >> "$(git rev-parse --git-path info/exclude)"

# 1. Read-only copy of the dev snapshot, then repair (see "The dev snapshot is damaged").
rm -f .toa-probe/snapshot.base.db .toa-probe/snapshot.clean.db
python3 - <<'EOF'
import sqlite3
src = sqlite3.connect('file:/mnt/c/OfflineDAoC/playable-dev/db-snapshot/opendaoc.snapshot.db?mode=ro', uri=True)
dst = sqlite3.connect('.toa-probe/snapshot.base.db'); src.backup(dst); dst.close()
c = sqlite3.connect('file:.toa-probe/snapshot.base.db?mode=ro', uri=True)
c.execute("VACUUM INTO '.toa-probe/snapshot.clean.db'")
c = sqlite3.connect('.toa-probe/snapshot.clean.db', isolation_level=None)
cols = ",".join('"%s"' % r[1] for r in c.execute("PRAGMA table_info(offline_guild_grudges)"))
c.execute("BEGIN")
c.execute("CREATE TEMP TABLE g AS SELECT rowid AS rid, * FROM offline_guild_grudges")
c.execute("DELETE FROM offline_guild_grudges")
c.execute(f"INSERT INTO offline_guild_grudges(rowid,{cols}) SELECT rid,{cols} FROM g ORDER BY rid")
c.execute("COMMIT")
print(c.execute("PRAGMA quick_check").fetchone())
EOF

# 2. Convert (round 0 = no schema rules, default = all rules).
python3 tools/toa/convert_mysql_to_sqlite.py --src /mnt/c/OfflineDAoC-ext/gaheris/sql \
  --out .toa-probe/sql-round0 --rules-round 0
python3 tools/toa/convert_mysql_to_sqlite.py --src /mnt/c/OfflineDAoC-ext/gaheris/sql \
  --out tools/toa/sql

# 3. Probe (about 2.5 minutes on /mnt/c).
python3 tools/toa/probe_import.py --fresh-from .toa-probe/snapshot.clean.db \
  --db .toa-probe/probe.db --sql-dir .toa-probe/sql-round0 --json .toa-probe/report-round0.json
python3 tools/toa/probe_import.py --fresh-from .toa-probe/snapshot.clean.db \
  --db .toa-probe/probe.db --sql-dir tools/toa/sql --json .toa-probe/report-final.json

# 4. Unit tests.
python3 -m unittest discover -s tools/toa -p 'test_*.py'
```

## Not checked

- Server start on the probe DB. No server was started, so region load time,
  memory and the startup table rebuild with 41,727 more mobs are unmeasured.
- In-game behaviour of any row: travel, portals, merchants, ML training.
- The SQLite version that System.Data.SQLite 1.0.119 actually loads at
  runtime.
- Whether db-public placements are sensible in game (heights, underwater
  spawns, pathing). Bots are out of scope in Atlantis; players will notice.
