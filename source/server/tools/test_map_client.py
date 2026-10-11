"""Offline unit tests for build_map_client.py using small synthetic fixtures.

Run from the repository root with:
    python3 -m unittest discover -s source/server/tools -p "test_map_client.py"
No client, server, or game file is needed; the stock hashes are patched to the fixtures.
"""

import struct
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock
import xml.etree.ElementTree as ET

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_map_client as builder


def window_fixture(eol="\r\n", extra_group=False):
    """A miniature map_window.xml with the same element structure as the stock Isles file."""
    lines = [
        '<?xml version="1.0" encoding="ISO-8859-1"?>',
        '<Root_Element ID="DAOCUi">',
        "\t<Texture>",
        "\t\t<Name>no_map</Name>",
        "\t\t<File>maps/no_map.dds</File>",
        "\t</Texture>",
        "\t<WindowTemplate>",
        "\t\t<Name>map_window</Name>",
        "\t\t<CloseButton>true</CloseButton>",
        "\t\t<Width>270</Width>",
        "\t\t<Height>498</Height>",
        "\t\t<TitleWidth>270</TitleWidth>",
        "\t\t<TitleHeight>18</TitleHeight>",
        "\t\t<FullResizeImageDef>",
        "\t\t\t<ControlId>Background</ControlId>",
        "\t\t\t<Height>498</Height>",
        "\t\t\t<Width>270</Width>",
        "\t\t\t<Position>",
        "\t\t\t\t<X>0</X>",
        "\t\t\t\t<Y>0</Y>",
        "\t\t\t</Position>",
        "\t\t</FullResizeImageDef>",
        "\t\t<LabelDef>",
        "\t\t\t<FontName>title</FontName>",
        "\t\t\t<Width>270</Width>",
        "\t\t\t<Data>MAP TITLE GOES HERE</Data>",
        "\t\t\t<Position>",
        "\t\t\t\t<X>0</X>",
        "\t\t\t\t<Y>2</Y>",
        "\t\t\t</Position>",
        "\t\t\t<Adapter>map_title</Adapter>",
        "\t\t</LabelDef>",
        "\t\t<LabelDef>",
        "\t\t\t<FontName>arial11</FontName>",
        "\t\t\t<Width>300</Width>",
        "\t\t\t<Position>",
        "\t\t\t\t<X>12</X>",
        "\t\t\t\t<Y>340</Y>",
        "\t\t\t</Position>",
        "\t\t\t<Adapter>map_info0</Adapter>",
        "\t\t</LabelDef>",
        "\t\t<TextAreaDef>",
        "\t\t\t<TemplateName>map_textarea</TemplateName>",
        "\t\t\t<Position>",
        "\t\t\t\t<X>7</X>",
        "\t\t\t\t<Y>377</Y>",
        "\t\t\t</Position>",
        "\t\t\t<Width>256</Width>",
        "\t\t\t<Height>120</Height>",
        "\t\t\t<ControlId>1005</ControlId>",
        "\t\t</TextAreaDef>",
        "\t\t<LabelDef>",
        "\t\t\t<FontName>arial11</FontName>",
        "\t\t\t<Width>150</Width>",
        "\t\t\t<Data>Zone Description</Data>",
        "\t\t\t<Position>",
        "\t\t\t\t<X>35</X>",
        "\t\t\t\t<Y>337</Y>",
        "\t\t\t</Position>",
        "\t\t</LabelDef>",
        "\t\t<ButtonDef>",
        "\t\t\t<TemplateName>button_checkbox</TemplateName>",
        "\t\t\t<ControlId>1003</ControlId>",
        "\t\t\t<Position>",
        "\t\t\t\t<X>17</X>",
        "\t\t\t\t<Y>336</Y>",
        "\t\t\t</Position>",
        "\t\t</ButtonDef>",
        "\t\t<LabelDef>",
        "\t\t\t<FontName>arial11</FontName>",
        "\t\t\t<Width>150</Width>",
        "\t\t\t<Data>Follow Location</Data>",
        "\t\t\t<Position>",
        "\t\t\t\t<X>161</X>",
        "\t\t\t\t<Y>337</Y>",
        "\t\t\t</Position>",
        "\t\t</LabelDef>",
        "\t\t<ButtonDef>",
        "\t\t\t<TemplateName>button_checkbox</TemplateName>",
        "\t\t\t<ControlId>1006</ControlId>",
        "\t\t\t<Position>",
        "\t\t\t\t<X>143</X>",
        "\t\t\t\t<Y>336</Y>",
        "\t\t\t</Position>",
        "\t\t</ButtonDef>",
        "\t\t<!--<LabelDef>",
        "\t\t\t<FontName>arial11</FontName>",
        "\t\t\t<Data>Show Quest Givers</Data>",
        "\t\t\t<Position>",
        "\t\t\t\t<X>35</X>",
        "\t\t\t\t<Y>358</Y>",
        "\t\t\t</Position>",
        "\t\t</LabelDef>",
        "\t\t<ButtonDef>",
        "\t\t\t<TemplateName>button_checkbox</TemplateName>",
        "\t\t\t<ControlId>1009</ControlId>",
        "\t\t\t<Position>",
        "\t\t\t\t<X>17</X>",
        "\t\t\t\t<Y>356</Y>",
        "\t\t\t</Position>",
        "\t\t</ButtonDef>-->",
        "\t\t<DynamicImageDef>",
        "\t\t\t<ControlId>1000</ControlId>",
        "\t\t\t<Position>",
        "\t\t\t\t<X>7</X>",
        "\t\t\t\t<Y>75</Y>",
        "\t\t\t</Position>",
        "\t\t\t<TextureName>no_map</TextureName>",
        "\t\t\t<Dimensions>",
        "\t\t\t\t<X>256</X>",
        "\t\t\t\t<Y>256</Y>",
        "\t\t\t</Dimensions>",
        "\t\t</DynamicImageDef>",
        "\t\t<!-- These are given priority 2 -->",
        "\t\t<ImageAreaDef>",
        "\t\t\t<TemplateName>quest_waypoint</TemplateName>",
        "\t\t\t<ControlId>1050</ControlId>",
        "\t\t</ImageAreaDef>",
    ]
    for control in range(1100, 1108):
        lines += [
            "\t\t<ImageAreaDef>",
            "\t\t\t<TemplateName>group_location</TemplateName>",
            "\t\t\t<Position>",
            "\t\t\t\t<X>0</X>",
            "\t\t\t\t<Y>0</Y>",
            "\t\t\t</Position>",
            f"\t\t\t<ControlId>{control}</ControlId>",
            "\t\t</ImageAreaDef>",
        ]
    if extra_group:
        # Not a group member dot: its template must stay group_location.
        lines += [
            "\t\t<ImageAreaDef>",
            "\t\t\t<TemplateName>group_location</TemplateName>",
            "\t\t\t<ControlId>1110</ControlId>",
            "\t\t</ImageAreaDef>",
        ]
    lines += [
        "\t\t<ImageAreaDef>",
        "\t\t\t<TemplateName>player_location</TemplateName>",
        "\t\t\t<ControlId>1004</ControlId>",
        "\t\t</ImageAreaDef>",
        "\t\t<ComboBoxDef>",
        "\t\t\t<Position>",
        "\t\t\t\t<X>89</X>",
        "\t\t\t\t<Y>50</Y>",
        "\t\t\t</Position>",
        "\t\t\t<ControlId>1002</ControlId>",
        "\t\t</ComboBoxDef>",
        "\t\t<ComboBoxDef>",
        "\t\t\t<Position>",
        "\t\t\t\t<X>89</X>",
        "\t\t\t\t<Y>25</Y>",
        "\t\t\t</Position>",
        "\t\t\t<ControlId>1001</ControlId>",
        "\t\t</ComboBoxDef>",
        "\t\t<LabelDef>",
        "\t\t\t<Width>45</Width>",
        "\t\t\t<Data>Area</Data>",
        "\t\t\t<Position>",
        "\t\t\t\t<X>45</X>",
        "\t\t\t\t<Y>54</Y>",
        "\t\t\t</Position>",
        "\t\t\t<ControlId>1008</ControlId>",
        "\t\t</LabelDef>",
        "\t\t<LabelDef>",
        "\t\t\t<Width>45</Width>",
        "\t\t\t<Data>Zone</Data>",
        "\t\t\t<Position>",
        "\t\t\t\t<X>45</X>",
        "\t\t\t\t<Y>29</Y>",
        "\t\t\t</Position>",
        "\t\t\t<ControlId>1007</ControlId>",
        "\t\t</LabelDef>",
        "\t</WindowTemplate>",
        "</Root_Element>",
    ]
    return (eol.join(lines) + eol).encode("latin-1")


