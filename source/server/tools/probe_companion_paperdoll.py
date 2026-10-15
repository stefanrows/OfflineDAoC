"""Research probe for the companion paperdoll (TASKS 129). This is NOT the paperdoll window.

Stages a copy of the 0.272.0 Companion Manager client with one extra PE section, `.cpdl`, and a small
Custom6 window with four item slots: the companion's helm and chest (house-vault client slots 150 and
151, positions 1 and 2 of PersistentCompanionInventoryView) and its first two backpack slots (170 and
171, positions 21 and 22). One real-client session answers the risks in docs/COMPANION_PAPERDOLL_RE.md:

R1  a click on a slot while an item is on the cursor reaches the event hook with the drag record set;
R2  an IconDef in a Custom window draws the icon number held by a scalar adapter (-1 draws nothing);
R3  BeginDrag called from the event hook picks the slot's item up, and the stock inventory drop moves it;
R4  the pick-up does not end at the same click.

The pattern is the stock quiver window's click handler (0x534F40): an item on the cursor is sent to the
slot with the stock move-item sender; an empty cursor on a filled slot starts a kind-0x66 drag whose
index is the client slot, which the stock inventory drop turns into move(dest, slot, 0).

Changes to the 0.272.0 manager: three jumps. The manager init's final `jmp 0x247E000` enters the probe
init; the packet hook at 0x411201 enters the probe packet filter (operations 7-9, everything else goes
to the manager packet block unchanged); the event hook at 0x4E04DE enters the probe event handler
(0x900-0x903, everything else goes to the manager eventHandler unchanged). The manager's blocks are
byte-identical.

Server to client (protocol 3 bodies; a client without this probe ignores the new operations):
  7 refresh the four icons from the client's item array, 8 show Custom6 and refresh, 9 hide Custom6.

Offline emulation (test_companion_paperdoll_probe.py) proves the hook logic only. It is not acceptance.
Output: a new directory with game.dll, uimain.xml (the 0.272.0 file plus one Custom6 include), atlantis and
isles custom6_window.xml, and manifest.json with "probe": true. Never written inside the installation;
deterministic (no timestamps).
"""

import argparse
import datetime
import hashlib
import json
from pathlib import Path
import struct
import xml.etree.ElementTree as ET

import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from keystone import Ks, KS_ARCH_X86, KS_MODE_32

import build_companion_manager_client as manager


# Verified inputs (the 0.272.0 stage). The builder fails closed on any other file.
INPUT_GAME_SHA256 = "c938e6b50057a4a99ec39211004765dcacb3ffa47880e4ba3fc193add87e612d"
INPUT_UIMAIN_SHA256 = "7d573c315a56bcacae8e8ebc871646654bfe8da5bb1dc745d6023da7f93b9dd9"
INPUT_CUSTOM8_SHA256 = "9cd3b8a5fb3d2a4671192b9d52287173e2fe194a4f19979de1c339f408235efd"
INPUT_CUSTOM7_SHA256 = "5b82057450ac1d510fe66bba5813f0f3f4d7a9fda263bfd23d4638be2cd69bac"

INSTALLATION = Path("/mnt/d/Games/OfflineDAoC")
DEFAULT_OUTPUT_ROOT = Path("/mnt/d/Games/OfflineDAoC-dev")

RAID_INIT = manager.RAID_INIT
# Verified manager layout (the .cmgr section is the last section of the 0.272.0 game.dll).
MANAGER_INIT_LENGTH = 91
MANAGER_PACKET_OFFSET = 0x1000
MANAGER_EVENT_OFFSET = 0x1C00
MANAGER_SEND_OFFSET = 0x2600
HOOK_INIT = 0x4DA938     # stock -> manager init (target .cmgr+0)
HOOK_PACKET = 0x411201   # stock -> manager packet (target .cmgr+0x1000)
HOOK_EVENT = 0x4E04DE    # stock event dispatch -> manager eventHandler (target .cmgr+0x1C00)

# Probe events (OnClickEvent 2304-2307). Raid 0x600-0x64F, manager 0x700-0x7DF, client helpers 0x7E0/0x7E1.
EVENT_BASE = 0x900
# PersistentCompanionInventoryView positions: 1-19 worn (character-sheet order), 21-60 backpack.
WORN_CAPTIONS = ("Helm", "Chest", "Arms", "Hands", "Legs", "Feet", "Cloak", "Neck", "Jewel", "Belt",
                 "L Wrist", "R Wrist", "L Ring", "R Ring", "R Hand", "L Hand", "2 Hand", "Ranged", "Mythic")
