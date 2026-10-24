# World data packs

World data packs bring new world data into every existing save. A pack is a
SQLite script shipped with the server build. The server applies each pack
once per save, at startup, before it reads any world data. The first packs
carry the Trials of Atlantis data from gaheris (packages WP1.1a and WP2.1 in
[TOA_PLAN.md](TOA_PLAN.md)).

Status: implemented in source, real-client check pending. No server has
booted with the packs yet.

## Shipped packs

The row counts were measured on a repaired copy of the dev snapshot. The
`ShippedPacks_ApplyOnTheDevSnapshot` test produced them.

| Pack | Content (gaheris file) | Rows |
|---|---|---|
| `010-toa-ml-spells` | Master Level spells and their eight spell lines (12, 42, 43): 73 of 80 ranks | 172 |
| `011-toa-ml-paths` | The eight ML paths in every class career at level 50 (39) | 496 |
| `012-toa-artifact-spells` | 73 artifact spells, filtered from gaheris 90 (see below) | 73 |
| `013-toa-artifact-spells-missing` | The 4 artifact spells that exist nowhere, hand-written from the live delve texts (WP3.0b, see below) | 4 |
| `020-atlantis-mobs` | 41,727 Atlantis mobs, tagged `PackageID = 'gaheris-atlantis'` (13). Gzipped | 41,727 |
| `021-atlantis-travel` | 13 Atlantis teleport destinations of type `gaheris` (14) | 13 |
| `022-atlantis-zonepoints` | 100 Atlantis zone points. Existing ones are kept (29) | 100 |
| `023-artifact-scroll-binders` | 162 scroll merchant rows, scroll prices, Zosyne and Mnosus (36) | 328 |
| `024-toa-arbiter-npcs` | The three Arbiters become `DOL.GS.MasterLevelArbiter`; Demyphon in region 72 becomes a Master Level credit merchant (subset of 35) | 4 |
| `025-toa-atlantis-arena-fix` | Deletes the three Atlas fight-club Mordreds (`DOL.GS.LordOfBattle`) in regions 73 and 80 | 3 |
| `026-toa-travel-realms` | Atlantis travel per realm: 33 Teleport rows of type `atlantis`, 10 haven porters (`PackageID = 'toa-travel'`), 3 Oceanus channelers become porters | 46 |
| `027-toa-open-atlantis` | `atlantis_teleport_plvl` 4 → 1 and `rog_toa_item_chance` 0 → 25, only where the setup values stand | 2 |
| `030-toa-dungeon-mobs` | 2,135 ToA dungeon mobs from db-public, tagged `PackageID = 'dbpublic-toa-dungeons'` | 2,135 |
| `031-toa-dungeon-templates` | 119 NPC templates and 827 equipment rows of those mobs, only where missing | 0 |
| `040-toa-loot` | 2,106 ToA loot rows in 363 templates and 364 loot links from db-public, only where missing | 2,106 |
| `041-toa-loot-caps` | One loot link that caps The Phoenix at one random item per kill | 1 |
| `050-artifact-item-classtype` | The 197 artifact item templates load as `DOL.GS.InventoryArtifact` | 197 |
| `051-artifact-scroll-wiring` | The 54 scroll sets on the Artifact rows (gaheris 37): real page names, readable pair and book names; empty BookID for the three artifacts without a set | 57 |
| `052-artifact-version-fixes` | Gem of Lost Memories points at its archer template (and that loads as an artifact), the studded Maddening Scalars move to their artifact, two version rows without a template are dropped | 5 |
| `061-toa-scholar-classes` | Aphaestia in region 72 becomes the credit merchant; 22 `Artifact.Credit` values match their token; five tokens of absent artifacts leave her list | 28 |
| `070-toa-encounter-targets` | Encounter credit from kills: 61 `ArtifactEncounterTarget` rows, at least one creature for each of the 57 artifacts, group or battlegroup credit, the zone each creature stands in (WP3.3, hand-written) | 61 |
| `071-toa-encounter-spawns` | The six encounter creatures no source spawns (Battler, Snatcher, Linos, Sobekhotep, Malamis, a wavelord), level 65, one per realm copy, `PackageID = 'toa-encounters'` (WP3.3, hand-written) | 18 |

