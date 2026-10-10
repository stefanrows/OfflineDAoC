# Changelog

All notable changes to this fork are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this fork uses [MAJOR.MINOR.PATCH](https://semver.org/). See `AGENTS.md`
for when to bump each number.

The launcher pin `DisplayVersion` must match the latest dated heading below.
The upstream playable download remains GitHub **v0.3**; that is the runtime
package, not this fork's version.

## [Unreleased]

## [0.232.0] - 2026-10-10

### Added

- Eden-style battlegrounds, keeping Camlann guild/group alliances (task 113).
  Server-built central keeps for Proving Grounds (tower), Lion's Den, Hills of
  Claret (Caer Claret), Thidranki and Wilton (new keep rows 140–144), and
  walls/gates for Killaloe, Molvik and Leirvik's existing rows, from the
  `/keep fastcreate` battleground layouts. Rows and components are added only
  where none exist; corrupt KeepPosition offsets are skipped.
- Keep lords and retainers spawn only on navigation-proved points behind
  closed gates. Battleground keeps stay at level 1 (no guild progression).
- Battleground navmeshes bake the real New Frontiers keep pieces from the
  client, with door volumes for every server gate. Sites come from a
  flat-ground finder (`tools/dev/battleground-keeps.json`); Thidranki uses a
  relaxed slope limit at its lowest ground point.
- Bracket-eligible autonomous RvR bots buy the free battleground medallion,
  board real porters and fight over the keep (defend, claim, assault without
  rams, roam), up to 24 per map within a 40-actor cap shared with patrols.
  They graduate out to their capital when they outlevel the bracket.
  Bots of every realm can join: each frontier porter has a native medallion
  merchant, and startup re-adds Midgard's Gwulla beside Stor Gothi Annark
  if a save lacks it.
- A real guild holding a battleground keep with a living lord earns 2% level
  XP every 10 gameplay minutes for members present (and 2 tokens for humans).
  Captures are announced region-wide and `/bgs` shows the holder.

### Changed

- Development docs describe the keep-aware battleground navmesh build. The
  ten rebuilt meshes must be deployed with `-NavmeshBuild` for keep walls to
  block movement.
- Server and Tests Release builds pass with zero errors; the nav builder
  builds and all ten meshes validate. Automated tests were skipped;
  real-client checks (keep rendering, wall alignment, lord gating, bot
  arrival/claim/graduation, holding rewards) remain pending.

## [0.231.0] - 2026-10-10

### Added

- Battleground diagnostics: `BATTLEGROUND_CAMPAIGN_READY`, `_OCCUPANCY`,
  `_SQUAD_SPAWNED`/`_SQUAD_SKIPPED` (with reason), `_TICK_SLOW` phase timings,
  `_CAMP_DOOR_ROUTE` and `_NATIVE_KEEP_READY`/`_UNAVAILABLE`.
- Murdaigean (25–29) gains keep row 139 at its native client keep. Its two
  existing gates close at full health, and a lord with up to four retainers
  is placed only on navigation-proved points behind those gates. The row,
  gate reset and garrison are additive and idempotent; existing saves load.

### Changed

- Battleground patrols now walk from a camp toward an occupied human
  position outside sanctuaries (or the camp nearest them while they stand in
  one), and a newly occupied map schedules its first patrol within about
  15 seconds. Ambushes start from the camp nearest their target. Patrols
  re-path only when their participant moves away; sieges are unchanged.
- Campaign timers, first patrols and ambushes are staggered per map, and at
  most one camp captain spawns per map per tick.
- The autonomous realm boundary now covers every campaign battleground region.
- Hills of Claret, Proving Grounds, Lion's Den, Thidranki and Wilton stay
  keepless: their client zone data has no placed keep. Server-built keeps
  and autonomous gamebot participation follow in 0.232.0.
- Server and Tests Release builds passed with zero errors; existing warnings
  remain. Automated tests were skipped; real-client checks remain pending.

### Fixed

- Unsponsored battleground camps no longer restore a sponsor expiry after a
  restart, which reset every camp and respawned 27 captains in one tick with
  multi-second timer stalls (bug 125).
- Murdaigean camp anchors match their portal keep by area and accept the
  ordinary route when the native portal gates are closed (bug 122).

## [0.230.0] - 2026-10-10

### Changed

- Active Population companion delete is limited to companions owned by this
  installation's account (from account.txt) and orphaned companions whose
  owning character no longer exists. A friend's companions keep the delete
  button and menu item disabled, and the delete transaction re-checks
  ownership. Orphaned companions now show "No owner (orphan)" in the Zone
  column instead of "With owner".
- Server and Windows launcher Release builds pass. Automated tests were skipped.
  Pushed directly to fork main and deployed on 2026-10-10: 17 files replaced
  with verified backups; accounts, database and settings unchanged. Backup:
  `D:\Games\OfflineDAoC-backups\deploy-20261010-134149`. The installation
  remains stopped. Real-client verification is pending.

## [0.229.1] - 2026-10-10

### Fixed

- Saving a rate key that was not yet in the database (for example AUTONOMOUS
  BOT RP) no longer fails with "UNIQUE constraint failed:
  ServerProperty.ServerProperty_ID". The launcher wrote an empty-string ID for
  new rows, so only one launcher-created key could exist. New rows now store
  NULL like server-created rows, and any existing empty-string ID is
  normalized on save. The launcher test fixture now has the real unique index.
- Server and Windows launcher Release builds pass. Automated tests were skipped.
  Pushed to fork main and deployed on 2026-10-10: 17 files replaced; accounts,
  database and settings unchanged. Backup:
  `D:\Games\OfflineDAoC-backups\deploy-20261010-133848`. The installation
  remains stopped; the GitHub release was not published.

## [0.229.0] - 2026-10-10

### Added

- Launcher XP Settings add YOUR PLAYER RP (`rp_rate`) and AUTONOMOUS BOT RP
  (new `bot_rp_rate`) selectors with the same 1×–100× choices as XP. They are
  editable only while the server is stopped.

### Changed

- Autonomous bots scale realm point awards by `bot_rp_rate`. Persistent
  companions follow the player `rp_rate`, like their XP. Previously all bot RP
  ignored rate multipliers. The default 1× leaves rewards unchanged.
- Progress import resets the RP rates to 1× along with the XP rates and GM option.
- Server and Windows launcher Release builds pass. Automated tests were skipped.
  Pushed directly to fork main and deployed on 2026-10-10: 17 files replaced
  with verified backups; accounts, database and settings unchanged. Backup:
  `D:\Games\OfflineDAoC-backups\deploy-20261010-133410`. The installation
  remains stopped. GitHub release v0.229.0 was packaged locally but not
  published. Real-client verification is pending.

## [0.228.0] - 2026-10-10

### Added

- Fork releases: `tools/release/build_fork_release.py` packages the Release
  server and launcher builds, the ten battleground navmeshes and the Companion
  Manager / raid click-fix client files as an update pack for an Offline DAoC
  v0.3 folder, every file pinned by SHA-256. `publish-fork-release.sh`
  publishes it as GitHub release `v<version>` on the fork with the changelog
  section as notes; no GitHub Actions are involved.
- `UPDATE OFFLINE DAOC.cmd` downloads the latest fork release, verifies its
  SHA-256, refuses to run while the game, server or launcher is open, backs up
  every replaced file under `update-backups` with a `-RestoreBackup` undo, and
  rolls back on any failure. Native client files are replaced only over a
  known v0.3 or fork build. Saves, `account.txt`, server config and bot
  settings are never touched.

### Changed

- The "ship now" workflow in `AGENTS.md` now packages the same build and
  publishes the fork release after a successful push.

### Fixed

- None.

### Removed

- None.

## [0.227.0] - 2026-10-10

### Added

- Active Population: DELETE CHARACTER… and the grid context-menu delete now
  permanently delete a selected persistent companion with its equipment and
  backpack items. This works only while the server is stopped; the launcher
  re-checks server state inside the delete transaction. While the server runs,
  the button and menu item are disabled, and the Companion Manager in-game is
  the way to delete a companion. World bots and DELETE ALL BOTS are unchanged.

### Changed

- Server and Windows launcher Release builds pass. Automated tests were skipped.
  Pushed directly to fork main and deployed on 2026-10-10: 17 files replaced
  with verified backups; accounts, database and settings unchanged. Backup:
  `D:\Games\OfflineDAoC-backups\deploy-20261010-132203`. The installation
  remains stopped. Real-client verification is pending.

## [0.226.0] - 2026-10-10

### Fixed

- The launcher's Realm Events tab now shows a one-click "Reset all keeps &
  relics" button. The existing reset (clear every keep's guild claim, return
  all six relics to their temple shrines, small JSON backup of the keep/relic
  rows) had been unreachable since its old panel stopped being displayed. It
  asks for confirmation, works only while the server is fully stopped, and
  does not change characters, bots, inventories, coins, Realm Exchange or
  event records. Task 110 awaits a launcher check on Windows; automated tests
  were skipped.

## [0.225.0] - 2026-10-10

### Added

- Launcher XP Settings offer 15×, 20×, 25×, 50×, and 100× for both
  YOUR PLAYER XP and AUTONOMOUS BOT XP (still editable only while the server
  is stopped).

### Fixed

- Persistent companions now earn PvP experience from qualifying damage on
  real-player and autonomous-bot kills. They use the same formula as autonomous
  bots, scaled by the player XP rate and PvP danger multiplier and capped at the
  owner's total experience. Previously they earned only realm points, so they
  fell behind the player in PvP. Loot-owner eligibility is unchanged.

### Changed

- AUTONOMOUS BOT XP description now says companions follow the player XP rate.
- Server and Windows launcher Release builds pass. Automated tests were skipped.
  Pushed directly to fork main and deployed on 2026-10-10: 17 files replaced
  with verified backups; accounts, database and settings unchanged. Backup:
  `D:\Games\OfflineDAoC-backups\deploy-20261010-131728`. The installation
  remains stopped. Real-client verification is pending.

## [0.224.1] - 2026-10-10

### Added

- Server `ProcessPriority` XML setting: Normal, BelowNormal (default), or Idle.
  Missing/invalid settings use BelowNormal; invalid values log a warning.

### Fixed

- Windows startup applies process priority after logging initializes and before
  server initialization, giving normal-priority desktop apps scheduling
  preference over server work. Log the applied priority; API failures warn and
  allow startup to continue. Non-Windows hosts skip the change. No bot/NPC
  cadence or individual thread priority changes.
- Server and Windows launcher Release builds passed with zero errors; warnings
  remain. Automated tests were skipped. Bug 124 awaits Windows
  priority/responsiveness and real-client bot verification.

### Changed

- Ship all pending source/documentation changes together as 0.224.1, including
  AssistTrain and the ordinary crowd-control auto-assist fix. Pushed directly
  to fork main and deployed the same Release server/launcher outputs on
  2026-10-10: 17 files replaced with verified backups; protected accounts,
  database and settings unchanged. Backup: `D:\Games\OfflineDAoC-backups\deploy-20261010-101806`.
  No components were running at deployment; the installation remains stopped.
  Generated test scratchpad language copies were preserved locally and excluded
  from the source commit. Automated tests and post-deploy monitoring skipped;
  real-client verification remains pending.

## [0.224.0] - 2026-10-10

### Fixed

- Aggressive and Defensive companions no longer treat the human leader's mez,
  root, stun, or pure debuff casts as assist orders. Cast notifications, spell
  attack events, shared target polling and idle/follow polling now require
  damage casts or weapon attacks. Cast polling uses the spell's captured target,
  so selecting another enemy during a cast does not redirect assistance.
- Retain retaliation, tank peeling, area-damage selection, defensive ranges,
  explicit pulls, human-pet assistance and petpull behavior. AssistTrain keeps
  its strict focus policy. Bug 123 awaits installation and real-client checks;
  automated tests skipped under project policy.

## [0.223.0] - 2026-10-10

### Added

- `/assisttrain` and a Companion Manager group order: the human leader calls
  one target by starting a damage cast or weapon attack. Companions and their
  pets concentrate single-target offense; selecting targets, mez, debuffs, pet
  attacks and old spell ticks do not call a new target. Healing and add control
  remain available. The order is session-only and needs no save migration.

### Changed

- AssistTrain suppresses area offense, bombing and multi-target styles, ranks
  available damage spells by damage per cast time, and uses the strongest
  currently legal melee style. Damage dealers do not peel onto other attackers.
  Entering it clears petpull/stay and old attacks; `/passive` stops the train.
- Document the DAoC assist/mez distinction and the existing Aggressive and
  Defensive behavior. Ordinary modes retain their existing combat policies.
  Source build only; automated tests skipped. Installation and caster/tank
  real-client verification remain pending (task 108).

## [0.222.1] - 2026-10-10

### Added

- Track the owner-started 0.222.0 live observation of keep raids, battlegrounds,
  movement, keep progression and realm abilities. Record Murdaigean's confirmed
  startup camp failure as bug 122 and the partial results of observation task
  107, paused at the owner's request with a private handover retained.
  Private evidence stays outside Git; gameplay verification remains pending.

### Changed

- Documentation/version pins only. The running installation remains 0.222.0;
  no gameplay code, server lifecycle, settings, save or deployment changes.
  Automated tests were skipped under the project workflow.

## [0.222.0] - 2026-10-10

### Added

- Eden-inspired battleground campaign across ten level brackets from 1–4
  through 45–49, retaining Camlann guild/group alliances. `/battleground`
  (`/bgs`) exposes entry, departure, readiness, field contracts and camp funding.
  Joining suspends sub-10 safety only inside battlegrounds; saved flags, outside
  binds, portal sanctuaries and release immunity remain intact.
- Repeatable native-journal monster, hostile-combatant and participated keep
  claim contracts, with personal nontradable siege tokens and level-scaled XP.
  Objective availability follows actual native hunting mobs and loaded lords;
  PvP bodies retain XP/RP-only rewards and ordinary repeat-kill protection.
- Twenty donated tokens fund a physical class escort against native doors,
  then the lord. Sponsored captains can be killed to block turn-ins for five
  gameplay minutes. Guild funding and sabotage deadlines persist additively;
  guildless group funding remains session-local. Existing saves need no reset.
- Occupied-map patrol/ambush director with 1–8 temporary class combatants,
  native healing/CC/immunity and a 24-actor per-map cap. Actors use proved native
  paths, expire cleanly and create no persistent roster or recovery records.
- Opt-in `/LFxp` and `/LFrvr` grouping every thirty seconds, preserving
  existing groups and `/bg` battlegroup commands.
- Focused navigation staging helper and optional guarded `-NavmeshBuild`
  deployment support for the ten generated meshes. Existing world meshes,
  protected save/config checks, backups and rollback remain intact.

### Changed

- Integrate the latest fork main, retaining friendly-keep exits, full-speed
  following and stable leader loops alongside local guild assault, keep
  progression, Reaver aura, Purge and battleground changes. Reconcile
  overlapping release notes and tracker IDs without another version bump.
- Battleground admission, teleporters, release and graduation use consistent
  level/RP ceilings, native landing floors and loaded-mesh guards. Murdaigean
  supplies 25–29 because imported Braemar lacks arrival keeps. Leirvik uses
  native zone254, not TestBG zone242.
- Loaded native battleground guards/lords participate in guild keep claims;
  frontier relic-pad creation and legacy flat capture rewards stay scoped away.
  Missing central keep/lord/monster data is exposed in contract availability.
- Player help and design/development guides describe campaign controls,
  imported-content limits and pending owner gameplay checks.
- Server and Windows launcher Release builds passed with zero errors; existing
  warnings remain. All ten native meshes load with three validated arrivals and
  three complete outside-camp routes per map. Automated tests were skipped;
  real-client checks remain pending.
- Deployed the combined 0.222.0 server, Windows launcher and ten campaign
  meshes on 2026-10-10: 27 files installed with verified backups and protected
  accounts, save and settings unchanged. The installation is left stopped;
  automated tests and post-deploy monitoring were skipped.

### Fixed

- Restore Cathal Valley's two missing native central keep gates from verified
  client fixture identities/positions, only after native approach validation.
  Existing door rows and damage state remain authoritative.
- Correct impossible battleground level-cap checks and treat a zero Realm Rank
  ceiling as unlimited for admission and native kill reward calculations.

## [0.221.0] - 2026-10-10

### Added

- Autonomous guild assaults assemble one or several existing parties at one
  route-validated exterior camp before attacking. Readiness uses living,
  physically present troops, healing support, completed supply trips and a
  current ram operator with equipment while gates remain closed. Separate
  guilds contesting the same keep never count as one allied army.
- Attack size varies: each new assault plans one, two or three parties,
  retaining that preference through its retries. Locally sighted enemies and
  guard/door strength can require more troops; a lightly defended target may
  be attacked by one ready eight-person party. Ready parties advance in a shared wave; late reinforcements
  require a substantial surviving attack. Failed assembly and separated
  columns have bounded deadlines; defeated waves must assemble again.
- `RVR_GUILD_ARMY` records physical readiness, defender sightings, equipment,
  launch/reinforcement decisions and explicit failure reasons. Installation
  and multi-party gameplay acceptance remain pending.

### Changed

- Release server compilation passed for this scoped change against the shipped
  baseline in an isolated source tree. The shared checkout build encountered
  unrelated in-progress battleground/keep errors. Automated tests were skipped;
  no deployment or live server changes were performed.

### Fixed

- Siege cohesion permits the leader's legal ticket/porter approach while
  followers wait to board; advancing toward the porter counts as recovery
  progress. Existing native boarding, supply isolation and travel rules remain.
- Cleared keep objectives no longer feed stale muster state into column
  handling or null destination reads. Reassigned operators release the old
  keep's job before preparing for the current target. Travel retains active
  operator leases; carried ram kits can reacquire expired assignments without
  launching another supply trip.

## [0.220.0] - 2026-10-09

### Changed

- Personal Purge now reuses after 20 minutes instead of 30 for players,
  companions and autonomous world bots, including the reduced-cost tank
  variant. Trainer cooldown details use the same handler value.
- Retain Atlas Old Frontiers ability costs and ranks, five-rank tank and
  three-rank hybrid Determination, and the separate 30-minute Druid Group
  Purge. Existing saved autonomous cooldown deadlines remain valid.
- Document the selected classic RA adjustment; nine-rank passive scaling
  remains deferred. Installation and real-client cooldown checks are pending.
- Server and Windows launcher Release builds passed with zero errors (warnings
  remain); automated tests were skipped under the project workflow.

## [0.219.0] - 2026-10-09

### Added

- All nine Reaver Soulrending damage-aura ranks add four times their learned
  spell level as extra threat to each NPC/mob they damage per pulse. This
  supplements normal damage threat without forcing aggro or matching the
  highest threat; manual taunt styles remain useful against heavy healing
  and bombing. Players, companion/world playerbots and controlled pets are
  excluded from the bonus.
- Aura tooltip and detailed spell descriptions explain the extra threat,
  rank scaling, target exclusions and continued use of taunt styles. Damage,
  range, pulse interval, power and cooldown remain unchanged.
- A five-minute `MOVE_PACE` log line: commanded-speed percentiles of moving
  autonomous world bots by role (leader, follower, solo) and flag (stealthed,
  hurt, snared), plus how many leaders looped.

### Changed

- Server and Windows launcher Release builds passed. Installation and
  real-client aggro balance verification are pending; automated tests were
  skipped under the project workflow.
- Task 101: autonomous groups run instead of crawling. Followers of a moving,
  unstealthed autonomous leader match its full run speed rather than the
  speed of its latest order. They keep the catch-up bonus and personal stride
  and do not overrun the leader beyond what their formation slot needs.
  Stealthed, companion and player-led groups are unchanged.
- A leader waiting for stragglers (siege column, RvR roam, group travel)
  runs a small loop of 300-450 units around the waiting spot at full speed
  instead of standing still. The loop points are checked against the navmesh.
  The leader holds as before in combat, near keeps, near zone crossings or its
  PvE camp, while operating siege equipment, during expedition attendance, in
  stealth, in dungeons, or when no loop point is reachable. Existing hold timers,
  gap thresholds and give-up rules are unchanged. The loop is built once per
  hold around a fixed centre and ends with the hold. Siege-column regroup
  progress is measured from that centre, and circling inside the loop does not
  count as movement for the 15-minute stuck watchdog.

## [0.218.0] - 2026-10-09

### Added

- Guild-held keeps start at level 1 and automatically strengthen to level 10:
  level 5 after one hour and level 10 after 32 hours of gameplay time. Saved
  upgrade deadlines survive restarts; downtime counts at normal speed.
- A one-time, per-keep migration resets existing human and autonomous-bot
  guild holdings to level 1 when the updated server loads them. Ownership,
  claim dates and relic placement remain intact; Frontier Wardens, unowned
  keeps, relic keeps and portal keeps are excluded.

### Changed

- Default claimed level is 1, maximum level is 10, and the upgrade timer is
  enabled; untouched legacy settings migrate on startup. Recapture/release
  clears upgrade progress. The timer stops at the configured cap.
- Added regression source for reset idempotence, NPC exclusions, upgrade
  thresholds and restart catch-up. Server Release build passed; installation
  and real-client checks are pending. Automated tests were not run.

### Fixed

- Bug 120: autonomous groups no longer stand idle inside their own guild
  keep. Every autonomous world bot may plan through and use a keep door its
  realm, guild or alliance may pass, on PvE, solo and meetup legs as well as
  RvR; enemies, companions and `/spawn` helpers keep the native door rules.
- A member whose meetup route stays unreachable after the one rendezvous
  reselection now first tries the terminal pocket escape, then leaves the
  party at once ("can't get there, go without me") instead of standing
  frozen until the 15/20/45-minute no-show deadline. The party shrinks,
  replaces a leaving leader or ends below two bots as before; realm
  expedition musters keep their hub retry.
- RvR guild-keep meetups prefer a point in front of the outer gate; the
  courtyard is only the fallback.

## [0.217.1] - 2026-10-09

### Added

- Track bug 120: autonomous groups held idle inside their own guild keep
  (Dun Crimthain) because non-RvR route legs cannot traverse friendly keep
  doors and unreachable rendezvous members wait for the no-show deadline.
  Root-cause analysis only; no gameplay change.

## [0.217.0] - 2026-10-09

### Fixed

- Keep-raid supply operators retain one merchant, use legal frontier passages
  for outbound and return legs, and return before the warband may depart.
  Independent supply passengers cannot redirect or board the waiting column;
  failed purchases also return, within the existing rally deadline.
- Siege columns retain distant and cross-region stragglers during bounded
  regrouping instead of silently dropping them after short holds. An isolated
  follower's repeated deaths cannot abandon a force still fighting its keep.
- Guild keep recall permits immediate roadside defense before resuming travel;
  it no longer clears current attack aggro every turn outside the keep.

### Added

- Throttled siege execution, supply-return, column and recall-defense diagnostics
  with force/operator identity and coordinates. Native ram placement, LOS,
  navigation, enemy-door checks and defenders are unchanged. Ram non-execution,
  invalid elevations and complete gameplay acceptance remain under investigation.
- Regression cases for supply passenger isolation and bounded siege cohesion;
  automated test execution and installation remain pending.

## [0.216.0] - 2026-10-09

### Fixed

- "Make Me a GM" in the launcher failed with an error popup and left the
  account at player level. The server creates `offline_local_options` with
  four columns, but the launcher inserted two positional values; it now names
  the `Key` and `Value` columns like the other launcher option writers
  (bug 118).

## [0.215.0] - 2026-10-09

### Fixed

