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
| Crowd control (0.36.0) | Training & Tactics offers the Crowd control role to Healer, Sorcerer, Bard, Mentalist, and Spiritmaster companions. Each build line says which role it sets. The roster's role filter keeps its four native buttons; a Crowd control filter needs a later client patch. |

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
The window is 640×420 and fits the smallest shipped layout (`default800.ini`,
800×600).

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
IDs `1792`–`1983` (`0x700`–`0x7BF`), which the stock `OnClickEvent` parser
accepts directly. (0.32.0 used names such as `CompMgr30`, which the parser
rejects; see the real-client result above.) Every other ID continues to the
raid handler unchanged.

## Protocol version 2

Server to client uses fixed 128-byte DebugMode bodies: byte 0 is `0`, marker
`0x43`, version `2`, then operation, label index, and NUL-terminated ASCII text
from byte 12, with byte 127 required to be `0`. Operations: `1` set one of 132
label adapters (130 before 0.33.0; the 0.32.1 client ignores indexes 130 and
131), `2` show (the client then sends control `be`, "ready"), `3` hide, `4` set the token (four lowercase hex digits). Version-1 probe packets,
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
| Role or stance | `PlayerCompanionRoster.TrySetTactics` (roles include `crowdcontrol`; `cc` is accepted) |
| Build switch | `PlayerCompanionRoster.TrySelectBuild` (free, no trainer, resets and retrains) |
| Training mode | `TrySetManualTrainingMode` / `TrySetAutomaticTrainingMode` |
| Train one rank | Rechecks active companion, class trainer (`CanUseCompanionTrainer`), line, and rank; `SaveProgress` with queued retry |
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
4. The raid windows (`/raid 40`) still work, and the window fits at 800×600.

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
