# Changelog

All notable changes to this fork are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this fork uses [MAJOR.MINOR.PATCH](https://semver.org/). See `AGENTS.md`
for when to bump each number.

The launcher pin `DisplayVersion` must match the latest dated heading below.
The upstream playable download remains GitHub **v0.3**; that is the runtime
package, not this fork's version.

## [Unreleased]

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

- `docs/DEV-SETUP.md`: Tier 5 (first real deploy, in-game smoke test,
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

- `docs/DEV-SETUP.md`: Tier 1–4 gates recorded; Tier 2 baseline counts added.
- `docs/DEVELOPMENT.md`: short WSL2 + Windows loop commands pointing at the
  new wrappers.
- `AGENTS.md`: preserve each file's existing line endings.

## [0.4.3] - 2026-09-19

### Added

- `docs/DEV-SETUP.md`: tiered WSL2 + Windows development setup plan. It covers
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
