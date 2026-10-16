"""Offline mock renderer for DAoC client window XML (LabelDef, FullResizeImageDef, HorizontalResizeImageDef,
ImageAreaDef, StatusBarDef as empty back). Usage: render.py <window.xml> <labels.json> <out.png> [skin]"""
import sys, json, glob, os
import xml.etree.ElementTree as ET
from PIL import Image
UI = "/mnt/c/OfflineDAoC/playable/runtime/client-opendaoc/app/ui/"
win_path, labels_path, out = sys.argv[1:4]
skin = sys.argv[4] if len(sys.argv) > 4 else "atlantis"
labels = {int(k): v for k, v in json.load(open(labels_path)).items()}

textures, templates = {}, {}
def scan(root):
    for e in root:
        n = (e.findtext("Name") or "").strip()
        if e.tag in ("Texture", "Canvas"):
            textures[n] = (e.findtext("File") or "").strip()
        elif e.tag.endswith("Template") and n:
            templates.setdefault((e.tag, n), e)
for f in sorted(glob.glob(UI + skin + "/*.xml")):
    try: scan(ET.fromstring(open(f, "rb").read()))
    except Exception: pass
win = ET.fromstring(open(win_path, "rb").read())
scan(win)
_tex = {}
def tex(name):
    if name not in _tex:
        path = textures.get(name)
        cands = [UI + path] if path else []
        cands += [p for p in glob.glob(UI + "*/" + os.path.basename(path or "x")) ] if path else []
        for c in cands:
            for real in glob.glob(c, recursive=False) or [c]:
                if os.path.exists(real):
                    _tex[name] = Image.open(real).convert("RGBA"); break
            if name in _tex: break
        else:
            # case-insensitive lookup
            low = (path or "").lower()
            for p in glob.glob(UI + "**/*", recursive=True):
                if p[len(UI):].lower() == low:
                    _tex[name] = Image.open(p).convert("RGBA"); break
        _tex.setdefault(name, None)
    return _tex[name]
def pt(e, tag):
    p = e.find(tag)
    return (int(p.findtext("X") or 0), int(p.findtext("Y") or 0)) if p is not None else (0, 0)
def tile(canvas, src, box, x, y, w, h):
    if w <= 0 or h <= 0 or src is None: return
    piece = src.crop(box)
    pw, ph = piece.size
    if pw == 0 or ph == 0: return
    for yy in range(0, h, ph):
        for xx in range(0, w, pw):
            p = piece.crop((0, 0, min(pw, w - xx), min(ph, h - yy)))
            canvas.alpha_composite(p, (x + xx, y + yy))
def full(canvas, name, x, y, w, h):
    t = templates.get(("FullResizeImageTemplate", name))
    if t is None: print("missing FR", name); return
    g = lambda k: int(t.findtext(k) or 0)
    T, M, B, L, C, R = g("TopHeight"), g("MiddleHeight"), g("BottomHeight"), g("LeftWidth"), g("MiddleWidth"), g("RightWidth")
    tx = t.find("Texture"); src = tex(tx.findtext("TextureName").strip())
    pieces = {k: pt(tx, k) for k in ("TopLeft","TopMiddle","TopRight","MiddleLeft","MiddleMiddle","MiddleRight","BottomLeft","BottomMiddle","BottomRight")}
    cols = [(L, x, L), (C, x + L, w - L - R), (R, x + w - R, R)]
    rws = [(T, y, T), (M, y + T, h - T - B), (B, y + h - B, B)]
    names = [["TopLeft","TopMiddle","TopRight"],["MiddleLeft","MiddleMiddle","MiddleRight"],["BottomLeft","BottomMiddle","BottomRight"]]
    for ri, (sh, dy, dh) in enumerate(rws):
        for ci, (sw, dx, dw) in enumerate(cols):
            sx, sy = pieces[names[ri][ci]]
            tile(canvas, src, (sx, sy, sx + sw, sy + sh), dx, dy, dw, dh)
def hres(canvas, name, x, y, w):
    t = templates.get(("HorizontalResizeImageTemplate", name))
    if t is None: print("missing HR", name); return
    g = lambda k: int(t.findtext(k) or 0)
    H, L, C, R = g("Height"), g("LeftWidth"), g("RepeatWidth"), g("RightWidth")
    tx = t.find("Texture"); src = tex(tx.findtext("TextureName").strip())
    (lx, ly), (cx, cy), (rx, ry) = pt(tx, "Left"), pt(tx, "Repeat"), pt(tx, "Right")
    if L: tile(canvas, src, (lx, ly, lx + L, ly + H), x, y, L, H)
    tile(canvas, src, (cx, cy, cx + C, cy + H), x + L, y, w - L - R, H)
    if R: tile(canvas, src, (rx, ry, rx + R, ry + H), x + w - R, y, R, H)
