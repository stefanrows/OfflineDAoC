"""Stage the bigger Map window for the installed DAoC client.

The stock map_window.xml (Atlantis and Isles, byte-identical) shows a 256x256
map. This builder widens the window to 526 px, doubles the map control to
512x512, moves everything below the map down by 256 px, centres the Zone and
Area boxes, and enables the commented-out Show Quest Givers checkbox on the
checkbox row.

Companion and group member dots (ImageAreaDefs 1100-1107) use a new template,
offline_group_member, that points at an empty 16x16 cell of the Isles
map_icons.tga atlas. The builder draws a cyan marker in that cell. The
world-bot dots keep the stock group_location template and its atlas cell.

The 512x512 map textures are not made here. Pass --maps with DXT1 .dds files
named as in the client's ui/maps folder to stage them too; without it only the
window XML and the atlas change. The input client is read only.
"""

import argparse
from functools import partial
import hashlib
import json
import math
import os
from pathlib import Path
import re
import struct
import xml.etree.ElementTree as ET


SKINS = ("atlantis", "isles")
# Stock files as installed in the verified client. Both skins carry identical
# map_window.xml bytes; each path is pinned so a different client is refused.
BASELINE = {
    "ui/atlantis/map_window.xml": "cd1c6cf793363e8eecbdc922af34c83e3dc84b8c86d65ac4b8abd4bf480913f7",
    "ui/isles/map_window.xml": "cd1c6cf793363e8eecbdc922af34c83e3dc84b8c86d65ac4b8abd4bf480913f7",
    "ui/isles/map_icons.tga": "cd1f458f86d096dd3b7c3a1772626994345403a7c99b374f8218d2babcf42311",
}
ATLAS = "ui/isles/map_icons.tga"
MAP_SIZE = 512
TEMPLATE = "offline_group_member"
MARKER_CELL = (144, 112)  # top-left of the empty 16x16 cell in the stock atlas
CELL = 16
ATLAS_WIDTH, ATLAS_HEIGHT = 256, 128
GROUP_MEMBER_IDS = tuple(range(1100, 1108))
FILL, OUTLINE, HIGHLIGHT = (0, 220, 255), (0, 28, 44), (255, 255, 255)
MAP_NAME = re.compile(r"[A-Za-z0-9_]+\.dds")
DDS_HEADER = 128

# Each entry: element tag, a marker that identifies the one element, and the
# (parent, field, old, new) value changes. Old values must match exactly.
EDITS = (
    ("FullResizeImageDef", "<ControlId>Background</ControlId>", [
        (None, "Width", "270", "526"), (None, "Height", "498", "731")]),
    ("LabelDef", "<Data>MAP TITLE GOES HERE</Data>", [(None, "Width", "270", "526")]),
    ("DynamicImageDef", "<ControlId>1000</ControlId>", [
        ("Dimensions", "X", "256", "512"), ("Dimensions", "Y", "256", "512")]),
    ("TextAreaDef", "<ControlId>1005</ControlId>", [
        (None, "Width", "256", "512"), (None, "Height", "120", "90"), ("Position", "Y", "377", "633")]),
    ("ComboBoxDef", "<ControlId>1001</ControlId>", [("Position", "X", "89", "217")]),
    ("ComboBoxDef", "<ControlId>1002</ControlId>", [("Position", "X", "89", "217")]),
    ("LabelDef", "<Data>Zone</Data>", [("Position", "X", "45", "173")]),
    ("LabelDef", "<Data>Area</Data>", [("Position", "X", "45", "173")]),
    ("ButtonDef", "<ControlId>1003</ControlId>", [("Position", "Y", "336", "592")]),
    ("LabelDef", "<Data>Zone Description</Data>", [("Position", "Y", "337", "593")]),
    ("ButtonDef", "<ControlId>1006</ControlId>", [("Position", "Y", "336", "592")]),
    ("LabelDef", "<Data>Follow Location</Data>", [("Position", "Y", "337", "593")]),
    # Show Quest Givers is uncommented first (see transform_window), then placed on the row.
    ("ButtonDef", "<ControlId>1009</ControlId>", [("Position", "X", "17", "300"), ("Position", "Y", "356", "592")]),
    ("LabelDef", "<Data>Show Quest Givers</Data>", [("Position", "X", "35", "318"), ("Position", "Y", "358", "593")]),
    ("LabelDef", "<Adapter>map_info0</Adapter>", [("Position", "Y", "340", "596")]),
)


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def _newline(text):
    crlf, lf = text.count("\r\n"), text.count("\n")
    if crlf and crlf == lf:
        return "\r\n"
    if lf and not crlf:
        return "\n"
    raise ValueError("map_window.xml has mixed line endings")


