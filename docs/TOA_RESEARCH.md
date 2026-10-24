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

## ToA-era realm abilities (WP2.7, 2026-10-24)

Question: which realm abilities (RAs) did patches 1.65 to 1.69 (October 2003 to
spring 2004, before New Frontiers 1.70 replaced the whole RA system) add or
change, and what is missing or different in our active set (`realmabilities_atlasOF`)?
Research only; nothing was changed in code or database.

### Result in short

- **Little was added.** ToA era did not introduce a new RA wave. The whole RA
  system (Aug stats, Mastery of ..., Wild Power/Arcana/Healing/Minion, Dodger,
  Determination, Veil Recovery, class RAs) already existed in the 2002 chart
  (Herald, 26 April 2002). 1.65 to 1.69 only rebalanced and bug-fixed it, plus a
  few class-access additions.
- **Our set is the old (pre-New-Frontiers) set.** 101 distinct `AtlasOF_*` keys in
  `ClassXRealmAbility_Atlas`, 69 handler files. Not compared name by name
  against the 1.70 New Frontiers chart (Herald article 502); the RA names seen are
  all from the 2002 chart or the 1.65-1.69 notes.
- **Four build candidates:** Majestic Will rework (1.68) and three class-access
  additions (Bard Mastery of Concentration, Cleric Wild Power, Thane Mastery of
  Magery plus Wild Power). The three class-access additions shipped as pack 080
  (four rows, [TOA_DATA_PACKS.md](TOA_DATA_PACKS.md)); the Majestic Will rework
  is implemented in the handler (2026-10-24, owner decision). Six more items need a code check (resist stages and
  1.69 bug fixes).
- **Version labels are approximate.** The KB copies of Herald articles 495/500 carry
  no version header. Article 500 mentions 1.68 internally and sits before article
  501 (= 1.68/1.69 notes, which mention the RA respec popup); article 495
  mentions 1.64 as past. Treat "1.66-1.68" style ranges as unresolved to the letter.

### Added or changed per patch (1.65 to 1.69)