def area(canvas, name, x, y):
    t = templates.get(("ImageAreaTemplate", name))
    if t is None: print("missing IA", name); return
    src = tex(t.findtext("TextureName").strip()); (sx, sy) = pt(t, "TopLeft"); (w, h) = pt(t, "Size")
    if src: canvas.alpha_composite(src.crop((sx, sy, sx + w, sy + h)), (x, y))
# bitmap fonts: glyph rows end in a marker line (pixel at x=0 set); segments after the first are glyph extents
def load_font(path):
    f = Image.open(path).convert("RGBA"); fa = f.getchannel("A"); W2, H2 = f.size
    marks = [y for y in range(H2) if fa.getpixel((0, y)) > 128 and sum(1 for x in range(W2) if fa.getpixel((x, y)) > 128) > 60]
    out, code, top = {}, 33, 0
    for my in marks:
        xs = [x for x in range(W2) if fa.getpixel((x, my)) > 128]
        segs, s0, p = [], xs[0], xs[0]
        for xv in xs[1:]:
            if xv != p + 1: segs.append((s0, p)); s0 = xv
            p = xv
        segs.append((s0, p))
        for a0, e0 in segs[1:]:
            out[code] = f.crop((a0, top, e0 + 1, my)); code += 1
        top = my + 1
    return out
FONTS = {"arial14": load_font(UI + "fonts/Arial14.tga"), "title": load_font(UI + "fonts/title_24.tga")}
glyphs = FONTS["arial14"]
def text(canvas, s, x, y, w, color, centered=False, font="arial14", end=False):
    gl = FONTS.get(font, FONTS["arial14"]); space = 5 if font != "title" else 7
    data = s.encode("cp1252", "replace")
    width = sum(gl[b].size[0] if b in gl else space for b in data)
    if centered: x += max(0, (w - width) // 2)
    elif end: x += max(0, w - width)
    limit = x + w
    for b in data:
        g = gl.get(b)
        if g is None: x += space; continue
        if x + g.size[0] > limit + 1: break
        r, gg, bb, aa = g.split()
        tint = Image.merge("RGBA", (r.point(lambda v: v * color[0] // 255), gg.point(lambda v: v * color[1] // 255),
                                     bb.point(lambda v: v * color[2] // 255), aa.point(lambda v: v * color[3] // 255)))
        canvas.alpha_composite(tint, (x, y)); x += g.size[0]
panel = win.find("WindowTemplate")
W, H = int(panel.findtext("Width")), int(panel.findtext("Height"))
canvas = Image.new("RGBA", (W + 40, H + 40), (58, 66, 52, 255))  # grassy backdrop
sub = Image.new("RGBA", (W, H), (0, 0, 0, 0))
from_names = lambda name: int(name.split("_")[-1]) if name and name.startswith("cmgr_") and name.split("_")[-1].isdigit() else None
for e in panel:
    if e.tag == "FullResizeImageDef":
        x, y = pt(e, "Position"); full(sub, e.findtext("TemplateName").strip(), x, y, int(e.findtext("Width")), int(e.findtext("Height")))
    elif e.tag == "HorizontalResizeImageDef":
        x, y = pt(e, "Position"); hres(sub, e.findtext("TemplateName").strip(), x, y, int(e.findtext("Width")))
    elif e.tag == "ImageAreaDef":
        x, y = pt(e, "Position"); area(sub, e.findtext("TemplateName").strip(), x, y)
    elif e.tag == "LabelDef":
        x, y = pt(e, "Position"); c = e.find("Color"); color = tuple(int(c.findtext(k)) for k in "RGBA")
        adapter = (e.findtext("Adapter") or "").strip(); idx = from_names(adapter)
        s = labels.get(idx, "") if idx is not None else (e.findtext("Data") or "")
        if s: text(sub, s, x, y, int(e.findtext("Width")), color, (e.findtext("TextCentered") or "").strip() == "true",
                   (e.findtext("FontName") or "arial14").strip(), (e.findtext("EndAligned") or "").strip() == "true")
canvas.alpha_composite(sub, (20, 20))
canvas.convert("RGB").save(out)
print("ok", W, H, "glyphs", len(glyphs))