def tga_fixture(group_cell_alpha=255):
    """A 256x128 uncompressed 32-bit TGA, bottom-left origin, with an empty cell at MARKER_CELL."""
    width, height = builder.ATLAS_WIDTH, builder.ATLAS_HEIGHT
    header = struct.pack("<BBBHHBHHHHBB", 0, 0, 2, 0, 0, 0, 0, 0, width, height, 32, 8)
    rows = []
    empty = cell_pixels(builder.MARKER_CELL)
    for row in range(height):  # file order: bottom row first
        y = height - 1 - row
        for x in range(width):
            if (x, y) in empty:
                rows.append(bytes((0, 0, 0, 0)))
            elif 128 <= x < 144 and 112 <= y < 128:  # stock green dot cell, not touched
                rows.append(bytes((10, 153, 11, 255)))
            else:
                rows.append(bytes(((x * 7) % 256, (y * 3) % 256, 90, 200)))
    footer = b"\0" * 8 + b"TRUEVISION-XFILE.\0"  # 26-byte TGA 2.0 footer
    return header + b"".join(rows) + footer


def cell_pixels(cell):
    x0, y0 = cell
    return {(x, y) for y in range(y0, y0 + builder.CELL) for x in range(x0, x0 + builder.CELL)}


def read_tga_pixel(data, x, y):
    """Top-left coordinates into a bottom-left TGA. Returns (r, g, b, a)."""
    width, height = struct.unpack_from("<HH", data, 12)
    row = height - 1 - y
    b, g, r, a = data[18 + (row * width + x) * 4:18 + (row * width + x) * 4 + 4]
    return r, g, b, a


