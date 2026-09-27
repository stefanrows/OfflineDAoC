# Companion inventory: worn slots, one-step equip, soft manual choice

Date: 2026-09-27. Requested by Aaron. Status: design approved in chat, awaiting
spec review.

## Goal

The owner handles a persistent roster companion's gear almost like their own:

- sees what is worn in every slot and can inspect every item;
- equips an item straight from their own backpack, by drag or by click;
- the companion remembers a manual choice, but replaces it on its own when it
  earns a clearly better item.

Temporary `/spawn` helpers and autonomous world bots are out of scope.

## Constraints (fact-checked)

- The stock client shows a paper doll only for the player's own character.
  The `B` key cannot open a companion. No new client patch is part of this work.
- The companion bag is the stock house-vault window (`HouseVault`, client
  slots 150-249, 100 positions). Drag in/out and item delve already work there.
- 0.38.0 put worn slots at positions 51-69 of this window; 0.39.0 removed them
  as unintuitive (unlabelled, far down, two-step equip). This design answers
  that with worn slots on top, one-step equip, and a labelled legend in the
  Gear tab. Aaron chose to rebuild it knowingly.
- Custom8 Companion Manager (Gear tab) shows only text labels and invisible
  buttons: no icons, no drag and drop.
- Access rules stay: grouped with the owner, same region, within 400 units
  (`PlayerCompanionRoster.CanManageInventory`). Benched gear stays read-only.
- Save compatibility: no schema change; new state rides in the existing
  `SerializedEquipmentState` string. MINOR version bump.

## Part 1: bag window becomes the inventory window

`PersistentCompanionInventoryView` (reuse the 0.38.0 mapping from commit
463056e, reordered):

| Positions | Content |
| --- | --- |
| 1-19 | Worn slots in character-sheet order: helm, chest, arms, hands, legs, feet, cloak, neck, jewel, belt, left/right wrist, left/right ring, right hand, left hand, two-handed, ranged, mythical. Pairs stay adjacent (positions 11-12 and 13-14). |
| 20 | Empty separator. |
| 21-60 | Backpack (companion slots 40-79), still filtered by `OwnerTakeableBackpack`. |
| 61-100 | Unused. |

Worn items are always shown, including starter gear, so the slot view is
complete; the take rules below decide what may leave the companion.

Moves:

| From → to | Result |
| --- | --- |
| owner backpack → worn position | Transfer to the companion (flag P) and equip in one step. The replaced item goes to the companion's backpack. If the equip fails, the transfer is rolled back and the reason is told. |
| worn position → owner backpack | Unequip, then transfer to the owner when `CanReturnItemToOwner` allows it; otherwise it stays in the companion's backpack with a message. |
| companion backpack ↔ worn position | Equip / unequip (as 0.38.0). A ring or bracer takes the dropped side. |
| companion backpack ↔ owner backpack | Unchanged. |
| worn → worn | Refused with a hint (drag to a backpack first). |

Every equip needs a free companion backpack slot for displaced items; the
error names the blocker. Delve on any shown position works through the existing
`ActiveInventoryObject.GetClientInventory` fallback.

The Manager's `[Open bag]` becomes `[Open inventory]`; opening also selects the
Gear tab so the legend is visible.

## Part 2: Gear tab extensions

- Legend: each worn-slot line shows its window position (`1 Helm`, `2 Chest`, ...).
- Slot detail lists candidates from the companion's backpack and, new, from the
  owner's backpack (marked "yours"), best score first, only items the companion
  can use and the owner may transfer.
- `[Equip + lock]` becomes `[Equip]` (soft manual choice, see Part 3). Equipping
  an owner item uses the same one-step transfer + equip as the window.
- `[Info]` on the worn item and the selected candidate opens the native item
  info text window. Extract the item branch of `DetailDisplayHandler` (case 1,
  sent via `SendCustomTextWindow`) into a reusable builder; no duplicate text.
- A worn item chosen by the owner shows a `*` marker; a hard lock keeps its
  existing marker. `[Lock slot]` / `[Unlock slot]` stay.

## Part 3: soft manual choice

Slot state key `s:<slot>` gains a second value:

| Value | Set by | Auto-upgrade |
| --- | --- | --- |
| `L` (hard lock) | `[Lock slot]`; every lock already in a save | Never (unchanged) |
| `M` (manual choice) | Window drag equip, `[Equip]` | Only with a clear margin |
| none | Companion's own choice | Any improvement (unchanged) |

- Clear margin: improvement must exceed `max(MinimumEquipmentUpgrade (8),
  5% of EquipmentValue(worn item))`, using the existing `IsEquipmentUpgrade`
  total (worn plus displaced weapons).
- Candidates stay as today: only items the companion earned (flag E) are
  auto-equipped; items the owner placed in its backpack (P) never are.
- Paired slots: prefer an unmarked partner slot; an `M` slot is replaced only
  with the margin; `L` slots never.
- Starter-gear refresh treats `M` like `L` (never overrides a manual choice).
- When an `M` item is replaced: it goes to the companion's backpack, keeps its
  P flag (owner can take it back), the slot mark is cleared, and the owner gets
  one chat line: "<Companion> swapped your <old> for the better <new>."
- Unequip clears `M` and `L` as today.
- Older servers read `M` as locked (key present), so a downgrade is safe.

API sketch: `PlayerCompanionRoster.GetEquipmentSlotMark(record, slot)` returning
None/Manual/Locked; `IsEquipmentSlotLocked` keeps meaning hard lock only; the
manual-equip path in `GameBot` writes `M`; `TryGetEquipmentUpgrade` raises the
minimum for `M` slots.

## Testing

Unit tests:

- Window mapping: worn positions 1-19, backpack 21-60, no overlap, pairs adjacent.
- Move routing table above, including rollback when the equip fails and the
  starter-gear stays-with-companion case.
- Slot marks: `L` blocks, `M` needs the margin (just below / just above), none
  accepts any gain, legacy `L` data stays hard, starter refresh respects `M`.
- Owner candidates in the Gear tab: usable and transferable only.

Real-client checks (owner): vault grid columns and pair adjacency, drag from
own backpack onto a worn position, `[Info]` window, auto-swap chat line after a
clearly better drop.

## Out of scope

Paper-doll or icon window via client patch; temporary helpers; autonomous bots;
changing which items count as earned.
