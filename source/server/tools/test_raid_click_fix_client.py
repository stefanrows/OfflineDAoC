"""Offline x86 checks for the staged raid click-to-target fix.

Runs the installed client's own OnClickEvent parser on every staged value and
its event-handler chain (Companion Manager, then raid) for every member row.
Emulation is not acceptance; the owner's real-client check is still required.
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
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EIP, UC_X86_REG_ESI, UC_X86_REG_ESP

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_raid_click_fix_client as builder


parser = argparse.ArgumentParser()
parser.add_argument("--client", type=Path, required=True, help="Installed client app directory (read only)")
parser.add_argument("--stage", type=Path, required=True)
args = parser.parse_args()
sha256 = lambda data: hashlib.sha256(data).hexdigest()
manifest = json.loads((args.stage / "manifest.json").read_text(encoding="utf-8"))
image = (args.client / "game.dll").read_bytes()
assert sha256(image) in builder.SUPPORTED_GAME_SHA256, "Unsupported game.dll"
assert manifest["raidEventBase"] == builder.RAID_EVENT_BASE

CLICK_EVENT_MAPPER = 0x4EA06F
EVENT_HOOK = 0x4E04DE
SELECT_TARGET = 0x41AB40
pe = pefile.PE(data=image)
base = pe.OPTIONAL_HEADER.ImageBase
raid = next(section for section in pe.sections if section.Name.rstrip(b"\0") == b".raid")
# Raid data layout from build_native_raid80_client: hp, power, names, ids, active.
raid_ids = base + raid.VirtualAddress + 0xA000 + 3 * 320
raid_active = raid_ids + 320

uc = Uc(UC_ARCH_X86, UC_MODE_32)
uc.mem_map(0x400000, 0x2100000)
for section in pe.sections:
    if section.SizeOfRawData:
        uc.mem_write(base + section.VirtualAddress, section.get_data())
uc.mem_map(0x5000000, 0x10000)
sp, body, sentinel = 0x5008000, 0x500A000, 0x500F000
selected = []


def read32(address):
    return struct.unpack("<I", uc.mem_read(address, 4))[0]


def cstring(address):
    return bytes(uc.mem_read(address, 256)).split(b"\0")[0].decode("ascii")


def ret():
    stack = uc.reg_read(UC_X86_REG_ESP)
    uc.reg_write(UC_X86_REG_ESP, stack + 4)
    uc.reg_write(UC_X86_REG_EIP, read32(stack))


def stub(machine, address, size, user):
    stack = machine.reg_read(UC_X86_REG_ESP)
    if address == 0x77156E:  # _stricmp
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
    elif address == SELECT_TARGET:
        # EAX points at the object ID word; the byte after it selects the target mode.
        record = machine.reg_read(UC_X86_REG_EAX)
        selected.append((struct.unpack("<H", machine.mem_read(record, 2))[0], machine.mem_read(record + 2, 1)[0]))
        ret()


uc.hook_add(UC_HOOK_CODE, stub)


def stock_click_event(value):
    uc.reg_write(UC_X86_REG_ESP, sp)
    uc.mem_write(sp, struct.pack("<I", sentinel))
    uc.mem_write(body, value.encode("ascii") + b"\0")
    uc.reg_write(UC_X86_REG_ESI, body)
    uc.emu_start(CLICK_EVENT_MAPPER, sentinel, count=2000000)
    return uc.reg_read(UC_X86_REG_EAX)


def click(event_id):
    uc.reg_write(UC_X86_REG_ESP, sp)
    uc.mem_write(sp, struct.pack("<I", sentinel))
    uc.mem_write(body + 8, struct.pack("<I", event_id))
    uc.reg_write(UC_X86_REG_EAX, body)
    uc.emu_start(EVENT_HOOK, sentinel, count=100000)
    assert uc.reg_read(UC_X86_REG_ESP) == sp + 4 and uc.reg_read(UC_X86_REG_EAX) & 0xFF == 1


assert stock_click_event("RaidMember00") == 0xFFFFFFFF, "the parser should reject the old names"
assert stock_click_event("1536") == 0x600

# Staged files: hashes match the manifest, and only OnClickEvent values changed.
for name, (capacity, baseline) in builder.WINDOWS.items():
    record = manifest["windows"][name]
    assert record == {"capacity": capacity, "baselineSha256": baseline, "outputSha256": record["outputSha256"]}
    original = (args.client / "ui/atlantis" / name).read_bytes()
    for skin in builder.SKINS:
        staged = (args.stage / skin / name).read_bytes()
        assert sha256(staged) == record["outputSha256"], f"{skin}/{name}"
        assert sha256((args.client / "ui" / skin / name).read_bytes()) in (baseline, record["outputSha256"])
    if sha256(original) == baseline:
        blank = lambda data: re.sub(rb"<OnClickEvent>[^<]*</OnClickEvent>", b"<OnClickEvent/>", data)
        assert blank(original) == blank(staged), f"{name}: something besides OnClickEvent changed"
    window = ET.fromstring(staged).find("WindowTemplate")
    buttons = window.findall("InvisibleButtonDef")
    assert len(buttons) == capacity
    for button in buttons:
        member = int(button.find("Label").text.split()[-1]) - 1
        assert stock_click_event(button.find("OnClickEvent").text) == builder.RAID_EVENT_BASE + member, member

# Event chain from the stock hook: every visible row selects its member's object ID.
for capacity in (40, 80):
    uc.mem_write(raid_active, struct.pack("<I", capacity))
    for member in range(80):
        uc.mem_write(raid_ids + 4 * member, struct.pack("<I", 300 + member))
    selected.clear()
    for member in range(80):
        click(builder.RAID_EVENT_BASE + member)
    assert selected == [(300 + member, 0) for member in range(capacity)], (capacity, selected[:3])
uc.mem_write(raid_ids + 4 * 7, struct.pack("<I", 0))
selected.clear()
click(builder.RAID_EVENT_BASE + 7)
assert selected == [], "an empty or away row must not select anything"
uc.mem_write(raid_active, struct.pack("<I", 0))
click(builder.RAID_EVENT_BASE)
assert selected == [], "a closed raid window must not select anything"

# Optional: the updated raid builder produces the same files from source.
try:
    import native_raid_probe as probe
    probe.CLIENT = args.client
    sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "build/native-raid-probe/deps"))
    import build_native_raid80_client as raid_builder
    for name, (capacity, _baseline) in builder.WINDOWS.items():
        assert raid_builder.window(capacity) == (args.stage / "atlantis" / name).read_bytes(), name
    print("PASS (offline only): build_native_raid80_client.window() reproduces both staged windows")
except ImportError as error:
    print(f"SKIP: raid builder comparison needs its dependencies ({error})")

print(f"PASS (offline only): installed game.dll is {builder.SUPPORTED_GAME_SHA256[sha256(image)]}")
print("PASS (offline only): the stock OnClickEvent parser maps every staged row to 0x600 + member")
print("PASS (offline only): each click reaches the raid handler and selects that member's object ID")
print("PASS (offline only): empty rows, hidden rows, and a closed raid window select nothing")
print("NOTE: emulation is not acceptance; the owner's real-client check is still required")
