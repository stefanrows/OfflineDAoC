# Trials of Atlantis: implementation plan and to-do list

Owner decision (Aaron, 2026-10-24): **build everything of ToA we can**, on top
of the 1.65 Camlann world. RvR rules stay Old Frontiers. Master Level
experience is not restricted to Atlantis (any mob of level 45+ counts, the
gaheris rule). Licences are irrelevant: this fork stays private. Bots are not
required in Atlantis. Research behind this plan: [TOA_RESEARCH.md](TOA_RESEARCH.md),
task 134 in [TASKS.md](TASKS.md).

This file is the single to-do list for the ToA work. Keep it current: every
agent or session that finishes a package updates its row and the log below.

## Status legend

`[ ]` open · `[~]` running (agent/session named) · `[x]` done (version) ·
`[!]` blocked (reason)

## Work packages

### Phase 0: ground truth (parallel, read-only)

- [x] **WP0.1 Supplementary sources beyond gaheris.** (2026-10-24, see
  TOA_RESEARCH.md "Supplementary sources". Main finds: db-public holds the ToA
  loot gaheris lacks, 1,876 rows / 1,423 items, incl. 100 scroll items on 179
  mobs; DOLSharp's 62 per-artifact quests name 27 encounter targets; our tree
  already has 90 ML step texts; OpenDAoC-Core has 30 artifact spell handlers we
  lack.) What else exists that
  gaheris does not carry: DOLSharp per-artifact quests (`quests/Atlantis/Artifacts/*`),
  db-public loot/quest/merchant rows for Atlantis, SWDB, Allakhazam-style
  drop and encounter mappings, newer OpenDAoC-Core ToA commits, ML trial
  designs anywhere. Output: a ranked list with licence-free fetch paths,
  appended to TOA_RESEARCH.md. Agent: research (2026-10-24).
- [x] **WP0.2 Fit of the gaheris scripts to our server.** (2026-10-24,
  `docs/TOA_GAHERIS_FIT.md`. API fit good; scripts become ordinary GameServer
  source, not boot-compiled scripts. Gaps: artifact path not wired end to end
  in gaheris either, our Artifact rows carry numeric placeholders, our Dying
  event passes no PlayerKillers. 5-6 sessions for ML + artifacts.) Clone gaheris,
  compile-check its `scripts/toa` and `scripts/progression` against our
  `source/server` (APIs, events, renamed tables, ECS effects), list every
  gap with the fix. Output: `docs/TOA_GAHERIS_FIT.md`. Agent: advisor
  (2026-10-24).
- [x] **WP0.3 SQL import probe.** (2026-10-24, `docs/TOA_IMPORT_PROBE.md`,
  tooling in `tools/toa/`.) All 385 gaheris statements load after one rule
  (drop `LineXSpell.PackageID`): 41,727 mobs, 73 of 80 ML ranks, 0 rejected.
  Seven NPC ClassTypes have no class yet; ToA dungeons and Celestius stay
  empty (need a db-public dungeon import); files 28/35 must wait for the
  gaheris NPC classes (WP2.2, WP3.1). Dev snapshot had a damaged freelist;
  check the next refresh with quick_check.

### Phase 1: Atlantis exists

- [x] **WP1.1a Data-pack mechanism + gaheris packs.** (0.317.0, real-client
  and first-boot check pending; `docs/TOA_DATA_PACKS.md`) Generic
  `WorldDataPacks` applied at server start before world load: sha-checked
  manifest, one transaction per pack with marker row, online backup before
  the first pack (newest 3 kept), failed packs recorded and not retried until
  `/datapacks apply` or a new file, journal_mode off/memory raised to
  TRUNCATE for the run (0.321.2), SQLite only; deploy and release pack carry `runtime/server/data-packs`.
  Packs: 010 ML spells (73 of 80 ranks), 011 ML paths, 012 artifact spells
  (73 rows), 020 Atlantis mobs (41,727), 021 travel, 022 zone points, 023
  scroll binders. Not shipped: gaheris 28/35 (portals, Arbiter/NPC rows)
  until the NPC classes land (WP2.2/3.1b); 35 NPCs in pack 020 load as plain
  GameNPC until then (pack set 2, 0.321.0, made the Arbiters and Demyphon
  functional and shipped 030/031/040/041/050; scholars, Elmer and Aphaestia
  stay plain until WP3.1b).
