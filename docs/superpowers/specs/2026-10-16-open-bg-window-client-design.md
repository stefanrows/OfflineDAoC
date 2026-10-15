# OPEN BG window: client and server design (Custom5)

Date 2026-10-16. Supersedes the open points of `2026-10-15-open-bg-window-design.md` (approved by the owner: own window, pages Lage and Steuern). Analysis by a read-only advisor (opus) against main 0.279.0 using Stefan's RE docs (`COMPANION_PAPERDOLL_RE.md`, `CLIENT_UI_XML_REFERENCE.md`, `COMPANION_MANAGER_INTEGRATION.md`), the builder `source/server/tools/build_companion_manager_client.py`, the installer and the release pack. Decisions below adopt the advisor's recommended defaults because the owner asked for urgency; the owner decided the content on 2026-10-15 (see section 2).

Plan: `docs/superpowers/plans/2026-10-16-open-bg-window.md`.

> **Owner amendment 2026-10-16:** no GM lock. Every player may change the settings and relieve units, in the window and by chat, as `/bgsetup` already allows today. All GM-only statements in sections 2, 6 and 9 are void.

## 1. Facts that changed the picture

- **The owner's installation already has the 0.276.0 manager stage** (`game.dll` `bfefe97d...`, `uimain.xml` `cef698f3...`, custom6/7/8 files of 15.10. 17:36; backup `playable-backups/companion-manager-upgrade-20261015-173803`). The installer must accept 0.169.0 **and** 0.276.0 as starting state (Stefan's D: install not inspected).
- **There is no GM lock today.** `OpenBgSetupCommand.cs:20` is `ePrivLevel.Player`; write (`:79`) and relieve (`:67`) check no rights; `OpenBgManager.cs:9` says every player may use it. The GM lock for steering must be built (decision below).
- **Archetype data is missing**: `UnitInfo` stores no archetype (`OpenBattlegroundRoster.cs:590-599`), `OpenBgBotStatus` has no archetype field. **Field figures exist only in the log** (`OpenBattlegroundDirector.cs:559`).
- Custom5 is free: bytes `6a 01 6a 72` at `0x4E0B3E` (ShowCustom5 -> `0x72`), no stock XML uses it, events `ToggleCustom5/ShowCustom5/HideCustom5` exist.

## 2. Window content (owner decided)

**Lage (read-only, everyone):** phase and time left, mode (Server PvP / Per realm) with landing peace, raid (units/bots, lead guild), keep 11 owner, world speed (set/achieved), field figure (units/bots out, %), players inside, crew counts (inside, travelling, leaving, cap, band); then crew rows by unit (name, level, class, unit/archetype, role or state), guest squads, players inside. In Server PvP the unit summary is per archetype, in Per realm per realm.

**Steuern (GM only; non-GMs read the values without buttons):** open BG on/off; mode switch (serverpvp | perrealm); landing peace (auto | all | realm); crew level band min/max (+/-); bot cap (+/-, step 4); crew targets 8-man/small/solos (+/-) and, in Per realm mode, `open_bg_units_<realm>` per realm; preset counts per archetype (std8, caster8, raid8, stealth4, small, duo) with +/-; drift, cycle min/max (end of the page, step 5 for cycle); relieve a unit by hand (`[Relieve]`); last reply line. No search. Own title "OPEN BG"; may be open next to the Companion Manager (Custom8), COMPANIONS (Custom7) and COMPANION GEAR (Custom6).

## 3. Architecture

- Closest pattern: the Companion Manager's grid (Custom8: overlaid colour labels, row grid with server paging, token, signature). Pages Lage and Steuern are the same rows filled differently (XML cannot hide controls). From the gear window (Custom6): own show/hide/ready operations, own OnClickEvent range above `0x900`, the 3 s text fallback with a `ClientMissing` flag (`CompanionGearWindow.cs:156-171`). From the COMPANIONS bar (Custom7): an own active flag (`bar_active`, builder:512-513) and a fixed window size.
- **Own sender and own token buffer** (decision): command line `&bgsetup ui <tttt> <cc>` set by a new token operation. The shared `&companions ui` sender is not extended: `sendControl` masks to one byte (builder:785), `CONTROL_LIMIT 0xE0` (builder:109), `0x700 + c` must stay below `0x7E0`, the server checks `value < ControlLimit` (`CompanionManagerProtocol.cs:273`), and the token buffer is shared (every manager redraw would stale BG clicks and vice versa). Separate buffer = structural isolation of the two windows.
- The server runtime moves to a new `OpenBgWindow` on `CompanionWindowTransport`; the Custom8 variant of the open BG manager (`CompanionWindowOwner`, `OpenBgManager`, the OpenBg branches in `PlayerCompanionCommands.cs`) is removed when the window ships.

## 4. Label budget and geometry

New labels from `BG_LABEL_BASE = 620` (= `GEAR_LABEL_END`, builder:331-333), total `985`:

