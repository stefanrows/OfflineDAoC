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

## World contract (RvR mode, 0.332.0-0.345.0)

The rules every change must keep. Task 147, plan
[RVR_MODE_PLAN.md](RVR_MODE_PLAN.md), port details
[RVR_PORT_MAP.md](RVR_PORT_MAP.md).

1. **Two worlds, two saves.** RvR (Realm vs Realm, the default) lives in
   `opendaoc.sqlite3.db`, PvP (Camlann) in `opendaoc-camlann.sqlite3.db`, each
   with its own side files (below). There is no rule switch inside one save:
   guilds, companions, keeps and settings belong to the save.
2. **The marker decides.** `Realm-1` or `Camlann-1` in
   `offline_local_options.WorldModel`; any other value or none is refused.
   `GameServer.ValidateWorldModel` reads it once at startup into `WorldMode`,
   which never changes at run time. Before startup (tools, unit tests) the
   world is Camlann.
3. **One dispatch.** Code that differs per world asks `WorldMode.IsRvr` (or
   compares `WorldModelKind.Realm`). Realm-1 creates `RvrServerRules`, a
   subclass of `PvPServerRules`. The RvR event layer is a facade:
   `AutonomousRvrEventLayer` dispatches every public call to
   `RealmRvrEventLayer` in Realm-1 (upstream 0.38c `AutonomousRvrEventLayer`
   and `.Defense` verbatim, local changes marked `Fork:`) or to
   `CamlannRvrEventLayer` in Camlann (the fork's layer, renamed, otherwise
   byte-for-byte). A call only one layer knows answers a neutral default in the
   other world. Controller entry points branch the same way (`ExecuteRvrRealm`,
   `HandleSiegeRallyRealm`, `TryEngageFrontierThreatRealm`,
   `ChooseRvrDestinationRealm`).
4. **Camlann behaviour unchanged.** Every RvR branch answers the old value
   outside Realm-1. A change meant for both worlds says "both worlds" in its
   changelog line (so far: road-graph fallback, STOP EVENT, grudges against
   human killers, open BG units not split by keep guild).
5. **Test both worlds.** A rule that must hold in both worlds uses the
   `BothWorlds` helper (`source/server/Tests/UnitTests/BothWorlds.cs`): the
   test takes `[TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]`,
   wraps its body in `using var _ = BothWorlds.Use(world);`, builds the rules
   with `BothWorlds.Rules(world)` and picks the answer with
   `BothWorlds.Expect(world, camlann, realm)`. Such fixtures are
   `NonParallelizable` (the world is process-wide). An RvR-only test wraps its
   body in `WorldMode.OverrideForTests(WorldModelKind.Realm)`; a test without
   either runs as Camlann.
6. **What the RvR world takes from upstream.** Realm rules (same realm allied,
   other realms enemies, enemy names hidden, realm-only travel and parties)
   and the upstream 0.38c RvR bot layer: one siege roll a minute, one attack
   per realm, level 50 for every siege, muster at the own portal outpost,
   march column with push and detour, siege engines and resurrection, defense
   rally, retake, relic pad 8, realm raids per realm, town teleporters, route
   threat awareness, keep level 4 after a capture and 5 after a claim.
7. **Fork features that stay in the RvR world.** Companions (roster, squads,
   gear, companion ML), Trials of Atlantis and Master Levels (bot ML too),
   Realm Exchange (own realm), enemy name masking through `RvrServerRules`,
   the realm boundary (bots walk out of enemy homelands), Stefan's movement
   port (tasks 135-139), the road-graph fallback, the open BG in Thidranki
   per realm, the realm login balancer (600 per realm) and the 2003-style
   realm guild names (owner decision 9). Built for RvR but off in Realm-1
   since 0.342.0: the R4a/R4b realm armies, home reserve, grudge and revenge
   trips, bot keep claims, guild keep defense, the fork's march restraint and
   the livelier-RvR waves; the full list with integration notes is
   [RVR_FORK_EXTENSIONS.md](RVR_FORK_EXTENSIONS.md).
8. **Keep start.** A fresh RvR world and RESET KEEPS set the 21 frontier keeps
   to their original realm at upstream's release level (`RvrKeepStart`:
   Albion and Midgard 5; Hibernia Dun Crauchon 4, Crimthain, Bolg and
   Scathaig 5, nGed, Da Behnn and Ailinne 1). Portal keeps and relic keeps
   keep their realm, battleground keeps their own start, as before. An older
   Realm-1 save moved its Warden-held frontier keeps once at the next start
   (log `RVR_KEEP_ORIGINAL_START`).
9. **Level-50 fresh world.** CREATE WORLD and RESET WORLD in the RvR world
   create the 600 bots per realm at level 50 with the class loadout
   (`WorldReset.FreshLevels`), because every upstream siege needs level 50.
   An explicit world shape in the request still wins. Camlann's fresh world
   keeps the `bot-goals.json` levels. An existing save keeps its bots' levels.