- Keep-raid groups use their leader's porter network and preserve intermediate
  home passages. Transfer the leader first, let distant members finish legal
  reunion hops, and require a real quorum before bypassing a near-keep rally.
- Give each rally a short preparation window, wait for started supply trips
  to return, and prevent independent horse rides from splitting its column.
  Distinguish stalled or aborted approaches from actual defended outcomes.
- Keep siege operators with their marching party while coordinating supplies
  during assembly. Keep-route planning accepts only the native mover's small
  vertical start tolerance; it does not relocate bots or replace navigation.
- Bot leader promotion announces the promoted living member without casting
  it to a human player. PvE enemy-hold movement preserves route-recovery state
  when a failed path abandons its camp.

### Changed

- Gate frontier threat coordination and area checks behind the existing scan
  cadence, reuse one party snapshot, and bound expensive candidate checks while
  retaining native attack permissions, combatant priority and immediate defense.
- Add focused regression coverage and record source fixes separately from
  installation/gameplay acceptance. Shipping leaves the installation stopped
  for the owner's next monitored run; gameplay acceptance remains pending.

## [0.214.2] - 2026-10-09

### Added

- Record the owner's live observation of installed 0.214.1, covering achieved
  world speed, bot progression, keep musters, travel and siege outcomes.
- Track bot-leader promotion and PvE camp null-reference failures as bugs
  116 and 117; reopen keep-assault bug 75 after its runtime check failed.

### Changed

- Update upstream-adaptation and accelerated-speed verification status using
  bounded runtime evidence. Keep raw logs, snapshots and save-derived data
  outside Git; combat balance, native routing, active ability effects and
  restart acceptance remain pending. No gameplay repair or deployment was
  performed for this observation.

## [0.214.1] - 2026-10-09

### Added

- Focused defense-resolution checks for bot shield, weapon, ability, facing,
  quality/condition and PvP-cap gates, plus cooldown token round-trip and
  malformed-state preservation checks using the production serialization path.
- A clean-fixture-only quest migration regression harness covering dry-run,
  apply, idempotent rerun, partial ownership, interruption recovery, rollback,
  unrelated progress preservation and unsafe-target/reference refusals.

### Changed

- Record the owner's isolated verification authorization and successful
  checks: 40 server regression cases, seven disposable quest migration cases
  and one Windows launcher version/timer-constant case passed. Keep these
  results separate from ordinary-client acceptance.
  SOL 6.1 High found no actionable defects in the new test sources. Appearance,
  runtime combat/effects, database cooldown reload, native routing, performance
  and quest gameplay acceptance remain open; no installation was changed.

## [0.214.0] - 2026-10-09

### Added

- A source-backed Miari's Seed classic quest pilot using the existing
  DataQuest engine, one quest definition and one non-tradable quest item.
  Document clean release attribution, eligibility, actor/zone dependencies
  and original ordinary-journal guidance without importing scraped guides.
- A dry-run-first, hash-pinned development migration restricted to marked
  disposable copies, with exact collision/actor/schema checks, durable
  recovery manifests, verified backups and owned-row-only rollback.

### Changed

- Adapt Stage 7 against upstream `c8b0dca0` and its source-compatible clean
  v0.35b release data. No world populations, native client features, meshes,
  played databases or installed runtime files are replaced.
- Record Stage 4's appearance evidence audit without enabling an unverified
  realm-based gear filter. Model/race evidence and bug 5's client check remain
  open. Source review is separate from unexecuted migration/gameplay checks;
  later content batches still require pilot acceptance.
- SOL 6.1 High final source review found no remaining actionable defects
  after travel/migration fixes. Server and Windows launcher Release builds,
  regression-source compilation and pilot syntax/resource checks passed;
  automated tests, migration operations and gameplay checks were not run.

## [0.213.0] - 2026-10-09

### Added

- Independently switchable outdoor PvE route-threat checks for autonomous
  solo bots and group leaders, using the designated puller, bounded attempts,
  retained travel holds and complete two-leg detours on existing meshes.
- A historical performance snapshot and controlled comparison checklist,
  decision logs and a profiling phase. The feature defaults off until the
  owner accepts measured routing behavior and overhead.

### Changed

- Adapt Stage 6 against upstream `c8b0dca0`, preserving frontier/dungeon
  handling, safe areas, quest/service actors, companions and existing pet
  pulls. Limit each scan to 24 candidates and six inline corridor queries;
  existing defensive-pull validation has its separate bounded query work.
- SOL 6.1 High source review corrected hold/recovery ordering and retained
  the verified detour rejoin leg. Regression source is compiled, not executed;
  enabled-travel and performance acceptance remain pending.

## [0.212.0] - 2026-10-09

### Added

- Autonomous bots can train and use class-legal Purge, Ignore Pain, Second
  Wind and First Aid alongside the existing 39 passive paths. Purchases use
  earned points and runtime prerequisites; existing ranks are not respecced.
- Optional saved cooldown tokens use gameplay UTC and are persisted before
  activation. Queued status writes cannot restore an older cooldown snapshot.

### Changed

- Adapt Stage 5 against upstream `c8b0dca0` with actual catalog keys and
  handler-type variants, deterministic purchase phases, and scoped GameBot
  support in Purge and First Aid. Companion/player training is unchanged.
- Document costs, activation policy and rollback limitations; add regression
  source without executing tests. Final review, builds and gameplay/reload
  acceptance remain pending.

## [0.211.0] - 2026-10-09

### Changed

- Companions and autonomous GameBots use the existing player block, parry and
  evade calculations, ability/buff bonuses, facing and equipment requirements,
  shield block rounds and player-shaped PvP caps. Human formulas and ordinary
  NPC/pet defense paths are preserved; realm identity does not determine caps.
- Adapt Stage 3 from upstream `bf9bf38b` with the fork's existing specialization
  and real-equipment APIs. Source review also closed the NPC parry fallback for
  bots failing equipment eligibility. Combat balance and client checks remain
  pending; regression source was added but not executed.

## [0.210.0] - 2026-10-09

### Fixed

- Merge coincident navigation points while retaining their combined door flags,
  the final endpoint and partial-path status. A narrowly checked forward corner
  sight retry requires a reachable surface step, reverse segment proof and
  door exclusion.
  Existing meshes, Darkness Falls preparation and native interfaces are retained.

### Changed

- Implement adaptation-plan Stage 2 against upstream revision `c8b0dca0`.
  Focused route-point regression source was added but not executed; corner,
  wall, closed-door and ordinary-route verification remains pending.

## [0.209.2] - 2026-10-09

### Fixed

- Closed console input waits one second between reads instead of spinning,
  preserving normal console commands and independent server operation (bug 115).

### Changed

- Implement adaptation-plan Stage 1 from upstream commit `c1c465c3`.
  Source exit/retry paths reviewed; runtime CPU measurement and console shutdown
  verification remain pending. No server was started or deployment performed.

## [0.209.1] - 2026-10-09

### Added

- A staged plan for adapting selected upstream server, navigation, bot combat,
  equipment, travel and classic quest improvements while preserving Camlann
  rules and existing saves. Other forks are outside its scope.
- Track the upstream adaptation proposal and the existing closed-console-input
  spin defect; implementation and gameplay verification remain outstanding.

### Changed

- Align version labels for this documentation-only change. No gameplay code,
  runtime assets, download baseline or installed game files were changed.

## [0.209.0] - 2026-10-04

### Added

- One fixed passive realm ability priority path for each of the 39
  autonomous world bot classes, with class-legal runtime costs, persisted
  ranks, and automatic spending from the ordinary earned point pool.

### Fixed

- Autonomous world bots now derive Realm Level from earned Realm Points
  on award and load, so realm-rank-dependent behavior uses their true rank.

### Changed

- Document the class paths and pending real-client checks in
  `docs/AUTONOMOUS_RA_BUILDS.md` and task 88. Timed active abilities remain
  outside this bot training path. Server and launcher deployed locally on
  2026-10-04 with protected saves/settings unchanged; real-client checks remain
  pending.

## [0.208.0] - 2026-10-04

### Added

- `/offline` opens a short in-game guide for getting started, companions,
  training, travel, recovery and Camlann PvP. A single login chat hint makes
  it discoverable without another automatic popup.
- `/companions status` reports live companion readiness, location and effective
  orders; topic-based `/companions help` explains roster, tactics, training,
  squads and recovery without requiring the Companion Manager extension.
- `/train list` shows the player's own trainable specialization names, current
  training and available points anywhere. Training still requires a valid
  trainer and earned points; ambiguous abbreviated names ask for a full name.

### Changed

- `/mobs` includes zone names; `/mobs nearby [level] [page]` limits the list
  to the current zone and defaults to the player's level. Existing monster
  eligibility and teleport destinations are preserved.
- Record the freeshard research and player-flow review under task 87. Existing
  saves, progression, economy and combat rules are preserved. Server and
  launcher deployed on 2026-10-04 with protected saves/settings unchanged;
  the game remains stopped and real-client verification is pending.

## [0.207.0] - 2026-10-04

### Fixed

- Update the OpenTelemetry OTLP exporter and resolved core/API dependencies
  to 1.15.3 for four reported security advisories covering unbounded response/
  propagation allocation and unsafe disk-retry input (bug 114).
- TCP receives cannot lose an immediate completion notification and stall
  the connection (bug 106). Database batch saves acknowledge success and
  clear dirty state only after commit, preserving retries on rollback
  (bug 107).
- Delayed companion progress saves reject former actor instances after
  benching, reinviting or deleting the companion (bug 108).
- Launcher console logging opens one writer on startup rotation and ignores
  callbacks after disposal (bug 109). Server-exit callbacks tolerate launcher
  teardown and ignore obsolete processes (bug 110).
- Deployment restore handles files newly added by the deployment, restoring
  their original absence after hash verification (bug 111).
- Camp catalog deduplication uses the same normalized monster names for
  restored and fallback spawn cells (bug 113).

### Changed

- Launcher logging caches archive sizes instead of scanning eight archive
  paths per console line (bug 109). Camp danger queries read immutable
  snapshots without a shared lock; matchmaking state is resolved once per
  planning pass (bug 112). Live performance measurements remain pending.
- Record this source audit and outstanding runtime checks in `docs/BUGS.md`.
  Server and launcher deployed on 2026-10-04 with saves/settings preserved;
  the game remains stopped and real-client checks are pending. The four
  OpenTelemetry DLL updates were also deployed with separate owner approval.

## [0.206.0] - 2026-10-04

### Fixed

- UDP handling copies validated socket bytes before queuing them, ignores
  incomplete headers, and releases pooled packets on every rejection path
  (bug 92). Login version negotiation now supports split or coalesced TCP
  receives (bug 93), and the highest valid client session ID can register
  and resolve normally (bug 95).
- Weekly quests reset after seven calendar days, including across New Year,
  instead of waiting eight days or resetting early (bug 94). Automatic game
  loop pool sizing respects its 128-thread maximum on larger hosts (bug 96).
- Companion loot no longer awards the final rejected item after exhausted
  category/weapon rerolls (bug 97). Hostile same-realm Camlann kills qualify
  for companion PvP gear under the existing alliance and per-death rules
  (bug 104).
- Dynamic group changes clear stale autonomous RvR plans and force metadata;
  active stablemaster legs finish before metadata changes. Ended forces
  release keep-claim reservations without disturbing a surviving group when
  one member leaves (bug 102).
- Launcher server/client scans dispose unused process objects, and presence
  checks dispose their match (bug 98). Join Friend profiles and world-speed
  requests use separate temporary files per writer (bug 105).
- The existing confirmed fresh-world conversion clears persistent companion
  records and gear with their deleted owners (bug 99). Fresh-world Setup
  refuses existing or colliding database/credentials/ruleset output paths
  (bug 100), and spawn migrations give each backup a unique filename
  (bug 101). No world reset, setup, migration or deployment was executed
  during the source sweep.

### Changed

- Correct the companion guide's PvE reward selection and full-backpack sale
  description to match existing behavior (bug 103). Source fixes await
  installation and the relevant owner-run checks in `docs/BUGS.md`.

## [0.205.0] - 2026-10-04

### Fixed

- Keep guard-death alerts now include hostile autonomous playerbots in the
  nearby enemy count (bug 91), instead of reporting zero during bot attacks.
  Playerbots use the existing keep ownership, guild, alliance and group
  hostility rules; ordinary NPCs and pets do not inflate the count. The
  shared correction also applies to relic-guard alerts and capture logs.

## [0.204.0] - 2026-10-03

### Fixed

- Companions earn realm points from RvR kills of bots again (bug 90). Their
  damage was credited to their owner, so every companion stayed at 0 realm
  points while the owner collected their share. Each companion now gets its
  own share like any group member; `/spawn` helpers still credit their owner.

## [0.203.0] - 2026-10-03

### Fixed

- Players without a guild no longer get their own `/spawn` helpers as TAB
  targets (bug 89). Logging in or changing region reset the player's
  friend marker, so helpers that arrived first looked like enemies.

## [0.202.0] - 2026-10-03

### Changed

- RvR bot groups now meet at their realm's border hub (Castle Sauvage,
  Svasud Faste, Druim Ligen), travel there by porter, and port and march
  together (task 85). Before, a third of the groups met deep in the enemy
  frontier and most missing members died on the way there, then roamed
  alone. A group leaves when everyone is there, or at three quarters when
  the rest is not expected within five minutes; nobody is thrown out, and
  latecomers follow. A group that never fills up returns its bots to the
  hub to look again instead of sending them off alone.

## [0.201.0] - 2026-10-03

### Fixed

- Bots no longer gather and kill each other at the capital exits (bug 88).
  About 85 Albion bots looped between Camelot and its outdoor exit because
  a PvP hunt in a foreign frontier could not be reached; they now drop such
  a target for 30 minutes and pick another. Same-realm bots also keep the
  peace within 1,500 units of their own capital's exits, as at bindstones.

## [0.200.0] - 2026-10-03

### Changed

- A group now keeps its fastest speed (task 84). A Warden no longer starts
  its travel speed chant when a Bard, Minstrel or Skald in the group has the
  faster song; it keeps bladeturn instead. Bots also leave speed to a real
  player who is running a faster speed song.

## [0.199.1] - 2026-10-03

### Added

- docs/LOOT_LEVEL50.md lists every level-50 item from the database loot
  tables by place (Darkness Falls seal merchants and drops, frontier zones
  and frontier dungeons, PvE dungeons) with mob, kind, realm and chance.

## [0.199.0] - 2026-10-03

### Added

- Bot guild keep defense (task 83): actual hostile damage to an owned keep's
  walls, gates, guards or lord immediately recalls all active independent autonomous
  guild troops, at every level and from PvE, services, raids, roaming or other
  sieges. Player-led troops and companions retain player control.

### Changed

- Keep ownership takes priority over unrelated combat and travel. Same-guild
  parties retain their members; recalled members leave mixed-guild raids
  individually. Existing routes, native damage, death recovery, loot and
  inventories remain authoritative. Failed defense routes retry instead of
  switching to another goal. Further hostile damage renews the four-hour
  response; owner loss ends it, and simultaneous attacks queue behind the
  first keep. Normal activity allocation resumes after stand-down.
  Deployed on 2026-10-03; real-client verification is pending.

## [0.198.0] - 2026-10-03

### Fixed

- Healer bots recognize the installed CombatSpeedBuff form of Group Celerity
  and cast the strongest learned rank during combat when healing and crowd
  control permit (bug 87). Concentration haste joins normal buff upkeep;
  companions continue to skip short timed buffs outside combat. Deployed
  with 0.199.0 on 2026-10-03; real-client verification is pending.

## [0.197.1] - 2026-10-03

### Added

- Record the Healer Group Celerity investigation (bug 87): installed spells
  use CombatSpeedBuff, which the active defensive target selector omits.
  Gameplay behavior is unchanged; a source fix remains open.

## [0.197.0] - 2026-10-03

### Changed

- Guild keep claims are unlimited, including existing saves that carried
  the three-keep cap. Eligible autonomous guild forces prioritize free,
  defeated keeps and travel to their stewards; one force reserves each
  claim journey, while nearby guild leaders can still claim opportunistically.

### Fixed

- Defeated keep lords restore their claim stewards when saved mobs load,
  so keeps such as Fensalir Faste remain claimable after restarting (bug 85).
- Svasud Faste's home-side gates use border-door range and opening rules;
  stale client state is refreshed and close requests leave the timed opening
  intact. Keep-door clicks share one range, detect the correct side near the
  door, and suppress duplicates only for successful use of that same door.
  Breached enemy gates also allow entry; intact hostile gates remain blocked
  (bug 86). Deployed with 0.199.0 on 2026-10-03; real-client verification
  is pending.

## [0.196.0] - 2026-10-03

### Changed

- Companion deletion now removes all equipped and carried items regardless of
  origin, so earned, traded, or unclassified gear no longer blocks deletion
  (bug 84). The confirmation explicitly warns that all items will be lost.

## [0.195.0] - 2026-10-03

### Changed

- Autonomous world bots release in validated sanctuaries; RvR bots prefer
  their own border hub after either PvP or PvE deaths. Recovery positions
  spread within safety, and failed safe transfers retain the corpse for retry.
- Keep-bound warbands commit to marching, avoid optional PvP and guildmate
  detours, and stop distant pursuit after incoming party attacks cease.
  Distant rally followers travel behind their leader; cohesion and unavailable
  rally posts have bounded failure handling.
- Keep departures can insert a stable, connected local hub fan, flank or
  cover waypoint while retaining validated road seams and assault approaches.
  Repeated individual PvP losses near the same place prompt temporary
  avoidance or force withdrawal from a failed approach; siege-area casualties
  continue to use existing battle recovery. Task 81 awaits gameplay checks.

## [0.194.0] - 2026-10-02

### Fixed

- In a shared battlegroup, `/spawn` helpers and companions no longer treat
  the other owner and his group as enemies (bug 83). Only players carried
  the battlegroup, so bots of two players in different guilds would fight
  each other on Camlann; bots now count as members of their owner's
  battlegroup.

## [0.193.0] - 2026-10-02

### Fixed

- Paladin companions fight again (bug 82). With the Healer or Buffer role
  they counted as pure support and only chanted; like Friar and Warden they
  now heal when needed and otherwise attack. Switching chants no longer
  interrupts a Paladin's or Warden's melee swing.

## [0.192.0] - 2026-10-02

### Changed

- Minstrel, Skald and Bard speed songs now keep running while another song
  plays (task 80). Before, every new song ended speed, so a twisting Bard
  kept dropping it. This applies to players and bots in all three realms;
  performer bots start speed first and then switch only between their other
  songs. This departs from 1.65, where only one song could play at a time.

## [0.191.0] - 2026-10-02

### Fixed

- Keeps claimed by bot guilds stay claimed after a server restart (bug 81).
  The bots did claim captured keeps, but their guild's new name was never
  saved, so at the next start the keep found no owner and went back to the
  Frontier Wardens; all five bot claims in the logs were lost this way. The
  name is now saved, a keep with an unknown owner is no longer given away,
  and every claim is logged.

## [0.190.0] - 2026-10-02

### Changed

- Buffers hand out optional buffs while concentration lasts and take them
  back for newcomers (task 78). Strength on a caster or healing-spec bot is
  optional: it goes out whenever the buffer can afford it. When someone
  joins who needs a buff and concentration runs short, the buffer ends an
  optional buff on a bot and buffs the newcomer instead. Real players are
  buffed first, get every buff that has an effect, and never lose one.

## [0.189.0] - 2026-10-02

### Changed

- Whether a Cleric, Healer, Druid, Shaman or Bard gets base strength now
  depends on its spec (task 78): with a weapon trained to at least half its
  level (a battle Cleric, a melee Druid) it counts as a fighter and gets
  strength; a pure healing spec does not, unless overloaded.

## [0.188.0] - 2026-10-02

### Changed

- Acuity buffs go only to pure casters (task 78). The server adds acuity to
  the casting stat of list casters only, so Clerics, Druids, Healers,
  Shamans, Bards and other hybrids got nothing from it. The healers Cleric,
  Healer, Druid, Shaman and Bard now count as casters for base strength.
  docs/BUFF_RULES.md lists who gets which buff and in which order.

## [0.187.0] - 2026-10-02

### Fixed

- Minstrel bots no longer freeze at their bind stone playing a mez song
  (bug 80). When the mez could not land, the song retried forever, which
  real players cancel with another song but bots could not; 35 Minstrels
  stood still for hours this way. Bots now give up and pick a new goal, and
  the stuck rescue ends any hanging cast before moving a bot.

## [0.186.0] - 2026-10-02

### Changed

- Pet pull with `/stay`: when you walk out to pull, your Mentalist follows
  about 250 units behind you so its heal-over-time on the pet stays in range
  (task 79). It keeps out of every idle monster's aggro range on the way and
  waits if no safe spot is left. It casts damage only once it is back near
  the camp, and holds its own camp spot again when you return.

## [0.185.0] - 2026-10-02

### Fixed

- Valewalkers from level 46 no longer cast an old weapon proc forever in
  their capital (bug 79). Their top proc rank has a different proc chance,
  so the bot kept a weaker rank as well and recast it after every rejected
  cast, standing still and spending power; 18 high Valewalkers were stuck
  this way. Ranks of one proc line now replace one another, and a stronger
  rank already on the target counts as present.

## [0.184.0] - 2026-10-02

### Changed

- Bots and companions that buff first give themselves their own dexterity
  and dexterity/quickness buffs (task 78). Dexterity shortens every cast, so
  the rest of the group is buffed faster.
- Single-target class buffs now go only where they help, in every realm
  alike: classes without power get no acuity, and pure casters get base
  strength only when they are overloaded or the buffer still has that much
  concentration left. Strength/constitution still reaches everyone.

## [0.183.0] - 2026-10-02

### Fixed

- A pet summoned at a spot outside every zone no longer throws in the
  casting service. Since 0.177.0 about 300 `CastingService` null-reference
  errors per 10 minutes at 20x came from `SummonSpellHandler.GetPetLocation`;
  the pet now appears at the caster instead.

## [0.182.0] - 2026-10-02

### Fixed

- Route search is fast again (bug 78). 0.180.0 searched a route twice
  whenever the way around the shared frontier dungeons failed, which
  multiplied cross-region think time by ten and held the server at about
  2.4x instead of 4-6x world speed. Whether a way around exists depends only
  on the static zone-point graph, so it is now worked out once per realm and
  region pair; every step runs a single search. All other bug 78 fixes stay.

## [0.181.0] - 2026-10-02

### Fixed

- XP camp choice and the route search agree again (bug 78). Camp choice
  let its reachability check pass through Darkness Falls, which the route
  search never uses as a shortcut, so it picked camps whose only real road
  ran through the boss dungeons. 0.180.0 then walked them through: route
  failures fell from 1,511 to 6, but 44 boss deaths came back and world
  speed fell to 2.5x. Camp choice now treats Darkness Falls like the
  dungeons: a destination, never a road.

## [0.180.0] - 2026-10-01

### Fixed

- Goals that need the frontier dungeon road reach it for real (bug 78). The
  0.177.0 check used a plain region graph, which found a way around that
  the real route search (entrance and Darkness Falls rules) could not use:
  still 1,511 "No legal region route" failures. The route search itself now
  decides: when no way around exists it takes the full road, and that region
  pair skips the detour try for ten minutes. (The code landed in commit
  ad55bd7, labelled 0.178.0 by mistake; 0.178.0 and 0.179.0 are the buffbot.)

## [0.179.0] - 2026-10-01

### Fixed