"Rows" counts inserted, updated and deleted rows together. Packs 030 to 041
are described in [TOA_DUNGEON_IMPORT.md](TOA_DUNGEON_IMPORT.md), pack 050 in
[TOA_ARTIFACTS.md](TOA_ARTIFACTS.md). 031 inserts nothing on the dev snapshot
because every template is already there; on an empty server schema it
inserts 946 rows, and 040 inserts 2,470.

Packs 026 and 027 are described in [TOA_TRAVEL.md](TOA_TRAVEL.md).
Packs 051, 052 and 061 are described in [TOA_ARTIFACTS.md](TOA_ARTIFACTS.md)
("Earning an artifact"), packs 070 and 071 there in "Encounter credit" (with
the whole artifact to creature table). The table `ArtifactEncounterTarget` is
new: the server creates it from `DbArtifactEncounterTarget` at boot, before
the packs run, so pack 070 only inserts. Both packs insert with fixed row IDs
(`INSERT OR IGNORE`); a second run changes nothing. Their counts were measured
on a repaired copy of the dev snapshot that already carried packs 010-012,
020-025, 030, 031, 040, 041 and 050 (the test counts deltas only for the packs
still pending), and on an empty server schema: 61 and 18 rows in both. Every
creature pack 070 names stands in all three realm copies (30, 73, 130). Their row counts were measured on a copy of the live
save after the 16 earlier packs (2026-10-24); a second run of each changes
nothing. 051 is generated by `tools/toa/build_artifact_scroll_pack.py` from
gaheris `sql/37-scroll-wiring.sql`; it takes only the page names, not gaheris'
`artifact_scroll` template or the Object_Type change of every scroll template.

No scroll-drop pack ships: pack 040 already carries the db-public scroll drops
(573 loot rows with scroll items, chances 5 to 15 %). Every page of 53 of the
54 sets drops in all three realm copies of Atlantis; "Ring of Fire, Scroll 3 of
3" drops nowhere and comes from the binders of pack 023, which stock every set.

After all packs the dev snapshot has 43,887 more mobs: 41,727 from 020,
2,135 from 030, the ten porters of 026 and the 18 encounter creatures of 071,
minus the three Mordreds of 025.

File 28 is not shipped: it points the portal NPCs at the gaheris portal
teleporter, which this server lacks. WP1.2 uses pack 026 and our own porter
class instead; the 13 gaheris travel rows of pack 021 stay unused
([TOA_TRAVEL.md](TOA_TRAVEL.md) says why). File 35 ships only in part, as
pack 024 and, for Aphaestia in region 72, pack 061. Its section for Zosyne
and Mnosus is left out: pack 023 already makes them scroll merchants.

### NPC classes after the packs

The Master Level NPCs work. The three Arbiters (regions 70, 71, 72) are
`DOL.GS.MasterLevelArbiter` and enrol on click. Demyphon is a
`DOL.GS.Scripts.MasterLevelsMerchant` with the list "Master Level Credits"
in all three regions. Both classes came with WP2.2; pack 024 points the rows
of region 72 and the Arbiters at them.

The 33 artifact NPCs of pack 020 work since WP3.1b; their rows already named
the classes, which now exist in GameServer:

| NPC | ClassType | Count | Regions |
|---|---|---|---|
| Aphaestia | `DOL.GS.ArtifactCreditMerchant` | 2 (+1 by pack 061) | 70, 71 (72) |
| Scholars (Ashton, Ailigean, Daelin, Brivela, Loremaster Sverrir) | `DOL.GS.Scholar` | 26 | 70, 71, 73 |
| Artifact Scholars (Jarron, Heiron, Alaria, Erelacen, Elmer) | `DOL.GS.ArtifactScholar` | 5 | 70, 71, 72 |