10. **STOP EVENT.** Forced realm raids in the RvR world have no time limit, as
    upstream. The launcher's STOP EVENT… button on the realm events panel ends
    a running forced dragon or epic dungeon raid at once ("Stopped by
    player", no cooldown, the reserved bots go back to their goals). Keep
    events cannot be stopped. The button works in both worlds; Camlann's
    timed raids are otherwise unchanged.

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

The launcher's World dropdown picks the world (next section).

## World picker (launcher)

Package R5b. The header has a dropdown left of START SERVER. Closed it reads
"World: RvR" or "World: PvP"; open it lists "RvR (Realm vs Realm)" and
"PvP (Camlann)".

- **Switch only while stopped.** The dropdown is enabled only when the server
  is fully stopped and no population, open BG or Trials of Atlantis edit is
  waiting to be saved.
- **One window, one world.** Every launcher read and write (dashboard, rates,
  GM switch, Trials of Atlantis block, open BG tab, population tab, Realm
  Records, Herald, Realm Exchange, keep reset) uses the picked world's save,
  fixed when the window is built. Switching closes the window and opens a new
  one on the other world, at the same place and tab. Nothing is lost: each
  world keeps its own save and side files.
- **Remembered per Windows user and installation** in
  `%LOCALAPPDATA%\OfflineDAoC\world-choice.json`, beside the Join Friend
  profile, keyed by the installation's runtime folder.
- **Server configuration.** On a switch, when the window opens, and right
  before every START SERVER, the launcher writes the picked world's save into
  `serverconfig.xml` with `CamlannServerConfig.EnsureWorld`. It does so only
  for a save that carries the world's marker. A world without a startable
  save is never written there, because a server started by hand would create
  an empty save in its place.
- **While a server runs**, a new launcher window follows the save the
  configuration names, so it never reads another world under a running
  server.
- **Status line and title.** The status line starts with `WORLD: RvR` or
  `WORLD: PvP (Camlann)`; the window title and the header's subtitle name the
  world too.
- **Gothwaite** controls appear only in the PvP world. In the RvR world the
  World mode row of the population tab is hidden and a save that turns
  Gothwaite on is refused (owner decision 18).

### CREATE WORLD and RESET WORLD

| The picked world's save | START SERVER button | RESET WORLD… |
|---|---|---|
| Carries the world's marker | START SERVER | Starts the world over |
| Missing | CREATE WORLD… | Off |
| `Normal` or no marker (an old save) | CREATE WORLD…, built from that save | Same as CREATE WORLD |
| The other world's marker | CREATE WORLD…, the old file goes to the backups | Same as CREATE WORLD |
| Any other marker | Off; the status line names the marker | Off |

Both buttons ask once. The confirmation names the world, the save the world
data comes from, and the backup folder
`<install>-backups\<date-time>-<rvr|camlann>-world-reset`. Both run
`WorldReset.CreateFresh` for the picked world; the other world is not
touched. The window then reopens on the fresh world. RESET WORLD… sits in the
status line beside DELETE ALL BOTS….

The earlier one-time in-place Camlann conversion of a `Normal` save
(`CamlannWorldReset.Apply`) no longer runs from the launcher. CREATE WORLD
replaces it and moves the old save to the backups instead.

### First run

- **Nothing picked yet:** RvR when the RvR save carries `Realm-1`, or when
  neither world exists. PvP when only a Camlann world exists.
- **Today's installs** keep their Camlann world in `opendaoc.sqlite3.db`. On
  the first launcher start with the server stopped, the launcher renames that
  save, its `-wal`/`-shm`/`-journal` and its side files to the Camlann names
  (`opendaoc-camlann.sqlite3.db`, `realm-event-records-camlann.sqlite3`, and
  so on). Nothing is copied, reset or deleted, and a failed rename puts every
  file back. It refuses when a Camlann file already exists. The dropdown then
  shows PvP. Picking RvR offers CREATE WORLD…, which builds the RvR world
  from the Camlann save without changing it.
- Tools that read `runtime/data/opendaoc.sqlite3.db` directly now read the
  RvR world. Before the RvR world exists that file is absent.

### Progress import

`ImportEngine.IsCamlannRuntime` treats a runtime as a fork world when its
configuration pins GameType PvP or a save carries `Realm-1` or `Camlann-1`,
and it finds a runtime that has only the Camlann save. Import stays disabled
for both worlds; the message names the world. A save of one world is refused
for the other world first, with its own message.

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
| The 21 frontier keeps | Original realm at upstream's release level, no guild (since 0.342.0; contract item 8) | Realm 0, no guild; the server hands them to the Frontier Wardens |
| Other ordinary keeps and towers | Realm 0, no guild; the server hands them to the Frontier Wardens | Same |
| Portal keeps (base level 100 or more) | Original realm | Realm 0, as today |
| Relic keeps (skin 99) | Original realm | Realm 0, as today |
| Relics | Home pads, realm = original realm | Same |
| Bot roster | 600 per realm at level 50 (since 0.342.0; contract item 9) | 600 per realm, level shape from `bot-goals.json` |
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

- **Start check.** The launcher starts the picked world when its save carries
  that world's marker. Every other save gets CREATE WORLD… (World picker
  above).
- **Gothwaite switch.** In the RvR world the population tab hides the world
  mode row and refuses to save Gothwaite PvP (owner decision 18).
- **"Reset all keeps & relics".** In a `Realm-1` save the button returns portal
  and relic keeps to their original realm and, since 0.342.0, the 21 frontier
  keeps to their original realm at upstream's release level; every other keep
  goes neutral, as in Camlann.
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

Since R4a the RvR guilds take realm names and a charter plan per realm, and a
realm login balancer keeps each realm near its own online target: see
[RVR_BOTS.md](RVR_BOTS.md).

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