- Free buffbot buffs vanished right after being cast (task 77). Their
  length overflowed the server's duration clamp and became negative. They
  now last 65,000 seconds (about 18 hours, the longest the client's buff
  timer can show), still ending on death or logout.

## [0.178.0] - 2026-10-01

### Added

- Free buffbot (task 77). Right-clicking a buff merchant (`BuffMerchant`)
  now gives every buff at once, free of charge: base and spec armor,
  strength, constitution, dexterity, strength/constitution,
  dexterity/quickness, acuity, haste and a new endurance regeneration buff.
  Casters get the caster variants. The buffs last until death or logout and
  do not use your own concentration. Group members and pets in range get
  them too. A "Realm Enchanter" now stands beside every realm teleporter
  (Master Visur, Stor Gothi Annark, Channeler Glasny) in the capitals and
  levelling towns. It is created at server start and not written to the
  save. More can be placed as GM with `/mob create DOL.GS.BuffMerchant`.

### Changed

- The buff merchant no longer sells tokens. Tokens you already have still
  work when handed over.

## [0.177.0] - 2026-10-01

### Fixed

- Goals that only the shared frontier dungeons lead to are reachable again
  (bug 78). 0.176.0 cut boss deaths from about 80 to 2 per measurement and
  restored world speed (5.9x, tick p95 19 ms), but dropped the dungeon road
  for every PvE route, so goals picked outside camp choice (Darkness Falls,
  Vigilant Rock and others) failed 1,670 times with "No legal region route".
  Such a route keeps the road now; whether it is needed is decided once per
  realm and region pair.

## [0.176.0] - 2026-10-01

### Fixed

- Levelling bots choose XP camps they can reach without a road through the
  shared frontier dungeons (bug 78). The 0.175.0 detour searched every route
  twice and slowed the server from about 8x to 2.5x world speed (tick p95
  14 ms to 70 ms) without reducing boss deaths. Camp choice now counts a
  region as reachable only when no road through those dungeons is needed;
  a camp inside them stays allowed and is covered by the boss spots of
  0.173.0. Outside the dungeons a route never enters them unless its goal
  lies inside; one search per step as before.

## [0.175.0] - 2026-10-01

### Fixed

- Levelling bots no longer walk through the shared frontier dungeons on the
  way to their XP camps (bug 78). The 0.173.0 boss spots did not help: the
  bots did not hunt there, they crossed Marfach Cavern toward camps
  elsewhere and died to its boss on the way (90 deaths in 11 minutes at
  20x, 81 different bots). All bots now use the rule RvR forces already had:
  no road through those dungeons unless the goal lies inside them or no
  other way exists.

## [0.174.0] - 2026-10-01

### Changed

- RvR groups attack instead of standing by (task 76). Live 0.162.0: of
  13,696 observe decisions, 46 % were "roam on" and 27 % "leave"; groups
  attacked only after three of eight enemies were down. A group now attacks
  a watched party that has no more living members than it has. It leaves
  when charged only by a bigger party, a third party near or behind, or when
  it is seen by one it would not fight. Smaller groups keep the counted rule.

## [0.173.0] - 2026-10-01

### Fixed

- Levelling bots level again (bug 78). Since 0.158.0 (task 70) an RvR tour
  never ended, also below level 50: 417 of 586 bots under 50 sat in RvR,
  230 of 255 in their thirties. Only level-50 characters now stay in RvR
  without end. A levelling bot returns to PvE after its tour and owes one
  PvE task before the next one; a warband with a levelling member ends
  when its task clock runs out instead of renewing. No pauses come back.
- Bots keep away from bosses far above their level (bug 78). Two named
  bosses (levels 65 and 75) killed level-50 bots about 5,500 times in 19
  hours next to their XP camps. A monster that kills a bot 10 or more levels
  below it marks the spot; for six hours no bot picks an XP camp within
  3,000 units of it.

## [0.172.0] - 2026-10-01

### Fixed

- RvR warbands no longer deadlock at the frontier porter (bug 76, after
  0.171.0). A warband leader bound for another region held "formation"
  until the whole party stood recovered beside it, while its members waited
  at the porter for the leader: 506 bots stood "Boarding frontier teleporter"
  at 20x speed. The leader now walks on to the porter, whose muster gathers
  the party.
- A bot with a full backpack and nothing it may sell no longer stands at the
  medallion merchant forever ("Making room for frontier medallion", 56 bots);
  it takes the legal dungeon road instead.

## [0.171.0] - 2026-10-01

### Fixed

- RvR warbands cross the frontier with their leader (bug 76, second cause).
  Since 0.162.0 a member boarded at once whenever any member of its force was
  already across. Members went ahead without the leader, found it missing,
  ported back, and followed its pending passage out again. Live 0.170.0 still
  showed about 3,600 departures per hour: one single-realm warband alternated
  Emain and Home 141 times in 78 minutes without a death. Members now board
  only together with the leader or after it; a solo force is unchanged. A new
  `RVR_FRONTIER_LEADER_HOLD` line, at most once per force every five minutes,
  shows why a leader is not boarding.

## [0.170.0] - 2026-10-01

### Fixed

- RvR warbands no longer ping-pong between the frontier porters (bug 76).
  Members follow a leader's pending porter passage, but that passage was
  only cleared on departure. A leader who re-planned to a target in its own
  region kept the old passage, so members ported away from it and then back
  to it all day (live 0.162.0: about 3,700 departures per hour, one warband
  2,000 times in 19 hours). The passage is now dropped as soon as the bot no
  longer needs a crossing.

## [0.169.0] - 2026-10-01

### Added

- Companion Manager roster organisation. The list now has columns (name in
  the realm colour, level, class, type Story/Regular/New, state) and the
  state is its own colour: green **Active** (or **Squad N**), grey **Bench**;
  Recruit shows **Available**, **Recruited** or **Create**. **Group** buttons
  split the list into sections with a count: **Smart** (default: your group
  first, then the bench by realm), Realm, Role, Level bands, or None. **Sort**
  buttons order each section by Level (highest first, default), Name or Class.
  A click on a section header folds or unfolds it. Search also matches Story,
  Regular, Active and Bench, and the Active tab shows its count.
- **Rows** buttons (16, 22, 28, 34) choose how many list rows and detail
  lines (five fewer) the window fills; the message line says how tall a window
  each needs. Grouping, sorting and rows are remembered per account.
- `Install-CompanionManager.ps1` upgrades an installed 0.32.1 or 0.33.0 manager
  in place (backs up `game.dll` and both window files; `-RestoreBackup` puts
  them back).

### Changed

- Companion Manager text is the client's `arial14` font, about a third larger
  than before, and the window is 980×700 (it no longer fits an 800×600
  client; 1024×768 is fine). Row and detail spacing follow the font.
- Training & Tactics, Gear and the other detail pages use the extra lines of a
  larger window instead of always showing 11.
- The Group orders row shows its order in the class column.
- Client protocol 3: label numbers are 16 bits (the layout has 440 label
  adapters), and manager click controls run to `0xDF`. The client files must be
  upgraded with this version's stage; an older client ignores the new packets,
  the window stays empty, and `/companions` commands keep working.
- The raid click-to-target XML builder also accepts a client with this
  manager build.

### Fixed

- Enlarging the Companion Manager no longer leaves the list at 12 rows and the
  detail panel at 11 lines: pick a larger **Rows** size to use the space (the
  client cannot report the window size, so this is one click, not automatic).

### Removed

- The 800×600 fit of the Companion Manager window and the restore-then-install
  upgrade route for the manager client.

Verification: offline only. Native-client emulation (440 adapters, packets,
clicks, raid passthrough), the companion server tests (420), an installer
upgrade/restore round trip on a scratch copy, and a preview rendered from the
window XML with the client's own `arial14` glyphs passed. The real client has
never drawn `arial14`; installation and a real-client check are pending
(TASKS item 75).

## [0.168.0] - 2026-10-02

### Fixed

- Companions no longer stay stuck on the Darkness Falls entrance ledges
  (bug 54, reported again). The 0.116.0 rescue only fired when the path query
  itself failed, so a companion whose route over the stair links was valid but
  who still could not get down was never helped. A new watch in the follow turn
  is independent of the path result: a companion that stays put for 4 seconds
  while more than 250 units from its leader, with both out of combat, now
  joins the floor beneath the leader, like a native pet. Companions that are
  fighting, casting, resting, stunned, mezzed, snared, on a stable route, or
  still walking after a running leader are left alone. The server logs
  `COMPANION_LEFT_BEHIND_REJOIN` with both positions.
- Real-client check at all Darkness Falls entrances is pending.

## [0.167.0] - 2026-10-02

### Changed

- Higher World Speed steps keep up better. Live 10× ran at about 6.7× because
  a handful of heavy bot turns each held the whole tick at its barrier: stable
  (horse) route planning alone was about a fifth of wall time. Above 1× the
  plan now runs on a small background pool and the bot waits for it the same
  way it waited for a sliced plan (about 25 ms real at 10×); the plan itself
  and its choice are unchanged. At 1× the old in-turn planning is kept.
  If the pool's queue is ever full, the bot plans in its own turn as before.
- The achieved-speed reading is recomputed four times a second instead of
  rescanning thousands of samples on every tick.
- Real-client and live-population measurement of the new achieved speed is
  pending; `SelectCamp` (about 20 ms per call), zone itinerary and path
  issuing are still single-turn stalls (TASKS item 74).

## [0.166.0] - 2026-10-01

### Added

- Higher World Speed steps: 5×, 10×, and 20× join 1×, 2×, and 3× in the
  launcher's World Speed control. The server accepts exactly those six values.

### Changed

- The achieved-speed reading, its sanity limit, and the launcher's live clock
  now scale to 20×, and the server keeps enough tick samples to measure it.
  As before, a connected client forces 1×, the selected speed resumes five
  seconds after the last disconnect, and a PC that cannot keep up simply
  achieves less than the selected speed (the launcher shows the measured value).
  Real-client and heavy-population checks of the new steps are pending.

## [0.165.0] - 2026-10-01

### Added

- Battlegroup load check tooling (task 45): the server writes a
  `BATTLEGROUP_LOAD` line once a minute (owners, companion groups, companions,
  how many in RvR) while a player is in a battlegroup, and
  `tools/dev/battlegroup-load.py` compares those minutes with the baseline using
  the existing `SERVER_WORK` tick and `BOT_THINK_PROFILE` pathing lines. The
  measurement itself still needs a live run.

### Changed

- Bots below level 50 level in Darkness Falls much more often. A solo bot from
  level 20 and a group from level 16 (the lowest Darkness Falls creatures are
  about level 16) take the dungeon on 40% / 60% of their camp draws, up from
  10% / 30%, whenever a Darkness Falls camp is among their legal camps. The
  soft population limit still applies to Darkness Falls itself, so a crowded
  Darkness Falls sends bots back outdoors. Other dungeons keep the old rates,
  and level-50 bots keep their own rule.
- Solo levelers up to level 35 include Darkness Falls camps up to 30 minutes
  away in their camp pool, instead of only camps within 10 (then 20) minutes.

### Fixed

- None.

### Removed

- None.

## [0.164.0] - 2026-10-01

### Added

- The launcher writes unhandled exceptions with their full stack trace to
  `logs/launcher-errors.log` and names that file in its dialog (bug 70, to
  capture the "given key was not present" error).

### Changed

- Solo world bots at a PvE camp wake from rest as soon as their class
  threshold is met (casters about 75 % power, melee about 80 % health) instead
  of only at 100 %, so they pull at those levels again (bug 72). Groups still
  rest to full.
- Unknown abilities are warned once; the key-only `ConfusionImmunity`,
  `RootImmunity` and `MezzImmunity` no longer warn. Spell-line rows that point
  to missing spells give one summary line instead of one error each (bug 60).
- Friar and Warden companions on a Healer or Buffer role (the default Group
  support and Nurture builds) no longer stand by as pure support: they heal
  first and melee the group's target when nobody needs healing (bug 77).
  Druid, Cleric and Healer companions are unchanged.

### Fixed

- NPC templates skip unknown style ids instead of storing null, which logged
  `NULL style for NPC named new mob` thousands of times; `Bladeturn` and
  `AblativeArmor` spells no longer log `Unhandled spell` (bug 62).

### Removed

- None.

## [0.163.0] - 2026-10-01

### Changed

- Level-50 autonomous bots lean on RvR: every player type now has a level-50
  floor for choosing an RvR task (casual 35%, leveler 55%, hybrid 75%, hunter
  80%, roamer 85%, keep warrior 90%, shifted by aggression and risk tolerance),
  and PvE (gear farming, Darkness Falls, camps) fills the rest. Bots in a PvE
  intermission or with a PvE block still stay out of RvR as before.

### Removed

- Autonomous dragon and epic-dungeon raids: bots no longer open or join a realm
  raid on their own, and the sign-up raid calendar no longer announces raids.
  Raids started from the launcher's event controls still run.

## [0.162.0] - 2026-09-30

### Added

- None.

### Changed

- Human players of any realm may use any frontier porter; a frontier
  medallion lands at the porter's realm landing (bug 76, owner decision
  "Jeder nutzt jeden Porter"). Inner-keep medallions (Snowdonia, Vindsaul,
  Druim Cain) work at a porter of their realm.
- `RVR_FRONTIER_DEPARTURE` gains `porter_realm=`, `mixed=` and `straggler=`.

### Fixed

- A straggler of a warband that already has a member across the porter
  boards at once instead of waiting out the 5-minute departure cap, and its
  crossing no longer restarts that cap (bug 76).
- Bindstone peace no longer throws for actors without a region; tests
  updated for the `bind:` peace counter and the removed town downtime.

### Removed

- None.

## [0.161.0] - 2026-09-30

### Added

- A siege warband musters before it marches. After an automatic or forced keep
  assault opens, each attacking warband first gathers on its leader (all
  living members, or six of eight after four minutes, or half of them after
  ten), then leaves together; a warband already near the keep leaves at once.
  A warband that never comes together drops out with "Rally failed: the
  warband never mustered" (bug 75).
- A realm raid (dragon or epic dungeon) is one battlegroup: every party of the
  raid is allied with every other party of it and cannot attack it, and the
  bond ends when a party leaves or the raid ends (bug 74).
- Log lines `RVR_SIEGE_MUSTER_DEPARTED` and `RVR_SIEGE_MUSTER_FAILED`.

### Changed

- On the march every member follows the leader (column order) until the leader
  is at the keep; a member that died and released rejoins the group at the
  leader instead of walking the road alone.
- A mixed-realm warband uses one frontier passage (its leader's realm), so it
  boards one porter and lands at one place instead of splitting over Odin Alb,
  Odin Hib and Home Mid, one to three members per departure.
- The siege's fifteen-minute idle and forty-five-minute absence clocks stand
  still while a force is mustering and count from its departure.
- A freshly formed warband finishes its own assembly before it opens or joins a
  siege.
- The automatic opener skips a keep whose exterior route failed three times
  for a member: for an hour, doubling with each repeat up to eight hours; an
  attacker reaching the walls clears it (keeps 51, 57, 102, 105, 106).

### Fixed

- Raid parties no longer kill each other (about half of all raider deaths in
  the live log were kills by another party of the same raid) (bug 74).
- Keep sieges no longer send members one by one to die alone at the walls: 36
  sieges since 2026-09-26 had no capture, no ram deployment and no guard
  fight (bug 75).

### Removed

- The "each assigned bot converges without formation staging" opening: bots no
  longer travel to a siege individually.

## [0.160.0] - 2026-09-30

### Added

- None.

### Changed

- Right-clicking a Keep Claim Steward now asks for confirmation; declining
  leaves the keep unclaimed.
- Breached Camlann keep doors remain open after a claim; the project guide now
  describes their repair and automatic-close behavior.

### Fixed

- A guild's keep-claim message now includes the keep just claimed (bug 68).

### Removed

- None.

## [0.159.0] - 2026-09-29

### Added

- None.

### Changed

- None.

### Fixed

- Same-realm autonomous world bots no longer kill each other in a loop at
  their own realm's bindstones (bug 63): the hub peace now also holds within
  2,500 units of a realm's own bindstone. Live 0.158.0 (20:40–22:21): 3,437
  RvR/PvE deaths at the Mularn, Connacht and Cotswold binds, one bot 86 times.
  `RVR_HUB_PEACE` gains `by_rule=...,bind:N`.

### Removed

- None.

## [0.158.0] - 2026-09-29

### Added

- None.

### Changed

- Autonomous world bots play continuously (task 70): RvR tours no longer
  end, a bot leaving an RvR group stays in RvR instead of owing a PvE task,
  productive PvE parties keep renewing, and optional town breaks are gone.
  Training and a full backpack still send a bot to town.
- RvR groups are pickup groups across guilds and realms (task 71):
  guildmates first, then the leader's realm, then any realm. Warband merges
  and siege openings stay one-guild; group sizes stay mixed by player type.

### Fixed

- RvR group members help a mate who is attacked (bug 73): a helper no longer
  drops its fight after 6 s while it is still running to the attacker.

### Removed

- Optional town downtime for autonomous world bots.

## [0.157.3] - 2026-09-29

### Added

- `docs/BUGS.md` 71 (RvR stealthers kill less than before wave 5) and 72
  (PvE world bots still rest to near full) from the 0.157.1 live log.

### Changed

- `docs/TASKS.md`: tasks 62, 64 and 66 finished on the 0.157.1 live log;
  tasks 63, 65, 67, 68 and 69 carry their live-log result and stay pending.

### Fixed

- None.

### Removed

- None.

## [0.157.2] - 2026-09-29

### Added

- None.

### Changed

- `docs/TASKS.md` cleaned up: 54 tasks from 0.61.0–0.143.0 that were still
  waiting for a real-client check move to Finished as accepted through use;
  task 7 (XP wall, already answered) and task 40 (goal delivered as 41–44)
  leave Open. Nine tasks from 0.144.0–0.157.0 stay pending.

### Fixed

- None.

### Removed

- None.

## [0.157.1] - 2026-09-29

### Added

- None.

### Changed

- Night/day report for 2026-09-29 in `docs/NIGHT_REPORT.md`: the ten RvR/PvE
  behaviour packages 0.146.0–0.157.0 with their principles and the measured
  before/after numbers (RvR deaths per hour 1,763 → 790, hotspot share
  54 % → 13 %).

### Fixed

- None.

### Removed

- None.

## [0.157.0] - 2026-09-29

### Changed

- Autonomous RvR groups roam with a speed class (task 69). A group of three
  or more that has no Bard, Skald or Minstrel strongly prefers one when it
  forms and backfills, right after a missing healer and before a missing
  tank (a Healer with the augmentation speed is the fallback); a group
  still leaves without one under the usual wait rules.
  The server logs `RVR_SPEED_STATE group=… speed_class=<class|none>
  members=n` when a group sets out.
- While an autonomous RvR group travels out of combat, its performer sings
  only its speed (a Bard twists endurance with it, as for player-led
  groups) and recasts it when it drops; a Healer who is the group's speed
  stops once on the march to start it. No speed in combat, as before.
- The group moves as one body: the leader stands about 6 seconds (±25 %)
  when a member (within 4,000) falls more than 1,200 behind out of combat,
  until everyone is within 600, then walks about 10 seconds (±25 %) before
  waiting again, and walks on without a member it already waited for three
  times; members sprint to close a gap of more than 150 to their slot while
  they have endurance, and a member ahead of its leader never runs faster
  than the leader.
- Groups of three or more without speed favour roaming spots within 8,000
  of a keep, tower or border hub (weight 1.35, others 0.8), like 2003
  groups without speed that stayed near keeps and milegates.
- Every five minutes: `RVR_SPEED_TRAVEL window_s=300 groups=n with_speed=n
  travel_under_speed_pct=n leader_holds=n`.

### Fixed

- A travelling world bot kept the speed of the order it was walking when a
  speed song landed or ended, so a group under speed ran at normal speed
  until its next order; autonomous RvR bots now pick up the new speed at
  once.
- An autonomous world bot's sprint now makes it 30 % faster, as for a
  player (before it only cost endurance), applied after the relic
  carrier cap in the same order as for players.
- Autonomous RvR bots no longer slow to a crawl below a third of their
  health (a monster rule), and the on-foot habit after a PvE release no
  longer carries into RvR. Companions are unchanged.
## [0.156.0] - 2026-09-29

### Changed

- The hub peace between same-realm world bots now also holds for eight
  minutes after either bot left its own hub's safe circle, wherever it is,
  and the keep band grows from 6,000 to 7,500 around the keep centre (outer
  bindstone landings unchanged). A bot that walks back into a safe circle
  loses its clock; one never seen inside has none. Reason: in the 0.152.0
  live log (one hour) the grinder moved to the band edge, a cell in Forest
  Sauvage 6.3 km from Castle Sauvage held 316 deaths (18 % of RvR bot
  deaths), all Albion killed by Albion: groups leaving the hub a few
  minutes apart met just outside 6,000. Humans, companions, player-led
  groups and other realms are unaffected, as before.
- `RVR_HUB_PEACE` adds `by_rule=band:<n>,recent:<n>`, splitting the refused
  attack checks between the band and the departure clock.
## [0.155.0] - 2026-09-29

### Fixed

- Companion realm ability training now opens a focused list with the earned RP,
  realm rank and remaining points visible while scrolling. Each ability shows
  its next rank and cost, or the point shortage; purchases update the balance
  and show their result at the top of the manager.

## [0.154.0] - 2026-09-29

### Fixed

- Restore the Keep Claim Steward from a defeated keep's saved lord position when
  a player enters the keep or attempts to claim after a server restart. Record
  steward spawn failures in the server log.
- Let human guild members pass the keep claim check; it previously returned
  silently because it required the bot-only `IGamePlayer` interface.

## [0.153.0] - 2026-09-29

### Fixed

- A defeated Old Frontiers keep lord now unlocks guild claiming immediately, even
  if later siege damage refreshes the keep's five-minute combat timer. A guild
  member with claim rank may claim alone beside the steward; guild limits still
  apply.
- `/gc claim` repeats an exact refusal in the main chat and records that reason
  in `KEEP_CLAIM_ATTEMPT`, so a rejected claim is visible and diagnosable.

## [0.152.0] - 2026-09-29

### Changed

- Solo world bots pick camps by class: pet casters (Necromancer,
  Bonedancer, Theurgist, Animist, Enchanter, Cabalist, Spiritmaster) look
  for orange mobs, and so do casters that know a root or snare; other
  casters look for yellow, melee and hybrids for yellow or blue, stealthers
  and Clerics/Healers for blue. Pet casters and rooting casters may now solo
  orange at all from level 5; everyone else keeps the yellow ceiling. A defeat still
  lowers the ceiling and clean kills still earn it back.
- After a solo fight a world bot rests only as long as a 2003 player did:
  casters and healers to about 75 % power and 60 % health, melee to about
  80 % health and half endurance, hybrids and stealthers also to half power
  (each bot a few points off), instead of always sitting to full, and
  they do not sit down again while above those marks.
- Autonomous PvE groups pull by composition. A Sorcerer, Mentalist or Bard
  makes a mez group: the puller brings one mob, crowd-control classes mez
  the adds and nobody hits a mezzed add or drops an area spell on it (the
  add control companions already had). Two or more pet classes make a
  mass-pull group: outdoors, the pull also brings idle mobs of the same
  kind within 600 units of the target, on the same floor and in sight, not
  nearer to another party than to the camp, min(6, 2 + pets) in all, and
  never after a recent wipe. Other groups pull one at a time (PBAoE casters
  and the Healer's area stun do not count yet: world bots do not use them
  in PvE).
- Autonomous PvE groups leave a camp when another party of three or more
  that was already fighting there when they arrived keeps fighting for
  three minutes (the later arrival yields), when they have levelled
  out of it (grey to the highest member, or green to the average without a
  recent wipe), or after a wipe, and do not return to it for 15 minutes.
  Enemy-realm players within 2,000 units of the group, at least blue to
  its average level, make the leader walk the group 300-600 units away and
  hold; after 90 seconds of their presence the group leaves. A soloer
  leaves a camp that a group was working before it arrived, after three
  minutes.
  Companions and player-led groups are unchanged.

### Added

- Log lines `PVE_PULL_STYLE group=... style=mez_group|mass_pull|single
  mezzer=... bombers=... pets=... max_pull=...` when a group's style is set
  or changes, `PVE_CAMP_LEAVE group=... reason=rival|outgrown|wipe|enemy
  camp=...`, `PVE_CAMP_ENEMY ... action=shift`, and every ten minutes
  `PVE_REST window_s=600 rests=... avg_power_pct_at_pull=...
  avg_hp_pct_at_pull=... pulls=...` for solo world bots.

## [0.151.0] - 2026-09-29

### Added

- Stealther loop for autonomous RvR assassins (Infiltrator, Shadowblade,
  Nightshade in a solo-assassin, stealth-pack or gank-squad doctrine). They
  now roam and wait stealthed; a solo assassin or pack leader waits at its
  roaming spot 600-1,200 units beside the road instead of walking circles.
  They open only on a soft victim: a caster or healer at the edge of its
  group, a resting or rezzing enemy, a lone walker, the last of a passing
  column or a straggler; never the tank or caster in the middle of a group.
  The opener is the class's own stealth style (Perforate Artery, Backstab
  and so on), which world bots could not use before; the chain follows
  through the normal style choice. After one kill, when a second enemy joins
  in, or below 40 % health they run 300-600 units off at an angle, hide
  again once the server's 10 s combat timer allows, and roam on elsewhere.
  Stealthed world bots now move at the player stealth speed.
