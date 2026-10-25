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
- [x] **R3a Keeps.** (0.337.0; `docs/RVR_KEEPS.md`) RvR world: killing a
  neutral or enemy keep's lord flips the keep to the killer's realm at once
  (saved; guards respawn as that realm; level 1; awaiting claim; 1,500 RP to
  the capturing group/battlegroup once per keep per 30 min); claims only by
  guilds of the keep's realm (solo guild ok, bots claim automatically), never
  while the keep is in combat; Wardens hostile to all, realm keeps friend/enemy
  by realm incl. portal keeps; Darkness Falls by keep count (previous owner 15
  min grace); bots never siege own-realm keeps; open BG roles read a real
  lord state. Camlann unchanged. 72 tests.
- [x] **R3b Relics.** (0.338.0; `docs/RVR_RELICS.md`) RvR world: relics on
  realm temple pads; a relic can be lifted only when the temple's three
  first-milegate keeps are in enemy hands, the taking realm holds its own
  relic of that type and 16 enemies stand on the pad; delivery to the own
  realm's pad of the same type; realm-wide bonus (+10 % per enemy relic while
  the own one is held) for players, bots and their pets; temple guards by realm;
  no guild pads; bots target open enemy temples (march logic R4b); relic
  keeps are not capturable and return to their original realm at start. 50 tests.
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
- [x] **R5b Launcher world picker.** (0.340.0; `docs/RVR_WORLDS.md`)
  Dropdown "World: RvR (Realm vs Realm) / PvP (Camlann)" beside START SERVER,
  switchable only while the server is stopped, choice remembered per Windows
  user; one window per world, the server config gets the selected world's
  save path on switch, open and start; CREATE WORLD / RESET WORLD per world
  (old save moved to the backups, every settings writer locked meanwhile);
  WORLD label in the status line; Gothwaite controls only in PvP; today's
  Camlann save renamed to `opendaoc-camlann.sqlite3.db` (+ side files) on the
  first stopped open; importer names the world and refuses cross-world
  imports. Review ACCEPT after two fix rounds. Low follow-up for R6: REFRESH
  in the CREATE WORLD / NEEDS ATTENTION state greys CREATE WORLD out until
  the window is reopened. Launcher tests 325 passed / 2 env-skipped.
  **Wipe + first RvR boot on the owner's PC right after R5b.**
- [x] **R4a Bot guilds + login per realm.** (0.339.0; `docs/RVR_BOTS.md`)
  RvR world: 120 realm guild names in 2003 style (8 per charter per realm),
  the same charter mix in every realm (Leveling 5, RvR 4, Social 3, Hunting
  2, Keep 1), no cross-realm membership on any path, legacy R5a guilds
  renamed once; realm login balancer (`rvr_realm_active_target` 600, per-realm
  ramp, lowest-load realm first, shortfall not filled by others); grudges per
  realm (48, 3 h); companions unchanged. Open for R4b: revenge trips must be
  capped and measured. 32 tests.
  Note: built as own logic; gated off in Realm-1 since 0.342.0 (R4a login
  balancer + guild names stay on by owner decision 9).
- [x] **R4b Realm armies.** (0.341.0; `docs/RVR_ARMIES.md`) (`docs/RVR_ARMIES.md`; implemented in source,
  real-client check pending) RvR world: a whole warband of eight of one realm
  opens a siege whatever its guilds, every warband of the realm joins one realm
  army (keyed by realm, never a member of another realm); the army musters at
  the realm's own portal outpost in the keep's frontier, at home at the nearer
  border keep, with a realm chat call, and marches together (march window from
  the muster distance); targets never an own-realm keep, neutral 1.0 / enemy
  1.5 / home frontier x2; per-realm attack plan single / double / opportunist
  (open BG odds, about an hour each) sets siege slots and the third-realm
  response (after the keep fight starts, half cap); the whole realm defends
  its keeps; the live home reserve grows and stays home during an invasion
  (at least 4 enemies incl. players and companions) and hunts them, other
  warbands roam as before; relic cap 240; revenge trips capped at 2 warbands
  per target and realm with `RVR_GRUDGE_SHARE` counts; open BG units no
  longer split by keep guild; grudges against human killers resolve again
  (both worlds). Camlann unchanged otherwise. 44 tests. R4c keeps the
  upstream thresholds, timings, retake, push/detour.
  Note: built as own logic; gated off in Realm-1 since 0.342.0 (R4a login
  balancer + guild names stay on by owner decision 9).
