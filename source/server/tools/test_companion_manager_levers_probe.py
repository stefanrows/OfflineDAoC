"""Offline x86 checks for the Companion Manager levers research probe.

Emulation proves the probe's hook logic, call sequences, return sizes, and pass-through only. It is
not real-client acceptance: the owner's real-client session answers the four levers. Run it against a
copy of the probe stage, never the installation:

    python3 test_companion_manager_levers_probe.py --stage <probe stage> [--input-stage <0.169.0 stage>]

--input-stage enables the byte-identity checks against the verified 0.169.0 manager.
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
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from unicorn import Uc, UC_ARCH_X86, UC_HOOK_CODE, UC_MODE_32
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EBP, UC_X86_REG_EBX, UC_X86_REG_ECX
from unicorn.x86_const import UC_X86_REG_EDI, UC_X86_REG_EDX, UC_X86_REG_EFLAGS, UC_X86_REG_EIP
from unicorn.x86_const import UC_X86_REG_ESI, UC_X86_REG_ESP

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_companion_manager_client as manager
import probe_companion_manager_levers as probe


parser = argparse.ArgumentParser()
parser.add_argument("--stage", type=Path, required=True)
parser.add_argument("--input-stage", type=Path, default=None)
args = parser.parse_args()

# Static: hashes, section layout, redirects, and the decoded probe blocks.
manifest = json.loads((args.stage / "manifest.json").read_text(encoding="utf-8"))
image = (args.stage / "game.dll").read_bytes()
assert manifest["probe"] is True, "Not a probe manifest"
assert hashlib.sha256(image).hexdigest() == manifest["outputSha256"], "game.dll hash differs from the manifest"
assert manifest["inputSha256"] == probe.INPUT_GAME_SHA256, "Probe was not built from the verified 0.169.0 manager"
assert hashlib.sha256((args.stage / "uimain.xml").read_bytes()).hexdigest() == probe.INPUT_UIMAIN_SHA256
window_bytes = {skin: (args.stage / skin / "custom8_window.xml").read_bytes() for skin in ("atlantis", "isles")}
assert window_bytes["atlantis"] == window_bytes["isles"], "Atlantis and Isles Custom8 XML differ"
assert hashlib.sha256(window_bytes["atlantis"]).hexdigest() == manifest["windowSha256"]
pe = pefile.PE(data=image)
base = pe.OPTIONAL_HEADER.ImageBase
assert [s.Name.rstrip(b"\0") for s in pe.sections[-2:]] == [b".cmgr", b".cprb"], "Probe section must follow .cmgr"
assert pe.OPTIONAL_HEADER.CheckSum == pe.generate_checksum(), "Checksum is not the PE checksum"
assert manifest["sectionVa"] == base + pe.sections[-1].VirtualAddress
assert pe.sections[-1].VirtualAddress + pe.sections[-1].Misc_VirtualSize <= pe.OPTIONAL_HEADER.SizeOfImage
cmgr_va = base + pe.sections[-2].VirtualAddress
assert manifest["managerBlocks"]["init"]["address"] == cmgr_va
assert manifest["managerBlocks"]["eventHandler"]["address"] == cmgr_va + 0x1C00
md = Cs(CS_ARCH_X86, CS_MODE_32)
probe_init = manifest["blocks"]["init"]["address"]
probe_handler = manifest["blocks"]["eventHandler"]["address"]


def code_at(address, length):
    return image[pe.get_offset_from_rva(address - base):][:length]


def jump_target(address):
    site = code_at(address, 5)
    assert site[0] == 0xE9, f"{address:#x} is not a jump"
    return address + 5 + struct.unpack("<i", site[1:5])[0]


assert jump_target(0x4E04DE) == probe_handler, "Event hook must enter the probe handler"
assert jump_target(0x4DA938) == cmgr_va, "Stock init hook still enters the manager init"
assert jump_target(0x411201) == cmgr_va + 0x1000, "Stock packet hook still enters the manager packet"
init_instructions = [i for i in md.disasm(code_at(cmgr_va, 81 + 16), cmgr_va) if i.address < cmgr_va + 81]
assert init_instructions[-1].mnemonic == "jmp" and jump_target(init_instructions[-1].address) == probe_init, \
    "Manager init must end by entering the probe init"
probe_jumps = [i for i in md.disasm(code_at(probe_init, 0x100), probe_init) if i.mnemonic == "jmp"]
assert probe_jumps[-1].op_str == "0x247e000", "Probe init must end with jmp 0x247E000"
assert manifest["blocks"]["init"]["length"] < 0x100 and manifest["blocks"]["eventHandler"]["length"] < 0xF00

if args.input_stage is not None:
    input_image = (args.input_stage / "game.dll").read_bytes()
    assert hashlib.sha256(input_image).hexdigest() == probe.INPUT_GAME_SHA256
    input_pe = pefile.PE(data=input_image)
    assert len(pe.sections) == len(input_pe.sections) + 1
    # Only these input bytes may change: the header fields that count and size the sections, the new
    # section-header slot, the two redirected jumps. Everything else is byte-identical.
    header_slot = input_pe.sections[0].get_file_offset() + 40 * input_pe.FILE_HEADER.NumberOfSections
    cmgr_raw = input_pe.sections[-1].PointerToRawData
    allowed = set()
    for start, length in ((pe.FILE_HEADER.get_field_absolute_offset("NumberOfSections"), 2),
                          (pe.OPTIONAL_HEADER.get_field_absolute_offset("SizeOfImage"), 4),
                          (pe.OPTIONAL_HEADER.get_field_absolute_offset("CheckSum"), 4),
                          (header_slot, 40),
                          (input_pe.get_offset_from_rva(0x4E04DE - base), 5),
                          (cmgr_raw + 0x4C, 5)):
        allowed.update(range(start, start + length))
    changed = {offset for offset in range(len(input_image)) if input_image[offset] != image[offset]}
    assert changed <= allowed, f"Unexpected byte changes at {sorted(changed - allowed)[:8]}"
    # The manager's init (before its final jump), packet, eventHandler and sendControl blocks are unchanged.
    for name, rva, length in (("init", 0x0, 0x4C), ("packet", 0x1000, manifest["managerBlocks"]["packet"]["length"]),
                              ("eventHandler", 0x1C00, manifest["managerBlocks"]["eventHandler"]["length"]),
                              ("sendControl", 0x2600, manifest["managerBlocks"]["sendControl"]["length"])):
        start = cmgr_raw + rva
        assert image[start:start + length] == input_image[start:start + length], f"Manager {name} block differs"
    print("PASS: only the two redirected jumps and the PE section fields changed; manager blocks are byte-identical")
else:
    print("NOTE: byte-identity against the 0.169.0 input skipped (no --input-stage)")

shift_ids, plain_ids = probe.probe_ids()
assert shift_ids == tuple(range(0x800, 0x810)) and plain_ids == tuple(range(0x900, 0x910))
assert manifest["probeIds"]["clickEventShift"] == 2048 and manifest["probeIds"]["clickEventPlain"] == 2304
assert probe.is_installation_path(Path("/mnt/d/Games/OfflineDAoC/runtime"))
assert probe.is_installation_path(Path("/mnt/d/Games/OfflineDAoC"))
assert not probe.is_installation_path(probe.default_output())
assert not probe.is_installation_path(Path("/mnt/d/Games/OfflineDAoC-dev/x"))
print("PASS: probe IDs are 0x800-0x80F and 0x900-0x90F; the output refuses the installation path only")

# Emulation: the stock OnClickEvent mapper turns the probe's XML values into the probe IDs.
uc = Uc(UC_ARCH_X86, UC_MODE_32)
uc.mem_map(0x400000, 0x2100000)
for section in pe.sections:
    if section.SizeOfRawData:
        uc.mem_write(base + section.VirtualAddress, bytes(section.get_data()))
uc.mem_map(0x5000000, 0x10000)
uc.mem_map(0x6000000, 0x40000)
uc.mem_map(0x7000000, 0x100000)
sp, frame, body, sentinel = 0x5008000, 0x5008100, 0x6030000, 0x603F000
MANAGER_WINDOW, WINDOW, OWNER = 0x7000000, 0x7010000, 0x7020000
FLAGS = 0x000008D7
MANAGER_OBJECT_MARK = 0x5A5A5A5A
registrations, setter_calls, texts = [], [], {}
commands = []
next_object = [0x6000000]


def read32(address):
    return struct.unpack("<I", uc.mem_read(address, 4))[0]


def cstring(address, limit=256):
    return bytes(uc.mem_read(address, limit)).split(b"\0")[0].decode("ascii")


def ret(cleanup=0):
    stack = uc.reg_read(UC_X86_REG_ESP)
    address = read32(stack)
    uc.reg_write(UC_X86_REG_ESP, stack + 4 + cleanup)
    uc.reg_write(UC_X86_REG_EIP, address)


def allocate(size):
    address = next_object[0]
    next_object[0] += 64
    uc.mem_write(address, bytes(size))
    return address


def stub_text_registrar(stack):
    registry, slot, name = read32(stack + 4), read32(stack + 8), cstring(read32(stack + 12))
    obj = allocate(0x24)
    uc.mem_write(slot, struct.pack("<I", obj))
    registrations.append({"kind": "text", "registry": registry, "slot": slot, "name": name, "object": obj})
    ret(12)


def stub_color_registrar(stack):
    registry, slot, name = read32(stack + 4), read32(stack + 8), cstring(read32(stack + 12))
    obj = allocate(0x18)
    # Stock creates four floats of 1.0 and zeroes the dword at +0x14. The probe must leave +0x14 alone,
    # so the stub marks it and the checks look for the mark.
    uc.mem_write(obj, struct.pack("<4f", 1.0, 1.0, 1.0, 1.0))
    uc.mem_write(obj + 0x14, struct.pack("<I", MANAGER_OBJECT_MARK))
    uc.mem_write(slot, struct.pack("<I", obj))
    registrations.append({"kind": "color", "registry": registry, "slot": slot, "name": name, "object": obj})
    ret(12)


def stub_scalar_registrar(stack):
    registry, slot, name = read32(stack + 4), read32(stack + 8), cstring(read32(stack + 12))
    maximum, initial = read32(stack + 16), read32(stack + 20)
    obj = allocate(0x14)
    uc.mem_write(obj, struct.pack("<IIIII", 0x946A10, 1, maximum, initial, 0xFF7FFFFF))
    uc.mem_write(slot, struct.pack("<I", obj))
    registrations.append({"kind": "scalar", "registry": registry, "slot": slot, "name": name, "object": obj,
                          "max": maximum, "initial": initial})
    ret(20)


def stub_scalar_setter(stack):
    setter_calls.append({"ecx": uc.reg_read(UC_X86_REG_ECX), "value": struct.unpack("<f", uc.mem_read(stack + 4, 4))[0]})
    ret(4)


def stub_text(stack):
    texts.setdefault(uc.reg_read(UC_X86_REG_EBX), []).append(cstring(read32(stack + 4)))
    ret(4)


def stub_stricmp(stack):
    same = cstring(read32(stack + 4)).lower() == cstring(read32(stack + 8)).lower()
    uc.reg_write(UC_X86_REG_EAX, 0 if same else 1)
    ret()


def stub_isdigit(stack):
    uc.reg_write(UC_X86_REG_EAX, 4 if chr(read32(stack + 4) & 0xFF).isdigit() else 0)
    ret()


def stub_atoi(stack):
    digits = re.match(r"\s*([+-]?\d+)", cstring(read32(stack + 4)))
    uc.reg_write(UC_X86_REG_EAX, (int(digits.group(1)) if digits else 0) & 0xFFFFFFFF)
    ret()


def stub_send_command(stack):
    commands.append(cstring(read32(stack + 4)))
    ret()


STUBS = {
    manager.REGISTER_TEXT_ADAPTER: stub_text_registrar,
    probe.REGISTER_SCALAR_ADAPTER: stub_scalar_registrar,
    probe.REGISTER_COLOR_ADAPTER: stub_color_registrar,
    probe.SET_SCALAR_VALUE: stub_scalar_setter,
    probe.SET_ADAPTER_TEXT: stub_text,
    0x77156E: stub_stricmp,
    0x770FBE: stub_isdigit,
    0x7720C0: stub_atoi,
    manager.SEND_SLASH_COMMAND: stub_send_command,
}


STOP_AT = [None]


def hook(machine, address, size, user):
    # Unicorn's `until` is only checked at translation-block starts, and the probe's jumps land mid-block
    # in the manager. The stop is therefore checked on every instruction.
    if address == STOP_AT[0]:
        machine.emu_stop()
        return
    handler = STUBS.get(address)
    if handler is not None:
        handler(machine.reg_read(UC_X86_REG_ESP))


def run(begin, until, count):
    """Runs from `begin` until `until` and insists that the emulation stopped exactly there."""
    STOP_AT[0] = until
    try:
        uc.emu_start(begin, until, count=count)
    finally:
        STOP_AT[0] = None
    assert uc.reg_read(UC_X86_REG_EIP) == until, f"Stopped at {uc.reg_read(UC_X86_REG_EIP):#x}, expected {until:#x}"


uc.hook_add(UC_HOOK_CODE, hook)
uc.mem_write(probe.WINDOW_MANAGER, struct.pack("<I", MANAGER_WINDOW))
uc.mem_write(MANAGER_WINDOW + probe.CUSTOM8_WINDOW_OFFSET, struct.pack("<I", WINDOW))
uc.mem_write(WINDOW + probe.OWNER_OFFSET, struct.pack("<I", OWNER))
uc.mem_write(OWNER + probe.WIDTH_OFFSET, struct.pack("<I", 0x3D4))
uc.mem_write(OWNER + probe.HEIGHT_OFFSET, struct.pack("<I", 0x2BC))
setter_calls = []


def set_registers():
    uc.reg_write(UC_X86_REG_ESP, sp)
    uc.reg_write(UC_X86_REG_EBP, frame)
    uc.reg_write(UC_X86_REG_EBX, 0xB0B0B0B0)
    uc.reg_write(UC_X86_REG_EAX, 0xA0A0A0A0)
    uc.reg_write(UC_X86_REG_ECX, 0xC0C0C0C0)
    uc.reg_write(UC_X86_REG_EDX, 0xD0D0D0D0)
    uc.reg_write(UC_X86_REG_ESI, 0xE0E0E0E0)
    uc.reg_write(UC_X86_REG_EDI, 0x123456)
    uc.reg_write(UC_X86_REG_EFLAGS, FLAGS)


def snapshot():
    names = (("eax", UC_X86_REG_EAX), ("ebx", UC_X86_REG_EBX), ("ecx", UC_X86_REG_ECX), ("edx", UC_X86_REG_EDX),
             ("esi", UC_X86_REG_ESI), ("edi", UC_X86_REG_EDI), ("ebp", UC_X86_REG_EBP), ("esp", UC_X86_REG_ESP),
             ("eflags", UC_X86_REG_EFLAGS))
    return {name: uc.reg_read(reg) for name, reg in names}


# Init: the manager's 440 registrations run first, then the probe's three, then the redirect to 0x247E000.
set_registers()
uc.mem_write(sp, struct.pack("<I", sentinel))
before = snapshot()
run(cmgr_va, probe.RAID_INIT, count=400000)
after = snapshot()
assert uc.reg_read(UC_X86_REG_EIP) == probe.RAID_INIT
assert after["esp"] == sp, "Stack unbalanced across the init"
for name in ("eax", "ebx", "ecx", "edx", "esi", "edi", "ebp", "eflags"):
    assert after[name] == before[name], f"Init changed {name}"
manager_entries = [entry for entry in registrations if entry["name"].startswith("cmgr_")]
assert [entry["name"] for entry in manager_entries] == \
    [manager.adapter_name(index) for index in range(manifest["layout"]["LabelCount"])]
probe_entries = registrations[len(manager_entries):]
assert [entry["name"] for entry in probe_entries] == [probe.ADAPTER_TEXT, probe.ADAPTER_BAR, probe.ADAPTER_COLOR]
assert all(entry["registry"] == 0x123456 for entry in registrations), "The registry must be EDI"
text_entry, bar_entry, color_entry = probe_entries
assert text_entry["kind"] == "text" and text_entry["slot"] == manifest["addresses"]["slotText"]
assert bar_entry["kind"] == "scalar" and bar_entry["slot"] == manifest["addresses"]["slotBar"]
assert bar_entry["max"] == 0x42C80000 and bar_entry["initial"] == 0, "Bar: max 100.0, initial 0.0"
assert color_entry["kind"] == "color" and color_entry["slot"] == manifest["addresses"]["slotColor"]
for entry in probe_entries:
    assert read32(entry["slot"]) == entry["object"], "Each slot must hold its adapter object"
text_object, bar_object, color_object = text_entry["object"], bar_entry["object"], color_entry["object"]
assert read32(color_object + 0x14) == MANAGER_OBJECT_MARK
print("PASS: probe init registers cprb_text, cprb_bar, cprb_color after the 440 manager labels, EDI as registry")
print("PASS: init returns to 0x247E000 with registers, flags, and stack restored")


def send_manager_label(index, text):
    """A manager label packet still reaches the manager's own adapter with the probe installed."""
    packet = bytearray(128)
    packet[1:4] = bytes((manager.MARKER, manager.PROTOCOL_VERSION, manager.OP_LABEL))
    packet[4:6] = struct.pack("<H", index)
    packet[12:12 + len(text)] = text.encode("ascii")
    uc.mem_write(body, bytes(packet))
    uc.mem_write(frame + 0x18, struct.pack("<I", 128))
    set_registers()
    uc.reg_write(UC_X86_REG_EBX, body)
    uc.mem_write(sp, struct.pack("<I", sentinel))
    run(manifest["managerBlocks"]["packet"]["address"], manager.PACKET_DONE, count=200000)