- Assist discipline: in groups with a caller, melee, archers and nukers
  take the caller's new target after 1-2 s (the /assist delay players
  reported in 2003), unless their own target is below 30 % health; each
  one misses a call now and then (5-15 % by Patience). The caller leans
  harder on casters and healers. Healers, mezzers and speed classes keep
  doing their own job.
- Interrupts: archers, melee in reach and bots with an instant spell lean
  toward an enemy caster or healer who is casting right now.
- New log lines: `RVR_STEALTH_OPEN`, `RVR_STEALTH_BREAK`, and every five
  minutes `RVR_ASSIST_SWITCH switches=... ignored=... median_delay_ms=...`.

### Changed

- Autonomous RvR bots no longer put a damage-over-time spell on a mezzed
  enemy, or an area spell over one, unless it is the group's assist target.
  Companions and player-led groups are unchanged.

## [0.150.0] - 2026-09-29

### Changed

- Hub-band peace: two autonomous world bots of the same realm can no
  longer fight each other while either of them stands within 6,000 units of
  its own border hub's keep centre (Svasud Faste, Castle Sauvage, Druim
  Ligen), or within 2,500 units beyond the edge of a hub's outer bindstone
  landing. The rule sits in the attack permission itself, so melee, spells,
  pets, area splash, group assist and retaliation are all covered. Reason:
  in the 0.146.0 live log 770 of 1,847 bot PvP deaths happened in one cell
  just outside Svasud Faste, 98 % of them Midgard bots killing Midgard bots,
  and the departure truce from 0.146.0 fired only 41 times because only
  opportunity picks asked it. Humans, companions, player-led groups, other
  realms, monsters and guards are unchanged; a bot attacked by any of them
  inside the band defends normally.

### Added

- New log line every five minutes: `RVR_HUB_PEACE window_s=300 blocked=...
  by_hub=Svasud:...,Sauvage:...,Druim:... stray=...`, and a `hub_band=`
  count of new fights inside a hub band in `AUTONOMOUS_PVP_ENGAGE_SUMMARY`.

## [0.149.0] - 2026-09-29

### Added

- Support by spec for autonomous RvR groups: a smite Cleric or nature Druid
  smites/nukes the leader's target (the Druid also sends its pet) only while
  a second heal-spec healer nearby can cast, the group is at 70-80 % health
  or more (per bot), nobody needs a cure and it has half its power; one fight in three it stays on heals
  anyway. Pac Healers and cave Shamans mez/stun whoever is hitting a group
  mate before healing, unless a mate is below 40 %. Companions and
  player-led groups are unchanged.
- New log lines every five minutes: `RVR_SUPPORT_OFFENSE` and
  `RVR_HEALER_AREA_STUN`.

### Changed

- Autonomous Healers in an RvR bomb group now use their area stun (before:
  player-led groups only).
- Grouped autonomous RvR Bards never melee, even before they have an
  endurance song.

## [0.148.0] - 2026-09-29

### Added

- Observe before engaging: a roaming RvR group (or a solo RvR bot) that sees
  two or more enemy groups, or a group fighting other players, within 5,000
  units, while nobody in it is fighting, holds position and steps back to
  2,200-2,600 units from the nearest enemy (off the road when it stood on it;
  stealth groups watch stealthed). Lone enemies walking by and the heat of
  its own last fight do not start a hold. Every 3 seconds the leader decides:
  engage at once when the group would take that group on anyway, add on the
  watched group once 3 of 8 are seen dead (2 for a bold leader, 1 for a
  clearly bigger group), push in on a mezzer
  (aggressive leaders), pick off a straggler (small and stealth groups),
  leave when charged by a bigger group, seen up close or flanked by a third
  group, or move on past the fight after 30-110 s depending on patience.
  About one hold in seven a leader breaks the rule: the daring add early, the
  careful leave early. Being attacked ends the hold at once.
- New log lines `RVR_OBSERVE` and `RVR_OBSERVE_DECISION`; `RVR_RETREAT`
  gains `reason=observe_leave`.

## [0.147.0] - 2026-09-29

### Added

- RvR groups remember for an hour where they lost people: a careful leader
  stays away from the place, a bold one returns only with more members than
  it lost there, and a revenge hunt waits until the group has at least two
  thirds of the size it lost. Cover routes pass on the side away from the
  worst remembered place.
- Rest after a fight: a roaming RvR group with anyone dead or below 70 %
  health or power sits down, heals, rezzes and regains power until everyone
  is at 90 % or 1-5 minutes pass. It first moves off the road when it stands
  on it or near the fight; any attack ends the rest. Stealth groups hide again
  instead.
- New log lines `RVR_DANGER_RECORD`, `RVR_GROUP_PAUSE`,
  `RVR_GROUP_PAUSE_END` and `RVR_GRUDGE_GATE`; `RVR_ROAM_PICK` gains
  `danger_factor`, `RVR_RETREAT` gains `dest` and `anchor`.

### Changed

- A PvP retreat now runs toward the nearest border hub or landing in the
  zone or a keep the group may pass (or the last roaming spot) instead of just away, and the group chooses a new
  destination afterwards instead of walking back into the same fight.

### Fixed

- RvR route variety now actually happens: about 90 % of flank, cover and
  hub-fan bends (and most road points) were thrown away because the floor
  search looked only 128-512 units up and down, while the frontier's height
  changes by thousands of units within a few kilometres. Bends are now
  searched 4,096 units up and down, placed within the next 8 km of a long
  trip (not halfway along a 100 km leg in another zone), tried at three side
  distances, and accepted when a real path reaches them within 1.5 times the
  straight distance. `RVR_ROUTE_CHOSEN` names the rejecting check with
  `reason=`.

## [0.146.0] - 2026-09-29

### Added

- RvR groups now leave their border keep in different directions: a group
  (or solo RvR bot) setting out from Castle Sauvage, Svasud Faste or Druim
  Ligen first walks to a random point 1,000-2,500 units beyond the edge of
  the safe area it leaves (keep or outer bindstone), on the side of its goal, instead of every group taking the same shortest corridor.
- Departure truce: for up to three minutes after leaving the safe hub, and
  within 2,500 units of its edge, an autonomous RvR bot does not start a fight with
  a same-realm autonomous RvR bot that is also just leaving. Answering an
  attack is always allowed; players, companions and other realms are not
  affected.
- Route variety on the way: each new destination rolls a road, flank or
  cover route by the group's doctrine. Stealth groups mostly leave the road,
  assist trains, melee trains and keep raids mostly keep to it, a cautious
  leader prefers cover away from the latest fighting. Keep assaults and siege
  rallies keep their straight approach.
- New log lines `RVR_ROUTE_CHOSEN`, `RVR_ROAM_PICK`, `RVR_RETREAT` and
  `RVR_HUB_TRUCE` to measure where groups go, why they retreat and how often
  the truce holds.

### Changed

- The straight road route no longer bends by a fixed per-bot offset; a
  leader no longer walks the same bend to every destination.

### Fixed

- None.

### Removed

- None.

## [0.145.1] - 2026-09-29

### Added

- None.

### Changed

- Documented the live 6,210-bot performance investigation in bug 56, with
  measured tick loss, navigation stack samples, and a synchronous Realm
  Exchange listing stall path. The performance fix remains open.

### Fixed

- None.

### Removed

- None.

## [0.145.0] - 2026-09-29

### Added

- None.

### Changed

- None.

### Fixed

- Player-led companions react to song-speed runs after half a second and
  use their bounded catch-up speed on the stick route, so a running group
  closes its gap instead of falling farther behind. Normal walking retains
  its delayed first-step follow.

### Removed

- None.

## [0.144.0] - 2026-09-29

### Added

- A 2880×2160 master splash illustration for the PvP & Co-op world, with
  editable background artwork and an 800×600 preview.

### Changed

- The client startup splash now shows a mixed-realm party defending a breached
  Old Frontiers milegate against a rival guild, with the exact "Offline DAoC"
  and "PvP & Co-op" names
  and the line "Rival guilds. Besieged keeps. Legends forged together."
  The client archive retains its legacy 1024×768, eight-frame TGA format.

### Fixed

- None.

### Removed

- None.

## [0.143.0] - 2026-09-29

### Added

- None.

### Changed

- Traveling Bards cycle speed and endurance songs, including while sprinting.
  The speed song remains the anchor; combat song selection is unchanged.

### Fixed

- None.

### Removed

- None.

## [0.142.0] - 2026-09-29

### Added

- None.

### Changed

- Player-facing launcher and progress-import naming now calls the world
  "PvP & Co-op". The launcher banner describes the Old Frontiers full-PvP
  setting, bots and co-op; population presets and frontier labels use plain
  PvP wording. The `/level` refusal uses the same generic PvP wording.
- Saved world markers, population setting values and backup paths keep their
  existing names so current installations remain compatible.

### Fixed

- None.

### Removed

- None.

## [0.141.1] - 2026-09-28

### Fixed

- A successful keep claim is announced to every player ("<guild> has
  claimed <keep>!"); before, the announcement reached nobody.
- `/gc claim` no longer fails without a word: a server error during the
  claim is shown to the player and logged as `KEEP_CLAIM_FAILED`, and each
  claim attempt is logged as `KEEP_CLAIM_ATTEMPT` (bug 68).

## [0.141.0] - 2026-09-28

### Changed

- Pet pull mode: once a fight is over and the group needs no healing,
  healer companions heal your pet back to full health before the next
  pull (before: only while it was under 80%, and only for a few seconds
  after the release).

## [0.140.0] - 2026-09-28

### Added

- `/stay [on|off]` in pet pull mode: companions of your group and squads
  hold the spots they stand on instead of following you; tanks still meet
  adds at camp and walk back afterwards. An Animist keeps one main turret
  and as many damage mushrooms (Forest's series, no tanglers) as the turret
  caps allow in front of the camp and replants expired ones down to 10%
  power; a Mentalist keeps its heal-over-time on your pet, also out of
  combat. `/stay off`, `/petpull off`, `/passive`, logout or leaving the
  region end it.

### Changed

- `/passive` makes every Animist companion take down all its mushrooms,
  main turret included; it plants nothing while passive.
- Pet pull buffs: Druid and Cleric armor factor buffs (base and spec) and
  the Cleric heal proc now go on the pulling pet first, like damage add and
  damage shield already did.

## [0.139.1] - 2026-09-28

### Changed

- Night report: closing note for the evening round (0.139.0 deployed, keep
  levels and properties migrated at start). Documentation only.

## [0.139.0] - 2026-09-28

### Changed

- NPC-held (Frontier Wardens) frontier keeps start at keep level 1 like an
  unclaimed 1.65 keep: guards 52, lord 63, outer door 10,000 hit points
  (before: level 5 with guards 59, lord 70 and 50,000). Guild-claimed keeps
  keep their level and upgrade path. Wall height shown in the client drops
  accordingly.
- Each guild may run its own siege (at most six automatic sieges server-wide)
  and claim up to three keeps; before, one siege per server and one keep per
  guild.
- Direct-damage spells and bolts hit keep doors at half effect after door
  toughness, as in patch 1.46; damage-over-time, debuffs, crowd control and
  area spells do not. Bot casters without a ram seat nuke the gate.
- The installed save's `starting_keep_level` (4) and `guilds_claim_limit`
  (1) are updated to 1 and 3 at the first start only while they still hold
  the shipped defaults. Relic raids stay off (task 56, later).

## [0.138.0] - 2026-09-28

### Added

- None.

### Changed

- During a held `/petpull`, companions keep a heal-over-time on the pet but
  delay direct pet heals and tank peels until the pull is released. They still
  intercept mobs already attacking or running toward the group.

### Fixed

- Pre-release companion heals and tank peels no longer draw some of the pet's
  attackers toward the waiting group, including group heals redirected from a
  Necromancer shade to its servant.

### Removed

- None.

## [0.137.0] - 2026-09-28

### Changed

- Fewer server stutters from RvR bots: the ExecuteRvr and stable-network
  spikes were bot turns waiting on SQLite reads (merchant lists behind slow
  saves). Merchant lists and the stable-master network now refresh on a
  background worker while callers keep the old value, world bots run the
  stable-route corridor scan in short slices, solo bots skip the group
  coordinator lock, and lock waits and database opens appear in the minute
  profile. Bot decisions are unchanged.

## [0.136.0] - 2026-09-28

### Changed

- RvR warbands walk like a 2003 group: routes bend once around named
  monsters of level 55 and above, red or purple monsters and dense camps;
  a warband attacked by such a monster breaks off instead of fighting it
  (keep guards and lords excepted); the shared frontier dungeons (Hall of
  the Corrupt, Summoner's Hall, Marfach Caverns, Dodens Gruva) are no longer
  used as destinations, hunting grounds or shortcuts. A released member
  rejoins a leader in another frontier through the porter; Albion and
  Midgard forces port home first where their foreign portal keep sells only
  the home medallion; relic carriers and forces without a porter route keep
  the dungeon road as a last resort and walk it through.
- Warbands regroup before porting back: nobody ports within 75 seconds of
  its own release, a warband waits until its members are alive and gathered
  at the porter (at most three minutes), and a warband leaves at most once
  per five minutes. Before, single released members re-ported every three to
  four minutes (about 690 departures per hour).

## [0.135.0] - 2026-09-28

### Fixed

- World-bot warbands can besiege keeps again. The siege job (buy a ram at the
  hub, place it at the outer gate, fire it) had no caller since the Camlann
  tier-4 rework on 2026-09-20, so in eleven hours nobody hit a door, no lord
  was ever exposed and every siege waited out its four-hour timer.

### Changed

- At a keep, melee bots hit the closed outer gate when no guard is in reach,
  then the inner gate; casters of the operator's group ride the ram; the lord
  becomes a target only when every gate is down. Rams are operated by tanks,
  melee fighters, Scouts and Rangers.
- Only claimable keeps (base level 50, not portal or relic keeps) are chosen
  for automatic sieges; automatic relic-keep assaults stop until relic raids
  return as their own mode.
- A siege nobody reaches closes after 45 minutes without an attacker within
  3,000 units of the keep (porting back and forth no longer counts as
  progress), which frees the server's single siege slot for another keep.
- Only a whole one-guild warband of eight opens a siege; smaller forces join
  only their own guild's siege. Players attacking a keep count as presence.

## [0.134.0] - 2026-09-28

### Fixed

- Generated charm pets of Sorcerer, Minstrel and Mentalist bots (and of
  players using the generated charm) no longer come back as wild aggressive
  mobs after they die. Their body kept the mob template's respawn timer and
  lost its pet marker on death, so phantom magi, savage dragonflies and
  similar Shrouded Isles mobs respawned at the hub bindstones and killed bots
  there: 47 % of all PvE kills in an eleven-hour run. Existing ghost camps
  disappear at the next server start.

### Added

- A second safe circle at Castle Sauvage (1,500 units) and Svasud Faste
  (1,800 units) covers the fallback teleporter landing and the outer
  bindstones, for players and bots alike (Camlann decision 7, option b). The
  road between the outer bindstones and Castle Sauvage stays open for PvP.

## [0.133.1] - 2026-09-28

### Changed

- Agent docs: the orchestrator brief and the advisor agent reference the
  local checkout at its new SSD location (`C:\OfflineDAoC`). Documentation
  only.

## [0.133.0] - 2026-09-28

### Added

- None.

### Changed

- None.

### Fixed

- Mobs attacked by the controlled pet during a held `/petpull` no longer
  trigger ordinary group BAF, even when a group member is selected by
  threat. Normal BAF remains available outside the held pet pull.

### Removed

- None.

## [0.132.1] - 2026-09-28

### Changed

- Night report: live re-measurement after eleven hours on 0.125.0 (levelling,
  RvR presence, ticks, deaths) with follow-up proposals; bugs 65 and 66
  recorded, bug 56 extended with the new hot spots. Documentation only.

## [0.132.0] - 2026-09-28

### Added

- None.

### Changed

- Siege Rams deal 10x their rider-adjusted damage for temporary testing; the
  source comment marks the multiplier for removal after testing.

### Fixed

- None.

### Removed

- None.

## [0.131.0] - 2026-09-28

### Added

- The Companion Manager now shows each companion's earned Realm Points, realm
  rank, and unspent realm ability points. Its Training & Tactics pane lets the
  owner buy one class-legal passive realm ability rank at a time for active or
  benched companions; purchases persist across sessions.

### Changed

- Companion realm rank now follows earned Realm Points when awards arrive and
  when the companion is loaded.

### Fixed

- None.

### Removed

- None.

## [0.130.0] - 2026-09-28

### Added

- The Companion Manager labels roster entries as Regular or Story and offers a
  two-click permanent delete action on each companion's Overview. Deletion
  removes the roster record and disposable starter gear together, while earned,
  traded, or unclassified items block deletion.

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.129.1] - 2026-09-28

### Added

- None.

### Changed

- Bug 5 now records that generated cloth-cap models vary by realm and the
  equipment packet sends the stored model unchanged; a cross-realm model
  mismatch is the leading explanation, pending client visual confirmation.

### Fixed

- None.

### Removed

- None.

## [0.129.0] - 2026-09-28

### Added

- The Companion Manager window can be enlarged by dragging its lower-right
  resize handle; its list and detail panels, background, and bottom controls
  remain aligned as the window grows.

### Changed

- The default Companion Manager size is 720×500 instead of 640×420. Its rows
  and detail lines have more space, and the detail text area is wider.

### Fixed

- None.

### Removed

- None.

## [0.128.1] - 2026-09-28

### Added

- None.

### Changed

- Bug 5 now records the Matterbender Cloth Cap and Elf female Enchanter
  shown in the supplied oversized-helmet screenshots; server/client versions
  and the item template ID remain unknown.

### Fixed

- None.

### Removed

- None.

## [0.128.0] - 2026-09-28

### Added

- None.

### Changed

- Persistent Enchanter companions now prefer the highest learned Underhill
  Ally pet across builds, falling back to normal pet selection when it is
  unavailable. An existing alternate pet is replaced only while idle.

### Fixed

- None.

### Removed

- None.

## [0.127.0] - 2026-09-28

### Added

- `/gc join <guild name>` lets a character join a guild at rank 0 when another
  character on the same account already holds rank 0 in that guild.

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.126.0] - 2026-09-28

### Added

- `/companions guild leave <name>` removes an owned active or benched
  companion from their guild, including after the owner has left it.

### Changed

- Recorded intermittent missing overhead names for players and companions
  after login, zoning, or reappearing as bug 64; the source cause remains
  unconfirmed.

### Fixed

- None.

### Removed

- None.

## [0.125.0] - 2026-09-28

### Changed

- `/petpull` is now a group mode instead of a one-off command: `/petpull`
  toggles it (`/petpull on|off` sets it) for your companions and squads.
  While it is on, you pull the normal way with your pet, and every pull runs
  as a pet pull: companions hold their damage until the passive pet is back
  beside you, heal and buff the pet first, tanks taunt an add off a swarmed
  or hurt pet, and only adds on the group or heading for it are engaged. A
  release ends that one pull, not the mode; the next pet attack is the next
  pull. Fights you open yourself, orders onto monsters already in combat and
  orders onto enemy players never count as pet pulls. The Mentalist HoT, the
  Animist camp front and the pet buff priority stay as before.

## [0.124.0] - 2026-09-28

### Changed

- Autonomous PvE parties that are within reach of their camp when the
  30-minute travel window closes, and fought or gained experience in the last
  ten minutes, start their full task instead of disbanding (dungeon parties
  fighting their way in no longer get broken up). A party merely idling near
  the camp still ends.
- Pickup parties prefer outdoor camps within about ten minutes of their
  meeting point.
- Members of a group that is still meeting up or travelling carry a real
  future task expiry instead of an empty one, so RvR tours no longer end at
  once after a restart or raid transfer. Party ends and members' camp
  arrivals are logged truthfully.

## [0.123.1] - 2026-09-28

### Fixed

- The launcher test suite is green again: the population-settings fixture
  passes the control's current constructor arguments, and the XP-rate
  persistence test no longer fails while a real local server is running.
  Launcher behaviour is unchanged.

## [0.123.0] - 2026-09-28

### Added

- Castle Sauvage, Svasud Faste and Druim Ligen are safe hubs within about
  3,500 units, for players and bots alike (Camlann decision 7): nobody can be
  attacked inside, and nobody inside can be hit from the edge. Before, 82 % of
  all bot deaths happened at these hubs.
- Warbands board the frontier porter together: a group waits up to a minute
  for members near the porter, then leaves with whoever is ready; the
  departure log names the party size and who was left behind.

### Changed

- The keep-route planner uses the same door rule as the doors themselves, so
  portal keeps are passable for every realm and bots no longer freeze inside
  the enemy portal keeps of Odin's Gate (76 level-50 RvR bots were stuck there
  with 519 failed exterior routes in 3.4 hours).
- When any member of a warband fails a keep route three times, the whole
  warband calls that keep off for 20 minutes and roams from where it stands;
  an automatic siege nobody makes progress toward closes after 15 minutes
  instead of blocking every other assault for hours. Marching toward the
  keep, porting over and reaching the walls count as progress.
- RvR world bots killed by an enemy in the frontier release at their own
  realm's hub, and bots still under release immunity do not pick fights.

## [0.122.0] - 2026-09-28

### Fixed

- The live bot dashboard snapshot and the world-speed status file no longer
  fail when another program briefly holds the published file open: both
  publishers retry the atomic replace for about a quarter second before
  warning, and the launcher reads the world-speed status with delete sharing.
  Bug 26, listed as finished, was still occurring and is fixed the same way.

## [0.121.0] - 2026-09-28

### Changed

