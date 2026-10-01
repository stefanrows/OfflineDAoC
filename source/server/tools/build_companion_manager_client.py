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
SCROLL_LINK_LABELS = {"DetailUp": LABEL_DETAIL_UP, "DetailDown": LABEL_DETAIL_DOWN}

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


def build(image):
    if sha256(image) != RAID_SHA256:
        raise ValueError("Unknown game.dll: expected the verified native-raid client")
    pe = pefile.PE(data=image)
    if pe.OPTIONAL_HEADER.ImageBase != 0x400000 or pe.FILE_HEADER.Machine != 0x14C:
        raise ValueError("Expected the verified Windows x86 client")
    if pe.sections[-1].Name.rstrip(b"\0") != b".raid":
        raise ValueError("Native raid section was changed")
    align = lambda value, unit: (value + unit - 1) // unit * unit
    last = pe.sections[-1]
    rva = align(last.VirtualAddress + max(last.Misc_VirtualSize, last.SizeOfRawData),
                pe.OPTIONAL_HEADER.SectionAlignment)
    va = pe.OPTIONAL_HEADER.ImageBase + rva

    data_base = 0x4000
    slots = va + data_base
    active = slots + 4 * LABEL_COUNT
    command = active + 16
    hex_digits = command + 32
    search_text = hex_digits + 16
    counter = search_text + 32
    names = counter + 16
    payload = bytearray(data_base + 4 * LABEL_COUNT + 16 + 32 + 16 + 32 + 16 + 16 * LABEL_COUNT)
    offset = lambda address: address - va
    initial_command = (COMMAND_PREFIX + "0000 00").encode("ascii") + b"\0"
    payload[offset(command):offset(command) + len(initial_command)] = initial_command
    payload[offset(hex_digits):offset(hex_digits) + 16] = b"0123456789abcdef"
    encoded_search = SEARCH_PREFIX.encode("ascii") + b"\0"
    payload[offset(search_text):offset(search_text) + len(encoded_search)] = encoded_search
    for index in range(LABEL_COUNT):
        encoded = adapter_name(index).encode("ascii") + b"\0"
        payload[offset(names) + 16 * index:offset(names) + 16 * index + len(encoded)] = encoded
    token_offset = len(COMMAND_PREFIX)
    control_offset = token_offset + 5

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
        cmp dword ptr [{counter}], {LABEL_COUNT}
        jb register
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
        jmp done
    label:
        movzx ecx, word ptr [esi+4]
        cmp ecx, {LABEL_COUNT}
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

    # EAX is the event record and [EAX+8] its ID; unrelated IDs keep the raid chain intact.
    put("eventHandler", 0x1C00, f"""
        cmp dword ptr [eax+8], {EVENT_BASE}
        jb raid
        cmp dword ptr [eax+8], {EVENT_BASE + CONTROL_LIMIT}
        jae raid
        pushfd; pushad
        mov ecx, dword ptr [eax+8]
        sub ecx, {EVENT_BASE}
        cmp dword ptr [{active}], 1
        jne handled
        cmp ecx, {SEARCH_CONTROL}
        je search
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
    """, 0x2800)

    data = bytearray(image)
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
                         "adapterSlots": slots, "activeFlag": active, "command": command,
                         "searchText": search_text, "layout": layout_constants()}


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
           *, grow_width=False, anchor_right=False, anchor_bottom=False):
    label = ET.SubElement(panel, "LabelDef")
    ET.SubElement(label, "ControlId").text = "1000"
    _position(label, x, y, width, height, grow_width=grow_width,
              anchor_right=anchor_right, anchor_bottom=anchor_bottom)
    rgba = ET.SubElement(label, "Color")
    for channel, value in zip("RGBA", (*color, 255)):
        ET.SubElement(rgba, channel).text = str(value)
    ET.SubElement(label, "FontName").text = FONT
    ET.SubElement(label, "Width").text = str(width)
    ET.SubElement(label, "Height").text = str(height)
    ET.SubElement(label, "ColorAdapter")
    ET.SubElement(label, "MaxCharacters").text = str(characters)
    ET.SubElement(label, "Data").text = text
    ET.SubElement(label, "EndAligned").text = "false"
    ET.SubElement(label, "TextCentered").text = "false"
    if adapter is not None:
        ET.SubElement(label, "Adapter").text = adapter_name(adapter)


def _click(panel, x, y, width, control, caption, height=18, *, grow_width=False,
           anchor_right=False, anchor_bottom=False):
    button = ET.SubElement(panel, "InvisibleButtonDef")
    ET.SubElement(button, "ControlId")
    _position(button, x, y, width, height, grow_width=grow_width,
              anchor_right=anchor_right, anchor_bottom=anchor_bottom)
    ET.SubElement(button, "Label").text = caption
    ET.SubElement(button, "OnClickEvent").text = click_event(control)
    ET.SubElement(button, "Width").text = str(width)
    ET.SubElement(button, "Height").text = str(height)


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


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--client", type=Path, required=True, help="Verified client app directory (read only)")
    parser.add_argument("--output", type=Path, required=True, help="New staging directory")
    args = parser.parse_args()
    if args.output.exists():
        parser.error("Output directory already exists")
    original = (args.client / "game.dll").read_bytes()
    try:
        patched, report = build(original)
    except ValueError as error:
        parser.error(str(error))
    main_xml = (args.client / "ui/uimain.xml").read_bytes()
    if b"custom8_window.xml" in main_xml or b"</XML>" not in main_xml:
        parser.error("Unexpected UI include state")
    output_main = main_xml.replace(b"</XML>", b"\t<Include>custom8_window.xml</Include>\r\n</XML>")
    for skin in ("atlantis", "isles"):
        if (args.client / "ui" / skin / "custom8_window.xml").exists():
            parser.error(f"Custom8 is already in use in {skin}")
    xml = window()
    args.output.mkdir(parents=True)
    (args.output / "game.dll").write_bytes(patched)
    (args.output / "uimain.xml").write_bytes(output_main)
    for skin in ("atlantis", "isles"):
        (args.output / skin).mkdir()
        (args.output / skin / "custom8_window.xml").write_bytes(xml)
    report["uimainBaselineSha256"] = sha256(main_xml)
    report["uimainOutputSha256"] = sha256(output_main)
    report["windowSha256"] = sha256(xml)
    report["status"] = ("staged Companion Manager client; offline emulation only until the owner's "
                        "combined real-client gate passes")
    (args.output / "manifest.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: report[key] for key in ("baselineSha256", "outputSha256", "windowSha256",
                                                   "uimainOutputSha256", "protocolVersion")}, indent=2))


if __name__ == "__main__":
    main()