| Patch | Change | Source |
|---|---|---|
| 1.65 (Oct 2003) | Empty Mind was broken; fixed and cheaper. Now 5 % resist per level, cost 3 / 6 / 10 for levels 1 / 2 / 3. Earlier purchases removed and refunded | [Herald 1095](http://web.archive.org/web/20031027091225/http://www.camelotherald.com:80/more/1095.shtml) |
| 1.65 | Spell resists from RAs are shown and applied separately from item/buff/racial resists (two consecutive resist stages instead of one sum). Example given: 40 % from items and buffs first, then the RA percent on the remainder | Herald 1095; explanation in [Herald 1125 Q&A](http://web.archive.org/web/20100105202209/http://camelotherald.com/news/news_article.php?t=game&storyid=1125) |
| 1.65 | Because of that: Brilliant Aura of Deflection and Avoidance of Magic removed and refunded for everyone who owned them (re-bought afterwards; Avoidance of Magic III still 9 %, so 3 % per level stays) | Herald 1095; test notes [Herald 1087](http://web.archive.org/web/20100104193659/http://www.camelotherald.com/article.php?id=497) |
| 1.65 | Volcanic Pillar radius 700 to 500; owners refunded | Herald 1095; test notes Herald 1089 |
| 1.65 | Thanes may buy Mystic Crystal Lore | Herald 1095 |
| 1.65 | Viper now chains off Tarantula | Herald 1095 |
| 1.66 to 1.67 (letter unresolved) | Mastery of Arms now affects attack speed properly (bug fix) | [Herald article 495](http://web.archive.org/web/20100104194002/http://www.camelotherald.com/article.php?id=495) |
| 1.67/1.68 | RA points for Majestic Will refunded after hot fix (Aug Acuity III not refunded; "all RAs will be refunded in New Frontiers") | [Herald 1367 Q&A](http://web.archive.org/web/20110104104640/http://camelotherald.com/news/news_article.php?t=game&storyid=1367) |
| 1.68 (hot fix 24 Feb 2004, "1.68j" in other notes) | Majestic Will: bonus corrected to the documented 5 % per level; timer cut from 30 to 10 minutes. Announced for later that week: Aug Acuity III prerequisite removed, five levels instead of three, cost 1 / 3 / 6 / 10 / 14 (34 total), 25 % at level V | [IGN board post quoting the Mythic announcement](http://web.archive.org/web/20050128161849/http://vnboards.ign.com:80/Hibernia_Magician_Professions/b20909/64868812/p1) (KB document 49757) |
| 1.68 | Animist bomber pets gain their caster's Majestic Will | [Herald article 500](http://web.archive.org/web/20100104195148/http://www.camelotherald.com/article.php?id=500) |
| 1.68 | Bards may buy Mastery of Concentration; Clerics may buy Wild Power; Thanes may buy Mastery of Magery and Wild Power. Non-list casters with Wild Power now get a crit chance on direct damage spells (without the list-caster base chance) | Herald article 500 |
| 1.69 (April 2004) | Confirmation popup when using an RA respec. Master Level respec NPC added (Pendragon only first). Soldier's Barricade blocks the stated percent, no longer breaks in combat, does not stack with the new focus shell line. Vanish clears all DoT spells and poisons and no longer drops when damaged. Thornweed Field uses the variable duration from the caster's RA attribute instead of lasting forever. Animist bombers and necromancer pets gain Wild Power. Veil Recovery also shortens the release sickness duration | [Herald 1411 (1.69n)](http://web.archive.org/web/20040601101419/http://www.camelotherald.com:80/more/1411.shtml), [Herald 1488 (1.69 follow-up)](http://web.archive.org/web/20110104074928/http://camelotherald.com/news/news_article.php?t=game&storyid=1488), [Herald 1511 (1.70x)](http://web.archive.org/web/20101028151325/http://camelotherald.com/news/news_article.php?t=game&storyid=1511), [Herald 501](http://web.archive.org/web/20100104195132/http://www.camelotherald.com/article.php?id=501) |
| 1.69 | Unique RAs Rune of Utter Agility (Runemaster) and Blade Barrier (Blademaster): evade/parry above 50 % in effect (delve still shows 90 %) | Herald 1511 (listed as fixes made while testing 1.70, referring back to 1.69 behaviour; weakest source in this table) |

Not found in any source: a ToA-specific RA, an RA granted by a Master Level, or
an RA cost change between 1.65 and 1.69 other than the ones above.

### Missing or different in our set

Our active set was checked from the handlers (levels, cost, timer) and from
`ClassXRealmAbility_Atlas`. Class ids as in `eCharacterClass` (Bard 48, Cleric 6,
Thane 21).

| Item | Ours | ToA era (1.65-1.69) | Recommendation |
|---|---|---|---|
| Majestic Will | 3 levels, cost 3 / 6 / 10 (3-level helper), 30 min recast, Aug Acuity III style prerequisite not checked in handler, 60 s effect, 13 casters | 5 levels, cost 1 / 3 / 6 / 10 / 14, 10 min recast, no Aug Acuity III prerequisite, 25 % at level V (1.68 hot fix and follow-up) | **Shipped in source** (2026-10-24, owner chose the 1.68 rework: handler only, no DB row change; 5 % per level, real-client check pending) |
| Mastery of Concentration for Bard | Bard (48) not in the list (17 classes have it) | Added in 1.68 | **Shipped** (pack 080, one DB row) |
| Wild Power for Cleric | Cleric (6) not in the list (13 classes have it) | Added in 1.68; Cleric/Thane get crit chance on direct damage spells | **Shipped** (pack 080, one DB row) |
| Mastery of Magery and Wild Power for Thane | Thane (21) in neither list | Added in 1.68 | **Shipped** (pack 080, two DB rows) |
| Mystic Crystal Lore for Thane | Present (21 in list) | Added in 1.65 | **Already present** |
| Empty Mind | 3 levels, 5 / 10 / 15 %, cost 3 / 6 / 10, 30 min recast, 60 s duration | 5 % per level, cost 3 / 6 / 10 (1.65) | **Already present** (matches 1.65; recast and duration not stated in the notes) |
| Avoidance of Magic | 5 levels, 3 % per level, cost 1 / 3 / 6 / 10 / 14 (34), 15 % | 3 % per level kept after the 1.65 refund | **Already present** |
| Brilliant Aura | Class 41 only (Enchanter), 1 level, cost 14, applies as secondary resist buff | Removed/refunded in 1.65 for rework, 36 % example in the Herald Q&A | **Check**: confirm the effect stays in the second resist stage; no change to the RA itself |
| RA resists as second resist stage | Not checked in the RA files (this lives in the resist calculation) | Items, buffs and racial resists first; RA resists applied afterwards on the remainder (1.65) | **Check** `PropertyCalc`/resist code; build only if the sum is still one stage |
| Volcanic Pillar | Handler cost 14, 1 level; radius set in the spell data, not verified here | Radius 500 since 1.65 | **Check** radius in the spell row (one value) |
| Soldier's Barricade | Handler cost 10, 1 level; break-in-combat and stacking not verified | Does not break in combat, correct block percent, no stacking with focus shell line (1.69) | **Check** against `AtlasOF_SoldiersBarricadeECSEffect`; focus shell spells do not exist in our era, so only "no break in combat" matters |
| Vanish | Handler cost 10, 1 level, 30 s | Clears all DoT and DoT poisons on use; damage no longer destealths (1.69) | **Check** effect file; skip if already cleared |
| Thornweed Field | Handler cost 14, 1 level | Duration from the caster's RA attribute, not infinite (1.69) | **Check** duration |
| Veil Recovery | 39 classes | Also shortens release sickness (1.69) | **Check** whether `Veil Recovery` is read by release logic; skip if release sickness is not used on this server |
| Wild Power on pets | Not checked | Animist bombers and necromancer pets gain it (1.69); bombers gain Majestic Will (1.68) | **Skip** unless bomber damage is audited; low value for an offline server |
| Rune of Utter Agility / Blade Barrier | Unique RAs present (Runemaster, Blademaster keys in handlers: Bladedance family and style RAs) | Evade/parry above 50 % in effect | **Skip**: weakest source, tuning not documented |

Counts: 13 change entries across 1.65 to 1.69 in the first table (one of them weak),
4 build candidates (Majestic Will, Bard MoC, Cleric Wild Power, Thane MoM + Wild
Power), 6 "check" items, 4 "already present", 2 "skip".

### Master Levels and RAs

- **No source ties Master Levels to RA points.** RA points come from realm
  levels (one point per realm level, 100 realm levels), per the Herald RA intro
  of 26 April 2002 and unchanged to 1.69. Master Levels are a separate track
  (ML points, ML abilities, ML respec).
- **Interactions found:** (1) 1.68: Master Level experience can now also be earned
  in RvR (only for kills that also grant realm points), and keep lords award it
  (Herald article 500). (2) 1.69: a separate Master Level respec NPC next to the
  existing RA respec stones; RA respec stones were 10 silver for a week only at
  1.70 (Herald article 502). (3) Pets: bomber pets copy the owner's Majestic Will
  and Wild Power (see table).
- **Consequence for us:** the ML implementation (see [TOA_MASTER_LEVELS.md](TOA_MASTER_LEVELS.md))
  needs no RA hook. Do not grant RA points from ML kills.

### Sources and gaps

- Local knowledge base (`/home/aaron/daoc-knowledge-db/knowledge.sqlite`):
  Herald documents 10666 (1.65), 10688/10690 (1.65 test), 6343 (resist Q&A),
  12445 (Majestic Will refund Q&A), 5497, 5519, 5516, 14707/88883 (1.69n),
  12338 (1.69 follow-up), 9849 (1.70x), 16237 (RA chart, 26 April 2002), IGN board
  document 49757 (Majestic Will announcement), Uthgard "Patch goal 1.69" thread
  (15060, confirms 1.69 as the last pre-New-Frontiers patch).
- Web search (Fandom, gamebanshee) returned the 1.65 notes only; Fandom refuses
  automated fetches (see Supplementary sources above). No ToA-specific RA list
  was found anywhere.
- Not checked: the exact patch letter of Herald articles 495/500; pre-1.70 RA lists per class (the 2002
  per-class pages exist in the KB as `camelotherald.com/realmabilities/<class>.html`,
  not compared class by class); handler internals beyond level, cost and recast.
