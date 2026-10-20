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

## Not checked

- Whether db-public mob rows load in our server unchanged.
- SWDB contents beyond the forum claims.
- Whether a different, complete client build contains the Labyrinth.
- db-public and SWDB licences (none stated in either repository).

## Sources

- [Eve-of-Darkness/db-public](https://github.com/Eve-of-Darkness/db-public)
- [Dawn-of-Light/DOLSharp](https://github.com/Dawn-of-Light/DOLSharp)
- [djsniperwolf/SWDB](https://github.com/djsniperwolf/SWDB)
- [SWDB forum release (Atlantis spawns)](https://dolserver.sourceforge.net/viewtopic.php?p=156639)
- [DOL forum: artifact files and levelling](https://dolserver.sourceforge.net/viewtopic.php?p=140510)