def dds_fixture(width=512, height=512, fourcc=b"DXT1", mips=0, payload=None):
    header = bytearray(128)
    header[0:4] = b"DDS "
    struct.pack_into("<I", header, 4, 124)
    struct.pack_into("<I", header, 8, 0x81007)
    struct.pack_into("<II", header, 12, height, width)
    struct.pack_into("<I", header, 28, mips)
    struct.pack_into("<I", header, 76, 32)
    struct.pack_into("<I", header, 80, 4)
    header[84:88] = fourcc
    if payload is None:
        payload = builder._dds_payload(width, height, mips)
    return bytes(header) + b"\0" * payload


class WindowTransformTests(unittest.TestCase):
    def setUp(self):
        self.original = window_fixture()
        self.transformed = builder.transform_window(self.original)
        self.root = ET.fromstring(self.transformed)
        self.window = self.root.find("WindowTemplate")

    def control(self, cid):
        return next(el for el in self.window if el.findtext("ControlId") == str(cid))

    def test_window_and_map_are_resized(self):
        self.assertEqual(self.window.findtext("Width"), "526")
        self.assertEqual(self.window.findtext("Height"), "731")
        self.assertEqual(self.window.findtext("TitleWidth"), "526")
        background = self.window.find("FullResizeImageDef")
        self.assertEqual((background.findtext("Width"), background.findtext("Height")), ("526", "731"))
        image = self.control(1000)
        self.assertEqual(image.findtext("Dimensions/X"), "512")
        self.assertEqual(image.findtext("Dimensions/Y"), "512")
        self.assertEqual((image.findtext("Position/X"), image.findtext("Position/Y")), ("7", "75"))
        area = self.control(1005)
        self.assertEqual((area.findtext("Width"), area.findtext("Height")), ("512", "90"))
        self.assertEqual(area.findtext("Position/Y"), "633")
        self.assertEqual(int(self.window.findtext("Height")), 633 + 90 + 8)

    def test_group_members_use_offline_template_and_group_location_stays(self):
        for control in range(1100, 1108):
            self.assertEqual(self.control(control).findtext("TemplateName"), "offline_group_member")
        templates = [el for el in self.root.findall("ImageAreaTemplate") if el.findtext("Name") == "offline_group_member"]
        self.assertEqual(len(templates), 1)
        self.assertEqual(templates[0].findtext("TextureName"), "map_icons")
        self.assertEqual((templates[0].findtext("Size/X"), templates[0].findtext("Size/Y")), ("16", "16"))
        self.assertEqual((templates[0].findtext("TopLeft/X"), templates[0].findtext("TopLeft/Y")), ("144", "112"))
        self.assertEqual(self.control(1050).findtext("TemplateName"), "quest_waypoint")
        self.assertEqual(self.control(1004).findtext("TemplateName"), "player_location")

    def test_group_location_outside_1100_1107_is_untouched(self):
        transformed = builder.transform_window(window_fixture(extra_group=True))
        window = ET.fromstring(transformed).find("WindowTemplate")
        extra = next(el for el in window if el.findtext("ControlId") == "1110")
        self.assertEqual(extra.findtext("TemplateName"), "group_location")

    def test_show_quest_givers_is_enabled_on_the_checkbox_row(self):
        self.assertNotIn(b"<!--<LabelDef>", self.transformed)
        self.assertNotIn(b"</ButtonDef>-->", self.transformed)
        shown = self.control(1009)
        self.assertEqual(shown.tag, "ButtonDef")
        self.assertEqual((shown.findtext("Position/X"), shown.findtext("Position/Y")), ("300", "592"))
        label = next(el for el in self.window.findall("LabelDef") if el.findtext("Data") == "Show Quest Givers")
        self.assertEqual((label.findtext("Position/X"), label.findtext("Position/Y")), ("318", "593"))
        # The stock priority comments are not part of the Show Quest Givers pair and must remain.
        self.assertIn(b"<!-- These are given priority 2 -->", self.transformed)

    def test_checkbox_row_and_info_label_move_with_the_map(self):
        for control, (x, y) in {1003: (17, 592), 1006: (143, 592)}.items():
            self.assertEqual((self.control(control).findtext("Position/X"), self.control(control).findtext("Position/Y")),
                             (str(x), str(y)))
        info = next(el for el in self.window.findall("LabelDef") if el.findtext("Adapter") == "map_info0")
        self.assertEqual((info.findtext("Position/X"), info.findtext("Position/Y")), ("12", "596"))

    def test_zone_and_area_are_centred_above_the_map(self):
        self.assertEqual(self.control(1001).findtext("Position/X"), "217")
        self.assertEqual(self.control(1002).findtext("Position/X"), "217")
        labels = {el.findtext("Data"): el for el in self.window.findall("LabelDef") if el.findtext("Data")}
        self.assertEqual(labels["Zone"].findtext("Position/X"), "173")
        self.assertEqual(labels["Area"].findtext("Position/X"), "173")

    def test_line_endings_and_bytes_outside_the_edits_are_preserved(self):
        self.assertEqual(self.transformed.count(b"\r\n"), self.transformed.count(b"\n"))
        lf_original = window_fixture(eol="\n")
        lf_result = builder.transform_window(lf_original)
        self.assertEqual(lf_result.count(b"\r"), 0)
        self.assertTrue(lf_result.startswith(b'<?xml version="1.0" encoding="ISO-8859-1"?>\n'))
        # Apart from the edited lines, the file text is identical.
        before = self.original.split(b"\r\n")
        after = self.transformed.split(b"\r\n")
        self.assertEqual(len(after) - len(before), 12)  # the new template adds 12 lines; the comment edit adds none

    def test_mixed_line_endings_are_refused(self):
        mixed = window_fixture().replace(b"\r\n", b"\n", 1)
        with self.assertRaises(ValueError):
            builder.transform_window(mixed)

    def test_unexpected_layout_is_refused(self):
        without_map = window_fixture().replace(b"<ControlId>1000</ControlId>", b"<ControlId>1999</ControlId>")
        with self.assertRaises(ValueError):
            builder.transform_window(without_map)