| Group | Count | Indexes |
| --- | --- | --- |
| status right of the fixed title "OPEN BG" | 1 | 620 |
| tabs Lage (inactive/active), Steuern (inactive/active) | 4 | 621-624 |
| page indicator ("1-22 of 61") | 1 | 625 |
| header lines (Lage: phase/raid/keep/speed/field/crew; Steuern: mode summary and GM note) | 5 | 626-630 |
| reply lines | 2 | 631-632 |
| 22 rows x 16 | 352 | 633-984 |

Row layout (stride 16): 0 section title (gold, full width); 1 plain text (full width); 2-5 name in Albion, Midgard, Hibernia colour and neutral; 6 level; 7 class; 8 unit/archetype; 9-10 role/state (green or grey); 11 value/link (gold, e.g. `[Relieve]`, `[global]`); 12-13 `[-]` (active/grey); 14-15 `[+]` (active/grey). Column headings are fixed XML text (no adapter). Emergency cut: without the grey +/- variants the stride is 14 (total 321). The client has no fixed adapter limit (registrar `0x4B6C84` allocates a heap object per adapter into a map); 985 adapters are only statically reasoned, not run in the real client.

Window: fixed 760 x 648 on `dlg_sm_title_noresize` (like Custom6/7), font arial14 with the existing advance table (builder:163-172 = C# `:217-231`). Title y=1; tabs, paging, `[Refresh]`/`[Close]` y=24; header lines y=46..118 (18 apart); separator y=140; column heads y=144; rows from y=164 every 20 px, 22 rows to y=602; reply lines y=606 and y=624. Columns (x, width): name (10,140), Lv (154,26), class (186,104), unit (296,100), state (402,150), value/link (558,110), `[-]` (672,36), `[+]` (712,36); wide row labels 740 px. Beside the Manager (980 x 700) the window needs about 1750 px of screen width.

## 5. Controls, events, operations

- `OnClickEvent` = `0xA00 + cc` (decimal 2560-2815; free: stock < `0x246`, raid `0x600-0x64F`, manager `0x700-0x7DF`, helper `0x7E0/0x7E1`, lever probe `0x800-0x8DF`, gear `0x900-0x93A`). The raid handler (`0x2485000`) handles only `0x600-0x64F` and passes the rest to the stock dispatcher; the emulation test (test:408) expects `0xA00` to pass through to the raid and must be adapted.
- Controls: `0x00-0x15` row link; `0x20-0x35` row `[-]`; `0x40-0x55` row `[+]`; `0xE0`/`0xE1` tabs Lage/Steuern; `0xE8-0xEB` up, down, page up, page down; `0xFD` close; `0xFE` refresh; `0xFF` ready (sent by the show op, not a click). 75 of 256 values.
- Operations after 9: `10` BgShow (`bg_active=1`, `vtbl[2](0x72,1)`, sends ready `ff`), `11` BgHide (`bg_active=0`, `vtbl[2](0x72,0)`), `12` BgToken (four hex characters into the BG buffer, same check as op 4, builder:709-726). Labels keep using op 1 (only `TOTAL` changes). A 0.276.0 client drops ops 10-12 (`jmp done`) and label indexes >= 620 (builder:631-633); the server falls back after 3 s.
- Gate: `bg_active` at `active+8` (free byte, builder:512): clicks on the closed window send nothing. Stale-click protection: token and revision after `OpenBgManagerSession.CommitView/IsStale` (:94-104); row controls with an old token are rejected, navigation is always safe. Side effect: `0x42BC08` drops an identical command within 250 ms, so a fast double click on `[+]` counts once.

## 6. Server side

New `OpenBgWindow` (runtime), `OpenBgWindowSession` and `OpenBgWindowScreen` (pure rendering, testable). Reused: `OpenBattlegroundStatus.Capture`, `OpenBgSetupValues.Current`, `OpenBgSetupStore.TryWrite/Relieve`, `OpenBgSetupParser.Parse` for limits and steps (as `AddSettingDetail` :514-526), `Change()` (:556-574), `UnitsWrite`, `OrderedUnits`, `StateText`, `UnitLabel`, `LeaderOf`, `RealmHeader`, the 15 s refresh (`StartRefresh/KeepsRefreshing`, OpenBgManager.cs:243-272). Dropped: detail panel, action buttons, realm filters, search, row-count setting.

Pages: each page builds a full row list with its own offset; shown rows `Skip(offset).Take(22)`; every row carries up to three intents (link, -, +); the signature is the visible row keys plus the intent keys. Lage about 85 rows at 72 bots (4 screens); Steuern about 30 rows plus one per unit.

**GM lock:** steering intents exist only for `client.Account.PrivLevel >= (uint)ePrivLevel.GM` (as `Online.cs:55`); `Apply` re-checks whatever the client sends; non-GMs see Steuern read-only. Decision (owner default): the same lock applies to `/bgsetup <key> <value>` and `relieve` by chat (reverses the 11.10. "every player may use it" for writing; viewing stays open to everyone). Open question for the owner: is his account GM (launcher option GM, default off)?

**Transport:** `CompanionWindowTransport.Publish` (:97-108) gets `labelBase`, `tokenOp`, `showOp` parameters (today fixed to Custom8: ops 4 and 2); existing callers keep the defaults. New `TryParseBgControl` accepts two hex characters `00-ff` without `ControlLimit`.

**Routing of `/bgsetup`:** empty -> `OpenBgWindow.Open` (labels, token op 12, show op 10; no ready after 3 s -> `ClientMissing`, page 1 of `/bgsetup text`, one line: "This client has no OPEN BG window; update the client files (UPDATE OFFLINE DAOC.cmd / Install-CompanionManager.ps1)"; later calls go to the text page at once); `ui <t> <c>` -> `HandleClientControl` (checked before `Parse`, "ui" is no key); `text` unchanged.

**New data:** `UnitInfo.Archetype` set at Register and carried as optional `Archetype` in `OpenBgBotStatus`; `OpenBgMetrics.LastField` set (volatile) by the director plus optional field figures in the snapshot; the launcher's JSON file is extended additively (the launcher parser must tolerate it: verify).

## 7. Builder, tests, installer, release

Builder `build_companion_manager_client.py`: constants (`BG_WINDOW=0x72`, `OP_BG_SHOW/HIDE/TOKEN=10/11/12`, `BG_EVENT_BASE=0xA00`, `BG_READY_CONTROL=0xFF`, `BG_COMMAND_PREFIX="&bgsetup ui "`, label constants, `TOTAL_LABEL_COUNT=985`, entries in `layout_constants()`); in `build()`: data area `bg_active` and a 32-byte `bg_command` buffer `"&bgsetup ui 0000 00"`, init sets `bg_active=0`, the packet block gets ops 10-12 (466 B used of `0x800`), the event handler checks `0xA00-0xAFF` before the gear range and jumps to `bgEvent` at `.cmgr+0x3000` (free `0x3000-0x4000`), `bgSend` = copy of `sendControl` on the BG buffer, extra hash guard `6a016a72` at `0x4E0B3E`, manifest section `openBg`; `open_bg_window()` writes `custom5_window` (WindowId `Custom5`, stock templates only, click areas last for the raid's z-order, both skins identical bytes); `main()` refuses an existing custom5 in uimain or a skin, appends the include after custom6, adds manifest entries `bgWindowSha256` and `uimainGearSha256` (= `cef698f3...`). custom8 (`9cd3b8a5...`), custom7 (`5b820574...`), custom6 (`dfc5bbdb...`) must not change byte for byte.

Tests `test_companion_manager_client.py`: init registers 985 text + 59 icon + 240 raid adapters; op 1 on index 984 works, 985 ignored; op 10 gives `(0x72,1)`, flag 1 and `&bgsetup ui 0000 ff`; op 12 valid and invalid token; op 11; all 75 controls with and without the flag; `0x9FF` and `0xB00` pass to the raid; manager, bar and gear clicks unchanged; the stock mapper `0x4EA06F` on all BG values; XML: every BG label exactly once, no overlapping click areas, everything inside 760 x 648; uimain order 8<7<6<5; C# constants compared as test:641-645.

Installer `Install-CompanionManager.ps1`: `$KnownManagerBuilds` (:41-47) gains `bfefe97d...` (0.276.0); known uimain (:213) gains `cef698f3...`; custom5 paths; a foreign custom5 is refused; backup/restore fields `PreviousBg*Sha256`, `BgWindowSha256`; `RestoreAddedWindow` removes custom5; the "already installed" check includes custom5. Release pack `build_fork_release.py`: `COMPANION_STAGE` (:18), `known_games` (:119-125) gains `bfefe97d...`, uimain list (:128-129) gains `cef698f3...`, custom5 `['absent']`, stage check on `bgWindowSha256`; `Apply-OfflineDAoCFork.ps1` unchanged (generic check :108-113).

## 8. Risks

- The new binary patch region is only emulated: expect one owner round, as with 0.273.0. `vtbl[2](0x72)` has never run for Custom5 in the real client. 985 adapters only statically covered. arial14 measured, not seen.
- A full refresh sends about 365 packets of 128 B (about 47 KB), then only differences.
- Unused click areas stay active; the server ignores them.
- Click collisions are structurally excluded (own range, own sender, own token).

## 9. Owner defaults adopted (change on request)

1. GM lock for steering also by chat (viewing open to all). 2. Window 760 x 648 with 22 rows. 3. Non-GMs read Steuern without buttons. 4. Steps: bot cap 4, cycle 5, rest 1. 5. drift/cycle rows at the end of Steuern. 6. No search.