def _replace_once(text, old, new):
    if text.count(old) != 1:
        raise ValueError(f"Expected one {old!r}, found {text.count(old)}")
    return text.replace(old, new)


def _set(block, parent, tag, old, new):
    """Changes <tag>old</tag> to new, directly or inside <parent>; each must match once."""
    if parent is None:
        return _replace_once(block, f"<{tag}>{old}</{tag}>", f"<{tag}>{new}</{tag}>")
    sections = re.findall(rf"<{parent}>.*?</{parent}>", block, re.S)
    if len(sections) != 1:
        raise ValueError(f"Expected one <{parent}> in the block, found {len(sections)}")
    return block.replace(sections[0], _set(sections[0], None, tag, old, new))


def _blocks(text, tag):
    """Spans of every uncommented <tag> element."""
    comments = [m.span() for m in re.finditer(r"<!--.*?-->", text, re.S)]
    return [m.span() for m in re.finditer(rf"<{tag}>.*?</{tag}>", text, re.S)
            if not any(a <= m.start() < b for a, b in comments)]


def _edit(text, tag, marker, edit):
    """Applies edit to the single uncommented <tag> element that contains marker."""
    spans = [span for span in _blocks(text, tag) if marker in text[span[0]:span[1]]]
    if len(spans) != 1:
        raise ValueError(f"Expected one <{tag}> containing {marker}, found {len(spans)}")
    start, end = spans[0]
    return text[:start] + edit(text[start:end]) + text[end:]


def _apply(block, changes):
    for parent, field, old, new in changes:
        block = _set(block, parent, field, old, new)
    return block


def transform_window(data):
    """Returns the widened map_window.xml; encoding and line endings are preserved."""
    text = data.decode("latin-1")
    newline = _newline(text)
    # Show Quest Givers is the only commented LabelDef/ButtonDef pair; the other
    # comments in the file are priority notes and stay as they are.
    text = _replace_once(text, "<!--<LabelDef>", "<LabelDef>")
    text = _replace_once(text, "</ButtonDef>-->", "</ButtonDef>")

    start, stop = text.index("<WindowTemplate>"), text.index("<FullResizeImageDef>")
    head = _set(text[start:stop], None, "Width", "270", "526")
    head = _set(head, None, "Height", "498", "731")
    head = _set(head, None, "TitleWidth", "270", "526")
    text = text[:start] + head + text[stop:]

    for tag, marker, changes in EDITS:
        text = _edit(text, tag, marker, partial(_apply, changes=changes))
    for control in GROUP_MEMBER_IDS:
        text = _edit(text, "ImageAreaDef", f"<ControlId>{control}</ControlId>",
                     partial(_set, parent=None, tag="TemplateName", old="group_location", new=TEMPLATE))

    template = [
        "\t<ImageAreaTemplate>",
        f"\t\t<Name>{TEMPLATE}</Name>",
        "\t\t<TextureName>map_icons</TextureName>",
        "\t\t<Size>",
        f"\t\t\t<X>{CELL}</X>",
        f"\t\t\t<Y>{CELL}</Y>",
        "\t\t</Size>",
        "\t\t<TopLeft>",
        f"\t\t\t<X>{MARKER_CELL[0]}</X>",
        f"\t\t\t<Y>{MARKER_CELL[1]}</Y>",
        "\t\t</TopLeft>",
        "\t</ImageAreaTemplate>",
    ]
    at = text.index("<WindowTemplate>")
    at = text.rfind("\n", 0, at) + 1
    text = text[:at] + "".join(line + newline for line in template) + text[at:]
    result = text.encode("latin-1")
    verify_window(result)
    return result


def _control(window, control):
    found = [el for el in window if el.findtext("ControlId") == str(control)]
    if len(found) != 1:
        raise ValueError(f"Expected one control {control}, found {len(found)}")
    return found[0]


def _labelled(window, data):
    found = [el for el in window.findall("LabelDef") if el.findtext("Data") == data]
    if len(found) != 1:
        raise ValueError(f"Expected one label {data!r}, found {len(found)}")
    return found[0]


def _xy(element):
    return int(element.find("Position/X").text), int(element.find("Position/Y").text)