- [x] **WP1.1b db-public dungeon mobs + ToA loot, as SQL.** (0.319.1,
  `docs/TOA_DUNGEON_IMPORT.md`, generator `tools/toa/build_dbpublic_packs.py`)
  `tools/toa/sql/030-toa-dungeon-mobs.sql` 2,135 mobs (Sobekite Eternal 167,
  Temple of Twilight 299, Halls of Ma'ati 244-246 per realm, Cetus), 031
  templates/equipment (safety net, 0 new on our DB), 040 ToA loot 2,106
  rows + 364 links. 0 duplicates with gaheris, 0 broken references. Deep
  Volcanus and Celestius have no source anywhere; Pyramid/Aerus come from
  gaheris 13. Shipped as packs 030/031/040 in 0.321.0.
- [x] **WP1.2 Travel.** (0.322.0, real-client check pending; `docs/TOA_TRAVEL.md`)
  `/tc` → capital porter → `[Atlantis]` ports the character to its realm's
  ruins city (70/71/72, next to the Arbiter); `[Atlantis Havens]` (4) and
  `[Atlantis Dungeons]` (6) per realm copy; porters in every haven and ruins
  city with the full menu; `/tc` works inside. Pack 026 (33 Teleport rows, 10
  porters, 3 channelers), pack 027 opens Atlantis (`atlantis_teleport_plvl` 1)
  and sets `rog_toa_item_chance` 25; packs that change a property trigger
  `Properties.Refresh()`. The 13 gaheris travel rows stay unused (9 land on
  monster spawns). Client check: ruins-city porter floor, Halls of Ma'ati
  landing near a level-65 anubite.
- [x] **WP1.3 Loot.** (0.322.0) Loot data shipped as pack 040 (0.321.0),
  Phoenix cap 041; random drops from mob kills inside Atlantis are ToA items
  (`toa_loot_in_atlantis`, default on; `rog_toa_item_chance` = per-bonus cap
  chance, 25). Loot outside Atlantis is unchanged. Test gap: no end-to-end
  ROG test with a mob in an Atlantis region.
- [ ] **WP1.4 Launcher.** Atlantis on/off switch and ML/artifact XP rates on
  the launcher settings page (server properties). Independent.

### Phase 2: Master Levels

- [x] **WP2.0 Core hooks.** (0.315.0) Dying carries all player attackers;
  `GainExperience` raises `GainedExperience` with the pre-rate arguments (fires
  for every positive gain incl. quests/praying: listeners filter on XPSource).
- [x] **WP2.1 ML data.** (0.317.0) Packs 010 (73 ML spells, gaheris
  12+42+43) and 011 (class paths, 39) and 012 (artifact spells). `/train`
  needs no guard: ML specs are career specs, loaded only with MLGranted and
  the chosen line. 7 ML ranks still missing everywhere (Battlemaster mostly).
- [x] **WP2.2 ML progression.** (0.318.0, real-client check pending;
  `docs/TOA_MASTER_LEVELS.md`) Arbiter enrols the group (level 40+, 2000
  units) at ML1 without discipline; exclusive discipline by fixed MLLine
  index, Star of Destiny resets it and drops the old spell line at once;
  ML XP = mob level x `ml_xp_per_level` for every earner on level 45+ NPC
  kills that are reward-eligible, level granted in the field; Demyphon
  redeems the next credit; `/ml`; title follows the discipline; GM
  startml/mlxp/setml/setmlline rewired. **Data follow-up:** pack 020 still
  places the Arbiters as lore-only `DOL.GS.Arbiter`; a small pack (gaheris
  35 subset: Arbiter → `DOL.GS.MasterLevelArbiter`, region 72 Demyphon) is
  needed before anyone can enrol without a GM; goes into the next pack set
  with 050 and the dungeon packs.
- [x] **WP2.3 ML ability fixes.** (0.315.0, real-client check pending with
  WP2.1 data) `MovementWatch` in gameutils; Lookout (partner same realm,
  sitting, within 200 units; ends when either moves or the sitter stands),
  Blanket of Camouflage (own effect per member, positive spell, stealthers
  follow normal rules), Battlewarder (`BattleWarderNpc`, no respawn, no
  reward), Focusing Winds as ECS effects that end on move/attack/cast.
  Forceful Zephyr / Phaseshift untouched. Open for WP2.4: Blanket hides
  only players, companions stay visible.
- [ ] **WP2.5 ML trial texts (optional, later).** Our tree holds 90 ML step
  texts (ML1-9 x 10 steps); DOLSharp has one scripted step (Ianetor ML1.1,
  980 lines, no credit wiring). Only if the owner wants named steps on top of
  kill-XP. Not before Phase 3.
- [~] **WP2.4 Bots and ML.** Owner decision 2026-10-24 19:10: build it.
  (a) Blanket of Camouflage treats companions exactly like players (hidden,
  watched, revealed); pets stay visible as on live. (b) Companions are
  enrolled with their owner at the Arbiter, earn ML XP from the same kills,
  keep ML level/line in their persistent record, get a discipline by role
  (tank Warlord, melee Battlemaster, healer Perfecter, caster
  Convoker/Stormlord, stealther Spymaster) and use ML spells through the
  existing ability choice. (c) World bots 40-50 spawn with an ML level from a
  distribution (like realm ranks today), discipline by archetype, and progress
  by the same kill rule credited like realm points. Agent toa-botml
  (2026-10-24 19:10).
- [x] **WP3.0 Artifact spell handlers.** (0.316.0, real-client check pending)
  All 43 artifact spell types have one handler under `spells/Artifacts`,
  bridged into our ECS (upstream's were legacy-hook dead code); debuffs use the
  stock sign rule and conflict only with their own type; `docs/TOA_ARTIFACT_SPELLS.md`.
  Follow-ups: pack gaheris `90-spells.sql` (68 of the 73 missing artifact
  spell IDs, in WP2.1); 5 spell IDs exist nowhere (7206, 13135, 30115, 30125,
  32223, after the packs only 4: 7206 resolves to the ML spell Purify Vision)
  and 7 pack-012 spell types have no handler anywhere (ZoAura, ZahurAura,
  ZahurEnduProc, CrocodileTearsProc, UnyeldingProc, YouthIllness,
  PowerHealthEnduranceRegenBuff; EternalPlant has no row), to be written new
  as **WP3.0b**; two core one-liners (Ceremonial Bracer mez intercept,
  Cloudsong fall damage) and the stock DexterityConstitutionDebuff's
  always-on conflict go to WP3.1; GM accounts can log back in morphed.
- [x] **WP3.1 Artifact core.** (0.319.0, real-client check pending;
  `docs/TOA_ARTIFACTS.md`) ArtifactMgr/InventoryArtifact under
  gameutils/Atlantis; bonus gating in the item getters (no template clone);
  level from item Experience, 10 levels at 50 M; XP from GainedExperience
  (NPC/Player sources only, DOL formula, `artifact_xp_rate`); generic
  encounter credit per artifact; Ceremonial Bracer and Cloudsong effects;
  vault, account vault and companion inventories wrap artifacts like a
  login; dye/reskin NPCs refuse artifacts; bots never roll artifact
  templates; ArtifactMgr starts right after the data packs. **Data
  follow-up:** `tools/toa/sql/050-artifact-item-classtype.sql` (197
  templates) must ship as a pack before any artifact is handed out. 3
  ArtifactXItem rows name no template.
- [~] **WP3.1b Earning artifacts, data-driven.** Agent toa-scholars (2026-10-24 18:20; folds in WP3.2 scroll drops and WP3.4 texts). Our Artifact rows use
  numeric placeholders (ScholarID 1001, EncounterID = template name, Version
  0-15), so DOL's type-name quest lookup cannot work: Scholar + ArtifactQuest
  rewritten data-driven, credit merchant matches `Id_nb` to
  `lower(Credit)+"_credit"`, scroll-combine hook, `QuestList()` extension
  renamed, `RemoveEncounterCredit` step -1/-2 mismatch fixed. 1.5-2 sessions.
  Depends on WP3.1, WP1.1.
- [ ] **WP3.2 Scrolls and turn-in.** Live style first: the db-public scroll
  drops (100 scroll items, 37 sets, 179 mobs at 15 %); gaheris's binder
  merchants as the fallback for the sets without drops; scholars,
  `ArtifactTurnInQuest`; Flask, Shield of Khaos, Bruiser get generated sets.
  Depends on WP3.1, WP1.1.
- [ ] **WP3.3 Encounters.** Credit per artifact from DOLSharp's per-artifact
  quests (38 of 62 name the target, 27 named mobs; 21 already stand in the
  gaheris spawns, 6 need spawns: Battler, Snatcher, Linos, Sobekhotep,
  Malamis, wavelord); gaheris encounter mobs for the rest. Depends on WP3.2.
- [ ] **WP3.4 Texts.** Replace the `1001` placeholders and generated scroll
  titles with readable text (generic where live text is unknown). Depends on WP3.2.

### Phase 4: polish and verification

- [ ] **WP4.1 Real-client check** of travel, Arbiter, one artifact end to end,
  one ML gained. Owner.
- [~] **WP4.2 Stefan's server.** Journal-mode risk closed in 0.321.2: the
  applier raises its own connections to TRUNCATE when a save runs with
  `Journal Mode=Off` (code and Setup default) instead of refusing, so his
  install gets the packs at the next start; MySQL stays skipped (log line).
  Still to do: tell Stefan to check the server log for "World data packs: 14
  applied" after pulling; release pack carries the packs since 0.317.0;
  property notes.
- [ ] **WP4.3 Docs.** CAMLANN.md era note, PLAY.md section, TASKS 134 closed.

## Next steps (handoff 2026-10-24 13:30, session limit)

Resume here. Everything below is on main 0.319.1; no worktree is pending.
Local server: stopped since the upstream comparison; upstream 0.37b install
at `C:\OfflineDAoC-upstream` is stopped too. Nothing of ToA has run on a
server yet.

1. ~~Pack set 2~~ DONE 0.321.0 (packs 024, 025, 030, 031, 040, 041, 050). Original text: add to
   `tools/toa/build_data_packs.py` and ship as packs
   - 024-toa-arbiter-npcs: gaheris sql/35 subset or `UPDATE Mob SET
     ClassType='DOL.GS.MasterLevelArbiter' WHERE Name='Arbiter' AND
     ClassType='DOL.GS.Arbiter'` plus the region 72 Demyphon row
     (`DOL.GS.Scripts.MasterLevelsMerchant`); without it nobody can enrol.
   - 025-toa-atlantis-arena-fix: the three `LordOfBattle` (Atlas fight club)
     rows in Albion Atlantis regions 73 (Arbor Glen, Typhon's Reach) and 80
     (Temple of Twilight) pull every player within 7,000 units; disable or
     delete them (decision taken by the orchestrator 2026-10-24: remove, they
     are Atlas leftovers in zones that were unreachable).
   - 030/031/040 from WP1.1b (files exist), plus one MobXLootTemplate row
     capping the Phoenix loot.
   - 050-artifact-item-classtype from WP3.1 (file exists).
   Verify with the UT_WorldDataPacks snapshot test, review, merge (MINOR).
2. ~~First boot + deploy~~ DONE 2026-10-24 17:18 (0.321.0 deployed, see Log). Original text: **First boot + deploy on Aaron's PC** (he authorised stopping the fork
   server for ToA tests): run `quick_check` on the live save with the
   server stopped (`playable/runtime/data/opendaoc.sqlite3.db`, read-only
   copy), then `playable-dev/server.sh update` (build + deploy + start),
   watch the log for "World data packs" lines, startup time and memory with
   +44k mobs, then stop. Record in TOA_PLAN log.
3. ~~WP1.2 Travel~~ DONE 0.322.0. Original text: **WP1.2 Travel**: `atlantis_teleport_plvl` to 1, `[Atlantis]` line in the
   porter menu, verify the gaheris travel rows (14) and zone points (29).
4. **WP3.1b** scholars/quests data-driven, **WP3.3** encounters (DOLSharp
   mapping, 6 missing spawns), **WP3.0b** 7 handlers + 4 spell rows,
   **WP2.3 follow-up** Blanket for companions (WP2.4), **WP1.4** launcher.
5. Bug 160 (7 old test failures) and bug 172 (deploy restore) stay open.

Agent rhythm that worked today: developer (opus/high) in its own worktree
→ reviewer (opus/medium-high) → fix → re-check → orchestrator commits with
version bump on the worktree branch and fast-forwards main. Reviewers found
a real defect in every package; keep the review step.

## Out of scope (owner or client limits)

- Labyrinth of the Minotaur (not in the client).
- Live-accurate ML trials (exist nowhere; kill-XP instead).
- Underwater breath/swim mechanics beyond what the server already does
  (check during WP4.1, fix only if broken).

## Log

- 2026-10-24: plan created; WP0.1-0.3 started as agents.
- 2026-10-24 18:20: 0.322.0 deployed on Aaron's PC (packs 026/027 applied, 2
  properties refreshed live, 33 Atlantis destinations, Atlantis open to
  players, server running). WP3.0b and WP3.1b started as agents.
- 2026-10-24 18:15: WP1.2 + WP1.3 reviewed (no blocking finding) and merged as
  0.322.0 (16 packs). Deploy to Aaron's PC next, then client checks.
- 2026-10-24 17:45: journal-mode fallback reviewed (rollback proven through
  the file; journal Off really corrupts on rollback once pages spill) and
  merged as 0.321.2. WP1.2 travel agent running.
- 2026-10-24 17:18: **first boot with the packs on Aaron's PC** (0.321.0 via
  `server.sh update`, deploy backup `playable-backups/deploy-20261024-171744`):
  all 14 packs applied in one run, 0 refused, DB backup
  `opendaoc.sqlite3.db.bak-datapacks-20261024-171800`; Mob 60,505 → 104,364;
  Atlantis 130/73/30 loaded 14,536 / 11,132 / 11,098 mobs, havens ~1,300 each,
  dungeons as planned; startup ~29 s to world init, memory 1.96 GB used after
  start (was ~2.2 GB on long runs before). Startup errors are pre-existing
  base-world rows (missing npctemplates for Public_DB mobs, Weno'iak/Apocalypse
  loot items), none from the packs; 3 Arbiters carry MasterLevelArbiter.
  Server left running for the owner's tests. Not yet checked in a client.