send_manager_label(manifest["layout"]["LabelStatus"], "Roster 2/78 - All realms")
status_object = manager_entries[manifest["layout"]["LabelStatus"]]["object"]
assert texts[status_object] == ["Roster 2/78 - All realms"], texts.get(status_object)
print("PASS: the manager label packet still updates its own adapter with the probe installed")


def expected_text(mode, counter, width, height):
    w = "----" if width is None else f"{width:04x}"
    h = "----" if height is None else f"{height:04x}"
    return f"{mode}#{counter:02x} W={w} H={h}"


def fire(event_id, stop=None):
    """Runs the probe handler on an event record; returns the state at entry and at exit."""
    set_registers()
    uc.mem_write(body, bytes(16))
    uc.mem_write(body + 8, struct.pack("<I", event_id))
    uc.reg_write(UC_X86_REG_EAX, body)
    uc.mem_write(sp, struct.pack("<I", sentinel))
    entry = snapshot()
    run(probe_handler, stop or sentinel, count=200000)
    return entry, snapshot()


def counter():
    return read32(manifest["addresses"]["counter"])


def colour():
    return struct.unpack("<4f", uc.mem_read(color_object, 16))


def check_click(event_id, mode, counter_value, bar_value, colour_value, width, height):
    """One click: the bar, colour and text the probe writes, and a clean return to the caller."""
    texts_before = len(texts.get(text_object, []))
    setter_before = len(setter_calls)
    entry, exit_state = fire(event_id)
    assert uc.reg_read(UC_X86_REG_EIP) == sentinel, f"{event_id:#x} did not return"
    assert exit_state["esp"] == sp + 4, f"{event_id:#x} stack unbalanced"
    assert exit_state["eax"] & 0xFF == 1 and exit_state["eax"] & 0xFFFFFF00 == entry["eax"] & 0xFFFFFF00
    for name in ("ebx", "ecx", "edx", "esi", "edi", "ebp", "eflags"):
        assert exit_state[name] == entry[name], f"{event_id:#x} changed {name}"
    assert counter() == counter_value, (counter(), counter_value)
    assert len(setter_calls) == setter_before + 1, "Exactly one scalar setter call per click"
    assert setter_calls[-1] == {"ecx": bar_object, "value": bar_value}, setter_calls[-1]
    assert colour() == colour_value, (colour(), colour_value)
    assert read32(color_object + 0x14) == MANAGER_OBJECT_MARK, "The probe must not write the colour object's +0x14"
    assert len(texts[text_object]) == texts_before + 1, "Exactly one text update per click"
    assert texts[text_object][-1] == expected_text(mode, counter_value, width, height), texts[text_object][-1]


