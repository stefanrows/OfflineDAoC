"""Stage the Companion Manager Custom8 extension from the verified native-raid client.

The builder writes a fresh staging directory and never modifies the input
client. It accepts only the verified raid-patched Windows x86 game.dll.

Protocol version 2 replaces the failed probe paths:

* Server to client: fixed 128-byte DebugMode bodies with marker 0x43 update
  registered label adapters, show or hide Custom8, and set the view token.
* Client to server: manager clicks call the client's own slash-command sender
  (the path used by its context menu for /talk and /loco) with
  ``&companions ui <token> <control>``. No new packet opcode is needed.
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
RAID_EVENT_NAME = 0x2483000
RAID_EVENT_HANDLER = 0x2485000
HOOKS = (
    (0x4DA938, bytes.fromhex("e9 c3 36 fa 01 90 90 90"), 0x0000),
    (0x411201, bytes.fromhex("e9 fa 0d 07 02 90 90 90"), 0x1000),
    (0x4E99E6, bytes.fromhex("e9 15 96 f9 01"), 0x1800),
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

CUSTOM8_WINDOW = 0x75
MARKER = 0x43
PROTOCOL_VERSION = 2
BODY_SIZE = 128
TEXT_OFFSET = 12
EVENT_BASE = 0x700
CONTROL_LIMIT = 0xC0
SEARCH_CONTROL = 0xB0
READY_CONTROL = 0xBE
COMMAND_PREFIX = "&companions ui "
SEARCH_PREFIX = "/companions find "
OP_LABEL, OP_SHOW, OP_HIDE, OP_TOKEN = 1, 2, 3, 4

WINDOW_WIDTH = 640
WINDOW_HEIGHT = 420
ROWS = 12
DETAIL_LINES = 11
ACTIONS = 6
# Label widths in pixels; the server fits text to them using arial11 advances.
WIDTH_STATUS = 616
WIDTH_MESSAGE = 556
WIDTH_ROW_NAME = 110
WIDTH_ROW_INFO = 158
WIDTH_DETAIL = 312
WIDTH_ACTION = 104

GOLD = (255, 210, 90)
MUTED = (150, 150, 150)
TEXT = (225, 225, 225)
LINK = (240, 200, 110)
STATUS = (200, 200, 200)
MESSAGE = (255, 235, 160)
DISABLED = (115, 115, 115)
REALM_COLORS = ((220, 125, 120), (135, 165, 235), (125, 200, 135))  # Albion, Midgard, Hibernia

# name, control, x, y, width
TOGGLES = (
    ("TabRoster", 0x30, 12, 28, 90),
    ("TabRecruit", 0x31, 104, 28, 90),
    ("DetailOverview", 0x38, 312, 110, 76),
    ("DetailTraining", 0x39, 390, 110, 132),
    ("DetailGear", 0x3A, 524, 110, 60),
    ("RealmAll", 0x40, 58, 48, 76),
    ("RealmAlbion", 0x41, 136, 48, 64),
    ("RealmMidgard", 0x42, 202, 48, 68),
    ("RealmHibernia", 0x43, 272, 48, 72),
    ("RoleAny", 0x48, 58, 66, 70),
    ("RoleTank", 0x49, 130, 66, 50),
    ("RoleHealer", 0x4A, 182, 66, 56),
    ("RoleBuffer", 0x4B, 240, 66, 56),
    ("RoleAttacker", 0x4C, 298, 66, 70),
)
# protocol name, text, control, x, y, width
STATIC_LINKS = (
    ("Search", "[Search]", SEARCH_CONTROL, 448, 28, 60),
    ("Clear", "[Clear]", 0x58, 512, 28, 52),
    ("Refresh", "[Refresh]", 0x5E, 568, 28, 60),
    ("ListUp", "[Up]", 0x50, 14, 358, 34),
    ("ListDown", "[Down]", 0x51, 50, 358, 46),
    ("ListPageUp", "[PgUp]", 0x52, 100, 358, 48),
    ("ListPageDown", "[PgDn]", 0x53, 150, 358, 48),
    ("DetailUp", "[Up]", 0x54, 312, 332, 34),
    ("DetailDown", "[Down]", 0x55, 348, 332, 46),
    ("Close", "[Close]", 0x5F, 572, 396, 56),
)

LABEL_STATUS = 0
LABEL_MESSAGE = 1
LABEL_TOGGLE_BASE = 2                    # +2k inactive, +2k+1 active
LABEL_ROW_BASE = LABEL_TOGGLE_BASE + 2 * len(TOGGLES)
ROW_STRIDE = 5                           # marker, Albion, Midgard, Hibernia, info
LABEL_LIST_INDICATOR = LABEL_ROW_BASE + ROW_STRIDE * ROWS
LABEL_HEADER_BASE = LABEL_LIST_INDICATOR + 1   # Albion, Midgard, Hibernia
LABEL_SUBHEADER = LABEL_HEADER_BASE + 3
LABEL_DETAIL_BASE = LABEL_SUBHEADER + 1  # +2j text, +2j+1 link
LABEL_DETAIL_INDICATOR = LABEL_DETAIL_BASE + 2 * DETAIL_LINES
LABEL_ACTION_BASE = LABEL_DETAIL_INDICATOR + 1  # +2k enabled, +2k+1 disabled
LABEL_COUNT = LABEL_ACTION_BASE + 2 * ACTIONS

CONTROL_ROW_BASE = 0x00
CONTROL_DETAIL_BASE = 0x10
CONTROL_ACTION_BASE = 0x20


def layout_constants():
    """Numbers shared with the server; exported to the manifest and checked by the test."""
    values = {
        "Marker": MARKER, "ProtocolVersion": PROTOCOL_VERSION, "BodySize": BODY_SIZE,
        "TextOffset": TEXT_OFFSET, "MaximumTextLength": BODY_SIZE - TEXT_OFFSET - 1,
        "OpLabel": OP_LABEL, "OpShow": OP_SHOW, "OpHide": OP_HIDE, "OpToken": OP_TOKEN,
        "Rows": ROWS, "DetailLines": DETAIL_LINES, "Actions": ACTIONS,
        "LabelStatus": LABEL_STATUS, "LabelMessage": LABEL_MESSAGE,
        "LabelToggleBase": LABEL_TOGGLE_BASE, "LabelRowBase": LABEL_ROW_BASE, "RowStride": ROW_STRIDE,
        "LabelListIndicator": LABEL_LIST_INDICATOR, "LabelHeaderBase": LABEL_HEADER_BASE,
        "LabelSubheader": LABEL_SUBHEADER, "LabelDetailBase": LABEL_DETAIL_BASE,
        "LabelDetailIndicator": LABEL_DETAIL_INDICATOR, "LabelActionBase": LABEL_ACTION_BASE,
        "LabelCount": LABEL_COUNT, "ControlRowBase": CONTROL_ROW_BASE,
        "ControlDetailBase": CONTROL_DETAIL_BASE, "ControlActionBase": CONTROL_ACTION_BASE,
        "ControlLimit": CONTROL_LIMIT, "ControlSearch": SEARCH_CONTROL, "ControlReady": READY_CONTROL,
        "WidthStatus": WIDTH_STATUS, "WidthMessage": WIDTH_MESSAGE, "WidthRowName": WIDTH_ROW_NAME,
        "WidthRowInfo": WIDTH_ROW_INFO, "WidthDetail": WIDTH_DETAIL, "WidthAction": WIDTH_ACTION,
    }
    for index, (name, control, *_rest) in enumerate(TOGGLES):
        values["Control" + name] = control
        values["Toggle" + name] = index
    for name, _text, control, *_rest in STATIC_LINKS:
        values["Control" + name] = control
    return values


def adapter_name(index):
    return f"cmgr_{index:03d}"


def event_name(control):
    return f"CompMgr{control:02X}"


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
    names = search_text + 32
    payload = bytearray(data_base + 4 * LABEL_COUNT + 16 + 32 + 16 + 32 + 16 * LABEL_COUNT)
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
    registrations = "\n".join(
        f"push {names + 16 * index}; push {slots + 4 * index}; push edi; call {REGISTER_TEXT_ADAPTER}"
        for index in range(LABEL_COUNT))
    put("init", 0x0000, f"""
        pushfd; pushad
        mov dword ptr [{active}], 0
        {registrations}
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
        movzx ecx, byte ptr [esi+4]
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

    # ESI is the event name. Only CompMgr00..CompMgrBF map into 0x700..0x7BF.
    prefix_checks = "\n".join(
        f"cmp byte ptr [esi+{index}], {ord(char)}; jne raid" for index, char in enumerate("CompMgr"))
    put("eventName", 0x1800, f"""
        {prefix_checks}
        movzx eax, byte ptr [esi+7]
        call nibble
        cmp eax, 15
        ja raid
        mov edx, eax
        shl edx, 4
        movzx eax, byte ptr [esi+8]
        call nibble
        cmp eax, 15
        ja raid
        or edx, eax
        cmp byte ptr [esi+9], 0
        jne raid
        cmp edx, {CONTROL_LIMIT}
        jae raid
        lea eax, [edx+{EVENT_BASE}]
        ret
    nibble:
        sub eax, 0x30
        cmp eax, 9
        jbe nibble_done
        sub eax, 7
        cmp eax, 10
        jb nibble_bad
        cmp eax, 15
        jbe nibble_done
    nibble_bad:
        or eax, -1
    nibble_done:
        ret
    raid:
        jmp {RAID_EVENT_NAME}
    """, 0x1C00)

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


