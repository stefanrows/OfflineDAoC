"""Stage the Companion Manager Custom8 extension from the verified native-raid client.

The builder writes a fresh staging directory and never modifies the input
client. It accepts only the verified raid-patched Windows x86 game.dll.

Protocol version 3 (16-bit label index in bytes 4-5, because the layout has more than
256 label adapters; version 2 used one byte) replaces the failed probe paths:

* Server to client: fixed 128-byte DebugMode bodies with marker 0x43 update
  registered label adapters, show or hide Custom8, and set the view token.
* Client to server: each click area's OnClickEvent is a decimal event ID
  (0x700 + control). The stock XML parser maps OnClickEvent through 0x4EA06F,
  which returns -1 for unknown names but accepts a leading-digit value via
  atoi; names such as "CompMgr30" therefore never fire. The event handler
  hook calls the client's own slash-command sender (the path used by its
  context menu for /talk and /loco) with ``&companions ui <token> <control>``.
  No new packet opcode is needed.
* Search: the Search link opens the ordinary chat line prefilled with
  ``/companions find `` using the same calls as the chat window's own
  channel-prefix menu. No native edit box is used.
* COMPANIONS window (Custom7, window 0x74): operations 5 and 6 show and hide it. Its
  tiles and row clicks send controls 0xA8-0xBF through the same command path, gated by
  their own flag, so they work while the manager is closed. Its member rows are a full
  group window built from the stock group adapters (group_name/color/level/class/
  health/power/endurance) with SelectGroupMember<i> clicks; the stance column and the
  leader mark are server labels, and the Invite tile is the stock GroupInvite event.
* COMPANION GEAR window (Custom6, window 0x73, TASKS 129): 19 worn slots and the 40-slot backpack of one
  companion, shown from the house-vault client slots 150-168 and 170-209 that
  PersistentCompanionInventoryView fills. Each slot is an IconDef on a scalar icon adapter (cmgr_icon_<n>) and a
  click area with OnClickEvent 0x900 + n. The click copies the stock quiver window (0x534F40): an item on the
  cursor is sent to the slot with the stock move-item packet; an empty cursor on a filled slot starts a kind-0x66
  drag whose index is the client slot. Operations 7, 8 and 9 refresh the icons from the client's item array, show
  the window (then refresh it and send the gear ready control), and hide it. The title, the backpack heading, the
  worn captions, the last reply and a "*" mark per slot are server labels. The real-client behaviour of this
  pattern was proven by the 0.273.0 probe (docs/COMPANION_PAPERDOLL_RE.md sections 6-8).
* OPEN BG window (Custom5, window 0x72): operations 10 and 11 show and hide it (show also sends the ready control
  0xFF), operation 12 sets its own view token. Its clicks use OnClickEvent 0xA00 + control; the event handler sends
  them as ``&bgsetup ui <token> <control>`` from a separate command buffer through a copy of the manager's sender, gated
  by the window's own flag, so the manager's and the OPEN BG window's tokens never invalidate each other. Labels
  620-984 use operation 1.

The layout below is the single source for label and control numbers. The C#
constants in CompanionManagerProtocol.cs are checked against it by
test_companion_manager_client.py.
"""

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import xml.etree.ElementTree as ET

import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from keystone import Ks, KS_ARCH_X86, KS_MODE_32


RAID_SHA256 = "67dcf68a37b95a93946a943b99d5e19b4a03e08cd6469275e25c7b909de21e99"
RAID_INIT = 0x247E000
RAID_PACKET = 0x2482000
RAID_EVENT_HANDLER = 0x2485000
HOOKS = (
    (0x4DA938, bytes.fromhex("e9 c3 36 fa 01 90 90 90"), 0x0000),
    (0x411201, bytes.fromhex("e9 fa 0d 07 02 90 90 90"), 0x1000),
    (0x4E04DE, bytes.fromhex("e9 1d 4b fa 01 90 90 90 90 90"), 0x1C00),
)

# Native client entry points used by the stock client and the raid extension.
REGISTER_TEXT_ADAPTER = 0x4B6C84   # stdcall(registry, slot*, name)
SET_ADAPTER_TEXT = 0x524436        # stdcall(text), EBX = adapter
WINDOW_MANAGER = 0x104C2BC         # [manager]->vtbl[2](window id, visible)
SEND_SLASH_COMMAND = 0x42BC08      # cdecl(const char* "&command ...")
CHAT_INPUT_STATE = 0x161CE68       # ESI for SET_CHAT_MODE; EDI = mode
SET_CHAT_MODE = 0x40D56C
SET_CHAT_TEXT = 0x40D5D4           # cdecl(const char* text)
CHAT_HISTORY_INDEX = 0x9A5EC4
CHAT_SCROLL_INDEX = 0x10498D8
PACKET_DONE = 0x4113AD
# Stock OnClickEvent mapper used by ButtonDef and InvisibleButtonDef parsers.
CLICK_EVENT_MAPPER = 0x4EA06F
# Gear window natives (docs/COMPANION_PAPERDOLL_RE.md section 2). Each return size is checked by verify_natives().
REGISTER_SCALAR_ADAPTER = 0x4B6BC0  # stdcall(registry, slot*, name, maxBits, initialBits)
SET_SCALAR_VALUE = 0x524370        # ECX = scalar object, stdcall(float)
SEND_MOVE_ITEM = 0x42A976          # cdecl(to, from, count): opcode 0xDD
BEGIN_DRAG = 0x4C34CC              # stdcall(ui, kind, index, flag, x, y), EAX = cursor icon
END_DRAG = 0x47FC63                # no arguments: EAX = 0x20, ECX = ui, call 0x4C2D72
NATIVE_RETURN_BYTES = {
    REGISTER_TEXT_ADAPTER: 12,
    REGISTER_SCALAR_ADAPTER: 20,
    SET_SCALAR_VALUE: 4,
    SET_ADAPTER_TEXT: 4,
    SEND_MOVE_ITEM: 0,
    BEGIN_DRAG: 0x18,
    END_DRAG: 0,
}
DRAG_RECORD, DRAG_COUNT = 0x168, 0x16C   # [ui+0x168]: +0x80 kind, +0x84 index (-1 none); [ui+0x16C] count
DRAG_KIND, DRAG_INDEX = 0x80, 0x84
CURSOR_X, CURSOR_Y = 0x99DFE8, 0x99DFEC  # cursor in UI coordinates (stock BeginDrag callers)
ITEM_ARRAY, ITEM_STRIDE = 0xF96FB4, 0x1D4  # record(s) = ITEM_ARRAY + s * ITEM_STRIDE
ITEM_TEMPLATE, ITEM_NAME = 0x00, 0x48      # template index (int), name (char[64]; empty = no item)
ICON_TABLE, ICON_STRIDE = 0x106A7F2, 0xC4  # *(short *)(ICON_TABLE + template * ICON_STRIDE)
TEMPLATE_LIMIT = 0x10000                   # 0x106A7F2 + 0xFFFF * 0xC4 stays inside .data
KIND_INVENTORY, KIND_QUIVER = 9, 0x66      # player paperdoll/bag (hotspot index); quiver (client slot index)

CUSTOM8_WINDOW = 0x75
MARKER = 0x43
PROTOCOL_VERSION = 3
BODY_SIZE = 128
TEXT_OFFSET = 12
EVENT_BASE = 0x700
# 0x700 + control must stay below 0x7E0, which the client's own helpers return.
CONTROL_LIMIT = 0xE0
SEARCH_CONTROL = 0xD0
READY_CONTROL = 0xDD
COMMAND_PREFIX = "&companions ui "
SEARCH_PREFIX = "/companions find "
OP_LABEL, OP_SHOW, OP_HIDE, OP_TOKEN = 1, 2, 3, 4

# COMPANIONS window (Custom7). Operations 5 and 6 show and hide it; its clicks use the manager's
# event path with controls 0xA8-0xBF, which the manager never uses. Protocol version stays 3.
BAR_WINDOW = 0x74                      # Custom7: the ShowCustomN switch pushes 0x6D + N (0x4E0B4A: 6a 01 6a 74)
OP_BAR_SHOW, OP_BAR_HIDE = 5, 6
BAR_CONTROL_FIRST, BAR_CONTROL_LIMIT, BAR_READY_CONTROL = 0xA8, 0xC0, 0xBF
# Server tiles in control order (BAR_CONTROL_FIRST + index): two rows of five stance and camp tiles, then the
# group row (after the native Invite tile): Kick, Leader, Leave, Grind.
BAR_TILE_NAMES = ("Aggressive", "Defensive", "Passive", "Assist", "Default", "Follow", "Stay", "PetPull", "Pull",
                  "Manager", "Kick", "Leader", "Leave", "Grind")
BAR_TILES = len(BAR_TILE_NAMES)
BAR_TILES_PER_ROW = 5
# Tiles that fire a stock client event instead of a server control: (name, caption, event, row, column).
BAR_NATIVE_TILES = (("Invite", "Invite", "GroupInvite", 2, 0),)
BAR_SLOTS = 8
# A row's stance click (cycle the companion's saved stance) is control BAR_CONTROL_SLOT_BASE + slot.
BAR_CONTROL_SLOT_BASE = BAR_CONTROL_FIRST + BAR_TILES
assert BAR_CONTROL_SLOT_BASE + BAR_SLOTS < BAR_READY_CONTROL
BAR_TILE_STRIDE = 4
# Per member row: four stance colours, then the leader mark.
BAR_SLOT_STRIDE = 5
BAR_SLOT_LEADER_OFFSET = 4
# Offsets inside a tile's four labels: inactive text, active text, disabled text, underline.
BAR_TILE_INACTIVE_OFFSET, BAR_TILE_ACTIVE_OFFSET, BAR_TILE_DISABLED_OFFSET, BAR_TILE_UNDERLINE_OFFSET = 0, 1, 2, 3
BAR_WIDTH, BAR_HEIGHT = 440, 312
BAR_TILE_W, BAR_TILE_H, BAR_TILE_PITCH_X, BAR_TILE_PITCH_Y, BAR_TILE_TOP = 80, 22, 84, 26, 22
BAR_SLOT_TOP, BAR_SLOT_PITCH = 128, 22
BAR_TILE_TEMPLATE = "tool_tip_background"
# Member row columns (x, width): power %, endurance %, health bar (name over it, health % at its right end),
# class, level, stance. The leader mark sits just left of the bar.
# "100" is 24 px in arial14, so the percent columns are 24 wide; the mark has its own 10 px gap before the bar.
BAR_COLUMN_POWER, BAR_COLUMN_ENDURANCE, BAR_COLUMN_LEADER = (8, 24), (34, 24), (60, 10)
BAR_COLUMN_BAR, BAR_COLUMN_NAME, BAR_COLUMN_HEALTH = (70, 168), (74, 130), (208, 26)
BAR_COLUMN_CLASS, BAR_COLUMN_LEVEL, BAR_COLUMN_STANCE = (240, 86), (328, 26), (356, 78)
BAR_WIDTH_STATUS, BAR_WIDTH_MESSAGE, BAR_WIDTH_SLOT = 290, 420, BAR_COLUMN_STANCE[1]
# Stance colours in eCompanionEngagementMode order: Aggressive, Defensive, Passive, AssistTrain.
STANCE_COLORS = ((235, 120, 100), (120, 170, 240), (165, 165, 165), (255, 210, 90))
POWER_COLOR, ENDURANCE_COLOR, HEALTH_COLOR = (204, 204, 0), (0, 204, 0), (255, 255, 255)

# Window geometry. The text font is the client's registered "arial14" bitmap font
# (ui/fonts/Arial14.tga, never used by the stock windows). It is roughly a third
# larger than the earlier arial11 and is measured the same way, so the server can
# fit text to the labels. Heights below are for the default window; every control
# is positioned from the top-left and the window resizes with its grow/offset flags.
FONT = "arial14"
# Advance widths for '!' through '~', read from the glyph width markers of
# ui/fonts/Arial14.tga (the same method that reproduces the arial11 table). A space
# has no glyph; 5 is a deliberate slight overestimate (the font's em is 14).
ARIAL14_ADVANCES = (
    3, 6, 7, 8, 9, 9, 3, 4, 4, 6, 9, 3, 5, 3, 4, 8, 5, 8, 8, 8, 8, 8, 8, 8, 8, 3, 3, 8, 8, 8, 9, 15,
    10, 9, 9, 9, 8, 8, 10, 9, 3, 8, 9, 8, 12, 9, 10, 8, 10, 10, 8, 9, 9, 10, 14, 8, 9, 9, 5, 5, 5, 7, 9,
    3, 8, 8, 7, 8, 8, 6, 8, 8, 3, 5, 7, 3, 11, 8, 8, 8, 8, 6, 7, 6, 8, 8, 12, 7, 8, 6, 6, 2, 6, 8,
)
SPACE_ADVANCE = 5


def text_width(text):
    return sum(ARIAL14_ADVANCES[ord(ch) - 33] if "!" <= ch <= "~" else SPACE_ADVANCE for ch in text)


WINDOW_WIDTH = 980
WINDOW_HEIGHT = 700
PITCH = 22
# The client cannot tell the server how tall the window is, and XML cannot hide
# controls, so the list and detail panel have this many rows built in and the
# server fills only as many as the player chose (the "Rows" buttons). The window
# needs about WINDOW_HEIGHT_BASE + PITCH * rows pixels of height for a choice (16 -> 556, 22 -> 688,
# 28 -> 820, 34 -> 952); the default window fits 22.
ROW_SIZES = (16, 22, 28, 34)
WINDOW_HEIGHT_BASE = 204
ROWS = ROW_SIZES[-1]
DETAIL_RESERVE = 5
DETAIL_LINES = ROWS - DETAIL_RESERVE
ACTIONS = 6
LIST_X, LIST_WIDTH = 8, 472
PANE_Y = 118
DETAIL_X = 496
COLUMN_HEADS_Y = 122
ROW_TOP = 146
DETAIL_TAB_Y, DETAIL_HEADER_Y, DETAIL_SUBHEADER_Y, DETAIL_TOP = 122, 146, 168, 194
# Label widths in pixels; the server fits text to them using the arial14 advances.
WIDTH_STATUS = 956
WIDTH_MESSAGE = 760
COLUMN_MARK, COLUMN_NAME, COLUMN_LEVEL, COLUMN_CLASS, COLUMN_TYPE, COLUMN_STATE = 14, 28, 152, 186, 300, 366
WIDTH_ROW_HEADER = 452
WIDTH_ROW_NAME = 120
WIDTH_ROW_LEVEL = 30
WIDTH_ROW_CLASS = 110
WIDTH_ROW_TYPE = 62
WIDTH_ROW_STATE = 102
WIDTH_DETAIL = 468
WIDTH_ACTION = 152

