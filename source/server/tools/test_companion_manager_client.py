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
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EBP, UC_X86_REG_EBX
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

uc = Uc(UC_ARCH_X86, UC_MODE_32)
uc.mem_map(0x400000, 0x2100000)
for section in pe.sections:
    if section.SizeOfRawData:
        uc.mem_write(pe.OPTIONAL_HEADER.ImageBase + section.VirtualAddress, section.get_data())
uc.mem_map(0x5000000, 0x10000)
uc.mem_map(0x6000000, 0x40000)
sp, frame, body, sentinel = 0x5008000, 0x5008100, 0x6030000, 0x603F000
labels, texts, windows, commands, chat = [], {}, [], [], []


def read32(address):
    return struct.unpack("<I", uc.mem_read(address, 4))[0]


def cstring(address, limit=256):
    return bytes(uc.mem_read(address, limit)).split(b"\0")[0].decode("ascii")


def ret(cleanup=0):
    stack = uc.reg_read(UC_X86_REG_ESP)
    address = read32(stack)
    uc.reg_write(UC_X86_REG_ESP, stack + 4 + cleanup)
    uc.reg_write(UC_X86_REG_EIP, address)


def stub(machine, address, size, user):
    stack = machine.reg_read(UC_X86_REG_ESP)
    if address in (0x4B6BC0, builder.REGISTER_TEXT_ADAPTER):
        slot = read32(stack + 8)
        label = cstring(read32(stack + 12))
        obj = 0x6000000 + 64 * len(labels)
        machine.mem_write(slot, struct.pack("<I", obj))
        labels.append(label)
        ret(20 if address == 0x4B6BC0 else 12)
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
assert labels[:LABELS] == [builder.adapter_name(index) for index in range(LABELS)]
assert len(labels) == LABELS + 240 and len(set(labels)) == len(labels), "Raid adapter registration was changed"
assert read32(manifest["activeFlag"]) == 0


def packet(operation, index=0, text="", marker=0x43, version=2, length=128, terminator=0, raw_text=None):
    setup()
    data = bytearray(128)
    data[1:5] = bytes((marker, version, operation, index))
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
packet(1, LABELS - 1, "Last action")
assert texts[adapter(LABELS - 1)] == "Last action"
before = dict(texts)
packet(1, LABELS, "Out of range")
packet(1, 0, "Bad terminator", terminator=1)
packet(1, 0, "Probe version", version=1)
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
assert stock_click_event("1983") == 0x7BF
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
assert commands == ["&companions ui 0a9f be"], commands
click(0x700 + layout["ControlRowBase"] + 5)
assert commands[-1] == "&companions ui 0a9f 05"
click(0x700 + layout["ControlActionBase"] + 3)
assert commands[-1] == "&companions ui 0a9f 23"
packet(4, text="1b2c")
click(0x700 + layout["ControlTabRecruit"])
assert commands[-1] == "&companions ui 1b2c 31"
sent = len(commands)
click(0x700 + builder.SEARCH_CONTROL)
assert len(commands) == sent, "Search must stay in the client"
assert chat == [("mode", builder.CHAT_INPUT_STATE, 1), ("text", builder.SEARCH_PREFIX)], chat
assert click(0x600, stop=builder.RAID_EVENT_HANDLER) == builder.RAID_EVENT_HANDLER
assert click(0x7C0, stop=builder.RAID_EVENT_HANDLER) == builder.RAID_EVENT_HANDLER
assert click(0x6FF, stop=builder.RAID_EVENT_HANDLER) == builder.RAID_EVENT_HANDLER

packet(3)
assert windows[-1] == (builder.CUSTOM8_WINDOW, 0)
assert read32(manifest["activeFlag"]) == 0
sent = len(commands)
click(0x701)
assert len(commands) == sent, "Clicks after hide must not send commands"

# A raid show message still reaches the existing 40/80 native parser.
packet(2, marker=0x52, version=1)
assert windows[-1] == (0x76, 1)

# Other DebugMode packets still reach the stock handler.
setup()
uc.mem_write(body, bytes(128))
uc.mem_write(frame + 0x18, struct.pack("<I", 2))
uc.emu_start(manifest["blocks"]["packet"]["address"], 0x411209, count=100000)
assert uc.reg_read(UC_X86_REG_EIP) == 0x411209

# XML: every adapter is shown exactly once, every event maps into the manager range.
controls = set()
for skin in ("atlantis", "isles"):
    root = ET.parse(args.stage / skin / "custom8_window.xml").getroot()
    window = root.find("WindowTemplate")
    assert window.find("WindowId").text == "Custom8"
    width, height = int(window.find("Width").text), int(window.find("Height").text)
    assert width <= 800 - 40 and height <= 600 - 60, "Window must fit the 800x600 client"
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
        assert element.tag in {"WindowTemplate", "Name", "WindowId", "CloseButton", "MoveButton",
                               "TopRightResizeButton", "BottomRightResizeButton", "BottomLeftResizeButton",
                               "ResizeButtonOffsetX", "ResizeButtonOffsetY", "TitleWidth", "TitleHeight",
                               "Width", "Height", "ResizeableWidth", "ResizeableHeight",
                               "ResizeableTwoWayWidth", "ResizeableTwoWayHeight", "MinWidth", "MinHeight",
                               "ContextTemplateName", "FullResizeImageDef", "ControlId", "Position", "X", "Y",
                               "Alignment", "TopLeft", "TemplateName", "LabelDef", "Color", "R", "G", "B", "A",
                               "FontName", "ColorAdapter", "MaxCharacters", "Data", "EndAligned",
                               "TextCentered", "Adapter", "InvisibleButtonDef", "Label", "OnClickEvent"}, element.tag
assert b"custom8_window.xml" in (args.stage / "uimain.xml").read_bytes()
assert builder.SEARCH_CONTROL in controls and builder.READY_CONTROL not in controls

# The server protocol constants must match the native layout.
protocol = (Path(__file__).resolve().parents[1] / "GameServer/bots/CompanionManagerProtocol.cs").read_text(encoding="utf-8")
declared = {name: int(value, 0) for name, value in
            re.findall(r"public const (?:int|byte) (\w+) = (0x[0-9A-Fa-f]+|\d+);", protocol)}
for name, value in layout.items():
    assert declared.get(name) == value, f"CompanionManagerProtocol.{name} = {declared.get(name)}, native {value}"

print(f"PASS (offline only): {LABELS} manager adapters registered before the unchanged raid adapters")
print("PASS (offline only): versioned label/token/show/hide packets; malformed packets ignored")
print("PASS (offline only): the stock OnClickEvent mapper turns every XML click value into a manager event")
print("PASS (offline only): clicks send &companions ui <token> <control>; Search opens the chat line")
print("PASS (offline only): raid events, raid packets, and stock DebugMode packets pass through")
print("PASS (offline only): Custom8 XML uses only raid-proven controls; server constants match")
print("NOTE: emulation is not acceptance; the owner's combined real-client check is still required")