def _position(parent, x, y):
    position = ET.SubElement(parent, "Position")
    ET.SubElement(position, "X").text = str(x)
    ET.SubElement(position, "Y").text = str(y)
    ET.SubElement(parent, "Alignment").text = ""


def _label(panel, x, y, width, color, adapter=None, text="", characters=64, height=16):
    label = ET.SubElement(panel, "LabelDef")
    ET.SubElement(label, "ControlId").text = "1000"
    _position(label, x, y)
    rgba = ET.SubElement(label, "Color")
    for channel, value in zip("RGBA", (*color, 255)):
        ET.SubElement(rgba, channel).text = str(value)
    ET.SubElement(label, "FontName").text = "arial11"
    ET.SubElement(label, "Width").text = str(width)
    ET.SubElement(label, "Height").text = str(height)
    ET.SubElement(label, "ColorAdapter")
    ET.SubElement(label, "MaxCharacters").text = str(characters)
    ET.SubElement(label, "Data").text = text
    ET.SubElement(label, "EndAligned").text = "false"
    ET.SubElement(label, "TextCentered").text = "false"
    if adapter is not None:
        ET.SubElement(label, "Adapter").text = adapter_name(adapter)


def _click(panel, x, y, width, control, caption, height=16):
    button = ET.SubElement(panel, "InvisibleButtonDef")
    ET.SubElement(button, "ControlId")
    _position(button, x, y)
    ET.SubElement(button, "Label").text = caption
    ET.SubElement(button, "OnClickEvent").text = event_name(control)
    ET.SubElement(button, "Width").text = str(width)
    ET.SubElement(button, "Height").text = str(height)


