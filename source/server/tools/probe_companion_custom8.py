"""Stage a Custom8 client-control probe from the verified native-raid client.

This is deliberately a probe, not an installer or the Companion Manager. It
checks the exact raid-patched input and leaves the installed client untouched.
The experimental dedicated action and edit-box path failed in the real client;
this builder remains an offline research artifact until that path is repaired.
"""

import argparse
import copy
import hashlib
import json
from pathlib import Path
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
    title = va + 0x2200
    selected = va + 0x2204
    search_adapter = va + 0x2208
    name = va + 0x2300
    event = va + 0x2320
    clicked = va + 0x2340
    action = va + 0x2360
    search_name = va + 0x2368
    search_message = va + 0x2380
    search_event = va + 0x23B0
    payload = bytearray(0x2400)
    payload[0x2300:0x2300 + len(b"comp_probe_title\0")] = b"comp_probe_title\0"
    payload[0x2320:0x2320 + len(b"CompanionProbeClick\0")] = b"CompanionProbeClick\0"
    payload[0x2340:0x2340 + len(b"Client click fired\0")] = b"Client click fired\0"
    payload[0x2360:0x2368] = bytes((0x43, 0x4d, 1, 1, 0, 0, 0, 0))
    payload[0x2368:0x2368 + len(b"comp_probe_search\0")] = b"comp_probe_search\0"
    payload[0x2380:0x2384] = bytes((0x43, 0x4d, 1, 2))
    payload[0x23B0:0x23B0 + len(b"CompanionProbeSearch\0")] = b"CompanionProbeSearch\0"
    ks = Ks(KS_ARCH_X86, KS_MODE_32)
    blocks = {}

    def put(label, offset, assembly, limit):
        code, _ = ks.asm(assembly, addr=va + offset)
        if offset + len(code) > limit:
            raise ValueError(f"{label} exceeds reserved code space")
        payload[offset:offset + len(code)] = bytes(code)
        blocks[label] = {"address": va + offset, "length": len(code)}

    put("init", 0, f"""
        pushfd; pushad
        push {name}; push {title}; push edi; call 0x4b6c84
        push {search_name}; push {search_adapter}; push edi; call 0x4b710d
        mov dword ptr [{selected}], 0
        popad; popfd
        jmp {RAID_INIT}
    """, 0x1000)

    # DebugMode marker 0x43 is distinct from raid marker 0x52. The fixed body
    # has a terminator at byte 43 and an optional local object ID at bytes 8-9.
    put("packet", 0x1000, f"""
        cmp dword ptr [ebp+0x18], 2
        jb raid
        cmp byte ptr [ebx+1], 0x43
        jne raid
        cmp dword ptr [ebp+0x18], 128
        jb ignored
        cmp byte ptr [ebx+2], 1
        jne ignored
        pushfd; pushad
        mov esi, ebx
        movzx eax, byte ptr [esi+3]
        cmp eax, 1
        je update
        cmp eax, 2
        je show
        cmp eax, 3
        je hide
        jmp done
    update:
        cmp byte ptr [esi+43], 0
        jne done
        mov ebx, dword ptr [{title}]
        test ebx, ebx
        jz done
        lea eax, [esi+12]
        push eax
        call 0x524436
        movzx eax, word ptr [esi+8]
        mov dword ptr [{selected}], eax
        jmp done
    show:
        mov ecx, dword ptr [0x104c2bc]
        test ecx, ecx
        jz done
        push 1
        push 0x75
        mov eax, dword ptr [ecx]
        call dword ptr [eax+8]
        jmp done
    hide:
        mov ecx, dword ptr [0x104c2bc]
        test ecx, ecx
        jz done
        push 0
        push 0x75
        mov eax, dword ptr [ecx]
        call dword ptr [eax+8]
        mov dword ptr [{selected}], 0
    done:
        popad; popfd
    ignored:
        jmp 0x4113ad
    raid:
        jmp {RAID_PACKET}
    """, 0x1800)

    put("eventName", 0x1800, f"""
        push {event}
        push esi
        call 0x77156e
        add esp, 8
        test eax, eax
        jnz check_search
        mov eax, 0x700
        ret
    check_search:
        push {search_event}
        push esi
        call 0x77156e
        add esp, 8
        test eax, eax
        jnz raid
        mov eax, 0x701
        ret
    raid:
        jmp {RAID_EVENT_NAME}
    """, 0x1C00)
    put("eventHandler", 0x1C00, f"""
        cmp dword ptr [eax+8], 0x700
        je click
        cmp dword ptr [eax+8], 0x701
        je search
        jmp raid
    click:
        pushfd; pushad
        mov ebx, dword ptr [{title}]
        test ebx, ebx
        jz select
        push {clicked}
        call 0x524436
    select:
        push 0xffff
        push 8
        push 0x5e
        push {action}
        call 0x4281df
        add esp, 16
        popad; popfd
        mov al, 1
        ret
    search:
        pushfd; pushad
        mov esi, dword ptr [{search_adapter}]
        test esi, esi
        jz search_done
        mov ebx, dword ptr [esi+0x1c]
        cmp ebx, 31
        jbe length_ok
        mov ebx, 31
    length_ok:
        lea edx, [esi+8]
        cmp dword ptr [esi+0x20], 16
        jb inline_text
        mov edx, dword ptr [esi+8]
    inline_text:
        test edx, edx
        jz search_done
        mov edi, {search_message+4}
        xor eax, eax
        mov ecx, 8
        rep stosd
        mov edi, {search_message+4}
        mov esi, edx
        mov ecx, ebx
        rep movsb
        mov byte ptr [edi], 0
        push 0xffff
        push 36
        push 0x5e
        push {search_message}
        call 0x4281df
        add esp, 16
    search_done:
        popad; popfd
        mov al, 1
        ret
    raid:
        jmp {RAID_EVENT_HANDLER}
    """, 0x2200)

    data = bytearray(image)
    for address, expected, target_offset in HOOKS:
        offset = pe.get_offset_from_rva(address - pe.OPTIONAL_HEADER.ImageBase)
        if data[offset:offset + len(expected)] != expected:
            raise ValueError(f"Native raid hook at {address:#x} differs")
        target = va + target_offset
        data[offset:offset + len(expected)] = (b"\xe9" + struct.pack("<i", target - address - 5)
                                               + b"\x90" * (len(expected) - 5))
    raw = align(len(data), pe.OPTIONAL_HEADER.FileAlignment)
    size = align(len(payload), pe.OPTIONAL_HEADER.FileAlignment)
    header = pe.sections[0].get_file_offset() + 40 * pe.FILE_HEADER.NumberOfSections
    if header + 40 > min(s.PointerToRawData for s in pe.sections if s.PointerToRawData) or any(data[header:header + 40]):
        raise ValueError("No unused PE section-header slot")
    data.extend(bytes(raw + size - len(data)))
    data[raw:raw + len(payload)] = payload
    data[header:header + 40] = struct.pack("<8sIIIIIIHHI", b".cmp8\0\0\0", len(payload),
                                           rva, size, raw, 0, 0, 0, 0, 0xE0000060)
    struct.pack_into("<H", data, pe.FILE_HEADER.get_field_absolute_offset("NumberOfSections"),
                     pe.FILE_HEADER.NumberOfSections + 1)
    struct.pack_into("<I", data, pe.OPTIONAL_HEADER.get_field_absolute_offset("SizeOfImage"),
                     align(rva + len(payload), pe.OPTIONAL_HEADER.SectionAlignment))
    patched = pefile.PE(data=data)
    struct.pack_into("<I", data, pe.OPTIONAL_HEADER.get_field_absolute_offset("CheckSum"),
                     patched.generate_checksum())
    return bytes(data), {"baselineSha256": RAID_SHA256, "outputSha256": sha256(data),
                         "sectionVa": va, "blocks": blocks, "titleAdapter": title,
                         "selectedObjectId": selected, "searchAdapter": search_adapter}


