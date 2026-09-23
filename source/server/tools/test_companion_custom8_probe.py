"""Offline x86-hook checks for the staged Custom8 probe and raid passthrough."""

import argparse
import hashlib
import json
from pathlib import Path
import struct
import xml.etree.ElementTree as ET

import pefile
from unicorn import Uc, UC_ARCH_X86, UC_HOOK_CODE, UC_MODE_32
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EBP, UC_X86_REG_EBX
from unicorn.x86_const import UC_X86_REG_EDI, UC_X86_REG_EIP, UC_X86_REG_ESI, UC_X86_REG_ESP


parser = argparse.ArgumentParser()
parser.add_argument("--stage", type=Path, required=True)
args = parser.parse_args()
manifest = json.loads((args.stage / "manifest.json").read_text(encoding="utf-8"))
image = (args.stage / "game.dll").read_bytes()
assert hashlib.sha256(image).hexdigest() == manifest["outputSha256"]
pe = pefile.PE(data=image)
assert pe.sections[-1].Name.rstrip(b"\0") == b".cmp8"
assert pe.sections[-2].Name.rstrip(b"\0") == b".raid"

uc = Uc(UC_ARCH_X86, UC_MODE_32)
uc.mem_map(0x400000, 0x2100000)
for section in pe.sections:
    if section.SizeOfRawData:
        uc.mem_write(pe.OPTIONAL_HEADER.ImageBase + section.VirtualAddress, section.get_data())
uc.mem_map(0x5000000, 0x10000)
uc.mem_map(0x6000000, 0x20000)
sp, frame, body, sentinel = 0x5008000, 0x5008100, 0x6010000, 0x601F000
labels, texts, windows, actions = [], {}, [], []


def read32(address):
    return struct.unpack("<I", uc.mem_read(address, 4))[0]


def cstring(address):
    return bytes(uc.mem_read(address, 128)).split(b"\0")[0].decode("ascii")


def ret(cleanup=0):
    stack = uc.reg_read(UC_X86_REG_ESP)
    address = read32(stack)
    uc.reg_write(UC_X86_REG_ESP, stack + 4 + cleanup)
    uc.reg_write(UC_X86_REG_EIP, address)


def stub(machine, address, size, user):
    stack = machine.reg_read(UC_X86_REG_ESP)
    if address in (0x4B6BC0, 0x4B6C84, 0x4B710D):
        slot = read32(stack + 8)
        label = cstring(read32(stack + 12))
        obj = 0x6000000 + 64 * len(labels)
        machine.mem_write(slot, struct.pack("<I", obj))
        if address == 0x4B6BC0:
            machine.mem_write(obj + 8, struct.pack("<3f", 100, 0, -3.402823e38))
        if address == 0x4B710D:
            machine.mem_write(obj + 0x20, struct.pack("<I", 15))
        labels.append(label)
        ret(20 if address == 0x4B6BC0 else 12)
    elif address == 0x524436:
        texts[machine.reg_read(UC_X86_REG_EBX)] = cstring(read32(stack + 4))
        ret(4)
    elif address == 0x77156E:
        machine.reg_write(UC_X86_REG_EAX, 0 if cstring(read32(stack + 4)) == cstring(read32(stack + 8)) else 1)
        ret()
    elif address == 0x4281DF:
        actions.append((read32(stack + 8), read32(stack + 12),
                        bytes(machine.mem_read(read32(stack + 4), read32(stack + 12)))))
        ret()
    elif address == 0x601E000:
        windows.append((read32(stack + 4), read32(stack + 8)))
        ret(8)


uc.hook_add(UC_HOOK_CODE, stub)
uc.mem_write(0x104C2BC, struct.pack("<I", 0x601D000))
uc.mem_write(0x601D000, struct.pack("<I", 0x601D100))
uc.mem_write(0x601D108, struct.pack("<I", 0x601E000))


def setup():
    uc.reg_write(UC_X86_REG_ESP, sp)
    uc.reg_write(UC_X86_REG_EBP, frame)
    uc.reg_write(UC_X86_REG_EBX, body)
    uc.reg_write(UC_X86_REG_EDI, 0x123456)


setup()
uc.emu_start(manifest["blocks"]["init"]["address"], 0x4DA940, count=200000)
assert labels[0] == "comp_probe_title"
assert labels[1] == "comp_probe_search"
assert len(labels) == 242 and len(set(labels)) == 242, "Raid adapter registration was changed"


