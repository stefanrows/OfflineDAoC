# RvR mode: implementation plan and to-do list

Owner decisions: [RVR_MODE_ANALYSIS.md](RVR_MODE_ANALYSIS.md) sections 5 and
"Further owner decisions". Spec: `docs/superpowers/specs/2026-10-24-rvr-mode-design.md`
(written by the Phase 0 agent; refine this list from it). Night session
2026-10-24/25 authorised for everything on the owner's PC incl. wiping the
current save and creating the RvR world as the default; the Camlann PvP
world becomes a working launcher button with its own fresh save.

Status legend: `[ ]` open · `[~]` running (agent) · `[x]` done (version) ·
`[!]` blocked

## Packages (from the spec; one developer + one reviewer per package)

Spec decision: both worlds run GameType=PvP; a `Realm-1` marker selects a new
`RvrServerRules` (subclass of PvPServerRules) with colour handling 0 and
realm-only group/guild/trade/battlegroup/speech. RvR save at the existing DB
path, Camlann at `opendaoc-camlann.sqlite3.db` plus per-world side files; the
launcher writes the connection string and resets per world. No RvR data pack:
defaults in code, keep/relic start state from the reset.

- [x] **R0 Spec.** `docs/superpowers/specs/2026-10-24-rvr-mode-design.md`
  (2026-10-25 00:15).
- [x] **R1a World marker + rules class.** (0.332.0) Saves declare their world
  (`Camlann-1` / `Realm-1`, GameType stays PvP for both, unknown markers
  refused); `WorldMode` helper with a startup latch; `RvrServerRules :
  PvPServerRules` selected for Realm-1: colour handling 0, true realm byte,
  realm-only group/trade/speech/guild join, dummy-guild trick off; realm-only
  invites for bots, battlegroups, guild alliances. Camlann unchanged.
  **RvR not playable yet:** the alliance core is R1b.
- [x] **R1b Alliance core, client, GST_PvP audit.** (0.334.0) In the RvR
  world the same realm is allied everywhere and other realms are enemies;
  guild, group, battlegroup, open BG rule and companion memory never bridge
  realms; pets, companions and `/spawn` helpers fight as their owner's realm;
  realm NPCs are friendly only to their realm; keep hostility by realm
  (Wardens hostile to all, portal keeps until R3a); enemy names hidden as on
  1.65; bind and craft by realm; audit of 54 server-type sites in
  `docs/RVR_RULES_AUDIT.md` (19 + 4 gated, 1 + 3 deferred). Camlann unchanged.
  Open before the first RvR boot: R2 travel lock and per-realm safe areas.
- [x] **R2 Travel + population rules.** (0.336.0; `docs/RVR_TRAVEL_POPULATION.md`)
  RvR world: porters, `/tc`, frontier porters, BG release and the Realm
  Exchange serve only the own realm; `/spawn`, `/classes`, recruiting and
  stored-companion invites own realm only; bot parties same-realm (4
  coordinator gates + Group.AddMember backstop); world bots stay out of enemy
  homelands (upstream rule; a bot already inside walks out, invalid login spot
  sends it home); open BG forced perrealm with per-realm landing peace;
  Gothwaite off; enemy-land binds reset on release; per-realm safe hubs: own
  realm safe in its hub, an intruder attackable, no attacking out of cover;
  capitals/housing/newbie dungeons still block attackers. 53 + 8 tests.
- [~] **R3a Keeps.** Implemented (worktree rvr-r3a, 54 tests), in review (10:45). Neutral Warden start, capture by realm sticks, claims by
  the keep's realm (solo guild ok), guards by owner realm. 1 session.
- [~] **R3b Relics.** Agent rvr-r3b (2026-10-25 10:45, parallel to the R3a review). Realm pads, realm-wide bonus, relic sieges. 1 session.
- [x] **R5a World reset + files.** (0.335.0; `docs/RVR_WORLDS.md`) One
  reset routine creates a fresh RvR or Camlann world: seed copy, progress
  cleared in one transaction (characters, inventories, bots, companions,
  guilds, other accounts, exchange, sponsor tokens, hookpoint items; local
  account kept and set to PrivLevel 1), integrity check, then the old save
  and all side files MOVE to `playable-backups/<timestamp>-<world>-world-reset/`.
  Marker `Realm-1`/`Camlann-1`, today's rates, 600 bots per realm (RvR:
  single-realm guilds, 45; Camlann: 15 mixed), RvR keeps neutral except 45
  portal + 6 relic keeps, relics home. Files: RvR `opendaoc.sqlite3.db`,
  Camlann `opendaoc-camlann.sqlite3.db` + `-camlann` side files;
  `CamlannServerConfig.EnsureWorld` writes the connection string. Gothwaite
  switch locked for a Realm-1 save. Launcher tests 297, server fixtures 19.
