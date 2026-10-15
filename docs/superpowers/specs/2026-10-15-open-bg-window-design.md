# Open BG window: own client window (design)

Date 2026-10-15. Owner decision: option "own window" over sharing Custom8,
purpose **observe and steer** while playing. Status: spec for review; no code.
Builds on the merged `obg-window` branch (open BG manager in Custom8, TASKS 116)
and the window techniques of 0.251.0 (COMPANIONS, Custom7) and 0.273.0-0.276.0
(COMPANION GEAR, Custom6). Reverse-engineering sources:
`docs/COMPANION_PAPERDOLL_RE.md`, `docs/CLIENT_UI_XML_REFERENCE.md`,
`docs/COMPANION_MANAGER_INTEGRATION.md`.

## 1. Intent

The owner watches and steers the permanently open Thidranki (region 252) from
inside the game: where the crews are, what the phase director does, who holds
keep 11, and a few hands-on levers. The window is not a player feature and not
an admin console for the whole server.

Success looks like:

- `/bgsetup` (alias `/openbg`) opens a window titled **OPEN BG**, not "Companion
  Manager", and it can stay open next to the Companion Manager, COMPANIONS and
  COMPANION GEAR windows.
- Page **Lage** shows phase, raid, keep 11 owner, achieved world speed and the
  crew per realm and unit, refreshed live.
- Page **Steuern** changes each realm's crew targets with + and - and relieves a
  unit by hand, as the Custom8 version already does on the server side.
- A client without the new stage still gets the same information as chat text
  (`/bgsetup text`), nothing breaks.

Out of scope: any change to how the crew, phases or raids behave (those are the
separate bug strand: allied claim flip, Midgard pool, bug 154), player-facing
BG sign-up, a second world mode.

## 2. Approach (chosen)

**A: own window on Custom5** (window id `0x72`, the ShowCustomN switch pushes
`0x6D + N`), built like the gear window. Custom5 is unused by the stock UI and
by every stage so far (raid windows use Custom9/10, manager 8, COMPANIONS 7,
gear 6).

Rejected: sharing Custom8 (one window at a time, title and layout tied to the
manager, every later manager change touches the BG view too) and a chat-only
view (nothing to click, no live picture).

## 3. Server side

- `OpenBgWindow` (new, replaces `OpenBgManager` + `OpenBgManagerSession`'s
  transport use): session per player, rendering to labels, click handling.
  The data and the actions stay as in the merged branch: live snapshot from
  `OpenBattlegroundStatus`, `open_bg_units_<realm>` targets, manual relief.
- Transport: the shared `CompanionWindowTransport` (fixed 128-byte DebugMode
  bodies, label diffing, token, guidance fallback). `CompanionWindowOwner` and
  its Claim/Release routing go away: the BG window no longer competes for
  Custom8, so `PlayerCompanionCommands` routes by control range only.
- Fallback: a client that never answers the ready control gets the existing
  one-line guidance and `/bgsetup text`.
- Non-GM players: the window is read-only for them (Lage page); Steuern and the
  relief action stay behind the existing GM check on the server, whatever the
  client sends.

## 4. Client side (stage 0.2xx, one Companion Manager stage)

- `build_companion_manager_client.py` gains `open_bg_window()` and a label
  range after the gear labels (gear ends at adapter 619). New operations for
  show, hide and a ready answer (the next free numbers after 9) and a ready
  control, following the gear window's pattern. A client without the stage
  ignores the operations.
- Click IDs: the window uses its own `OnClickEvent` range (free above the
  gear's 0x900-0x93A, proposed 0xA00 upward) and control values the existing
  command sender can carry. The control value space of the shared sender
  (two hex digits, `ControlLimit` 0xE0) is nearly used; the plan must either
  extend it for the new range or give the BG window its own sender prefix.
  Decide in the plan after reading the sender hook.
- `game.dll` stays hash guarded: the stage is built only from the verified
  raid baseline (`67dcf68a...`) and the installer upgrades the 0.276.0 stage
  in place, with backup and restore, like 0.272.0 to 0.276.0.
- `Install-CompanionManager.ps1` also writes `custom5_window.xml` (both skins)
  and the `uimain.xml` include; the release pack ships the new stage and keeps
  accepting the 0.276.0 files as upgrade sources.

## 5. Layout

Two pages with one tab strip, stock templates only (as the gear window):

| Page | Content |
| --- | --- |
| Lage | header: phase, raid, keep 11 owner, world speed, players inside; three realm blocks with 8-man, small and solo units present/target; below, the crew bots by unit (name, level, class, role or state) with scrolling |
| Steuern | per realm: crew targets 8-man / small / solos with + and -; unit list with a [Relieve] click; last reply line |

Row counts and widths are fixed by the label budget; the plan fixes the exact
number of adapters.

## 6. Testing and acceptance

- Offline: the builder's emulation test gains the new window (ops, labels,
  clicks, XML sockets, unchanged other windows); server unit tests for session,
  GM gate, routing by control range; launcher untouched.
- Regression: COMPANIONS, gear and manager clicks keep working with the BG
  window open (the click-routing bug fixed on the merged branch becomes
  structurally impossible).
- Real client (owner, pending after install): window opens with its own title,
  both pages render, + and - change a target live, relief works, a second
  window (Manager or COMPANIONS) opens beside it, an unpatched client shows the
  chat fallback.

## 7. Impact and risks

- One more client installation for the owner (stage upgrade, backup kept).
- A new binary patch region: only emulation proves it before the real client;
  the 0.273.0 probe needed one owner round to fix findings, expect the same.
- The Custom8 open BG view is removed when the window ships; until then the
  merged branch stays usable as a server-only version.
- Later changes to the BG window no longer touch the Companion Manager.
