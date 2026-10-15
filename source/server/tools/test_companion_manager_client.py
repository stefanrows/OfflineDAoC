"""Offline x86 checks for the staged Companion Manager client and raid passthrough.

Emulation proves the hook logic, validation, and call sequences only. It is
not real-client acceptance; the owner's combined in-game check is required.
"""

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import sys
import xml.etree.ElementTree as ET

import pefile
from unicorn import Uc, UC_ARCH_X86, UC_HOOK_CODE, UC_MODE_32
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EBP, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX
from unicorn.x86_const import UC_X86_REG_EDI, UC_X86_REG_EIP, UC_X86_REG_ESI, UC_X86_REG_ESP

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_companion_manager_client as builder


parser = argparse.ArgumentParser()
parser.add_argument("--stage", type=Path, required=True)
args = parser.parse_args()
manifest = json.loads((args.stage / "manifest.json").read_text(encoding="utf-8"))
image = (args.stage / "game.dll").read_bytes()
assert hashlib.sha256(image).hexdigest() == manifest["outputSha256"]
assert manifest["baselineSha256"] == builder.RAID_SHA256
assert manifest["layout"] == builder.layout_constants()
pe = pefile.PE(data=image)
assert pe.sections[-1].Name.rstrip(b"\0") == b".cmgr"
assert pe.sections[-2].Name.rstrip(b"\0") == b".raid"
layout = manifest["layout"]
LABELS = layout["LabelCount"]
TOTAL = layout["TotalLabelCount"]

uc = Uc(UC_ARCH_X86, UC_MODE_32)
uc.mem_map(0x400000, 0x2100000)
for section in pe.sections:
    if section.SizeOfRawData:
        uc.mem_write(pe.OPTIONAL_HEADER.ImageBase + section.VirtualAddress, section.get_data())
uc.mem_map(0x5000000, 0x10000)
uc.mem_map(0x6000000, 0x40000)
sp, frame, body, sentinel = 0x5008000, 0x5008100, 0x6030000, 0x603F000
labels, texts, windows, commands, chat = [], {}, [], [], []
scalars, setter_calls, moves, drags, end_drags = {}, [], [], [], []


def read32(address):
    return struct.unpack("<I", uc.mem_read(address, 4))[0]


def cstring(address, limit=256):
    return bytes(uc.mem_read(address, limit)).split(b"\0")[0].decode("ascii")


def ret(cleanup=0):
    stack = uc.reg_read(UC_X86_REG_ESP)
    address = read32(stack)
    uc.reg_write(UC_X86_REG_ESP, stack + 4 + cleanup)
    uc.reg_write(UC_X86_REG_EIP, address)


def clobber(machine):
    """Natives may change EAX, ECX and EDX; the patch must not rely on them afterwards."""
    for register in (UC_X86_REG_EAX, UC_X86_REG_ECX, UC_X86_REG_EDX):
        machine.reg_write(register, 0xDEADBEEF)


def stub(machine, address, size, user):
    stack = machine.reg_read(UC_X86_REG_ESP)
    if address in (builder.REGISTER_SCALAR_ADAPTER, builder.REGISTER_TEXT_ADAPTER):
        slot = read32(stack + 8)
        label = cstring(read32(stack + 12))
        obj = 0x6000000 + 48 * len(labels)
        machine.mem_write(slot, struct.pack("<I", obj))
        labels.append(label)
        if address == builder.REGISTER_SCALAR_ADAPTER:
            scalars[label] = (obj, read32(stack + 16), read32(stack + 20), read32(stack + 4))
        ret(20 if address == builder.REGISTER_SCALAR_ADAPTER else 12)
    elif address == builder.SET_SCALAR_VALUE:
        setter_calls.append((machine.reg_read(UC_X86_REG_ECX), struct.unpack("<f", machine.mem_read(stack + 4, 4))[0]))
        clobber(machine)
        ret(4)
    elif address == builder.SEND_MOVE_ITEM:
        moves.append(tuple(read32(stack + 4 * index) for index in (1, 2, 3)))
        clobber(machine)
        ret()
    elif address == builder.BEGIN_DRAG:
        drags.append((machine.reg_read(UC_X86_REG_EAX), tuple(read32(stack + 4 * index) for index in range(1, 7))))
        clobber(machine)
        ret(0x18)
    elif address == builder.END_DRAG:
        end_drags.append(True)
        clobber(machine)
        ret()
    elif address == builder.SET_ADAPTER_TEXT:
        texts[machine.reg_read(UC_X86_REG_EBX)] = cstring(read32(stack + 4))
        ret(4)
    elif address == 0x77156E:  # _stricmp
        same = cstring(read32(stack + 4)).lower() == cstring(read32(stack + 8)).lower()
        machine.reg_write(UC_X86_REG_EAX, 0 if same else 1)
        ret()
    elif address == 0x770FBE:  # isdigit
        machine.reg_write(UC_X86_REG_EAX, 4 if chr(read32(stack + 4) & 0xFF).isdigit() else 0)
        ret()
    elif address == 0x7720C0:  # atoi
        digits = re.match(r"\s*([+-]?\d+)", cstring(read32(stack + 4)))
        machine.reg_write(UC_X86_REG_EAX, (int(digits.group(1)) if digits else 0) & 0xFFFFFFFF)
        ret()
    elif address == builder.SEND_SLASH_COMMAND:
        commands.append(cstring(read32(stack + 4)))
        machine.reg_write(UC_X86_REG_EAX, 0x5A5A5A5A)
        ret()
    elif address == builder.SET_CHAT_MODE:
        chat.append(("mode", machine.reg_read(UC_X86_REG_ESI), machine.reg_read(UC_X86_REG_EDI)))
        ret()
    elif address == builder.SET_CHAT_TEXT:
        chat.append(("text", cstring(read32(stack + 4))))
        ret()
    elif address == 0x6010000:
        windows.append((read32(stack + 4), read32(stack + 8)))
        ret(8)