# Click sequence. Counter n: bar = 25 * (n mod 5), colour = red, green, blue, white for n mod 4 = 0, 1, 2, 3.
check_click(0x900, "P", 1, 25.0, (0.0, 1.0, 0.0, 1.0), 0x3D4, 0x2BC)
print("PASS: 0x900 (OnClickEvent) is mode P, bar 25.0, colour green, text 'P#01 W=03d4 H=02bc', AL=1 to the caller")
check_click(0x800, "S", 2, 50.0, (0.0, 0.0, 1.0, 1.0), 0x3D4, 0x2BC)
print("PASS: 0x800 (OnShiftClickEvent) is mode S, bar 50.0, colour blue, text 'S#02 W=03d4 H=02bc'")
check_click(0x80F, "S", 3, 75.0, (1.0, 1.0, 1.0, 1.0), 0x3D4, 0x2BC)
check_click(0x90F, "P", 4, 100.0, (1.0, 0.0, 0.0, 1.0), 0x3D4, 0x2BC)
print("PASS: the last ID of each probe range (0x80F, 0x90F) is handled the same way")

# A null window manager, window, or owner prints ---- for the sizes and does not crash.
uc.mem_write(probe.WINDOW_MANAGER, struct.pack("<I", 0))
check_click(0x800, "S", 5, 0.0, (0.0, 1.0, 0.0, 1.0), None, None)
uc.mem_write(probe.WINDOW_MANAGER, struct.pack("<I", MANAGER_WINDOW))
uc.mem_write(MANAGER_WINDOW + probe.CUSTOM8_WINDOW_OFFSET, struct.pack("<I", 0))
check_click(0x900, "P", 6, 25.0, (0.0, 0.0, 1.0, 1.0), None, None)
uc.mem_write(MANAGER_WINDOW + probe.CUSTOM8_WINDOW_OFFSET, struct.pack("<I", WINDOW))
uc.mem_write(WINDOW + probe.OWNER_OFFSET, struct.pack("<I", 0))
check_click(0x800, "S", 7, 50.0, (1.0, 1.0, 1.0, 1.0), None, None)
uc.mem_write(WINDOW + probe.OWNER_OFFSET, struct.pack("<I", OWNER))
check_click(0x900, "P", 8, 75.0, (1.0, 0.0, 0.0, 1.0), 0x3D4, 0x2BC)
print("PASS: a null manager, window, or owner prints W=---- H=---- and the probe returns cleanly")

