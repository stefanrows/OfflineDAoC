# OPEN BG window (Custom5) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the owner-decided own client window "OPEN BG" on Custom5 (pages Lage and Steuern; modes, landing peace, band, presets, crew targets, relief) with an own click sender and token, remove the Custom8 variant, and ship it through the client stage, installer and release pack.

**Architecture:** One new Companion Manager client stage: new `game.dll` operations (BgShow/BgHide/BgToken), a new `OnClickEvent` range `0xA00-0xAFF` with its own sender `&bgsetup ui <tttt> <cc>` and token buffer, 365 new label adapters from index 620, a fixed 760 x 648 XML window `custom5_window.xml`; the server renders both pages through `CompanionWindowTransport` with a pure screen builder; GM-only steering; chat fallback for clients without the stage.

**Tech Stack:** Python 3 (builder `source/server/tools/build_companion_manager_client.py` with pefile, capstone, keystone, unicorn), C# (.NET 10, `source/server/GameServer`), NUnit, PowerShell installer, built from WSL (`tools/dev/winnet.sh`).

**Spec:** `docs/superpowers/specs/2026-10-16-open-bg-window-client-design.md` (read it fully: it holds label indexes, geometry, control and op numbers, the server design and the owner defaults). Earlier approved spec: `docs/superpowers/specs/2026-10-15-open-bg-window-design.md`. Stefan's RE docs: `docs/COMPANION_PAPERDOLL_RE.md`, `docs/CLIENT_UI_XML_REFERENCE.md`, `docs/COMPANION_MANAGER_INTEGRATION.md`.

## Global Constraints

- The client stage is built ONLY from the verified raid baseline (`game.dll` `67dcf68a37b95a93946a943b99d5e19b4a03e08cd6469275e25c7b909de21e99`; a copy with its `ui/uimain.xml` `637bf806...` is in `playable-backups/companion-manager-client-20260926-182418/`). Never patch an unverified client. Keep every hash guard; the builder and its output must be deterministic (build twice, identical hashes).
- Window files custom6, custom7, custom8 (hashes `dfc5bbdb...`, `5b820574...`, `9cd3b8a5...`) and their control/event ranges (manager `0x700-0x7DF`, gear `0x900-0x93A`, raid `0x600-0x64F`) must not change byte for byte or number.
- Python: run the builder and its tests with a scratch venv that has `pefile capstone keystone-engine unicorn` (create one under the scratchpad / `/tmp`; the repo has no requirement file): `python3 -m venv <dir>; <dir>/bin/pip install pefile capstone keystone-engine unicorn`.
- Preserve each file's line endings; check `git diff --stat` for whole-file churn. New files LF. `ALL SERVER COMMANDS.txt` is CRLF (binary-safe edits).
- Version: one MINOR bump when finished (0.279.0 -> 0.280.0): CHANGELOG heading, `source/tools/OfflineDaoc.Launcher/MainForm.cs` (`DisplayVersion`), `source/tools/OfflineDaoc.Launcher.Tests/LauncherPresentationTests.cs`, `ALL SERVER COMMANDS.txt` header.
- Offline emulation only: say so in docs and report that the real-client check is the owner's. The owner's install already has the 0.276.0 stage (`game.dll` `bfefe97d...`), the installer must accept 0.169.0 and 0.276.0 as starting state.
- Build only into default output trees; never into the install. Run tests with filters named per task.
- Commands from `C:\OfflineDAoC-wt-obgwindow`, `export OFFLINE_DAOC_ROOT=/mnt/c/OfflineDAoC/playable`. C# build: `tools/dev/winnet.sh build source/server/Tests/Tests.csproj -c Release --no-restore 2>&1 | grep -E ' error |Fehler$'`; C# test: `tools/dev/winnet.sh test source/server/Tests/Tests.csproj -c Release --no-build --no-restore --filter "<filter>" 2>&1 | grep -E 'Fehler|Bestanden'`.

## Owner amendment 2026-10-16 (binding, supersedes every GM mention in this plan and in the spec)

**There is NO GM lock.** Every player may change the open BG settings and relieve units, in the window (Steuern page: all `[-]`/`[+]`/switch/`[Relieve]` intents exist for every viewer) and by chat (`/bgsetup <key> <value>`, `relieve`), exactly as `/bgsetup` already allows today (`ePrivLevel.Player`). Where a task below says GM, GM-only, `isGm`, `PrivLevel`, "GM message" or "non-GM": ignore it. Do not add any privilege check; add a regression test that a plain player can still write a setting and relieve a unit. The server still validates every value against the parser limits and ignores unknown controls. The Lage/Steuern split stays as pages only.