def window(source):
    original = ET.parse(source).getroot().find("WindowTemplate")
    root = ET.Element("Root_Element", ID="DAOCUi")
    edit_template = copy.deepcopy(ET.parse(source.parent / "bazaar_query.xml").getroot().find("EditBoxTemplate"))
    edit_template.find("Name").text = "comp_search_editbox"
    root.append(edit_template)
    panel = ET.SubElement(root, "WindowTemplate")
    for child in original:
        if len(child) == 0:
            panel.append(copy.deepcopy(child))
    for key, value in {"Name": "custom8_window", "WindowId": "Custom8", "Width": "360",
                       "Height": "164", "CloseButton": "true", "MoveButton": "true"}.items():
        child = panel.find(key)
        if child is None:
            child = ET.SubElement(panel, key)
        child.text = value
    background = copy.deepcopy(original.find("FullResizeImageDef"))
    background.find("Width").text = "360"
    background.find("Height").text = "164"
    panel.append(background)
    label = copy.deepcopy(original.find("LabelDef"))
    label.find("Position/X").text = "14"
    label.find("Position/Y").text = "34"
    label.find("Width").text = "330"
    label.find("Data").text = "Waiting for server data"
    label.find("MaxCharacters").text = "32"
    color_adapter = label.find("ColorAdapter")
    if color_adapter is not None:
        label.remove(color_adapter)
    adapter = label.find("Adapter")
    if adapter is None:
        adapter = ET.SubElement(label, "Adapter")
    adapter.text = "comp_probe_title"
    panel.append(label)
    button = ET.Element("ButtonDef")
    ET.SubElement(button, "TemplateName").text = "button_large"
    ET.SubElement(button, "ControlId").text = "1001"
    position = ET.SubElement(button, "Position")
    ET.SubElement(position, "X").text = "14"
    ET.SubElement(position, "Y").text = "62"
    alignment = ET.SubElement(button, "Alignment")
    ET.SubElement(alignment, "TopLeft").text = "true"
    ET.SubElement(button, "OnClickEvent").text = "CompanionProbeClick"
    ET.SubElement(button, "Label").text = "Send click"
    panel.append(button)
    search_label = copy.deepcopy(label)
    search_label.find("ControlId").text = "1002"
    search_label.find("Position/Y").text = "94"
    search_label.find("Data").text = "Search name or class"
    search_label.remove(search_label.find("Adapter"))
    panel.append(search_label)
    edit = ET.SubElement(panel, "EditBoxDef")
    ET.SubElement(edit, "TemplateName").text = "comp_search_editbox"
    ET.SubElement(edit, "ControlId").text = "1003"
    position = ET.SubElement(edit, "Position")
    ET.SubElement(position, "X").text = "14"
    ET.SubElement(position, "Y").text = "115"
    ET.SubElement(edit, "Width").text = "224"
    ET.SubElement(edit, "Height").text = "24"
    ET.SubElement(edit, "MaxCharacters").text = "31"
    ET.SubElement(edit, "AdapterName").text = "comp_probe_search"
    search_button = copy.deepcopy(button)
    search_button.find("ControlId").text = "1004"
    search_button.find("Position/X").text = "265"
    search_button.find("Position/Y").text = "115"
    search_button.find("OnClickEvent").text = "CompanionProbeSearch"
    search_button.find("Label").text = "Search"
    panel.append(search_button)
    ET.indent(root)
    return ET.tostring(root, encoding="iso-8859-1", xml_declaration=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--client", type=Path, required=True, help="Verified client app directory")
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
    xml = window(args.client / "ui/atlantis/new_group_window.xml")
    args.output.mkdir(parents=True)
    (args.output / "game.dll").write_bytes(patched)
    (args.output / "uimain.xml").write_bytes(output_main)
    for skin in ("atlantis", "isles"):
        (args.output / skin).mkdir()
        (args.output / skin / "custom8_window.xml").write_bytes(xml)
    report["uimainBaselineSha256"] = sha256(main_xml)
    report["uimainOutputSha256"] = sha256(output_main)
    report["windowSha256"] = sha256(xml)
    report["status"] = "experimental staged probe only; dedicated actions and typed search failed real-client verification"
    (args.output / "manifest.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