uc.hook_add(UC_HOOK_CODE, stub)
uc.mem_write(builder.WINDOW_MANAGER, struct.pack("<I", 0x600F000))
uc.mem_write(0x600F000, struct.pack("<I", 0x600F100))
uc.mem_write(0x600F108, struct.pack("<I", 0x6010000))


def setup():
    uc.reg_write(UC_X86_REG_ESP, sp)
    uc.reg_write(UC_X86_REG_EBP, frame)
    uc.reg_write(UC_X86_REG_EBX, body)
    uc.reg_write(UC_X86_REG_EDI, 0x123456)


def adapter(index):
    return read32(manifest["adapterSlots"] + 4 * index)


setup()
uc.emu_start(manifest["blocks"]["init"]["address"], 0x4DA940, count=400000)
assert uc.reg_read(UC_X86_REG_EDI) == 0x123456
GEAR_SLOTS = layout["GearSlots"]
assert labels[:TOTAL] == [builder.adapter_name(index) for index in range(TOTAL)]
assert labels[TOTAL:TOTAL + GEAR_SLOTS] == builder.gear_icon_names() == manifest["gear"]["iconAdapters"]
assert len(labels) == TOTAL + GEAR_SLOTS + 240 and len(set(labels)) == len(labels), "Raid adapter registration was changed"
for index, name in enumerate(builder.gear_icon_names()):
    obj, maximum, initial, registry = scalars[name]
    assert read32(manifest["gear"]["iconSlots"] + 4 * index) == obj
    assert (maximum, initial, registry) == (builder.float_bits(1000000.0), builder.float_bits(-1.0), 0x123456), \
        "Gear icon adapters: max 1e6, initial -1 (the quiver's values), registry EDI"
assert read32(manifest["activeFlag"]) == 0
assert read32(manifest["barActiveFlag"]) == 0


def packet(operation, index=0, text="", marker=0x43, version=builder.PROTOCOL_VERSION, length=128, terminator=0,
           raw_text=None):
    setup()
    data = bytearray(128)
    data[1:4] = bytes((marker, version, operation))
    data[4:6] = struct.pack("<H", index)
    encoded = raw_text if raw_text is not None else text.encode("ascii")
    data[12:12 + len(encoded)] = encoded
    data[127] = terminator
    uc.mem_write(body, bytes(data))
    uc.mem_write(frame + 0x18, struct.pack("<I", length))
    end = builder.PACKET_DONE if marker in (0x43, 0x52) and length >= 2 else 0x411209
    uc.emu_start(manifest["blocks"]["packet"]["address"], end, count=200000)
    assert uc.reg_read(UC_X86_REG_ESP) == sp
    assert uc.reg_read(UC_X86_REG_EBX) == body


def command():
    return cstring(manifest["command"])


packet(1, layout["LabelStatus"], "Roster 2/78 - All realms")
assert texts[adapter(layout["LabelStatus"])] == "Roster 2/78 - All realms"
assert LABELS > 256, "The 16-bit index is what makes the high labels reachable"
packet(1, LABELS - 1, "Last action")
assert texts[adapter(LABELS - 1)] == "Last action"
packet(1, LABELS, "First COMPANIONS label")
assert texts[adapter(LABELS)] == "First COMPANIONS label"
packet(1, TOTAL - 1, "Last bar label")
assert texts[adapter(TOTAL - 1)] == "Last bar label"
packet(1, 300, "A label past the old one-byte index")
assert texts[adapter(300)] == "A label past the old one-byte index"
before = dict(texts)
packet(1, TOTAL, "Out of range")
packet(1, 0xFFFF, "Far out of range")
packet(1, 0, "Bad terminator", terminator=1)
packet(1, 0, "Probe version", version=1)
packet(1, 0, "Version 2 client format", version=2)
packet(1, 0, "Too short", length=127)
assert texts == before, "Malformed manager packets must not change labels"

assert command() == "&companions ui 0000 00"
packet(4, text="0a9f")
assert command() == "&companions ui 0a9f 00"
for bad in ("0A9F", "0a9g", "0a9", "zzzz"):
    packet(4, raw_text=bad.encode("ascii"))
    assert command() == "&companions ui 0a9f 00", bad


def stock_click_event(value):
    """Runs the client's own OnClickEvent mapper on an XML value."""
    setup()
    uc.mem_write(sp, struct.pack("<I", sentinel))
    uc.mem_write(body, value.encode("ascii") + b"\0")
    uc.reg_write(UC_X86_REG_ESI, body)
    uc.emu_start(builder.CLICK_EVENT_MAPPER, sentinel, count=2000000)
    return uc.reg_read(UC_X86_REG_EAX)


# Static: InvisibleButtonDef (parser 0x4ED9B4) maps OnClickEvent through
# 0x4EA06F, not the ControlId mapper 0x4E99E6 that the raid hooks.
parser = image[pe.get_offset_from_rva(0x4ED9B4 - 0x400000):][:0x200]
keyword = parser.find(b"\xff\x35" + struct.pack("<I", 0x99B780))
assert keyword > 0, "OnClickEvent keyword test not found in the InvisibleButtonDef parser"
assert pe.get_string_at_rva(struct.unpack("<I", pe.get_data(0x99B780 - 0x400000, 4))[0] - 0x400000).lower() == b"onclickevent"
# The branch is: xmlStrcasecmp(name, keyword), xmlNodeGetContent(node), mapper(text).
branch_calls = [0x4ED9B4 + offset + 5 + struct.unpack("<i", parser[offset + 1:offset + 5])[0]
                for offset in range(keyword, keyword + 0x28) if parser[offset] == 0xE8]
