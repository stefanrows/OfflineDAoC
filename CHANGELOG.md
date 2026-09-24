# Changelog

All notable changes to this fork are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this fork uses [MAJOR.MINOR.PATCH](https://semver.org/). See `AGENTS.md`
for when to bump each number.

The launcher pin `DisplayVersion` must match the latest dated heading below.
The upstream playable download remains GitHub **v0.3**; that is the runtime
package, not this fork's version.

## [Unreleased]

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
