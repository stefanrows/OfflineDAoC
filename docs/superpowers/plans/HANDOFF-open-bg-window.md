# Hand-off: OPEN BG window (Custom5), state 2026-10-16

Written because the session limit is nearly used up. Everything below is committed on branch `obg-window` (worktree `C:\OfflineDAoC-wt-obgwindow`) and pushed to the fork as branch `obg-window`.

## Done and shipped (main, 0.279.0, deployed on the owner's PC)
Open BG modes (Server PvP / Per realm), landing peace by mode, presets (archetype mix), claim lock, OPEN_BG_FIELD, own-porter boarding, team-fight break-off, configurable level band, and the open BG running together with the Gothwaite PvP mode (reserved bot set). See `CHANGELOG.md` 0.279.0 and `docs/OPEN_BG_MODES_RULINGS.md` (rulings, live-run results, open items).

## In progress: the own client window "OPEN BG" (owner-decided)
- Spec: `docs/superpowers/specs/2026-10-16-open-bg-window-client-design.md` (label indexes, geometry, control map, ops, server design). Plan: `docs/superpowers/plans/2026-10-16-open-bg-window.md` (8 tasks). **Owner amendment: NO GM lock** (every player may change settings and relieve units, window and chat).
- Task 1 (protocol constants, builder + C#): DONE, commit `add45d6`.
- Task 2 (native client additions: ops 10-12, click range 0xA00, own sender): DONE, commit `c804a5a` (emulation suite green, two builds identical, `game.dll` sha256 `7d1f52af90de1dc5a6eb608fd6fd8a69273bec91bb8dcd24c6b7701973b96a48`; manager/bar/gear window hashes unchanged; emulation only, real client untested). Not independently reviewed yet: review it (opus) before Task 3 builds on it.
- Tasks 3-8 not started: XML `custom5_window.xml` + uimain + manifest; snapshot data (archetype per unit, field figures); pure page builder and session; runtime + `/bgsetup` routing + removal of the Custom8 variant; installer + release pack; final stage build, docs, version 0.280.0.
- Resume with the superpowers `subagent-driven-development` skill on the plan file (ledger: `.superpowers/sdd/2026-10-16-open-bg-window/progress.md`, may be gone: recover from `git log`). Task briefs: `scripts/task-brief PLAN N`.

## Local test state on the owner's PC (leave as is unless told)
Server stopped. Gothwaite mode ON (`playable/runtime/server/gothwaite-pvp.json` enabled=true). `open_bg_presets` empty. The installed client has the 0.276.0 Companion Manager stage; the new OPEN BG window needs a new stage installed by the owner (installer upgrade path in Task 7).

## Other open items
Stefan's server needs `reserve=` checked in the log (`GOTH_PVP_OPENBG_RESERVE`, 0 = no open BG; fix with `/bgsetup band_min|band_max` + restart); preset quality (ARCHETYPE_MISS); Minstrel stealth flag and an open-BG-only bomb-group doctrine; vulture rule / second raid (owner OK pending).

## Build/test cheatsheet
`cd /mnt/c/OfflineDAoC-wt-obgwindow; export OFFLINE_DAOC_ROOT=/mnt/c/OfflineDAoC/playable`; C#: `tools/dev/winnet.sh build|test source/server/Tests/Tests.csproj -c Release --no-restore [--no-build --filter ...]`; Python builder needs a venv with `pefile capstone keystone-engine unicorn`; stage input = raid baseline copy in `playable-backups/companion-manager-client-20260926-182418/` (game.dll sha256 `67dcf68a...`); server control: `playable-dev/server.sh status|stop|start|update`; speed: write `server/world-speed.request.json` with the new SessionId.