- [x] **R4c Upstream siege-flow port.** (0.342.0-0.345.0; upstream port
  S1-S3 and R6 fixes; [RVR_PORT_MAP.md](RVR_PORT_MAP.md)) The Realm-1 world runs the
  upstream 0.38c RvR bot layer: event layer verbatim behind the facade (S1,
  0.342.0), march column and siege floor (S2, 0.343.0), relic pad, realm raids,
  town teleporters and road fallback (S3, 0.344.0), review fixes (0.345.0:
  live-clock expiry, fork march restraint off, STOP EVENT for forced raids,
  keep start level 4, guild keep defense off). Camlann keeps the fork flow.
  Deployed on the owner's PC 2026-10-25 on a fresh level-50 RvR world;
  implemented in source, real-client check pending. What upstream has and the
  port left out is the S4 backlog below.
  Scope: owner 2026-10-25 15:50: **port the
  upstream (shadowofze 0.37b+/0.38c) RvR army and siege behaviour, never
  worse than the original, invent nothing.** Source: fetch-only remote
  `upstream` (0.38c), Stefan's `docs/UPSTREAM_ADAPTATION_PLAN.md` "Deferred
  upstream work" block (realm armies, realm-owned siege/retaliation, relic
  raid activation, battleground goals, realm-only teleports, enemy name
  masking) is the R4c scope for the Realm-1 world, plus retake, 108/170
  march threshold, siege scheduling, ArmyPush/Detour, siege resurrection and
  task 139 step 2 (road-graph fallback). Where R4b invented a rule and
  upstream has one, upstream wins in RvR; Camlann keeps the fork flow. No
  reviewer per package (owner 2026-10-25 15:35), one review in R6. 2 sessions.
  S1 (event-layer core; branch rvr-port-s1, implemented in source, real-client
  check pending; details in [RVR_PORT_MAP.md](RVR_PORT_MAP.md) section 6): the
  RvR world runs upstream's event layer verbatim behind an `AutonomousRvrEventLayer`
  facade (fork layer renamed `CamlannRvrEventLayer`, Camlann unchanged): one
  siege roll a minute, one attack per realm, level 50 for every siege, muster
  at the realm's portal outpost (12+5 min, relic 25 + up to 6x10), march at
  108/170 or 85 %, arrival quorum 60 %, repel after 10 min, one retake after a
  player capture, the player-sized defense rally and caps 128/64/64 and
  240/120/120; R4a/R4b armies, home reserve, grudge trips and bot keep claims
  are gated off in RvR. The 21 frontier keeps start for their realm at
  upstream's level (an existing save moves its Warden keeps once at the next
  start); a fresh RvR world's bots start at level 50 (upstream ships no bots,
  players add "+ Lv.50" ones). Server 4,994 passed / 7 bug-160, launcher 328
  passed. Open for S2: the march column behind a march leader, siege floor,
  siege resurrection, siege parties, livelier-RvR wave guards, friend/foe sites.
  S2 (march and siege floor; branch rvr-port-s2, implemented in source,
  real-client check pending; details in [RVR_PORT_MAP.md](RVR_PORT_MAP.md)
  section 7): the RvR world's RvR turn, siege rally, frontier threat scan and
  planner are upstream's verbatim behind one world branch each: the army
  marches as one column behind its march leader (regroup holds, push after 4
  minutes stalled, detour around enemy keeps, the leader waits up to 12
  minutes at the camp edge); siege crews set up synthetic engines on the spot
  (two rams at the outer gate then the keep door, trebuchet, catapult; heavy
  ram crewed by the bots at it, no 10x test multiplier; artillery arcs);
  healers raise the army's fallen from behind the line, the dead wait 240 s
  and release at the muster; solo recruits merge into parties; keep travel
  re-plans when stalled; the frontier commit and roam anchors of the
  original planner; capture level 4, claim level 5; no RvR task below level
  20; the livelier-RvR waves (doctrine, observe, pause, stealth loop, speed,
  opportunity hunts, LFG, hub muster, danger memory, route variants, mob
  bends) are off for Realm-1 world bots, battleground crews keep the fork's.
  Friend and foe of the ported code go through PvpCombatant and the keep rule.
  Server 5,059 passed / 7 bug-160 (65 new). Open for S3: realm raids,
  ForcedRaid, RP rewards, battlegrounds, relic pad, teleporters.
  S3 (periphery; 0.344.0, details in [RVR_PORT_MAP.md](RVR_PORT_MAP.md) "S3
  done"): 8 enemies on the relic pad lift a relic (companions count; Camlann
  16); route threat awareness on (`rvr_bot_route_threat_awareness`); realm
  raids per realm with upstream rally timing (one automatic expedition per
  realm, forced raids start at 200 staged and have no time limit); bracket
  battleground tours off as upstream's 0 % default (the open BG crew in
  Thidranki stays); town teleporters of the own realm; road-graph fallback
  from our 63 meshes in both worlds (task 143). Server 5,016 passed / 7
  bug-160. Open for S4: DF route and neutral raids, the remaining raid fixes,
  the opt-in battleground goal.