def packet(marker, operation, object_id=0, name="", length=128, version=1, terminator=0):
    setup()
    data = bytearray(128)
    data[1:4] = bytes((marker, version, operation))
    struct.pack_into("<H", data, 8, object_id)
    data[12:12 + len(name)] = name.encode("ascii")
    data[43] = terminator
    uc.mem_write(body, bytes(data))
    uc.mem_write(frame + 0x18, struct.pack("<I", length))
    end = 0x4113AD if marker in (0x43, 0x52) and length >= 2 else 0x411209
    uc.emu_start(manifest["blocks"]["packet"]["address"], end, count=100000)
    assert uc.reg_read(UC_X86_REG_ESP) == sp


packet(0x43, 1, 472, "Server says hello")
assert read32(manifest["selectedObjectId"]) == 472
assert texts[read32(manifest["titleAdapter"])] == "Server says hello"
packet(0x43, 1, 999, "Bad", terminator=1)
packet(0x43, 1, 999, "Bad", length=44)
packet(0x43, 1, 999, "Bad", version=2)
assert read32(manifest["selectedObjectId"]) == 472
packet(0x43, 2)
assert windows[-1] == (0x75, 1)

setup()
uc.mem_write(sp, struct.pack("<I", sentinel))
uc.mem_write(body, b"CompanionProbeClick\0")
uc.reg_write(UC_X86_REG_ESI, body)
uc.emu_start(manifest["blocks"]["eventName"]["address"], sentinel, count=100000)
assert uc.reg_read(UC_X86_REG_EAX) == 0x700
setup()
uc.mem_write(sp, struct.pack("<I", sentinel))
uc.mem_write(body + 8, struct.pack("<I", 0x700))
uc.reg_write(UC_X86_REG_EAX, body)
uc.emu_start(manifest["blocks"]["eventHandler"]["address"], sentinel, count=100000)
assert actions == [(0x5e, 8, bytes((0x43, 0x4d, 1, 1, 0, 0, 0, 0)))]
assert texts[read32(manifest["titleAdapter"])] == "Client click fired"

search_obj = read32(manifest["searchAdapter"])
uc.mem_write(search_obj + 8, b"Brakka\0")
uc.mem_write(search_obj + 0x1c, struct.pack("<I", 6))
setup()
uc.mem_write(sp, struct.pack("<I", sentinel))
uc.mem_write(body, b"CompanionProbeSearch\0")
uc.reg_write(UC_X86_REG_ESI, body)
uc.emu_start(manifest["blocks"]["eventName"]["address"], sentinel, count=100000)
assert uc.reg_read(UC_X86_REG_EAX) == 0x701
setup()
uc.mem_write(sp, struct.pack("<I", sentinel))
uc.mem_write(body + 8, struct.pack("<I", 0x701))
uc.reg_write(UC_X86_REG_EAX, body)
uc.emu_start(manifest["blocks"]["eventHandler"]["address"], sentinel, count=100000)
assert actions[-1] == (0x5e, 36, bytes((0x43, 0x4d, 1, 2)) + b"Brakka" + bytes(26))

packet(0x43, 3)
assert windows[-1] == (0x75, 0)
assert read32(manifest["selectedObjectId"]) == 0

# A raid show message still reaches the existing 40/80 native parser.
packet(0x52, 2)
assert windows[-1] == (0x76, 1)

for skin in ("atlantis", "isles"):
    root = ET.parse(args.stage / skin / "custom8_window.xml").getroot()
    assert root.find(".//WindowId").text == "Custom8"
    assert root.find(".//LabelDef/Adapter").text == "comp_probe_title"
    assert root.find(".//LabelDef/ColorAdapter") is None
    assert root.find(".//ButtonDef/OnClickEvent").text == "CompanionProbeClick"
    assert root.find(".//ButtonDef/Label").text == "Send click"
    assert root.find(".//EditBoxDef/AdapterName").text == "comp_probe_search"
    assert root.find(".//ButtonDef[OnClickEvent='CompanionProbeSearch']/Label").text == "Search"
    assert b"custom8_window.xml" in (args.stage / "uimain.xml").read_bytes()

print("PASS (offline only): Custom8 registration, server label update, show/hide, and dedicated action hook")
print("PASS (offline only): invalid probe packets rejected and existing native raid packet passed through")
print("NOTE: real-client dedicated action and editable search failed; this emulation is not acceptance")
