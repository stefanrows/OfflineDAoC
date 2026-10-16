#!/usr/bin/env python3
"""Offline mock renderer for the OPEN BG window (Custom5): draws the window XML the client builder generates
with the label texts the server would send, using the stock client textures and bitmap fonts. Needs only Pillow.

  render_open_bg_mock.py --window <stage dir | custom5_window.xml> --labels <labels.json> --out <png>
                         [--ui <client ui dir or app dir>] [--skin atlantis|isles]

--window  a stage directory of build_companion_manager_client.py (reads <skin>/custom5_window.xml), a client app/ui
          directory, or the XML file itself.
--labels  JSON object {"620": "text", ...} for the labels 620-984 (the Adapter names cmgr_<n> of the window).
          The explicit C# test DumpLabelsForTheMockRenderer (UT_OpenBgWindow.cs) writes overview.json and setup.json from
          a status snapshot through the real session; see docs/open-bg-redesign/README.md. Committed samples:
          docs/open-bg-redesign/sample-labels-lage.json and sample-labels-setup.json.
--ui      the stock client UI (default /mnt/c/OfflineDAoC/playable/runtime/client-opendaoc/app); either the app
          folder or its ui subfolder. Textures and fonts come from there; the window file comes from --window.

This is an approximation: glyph placement, label alpha, hover states, click areas and the Isles skin textures are
not what the real client shows (see the checklist in docs/COMPANION_MANAGER_INTEGRATION.md).
"""
import argparse
import glob
import json
import os
import sys
import xml.etree.ElementTree as ET

from PIL import Image

DEFAULT_UI = "/mnt/c/OfflineDAoC/playable/runtime/client-opendaoc/app"
LABEL_BASE = 620


def find_ui_dir(path):
    path = os.path.abspath(path)
    for cand in (path, os.path.join(path, "ui")):
        if os.path.isdir(os.path.join(cand, "fonts")) and os.path.isdir(os.path.join(cand, "atlantis")):
            return cand + os.sep
    raise SystemExit(f"No stock client UI (fonts/ and atlantis/) under {path}")


def find_window_xml(window, skin):
    if os.path.isfile(window):
        return window
    for cand in (os.path.join(window, skin, "custom5_window.xml"),
                 os.path.join(window, "ui", skin, "custom5_window.xml"),
                 os.path.join(window, "custom5_window.xml")):
        if os.path.isfile(cand):
            return cand
    raise SystemExit(f"custom5_window.xml for skin {skin} not found under {window}")


def load_bitmap_font(path):
    """Glyph rows end in a marker line (pixel at x=0 set); segments after the first are glyph extents."""
    f = Image.open(path).convert("RGBA")
    fa = f.getchannel("A")
    width, height = f.size
    marks = [y for y in range(height)
             if fa.getpixel((0, y)) > 128 and sum(1 for x in range(width) if fa.getpixel((x, y)) > 128) > 60]
    out, code, top = {}, 33, 0
    for my in marks:
        xs = [x for x in range(width) if fa.getpixel((x, my)) > 128]
        segs, start, prev = [], xs[0], xs[0]
        for xv in xs[1:]:
            if xv != prev + 1:
                segs.append((start, prev))
                start = xv
            prev = xv
        segs.append((start, prev))
        for a0, e0 in segs[1:]:
            out[code] = f.crop((a0, top, e0 + 1, my))
            code += 1
        top = my + 1
    return out