- 2026-10-24 15:15: pack set 2 reviewed and merged as 0.321.0 (14 packs).
  Live save quick_check ok (WAL, 60,505 mobs, no packs yet). Next: first boot.
- 2026-10-24 13:30: WP1.1b files merged (0.319.1); session limit handoff
  written (Next steps). Pack set 2 and the first boot are the next actions.
- 2026-10-24: WP3.1 reviewed (vault/companion wrap, dye refusal, bot pools,
  init order fixed) and merged as 0.319.0.
- 2026-10-24: WP2.2 reviewed (reward gate, spell-line removal, title fixed)
  and merged as 0.318.0.
- 2026-10-24: WP1.1a + WP2.1 reviewed (save safety verified; failed-pack
  handling, journal-mode guard and /datapacks hardening added) and merged as
  0.317.0.
- 2026-10-24: WP2.2 (toa-mlprog, 11 tests) and WP3.1 (toa-artcore, 15 tests)
  implemented; reviews running.
- 2026-10-24: WP1.1a + WP2.1 implemented by toa-datapack (20 tests); pack
  012 artifact spells added on request, review next.
- 2026-10-24: WP2.2 (ML progression) and WP3.1 (artifact core) started as
  agents on 0.316.0.
- 2026-10-24: WP3.0 reviewed (two stacking defects fixed) and merged as
  0.316.0.