BACKPACK_SLOTS = 40
SLOT_POSITIONS = tuple(range(1, len(WORN_CAPTIONS) + 1)) + tuple(range(21, 21 + BACKPACK_SLOTS))
SLOT_CAPTIONS = WORN_CAPTIONS + tuple(f"Bag {index + 1}" for index in range(BACKPACK_SLOTS))
FIRST_CLIENT_SLOT = 150                  # eInventorySlot.HousingInventory_First
CLIENT_SLOTS = tuple(FIRST_CLIENT_SLOT + position - 1 for position in SLOT_POSITIONS)
SLOTS = len(CLIENT_SLOTS)
RAID_IDS = (0x600, 0x650)
MANAGER_IDS = (0x700, 0x7E0)
HELPER_IDS = (0x7E0, 0x7E1)

# Protocol 3 operations added by the probe.
OP_REFRESH, OP_SHOW, OP_HIDE = 7, 8, 9
PAPERDOLL_WINDOW = 0x73                  # Custom6: the ShowCustomN switch pushes 0x6D + N

ADAPTER_ICON = "cpdl_icon_{}"
ADAPTER_TEXT = "cpdl_text"
ICON_MAX = 1000000.0                     # the quiver's icon adapters: max 1e6, initial -1
ICON_INITIAL = -1.0
ICON_EMPTY = -1
TEMPLATE_LIMIT = 0x10000                 # 0x106A7F2 + 0xFFFF * 0xC4 stays inside .data

# Native client entry points and data. Each return size is verified against the binary by verify_natives().
WINDOW_MANAGER = manager.WINDOW_MANAGER          # [ui] UI manager; [manager]->vtbl[2](window id, visible)
DRAG_RECORD = 0x168                               # [ui+0x168]: +0x80 kind, +0x84 index (-1 none)
DRAG_COUNT = 0x16C                                # [ui+0x16C]
DRAG_KIND, DRAG_INDEX = 0x80, 0x84
CURSOR_X, CURSOR_Y = 0x99DFE8, 0x99DFEC           # cursor in UI coordinates (stock BeginDrag callers)
ITEM_ARRAY = 0xF96FB4                             # record(s) = ITEM_ARRAY + s * ITEM_STRIDE
ITEM_STRIDE = 0x1D4
ITEM_TEMPLATE, ITEM_NAME = 0x00, 0x48             # template index (int), name (char[64]; empty = no item)
ICON_TABLE = 0x106A7F2                            # *(short *)(ICON_TABLE + template * ICON_STRIDE)
ICON_STRIDE = 0xC4
KIND_INVENTORY = 9                                # player paperdoll and bag: hotspot index
KIND_QUIVER = 0x66                                # quiver drag: the index is the client slot
REGISTER_TEXT_ADAPTER = manager.REGISTER_TEXT_ADAPTER   # 0x4B6C84 stdcall(registry, slot*, name)
REGISTER_SCALAR_ADAPTER = 0x4B6BC0                      # stdcall(registry, slot*, name, maxBits, initialBits)
SET_SCALAR_VALUE = 0x524370                             # ECX = scalar object, stdcall(float), ret 4
SET_ADAPTER_TEXT = manager.SET_ADAPTER_TEXT             # 0x524436, EBX = adapter, stdcall(text)
SEND_MOVE_ITEM = 0x42A976                               # cdecl(to, from, count): opcode 0xDD
BEGIN_DRAG = 0x4C34CC                                   # stdcall(ui, kind, index, flag, x, y), EAX = cursor icon
END_DRAG = 0x47FC63                                     # no arguments: EAX = 0x20, ECX = ui, call 0x4C2D72
PACKET_DONE = manager.PACKET_DONE
NATIVE_RETURN_BYTES = {
    REGISTER_TEXT_ADAPTER: 12,
    REGISTER_SCALAR_ADAPTER: 20,
    SET_SCALAR_VALUE: 4,
    SET_ADAPTER_TEXT: 4,
    SEND_MOVE_ITEM: 0,
    BEGIN_DRAG: 0x18,
    END_DRAG: 0,
}

# Offsets inside the .cpdl section.
INIT_OFFSET = 0x0000
SHOW_TEXT_OFFSET = 0x00C0
HEXFMT_OFFSET = 0x00E0
PACKET_OFFSET = 0x0100
HANDLER_OFFSET = 0x0300
CODE_LIMIT = 0x1000
DATA_OFFSET = 0x1000
HEX_DIGITS = DATA_OFFSET + 0x00
TEXT_SLOT = DATA_OFFSET + 0x10
SCRATCH = DATA_OFFSET + 0x20              # int -> float conversion
TEXT_BUFFER = DATA_OFFSET + 0x40          # status line, at most 63 characters
ICON_SLOTS = DATA_OFFSET + 0x100          # SLOTS adapter object pointers
CLIENT_SLOT_TABLE = DATA_OFFSET + 0x200   # SLOTS dwords
ICON_VALUES = DATA_OFFSET + 0x300         # last icon written per slot (int)
NAMES = DATA_OFFSET + 0x400               # 16 bytes per name: SLOTS icons, then the text adapter
PAYLOAD_SIZE = NAMES + 16 * (SLOTS + 1)
SECTION_NAME = b".cpdl\0\0\0"
SECTION_CHARACTERISTICS = 0xE0000060

