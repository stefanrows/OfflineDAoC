"""Offline x86 checks for the companion paperdoll research probe (TASKS 129).

Emulation proves the probe's hook logic, call sequences, return sizes, and pass-through only. It is not
real-client acceptance: the owner's real-client session answers risks R1-R4. Run it against a copy of the
probe stage, never the installation:

    python3 test_companion_paperdoll_probe.py --stage <probe stage> [--input-stage <0.272.0 stage>]

--input-stage enables the byte-identity checks against the verified 0.272.0 manager.
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
import probe_companion_paperdoll as probe


parser = argparse.ArgumentParser()
parser.add_argument("--stage", type=Path, required=True)
parser.add_argument("--input-stage", type=Path, default=None)
parser.add_argument("--stock-ui", type=Path, default=Path("/mnt/d/Games/OfflineDAoC/runtime/client-opendaoc/app/ui"),
                    help="Stock UI folder, read only, for the template-name check (skipped when absent)")
args = parser.parse_args()

# Static: hashes, section layout, redirects.
manifest = json.loads((args.stage / "manifest.json").read_text(encoding="utf-8"))
image = (args.stage / "game.dll").read_bytes()
assert manifest["probe"] is True and manifest["probeName"] == "companion-paperdoll-probe", "Not this probe's manifest"
assert hashlib.sha256(image).hexdigest() == manifest["outputSha256"], "game.dll hash differs from the manifest"
assert manifest["inputSha256"] == probe.INPUT_GAME_SHA256, "Probe was not built from the verified 0.272.0 manager"
window_bytes = {skin: (args.stage / skin / "custom6_window.xml").read_bytes() for skin in ("atlantis", "isles")}
assert window_bytes["atlantis"] == window_bytes["isles"], "Atlantis and Isles Custom6 XML differ"
assert hashlib.sha256(window_bytes["atlantis"]).hexdigest() == manifest["windowSha256"]
uimain = (args.stage / "uimain.xml").read_bytes()
assert hashlib.sha256(uimain).hexdigest() == manifest["uimainOutputSha256"]
pe = pefile.PE(data=image)
base = pe.OPTIONAL_HEADER.ImageBase
assert [s.Name.rstrip(b"\0") for s in pe.sections[-2:]] == [b".cmgr", b".cpdl"], "Probe section must follow .cmgr"
assert pe.OPTIONAL_HEADER.CheckSum == pe.generate_checksum(), "Checksum is not the PE checksum"
assert manifest["sectionVa"] == base + pe.sections[-1].VirtualAddress
assert pe.sections[-1].VirtualAddress + pe.sections[-1].Misc_VirtualSize <= pe.OPTIONAL_HEADER.SizeOfImage
cmgr_va = base + pe.sections[-2].VirtualAddress
manager_packet, manager_event = cmgr_va + probe.MANAGER_PACKET_OFFSET, cmgr_va + probe.MANAGER_EVENT_OFFSET
assert manifest["managerBlocks"]["init"] == {"address": cmgr_va, "length": probe.MANAGER_INIT_LENGTH}
blocks = manifest["blocks"]
probe_init, probe_packet, probe_handler = (blocks[name]["address"] for name in ("init", "packet", "eventHandler"))
md = Cs(CS_ARCH_X86, CS_MODE_32)


def code_at(address, length):
    return image[pe.get_offset_from_rva(address - base):][:length]


def jump_target(address):
    site = code_at(address, 5)
    assert site[0] == 0xE9, f"{address:#x} is not a jump"
    return address + 5 + struct.unpack("<i", site[1:5])[0]


assert jump_target(probe.HOOK_INIT) == cmgr_va, "Stock init hook still enters the manager init"
assert jump_target(probe.HOOK_PACKET) == probe_packet, "Packet hook must enter the probe packet filter"
assert jump_target(probe.HOOK_EVENT) == probe_handler, "Event hook must enter the probe handler"
init_instructions = [i for i in md.disasm(code_at(cmgr_va, probe.MANAGER_INIT_LENGTH + 16), cmgr_va)
                     if i.address < cmgr_va + probe.MANAGER_INIT_LENGTH]
assert init_instructions[-1].mnemonic == "jmp" and jump_target(init_instructions[-1].address) == probe_init, \
    "Manager init must end by entering the probe init"
for name, limit in (("init", probe.SHOW_TEXT_OFFSET), ("showText", probe.HEXFMT_OFFSET - probe.SHOW_TEXT_OFFSET),
                    ("hexfmt", probe.PACKET_OFFSET - probe.HEXFMT_OFFSET),
                    ("packet", probe.HANDLER_OFFSET - probe.PACKET_OFFSET),
                    ("eventHandler", probe.CODE_LIMIT - probe.HANDLER_OFFSET)):
    assert blocks[name]["length"] <= limit, f"{name} overruns its space"
print("PASS: three redirects (manager init tail, packet hook, event hook) enter the .cpdl blocks; checksum valid")

if args.input_stage is not None:
    input_image = (args.input_stage / "game.dll").read_bytes()
    assert hashlib.sha256(input_image).hexdigest() == probe.INPUT_GAME_SHA256
    input_pe = pefile.PE(data=input_image)
    assert len(pe.sections) == len(input_pe.sections) + 1
    header_slot = input_pe.sections[0].get_file_offset() + 40 * input_pe.FILE_HEADER.NumberOfSections
    allowed = set()
    for start, length in ((pe.FILE_HEADER.get_field_absolute_offset("NumberOfSections"), 2),
                          (pe.OPTIONAL_HEADER.get_field_absolute_offset("SizeOfImage"), 4),
                          (pe.OPTIONAL_HEADER.get_field_absolute_offset("CheckSum"), 4),
                          (header_slot, 40),
                          *((redirect["fileOffset"], 5) for redirect in manifest["redirects"])):
        allowed.update(range(start, start + length))
    changed = {offset for offset in range(len(input_image)) if input_image[offset] != image[offset]}
    assert changed <= allowed, f"Unexpected byte changes at {sorted(changed - allowed)[:8]}"
    assert len(manifest["redirects"]) == 3
    cmgr_raw = input_pe.sections[-1].PointerToRawData
    for name in ("packet", "eventHandler", "sendControl"):
        block = manifest["managerBlocks"][name]
        start = cmgr_raw + block["address"] - cmgr_va
        assert image[start:start + block["length"]] == input_image[start:start + block["length"]], f"{name} differs"
    assert image[cmgr_raw:cmgr_raw + probe.MANAGER_INIT_LENGTH - 5] == \
        input_image[cmgr_raw:cmgr_raw + probe.MANAGER_INIT_LENGTH - 5], "Manager init body differs"
    assert uimain == probe.probe_uimain((args.input_stage / "uimain.xml").read_bytes())
    removed = (args.input_stage / "uimain.xml").read_bytes().replace(probe.INCLUDE_LINE, b"", 1)
    assert uimain.replace(probe.INCLUDE_LINE, b"", 1) == removed and uimain.count(b"\r\n") == removed.count(b"\r\n") + 1
    print("PASS: only the three jumps and the PE section fields changed; manager blocks are byte-identical")
    print("PASS: uimain.xml is the 0.272.0 file plus one CRLF Custom6 include")
else:
    print("NOTE: byte-identity against the 0.272.0 input skipped (no --input-stage)")

ids = probe.probe_ids()
assert ids == tuple(range(0x900, 0x93B)) and manifest["clickEvents"] == [str(v) for v in range(2304, 2363)]
expected_slots = list(range(150, 169)) + list(range(170, 210))
assert list(probe.CLIENT_SLOTS) == expected_slots and manifest["clientSlots"] == expected_slots
assert [probe.inventory_source(i) for i in (0, 3, 4, 9, 10, 27, 28, 39, 40, 79, 80)] == \
    [10, 13, None, None, 21, 38, None, None, 40, 79, None]
assert probe.is_installation_path(Path("/mnt/d/Games/OfflineDAoC/runtime"))
assert not probe.is_installation_path(probe.default_output())
print("PASS: events 0x900-0x93a map to client slots 150-168 (worn) and 170-209 (backpack); output never inside the installation")

# Emulation setup.
uc = Uc(UC_ARCH_X86, UC_MODE_32)
uc.mem_map(0x400000, 0x2100000)
for section in pe.sections:
    if section.SizeOfRawData:
        uc.mem_write(base + section.VirtualAddress, bytes(section.get_data()))
uc.mem_map(0x5000000, 0x10000)
uc.mem_map(0x6000000, 0x40000)
uc.mem_map(0x7000000, 0x100000)
sp, frame, body, sentinel = 0x5008000, 0x5008100, 0x6030000, 0x603F000
UI, UI_VTABLE, WINDOW_SHOW, DRAG = 0x7000000, 0x7080000, 0x7090000, 0x70A0000
FLAGS = 0x000008D7
registrations, setter_calls, texts, moves, drags, end_drags, windows = [], [], {}, [], [], [], []
next_object = [0x6000000]


def read32(address):
    return struct.unpack("<I", uc.mem_read(address, 4))[0]


def write32(address, value):
    uc.mem_write(address, struct.pack("<I", value & 0xFFFFFFFF))


def cstring(address, limit=256):
    return bytes(uc.mem_read(address, limit)).split(b"\0")[0].decode("ascii")


def ret(cleanup=0):
    stack = uc.reg_read(UC_X86_REG_ESP)
    address = read32(stack)
    uc.reg_write(UC_X86_REG_ESP, stack + 4 + cleanup)
    uc.reg_write(UC_X86_REG_EIP, address)


def clobber():
    """Natives may change EAX, ECX and EDX; the probe must not rely on them afterwards."""
    for reg in (UC_X86_REG_EAX, UC_X86_REG_ECX, UC_X86_REG_EDX):
        uc.reg_write(reg, 0xDEADBEEF)


def allocate(size):
    address = next_object[0]
    next_object[0] += 64
    uc.mem_write(address, bytes(size))
    return address


def stub_text_registrar(stack):
    registry, slot, name = read32(stack + 4), read32(stack + 8), cstring(read32(stack + 12))
    obj = allocate(0x24)
    write32(slot, obj)
    registrations.append({"kind": "text", "registry": registry, "slot": slot, "name": name, "object": obj})
    ret(12)


def stub_scalar_registrar(stack):
    registry, slot, name = read32(stack + 4), read32(stack + 8), cstring(read32(stack + 12))
    maximum, initial = read32(stack + 16), read32(stack + 20)
    obj = allocate(0x14)
    write32(slot, obj)
    registrations.append({"kind": "scalar", "registry": registry, "slot": slot, "name": name, "object": obj,
                          "max": maximum, "initial": initial})
    ret(20)


def stub_scalar_setter(stack):
    setter_calls.append({"ecx": uc.reg_read(UC_X86_REG_ECX), "value": struct.unpack("<f", uc.mem_read(stack + 4, 4))[0]})
    clobber()
    ret(4)


def stub_text(stack):
    texts.setdefault(uc.reg_read(UC_X86_REG_EBX), []).append(cstring(read32(stack + 4)))
    clobber()
    ret(4)


def stub_send_move(stack):
    moves.append(tuple(read32(stack + 4 * index) for index in (1, 2, 3)))
    clobber()
    ret()


def stub_begin_drag(stack):
    drags.append({"eax": uc.reg_read(UC_X86_REG_EAX),
                  "args": tuple(read32(stack + 4 * index) for index in range(1, 7))})
    clobber()
    ret(0x18)


def stub_end_drag(stack):
    end_drags.append(True)
    clobber()
    ret()


def stub_window_show(stack):
    windows.append({"this": uc.reg_read(UC_X86_REG_ECX), "window": read32(stack + 4), "visible": read32(stack + 8)})
    clobber()
    ret(8)


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
    ret()


STUBS = {
    manager.REGISTER_TEXT_ADAPTER: stub_text_registrar,
    probe.REGISTER_SCALAR_ADAPTER: stub_scalar_registrar,
    probe.SET_SCALAR_VALUE: stub_scalar_setter,
    probe.SET_ADAPTER_TEXT: stub_text,
    probe.SEND_MOVE_ITEM: stub_send_move,
    probe.BEGIN_DRAG: stub_begin_drag,
    probe.END_DRAG: stub_end_drag,
    WINDOW_SHOW: stub_window_show,
    0x77156E: stub_stricmp,
    0x770FBE: stub_isdigit,
    0x7720C0: stub_atoi,
    manager.SEND_SLASH_COMMAND: stub_send_command,
}
STOP_AT = [None]


def hook(machine, address, size, user):
    if address == STOP_AT[0]:
        machine.emu_stop()
        return
    handler = STUBS.get(address)
    if handler is not None:
        handler(machine.reg_read(UC_X86_REG_ESP))


def run(begin, until, count):
    STOP_AT[0] = until
    try:
        uc.emu_start(begin, until, count=count)
    finally:
        STOP_AT[0] = None
    assert uc.reg_read(UC_X86_REG_EIP) == until, f"Stopped at {uc.reg_read(UC_X86_REG_EIP):#x}, expected {until:#x}"


uc.hook_add(UC_HOOK_CODE, hook)
uc.mem_write(WINDOW_SHOW, b"\xc3")
write32(UI, UI_VTABLE)
write32(UI_VTABLE + 8, WINDOW_SHOW)
write32(UI + probe.DRAG_RECORD, DRAG)
write32(probe.CURSOR_X, 412)
write32(probe.CURSOR_Y, 233)


def set_drag(kind, index, count=0):
    write32(DRAG + probe.DRAG_KIND, kind)
    write32(DRAG + probe.DRAG_INDEX, index)
    write32(UI + probe.DRAG_COUNT, count)


def set_item(slot, name, template):
    record = probe.ITEM_ARRAY + slot * probe.ITEM_STRIDE
    uc.mem_write(record, bytes(probe.ITEM_STRIDE))
    write32(record + probe.ITEM_TEMPLATE, template)
    uc.mem_write(record + probe.ITEM_NAME, name.encode("ascii") + b"\0")


def set_icon(template, icon):
    uc.mem_write(probe.ICON_TABLE + template * probe.ICON_STRIDE, struct.pack("<h", icon))


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


# Init: the manager's 538 labels, then the probe's four scalar icons and the text adapter, then 0x247E000.
write32(probe.WINDOW_MANAGER, UI)
set_registers()
write32(sp, sentinel)
before = snapshot()
run(cmgr_va, probe.RAID_INIT, count=600000)
after = snapshot()
assert after["esp"] == sp, "Stack unbalanced across the init"
for name in ("eax", "ebx", "ecx", "edx", "esi", "edi", "ebp", "eflags"):
    assert after[name] == before[name], f"Init changed {name}"
manager_entries = [entry for entry in registrations if entry["name"].startswith("cmgr_")]
assert [entry["name"] for entry in manager_entries] == \
    [manager.adapter_name(index) for index in range(manifest["layout"]["TotalLabelCount"])]
probe_entries = registrations[len(manager_entries):]
assert [entry["name"] for entry in probe_entries] == probe.adapter_names()
assert all(entry["registry"] == 0x123456 for entry in registrations), "The registry must be EDI"
icon_entries, text_entry = probe_entries[:-1], probe_entries[-1]
for index, entry in enumerate(icon_entries):
    assert entry["kind"] == "scalar" and entry["slot"] == manifest["addresses"]["iconSlots"] + 4 * index
    assert entry["max"] == probe.float_bits(1000000.0) and entry["initial"] == probe.float_bits(-1.0), \
        "Icon adapters: max 1e6, initial -1 (the quiver's values)"
assert text_entry["kind"] == "text" and text_entry["slot"] == manifest["addresses"]["textSlot"]
icon_objects = [entry["object"] for entry in icon_entries]
text_object = text_entry["object"]
print("PASS: init registers cpdl_icon_0-3 (scalar, max 1e6, initial -1) and cpdl_text after the 538 manager labels")
print("PASS: init returns to 0x247E000 with registers, flags, and stack restored")


def packet(operation, index=0, text="", length=128, version=manager.PROTOCOL_VERSION, marker=manager.MARKER,
           terminator=0):
    data = bytearray(128)
    data[1:4] = bytes((marker, version, operation))
    data[4:6] = struct.pack("<H", index)
    data[12:12 + len(text)] = text.encode("ascii")
    data[127] = terminator
    uc.mem_write(body, bytes(data))
    write32(frame + 0x18, length)
    set_registers()
    uc.reg_write(UC_X86_REG_EBX, body)
    write32(sp, sentinel)


def run_packet(expect_manager):
    """Runs the hooked packet path; returns the state on arrival at the manager block or at PACKET_DONE."""
    entry = snapshot()
    stop = manager_packet if expect_manager else manager.PACKET_DONE
    run(probe_packet, stop, count=400000)
    arrival = snapshot()
    if expect_manager:
        for name in ("eax", "ebx", "ecx", "edx", "esi", "edi", "ebp", "esp"):
            assert arrival[name] == entry[name], f"Pass-through changed {name}"
    else:
        # Flags are not restored here (the compares run before pushfd), as in the manager's own packet block.
        for name in ("eax", "ebx", "ecx", "edx", "esi", "edi", "ebp", "esp"):
            assert arrival[name] == entry[name], f"Probe packet changed {name}"
    return arrival


# Manager packets still work through the filter.
packet(manager.OP_LABEL, manifest["layout"]["LabelStatus"], "Roster 2/78")
run_packet(expect_manager=True)
run(manager_packet, manager.PACKET_DONE, count=200000)
assert texts[manager_entries[manifest["layout"]["LabelStatus"]]["object"]] == ["Roster 2/78"]
for operation, kwargs in ((10, {}), (6, {}), (probe.OP_REFRESH, {"version": 2}), (probe.OP_REFRESH, {"length": 127}),
                          (probe.OP_REFRESH, {"terminator": 1}), (probe.OP_REFRESH, {"marker": 0x52})):
    calls = (len(setter_calls), len(windows))
    packet(operation, **kwargs)
    run_packet(expect_manager=True)
    assert (len(setter_calls), len(windows)) == calls
print("PASS: manager operations, other markers, other versions, short and unterminated bodies pass to the manager")

# Refresh: helm present (icon 0x1a2), chest empty, bag 21 with an out-of-range template, bag 22 present.
set_item(150, "Matterbender Chain Helm", 17)
set_icon(17, 0x1A2)
set_item(151, "", 0)
set_item(170, "Broken Record", 0x20000)
set_item(171, "Willful Ring", 0x2B0)
set_icon(0x2B0, 0x38D)
expected_icons = [0x1A2] + [-1] * 19 + [0x38D] + [-1] * 38
packet(probe.OP_REFRESH)
run_packet(expect_manager=False)
assert windows == [], "Refresh alone must not show the window"
assert setter_calls[-59:] == [{"ecx": obj, "value": float(value)} for obj, value in zip(icon_objects, expected_icons)]
assert texts[text_object][-1] == "R 02", texts[text_object][-1]
print("PASS: op 7 sets all 59 icons from the item array (-1 for empty or out-of-range); status 'R 02'")

packet(probe.OP_SHOW)
run_packet(expect_manager=False)
assert windows[-1] == {"this": UI, "window": 0x73, "visible": 1} and len(setter_calls) % 59 == 0
assert setter_calls[-59:] == [{"ecx": obj, "value": float(value)} for obj, value in zip(icon_objects, expected_icons)]
packet(probe.OP_HIDE)
setters = len(setter_calls)
run_packet(expect_manager=False)
assert windows[-1] == {"this": UI, "window": 0x73, "visible": 0} and len(setter_calls) == setters
write32(probe.WINDOW_MANAGER, 0)
packet(probe.OP_SHOW)
shown = len(windows)
run_packet(expect_manager=False)
assert len(windows) == shown
write32(probe.WINDOW_MANAGER, UI)
print("PASS: op 8 shows Custom6 (0x73) and refreshes; op 9 hides it; a null window manager is skipped")


def fire(event_id, stop=None):
    set_registers()
    uc.mem_write(body, bytes(16))
    write32(body + 8, event_id)
    uc.reg_write(UC_X86_REG_EAX, body)
    write32(sp, sentinel)
    entry = snapshot()
    run(probe_handler, stop or sentinel, count=200000)
    return entry, snapshot()


def click(event_id, expected_text):
    entry, exit_state = fire(event_id)
    assert exit_state["esp"] == sp + 4, f"{event_id:#x} stack unbalanced"
    assert exit_state["eax"] == (entry["eax"] & 0xFFFFFF00) | 1, "AL=1, rest of EAX unchanged"
    for name in ("ebx", "ecx", "edx", "esi", "edi", "ebp", "eflags"):
        assert exit_state[name] == entry[name], f"{event_id:#x} changed {name}"
    if expected_text is not None:
        assert texts[text_object][-1] == expected_text, (texts[text_object][-1], expected_text)


# Drops: kind 9 bag slot 40 onto the helm; weapon and armour hotspots map like the stock inventory drop.
set_drag(9, 0x28, count=1)
click(0x900, "D 09:0028>096")
assert moves[-1] == (150, 40, 1) and len(end_drags) == 1
set_drag(9, 2)
click(0x901, "D 09:000c>097")
assert moves[-1] == (151, 12, 0)
set_drag(9, 15)
click(0x913, "D 09:001a>0aa")
assert moves[-1] == (170, 26, 0)
set_drag(9, 79)
click(0x914, "D 09:004f>0ab")
assert moves[-1] == (171, 79, 0) and len(end_drags) == 4
print("PASS: an inventory drag (kind 9) dropped on a slot sends move(slot, source, count) and ends the drag")
print("PASS: hotspots 0-3 -> 10-13, 10-27 -> 21-38, 40-79 as is (the stock 0x408151 mapping)")

# A probe pick-up (kind 0x66) dropped on another slot, and back on its own slot.
set_drag(0x66, 150)
click(0x913, "D 66:0096>0aa")
assert moves[-1] == (170, 150, 0) and len(end_drags) == 5
set_drag(0x66, 170)
sent = len(moves)
click(0x913, "C 0aa")
assert len(moves) == sent and len(end_drags) == 6
print("PASS: a kind-0x66 drag moves between slots; dropping it on its own slot only ends the drag")

# Refused sources: unmapped hotspots and other drag kinds send nothing and keep the drag.
for kind, index, text in ((9, 5, "X 09:0005"), (9, 0x50, "X 09:0050"), (9, 0x1C, "X 09:001c"),
                          (0x5B, 3, "X 5b:0003"), (0x39, 7, "X 39:0007")):
    set_drag(kind, index)
    sent, ended = len(moves), len(end_drags)
    click(0x900, text)
    assert len(moves) == sent and len(end_drags) == ended
print("PASS: unmapped hotspots and other drag kinds (house vault, player vault) send nothing")

# Empty cursor: a filled slot starts a kind-0x66 drag at the cursor; an empty slot does nothing.
set_drag(0xFFFFFFFF, 0xFFFFFFFF)
click(0x900, "P 096 i=01a2")
assert drags[-1] == {"eax": 0x1A2, "args": (UI, 0x66, 150, 0, 412, 233)}
click(0x914, "P 0ab i=038d")
assert drags[-1]["args"][2] == 171 and drags[-1]["eax"] == 0x38D
picked = len(drags)
click(0x901, "E 097")
click(0x913, "E 0aa")
assert len(drags) == picked and not any(m for m in moves[sent:])
write32(UI + probe.DRAG_RECORD, 0)
click(0x900, "P 096 i=01a2")
write32(UI + probe.DRAG_RECORD, DRAG)
print("PASS: an empty cursor on a filled slot calls BeginDrag(ui, 0x66, slot, 0, cursor x, cursor y), EAX = icon")
print("PASS: an empty slot or an out-of-range template does nothing")

write32(probe.WINDOW_MANAGER, 0)
before_counts = (len(moves), len(drags), len(texts[text_object]))
click(0x900, None)
assert (len(moves), len(drags), len(texts[text_object])) == before_counts
write32(probe.WINDOW_MANAGER, UI)
print("PASS: a null UI manager returns AL=1 without any call")

# Pass-through: every other ID reaches the manager eventHandler unchanged.
raid_ids = (0x123, 0x5FF, 0x600, 0x64F, 0x650, 0x6FF, 0x7E0, 0x7E1, 0x7FF, 0x8FF, 0x93B, 0x93F, 0xA00)
for event_id in raid_ids + (0x700, 0x7DF):
    entry, arrival = fire(event_id, stop=manager_event)
    assert arrival["eax"] == body and arrival["esp"] == sp and arrival["eflags"] == entry["eflags"]
    for name in ("ebx", "ecx", "edx", "esi", "edi", "ebp"):
        assert arrival[name] == entry[name], f"{event_id:#x} changed {name}"
    if event_id in raid_ids:
        run(manager_event, manager.RAID_EVENT_HANDLER, count=100000)
print("PASS: IDs outside 0x900-0x93a reach the manager eventHandler unchanged; raid IDs reach the raid handler")


def stock_click_event(value):
    set_registers()
    write32(sp, sentinel)
    uc.mem_write(body, value.encode("ascii") + b"\0")
    uc.reg_write(UC_X86_REG_ESI, body)
    run(manager.CLICK_EVENT_MAPPER, sentinel, count=2000000)
    return uc.reg_read(UC_X86_REG_EAX)


assert [stock_click_event(value) for value in manifest["clickEvents"]] == list(ids)
print("PASS: the stock OnClickEvent mapper turns 2304-2362 into 0x900-0x93a")

# XML: Custom6, four sockets/icons/click areas on the same squares, registered adapter names only.
window = ET.fromstring(window_bytes["atlantis"]).find("WindowTemplate")
assert window.find("WindowId").text == "Custom6"
assert (window.find("Width").text, window.find("Height").text) == (str(probe.WINDOW_WIDTH), str(probe.WINDOW_HEIGHT))
icons = window.findall("IconDef")
buttons = window.findall("InvisibleButtonDef")
sockets = window.findall("ImageAreaDef")
assert [icon.find("AdapterName").text for icon in icons] == [f"cpdl_icon_{i}" for i in range(59)]
assert all(icon.find("TemplateName").text == "plain_icon_large" for icon in icons)
assert [button.find("OnClickEvent").text for button in buttons] == manifest["clickEvents"]
assert all(socket.find("TemplateName").text == "icon_socket_large" for socket in sockets) and len(sockets) == 59
for index, (icon, button) in enumerate(zip(icons, buttons)):
    position = (int(icon.find("Position/X").text), int(icon.find("Position/Y").text))
    assert position == (int(button.find("Position/X").text), int(button.find("Position/Y").text))
    assert (int(button.find("Width").text), int(button.find("Height").text)) == (probe.ICON, probe.ICON)
    assert list(position) + [probe.ICON, probe.ICON] == manifest["slotRectangles"][index]
rectangles = manifest["slotRectangles"]
for a in range(59):
    for b in range(a + 1, 59):
        ax, ay, aw, ah = rectangles[a]
        bx, by, bw, bh = rectangles[b]
        assert not (ax < bx + bw and bx < ax + aw and ay < by + bh and by < ay + ah), "Slots overlap"
    x, y, w, h = rectangles[a]
    assert 0 <= x and x + w <= probe.WINDOW_WIDTH and 18 <= y and y + h <= probe.WINDOW_HEIGHT
adapters = {element.text for element in window.iter("Adapter")} | {icon.find("AdapterName").text for icon in icons}
assert adapters == set(probe.adapter_names()), adapters
registered = {entry["name"] for entry in registrations}
assert adapters <= registered
templates = {element.text for element in window.iter("TemplateName")}
if args.stock_ui.is_dir():
    for skin in ("atlantis", "isles"):
        stock = b"".join(path.read_bytes() for path in (args.stock_ui / skin).glob("*.xml"))
        for template in templates:
            assert f"<Name>{template}</Name>".encode() in stock, f"{template} is not a stock {skin} template"
    print("PASS: every template (sockets, large icons, background) is a stock Atlantis and Isles template")
else:
    print("NOTE: stock template check skipped (no stock UI folder)")
print("PASS: Custom6 has four sockets, icons and click areas on the same 32x32 squares; adapters are registered")
protocol = (Path(__file__).resolve().parents[1] / "GameServer/bots/CompanionManagerProtocol.cs").read_text(encoding="utf-8")
declared = {name: int(value, 0) for name, value in re.findall(r"public const (?:int|byte) (\w+) = (0x[0-9A-Fa-f]+|\d+);", protocol)}
for name, value in (("OpPaperdollRefresh", probe.OP_REFRESH), ("OpPaperdollShow", probe.OP_SHOW),
                    ("OpPaperdollHide", probe.OP_HIDE)):
    assert declared.get(name) == value, f"CompanionManagerProtocol.{name} = {declared.get(name)}, probe {value}"
assert {probe.OP_REFRESH, probe.OP_SHOW, probe.OP_HIDE}.isdisjoint(
    {declared[name] for name in ("OpLabel", "OpShow", "OpHide", "OpToken", "OpBarShow", "OpBarHide")})
print("PASS: server operations 7, 8, 9 match CompanionManagerProtocol.cs and differ from the manager's 1-6")
print("NOTE: emulation proves the probe logic only; the owner's real-client session answers R1-R4")