class Renderer:
    def __init__(self, ui_dir, skin, window_xml):
        self.ui = ui_dir
        self.textures, self.templates, self._tex = {}, {}, {}
        self._index = None
        for path in sorted(glob.glob(self.ui + skin + "/*.xml")):
            try:
                self._scan(ET.fromstring(open(path, "rb").read()))
            except ET.ParseError:
                pass
        self.window = ET.fromstring(open(window_xml, "rb").read())
        self._scan(self.window)
        self.fonts = {"arial14": load_bitmap_font(self.ui + "fonts/Arial14.tga"),
                      "title": load_bitmap_font(self.ui + "fonts/title_24.tga")}

    def _scan(self, root):
        for e in root:
            name = (e.findtext("Name") or "").strip()
            if e.tag in ("Texture", "Canvas"):
                self.textures[name] = (e.findtext("File") or "").strip()
            elif e.tag.endswith("Template") and name:
                self.templates.setdefault((e.tag, name), e)

    def tex(self, name):
        if name not in self._tex:
            path = self.textures.get(name)
            found = None
            if path:
                if self._index is None:
                    self._index = {f[len(self.ui):].lower().replace("\\", "/"): f
                                   for f in glob.glob(self.ui + "**/*", recursive=True) if os.path.isfile(f)}
                real = self._index.get(path.lower().replace("\\", "/"))
                if real:
                    found = Image.open(real).convert("RGBA")
            self._tex[name] = found
        return self._tex[name]

    @staticmethod
    def _pt(e, tag):
        p = e.find(tag)
        return (int(p.findtext("X") or 0), int(p.findtext("Y") or 0)) if p is not None else (0, 0)

    @staticmethod
    def _tile(canvas, src, box, x, y, w, h):
        if w <= 0 or h <= 0 or src is None:
            return
        piece = src.crop(box)
        pw, ph = piece.size
        if pw == 0 or ph == 0:
            return
        for yy in range(0, h, ph):
            for xx in range(0, w, pw):
                canvas.alpha_composite(piece.crop((0, 0, min(pw, w - xx), min(ph, h - yy))), (x + xx, y + yy))

    def full(self, canvas, name, x, y, w, h):
        t = self.templates.get(("FullResizeImageTemplate", name))
        if t is None:
            print("missing FullResizeImageTemplate", name, file=sys.stderr)
            return
        g = lambda k: int(t.findtext(k) or 0)
        top, mid, bot, left, cen, right = (g("TopHeight"), g("MiddleHeight"), g("BottomHeight"),
                                           g("LeftWidth"), g("MiddleWidth"), g("RightWidth"))
        tx = t.find("Texture")
        src = self.tex(tx.findtext("TextureName").strip())
        names = [["TopLeft", "TopMiddle", "TopRight"], ["MiddleLeft", "MiddleMiddle", "MiddleRight"],
                 ["BottomLeft", "BottomMiddle", "BottomRight"]]
        cols = [(left, x, left), (cen, x + left, w - left - right), (right, x + w - right, right)]
        rows = [(top, y, top), (mid, y + top, h - top - bot), (bot, y + h - bot, bot)]
        for ri, (sh, dy, dh) in enumerate(rows):
            for ci, (sw, dx, dw) in enumerate(cols):
                sx, sy = self._pt(tx, names[ri][ci])
                self._tile(canvas, src, (sx, sy, sx + sw, sy + sh), dx, dy, dw, dh)

    def hres(self, canvas, name, x, y, w):
        t = self.templates.get(("HorizontalResizeImageTemplate", name))
        if t is None:
            print("missing HorizontalResizeImageTemplate", name, file=sys.stderr)
            return
        g = lambda k: int(t.findtext(k) or 0)
        height, left, rep, right = g("Height"), g("LeftWidth"), g("RepeatWidth"), g("RightWidth")
        tx = t.find("Texture")
        src = self.tex(tx.findtext("TextureName").strip())
        (lx, ly), (cx, cy), (rx, ry) = self._pt(tx, "Left"), self._pt(tx, "Repeat"), self._pt(tx, "Right")
        if left:
            self._tile(canvas, src, (lx, ly, lx + left, ly + height), x, y, left, height)
        self._tile(canvas, src, (cx, cy, cx + rep, cy + height), x + left, y, w - left - right, height)
        if right:
            self._tile(canvas, src, (rx, ry, rx + right, ry + height), x + w - right, y, right, height)

    def area(self, canvas, name, x, y):
        t = self.templates.get(("ImageAreaTemplate", name))
        if t is None:
            print("missing ImageAreaTemplate", name, file=sys.stderr)
            return
        src = self.tex(t.findtext("TextureName").strip())
        (sx, sy), (w, h) = self._pt(t, "TopLeft"), self._pt(t, "Size")
        if src:
            canvas.alpha_composite(src.crop((sx, sy, sx + w, sy + h)), (x, y))

    def text(self, canvas, s, x, y, w, color, centered=False, font="arial14", end=False):
        glyphs = self.fonts.get(font, self.fonts["arial14"])
        space = 7 if font == "title" else 5
        data = s.encode("cp1252", "replace")
        width = sum(glyphs[b].size[0] if b in glyphs else space for b in data)
        if centered:
            x += max(0, (w - width) // 2)
        elif end:
            x += max(0, w - width)
        limit = x + w
        for b in data:
            g = glyphs.get(b)
            if g is None:
                x += space
                continue
            if x + g.size[0] > limit + 1:
                break
            r, gg, bb, aa = g.split()
            tint = Image.merge("RGBA", (r.point(lambda v: v * color[0] // 255), gg.point(lambda v: v * color[1] // 255),
                                        bb.point(lambda v: v * color[2] // 255), aa.point(lambda v: v * color[3] // 255)))
            canvas.alpha_composite(tint, (x, y))
            x += g.size[0]

    def render(self, labels):
        panel = self.window.find("WindowTemplate")
        width, height = int(panel.findtext("Width")), int(panel.findtext("Height"))
        canvas = Image.new("RGBA", (width + 40, height + 40), (58, 66, 52, 255))  # grassy backdrop
        sub = Image.new("RGBA", (width, height), (0, 0, 0, 0))
        for e in panel:
            if e.tag == "FullResizeImageDef":
                x, y = self._pt(e, "Position")
                self.full(sub, e.findtext("TemplateName").strip(), x, y, int(e.findtext("Width")), int(e.findtext("Height")))
            elif e.tag == "HorizontalResizeImageDef":
                x, y = self._pt(e, "Position")
                self.hres(sub, e.findtext("TemplateName").strip(), x, y, int(e.findtext("Width")))
            elif e.tag == "ImageAreaDef":
                x, y = self._pt(e, "Position")
                self.area(sub, e.findtext("TemplateName").strip(), x, y)
            elif e.tag == "LabelDef":
                x, y = self._pt(e, "Position")
                c = e.find("Color")
                color = tuple(int(c.findtext(k)) for k in "RGBA")
                adapter = (e.findtext("Adapter") or "").strip()
                index = int(adapter.split("_")[-1]) if adapter.startswith("cmgr_") and adapter.split("_")[-1].isdigit() else None
                s = labels.get(index, "") if index is not None else (e.findtext("Data") or "")
                if s:
                    self.text(sub, s, x, y, int(e.findtext("Width")), color,
                              (e.findtext("TextCentered") or "").strip() == "true",
                              (e.findtext("FontName") or "arial14").strip(),
                              (e.findtext("EndAligned") or "").strip() == "true")
        canvas.alpha_composite(sub, (20, 20))
        return canvas.convert("RGB")


def load_labels(path):
    with open(path, encoding="utf-8") as f:
        raw = json.load(f)
    return {int(k): v for k, v in raw.items()}


def render_file(window, labels_path, out, ui=DEFAULT_UI, skin="atlantis"):
    """Renders and saves one PNG; returns its (width, height)."""
    renderer = Renderer(find_ui_dir(ui), skin, find_window_xml(window, skin))
    image = renderer.render(load_labels(labels_path))
    image.save(out)
    return image.size


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--window", required=True, help="stage dir, client ui/app dir or custom5_window.xml")
    p.add_argument("--labels", required=True, help="labels JSON (620-984)")
    p.add_argument("--out", required=True, help="PNG to write")
    p.add_argument("--ui", default=DEFAULT_UI, help="stock client app or ui folder (textures, fonts)")
    p.add_argument("--skin", default="atlantis", choices=("atlantis", "isles"))
    a = p.parse_args(argv)
    size = render_file(a.window, a.labels, a.out, a.ui, a.skin)
    print("ok", size[0], size[1], a.out)


if __name__ == "__main__":
    main()