- Solo world bots regain their nerve: after a PvE death the target
  difficulty still drops one step, but it now recovers one step after about
  ten kills without dying, on a level-up and on a new task, and a solo bot
  then moves to a harder camp within local reach ("Moving on to tougher
  prey") when one exists. Before, the ceiling only reset with a server
  restart, so most sub-50 bots were farming green mobs for a quarter of the
  experience.
- A death counts as PvP for that ceiling when an enemy player, companion or
  world bot damaged the bot in the last 30 seconds, even if a mob landed the
  killing blow.
- Solo world bots up to level 35 pick levelling camps within about ten
  minutes of travel (then twenty, then anywhere), and solo camp choice
  weights blue and yellow camps twice as high as green ones.

### Added

- One structured `AUTONOMOUS_BOT_DEATH` log line per world-bot death
  (killer, type, class, area effect, whether the bot was the killer's target,
  PvP/PvE classification and the resulting con ceiling), plus
  `AUTONOMOUS_CON_RECOVERY` lines, so levelling and collateral-PvP deaths can
  be measured live.

## [0.120.0] - 2026-09-28

### Changed

- World-bot AI turns cost less: a bot whose route stalled now searches its
  side-step over several short turns (standing still for a few seconds at
  most) instead of checking 21 corridors in one turn, and the stable-master
  network is cached for 30 minutes and refreshed in the background instead of
  being rebuilt every 5 minutes under one global lock. Decisions are unchanged.
- The server logs a one-line bot think profile every minute
  (`BOT_THINK_PROFILE`, plus up to five `BOT_THINK_SLOW` lines naming the
  slowest turns and their phases) so slow AI phases can be measured live.

### Fixed

- Skill cooldown lookups no longer hash through boxed value types, and a
  world bot without a group no longer waits on the group coordinator lock
  for its watchdog check.

## [0.119.0] - 2026-09-28

### Fixed

- Autonomous PvE parties no longer wait for an hour or more on one group
  member who died far away or in another region. The group's camp activity
  no longer restarts the resurrection wait, only living members near the
  corpse count as rescuers, a corpse with no rescuer nearby releases at once,
  and a member who dies twice more on the way back is dropped so the rest of
  the party keeps hunting. A party that loses its camp during such a wait
  picks a new target instead of idling out its travel window.

## [0.118.0] - 2026-09-28

### Fixed

- `/tc` is registered once again: the corpse-transfer command dropped its
  `&tc` alias, so `/tc` reliably teleports to the capital's Realm Exchange and
  the server start no longer logs the `LoadCommands` duplicate-key error. A
  test now fails if two command handlers ever claim the same key.

## [0.117.0] - 2026-09-28

### Added

- Companion squads for battlegroups: `/companions squad <1-5> add|remove|lead
  <name>`, `disband` and `list` form up to five companion-led groups beside
  your own. Squad leaders march in a staggered fan 200–400 units behind you
  and their members follow them; portals and region changes bring every
  squad along. Assignments are saved and restored at login.
- `/bg` shows each owner's companions and takes them along, so two players
  can share one battlegroup with their own companion squads.
- Companions appear like players: in `/who`, in the launcher's Active
  Population list (with Realm Points and "With <owner>"), and `/send` to a
  companion reaches its owner and gets a short reply.
- Companions earn Realm Points in RvR by the autonomous-bot formula (no PvP
  XP, no loot ownership); saved in a new additive column.

### Changed

- Squad members fight with their owner: they assist his target, join his
  pulls and pet pulls, defend him, his group and his other squads, heal and
  resurrect across his squads once their own group is fine, and obey
  `/passive`, `/defensive` and `/aggressive`.
- New companion recruits need a name no real character or world bot uses.

### Fixed

- Companions buffed before resurrecting a dead player (bug 55). In combat
  they now resurrect at once with the strongest rank their power allows; out
  of combat they save power for their best resurrection, then buff.

### Removed

- None.

## [0.116.0] - 2026-09-28

### Added

- None.

### Changed

- None.

### Fixed

- Companions no longer stay behind where the navigation mesh cannot follow
  their player, such as the tall steps at the Darkness Falls entrance that a
  player walks down almost like a drop. When a following companion's paths
  to its leader keep failing for 2 s, it joins the leader on the floor at
  the leader's feet, the way native pets already do, if both are out of
  combat and within 1,024 units. Companions in combat and autonomous
  gamebots keep their existing behavior (bug 54).

### Removed

- None.

## [0.115.0] - 2026-09-27

### Added

- `/petpull`: an Animist companion plants its field turrets in front of the
  waiting group, toward the pull, so the returning pet drags the pack
  through the mushrooms.
- `/petpull`: for ten minutes after the last pet pull, the pulling pet is the
  group's tank and companions give it every buff that works on pets before
  anyone else: strength, constitution, dexterity and quickness buffs, damage
  add, damage and ablative shields, resists and heal-over-time. Armor, haste
  and acuity buffs stay with the group (no other concentration buff affects
  pets). Without a pet pull the buff order is unchanged.

### Changed

- `/petpull` now opens the fight the 1.65 way: the player sets the pet
  passive and it brings the pull back to camp; companions engage once the
  passive pet is within 400 units of the player. The 75 % target-health
  release is gone; the release when the pet drops below 45 % or dies, or
  when the player attacks, stays, with a 60 s safety release (task 39).

### Fixed

- None.

### Removed

- None.

## [0.114.0] - 2026-09-27

### Added

- `/petpull`, the 1.65 Enchanter pet pull: with an enemy targeted, the
  player's pet goes in alone and takes the pack. Companions hold their
  damage and do not defend the pet, companions with a heal-over-time keep it
  on the pet (the Mentalist HoT drew no aggro), healers heal only the group,
  and tanks peel adds that reach the group. The group opens once the pet has
  held for 3 s and its target is under 75 % health (after 10 s at the
  latest), at once if the pet drops below 45 % or dies, or when the player
  attacks. After the release every companion heals the pet like a group
  member until the fight has been quiet for 8 s; bombers skip their tank
  wait for that pull (task 39).

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.113.0] - 2026-09-27

### Added

- Companion Manager: an **Active** tab next to Roster and Recruit lists only
  the roster companions currently in the player's group, with the same
  detail panel (overview, training and tactics, gear) as Roster. The tab is
  a new plain text link in the Custom8 window XML; the patched game.dll is
  byte-identical, so the tab needs only the new `custom8_window.xml` in both
  UI skins. Clients with the old XML keep working without the tab. The
  selected tab is named in the status line; the Active link itself is not
  highlighted (task 38).

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.112.0] - 2026-09-27

### Added

- None.

### Changed

- Persistent companions belong to the account instead of one character:
  every character of an account sees and can invite the same companion
  roster, with their levels, builds, equipment and guild. Each record keeps
  the character that recruited it; the 78-companion roster limit and name
  lookups now count the whole account. One account is never online on two
  characters at once, so a companion is never invited twice (task 37).

### Fixed

- None.

### Removed

- None.

## [0.111.0] - 2026-09-27

### Added

- Companions sprint on a stick run while their leader sprints: when the
  leader runs a speed run long enough for the companions to fall into the
  stick line and turns on sprint, each companion sprints as well, and stops
  when the leader stops sprinting, stops running, or the fight starts. A
  sprinting companion pays the player's endurance cost (5 per second before
  an endurance regeneration buff); without sprint, companion travel still
  costs no endurance (task 36).

### Changed

- None.

### Fixed

- The sprint effect's name no longer requires a player owner.

### Removed

- None.

## [0.110.0] - 2026-09-27

### Added

- None.

### Changed

- Player-led companions ration pure debuffs (strength, strength/constitution,
  dexterity, combat speed, resistance and similar debuffs without damage):
  each debuff type is cast at most once per fight, area debuffs at most
  twice. The budget renews once the companion has been out of combat for
  5 s after its last debuff. Damage spells with a debuff component, snares,
  and damage over time are unchanged; autonomous gamebots keep their
  rotation (bug 53).

### Fixed

- A Suppression Spiritmaster companion no longer drains its power by
  re-applying up to four instant debuffs to every mob of a pull and again
  whenever they expired: at level 50 one full set cost about 113 power, as
  much as seven bombs (bug 53).

### Removed

- None.

## [0.109.0] - 2026-09-27

### Added

- None.

### Changed

- None.

### Fixed

- Automatic companions whose preserved weapons no longer match their selected
  build can fight with a class-legal weapon already equipped instead of clearing
  their target and standing idle. Build-matching weapons remain preferred;
  owned equipment and automatic upgrade rules are preserved (bug 52).

### Removed

- None.

## [0.108.0] - 2026-09-27

### Added

- None.

### Changed

- None.

### Fixed

- Companion bombers in PvE no longer wait again for every new mob: they
  give the tank one short moment at the start of a fight, then bomb the
  rest of the fight without fresh waits, and stay in the knot when they
  switch to the next mob.

### Removed

- None.

## [0.107.0] - 2026-09-27

### Added

- None.

### Changed

- Companion kill rewards follow 1.65-style loot: about 45 % armor, 35 %
  jewelry and 20 % weapons or shields, and only weapons the companion can
  actually use. A full backpack sells unusable gear first, then the weakest
  by level, quality and bonuses, no longer jewelry before weapons.
- Companions' hits, resists and blocks no longer appear in the owner's own
  combat chat; they show like any groupmate's. The player's own pets are
  unchanged.

### Fixed

- The Darkness Falls weekly quests count again: companions no longer raise
  the required monster con in "Darkness Falls Invasion", and enemy-realm
  bots count for "Femurs from Darkness Falls" (all three realms).

### Removed

- None.

## [0.106.0] - 2026-09-27

### Added

- None.

### Changed

- Indoors (dungeons such as Darkness Falls) companions keep a step or two
  away from their player instead of standing on top of them, and they ignore
  the player's first steps there as well.

### Fixed

- A healer no longer runs its own weaker speed when a groupmate skald, bard
  or minstrel has a stronger speed song; the healer's speed replaced the song.

### Removed

- None.

## [0.105.0] - 2026-09-27

### Added

- A raid calendar: about every 1.5-2.5 hours a dragon or epic dungeon raid is
  announced 25 minutes ahead. Level-50 bots sign up by type and sociability
  and keep doing what they do; at the start time everyone who signed up leaves
  their group (the rest of the group carries on) and gathers at the muster.
  The raid needs at least 40 sign-ups, moves out once most of them (at least
  24) have gathered, and stages for at least 10 minutes. Too few sign-ups call
  it off.

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.104.0] - 2026-09-27

### Added

- Bots in Darkness Falls spend their Diamond, Emerald and Sapphire seals at
  the DF seal merchants on the best equipment upgrade they can afford.

### Changed

- Level-50 bots no longer owe a PvE task after an RvR tour: they take a town
  break (sell, buy, train) and may go straight back out.
- Level-50 PvE prefers dungeons, above all Darkness Falls (double weight
  among dungeons), for seals and gear.

### Fixed

- None.

### Removed

- None.

## [0.103.0] - 2026-09-27

### Added

- Companions follow like a groupmate: they ignore your first steps, trail in
  a /stick line behind you on a long speed run, and otherwise keep a looser,
  more spread-out spot around you.

### Changed

- Bots released to their capital (or recovered there) get a personal spot
  300-800 units around the anchor instead of one shared coordinate.
- A PvE party that cannot choose a target for five minutes breaks up;
  members far from their party leader walk back to it instead of freezing.
- Bots visit a merchant once their backpack is 70 % full, not only when it is
  completely full.

### Fixed

- Autonomous bots, solo or grouped, no longer stand still for 2.5-6.5 s at
  every waypoint: arriving on a travel leg wakes them to choose the next leg.
- Bots stacked on one spot next to the Jordheim vault keeper (and the matching
  anchors in Camelot and Tir na Nog) after dying in foreign zones.
- A bot at a seal merchant can no longer buy seal gear for copper.

### Removed

- None.

## [0.102.0] - 2026-09-27

### Added

- RvR warbands keep recruiting after they leave: nearby roaming guildmates
  join, guildmates waiting LFG at the border keep are invited and run out to
  the group, and two small groups of one guild that meet in the field merge
  into one without ending anyone's RvR tour.

### Changed

- Group travel is smooth: followers aim at a slot around where the leader is
  about to be, keep walking while the leader walks, re-steer only when the
  slot really moved, and match the leader's pace with a small personal stride
  and catch-up. The march is a loose staggered column with smoothed turns
  instead of a two-file queue.
- A walking group no longer stops for cast-time buffs; they wait for the
  leader's next pause. Followers of a moving leader think at travel cadence.
- The leader waits for stragglers only past 700 units and walks on once all
  are within 400, instead of stopping and starting at a single 500 limit.
- Player companions look half a second ahead and only re-issue a walk when
  their slot really moved, so they no longer stop-start several times a
  second.

### Fixed

- Followers no longer walk to where the leader was, stop, and wait seconds
  for their next decision (stop-go conga lines).

### Removed

- None.

## [0.101.0] - 2026-09-27

### Added

- RvR bots who want a group (healers, casters, tanks; not stealthers,
  archers or Hunter-type players) wait at their realm's border keep with LFG
  for 8-20 minutes by patience, idle and out of combat, where guild leaders
  can recruit them; then they go out alone.

### Changed

- Roamer and keep-warrior leaders leave with a viable group: eight when
  available, four after three minutes, three after eight, instead of waiting
  ten minutes for six. Guildmates happy in groups of four or more fill open
  slots of bigger groups.
- A partial death no longer sends an RvR group back to regroup: the living
  finish the fight and rez afterwards, and the dead wait up to three minutes
  while a rezzer lives. Only a wipe (one survivor, or most dead with no
  rezzer) regroups, now at the realm's border keep instead of a town.
- PvP deaths inside an RvR group of three or more no longer count toward the
  three-deaths "take a break" rule that pulled bots out of their group.

### Fixed

- Bots now get the spells of their trained specialization level. Every bot
  spec line was capped at 75 % of the character level (38 at level 50), so
  a Battlesongs 46 Skald sang speed 4 (Magnificent Song of Travel) instead
  of speed 5 (Heavenly Song of Travel), and every hybrid and caster missed
  its top spec spells. Untrained lines keep the old fallback.

### Removed

- None.

## [0.100.0] - 2026-09-27

### Added

- None.

### Changed

- Roaming RvR groups are drawn to the classic meeting grounds: Emain Macha
  counts three times, Hadrian's Wall and Odin's Gate 1.6 times, and other
  frontiers now weigh 0.6 instead of 0.35, so groups cross realm borders more
  often. Keep destinations use their zone, not their region, for this.

### Fixed

- None.

### Removed

- None.

## [0.99.0] - 2026-09-27

### Added

- RvR warbands already in the frontier may gather at their own guild keep or
  their realm's border keep instead of walking back to a town first.

### Changed

- Companion PvE loot: one kill gives one roll to one random eligible
  companion per owner, instead of a roll for every companion.
- A companion with a full backpack sells up to 16 of its worst earned,
  unlocked items at once instead of one item per drop; [Keep], starter,
  player-supplied and legacy items are never sold.
- Siege sides on Camlann are guilds: the guild that opens an assault
  attacks, the keep owner's guild defends, and other guilds contest on their
  own. Rally orders, keep plans, attendance, rally posts, friendly doors,
  siege engines and siege jobs follow the guild instead of each member's
  realm, so mixed-realm guild warbands gather and fight as one force.

### Fixed

- The companion inventory window no longer jumps back to the top after an
  item is moved (bug 44).
- Mixed-realm guild warbands no longer lose their siege rally, plan or
  attendance for members born in another realm; a hostile guild that shares
  a keep's realm is no longer recruited as its defender.
- Server unit tests no longer fail by run order (bug 38); stale expectations
  were updated and the buff pet pass skips its realm scan when nothing is
  affordable.

### Removed

- None.

## [0.98.0] - 2026-09-27

### Added

- Autonomous RvR warbands read a doctrine from their real classes: 14 group
  systems of the 1.65 era, from solo assassin, stealth pack and caster duo
  to small-man, assist train, bomb group, melee train, pickup group, gank
  squad and keep raid party (docs/RVR_GROUP_DOCTRINE.md). Imperfect groups
  are normal and still roam and fight.
- Guildmates nearby who are not busy come to help a guildmate under attack,
  depending on their sociability.
- Guilds remember recent wins and losses against other guilds for an hour and
  take bolder or more careful fights against them.

### Changed

- RvR bots choose targets like people: they stick to a target for a while,
  follow the caller in called groups most of the time, otherwise lean toward
  enemy healers and mezzers, wounded enemies and whoever is on their healers,
  with some spread.
- Fight appetite follows doctrine and the leader's personality; a group may
  dare a bigger group now and then.
- A losing group may retreat (healer dead, half down, clearly outnumbered)
  and run about 2,200 units away for 25-40 s before regrouping; some groups
  stay in anyway.
- Roaming groups wander between keeps, frontier clearings, enemy sightings
  and recent fight spots instead of one fixed loop, linger at a spot from
  arrival, and march in a doctrine formation (two-file column, clump, or
  loose fan) with melee in front, healers in the middle and casters behind.
- RvR healers heal the keep guards of their own guild's keep, not every guard
  of their realm.

### Fixed

- Roaming RvR groups no longer leave a patrol spot the moment they reach it
  after a long walk.

### Removed

- The fixed west-to-east camp loop for Roamer warbands.

## [0.97.0] - 2026-09-27

### Added

- Healer companion build `support` (Mending 42 / Augmentation 24 /
  Pacification 23): a main healer with celerity, group resists, and a second
  cure mez, so the pac Healer is not the only one who can demez.

### Changed

- Automatic companion builds now level breakpoint-first, as players did:
  Skald Battlesongs follows the character level to 43 (speed 5), the
  Augmentation Shaman reaches Cave 27 (instant area disease) by about level
  43, the shield Thanes reach Stormcalling 34 and Shields 42 (Slam), and the
  Pacification Healer reaches 38 (instant area stun). Points are saved for the
  next breakpoint instead of being spread thin. Level-50 targets are
  unchanged, and existing automatic companions are retrained to the new
  schedule for free when they next load or level.
- Companion bombers hold their first PBAoE on an enemy player clump for up to
  2.5 s while a group Healer has an area stun ready, then bomb the stunned
  pile (stun, then bomb).
- Roster companions now assist their owner's PvP target like an assist train
  and defend group members against attackers, as temporary helpers already
  did.

### Fixed

- Companion bombers run into the middle of the pile before bombing instead of
  casting from half the spell radius away, where linear falloff cost about half
  the damage (bug 43). A blocked run bombs from where it stands after 3 s.

### Removed

- None.

## [0.96.0] - 2026-09-27

### Added

- Expanded companion builds to at least three researched plans for all 39
  Classic + SI classes (118 plans total), retaining the original default IDs.
- Added a saved per-companion ranged-AoE threshold from Off or 2+ through 8+,
  defaulting to 3+ enemies.

### Changed

- Automatic builds now drive companion specialization and combat profiles
  from the selected plan. Fresh recruits receive matching starter gear;
  existing owner-supplied items and manual equipment locks are preserved.
- Ranged area damage counts committed mobs and hostile guards from the same
  keep, and refuses casts that would hit bystanders, players, or protected
  mezzes. Harmful Necromancer servant area wrappers use the same threshold and
  safety checks at the servant-centered payload area.

### Fixed

- Made build roles follow their class-legal plan mapping and allowed Attacker
  Smite Clerics to use offense without the random low-mana throttle, while
  preserving the support role path.
- Corrected Spiritmaster build labels so Suppression, not Darkness, is identified
  as the PBAoE bomb specialization.

### Removed

- Removed stale manual-only build blockers; every Classic + SI class now has
  an available default and at least three build choices.

## [0.95.0] - 2026-09-27

### Added

- None.

### Changed

- Offensive caster damage spells now cost 70% less mana, up from 50%; all
  mana-costing buffs and pet summons cost 90% less.

### Fixed

- None.

### Removed

- None.

## [0.94.0] - 2026-09-27

### Added

- None.

### Changed

- Realm ability training no longer enforces character-level or ability-specific
  prerequisites, including Augmented Dexterity II for Mastery of Pain. Realm Point
  costs, maximum ranks, and class availability remain; RR5 abilities still unlock
  at Realm Level 40.

### Fixed

- None.

### Removed

- None.

## [0.93.0] - 2026-09-27

### Added

- None.

### Changed

- Player-led companions buff less and waste less power. Out of combat they
  keep only long buffs (5 minutes or longer, or concentration buffs) and
  refresh them in their last minute; short buffs are no longer kept up out of
  combat, and speed only while the group travels. Group pets get only long
  buffs.
- Player-led Skalds no longer twist every chant out of combat: they sing the
  speed song while the group travels and stay quiet while it stands or rests.

### Fixed

- None.

### Removed

- None.

## [0.92.0] - 2026-09-27

### Added

- The companion inventory window (Companion Manager **[Open inventory]**)
  shows the worn slots at positions 1-19 in character-sheet order and the
  backpack at 21-60. Drag an item from your own backpack onto a worn slot to
  hand it over and equip it in one step; drag a worn item to your backpack to
  take it back (starter gear stays in the companion's backpack). Item info
  works on every position.
- **[Info]** in the Gear tab opens the native item info window for a worn or
  selected backpack item. Worn slots show their inventory window position.

### Changed

- **[Equip + lock]** is now **[Equip]**: an item you equip is marked as your
  choice (`*`) and is replaced only when the companion earns an item that is
  clearly better (more than 8 points and 5% of what it replaces). You get a
  chat line when that happens, and your item goes to its backpack, still
  yours. **[Lock slot]** keeps a slot fixed as before; existing locks stay
  locks.
- **[Open bag]** is now **[Open inventory]**, and the companion's backpack
  moved from window positions 1-40 to 21-60.
- A clicked worn slot in the Gear tab shows only the worn item and its
  actions; the lists of fitting items are gone, because equipping is done by
  dragging in the inventory window.

### Fixed

- None.

### Removed

- None.

## [0.91.0] - 2026-09-27

### Added

- None.

### Changed

- Players sitting out of combat now recover health, power, and endurance like
  resting companions: one tick per second, at least 10% of the pool, starting
  as soon as they sit. Standing and combat regeneration stay classic.

### Fixed

- None.

### Removed

- None.

## [0.90.1] - 2026-09-27

### Added

- Bug #42 in `docs/BUGS.md`: every player kill freezes the server for about
  0.4-6 s because each companion gets a synchronously saved gear reward, with
  questions for the owner (per-companion rolls vs. a loot pool per fight).

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.90.0] - 2026-09-27

### Added

- None.

### Changed

- Roster companions now rest to full whenever their player-leader sits and any
  health, power, or endurance is missing, instead of only below 70% health,
  45% power, or 35% endurance. Travel and combat still interrupt the rest.

### Fixed

- None.

### Removed

- None.

## [0.89.0] - 2026-09-27

### Added

- None.

### Changed

- Offensive caster damage spells cost 50% less mana for companion bots and player characters, including PBAoE bombs; healing, buffs and nondamaging spells retain their existing costs.

### Fixed

- None.

### Removed

- None.

## [0.88.0] - 2026-09-27

### Added

- None.

### Changed

- None.

### Fixed

- Healer bots cast available Celerity on group members who need it, including during combat; Celerity effects now update melee speed and are recognized during buff upkeep.

### Removed

- None.

## [0.87.0] - 2026-09-27

### Added

- None.

### Changed

- Realm abilities with character-level requirements can be trained at any character
  level, including Charge, Wild Power, Toughness, maximum health, Decimation
  Trap, and Atlas Old Frontiers Striking the Soul. Realm Rank requirements and
  ability-specific prerequisites remain.

