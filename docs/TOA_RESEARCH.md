# Trials of Atlantis: research status

Research only. Nothing here is implemented or authorized for implementation.
Owner request 2026-10-20 (Aaron): document what of Trials of Atlantis (ToA) is
already available, what open projects can supply, and where gaps remain, so
Stefan can continue from this state. Task: [TASKS.md](TASKS.md) item 134.

## Goal and scope (owner, 2026-10-20)

- **Gameplay first:** ToA items and overcaps, realm abilities, artifacts with
  levelling, Master Levels, Atlantis zones as player content.
- **Bots are not required in ToA.** No Atlantis navmeshes, no autonomous bot
  routing there. This removes the largest cost (underwater navigation).
- **Invent as little as possible.** Take data and code from existing open
  projects. Where something is missing, prefer a simple mechanism over a
  reconstruction of live behaviour.

### Conflict with the current era decision

[CAMLANN.md](CAMLANN.md) owner decision 1 sets the era to 1.65 Old Frontiers,
**pre-ToA**, and lists "ToA zones/items/Master Levels" under Non-goals. ToA
work needs an explicit owner decision first: replace the era, or add ToA as an
opt-in mode/server property that leaves the 1.65 default intact. Not decided.

## Inventory

Counts measured 2026-10-20 against the dev DB snapshot
(`playable-dev/db-snapshot/opendaoc.snapshot.db`), the installed client in
`playable/runtime/client-opendaoc/app`, and the upstream repositories listed
under Sources.

### Client (installed, game.dll 1.127)

| Content | Status |
|---|---|
| Atlantis zones, all three realms (Oceanus, Stygia, Volcanus, Aerus; zones 30-44, 73-87, 130-144) | present in `zones.dat` |
| Celestius (91), Atlantis city/ruins regions 70-72 | present |
| ToA dungeons: Cetus' Pit, Sobekite Eternal, Temple of Twilight, Halls of Ma'ati, Great Pyramid, Deep Volcanus, City of Aerus | present |
| Labyrinth of the Minotaur | **absent**: no zone entry in the client's `zones.dat` (classic zone file). Only the server's Minotaur relic code exists |

### Our server code (OpenDAoC base)

| System | Status |
|---|---|
| ToA item bonuses: stat/resist/HP/power cap bonuses, mythical caps, ToA % bonuses (`StatCalculator`, `TOABonusCalculator`, `MaxManaCalculator`, `ResistCalculator`) | **active**: any item carrying these bonuses already works |
| Random item generator with ToA stats | code active, `rog_toa_item_chance` = **0 %** |
| Artifact system (`ArtifactMgr`, `InventoryArtifact`, scholars) | **missing**: `gameobjects/Atlantis/ArtifactScholar.cs` and `Scholar.cs` are fully commented out; no `ArtifactMgr` in the tree |
| Djinn teleporters (`gameobjects/Atlantis/Teleporter/*`) | present |
| Atlantis entry | `atlantis_teleport_plvl` = 2 (GM only) |
| Master Level spell handlers (Banelord, Battlemaster, Convoker, Perfecter, Sojourner, Spymaster, Stormlord, Warlord) | present in `spells/Masterlevel` |
| Champion Level properties | present (`hps_per_championlevel`, `cl_xp_rate`); not ToA |
| Realm abilities | two sets in the tree: Atlas OF set (69 handlers, active per CAMLANN.md) and a DOL-style set (51 handlers, e.g. Dashing Defense, Bedazzling Aura, Wrath of Champions). Which RAs belong to ToA-era rules is **not researched** |
| Minotaur relics | code present; no zone in the client |

### Our database (dev snapshot)

| Data | Count |
|---|---|
| Items with ToA bonus types (201-260) | 2,370 of 35,204 |
| Artifacts / artifact bonus rows / artifact item versions | 57 / 516 / 200 (197 resolve to items) |
| Mobs in Atlantis regions 30/73/130 | 28 |
| Teleports into Atlantis | 0 |
| Master Level spells | 0 (one `Stormlord` spell line) |
| `CharacterXMasterLevel` rows | 0 |