assert branch_calls[:3] == [0x6DDD8E, 0x6DDD7C, builder.CLICK_EVENT_MAPPER], [hex(t) for t in branch_calls]
# The raid's ControlId-mapper hook is left exactly as the raid built it.
assert image[pe.get_offset_from_rva(0x4E99E6 - 0x400000):][:5] == bytes.fromhex("e9 15 96 f9 01")

assert stock_click_event("1840") == 0x730
assert stock_click_event(str(0x700 + builder.CONTROL_LIMIT - 1)) == 0x700 + builder.CONTROL_LIMIT - 1
assert builder.CONTROL_LIMIT <= 0xE0, "0x7E0 and 0x7E1 are returned by the client's own helpers"
assert stock_click_event("toggleattackmode") == 0x64, "stock names still resolve, case-insensitively"
for unknown in ("CompMgr30", "RaidMember00", "CompanionProbeClick"):
    assert stock_click_event(unknown) == 0xFFFFFFFF, unknown


def click(event_id, stop=None):
    setup()
    uc.mem_write(sp, struct.pack("<I", sentinel))
    uc.mem_write(body + 8, struct.pack("<I", event_id))
    uc.reg_write(UC_X86_REG_EAX, body)
    uc.emu_start(manifest["blocks"]["eventHandler"]["address"], stop or sentinel, count=100000)
    if stop is None:
        assert uc.reg_read(UC_X86_REG_ESP) == sp + 4
        assert uc.reg_read(UC_X86_REG_EAX) & 0xFF == 1
    return uc.reg_read(UC_X86_REG_EIP)


click(0x705)
assert commands == [], "Clicks before the server shows the manager must not send commands"
packet(2)
assert windows[-1] == (builder.CUSTOM8_WINDOW, 1)
assert read32(manifest["activeFlag"]) == 1
assert commands == [f"&companions ui 0a9f {builder.READY_CONTROL:02x}"], commands
click(0x700 + layout["ControlRowBase"] + 5)
assert commands[-1] == "&companions ui 0a9f 05"
click(0x700 + layout["ControlActionBase"] + 3)
assert commands[-1] == f"&companions ui 0a9f {layout['ControlActionBase'] + 3:02x}"
packet(4, text="1b2c")
click(0x700 + layout["ControlTabRecruit"])
assert commands[-1] == f"&companions ui 1b2c {layout['ControlTabRecruit']:02x}"
sent = len(commands)
click(0x700 + builder.SEARCH_CONTROL)
assert len(commands) == sent, "Search must stay in the client"
assert chat == [("mode", builder.CHAT_INPUT_STATE, 1), ("text", builder.SEARCH_PREFIX)], chat
assert click(0x600, stop=builder.RAID_EVENT_HANDLER) == builder.RAID_EVENT_HANDLER
assert click(0x700 + builder.CONTROL_LIMIT, stop=builder.RAID_EVENT_HANDLER) == builder.RAID_EVENT_HANDLER
click(0x700 + builder.CONTROL_LIMIT - 1)
assert commands[-1] == f"&companions ui 1b2c {builder.CONTROL_LIMIT - 1:02x}"
assert click(0x6FF, stop=builder.RAID_EVENT_HANDLER) == builder.RAID_EVENT_HANDLER

packet(3)
assert windows[-1] == (builder.CUSTOM8_WINDOW, 0)
assert read32(manifest["activeFlag"]) == 0
sent = len(commands)
click(0x701)
assert len(commands) == sent, "Clicks after hide must not send commands"

# COMPANIONS window (Custom7): its own flag gates its clicks, so the bar works with the manager hidden.
sent = len(commands)
click(0x7A8)
assert len(commands) == sent, "Bar clicks before the bar is shown must not send commands"
packet(builder.OP_BAR_SHOW)
assert windows[-1] == (builder.BAR_WINDOW, 1)
assert read32(manifest["barActiveFlag"]) == 1
assert commands[-1].endswith(f" {builder.BAR_READY_CONTROL:02x}"), commands[-1]
assert read32(manifest["activeFlag"]) == 0, "Showing the bar leaves the manager flag alone"
click(0x7A8)
assert commands[-1].endswith(" a8"), commands[-1]
click(0x7B1)
assert commands[-1].endswith(" b1"), commands[-1]
click(0x700 + builder.BAR_CONTROL_SLOT_BASE + builder.BAR_SLOTS - 1)
assert commands[-1].endswith(f" {builder.BAR_CONTROL_SLOT_BASE + builder.BAR_SLOTS - 1:02x}"), commands[-1]
click(0x700 + builder.BAR_TILES + builder.BAR_CONTROL_FIRST - 1)
assert commands[-1].endswith(f" {builder.BAR_CONTROL_FIRST + builder.BAR_TILES - 1:02x}"), commands[-1]
sent = len(commands)
click(0x705)
assert len(commands) == sent, "Manager clicks while only the bar is shown must not send commands"
packet(builder.OP_BAR_HIDE)
assert windows[-1] == (builder.BAR_WINDOW, 0)
assert read32(manifest["barActiveFlag"]) == 0
sent = len(commands)
click(0x7A9)
assert len(commands) == sent, "Bar clicks after hide must not send commands"
packet(2)
assert read32(manifest["barActiveFlag"]) == 0, "Showing the manager leaves the bar flag alone"
assert click(0x7A, stop=builder.RAID_EVENT_HANDLER) == builder.RAID_EVENT_HANDLER, "Stock member events pass through"
packet(3)
assert read32(manifest["activeFlag"]) == 0