Before WP3.1b they loaded as plain `GameNPC` and each region logged "Error
loading the following NPC ClassType(s), GameNPC used instead"; on a server
with the WP3.1b build that line no longer names them. The 13 rows of
`DOL.GS.Scripts.Recharger` are not affected: their guild `Recharger`
resolves through the NPC guild script.

The 7 missing ML ranks are listed in
[TOA_IMPORT_PROBE.md](TOA_IMPORT_PROBE.md): Battlemaster 10, Perfecter 4,
Sojourner 1 and 3, Spymaster 1 and 3, and Warlord 1.

## Artifact spells (pack 012)

Our 197 artifact item templates reference 85 distinct spell IDs. The save
lacks 73 of them. Item templates are found through `ArtifactXItem`, and the
spell columns are `SpellID`, `SpellID1`, `ProcSpellID`, `ProcSpellID1` and
`PoisonSpellID`.

gaheris `sql/90-spells.sql` is gaheris's whole expansion-class spell sweep.
It holds 1,886 rows, which it deletes by SpellID and inserts again. Our save
lacks 1,885 of them. For the one it has, 15001 "Mind Agony", gaheris would
put "Accursed Scourge" in its place. Nothing in our data uses 15001.

Pack 012 therefore ships only 73 rows. `ARTIFACT_SPELL_IDS` in
`tools/toa/build_data_packs.py` lists them:

- **68 rows** are the missing IDs that our artifact items reference and
  gaheris has.
- **5 rows** are spells those 68 chain to that we lack as well:
  - 25618, the OffensiveProc `Value` of 30196, and its `SubSpellID` 25619;
  - 30138, the `SubSpellID` of the Scarab curse 30136;
  - 30160, the UnyeldingProc `Value` of 30162;
  - 30198, the `SubSpellID` of 30110.

Spell 24 is left out on purpose. StarsProc2 (30170) reads its `Value` of 24
as a stat amount, not as a spell ID.

Four artifact spell IDs exist nowhere: not in gaheris, not in our save, not
in DOL or OpenDAoC. (gaheris' class sweep, file 46, uses 13135 for the
Vampiir spell "Wither Defense" in the line Dementia, which we do not ship.)
Pack 013 (WP3.0b) writes them from the live delve texts in the Camelot Herald
item database and patch notes. Each row is inserted only where its SpellID is
missing. After the packs, all 85 artifact spell IDs resolve.

| SpellID | Used by | Pack 013 row |
|---|---|---|
| 13135 | Scepter of the Meritious (crush and hammer), second proc | "Stat Drain", StrengthConstitutionDrain 25, 1000 range, 60 s: the Scepter's live proc, same as 30114 |
| 30115 | Spear of Kings (17 templates) and Scepter of the Meritious (blunt), use spell | "Boon of Kings", AFHitsBuff -5 (armor and health +5 %), realm aura radius 1500, 5 s pulsing every 4.5 s: the Spear's live version |
| 30125 | Shield of Khaos, use spell | "Disoriented", HarpyFeatherCloak 10 (melee to-hit -10 %), 1500 range, radius 700, 60 s, body; SubSpellID 30126 (the energy DoT live casts with it; the handler casts it, the core would not; 30126 has range 0, so it lands point-blank around the wearer) |
| 32223 | Malice's Axe (13 templates) and Scepter of the Meritious (crush and hammer), first proc | "Vision of Malice", AllStatsPercentDebuff 25, 350 range, 60 s, body: the ToA-era value; patch 1.87 (2007) cut it to 10 % for 30 s |

Data quirks around these rows, not changed (item templates are not pack 013's
business):

- The live Scepter of the Meritorious has one proc (Stat Drain) and one charge
  (Boon of Kings, self, +4 %, 10 min). Our crush and hammer templates instead
  name Malice's proc 32223 (level 6) and 13135 as `ProcSpellID1`, which has no
  `ArtifactBonus` row and so is active from artifact level 0; they have no use
  spell. The blunt template uses 30114 and 30115 like live, but 30115 is the
  Spear's realm aura.
