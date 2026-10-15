# Companion paperdoll: game.dll analysis

Goal (owner, 2026-10-15, TASKS 129): drag and drop items into a companion's
character window the way the player does on his own paperdoll, instead of
the House Vault list and the Gear tab text.

**Analysis only.** Static reading of the raid baseline `game.dll`
(SHA-256 `67dcf68a37b95a93946a943b99d5e19b4a03e08cd6469275e25c7b909de21e99`)
with REA 6.4.0 / Ghidra 12.1.4, capstone and a Unicorn call of one helper.
No client, server or file in the installation was changed or run. Nothing
here is real-client proof; the probe in section 5 exists to get it.
Addresses are virtual addresses in the 0x400000 image.

## 1. Result

The player's paperdoll cannot be reused: it is a hard-coded page (mode 1) of
the huge stats window at `[ui+0x104]` (object larger than 0x5BCE0 bytes,
skin bitmaps `pperdoll.bmp`, `sacks.bmp`), not XML, and it reads only the
player's own slots. Cloning it is out of scale.

A companion paperdoll in its own XML window **is feasible** with a small
`game.dll` addition that copies a stock pattern: the quiver window. Its four
slots are plain XML (`IconDef` with a scalar icon adapter plus an
`InvisibleButtonDef`), and its native click handler does exactly what a
companion slot needs:

- item on the cursor: send the stock move-item packet to the slot;
- empty cursor and the slot holds an item: pick the item up (start a drag)
  so the stock inventory drop moves it back.

The server side already exists: `PersistentCompanionInventoryView` maps
house-vault client slots 150-168 to the 19 worn slots and 170-209 to the
backpack, and handles every move through `PersistentCompanionGear`
(`TryGiveAndEquip`, `TryUnequipToOwner`, `TryEquip`, `TryUnequip`).

## 2. Evidence

### 2.1 Move-item packet (client to server)

| Item | Address | Finding |
|---|---|---|
| SendMoveItem | `0x42A976` | cdecl `(to, from, count)`; writes opcode `0xDD` through the TCP sender `0x4281DF`. The server's `PlayerMoveItemRequestHandler` reads `to`, `from`, `count` in that order. |
| Callers | 15 | Paperdoll/bag drop (`0x408151`), quiver (`0x4068F6`), give to target (`0x4067E5`, `to = 1000 + objectId`), house vault (`0x40B764`, `to = 150 + row`), shift-drop (`0x4C3148`, `to = 600/601/602/603`). |

### 2.2 Client item array

One array holds every client slot, house vault included:
`record(s) = 0xF96FB4 + s * 0x1D4`.

| Field | Offset | Evidence |
|---|---|---|
| template index (int) | `+0x00` | `0x4214FF`, `0x535183`: icon lookup |
| count (int) | `+0x3C` | `0x40B764` reads `0xF96FF0 + i*0x1D4`; `0xFA8228 = 0xF96FF0 + 150*0x1D4` is the house-vault count |
| name (char[64]) | `+0x48` | `0x4214FF` prints it; empty name = empty slot |
| icon id | — | `*(short *)(0x106A7F2 + template * 0xC4)` |

The server's slot-only refresh (`eInventoryWindowType.Update`, bug 44)
already updates slots 150+ in the client, so the data is present without the
stock vault window.

### 2.3 Icons: scalar adapters

`quiver_icon%d` is registered with the scalar registrar `0x4B6BC0`
(stdcall registry, slot*, name, max, initial) at `0x4DC5DD`; the getter
`0x4E03AF` returns `[registry + 0x1194 + 4*i]`. The refresh `0x535183`
writes the icon id with the scalar setter `0x524370` (ECX = adapter, float),
or `-1` when the slot is empty. An `IconDef` with `AdapterName` therefore
draws whatever icon number its scalar adapter holds. The Companion Manager
patch already registers adapters at init (hook `0x4DA938`), so 59 more
scalar adapters (`cmgr_icon_0` ... ) are the same kind of change.

### 2.4 Drag state

The UI manager is `[0x104C2BC]`.

