# OPEN BG window redesign "Kriegstafel" Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Redesign the OPEN BG window (Custom5) as concept A "Kriegstafel": crests and banner, one-page folded crew view with units as blocks (leader, members individually), a clear Setup page, English copy; new client stage 0.283.0 (no game.dll change).

**Spec:** `docs/superpowers/specs/2026-10-16-open-bg-window-redesign-analysis.md` (concept A wireframes, row slots, budget 355 of 365, copy, plan) and `docs/open-bg-redesign/` (mock renders and tools). Base design: `docs/superpowers/specs/2026-10-16-open-bg-window-client-design.md`. Hand-off: `docs/superpowers/plans/HANDOFF-open-bg-window-design.md`.

**Owner defaults adopted (he was out of session limit; change on request):** concept A; English, tabs "Overview" / "Setup"; units folded on open; Relieve at the opened unit header with a second click within 10 s; real HP bars later; skin: check Atlantis first, verify Isles textures.

## Global constraints
Same as the window plan `2026-10-16-open-bg-window.md` (raid baseline only, no GM lock, deterministic builds, CRLF `ALL SERVER COMMANDS.txt`, no push of unreviewed native changes; this plan needs NO game.dll change: the game.dll hash `7d1f52af...` must stay identical; only `custom5_window.xml`, `uimain.xml` stays, manifest hashes change). Version 0.283.0 (MINOR) with the four pins. Label budget 365 (355 used).

## Tasks
1. **Server data:** leader via `LivingLeader` (`OpenBattlegroundStatus.cs:161`; a solo is its own leader); tests; launcher JSON unaffected.
2. **Builder XML:** new `open_bg_window()` / `open_bg_row_slots()` (18 rows x 18 slots from the analysis, size 760 x 640, own templates on stock textures, crests, `title` banner, zebra, tab plates, hover buttons only for tabs/arrows first); `BgRowCount`/`BgRowStride` 22/16 -> 18/18 in builder and `CompanionManagerProtocol.cs:202-203`; loosen the 2-template test (`test_companion_manager_client.py:719`); game.dll unchanged; both skins identical; double build.
3. **Server screen:** `OpenBgWindowScreen` / `OpenBgWindowSession`: realm rows, unit blocks with fold set (controls `0x60+r`, `0xEC`/`0xED` open/fold all, crest jump `0xF0-0xF2`), member rows, leader `*`, states, strength pips, banner and phase lines, Setup sections with switches/steppers/bars, selection + hint line, Relieve with two clicks, English copy and empty states from the analysis section 6; tests.
4. **Preview tool:** move `docs/open-bg-redesign/render.py` into `source/server/tools/render_open_bg_mock.py` driven by the real XML and a sample snapshot, PNG output (Pillow); test that it runs.
5. **Stage 0.283.0:** installer accepts the 0.282.0 `custom5`/manifest as starting state (game.dll unchanged), release pack, docs (`COMPANION_MANAGER_INTEGRATION.md` section, checklist: crests render, banner, fold/open, Setup, Isles), CHANGELOG, TASKS pending, version pins, build stage into `playable-dev/stage-companion-manager-0.283.0`.
