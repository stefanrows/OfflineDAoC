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
  `/datapacks apply` or a new file, refused on journal_mode off/memory,
  SQLite only; deploy and release pack carry `runtime/server/data-packs`.
  Packs: 010 ML spells (73 of 80 ranks), 011 ML paths, 012 artifact spells
  (73 rows), 020 Atlantis mobs (41,727), 021 travel, 022 zone points, 023
  scroll binders. Not shipped: gaheris 28/35 (portals, Arbiter/NPC rows)
  until the NPC classes land (WP2.2/3.1b); 35 NPCs in pack 020 load as plain
  GameNPC until then. Follow-up: pack 050 artifact ClassType (from WP3.1),
  WP1.1b dungeon packs.
- [~] **WP1.1b db-public dungeon mobs + ToA loot.** Agent toa-dungeons
  (2026-10-24): Cetus' Pit, Sobekite Eternal, Temple of Twilight, Halls of
  Ma'ati, Deep Volcanus, Great Pyramid, Celestius plus the 1,876 db-public
  loot rows as packs 030/031/040.
- [ ] **WP1.2 Travel.** Open Atlantis to players (`atlantis_teleport_plvl` 1),
  Djinn/boat/teleport rows working from each realm's hub; add an
  `[Atlantis]` line to our porter menu. Depends on WP1.1.
- [ ] **WP1.3 Loot.** Import db-public's ToA loot (1,876 LootTemplate rows,
  1,423 items; 7 items missing from our ItemTemplate to add); then
  `rog_toa_item_chance` > 0 inside Atlantis regions as filler. Depends on WP1.1.
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
- [~] **WP2.2 ML progression.** Implemented in source (worktree toa-mlprog,
  11 tests, `docs/TOA_MASTER_LEVELS.md`), in review: Arbiter enrols the group
  (level 40+, 2000 units) at ML1 without discipline; exclusive discipline by
  fixed MLLine index; ML XP = mob level x `ml_xp_per_level` for every earner
  on level 45+ NPC kills, level granted in the field; Demyphon redeems the
  next credit, Star of Destiny resets the line; `/ml`; GM
  startml/mlxp/setml/setmlline rewired. Visible only with WP2.1 data. gaheris Arbiter (group enrolment, discipline
  choice, respec via Demyphon) and `MasterLevelExperience` (level 45+ kills,
  `ml_xp_per_level`) adapted to our server; owner rule: no Atlantis
  restriction. Depends on WP0.2.
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
- [ ] **WP2.4 Bots and ML.** Decide whether 50 bots train ML lines (companions
  at least). Design note first; not before WP2.2.

### Phase 3: Artifacts

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
- [~] **WP3.1 Artifact core.** Implemented in source (worktree toa-artcore,
  15 tests, `docs/TOA_ARTIFACTS.md`), in review: ArtifactMgr/InventoryArtifact
  under gameutils/Atlantis, bonus gating in the item getters (no template
  clone), level from item Experience, XP from GainedExperience (NPC/Player
  sources only, DOL formula, 10 levels at 50 M), generic encounter credit per
  artifact, Ceremonial Bracer + Cloudsong one-liners, ClassType SQL
  `tools/toa/sql/050-artifact-item-classtype.sql` (197 templates; to become a
  pack). 3 ArtifactXItem rows have no template. gaheris `ArtifactMgr` (+ an `Init` caller),
  `InventoryArtifact` (+ stat refresh), `ArtifactCompat` adapted; artifact
  item templates get `ClassType` InventoryArtifact by SQL, otherwise they
  reload as plain items with every bonus active; artifacts level on kills
  (`ArtifactExperience`), unlock bonuses per level. Depends on WP2.0.
- [ ] **WP3.0b Missing artifact handlers.** Write the 7 handlers no project
  has (Crocodile Tear Ring, Kalare's Necklace regen buff, Ring of Unyielding
  Will, Gem of Lost Memories, Cloudsong/Crown of Zahur auras and endu proc,
  Bracer of Zo'arkat/Phoebus' Harp aura) from Allakhazam/KB delve text; the
  4 spell IDs that exist nowhere (Spear of Kings, Malice's Axe, Shield of
  Khaos, Scepter of the Meritious) get rows written from delve text. ~1 session.
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
