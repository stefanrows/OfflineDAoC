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

- [~] **WP0.1 Supplementary sources beyond gaheris.** What else exists that
  gaheris does not carry: DOLSharp per-artifact quests (`quests/Atlantis/Artifacts/*`),
  db-public loot/quest/merchant rows for Atlantis, SWDB, Allakhazam-style
  drop and encounter mappings, newer OpenDAoC-Core ToA commits, ML trial
  designs anywhere. Output: a ranked list with licence-free fetch paths,
  appended to TOA_RESEARCH.md. Agent: research (2026-10-24).
- [~] **WP0.2 Fit of the gaheris scripts to our server.** Clone gaheris,
  compile-check its `scripts/toa` and `scripts/progression` against our
  `source/server` (APIs, events, renamed tables, ECS effects), list every
  gap with the fix. Output: `docs/TOA_GAHERIS_FIT.md`. Agent: advisor
  (2026-10-24).
- [~] **WP0.3 SQL import probe.** Convert the gaheris Atlantis/ML migrations
  (12, 13, 14, 28, 29, 35, 36, 39, 42, 43) from MySQL to SQLite and load them
  into a copy of `playable-dev/db-snapshot/opendaoc.snapshot.db`; count rows
  loaded, rejected, and columns our schema lacks. Output: converted SQL under
  `tools/toa/sql/` plus `docs/TOA_IMPORT_PROBE.md`. Agent: developer
  (2026-10-24).

### Phase 1: Atlantis exists

- [ ] **WP1.1 Atlantis population.** Load the converted mob, NPC, portal and
  zone-point rows into the runtime DB path used by deploys; Arbiter, scholars,
  Demyphon, binders in place. Depends on WP0.3.
- [ ] **WP1.2 Travel.** Open Atlantis to players (`atlantis_teleport_plvl` 1),
  Djinn/boat/teleport rows working from each realm's hub; add an
  `[Atlantis]` line to our porter menu. Depends on WP1.1.
- [ ] **WP1.3 Loot.** `rog_toa_item_chance` > 0 inside Atlantis regions; the
  77 db-public loot links imported; named ToA bosses get drop tables for the
  ToA item sets we hold (mapping from WP0.1). Depends on WP1.1.
- [ ] **WP1.4 Launcher.** Atlantis on/off switch and ML/artifact XP rates on
  the launcher settings page (server properties). Independent.

### Phase 2: Master Levels

- [ ] **WP2.1 ML data.** The 80 ML spells, eight lines, class paths
  (gaheris 12/39/42/43) in our DB; `/train` shows the paths. Depends on WP0.3.
- [ ] **WP2.2 ML progression.** gaheris Arbiter (group enrolment, discipline
  choice, respec via Demyphon) and `MasterLevelExperience` (level 45+ kills,
  `ml_xp_per_level`) adapted to our server; owner rule: no Atlantis
  restriction. Depends on WP0.2.
- [ ] **WP2.3 ML ability fixes.** gaheris `MasterLevelHolds` (Lookout, Blanket
  of Camouflage, Battlewarder, Focusing Winds) checked against our event
  model; Forceful Zephyr / Phaseshift decision. Depends on WP2.2.
- [ ] **WP2.4 Bots and ML.** Decide whether 50 bots train ML lines (companions
  at least). Design note first; not before WP2.2.

### Phase 3: Artifacts

- [ ] **WP3.1 Artifact core.** gaheris `ArtifactMgr`, `InventoryArtifact`,
  `ArtifactCompat` adapted; artifacts activate, level on kills
  (`ArtifactExperience`), unlock bonuses per level. Depends on WP0.2.
- [ ] **WP3.2 Scrolls and turn-in.** Binder merchants with the 54 scroll
  sets, scholars, `ArtifactTurnInQuest`; the three artifacts without sets
  (Flask, Shield of Khaos, Bruiser) get generated sets. Depends on WP3.1, WP1.1.
- [ ] **WP3.3 Encounters.** Credit per artifact: gaheris encounter mobs
  (doppleganger, splitter, seedsman, reward) placed in the right zones, one
  named mob per artifact where WP0.1 supplies the mapping. Depends on WP3.2.
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