def _image(panel, x, y, width, height, template, control=None):
    image = ET.SubElement(panel, "FullResizeImageDef")
    if control:
        ET.SubElement(image, "ControlId").text = control
    position = ET.SubElement(image, "Position")
    ET.SubElement(position, "X").text = str(x)
    ET.SubElement(position, "Y").text = str(y)
    alignment = ET.SubElement(image, "Alignment")
    ET.SubElement(alignment, "TopLeft").text = "true"
    ET.SubElement(image, "TemplateName").text = template
    ET.SubElement(image, "Width").text = str(width)
    ET.SubElement(image, "Height").text = str(height)


def window():
    """Custom8 XML built only from controls proven by the installed raid window.

    Every interactive element is a label plus an InvisibleButtonDef with a
    custom OnClickEvent, exactly like the raid's member rows. State colours
    come from overlapping fixed-colour labels; the server fills one of them.
    """
    root = ET.Element("Root_Element", ID="DAOCUi")
    panel = ET.SubElement(root, "WindowTemplate")
    for key, value in (("Name", "custom8_window"), ("WindowId", "Custom8"), ("CloseButton", "true"),
                       ("MoveButton", "true"), ("TopRightResizeButton", "false"),
                       ("BottomRightResizeButton", "false"), ("BottomLeftResizeButton", "false"),
                       ("ResizeButtonOffsetX", "0"), ("ResizeButtonOffsetY", "0"), ("TitleWidth", "0"),
                       ("TitleHeight", "0"), ("Width", str(WINDOW_WIDTH)), ("Height", str(WINDOW_HEIGHT)),
                       ("ResizeableWidth", "0"), ("ResizeableHeight", "0"), ("ResizeableTwoWayWidth", "0"),
                       ("ResizeableTwoWayHeight", "0"), ("MinWidth", "0"), ("MinHeight", "0"),
                       ("ContextTemplateName", None)):
        ET.SubElement(panel, key).text = value
    _image(panel, 0, 0, WINDOW_WIDTH, WINDOW_HEIGHT, "dlg_sm_title_noresize", "Background")
    _image(panel, 8, 104, 294, 280, "dlg_lg_title_noresize")
    _image(panel, 306, 104, 326, 286, "dlg_lg_title_noresize")

    _label(panel, 12, 6, 300, GOLD, text="Companion Manager", characters=32)
    _label(panel, 12, 48, 44, MUTED, text="Realm:", characters=8)
    _label(panel, 12, 66, 44, MUTED, text="Role:", characters=8)
    _label(panel, 12, 86, WIDTH_STATUS, STATUS, LABEL_STATUS, characters=120)
    _label(panel, 12, 396, WIDTH_MESSAGE, MESSAGE, LABEL_MESSAGE, characters=120)
    for index, (name, _control, x, y, width) in enumerate(TOGGLES):
        _label(panel, x, y, width, MUTED, LABEL_TOGGLE_BASE + 2 * index, characters=32)
        _label(panel, x, y, width, GOLD, LABEL_TOGGLE_BASE + 2 * index + 1, characters=32)
    for row in range(ROWS):
        y = 112 + 20 * row
        base = LABEL_ROW_BASE + ROW_STRIDE * row
        _label(panel, 14, y, 12, GOLD, base, characters=4)
        for realm in range(3):
            _label(panel, 28, y, WIDTH_ROW_NAME, REALM_COLORS[realm], base + 1 + realm, characters=32)
        _label(panel, 140, y, WIDTH_ROW_INFO, STATUS, base + 4, characters=40)
    _label(panel, 202, 358, 94, STATUS, LABEL_LIST_INDICATOR, characters=24)
    for realm in range(3):
        _label(panel, 312, 130, WIDTH_DETAIL, REALM_COLORS[realm], LABEL_HEADER_BASE + realm, characters=48)
    _label(panel, 312, 146, WIDTH_DETAIL, STATUS, LABEL_SUBHEADER, characters=80)
    for line in range(DETAIL_LINES):
        y = 166 + 15 * line
        _label(panel, 312, y, WIDTH_DETAIL, TEXT, LABEL_DETAIL_BASE + 2 * line, characters=80)
        _label(panel, 312, y, WIDTH_DETAIL, LINK, LABEL_DETAIL_BASE + 2 * line + 1, characters=80)
    _label(panel, 400, 332, 222, STATUS, LABEL_DETAIL_INDICATOR, characters=32)
    for action in range(ACTIONS):
        x, y = 312 + 106 * (action % 3), 352 + 18 * (action // 3)
        _label(panel, x, y, WIDTH_ACTION, GOLD, LABEL_ACTION_BASE + 2 * action, characters=32)
        _label(panel, x, y, WIDTH_ACTION, DISABLED, LABEL_ACTION_BASE + 2 * action + 1, characters=32)
    for _name, text, _control, x, y, width in STATIC_LINKS:
        _label(panel, x, y, width, LINK, text=text, characters=16)

    # Click areas after every label, matching the raid's z-order.
    for name, control, x, y, width in TOGGLES:
        _click(panel, x, y, width, control, re.sub(r"(?<!^)(?=[A-Z])", " ", name))
    for row in range(ROWS):
        _click(panel, 12, 112 + 20 * row, 286, CONTROL_ROW_BASE + row, f"Companion row {row + 1}", 18)
    for line in range(DETAIL_LINES):
        _click(panel, 310, 166 + 15 * line, 316, CONTROL_DETAIL_BASE + line, f"Detail line {line + 1}", 15)
    for action in range(ACTIONS):
        x, y = 312 + 106 * (action % 3), 352 + 18 * (action // 3)
        _click(panel, x, y, WIDTH_ACTION, CONTROL_ACTION_BASE + action, f"Action {action + 1}")
    for name, _text, control, x, y, width in STATIC_LINKS:
        _click(panel, x, y, width, control, re.sub(r"(?<!^)(?=[A-Z])", " ", name))
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