# Pass-through: every other ID reaches the manager eventHandler with EAX, the flags, the registers and the stack unchanged.
raid_ids = (0x123, 0x5FF, 0x600, 0x64F, 0x650, 0x6FF, 0x7E0, 0x7E1, 0x7FF, 0x810, 0x8FF, 0x910, 0xA00)
manager_ids = (0x700, 0x7DF)
counter_before = counter()
setters_before = len(setter_calls)
texts_before = len(texts.get(text_object, []))
for event_id in raid_ids + manager_ids:
    entry, arrival = fire(event_id, stop=cmgr_va + 0x1C00)
    assert uc.reg_read(UC_X86_REG_EIP) == cmgr_va + 0x1C00, f"{event_id:#x} did not reach the manager"
    assert arrival["eax"] == body, f"{event_id:#x} changed EAX"
    assert arrival["esp"] == sp and arrival["eflags"] == entry["eflags"], f"{event_id:#x} changed stack or flags"
    for name in ("ebx", "ecx", "edx", "esi", "edi", "ebp"):
        assert arrival[name] == entry[name], f"{event_id:#x} changed {name}"
    if event_id in raid_ids:
        run(cmgr_va + 0x1C00, manager.RAID_EVENT_HANDLER, count=100000)
        assert uc.reg_read(UC_X86_REG_EIP) == manager.RAID_EVENT_HANDLER, f"{event_id:#x} did not reach the raid"
    else:
        run(cmgr_va + 0x1C00, sentinel, count=100000)
        assert uc.reg_read(UC_X86_REG_EIP) == sentinel and uc.reg_read(UC_X86_REG_EAX) & 0xFF == 1
        assert uc.reg_read(UC_X86_REG_ESP) == sp + 4