- Shield of Khaos names the energy DoT 30126 as `ProcSpellID1` as well (no
  `ArtifactBonus` row, active from level 0); live it came only with the charge.

A fifth ID, 7206, is the Erinys Charm's use spell. It resolves through pack
010: 7206 is the Perfecter ML 2 spell "Purify Vision" (CureNearsight, group).
Whether the charm should cast an ML spell is unverified.

These data quirks are noted here and not fixed:

- **30205 "Great Shade Form."** (TraitorsDaggerProc) has `Value` 200. Read
  as a spell ID, it would proc spell 200.
- **30155 "Lore Debuff"** has the type RealmLore.
- **30136 "Curse of the Scarab"** (ScarabProc) has `Value` 0. Its handler
  applies the speed decrease only when `Value` is above 0.
- **30131** chains to 49821, **30154** (CrocodileTearsProc) to `Value` 5, and
  **30152** (ZahurEnduProc) to `Value` 10. All three point at spells or
  values that already exist in our save, so the meaning is unverified.
- **Seven of the 52 spell types in pack 012 had no `[SpellHandler]`** on
  main 0.316.0, so their rows loaded but did nothing. WP3.0b wrote all seven
  (behaviour and sources in
  [TOA_ARTIFACT_SPELLS.md](TOA_ARTIFACT_SPELLS.md), "WP3.0b"); implemented in
  source, real-client check pending.

  | Type | Spell | Artifact |
  |---|---|---|
  | CrocodileTearsProc | 30154 | Crocodile Tear Ring |
  | PowerHealthEnduranceRegenBuff | 30184 | Kalare's Necklace |
  | UnyeldingProc | 30162 | Ring of Unyielding Will |
  | YouthIllness | 30187 | Gem of Lost Memories |
  | ZahurAura | 30151 | Cloudsong, Crown of Zahur |
  | ZahurEnduProc | 30152 | Crown of Zahur |
  | ZoAura | 30165 | Bracer of Zo'arkat, Phoebus' Harp |

  The brains name PowerHealthEnduranceRegenBuff when they choose buffs; it
  now has a handler. The only artifact spell type still without one is
  `EternalPlant` (30181, Eternal Plant).

## How a save gets them