## Open sources

| Source | Licence | What it supplies |
|---|---|---|
| [Eve-of-Darkness/db-public](https://github.com/Eve-of-Darkness/db-public) (DOL public DB, updated 2026-10-02, JSON per table, exports MySQL/SQLite) | none stated ❓ | **39,914 mobs** in Atlantis and ToA dungeons (1,254 distinct names; Alb 11,164, Mid 11,145, Hib 14,554, dungeons 3,051). Includes 81 merchants, 72 guards, 27 boat stable masters, 20 taxi boats, 13 scholars, 12 hasteners. Same 57 artifacts. 80 ML spell lines but only 14 ML spells. Data follows Allakhazam |
| [Dawn-of-Light/DOLSharp](https://github.com/Dawn-of-Light/DOLSharp) (active) | GPL-2.0, same as ours | Full artifact code: `ArtifactMgr.cs` (895 lines), `InventoryArtifact.cs` (728), `quests/Atlantis` with one turn-in quest per artifact and one encounter class per artifact (62 files), scholars, Djinn teleporters, `LiveMasterLevelsSpecialization` |
| [djsniperwolf/SWDB](https://github.com/djsniperwolf/SWDB) (last push 2022) | none stated | Older DOL DB; forum posts claim Atlantis spawns for all realms and pathing. `mob.sql` not parsed here ❓ |
| Eden, Uthgard, Phoenix | closed | No code or data available |

Our server descends from DOL, so DOL code is a port, not a rewrite. Schema
differences (OpenDAoC renamed/changed some tables and the ECS rewrite of
effects) are the main port cost.

## How DOL's artifact system works (read 2026-10-20)

- **Levelling:** every equipped artifact gains XP from player and NPC kills
  (base + camp + group + outpost XP; not while praying). Gain =
  `xp × Artifact.XPRate / artifact_xp_rate` (server property, default 350).
  Ten levels at 50 M XP each; bonuses unlock per `ArtifactBonus.Level`. A guild
  buff adds `guild_buff_artifact_xp` %. Live had different XP sources per
  artifact; DOL simplifies this to "slaying enemies anywhere" for all.
- **Scrolls and books:** created at runtime from the `Artifact` row (title
  fields `Scroll1`…`Scroll23`, models), combined into a book, turned in at a
  scholar. No item template per scroll is needed.
- **Encounters:** `quests/Atlantis/Encounters/*.cs` are 47-line stubs that
  only carry a name. Credit is granted by `GrantArtifactCredit` /
  `GrantArtifactBountyCredit` (matches `Artifact.Credit`); the actual encounter
  content (who or what grants credit) does not exist.

## Gaps and the simplest fill for each

| Gap | Simple mechanism (proposal) |
|---|---|
| Atlantis empty on our server | Import the db-public Atlantis/ToA-dungeon mobs, merchants, boats, scholars into a copy of the DB; open entry by lowering `atlantis_teleport_plvl` |
| Mob loot: only 77 of 1,254 ToA mob names have loot links in db-public | Use our existing random generator inside Atlantis with `rog_toa_item_chance` > 0, plus the 2,370 ToA items we already have as named-mob drops. No new items invented |
| Artifact encounters are stubs | Credit for killing one named mob per artifact in its zone (DOL's `Credit` field already supports bounty-style credit). Mapping artifact → named mob from Allakhazam / the DAoC knowledge base |
| Artifact text placeholders: `MessageUse`, `MessageCombine*`, `MessageReceive*` = `1001`, scroll titles like `Alvarus_Leggings1`, `ScholarID` = `1001`, `Zone` = `0`, identical in db-public | Generic messages and generated titles ("<Artifact>, page 1 of 3"); assign scholars per realm instead of per artifact |
| Artifact XP sources per artifact (live behaviour) | Keep DOL's uniform kill-XP rule |
| Master Level trials: no encounter data anywhere | Defer. Simplest candidate: one ML step per ToA raid-boss kill. ML spells: 14 in db-public; the rest must come from Allakhazam/KB |
| ToA-era realm abilities | Research which RAs ToA added/changed (patch notes via the DAoC knowledge base) before touching either RA set |
| Travel into Atlantis | Djinn code exists; db-public has 3 teleports plus boat NPCs; verify which works |
| Labyrinth of the Minotaur | Out of reach with this client |

## Suggested order (estimates, not commitments)

| Step | Effort | Result |
|---|---|---|
| 1. Era decision (owner) | — | ToA replaces 1.65, or ToA as an opt-in property |
| 2. Trial import of one Atlantis zone from db-public into a DB copy | ~1 session | Shows schema fit and load errors |
| 3. ToA items/overcaps in loot (property + drop tables) | ~1 session | Overcap gear obtainable |
| 4. Port DOL artifact core (manager, inventory artifact, scholars, turn-in) | 2-3 sessions | Artifacts activate and level |
| 5. Encounter credit + texts (simple mechanisms above) | ~1 session | Artifacts obtainable end to end |
| 6. Full Atlantis import, travel, merchants | 1-2 sessions | Atlantis playable |
| 7. Master Levels | open | Needs a design decision; least data available |

## Found 2026-10-24: gaheris, an OpenDAoC overlay with ToA and Master Levels wired up

[Pactor/gaheris](https://github.com/Pactor/gaheris) (GPL-3.0, last push
2026-10-15) is a co-operative PvE conversion of **OpenDAoC**, our own base,
not of DOL. It is an overlay: 46 scripts in OpenDAoC's custom-script folder
plus 95 SQL migrations against the stock OpenDAoC database. For us that means
no DOL-to-OpenDAoC port; the ECS and table-name shims are already written.

| Piece | File(s) | What it gives us |
|---|---|---|
| Artifact system ported to OpenDAoC | `scripts/toa/ArtifactMgr.cs`, `InventoryArtifact.cs`, `ArtifactCompat.cs` (shim over everything OpenDAoC renamed) | the DOL artifact core, already compiling against OpenDAoC |
| Earning an artifact | `ArtifactQuest.cs`, `ArtifactTurnInQuest.cs`, `ArtifactEncounter.cs`, `Encounters.cs` | encounter, book, turn-in; four reusable encounter mob classes (doppleganger, splitter, seedsman, reward mob) placed with `/mob create` and configured on the mob row |
| Artifact levelling | `ArtifactExperience.cs` | hooks `GameLivingEvent.Dying`, because OpenDAoC never raises `GainedExperience` (our server shares this gap) |
| Scholars, credit merchants | `Scholar.cs`, `ArtifactScholar.cs`, `ArtifactCreditMerchant.cs`, `MasterLevelsMerchant.cs` | the NPCs that hand artifacts and ML credits over |
| Artifact scrolls | `sql/36-artifact-scrolls.sql` | 54 of 57 artifacts with complete scroll sets (162 scrolls) on two binders; Flask, Shield of Khaos and Bruiser have none |
| Master Level spells | `sql/12-master-levels.sql` (64 from db-public) + `sql/42-master-levels-gaps.sql` and `43-…-corrected.sql` (the 16 missing ranks, written by hand) | the full 80-ability set, eight paths of ten ranks |
| ML paths per class | `sql/39-master-level-paths.sql` | the `classxspecialization` rows OpenDAoC never had |
| ML progression | `scripts/progression/MasterLevels.cs` (`GaherisArbiter`, `MasterLevelExperience`) | the Arbiter enrols a player (and group); ML XP comes from killing Atlantis-grade mobs (`ml_xp_per_level` property). **No trials.** |
| ML hold fixes | `scripts/progression/MasterLevelHolds.cs`, `docs/master-levels.md` | four ML abilities that never ended on OpenDAoC (Lookout, Blanket of Camouflage, Battlewarder, Focusing Winds) are repaired |
| Atlantis population and travel | `sql/13-atlantis-mobs.sql` (11.6 MB), `14-atlantis-travel.sql`, `28-…-portals.sql`, `29-…-zonepoints.sql`, `35-…-npcs.sql` | mobs, portals, zone points, Arbiter, Demyphon, scholars, already in OpenDAoC's schema |

What gaheris confirms about the gap list above: **nobody has built the ML
trials.** The gaheris author checked the same sources and wrote (in
`MasterLevels.cs`): "the trials were never implemented" in OpenDAoC or DOL;
the DOL community scripts only supply a few generic encounter mobs. gaheris
answers that with kill-based ML experience, which is exactly the simple
mechanism proposed above. The 2014 DOL forum thread
["Master Level Overhaul"](https://dolserver.sourceforge.net/viewtopic.php?p=153086)
reworked DOL's ML data handling, not the trials either.

Fit for this fork (not verified by building):

- gaheris targets stock OpenDAoC-Core on .NET 10 and loads its scripts from
  the custom-script folder. Our server is an older, heavily changed OpenDAoC;
  the scripts will need adjusting, but to the same code family, which is far
  less than the DOL port estimated above.
- gaheris is co-op PvE (one realm, mercenary companions). Its artifact and ML
  pieces do not depend on that ruleset; the mercenary credit hook
  (`GaherisLoot.Credit()`) does, and would be swapped for our bot/companion
  credit.
- The SQL migrations are MySQL/MariaDB flavoured for the OpenDAoC Docker DB;
  our runtime uses SQLite. Expect a syntax pass, not a schema pass.
- Licence GPL-3.0; irrelevant for a private server (owner, 2026-10-24).

Revised order: step 4 above (port the DOL artifact core, 2-3 sessions) becomes
"adopt the gaheris scripts", and step 7 (Master Levels) gets a concrete
baseline: gaheris's kill-XP Arbiter plus the complete 80-spell set. Trials
remain unbuilt anywhere and would be our own design if wanted.

## Not checked

- Whether db-public mob rows load in our server unchanged.
- Whether the gaheris scripts compile against our server tree, and how many
  of its SQL statements SQLite accepts as written.
- SWDB contents beyond the forum claims.
- Whether a different, complete client build contains the Labyrinth.
- db-public and SWDB licences (none stated in either repository).

## Sources

- [Eve-of-Darkness/db-public](https://github.com/Eve-of-Darkness/db-public)
- [Dawn-of-Light/DOLSharp](https://github.com/Dawn-of-Light/DOLSharp)
- [djsniperwolf/SWDB](https://github.com/djsniperwolf/SWDB)
- [SWDB forum release (Atlantis spawns)](https://dolserver.sourceforge.net/viewtopic.php?p=156639)
- [DOL forum: artifact files and levelling](https://dolserver.sourceforge.net/viewtopic.php?p=140510)

## Supplementary sources (WP0.1, 2026-10-24)

Question: which open ToA content gaheris does not carry, and where to get it.
Counts measured 2026-10-24 from the GitHub API and raw downloads (scratch
copies only), compared against gaheris, our dev DB snapshot and the stock
`OpenDAoC/OpenDAoC-Database` dump. Atlantis region set used for every count:
30-44, 73-87, 130-144, 70-72, 91.

### Ranked: what adds something

| # | Source | Adds beyond gaheris | Measured | Fetch path | Effort |
|---|---|---|---|---|---|
| 1 | db-public `LootTemplate` + `MobXLootTemplate` | **ToA mob loot.** Our DB and the stock OpenDAoC DB both hold the 341 mob-to-template links for ToA mobs but **0** of the loot rows behind them (both have 1,792 `LootTemplate` rows in total). gaheris carries no loot rows either | 341 of 1,437 distinct ToA mob names have loot; 341 templates, 1,876 rows, 1,423 items (765 at level 50+, 429 at 40-49); 7 items missing from our `ItemTemplate`, all others resolve | `raw.githubusercontent.com/Eve-of-Darkness/db-public/master/data/LootTemplate.json` and `MobXLootTemplate.json` | small: JSON to SQLite insert, filter to the 341 templates, skip 7 unresolved items |
| 2 | db-public artifact **scroll drops** (subset of #1) | Live-style scroll drops instead of gaheris's two scroll binders (`36-artifact-scrolls.sql` sells 162 scrolls for bounty points) | 100 scroll items ("N of 3") in 37 scroll sets, dropped by 179 mob names, chance 15 % each; all 100 items already in our DB | as #1; mapping is item name → loot template → mob name | small, comes with #1; owner decides binder, drops or both |
| 3 | DOLSharp `GameServer/quests/Atlantis/Artifacts/*.cs` | **Artifact → encounter mapping** and per-artifact scholar texts. gaheris replaced these with one generic `ArtifactQuest`/`ArtifactTurnInQuest` and does not carry the names | 62 files: 38 name step 1 (e.g. "Defeat Minkhat", "Defeat Eramai", "Win three consecutive rounds of battle with the gladiators"), 24 are stubs with no step text. 27 distinct named targets; 21 stand in gaheris's Atlantis spawns, 6 do not (Battler, Snatcher, Linos, Sobekhotep, Malamis, "a wavelord"). Level gate 45 for most, 30/35/40 for four. 133 scholar `SayTo` lines | `gh api 'repos/Dawn-of-Light/DOLSharp/git/trees/HEAD?recursive=1'`, then raw files from `master/GameServer/quests/Atlantis/Artifacts/` | small: a text-extraction script gives the mapping table for WP3.3; the dialogue can feed WP3.4. The 24 stubs need the Herald/KB sources below |
| 4 | Our own tree: `GameServer/language/EN/Expansions/MasterLevel.txt` | **ML trial skeleton**: the in-game text of every ML step, already shown by `GamePlayer`'s ML window | 90 step texts (ML1-ML9, 10 steps each; no ML10 steps): 47 battlegroup, 30 group, 2 solo, 11 unlabelled; complete and incomplete wording; FR copy in `language/FR/Quests/ML.txt` | already local | none to fetch; it names the encounter, zone and group size per step, so a trial design can follow it |
| 5 | DOLSharp `GameServerScripts/quests/Examples and  Tutorials/MasterLevel encounter/ML1FireIsland.cs` (+ `EncounterMgr.cs`) | **The one ML trial step scripted anywhere** (ML1.1, Ianetor's fire game in Oceanus Anatole): spawn coordinates, Sunkaio/Zopureo/Aithos, wall of fire, purros, five brains, 15-minute despawn. Corrects "trials implemented nowhere": one step exists, without ML step credit | 980 + 170 lines; `Artifact encounter/BasicEncounterMob.cs` (300) and `TetheredEncounterMob.cs` are generic artifact encounter bases | raw files from `Dawn-of-Light/DOLSharp/master/GameServerScripts/quests/Examples%20and%20%20Tutorials/` (two spaces) | medium: port to OpenDAoC timers/brains plus a credit call; template for further steps |
| 6 | OpenDAoC-Core `GameServer/spells/Artifacts/*.cs` | **Artifact spell handlers** (Alvarus morph, Dream Sphere morph, Shades of Mist, Atens Shield, Band of Stars, Traitor's Dagger, ...). gaheris does not carry them because stock OpenDAoC-Core has them; **our tree has the `eSpellType` values but none of the handler files** | 30 files | `gh api 'repos/OpenDAoC/OpenDAoC-Core/git/trees/HEAD?recursive=1'`, raw files under `GameServer/spells/Artifacts/` | medium: port with WP0.2/WP1.1; without them artifact use-abilities do nothing |
| 7 | DAoC knowledge base (`/home/aaron/daoc-knowledge-db/knowledge.sqlite`, read-only FTS) | Patch-note and forum evidence for the 24 stub artifacts and for ML step contents | Herald: 361 `article.php` docs, 69 mention ToA terms (patch notes 156, 157, 400, 405, 499-502 are the dense ones). FTS chunk hits: Mesedsubastet 26, Ianetor 15, Sobekhotep 15, "ML1" step 162, "artifact encounter" 721. Only 4 Allakhazam docs in the KB. `ask.py` on `E:\daoc-knowledge` not used | `sqlite3` FTS5 on `chunks_fts` joined to `documents` | medium: good for spot answers per artifact or step; a full scripted mapping from patch notes is not realistic (names are scattered, no tables) |

### Already covered (no action)

| Source | Finding |
|---|---|
| db-public `MerchantItem` | 73 item lists used by Atlantis NPCs, 4,230 rows; 72 lists and all 4,230 rows already in our DB |
| db-public `Teleport` / `ZonePoint` | 3 teleports (all "Oceanus"), 28 Atlantis zone points; ours have 0, but gaheris `14-atlantis-travel.sql` (13 teleports) and `29-atlantis-zonepoints.sql` (100) cover more |
| db-public `ArtifactXItem` / `ItemTemplate` | 200 artifact versions, 197 resolve; identical to our DB |
| db-public `Mob` | 42,943 rows in the region set (1,437 names; 70-72 hold 3,945 sea mobs; 91 Celestius holds 0); gaheris imported 41,727 of them |

### Nothing found

- **SWDB** (`djsniperwolf/SWDB`, `mob.sql` + `mob2.sql`, 121,600 rows): 37,769 Atlantis-region mobs, 969 names; the 6 names not in db-public are placeholders and NPCs ("new mob", "scholar", "albion teleporter", ...). 327 ToA names with loot, all already in db-public. `artifact.sql` has 64 rows with the same `1001`-style placeholders and `Zone` = 0. Adds nothing.
- **db-public quests in Atlantis:** `DataQuest` 0 of 25, `RewardQuest` 0 of 32 start in the region set. No Atlantis paths (`Path` 0, 20 mobs reference a `PathID`).
- **OpenDAoC-Core commits:** `spells/Masterlevel` and `gameobjects/Atlantis` only receive engine-wide refactors (latest 2026-08-11 and 2025-01-17); `quests/Atlantis` last changed 2021-10-06 ("Removed a bunch of Cata/ToA quests/teleporters"). No `Arbiter.cs` history beyond the move. No new ToA content.
- **OpenDAoC/OpenDAoC-Database** (pushed 2026-09-29): same 341 links, 0 ToA loot rows; same 1,792 loot rows as our snapshot.
- **DOL-Avalonia** `Avaloniaserver`, `GondwanaServer`, `DOLSharp`: stock DOL Atlantis/artifact/ML code only (confirmed).
- **Other GitHub forks** found by code search (`ArtifactEncounter`, `GrantArtifactCredit`, `CharacterXMasterLevel`): `tobz/dawnoflight` (2014 SVN mirror), `Bootz/Dawn-of-Light-core` (2012), `wicurcyn/TestEra` (2025): older DOL copies with 38 artifact quests and the same 62 encounter stubs, a subset of DOLSharp. Searches for ML trial terms ("Master Level" trial, MLStep) returned nothing.
- **Web:** no public page with a tabular artifact → encounter → scroll-drop list turned up in search; the DOL forum "Artifact FAQ" thread (https://dolserver.sourceforge.net/viewtopic.php?p=116475) and the Camelot Herald fandom wiki (https://camelotherald.fandom.com/wiki/Hall_of_Heroes) are the remaining leads, not read in full.

Consequence for the plan: WP3.3 gets its mapping from #3 (38 artifacts) and #2
(scroll drops); WP1.x should import #1 and port #6; Master Level trials have a
text skeleton (#4) and one worked example (#5) but no other data anywhere.

Web pages checked by the fetch helper on 2026-10-24: the DOL forum "Artifact
FAQ" thread has no artifact table (scrolls drop by region, not per mob); the
Camelot Herald fandom wiki refuses automated fetches (HTTP 402); the Wayback
Machine is not reachable from this tooling. None of the three is a usable
mapping source.