- 2026-10-24: WP2.0 + WP2.3 reviewed (one Battlewarder respawn defect fixed)
  and merged as 0.315.0.
- 2026-10-24: WP3.0 built and tested by toa-artispells; WP2.0/2.3 review
  found a Battlewarder respawn defect, fix in progress.
- 2026-10-24: WP1.1a/WP2.1 (data-pack mechanism, gaheris packs) and WP1.1b
  (db-public dungeon mobs) started as agents.
- 2026-10-24: WP0.3 done (toa-import): converter + probe under `tools/toa/`,
  everything loads; WP1.1 scope widened to the dungeon import and a shipped
  migration.
- 2026-10-24: WP2.0 + WP2.3 built and tested by toa-core (full suite: the
  same 7 failures as on main); review started.
- 2026-10-24: WP0.2 done (toa-fit); WP2.0, WP2.3 and WP3.0 started as
  agents (independent of the SQL probe).
- 2026-10-24: WP0.1 done (toa-sources). Loot moves to db-public, scrolls get
  live-style drops, encounters get the DOLSharp mapping, WP3.0 added.
- 2026-10-24: pack set 2 built by toa-packs2 (packs 024, 025, 030, 031, 040,
  041, 050; 14 packs apply on the dev snapshot copy, quick_check ok); review
  next.