### Fixed

- None.

### Removed

- None.

## [0.86.0] - 2026-09-27

### Added

- None.

### Changed

- None.

### Fixed

- Real players are evaluated against keep ownership like gamebots. Hostile Fensalir Faste guards become attackable to players, and right-clicking its intact doors no longer traverses them; friendly keep access remains available.

### Removed

- None.

## [0.85.0] - 2026-09-26

### Added

- None.

### Changed

- Player logout completes immediately, including during combat and while moving. The former quit timer and its server setting are removed; existing logout restrictions and save handling remain.

### Fixed

- `/stuck` clears its safe-position request when logout is refused.

### Removed

- None.

## [0.84.0] - 2026-09-26

### Added

- Persistent Armsman, Hero, and Warrior companions use Taunting Shout on packs already attacking their group when at least two enemies are in the cone. They avoid idle enemies, protected mezzes, and player targets; ordinary single-target taunts remain the fallback.

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.83.1] - 2026-09-26

### Added

- None.

### Changed

- Documented that persistent companions receive no PvP kill XP or Realm Points and that their damage does not give their owner PvP kill credit.

### Fixed

- None.

### Removed

- None.

## [0.83.0] - 2026-09-26

### Added

- None.

### Changed

- Bot buff upkeep and fallback selection cover eligible group members before attached pets across routine buffs. Pets receive remaining coverage once member needs are met.

### Fixed

- None.

### Removed

- None.

## [0.82.0] - 2026-09-26

### Added

- None.

### Changed

- Grouped Shamans prioritize their highest learned endurance regeneration buff for nearby group members before other routine buffs. If their concentration is full, they release a lower priority buff to make room for endurance coverage.

### Fixed

- None.

### Removed

- None.

## [0.81.0] - 2026-09-26

### Added

- Companion bombers open each pull with a loose volley: the first bomber in position holds its first bomb for at most 1.2 s until the group's other companion bombers within 1,500 units are ready, then all fire and chain freely; each bomb keeps a 6 s chain window open. No hold starts while a cast is running.

### Changed

- None.

### Fixed

- Tanks only queue taunts their wielded weapon can execute; previously the highest-level learned taunt was chosen regardless of weapon (for example an Armsman's polearm Distract while wielding sword and shield), so no taunt fired. Applies to every tank class with taunts in several weapon lines.

### Removed

- None.

## [0.80.0] - 2026-09-26

### Added

- None.

### Changed

- Gear trades with a persistent companion reach the 400-unit follow distance instead of the 256-unit loot pickup distance.
- A refused gear trade names its actual blocker: distance with both values, remaining combat seconds, casting, aggro, a fighting pet, group or zone.
- The companion [Open bag] window lists only items the owner can take out; starter, protected and untradable gear remains visible in the Gear tab.

### Fixed

- Dragging starter or protected gear out of the companion bag window silently failed.
- Gear trades failed with a generic message although the companion stood at its normal follow distance.

### Removed

- None.

## [0.79.0] - 2026-09-26

### Added

- None.

### Changed

- Darkness Falls receives triple rather than double destination weight within eligible dungeon choices for autonomous XP camps and Hunter PvP patrols. The overall dungeon choice rates and population pressure remain unchanged.

### Fixed

- Autonomous routes to destinations outside Darkness Falls no longer use its entrances and exits as a cross-realm transit shortcut. Explicit DF camp and patrol destinations remain reachable.

### Removed

- None.

## [0.78.0] - 2026-09-26

### Added

- None.

### Changed

- Companion bombers count NPCs inside the bomb radius that are already fighting a group member or a member's pet toward the PvE bomb threshold; idle spawns still do not count.

### Fixed

- Companion bombers no longer stay at range in PvE: the per-member focus set rarely reached the three targets Auto requires, so the bomb never counted as ready and no approach started.

### Removed

- None.

## [0.77.0] - 2026-09-26

### Added

- Join Friend remembers the host address, host account and guest account per Windows user, and can store the guest password encrypted with Windows DPAPI when "Remember my password" is ticked.

### Changed

- The Join Friend form pre-fills the saved details; unticking "Remember my password" removes the stored password.

### Fixed

- None.

### Removed

- None.

## [0.76.0] - 2026-09-26

### Added

- None.

### Changed

- None.

### Fixed

- Persistent companions no longer swap a shield and a two-handed weapon on every AI tick; equipment upgrades are weighed against every weapon they displace. The loop saved to the database each time and stalled the game loop for up to 1.4 s.
- /pull only commands companions assigned to the player issuing it, preserving each other player's companion follow leader in a shared group.

### Removed

- None.

## [0.75.0] - 2026-09-26

### Added

- Guild recruitment logs exclusive first-rejection counts across the live roster: other guild, existing group, current task, level, preferred party size, death, missing region, raid reservation, combat, riding and required PvE. Route probes and unprobed candidates are reported separately.
- Late-party recruitment logs route, planned-camp, formation-slot and join rejections; bounded travel-hold snapshots show member positions and distances to the leader.

### Changed

- Ordinary PvE combat/recovery travel holds apply to nearby party members, allowing safe remote members to resume real travel. Personal defense, full-party pull checks, expedition rules and task deadlines remain intact.

### Fixed

- Rendezvous town detection tolerates an actor with no current zone during travel/recovery instead of dereferencing the unsafe area getter.
- A PvE leader recognizes members already in the final camp region as ahead of its next crossing, avoiding a circular wait across multiple region edges.
- A single remaining population allocation seat can backfill an assembling PvE party before the minimum-two check for creating new parties.

### Removed

- None.

## [0.74.0] - 2026-09-26

### Added

- Guild recruitment diagnostics distinguish eligible candidates, reachable candidates, route-budget usage and requested party size. Group outcome records include camp coordinates, member classes, motion/combat/riding state and same-region distance to camp.

### Changed

- Initial PvP parties can recruit for ten simulated minutes. Eligible guildmates consider spare seats in assembling parties at task boundaries; eight waiting guildmates no longer suppress invitations. Active tasks and mandatory PvE intermissions remain authoritative.
- Late recruitment recalculates missing healer/tank roles after each addition and defers failed candidates for thirty simulated seconds so other candidates can use the bounded route budget.

### Fixed

- Late PvE recruits must leave the planned pickup camp usable by every member before joining, matching the initial roster check.

### Removed

- None.

## [0.73.0] - 2026-09-26

### Added

- Initial autonomous parties recruit reachable members into spare seats for two minutes, prioritizing existing assembling parties before creating more small groups.

### Changed

- PvP matchmaking recruits same-guild members across regions using validated town-teleporter routes, prioritizes organized warband leaders and selects healing/tank support before filling damage slots. Eligible Hybrid, Roamer and KeepWarrior bots can accept waiting guildmates' invitations at their next task boundary.
- Hybrid and KeepWarrior parties prefer eight members, with bounded waiting and smaller roaming fallbacks. New automatic keep assaults require at least eight members, a healer and siege supplies; smaller parties can still reinforce existing battles.
- PvE remote recruitment can consider enough candidates to fill a normal eight-member party, with bounded route checks.

### Fixed

- PvP opponent evaluation handles groups with no living nearby members without throwing an empty-sequence exception.

### Removed

- The same-region-only restriction on autonomous guild PvP matchmaking.

## [0.72.0] - 2026-09-26

### Added

- Frontier Wardens stationary NPC garrisons hold initially unclaimed Old Frontier keeps and guard the six home relic shrines. Killing a keep lord unlocks a claim steward; player groups and autonomous guild crews can claim through the same proximity, rank, group-size, and guild-limit checks.
- Keep guards award 25 base RP; nearby living members of the capturing group and guild receive 1,500 base RP, with a persistent 30-minute reward cooldown per keep.
- Setup can restore 4,851 archived Old Frontier monster rows matching retained zone, species, and level rosters, preserving their saved locations, templates, and loot.

### Changed

- Old Frontiers and Darkness Falls monster kills grant a 50% base-XP bonus on top of the selected player/companion or autonomous-bot XP rate.
- Keep ownership and defeated-lord claim state survive restarts. Fresh/reset worlds initialize NPC garrisons; existing player and autonomous guild claims stay intact. Relic pickup requires a defeated shrine garrison or keep lord, and the dashboard names guild owners.

### Fixed

- Human frontier XP no longer substitutes the frontier rate for the selected XP rate in Camlann.
- Ordinary keep doors handle door-request packets and suppress duplicate traversal from one client click; bot traversal and guard hostility use guild ownership.
- Pure autonomous guilds receive native claim permission for their ordinary members, and crews explicitly approach and use defeated keeps' claim stewards.

### Removed

- Free claiming of undefeated Old Frontier keeps.

## [0.71.0] - 2026-09-26

### Added

- None.

### Changed

- None.

### Fixed

- Effect expiration and replacement no longer acquire effect-state and effect-list locks in opposite order, preventing the observed game-loop deadlock.
- Neutral world and frontier hasteners use the server's alliance rules and show success only when their speed effect takes hold; blocked casts explain why.
- Legacy saved tank companions align a missing build plan with their trained weapon specialization without replacing saved equipment or valid plans.
- Autonomous PvE groups choose camps within a bounded party travel estimate and verify candidate routes before committing their fixed 30-minute travel window.

### Removed

- None.

## [0.70.0] - 2026-09-26

### Added

- Added a live launcher action to rebalance the full non-retired saved autonomous-bot roster, including offline bots, with pending/applied/failed status and safe-boundary handling for active bots.

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.69.0] - 2026-09-26

### Added

- None.

### Changed

- Grouped Bards with endurance songs maintain their instrument pulse during combat and continue their group-support actions; mana and speed songs wait until after combat.

### Fixed

- None.

### Removed

- None.

## [0.68.0] - 2026-09-26

### Added

- None.

### Changed

- Replaced the Active Groups card stack with a sortable, realm-filterable table and selected-group roster details, reducing per-refresh control construction while retaining live group timers.

### Fixed

- None.

### Removed

- None.

## [0.67.0] - 2026-09-26

### Added

- Active Population shows sortable Realm Points for autonomous bots, using live values when available and saved values otherwise.

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.66.1] - 2026-09-25

### Added

- Recorded the live effect-lock server deadlock and client disconnect for investigation.

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.66.0] - 2026-09-25

### Added

- None.

### Changed

- Realm frontier travel now leads to Old Frontiers mob areas and no longer offers Agramon.

### Fixed

- The keep Chief claim prompt accepts eligible guilds at unclaimed Camlann keeps, and eight-member claims use each group member's actual position at the keep.
- Legacy New Frontiers teleport rows can no longer send players to region 163 through the shared teleporter.

### Removed

- None.

## [0.65.0] - 2026-09-25

### Added

- Player-led casters set to Bomb can use PBAoE against at least two PvP opponents already fighting their group, while avoiding idle and mezzed players.

### Changed

- Ready bombers prefer their highest learned PBAoE rank. Focus casters prioritize staves by the focus levels covering their learned spell lines, including all-lines focus, before ordinary item value.

### Fixed

- A staff with unrelated focus no longer displaces a staff covering the caster's spell lines solely because its item level is higher.

### Removed

- None.

## [0.64.0] - 2026-09-25

### Added

- Owned companions accept `/gc invite` immediately; persistent companions keep guild membership after being benched or reloaded and show the guild emblem on equipped cloaks and shields.

### Changed

- `/gc form` permits a solo founder at a registrar and asks only other human group members to approve the guild.

### Fixed

- Guild founding no longer stalls when companion bots occupy group slots.

### Removed

- None.

## [0.63.0] - 2026-09-25

### Added

- Nearby player-led companions board the player's siege ram in available seats and leave when the player dismounts. Companion seats count toward ram damage and reload timing.
- Grouped player-led Healers use learned area stuns against clustered enemies already fighting the group. A bomb caster with a learned PBAoE spell draws stun placement toward its pull, with healing still taking priority when a group member is below 65% health.

### Changed

- Defensive companions follow a player's active attack on a closed enemy keep door beyond the normal defensive radius. Pet classes command their pets to that door, and Theurgists repeatedly summon pets while their normal cast and power rules allow it.

### Fixed

- Keep doors with a grey con no longer block an explicit player-led companion attack or pet order.

### Removed

- None.

## [0.62.1] - 2026-09-25

### Added

- A tracked task and implementation checklist for applying population-type
  mixes to existing autonomous bots while the server runs, with safe task
  transitions, progress preservation and restart persistence acceptance checks.

## [0.62.0] - 2026-09-25

### Changed

- Expanded all six population-type tooltips with practical PvE/PvP guidance,
  including Hunter crews versus Roamer groups, level thresholds, and the fact
  that slider weights apply to new bots rather than changing saved types.
- The Danger tooltip now explains that it affects existing Hunters after a
  restart, including the increased grey-target aggression of Full Camlann.

## [0.61.0] - 2026-09-25

### Added

- Darkness Falls destinations for autonomous XP parties and Hunters, backed by
  1,417 entrance-specific round-trip navigation proofs. A hash-checked cached
  repair connects the nine entrance stairs while preserving all other mesh
  tiles and the installed navigation file.
- An offline DF audit/patch-packaging tool and a Camlann activity review with
  read-only live-session findings and pending real-client checks.

### Changed

- Hunters choose dungeons on 30% of eligible destination draws. Darkness Falls
  gets twice the destination weight within the dungeon share for XP and hunts;
  ordinary pickup groups can select proved camps in connected dungeons.
- Launcher charter/activity labels and population-setting explanations use PvP
  while retaining compatible saved charter and objective identifiers.

### Fixed

- Frontier first-strike scans respect PvE assignments, recovery and opposing
  party strength while retaining real defense and committed siege combat.
- Hunter patrol dwell time starts on arrival rather than departure; dungeon
  arrival checks include height, and generic hunt scans reject stealthed targets.
- Gamebot path smoothing retains DF stair-link endpoints, including companions
  following over the previously disconnected Midgard entrance stairs.

### Removed

- The separate shared-dungeon aggression scan that bypassed assigned PvE work.

## [0.60.0] - 2026-09-25

### Added

- A client-only Join Friend launcher mode (`--join` and a Windows shortcut) for
  connecting to a host's Tailscale IPv4 address without starting the local
  server or opening the local world save. Source implementation awaits a real
  two-home client check.

### Changed

- Launcher deployment now installs `Join Friend.cmd` with backup and rollback
  handling for the new file.

### Fixed

- None.

### Removed

- None.

## [0.59.2] - 2026-09-25

### Added

- A fast implementation plan for private two-home Tailscale co-op, with a
  client-only guest launcher path and live verification in both hosting directions.

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.59.1] - 2026-09-25

### Added

- A dedicated `docs/TASKS.md` for rapid-fire tasks and ideas, with open,
  verification-pending, and finished states and explicit Done records.
- Agent tracking rules and README links for the bug and task lists.

### Changed

- Moved the autonomous-bot dungeon review from the Tasks section in
  `docs/BUGS.md` to `docs/TASKS.md`, preserving its scope and open status.

## [0.59.0] - 2026-09-25

### Added

- Autonomous PvE pickup groups can match compatible bots across all three realms
  and prefer a viable mixed-realm party. Remote members travel through an active
  `AllRealmsTeleporter` to the leader's town rendezvous; the Active Groups card
  shows their 45-minute simulated-time meetup deadline, which starts when the
  remote group forms.

### Changed

- Pickup groups compare a bounded shortlist of live camps across up to three
  regions, then elect a nearby leader and stage in a connected town. Camp
  suitability, crowding, travel, familiar locations, and known deaths affect the
  choice. Mixed-realm parties are preferred within a 20-minute estimated meetup
  journey and a five-minute detour over a viable nearby party.
- Remote members start traveling while the leader stages. Productive, healthy
  ordinary PvE parties can continue together for one additional task when no
  member needs training or inventory services; camps are reconsidered at renewal.
- PvE visitors stay where their activities leave them, including during town
  downtime and after save reload. Explicit post-RvR returns still go home, and
  completing the journey preserves the separate requirement to finish PvE.

### Fixed

- Remote invitations validate the complete porter approach and onward town
  journey, including intervening zone crossings. Arrival towns are chosen near
  the actual rendezvous; unreachable nearby porters and local applicants no
  longer prevent usable alternatives from joining. Travel requires real porter
  proximity, life, and no combat, without a catch-up teleport.
- Post-group location no longer depends on mutable activity text. Removing a
  no-show immediately refreshes party roles and preserves prior attendance when
  formation slots shift at the expired shared deadline. Remote meetups use a
  shared 45-minute simulated-time deadline; local-only meetups remain 15 minutes and the leader
  staging limit remains 20 minutes. The shared 45–120-minute task still starts
  on camp arrival, with its separate 30-minute camp travel window.

### Removed

- None.

## [0.58.0] - 2026-09-25

### Added

- A separate 30-minute simulated-time travel window for autonomous PvE parties after choosing a camp. The Active Groups card shows its remaining travel time.

### Changed

- The shared 45–120-minute PvE task starts when a party reaches its camp instead of when the leader chooses the camp. Subsequent recovery and camp replanning still consume the original task duration.

### Fixed

- Long outbound trips no longer exhaust a group's fighting time before it reaches the camp. A party that misses its travel window sends its members to solo PvE before they can seek another group.

### Removed

- None.

## [0.57.0] - 2026-09-25

### Added

- World Speed control in the launcher for 1×, 2×, and 3× live-world simulation while no game client is connected, with selected, effective, and achieved speed status.

### Changed

- Gameplay clocks and saved deadlines advance with completed world ticks at the selected speed. A connecting client restores 1×; the selected speed resumes after the last client disconnects. Each server start selects 1×.
- Simulated time is checkpointed with the local save and resumes through server downtime at 1×.

### Fixed

- Launcher bot-task and bot-auction countdowns use simulated time during and after accelerated sessions.

### Removed

- None.

## [0.56.0] - 2026-09-24

### Added

- None.

### Changed

- Bound starter equipment on saved companions is refreshed as they level while earned and manually equipped items remain intact.
- Tanks prioritize attackers of healers, then bomb casters, then group leaders when peeling adds.
- Mixed-realm group equipment drops use one selected member's class and realm; autonomous group and raid drops go first to members who can equip an upgrade.

### Fixed

- New persistent companions and loaded autonomous bots fill missing armor and appropriate shield slots even when the normal item tables are sparse.
- PvP NPC creation and group membership changes maintain allied gamebot name colors and Tab targeting status without marking hostile gamebots friendly.
- An urgent peel can interrupt a tank's offensive cast against a different target.

### Removed

- None.

## [0.55.0] - 2026-09-24

### Added

- None.

### Changed

- Pickup-group formation now applies the same role-adjusted monster-level limit as its camp planner, so incomplete parties do not form for camps they cannot use.
- Solo death recovery below level 20 keeps the safest non-grey target con while favoring nearby home-realm camps.

### Fixed

- Autonomous world bots below level 10 stay protected from player-shaped attacks even after a stale RvR safety opt-in. Direct attack and damage paths also protect the bot and its controlled pets; monster combat and player companions keep their existing rules.

### Removed

- None.

## [0.54.0] - 2026-09-24

### Added

- Pickup groups check the live local camp catalog before forming.

### Changed

- Solo bots below level 20 favor camps within ten estimated travel minutes in
  their home realm, weighing travel and crowding together across nearby zones.
  Ordinary leveling trips use at most two stable hops; bots walk to the next
  camp after release.
- Autonomous release prefers a validated bind point in the death zone, and
  movement or goal-stall recovery prefers a bind in the current leveling zone
  before falling back to the capital.

### Fixed

- A group with no usable camp dissolves after its planner exhausts the level
  fallbacks. Recovery no longer keeps a fully rested, targetless group idle.

### Removed

- None.

## [0.53.0] - 2026-09-24

### Added

- `AUTONOMOUS_PVP_ENGAGE_SUMMARY`: a per-minute count of new PvP fights that
  involve autonomous bots, grouped by how the first blow was struck and by the
  attacker's level band and player type.
- `pvpDeaths` and `pveDeaths` on `AUTONOMOUS_ACTIVITY_SUMMARY`.

### Changed

- Autonomous bots below level 10 have the same implicit PvP safety as a
  flagged player until an RvR assignment relinquishes it (review phase A1).
- Guild kill-on-sight lists skip grey killers, fights the victim started, and
  bot-on-bot kills below level 10. A KOS target is hunted first but still has to
  pass the level, grey, and party-strength checks.
- Outside RvR tasks, bot crowd control targets only opponents already in the
  fight and does not use area mezzes.
- A PvP death no longer lowers a bot's PvE target difficulty. The
  three-PvP-deaths wall applies only to RvR tasks and to level 10+ types other
  than Leveler and Casual.

### Fixed

- Guild kill-on-sight rows are written in batches on a timer thread instead
  of synchronously during bot death processing, which stalled the reaper
  tick for 30–80 ms on each bot-on-bot kill.

### Removed

- None.

## [0.52.1] - 2026-09-24

### Added

- `docs/AUTONOMOUS_BOT_M7_REVIEW.md`: first live review of the autonomous bot
  roadmap on a 1,500-bot fresh launch, with a phased improvement plan.

### Changed

- The autonomous bot roadmap links the review as the first M7 input.

### Fixed

- None.

### Removed

- None.

## [0.52.0] - 2026-09-24

### Added

- None.

### Changed

- Companion buff coverage now checks active buffs from other group members on the target; a human player's known spell alone no longer suppresses a base buff.
- Guard and Protect assignments honor their native effect ranges, including existing effects from outside the managed companion group.
- Bombing's tank-aggro grace period restarts when the focused pull target changes.

### Fixed

- Idle player-led companions no longer repeat the same pet summon without an owner-level increase that improves the pet.

### Removed

- None.

## [0.51.0] - 2026-09-24

### Added

- None.

### Changed

- None.

### Fixed

- Player-led companions keep an idle pet summoned by the preferred spell until they learn a stronger summon or gain enough levels to improve the pet.

### Removed

- None.

## [0.50.0] - 2026-09-24

### Added

- M6 autonomous social behavior: player-type chat, guild banter, post-fight
  taunts, trade and LFG lines, invented charter-themed guild names, and
  common/joking character-name styles.
- Guild KOS memories that include the player, expire after three hours, respect
  worth and safe-area rules, and direct existing RvR crews toward reachable
  targets.
- A blocklist for slurs in authored autonomous chat and generated names while
  leaving ordinary profanity available.

### Changed

- Autonomous guild and faction conversations use existing chat rate limits and
  only speak to audiences with permission to hear the channel.

### Fixed

- Exchange advertisements use channel-appropriate wording, and generated bot
  replies pass through the chat safety policy.

### Removed

- None.

## [0.49.0] - 2026-09-24

### Added

- Plain-language tooltips for server population presets, player-type mix,
  leveling-zone danger, and world-shape controls.
- A saved per-companion Auto/Bomb/Off preference for PBAoE spell use.

### Changed

- Companion buff maintenance recognizes stronger learned ranks, prioritizes
  specialization buffs, and assigns Guard and Protect across group members.
- Idle player-led companions upgrade to stronger summons; Enchanters prefer
  Underhill Ally when available.
- Bomb-capable companions prioritize PBAoE on sufficiently large focused pulls
  and allow tanks a brief aggro window before bombing.

### Fixed

- Mob BAF resolves player-led companions and controlled pets to their group,
  counts bot members for add selection, and retains the related experience bonus.

### Removed

- None.

## [0.48.0] - 2026-09-24

### Added