# Custom6 geometry: worn gear in three columns with captions, the backpack as an 8 x 5 grid.
SOCKET = 34
ICON = 32
WORN_X, WORN_Y, WORN_PITCH_X, WORN_PITCH_Y, WORN_COLUMNS = 14, 30, 58, 50, 3
# Worn slot index (SheetSlots order) at each (column, row) of the worn panel.
WORN_LAYOUT = ((0, 0), (0, 1), (2, 1), (0, 2), (2, 2), (0, 3),   # helm, chest, arms, hands, legs, feet
               (2, 0), (1, 0), (1, 1), (1, 2),                   # cloak, neck, jewel, belt
               (1, 3), (2, 3), (0, 4), (1, 4),                   # wrists, left ring, right ring
               (0, 5), (1, 5), (2, 5), (0, 6), (2, 4))           # right hand, left hand, two hand, ranged, mythic
BAG_X, BAG_Y, BAG_PITCH, BAG_COLUMNS = 206, 48, 38, 8
CAPTION_WIDTH = 56
WINDOW_WIDTH = BAG_X + BAG_COLUMNS * BAG_PITCH + 8
WINDOW_HEIGHT = WORN_Y + 7 * WORN_PITCH_Y + 6
STATUS_X, STATUS_Y, STATUS_WIDTH = BAG_X, BAG_Y + 5 * BAG_PITCH + 10, BAG_COLUMNS * BAG_PITCH
HELP_Y = STATUS_Y + 22
HELP_TEXT = ("Click your item, then a slot: give or equip.",
             "Click a filled slot to pick its item up.",
             "Starter gear stays with the companion.")