| Item | Address | Finding |
|---|---|---|
| Drag record | `[ui+0x168]` | `+0x34` active flag, `+0x80` source kind, `+0x84` source index (-1 none), `+0x88` flag |
| Drag count | `[ui+0x16C]` | 0 after BeginDrag |
| BeginDrag | `0x4C34CC` | `(ui, kind, index, flag, x, y)`, EAX = cursor icon id |
| EndDrag | `0x4C2D72` | EAX = `0x20` clears the drag record; other EAX values clear one window's pending event |
| Mouse buttons | `0x4C362A` | button code 1 (left): windows in the list at `[ui+0x36F78]` get `0x4DA04B` -> `0x53D7AF`, the control click, while the drag is still set; shift + left: `0x4C2F89`. Button code 2 (right): with a drag active and `[ui+0x700]` 0 the drag is cancelled; otherwise the context menu (`main_chat_context`); shift + right: `0x4C3148`, the stock shift-right-click that sends an item to the open vault or the paperdoll (600-603). Corrected 2026-10-15: the first reading took code 2 for left-up. |
| `[ui+0x700]` | `0x47FC7C` | set to 1 by stock drop code right before `SendMoveItem` |
| Cursor position | `0x99DFE8` / `0x99DFEC` | UI coordinates, used by stock BeginDrag callers |

Source kinds, mapped by `0x47F747` (all 512 values emulated):

| Kind | Meaning | Index | How the stock inventory drop reads it (`0x408151`, event type 5) |
|---|---|---|---|
| 9 | player paperdoll and bag | hotspot: 0-3, 10-27, 40-79 | slot = index + 10 for 0-3, + 11 for 10-27, index for 40-79 |
| `0x5B` | house vault / consignment list | list row | slot = selected row + 150, count from the array |
| `0x39` | player vault | row | slot = vault base `[ui+0x2524]` + row |
| `0x66` | quiver | **real client slot** (14-17) | slot = index, count 0 |

Kind `0x66` is the useful one: a drag started with kind `0x66` and index
`150 + n` is dropped by the stock inventory code as `move(dest, 150 + n, 0)`.

### 2.5 The quiver pattern

The quiver window's control handler (`0x534F40`-`0x53501A`) for its
`QuiverAdd0`-`3` buttons (events `0xB4`-`0xB7`):

```text
drag = [ui+0x168]
if drag and drag.index != -1 and drag.kind in (9, 0x66):
    PostUIEvent(window, control, 0xB4+i, 0, drag.index)   # -> 0x4068F6: move(14+i, src, count)
elif quiver slot i has an icon:
    BeginDrag(ui, 0x66, 14+i, 0, x, y)  with EAX = icon
```

### 2.6 Event path of our own buttons

- A generic XML button click (`0x53AFF3` -> `0x53F330`) posts
  `PostUIEvent(controlId, OnClickEvent, 0, 0)` (`0x4E16D6` / `0x4E1719`).
- `PostUIEvent` first calls the immediate dispatcher `0x4E04DE` with
  EAX = the 5-dword record `{?, controlId, eventId, p1, p2}`; only unhandled
  events are queued for the pump `0x408EFD`.
- The raid and Companion Manager hook sits at `0x4E04DE`, so it runs inside
  the click. If the click arrives while a drag is set (risk R1), the hook can
  read the drag source itself, as every stock window class does
  (`0x5390B7` give-to-target, `0x531AC3`, `0x5274C4`, the quiver).

Free event range: stock IDs end near `0x246`, the raid uses `0x600`-`0x64F`,
the manager `0x700`-`0x7DF`, client helpers `0x7E0`/`0x7E1`; `0x800`-`0x8DF`
is reserved by the lever probe idea. `0x900`-`0x93F` is unused.

### 2.7 Window

`Custom0`-`Custom19` exist; window id = `0x6D + N`. Custom7 (`0x74`) is the
COMPANIONS window, Custom8 (`0x75`) the manager, Custom9/10 the raid.
**Custom6 (`0x73`) is unused** by the installed UI. The manager already shows
windows with `[manager]->vtbl[2](window id, visible)`.

