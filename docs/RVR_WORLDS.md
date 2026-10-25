# Worlds: files, reset and backups

Package R5a of [RVR_MODE_PLAN.md](RVR_MODE_PLAN.md); spec
`docs/superpowers/specs/2026-10-24-rvr-mode-design.md` sections 2, 5.1 and 7.
The fork has two worlds. Each lives in its own save with its own side files.
One reset routine creates either world fresh.

| World | Marker in `offline_local_options.WorldModel` | Save |
|---|---|---|
| Realm vs Realm (default) | `Realm-1` | `runtime/data/opendaoc.sqlite3.db` |
| PvP world (Camlann) | `Camlann-1` | `runtime/data/opendaoc-camlann.sqlite3.db` |

Both worlds run GameType PvP. The marker inside the save decides the rules.
The server refuses a save without one of the two markers.

## Files per world

The server names every per-save side file after the save it opens, so each
save has its own set. The RvR save keeps the plain names. The Camlann save
adds `-camlann` before the first dot. One shared class,
`source/server/GameServer/world/WorldSaveLayout.cs`, holds the names for the
server and the launcher.

| File in `runtime/server` | RvR world | Camlann world | Kind |
|---|---|---|---|
| Realm event history | `realm-event-records.sqlite3` | `realm-event-records-camlann.sqlite3` | state |
| Open BG live status | `open-bg-status.json` | `open-bg-status-camlann.json` | state |
| Bot dashboard snapshot | `bot-world.json` | `bot-world-camlann.json` | state |
| RvR dashboard snapshot | `rvr-world.json` | `rvr-world-camlann.json` | state |
| Gothwaite switch | `gothwaite-pvp.json` | `gothwaite-pvp-camlann.json` | setting |

- **Keyed by the save file, not by the marker.** The server reads the
  Gothwaite switch before it checks the marker. A save that still sits at the
  default path, such as today's Camlann save, keeps the side files it has.
- **Shared between the worlds:** `bot-goals.json` (population settings),
  `population-benchmarks.json`, `serverconfig.xml`, and the live request and
  status files of world speed, population mix, realm events and the bot
  dashboard request. Only one server runs per installation, and each of those
  protocols carries a session id.
- **Data pack backups** (`<save>.bak-datapacks-<time>`) already carry the save
  name, so they stay apart per world.

## How the server and the launcher pick the world

1. The launcher writes the chosen world's save into the `Data Source` of
   `DBConnectionString` in `runtime/server/config/serverconfig.xml`.
   `CamlannServerConfig.EnsureWorld` does it and pins GameType to PvP. Only
   the file name changes; the folder, the options and the line endings stay.
2. The server opens that save. `WorldSaveFiles` names its side files after it.
3. At startup the server reads the marker. `Realm-1` and `Camlann-1` start;
   anything else is refused. The world data packs apply to whichever save
   boots, once per save.

Until R5b, the launcher always selects the default save
`opendaoc.sqlite3.db`. Its side-file paths already come from the same layout
class, so R5b only has to switch the save.

## Reset steps

`WorldReset.CreateFresh` in `source/tools/OfflineDaoc.Launcher/WorldReset.cs`
runs one path for both worlds:

1. **Checks.** The server must be fully stopped. The launcher checks this
   before the copy and again before any file moves. The seed must exist and
   carry a known marker, `Normal`, or none. Build files an earlier run left
   behind (older than ten minutes) are swept from `runtime/data`.
2. **Seed.** The seed is the world's own save. A world without a save is built
   from the other world's save, which is only read. The seed brings the world
   data, the item templates, the spawns and the applied data packs.
3. **Build.** The SQLite backup API copies the seed to
   `runtime/data/.<save>.building-<id>`. One transaction then:
   - clears characters, inventories, bots, companions, guilds except the
     dummy guild, accounts except the local one, Realm Exchange, keep claims,
     capture logs, keep hook-point items, the fork's bot guild charters, crew
     mappings, Gothwaite homes, guild grudges and runtime status;
   - clears the battleground campaign camps' sponsors, tokens and timers (the
     camp rows stay);
   - sets the keeps' start state (below) and puts the six relics home;
   - writes the marker, `WorldResetUtc` and `MakeMeGM=false`, and sets the
     local account to privilege level 1, a normal player;
   - removes the simulation clock, so the new world starts its own clock;
   - writes today's properties (below);
   - creates the roster, 600 bots per realm by default, and points the
     population at it.
4. **Integrity.** `PRAGMA quick_check` must report `ok`.
5. **Backup.** The replaced save, its `-wal`, `-shm` and `-journal`, and every
   per-save side file move to `<install>-backups/<yyyyMMdd-HHmmss>-<rvr|camlann>-world-reset/`.
   For the owner's install that is `C:\OfflineDAoC\playable-backups`. Nothing
   is deleted.