def verify_window(data):
    """Raises ValueError unless data has every value the widened window needs."""
    root = ET.fromstring(data)
    window = root.find("WindowTemplate")

    def want(condition, message):
        if not condition:
            raise ValueError(message)

    want(b"<!--<LabelDef>" not in data and b"</ButtonDef>-->" not in data, "Show Quest Givers is still commented out")
    want(window.findtext("Width") == "526" and window.findtext("Height") == "731"
         and window.findtext("TitleWidth") == "526", "window size")
    background = window.find("FullResizeImageDef")
    want(background.findtext("Width") == "526" and background.findtext("Height") == "731", "background size")
    want(_labelled(window, "MAP TITLE GOES HERE").findtext("Width") == "526", "title width")

    image = _control(window, 1000)
    want(_xy(image) == (7, 75) and image.findtext("Dimensions/X") == "512"
         and image.findtext("Dimensions/Y") == "512", "map image must be 512x512 at (7,75)")
    area = _control(window, 1005)
    want(_xy(area) == (7, 633) and area.findtext("Width") == "512" and area.findtext("Height") == "90",
         "text area must be 512x90 at (7,633)")
    want(int(window.findtext("Height")) == 633 + 90 + 8, "window height must end 8 px below the text area")

    want(_xy(_control(window, 1001)) == (217, 25) and _xy(_control(window, 1002)) == (217, 50), "combo boxes")
    want(_xy(_labelled(window, "Zone")) == (173, 29) and _xy(_labelled(window, "Area")) == (173, 54), "zone/area labels")
    want(_xy(_control(window, 1003)) == (17, 592) and _xy(_control(window, 1006)) == (143, 592), "checkbox row")
    want(_xy(_labelled(window, "Zone Description")) == (35, 593)
         and _xy(_labelled(window, "Follow Location")) == (161, 593), "checkbox labels")
    shown = _control(window, 1009)
    want(shown.tag == "ButtonDef" and _xy(shown) == (300, 592), "Show Quest Givers button")
    want(_xy(_labelled(window, "Show Quest Givers")) == (318, 593), "Show Quest Givers label")
    info = [el for el in window.findall("LabelDef") if el.findtext("Adapter") == "map_info0"]
    want(len(info) == 1 and _xy(info[0]) == (12, 596), "map_info0 label")

    templates = [el for el in root.findall("ImageAreaTemplate") if el.findtext("Name") == TEMPLATE]
    want(len(templates) == 1, "offline_group_member template")
    marker = templates[0]
    want(marker.findtext("TextureName") == "map_icons" and marker.findtext("Size/X") == str(CELL)
         and marker.findtext("Size/Y") == str(CELL) and marker.findtext("TopLeft/X") == str(MARKER_CELL[0])
         and marker.findtext("TopLeft/Y") == str(MARKER_CELL[1]), "offline_group_member geometry")
    for control in GROUP_MEMBER_IDS:
        want(_control(window, control).findtext("TemplateName") == TEMPLATE, f"ImageAreaDef {control}")


def _tga_check(data):
    id_len, colormap, image_type, _, _, _, _, _, width, height, depth, descriptor = struct.unpack_from(
        "<BBBHHBHHHHBB", data, 0)
    if (id_len, colormap, image_type, width, height, depth, descriptor & 0x0F) != (0, 0, 2, ATLAS_WIDTH, ATLAS_HEIGHT, 32, 8):
        raise ValueError("map_icons.tga is not the expected 256x128 uncompressed RGBA atlas")
    if len(data) < 18 + width * height * 4:
        raise ValueError("map_icons.tga is truncated")
    top_origin = bool(descriptor & 0x20)

    def offset(x, y):
        row = y if top_origin else height - 1 - y
        return 18 + (row * width + x) * 4

    return offset


def marker_pixels():
    """Anti-aliased cyan disc (about 7 px) with a dark outline and white highlight; RGBA rows, top-left origin."""
    rows = []
    for y in range(CELL):
        row = []
        for x in range(CELL):
            total, hits = [0, 0, 0], 0
            for sy in range(4):
                for sx in range(4):
                    dx = x + (sx + 0.5) / 4 - CELL / 2
                    dy = y + (sy + 0.5) / 4 - CELL / 2
                    distance = math.hypot(dx, dy)
                    if distance > 3.5:
                        continue
                    if distance >= 2.5:
                        colour = OUTLINE
                    elif distance >= 1.2 and dx + dy < -1.6:
                        colour = HIGHLIGHT
                    else:
                        colour = FILL
                    hits += 1
                    total = [t + c for t, c in zip(total, colour)]
            if hits == 0:
                row.append((0, 0, 0, 0))
            else:
                row.append(tuple(round(t / hits) for t in total) + (round(255 * hits / 16),))
        rows.append(row)
    return rows


