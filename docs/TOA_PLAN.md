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

- [ ] **WP1.1 Atlantis population.** Ship a DB migration that loads files
  13, 14, 29, 36 (mobs, travel, zone points, scroll binders) into the player's
  save at deploy/first start; files 28/35 follow with their NPC classes. Add
  the db-public ToA dungeon mobs (Cetus' Pit, Sobekite Eternal, Temple of
  Twilight, Halls of Ma'ati, Deep Volcanus, Great Pyramid, Celestius), which
  gaheris does not carry. Check region load/memory with 41.7k extra mobs
  (`region_max_objects` 30,000, largest region 14.6k). Depends on WP0.3.
- [ ] **WP1.2 Travel.** Open Atlantis to players (`atlantis_teleport_plvl` 1),
  Djinn/boat/teleport rows working from each realm's hub; add an
  `[Atlantis]` line to our porter menu. Depends on WP1.1.
- [ ] **WP1.3 Loot.** Import db-public's ToA loot (1,876 LootTemplate rows,
  1,423 items; 7 items missing from our ItemTemplate to add); then
  `rog_toa_item_chance` > 0 inside Atlantis regions as filler. Depends on WP1.1.
- [ ] **WP1.4 Launcher.** Atlantis on/off switch and ML/artifact XP rates on
  the launcher settings page (server properties). Independent.

### Phase 2: Master Levels

- [~] **WP2.0 Core hooks.** Implemented in source (worktree toa-core,
  2026-10-24, 4 tests): Dying carries all player attackers; `GainExperience`
  raises `GainedExperience` with the pre-rate arguments. In review, then merge.
- [ ] **WP2.1 ML data.** The 80 ML spells, eight lines, class paths
  (gaheris 12/39/42/43) in our DB; `/train` shows the paths. Depends on WP0.3.
- [ ] **WP2.2 ML progression.** gaheris Arbiter (group enrolment, discipline
  choice, respec via Demyphon) and `MasterLevelExperience` (level 45+ kills,
  `ml_xp_per_level`) adapted to our server; owner rule: no Atlantis
  restriction. Depends on WP0.2.
- [~] **WP2.3 ML ability fixes.** Implemented in source (worktree toa-core,
  2026-10-24, 15 tests): `MovementWatch` in gameutils; Lookout, Blanket of
  Camouflage (own effect per member, now a positive spell, stealthers follow
  normal rules), Battlewarder (warder now actually planted), Focusing Winds
  as ECS effects that end on move/attack/cast. Forceful Zephyr / Phaseshift
  untouched. In review, then merge; castable only after WP2.1 data.
- [ ] **WP2.5 ML trial texts (optional, later).** Our tree holds 90 ML step
  texts (ML1-9 x 10 steps); DOLSharp has one scripted step (Ianetor ML1.1,
  980 lines, no credit wiring). Only if the owner wants named steps on top of
  kill-XP. Not before Phase 3.
- [ ] **WP2.4 Bots and ML.** Decide whether 50 bots train ML lines (companions
  at least). Design note first; not before WP2.2.

### Phase 3: Artifacts

- [~] **WP3.0 Artifact spell handlers.** Port the 30 handlers in
  OpenDAoC-Core `GameServer/spells/Artifacts` (our tree has only the
  eSpellType values). Agent toa-artispells (2026-10-24).
- [ ] **WP3.1 Artifact core.** gaheris `ArtifactMgr` (+ an `Init` caller),
  `InventoryArtifact` (+ stat refresh), `ArtifactCompat` adapted; artifact
  item templates get `ClassType` InventoryArtifact by SQL, otherwise they
  reload as plain items with every bonus active; artifacts level on kills
  (`ArtifactExperience`), unlock bonuses per level. Depends on WP2.0.
- [ ] **WP3.1b Earning artifacts, data-driven.** Our Artifact rows use
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
- [ ] **WP4.2 Stefan's server.** Properties and DB migration notes for his
  install; release pack includes the ToA SQL.
- [ ] **WP4.3 Docs.** CAMLANN.md era note, PLAY.md section, TASKS 134 closed.

## Out of scope (owner or client limits)

- Labyrinth of the Minotaur (not in the client).
- Live-accurate ML trials (exist nowhere; kill-XP instead).
- Underwater breath/swim mechanics beyond what the server already does
  (check during WP4.1, fix only if broken).

## Log

- 2026-10-24: plan created; WP0.1-0.3 started as agents.
- 2026-10-24: WP0.3 done (toa-import): converter + probe under `tools/toa/`,
  everything loads; WP1.1 scope widened to the dungeon import and a shipped
  migration.
- 2026-10-24: WP2.0 + WP2.3 built and tested by toa-core (full suite: the
  same 7 failures as on main); review started.
- 2026-10-24: WP0.2 done (toa-fit); WP2.0, WP2.3 and WP3.0 started as
  agents (independent of the SQL probe).
- 2026-10-24: WP0.1 done (toa-sources). Loot moves to db-public, scrolls get
  live-style drops, encounters get the DOLSharp mapping, WP3.0 added.