## Review Focus

- A click arriving for the closed window, with a stale token, or an unknown control must do nothing harmful; navigation clicks are always safe.
- A GM-only action must be refused on the server for a non-GM whatever the client sends, also through `/bgsetup <key> <value>` and `relieve` by chat.
- A client without the new stage (0.276.0 or older) must still get a usable `/bgsetup` (text pages) and one clear hint line; the manager, COMPANIONS and gear windows must keep working with the new window open or closed.
- Label count overflow (index >= 985) and a row with long text must never write outside the window or crash the packet builder (text is clipped by the existing width table).
- `/bgsetup` must keep working for every existing key and the `EveryPropertyRowHasAKey` rule.
- The deterministic build: two builds produce identical bytes; installer dry run against copies of the 0.169.0 and 0.276.0 states.

## File Structure

| File | Responsibility |
| --- | --- |
| `source/server/tools/build_companion_manager_client.py` | constants, native additions (`build()`), `open_bg_window()` XML, `main()` wiring, manifest |
| `source/server/tools/test_companion_manager_client.py` | emulation and XML tests for the new window |
| `source/server/GameServer/bots/CompanionManagerProtocol.cs` | C# mirror of the new constants, `TryParseBgControl` |
| `.../battlegrounds/open/OpenBattlegroundRoster.cs`, `OpenBattlegroundStatus.cs`, `OpenBattlegroundDirector.cs` | archetype per unit in the snapshot, field figures |
| `.../commands/playercommands/OpenBgWindowScreen.cs` (new), `OpenBgWindowSession.cs` (new), `OpenBgWindow.cs` (new) | pure page builder, per-player session, runtime |
| `.../commands/playercommands/CompanionWindowTransport.cs`, `CompanionManager.cs`, `PlayerCompanionCommands.cs`, `OpenBgSetupCommand.cs` | transport parameters, removal of the Custom8 variant, `/bgsetup` routing and GM lock |
| `tools/dev/Install-CompanionManager.ps1`, `tools/release/build_fork_release.py` | installer and release pack |
| `source/server/Tests/UnitTests/UT_OpenBgWindow.cs` (new, replaces `UT_OpenBgManager.cs`) | server tests |

Paths under `source/server/GameServer/` unless given.

---

### Task 1: Protocol constants (builder and C#)