# COMPANION GEAR window (Custom6): operations 7-9 and the slot clicks 0x900 + n (the 0.273.0 probe's proven logic).
UI, DRAG = 0x600F000, 0x6038000
GEAR_EVENT = builder.GEAR_EVENT_BASE
uc.mem_write(UI + builder.DRAG_RECORD, struct.pack("<I", DRAG))
uc.mem_write(builder.CURSOR_X, struct.pack("<II", 412, 233))
icon_objects = [scalars[name][0] for name in builder.gear_icon_names()]


def set_item(slot, name, template):
    record = builder.ITEM_ARRAY + slot * builder.ITEM_STRIDE
    uc.mem_write(record, bytes(builder.ITEM_STRIDE))
    uc.mem_write(record + builder.ITEM_TEMPLATE, struct.pack("<I", template))
    uc.mem_write(record + builder.ITEM_NAME, name.encode("ascii") + b"\0")


def set_icon(template, icon):
    uc.mem_write(builder.ICON_TABLE + template * builder.ICON_STRIDE, struct.pack("<h", icon))


def set_drag(kind, index, count=0):
    uc.mem_write(DRAG + builder.DRAG_KIND, struct.pack("<II", kind & 0xFFFFFFFF, index & 0xFFFFFFFF))
    uc.mem_write(UI + builder.DRAG_COUNT, struct.pack("<I", count))


assert list(builder.GEAR_CLIENT_SLOTS) == list(range(150, 169)) + list(range(170, 210)) == manifest["gear"]["clientSlots"]
assert manifest["gear"]["clickEvents"] == [str(value) for value in range(2304, 2363)]
assert [builder.inventory_source(i) for i in (0, 3, 4, 9, 10, 27, 28, 39, 40, 79, 80)] == \
    [10, 13, None, None, 21, 38, None, None, 40, 79, None]
# Helm present (icon 0x1a2), chest empty, bag 1 with an out-of-range template, bag 2 present.
set_item(150, "Matterbender Chain Helm", 17)
set_icon(17, 0x1A2)
set_item(151, "", 0)
set_item(170, "Broken Record", 0x20000)
set_item(171, "Willful Ring", 0x2B0)
set_icon(0x2B0, 0x38D)
expected_icons = [0x1A2] + [-1] * 19 + [0x38D] + [-1] * 38
shown, sent = len(windows), len(commands)
packet(builder.OP_GEAR_REFRESH)
assert len(windows) == shown and len(commands) == sent, "Refresh alone must not show the window or send a command"
assert setter_calls[-GEAR_SLOTS:] == [(obj, float(value)) for obj, value in zip(icon_objects, expected_icons)]
packet(builder.OP_GEAR_SHOW)
assert windows[-1] == (builder.GEAR_WINDOW, 1) and builder.GEAR_WINDOW == 0x6D + 6
assert setter_calls[-GEAR_SLOTS:] == [(obj, float(value)) for obj, value in zip(icon_objects, expected_icons)]
assert len(setter_calls) == 2 * GEAR_SLOTS
assert commands[-1].endswith(f" {builder.GEAR_READY_CONTROL:02x}") and len(commands) == sent + 1, commands[-1]
assert read32(manifest["activeFlag"]) == 0 and read32(manifest["barActiveFlag"]) == 0, "Gear show leaves both flags"
packet(builder.OP_GEAR_HIDE)
assert windows[-1] == (builder.GEAR_WINDOW, 0) and len(setter_calls) == 2 * GEAR_SLOTS
uc.mem_write(builder.WINDOW_MANAGER, struct.pack("<I", 0))
shown = len(windows)
packet(builder.OP_GEAR_SHOW)
assert len(windows) == shown and len(commands) == sent + 1, "A null window manager skips the show"
uc.mem_write(builder.WINDOW_MANAGER, struct.pack("<I", UI))

# Drops: a kind-9 bag drag and the weapon/armour hotspot maps of the stock inventory drop (0x408151).
sent = len(commands)
for kind, index, count, event, move in ((9, 0x28, 1, 0, (150, 40, 1)), (9, 2, 0, 1, (151, 12, 0)),
                                        (9, 15, 0, 19, (170, 26, 0)), (9, 79, 0, 20, (171, 79, 0)),
                                        (0x66, 150, 0, 19, (170, 150, 0))):
    set_drag(kind, index, count)
    ended = len(end_drags)
    click(GEAR_EVENT + event)
    assert moves[-1] == move and len(end_drags) == ended + 1, (moves[-1], move)
set_drag(0x66, 170)
moved, ended = len(moves), len(end_drags)
click(GEAR_EVENT + 19)
assert len(moves) == moved and len(end_drags) == ended + 1, "A pick-up dropped on its own slot only ends the drag"
for kind, index in ((9, 5), (9, 0x50), (9, 0x1C), (9, -2), (0x5B, 3), (0x39, 7)):
    set_drag(kind, index)
    moved, ended = len(moves), len(end_drags)
    click(GEAR_EVENT)
    assert len(moves) == moved and len(end_drags) == ended, (kind, index)