class AtlasTests(unittest.TestCase):
    def setUp(self):
        self.original = tga_fixture()
        self.staged = builder.stage_atlas(self.original)

    def test_only_the_marker_cell_changes(self):
        self.assertEqual(len(self.staged), len(self.original))
        self.assertEqual(self.staged[:18], self.original[:18])
        self.assertEqual(self.staged[-26:], self.original[-26:])
        cell = cell_pixels(builder.MARKER_CELL)
        cell_bytes = set()
        for x, y in cell:
            width = builder.ATLAS_WIDTH
            row = builder.ATLAS_HEIGHT - 1 - y
            offset = 18 + (row * width + x) * 4
            cell_bytes.update(range(offset, offset + 4))
        for index in range(len(self.original)):
            if index not in cell_bytes:
                self.assertEqual(self.staged[index], self.original[index], index)

    def test_marker_and_group_dot_cell_read_back(self):
        self.assertEqual(read_tga_pixel(self.staged, 152, 120)[:3], builder.FILL)  # fully inside the disc
        self.assertEqual(read_tga_pixel(self.staged, 152, 120)[3], 255)
        self.assertEqual(read_tga_pixel(self.staged, 144, 112)[3], 0)  # corner stays transparent
        self.assertEqual(read_tga_pixel(self.staged, 130, 116), read_tga_pixel(self.original, 130, 116))

    def test_marker_pixels_are_a_centred_disc(self):
        rows = builder.marker_pixels()
        self.assertEqual(len(rows), 16)
        self.assertTrue(all(len(row) == 16 for row in rows))
        opaque = [(x, y) for y in range(16) for x in range(16) if rows[y][x][3] > 0]
        xs, ys = [x for x, _ in opaque], [y for _, y in opaque]
        self.assertEqual((min(xs) + max(xs)), 15)  # horizontally centred in the cell
        self.assertEqual((min(ys) + max(ys)), 15)  # vertically centred in the cell
        self.assertTrue(7 <= max(xs) - min(xs) + 1 <= 9)
        self.assertEqual(rows[0][0], (0, 0, 0, 0))
        # Highlight pixels are anti-aliased against the fill, so look for near-white ones.
        self.assertTrue(any(min(pixel[:3]) > 180 for row in rows for pixel in row if pixel[3]))

    def test_refuses_an_occupied_cell(self):
        data = bytearray(self.original)
        width = builder.ATLAS_WIDTH
        row = builder.ATLAS_HEIGHT - 1 - 112
        data[18 + (row * width + 144) * 4 + 3] = 255
        with self.assertRaises(ValueError):
            builder.stage_atlas(bytes(data))

    def test_refuses_a_different_atlas_format(self):
        data = bytearray(self.original)
        data[16] = 24  # bits per pixel
        with self.assertRaises(ValueError):
            builder.stage_atlas(bytes(data))


