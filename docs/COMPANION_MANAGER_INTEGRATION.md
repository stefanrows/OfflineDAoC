# Companion Manager integration handoff

Status: **real-client gate passed on 0.32.1 (2026-09-24).** The owner confirmed
that the window opens and that clicking and chat-line search work; the
companion bag opened beside it. On 2026-09-24 the owner also marked gate step 4
(raid windows, 800×600) and the companion-window acceptance items complete.
Detailed observations were not supplied. Next steps are in the
[Companion Manager roadmap](COMPANION_MANAGER_ROADMAP.md).

0.33.0 (offline only, owner check pending) rebuilds the manager `game.dll` with
two extra labels for the detail scroll links and adds a separate raid
click-to-target XML patch. See [Packaging](#packaging) and
[Raid click-to-target fix](#raid-click-to-target-fix-0330). Offline results
(2026-09-24): the manager emulation test passed with 132 adapters, the
unchanged old builder still reproduced the installed 0.32.1 `game.dll`, and
the companion, raid, and command server tests passed 318/318.

**0.169.0 (offline only, owner check pending)** redesigns the window for large
account-bound rosters: arial14 text, a sectioned list with level, class, type
and state columns, grouping and sorting buttons, and a list size that fills a
larger window. It rebuilds the manager `game.dll` (protocol 3) and the window
XML, so the client files must be upgraded. See
[Roster organisation, text size, and list size](#roster-organisation-text-size-and-list-size-01690).

## Real-client result, 2026-09-24 (0.32.0)

The window opened and showed server text, so labels, the token, and show work
in the real client. No click did anything. Root cause (static disassembly,
confirmed by emulating the client's own code): the `ButtonDef` and
`InvisibleButtonDef` parsers read `OnClickEvent` through `0x4EA06F`, not the
`ControlId` mapper `0x4E99E6` that the raid and the 0.32.0 manager hooked.
`0x4EA06F` knows only stock event names. For anything else it returns `-1`
(no event) unless the text starts with a digit, which it converts with `atoi`.
Names such as `CompMgr30` therefore never fired. This also explains the probe's
silent dedicated-action click. The raid's `RaidMemberNN` click areas are
affected the same way, so raid click-to-target most likely never fired either.
0.33.0 fixes that separately; see
[Raid click-to-target fix](#raid-click-to-target-fix-0330).

0.32.1 writes decimal event IDs (`0x700 + control`, for example `1840`) into
`OnClickEvent` and no longer patches `0x4E99E6`. The raid's bytes there stay as
the raid built them. The offline test now runs the client's own `0x4EA06F` on
every XML value and checks statically that the `InvisibleButtonDef` parser
calls it.

## Intended player experience

Bare `/companions` opens one movable, dark DAoC-style `Custom8` window.
Roster and Recruit share a scrollable, searchable list and a detail panel.
Selecting a row replaces the detail content without appending a speech page.
Every subcommand remains available for an unpatched client. If the patched
window does not answer within three seconds, the server prints one line of
command guidance. The accumulating NPC speech menu stays closed.

| Area | Implemented behavior |
| --- | --- |
| Roster | Active or benched state, class, level, XP, training mode; Invite and Bench on the selected owned record. |
| Recruit | Starts on the player's realm and allows Albion, Midgard, and Hibernia; filters authored people and generated classes by name, class, realm, and role. |
| Details | Overview, Training & Tactics, and Gear tabs; the selected record and list position are kept per tab during refreshes. |
| Gear | Worn slots and backpack items with item actions, plus the existing native companion bag for inspection and drag/drop. Benched gear is read-only. |
| Builds (0.35.0) | Training & Tactics lists the class's builds as links with their role; selecting one and choosing **[Use build]** switches it. Recruit details list the builds with the class default preselected, and **[Recruit]**/**[Create]** use the selection. |
| Realm abilities (0.131.0, UI revised 0.155.0) | Overview links to a focused realm ability view in Training & Tactics. The earned RP, realm rank and unspent RA points stay visible while scrolling; each class-legal passive RA shows its current rank, next-rank cost, and Buy or point shortage. A click buys one rank for an active or benched companion and reports the result at the top. Purchases are saved separately from career specialization points. Timed active RAs are withheld because companion AI does not activate them. |
| Crowd control (0.36.0) | Training & Tactics offers the Crowd control role to Healer, Sorcerer, Bard, Mentalist, and Spiritmaster companions. Each build line says which role it sets. The roster's role filter keeps its four native buttons; a Crowd control filter needs a later client patch. |
| Group orders (0.37.0) | A **Group orders** row leads the roster list, whatever the search and filters. It shows the group order (Aggressive, Defensive, Passive, or saved stances) as links, each grouped companion's effective stance with its saved stance when an order overrides it, and **[Pull]**, **[Invite all]**, **[Bench all]**, and **[Grind]**/**[Stop grind]**. A companion stays the default selection. |
| Worn slots (0.38.0) | Replaced in 0.39.0. The companion bag window showed the worn slots at positions 51-69; the owner found it unintuitive. |
| Gear slot sheet (0.39.0) | The Gear tab lists all 19 worn slots in character-sheet order, including empty ones, with `N fit` or `upgrade in bag` hints. Clicking a slot opens it at the top of the detail pane: the worn item and every backpack item the companion can equip there, best first, with its score change. **[Equip + lock]** equips the selected item in that slot (a ring or bracer on the clicked side); **[Unequip]**, **[Lock slot]**, and **[Keep]** act on the worn item. The bag window is backpack-only again (40 positions). Server only. |
| Inventory window (0.92.0) | **[Open inventory]** shows worn slots at positions 1-19 (character-sheet order) and the takeable backpack at 21-60. Drag own backpack → worn slot hands the item over and equips it; worn → own backpack unequips and returns it when the owner may take it. An owner equip is a manual choice (`*`), replaced only by clearly better earned loot. The Gear tab numbers each worn slot with its window position; a clicked slot shows the worn item with **[Info]** (native item info), **[Unequip]**, **[Lock slot]** and **[Keep]**; its fitting-item lists were removed in favor of dragging. Server only. |

The recruitment copy is shown in the detail panel exactly as agreed:

> **Story companions (authored):** Named characters with their own background
> and personality. You can recruit each one once.

> **Create a companion (generated):** Choose a class and a new person is
> created for your roster.

Both end with: “Both are permanent companions who earn XP and keep their
training and gear. `/spawn` helpers are temporary.” Visible labels say
**Albion**, **Midgard**, and **Hibernia**; saved enum values and keys are
unchanged. Names use restrained realm colors, the selected row has a gold `>`
marker, and active tabs and filters are gold. Roles are text (`Tank`, `Healer`,
`Buffer`, `Attacker`); the client has no proven icon adapter for this window.
Source 0.129.0 enlarged the default window from 640×420 to 720×500 (0.169.0
supersedes it: 980×700, see below). The XML uses
the stock lower-right resize handle, resizable backgrounds, wider detail text,
and bottom-aligned controls. Installation and real-client resize checks remain
pending in task 52; the generated `game.dll` is unchanged.

## Roster organisation, text size, and list size (0.169.0)

The owner's report: enlarging the window did not show more companions or
detail lines, the 11-pixel text was hard to read, and an account-wide roster of
dozens of companions across all three realms and every level was one
unorganised list that mixed class, state, and level in a single line.

**Why enlarging cannot add rows by itself.** The window is client XML. Every
control sits at a fixed position, the number of label controls is built into
`game.dll`, XML cannot hide a control, and the client never reports the window's
size to the server. So rows exist or they do not; the server can only fill the
ones it was told to. The fix is to build in many more rows (34 list rows, 29
detail lines) and let the player choose how many the server fills with the
**Rows** buttons (16, 22, 28, 34; the detail panel shows five fewer lines).
Blank rows draw nothing, and the status message says how tall a window each
choice needs (`204 + 22 × rows` pixels: 16 → 556, 22 → 688, 28 → 820, 34 →
952). The default window is 980×700 and fits 22 rows. The choice, with the
grouping and sorting below, is saved per account in the save's
`offline_local_options` table (key `companion_manager.view.<account>`, value
such as `group=Smart;sort=Level;rows=22`) so it survives closing the window and
logging out. Nothing else about the window is saved, an unreadable value leaves
the defaults, and a missing row is created on the first change.

**Text size.** All text uses the client's registered `arial14` bitmap font
(`ui/fonts/Arial14.tga`), about a third larger than `arial11`. No stock window
uses it, so its first real-client rendering is part of the owner's check. The
server fits and wraps text with its measured advances (the same method that
reproduces the arial11 table exactly; a space has no glyph and is estimated at
5 pixels, slightly generous). The Python test fails if the server's table and
the builder's differ.

**Organisation.** The list has columns: name (realm colour), level, class,
type (Story, Regular, New), and state. State has its own colours: green
**Active** (or **Squad N** / **Squad N lead**), grey **Bench**; Recruit shows
**Available**, **Recruited**, or **Create**. Section headers split the list and
a click on a header folds or unfolds its section (`[-]`/`[+]` with the count).

| Control | Choices | Notes |
| --- | --- | --- |
| Group | **Smart** (default), Realm, Role, Level, None | Smart: **In your group** first, then **On the bench** by realm. Role uses the saved role. Level uses bands (50, 40-49, ...). Recruit shows realm sections for Smart and Realm, and none for Level. |
| Sort | **Level** (highest first, default), Name, Class | Within each section. Recruit treats Level as Class. |
| Rows | 16, 22, 28, 34 | How many list rows and (minus five) detail lines are filled. |

The **Group orders** row still leads the Roster list, outside every section.
Search now also matches Story, Regular, Active, and Bench. Active is a real
tab with a count. Up, Down, PgUp and PgDn scroll list lines (headers included)
by the chosen row count.

**Geometry.** Window 980×700 (default and minimum), list pane 472 wide, detail
pane growing with the window, row pitch 22. Controls use event IDs
`0x700`–`0x7DF` (control numbers below `0xE0`; the client's own helpers use
`0x7E0` and `0x7E1`). The window fits a 1024×768 client but no longer an
800×600 one. Click areas for rows past the chosen size exist below the window
edge; the real-client check should confirm they never catch clicks meant for
the game world.

## Client levers research (2026-10-11, static only)

Read-only analysis of the raid baseline (`67dcf68a…`) and the 0.169.0
manager `game.dll` (`49095c51…`) with capstone, for a better manager window.
None of this is real-client proof; the lever probe below exists to get it.

| Lever | Evidence | Result |
| --- | --- | --- |
| Window size | `0x53DCAA` stores the final width/height at owner `+0x2C`/`+0x30` (`0x53DE73`/`0x53DE76`); Custom8 stub `[[0x104C2BC]+0x37178]`, owner `[stub+0x1C]` (lookup inferred) | A click can carry the current size, so the server could choose the row count itself |
| Scalar (bar) adapters | Registrar `0x4B6BC0` stdcall(registry, slot*, name, max, initial), `ret 0x14`; object: dirty `+4`, max `+8`, value `+0xC`, min `+0x10`; setter `0x524370` (ECX = object, float, clamps, sets dirty) | Server-fed `StatusBarDef` bars (HP, power, XP) look feasible |
| Colour adapters | Registrar `0x4B6F87` stdcall(registry, slot*, name), `ret 0xC`; 0x18-byte object, RGBA floats `+0..+0xC`, unknown dword `+0x14`; no setter found | Direct float writes; live repaint unknown |
| Shift-click | `OnShiftClickEvent` is parsed on buttons, invisible buttons, list boxes and checkboxes through the same mapper `0x4EA06F`; no runtime reader found; IDs `0x800`–`0x8DF` are unused | Needs the probe; the manager's `sendControl` masks to one byte, so a shift range needs its own send path |
| Native tabs | Tab records parsed (`0x4ECD60`, `0x4ECCC7`); the stock tab event sends a command and hides nothing locally; no per-control show/hide setter found (window-level only, `0x4BED28`) | Parked |
| List box, combo box, check box, edit box | Row hit-test exists, but no proof the row or item index reaches an event; edit boxes have no Enter event; right-click and double-click are never read | Parked |

**Lever probe.** `probe_companion_manager_levers.py --input-stage <0.169.0
stage>` writes a probe stage (`.cprb` section, event IDs `0x900`–`0x90F`
plain and `0x800`–`0x80F` shift, everything else passed to the manager).
`test_companion_manager_levers_probe.py --stage <probe> --input-stage <0.169.0
stage>` emulates it. `Install-CompanionManager.ps1` does not install probe
stages (no raid baseline in the manifest, and 0.169.0 is not yet a known
manager build); that is a separate decision. In the client, each `[Probe]`
click should show `P#nn W=<hex> H=<hex>`, step the bar by 25 %, and cycle
the label colour; Shift+click should show `S#nn`.

## What the 2026-09-23 probes proved

The supported client is the verified Windows x86 `game.dll` with the native raid
patch (SHA-256
`67dcf68a37b95a93946a943b99d5e19b4a03e08cd6469275e25c7b909de21e99`).
`Custom8` is unused by the installed UI; raid windows use `Custom9` and
`Custom10`.

| Control | Real-client observation | Status |
| --- | --- | --- |
| Open `Custom8` from `/companions` | Window appeared in the normal game. | Proven |
| Server-fed label update | The window displayed server data. | Proven |
| Button through existing target selection | The label changed to “Click reached server” and chat acknowledged it. | Proven, diagnostic only |
| Dedicated client action packet (`0x5E`) | No server acknowledgment. | Failed |
| Editable search (`EditBoxDef`) | Visible, but could not be focused or typed into. | Failed |

The failed probe was removed and the raid-patched `game.dll` restored. The
research builder `source/server/tools/probe_companion_custom8.py` is kept for
reference only.

## Offline diagnosis and repair

Static disassembly of the verified client was used; no client was run.

**Action packet.** The event-name hook and handler used the same `0x700` event
path whose button already reached the server in the first probe. The probe's
handler label (`select:`) shows the dedicated packet replaced the working
target-selection call. The client's TCP sender at `0x4281DF` writes the given
opcode unchanged, as its own callers show (`0xB0` target, `0xA9` position, and
so on). The server silently drops opcodes with no handler, and it discovers
handlers only in namespaces ending in `v168`. The probe's server handler was
never committed, so which layer failed cannot be proven after the fact. The
repair removes that layer instead of guessing:

- Manager clicks call the client's own slash-command sender (`0x42BC08`). The
  stock right-click menu calls it from UI events for `&talk`, `&loco`, and
  `&brandish`. The command travels in the normal `0xAF` command packet that
  every typed `/command` uses. No new opcode or server packet handler exists.
- The command is `&companions ui <token> <control>`: a four-digit lowercase
  hex view token and a two-digit control number. The client supplies nothing
  else. The sender drops an identical repeat within 250 ms, which is harmless.

**Search field.** Both stock edit boxes live in windows backed by dedicated
native classes (the chat-rename and bazaar windows). The generic `Custom8`
window has no such owner, which is the most likely reason the field never took
focus. Rather than depend on an unproven focus path, **[Search]** opens the
ordinary chat line prefilled with `/companions find `. It uses the same calls as
the chat window's own channel-prefix menu: `0x40D56C` switches to chat entry and
`0x40D5D4` sets the text with the cursor at the end. The player types a name
or class and presses Enter. The query reaches the server as a normal command,
and the window shows the current query. `/companions find` with no text clears it.

**Event IDs.** Stock event names map to IDs up to about `0x246`; small client
helpers return `0x7E0` and `0x7E1`. Manager click areas use the decimal event
IDs `1792`–`2015` (`0x700`–`0x7DF`; `0x7BF` before 0.169.0), which the stock `OnClickEvent` parser
accepts directly. (0.32.0 used names such as `CompMgr30`, which the parser
rejects; see the real-client result above.) Every other ID continues to the
raid handler unchanged.

## Protocol version 3

Version 3 (0.169.0) widens the label index to 16 bits (bytes 4-5, little
endian) because the layout now has 440 label adapters. A version-2 client
ignores every version-3 packet and a version-3 client ignores version 2, so a
mismatched client and server fail closed: the window stays empty, the server
prints its one guidance line after three seconds, and every `/companions`
subcommand still works. Install the matching client files.

Server to client uses fixed 128-byte DebugMode bodies: byte 0 is `0`, marker
`0x43`, version `3`, then operation, a 16-bit label index (bytes 4-5), and
NUL-terminated ASCII text
from byte 12, with byte 127 required to be `0`. Operations: `1` set one of 514
label adapters (440 manager plus 74 COMPANIONS window since 0.251.0; 440 before 0.251.0, 132 before 0.169.0, 130 before 0.33.0), `2` show (the client then sends control `dd`, "ready"), `3` hide, `4` set the token (four lowercase hex digits). Version-1 probe packets,
short bodies, bad terminators, unknown operations, and out-of-range indexes are
ignored. Raid marker `0x52` and ordinary DebugMode packets pass through. An
unpatched client sees only a DebugMode packet whose flag byte is `0`, as with
raid packets; byte 1 values other than `1` and `2` are ignored by its flight
extension.

The window uses only controls the installed raid window already proves in the
real client: `LabelDef` with an adapter and an `InvisibleButtonDef` with a
custom `OnClickEvent` laid over it. State colors come from overlapping labels
with fixed colors; the server fills one of them and blanks the others.
`source/server/tools/build_companion_manager_client.py` is the single source
of the label and control numbers. `CompanionManagerProtocol.cs` mirrors them,
and the offline test fails if the two differ.

## Server manager

`CompanionManager` keeps one session per player: region, last use, tab, detail
tab, per-tab realm and role filters, list offset and selection, the search query,
detail offset, selected item, view revision, and the identity behind each visible
row, detail line, and action. The client receives display text and bounded row
numbers only. A click resolves against the session and then re-reads the
current roster or catalog. The revision changes only when a click could now
mean something different. Row, detail, and action clicks with an old token,
after a region change, or after 30 idle minutes are rejected with a short
message and a fresh view. Navigation clicks are always safe. A click without a
session reopens the window and does nothing. Label updates are sent as
differences; **[Refresh]** resends everything.

| Manager action | Existing entry point |
| --- | --- |
| Recruit generated or authored | `PlayerCompanionRoster.TryRecruit` / `TryRecruitAuthored` with the selected build (capacity and once-per-owner checks stay there) |
| Invite or bench | `PlayerCompanionRoster.TryInvite` / `TryBench` |
| Invite all | `TryInvite` for each benched companion shown in the list (search and filters apply), top to bottom, until the group is full |
| Bench all | `TryBench` for every active companion |
| Group order | `CompanionGroupOrders`, shared with `/aggressive`, `/defensive`, `/passive`, and `/companions group default` |
| Pull | `PullGroupCommandHandler.Order`, shared with `/pull` |
| Grind | `PlayerCompanionGrind.TryStart` / `Stop`, shared with `/grind`; still only for temporary `/spawn` helpers |
| Role or stance | `PlayerCompanionRoster.TrySetTactics` (roles include `crowdcontrol`; `cc` is accepted) |
| Build switch | `PlayerCompanionRoster.TrySelectBuild` (free, no trainer, resets and retrains) |
| Training mode | `TrySetManualTrainingMode` / `TrySetAutomaticTrainingMode` |
| Train one rank | Rechecks active companion, class trainer (`CanUseCompanionTrainer`), line, and rank; `SaveProgress` with queued retry |
| Buy passive RA rank | `PlayerCompanionRoster.TrySpendRealmAbility` rechecks ownership, class, rank, point cost, and saved allocation before buying exactly one rank |
| Respecialize | `PlayerCompanionCommandHandler.TryBeginCompanionRespec` and the existing confirmation dialog |
| Open companion bag | `PersistentCompanionInventoryView.Open` for an active companion |
| Equip, lock, keep, unequip, return | `PersistentCompanionGear`, now shared with the legacy menu; every call re-resolves the item and slot and uses `TryApplyEquipmentMutation` or `TryTransferItem` |

Search accepts letters, digits, spaces, apostrophes, and hyphens, up to 24
characters. Every term must appear in the name, class, or realm. All 78 authored
people and all 39 generated classes can be reached by scrolling or filtering.
No save migration was added; companion IDs, authored keys, XP, training,
tactics, flags, inventories, and relogin restoration are unchanged.

## Packaging

```bash
# Read-only input; writes a fresh stage and manifest.
python source/server/tools/build_companion_manager_client.py \
  --client /mnt/d/Games/OfflineDAoC/runtime/client-opendaoc/app --output <new stage dir>
python source/server/tools/test_companion_manager_client.py --stage <stage dir>
```

The builders need `pefile`, `keystone-engine`, and `unicorn` (the emulation test
only). The builder refuses a client that already has the manager installed;
restore it first with the installer's `-RestoreBackup`, then rebuild. The patch is deterministic: from the verified client it produces
`game.dll` SHA-256
`88530c0093b285fd38fd6759e464373baa65ebb20473a949bc3b79fccbb41fd3` (0.33.0,
with `custom8_window.xml`
`27d85bd6d192e574bff96d6898674c201863db36f5d4f67a7ab23fbf0f90bc64`). Earlier
builds: 0.32.1 `3b6274dc…4d70890`, broken 0.32.0 `c36faf71…acf2ee`.

**0.169.0 (protocol 3):** `game.dll`
`49095c51e792200daf824f8a00880c70815a0cacbc37f452cf062cffacb3b63d`,
`custom8_window.xml`
`9cd3b8a5fb3d2a4671192b9d52287173e2fe194a4f19979de1c339f408235efd` (the 0.33.0
`uimain.xml` is unchanged). The builder needs the raid-only baseline: assemble
a folder with the baseline `game.dll` (`67dcf68a…`, kept in any
`companion-manager-client-*` backup under `D:\Games\OfflineDAoC-backups`) and
`ui/uimain.xml` (`637bf806…`) and pass it as `--client`. The built stage is kept
at `D:\Games\OfflineDAoC-dev\companion-manager-stage-0.169.0`.

**Upgrading an installed 0.32.1 or 0.33.0 manager (0.169.0):** close the game,
server, and launcher, then run `Install-CompanionManager.ps1 -InstallRoot
<root> -Stage <stage>` (dry run) and again with `-Apply`. The installer detects
the earlier manager `game.dll` and its `uimain.xml`, backs up `game.dll` and
both window files to `companion-manager-upgrade-<stamp>`, and installs the new
ones; `-RestoreBackup <that folder>` puts the earlier build back. The earlier
restore-then-install route is no longer needed.

`tools/dev/Install-CompanionManager.ps1 -InstallRoot <root> -Stage <stage>` is a
dry run by default. With `-Apply` it checks every input and output hash,
requires the game, server, and launcher to be closed (it stops nothing), backs up
`game.dll` and `uimain.xml`, and rolls back automatically on failure.
`-RestoreBackup <backup>` restores them and removes both `custom8_window.xml`
files. Saves and server files are untouched; `tools/dev/deploy.sh` still handles
the server and launcher.

To upgrade an installed 0.32.1 manager, close the game, server, and launcher,
then run the installer with `-RestoreBackup` on its 0.32.1 backup (it restores
the raid `game.dll` and `uimain.xml` and removes both `custom8_window.xml`),
and install the 0.33.0 stage with `-Apply`. The server works with either
client: a 0.32.1 client shows static `[Up]`/`[Down]` links.

## Raid click-to-target fix (0.33.0)

The raid windows (`Custom9` for 40, `Custom10` for 80) laid an
`InvisibleButtonDef` over each member row with `OnClickEvent` `RaidMember00`
to `RaidMember79`. The stock parser `0x4EA06F` returns `-1` for those names, so
the rows never fired. The raid's handler at `0x4E04DE` already handles event
`0x600 + n`: if row *n* is visible and has an object ID, it calls `0x41AB40`,
the routine the client's own server-target packet handler calls (mode 0, with
the stock messages "You can't assist with that target!" and "That target is
not visible to you!"). The Companion Manager
handler passes any ID outside `0x700`–`0x7BF` on to it unchanged.

The fix changes only the XML: each value becomes the decimal ID `1536`–`1615`.
Both raid builders now write these values, and `build_native_raid80_client.window()`
reproduces the staged files byte for byte. `game.dll` is checked, never
changed.

```bash
# Read-only input; writes a fresh stage and manifest.
python source/server/tools/build_raid_click_fix_client.py \
  --client /mnt/d/Games/OfflineDAoC/runtime/client-opendaoc/app --output <new stage dir>
python source/server/tools/test_raid_click_fix_client.py \
  --client /mnt/d/Games/OfflineDAoC/runtime/client-opendaoc/app --stage <stage dir>
```

The stage builder accepts the raid `game.dll` (`67dcf68a…`) or the raid client
with manager 0.32.1 (`3b6274dc…`) or 0.33.0 (`88530c00…`). It also requires
the installed raid XML hashes (`custom9` `6ff8e226…`, `custom10` `aca82bf6…`).
The staged windows are `custom9` `3f5f037d…` and `custom10` `2bd9e89b…`. The
test runs the client's own parser on every value and its event chain from
`0x4E04DE` for all 80 rows at both capacities, including empty rows, hidden
rows, and a closed window. `tools/dev/Install-RaidClickFix.ps1 -InstallRoot <root> -Stage <stage>`
is a dry run by default. With `-Apply` it checks every hash, requires the game,
server, and launcher to be closed, backs up the four XML files, and rolls back
on failure. `-RestoreBackup <backup>` restores them.

Offline results (2026-09-24): the test passed against the installed 0.32.1
client, the raid-only baseline, and the 0.33.0 manager build. The installer's
dry run accepted the real installation; apply, refusal of a second install, and
restore were exercised on a scratch copy. None of this is real-client
acceptance.

## Remaining gate: one combined real-client check

Offline results (2026-09-23): the x86 emulation passed every hook, validation,
command, search, and passthrough check. The installer dry run accepted the stage
against the installed client. The focused server tests passed 27/27, and the
wider companion, raid, and command selection passed 317/317. A preview rendered
from the window XML with the client's own `arial11` glyphs checked that the
labels fit. None of this is real-client acceptance.

With the server and launcher deployed from this build and the client patch
installed, the owner checks in one session:

1. `/companions` opens the window, and no guidance line appears after three
   seconds. Rows, tabs, and filters show server text.
2. Clicking **Recruit**, a realm filter, and a row changes the window. This
   proves the command path; a click should never print “No such command”.
3. **[Search]** opens the chat line with `/companions find `; typing `sham` and
   pressing Enter filters the list to Shaman entries.
4. The raid windows (`/raid 40`) still work, and the window fits at 800×600
   (0.169.0 raised the window to 980×700: check at 1024×768 instead).

If step 2 or 3 fails, stop and record the exact observation before building a
reduced UI. After the gate passes, the Stage 6 checklist items below apply.
Record them in `docs/COMPANION_STAGE6_ACCEPTANCE.md`, and close the matching
roadmap items only after owner confirmation:

- Roster and Recruit scrolling, search, realm and role filters, selection, all
  78 authored people and every generated class, and both recruitment texts.
- Recruit one authored and one generated companion; a second recruit of the
  same authored person is refused; invite, bench, train, tactics, XP, relog.
- Inspect, equip, lock, keep, unequip, and transfer via the manager and native
  bag; rejected and full-bag actions preserve items and coins.
- An unsupported client build fails closed, and `/companions` commands keep
  working.

## COMPANIONS window (Custom7), 0.251.0

Status: **offline only; the owner's real-client check is pending.** The window
is a fixed 332x296 bar that the server shows while the account's setting is on
and an owned companion is with the player. It is separate from the Companion
Manager's Custom8 window, which is unchanged.

### Layout and behaviour

| Element | Content |
| --- | --- |
| Title and status | "COMPANIONS"; status "{order or Own stances} \| n with you \| m in squads". |
| Ten tiles | Aggressive, Defensive, Passive, Assist (the group orders); Default (saved stances); Follow; Stay; Pet pull; PULL; MANAGER. Active tiles are gold with an underline; disabled tiles are grey. |
| Message line | The reply to the last tile click. Only PULL and refusals are echoed to chat. |
| Member rows | Eight rows that mirror the group window's member slots: native name (group_name), health bar (group_health), level (group_level) and a server-written stance. A row click is the stock SelectGroupMember<i> target. |

Tile rules (`CompanionBarView.ComputeTiles`): Stay is disabled while pet pull is
off and the companions are not staying; Pet pull is disabled under Assist train;
PULL is disabled with no owned companion. The owned set is
`CompanionSquads.OwnerForceBots` filtered to the owner's persistent or temporary
companions, the same predicate as the manager's group detail.

The window shows only while the setting is on and at least one owned companion
is present. It hides when the last one leaves, and at login the server always
sends one hide when the window should not show.

### Protocol (still version 3)

- Operation 5 shows Custom7 (window `0x74`) and sends the ready control `0xBF`;
  operation 6 hides it. A version-3 client without this stage ignores both.
- Labels 440-513: 74 new adapters `cmgr_440` to `cmgr_513`. Status 440, message
  441, tiles 442-481 (four labels each: inactive, active, disabled, underline),
  member rows 482-513 (four stance colours each: Aggressive, Defensive, Passive,
  Assist). The label count is 514 in total; `LabelCount` stays 440.
- Clicks use the manager's own sender, `&companions ui <token> <control>`, with
  controls 0xA8-0xBF (decimal event IDs 1960-1983). A separate flag
  (`barActiveFlag`, at `active + 4`) gates them, so the bar works with the
  manager closed. Manager controls never use 0xA8-0xBF. The server routes these
  controls before any token or session check (`CompanionManager.HandleClientControl`).
- Stance and camp tiles are silent; PULL and refusals echo to chat.

### Evidence for the window ID and the ini position

- `ShowCustomN` pushes `0x6D + N`: Custom7 is `0x74`, Custom8 `0x75`. The bytes
  at 0x4E0B4A are `6a 01 6a 74` (Custom7) and at 0x4E0B50 `6a 01 6a 75` (Custom8)
  in the raid baseline `game.dll` (`67dcf68a...`). Nothing installed uses Custom7.
- The client saves a window's position in the ini `[Panels]` line, here
  `Custom7=x,y,a,b,flag`. The window reopens where it was closed.
- The member rows use the stock adapters `group_name<i>`, `group_color<i>`,
  `group_level<i>` and `group_health<i>`, and the stock events
  `SelectGroupMember0` to `7` (event IDs 122-129, checked through the client's
  own `0x4EA06F` mapper in the emulation test).

### Setting and commands

- Stored per account in `offline_local_options` as `companion_bar.enabled.<account>`
  with the value `on` or `off`. A missing or unreadable value means on.
- `/companions bar on|off` changes it. Bare `/companions bar` shows the window
  again after its X. If the client does not answer the show within three
  seconds, one line explains that the client files need updating.
- The Companion Manager's Group orders panel has a line
  "COMPANIONS window: on (click to turn it off)" (or the reverse).
- MANAGER opens the Companion Manager on its Group orders row.

### Build, test and install

```bash
# Read-only input; writes a fresh stage and manifest.
python3 source/server/tools/build_companion_manager_client.py \
  --client /mnt/d/Games/OfflineDAoC-dev/raid-baseline-client --output <new stage dir>
python3 source/server/tools/test_companion_manager_client.py --stage <stage dir>
```

The raid-only baseline client is a folder with the `game.dll` (`67dcf68a...`) and
`ui/uimain.xml` (`637bf806...`) from a `companion-manager-client-*` backup. The
verified stage is `D:\Games\OfflineDAoC-dev\companion-manager-stage-0.251.0`.

| Output | SHA-256 |
| --- | --- |
| game.dll (manager and COMPANIONS window, protocol 3) | `73223deffd8455944974b555f5bce5762c43a642c4442b5102194e337e47daf4` |
| custom7_window.xml (both skins) | `7920bcd32909a0e6b5ad2e625902f9f8807fe04ea4370f2ace2b301a8ee59aba` |
| uimain.xml (with both window includes) | `7d573c315a56bcacae8e8ebc871646654bfe8da5bb1dc745d6023da7f93b9dd9` |
| uimain.xml, manager only (0.169.0 UI) | `e1266fcceb954b5e835d3e3ca6563d682f4bb0f6430d36947923a74974dfd2a0` |
| custom8_window.xml (unchanged) | `9cd3b8a5fb3d2a4671192b9d52287173e2fe194a4f19979de1c339f408235efd` |

The 0.169.0 manager `game.dll` (`49095c51...`) is the installed build that the
upgrade starts from. The build is deterministic: a second build gives the same
`game.dll` and window hashes.

Install with `tools/dev/Install-CompanionManager.ps1`, a dry run by default.
With `-Apply` it upgrades an installed 0.169.0 manager in place: it backs up
`game.dll`, `uimain.xml` and the window files (custom8 and custom7) to
`companion-manager-upgrade-<stamp>`, and rolls back on failure. `-RestoreBackup`
restores the earlier files and removes the custom7 files this stage installed.
The game, server and launcher must be closed; the script stops nothing.

### Real-client checklist

1. Login with a companion present: the bar appears once, and no ghost bar
   remains when no companion is with you.
2. Zoning, and a companion leaving and rejoining: the bar hides and shows
   again. The window keeps its position after a relog (ini `Custom7=`).
3. Member rows: health, level and stance fill for each group member, empty rows
   draw nothing, and the player's own row is in its slot. Clicking a row targets
   that member.
4. Tiles: each click sends its order; PULL echoes; the active gold tile and the
   underline match the state. Check the "Aggressive" label fit in 76 px.
5. The status line fits (the label is 170 px; longer text is shortened with "...").
6. `/companions bar off` hides it at once; `on` shows it again; the manager's
   Group orders line switches the same setting.
7. The manager still works with 514 adapters: open `/companions`, check the rows,
   tabs and the raid windows (`/raid 40`).
8. An unpatched 0.169.0 client with this server: `/companions` and every command
   still work, and no bar appears.