# Empty cursor: a filled slot starts a kind-0x66 drag at the cursor; an empty one or a bad template does nothing.
set_drag(-1, -1)
click(GEAR_EVENT)
assert drags[-1] == (0x1A2, (UI, 0x66, 150, 0, 412, 233))
click(GEAR_EVENT + 20)
assert drags[-1] == (0x38D, (UI, 0x66, 171, 0, 412, 233))
picked = len(drags)
click(GEAR_EVENT + 1)
click(GEAR_EVENT + 19)
assert len(drags) == picked
uc.mem_write(UI + builder.DRAG_RECORD, struct.pack("<I", 0))
click(GEAR_EVENT)
assert len(drags) == picked + 1 and drags[-1][1][2] == 150, "No drag record: a filled slot still picks up"
uc.mem_write(UI + builder.DRAG_RECORD, struct.pack("<I", DRAG))
uc.mem_write(builder.WINDOW_MANAGER, struct.pack("<I", 0))
calls = (len(moves), len(drags), len(end_drags))
click(GEAR_EVENT + GEAR_SLOTS - 1)
assert (len(moves), len(drags), len(end_drags)) == calls, "A null UI manager returns AL=1 without any call"
uc.mem_write(builder.WINDOW_MANAGER, struct.pack("<I", UI))
assert len(commands) == sent, "Gear clicks never send slash commands"
for event_id in (GEAR_EVENT - 1, GEAR_EVENT + GEAR_SLOTS, 0x93F, 0xA00, 0x8FF):
    assert click(event_id, stop=builder.RAID_EVENT_HANDLER) == builder.RAID_EVENT_HANDLER, hex(event_id)
assert [stock_click_event(value) for value in manifest["gear"]["clickEvents"]] == \
    list(range(GEAR_EVENT, GEAR_EVENT + GEAR_SLOTS))
assert not any(GEAR_EVENT <= 0x700 + value < GEAR_EVENT + GEAR_SLOTS for value in range(builder.CONTROL_LIMIT))
assert manifest["gear"]["nativeReturnBytes"] == {f"{address:#x}": size for address, size in builder.NATIVE_RETURN_BYTES.items()}
for name, limit in (("gearRefresh", builder.GEAR_EVENT_OFFSET - builder.GEAR_REFRESH_OFFSET),
                    ("gearEvent", builder.GEAR_CODE_LIMIT - builder.GEAR_EVENT_OFFSET)):
    assert manifest["blocks"][name]["length"] <= limit, name

# A raid show message still reaches the existing 40/80 native parser.
packet(2, marker=0x52, version=1)
assert windows[-1] == (0x76, 1)

# Other DebugMode packets still reach the stock handler.
setup()
uc.mem_write(body, bytes(128))
uc.mem_write(frame + 0x18, struct.pack("<I", 2))
uc.emu_start(manifest["blocks"]["packet"]["address"], 0x411209, count=100000)
assert uc.reg_read(UC_X86_REG_EIP) == 0x411209

# Element names the manager windows may use. The COMPANIONS window adds the stock status bar, scalar label and divider.
MANAGER_XML_TAGS = {"WindowTemplate", "Name", "WindowId", "CloseButton", "MoveButton",
                    "TopRightResizeButton", "BottomRightResizeButton", "BottomLeftResizeButton",
                    "ResizeButtonOffsetX", "ResizeButtonOffsetY", "TitleWidth", "TitleHeight",
                    "Width", "Height", "ResizeableWidth", "ResizeableHeight",
                    "ResizeableTwoWayWidth", "ResizeableTwoWayHeight", "MinWidth", "MinHeight",
                    "ContextTemplateName", "FullResizeImageDef", "ControlId", "Position", "X", "Y",
                    "Alignment", "TopLeft", "GrowWidth", "GrowHeight", "offsetright",
                    "offsetbottom", "TemplateName", "LabelDef", "Color", "R", "G", "B", "A",
                    "FontName", "ColorAdapter", "MaxCharacters", "Data", "EndAligned",
                    "TextCentered", "Adapter", "InvisibleButtonDef", "Label", "OnClickEvent"}
BAR_XML_TAGS = MANAGER_XML_TAGS | {"StatusBarDef", "AdapterName", "ScalarLabelDef", "DontDrawWhenNegOne",
                                   "HorizontalResizeImageDef"}

# XML: every adapter is shown exactly once, every event maps into the manager range.
controls = set()
for skin in ("atlantis", "isles"):
    root = ET.parse(args.stage / skin / "custom8_window.xml").getroot()
    window = root.find("WindowTemplate")
    assert window.find("WindowId").text == "Custom8"
    width, height = int(window.find("Width").text), int(window.find("Height").text)
    assert width <= 1024 - 40 and height <= 768 - 60, "Window must fit the 1024x768 client"
    assert (width, height) == (builder.WINDOW_WIDTH, builder.WINDOW_HEIGHT)
    assert {element.find("FontName").text for element in window.iter("LabelDef")} == {builder.FONT}
    adapters = [element.text for element in window.iter("Adapter")]
    assert sorted(adapters) == sorted(builder.adapter_name(index) for index in range(LABELS))
    for button in window.iter("InvisibleButtonDef"):
        text = button.find("OnClickEvent").text
        assert re.fullmatch(r"[0-9]+", text), text
        event = stock_click_event(text)
        assert builder.EVENT_BASE <= event < builder.EVENT_BASE + builder.CONTROL_LIMIT, text
        controls.add(event - builder.EVENT_BASE)
    assert window.find(".//EditBoxDef") is None and window.find(".//ClickableEditBoxDef") is None
    for element in window.iter():
        assert element.tag in MANAGER_XML_TAGS, element.tag
assert b"custom8_window.xml" in (args.stage / "uimain.xml").read_bytes()
assert builder.SEARCH_CONTROL in controls and builder.READY_CONTROL not in controls
assert not any(builder.BAR_CONTROL_FIRST <= value < builder.BAR_CONTROL_LIMIT for value in controls), \
    "Manager controls must stay out of the COMPANIONS range"

