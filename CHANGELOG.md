# Changelog

All notable changes to this fork are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this fork uses [MAJOR.MINOR.PATCH](https://semver.org/). See `AGENTS.md`
for when to bump each number.

The launcher pin `DisplayVersion` must match the latest dated heading below.
The upstream playable download remains GitHub **v0.3**; that is the runtime
package, not this fork's version.

## [Unreleased]

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