1. The build copies `source/server/CoreServer/data-packs/` next to
   `CoreServer.dll`. `tools/dev/deploy.sh` copies the pack files to
   `runtime\server\data-packs\`. It adds new files and replaces changed
   ones. The fork update pack carries the same files.
2. At startup the component "World data packs" runs. It comes after the
   server properties, the world-model check and the database updaters. It
   comes before the world clock, NPC templates, spells and regions load. It
   applies only to SQLite saves. A MySQL server skips it with one log line.
3. The table `offline_data_packs` records each pack with its name, sha256,
   applied time in UTC and row count. A recorded pack never runs again for
   that save. That holds even if the shipped file later changes; the server
   then logs a warning and keeps the old state.
4. Pending packs run in name order. Each pack runs in one transaction,
   together with its marker row. A pack is therefore either fully applied
   and recorded, or not applied at all.
5. When a pack fails, the server rolls it back and logs the failing
   statement. It then goes on with the other packs and keeps booting. The
   failure is recorded in `offline_data_pack_failures` with the pack's name,
   sha256, time and message. Later starts skip that pack with one warning
   and take no backup for it. A failed pack is retried in two cases: a GM
   runs `/datapacks apply <name>`, or a new pack file with another sha256
   ships. A successful apply removes the failure row.
6. The server refuses a pack in these cases, without touching the save:
   - its file is missing;
   - its sha256 differs from the manifest;
   - it contains a forbidden statement: `BEGIN`, `COMMIT`, `END`,
     `ROLLBACK`, `SAVEPOINT`, `RELEASE`, `ATTACH`, `DETACH`, `VACUUM`,
     `PRAGMA`, `REINDEX`, `CREATE TRIGGER`, or anything that names one of
     the two marker tables.

   A pack file that `manifest.json` does not list is ignored, with a warning.

7. Before anything else, the server reads the save's journal mode. With
   `Journal Mode=Off` or `Memory`, a failed pack could not be rolled back
   safely and a crash could corrupt the save. The server's built-in default
   and the setup tool's connection string use `Journal Mode=Off`; Aaron's
   live install uses WAL. For such a save the data pack connections switch
   themselves to `TRUNCATE` for that run and log one INFO line saying so.
   Each of them checks that SQLite really took the mode. Then the normal
   backup and per-pack transactions follow, so a failed pack rolls back
   fully.

   This leaves the server's own connections alone. For a database that is
   not in WAL mode, the journal mode belongs to the connection, not to the
   file. A connection that sets `TRUNCATE` and reopens comes back as
   `DELETE` (sqlite.org/wal.html, "Persistence of WAL mode"). If the server
   dies in the middle of a pack, the `-journal` file is hot. The next
   connection rolls it back before reading, whatever its own journal mode
   (sqlite.org/lockingv3.html, section 4.1). A unit test checks this with a
   `Journal Mode=Off` reader. WAL is never used for the switch: it would
   persist in the file and change the server's connections too. After a run
   an empty `<save>-journal` file may stay next to the save; it is harmless.

   The switch can fail: SQLite keeps `OFF`/`MEMORY`, or a rolled-back test
   write fails because the save file is read-only or no journal can be
   created next to it. Then the server applies no pack and logs one warning
   at that start, as before.

A launcher world reset creates a new database. That database has no marker
table yet, so the next start applies all packs again.

Some tables the server pre-caches at startup, before the packs run. These
are `ItemTemplate`, `MobXAmbientBehaviour` and `Minotaurrelic`. The applier
records which of their rows a pack changed, and the server reloads exactly
those rows into the cache. The scroll prices of pack 023 and the artifact
class type of pack 050 are therefore live on the first boot (359 item
templates on the dev snapshot). The server properties are read before the
packs as well. When a pack changes a `ServerProperty` row, the server reloads
all properties after the packs (pack 027, 2 rows on the dev snapshot). All
other tables are read after the packs, so nothing else needs a reload.

## Backup and rollback

Before the first pack of a start, the server copies the whole database next
to itself. It uses SQLite's online backup API, which is consistent in WAL
mode:

```
runtime\data\opendaoc.sqlite3.db.bak-datapacks-YYYYMMDD-HHMMSS
```

The timestamp is local time. A start with nothing pending makes no backup;
neither does a start where every pending pack has already failed with the
same file. The server log names the file ("database backup before
applying"). Only the newest three `.bak-datapacks-` files of the save are
kept. Each new backup deletes the older ones, and the log names every file
it deleted. A backup that fails part-way is deleted, and no pack is applied
without a backup.

To return to the state before the packs:

1. Stop the server and the launcher.
2. Delete `opendaoc.sqlite3.db-wal` and `opendaoc.sqlite3.db-shm`. A WAL file
   left behind would be replayed onto the restored copy and damage it.
3. Copy the backup over `opendaoc.sqlite3.db`.
4. Set `world_data_packs_enabled` to false, or the next start applies the
   packs again.

Undoing one pack without a restore is possible for packs 020, 030, 070 and 071:

```sql
DELETE FROM Mob WHERE PackageID = 'gaheris-atlantis';
DELETE FROM offline_data_packs WHERE Name = '020-atlantis-mobs';  -- only if it should apply again
DELETE FROM Mob WHERE PackageID = 'dbpublic-toa-dungeons';
DELETE FROM offline_data_packs WHERE Name = '030-toa-dungeon-mobs';  -- only if it should apply again
DELETE FROM Mob WHERE PackageID = 'toa-encounters';                   -- pack 071
DELETE FROM ArtifactEncounterTarget WHERE ArtifactEncounterTarget_ID LIKE 'toa-enc-%';  -- pack 070
```

Pack 025 deletes rows; only the backup brings the Mordreds back.

## Controls

- **Server property `world_data_packs_enabled`** (category `world`,
  default true). When false, startup applies nothing.
- **`/datapacks list`** (GM) shows every shipped pack and its state for this
  save: Applied, Pending, AppliedDifferentHash, MissingFile, or Failed with
  the recorded message. It also says whether each file is present. It reads
  only the manifest and the two marker tables. It does not decompress or
  hash pending pack files, so a changed file is detected only at apply
  time.
- **`/datapacks apply <name>`** (GM) applies one pack while the server runs,
  or retries one that failed. It refuses while any other client is online;
  pending packs apply at the next start anyway. It works in the background
  and takes a backup first. The running world does not reload, so restart
  the server to see the data. Use it for diagnostics. A pack applied at
  startup is the normal path.

## Master Levels and /train

Pack 011 adds the eight ML specialisations to every class career at level
50. A level 50 player without Master Levels sees none of them:

- `GamePlayer.LoadClassSpecializations` adds an ML specialisation only when
  `MLGranted` is true and `MLLevel` is at least 1. It also adds only the
  path at index `MLLine`.
- `LiveMasterLevelsSpecialization` is a career specialisation and not
  trainable. `/train`, the trainer window and the spec-point count list
  trainable specialisations only.
- World bots and companions skip ML specialisations
  (`GameBot.LoadClassSpecializations`).

No guard was needed. In the dev snapshot, no character has `MLGranted` or an
ML level. The Arbiter (WP2.2) will be the only way to grant Master Levels.

## Adding a pack

1. Write or convert the SQL in SQLite dialect. The runtime SQLite is 3.46.1
   (System.Data.SQLite 1.0.119), so upserts without a conflict target work.
   For gaheris files use `tools/toa/convert_mysql_to_sqlite.py` (see
   [tools/toa/README.md](../tools/toa/README.md)).
2. Add the pack to `PACKS` in `tools/toa/build_data_packs.py`. Give it a new
   three-digit number, a name, its source files and a description. Gzip
   anything over about 1 MB.
3. Run `python3 -I tools/toa/build_data_packs.py`. It writes the pack and
   `manifest.json` to `source/server/CoreServer/data-packs/`.
4. Run the server tests; `UT_WorldDataPacks` applies every shipped pack to
   an empty server schema. Then run them against the dev snapshot: put a
   repaired copy at `.toa-probe/snapshot.clean.db` in the checkout, or set
   `OFFLINE_DAOC_TOA_SNAPSHOT`. [TOA_IMPORT_PROBE.md](TOA_IMPORT_PROBE.md)
   describes the repair.

Rules for pack content:

- **A shipped pack is immutable.** Saves that already applied it never see
  an edit. Ship a fix as a new pack with a higher number. The builder
  refuses to change an existing pack unless `--allow-changed` is passed.
  That flag is only for a pack that was never released.
- **Make a pack idempotent where possible**: `DELETE` by tag, then
  `INSERT`, or use `ON CONFLICT`. A pack that failed is retried on every
  start.
- **Touch world data only.** Characters, inventories, coins, guilds and
  companions are save data and do not belong in a pack.

## Not checked

- No server has started with the packs. The boot time with 43,859 more mobs,
  the memory use, and the load of Hibernia's Atlantis copy are unmeasured.
  Hibernia's copy grows to 14,563 mobs; the cap `region_max_objects` is
  30,000.
- Nothing has been checked in the game: travel, zone points, scroll
  merchants, ML spells, the Arbiter and Demyphon, the dungeon spawns, loot,
  artifacts, the scholars and Aphaestia, encounter credit and the six
  encounter spawns of 071.
- The `/datapacks` command was not run. The applier behind it is unit
  tested on temporary databases.