**Files:**
- Modify: `source/server/tools/build_companion_manager_client.py` (constants block near the gear constants ~line 305-340, `layout_constants()`), `source/server/GameServer/bots/CompanionManagerProtocol.cs`
- Test: `source/server/tools/test_companion_manager_client.py` (the C#-constants comparison, test ~641-645)

**Interfaces:**
- Produces (Python and C# must agree): `BG_WINDOW = 0x72`; `OP_BG_SHOW = 10`, `OP_BG_HIDE = 11`, `OP_BG_TOKEN = 12`; `BG_EVENT_BASE = 0xA00` (C#: `BgEventBase`); `BG_READY_CONTROL = 0xFF`; `BG_COMMAND_PREFIX = "&bgsetup ui "`; `BG_LABEL_BASE = 620`, `BG_ROW_COUNT = 22`, `BG_ROW_STRIDE = 16`, `BG_HEADER_LABELS = 5`, label group indexes: status 620, tabs 621-624, page indicator 625, headers 626-630, reply 631-632, rows 633-984, `BG_LABEL_END = 985`, `TOTAL_LABEL_COUNT = 985`; controls: row link `0x00-0x15`, row minus `0x20-0x35`, row plus `0x40-0x55`, tabs `0xE0`/`0xE1`, navigation `0xE8-0xEB`, close `0xFD`, refresh `0xFE`, ready `0xFF`; row label offsets 0-15 as in the spec section 4; C# `CompanionManagerProtocol.TryParseBgControl(string token, string control, out ushort revision, out int value) -> bool` (token four lower-case hex digits, control two lower-case hex digits `00-ff`, no `ControlLimit`).

- [ ] **Step 1: Write the failing test.** Extend the constants comparison test in `test_companion_manager_client.py` so that every constant above is compared with the C# constant of the same name by the same parsing used for the gear constants; add a C# unit test in `UT_OpenBgWindow.cs` (create the file with just this test class for now): `TryParseBgControl("0001","ff")` is true with revision 1 and value 255; `("0001","FF")`, `("001","ff")`, `("0001","f")`, `("000g","ff")`, `(null,"ff")` are false.
- [ ] **Step 2: Run to verify they fail** (Python: missing constants; C#: missing member).
- [ ] **Step 3: Implement** the constants in both files with the values above and `TryParseBgControl` next to `TryParseClientControl` (reuse `IsLowerHex`). `TOTAL_LABEL_COUNT` changes from 620 to 985 in `layout_constants()` and the C# `LabelCount` equivalent; check that nothing else assumes 620 (grep `620`, `TOTAL_LABEL_COUNT`, `LabelCount`) and that the existing manager/bar/gear label tests still pass.
- [ ] **Step 4: Run** `python <venv>/bin/python source/server/tools/test_companion_manager_client.py --help` to learn the invocation, then the full builder test against a fresh stage (build with `--client <dir containing game.dll and ui/uimain.xml of the raid baseline copy> --output <new empty dir>` as in the docs), and the C# filter `UT_OpenBgWindow|UT_CompanionManager|UT_CompanionBar|UT_CompanionGear`. All green.
- [ ] **Step 5: Commit** `feat(open-bg-window): protocol constants for the OPEN BG window (builder and C#)`.

---

### Task 2: Native client additions (ops, event range, sender, flag)

**Files:**
- Modify: `source/server/tools/build_companion_manager_client.py` (`build()`: data area `~line 509-524`, init, packet block ops, event handler `~eventHandler`, a new `bgEvent` block at `.cmgr+0x3000`, `bgSend` copy of `sendControl` `~line 785`, hash guard at `0x4E0B3E`, manifest `openBg` section)
- Test: `source/server/tools/test_companion_manager_client.py`

**Interfaces:**
- Consumes: Task 1 constants.
- Produces: operation 10 BgShow (`bg_active = 1`, `vtbl[2](0x72, 1)`, then sends the ready control `ff` through `bgSend`), 11 BgHide (`bg_active = 0`, `vtbl[2](0x72, 0)`), 12 BgToken (four hex characters into the BG command buffer `"&bgsetup ui 0000 00"` with the same validation as op 4, builder ~709-726); event handler: an `OnClickEvent` in `0xA00-0xAFF` jumps to `bgEvent`, which sends `&bgsetup ui <token> <cc>` through the client's own slash-command sender only while `bg_active` is 1 (at `active+8`, free byte, builder:512), then returns "handled"; all other event ids unchanged (raid `0x600-0x64F` handled, rest to the stock dispatcher); the extra hash guard `6a 01 6a 72` at `0x4E0B3E` is verified; `TOTAL_LABEL_COUNT` 985 in the init loop (text adapters 985, then the 59 icon adapters, then the raid adapters, exactly the order of the existing init); manifest gets an `openBg` section (block addresses and lengths).

- [ ] **Step 1: Failing emulation tests** (Unicorn, the existing harness): init registers 985 text + 59 icon + 240 raid adapters; op 1 on label index 984 takes effect, index 985 is ignored; op 10 results in `vtbl[2](0x72,1)`, `bg_active == 1` and the sent command `&bgsetup ui 0000 ff`; op 12 with a valid token updates the buffer, an invalid token leaves it; op 11 results in `vtbl[2](0x72,0)` and flag 0; every BG control `0x00-0x55`, `0xE0`, `0xE1`, `0xE8-0xEB`, `0xFD`, `0xFE` with `OnClickEvent = 0xA00 + cc` sends `&bgsetup ui <token> <cc>` when the flag is 1 and nothing when it is 0; events `0x9FF` and `0xB00` pass through (the existing test that expects `0xA00` to be passed to the raid, test ~408, must be changed to expect `0xB00` as the first passed-through value above the gear range and `0xA00` handled); manager, bar and gear operations and clicks behave exactly as before (run the existing tests unchanged).
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** in `build()`. Read how the gear block (ops 7-9, `0x900+n` events) and `sendControl` are assembled with keystone and mirror them; keep every new block inside its reserved area and update the block-overlap/space assertions of the builder. Do not touch the manager, bar or gear code paths.
- [ ] **Step 4: Run** the full emulation test suite (all old tests plus the new ones) and a double build for determinism (two outputs, identical `game.dll` hash; print the hash).
- [ ] **Step 5: Commit** `feat(open-bg-window): native OPEN BG operations, click range 0xA00 and own sender in the manager stage`.

---

### Task 3: XML window `custom5_window.xml`, uimain include, manifest

**Files:**
- Modify: `source/server/tools/build_companion_manager_client.py` (`open_bg_window()` new, `main()`), `source/server/tools/test_companion_manager_client.py`

**Interfaces:**
- Consumes: Tasks 1-2.
- Produces: `open_bg_window() -> bytes` (the XML for `custom5_window.xml`, WindowId `Custom5`, fixed 760 x 648 on the `dlg_sm_title_noresize` template, stock templates only, the fixed title "OPEN BG", fixed column heading texts, the 365 label controls placed per spec sections 4 (geometry) and the click areas (`OnClickEvent` `0xA00 + cc`) for controls `0x00-0x15`, `0x20-0x35`, `0x40-0x55`, tabs, navigation, close, refresh, placed LAST so they sit on top (raid z-order as in the other windows); the stage writes `atlantis/custom5_window.xml` and `isles/custom5_window.xml` with identical bytes and `uimain.xml` with the include list `custom8, custom7, custom6, custom5` (hash recorded as `uimainBgSha256`, the 0.276.0 uimain `cef698f3...` recorded as `uimainGearSha256`); manifest entries `bgWindowSha256`, `uimainBgSha256`, `uimainGearSha256`, `protocolVersion` 3 unchanged; `main()` refuses when `custom5_window.xml` already exists in a skin of the input client or `custom5_window.xml` is already included in `uimain.xml`.

- [ ] **Step 1: Failing tests:** every BG label (indexes 620-984) appears exactly once in the XML as an adapter binding; the click areas for the 75 controls exist once each with the right `OnClickEvent`; no two click areas overlap (the pattern of the gear/bar XML tests); every control lies inside 760 x 648; only stock templates are used (the existing check for the other windows); both skins identical; uimain contains the includes in the order 8, 7, 6, 5; custom6/7/8 bytes unchanged (compare with the known hashes); `main()` refuses an input that already has custom5.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** by reading `gear_window()` and `bar_window()` (templates, label binding syntax, click areas, `ET.indent` serialisation, encoding iso-8859-1) and building the window from the row geometry of the spec (columns x/width: name 10/140, Lv 154/26, class 186/104, unit 296/100, state 402/150, value 558/110, minus 672/36, plus 712/36; rows from y=164 every 20 px; title y=1; tabs/paging/refresh/close y=24; headers y=46..118 every 18; separator y=140; column heads y=144; reply lines y=606 and y=624). The 16 label slots per row share one row band: slot offsets 0/1 are full-width (740 px) lines, 2-5 name colours, 6 level, 7 class, 8 unit, 9-10 state, 11 value/link, 12-13 minus, 14-15 plus (see the spec table; the active/grey pairs sit on the same spot).
- [ ] **Step 4: Run** the whole emulation + XML suite; build the stage twice (identical hashes); print the final `game.dll`, `custom5_window.xml`, `uimain.xml` hashes and write them into the commit message body.
- [ ] **Step 5: Commit** `feat(open-bg-window): custom5 OPEN BG window XML, uimain include and manifest`.

---

### Task 4: Snapshot data (archetype per unit, field figures)

**Files:**
- Modify: `.../battlegrounds/open/OpenBattlegroundRoster.cs` (`UnitInfo` ~590-599, `Register`, `AdvancePreset`/`PresetFor` call sites), `OpenBattlegroundStatus.cs` (`OpenBgBotStatus` ~22-23, `Capture`), `OpenBattlegroundDirector.cs` (`OpenBgMetrics`, the 60 s field log ~559)
- Test: `UT_OpenBgWindow.cs`

**Interfaces:**
- Produces: `UnitInfo.Archetype` (string, empty when none) set at Register from the preset chosen for the unit; `OpenBgBotStatus.Archetype` (optional string, empty default) filled by `Capture`; `OpenBgMetrics.LastField` (a small immutable record `(int UnitsOut, int Units, int BotsOut, int Bots, int Pct)`, volatile, set by the director every time it logs `OPEN_BG_FIELD`; `OpenBgMetrics.TryGetLastField(out ...)`); the snapshot (`OpenBgStatus`) exposes the last field figures as optional values; the launcher's `open-bg-status.json` is extended additively only if the launcher's parser tolerates unknown fields (read `OpenBgSettings`/the launcher status reader first and only add fields it ignores).

- [ ] **Step 1: Failing tests:** `OpenBgMetrics.LastField` round trip (set via the pure `FieldLine` path or a new setter and read back); a status capture helper (pure part) maps the unit archetype into the bot status; the JSON writer output still parses with the launcher's reader (test in the launcher test project if the reader is testable there).
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement.** Keep every existing field and behaviour; archetype empty means "none". No behaviour change for the roster.
- [ ] **Step 4: Run** `UT_OpenBg|UT_OpenBattleground|UT_OpenBgWindow` and the launcher `OpenBg|Presentation` filter. All green.
- [ ] **Step 5: Commit** `feat(open-bg): archetype per unit and last field figures in the open BG snapshot`.

---

### Task 5: Pure page builder and session

**Files:**
- Create: `.../commands/playercommands/OpenBgWindowScreen.cs`, `OpenBgWindowSession.cs`
- Test: `UT_OpenBgWindow.cs`

**Interfaces:**
- Consumes: Task 1 constants and label layout, Task 4 data, `OpenBattlegroundStatus.Capture` result, `OpenBgSetupValues.Current`, `OpenBgSetupParser` (limits/steps), the helper functions of `OpenBgManagerSession` (`Change()`, `UnitsWrite`, `OrderedUnits`, `StateText`, `UnitLabel`, `LeaderOf`, `RealmHeader`: read them and move/extract what the new screen needs into the new file or a shared static; the old `OpenBgManagerSession` is deleted in Task 6).
- Produces: `OpenBgWindowPage { Lage, Steuern }`; `OpenBgWindowScreen.Build(OpenBgStatus status, OpenBgSetupValues values, OpenBgWindowPage page, int offset, bool isGm, string reply) -> OpenBgWindowView` where the view carries: status text, tab states, page indicator text, header lines (5), reply lines (2), up to 22 row records (each: section title or plain text, up to four name-colour variants, level, class, unit, state texts with active/grey, value text, minus/plus availability and the intent for link, minus, plus) and the total row count; all texts already clipped with the arial14 advance table (`CompanionManagerProtocol` width helpers) to the column widths; `OpenBgWindowView.ToLabels() -> string[985 - 620]` (labels for indexes 620..984 in the exact slot order of the spec; unused slots empty); intents: an enum + argument (`SetEnabled(bool)`, `SetMode(serverpvp|perrealm)`, `CyclePeace`, `Adjust(key, +/-1)`, `Relieve(unit)`, `Tab`, `Scroll(delta)`, `Refresh`, `Close`); `OpenBgWindowSession` with per-page offsets, token and revision (`CommitView`, `IsStale` like `OpenBgManagerSession.CommitView/IsStale` ~94-104), `Resolve(control) -> intent` honouring the row/control map of Task 1, GM-only intents absent for non-GM.

- [ ] **Step 1: Failing tests** (pure, no world): Lage page for a status with 3 realms/units/bots renders the headers (phase, mode, raid, keep owner, speed, field, crew) and rows; Server PvP summarises by archetype and Per realm by realm; scrolling/offset clamp and "1-22 of N" text; text clipping never exceeds the column width; Steuern for a GM has `[-]/[+]` intents for band min/max, bot cap, groups8/small/solos, each preset archetype (std8, caster8, raid8, stealth4, small, duo), drift, cycle min/max, switches enabled/mode/peace, and `[Relieve]` per unit; Steuern for a non-GM has the same rows but no intents; steps: bot cap 4, cycle 5, rest 1 and the limits from `OpenBgSetupParser`; `Resolve` maps control values to intents and rejects a row control with a stale token while navigation controls are always accepted; the label array length is 365 and index 0 is the status label (620).
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** the screen and session. No packet sending, no player objects in this task.
- [ ] **Step 4: Run** `UT_OpenBgWindow|UT_OpenBg|UT_OpenBattleground`. All green.
- [ ] **Step 5: Commit** `feat(open-bg-window): pure OPEN BG window pages and session (Lage, Steuern, intents, GM gate)`.

---

### Task 6: Runtime, transport, routing, GM lock, removal of the Custom8 variant

**Files:**
- Create: `.../commands/playercommands/OpenBgWindow.cs`
- Modify: `CompanionWindowTransport.cs` (`Publish` parameters `labelBase`, `tokenOp`, `showOp` with the old values as defaults; remove `CompanionWindowOwner`/`CompanionWindowKind`), `CompanionManager.cs` (remove `Claim`/`Release` ~41-45, ~79 and `IsIndependentWindowControl` ~99-101), `PlayerCompanionCommands.cs` (remove the OpenBg branches ~44-57), `OpenBgSetupCommand.cs` (route `/bgsetup` empty to the window, `ui <t> <c>` to `HandleClientControl` before `Parse`, `text` unchanged; GM lock for write and `relieve`), delete `OpenBgManager.cs`, `OpenBgManagerSession.cs`, `UT_OpenBgManager.cs` (the still needed pure tests move to `UT_OpenBgWindow.cs`), `docs/COMPANION_MANAGER_INTEGRATION.md` line 68 ("Open BG manager")
- Test: `UT_OpenBgWindow.cs`, `UT_OpenBgSetupCommand.cs`

**Interfaces:**
- Consumes: Tasks 1, 4, 5.
- Produces: `OpenBgWindow.Open(GamePlayer)`, `OpenBgWindow.HandleClientControl(GamePlayer, string token, string control)`, `OpenBgWindow.Close(GamePlayer)`; sessions kept in a `ConditionalWeakTable`; refresh every 15 s while open (pattern `StartRefresh/KeepsRefreshing`, OpenBgManager.cs:243-272); `Open` sends all labels (op 1 with `labelBase` 620), the token (op 12), then show (op 10); no ready control within 3 s -> `ClientMissing`: shows page 1 of `/bgsetup text` and one line "This client has no OPEN BG window; update the client files (UPDATE OFFLINE DAOC.cmd / Install-CompanionManager.ps1)" once, later `/bgsetup` calls go to the text page directly; `HandleClientControl` parses with `TryParseBgControl`, resolves the intent, re-checks `PrivLevel >= GM` for steering intents, applies through `OpenBgSetupStore.TryWrite/Relieve` and the parser limits, sets the reply line, re-renders; `OpenBgSetupCommandHandler`: the `<key> <value>` write path and `relieve` require `ePrivLevel.GM` (viewing and `text` stay for everyone, message "Only GMs may change the open BG setup."); the existing key table, the 1800-byte page budget and `EveryPropertyRowHasAKey` unchanged.

- [ ] **Step 1: Failing tests:** `UT_OpenBgSetupCommand`: a non-GM `/bgsetup groups8 2` and `/bgsetup relieve 5` are refused with the GM message and change nothing; a GM works as before; `/bgsetup text` works for everyone; `ui` with bad arguments does not throw and does not reach `Parse`. `UT_OpenBgWindow`: the pure routing decision (`RouteBgsetup(args) -> Open|Ui|Text|Write|Relieve`); the GM check helper; the transport `Publish` default arguments keep the Custom8 behaviour (the existing companion manager tests prove it: run them); `ClientMissing` state machine as a pure function (open sent at t0, no ready at t0+3 s => missing, ready => confirmed); stale token reject.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** and delete the Custom8 variant. Keep `CompanionManager`, COMPANIONS and gear behaviour exactly as before (their tests must pass unchanged). `ALL SERVER COMMANDS.txt` (CRLF, binary-safe): the `/bgsetup` line says the OPEN BG window, GM-only changes, `text`.
- [ ] **Step 4: Run** `UT_OpenBgWindow|UT_OpenBgSetupCommand|UT_OpenBg|UT_OpenBattleground|UT_Companion|UT_Gothwaite` filters; all green; build `CoreServer` Release.
- [ ] **Step 5: Commit** (two commits: window runtime + routing + GM lock; removal of the Custom8 variant).

---

### Task 7: Installer and release pack

**Files:**
- Modify: `tools/dev/Install-CompanionManager.ps1` (`$KnownManagerBuilds` ~41-47 add `bfefe97d7f7175204ae18c6d23a6958e13819e45155e84738bc5ae9c8d90c264` (0.276.0); known uimain ~213 add `cef698f354bd3d1c2e7c40f794dac38d0f172a5889d7b8996f1d41c131a1328c`; custom5 paths in both skins; a foreign custom5 is refused; backup/restore fields `PreviousBg*Sha256`, `BgWindowSha256`; `RestoreAddedWindow` removes custom5; the "already installed" check includes custom5), `tools/release/build_fork_release.py` (`COMPANION_STAGE` ~18, `known_games` ~119-125 add `bfefe97d...`, uimain list ~128-129 add `cef698f3...`, custom5 `['absent']`, stage check on `bgWindowSha256`)
- Test: `tools/dev/Test-DeployOfflineDAoC.ps1` pattern or a new `Test-InstallCompanionManager` dry-run script; release pack unit check

**Interfaces:**
- Consumes: the stage manifest keys of Task 3.
- Produces: an installer that upgrades from 0.169.0 and from 0.276.0 (`game.dll`, `uimain.xml`, window files incl. custom5; backups in `companion-manager-upgrade-<stamp>`; `-RestoreBackup` puts the previous files back and removes custom5 when it was added), dry run by default, requires game/server/launcher closed; the release pack allows the 0.276.0 files as upgrade sources.

- [ ] **Step 1: Failing check:** a dry-run script copies the 0.276.0 installed client files from `playable/runtime/client-opendaoc/app` (game.dll, ui/uimain.xml, ui/atlantis and ui/isles custom6/7/8 files) and the 0.169.0 set from `playable-backups/companion-manager-upgrade-20261015-173803` into scratch directories laid out like an install root and runs the installer dry run against the new stage; before the change the installer refuses (unknown build); after, it reports the upgrade path and the file list incl. custom5.
- [ ] **Step 2: Run to verify it fails.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the dry runs for both starting states; an apply+restore round trip on the scratch copies (only scratch; never the real install) leaves the previous files byte-identical; the release pack builder's self-checks pass (run it as far as it goes without publishing; do not run `gh`).
- [ ] **Step 5: Commit** `feat(open-bg-window): installer and release pack accept the 0.276.0 stage and add custom5`.

---

### Task 8: Final build, docs, changelog, version

**Files:**
- Modify: `docs/COMPANION_MANAGER_INTEGRATION.md` (new section for the OPEN BG window: build, hashes, install, checklist), `docs/CAMLANN.md`, `docs/TASKS.md` (item 116 and the window item: done offline, real-client check pending), `ALL SERVER COMMANDS.txt`, `CHANGELOG.md` (`## [0.280.0] - 2026-10-16`; Added: OPEN BG window, GM lock, new client stage; Changed: `/bgsetup` opens the window, the Custom8 variant removed; Fixed/Removed as built), version pins

- [ ] **Step 1:** Build the stage twice from the raid baseline copy into `playable-dev/stage-companion-manager-0.280.0` (second build in scratch to compare); record `game.dll`, `custom5_window.xml`, `uimain.xml` hashes in the docs table (the format of the 0.276.0 section).
- [ ] **Step 2:** Write the real-client checklist for the owner (install steps, `/bgsetup` opens the window with the title OPEN BG, both pages, +/- on a GM account, fallback on an old client, window next to Manager/COMPANIONS/gear, the three windows still work).
- [ ] **Step 3:** Changelog, version pins (four places), TASKS, docs.
- [ ] **Step 4: Run** the server filter `UT_OpenBg|UT_OpenBattleground|UT_Companion|UT_Gothwaite|UT_Rvr`, launcher `OpenBg|Presentation`, the Python suite, and Release builds of CoreServer and the Launcher. All green.
- [ ] **Step 5: Commit** in logical commits. Do NOT merge or push (the controller does after the final review).

---

## Self-Review

Spec coverage: content (Lage/Steuern, Task 5), own sender/token/range/ops (Tasks 1-2), XML and geometry (Task 3), data (Task 4), server runtime, GM lock, fallback, removal (Task 6), installer and release (Task 7), docs/version/checklist (Task 8). Not in this plan: a persisted per-player window state, the Per-realm per-realm crew rows beyond the fields of `open_bg_units_<realm>`, the vulture rule.

Type consistency: constants named identically in builder and C# (Task 1); the label array length 365 and base 620 used in Tasks 2, 3, 5, 6; intents defined in Task 5 and consumed in Task 6.

Placeholders: native code, XML and pixel geometry are specified by the spec and the existing builder patterns the tasks name (`gear_window()`, `bar_window()`, `sendControl`); the implementer reads those first; each pure rule has a named test.