GOLD = (255, 210, 90)
MUTED = (150, 150, 150)
TEXT = (225, 225, 225)
LINK = (240, 200, 110)
STATUS = (200, 200, 200)
MESSAGE = (255, 235, 160)
DISABLED = (115, 115, 115)
ACTIVE = (120, 215, 130)
BLOOD = (230, 110, 100)                  # OPEN BG: dead / at a limit
DIM = (70, 70, 70)                       # OPEN BG: bar ground under the Field bar
REALM_COLORS = ((220, 125, 120), (135, 165, 235), (125, 200, 135))  # Albion, Midgard, Hibernia


def flow(y, x, items, gap=12):
    """Left-to-right toggles; width follows the widest text the server can send."""
    placed = []
    for name, control, widest in items:
        width = text_width(widest) + 4
        placed.append((name, control, x, y, width))
        x += width + gap
    return placed


# name, control, x, y, width. Order is the toggle index order (Toggle<Name>).
TOGGLES = (
    *flow(30, 12, (("TabRoster", 0x70, "Roster (99/99)"), ("TabRecruit", 0x71, "Recruit"),
                   ("TabActive", 0x72, "Active (99)"))),
    ("DetailOverview", 0x78, DETAIL_X, DETAIL_TAB_Y, text_width("Overview") + 4),
    ("DetailTraining", 0x79, DETAIL_X + text_width("Overview") + 20, DETAIL_TAB_Y, text_width("Training & Tactics") + 4),
    ("DetailGear", 0x7A, DETAIL_X + text_width("Overview") + text_width("Training & Tactics") + 36,
     DETAIL_TAB_Y, text_width("Gear") + 4),
    *flow(52, 74, (("RealmAll", 0x80, "All realms"), ("RealmAlbion", 0x81, "Albion"),
                   ("RealmMidgard", 0x82, "Midgard"), ("RealmHibernia", 0x83, "Hibernia"))),
    *flow(74, 74, (("RoleAny", 0x88, "Any role"), ("RoleTank", 0x89, "Tank"), ("RoleHealer", 0x8A, "Healer"),
                   ("RoleBuffer", 0x8B, "Buffer"), ("RoleAttacker", 0x8C, "Attacker"))),
    *flow(52, 560, (("GroupSmart", 0x90, "Smart"), ("GroupRealm", 0x91, "Realm"), ("GroupRole", 0x92, "Role"),
                    ("GroupLevel", 0x93, "Level"), ("GroupNone", 0x94, "None"))),
    *flow(74, 560, (("SortLevel", 0x98, "Level"), ("SortName", 0x99, "Name"), ("SortClass", 0x9A, "Class"))),
    *flow(30, 470, tuple((f"Rows{size}", 0xA0 + index, str(size)) for index, size in enumerate(ROW_SIZES)), gap=14),
)
# Fixed text with a click area and no label adapter.
# protocol name, text, control, x, y, width. x is always the left edge in the default window;
# bottom-anchored links keep their distance from the bottom edge, right-anchored ones from the right.
def _links(y, x, items, gap=10):
    placed = []
    for name, text, control in items:
        width = text_width(text) + 6
        placed.append((name, text, control, x, y, width))
        x += width + gap
    return placed


def _links_from_right(y, items, margin=12, gap=10):
    """Chains links leftwards from the window's right edge; the first item is rightmost."""
    placed = []
    edge = WINDOW_WIDTH - margin
    for name, text, control in items:
        width = text_width(text) + 6
        placed.append((name, text, control, edge - width, y, width))
        edge -= width + gap
    return placed


STATIC_LINKS = (
    *_links_from_right(30, (("Refresh", "[Refresh]", 0xDE), ("Clear", "[Clear]", 0xC8), ("Search", "[Search]", 0xD0))),
    *_links(WINDOW_HEIGHT - 52, 14, (("ListUp", "[Up]", 0xC0), ("ListDown", "[Down]", 0xC1),
                                     ("ListPageUp", "[PgUp]", 0xC2), ("ListPageDown", "[PgDn]", 0xC3))),
    *_links(WINDOW_HEIGHT - 116, DETAIL_X, (("DetailUp", "[Up]", 0xC4), ("DetailDown", "[Down]", 0xC5))),
    *_links_from_right(WINDOW_HEIGHT - 30, (("Close", "[Close]", 0xDF),)),
)
ANCHORED_RIGHT = ("Search", "Clear", "Refresh", "Close")
ANCHORED_BOTTOM = ("ListUp", "ListDown", "ListPageUp", "ListPageDown", "DetailUp", "DetailDown", "Close")

LABEL_STATUS = 0
LABEL_MESSAGE = 1
LABEL_TOGGLE_BASE = 2                    # +2k inactive, +2k+1 active
LABEL_ROW_BASE = LABEL_TOGGLE_BASE + 2 * len(TOGGLES)
# header-or-marker (gold), name x3 realm colours, level, class, type, state active, state benched
ROW_STRIDE = 9
LABEL_LIST_INDICATOR = LABEL_ROW_BASE + ROW_STRIDE * ROWS
LABEL_HEADER_BASE = LABEL_LIST_INDICATOR + 1   # Albion, Midgard, Hibernia
LABEL_SUBHEADER = LABEL_HEADER_BASE + 3
LABEL_DETAIL_BASE = LABEL_SUBHEADER + 1  # +2j text, +2j+1 link
LABEL_DETAIL_INDICATOR = LABEL_DETAIL_BASE + 2 * DETAIL_LINES
LABEL_ACTION_BASE = LABEL_DETAIL_INDICATOR + 1  # +2k enabled, +2k+1 disabled
LABEL_DETAIL_UP = LABEL_ACTION_BASE + 2 * ACTIONS
LABEL_DETAIL_DOWN = LABEL_DETAIL_UP + 1
LABEL_COUNT = LABEL_DETAIL_DOWN + 1
# COMPANIONS window labels follow the manager's 440 and are registered by the same loop.
BAR_LABEL_BASE = LABEL_COUNT
BAR_LABEL_STATUS, BAR_LABEL_MESSAGE = BAR_LABEL_BASE, BAR_LABEL_BASE + 1
BAR_LABEL_TILE_BASE = BAR_LABEL_BASE + 2                              # +4k inactive, +1 active, +2 disabled, +3 underline
BAR_LABEL_SLOT_BASE = BAR_LABEL_TILE_BASE + BAR_TILE_STRIDE * BAR_TILES  # +4i + stance colour index
BAR_LABEL_END = BAR_LABEL_SLOT_BASE + BAR_SLOT_STRIDE * BAR_SLOTS       # 538
BAR_LABEL_COUNT = BAR_LABEL_END - BAR_LABEL_BASE                        # 98
SCROLL_LINK_LABELS = {"DetailUp": LABEL_DETAIL_UP, "DetailDown": LABEL_DETAIL_DOWN}

# COMPANION GEAR window (Custom6). Slot n (0-18 worn in character-sheet order, 19-58 backpack) is client slot
# GEAR_CLIENT_SLOTS[n]: PersistentCompanionInventoryView positions 1-19 and 21-60 from house-vault slot 150.
GEAR_WINDOW = 0x73                       # Custom6: the ShowCustomN switch pushes 0x6D + N
OP_GEAR_REFRESH, OP_GEAR_SHOW, OP_GEAR_HIDE = 7, 8, 9
GEAR_EVENT_BASE = 0x900                  # OnClickEvent 2304-2362; raid 0x600-0x64F, manager 0x700-0x7DF
GEAR_READY_CONTROL = 0xDC                # sent by operation 8 through the manager's command path
GEAR_WORN_CAPTIONS = ("Helm", "Chest", "Arms", "Hands", "Legs", "Feet", "Cloak", "Neck", "Jewel", "Belt",
                      "L Wrist", "R Wrist", "L Ring", "R Ring", "R Hand", "L Hand", "2 Hand", "Ranged", "Mythic")
GEAR_WORN_SLOTS = len(GEAR_WORN_CAPTIONS)
GEAR_BACKPACK_SLOTS = 40
GEAR_SLOTS = GEAR_WORN_SLOTS + GEAR_BACKPACK_SLOTS
GEAR_FIRST_CLIENT_SLOT = 150             # eInventorySlot.HousingInventory_First
GEAR_BACKPACK_FIRST_POSITION = 21
GEAR_CLIENT_SLOTS = tuple(GEAR_FIRST_CLIENT_SLOT + position - 1 for position in
                          (*range(1, GEAR_WORN_SLOTS + 1),
                           *range(GEAR_BACKPACK_FIRST_POSITION, GEAR_BACKPACK_FIRST_POSITION + GEAR_BACKPACK_SLOTS)))
GEAR_REFRESH_OFFSET, GEAR_EVENT_OFFSET, GEAR_CODE_LIMIT = 0x2800, 0x2A00, 0x3000   # inside .cmgr, before the data
GEAR_ICON_ADAPTER = "cmgr_icon_{}"
GEAR_ICON_MAX, GEAR_ICON_INITIAL, GEAR_ICON_EMPTY = 1000000.0, -1.0, -1   # the quiver's icon adapter values
# Server labels follow the COMPANIONS window's: title, backpack heading, two reply lines, the worn captions, then one
# mark per slot ("*" = the item stays with the companion).
GEAR_LABEL_BASE = BAR_LABEL_END
GEAR_LABEL_TITLE = GEAR_LABEL_BASE
GEAR_LABEL_BACKPACK = GEAR_LABEL_BASE + 1
GEAR_LABEL_MESSAGE_BASE = GEAR_LABEL_BASE + 2
GEAR_MESSAGE_LINES = 2
GEAR_LABEL_CAPTION_BASE = GEAR_LABEL_MESSAGE_BASE + GEAR_MESSAGE_LINES
GEAR_LABEL_MARK_BASE = GEAR_LABEL_CAPTION_BASE + GEAR_WORN_SLOTS
GEAR_LABEL_END = GEAR_LABEL_MARK_BASE + GEAR_SLOTS
GEAR_LABEL_COUNT = GEAR_LABEL_END - GEAR_LABEL_BASE                     # 82
# OPEN BG window (Custom5, 0x72): label adapters 620-984 follow the gear labels, operations 10-12, own click range.
# Spec docs/superpowers/specs/2026-10-16-open-bg-window-client-design.md sections 4 and 5; the 0.283.0 "Kriegstafel" layout
# is concept A of docs/superpowers/specs/2026-10-16-open-bg-window-redesign-analysis.md (see open_bg_window()).
BG_WINDOW = 0x72                         # Custom5: the ShowCustomN switch pushes 0x6D + N
OP_BG_SHOW, OP_BG_HIDE, OP_BG_TOKEN = 10, 11, 12
BG_EVENT_BASE = 0xA00                    # OnClickEvent 2560-2815; gear 0x900-0x93A, lever probe 0x800-0x8DF
BG_READY_CONTROL = 0xFF                  # sent by operation 10, not a click
BG_COMMAND_PREFIX = "&bgsetup ui "
BG_ROW_COUNT, BG_ROW_STRIDE = 18, 18
BG_LABEL_BASE = GEAR_LABEL_END
BG_LABEL_STATUS = BG_LABEL_BASE                                          # 620
BG_LABEL_TAB_BASE = BG_LABEL_STATUS + 1                                  # 621-624: Overview and Setup, inactive/active
BG_LABEL_PAGE = BG_LABEL_TAB_BASE + 4                                    # 625
BG_LABEL_BANNER = BG_LABEL_PAGE + 1                                      # 626 (title font)
BG_LABEL_PHASE = BG_LABEL_BANNER + 1                                     # 627
BG_LABEL_HINT = BG_LABEL_PHASE + 1                                       # 628
BG_LABEL_REPLY = BG_LABEL_HINT + 1                                       # 629
BG_LABEL_OPEN_ALL, BG_LABEL_FOLD_ALL = BG_LABEL_REPLY + 1, BG_LABEL_REPLY + 2   # 630, 631 (Overview only); 632 stays free
BG_LABEL_ROW_BASE = BG_LABEL_REPLY + 4                                   # 633, as in 0.282.0
BG_LABEL_ROW_END = BG_LABEL_ROW_BASE + BG_ROW_COUNT * BG_ROW_STRIDE      # 957
BG_LABEL_HEAD_BASE, BG_HEAD_LABELS = BG_LABEL_ROW_END, 6                 # 957-962 column headings
BG_LABEL_REALM_BASE, BG_REALM_STRIDE = BG_LABEL_HEAD_BASE + BG_HEAD_LABELS, 5   # 963-977: three realms x 5
BG_LABEL_USED_END = BG_LABEL_REALM_BASE + 3 * BG_REALM_STRIDE            # 978; 978-984 stay free
BG_LABEL_END = BG_LABEL_BASE + 365                                       # 985: the native label count is unchanged
# Row slots (open_bg_row_slots) and realm band offsets (open_bg_realm_labels).
BG_SLOT_MARK, BG_SLOT_TITLE, BG_SLOT_TEXT, BG_SLOT_NAME_BASE = 0, 1, 2, 3        # names: +0 Albion, +1 Midgard, +2 Hibernia
BG_SLOT_LEVEL, BG_SLOT_CLASS, BG_SLOT_ASH = 6, 7, 8
BG_SLOT_STATE_FIELD, BG_SLOT_STATE_BLOOD, BG_SLOT_STATE_ASH = 9, 10, 11
BG_SLOT_BAR_DIM, BG_SLOT_BAR_FIELD, BG_SLOT_NOTE, BG_SLOT_LINK, BG_SLOT_MINUS, BG_SLOT_PLUS = 12, 13, 14, 15, 16, 17
BG_REALM_NAME, BG_REALM_DETAIL, BG_REALM_BAR_DIM, BG_REALM_BAR_FIELD, BG_REALM_COUNT = 0, 1, 2, 3, 4
BG_CONTROL_LINK_BASE, BG_CONTROL_MINUS_BASE, BG_CONTROL_PLUS_BASE = 0x00, 0x20, 0x40   # + row 0-17
BG_CONTROL_FOLD_BASE, BG_CONTROL_SELECT_BASE = 0x60, 0x80                               # + row 0-17
BG_CONTROL_TAB_SITUATION, BG_CONTROL_TAB_CONTROL = 0xE0, 0xE1                           # Overview, Setup
BG_CONTROL_UP, BG_CONTROL_DOWN, BG_CONTROL_PAGE_UP, BG_CONTROL_PAGE_DOWN = 0xE8, 0xE9, 0xEA, 0xEB
BG_CONTROL_OPEN_ALL, BG_CONTROL_FOLD_ALL, BG_CONTROL_CREST_BASE = 0xEC, 0xED, 0xF0      # crests: + realm 0-2
BG_CONTROL_CLOSE, BG_CONTROL_REFRESH = 0xFD, 0xFE
# Native code for the OPEN BG window in the free .cmgr area after the gear code and before the data (0x4000).
BG_EVENT_OFFSET, BG_SEND_OFFSET, BG_CODE_LIMIT = 0x3000, 0x3200, 0x4000
# Hash guard: the ShowCustomN switch must push 0x72 for Custom5 (0x6D + 5), as 0x4E0B4A pushes 0x74 for Custom7.
BG_WINDOW_GUARD = (0x4E0B3E, bytes.fromhex("6a 01 6a 72"))
TOTAL_LABEL_COUNT = BG_LABEL_END                                        # 985
# Geometry (the 0.275.0 layout): worn gear in three captioned columns, the backpack as an 8 x 5 grid.
GEAR_SOCKET, GEAR_ICON = 34, 32
GEAR_WORN_X, GEAR_WORN_Y, GEAR_WORN_PITCH_X, GEAR_WORN_PITCH_Y = 14, 30, 58, 50
# Worn slot index (character-sheet order) at each (column, row) of the worn panel.
GEAR_WORN_LAYOUT = ((0, 0), (0, 1), (2, 1), (0, 2), (2, 2), (0, 3),   # helm, chest, arms, hands, legs, feet
                    (2, 0), (1, 0), (1, 1), (1, 2),                   # cloak, neck, jewel, belt
                    (1, 3), (2, 3), (0, 4), (1, 4),                   # wrists, left ring, right ring
                    (0, 5), (1, 5), (2, 5), (0, 6), (2, 4))           # right hand, left hand, two hand, ranged, mythic