assert counter() == counter_before and len(setter_calls) == setters_before and len(texts[text_object]) == texts_before
print("PASS: IDs outside the probe ranges reach the manager eventHandler unchanged; raid IDs reach the raid handler")
print("PASS: manager IDs 0x700 and 0x7DF are still handled by the manager (AL=1 to the caller)")


def stock_click_event(value):
    set_registers()
    uc.mem_write(sp, struct.pack("<I", sentinel))
    uc.mem_write(body, value.encode("ascii") + b"\0")
    uc.reg_write(UC_X86_REG_ESI, body)
    run(manager.CLICK_EVENT_MAPPER, sentinel, count=2000000)
    return uc.reg_read(UC_X86_REG_EAX)


assert stock_click_event("2304") == 0x900 and stock_click_event("2048") == 0x800
assert stock_click_event("2319") == 0x90F and stock_click_event("2063") == 0x80F
print("PASS: the stock OnClickEvent mapper turns 2304 and 2048 into 0x900 and 0x800")

# XML: the strip is appended after the manager's elements, names resolve, and the strip overlaps nothing.
def canonical(element):
    return (element.tag, (element.text or "").strip(), tuple(canonical(child) for child in element))


def rectangle(element):
    """Actual (x, y, width, height) for an element, using the stock anchor rules (offsetright, offsetbottom)."""
    width, height = element.find("Width"), element.find("Height")
    if width is None or height is None:
        return None
    x, y = int(element.find("Position/X").text), int(element.find("Position/Y").text)
    alignment = element.find("Alignment")
    flags = [child.tag for child in alignment] if alignment is not None else []
    if "offsetright" in flags:
        x = manager.WINDOW_WIDTH - x - int(width.text)
    if "offsetbottom" in flags:
        y = manager.WINDOW_HEIGHT - y - int(height.text)
    return (x, y, int(width.text), int(height.text))