# COMPANIONS window (Custom7): 98 labels, stock group rows, the stock GroupInvite tile, and tile and row clicks in
# the bar range only.
assert b"<Include>custom7_window.xml</Include>" in (args.stage / "uimain.xml").read_bytes()
assert builder.BAR_LABEL_BASE == LABELS and TOTAL == builder.TOTAL_LABEL_COUNT == builder.GEAR_LABEL_END
assert builder.GEAR_LABEL_BASE == builder.BAR_LABEL_END == builder.BAR_LABEL_BASE + builder.BAR_LABEL_COUNT
assert builder.BAR_LABEL_COUNT == 2 + builder.BAR_TILE_STRIDE * builder.BAR_TILES + builder.BAR_SLOT_STRIDE * builder.BAR_SLOTS
assert builder.BAR_CONTROL_SLOT_BASE == builder.BAR_CONTROL_FIRST + builder.BAR_TILES
assert builder.BAR_CONTROL_SLOT_BASE + builder.BAR_SLOTS < builder.BAR_READY_CONTROL < builder.BAR_CONTROL_LIMIT
bar_events = set()
native_tile_events = set()
for skin in ("atlantis", "isles"):
    root = ET.parse(args.stage / skin / "custom7_window.xml").getroot()
    window = root.find("WindowTemplate")
    assert window.find("Name").text == "custom7_window"
    assert window.find("WindowId").text == "Custom7"
    width, height = int(window.find("Width").text), int(window.find("Height").text)
    assert (width, height) == (builder.BAR_WIDTH, builder.BAR_HEIGHT) and width <= 760 and height <= 540
    assert {element.find("FontName").text for element in window.iter("LabelDef")} == {builder.FONT}
    bar_adapters = sorted(element.text for element in window.iter("Adapter") if element.text.startswith("cmgr_"))
    assert bar_adapters == sorted(builder.adapter_name(index) for index in range(builder.BAR_LABEL_BASE, builder.BAR_LABEL_END)), \
        "Each COMPANIONS label adapter appears exactly once"
    native = sorted(element.text for element in window.iter("Adapter") if not element.text.startswith("cmgr_"))
    assert native == sorted(f"group_{kind}{i}" for kind in ("name", "level", "class", "health", "power", "endurance")
                            for i in range(8)), "Each native group adapter is shown exactly once"
    assert {element.text for element in window.iter("ColorAdapter") if element.text} == \
        {f"group_color{i}" for i in range(8)}
    assert {element.text for element in window.iter("AdapterName")} == {f"group_health{i}" for i in range(8)}
    assert {element.find("TemplateName").text for element in window.iter("StatusBarDef")} == {"red_status_thick"}
    member_events = set()
    for button in window.iter("InvisibleButtonDef"):
        text = button.find("OnClickEvent").text
        if text.startswith("SelectGroupMember"):
            assert text in {f"SelectGroupMember{i}" for i in range(8)}, text
            member_events.add(text)
            continue
        if not re.fullmatch(r"[0-9]+", text):
            assert text in {event for _name, _caption, event, _row, _column in builder.BAR_NATIVE_TILES}, text
            assert stock_click_event(text) >= 0, text
            native_tile_events.add(text)
            continue
        event = stock_click_event(text)
        assert 0x700 + builder.BAR_CONTROL_FIRST <= event < 0x700 + builder.BAR_CONTROL_LIMIT, text
        bar_events.add(event)
    assert member_events == {f"SelectGroupMember{i}" for i in range(8)}
    for element in window.iter():
        assert element.tag in BAR_XML_TAGS, element.tag
    tiles = [(int(element.find("Position/X").text), int(element.find("Position/Y").text),
              int(element.find("Width").text), int(element.find("Height").text))
             for element in window.iter("FullResizeImageDef")
             if element.find("TemplateName").text == builder.BAR_TILE_TEMPLATE]
    assert len(tiles) == builder.BAR_TILES + len(builder.BAR_NATIVE_TILES)
    # Click areas never overlap each other, and every row's stance click sits right of its member click.
    areas = [(int(element.find("Position/X").text), int(element.find("Position/Y").text),
              int(element.find("Width").text), int(element.find("Height").text), element.find("OnClickEvent").text)
             for element in window.iter("InvisibleButtonDef")]
    for index, (ax, ay, aw, ah, aname) in enumerate(areas):
        assert 0 <= ax and 0 <= ay and ax + aw <= width and ay + ah <= height, (aname, ax, ay, aw, ah)
        for bx, by, bw, bh, bname in areas[index + 1:]:
            assert ax + aw <= bx or bx + bw <= ax or ay + ah <= by or by + bh <= ay, (aname, bname)
    for slot in range(builder.BAR_SLOTS):
        row = next(a for a in areas if a[4] == f"SelectGroupMember{slot}")
        stance = next(a for a in areas if a[4] == str(0x700 + builder.BAR_CONTROL_SLOT_BASE + slot))
        assert row[0] + row[2] <= stance[0] and row[1] == stance[1], (row, stance)
    for x, y, w, h in tiles:
        assert 0 <= x and 0 <= y and x + w <= width and y + h <= height, (x, y, w, h)
    for index, (ax, ay, aw, ah) in enumerate(tiles):
        for bx, by, bw, bh in tiles[index + 1:]:
            assert ax + aw <= bx or bx + bw <= ax or ay + ah <= by or by + bh <= ay, ((ax, ay, aw, ah), (bx, by, bw, bh))
assert bar_events == set(range(0x700 + builder.BAR_CONTROL_FIRST, 0x700 + builder.BAR_CONTROL_FIRST + builder.BAR_TILES)) | \
    set(range(0x700 + builder.BAR_CONTROL_SLOT_BASE, 0x700 + builder.BAR_CONTROL_SLOT_BASE + builder.BAR_SLOTS)), sorted(bar_events)