- Expanded `docs/BUGS.md` with the reported companion, dungeon, and server-population issues.

### Changed

- Ignore generated server build output and its NuGet/MSBuild cache files.

### Fixed

- Restored 2,310 archived monster rows across all Classic realm dungeons and the supported Old Frontiers dungeons through an additive Setup migration.

### Removed

- None.

## [0.47.0] - 2026-09-24

### Added

- None.

### Changed

- None.

### Fixed

- All-realm travel uses standard fallback routes when an advertised
  destination is missing from the installed teleport table. Menu labels
  with full Shrouded Isles names and trailing spaces now resolve correctly.

### Removed

- None.

## [0.46.1] - 2026-09-24

### Added

- `docs/BUGS.md` starts a list of confirmed, unresolved bugs.

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.46.0] - 2026-09-24

### Added

- `/companions reset [name]` recreates active persistent companions at the
  owner's current location, saving their roster progress and gear first.

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.45.0] - 2026-09-24

### Added

- Established-server bot creation across five level bands with level-matched
  starting experience, class equipment, first-login specialization training,
  and realm points for new level-50 bots.
- Configurable level-1 guild alts, limited by a total-roster cap.
- Local population samples at stable 500, 1,000, and 1,500 active bots for a
  measured launcher size recommendation after all three runs.

### Changed

- Add crew follows Fresh launch or Established world shape. The explicit Add
  Lv.50 action remains available, and existing bot progress is retained.
- The launcher withholds a numeric roster recommendation until this PC has
  all three measured memory and game-loop tick samples.

### Fixed

- New level-1–49 bots receive class-appropriate generated starting gear without
  depending on the world's sparse lower-level item templates. A missing
  level-50 class loadout still rolls back its creation batch.

### Removed

- The earlier unbenchmarked numeric population-size estimate.

## [0.44.0] - 2026-09-24

### Added

- A Server population launcher screen with named presets, six player-type
  percentages, leveling-zone danger, world shape, and an advisory roster-size
  suggestion based on CPU cores and memory.
- Player type, guild, and guild charter columns in Active Population.

### Changed

- `bot-goals.json` version 2 stores the population mix, danger, and world shape.
  The server reads it at startup; new autonomous identities and guild charters
  use the selected mix. Version-1 goal weights map to the nearest preset for
  launcher review before saving.

### Fixed

- Population settings cannot be saved with a mix other than 100%, while the
  server is running, or over a valid file after a failed write.

### Removed

- The old Bot Goals Setting sliders and their objective-exclusion behavior.

## [0.43.0] - 2026-09-24

### Added

- Type-specific Hunter patrols, Roamer frontier loops and group sizes, Keep
  warrior campaigns from level 35, and a server danger setting for leveling
  zones.

### Changed

- Hybrid roams favor local evening hours; Casuals take town breaks more often.
  Level-50 bots with weak weapons or armor favor gear farming and dungeons.
- Hunter patrols favor occupied, level-appropriate outdoor camps and nearby
  routes while retaining occasional quiet-area visits.

### Fixed

- Rare grey-target engagement now uses one stable chance per target and
  ten-minute window instead of rerolling every combat scan.

### Removed

- None.

## [0.42.0] - 2026-09-24

### Added

- Persistent player type, five temperament traits, PvE wall state, and managed
  guild charter rows for autonomous bots.

### Changed

- Generated guilds receive invented names and durable managed markers. New
  autonomous tasks follow each bot's type, level phase, wall state, and guild
  raid reservation while existing task timers continue.

### Fixed

- Guild consolidation and rename paths persist keep ownership and alliance
  reference updates before deleting or renaming generated guilds.

### Removed

- Random per-guild level-bucket task allocation and the visible `Camlann Crew`
  prefix on managed guilds.

## [0.41.0] - 2026-09-24

### Added

- Per-minute autonomous activity counts and live outdoor camp pressure signals.

### Changed

- Ordinary PvE bots form local pickup groups across guilds and realms. Groups
  depart with two arrivals, accept late followers, and preserve the party and
  camp after a single death while the released member returns.
- Outdoor camp selection softly favors less crowded and recently productive
  spawns, while keeping every valid destination eligible.

### Fixed

- Level 1–4 Flexible-build Reavers use Slash until their Flexible ability
  unlocks at level 5, then equip their planned weapon.

### Removed

- Automatic open-world ganking by ordinary PvE parties and whole-party
  disbanding when only some meetup members fail to arrive.

## [0.40.0] - 2026-09-24

### Added

- Focused XP checks for autonomous bots, temporary helpers, companion catch-up,
  and PvP con-color rewards.

### Changed

- PvP kill XP now uses the player's `XP_RATE` or the autonomous bot's
  `BOT_XP_RATE`. Challenge XP and realm-point bonuses follow the victim's con
  color: orange 1.25×, red 1.5×, purple 2×.
- A companion's NPC kill XP uses the smaller of its owner's base award and its
  own-level cap, then `XP_RATE` and a 1.5× catch-up boost when five or more
  levels behind. The owner's total XP remains the final ceiling.
- Updated the autonomous roadmap and companion reward guide; synchronized the
  launcher, launcher-test, and command-reference version pins.

### Fixed

- Autonomous bots and temporary `/spawn` helpers again receive their configured
  XP multiplier on mob kills. Autonomous bots also regain zone and item XP
  bonuses.

### Removed

- Unused autonomous XP scaling method.

## [0.39.0] - 2026-09-24

### Added

- A slot sheet in the Companion Manager's Gear tab. It lists all 19 worn slots
  in character-sheet order, including empty ones, and marks slots where the
  companion's bag holds items that fit (`2 fit`) or a better item
  (`upgrade in bag`).
- Clicking a slot opens it: the worn item with its stats, and every bag item
  the companion can equip there, best first, with its score change.
  **[Equip + lock]** equips the selected item in that slot (a ring or bracer on
  the side you clicked); **[Unequip]**, **[Lock slot]**, and **[Keep]** act on
  the worn item. Click the slot again to close it.
- A test for the slot sheet order and paired ring and wrist matching.

### Changed

- The slot sheet replaces the Gear tab's list of worn items. The backpack
  list below it keeps its **[Equip + lock]**, **[Return to me]**, and
  **[Keep]** actions.
- Benched companions show all worn slots, including empty ones.
- Updated the Companion Manager roadmap, the integration handoff, and the
  command references.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- The worn slots at positions 51-69 of the companion bag window (0.38.0). The
  bag window shows only the 40 backpack positions again, and the drag-to-equip
  and drag-to-unequip rules that came with it are gone.

## [0.38.0] - 2026-09-24

### Added

- Worn slots in the companion bag window (M3). The bag window now also shows
  the companion's worn equipment at positions 51-69, two per row: helm, chest,
  arms, gloves, legs, boots, cloak, neck, jewel, belt, left and right wrist,
  left and right ring, right hand, left hand, two-handed, ranged, and mythical.
  Positions 1-40 are still the backpack.
- Drag and drop to equip: drop a companion bag item on a worn position to
  equip and lock it (like **[Equip + lock]**). The item goes to its own slot;
  a ring or bracer goes to the side it was dropped on. Drag a worn item to an
  empty companion bag slot to unequip it there (like **[Unequip]**).
- A test for the vault layout: backpack and worn positions, no overlap, and
  paired rings and wrists in one row.

### Changed

- The Gear tab and the **[Open bag]** message name the worn positions.
- The equip message now names the slot the item went to.
- Updated the Companion Manager roadmap, the integration handoff, and the
  command references.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- None.

## [0.37.0] - 2026-09-24

### Added

- Group orders in the Companion Manager (M2). A **Group orders** row leads the
  roster list. It sets the group order (Aggressive, Defensive, Passive, or
  saved stances) and shows each grouped companion's effective stance, with
  its saved stance when the order overrides it.