manager_adapters = {manager.adapter_name(index) for index in range(manifest["layout"]["LabelCount"])}
registered = {entry["name"] for entry in registrations}
for skin in ("atlantis", "isles"):
    window = ET.fromstring(window_bytes[skin]).find("WindowTemplate")
    children = list(window)
    strip_elements = children[-4:]
    assert [child.tag for child in strip_elements] == ["LabelDef", "LabelDef", "StatusBarDef", "InvisibleButtonDef"]
    existing = children[:-4]
    if args.input_stage is not None:
        input_window = ET.fromstring((args.input_stage / skin / "custom8_window.xml").read_bytes()).find("WindowTemplate")
        assert [canonical(child) for child in input_window] == [canonical(child) for child in existing], \
            "The manager's Custom8 elements changed"
    adapters = [element.text for element in window.iter("Adapter")]
    colour_adapters = [element.text for element in window.iter("ColorAdapter") if element.text]
    bar_adapters = [element.text for element in window.iter("AdapterName")]
    assert set(adapters) - manager_adapters == {probe.ADAPTER_TEXT}
    assert colour_adapters == [probe.ADAPTER_COLOR] and bar_adapters == [probe.ADAPTER_BAR]
    assert {probe.ADAPTER_TEXT, probe.ADAPTER_COLOR, probe.ADAPTER_BAR} <= registered
    button = strip_elements[3]
    assert button.find("OnClickEvent").text == "2304" and button.find("OnShiftClickEvent").text == "2048"
    for element in strip_elements:
        for node in element.iter():
            if node.tag in ("TopLeft", "EndAligned", "TextCentered"):
                assert node.text in ("true", "false"), (node.tag, node.text)
    assert window.find("WindowId").text == "Custom8"
    assert window.find("Width").text == str(manager.WINDOW_WIDTH) and window.find("Height").text == str(manager.WINDOW_HEIGHT)
    strip = []
    for element in strip_elements:
        if element.tag == "StatusBarDef":
            strip.append((int(element.find("Position/X").text), int(element.find("Position/Y").text),
                          int(element.find("Width").text), probe.STRIP_HEIGHT))
        else:
            strip.append(rectangle(element))
    obstacles = [rectangle(element) for element in existing if element.tag in ("LabelDef", "InvisibleButtonDef")]
    for px, py, pw, ph in strip:
        assert py == probe.STRIP_Y and 0 <= px and px + pw <= manager.WINDOW_WIDTH and py + ph <= manager.WINDOW_HEIGHT
        for ex, ey, ew, eh in (rect for rect in obstacles if rect is not None):
            assert not (px < ex + ew and ex < px + pw and py < ey + eh and ey < py + ph), \
                f"Probe {(px, py, pw, ph)} overlaps {(ex, ey, ew, eh)}"
print("PASS: the strip is appended after the manager's Custom8 elements and overlaps no existing control")
print("PASS: the strip uses only registered adapter names, OnClickEvent 2304 and OnShiftClickEvent 2048")

uimain = (args.stage / "uimain.xml").read_bytes()
assert b"custom8_window.xml" in uimain
if args.input_stage is not None:
    assert uimain == (args.input_stage / "uimain.xml").read_bytes(), "uimain.xml must be a byte-identical copy"
print("PASS: uimain.xml is the verified manager copy with the Custom8 include")
print("NOTE: emulation proves the probe logic only; the owner's real-client session answers the four levers")