assert 0x700 + builder.BAR_READY_CONTROL not in bar_events
assert native_tile_events == {"GroupInvite"}
assert stock_click_event("SelectGroupMember3") == 125 and stock_click_event("GroupInvite") == 158
for needle in (b"custom7_window\0", b"group_health%d\0", b"group_name%d\0", b"group_level%d\0",
               b"group_color%d\0", b"group_class%d\0", b"group_power%d\0", b"group_endurance%d\0",
               b"SelectGroupMember0\0", b"GroupInvite\0"):
    assert needle in image, needle
# Static: the COMPANIONS window is Custom7 (0x74): the ShowCustomN switch pushes 0x6D + N, so Custom7 is 0x6D + 7.
assert image[pe.get_offset_from_rva(0x4E0B4A - 0x400000):][:4] == bytes.fromhex("6a016a74")
assert image[pe.get_offset_from_rva(0x4E0B50 - 0x400000):][:4] == bytes.fromhex("6a016a75")
assert builder.BAR_WINDOW == 0x6D + 7 and builder.CUSTOM8_WINDOW == 0x6D + 8
assert manifest["barWindowSha256"] == hashlib.sha256(
    (args.stage / "atlantis" / "custom7_window.xml").read_bytes()).hexdigest()
assert hashlib.sha256((args.stage / "uimain.xml").read_bytes()).hexdigest() == manifest["uimainOutputSha256"]
# Pins: the manager's own window and UI stay byte-identical to the 0.169.0 build.
assert manifest["windowSha256"] == "9cd3b8a5fb3d2a4671192b9d52287173e2fe194a4f19979de1c339f408235efd"
assert manifest["uimainManagerOnlySha256"] == "e1266fcceb954b5e835d3e3ca6563d682f4bb0f6430d36947923a74974dfd2a0"
# The COMPANIONS window and its UI stay byte-identical to the 0.272.0 build; the full UI equals the 0.273.0-0.275.0
# paperdoll probe's uimain.xml, so an upgrade from the probe leaves uimain.xml alone.
assert manifest["barWindowSha256"] == "5b82057450ac1d510fe66bba5813f0f3f4d7a9fda263bfd23d4638be2cd69bac"
assert manifest["uimainBarSha256"] == "7d573c315a56bcacae8e8ebc871646654bfe8da5bb1dc745d6023da7f93b9dd9"
assert manifest["uimainOutputSha256"] == "cef698f354bd3d1c2e7c40f794dac38d0f172a5889d7b8996f1d41c131a1328c"

# COMPANION GEAR window (Custom6): sockets, icons and click areas on the same squares; every gear label once.
GEAR_XML_TAGS = MANAGER_XML_TAGS | {"ImageAreaDef", "IconDef", "IconId", "AdapterName"}
gear_bytes = {skin: (args.stage / skin / "custom6_window.xml").read_bytes() for skin in ("atlantis", "isles")}
assert gear_bytes["atlantis"] == gear_bytes["isles"]
assert hashlib.sha256(gear_bytes["atlantis"]).hexdigest() == manifest["gearWindowSha256"]
assert b"<Include>custom6_window.xml</Include>" in (args.stage / "uimain.xml").read_bytes()
window = ET.fromstring(gear_bytes["atlantis"]).find("WindowTemplate")
assert (window.find("Name").text, window.find("WindowId").text) == ("custom6_window", "Custom6")
width, height = int(window.find("Width").text), int(window.find("Height").text)
assert (width, height) == (builder.GEAR_WIDTH, builder.GEAR_HEIGHT) == (518, 386)
assert {element.find("FontName").text for element in window.iter("LabelDef")} == {builder.FONT}
icons, buttons, sockets = window.findall("IconDef"), window.findall("InvisibleButtonDef"), window.findall("ImageAreaDef")
assert [icon.find("AdapterName").text for icon in icons] == builder.gear_icon_names()
assert all(icon.find("TemplateName").text == "plain_icon_large" and icon.find("IconId").text == "-1" for icon in icons)
assert len(sockets) == GEAR_SLOTS and all(socket.find("TemplateName").text == "icon_socket_large" for socket in sockets)
assert [button.find("OnClickEvent").text for button in buttons] == manifest["gear"]["clickEvents"]
rectangles = manifest["gear"]["slotRectangles"]
for index, (icon, button) in enumerate(zip(icons, buttons)):
    position = [int(icon.find("Position/X").text), int(icon.find("Position/Y").text)]
    assert position == [int(button.find("Position/X").text), int(button.find("Position/Y").text)]
    assert position + [int(button.find("Width").text), int(button.find("Height").text)] == rectangles[index]
for a in range(GEAR_SLOTS):
    ax, ay, aw, ah = rectangles[a]
    assert 0 <= ax and ax + aw <= width and 18 <= ay and ay + ah <= height
    for bx, by, bw, bh in rectangles[a + 1:]:
        assert ax + aw <= bx or bx + bw <= ax or ay + ah <= by or by + bh <= ay, "Gear slots overlap"
gear_adapters = sorted(element.text for element in window.iter("Adapter"))
assert gear_adapters == sorted(builder.adapter_name(index) for index in range(builder.GEAR_LABEL_BASE, TOTAL)), \
    "Each gear label adapter appears exactly once"
for element in window.iter("LabelDef"):
    x, y = int(element.find("Position/X").text), int(element.find("Position/Y").text)
    assert 0 <= x and x + int(element.find("Width").text) <= width and 0 <= y and y + int(element.find("Height").text) <= height
for element in window.iter():
    assert element.tag in GEAR_XML_TAGS, element.tag