- Group actions in that row: **[Pull]** (like `/pull`), **[Invite all]**,
  **[Bench all]**, and **[Grind]**/**[Stop grind]** (like `/grind`).
  **[Invite all]** invites the benched companions shown in the list, top to
  bottom, until the group is full, so search and filters choose who comes.
  **[Bench all]** benches every active companion.
- A test for the group row: order links, per-companion override text, and
  the pull, invite-all, and grind refusal messages.

### Changed

- `/aggressive`, `/defensive`, `/passive`, `/companions group default`, and
  `/pull` now share their code with the window; their behaviour and messages
  are unchanged. `/grind` still accepts only temporary `/spawn` helpers.
- Updated the Companion Manager roadmap, the integration handoff, and the
  command references.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- None.

## [0.36.0] - 2026-09-24

### Added

- Crowd control role for companions (M1c). Healer, Sorcerer, Bard, Mentalist,
  and Spiritmaster companions can take it with `/companions role <name> cc`
  or in the manager's Training & Tactics tab.
- PvE add control. Once the group is fighting a monster, a companion with
  crowd control duty mezzes extra monsters that are attacking the group,
  with non-tanks' attackers first. It never mezzes the group's target or the
  owner's target, and it skips monsters that are immune, below 75% health,
  or taking damage over time. Two companions never mezz the same add, and a
  mezz is recast after it wears off.
- Mezz protection in player-led groups. Companions leave a mezzed monster alone
  while any other enemy is left, and skip area spells that would hit it. If
  the owner attacks the mezzed monster, companions attack it too.
- Each build sets a role when it is chosen or used at recruitment, following
  the owner's Healer mapping: Tri-spec is Healer and also controls adds,
  Mending is Healer, Augmentation is Buffer, and Pacification is Crowd
  control. Sorcerer Body and Mind and Bard Music are Crowd control. Bard
  Nurture and Shaman Augmentation are Buffer, Friar Group support is Healer,
  and Armsman Two-handed is Attacker. The other builds keep the class default.
- Tests for the role values, the build-to-role mapping, and the `cc` role
  command.

### Changed

- Build lists in the window and in `/companions build` say which role each
  build sets. Role names show as "Crowd control" rather than the saved value.
- Class role labels include "Crowd control" for the five classes that can
  fill it.
- A Pacification Healer in the Crowd control role keeps healing when nothing
  needs control.
- Updated the Companion Manager roadmap, the integration handoff, and the
  command references.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- A support companion no longer cancels its own crowd-control cast while
  holding back from melee.

### Removed

- None.

## [0.35.0] - 2026-09-24

### Added

- Build list in the Companion Manager (M1b). The Training & Tactics tab lists
  every build for the selected companion with its role and marks the current
  one. Selecting a build shows its level-50 targets. **[Use build]** switches
  to it with the M1a rules: free, no trainer, reset and retrain to the current
  level. It works for active and benched companions.
- Build choice in the manager's recruit flow. Selecting a story companion or a
  class lists its builds with the class default preselected; **[Recruit]** or
  **[Create]** uses the selected build. Manual-only classes say so.
- `/companions recruit authored <name> [build]` recruits a story companion
  with a chosen build.
- Integration test for the window's build list, **[Use build]**, and build
  choice during recruitment.

### Changed

- The manager's Overview tab names the build that automatic training follows.
- Updated the Companion Manager roadmap, the integration handoff, and the
  command references.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- The Training & Tactics tab no longer tells players to switch builds with a
  command. `/companions build` still works.

## [0.34.0] - 2026-09-24

### Added

- Build choice for persistent companions (Companion Manager M1a). The catalog
  now holds 57 named, validated builds for 35 classes, for example Healer
  Tri-spec, Mending (healer), Augmentation (buffer), and Pacification (crowd
  control), or Spiritmaster Darkness (bomb), Suppression, and Summoning (pet).
- `/companions build <name>` lists a companion's builds and marks the current
  one. `/companions build <name> <build>` switches builds for active or benched
  companions: it is free, needs no trainer or respec eligibility, resets that
  companion's specializations, and retrains the new build to its level.
- `/companions recruit <class> [build]` recruits with a chosen build.
- Automatic builds for Wizard (Fire, Ice, Earth) and Animist (Creeping,
  Arboreal), which were manual-only.
- Research record for the 24 added builds, each labelled sourced, adjusted, or
  project recommendation, and tests for every build through level 50, build
  switching, and rejected build choices.

### Changed

- Automatic level-up training follows the companion's saved build instead of
  only the class default. The 33 original `general-pve-v1` plan IDs are
  unchanged and remain each class's default build, so existing saves need no
  migration.
- `/companions mode <name> automatic` keeps a companion's valid saved build.
- `/companions plan <name>`, `/companions list`, profiles, and the manager's
  Training & Tactics tab show build names and the build keys.
- Updated the Companion Manager roadmap: the M1 switch-cost and trainer
  decisions are recorded, and M1 is split into M1a, M1b, and M1c.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- None.

## [0.33.0] - 2026-09-24

### Added

- A separate, hash-guarded raid click-to-target client patch: a stage builder
  (`build_raid_click_fix_client.py`), an offline emulation test, and
  `tools/dev/Install-RaidClickFix.ps1` (dry run by default, backup, rollback,
  restore). It changes only the four raid window XML files and never
  `game.dll`.
- Companion Manager labels 130 and 131 for the detail `[Up]` and `[Down]`
  links, in a rebuilt manager `game.dll`.

### Changed

- The detail `[Up]` and `[Down]` links in the Companion Manager now appear
  only in the direction that can scroll. A 0.32.1 client ignores the new labels
  and keeps its static links.
- Recorded that the companion bag's "House Vault 1" caption comes from a fixed
  client string; the server can change only the number.
- Updated the Companion Manager roadmap and integration handoff. The M0 items
  passed offline checks and wait for the owner's real-client check.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- Clicking a member in the 40- or 80-person raid window did nothing. The raid
  XML used event names that the client's parser ignores; both raid builders
  and the new patch now use the numeric IDs `1536`–`1615`, which the existing
  raid handler turns into target selection.

### Removed

- None.

## [0.32.4] - 2026-09-24

### Added

- An autonomous bot behaviour roadmap (`docs/AUTONOMOUS_BOT_ROADMAP.md`): a
  review of the current population system, findings on the slowdown around
  levels 10–12, Camlann and Mordred research, the owner's design decisions,
  and milestones for player types, guild charters, the launcher's population
  settings, rewards, and chat.

### Changed

- Linked the roadmap from the README and the Camlann plan.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None. The roadmap records, but does not yet fix, two XP bugs: autonomous bots
  and `/spawn` helpers have received 1× XP from mob kills since 0.24.0, and a
  low-level persistent companion receives its owner's full kill award.

### Removed

- None.

## [0.32.3] - 2026-09-24

### Added

- None.

### Changed

- Recorded the owner's acceptance of persistent companion Stage 3, Stage 5,
  and companion-window checks. Stage 4 gear and Stage 6 integration checks
  remain open.
- Moved the completed development setup plan to `docs/completed/DEV-SETUP.md`
  and updated its references.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- None.

## [0.32.2] - 2026-09-24

### Added

- None.

### Changed

- Recorded the owner's build-selection decisions in the Companion Manager
  roadmap: research the popular 1.65 builds per class, switch builds with an
  automatic respec and retrain, let builds set roles (including a new crowd
  control role), and choose a build when recruiting.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- None.

## [0.32.1] - 2026-09-24

### Added

- The client patch test now emulates the client's own `OnClickEvent` parser on
  every window click value and checks which parser function reads it.
- A Companion Manager roadmap: close-out checks, the raid click fix, build
  selection with automatic training, behaviour controls, and a companion
  equipment slot layout.
- The owner's real-client gate result: clicking and search work in 0.32.1.

### Changed

- The Companion Manager client patch no longer hooks the `ControlId` name
  mapper; the raid's patch bytes there are left unchanged.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- Companion Manager buttons did nothing in the real client. The window now uses
  numeric click event IDs, which the client's XML parser accepts, instead of
  custom event names, which it silently ignored.

### Removed

- None.

## [0.32.0] - 2026-09-24

### Added

- A native Companion Manager window (`Custom8`): Roster and Recruit lists with
  realm and role filters, name/class search, and scrolling; Overview, Training
  & Tactics, and Gear details; invite, bench, recruit, tactics, training,
  respec, gear actions, and the native companion bag.
- `/companions find <name or class>` search. The window's **[Search]** link
  opens the chat line with it prefilled.
- A hash-guarded client patch builder, offline x86 emulation test, and dry-run
  installer with rollback (`tools/dev/Install-CompanionManager.ps1`). Nothing
  is installed automatically.

### Changed

- Manager clicks travel through the client's own slash-command path, and search
  uses the ordinary chat line. This replaces the probe's failed action packet
  and edit box. Every click is resolved against a server-held session and
  current roster; stale clicks are refused.
- Bare `/companions` opens the manager and prints one line of command guidance
  when no patched client answers. All subcommands are unchanged.
- Companion gear operations moved into a shared helper used by the manager and
  the legacy menu. The respec start is shared with the manager.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- None.

## [0.31.1] - 2026-09-23

### Added

- A Companion Manager integration handoff recording verified client controls,
  the failed action/search gate, server reuse, safe packaging, and acceptance.

### Changed

- Linked the handoff from the companion roadmap and synchronized version pins.

### Fixed

- None.

### Removed

- None.

## [0.31.0] - 2026-09-23

### Added

- A passive companion group order and saved individual stance to drop combat,
  recall pets, and regroup without attacking.

### Changed

- Aggressive and defensive companions break pursuit when more than 2100 units
  from their leader and return within 650 units before fighting again.
- Bare `/companions` now gives concise command guidance while the native
  window action and search controls remain unresolved.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- Companions no longer keep pursuing distant fights after their leader leaves.

### Removed

- None.

## [0.30.1] - 2026-09-23

### Added

- A hash-guarded, staged Custom8 client-control probe and offline x86 hook
  checks, with a review brief for the required in-client gate.

### Changed

- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- None.

## [0.30.0] - 2026-09-23

### Added

- None.

### Changed

- New player characters retain the class selected during character creation
  from level 1, with starter equipment assigned for that class when configured.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- The startup class rewrite. A saved `start_as_base_class` property is left in
  the database but no longer changes newly created characters.

## [0.29.0] - 2026-09-23

### Added

- A native external-inventory view for an active companion's backpack. Item
  inspection and drag/drop transfers use the existing protected companion
  ownership and atomic save path.
- Owner-reported Companion Step 2 UI findings and real-client retest tasks in
  the roadmap and acceptance brief.

### Changed

- Split the companion gear menu into short equipment and backpack pages, moved
  slot actions to item detail, and clarified generated versus authored recruits.
- Reopen the NPC conversation for each page and keep older visible choices
  bound to their original actions during the menu session.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- Removed hexadecimal action tokens from visible companion menu links.

### Removed

- None.

## [0.28.0] - 2026-09-23

### Added

- Focused disposable-SQLite Stage 6 integration coverage and an owner-run
  companion acceptance brief.

### Changed

- Recorded the existing GameBot death-recovery policy and temporary-helper-only
  `/raid 40|80` policy in the companion roadmap and player guide.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- Kept remembered defensive pull targets range-gated, so companions wait until
  distant PvE and PvP targets enter the 350-unit defensive radius.

### Removed

- None.

## [0.27.0] - 2026-09-23

### Added

- Two authored companions for each of the 39 Classic + SI classes, with stable
  identities, appearances, backgrounds, personalities, and dialogue.
- Persistent individual role and engagement preferences, authored-cast browsing,
  character profiles, and private menu and command controls.

### Changed

- Personality supplies initial engagement behavior; direct player orders and
  temporary group orders take precedence. Existing recruits retain their saved
  identity and class behavior.
- Updated companion roadmap, decision record, player commands, and version pins.

### Fixed

- Group engagement orders now apply to persistent companions as well as
  temporary helpers, while autonomous bots remain separate.

### Removed

- None.

## [0.26.0] - 2026-09-23

### Added

- Persistent automatic companion training with 33 runtime-validated, versioned
  project-recommended builds; six unsupported classes remain manual-only.
- Personal class-legal PvE/PvP companion gear rewards and a private clickable
  roster, training, equipment, and inventory menu.

### Changed

- Companion inventory now tracks starter, earned, and player-supplied gear,
  manual slot locks, keep flags, safe transfers, and positive-value surplus sales.
- Equipment changes, item ownership, sale removal, and owner coin proceeds use
  atomic database transactions. Updated companion roadmap, contract, research,
  decision records, and command help.

### Fixed

- Companion rewards and automatic upgrades preserve displaced items, respect
  manual locks and protected provenance, and roll back inventory and coin state
  together when persistence fails.
- Personal reward rolls no longer depend on whether the owner's XP bar advances;
  player loot shares and autonomous-bot reward handling retain their own paths.
- Persistent companions now follow accepted owner teleports and refresh their
  displayed group level as soon as they level up.
- Equipment saves use a consistent lock order. Manual equipping accepts legal
  weaker items, and tied accessory slots keep one choice through validation.

### Removed

- None.

## [0.25.0] - 2026-09-23

### Added

- None.

### Changed

- Launcher, command-reference, and launcher-test version pins identify 0.25.0.

### Fixed

- Reward-eligible autonomous-bot PvP deaths now generate the same guaranteed
  class-appropriate gear drop as human-player deaths, retaining existing
  contributor eligibility, repeat-kill protection, and automatic pickup.

### Removed

- None.

## [0.24.2] - 2026-09-23

### Added

- Project-specific fast local R&D shipping rules: direct fork merge/push and
  server/launcher deployment to the owner's local game installation.

### Changed

- Shipping builds both components and pre-authorizes stopping the target
  installation before deployment, retaining backups and save protections.
- Development guidance skips PR, CI, Docker, automated test, and monitoring
  gates during R&D; gameplay verification remains local and owner-driven.
- Launcher, command-reference, and launcher-test version pins identify 0.24.2.

### Fixed

- Generic merge guidance no longer makes unused upstream Docker tooling a
  prerequisite for this project's Windows development loop.

### Removed

- None.

## [0.24.1] - 2026-09-23

### Added

- Class-by-class Stage 3 build research and validation status for all 39
  companion classes, including static career/skill checks, no-autotrain point
  budgets, sourced milestone routes, and explicit blockers.

### Changed

- Companion plan commands now explain that static candidates await disposable
  runtime validation and owner selection. The roadmap and Stage 3 decision
  record distinguish static research from runtime and real-client acceptance.
- Launcher, command-reference, and launcher-test version pins now identify
  0.24.1.

### Fixed

- None.

### Removed

- None.

## [0.24.0] - 2026-09-23

### Added

- Persistent companions earn eligible NPC PvE experience and can spend saved
  specialization points through `/companions train` or reset them with
  `/companions respec`.

### Changed

- Companion damage and controlled-pet damage use a separate reward path that
  preserves player XP shares, group counts, and loot ownership. Companion XP
  progress saves through a coalesced record-only queue.
- The companion roadmap and command reference now describe Stage 3 policies and
  the automatic-plan validation gate. Launcher and command-reference pins now
  identify 0.24.0; the launcher presentation test pin matches.

### Fixed

- Persistent companion NPC rewards no longer flow through PvP XP or realm-point
  paths, and companion pet damage remains attributed to the companion.

### Removed

- None.

## [0.23.2] - 2026-09-23

### Added

- Companion Stage 3 code audit and progression/training decision brief with
  proposed XP, catch-up, respecialization, and realm-point policies.

### Changed

- Companion roadmap records Stage 3 decision prep; owner policy selections
  remain open. Launcher, command-reference, and test version pins now identify
  0.23.2. This documentation change does not alter gameplay.

### Fixed

- None.

### Removed

- None.

## [0.23.1] - 2026-09-23

### Added

- None.

### Changed

- Companion roadmap, decision brief, and feature verification notes now record
  owner-confirmed Stage 2 runtime acceptance and current offline checks.
- Launcher, command-reference, and test version pins now identify 0.23.1.

### Fixed

- None.

### Removed

- None.

## [0.23.0] - 2026-09-23

### Added

- None.

### Changed

- None.

### Fixed

- Persistent companion invites now save newly generated unique gear templates
  with the companion inventory, allowing recruits to join and retain their gear.

### Removed

- None.

## [0.22.0] - 2026-09-23

### Added

- Companion roster invite and bench commands accept companion names, and the
  roster lists names grouped by realm without showing internal IDs.

### Changed

- `/companions recruit <class>` resolves a class from any realm; `/classes`
  lists class names grouped by realm without role descriptions.
- Launcher, command-reference, and test version pins now identify 0.22.0.

### Fixed

- Albion class listings now show “Albion” instead of the `_FirstPlayerRealm`
  enum alias.

### Removed

- None.

## [0.21.0] - 2026-09-23

### Added

- Persistent per-character companion records with stable IDs, saved identity,
  specialization/build state, and namespaced companion inventories.
- `/companions list`, `recruit`, `invite`, and `bench` flows for free level-1
  Classic + SI recruits, with 78 stored roster slots per character.
- Active persistent companions restore on login; failed invitations keep the
  roster entry, and full groups leave companions benched.

### Changed

- Player group departure, explicit removal, and group disband save and bench
  persistent companions. `/spawn` remains temporary, and legacy saved bot
  profiles are left untouched without conversion.
- Launcher, command-reference, and test version pins now identify 0.21.0.

### Fixed

- Companion equipment is saved under its own inventory owner ID, preventing
  roster gear from sharing a player's or autonomous bot's inventory key.

### Removed

- None.

## [0.20.7] - 2026-09-23

### Added

- Stage 2 companion decision brief grounded in current roster, menu, and
  persistence code, with recommendations and unresolved owner choices.

### Changed

- Companion roadmap now records Stage 2 decision preparation; gameplay
  and save behavior remain unchanged. Launcher, command-reference, and
  test version pins now identify 0.20.7.

### Fixed

- None.

### Removed

- None.

## [0.20.6] - 2026-09-23

### Added

- Companion Stage 1 acceptance record with per-character ownership and additive
  save-boundary recommendations, compatibility limits, and menu design notes.
- Level-by-level source coverage, the nonportable Minstrel milestone, two
  classes without numeric templates, and three unvalidated forum build variants.

### Changed

- Companion roadmap Stage 1 is complete to its documented proposal-or-gap
  criteria; runtime database and real-client checks remain separate gates.
- Launcher, command-reference, and test version pins now identify 0.20.6.
  This documentation task does not change gameplay.

### Fixed

- None.

### Removed

- None.

## [0.20.5] - 2026-09-23

### Added

- First-pass sourced leveling and endgame build proposals or explicit research
  gaps for all 39 companion classes, with dated Healer and Necromancer forum
  supplements.
- Static career, skill-table, and point-budget cross-checks for the candidate
  builds, including four no-autotrain companion adjustments.

### Changed

- Companion Stage 1 remains in progress: five exact endgame templates, most
  per-level milestones, local runtime database confirmation, and Stage 2 roster
  decisions remain open.
- Launcher, command-reference, and test version pins now identify 0.20.5.
  This documentation task does not change gameplay.

### Fixed

- Mapped the Theurgist guide's Air line to the server career key Wind Magic.

### Removed

- None.

## [0.20.4] - 2026-09-22

### Added

- Dated companion class-roster reconciliation: 33 base classes plus six
  Shrouded Isles additions, matching the 39-class runtime catalog.

### Changed

- Companion Stage 1 class-era roster checkpoint is complete; per-class build
  proposals and server allocation validation remain open.
- Launcher, command-reference, and test version pins now identify 0.20.4.
  This documentation task does not change gameplay.

### Fixed

- None.

### Removed

- None.

## [0.20.3] - 2026-09-22

### Added

- A first-pass audit of 1.65 class-era sources and companion build-guide
  coverage, including the missing Necromancer entry and source limitations.

### Changed

- Companion Stage 1 remains in progress; dated per-class proposals and server
  allocation validation are still required.
- Launcher, command-reference, and test version pins now identify `0.20.3`.
  This documentation task does not change gameplay.

### Fixed

- Corrected the companion design notes: the three Uthgard realm guides cover
  38 of the 39 runtime classes, not all 39.

### Removed

- None.

## [0.20.2] - 2026-09-22

### Added

- Stage 1 companion design notes with a provisional 39-class catalog and an
  audit of existing persistence, reward, inventory, and client-menu boundaries,
  plus an initial assessment of 1.65-era build-research sources.

### Changed

- Companion roadmap Stage 1 is in progress; sourced class-build research and
  final roster decisions remain pending.
- Launcher, command-reference, and test version pins now identify `0.20.2`.
  This documentation task does not change gameplay.

### Fixed

- None.

### Removed

- None.

## [0.20.1] - 2026-09-22

### Added

- Persistent companion roadmap with agreed goals, six implementation stages,
  decision gates, and separate offline and real-client acceptance checks.

### Changed

- README and current companion documentation link to the proposed roadmap.
- Launcher, command-reference, and test version pins now identify `0.20.1`.
  This documentation task does not change gameplay.

### Fixed

- None.

### Removed

- None.

## [0.20.0] - 2026-09-22

### Added

- Reward-eligible player PvP kills now produce one guaranteed class-appropriate
  gear drop for the credited killer; autonomous bots collect it and equip usable
  upgrades.

### Changed

- Launcher, command-reference, and test version pins now identify `0.20.0`.

### Fixed

- None.

### Removed

- None.

## [0.19.0] - 2026-09-22

### Added

- Higher-level PvP kill bonuses: +25% XP and RP per level above the winner,
  capped at +100%, with the reward caps raised by the same bonus.

### Changed

- Low-level player and persistent-bot RP values now rise with level; fractional
  eligible shared kills retain at least one RP before server rate modifiers.
- Group-PvE bots hunt outdoor camps in their region while queued for guildmates.
- Solo PvP hunters acquire nearby legal rivals and move between hunting grounds.
- Launcher, command-reference, and test version pins now identify `0.19.0`.

### Fixed

- Inland, Live, and realm-specific town teleporters use the shared cross-realm
  menus and destination handling, including Midgard travel to foreign towns.
- Distant group members no longer inflate locally observed PvP party strength.

### Removed

- Stationary group-matchmaking waits and repeated selection of the same
  low-level PvP hunting destination when alternatives exist.

## [0.18.0] - 2026-09-22

### Added

- Cross-region matchmaking for ordinary same-guild PvE parties; nearby members
  remain preferred and remote members use the existing rendezvous travel.

### Changed

- Launcher, command-reference, and test version pins now identify `0.18.0`.

### Fixed

- Group-PvE bots no longer wait for the 20-minute fallback solely because their
  compatible guildmates are in other regions.
- Never-killed characters are no longer treated as recently killed just because
  their played time is shorter than the repeat-kill window.

### Removed

- None.

## [0.17.0] - 2026-09-22

### Added

- Restart-safe generated-guild consolidation with persisted source-to-survivor
  mappings, weighted 1:2:4 guild targets, protected human memberships, and
  keep/alliance reference reconciliation before autonomous login.
- Local low-level PvP hunts, opportunistic legal rival engagement for PvE
  parties, explicit autonomous PvP safety opt-in, and stronger-party avoidance.
- Regression coverage for guild caps and weighting, login cohorts, every
  ordinary party size, matchmaking cadence and navigation budgets, low-level
  PvP policy, Camlann alliances, and companion threat handling.
- A maintained `FEATURES.md` guide covering the fork's playable world,
  autonomous population, party, PvP, companion, launcher, and safety behavior.

### Changed

- Autonomous populations now use at most fifteen managed mixed-realm guilds,
  with deterministic realm, level-band, and class-role balancing; player guilds
  and generated guilds containing humans remain untouched.
- Matchmaking now runs every five seconds, rotates longest-waiting candidates,
  forms ordinary PvE parties with 2–8 compatible guildmates, and reassesses
  roles and content after permanent losses without changing exact raid sizes.
- Level 1–19 activity defaults are now 45% solo PvE, 40% group PvE, and 15%
  PvP; low-level PvP parties prefer pairs and are capped at four members.
- Temporary companions can focus legal human, autonomous-bot, and controlled-
  pet targets and remember hostile attempts that miss or are blocked.
- Launcher, command-reference, and test version pins now identify `0.17.0`.

### Fixed

- Guild consolidation failures now block autonomous login with an actionable
  error instead of allowing a partially reconciled population to enter.
- PvE content selection now uses the live party size and will not target above
  the party average when either healing or frontline capability is absent.
- Autonomous PvP acquisition now revalidates shared Camlann legality, safe
  areas, release immunity, grey restraint, visibility, and party strength.

### Removed

- Random matchmaking leader rejection, the low-level PvP allocation ban, and
  ordinary-PvE assumptions that every party must contain exactly eight bots.

## [0.16.1] - 2026-09-22

### Added

- None.

### Changed

- Repository Git guidance now permits intentional direct synchronization and
  local integration of the fork's `main` branch.
- Launcher, command-reference, and test version pins now identify this patch
  release as `0.16.1`.

### Fixed

- None.

### Removed

- PR-only and no-local-fast-forward restrictions from the repository agent
  instructions.

## [0.16.0] - 2026-09-22

### Added

- Camlann Tier 9 player-facing launcher copy for the single full-PvP world,
  autonomous crew generation, Active Population, guild-owned keeps, and
  guild-only relics.
- Focused command and play guidance for `/safety off`, `/gc form`, cross-realm
  companions, dangerous leveling zones, and safe Camlann hubs.

### Changed

- The launcher now identifies the Old Frontiers Camlann world directly and
  labels realm generation controls as adding bots to crews.
- The progress importer now refuses Camlann destinations and Camlann sources;
  the existing one-time fresh-world reset remains the only supported conversion.
- The roadmap now treats Tier 9 as the last numbered tier without making it an
  automatic `1.0.0` release; versioning stays on the current 0.x line until the
  owner explicitly calls for release 1.0.

### Fixed

- Removed stale player documentation that described realm cards as factions or
  suggested importing Normal progress into the Camlann world.

### Removed

- Normal-save progress import into the Camlann world.

## [0.15.0] - 2026-09-22

### Added

- Camlann Tier 8 population tuning for level-band activity mixes and
  deterministic autonomous crew-size distribution.
- Regression coverage for Camlann roamer reserves, pair/small-crew/eight-man
  formation bands, and the keep/relic objective cooldown.

### Changed

- Autonomous bots now default to 55/45 PvE activity below level 20,
  30/45/25 PvE/RvR activity from levels 20–49, and 15/35/50 at level 50.
- RvR formation planning reserves 25% of mature actors as independent roamers,
  favors gank pairs and full eight-man crews, and waits 30 minutes before
  selecting the same keep or relic objective again.

### Fixed

- Small autonomous populations no longer lose all independent frontier actors
  when the RvR grouping pass forms crews.

### Removed

- None.

## [0.14.0] - 2026-09-20

### Added

- Tier 7 mixed-realm PvE expedition recruitment and PvP-context regression
  coverage for grinding, loot, and Realm Exchange behavior.

### Changed

- Autonomous dragon and epic-dungeon expeditions use their encounter realm
  for world location and presentation only; class-, level-, and crew-eligible
  adventurers may form a shared PvE expedition across realms.
- PvE loot, currency sharing, equipment rules, Realm Exchange access, and
  existing navigation/client patch boundaries remain unchanged.

### Fixed

- Raid recruitment and expedition pet support no longer reject eligible
  cross-realm members or their controlled pets under Camlann PvP rules.
- PvE, economy, and Exchange regression tests now exercise `PvPServerRules`.

### Removed

- None.

## [0.13.1] - 2026-09-20

### Added

- CoreServer builds now use the tracked example server configuration as their
  output config when a clean checkout has no local runtime configuration.

### Changed

- Development documentation records the local server configuration fallback
  and preserves the launcher, command reference, and changelog version pin at
  `0.13.1`.

### Fixed

- A fresh checkout can build the server solution without an ignored
  `serverconfig.xml` copied from another installation.

### Removed

- None.

## [0.13.0] - 2026-09-20

### Added

- Full Camlann PvP consequence coverage for Old Frontiers safety scope,
  player-shaped death immunity, constitution loss, and XP/realm-point kills.

### Changed

- PvP player kills no longer award legacy bounty points or coin, and autonomous
  bots retain the player-kill immunity timer after release or resurrection.
- `/level` is disabled on the shipped Camlann PvP server regardless of mutable
  slash-level settings.

### Fixed

- Autonomous GameBot kills now classify human deaths as PvP for release
  immunity, and sub-10 `/safety` no longer protects actors inside Old Frontiers.

### Removed

- Atlas bounty-point generation from the PvP quest reward compatibility path.

## [0.12.0] - 2026-09-20

### Added

- Camlann guild-owned keep claims with bot-aware ranks, companion-aware claim
  counts, a three-keep guild limit, claim timestamps, and dynamic relic mounts.
- Relic keep persistence through `Relic.KeepID`, guild-only uncapped bonuses,
  claim-delay enforcement, and guild-aware autonomous keep/relic objectives.

### Changed

- Fresh and launcher-reset frontier keeps now use `Realm=0` until claimed;
  unclaimed keeps are hostile to every guild and portal keeps remain safe.
- Keep capture/reset, guard ownership, broadcasts, and relic pickup/mounting
  use guild ownership while realm remains only a cosmetic client display.

### Fixed

- GameBot and bot-owned pet keep kills now reset and display keeps correctly.
- Relics dropped from a keep return to their temple shrine, and the launcher
  clears old mounted-keep and claim-timestamp state without touching characters.

### Removed

- Realm-owned relic bonus gating and the PvP seal-mob lord respawn branch for
  Camlann keeps.

## [0.11.1] - 2026-09-20

### Added

- Dedicated documentation for `/spawn` companion-bot XP, attribution,
  progression, persistence, and future tuning points.

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.11.0] - 2026-09-20

### Added

- `/spawn` companion bots now earn PvE experience from their own combat
  contribution while they are active.

### Changed

- Temporary companions use the normal player XP rate and can level and train
  their temporary in-memory class progression without changing the owner's
  existing XP or loot treatment.

### Fixed

- `/spawn` helpers no longer discard their valid XP contribution while still
  remaining excluded from realm-point rewards and real-party XP divisors.

### Removed

- None.

## [0.10.0] - 2026-09-20

### Added

- Camlann Tier 4 autonomous crews with real `DbGuild` identity, persisted bot
  membership/rank, mixed-realm guild rosters, and player invitations for live
  autonomous bots.
- Crew-based login balancing, mixed-realm group formation, and guild-aware
  roster/rank handling for autonomous actors.

### Changed

- Autonomous frontier behavior now roams and hunts unallied actors; keep and
  relic contesting remains deferred to Tier 5.
- Cross-realm guild membership is enabled for the PvP ruleset while character
  realm identity remains unchanged.

### Fixed

- Existing `offline_world_bots` databases receive additive `GuildId` and
  `GuildRank` columns with safe defaults during setup/migration.

### Removed

- Realm-quota login and realm-local autonomous RvR group formation from the
  active Camlann population path.

## [0.9.0] - 2026-09-20

### Added

- Camlann Tier 3 hostility resolution through the player-shaped combatant
  helper, including same-realm stranger targeting across frontier and dungeon
  autonomous behavior.
- A tunable `camlann_bot_grey_engage_chance` property, with retaliation and
  crew-defense exceptions for grey player-shaped targets.

### Changed

- Companion engagement, stealth ambushes, crowd-control reservations, siege
  legality, shared-dungeon scans, and autonomous frontier target selection now
  use group/guild/battlegroup alliance instead of realm as the combat boundary.
- Autonomous target tests and bind recovery no longer treat foreign realm
  identity as an enemy-combat rule.

### Fixed

- Same-realm ungrouped bots are no longer filtered out of Camlann autonomous
  combat, while allied mixed-realm crews remain protected from friendly fire.
- Grey-target restraint now applies through the shared bot aggro gate instead
  of only the specialized frontier and dungeon scans.

### Removed

- Realm-inequality hostility filters from the Tier 3 autonomous combat paths.
- The obsolete relocation of autonomous bots saved at a foreign-realm bindstone.

## [0.8.0] - 2026-09-19

### Added

- Camlann Tier 2 cross-realm `/spawn` and `/classes` choices, including
  realm-correct companion identities and equipment.
- All-realm capital and Classic/SI leveling-town teleporter menus, with
  battleground destinations excluded.
- Offline tests for foreign-capital Realm Exchange listings and all-realm
  teleporter coverage.

### Changed

- Mixed-realm companions, group pulls, healing/buffs, autonomous town routes,
  stable travel, frontier portal travel, and service-NPC routing no longer use
  realm as an access boundary; PvP alliance rules still govern hostility and
  support.
- Realm Exchange access is available at any capital broker while each
  character's market remains partitioned by their own realm, preserving real
  items, coin, and proceeds.
- Bot name lookup is realm-agnostic, and autonomous chat knowledge covers
  open Classic/SI travel services.

### Fixed

- Battleground travel is closed by default across teleporters, medallions, and
  autonomous movement.
- Foreign-realm portal-keep teleporters can board autonomous bots carrying the
  correct ticket for their own identity.
- Server-owned dummy guild creation is restart-safe, so an existing save with
  duplicate legacy dummy-guild rows no longer prevents startup.

### Removed

- Same-realm restrictions from player-led companion grouping and temporary
  companion class selection.

## [0.7.0] - 2026-09-19

### Fixed

- Realm-point value of victims below level 20 is floored at `1 + RealmLevel`.
  The pre-1.81 `(level - 20)^2` formula grew again below 20, so a level-1
  kill paid 361 RP (more than a level-35 victim), for both player and bot
  victims.

### Changed

- `docs/CAMLANN.md` records the passed Tier 1 real-client spike and marks
  Tier 1 complete; `AGENTS.md` no longer calls the conversion unimplemented.

## [0.6.0] - 2026-09-19

### Added

- Tier 1 Camlann player-shaped combatant resolution, bot PvP immunity, and
  focused hostility/safe-zone coverage.

### Changed

- PvP attack, heal, alliance, safety, and client presentation decisions now
  treat humans, GameBots, and their controlled pets as player-shaped actors.
- Grouped GameBots and their pets receive the friendly client guild-ID update;
  temporary companions retain protection for the group they joined.
- Player-shaped PvP kills now use non-allied participation for XP/RP, including
  autonomous bot killers and bot-victim constitution-loss bookkeeping; `/assist`
  and `/who` use the same PvP alliance decision.

### Fixed

- Same-realm strangers and hostile GameBots are no longer treated as friendly
  NPCs or granted the dummy-guild packet hack, and bot-owned pets resolve to
  their living bot owner.

### Removed

- The obsolete region-163-only safety exception from PvP hostility decisions.

## [0.5.0] - 2026-09-19

### Added

- Tier 0 Camlann bootstrap: a typed `WorldModel` marker, launcher-owned
  one-time world reset with a timestamped database backup, and synthetic reset
  fixtures covering rollback, idempotency, and server-running refusal.

### Changed

- New server configurations default to PvP, and startup now fails closed unless
  the database carries the completed `Camlann-1` world marker.

### Fixed

- Keep and relic state is reset to neutral/homed values as part of the new-world
  transaction without changing world definitions, item templates, or meshes.
- Reset cleanup includes backup-character inventory rows and safely handles
  empty stopped-state SQLite sidecars while still rejecting an uncheckpointed WAL.

### Removed

- The launcher no longer exposes the legacy realm-owned keep/relic reset panel
  while the Camlann guild-claim implementation is still pending.

## [0.4.5] - 2026-09-19

### Changed

- `docs/completed/DEV-SETUP.md`: Tier 5 (first real deploy, in-game smoke test,
  restore, redeploy) and Tier 6 (hand-off to Camlann) recorded as done. The
  pre-Camlann save backup is deferred to the start of Camlann Tier 0.
- `docs/DEVELOPMENT.md`: the WSL2 + Windows loop is marked verified end to
  end against the real install.

## [0.4.4] - 2026-09-19

### Added

- `tools/dev/winnet.sh`: WSL wrapper for the complete-install bundled Windows
  SDK (CLI home, NuGet packages and HTTP cache under `OfflineDAoC-dev\state`,
  passed to `dotnet.exe` through `WSLENV`).
- `tools/dev/Deploy-OfflineDAoC.ps1`, `Restore-OfflineDAoC.ps1`,
  `OfflineDAoC.Deploy.psm1`, `deploy.sh`, and `Test-DeployOfflineDAoC.ps1`:
  parameterized dry-run/apply deploy and restore with process checks, path
  guards, protected-save hashing, verified rollback on any failure,
  resumable restore, an owner-approval `-IncludeThirdParty` switch,
  warnings for build-only DLLs and missing PDBs, and a fake-tree self-check
  that only deletes its own marked scratch folder.
- Root `.gitattributes` (`* -text`) so Git does not rewrite the mixed
  LF/CRLF tree.

### Changed

- `docs/completed/DEV-SETUP.md`: Tier 1–4 gates recorded; Tier 2 baseline counts added.
- `docs/DEVELOPMENT.md`: short WSL2 + Windows loop commands pointing at the
  new wrappers.
- `AGENTS.md`: preserve each file's existing line endings.

## [0.4.3] - 2026-09-19

### Added

- `docs/completed/DEV-SETUP.md`: tiered WSL2 + Windows development setup plan. It covers
  the install location, toolchains, baseline tests, line-ending guard,
  parameterized deploy/restore scripts, first client smoke test, and hand-off
  to the Camlann work.
- `AGENTS.md` rule: all GitHub work targets the fork
  `stefanrows/OfflineDAoC` only (never the upstream `shadowofze` repo), and
  every change lands through a pull request.

## [0.4.2] - 2026-09-19

### Changed

- Revised `docs/CAMLANN.md`. It now targets Camlann 1.65 on the existing Old
  Frontiers world (pre-ToA) and records owner decisions: player-founded
  guilds, uncapped guild relics, a one-time launcher world reset,
  any-realm companions, restrained grey-target ganking, safe portal-keep hubs,
  and XP + RP for player kills.
- The plan now covers gaps found in the code audit: the world is Old
  Frontiers, not New Frontiers, so safety checks are zone-level;
  `PvPServerRules` lacks fork guards; the client packet hack marks bots as
  friendly; bot guilds are not persisted; keep claims and relics are realm-
  and `GamePlayer`-only; and the Atlas PvP branches need an audit. Not
  implemented yet.

## [0.4.1] - 2026-09-19

### Added

- Tiered Camlann conversion plan in `docs/CAMLANN.md` (full-PvP only, fresh
  save, no dual Normal/PvP mode). Not implemented yet.

## [0.4.0] - 2026-09-19

### Fixed

- The launcher no longer overwrites Windowed mode on every Enter Realm launch.
  A new AppData profile still defaults to borderless fullscreen; an existing
  `user.dat` display choice and resolution are left alone.

## [0.3.1] - 2026-09-19

### Added

- Fork changelog and MAJOR.MINOR.PATCH versioning rules in `AGENTS.md`.
- Launcher pin set to `0.3.1` so this fork is distinct from upstream v0.3
  and from the original author's private 0.4 label.

## [0.3.0] - 2026-09-15

Upstream Offline DAoC v0.3 public baseline. No reconstructed author history.
The playable runtime, world data, and navigation meshes still come from that
GitHub release.
