"""Research probe for four Companion Manager client levers. This is NOT the Companion Manager.

Stages a copy of the 0.169.0 manager client with one extra PE section, `.cprb`, and a small probe
strip at the top of the Custom8 window. One real-client session answers four questions:

1. Window size: the probe reads the Custom8 owner's width and height (X+0x2C, X+0x30) and prints them.
2. Scalar (status bar) adapter: a StatusBarDef bound to `cprb_bar` moves when the probe calls the
   client's own scalar setter with 0, 25, 50, 75, 100.
3. Colour adapter: a label bound to `cprb_color` as its ColorAdapter. The probe writes RGBA floats
   straight into the colour object (red, green, blue, white). Whether the label repaints while open
   is the question.
4. Shift-click: the probe strip has an InvisibleButtonDef with OnClickEvent 2304 (0x900) and
   OnShiftClickEvent 2048 (0x800). The probe text says which one fired.

Only two jumps in the manager change: the final `jmp 0x247E000` of the manager init block now goes to
the probe init, and the event hook at 0x4E04DE goes to the probe handler. The probe handler takes
0x800-0x80F and 0x900-0x90F and jumps to the manager eventHandler (.cmgr+0x1C00) with EAX and the flags
unchanged for every other ID. The manager's init, packet, eventHandler and sendControl bytes are
unchanged. The probe does not change the shipped manager builder or CompanionManagerProtocol.cs.

Offline emulation (test_companion_manager_levers_probe.py) proves the hook logic only. It is not
acceptance: the owner's real-client session answers the four levers.

Output: a new directory with game.dll, uimain.xml (a byte-identical copy), atlantis and isles
custom8_window.xml, and manifest.json with "probe": true. The output is never written inside the
installation, and the builder is deterministic (no timestamps).
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


# Verified inputs (the 0.169.0 stage). The builder fails closed on any other file.
INPUT_GAME_SHA256 = "49095c51e792200daf824f8a00880c70815a0cacbc37f452cf062cffacb3b63d"
INPUT_WINDOW_SHA256 = "9cd3b8a5fb3d2a4671192b9d52287173e2fe194a4f19979de1c339f408235efd"
INPUT_UIMAIN_SHA256 = "e1266fcceb954b5e835d3e3ca6563d682f4bb0f6430d36947923a74974dfd2a0"

INSTALLATION = Path("/mnt/d/Games/OfflineDAoC")
DEFAULT_OUTPUT_ROOT = Path("/mnt/d/Games/OfflineDAoC-dev")

RAID_INIT = 0x247E000
# Verified manager layout (the .cmgr section is the last section of the 0.169.0 game.dll).
MANAGER_INIT_LENGTH = 81
MANAGER_PACKET_OFFSET = 0x1000
MANAGER_EVENT_OFFSET = 0x1C00
MANAGER_SEND_OFFSET = 0x2600
HOOK_INIT = 0x4DA938     # stock -> manager init (target .cmgr+0)
HOOK_PACKET = 0x411201   # stock -> manager packet (target .cmgr+0x1000)
HOOK_EVENT = 0x4E04DE    # stock event dispatch -> manager eventHandler (target .cmgr+0x1C00)

# Probe event IDs. The stock UI names its clicks; the numeric IDs in the installed UI are the raid
# (0x600-0x64F) and manager (0x700-0x7DF) ranges. 0x7E0 and 0x7E1 are the client's own helpers.
SHIFT_BASE = 0x800           # OnShiftClickEvent 2048
PLAIN_BASE = 0x900           # OnClickEvent 2304
IDS_PER_RANGE = 16
RAID_IDS = (0x600, 0x650)
MANAGER_IDS = (0x700, 0x7E0)
HELPER_IDS = (0x7E0, 0x7E1)

ADAPTER_TEXT = "cprb_text"
ADAPTER_BAR = "cprb_bar"
ADAPTER_COLOR = "cprb_color"
BAR_MAX = 100.0
BAR_STEPS = (0.0, 25.0, 50.0, 75.0, 100.0)   # value = (counter * 25) mod 125, i.e. 25 * (counter mod 5)
# Red, green, blue, white as RGBA floats; index = counter mod 4 (the first click shows green).
COLOR_STEPS = ((1.0, 0.0, 0.0, 1.0), (0.0, 1.0, 0.0, 1.0), (0.0, 0.0, 1.0, 1.0), (1.0, 1.0, 1.0, 1.0))

# Window geometry. Native: [manager] + 0x37178 = Custom8 window stub W; [W + 0x1C] = owner X;
# [X + 0x2C] = width px, [X + 0x30] = height px (stored by the resize code at 0x53DE73/0x53DE76).
WINDOW_MANAGER = 0x104C2BC
CUSTOM8_WINDOW_OFFSET = 0x37178
OWNER_OFFSET = 0x1C
WIDTH_OFFSET = 0x2C
HEIGHT_OFFSET = 0x30

# Native entry points. Each return size is verified against the binary by verify_natives().
REGISTER_TEXT_ADAPTER = manager.REGISTER_TEXT_ADAPTER  # 0x4B6C84 stdcall(registry, slot*, name)
REGISTER_SCALAR_ADAPTER = 0x4B6BC0                      # stdcall(registry, slot*, name, maxBits, initialBits)
REGISTER_COLOR_ADAPTER = 0x4B6F87                       # stdcall(registry, slot*, name)
SET_SCALAR_VALUE = 0x524370                             # ECX = scalar object, stdcall(float), ret 4
SET_ADAPTER_TEXT = manager.SET_ADAPTER_TEXT             # 0x524436, EBX = adapter, stdcall(text)
NATIVE_RETURN_BYTES = {
    REGISTER_TEXT_ADAPTER: 12,
    REGISTER_SCALAR_ADAPTER: 20,
    REGISTER_COLOR_ADAPTER: 12,
    SET_SCALAR_VALUE: 4,
    SET_ADAPTER_TEXT: 4,
}

# Offsets inside the .cprb section.
INIT_OFFSET = 0x0000
HANDLER_OFFSET = 0x0100
HANDLER_LIMIT = 0x1000
DATA_OFFSET = 0x1000
HEX_DIGITS = DATA_OFFSET + 0x00
BAR_TABLE = DATA_OFFSET + 0x10
COLOR_TABLE = DATA_OFFSET + 0x40
COUNTER = DATA_OFFSET + 0x80
MODE = DATA_OFFSET + 0x84
TEXT_SLOT = DATA_OFFSET + 0x88
BAR_SLOT = DATA_OFFSET + 0x8C
COLOR_SLOT = DATA_OFFSET + 0x90
TEXT_BUFFER = DATA_OFFSET + 0xA0
NAME_TEXT = DATA_OFFSET + 0x100
NAME_BAR = DATA_OFFSET + 0x110
NAME_COLOR = DATA_OFFSET + 0x120
PAYLOAD_SIZE = DATA_OFFSET + 0x130
SECTION_NAME = b".cprb\0\0\0"
SECTION_CHARACTERISTICS = 0xE0000060

# Probe strip in the Custom8 window, top band, right of the title and left of the Refresh/Clear/Search links.
STRIP_Y = 6
STRIP_HEIGHT = 18
CAPTION_X, CAPTION_WIDTH = 330, 60
TEXT_X, TEXT_WIDTH = 396, 300
BAR_X, BAR_WIDTH = 704, 120


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def float_bits(value):
    return struct.unpack("<I", struct.pack("<f", value))[0]


def probe_ids():
    """(shift IDs, plain IDs). Refuses any ID that overlaps the raid, manager, or helper ranges."""
    shift = tuple(SHIFT_BASE + index for index in range(IDS_PER_RANGE))
    plain = tuple(PLAIN_BASE + index for index in range(IDS_PER_RANGE))
    for probe in shift + plain:
        if RAID_IDS[0] <= probe < RAID_IDS[1] or MANAGER_IDS[0] <= probe < MANAGER_IDS[1] or probe in HELPER_IDS:
            raise ValueError(f"Probe ID {probe:#x} overlaps the raid, manager, or helper range")
    return shift, plain


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


def verify_manager_layout(pe, image, cmgr_va):
    """The manager init block must decode to exactly 81 bytes ending in `jmp 0x247E000`.

    Returns the address of that final jump, the one site the probe redirects.
    """
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
        raise ValueError("Manager init block length differs from the verified 81 bytes")
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


def assemble(va, manager_event_va, ks):
    """Returns (payload, blocks, addresses) for the probe init, the probe handler, and its data."""
    payload = bytearray(PAYLOAD_SIZE)
    payload[HEX_DIGITS:HEX_DIGITS + 16] = b"0123456789abcdef"
    for index, value in enumerate(BAR_STEPS):
        payload[BAR_TABLE + 4 * index:BAR_TABLE + 4 * index + 4] = struct.pack("<I", float_bits(value))
    for index, rgba in enumerate(COLOR_STEPS):
        payload[COLOR_TABLE + 16 * index:COLOR_TABLE + 16 * index + 16] = struct.pack("<4f", *rgba)
    for offset, name in ((NAME_TEXT, ADAPTER_TEXT), (NAME_BAR, ADAPTER_BAR), (NAME_COLOR, ADAPTER_COLOR)):
        encoded = name.encode("ascii") + b"\0"
        payload[offset:offset + len(encoded)] = encoded
    blocks = {}

    def put(label, offset, assembly, limit):
        code, _ = ks.asm(assembly, addr=va + offset)
        if offset + len(code) > limit:
            raise ValueError(f"{label} exceeds reserved code space")
        payload[offset:offset + len(code)] = bytes(code)
        blocks[label] = {"address": va + offset, "length": len(code)}

    slot_text, slot_bar, slot_color = va + TEXT_SLOT, va + BAR_SLOT, va + COLOR_SLOT
    counter, mode, bar_table, color_table = va + COUNTER, va + MODE, va + BAR_TABLE, va + COLOR_TABLE
    put("init", INIT_OFFSET, f"""
        pushfd
        pushad
        push {va + NAME_TEXT:#x}
        push {slot_text:#x}
        push edi
        call {REGISTER_TEXT_ADAPTER:#x}
        push 0
        push {float_bits(BAR_MAX):#x}
        push {va + NAME_BAR:#x}
        push {slot_bar:#x}
        push edi
        call {REGISTER_SCALAR_ADAPTER:#x}
        push {va + NAME_COLOR:#x}
        push {slot_color:#x}
        push edi
        call {REGISTER_COLOR_ADAPTER:#x}
        popad
        popfd
        jmp {RAID_INIT:#x}
    """, HANDLER_OFFSET)

    # EAX is the event record, [EAX+8] its ID. pushfd comes first, so a pass-through leaves EAX and
    # the flags exactly as the hook received them; popfd runs before the jump to the manager.
    put("eventHandler", HANDLER_OFFSET, f"""
        pushfd
        cmp dword ptr [eax+8], {PLAIN_BASE:#x}
        jb below_plain
        cmp dword ptr [eax+8], {PLAIN_BASE + IDS_PER_RANGE - 1:#x}
        ja passthrough
        mov byte ptr [{mode:#x}], 0x50
        jmp handle
    below_plain:
        cmp dword ptr [eax+8], {SHIFT_BASE:#x}
        jb passthrough
        cmp dword ptr [eax+8], {SHIFT_BASE + IDS_PER_RANGE - 1:#x}
        ja passthrough
        mov byte ptr [{mode:#x}], 0x53
    handle:
        pushad
        inc dword ptr [{counter:#x}]
        mov eax, dword ptr [{counter:#x}]
        xor edx, edx
        mov ecx, 5
        div ecx
        mov eax, dword ptr [{bar_table:#x}+edx*4]
        mov ecx, dword ptr [{slot_bar:#x}]
        test ecx, ecx
        jz bar_done
        push eax
        call {SET_SCALAR_VALUE:#x}
    bar_done:
        mov eax, dword ptr [{counter:#x}]
        and eax, 3
        shl eax, 4
        lea esi, [{color_table:#x}+eax]
        mov edi, dword ptr [{slot_color:#x}]
        test edi, edi
        jz color_done
        mov eax, dword ptr [esi]
        mov dword ptr [edi], eax
        mov eax, dword ptr [esi+4]
        mov dword ptr [edi+4], eax
        mov eax, dword ptr [esi+8]
        mov dword ptr [edi+8], eax
        mov eax, dword ptr [esi+12]
        mov dword ptr [edi+12], eax
    color_done:
        mov edi, {va + TEXT_BUFFER:#x}
        mov al, byte ptr [{mode:#x}]
        mov byte ptr [edi], al
        mov byte ptr [edi+1], 0x23
        add edi, 2
        mov eax, dword ptr [{counter:#x}]
        mov ecx, 2
        call hexfmt
        add edi, 2
        mov byte ptr [edi], 0x20
        mov byte ptr [edi+1], 0x57
        mov byte ptr [edi+2], 0x3d
        add edi, 3
        xor esi, esi
        mov eax, dword ptr [{WINDOW_MANAGER:#x}]
        test eax, eax
        jz owner_known
        mov eax, dword ptr [eax+{CUSTOM8_WINDOW_OFFSET:#x}]
        test eax, eax
        jz owner_known
        mov esi, dword ptr [eax+{OWNER_OFFSET:#x}]
    owner_known:
        test esi, esi
        jz width_unknown
        mov eax, dword ptr [esi+{WIDTH_OFFSET:#x}]
        mov ecx, 4
        call hexfmt
        add edi, 4
        jmp width_done
    width_unknown:
        mov dword ptr [edi], 0x2d2d2d2d
        add edi, 4
    width_done:
        mov byte ptr [edi], 0x20
        mov byte ptr [edi+1], 0x48
        mov byte ptr [edi+2], 0x3d
        add edi, 3
        test esi, esi
        jz height_unknown
        mov eax, dword ptr [esi+{HEIGHT_OFFSET:#x}]
        mov ecx, 4
        call hexfmt
        add edi, 4
        jmp height_done
    height_unknown:
        mov dword ptr [edi], 0x2d2d2d2d
        add edi, 4
    height_done:
        mov byte ptr [edi], 0
        mov ebx, dword ptr [{slot_text:#x}]
        test ebx, ebx
        jz text_done
        push {va + TEXT_BUFFER:#x}
        call {SET_ADAPTER_TEXT:#x}
    text_done:
        popad
        popfd
        mov al, 1
        ret
    passthrough:
        popfd
        jmp {manager_event_va:#x}
    hexfmt:
        push edi
        add edi, ecx
    hex_loop:
        dec edi
        mov edx, eax
        and edx, 15
        mov dl, byte ptr [{va + HEX_DIGITS:#x}+edx]
        mov byte ptr [edi], dl
        shr eax, 4
        dec ecx
        jnz hex_loop
        pop edi
        ret
    """, HANDLER_LIMIT)
    addresses = {"slotText": slot_text, "slotBar": slot_bar, "slotColor": slot_color,
                 "counter": va + COUNTER, "mode": va + MODE, "textBuffer": va + TEXT_BUFFER,
                 "nameText": va + NAME_TEXT, "nameBar": va + NAME_BAR, "nameColor": va + NAME_COLOR,
                 "colorTable": va + COLOR_TABLE, "barTable": va + BAR_TABLE, "hexDigits": va + HEX_DIGITS}
    return payload, blocks, addresses


def build(image, manifest):
    """Returns (patched game.dll bytes, report). Fails closed unless the input is the verified 0.169.0 manager."""
    if sha256(image) != INPUT_GAME_SHA256:
        raise ValueError("Unknown game.dll: expected the verified Companion Manager 0.169.0 build")
    if manifest.get("outputSha256") != INPUT_GAME_SHA256:
        raise ValueError("Input manifest does not describe the verified 0.169.0 game.dll")
    pe = pefile.PE(data=image)
    if pe.OPTIONAL_HEADER.ImageBase != 0x400000 or pe.FILE_HEADER.Machine != 0x14C:
        raise ValueError("Expected the verified Windows x86 client")
    cmgr = pe.sections[-1]
    if cmgr.Name.rstrip(b"\0") != b".cmgr":
        raise ValueError("Manager .cmgr section is not the last section")
    base = pe.OPTIONAL_HEADER.ImageBase
    align = lambda value, unit: (value + unit - 1) // unit * unit
    cmgr_va = base + cmgr.VirtualAddress
    if manifest["blocks"]["init"]["address"] != cmgr_va:
        raise ValueError("Input manifest init address differs from the .cmgr section")
    init_jump = verify_manager_layout(pe, image, cmgr_va)
    verify_hook(pe, image, HOOK_INIT, cmgr_va, 8)
    verify_hook(pe, image, HOOK_PACKET, cmgr_va + MANAGER_PACKET_OFFSET, 8)
    event_hook_offset = verify_hook(pe, image, HOOK_EVENT, cmgr_va + MANAGER_EVENT_OFFSET, 10)
    natives = verify_natives(pe, image)

    rva = align(cmgr.VirtualAddress + max(cmgr.Misc_VirtualSize, cmgr.SizeOfRawData), pe.OPTIONAL_HEADER.SectionAlignment)
    va = base + rva
    ks = Ks(KS_ARCH_X86, KS_MODE_32)
    payload, blocks, addresses = assemble(va, cmgr_va + MANAGER_EVENT_OFFSET, ks)
    shift_ids, plain_ids = probe_ids()

    data = bytearray(image)
    init_jump_offset = pe.get_offset_from_rva(init_jump - base)
    data[init_jump_offset:init_jump_offset + 5] = b"\xe9" + struct.pack("<i", va + INIT_OFFSET - init_jump - 5)
    data[event_hook_offset:event_hook_offset + 5] = b"\xe9" + struct.pack("<i", va + HANDLER_OFFSET - HOOK_EVENT - 5)

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
        "probeName": "companion-manager-levers-probe",
        "status": ("research probe, not the Companion Manager: offline emulation only; the owner's real-client "
                   "session answers the four levers"),
        "inputSha256": INPUT_GAME_SHA256,
        "outputSha256": sha256(bytes(data)),
        "protocolVersion": manifest["protocolVersion"],
        "layout": manifest["layout"],
        "sectionVa": va,
        "sectionName": SECTION_NAME.rstrip(b"\0").decode("ascii"),
        "blocks": blocks,
        "addresses": addresses,
        "managerBlocks": {
            "init": {"address": cmgr_va, "length": MANAGER_INIT_LENGTH},
            "packet": {"address": cmgr_va + MANAGER_PACKET_OFFSET, "length": manifest["blocks"]["packet"]["length"]},
            "eventHandler": {"address": cmgr_va + MANAGER_EVENT_OFFSET,
                             "length": manifest["blocks"]["eventHandler"]["length"]},
            "sendControl": {"address": cmgr_va + MANAGER_SEND_OFFSET,
                            "length": manifest["blocks"]["sendControl"]["length"]},
        },
        "redirects": [
            {"name": "manager init final jump", "site": init_jump, "fileOffset": init_jump_offset,
             "fromTarget": RAID_INIT, "toTarget": va + INIT_OFFSET},
            {"name": "event hook", "site": HOOK_EVENT, "fileOffset": event_hook_offset,
             "fromTarget": cmgr_va + MANAGER_EVENT_OFFSET, "toTarget": va + HANDLER_OFFSET},
        ],
        "probeIds": {"shift": [f"{value:#x}" for value in shift_ids],
                     "plain": [f"{value:#x}" for value in plain_ids],
                     "clickEventPlain": PLAIN_BASE, "clickEventShift": SHIFT_BASE,
                     "passThrough": "every other ID is jumped to the manager eventHandler unchanged"},
        "adapters": {"text": ADAPTER_TEXT, "bar": ADAPTER_BAR, "color": ADAPTER_COLOR},
        "barSteps": list(BAR_STEPS),
        "barMax": BAR_MAX,
        "colorSteps": [list(rgba) for rgba in COLOR_STEPS],
        "textFormat": "<mode>#<counter %02x> W=<width %04x or ---->  H=<height %04x or ---->",
        "windowManager": WINDOW_MANAGER,
        "customWindowOffset": CUSTOM8_WINDOW_OFFSET,
        "ownerOffset": OWNER_OFFSET,
        "widthOffset": WIDTH_OFFSET,
        "heightOffset": HEIGHT_OFFSET,
        "nativeReturnBytes": natives,
        "inputUimainSha256": INPUT_UIMAIN_SHA256,
        "inputWindowSha256": INPUT_WINDOW_SHA256,
    }
    return bytes(data), report


def _sub(parent, tag, text=None):
    element = ET.SubElement(parent, tag)
    if text is not None:
        element.text = text
    return element


def _position(parent, x, y):
    position = ET.SubElement(parent, "Position")
    _sub(position, "X", str(x))
    _sub(position, "Y", str(y))
    ET.SubElement(parent, "Alignment")


def _color(parent, color):
    rgba = ET.SubElement(parent, "Color")
    for channel, value in zip("RGBA", (*color, 255)):
        _sub(rgba, channel, str(value))


def probe_window(source):
    """Appends the probe strip to the Custom8 XML built for the 0.169.0 manager.

    The input must round-trip byte-identically through ElementTree, so the manager's own elements
    are reproduced exactly. The probe elements go at the end of the WindowTemplate: the manager's
    z-order and click areas do not change.
    """
    root = ET.fromstring(source)
    ET.indent(root)
    if ET.tostring(root, encoding="iso-8859-1", xml_declaration=True) != source:
        raise ValueError("Custom8 XML is not in the canonical form this probe expects")
    root = ET.fromstring(source)
    window = root.find("WindowTemplate")
    if window is None or window.find("WindowId").text != "Custom8":
        raise ValueError("Unexpected Custom8 window")

    caption = ET.SubElement(window, "LabelDef")
    _sub(caption, "ControlId", "1000")
    _position(caption, CAPTION_X, STRIP_Y)
    _color(caption, manager.GOLD)
    _sub(caption, "FontName", manager.FONT)
    _sub(caption, "Width", str(CAPTION_WIDTH))
    _sub(caption, "Height", str(STRIP_HEIGHT))
    ET.SubElement(caption, "ColorAdapter")
    _sub(caption, "MaxCharacters", "16")
    _sub(caption, "Data", "[Probe]")
    _sub(caption, "EndAligned", "false")
    _sub(caption, "TextCentered", "false")

    text = ET.SubElement(window, "LabelDef")
    _sub(text, "ControlId", "1000")
    _position(text, TEXT_X, STRIP_Y)
    _color(text, manager.TEXT)
    _sub(text, "FontName", manager.FONT)
    _sub(text, "Width", str(TEXT_WIDTH))
    _sub(text, "Height", str(STRIP_HEIGHT))
    _sub(text, "ColorAdapter", ADAPTER_COLOR)
    _sub(text, "MaxCharacters", "32")
    _sub(text, "Data", "probe: no click yet")
    _sub(text, "EndAligned", "false")
    _sub(text, "TextCentered", "false")
    _sub(text, "Adapter", ADAPTER_TEXT)

    bar = ET.SubElement(window, "StatusBarDef")
    _sub(bar, "ControlId")
    position = ET.SubElement(bar, "Position")
    _sub(position, "X", str(BAR_X))
    _sub(position, "Y", str(STRIP_Y))
    alignment = ET.SubElement(bar, "Alignment")
    _sub(alignment, "TopLeft", "true")
    _sub(bar, "TemplateName", "green_status")
    _sub(bar, "AdapterName", ADAPTER_BAR)
    _sub(bar, "Width", str(BAR_WIDTH))

    button = ET.SubElement(window, "InvisibleButtonDef")
    _sub(button, "ControlId")
    _position(button, CAPTION_X, STRIP_Y)
    _sub(button, "Label", "Probe click")
    _sub(button, "OnClickEvent", str(PLAIN_BASE))
    _sub(button, "OnShiftClickEvent", str(SHIFT_BASE))
    _sub(button, "Width", str(CAPTION_WIDTH))
    _sub(button, "Height", str(STRIP_HEIGHT))
    ET.indent(root)
    return ET.tostring(root, encoding="iso-8859-1", xml_declaration=True)


def probe_strip_rectangles():
    """Rectangles (x, y, width, height) of the probe elements, for the layout checks."""
    return {"caption": [CAPTION_X, STRIP_Y, CAPTION_WIDTH, STRIP_HEIGHT],
            "text": [TEXT_X, STRIP_Y, TEXT_WIDTH, STRIP_HEIGHT],
            "bar": [BAR_X, STRIP_Y, BAR_WIDTH, STRIP_HEIGHT]}


def default_output():
    return DEFAULT_OUTPUT_ROOT / f"companion-manager-levers-probe-{datetime.date.today():%Y%m%d}"


def is_installation_path(path):
    """True for the installed game folder and anything below it. Pure path logic; nothing is touched on disk."""
    resolved = Path(path).resolve()
    return resolved == INSTALLATION or resolved.is_relative_to(INSTALLATION)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--input-stage", type=Path, required=True,
                        help="Verified 0.169.0 manager stage (read only)")
    parser.add_argument("--output", type=Path, default=None,
                        help="New probe stage directory (default: companion-manager-levers-probe-<date> under "
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
    atlantis = (args.input_stage / "atlantis" / "custom8_window.xml").read_bytes()
    isles = (args.input_stage / "isles" / "custom8_window.xml").read_bytes()
    if sha256(main_xml) != INPUT_UIMAIN_SHA256:
        parser.error("Input uimain.xml does not match the verified 0.169.0 hash")
    if sha256(atlantis) != INPUT_WINDOW_SHA256 or sha256(isles) != INPUT_WINDOW_SHA256:
        parser.error("Input Custom8 XML does not match the verified 0.169.0 hash")
    try:
        patched, report = build(image, manifest)
    except ValueError as error:
        parser.error(str(error))
    window = probe_window(atlantis)
    output.mkdir(parents=True)
    (output / "game.dll").write_bytes(patched)
    (output / "uimain.xml").write_bytes(main_xml)
    for skin in ("atlantis", "isles"):
        (output / skin).mkdir()
        (output / skin / "custom8_window.xml").write_bytes(window)
    report["uimainOutputSha256"] = sha256(main_xml)
    report["uimainBaselineSha256"] = sha256(main_xml)
    report["windowSha256"] = sha256(window)
    report["probeStripRectangles"] = probe_strip_rectangles()
    (output / "manifest.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: report[key] for key in ("inputSha256", "outputSha256", "windowSha256", "probe")}, indent=2))


if __name__ == "__main__":
    main()