- [~] **R6 Verification + docs.** Both-worlds suite, load test, log review,
  client checklist, CAMLANN.md contract, PLAY.md, task 147. 1 session.
  Done: one review over S1-S3 (REJECT 20:45: expiry never ran live, fork
  march restraint ungated, forced raids never ended, keep start level 1,
  stale texts, guild keep defense ungated): review REJECT, all blocking
  defects fixed in 0.345.0, no re-check (owner: one review) (server 5,102
  passed / 7 bug-160, launcher 338 passed / 2 skipped; [RVR_PORT_MAP.md](RVR_PORT_MAP.md) "R6 fixes").
  Fidelity, Camlann isolation, realm separation and the fresh world (1,787
  level-50 bots, keeps realm-owned) checked OK. Docs closed 2026-10-25: world
  contract in [RVR_WORLDS.md](RVR_WORLDS.md) and [CAMLANN.md](CAMLANN.md),
  player section in [PLAY.md](PLAY.md), task 147 moved to verification
  pending. Open: live observation of the first sieges on 0.345.0 (muster
  marches after 12 (+5) min, battle, repel after 10 min, retake; log lines
  RVR_MARCH_PUSH, RVR_MARCH_DETOUR, RVR_KEEP_TRAVEL_STALL,
  RVR_SIEGE_PARTY_FORMED, AUTONOMOUS_SIEGE_RESURRECTION) with the load and
  log review of that run; the owner's real-client check; the S4 backlog
  (incl. the owner decisions of 22:20 below); R5b low follow-up (REFRESH
  greys out CREATE WORLD) not fixed.

### S4 backlog (upstream 0.38c, not ported yet; RvR world only)

- [ ] **Lord shield.** Upstream Lord.cs: the lord cannot be hurt while the
  gate and keep door stand (merge against R3a).