GEAR_BAG_X, GEAR_BAG_Y, GEAR_BAG_PITCH, GEAR_BAG_COLUMNS = 206, 48, 38, 8
GEAR_WIDTH, GEAR_HEIGHT = GEAR_BAG_X + GEAR_BAG_COLUMNS * GEAR_BAG_PITCH + 8, GEAR_WORN_Y + 7 * GEAR_WORN_PITCH_Y + 6
GEAR_WIDTH_TITLE = GEAR_WIDTH - 40
GEAR_WIDTH_CAPTION = 56
GEAR_WIDTH_MESSAGE = GEAR_BAG_COLUMNS * GEAR_BAG_PITCH
GEAR_WIDTH_MARK = 10
GEAR_MESSAGE_Y = GEAR_BAG_Y + 5 * GEAR_BAG_PITCH + 10
GEAR_HELP_Y = GEAR_MESSAGE_Y + 44
GEAR_HELP_TEXT = ("Click your item, then a slot: give or equip.",
                  "Click a filled slot to pick its item up.",
                  "* = stays with the companion (starter gear).")


def bar_tile_at(row, column):
    """Top-left corner of a COMPANIONS tile cell: five per row, three rows under the title."""
    return 8 + BAR_TILE_PITCH_X * column, BAR_TILE_TOP + BAR_TILE_PITCH_Y * row


def bar_tile(index):
    """(name, control, x, y, width) of server tile index. The third row starts after the native Invite tile."""
    name = BAR_TILE_NAMES[index]
    control = BAR_CONTROL_FIRST + index
    row, column = divmod(index, BAR_TILES_PER_ROW)
    if row >= 2:
        row, column = 2, index - 2 * BAR_TILES_PER_ROW + len(BAR_NATIVE_TILES)
    x, y = bar_tile_at(row, column)
    return name, control, x, y, BAR_TILE_W

CONTROL_ROW_BASE = 0x00
CONTROL_DETAIL_BASE = 0x30
CONTROL_ACTION_BASE = 0x60


def layout_constants():
    """Numbers shared with the server; exported to the manifest and checked by the test."""
    values = {
        "Marker": MARKER, "ProtocolVersion": PROTOCOL_VERSION, "BodySize": BODY_SIZE,
        "TextOffset": TEXT_OFFSET, "MaximumTextLength": BODY_SIZE - TEXT_OFFSET - 1,
        "OpLabel": OP_LABEL, "OpShow": OP_SHOW, "OpHide": OP_HIDE, "OpToken": OP_TOKEN,
        "WindowHeightBase": WINDOW_HEIGHT_BASE, "RowPitch": PITCH,
        "Rows": ROWS, "DetailLines": DETAIL_LINES, "DetailReserve": DETAIL_RESERVE, "Actions": ACTIONS,
        "LabelStatus": LABEL_STATUS, "LabelMessage": LABEL_MESSAGE,
        "LabelToggleBase": LABEL_TOGGLE_BASE, "LabelRowBase": LABEL_ROW_BASE, "RowStride": ROW_STRIDE,
        "LabelListIndicator": LABEL_LIST_INDICATOR, "LabelHeaderBase": LABEL_HEADER_BASE,
        "LabelSubheader": LABEL_SUBHEADER, "LabelDetailBase": LABEL_DETAIL_BASE,
        "LabelDetailIndicator": LABEL_DETAIL_INDICATOR, "LabelActionBase": LABEL_ACTION_BASE,
        "LabelDetailUp": LABEL_DETAIL_UP, "LabelDetailDown": LABEL_DETAIL_DOWN,
        "LabelCount": LABEL_COUNT, "ControlRowBase": CONTROL_ROW_BASE,
        "ControlDetailBase": CONTROL_DETAIL_BASE, "ControlActionBase": CONTROL_ACTION_BASE,
        "ControlLimit": CONTROL_LIMIT, "ControlSearch": SEARCH_CONTROL, "ControlReady": READY_CONTROL,
        "WidthStatus": WIDTH_STATUS, "WidthMessage": WIDTH_MESSAGE, "WidthRowHeader": WIDTH_ROW_HEADER,
        "WidthRowName": WIDTH_ROW_NAME, "WidthRowLevel": WIDTH_ROW_LEVEL, "WidthRowClass": WIDTH_ROW_CLASS,
        "WidthRowType": WIDTH_ROW_TYPE, "WidthRowState": WIDTH_ROW_STATE, "WidthDetail": WIDTH_DETAIL,
        "WidthAction": WIDTH_ACTION, "SpaceAdvance": SPACE_ADVANCE,
    }
    for index, size in enumerate(ROW_SIZES):
        values[f"RowSize{index}"] = size
    values.update({
        "TotalLabelCount": TOTAL_LABEL_COUNT, "BarLabelBase": BAR_LABEL_BASE,
        "BarLabelStatus": BAR_LABEL_STATUS, "BarLabelMessage": BAR_LABEL_MESSAGE,
        "BarLabelTileBase": BAR_LABEL_TILE_BASE, "BarTileStride": BAR_TILE_STRIDE, "BarTiles": BAR_TILES,
        "BarLabelSlotBase": BAR_LABEL_SLOT_BASE, "BarSlotStride": BAR_SLOT_STRIDE, "BarSlots": BAR_SLOTS,
        "BarSlotLeader": BAR_SLOT_LEADER_OFFSET, "BarLabelCount": BAR_LABEL_COUNT,
        "BarTileInactive": BAR_TILE_INACTIVE_OFFSET, "BarTileActive": BAR_TILE_ACTIVE_OFFSET,
        "BarTileDisabled": BAR_TILE_DISABLED_OFFSET, "BarTileUnderline": BAR_TILE_UNDERLINE_OFFSET,
        "OpBarShow": OP_BAR_SHOW, "OpBarHide": OP_BAR_HIDE,
        "BarControlFirst": BAR_CONTROL_FIRST, "BarControlLimit": BAR_CONTROL_LIMIT,
        "BarControlSlotBase": BAR_CONTROL_SLOT_BASE, "BarControlReady": BAR_READY_CONTROL,
        "BarWidthStatus": BAR_WIDTH_STATUS, "BarWidthMessage": BAR_WIDTH_MESSAGE,
        "BarWidthTile": BAR_TILE_W, "BarWidthSlot": BAR_WIDTH_SLOT,
        "OpGearRefresh": OP_GEAR_REFRESH, "OpGearShow": OP_GEAR_SHOW, "OpGearHide": OP_GEAR_HIDE,
        "ControlGearReady": GEAR_READY_CONTROL, "GearWornSlots": GEAR_WORN_SLOTS, "GearSlots": GEAR_SLOTS,
        "GearLabelBase": GEAR_LABEL_BASE, "GearLabelTitle": GEAR_LABEL_TITLE, "GearLabelBackpack": GEAR_LABEL_BACKPACK,
        "GearLabelMessageBase": GEAR_LABEL_MESSAGE_BASE, "GearMessageLines": GEAR_MESSAGE_LINES,
        "GearLabelCaptionBase": GEAR_LABEL_CAPTION_BASE, "GearLabelMarkBase": GEAR_LABEL_MARK_BASE,
        "GearLabelCount": GEAR_LABEL_COUNT, "GearWidthTitle": GEAR_WIDTH_TITLE,
        "GearWidthMessage": GEAR_WIDTH_MESSAGE, "GearWidthCaption": GEAR_WIDTH_CAPTION,
    })
    for name in ("BG_WINDOW", "OP_BG_SHOW", "OP_BG_HIDE", "OP_BG_TOKEN", "BG_EVENT_BASE", "BG_READY_CONTROL",
                 "BG_LABEL_BASE", "BG_ROW_COUNT", "BG_ROW_STRIDE", "BG_LABEL_STATUS", "BG_LABEL_TAB_BASE", "BG_LABEL_PAGE",
                 "BG_LABEL_BANNER", "BG_LABEL_PHASE", "BG_LABEL_HINT", "BG_LABEL_REPLY", "BG_LABEL_OPEN_ALL",
                 "BG_LABEL_FOLD_ALL", "BG_LABEL_ROW_BASE",
                 "BG_LABEL_ROW_END", "BG_LABEL_HEAD_BASE", "BG_HEAD_LABELS", "BG_LABEL_REALM_BASE", "BG_REALM_STRIDE",
                 "BG_LABEL_USED_END", "BG_LABEL_END",
                 "BG_SLOT_MARK", "BG_SLOT_TITLE", "BG_SLOT_TEXT", "BG_SLOT_NAME_BASE", "BG_SLOT_LEVEL", "BG_SLOT_CLASS",
                 "BG_SLOT_ASH", "BG_SLOT_STATE_FIELD", "BG_SLOT_STATE_BLOOD", "BG_SLOT_STATE_ASH", "BG_SLOT_BAR_DIM",
                 "BG_SLOT_BAR_FIELD", "BG_SLOT_NOTE", "BG_SLOT_LINK", "BG_SLOT_MINUS", "BG_SLOT_PLUS",
                 "BG_REALM_NAME", "BG_REALM_DETAIL", "BG_REALM_BAR_DIM", "BG_REALM_BAR_FIELD", "BG_REALM_COUNT",
                 "BG_CONTROL_LINK_BASE", "BG_CONTROL_MINUS_BASE", "BG_CONTROL_PLUS_BASE", "BG_CONTROL_FOLD_BASE",
                 "BG_CONTROL_SELECT_BASE", "BG_CONTROL_TAB_SITUATION", "BG_CONTROL_TAB_CONTROL", "BG_CONTROL_UP",
                 "BG_CONTROL_DOWN", "BG_CONTROL_PAGE_UP", "BG_CONTROL_PAGE_DOWN", "BG_CONTROL_OPEN_ALL",
                 "BG_CONTROL_FOLD_ALL", "BG_CONTROL_CREST_BASE", "BG_CONTROL_CLOSE", "BG_CONTROL_REFRESH"):
        values["".join(part.capitalize() for part in name.split("_"))] = globals()[name]
    for index, name in enumerate(BAR_TILE_NAMES):
        values["BarControl" + name] = BAR_CONTROL_FIRST + index
    for index, (name, control, *_rest) in enumerate(TOGGLES):
        values["Control" + name] = control
        values["Toggle" + name] = index
    for name, _text, control, *_rest in STATIC_LINKS:
        values["Control" + name] = control
    return values


def adapter_name(index):
    return f"cmgr_{index:03d}"


def click_event(control):
    """Decimal event ID; the stock OnClickEvent mapper atoi()s leading-digit values."""
    return str(EVENT_BASE + control)


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def float_bits(value):
    return struct.unpack("<I", struct.pack("<f", value))[0]


def gear_icon_names():
    return [GEAR_ICON_ADAPTER.format(index) for index in range(GEAR_SLOTS)]