def slot_origin(index):
    """Top-left of slot `index`'s socket (0-18 worn, 19-58 backpack)."""
    if index < len(WORN_CAPTIONS):
        column, row = WORN_LAYOUT[index]
        return WORN_X + column * WORN_PITCH_X, WORN_Y + row * WORN_PITCH_Y
    bag = index - len(WORN_CAPTIONS)
    return BAG_X + (bag % BAG_COLUMNS) * BAG_PITCH, BAG_Y + (bag // BAG_COLUMNS) * BAG_PITCH


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def float_bits(value):
    return struct.unpack("<I", struct.pack("<f", value))[0]


def probe_ids():
    ids = tuple(EVENT_BASE + index for index in range(SLOTS))
    for probe in ids:
        if RAID_IDS[0] <= probe < RAID_IDS[1] or MANAGER_IDS[0] <= probe < MANAGER_IDS[1] or probe in HELPER_IDS:
            raise ValueError(f"Probe ID {probe:#x} overlaps the raid, manager, or helper range")
    return ids


def adapter_names():
    return [ADAPTER_ICON.format(index) for index in range(SLOTS)] + [ADAPTER_TEXT]


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
    """The first ret of each native entry point must pop the expected byte count."""
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


def verify_manager_init(pe, image, cmgr_va):
    """The manager init block must decode to exactly MANAGER_INIT_LENGTH bytes ending in `jmp 0x247E000`."""
    base = pe.OPTIONAL_HEADER.ImageBase
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    code = image[pe.get_offset_from_rva(cmgr_va - base):][:MANAGER_INIT_LENGTH + 16]
    last = None
    for insn in md.disasm(code, cmgr_va):
        if insn.address >= cmgr_va + MANAGER_INIT_LENGTH:
            break
        last = insn
    if last is None or last.mnemonic != "jmp" or last.op_str != f"{RAID_INIT:#x}":
        raise ValueError("Manager init block does not end with jmp 0x247E000")
    if last.address + last.size != cmgr_va + MANAGER_INIT_LENGTH:
        raise ValueError(f"Manager init block length differs from the verified {MANAGER_INIT_LENGTH} bytes")
    return last.address


def verify_hook(pe, image, address, target, length):
    """A five-byte E9 jump plus NOP padding up to `length`, already pointing at `target`. Returns its file offset."""
    base = pe.OPTIONAL_HEADER.ImageBase
    offset = pe.get_offset_from_rva(address - base)
    site = image[offset:offset + length]
    if site[0] != 0xE9 or site[5:length] != b"\x90" * (length - 5):
        raise ValueError(f"Manager hook at {address:#x} is not a five-byte jump with NOP padding")
    if address + 5 + struct.unpack("<i", site[1:5])[0] != target:
        raise ValueError(f"Manager hook at {address:#x} does not target {target:#x}")
    return offset


def assemble(va, manager_packet_va, manager_event_va, ks):
    """Returns (payload, blocks, addresses) for the probe init, packet filter, event handler and data."""
    payload = bytearray(PAYLOAD_SIZE)
    payload[HEX_DIGITS:HEX_DIGITS + 16] = b"0123456789abcdef"
    for index, slot in enumerate(CLIENT_SLOTS):
        struct.pack_into("<I", payload, CLIENT_SLOT_TABLE + 4 * index, slot)
        struct.pack_into("<i", payload, ICON_VALUES + 4 * index, ICON_EMPTY)
    for index, name in enumerate(adapter_names()):
        encoded = name.encode("ascii") + b"\0"
        payload[NAMES + 16 * index:NAMES + 16 * index + len(encoded)] = encoded
    blocks = {}

    def put(label, offset, assembly, limit):
        code, _ = ks.asm(assembly, addr=va + offset)
        if offset + len(code) > limit:
            raise ValueError(f"{label} exceeds reserved code space")
        payload[offset:offset + len(code)] = bytes(code)
        blocks[label] = {"address": va + offset, "length": len(code)}

    icon_slots, text_slot = va + ICON_SLOTS, va + TEXT_SLOT
    slot_table, icon_values, scratch = va + CLIENT_SLOT_TABLE, va + ICON_VALUES, va + SCRATCH
    text, hex_digits, names = va + TEXT_BUFFER, va + HEX_DIGITS, va + NAMES
    # EDI is the adapter registry, as at the manager's own registrations. ESI and EDI survive the
    # stdcall registrar.
    put("init", INIT_OFFSET, f"""
        pushfd
        pushad
        xor esi, esi
    register_icon:
        push {float_bits(ICON_INITIAL):#x}
        push {float_bits(ICON_MAX):#x}
        mov eax, esi
        shl eax, 4
        add eax, {names:#x}
        push eax
        lea eax, [{icon_slots:#x}+esi*4]
        push eax
        push edi
        call {REGISTER_SCALAR_ADAPTER:#x}
        inc esi
        cmp esi, {SLOTS}
        jb register_icon
        push {names + 16 * SLOTS:#x}
        push {text_slot:#x}
        push edi
        call {REGISTER_TEXT_ADAPTER:#x}
        popad
        popfd
        jmp {RAID_INIT:#x}
    """, SHOW_TEXT_OFFSET)

    show_text, hexfmt = va + SHOW_TEXT_OFFSET, va + HEXFMT_OFFSET
    # EBX = the text adapter object (SET_ADAPTER_TEXT convention); the text is the probe's buffer.
    put("showText", SHOW_TEXT_OFFSET, f"""
        push ebx
        mov ebx, dword ptr [{text_slot:#x}]
        test ebx, ebx
        jz show_text_done
        push {text:#x}
        call {SET_ADAPTER_TEXT:#x}
    show_text_done:
        pop ebx
        ret
    """, HEXFMT_OFFSET)
    # EAX = value, ECX = digit count, EDI = destination; writes ECX lowercase hex digits, keeps EDI and EDX.
    put("hexfmt", HEXFMT_OFFSET, f"""
        push edi
        push edx
        add edi, ecx
    hex_loop:
        dec edi
        mov edx, eax
        and edx, 15
        mov dl, byte ptr [{hex_digits:#x}+edx]
        mov byte ptr [edi], dl
        shr eax, 4
        dec ecx
        jnz hex_loop
        pop edx
        pop edi
        ret
    """, PACKET_OFFSET)

    # EBX is the packet body and [EBP+0x18] its length (the manager's own convention). Only compares run
    # before the decision, so a packet that is not a probe operation reaches the manager unchanged.
    put("packet", PACKET_OFFSET, f"""
        cmp dword ptr [ebp+0x18], {manager.BODY_SIZE}
        jb to_manager
        cmp byte ptr [ebx+1], {manager.MARKER}
        jne to_manager
        cmp byte ptr [ebx+2], {manager.PROTOCOL_VERSION}
        jne to_manager
        cmp byte ptr [ebx+{manager.BODY_SIZE - 1}], 0
        jne to_manager
        cmp byte ptr [ebx+3], {OP_REFRESH}
        jb to_manager
        cmp byte ptr [ebx+3], {OP_HIDE}
        ja to_manager
        pushfd
        pushad
        movzx esi, byte ptr [ebx+3]
        cmp esi, {OP_REFRESH}
        je refresh
        mov ecx, dword ptr [{WINDOW_MANAGER:#x}]
        test ecx, ecx
        jz packet_done
        xor eax, eax
        cmp esi, {OP_SHOW}
        sete al
        push eax
        push {PAPERDOLL_WINDOW:#x}
        mov eax, dword ptr [ecx]
        call dword ptr [eax+8]
        cmp esi, {OP_SHOW}
        jne packet_done
    refresh:
        mov edi, {text:#x}
        mov byte ptr [edi], 0x52
        mov byte ptr [edi+1], 0x20
        add edi, 2
        xor esi, esi
        xor ebx, ebx
    refresh_slot:
        mov eax, dword ptr [{slot_table:#x}+esi*4]
        imul eax, eax, {ITEM_STRIDE:#x}
        mov ecx, {ICON_EMPTY}
        cmp byte ptr [eax+{ITEM_ARRAY + ITEM_NAME:#x}], 0
        je have_icon
        mov edx, dword ptr [eax+{ITEM_ARRAY + ITEM_TEMPLATE:#x}]
        cmp edx, {TEMPLATE_LIMIT:#x}
        jae have_icon
        imul edx, edx, {ICON_STRIDE:#x}
        movsx ecx, word ptr [edx+{ICON_TABLE:#x}]
    have_icon:
        mov dword ptr [{icon_values:#x}+esi*4], ecx
        mov dword ptr [{scratch:#x}], ecx
        fild dword ptr [{scratch:#x}]
        fstp dword ptr [{scratch + 4:#x}]
        mov ecx, dword ptr [{icon_slots:#x}+esi*4]
        test ecx, ecx
        jz icon_set
        push dword ptr [{scratch + 4:#x}]
        call {SET_SCALAR_VALUE:#x}
    icon_set:
        cmp dword ptr [{icon_values:#x}+esi*4], {ICON_EMPTY}
        je icon_counted
        inc ebx
    icon_counted:
        inc esi
        cmp esi, {SLOTS}
        jb refresh_slot
        mov eax, ebx
        mov ecx, 2
        call {hexfmt:#x}
        add edi, 2
        mov byte ptr [edi], 0
        call {show_text:#x}
    packet_done:
        popad
        popfd
        jmp {PACKET_DONE:#x}
    to_manager:
        jmp {manager_packet_va:#x}
    """, HANDLER_OFFSET)

    # EAX is the event record, [EAX+8] its ID. pushfd comes first, so a pass-through leaves EAX and the
    # flags exactly as the hook received them. A handled event returns AL=1 to the stock dispatcher.
    put("eventHandler", HANDLER_OFFSET, f"""
        pushfd
        cmp dword ptr [eax+8], {EVENT_BASE:#x}
        jb passthrough
        cmp dword ptr [eax+8], {EVENT_BASE + SLOTS - 1:#x}
        ja passthrough
        pushad
        mov ecx, dword ptr [eax+8]
        sub ecx, {EVENT_BASE:#x}
        mov esi, dword ptr [{slot_table:#x}+ecx*4]
        mov edi, {text:#x}
        mov edx, dword ptr [{WINDOW_MANAGER:#x}]
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
        jne ignored
        cmp ebx, 0
        jl ignored
        cmp ebx, 3
        jle weapon
        cmp ebx, 10
        jl ignored
        cmp ebx, 27
        jle armour
        cmp ebx, 40
        jl ignored
        cmp ebx, 79
        jle have_source
        jmp ignored
    weapon:
        add ebx, 10
        jmp have_source
    armour:
        add ebx, 11
    have_source:
        cmp ebx, esi
        jne send_move
        mov byte ptr [edi], 0x43
        call slot_text
        mov byte ptr [edi], 0
        call {END_DRAG:#x}
        jmp show
    send_move:
        mov byte ptr [edi], 0x44
        call kind_text
        mov byte ptr [edi], 0x3e
        inc edi
        mov eax, esi
        mov ecx, 3
        call {hexfmt:#x}
        add edi, 3
        mov byte ptr [edi], 0
        push dword ptr [edx+{DRAG_COUNT:#x}]
        push ebx
        push esi
        call {SEND_MOVE_ITEM:#x}
        add esp, 12
        call {END_DRAG:#x}
        jmp show
    ignored:
        mov byte ptr [edi], 0x58
        mov ebx, dword ptr [ebp+{DRAG_INDEX:#x}]
        call kind_text
        mov byte ptr [edi], 0
        jmp show
    no_drag:
        mov eax, esi
        imul eax, eax, {ITEM_STRIDE:#x}
        cmp byte ptr [eax+{ITEM_ARRAY + ITEM_NAME:#x}], 0
        je empty
        mov ecx, dword ptr [eax+{ITEM_ARRAY + ITEM_TEMPLATE:#x}]
        cmp ecx, {TEMPLATE_LIMIT:#x}
        jae empty
        imul ecx, ecx, {ICON_STRIDE:#x}
        movsx ecx, word ptr [ecx+{ICON_TABLE:#x}]
        push ecx
        push dword ptr [{CURSOR_Y:#x}]
        push dword ptr [{CURSOR_X:#x}]
        push 0
        push esi
        push {KIND_QUIVER:#x}
        push edx
        mov eax, ecx
        call {BEGIN_DRAG:#x}
        pop ebx
        mov byte ptr [edi], 0x50
        call slot_text
        mov byte ptr [edi], 0x20
        mov byte ptr [edi+1], 0x69
        mov byte ptr [edi+2], 0x3d
        add edi, 3
        mov eax, ebx
        mov ecx, 4
        call {hexfmt:#x}
        add edi, 4
        mov byte ptr [edi], 0
        jmp show
    empty:
        mov byte ptr [edi], 0x45
        call slot_text
        mov byte ptr [edi], 0
    show:
        call {show_text:#x}
    handled:
        popad
        popfd
        mov al, 1
        ret
    passthrough:
        popfd
        jmp {manager_event_va:#x}
    kind_text:
        mov byte ptr [edi+1], 0x20
        add edi, 2
        mov eax, dword ptr [ebp+{DRAG_KIND:#x}]
        mov ecx, 2
        call {hexfmt:#x}
        mov byte ptr [edi+2], 0x3a
        add edi, 3
        mov eax, ebx
        mov ecx, 4
        call {hexfmt:#x}
        add edi, 4
        ret
    slot_text:
        mov byte ptr [edi+1], 0x20
        add edi, 2
        mov eax, esi
        mov ecx, 3
        call {hexfmt:#x}
        add edi, 3
        ret
    """, CODE_LIMIT)
    addresses = {"iconSlots": icon_slots, "textSlot": text_slot, "clientSlotTable": slot_table,
                 "iconValues": icon_values, "textBuffer": text, "names": names, "hexDigits": hex_digits,
                 "showText": show_text, "hexfmt": hexfmt}
    return payload, blocks, addresses


def build(image, manifest):
    """Returns (patched game.dll bytes, report). Fails closed unless the input is the verified 0.272.0 manager."""
    if sha256(image) != INPUT_GAME_SHA256:
        raise ValueError("Unknown game.dll: expected the verified Companion Manager 0.272.0 build")
    if manifest.get("outputSha256") != INPUT_GAME_SHA256:
        raise ValueError("Input manifest does not describe the verified 0.272.0 game.dll")
    pe = pefile.PE(data=image)
    if pe.OPTIONAL_HEADER.ImageBase != 0x400000 or pe.FILE_HEADER.Machine != 0x14C:
        raise ValueError("Expected the verified Windows x86 client")
    cmgr = pe.sections[-1]
    if cmgr.Name.rstrip(b"\0") != b".cmgr":
        raise ValueError("Manager .cmgr section is not the last section")
    base = pe.OPTIONAL_HEADER.ImageBase
    align = lambda value, unit: (value + unit - 1) // unit * unit
    cmgr_va = base + cmgr.VirtualAddress
    if manifest["blocks"]["init"] != {"address": cmgr_va, "length": MANAGER_INIT_LENGTH}:
        raise ValueError("Input manifest init block differs from the verified 0.272.0 layout")
    init_jump = verify_manager_init(pe, image, cmgr_va)
    verify_hook(pe, image, HOOK_INIT, cmgr_va, 8)
    packet_hook_offset = verify_hook(pe, image, HOOK_PACKET, cmgr_va + MANAGER_PACKET_OFFSET, 8)
    event_hook_offset = verify_hook(pe, image, HOOK_EVENT, cmgr_va + MANAGER_EVENT_OFFSET, 10)
    natives = verify_natives(pe, image)
    ids = probe_ids()

    rva = align(cmgr.VirtualAddress + max(cmgr.Misc_VirtualSize, cmgr.SizeOfRawData), pe.OPTIONAL_HEADER.SectionAlignment)
    va = base + rva
    ks = Ks(KS_ARCH_X86, KS_MODE_32)
    payload, blocks, addresses = assemble(va, cmgr_va + MANAGER_PACKET_OFFSET, cmgr_va + MANAGER_EVENT_OFFSET, ks)

    data = bytearray(image)
    init_jump_offset = pe.get_offset_from_rva(init_jump - base)
    redirects = (
        ("manager init final jump", init_jump, init_jump_offset, RAID_INIT, va + INIT_OFFSET),
        ("packet hook", HOOK_PACKET, packet_hook_offset, cmgr_va + MANAGER_PACKET_OFFSET, va + PACKET_OFFSET),
        ("event hook", HOOK_EVENT, event_hook_offset, cmgr_va + MANAGER_EVENT_OFFSET, va + HANDLER_OFFSET),
    )
    for _name, site, offset, _old, target in redirects:
        data[offset:offset + 5] = b"\xe9" + struct.pack("<i", target - site - 5)

    raw = align(len(data), pe.OPTIONAL_HEADER.FileAlignment)
    size = align(len(payload), pe.OPTIONAL_HEADER.FileAlignment)
    header = pe.sections[0].get_file_offset() + 40 * pe.FILE_HEADER.NumberOfSections
    if header + 40 > min(s.PointerToRawData for s in pe.sections if s.PointerToRawData) or any(data[header:header + 40]):
        raise ValueError("No unused PE section-header slot")
    data.extend(bytes(raw + size - len(data)))
    data[raw:raw + len(payload)] = payload
    data[header:header + 40] = struct.pack("<8sIIIIIIHHI", SECTION_NAME, len(payload), rva, size, raw,
                                           0, 0, 0, 0, SECTION_CHARACTERISTICS)
    struct.pack_into("<H", data, pe.FILE_HEADER.get_field_absolute_offset("NumberOfSections"),
                     pe.FILE_HEADER.NumberOfSections + 1)
    struct.pack_into("<I", data, pe.OPTIONAL_HEADER.get_field_absolute_offset("SizeOfImage"),
                     align(rva + len(payload), pe.OPTIONAL_HEADER.SectionAlignment))
    patched = pefile.PE(data=bytes(data))
    struct.pack_into("<I", data, pe.OPTIONAL_HEADER.get_field_absolute_offset("CheckSum"), patched.generate_checksum())
    report = {
        "probe": True,
        "probeName": "companion-paperdoll-probe",
        "status": ("research probe, not the paperdoll window: offline emulation only; the owner's real-client "
                   "session answers risks R1-R4 of docs/COMPANION_PAPERDOLL_RE.md"),
        "inputSha256": INPUT_GAME_SHA256,
        "outputSha256": sha256(bytes(data)),
        "protocolVersion": manifest["protocolVersion"],
        "layout": manifest["layout"],
        "sectionVa": va,
        "sectionName": SECTION_NAME.rstrip(b"\0").decode("ascii"),
        "blocks": blocks,
        "addresses": addresses,
        "managerBlocks": {name: manifest["blocks"][name] for name in ("init", "packet", "eventHandler", "sendControl")},
        "redirects": [{"name": name, "site": site, "fileOffset": offset, "fromTarget": old, "toTarget": target}
                      for name, site, offset, old, target in redirects],
        "probeEvents": [f"{value:#x}" for value in ids],
        "clickEvents": [str(value) for value in ids],
        "clientSlots": list(CLIENT_SLOTS),
        "slotPositions": list(SLOT_POSITIONS),
        "operations": {"refresh": OP_REFRESH, "show": OP_SHOW, "hide": OP_HIDE},
        "window": {"name": "Custom6", "id": PAPERDOLL_WINDOW},
        "adapters": adapter_names(),
        "iconMax": ICON_MAX, "iconInitial": ICON_INITIAL,
        "statusFormats": {
            "drop": "D <kind %02x>:<source slot %04x>><slot %03x>  (move sent, drag ended)",
            "ignored": "X <kind %02x>:<index %04x>  (drag kind or index not accepted; nothing sent)",
            "pickup": "P <slot %03x> i=<icon %04x>  (kind-0x66 drag started)",
            "putBack": "C <slot %03x>  (item dropped on its own slot: drag ended, nothing sent)",
            "empty": "E <slot %03x>  (no item, nothing sent)",
            "refresh": "R <filled slot count %02x>",
        },
        "nativeReturnBytes": natives,
        "inputUimainSha256": INPUT_UIMAIN_SHA256,
        "inputCustom8Sha256": INPUT_CUSTOM8_SHA256,
        "inputCustom7Sha256": INPUT_CUSTOM7_SHA256,
    }
    return bytes(data), report


def _socket(panel, x, y):
    socket = ET.SubElement(panel, "ImageAreaDef")
    ET.SubElement(socket, "ControlId").text = "1000"
    position = ET.SubElement(socket, "Position")
    ET.SubElement(position, "X").text = str(x)
    ET.SubElement(position, "Y").text = str(y)
    ET.SubElement(socket, "TemplateName").text = "icon_socket_large"


def _icon(panel, x, y, adapter):
    """IconDef as in the stock quiver window, with the large icon template; the adapter holds the icon id."""
    icon = ET.SubElement(panel, "IconDef")
    ET.SubElement(icon, "ControlId").text = "1000"
    position = ET.SubElement(icon, "Position")
    ET.SubElement(position, "X").text = str(x)
    ET.SubElement(position, "Y").text = str(y)
    ET.SubElement(icon, "IconId").text = str(ICON_EMPTY)
    ET.SubElement(icon, "TemplateName").text = "plain_icon_large"
    ET.SubElement(icon, "AdapterName").text = adapter


def slot_rectangles():
    """(x, y, width, height) of each slot's click area, in slot order."""
    return [(x + 1, y + 1, ICON, ICON) for x, y in map(slot_origin, range(SLOTS))]


def probe_window():
    """Custom6 XML: four sockets, icons and click areas, captions, the probe status line and a hint."""
    root = ET.Element("Root_Element", ID="DAOCUi")
    panel = ET.SubElement(root, "WindowTemplate")
    for key, value in (("Name", "custom6_window"), ("WindowId", "Custom6"), ("CloseButton", "true"),
                       ("MoveButton", "true"), ("TopRightResizeButton", "false"),
                       ("BottomRightResizeButton", "false"), ("BottomLeftResizeButton", "false"),
                       ("ResizeButtonOffsetX", "0"), ("ResizeButtonOffsetY", "0"), ("TitleWidth", "0"),
                       ("TitleHeight", "0"), ("Width", str(WINDOW_WIDTH)), ("Height", str(WINDOW_HEIGHT)),
                       ("ResizeableWidth", "0"), ("ResizeableHeight", "0"), ("ResizeableTwoWayWidth", "0"),
                       ("ResizeableTwoWayHeight", "0"), ("MinWidth", "0"), ("MinHeight", "0"),
                       ("ContextTemplateName", None)):
        ET.SubElement(panel, key).text = value
    manager._image(panel, 0, 0, WINDOW_WIDTH, WINDOW_HEIGHT, "dlg_sm_title_noresize")
    origins = [slot_origin(index) for index in range(SLOTS)]
    for x, y in origins:
        _socket(panel, x, y)
    for index, (x, y) in enumerate(origins):
        _icon(panel, x + 1, y + 1, ADAPTER_ICON.format(index))
    manager._label(panel, 12, 1, WINDOW_WIDTH - 40, manager.GOLD, text="COMPANION GEAR", characters=32)
    for index, (x, y) in enumerate(origins[:len(WORN_CAPTIONS)]):
        manager._label(panel, x + SOCKET // 2 - CAPTION_WIDTH // 2, y + SOCKET + 1, CAPTION_WIDTH, manager.TEXT,
                       text=SLOT_CAPTIONS[index], characters=16, centered=True)
    manager._label(panel, BAG_X, BAG_Y - 20, 200, manager.GOLD, text="Backpack", characters=16)
    manager._label(panel, STATUS_X, STATUS_Y, STATUS_WIDTH, manager.MESSAGE, native_adapter=ADAPTER_TEXT,
                   text="", characters=48)
    for line, text in enumerate(HELP_TEXT):
        manager._label(panel, STATUS_X, HELP_Y + 16 * line, STATUS_WIDTH, manager.MUTED, text=text, characters=72)
    for index, (x, y, width, height) in enumerate(slot_rectangles()):
        manager._click(panel, x, y, width, None, SLOT_CAPTIONS[index], height=height, event=str(EVENT_BASE + index))
    ET.indent(root)
    return ET.tostring(root, encoding="iso-8859-1", xml_declaration=True)


INCLUDE_ANCHOR = b"\t<Include>custom7_window.xml</Include>\r\n"
INCLUDE_LINE = b"\t<Include>custom6_window.xml</Include>\r\n"


def probe_uimain(source):
    """The 0.272.0 uimain.xml with one Custom6 include after the Custom7 include (CRLF kept)."""
    if source.count(INCLUDE_ANCHOR) != 1 or b"custom6_window.xml" in source:
        raise ValueError("uimain.xml is not the verified 0.272.0 file")
    return source.replace(INCLUDE_ANCHOR, INCLUDE_ANCHOR + INCLUDE_LINE)


def default_output():
    return DEFAULT_OUTPUT_ROOT / f"companion-paperdoll-probe-{datetime.date.today():%Y%m%d}"


def is_installation_path(path):
    """True for the installed game folder and anything below it. Pure path logic; nothing is touched on disk."""
    resolved = Path(path).resolve()
    return resolved == INSTALLATION or resolved.is_relative_to(INSTALLATION)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--input-stage", type=Path, required=True,
                        help="Verified 0.272.0 manager stage (read only)")
    parser.add_argument("--output", type=Path, default=None,
                        help="New probe stage directory (default: companion-paperdoll-probe-<date> under "
                             f"{DEFAULT_OUTPUT_ROOT}); never inside {INSTALLATION}")
    args = parser.parse_args()
    output = args.output or default_output()
    if is_installation_path(output):
        parser.error("Refusing to write inside the installation")
    if output.exists():
        parser.error("Output directory already exists")
    manifest = json.loads((args.input_stage / "manifest.json").read_text(encoding="utf-8"))
    image = (args.input_stage / "game.dll").read_bytes()
    main_xml = (args.input_stage / "uimain.xml").read_bytes()
    if sha256(main_xml) != INPUT_UIMAIN_SHA256:
        parser.error("Input uimain.xml does not match the verified 0.272.0 hash")
    for skin in ("atlantis", "isles"):
        if sha256((args.input_stage / skin / "custom8_window.xml").read_bytes()) != INPUT_CUSTOM8_SHA256 or \
                sha256((args.input_stage / skin / "custom7_window.xml").read_bytes()) != INPUT_CUSTOM7_SHA256:
            parser.error(f"Input {skin} window XML does not match the verified 0.272.0 hashes")
    try:
        patched, report = build(image, manifest)
        uimain = probe_uimain(main_xml)
    except ValueError as error:
        parser.error(str(error))
    window = probe_window()
    output.mkdir(parents=True)
    (output / "game.dll").write_bytes(patched)
    (output / "uimain.xml").write_bytes(uimain)
    for skin in ("atlantis", "isles"):
        (output / skin).mkdir()
        (output / skin / "custom6_window.xml").write_bytes(window)
    report["uimainOutputSha256"] = sha256(uimain)
    report["windowSha256"] = sha256(window)
    report["slotRectangles"] = slot_rectangles()
    (output / "manifest.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: report[key] for key in ("inputSha256", "outputSha256", "uimainOutputSha256",
                                                   "windowSha256", "probe")}, indent=2))


if __name__ == "__main__":
    main()