class MapTextureTests(unittest.TestCase):
    def test_accepts_512_dxt1(self):
        builder.check_map(dds_fixture(), "z001.dds")

    def test_accepts_mipmapped_512_dxt1(self):
        builder.check_map(dds_fixture(mips=10), "z001.dds")

    def test_refuses_256_and_other_formats(self):
        for data in (dds_fixture(256, 256), dds_fixture(fourcc=b"DXT5"), dds_fixture(payload=10)):
            with self.assertRaises(ValueError):
                builder.check_map(data, "z001.dds")
        with self.assertRaises(ValueError):
            builder.check_map(b"NOPE" + dds_fixture()[4:], "z001.dds")


class BuildStageTests(unittest.TestCase):
    def make_client(self, root):
        client = root / "app"
        (client / "ui" / "atlantis").mkdir(parents=True)
        (client / "ui" / "isles").mkdir(parents=True)
        (client / "ui" / "maps").mkdir(parents=True)
        window = window_fixture()
        (client / "ui" / "atlantis" / "map_window.xml").write_bytes(window)
        (client / "ui" / "isles" / "map_window.xml").write_bytes(window)
        (client / "ui" / "isles" / "map_icons.tga").write_bytes(tga_fixture())
        (client / "ui" / "maps" / "z001.dds").write_bytes(dds_fixture(256, 256, payload=None))
        return client

    def pins(self):
        return {
            "ui/atlantis/map_window.xml": builder.sha256(window_fixture()),
            "ui/isles/map_window.xml": builder.sha256(window_fixture()),
            "ui/isles/map_icons.tga": builder.sha256(tga_fixture()),
        }

    def test_stage_manifest_and_files(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            client = self.make_client(root)
            maps = root / "maps"
            maps.mkdir()
            (maps / "z001.dds").write_bytes(dds_fixture())
            with mock.patch.dict(builder.BASELINE, self.pins()):
                manifest = builder.build_stage(client, root / "stage", maps)
            self.assertEqual(manifest["kind"], "map-window-512")
            entries = {entry["path"]: entry for entry in manifest["files"]}
            self.assertEqual(set(entries), {"ui/atlantis/map_window.xml", "ui/isles/map_window.xml",
                                            "ui/isles/map_icons.tga", "ui/maps/z001.dds"})
            for entry in entries.values():
                staged = (root / "stage" / entry["stagePath"]).read_bytes()
                self.assertEqual(builder.sha256(staged), entry["sha256"])
            self.assertEqual(entries["ui/maps/z001.dds"]["baselineSha256"],
                             builder.sha256((client / "ui/maps/z001.dds").read_bytes()))
            self.assertEqual(entries["ui/isles/map_window.xml"]["baselineSha256"],
                             self.pins()["ui/isles/map_window.xml"])
            self.assertEqual(entries["ui/atlantis/map_window.xml"]["sha256"],
                             entries["ui/isles/map_window.xml"]["sha256"])
            self.assertTrue((root / "stage" / "manifest.json").is_file())

    def test_refuses_existing_output_and_unknown_map_names(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            client = self.make_client(root)
            (root / "stage").mkdir()
            with mock.patch.dict(builder.BASELINE, self.pins()):
                with self.assertRaises(ValueError):
                    builder.build_stage(client, root / "stage")
                maps = root / "maps"
                maps.mkdir()
                (maps / "zz999.dds").write_bytes(dds_fixture())
                with self.assertRaises(ValueError):
                    builder.build_stage(client, root / "other", maps)

    def test_refuses_an_unpinned_client_file(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            client = self.make_client(root)
            with self.assertRaises(ValueError):
                builder.build_stage(client, root / "stage")


if __name__ == "__main__":
    unittest.main()