def gear_slot_origin(index):
    """Top-left of gear slot `index`'s socket (0-18 worn, 19-58 backpack)."""
    if index < GEAR_WORN_SLOTS:
        column, row = GEAR_WORN_LAYOUT[index]
        return GEAR_WORN_X + column * GEAR_WORN_PITCH_X, GEAR_WORN_Y + row * GEAR_WORN_PITCH_Y
    bag = index - GEAR_WORN_SLOTS
    return GEAR_BAG_X + (bag % GEAR_BAG_COLUMNS) * GEAR_BAG_PITCH, GEAR_BAG_Y + (bag // GEAR_BAG_COLUMNS) * GEAR_BAG_PITCH


def gear_slot_rectangles():
    """(x, y, width, height) of each gear slot's icon and click area, in slot order."""
    return [(x + 1, y + 1, GEAR_ICON, GEAR_ICON) for x, y in map(gear_slot_origin, range(GEAR_SLOTS))]


def inventory_source(index):
    """Client slot for a kind-9 drag index, exactly as the stock inventory drop maps it (0x408151), or None."""
    if 0 <= index <= 3:
        return index + 10
    if 10 <= index <= 27:
        return index + 11
    if 40 <= index <= 79:
        return index
    return None


def verify_natives(pe, image):
    """The first ret of each native entry point the patch calls must pop the expected byte count."""
    base = pe.OPTIONAL_HEADER.ImageBase
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    report = {}
    for address, expected in NATIVE_RETURN_BYTES.items():
        code = image[pe.get_offset_from_rva(address - base):][:0x800]
        found = None
        for insn in md.disasm(code, address):
            if insn.mnemonic == "ret":
                found = int(insn.op_str, 16) if insn.op_str else 0
                break
        if found != expected:
            raise ValueError(f"Native entry {address:#x} returns {found} bytes, expected {expected}")
        report[f"{address:#x}"] = expected
    return report


def build(image):
    if sha256(image) != RAID_SHA256:
        raise ValueError("Unknown game.dll: expected the verified native-raid client")
    pe = pefile.PE(data=image)
    if pe.OPTIONAL_HEADER.ImageBase != 0x400000 or pe.FILE_HEADER.Machine != 0x14C:
        raise ValueError("Expected the verified Windows x86 client")
    if pe.sections[-1].Name.rstrip(b"\0") != b".raid":
        raise ValueError("Native raid section was changed")
    natives = verify_natives(pe, image)
    align = lambda value, unit: (value + unit - 1) // unit * unit
    last = pe.sections[-1]
    rva = align(last.VirtualAddress + max(last.Misc_VirtualSize, last.SizeOfRawData),
                pe.OPTIONAL_HEADER.SectionAlignment)
    va = pe.OPTIONAL_HEADER.ImageBase + rva

    data_base = 0x4000
    slots = va + data_base
    active = slots + 4 * TOTAL_LABEL_COUNT
    # The 16 bytes after the manager flag: active+0 is the manager flag, active+4 the COMPANIONS flag, active+8 the
    # OPEN BG flag; active+12 is still free.
    bar_active = active + 4
    bg_active = active + 8
    command = active + 16
    hex_digits = command + 32
    search_text = hex_digits + 16
    counter = search_text + 32
    names = counter + 16
    # Gear window data: icon adapter objects, client slot per gear slot, int -> float scratch, icon adapter names.
    gear_icon_slots = names + 16 * TOTAL_LABEL_COUNT
    gear_slot_table = gear_icon_slots + 4 * GEAR_SLOTS
    gear_scratch = gear_slot_table + 4 * GEAR_SLOTS
    gear_names = gear_scratch + 16
    # OPEN BG command line "&bgsetup ui <tttt> <cc>", separate from the &companions buffer so neither window's token
    # or control bytes can change the other's.
    bg_command = gear_names + 16 * GEAR_SLOTS
    payload = bytearray(bg_command - va + 32)
    if BG_CODE_LIMIT > data_base:
        raise ValueError("OPEN BG code would overlap the data area")
    offset = lambda address: address - va
    for index, slot in enumerate(GEAR_CLIENT_SLOTS):
        struct.pack_into("<I", payload, offset(gear_slot_table) + 4 * index, slot)
    for index, name in enumerate(gear_icon_names()):
        encoded = name.encode("ascii") + b"\0"
        payload[offset(gear_names) + 16 * index:offset(gear_names) + 16 * index + len(encoded)] = encoded
    initial_command = (COMMAND_PREFIX + "0000 00").encode("ascii") + b"\0"
    payload[offset(command):offset(command) + len(initial_command)] = initial_command
    payload[offset(hex_digits):offset(hex_digits) + 16] = b"0123456789abcdef"
    encoded_search = SEARCH_PREFIX.encode("ascii") + b"\0"
    payload[offset(search_text):offset(search_text) + len(encoded_search)] = encoded_search
    for index in range(TOTAL_LABEL_COUNT):
        encoded = adapter_name(index).encode("ascii") + b"\0"
        payload[offset(names) + 16 * index:offset(names) + 16 * index + len(encoded)] = encoded
    token_offset = len(COMMAND_PREFIX)
    control_offset = token_offset + 5
    initial_bg_command = (BG_COMMAND_PREFIX + "0000 00").encode("ascii") + b"\0"
    payload[offset(bg_command):offset(bg_command) + len(initial_bg_command)] = initial_bg_command
    bg_token_offset = len(BG_COMMAND_PREFIX)
    bg_control_offset = bg_token_offset + 5

    ks = Ks(KS_ARCH_X86, KS_MODE_32)
    blocks = {}

    def put(label, start, assembly, limit):
        code, _ = ks.asm(assembly, addr=va + start)
        if start + len(code) > limit:
            raise ValueError(f"{label} exceeds reserved code space")
        payload[start:start + len(code)] = bytes(code)
        blocks[label] = {"address": va + start, "length": len(code)}

    # Registry is EDI at the raid hook, exactly as the raid's own registrations use it.
    # A loop keeps the block small however many adapters there are; the counter lives in
    # memory because the callee's register use is not relied on.
    put("init", 0x0000, f"""
        pushfd; pushad
        mov dword ptr [{active}], 0
        mov dword ptr [{bar_active}], 0
        mov dword ptr [{bg_active}], 0
        mov dword ptr [{counter}], 0
    register:
        mov eax, dword ptr [{counter}]
        shl eax, 4
        add eax, {names}
        push eax
        mov eax, dword ptr [{counter}]
        shl eax, 2
        add eax, {slots}
        push eax
        push edi
        call {REGISTER_TEXT_ADAPTER}
        inc dword ptr [{counter}]
        cmp dword ptr [{counter}], {TOTAL_LABEL_COUNT}
        jb register
        mov dword ptr [{counter}], 0
    register_icon:
        push {float_bits(GEAR_ICON_INITIAL):#x}
        push {float_bits(GEAR_ICON_MAX):#x}
        mov eax, dword ptr [{counter}]
        shl eax, 4
        add eax, {gear_names}
        push eax
        mov eax, dword ptr [{counter}]
        shl eax, 2
        add eax, {gear_icon_slots}
        push eax
        push edi
        call {REGISTER_SCALAR_ADAPTER}
        inc dword ptr [{counter}]
        cmp dword ptr [{counter}], {GEAR_SLOTS}
        jb register_icon
        popad; popfd
        jmp {RAID_INIT}
    """, 0x1000)

    # EBX is the packet body and EBP+18 its length, as in the raid dispatcher.
    # Validated before use: size, marker, version, opcode, index, and a NUL at 127.
    put("packet", 0x1000, f"""
        cmp dword ptr [ebp+0x18], 2
        jb raid
        cmp byte ptr [ebx+1], {MARKER}
        jne raid
        cmp dword ptr [ebp+0x18], {BODY_SIZE}
        jb ignored
        cmp byte ptr [ebx+2], {PROTOCOL_VERSION}
        jne ignored
        cmp byte ptr [ebx+{BODY_SIZE - 1}], 0
        jne ignored
        pushfd; pushad
        mov esi, ebx
        movzx eax, byte ptr [esi+3]
        cmp eax, {OP_LABEL}
        je label
        cmp eax, {OP_SHOW}
        je show
        cmp eax, {OP_HIDE}
        je hide
        cmp eax, {OP_TOKEN}
        je token
        cmp eax, {OP_BAR_SHOW}
        je bar_show
        cmp eax, {OP_BAR_HIDE}
        je bar_hide
        cmp eax, {OP_GEAR_REFRESH}
        je gear_refresh
        cmp eax, {OP_GEAR_SHOW}
        je gear_show
        cmp eax, {OP_GEAR_HIDE}
        je gear_hide
        cmp eax, {OP_BG_SHOW}
        je bg_show
        cmp eax, {OP_BG_HIDE}
        je bg_hide
        cmp eax, {OP_BG_TOKEN}
        je bg_token
        jmp done
    label:
        movzx ecx, word ptr [esi+4]
        cmp ecx, {TOTAL_LABEL_COUNT}
        jae done
        mov ebx, dword ptr [{slots}+ecx*4]
        test ebx, ebx
        jz done
        lea eax, [esi+{TEXT_OFFSET}]
        push eax
        call {SET_ADAPTER_TEXT}
        jmp done
    show:
        mov dword ptr [{active}], 1
        mov ecx, dword ptr [{WINDOW_MANAGER}]
        test ecx, ecx
        jz done
        push 1
        push {CUSTOM8_WINDOW}
        mov eax, dword ptr [ecx]
        call dword ptr [eax+8]
        mov eax, {READY_CONTROL}
        call {va + 0x2600}
        jmp done
    hide:
        mov dword ptr [{active}], 0
        mov ecx, dword ptr [{WINDOW_MANAGER}]
        test ecx, ecx
        jz done
        push 0
        push {CUSTOM8_WINDOW}
        mov eax, dword ptr [ecx]
        call dword ptr [eax+8]
        jmp done
    bar_show:
        mov dword ptr [{bar_active}], 1
        mov ecx, dword ptr [{WINDOW_MANAGER}]
        test ecx, ecx
        jz done
        push 1
        push {BAR_WINDOW}
        mov eax, dword ptr [ecx]
        call dword ptr [eax+8]
        mov eax, {BAR_READY_CONTROL}
        call {va + 0x2600}
        jmp done
    bar_hide:
        mov dword ptr [{bar_active}], 0
        mov ecx, dword ptr [{WINDOW_MANAGER}]
        test ecx, ecx
        jz done
        push 0
        push {BAR_WINDOW}
        mov eax, dword ptr [ecx]
        call dword ptr [eax+8]
        jmp done
    gear_refresh:
        call {va + GEAR_REFRESH_OFFSET}
        jmp done
    gear_show:
        mov ecx, dword ptr [{WINDOW_MANAGER}]
        test ecx, ecx
        jz done
        push 1
        push {GEAR_WINDOW}
        mov eax, dword ptr [ecx]
        call dword ptr [eax+8]
        call {va + GEAR_REFRESH_OFFSET}
        mov eax, {GEAR_READY_CONTROL}
        call {va + 0x2600}
        jmp done
    gear_hide:
        mov ecx, dword ptr [{WINDOW_MANAGER}]
        test ecx, ecx
        jz done
        push 0
        push {GEAR_WINDOW}
        mov eax, dword ptr [ecx]
        call dword ptr [eax+8]
        jmp done
    bg_show:
        mov dword ptr [{bg_active}], 1
        mov ecx, dword ptr [{WINDOW_MANAGER}]
        test ecx, ecx
        jz done
        push 1
        push {BG_WINDOW}
        mov eax, dword ptr [ecx]
        call dword ptr [eax+8]
        mov eax, {BG_READY_CONTROL}
        call {va + BG_SEND_OFFSET}
        jmp done
    bg_hide:
        mov dword ptr [{bg_active}], 0
        mov ecx, dword ptr [{WINDOW_MANAGER}]
        test ecx, ecx
        jz done
        push 0
        push {BG_WINDOW}
        mov eax, dword ptr [ecx]
        call dword ptr [eax+8]
        jmp done
    bg_token:
        xor ecx, ecx
    bg_token_check:
        movzx eax, byte ptr [esi+{TEXT_OFFSET}+ecx]
        cmp eax, 0x30
        jb done
        cmp eax, 0x39
        jbe bg_token_next
        cmp eax, 0x61
        jb done
        cmp eax, 0x66
        ja done
    bg_token_next:
        inc ecx
        cmp ecx, 4
        jb bg_token_check
        mov eax, dword ptr [esi+{TEXT_OFFSET}]
        mov dword ptr [{bg_command + bg_token_offset}], eax
        jmp done
    token:
        xor ecx, ecx
    token_check:
        movzx eax, byte ptr [esi+{TEXT_OFFSET}+ecx]
        cmp eax, 0x30
        jb done
        cmp eax, 0x39
        jbe token_next
        cmp eax, 0x61
        jb done
        cmp eax, 0x66
        ja done
    token_next:
        inc ecx
        cmp ecx, 4
        jb token_check
        mov eax, dword ptr [esi+{TEXT_OFFSET}]
        mov dword ptr [{command + token_offset}], eax
    done:
        popad; popfd
    ignored:
        jmp {PACKET_DONE}
    raid:
        jmp {RAID_PACKET}
    """, 0x1800)

    # EAX is the event record and [EAX+8] its ID; unrelated IDs keep the raid chain intact. The OPEN BG range
    # 0xA00-0xAFF is checked first and goes to its own block; every other ID takes the unchanged path below.
    put("eventHandler", 0x1C00, f"""
        cmp dword ptr [eax+8], {BG_EVENT_BASE}
        jb gear_range
        cmp dword ptr [eax+8], {BG_EVENT_BASE + 0xFF}
        jbe {va + BG_EVENT_OFFSET}
    gear_range:
        cmp dword ptr [eax+8], {GEAR_EVENT_BASE}
        jb manager_range
        cmp dword ptr [eax+8], {GEAR_EVENT_BASE + GEAR_SLOTS - 1}
        jbe {va + GEAR_EVENT_OFFSET}
    manager_range:
        cmp dword ptr [eax+8], {EVENT_BASE}
        jb raid
        cmp dword ptr [eax+8], {EVENT_BASE + CONTROL_LIMIT}
        jae raid
        pushfd; pushad
        mov ecx, dword ptr [eax+8]
        sub ecx, {EVENT_BASE}
        cmp ecx, {BAR_CONTROL_FIRST}
        jb manager_gate
        cmp ecx, {BAR_CONTROL_LIMIT}
        jae manager_gate
        cmp dword ptr [{bar_active}], 1
        jne handled
        jmp send_control
    manager_gate:
        cmp dword ptr [{active}], 1
        jne handled
        cmp ecx, {SEARCH_CONTROL}
        je search
    send_control:
        mov eax, ecx
        call {va + 0x2600}
        jmp handled
    search:
        mov esi, {CHAT_INPUT_STATE}
        xor edi, edi
        inc edi
        call {SET_CHAT_MODE}
        push {search_text}
        and dword ptr [{CHAT_HISTORY_INDEX}], 0
        and dword ptr [{CHAT_SCROLL_INDEX}], 0
        call {SET_CHAT_TEXT}
        add esp, 4
    handled:
        popad; popfd
        mov al, 1
        ret
    raid:
        jmp {RAID_EVENT_HANDLER}
    """, 0x2600)

    # EAX = control. Writes two lowercase hex digits and sends the slash command.
    put("sendControl", 0x2600, f"""
        and eax, 0xff
        mov edx, eax
        shr edx, 4
        mov dl, byte ptr [{hex_digits}+edx]
        mov byte ptr [{command + control_offset}], dl
        and eax, 15
        mov al, byte ptr [{hex_digits}+eax]
        mov byte ptr [{command + control_offset + 1}], al
        push {command}
        call {SEND_SLASH_COMMAND}
        add esp, 4
        ret
    """, GEAR_REFRESH_OFFSET)

    # Called from the packet block (registers already saved). For each gear slot: the client item record's icon
    # (empty name or out-of-range template -> -1) into the slot's scalar adapter. ESI survives the stdcall setter.
    put("gearRefresh", GEAR_REFRESH_OFFSET, f"""
        xor esi, esi
    refresh_slot:
        mov eax, dword ptr [{gear_slot_table}+esi*4]
        imul eax, eax, {ITEM_STRIDE:#x}
        mov ecx, {GEAR_ICON_EMPTY}
        cmp byte ptr [eax+{ITEM_ARRAY + ITEM_NAME:#x}], 0
        je have_icon
        mov edx, dword ptr [eax+{ITEM_ARRAY + ITEM_TEMPLATE:#x}]
        cmp edx, {TEMPLATE_LIMIT:#x}
        jae have_icon
        imul edx, edx, {ICON_STRIDE:#x}
        movsx ecx, word ptr [edx+{ICON_TABLE:#x}]
    have_icon:
        mov dword ptr [{gear_scratch}], ecx
        fild dword ptr [{gear_scratch}]
        fstp dword ptr [{gear_scratch + 4}]
        mov ecx, dword ptr [{gear_icon_slots}+esi*4]
        test ecx, ecx
        jz icon_set
        push dword ptr [{gear_scratch + 4}]
        call {SET_SCALAR_VALUE}
    icon_set:
        inc esi
        cmp esi, {GEAR_SLOTS}
        jb refresh_slot
        ret
    """, GEAR_EVENT_OFFSET)

    # Gear slot click (event GEAR_EVENT_BASE + n), entered from the eventHandler with EAX = the event record. The
    # stock quiver pattern: an inventory drag (kind 9, hotspot mapped like 0x408151) or a gear pick-up (kind 0x66,
    # index = client slot) dropped here sends move(slot, source, count) and ends the drag; dropped on its own slot it
    # only ends the drag; other drag kinds do nothing. An empty cursor on a filled slot starts a kind-0x66 drag.
    put("gearEvent", GEAR_EVENT_OFFSET, f"""
        pushfd; pushad
        mov ecx, dword ptr [eax+8]
        sub ecx, {GEAR_EVENT_BASE}
        mov esi, dword ptr [{gear_slot_table}+ecx*4]
        mov edx, dword ptr [{WINDOW_MANAGER}]
        test edx, edx
        jz handled
        mov ebp, dword ptr [edx+{DRAG_RECORD:#x}]
        test ebp, ebp
        jz no_drag
        mov ebx, dword ptr [ebp+{DRAG_INDEX:#x}]
        cmp ebx, -1
        je no_drag
        mov eax, dword ptr [ebp+{DRAG_KIND:#x}]
        cmp eax, {KIND_QUIVER:#x}
        je have_source
        cmp eax, {KIND_INVENTORY}
        jne handled
        cmp ebx, 0
        jl handled
        cmp ebx, 3
        jle weapon
        cmp ebx, 10
        jl handled
        cmp ebx, 27
        jle armour
        cmp ebx, 40
        jl handled
        cmp ebx, 79
        jle have_source
        jmp handled
    weapon:
        add ebx, 10
        jmp have_source
    armour:
        add ebx, 11
    have_source:
        cmp ebx, esi
        je end_drag
        push dword ptr [edx+{DRAG_COUNT:#x}]
        push ebx
        push esi
        call {SEND_MOVE_ITEM}
        add esp, 12
    end_drag:
        call {END_DRAG}
        jmp handled
    no_drag:
        mov eax, esi
        imul eax, eax, {ITEM_STRIDE:#x}
        cmp byte ptr [eax+{ITEM_ARRAY + ITEM_NAME:#x}], 0
        je handled
        mov ecx, dword ptr [eax+{ITEM_ARRAY + ITEM_TEMPLATE:#x}]
        cmp ecx, {TEMPLATE_LIMIT:#x}
        jae handled
        imul ecx, ecx, {ICON_STRIDE:#x}
        movsx eax, word ptr [ecx+{ICON_TABLE:#x}]
        push dword ptr [{CURSOR_Y}]
        push dword ptr [{CURSOR_X}]
        push 0
        push esi
        push {KIND_QUIVER:#x}
        push edx
        call {BEGIN_DRAG}
    handled:
        popad; popfd
        mov al, 1
        ret
    """, GEAR_CODE_LIMIT)

    # OPEN BG click (event BG_EVENT_BASE + cc), entered from the eventHandler with EAX = the event record. Sends
    # "&bgsetup ui <token> <cc>" only while the server has the window shown; always returns AL = 1 (handled).
    put("bgEvent", BG_EVENT_OFFSET, f"""
        pushfd; pushad
        cmp dword ptr [{bg_active}], 1
        jne handled
        mov eax, dword ptr [eax+8]
        sub eax, {BG_EVENT_BASE}
        call {va + BG_SEND_OFFSET}
    handled:
        popad; popfd
        mov al, 1
        ret
    """, BG_SEND_OFFSET)

    # sendControl on the OPEN BG buffer. EAX = control; two lowercase hex digits, then the client's slash-command
    # sender. Callers have saved the registers.
    put("bgSend", BG_SEND_OFFSET, f"""
        and eax, 0xff
        mov edx, eax
        shr edx, 4
        mov dl, byte ptr [{hex_digits}+edx]
        mov byte ptr [{bg_command + bg_control_offset}], dl
        and eax, 15
        mov al, byte ptr [{hex_digits}+eax]
        mov byte ptr [{bg_command + bg_control_offset + 1}], al
        push {bg_command}
        call {SEND_SLASH_COMMAND}
        add esp, 4
        ret
    """, BG_CODE_LIMIT)

    data = bytearray(image)
    guard_address, guard_bytes = BG_WINDOW_GUARD
    guard_offset = pe.get_offset_from_rva(guard_address - pe.OPTIONAL_HEADER.ImageBase)
    if data[guard_offset:guard_offset + len(guard_bytes)] != guard_bytes:
        raise ValueError(f"ShowCustom5 at {guard_address:#x} does not push window {BG_WINDOW:#x}")
    for address, expected, target_offset in HOOKS:
        file_offset = pe.get_offset_from_rva(address - pe.OPTIONAL_HEADER.ImageBase)
        if data[file_offset:file_offset + len(expected)] != expected:
            raise ValueError(f"Native raid hook at {address:#x} differs")
        target = va + target_offset
        data[file_offset:file_offset + len(expected)] = (b"\xe9" + struct.pack("<i", target - address - 5)
                                                         + b"\x90" * (len(expected) - 5))
    raw = align(len(data), pe.OPTIONAL_HEADER.FileAlignment)
    size = align(len(payload), pe.OPTIONAL_HEADER.FileAlignment)
    header = pe.sections[0].get_file_offset() + 40 * pe.FILE_HEADER.NumberOfSections
    if header + 40 > min(s.PointerToRawData for s in pe.sections if s.PointerToRawData) or any(data[header:header + 40]):
        raise ValueError("No unused PE section-header slot")
    data.extend(bytes(raw + size - len(data)))
    data[raw:raw + len(payload)] = payload
    data[header:header + 40] = struct.pack("<8sIIIIIIHHI", b".cmgr\0\0\0", len(payload),
                                           rva, size, raw, 0, 0, 0, 0, 0xE0000060)
    struct.pack_into("<H", data, pe.FILE_HEADER.get_field_absolute_offset("NumberOfSections"),
                     pe.FILE_HEADER.NumberOfSections + 1)
    struct.pack_into("<I", data, pe.OPTIONAL_HEADER.get_field_absolute_offset("SizeOfImage"),
                     align(rva + len(payload), pe.OPTIONAL_HEADER.SectionAlignment))
    patched = pefile.PE(data=data)
    struct.pack_into("<I", data, pe.OPTIONAL_HEADER.get_field_absolute_offset("CheckSum"),
                     patched.generate_checksum())
    return bytes(data), {"baselineSha256": RAID_SHA256, "outputSha256": sha256(data),
                         "protocolVersion": PROTOCOL_VERSION, "sectionVa": va, "blocks": blocks,
                         "adapterSlots": slots, "activeFlag": active, "barActiveFlag": bar_active,
                         "command": command,
                         "searchText": search_text, "layout": layout_constants(),
                         "gear": {"window": {"name": "Custom6", "id": GEAR_WINDOW},
                                  "iconSlots": gear_icon_slots, "clientSlotTable": gear_slot_table,
                                  "iconAdapters": gear_icon_names(), "clientSlots": list(GEAR_CLIENT_SLOTS),
                                  "clickEvents": [str(GEAR_EVENT_BASE + index) for index in range(GEAR_SLOTS)],
                                  "nativeReturnBytes": natives},
                         "openBg": {"window": {"name": "Custom5", "id": BG_WINDOW},
                                    "activeFlag": bg_active, "command": bg_command,
                                    "clickEvents": [BG_EVENT_BASE, BG_EVENT_BASE + 0xFF],
                                    "windowGuard": {"address": guard_address, "bytes": guard_bytes.hex()},
                                    "blocks": {name: blocks[name] for name in ("bgEvent", "bgSend")}}}


def _position(parent, x, y, width, height, *, grow_width=False, grow_height=False,
              anchor_right=False, anchor_bottom=False):
    position = ET.SubElement(parent, "Position")
    ET.SubElement(position, "X").text = str(WINDOW_WIDTH - x - width if anchor_right else x)
    ET.SubElement(position, "Y").text = str(WINDOW_HEIGHT - y - height if anchor_bottom else y)
    alignment = ET.SubElement(parent, "Alignment")
    for enabled, name in ((grow_width, "GrowWidth"), (grow_height, "GrowHeight"),
                          (anchor_right, "offsetright"), (anchor_bottom, "offsetbottom")):
        if enabled:
            ET.SubElement(alignment, name).text = "true"


def _label(panel, x, y, width, color, adapter=None, text="", characters=64, height=18,
           *, grow_width=False, anchor_right=False, anchor_bottom=False,
           centered=False, native_adapter=None, color_adapter=None, font=FONT):
    """LabelDef. ``adapter`` is a manager label index; ``native_adapter`` names a stock client adapter instead."""
    if adapter is not None and native_adapter is not None:
        raise ValueError("A label takes one adapter")
    label = ET.SubElement(panel, "LabelDef")
    ET.SubElement(label, "ControlId").text = "1000"
    _position(label, x, y, width, height, grow_width=grow_width,
              anchor_right=anchor_right, anchor_bottom=anchor_bottom)
    rgba = ET.SubElement(label, "Color")
    for channel, value in zip("RGBA", (*color, 255)):
        ET.SubElement(rgba, channel).text = str(value)
    ET.SubElement(label, "FontName").text = font
    ET.SubElement(label, "Width").text = str(width)
    ET.SubElement(label, "Height").text = str(height)
    ET.SubElement(label, "ColorAdapter").text = color_adapter
    ET.SubElement(label, "MaxCharacters").text = str(characters)
    ET.SubElement(label, "Data").text = text
    ET.SubElement(label, "EndAligned").text = "false"
    ET.SubElement(label, "TextCentered").text = "true" if centered else "false"
    if adapter is not None:
        ET.SubElement(label, "Adapter").text = adapter_name(adapter)
    elif native_adapter is not None:
        ET.SubElement(label, "Adapter").text = native_adapter


def _click(panel, x, y, width, control, caption, height=18, *, grow_width=False,
           anchor_right=False, anchor_bottom=False, event=None):
    """InvisibleButtonDef. Without ``event`` it sends the manager's decimal event for ``control``."""
    button = ET.SubElement(panel, "InvisibleButtonDef")
    ET.SubElement(button, "ControlId")
    _position(button, x, y, width, height, grow_width=grow_width,
              anchor_right=anchor_right, anchor_bottom=anchor_bottom)
    ET.SubElement(button, "Label").text = caption
    ET.SubElement(button, "OnClickEvent").text = click_event(control) if event is None else event
    ET.SubElement(button, "Width").text = str(width)
    ET.SubElement(button, "Height").text = str(height)


def _status_bar(panel, x, y, width, adapter):
    """Stock StatusBarDef (red_status_thick) bound to a native adapter such as group_health<i>."""
    bar = ET.SubElement(panel, "StatusBarDef")
    ET.SubElement(bar, "ControlId")
    _position(bar, x, y, width, 12)
    ET.SubElement(bar, "TemplateName").text = "red_status_thick"
    ET.SubElement(bar, "AdapterName").text = adapter
    ET.SubElement(bar, "Width").text = str(width)


def _scalar_label(panel, x, y, width, adapter, color=HEALTH_COLOR):
    """Stock ScalarLabelDef for a native integer adapter such as group_level<i>, hidden when the value is -1."""
    label = ET.SubElement(panel, "ScalarLabelDef")
    ET.SubElement(label, "ControlId")
    _position(label, x, y, width, 18)
    ET.SubElement(label, "FontName").text = FONT
    ET.SubElement(label, "Adapter").text = adapter
    ET.SubElement(label, "ColorAdapter")
    ET.SubElement(label, "Width").text = str(width)
    ET.SubElement(label, "Height").text = "18"
    ET.SubElement(label, "MaxCharacters").text = "8"
    rgba = ET.SubElement(label, "Color")
    for channel, value in zip("RGBA", (*color, 255)):
        ET.SubElement(rgba, channel).text = str(value)
    ET.SubElement(label, "Data")
    ET.SubElement(label, "EndAligned").text = "false"
    ET.SubElement(label, "TextCentered").text = "true"
    ET.SubElement(label, "DontDrawWhenNegOne").text = "true"


def _hr_image(panel, x, y, width, template):
    """Stock HorizontalResizeImageDef, used as the divider line."""
    image = ET.SubElement(panel, "HorizontalResizeImageDef")
    position = ET.SubElement(image, "Position")
    ET.SubElement(position, "X").text = str(x)
    ET.SubElement(position, "Y").text = str(y)
    ET.SubElement(image, "TemplateName").text = template
    ET.SubElement(image, "Width").text = str(width)


def _image(panel, x, y, width, height, template, control=None, *,
           grow_width=False, grow_height=False):
    image = ET.SubElement(panel, "FullResizeImageDef")
    if control:
        ET.SubElement(image, "ControlId").text = control
    position = ET.SubElement(image, "Position")
    ET.SubElement(position, "X").text = str(x)
    ET.SubElement(position, "Y").text = str(y)
    alignment = ET.SubElement(image, "Alignment")
    if grow_width:
        ET.SubElement(alignment, "GrowWidth").text = "true"
    if grow_height:
        ET.SubElement(alignment, "GrowHeight").text = "true"
    ET.SubElement(image, "TemplateName").text = template
    ET.SubElement(image, "Width").text = str(width)
    ET.SubElement(image, "Height").text = str(height)


def _caption(name):
    return re.sub(r"(?<!^)(?=[A-Z])", " ", name)


def window():
    """Custom8 XML built from the installed raid window and stock resize behavior.

    Every interactive element is a label plus an InvisibleButtonDef with a
    custom OnClickEvent, exactly like the raid's member rows. State colours
    come from overlapping fixed-colour labels; the server fills one of them.
    """
    root = ET.Element("Root_Element", ID="DAOCUi")
    panel = ET.SubElement(root, "WindowTemplate")
    for key, value in (("Name", "custom8_window"), ("WindowId", "Custom8"), ("CloseButton", "true"),
                       ("MoveButton", "true"), ("TopRightResizeButton", "false"),
                       ("BottomRightResizeButton", "true"), ("BottomLeftResizeButton", "false"),
                       ("ResizeButtonOffsetX", "9"), ("ResizeButtonOffsetY", "0"), ("TitleWidth", "0"),
                       ("TitleHeight", "0"), ("Width", str(WINDOW_WIDTH)), ("Height", str(WINDOW_HEIGHT)),
                       ("ResizeableWidth", "6"), ("ResizeableHeight", "6"), ("ResizeableTwoWayWidth", "0"),
                       ("ResizeableTwoWayHeight", "0"), ("MinWidth", str(WINDOW_WIDTH)),
                       ("MinHeight", str(WINDOW_HEIGHT)),
                       ("ContextTemplateName", None)):
        ET.SubElement(panel, key).text = value
    pane_height = WINDOW_HEIGHT - PANE_Y - 40
    _image(panel, 0, 0, WINDOW_WIDTH, WINDOW_HEIGHT, "dlg_background_resize", "Background",
           grow_width=True, grow_height=True)
    _image(panel, LIST_X, PANE_Y, LIST_WIDTH, pane_height, "dlg_background_resize", grow_height=True)
    detail_pane_x = LIST_X + LIST_WIDTH + 6
    _image(panel, detail_pane_x, PANE_Y, WINDOW_WIDTH - detail_pane_x - 8, pane_height,
           "dlg_background_resize", grow_width=True, grow_height=True)

    _label(panel, 12, 6, 300, GOLD, text="Companion Manager", characters=32)
    for text, x, y in (("Realm:", 12, 52), ("Role:", 12, 74)):
        _label(panel, x, y, 58, MUTED, text=text, characters=8)
    for text, y in (("Group:", 52), ("Sort:", 74)):
        _label(panel, 490, y, 66, MUTED, text=text, characters=8)
    _label(panel, 416, 30, 50, MUTED, text="Rows:", characters=8)
    _label(panel, 12, 96, WIDTH_STATUS, STATUS, LABEL_STATUS, characters=120, grow_width=True)
    _label(panel, 12, WINDOW_HEIGHT - 30, WIDTH_MESSAGE, MESSAGE, LABEL_MESSAGE, characters=120,
           grow_width=True, anchor_bottom=True)
    for index, (_name, _control, x, y, width) in enumerate(TOGGLES):
        _label(panel, x, y, width, MUTED, LABEL_TOGGLE_BASE + 2 * index, characters=32)
        _label(panel, x, y, width, GOLD, LABEL_TOGGLE_BASE + 2 * index + 1, characters=32)
    for text, x in (("Name", COLUMN_NAME), ("Lv", COLUMN_LEVEL), ("Class", COLUMN_CLASS),
                    ("Type", COLUMN_TYPE), ("State", COLUMN_STATE)):
        _label(panel, x, COLUMN_HEADS_Y, 80, MUTED, text=text, characters=12)
    for row in range(ROWS):
        y = ROW_TOP + PITCH * row
        base = LABEL_ROW_BASE + ROW_STRIDE * row
        _label(panel, COLUMN_MARK, y, WIDTH_ROW_HEADER, GOLD, base, characters=64)
        for realm in range(3):
            _label(panel, COLUMN_NAME, y, WIDTH_ROW_NAME, REALM_COLORS[realm], base + 1 + realm, characters=32)
        _label(panel, COLUMN_LEVEL, y, WIDTH_ROW_LEVEL, STATUS, base + 4, characters=6)
        _label(panel, COLUMN_CLASS, y, WIDTH_ROW_CLASS, TEXT, base + 5, characters=32)
        _label(panel, COLUMN_TYPE, y, WIDTH_ROW_TYPE, MUTED, base + 6, characters=12)
        _label(panel, COLUMN_STATE, y, WIDTH_ROW_STATE, ACTIVE, base + 7, characters=24)
        _label(panel, COLUMN_STATE, y, WIDTH_ROW_STATE, MUTED, base + 8, characters=24)
    _label(panel, 300, WINDOW_HEIGHT - 52, 170, STATUS, LABEL_LIST_INDICATOR, characters=24,
           anchor_bottom=True)
    for realm in range(3):
        _label(panel, DETAIL_X, DETAIL_HEADER_Y, WIDTH_DETAIL, REALM_COLORS[realm], LABEL_HEADER_BASE + realm,
               characters=48, grow_width=True)
    _label(panel, DETAIL_X, DETAIL_SUBHEADER_Y, WIDTH_DETAIL, STATUS, LABEL_SUBHEADER,
           characters=80, grow_width=True)
    for line in range(DETAIL_LINES):
        y = DETAIL_TOP + PITCH * line
        _label(panel, DETAIL_X, y, WIDTH_DETAIL, TEXT, LABEL_DETAIL_BASE + 2 * line,
               characters=80, grow_width=True)
        _label(panel, DETAIL_X, y, WIDTH_DETAIL, LINK, LABEL_DETAIL_BASE + 2 * line + 1,
               characters=80, grow_width=True)
    _label(panel, DETAIL_X + 130, WINDOW_HEIGHT - 116, 330, STATUS, LABEL_DETAIL_INDICATOR,
           characters=40, grow_width=True, anchor_bottom=True)
    action_x = lambda action: DETAIL_X + (WIDTH_ACTION + 6) * (action % 3)
    action_y = lambda action: WINDOW_HEIGHT - 92 + PITCH * (action // 3)
    for action in range(ACTIONS):
        _label(panel, action_x(action), action_y(action), WIDTH_ACTION, GOLD, LABEL_ACTION_BASE + 2 * action,
               characters=32, anchor_bottom=True)
        _label(panel, action_x(action), action_y(action), WIDTH_ACTION, DISABLED, LABEL_ACTION_BASE + 2 * action + 1,
               characters=32, anchor_bottom=True)
    for name, text, _control, x, y, width in STATIC_LINKS:
        anchored_bottom, anchored_right = name in ANCHORED_BOTTOM, name in ANCHORED_RIGHT
        if name in SCROLL_LINK_LABELS:
            _label(panel, x, y, width, LINK, SCROLL_LINK_LABELS[name], characters=16,
                   anchor_right=anchored_right, anchor_bottom=anchored_bottom)
        else:
            _label(panel, x, y, width, LINK, text=text, characters=16,
                   anchor_right=anchored_right, anchor_bottom=anchored_bottom)

    # Click areas after every label, matching the raid's z-order.
    for name, control, x, y, width in TOGGLES:
        _click(panel, x, y, width, control, _caption(name))
    for row in range(ROWS):
        _click(panel, LIST_X + 2, ROW_TOP + PITCH * row - 2, LIST_WIDTH - 4, CONTROL_ROW_BASE + row,
               f"Companion row {row + 1}", PITCH)
    for line in range(DETAIL_LINES):
        _click(panel, DETAIL_X - 2, DETAIL_TOP + PITCH * line - 2, WIDTH_DETAIL + 4, CONTROL_DETAIL_BASE + line,
               f"Detail line {line + 1}", PITCH, grow_width=True)
    for action in range(ACTIONS):
        _click(panel, action_x(action), action_y(action), WIDTH_ACTION, CONTROL_ACTION_BASE + action,
               f"Action {action + 1}", anchor_bottom=True)
    for name, _text, control, x, y, width in STATIC_LINKS:
        _click(panel, x, y, width, control, _caption(name),
               anchor_right=name in ANCHORED_RIGHT, anchor_bottom=name in ANCHORED_BOTTOM)
    ET.indent(root)
    return ET.tostring(root, encoding="iso-8859-1", xml_declaration=True)


def bar_window():
    """Custom7 XML: the fixed 440x312 COMPANIONS panel, built from stock controls only.

    A tile is a template image, three state labels and an underline label, with one click area over it.
    The native Invite tile has fixed text and the stock GroupInvite event. Member rows are a group window:
    the native group_power, group_endurance, group_health (bar and percent), group_name with group_color,
    group_class and group_level adapters fill the row, the stock SelectGroupMember<i> event is its click
    area, and the server fills the stance (four overlapping colour labels) and the gold leader mark. The
    stance column has its own click area, which cycles the companion's saved stance.
    """
    root = ET.Element("Root_Element", ID="DAOCUi")
    panel = ET.SubElement(root, "WindowTemplate")
    for key, value in (("Name", "custom7_window"), ("WindowId", "Custom7"), ("CloseButton", "true"),
                       ("MoveButton", "true"), ("TopRightResizeButton", "false"),
                       ("BottomRightResizeButton", "false"), ("BottomLeftResizeButton", "false"),
                       ("ResizeButtonOffsetX", "0"), ("ResizeButtonOffsetY", "0"), ("TitleWidth", "0"),
                       ("TitleHeight", "0"), ("Width", str(BAR_WIDTH)), ("Height", str(BAR_HEIGHT)),
                       ("ResizeableWidth", "0"), ("ResizeableHeight", "0"), ("ResizeableTwoWayWidth", "0"),
                       ("ResizeableTwoWayHeight", "0"), ("MinWidth", "0"), ("MinHeight", "0"),
                       ("ContextTemplateName", None)):
        ET.SubElement(panel, key).text = value

    # Images first.
    _image(panel, 0, 0, BAR_WIDTH, BAR_HEIGHT, "dlg_sm_title_noresize")
    tiles = [bar_tile(index) for index in range(BAR_TILES)]
    native_tiles = [(name, caption, event, *bar_tile_at(row, column), BAR_TILE_W)
                    for name, caption, event, row, column in BAR_NATIVE_TILES]
    for _name, _control, x, y, width in tiles:
        _image(panel, x, y, width, BAR_TILE_H, BAR_TILE_TEMPLATE)
    for _name, _text, _event, x, y, width in native_tiles:
        _image(panel, x, y, width, BAR_TILE_H, BAR_TILE_TEMPLATE)
    _hr_image(panel, 8, 122, BAR_WIDTH - 16, "context_hr_image_divider")

    # Native status bars, then the labels.
    for slot in range(BAR_SLOTS):
        _status_bar(panel, BAR_COLUMN_BAR[0], BAR_SLOT_TOP + BAR_SLOT_PITCH * slot + 3, BAR_COLUMN_BAR[1],
                    f"group_health{slot}")
    _label(panel, 12, 1, 114, GOLD, text="COMPANIONS", characters=32)
    _label(panel, 130, 1, BAR_WIDTH_STATUS, STATUS, BAR_LABEL_STATUS, characters=120)
    _label(panel, 10, 100, BAR_WIDTH_MESSAGE, MESSAGE, BAR_LABEL_MESSAGE, characters=120)
    for index, (_name, _control, x, y, width) in enumerate(tiles):
        base = BAR_LABEL_TILE_BASE + BAR_TILE_STRIDE * index
        _label(panel, x, y + 3, width, TEXT, base + BAR_TILE_INACTIVE_OFFSET, characters=32, centered=True)
        _label(panel, x, y + 3, width, GOLD, base + BAR_TILE_ACTIVE_OFFSET, characters=32, centered=True)
        _label(panel, x, y + 3, width, DISABLED, base + BAR_TILE_DISABLED_OFFSET, characters=32, centered=True)
        _label(panel, x + 6, y + 8, width - 12, GOLD, base + BAR_TILE_UNDERLINE_OFFSET, characters=64,
               height=14)
    for _name, text, _event, x, y, width in native_tiles:
        _label(panel, x, y + 3, width, TEXT, text=text, characters=32, centered=True)
    for slot in range(BAR_SLOTS):
        y = BAR_SLOT_TOP + BAR_SLOT_PITCH * slot
        _scalar_label(panel, BAR_COLUMN_POWER[0], y, BAR_COLUMN_POWER[1], f"group_power{slot}", POWER_COLOR)
        _scalar_label(panel, BAR_COLUMN_ENDURANCE[0], y, BAR_COLUMN_ENDURANCE[1], f"group_endurance{slot}",
                      ENDURANCE_COLOR)
        _label(panel, BAR_COLUMN_NAME[0], y, BAR_COLUMN_NAME[1], TEXT, native_adapter=f"group_name{slot}",
               color_adapter=f"group_color{slot}", characters=24)
        _scalar_label(panel, BAR_COLUMN_HEALTH[0], y, BAR_COLUMN_HEALTH[1], f"group_health{slot}", HEALTH_COLOR)
        _label(panel, BAR_COLUMN_CLASS[0], y, BAR_COLUMN_CLASS[1], TEXT, native_adapter=f"group_class{slot}",
               characters=32)
        _scalar_label(panel, BAR_COLUMN_LEVEL[0], y, BAR_COLUMN_LEVEL[1], f"group_level{slot}")
        base = BAR_LABEL_SLOT_BASE + BAR_SLOT_STRIDE * slot
        for mode, color in enumerate(STANCE_COLORS):
            _label(panel, BAR_COLUMN_STANCE[0], y, BAR_COLUMN_STANCE[1], color, base + mode, characters=24)
        _label(panel, BAR_COLUMN_LEADER[0], y, BAR_COLUMN_LEADER[1], GOLD, base + BAR_SLOT_LEADER_OFFSET,
               characters=4)

    # Click areas last, like the raid's z-order: tiles send the manager's event, the Invite tile the stock
    # GroupInvite event, rows the stock member click, and the stance column the row's own control.
    for name, control, x, y, width in tiles:
        _click(panel, x, y, width, control, _caption(name), BAR_TILE_H)
    for name, _text, event, x, y, width in native_tiles:
        _click(panel, x, y, width, None, name, BAR_TILE_H, event=event)
    for slot in range(BAR_SLOTS):
        y = BAR_SLOT_TOP + BAR_SLOT_PITCH * slot - 1
        _click(panel, 6, y, BAR_COLUMN_STANCE[0] - 8, None, f"Group member {slot + 1}", BAR_SLOT_PITCH,
               event=f"SelectGroupMember{slot}")
        _click(panel, BAR_COLUMN_STANCE[0] - 2, y, BAR_COLUMN_STANCE[1] + 4, BAR_CONTROL_SLOT_BASE + slot,
               f"Stance of member {slot + 1}", BAR_SLOT_PITCH)
    ET.indent(root)
    return ET.tostring(root, encoding="iso-8859-1", xml_declaration=True)


def _socket(panel, x, y):
    socket = ET.SubElement(panel, "ImageAreaDef")
    ET.SubElement(socket, "ControlId").text = "1000"
    position = ET.SubElement(socket, "Position")
    ET.SubElement(position, "X").text = str(x)
    ET.SubElement(position, "Y").text = str(y)
    ET.SubElement(socket, "TemplateName").text = "icon_socket_large"


def _icon(panel, x, y, adapter):
    """IconDef as in the stock quiver window, with the large icon template; the scalar adapter holds the icon id."""
    icon = ET.SubElement(panel, "IconDef")
    ET.SubElement(icon, "ControlId").text = "1000"
    position = ET.SubElement(icon, "Position")
    ET.SubElement(position, "X").text = str(x)
    ET.SubElement(position, "Y").text = str(y)
    ET.SubElement(icon, "IconId").text = str(GEAR_ICON_EMPTY)
    ET.SubElement(icon, "TemplateName").text = "plain_icon_large"
    ET.SubElement(icon, "AdapterName").text = adapter


def gear_window():
    """Custom6 XML: the fixed COMPANION GEAR window (0.275.0 geometry) with server labels.

    Sockets, icons, labels, then the slot click areas last (the raid's z-order). Each slot's "*" mark sits in the top
    right corner of its icon; the worn captions sit under their sockets.
    """
    root = ET.Element("Root_Element", ID="DAOCUi")
    panel = ET.SubElement(root, "WindowTemplate")
    for key, value in (("Name", "custom6_window"), ("WindowId", "Custom6"), ("CloseButton", "true"),
                       ("MoveButton", "true"), ("TopRightResizeButton", "false"),
                       ("BottomRightResizeButton", "false"), ("BottomLeftResizeButton", "false"),
                       ("ResizeButtonOffsetX", "0"), ("ResizeButtonOffsetY", "0"), ("TitleWidth", "0"),
                       ("TitleHeight", "0"), ("Width", str(GEAR_WIDTH)), ("Height", str(GEAR_HEIGHT)),
                       ("ResizeableWidth", "0"), ("ResizeableHeight", "0"), ("ResizeableTwoWayWidth", "0"),
                       ("ResizeableTwoWayHeight", "0"), ("MinWidth", "0"), ("MinHeight", "0"),
                       ("ContextTemplateName", None)):
        ET.SubElement(panel, key).text = value
    _image(panel, 0, 0, GEAR_WIDTH, GEAR_HEIGHT, "dlg_sm_title_noresize")
    origins = [gear_slot_origin(index) for index in range(GEAR_SLOTS)]
    for x, y in origins:
        _socket(panel, x, y)
    for index, (x, y) in enumerate(origins):
        _icon(panel, x + 1, y + 1, GEAR_ICON_ADAPTER.format(index))
    _label(panel, 12, 1, GEAR_WIDTH_TITLE, GOLD, GEAR_LABEL_TITLE, characters=64)
    for index, (x, y) in enumerate(origins[:GEAR_WORN_SLOTS]):
        _label(panel, x + GEAR_SOCKET // 2 - GEAR_WIDTH_CAPTION // 2, y + GEAR_SOCKET + 1, GEAR_WIDTH_CAPTION, TEXT,
               GEAR_LABEL_CAPTION_BASE + index, characters=16, centered=True)
    _label(panel, GEAR_BAG_X, GEAR_BAG_Y - 20, GEAR_WIDTH_MESSAGE, GOLD, GEAR_LABEL_BACKPACK, characters=32)
    for line in range(GEAR_MESSAGE_LINES):
        _label(panel, GEAR_BAG_X, GEAR_MESSAGE_Y + 18 * line, GEAR_WIDTH_MESSAGE, MESSAGE,
               GEAR_LABEL_MESSAGE_BASE + line, characters=72)
    for line, text in enumerate(GEAR_HELP_TEXT):
        _label(panel, GEAR_BAG_X, GEAR_HELP_Y + 16 * line, GEAR_WIDTH_MESSAGE, MUTED, text=text, characters=72)
    for index, (x, y) in enumerate(origins):
        _label(panel, x + GEAR_SOCKET - GEAR_WIDTH_MARK - 1, y + 1, GEAR_WIDTH_MARK, GOLD, GEAR_LABEL_MARK_BASE + index,
               characters=2, height=14)
    captions = GEAR_WORN_CAPTIONS + tuple(f"Bag {index + 1}" for index in range(GEAR_BACKPACK_SLOTS))
    for index, (x, y, width, height) in enumerate(gear_slot_rectangles()):
        _click(panel, x, y, width, None, captions[index], height=height, event=str(GEAR_EVENT_BASE + index))
    ET.indent(root)
    return ET.tostring(root, encoding="iso-8859-1", xml_declaration=True)


# OPEN BG window geometry, 0.283.0 "Kriegstafel" (concept A of docs/superpowers/specs/2026-10-16-open-bg-window-redesign-
# analysis.md): fixed 760 x 640; title bar, crest band, banner, phase line, tab line, column heads, 18 rows every 20 px from
# y=232, hint and reply line. Paddings 8/12/16 px; every text column starts at the same x in every row.
BG_WIDTH, BG_HEIGHT = 760, 640
BG_LEFT, BG_RIGHT = 12, 748                              # text edge left and right (12 px inside the window)
BG_TITLE_Y = 1
BG_BAND = (8, 24, 744, 100)                              # inner_frame around the three realm columns
BG_REALM_PITCH, BG_CREST_Y = 248, 42                     # crests are the stock 64 x 64 templates
BG_REALM_LINES = (44, 64, 86)                            # name, detail, bar + count
BG_REALM_TEXT_WIDTH, BG_REALM_BAR_WIDTH, BG_REALM_COUNT_WIDTH = 148, 104, 38
BG_BANNER = (130, 130, 500)                              # obg_banner (25 px high), the title label 2 px lower
BG_PHASE_Y, BG_RULE_TOP_Y = 159, 179
BG_TAB_Y, BG_TABS = 184, ((12, 96), (112, 96))           # Overview, Setup plates (tab_button_normal, 17 px)
BG_PAGE_INDICATOR = (220, 160)
BG_HEADS_Y, BG_RULE_HEADS_Y = 208, 226
BG_HEADS = ((28, 128), (160, 128), (190, 98), (292, 122), (418, 80), (504, 132))
BG_ROW_TOP, BG_ROW_PITCH = 232, 20
BG_RULE_BOTTOM_Y, BG_HINT_Y, BG_REPLY_Y = 592, 596, 614
BG_REPLY_WIDTH = 600
# Click bands of a row (x, width); the band is y - 2 .. y + 18, which is also the zebra band.
BG_CLICK_FOLD, BG_CLICK_SELECT = (8, 20), (28, 260)
BG_CLICK_MINUS, BG_CLICK_PLUS, BG_CLICK_LINK = (502, 44), (546, 44), (638, 110)
# Own templates, cut from stock textures only (frames_win: the gold-framed leather bar of the character frame; page1: the
# stone ground that the stock listbox_row_bg tiles, 20 px instead of 16 so one band is exactly one row).
BG_OWN_TEMPLATES = (
    ("obg_banner", 25, (14, 24, 28), "frames_win", ((9, 61), (44, 61), (106, 61))),
    ("obg_zebra", BG_ROW_PITCH, (0, 15, 0), "page1", ((0, 0), (149, 42), (0, 0))),
)
# [Open all] / [Fold all] are server labels (the server leaves them empty on Setup); the other links are fixed text.
BG_LINK_LABELS = {BG_CONTROL_OPEN_ALL: BG_LABEL_OPEN_ALL, BG_CONTROL_FOLD_ALL: BG_LABEL_FOLD_ALL}
BG_CRESTS = ("alb_symbol", "mid_symbol", "hib_symbol")   # stock ImageAreaTemplates (realmwar_*.xml, texture realm_symbols)
BG_TITLE_FONT = "title"                                  # stock gold engraved font (assets.xml, fonts/title_24.tga)
WHITE = (255, 255, 255)
# Advance widths of the title font for '!' through '~', read from fonts/title_24.tga like the arial14 table; the server
# fits the banner text to BG_BANNER's width with it. A space has no glyph; 7 is an estimate.
TITLE_ADVANCES = (
    7, 9, 12, 12, 15, 15, 6, 9, 9, 10, 10, 6, 9, 6, 10, 12, 8, 10, 10, 13, 10, 11, 11, 11, 12, 6, 6, 10, 11, 10, 9, 15,
    13, 11, 12, 14, 10, 10, 13, 13, 6, 9, 12, 10, 18, 13, 14, 11, 16, 12, 9, 10, 12, 12, 18, 12, 12, 11, 8, 10, 8, 10, 12,
    6, 10, 11, 9, 11, 10, 8, 10, 12, 6, 8, 10, 6, 15, 11, 11, 10, 10, 7, 9, 8, 12, 10, 15, 9, 10, 10, 9, 6, 8, 11,
)
TITLE_SPACE_ADVANCE = 7


def title_width(text):
    return sum(TITLE_ADVANCES[ord(ch) - 33] if "!" <= ch <= "~" else TITLE_SPACE_ADVANCE for ch in text)


def _hr_template(root, name, height, widths, texture, pieces):
    """A HorizontalResizeImageTemplate at file level, in the stock styles.xml form."""
    template = ET.SubElement(root, "HorizontalResizeImageTemplate")
    ET.SubElement(template, "Name").text = name
    ET.SubElement(template, "Height").text = str(height)
    for key, value in zip(("LeftWidth", "RepeatWidth", "RightWidth"), widths):
        ET.SubElement(template, key).text = str(value)
    source = ET.SubElement(template, "Texture")
    ET.SubElement(source, "TextureName").text = texture
    for key, (x, y) in zip(("Left", "Repeat", "Right"), pieces):
        position = ET.SubElement(source, key)
        ET.SubElement(position, "X").text = str(x)
        ET.SubElement(position, "Y").text = str(y)


def _image_area(panel, x, y, template):
    """ImageAreaDef in the stock realmwar_alb.xml form (position and template only)."""
    image = ET.SubElement(panel, "ImageAreaDef")
    position = ET.SubElement(image, "Position")
    ET.SubElement(position, "X").text = str(x)
    ET.SubElement(position, "Y").text = str(y)
    ET.SubElement(image, "TemplateName").text = template


def open_bg_tabs():
    """(control, x, width) of the two tab plates; each carries an inactive and an active label on the same spot."""
    return ((BG_CONTROL_TAB_SITUATION, *BG_TABS[0]), (BG_CONTROL_TAB_CONTROL, *BG_TABS[1]))


def open_bg_links():
    """(name, text, control, x, y, width) of the fixed links: Open/Fold all and paging right-aligned on the tab line (two
    groups 16 px apart, 6 px between links), Refresh and Close bottom right on the reply line."""
    def chain(y, groups):
        placed, edge = [], BG_RIGHT
        for group in groups:
            for name, text, control in reversed(group):
                width = text_width(text) + 6
                placed.append((name, text, control, edge - width, y, width))
                edge -= width + 6
            edge -= 10
        return sorted(placed, key=lambda link: link[3])
    return (*chain(BG_TAB_Y + 1, ((("OpenAll", "[Open all]", BG_CONTROL_OPEN_ALL),
                                    ("FoldAll", "[Fold all]", BG_CONTROL_FOLD_ALL)),
                                   (("Up", "[Up]", BG_CONTROL_UP), ("Down", "[Down]", BG_CONTROL_DOWN),
                                    ("PageUp", "[PgUp]", BG_CONTROL_PAGE_UP), ("PageDown", "[PgDn]", BG_CONTROL_PAGE_DOWN)))[::-1]),
            *chain(BG_REPLY_Y, ((("Refresh", "[Refresh]", BG_CONTROL_REFRESH), ("Close", "[Close]", BG_CONTROL_CLOSE)),)))


def open_bg_row_slots():
    """(x, width, colour, MaxCharacters, centered) of the 18 label slots of one row, in slot order.

    Row r (0-17) has label base 633 + 18 r and sits at y = 232 + 20 r (label height 18; its click and zebra band is
    y - 2 .. y + 18). Overlaid slots share a spot and the server fills at most one of each group.

      slot  x    width colour            MaxChars  use
       0    12   14    Gold, centered     2        mark: "+" folded unit, "-" opened unit, "*" leader, ">" selected row
       1    28   460   Gold               115      title: Setup section ("Crew") (gold is for titles, marks, selection)
       2    28   720   Ink                115      plain text: unit header ("caster8 #31"), Setup setting name, full-width
                                                    message (empty states)
       3    28   128   Albion  #DC7D78    40       name (realm row "ALBION", member, solo); one of 3-5; at the title x so a
       4    28   128   Midgard #87A5EB    40       realm row aligns with the unit titles; the server indents member names
       5    28   128   Hibernia #7DC887   40       with two leading spaces
       6    160  24    Ink                4        level (left, under the "Lv" head)
       7    190  98    Ink                32       class
       8    160  128   Ash #969696        42       secondary text over level+class ("6 bots", Setup range "8-120, step 4")
       9    292  122   Field #78D782      40       state/value: alive, on, in the field; one of 9-11
      10    292  122   Blood #E66E64      40       state/value: dead, at a limit
      11    292  122   Ash                40       state/value: travelling, outside, off
      12    418  80    Dim #464646        40       bar ground: "|" x 40 (2 px each) or "=" x n (8 px each)
      13    418  80    Field              40       bar value on top of slot 12, same glyph, fewer of them
      14    504  132   Ash                44       note ("Raid", "12 min" inside); Lage only (shares its spot with 16/17)
      15    640  108   Link #F0C86E       36       link ("[Relieve]", "[Turn off]", "[Use Per realm]", "[Next: All]")
      16    502  44    Link, centered     4        "[-]" (Setup steppers, next to the bar)
      17    546  44    Link, centered     4        "[+]"

    MaxCharacters never exceeds MaximumTextLength (115); the server clips every text to the label width with the arial14
    advance table (ARIAL14_ADVANCES, SPACE_ADVANCE) and to MaxCharacters. Server text is ASCII only.
    """
    return ((12, 14, GOLD, 2, True), (28, 460, GOLD, 115, False), (28, 720, TEXT, 115, False),
            *((28, 128, color, 40, False) for color in REALM_COLORS),
            (160, 24, TEXT, 4, False), (190, 98, TEXT, 32, False), (160, 128, MUTED, 42, False),
            (292, 122, ACTIVE, 40, False), (292, 122, BLOOD, 40, False), (292, 122, MUTED, 40, False),
            (418, 80, DIM, 40, False), (418, 80, ACTIVE, 40, False),
            (504, 132, MUTED, 44, False), (640, 108, LINK, 36, False),
            (502, 44, LINK, 4, True), (546, 44, LINK, 4, True))


def open_bg_realm_labels(realm):
    """(index, x, y, width, colour, MaxCharacters) of one realm column's five labels in the crest band (realm 0-2)."""
    base = BG_LABEL_REALM_BASE + BG_REALM_STRIDE * realm
    x = BG_BAND[0] + BG_REALM_PITCH * realm + 84
    name_y, detail_y, bar_y = BG_REALM_LINES
    bar_characters = BG_REALM_BAR_WIDTH // text_width("|")
    return ((base + BG_REALM_NAME, x, name_y, BG_REALM_TEXT_WIDTH, REALM_COLORS[realm], 40),
            (base + BG_REALM_DETAIL, x, detail_y, BG_REALM_TEXT_WIDTH, MUTED, 48),
            (base + BG_REALM_BAR_DIM, x, bar_y, BG_REALM_BAR_WIDTH, DIM, bar_characters),
            (base + BG_REALM_BAR_FIELD, x, bar_y, BG_REALM_BAR_WIDTH, ACTIVE, bar_characters),
            (base + BG_REALM_COUNT, x + BG_REALM_TEXT_WIDTH - BG_REALM_COUNT_WIDTH, bar_y, BG_REALM_COUNT_WIDTH, MUTED, 8))


def open_bg_crest(realm):
    """(x, y) of a realm's 64 px crest and the (x, y, width, height) of its click area (the whole realm column)."""
    column = BG_BAND[0] + BG_REALM_PITCH * realm
    return (column + 12, BG_CREST_Y), (column + 4, BG_BAND[1] + 4, BG_REALM_PITCH - 8, BG_BAND[3] - 8)


def open_bg_window():
    """Custom5 XML: the fixed 760 x 640 OPEN BG window "Kriegstafel" (0.283.0; concept A of
    docs/superpowers/specs/2026-10-16-open-bg-window-redesign-analysis.md). Stock frame, controls and textures only; the two
    own templates obg_banner and obg_zebra cut stock textures (frames_win, page1) and come before the WindowTemplate, as
    realmwar_key.xml does. No game.dll change: labels 620-984 (365, of which 357 are used) keep operation 1.

    Layout (y): 1 title bar ("OPEN BG", status) | 24-124 crest band (inner_frame; per realm a stock 64 px crest at y=42 and
    four text lines) | 130-155 banner (obg_banner, gold title font) | 159 phase line | 179 rule | 184 tab line (two
    tab_button_normal plates, page indicator, [Open all] [Fold all], [Up] [Down] [PgUp] [PgDn]) | 208 column heads |
    226 rule | 232 + 20 r rows 0-17 (zebra obg_zebra behind every odd row) | 592 rule | 596 hint | 614 reply,
    [Refresh] [Close].

    Label adapters (contract with the server's OpenBgWindowScreen, index: x, y, width, colour, MaxCharacters):
      620       status on the title bar: 84, 1, 650, Status #C8C8C8, 115
      621-624   tabs on the plates, centered, 16 each: 621 Overview inactive (Ash) and 622 active (Gold) at 12, 185, 96;
                623 Setup inactive (Ash) and 624 active (Gold) at 112, 185, 96
      625       page indicator ("Rows 1-18 of 40"): 220, 185, 160, Ash, 40
      626       banner ("Keep 11: Albion holds it"): 130, 132, 500, white (the font's own gold), font "title", centered,
                40; fit it with TITLE_ADVANCES / TITLE_SPACE_ADVANCE (title_width) to 468 px (the banner's end caps)
      627       phase line: 12, 159, 736, Ash, centered, 115
      628       hint line (explains the selected row): 12, 596, 736, Ash, 115
      629       reply line: 12, 614, 600, Message #FFEBA0, 115
      630, 631  "[Open all]" / "[Fold all]" on the tab line (Link, 16; positions in open_bg_links()); the server fills
                them on Overview only
      632       free (registered by the client, not in the XML)
      633-956   18 rows x 18 slots, row r at y = 232 + 20 r, slot table in open_bg_row_slots()
      957-962   column heads at y=208, Ash, 24 each: 957 at 28 (128 px), 958 at 160 (128; "Lv" or the Setup "Range"),
                959 at 190 (98; "Class"; empty on Setup), 960 at 292 (122), 961 at 418 (80), 962 at 504 (132)
      963-977   crest band, realm k (0 Albion, 1 Midgard, 2 Hibernia) has base 963 + 5 k, x = 92 + 248 k:
                +0 name line ("Albion  24 crew"), y=44, 148 px, realm colour, 40
                +1 detail ("11 units, plan 10"), y=64, 148 px, Ash, 48
                +2 bar ground, y=86, 104 px, Dim, 52 ("|" x 52)   +3 bar value on top, same spot, Field, 52
                +4 count ("22 in"), x + 110, y=86, 38 px, Ash, 8
      978-984   free

    Click areas (InvisibleButtonDef, OnClickEvent = decimal 0xA00 + control; placed after every other element, the raid's
    z-order; none on the title bar, none overlapping). Row r band = y 230 + 20 r, 20 px high:
      0x00 + r  row link     x 638, 110 px (slot 15)          0x20 + r  row [-]   x 502, 44 px (slot 16)
      0x40 + r  row [+]      x 546, 44 px (slot 17)           0x60 + r  fold      x 8, 20 px (slot 0, the +/- mark)
      0x80 + r  name select  x 28, 260 px (slots 1-7; on a unit header the server treats it as fold as well)
      0xE0 / 0xE1  Overview / Setup tab plates                0xE8-0xEB  [Up] [Down] [PgUp] [PgDn]
      0xEC / 0xED  [Open all] / [Fold all]                     0xF0-0xF2  crest columns Albion, Midgard, Hibernia
      0xFD  [Close]   0xFE  [Refresh]   (0xFF is the ready control sent by operation 10, not a click)
    The row ranges shrank from 22 to 18 rows; every new control lies in the free value space 0x56-0xDF / 0xEC-0xFC.
    Hover buttons (HorizontalResizeButtonDef) are not used: their decimal OnClickEvent path is unproven in the real client.
    Fixed XML text: "OPEN BG", [Up] [Down] [PgUp] [PgDn] [Refresh] [Close]. Both skins get the same bytes.
    """
    root = ET.Element("Root_Element", ID="DAOCUi")
    for name, height, widths, texture, pieces in BG_OWN_TEMPLATES:
        _hr_template(root, name, height, widths, texture, pieces)
    panel = ET.SubElement(root, "WindowTemplate")
    for key, value in (("Name", "custom5_window"), ("WindowId", "Custom5"), ("CloseButton", "true"),
                       ("MoveButton", "true"), ("TopRightResizeButton", "false"),
                       ("BottomRightResizeButton", "false"), ("BottomLeftResizeButton", "false"),
                       ("ResizeButtonOffsetX", "0"), ("ResizeButtonOffsetY", "0"), ("TitleWidth", "0"),
                       ("TitleHeight", "0"), ("Width", str(BG_WIDTH)), ("Height", str(BG_HEIGHT)),
                       ("ResizeableWidth", "0"), ("ResizeableHeight", "0"), ("ResizeableTwoWayWidth", "0"),
                       ("ResizeableTwoWayHeight", "0"), ("MinWidth", "0"), ("MinHeight", "0"),
                       ("ContextTemplateName", None)):
        ET.SubElement(panel, key).text = value

    # Images first: frame, crest band, crests, banner, rules, tab plates, zebra.
    _image(panel, 0, 0, BG_WIDTH, BG_HEIGHT, "dlg_sm_title_noresize")
    _image(panel, *BG_BAND, "inner_frame")
    for realm, template in enumerate(BG_CRESTS):
        _image_area(panel, *open_bg_crest(realm)[0], template)
    _hr_image(panel, *BG_BANNER[:2], BG_BANNER[2], "obg_banner")
    for y in (BG_RULE_TOP_Y, BG_RULE_HEADS_Y, BG_RULE_BOTTOM_Y):
        _hr_image(panel, 8, y, BG_WIDTH - 16, "context_hr_image_divider")
    for x, width in BG_TABS:
        _hr_image(panel, x, BG_TAB_Y, width, "tab_button_normal")
    for row in range(1, BG_ROW_COUNT, 2):
        _hr_image(panel, 10, BG_ROW_TOP + BG_ROW_PITCH * row - 2, BG_WIDTH - 20, "obg_zebra")

    # Labels.
    _label(panel, BG_LEFT, BG_TITLE_Y, 66, GOLD, text="OPEN BG", characters=16)
    _label(panel, 84, BG_TITLE_Y, 650, STATUS, BG_LABEL_STATUS, characters=115)
    for realm in range(3):
        for index, x, y, width, color, characters in open_bg_realm_labels(realm):
            _label(panel, x, y, width, color, index, characters=characters)
    _label(panel, BG_BANNER[0], BG_BANNER[1] + 2, BG_BANNER[2], WHITE, BG_LABEL_BANNER, characters=40, height=24,
           centered=True, font=BG_TITLE_FONT)
    _label(panel, BG_LEFT, BG_PHASE_Y, BG_RIGHT - BG_LEFT, MUTED, BG_LABEL_PHASE, characters=115, centered=True)
    for index, (_control, x, width) in enumerate(open_bg_tabs()):
        _label(panel, x, BG_TAB_Y + 1, width, MUTED, BG_LABEL_TAB_BASE + 2 * index, characters=16, centered=True)
        _label(panel, x, BG_TAB_Y + 1, width, GOLD, BG_LABEL_TAB_BASE + 2 * index + 1, characters=16, centered=True)
    _label(panel, BG_PAGE_INDICATOR[0], BG_TAB_Y + 1, BG_PAGE_INDICATOR[1], MUTED, BG_LABEL_PAGE, characters=40)
    for _name, text, control, x, y, width in open_bg_links():
        if control in BG_LINK_LABELS:
            _label(panel, x, y, width, LINK, BG_LINK_LABELS[control], characters=16)
        else:
            _label(panel, x, y, width, LINK, text=text, characters=16)
    for head, (x, width) in enumerate(BG_HEADS):
        _label(panel, x, BG_HEADS_Y, width, MUTED, BG_LABEL_HEAD_BASE + head, characters=24)
    slots = open_bg_row_slots()
    for row in range(BG_ROW_COUNT):
        y = BG_ROW_TOP + BG_ROW_PITCH * row
        base = BG_LABEL_ROW_BASE + BG_ROW_STRIDE * row
        for slot, (x, width, color, characters, centered) in enumerate(slots):
            _label(panel, x, y, width, color, base + slot, characters=characters, centered=centered)
    _label(panel, BG_LEFT, BG_HINT_Y, BG_RIGHT - BG_LEFT, MUTED, BG_LABEL_HINT, characters=115)
    _label(panel, BG_LEFT, BG_REPLY_Y, BG_REPLY_WIDTH, MESSAGE, BG_LABEL_REPLY, characters=115)

    # Click areas last, like the raid's z-order.
    event = lambda control: str(BG_EVENT_BASE + control)
    for control, x, width in open_bg_tabs():
        _click(panel, x, BG_TAB_Y, width, None, "Overview tab" if control == BG_CONTROL_TAB_SITUATION else "Setup tab",
               event=event(control))
    for name, _text, control, x, y, width in open_bg_links():
        _click(panel, x, y, width, None, _caption(name), event=event(control))
    for realm, name in enumerate(("Albion", "Midgard", "Hibernia")):
        x, y, width, height = open_bg_crest(realm)[1]
        _click(panel, x, y, width, None, f"{name} crest", height, event=event(BG_CONTROL_CREST_BASE + realm))
    for row in range(BG_ROW_COUNT):
        y = BG_ROW_TOP + BG_ROW_PITCH * row - 2
        for base, (x, width), caption in ((BG_CONTROL_FOLD_BASE, BG_CLICK_FOLD, "Row fold"),
                                          (BG_CONTROL_SELECT_BASE, BG_CLICK_SELECT, "Row select"),
                                          (BG_CONTROL_MINUS_BASE, BG_CLICK_MINUS, "Row minus"),
                                          (BG_CONTROL_PLUS_BASE, BG_CLICK_PLUS, "Row plus"),
                                          (BG_CONTROL_LINK_BASE, BG_CLICK_LINK, "Row link")):
            _click(panel, x, y, width, None, f"{caption} {row + 1}", BG_ROW_PITCH, event=event(base + row))
    ET.indent(root)
    return ET.tostring(root, encoding="iso-8859-1", xml_declaration=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--client", type=Path, required=True, help="Verified client app directory (read only)")
    parser.add_argument("--output", type=Path, required=True, help="New staging directory")
    args = parser.parse_args()
    if args.output.exists():
        parser.error("Output directory already exists")
    # The UI checks run before the game.dll is read, so a foreign window is reported whatever the game.dll is.
    main_xml = (args.client / "ui/uimain.xml").read_bytes()
    if b"custom5_window.xml" in main_xml:
        parser.error("custom5_window.xml is already included in uimain.xml")
    if any(name in main_xml for name in (b"custom8_window.xml", b"custom7_window.xml", b"custom6_window.xml")) or \
            b"</XML>" not in main_xml:
        parser.error("Unexpected UI include state")
    for skin in ("atlantis", "isles"):
        for window_file in ("custom8_window.xml", "custom7_window.xml", "custom6_window.xml", "custom5_window.xml"):
            if (args.client / "ui" / skin / window_file).exists():
                parser.error(f"{window_file} is already in use in {skin}")
    original = (args.client / "game.dll").read_bytes()
    try:
        patched, report = build(original)
    except ValueError as error:
        parser.error(str(error))
    # The manager-only UI (0.169.0), the manager plus COMPANIONS window (0.251.0-0.272.0), the manager plus both
    # windows (0.276.0; byte-identical to the 0.273.0-0.275.0 paperdoll probe UI) and the 0.282.0 UI with the OPEN BG
    # window differ only in the include list.
    manager_only = main_xml.replace(b"</XML>", b"\t<Include>custom8_window.xml</Include>\r\n</XML>")
    with_bar = main_xml.replace(b"</XML>", b"\t<Include>custom8_window.xml</Include>\r\n"
                                b"\t<Include>custom7_window.xml</Include>\r\n</XML>")
    with_gear = with_bar.replace(b"</XML>", b"\t<Include>custom6_window.xml</Include>\r\n</XML>")
    output_main = with_gear.replace(b"</XML>", b"\t<Include>custom5_window.xml</Include>\r\n</XML>")
    xml = window()
    bar_xml = bar_window()
    gear_xml = gear_window()
    bg_xml = open_bg_window()
    args.output.mkdir(parents=True)
    (args.output / "game.dll").write_bytes(patched)
    (args.output / "uimain.xml").write_bytes(output_main)
    for skin in ("atlantis", "isles"):
        (args.output / skin).mkdir()
        (args.output / skin / "custom8_window.xml").write_bytes(xml)
        (args.output / skin / "custom7_window.xml").write_bytes(bar_xml)
        (args.output / skin / "custom6_window.xml").write_bytes(gear_xml)
        (args.output / skin / "custom5_window.xml").write_bytes(bg_xml)
    report["uimainBaselineSha256"] = sha256(main_xml)
    report["uimainOutputSha256"] = sha256(output_main)
    report["uimainManagerOnlySha256"] = sha256(manager_only)
    report["uimainBarSha256"] = sha256(with_bar)
    report["uimainGearSha256"] = sha256(with_gear)
    report["uimainBgSha256"] = sha256(output_main)
    report["windowSha256"] = sha256(xml)
    report["barWindowSha256"] = sha256(bar_xml)
    report["gearWindowSha256"] = sha256(gear_xml)
    report["bgWindowSha256"] = sha256(bg_xml)
    report["gear"]["slotRectangles"] = gear_slot_rectangles()
    report["status"] = ("staged Companion Manager client with the COMPANIONS, COMPANION GEAR and OPEN BG windows; "
                        "offline emulation only until the owner's real-client check passes")
    (args.output / "manifest.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: report[key] for key in ("baselineSha256", "outputSha256", "windowSha256",
                                                   "barWindowSha256", "gearWindowSha256", "bgWindowSha256",
                                                   "uimainOutputSha256", "uimainManagerOnlySha256", "uimainBarSha256",
                                                   "uimainGearSha256", "uimainBgSha256", "protocolVersion")},
                     indent=2))


if __name__ == "__main__":
    main()