## 3. Design

### 3.1 Client (manager stage, new protocol operations)

1. `custom6_window.xml`: a paperdoll layout of 19 worn slots (character-sheet
   order and the paperdoll's positions) and a 40-slot backpack grid. Each
   slot is an `ImageAreaDef` socket, an `IconDef` (`plain_icon_small`,
   `AdapterName` `cmgr_icon_<n>`), and an `InvisibleButtonDef` with
   `OnClickEvent` `<2304 + n>` (`0x900 + n`). Slot names and the companion
   name are server labels.
2. Init hook: register 59 scalar adapters `cmgr_icon_0`-`58`.
3. Event hook, events `0x900`-`0x93A`, slot `s = 150 + n` for worn
   (n 0-18) and `s = 170 + (n - 19)` for the backpack:
   - drag active: map the source like `0x408151` (kind 9 hotspot map; kind
     `0x66` index as is; kind `0x5B` row + 150); `SendMoveItem(s, src,
     [ui+0x16C])`; ignore other kinds.
   - no drag and the slot holds an item: `BeginDrag(ui, 0x66, s, 0,
     [0x99DFE8], [0x99DFEC])` with EAX = the slot's icon.
4. New DebugMode operation "paperdoll refresh": for each slot read the
   record (empty name or count < 1 -> `-1`, else the icon id) and set the
   adapter with `0x524370`. Operations to show and hide Custom6.

### 3.2 Server

- `[Open inventory]` (and a new `/companions gear` window command) open the
  view as today but refresh with `Update` instead of `HouseVault`, so the
  stock vault window stays closed, then show Custom6 and send the refresh
  operation. Every `Refresh` sends the refresh operation after the slot
  update; TCP order keeps it after the item data.
- Moves need no change: `CanHandleMove` and `MoveItem` already cover owner
  bag <-> worn, owner bag <-> companion bag, worn <-> companion bag, and refuse
  the rest with a message.
- Labels: companion name, slot captions, the reply to the last move.

### 3.3 Not covered by this design

- Delve (item info): `OnRightClickEvent` is never read on icons and
  `OnShiftClickEvent` has no runtime reader; info stays the manager's
  **[Info]**.
- Drag from the paperdoll onto the stock House Vault list: the vault drop
  (`0x40B764`) reads its own list state; untested and not needed once the
  backpack grid exists.
- The bag cannot use the stock paperdoll's silhouettes; slot captions or
  socket images replace them.

## 4. Risks

| # | Risk | Evidence so far | Probe check |
|---|---|---|---|
| R1 | A drop on an `InvisibleButtonDef` in a generic Custom window may not fire its click while the drag is still set. | The quiver (a native class over the same XML controls) reads the drag in its click handler. A left click on a generic window reaches its controls through `0x4DA04B` with the drag set; only a right click cancels a drag. This fits DAoC's click-to-pick-up, click-to-drop model. Likely, not proven. | Click a bag item, then a probe slot: status `D 09:00xx>096` and the server's move reply. Also try press-hold-release. |
| R2 | An `IconDef` in a Custom window may not draw a scalar icon adapter. | Same controls and adapter kind as the quiver window. | A probe slot shows the item's icon; `-1` draws nothing. |
| R3 | BeginDrag from the event hook (no window `this`) may misplace the cursor icon or fail. | Stock callers pass the cursor globals; `0x4CD925` calls it outside a window class. | Click a filled probe slot, drop on the bag: `move(40+k, 150)`. |
| R4 | Pick-up-by-click may end at the same mouse-up. | The quiver uses the same click to start its drag. | As R3; if it ends at once, start the drag on press instead. |

## 5. Gate: one probe, then the window

As for the 0.32 manager gate: one isolated probe stage built on the current
manager stage (0.272.0, `c938e6b5…`), emulated offline first (adapter
registration, the event hook's move and BeginDrag calls through Unicorn),
then one owner check in the real client:

1. Custom6 with four slots (two worn, two bag) and a status label.
2. R1-R4 above, plus: the stock vault window stays closed, the raid
   windows, the manager and the COMPANIONS window still work.

If R1 fails, record the observation first: whether the status line changed
at all (no change = the click never reached the hook; `X` = it did, with a
drag kind the probe does not accept). The full window follows only after the
probe passes.

R5 (for the full window): whether the client keeps house-vault slots 150+
without the stock vault window. `/companions gearprobe <name> plain` opens the
view with a plain slot update instead of the house-vault window type.

## 6. Probe stage (0.273.0)

| File | Role |
|---|---|
| `source/server/tools/probe_companion_paperdoll.py` | Builds the probe stage from the 0.272.0 manager stage (fails closed on other hashes); deterministic |
| `source/server/tools/test_companion_paperdoll_probe.py` | Unicorn emulation and static checks (21 PASS lines); not acceptance |
| `tools/dev/Install-CompanionPaperdollProbe.ps1` | Installs the probe over an installed 0.272.0 manager, dry run by default, with backup and `-RestoreBackup` |
| `CompanionPaperdollProbe.cs` | `/companions gearprobe [name] [plain]` and `/companions gearprobe off` |

The `.cpdl` section adds four scalar adapters `cpdl_icon_0`-`3` and the text
adapter `cpdl_text`, a packet filter for operations 7 (refresh icons), 8
(show Custom6 and refresh) and 9 (hide), and an event handler for
`0x900`-`0x903` (OnClickEvent 2304-2307). Everything else reaches the
manager's blocks unchanged; the manager's code is byte-identical.

```bash
python3 source/server/tools/probe_companion_paperdoll.py \
  --input-stage /mnt/d/Games/OfflineDAoC-dev/companion-manager-stage-0.272.0 \
  --output /mnt/d/Games/OfflineDAoC-dev/companion-paperdoll-probe-0.273.0
python3 source/server/tools/test_companion_paperdoll_probe.py \
  --stage /mnt/d/Games/OfflineDAoC-dev/companion-paperdoll-probe-0.273.0 \
  --input-stage /mnt/d/Games/OfflineDAoC-dev/companion-manager-stage-0.272.0
```

| Output | SHA-256 |
|---|---|
| game.dll | `ff5fb5238fbf473c10503887e66ad1a8679d72342445bfa415e889ded6e5a022` |
| uimain.xml (0.272.0 plus the Custom6 include) | `cef698f354bd3d1c2e7c40f794dac38d0f172a5889d7b8996f1d41c131a1328c` |
| custom6_window.xml (both skins) | `60d41e0817463f6a17a1e2a053e960ae4d8f4f4b75b4aaf5e3f740d3dd626cd5` |

The probe window's status line reports every hook action:

| Status | Meaning |
|---|---|
| `R 01a2 ffff ffff 038d` | icons refreshed: helm, chest, bag 21, bag 22 (`ffff` = empty) |
| `D 09:0028>096` | drop: drag kind 9 (your bag/paperdoll), source slot 0x28 = 40, sent to slot 0x96 = 150 (helm) |
| `D 66:0096>0aa` | a picked-up companion item dropped on another probe slot |
| `C 096` | a picked-up item dropped on its own slot: put back, nothing sent |
| `P 096 i=01a2` | pick-up: a kind-0x66 drag of slot 150 started with icon 0x1A2 |
| `E 097` | click on an empty slot without an item on the cursor |
| `X 5b:0003` | a drag kind or index the probe does not accept; nothing sent |

### Real-client checklist (owner)

Install with the game, server and launcher closed (dry run first):

```powershell
.\tools\dev\Install-CompanionPaperdollProbe.ps1 -InstallRoot D:\Games\OfflineDAoC `
  -Stage D:\Games\OfflineDAoC-dev\companion-paperdoll-probe-0.273.0
# then the same line with -Apply; restore later with -RestoreBackup <backup folder>
```

1. With a companion in your group: `/companions gearprobe` (or
   `/companions gearprobe <name>`). The probe window and the House Vault
   window open; the status line reads `R ...`. **R2**: the helm and chest
   icons match the companion's gear; empty slots draw nothing.
2. **R1**: click a helm in your backpack, then the probe's Helm slot. Note the
   status line (`D 09:00xx>096`), the chat reply and whether the companion
   wears it. Repeat with press-hold-release instead of two clicks.
3. **R3/R4**: click the probe's filled Helm slot with an empty cursor
   (`P 096 ...`): does the icon stay on the cursor? Click an empty backpack
   slot: the helm returns to you (chat reply).
4. Bag slots: drop one of your items on Bag 21 (gives it to the companion);
   pick it up from Bag 21 and drop it on Helm or on your bag.
5. **R5**: `/companions gearprobe off`, then `/companions gearprobe <name>
   plain`: the House Vault window must not open; do the icons still show
   and do drops still work?
6. The Companion Manager, the COMPANIONS window and `/raid 40` still work.

Record each status line and chat reply. Restore with `-RestoreBackup`
before the next manager client update.

## 7. Real-client result (2026-10-15, probe 0.273.0)

Run in the installed game by the agent (window capture with `PrintWindow`,
input with `SendInput`), owner present. Brakka (Midgard Shaman) in the group.

| Check | Observation | Result |
|---|---|---|
| R2 icons | `R 0065 006a ffff 0070`: helm, hauberk and sleeves icons drawn; the empty-looking Bag 21 shows only its socket | Pass |
| R1 drop | Willful Mace clicked in the bag (sticks to the cursor), then Bag 21: "Transferred Willful Mace to Brakka" | Pass |
| R3/R4 pick-up | Click on the Helm slot: `P 096 i=0065`, the helm stays on the cursor; a click on Bag 21 unequipped it ("moved to Brakka's backpack") | Pass |
| R5 plain | With the House Vault window closed, `gearprobe plain` shows the icons; a pick-up from Bag 22 and a drop on Bag 21 reached the server | Pass |
| Show again | `gearprobe off` then `gearprobe` / `plain` shows Custom6 again | Pass |
| Other windows | Companion Manager, COMPANIONS window, House Vault drag still work | Pass |

Findings for the full window (server side, not client):

1. The view hides protected companion gear (starter items) but those items
   still occupy their backpack positions: Bag 21 looked empty yet held the
   Ancestral Ring, and the drop was refused ("That slot holds gear that stays
   with the companion"). The paperdoll must show every backpack item, marking
   the ones that cannot leave.
2. Giving an item to the companion and unequipping both use the first free
   backpack slot (`TryTransferItem`, `TryUnequip` with `FindFirstEmptySlot`),
   not the slot the item was dropped on. The mace landed in bag 35 and the
   helm in bag 37.
3. The companion's equipment AI refilled the empty helm slot at once (Speedy
   Chain Helm), and the manager's **[Equip]** then refused the original
   Matterbender Chain Helm (starter gear) with "not a legal choice, its slot
   is protected, or the companion is busy". Investigated and fixed in
   source 0.274.0, two causes:
   - **Cross-realm usability.** Brakka is a Midgard Shaman with an Albion
     owner. A companion's `GameNPC.Realm` follows its owner (its `BotBrain`
     is an `IControlledBrain`), and the starter gear generated for it
     carries that realm: the Matterbender helm is `Realm` 1 (Albion), and
     so is every non-tradable starter item of a cross-realm companion in
     the save. `CheckAbilityToUseItem` then judged the helm's armor by its
     item realm (`AlbArmor`, which a Shaman lacks) and refused it. Earned
     Midgard loot failed the realm filter instead (item realm 2 against
     the borrowed realm 1). The rule now uses the bot's class realm
     (`GameBot.ClassRealm`) for armor and weapon skills when the bot has
     an owner, and accepts items of either realm.
   - **The refill was not the AI.** The Speedy Chain Helm was created by
     `PlayerCompanionRoster.UpgradeStarterEquipment` 0.2 s after the
     unequip, because an empty armor slot without a mark gets a fresh
     starter piece. `TryUnequip` now marks the emptied slot as the owner's
     choice (`Manual`), so the starter refresh leaves it empty while
     earned gear may still fill it.
4. In the stock House Vault list, an item is picked up by clicking its icon,
   not its name.