- [~] **R5b Launcher world picker.** Agent rvr-r5b (2026-10-25 10:30). Owner 2026-10-25 10:05: a **dropdown**
  "World: RvR (default) / PvP (Camlann)", switchable only while the server is
  stopped, choice remembered; START SERVER starts the selected world (the
  launcher writes that world's save path into the server config); each world
  keeps its own save, switching loses nothing; "Reset world" per world (old
  save moved to backups, fresh world created, offered when a world's save is
  missing); active world shown in the status line; Gothwaite controls only in
  the PvP world; import guard accepts `Realm-1`. 1 session. **Wipe + first RvR
  boot on the owner's PC right after R5b.**
- [~] **R4a Bot guilds + login per realm.** Agent rvr-r4a (2026-10-25 11:05). ~600 bots per realm in
  single-realm guilds, realm login balance. 1 session.
- [ ] **R4b Realm armies.** Event layer armies per realm instead of per guild,
  muster at the realm's portal outpost, grudges/banter per realm. 2 sessions.
- [ ] **R4c Upstream siege-flow port.** Retake, 108/170 march threshold, siege
  scheduling, ArmyPush/Detour, siege resurrection from upstream 0.33-0.37.
  2 sessions. After a fetch of Stefan's branch.
- [ ] **R6 Verification + docs.** Both-worlds suite, load test, log review,
  client checklist, CAMLANN.md contract, PLAY.md, task 147. 1 session.

**Owner rule (2026-10-25 00:25): every bot extension the fork built carries
over into the RvR world, realm-based.** That means: companions (roster, squads,
gear, stages, companion ML), world bots (PvE grind, towns, economy, pickup
groups now same-realm, danger memory, attributes, equipment, bot ML), the
autonomous RvR layer (raids as battlegroups, keep sieges, attack plans, live
reserve, danger memory, grudges/banter) rebuilt per realm instead of per
guild, the open BG with its presets, attack orders and the OPEN BG window in
`perrealm` mode, siege operators, raid groups. Nothing is dropped; only the
friend/enemy key changes from guild/crew to realm. R4a-R4c are measured
against this list.

Total about 12 sessions. Order: R1a → R1b → R2 → R3a → R3b → R5a → R5b
(wipe, boot) → R4a → R4b → R4c → R6.

## Log

- 2026-10-24 23:55: plan created from the analysis and the owner's decisions;
  spec agent running; Majestic Will 1.68 started as a side package.
- 2026-10-25 00:15: R0 spec done (12 sessions, GameType stays PvP with an
  RvrServerRules subclass); packages refined; R1a started.
- 2026-10-25 01:05: R1a implemented and in review; R5a started in parallel so the
  PvP button lands early; R1b follows the R1a merge.
- 2026-10-24 23:55 (local clock): R1a in source on branch rvr-r1a, real-client
  check pending: `Realm-1` marker accepted next to `Camlann-1` (no marker
  still refused), `WorldMode` helper, `RvrServerRules` selected by the marker
  (colour handling 0, true realm byte, realm-only group/guild/alliance/
  battlegroup/trade/speech), dummy-guild trick off in RvR; Camlann unchanged.
- 2026-10-25 01:20: R1a reviewed (ACCEPT) and merged as 0.332.0. R1b started with
  the reviewer's complete gap list (alliance core, battlegroups, enemy names
  hidden, GST_PvP audit); R5a running.
- 2026-10-25 01:50: R1b implemented and in review; R2 started in parallel; R5a running.
- 2026-10-25 02:00: usage limit reached mid-night. R1b in review, R2 and R5a
  implemented/running. Launcher ToA block restored (0.333.0). The hourly
  fallback job continues the package list after the reset.
- 2026-10-25 09:35: the PC slept 01:13-09:32 (hibernate), the night produced only
  R1a/0.333.0. Standby disabled on mains. Owner: continue the night plan now,
  same authorisations (wipe after R5b without asking).
- 2026-10-25 (R1b agent): R1b in source on branch rvr-r1b, real-client check
  pending: same realm allied everywhere in the RvR world (players, bots,
  companions and helpers as their owner's realm, pets, town guards, claimed
  keeps; Wardens hostile to all); guild, group and battlegroup never bridge
  realms; enemy names, guilds and titles hidden as on 1.65 (bots too); bind and
  craft by realm; server-type audit 35 + 14 + 5 sites in RVR_RULES_AUDIT.md;
  Camlann unchanged. Mixed bot crews and groups fight themselves until R4a.
- 2026-10-25 09:50: R1b reviewed (ACCEPT, 3 lows fixed) and merged as 0.334.0.
  R5a in review, R2 running.
- 2026-10-25 00:40 (system clock): R5a implemented in worktree rvr-r5a (not
  merged): one reset routine for both worlds, per-world save and side files,
  45 single-realm bot guilds in RvR; docs/RVR_WORLDS.md; server 4638 passed /
  7 bug-160, launcher 297 passed / 0 failed. Review fixes: local account to
  privilege level 1, rollback names the backup folder, orphan sweep, camp
  sponsors and hook-point items cleared, Gothwaite refused in RvR saves.
- 2026-10-25 10:25: R5a reviewed (PrivLevel reset and 4 lows fixed) and merged
  as 0.335.0. R5b (world dropdown) starts now; R2 and R3a in review/progress.
- 2026-10-25 (R2 agent): R2 in source on branch rvr-r2, real-client check
  pending: porters, frontier porters, Realm Exchange and battleground landings
  per realm, `/spawn` and companion recruiting own realm only, parties and bot
  pickup groups same realm, bots kept out of enemy homelands, open BG forced
  per realm, Gothwaite off, enemy-realm binds reset (RVR_TRAVEL_POPULATION.md);
  safe hubs (portal keeps, border hubs, landings) protect only their own realm.
- 2026-10-25 11:00: R2 reviewed (hub sniping + 4 lows fixed) and merged as 0.336.0.
  R3a in review, R3b and R5b running.