def stage_atlas(data):
    """Returns the Isles atlas with the marker drawn in the empty cell; every other byte is unchanged."""
    offset = _tga_check(data)
    x0, y0 = MARKER_CELL
    for y in range(y0, y0 + CELL):
        for x in range(x0, x0 + CELL):
            if data[offset(x, y) + 3] != 0:
                raise ValueError(f"Atlas cell at {MARKER_CELL} is not empty")
    out = bytearray(data)
    for dy, row in enumerate(marker_pixels()):
        for dx, (r, g, b, a) in enumerate(row):
            position = offset(x0 + dx, y0 + dy)
            out[position:position + 4] = bytes((b, g, r, a))
    return bytes(out)


def _dds_payload(width, height, mips):
    total = 0
    for level in range(max(1, mips)):
        total += max(1, (width >> level) // 4) * max(1, (height >> level) // 4) * 8
    return total


def check_map(data, name):
    """Raises ValueError unless data is a 512x512 DXT1 DDS file."""
    if len(data) < DDS_HEADER or data[:4] != b"DDS " or struct.unpack_from("<I", data, 4)[0] != 124:
        raise ValueError(f"{name}: not a DDS file")
    height, width = struct.unpack_from("<II", data, 12)
    mips = struct.unpack_from("<I", data, 28)[0]
    if (width, height) != (MAP_SIZE, MAP_SIZE):
        raise ValueError(f"{name}: {width}x{height}, expected {MAP_SIZE}x{MAP_SIZE}")
    pixel_format_size, pixel_format_flags = struct.unpack_from("<II", data, 76)
    if pixel_format_size != 32 or not pixel_format_flags & 0x4 or data[84:88] != b"DXT1":
        raise ValueError(f"{name}: expected a DXT1 texture")
    if len(data) != DDS_HEADER + _dds_payload(width, height, mips):
        raise ValueError(f"{name}: payload size does not match a DXT1 {MAP_SIZE}x{MAP_SIZE} texture")


def _pinned(client, relative):
    data = (client / relative).read_bytes()
    if sha256(data) != BASELINE[relative]:
        raise ValueError(f"{relative} is not the verified stock file ({sha256(data)})")
    return data


def build_stage(client, output, maps=None):
    """Writes the staged map client into a new output folder and returns its manifest."""
    if output.exists():
        raise ValueError("Output directory already exists")
    staged = {}  # client path -> (bytes, baseline sha256)
    for skin in SKINS:
        path = f"ui/{skin}/map_window.xml"
        staged[path] = (transform_window(_pinned(client, path)), BASELINE[path])
    staged[ATLAS] = (stage_atlas(_pinned(client, ATLAS)), BASELINE[ATLAS])
    if maps is not None:
        if not maps.is_dir():
            raise ValueError(f"Map folder not found: {maps}")
        # Windows file names are case-insensitive; stage under the stock spelling (z027.DDS).
        installed = {p.name.lower(): p.name for p in (client / "ui" / "maps").iterdir()}
        sources = sorted(maps.glob("*.dds"))
        if not sources:
            raise ValueError(f"No .dds files in {maps}")
        for source in sources:
            name = source.name
            if not MAP_NAME.fullmatch(name):
                raise ValueError(f"Unexpected map file name: {name}")
            if name.lower() not in installed:
                raise ValueError(f"{name} is not a map in the client's ui/maps")
            name = installed[name.lower()]
            data = source.read_bytes()
            check_map(data, name)
            stock = (client / "ui" / "maps" / name).read_bytes()
            staged[f"ui/maps/{name}"] = (data, sha256(stock))

    files = []
    for path, (data, baseline) in staged.items():
        stage_path = path[len("ui/"):]
        (output / stage_path).parent.mkdir(parents=True, exist_ok=True)
        (output / stage_path).write_bytes(data)
        files.append({"path": path, "stagePath": stage_path, "baselineSha256": baseline, "sha256": sha256(data)})
    manifest = {
        "kind": "map-window-512",
        "mapSize": MAP_SIZE,
        "templateName": TEMPLATE,
        "atlasCell": {"x": MARKER_CELL[0], "y": MARKER_CELL[1], "size": CELL},
        "files": files,
        "status": "staged 512x512 map window and group marker; offline checks only until the owner's real-client check",
    }
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--client", type=Path, required=True, help="Installed client app directory (read only)")
    parser.add_argument("--output", type=Path, required=True, help="New staging directory")
    parser.add_argument("--maps", type=Path, help="Optional folder of 512x512 DXT1 .dds maps named as in ui/maps")
    args = parser.parse_args()
    try:
        manifest = build_stage(args.client, args.output, args.maps)
    except ValueError as error:
        parser.error(str(error))
    print(json.dumps({entry["path"]: entry["sha256"] for entry in manifest["files"]}, indent=2))
    print("Staged only. No client or server was started or modified.")


if __name__ == "__main__":
    main()