6. **Swap.** The build file moves into place. A fresh Camlann world gets a
   copy of its old Gothwaite switch back.

If anything fails before step 6, every old file stays where it was and the
build file is removed. If a move fails, the files already moved go back. The
error names the backup folder, and lists every file that could not be put
back so it can be moved by hand.

## What a fresh world writes

| Item | RvR world | Camlann world |
|---|---|---|
| Marker | `Realm-1` | `Camlann-1` |
| Ordinary keeps and towers | Realm 0, no guild; the server hands them to the Frontier Wardens | Same |
| Portal keeps (base level 100 or more) | Original realm | Realm 0, as today |
| Relic keeps (skin 99) | Original realm | Realm 0, as today |
| Relics | Home pads, realm = original realm | Same |
| Bot roster | 600 per realm, level shape from `bot-goals.json` | 600 per realm |
| Bot guilds | None written. At first start the crew manager seeds 15 per realm, each with one realm | None written. At first start the crew manager seeds 15 mixed guilds, as today |
| Gothwaite switch | None: the mode stays off | The old Camlann switch, if one existed |

On the dev snapshot the RvR reset gave 51 keeps with a realm: 45 portal
keeps and 6 relic keeps.

Properties written in both worlds. An existing row keeps its category and
description; only the value changes.

| Property | Value |
|---|---|
| `xp_rate`, `bot_xp_rate` | 10 |
| `rp_rate`, `bot_rp_rate` | 50, 15 |
| `ml_xp_per_level` | 10 |
| `artifact_xp_rate` | 350 |
| `toa_loot_in_atlantis` | True |
| `open_bg_enabled`, `open_bg_mode` | True, `perrealm` |
| `open_bg_groups8`, `open_bg_groups_small`, `open_bg_solos` | 12, 8, 22 |
| `open_bg_max_bots` | 450 |
| `open_bg_band_min`, `open_bg_band_max` | 50, 50 |
| `open_bg_cycle_min_minutes`, `open_bg_cycle_max_minutes` | 15, 45 |
| `open_bg_player_drift` | True |
| `open_bg_presets` | `std8:1,caster8:1,raid8:8` |
| `open_bg_units_*`, `open_bg_landing_peace` | empty |
| `population_enabled`, `active_target`, `hard_active_cap` | true, roster size, 0 |

## Other launcher pieces

- **Start check.** The launcher starts a save with either marker. A save with
  `Normal` still gets the one-time in-place Camlann conversion
  (`CamlannWorldReset.Apply`), as before.
- **Gothwaite switch.** In a `Realm-1` save the population tab locks the world
  mode on Normal and refuses to save Gothwaite PvP (owner decision 18).
- **"Reset all keeps & relics".** In a `Realm-1` save the button returns portal
  and relic keeps to their original realm; every other keep goes neutral, as
  in Camlann.
- **Deploy and release.** `tools/dev/OfflineDAoC.Deploy.psm1` protects the
  Camlann save, its `-wal` and `-shm`, and `rvr-world-camlann.json` like the
  RvR files. The release builder refuses to package `rvr-world-camlann.json`.

## Bot guilds per world

`AutonomousGuildSeeding` is the world rule of the crew manager
(`AutonomousCrewManager.Reconcile`):

- **Camlann:** one pool over the whole roster. Up to five 1:2:4 size triplets
  make 15 mixed guilds, exactly as before.
- **RvR:** one pool per realm with the same size rule. About 600 bots per
  realm make 15 guilds per realm, 45 in total. Each guild is created with its
  realm, and only bots of that realm join it. Charter ordinals are fixed per
  realm (Albion 0-14, Midgard 15-29, Hibernia 30-44), so names stay unique.

The realm login balance and per-realm charter names follow in R4a.

## Verification without a server

- Launcher tests `WorldResetTests` cover both worlds on a temporary install:
  marker, keeps, relics, properties, roster, backup move, file names, refusal
  while the server runs, and rollback.
- `FullFreshWorldFromARealSaveCopy` builds both worlds at full size from a
  copy of a real save. Set `OFFLINE_DAOC_RESET_SEED` to the copy and
  `OFFLINE_DAOC_RESET_OUT` to a folder. Never point it at a live save.
- Server test `UT_FreshWorldSave` takes that output from
  `OFFLINE_DAOC_RESET_SAVES`. On a copy it registers every table as the
  server does, checks the marker as startup does, applies the shipped data
  packs and plans the bot guilds.
- The existing `ShippedPacks_ApplyOnTheDevSnapshot` runs on the output with
  `OFFLINE_DAOC_TOA_SNAPSHOT`.
- The dev snapshot has a damaged `offline_guild_grudges` table. Rebuild the
  copy without that table's rows before seeding.
  [TOA_IMPORT_PROBE.md](TOA_IMPORT_PROBE.md) describes the damage.