gear_templates = {element.text for element in window.iter("TemplateName")}
stock_ui = Path("/mnt/d/Games/OfflineDAoC/runtime/client-opendaoc/app/ui")
if stock_ui.is_dir():
    for skin in ("atlantis", "isles"):
        stock = b"".join(path.read_bytes() for path in (stock_ui / skin).glob("*.xml") if not path.name.startswith("custom"))
        for template in gear_templates:
            assert f"<Name>{template}</Name>".encode() in stock, f"{template} is not a stock {skin} template"
assert hashlib.sha256((args.stage / "uimain.xml").read_bytes()).hexdigest() == manifest["uimainOutputSha256"]
uimain = (args.stage / "uimain.xml").read_bytes()
assert uimain.index(b"custom8_window.xml") < uimain.index(b"custom7_window.xml") < uimain.index(b"custom6_window.xml")

# Geometry: the chosen list sizes must fit a window of the advertised height without overlap.
for size in builder.ROW_SIZES:
    height = builder.WINDOW_HEIGHT_BASE + builder.PITCH * size
    assert builder.ROW_TOP + builder.PITCH * size <= height - 52, f"{size} rows run into the list pager"
    last_detail = builder.DETAIL_TOP + builder.PITCH * (size - builder.DETAIL_RESERVE) - 4
    assert last_detail <= height - 116 + 4, f"{size - builder.DETAIL_RESERVE} detail lines run into the scroll links"
    assert size - builder.DETAIL_RESERVE <= builder.DETAIL_LINES
assert builder.WINDOW_HEIGHT_BASE + builder.PITCH * builder.ROW_SIZES[1] <= builder.WINDOW_HEIGHT, "Default window fits the default size"
assert builder.ROWS == builder.ROW_SIZES[-1] and builder.DETAIL_LINES == builder.ROWS - builder.DETAIL_RESERVE
# Row columns stay inside the list pane and in order; toggles on one line never overlap or leave the window.
columns = [(builder.COLUMN_NAME, builder.WIDTH_ROW_NAME), (builder.COLUMN_LEVEL, builder.WIDTH_ROW_LEVEL),
           (builder.COLUMN_CLASS, builder.WIDTH_ROW_CLASS), (builder.COLUMN_TYPE, builder.WIDTH_ROW_TYPE),
           (builder.COLUMN_STATE, builder.WIDTH_ROW_STATE)]
for (x, width), (next_x, _next_width) in zip(columns, columns[1:]):
    assert x + width <= next_x, "Row columns overlap"
assert columns[-1][0] + columns[-1][1] <= builder.LIST_X + builder.LIST_WIDTH
assert builder.COLUMN_MARK + builder.WIDTH_ROW_HEADER <= builder.LIST_X + builder.LIST_WIDTH
by_line = {}
for name, _control, x, y, width in builder.TOGGLES:
    by_line.setdefault(y, []).append((x, x + width, name))
for y, spans in by_line.items():
    spans.sort()
    for (_x0, end, name), (start, _end, next_name) in zip(spans, spans[1:]):
        assert end <= start, f"{name} overlaps {next_name} at y={y}"
    assert spans[-1][1] <= builder.WINDOW_WIDTH
links = {name: (x, width) for name, _text, _control, x, _y, width in builder.STATIC_LINKS}
tabs_end = max(x + width for name, _control, x, y, width in builder.TOGGLES if y == 30)
assert links["Search"][0] >= tabs_end, "Top links overlap the tabs and row sizes"
assert links["Refresh"][0] + links["Refresh"][1] <= builder.WINDOW_WIDTH
assert links["Search"][0] + links["Search"][1] <= links["Clear"][0] <= links["Refresh"][0], "Top links overlap each other"
for text in ("Roster (78/78)", "Active (99)", "Training & Tactics"):
    assert builder.text_width(text) > 0

# The server protocol constants must match the native layout.
protocol = (Path(__file__).resolve().parents[1] / "GameServer/bots/CompanionManagerProtocol.cs").read_text(encoding="utf-8")
declared = {name: int(value, 0) for name, value in
            re.findall(r"public const (?:int|byte) (\w+) = (0x[0-9A-Fa-f]+|\d+);", protocol)}
for name, value in layout.items():
    assert declared.get(name) == value, f"CompanionManagerProtocol.{name} = {declared.get(name)}, native {value}"
advances = re.search(r"Arial14Advances =\s*\{(.*?)\};", protocol, re.S).group(1)
assert tuple(int(value) for value in re.findall(r"\d+", advances)) == builder.ARIAL14_ADVANCES, "Server font table differs"

print(f"PASS (offline only): {LABELS} manager adapters registered before the unchanged raid adapters")
print("PASS (offline only): versioned label/token/show/hide packets; malformed packets ignored")
print("PASS (offline only): the stock OnClickEvent mapper turns every XML click value into a manager event")
print("PASS (offline only): clicks send &companions ui <token> <control>; Search opens the chat line")
print("PASS (offline only): raid events, raid packets, and stock DebugMode packets pass through")
print("PASS (offline only): Custom8 XML uses only raid-proven controls; server constants match")
print("PASS (offline only): COMPANIONS Custom7 window: ops 5/6, 98 labels, tile and row controls 0xA8-0xBF, "
      "stock group rows and GroupInvite")
print(f"PASS (offline only): COMPANION GEAR Custom6 window: ops 7/8/9, {GEAR_SLOTS} scalar icon adapters, ready control, "
      "slot clicks 0x900-0x93a send move(slot, source, count) or start a kind-0x66 pick-up")
print("NOTE: emulation is not acceptance; the owner's combined real-client check is still required")