- [ ] **No item loot from guards, gamebots and their pets** (upstream
  AbstractServerRules; check the fork's own rule first).
- [ ] **AutonomousGroupPace** (upstream group travel pace).
- [ ] **Battleground siege announcements** (left out of the S1 event layer;
  needs BattlegroundBrackets, in since S3).
- [ ] **Dashboard muster column** (AutonomousRvrDashboard MERGE: "marching in
  6m", attack window).
- [ ] **Darkness Falls route + neutral raids** (RealmRaidDungeonRoute, DF
  navigation, RealmRaidNeutralEvents, multi-hub musters). Needs certified DF
  proofs regenerated on our patched `zone249.nav`; upstream's proofs belong to
  its mesh.
- [ ] **Opt-in battleground goal** (upstream "Battlegrounds %", default 0 %).
  Owner decision first: one RvR-only setting plus the upstream executor.
- [ ] **Upstream raid refill/regroup fixes** (refill short parties, release at
  the raid hub, dungeon regroup point, slower trash respawn while a raid is
  inside, pooled forced parties, failed-post retry).
- [ ] **SupersedeAutomatic for forced raids** (a forced expedition ends the
  realm's unstarted automatic rallies).
- [ ] **BotBrain.SupportOffense MinValue overflow.** `_supportLastTurn` starts
  at `long.MinValue`, so a bot's first fight skips its per-fight roll. Camlann
  wave (off in Realm-1); same pattern as the R6 expiry fix.
- [ ] **Fork extensions.** Owner picks extensions from
  [RVR_FORK_EXTENSIONS.md](RVR_FORK_EXTENSIONS.md) to integrate (what the RvR
  world is missing compared with Camlann; built properly into the upstream
  flow, one at a time with a live check).
- [ ] **Upstream relic rules in Realm-1, retire R3b there** (owner decision 1
  below): the relic itself is the objective as upstream; the R3b temple-pad
  rule (milegate keeps, own relic of the type) stays for Camlann only.
- [ ] **Frontier porters adapted to RvR** (owner decision 2 below): keep the
  fork's porters and frontier transport, realm-bound, serving only the own
  realm's frontier.

### Owner decisions 2026-10-25 22:20

1. **Relics in Realm-1: the original upstream rule.** The relic itself is the
   objective; the R3b temple-pad rule goes to Camlann only. S4 item above.
2. **Frontier porters: keep the fork's porters, adapted to RvR** (realm-bound,
   the own realm's frontier only). S4 item above.
3. **Bot task mix and warband composition: original by default.** Fork pieces
   only where they sensibly extend the original in RvR, chosen by the owner
   from [RVR_FORK_EXTENSIONS.md](RVR_FORK_EXTENSIONS.md).
4. **PvP release immunity: keep the fork's protection in Realm-1** (approved
   added value).

**Owner decision (2026-10-25 16:10, supersedes the rule below): the RvR
world runs the original upstream RvR bot behaviour (remote `upstream`,
0.38c). "RvR wie im Original, nichts Neues erfinden, nie schlechter als das
Original. ToA als Ergänzung dazu, Fork-Code nur wo echter Mehrwert." R4a and
R4b were built as own logic against this intent; they stay in Camlann-neutral
code but are gated off in Realm-1 once the upstream flow is ported (R4c, now
the port package). Fork features in RvR need the owner's explicit "Mehrwert"
yes; default is upstream behaviour. Map: RVR_PORT_MAP.md (agent rvr-port-map).**

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
- 2026-10-25 (R3a agent): R3a in source on branch rvr-r3a, real-client check
  pending ([RVR_KEEPS.md](RVR_KEEPS.md)): a lord kill flips the keep to the
  killer's realm at once and the capture is saved (level 1, steward for an
  optional claim, lord stands again); only a guild of the keep's realm claims;
  guards show and fight as their keep's realm, portal keeps hostile to other
  realms and never captured, Wardens neutral; Darkness Falls for the realm with
  most keeps; bots claim and siege by realm; Camlann unchanged.
- 2026-10-25 11:25: R3a reviewed (2 mediums + lows fixed) and merged as 0.337.0.
  R3b, R4a, R5b running.
- 2026-10-25 11:55: R3b, R4a and R5b implemented and in review; wipe follows the R5b merge.
- 2026-10-25 (R3b agent): R3b in source on branch rvr-r3b, real-client check
  pending ([RVR_RELICS.md](RVR_RELICS.md)): relics on the realm temple pads; a
  relic can be lifted only by an enemy realm once all three keeps of its
  temple's first milegate are in enemy hands and that realm holds its own
  relic of the type; carried strength to strength, magic to magic; realm-wide
  upstream bonus for players and bots; relic-temple guards of the temple's
  realm; no guild keep mounts; bot armies pick open enemy temples (march
  logic stays R4b); Camlann unchanged.
- 2026-10-25 12:15: R3b reviewed (ACCEPT, 2 lows fixed) and merged as 0.338.0.
- 2026-10-25 (R4a agent): R4a in source on branch rvr-r4a, real-client check
  pending: RvR bot guilds get realm names (8 per charter and realm) and the same
  charter plan in every realm, R5a-era names are renamed once; recruitment,
  alt trickle, guild mergers and login binding never cross realms; realm login
  balancer keeps each realm near `rvr_realm_active_target` (600), a short realm
  is not filled by the others; grudges keyed by realm, KOS call without "crew"
  (RVR_BOTS.md). Camlann unchanged.
- 2026-10-25 12:35: R4a reviewed (ACCEPT, 2 follow-ups) and merged as 0.339.0.
- 2026-10-25 (R5b agent): R5b in source on branch rvr-r5b (worktree
  OfflineDAoC-wt-rvr-r5b), real-client check pending: World dropdown RvR / PvP
  beside START SERVER, one window per world, choice remembered per Windows
  user, config written for a ready save on switch, open and every start,
  CREATE WORLD / RESET WORLD per world, WORLD label in the status line,
  Gothwaite only in PvP, today's Camlann save renamed to its own files on the
  first stopped start, importer names the world and refuses cross-world
  imports; launcher tests 318 passed / 2 env-skipped, importer 7 passed.
- 2026-10-25 12:45: R5b re-check ACCEPT, merged as 0.340.0; deploy + RvR CREATE WORLD on the owner PC next.
- 2026-10-25 13:20: 0.340.0 deployed on the owner PC. First open done as reviewed: Camlann save renamed to
  opendaoc-camlann.sqlite3.db (PvP world, untouched), RvR world created (0 chars, 600 bots per realm, 51 portal
  and relic keeps owned, 6 relics home), server started on RvR (World model Realm-1, RvR rules, 1 data pack
  applied), ~900 bots in after 8 min, no crash. Owner asked why two saves instead of one rule switch; answered
  (settings live per save; one save with a rule switch would rebuild guilds/companions/keeps on every switch).
  Decision open: keep two saves (built) or rework. Tools: playable-dev/world-ui.ps1 (dropdown, buttons),
  playable-dev/dlg.ps1 (message boxes). Next by cron: R4b, R4c, R6.
- 2026-10-25 13:40: session limit. Agent rvr-r4b (daoc-developer) started in worktree /mnt/c/OfflineDAoC-wt-rvr-r4b, branch rvr-r4b; not reviewed, not committed. Next session: check the worktree, review (daoc-reviewer), merge as 0.341.0. R4c may go to Stefan (copy-paste brief given to the owner). Server running on the RvR world.
- 2026-10-25 15:55: R4b merged as 0.341.0 after one fix round (no re-check, owner: build first, review once in R6). R4c rescoped to an upstream port. Server on the owner PC still runs 0.340.0 RvR.
- 2026-10-25 16:15: owner: RvR must be the original code, not a rebuild ("warum wurde das ganze neu gebaut"). R4a/R4b count as misdirected work; R4c becomes the upstream port of the whole RvR bot layer for Realm-1. Analysis agent started; no code change yet. Lesson recorded.
- 2026-10-25 18:05: Port S1 merged as 0.342.0 (upstream 0.38c event layer + defense + muster + realm-owned keep start in Realm-1; fresh RvR world seeds level-50 bots; Camlann untouched). S2 (march column, ArmyPush/Detour, siege placement/resurrection, friend/foe sites) started. Deploy + RvR world reset on the owner PC after S2.
- 2026-10-25 18:50: owner: R6 tonight right after S3, no waiting; fresh RvR world with pure level-50 bots confirmed. Plan: S2 merge -> deploy + RvR world reset + server start (~19:45); S3 merge -> update; R6 = one daoc-reviewer over the whole port + 10x live run + docs (CAMLANN.md contract, PLAY.md, task 147) tonight.
- 2026-10-25 19:35: Port S2 merged as 0.343.0 (march column, ArmyPush/Detour, siege placement with rams, siege resurrection, siege parties, realm siege-job pool, livelier-RvR waves off in Realm-1). Deploy + fresh level-50 RvR world on the owner PC now. S3 running in parallel.
- 2026-10-25 20:10: Port S3 merged as 0.344.0 (relic pad 8, route threat on, raids per realm with upstream timing, road-graph fallback from our meshes in both worlds, town teleporters, forced raids per realm; bracket BG tours off as upstream). Open for S4: DF route/neutral raids (proofs for our DF mesh), opt-in BG goal (owner). Owner PC: fresh level-50 RvR world created on 0.343.0, server started; 0.344.0 update next.
- 2026-10-25 20:20: 0.344.0 deployed (backup deploy-20261025-180426), server running on the fresh level-50 RvR world (zone roads 63 loaded, world model Realm-1, only pre-existing ERR noise). R6 reviewer running over S1-S3. Owner may log in.
- 2026-10-25 20:45: R6 review REJECT: (1) RealmRvrEventLayer Expire never runs live (_lastExpireTick MinValue overflow) so sieges open but never march; (2) fork march restraint (AutonomousSiegeMarch) ungated in Realm-1; (3) forced realm raids never end; (4) starting_keep_level 1 vs upstream 4; stale reset texts; guild keep defense ungated. Fidelity, Camlann isolation, realm separation, fresh world (1,787 level-50 bots, keeps realm-owned) all OK. Fix agent rvr-r6fix started; 0.345.0 + redeploy tonight. S4 list: lord shield, no item loot from guards/bots, GroupPace, BG siege announcements, dashboard muster column, DF route, opt-in BG goal; owner decisions: relic temples, frontier porters, task mix, release immunity.
- 2026-10-25 21:30: R6 fixes merged as 0.345.0 (live-clock expire, march restraint off in Realm-1, forced-raid STOP EVENT as upstream, keep start level 4 in Realm-1, texts, guild keep defense off). Deploy + restart on the owner PC. Remaining: S4 list, owner decisions (relic temples, frontier porters, task mix, release immunity), R6 docs (CAMLANN.md contract, PLAY.md, task 147 close) and a live observation of the first sieges.
- 2026-10-25 21:45: R4c closed (0.342.0-0.345.0). R6 docs done (world contract in RVR_WORLDS.md and CAMLANN.md, PLAY.md world section, task 147 and ToA task 134 to verification pending, TOA_PLAN WP4.3 done); S4 backlog and open owner decisions listed above. R6 stays open for the live siege observation and the owner's client check.
- 2026-10-25 22:40: R6 docs merged as 0.345.1. Live on the owner PC (0.345.0): first upstream-flow siege, Albion marched at 21:15 with 72 attackers after the muster deadline; arrival and battle being watched.
