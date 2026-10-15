#!/usr/bin/env python3
"""Render the DAoC region map textures (ui/maps/zNNN.dds) from the client's own data.

For each zone:
  terrain   the zone has a terrain heightmap (zones/ or an addon folder) and type 0:
            render a new map from terrain, ground texture, water, roads, trees and POIs.
  upscaled  everything else with a stock texture (dungeons, cities, sky cities, zones
            without terrain, area and region maps): Lanczos 256 -> 512 with a light unsharp
            mask. No other change.
  new       terrain present but no stock texture (type 0 only): a terrain render.

Output files keep the stock names (zNNN.dds, zNNN_NN.dds, rNNN.dds, no_map.dds) as 512x512
DXT1 DDS with the stock header layout, and maps-manifest.json lists every file with its
source and SHA-256 hashes.

The world database is copied (db, -wal and -shm) to a temporary directory and opened
read-only. Only static world tables are read; player, account, bot and companion data is
never touched.

Dependencies: Python 3 standard library, numpy, Pillow. No other packages.

Examples:
  render_client_maps.py --client <app dir> --world-db <opendaoc.sqlite3.db> --output <dir> \
      --zones 101,0,200 --preview-png
  render_client_maps.py --client <app dir> --world-db <opendaoc.sqlite3.db> --output <dir>
"""
import argparse
import datetime
import json
import math
import os
import re
import sys
import time

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import numpy as np  # noqa: E402
from PIL import Image, ImageDraw, ImageFilter, ImageFont  # noqa: E402

import client_map_data as D  # noqa: E402

TOOL_VERSION = 'v5'
OUT_SIZE = 512
SUPERSAMPLE = 2
FONT_CANDIDATES = ['/mnt/c/Windows/Fonts', 'C:/Windows/Fonts', 'C:\\Windows\\Fonts']
FONT_FALLBACK = {
    'georgia.ttf': ['LiberationSerif-Regular.ttf'],
    'georgiab.ttf': ['LiberationSerif-Bold.ttf'],
    'georgiai.ttf': ['LiberationSerif-Italic.ttf'],
}
FONT_FALLBACK_DIRS = ['/usr/share/fonts/truetype/liberation', '/usr/share/fonts/truetype/dejavu']
DEFAULT_LABELS = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'client_map_labels.json')

# Palette (sRGB)
ROAD_RED = (122, 23, 18)          # #7a1712
PARCHMENT = (239, 226, 191)       # #efe2bf
LABEL_FILL = (244, 234, 210)      # #f4ead2
LABEL_OUTLINE = (27, 20, 12)      # #1b140c
WATER_LABEL_FILL = (214, 232, 240)
WATER_LABEL_OUTLINE = (20, 44, 66)
ICON_DISC = (27, 20, 14)
FRAME_INK = (72, 52, 36)
GRID_INK = (30, 22, 14)
WATER_SHALLOW = (128, 176, 196)
WATER_DEEP = (58, 104, 136)
SHORE_DARK = (34, 66, 92)
FOREST_TINT = (0.70, 0.88, 0.66)
LAND_STOPS = [
    (0.00, (126, 150, 96)), (0.20, (150, 162, 102)), (0.42, (182, 170, 116)),
    (0.62, (182, 144, 100)), (0.78, (150, 116, 88)), (0.88, (128, 120, 112)),
    (0.95, (196, 194, 188)), (1.00, (244, 242, 236)),
]


# ---------------------------------------------------------------------------
# Fonts (system fonts are referenced by path, never copied)
# ---------------------------------------------------------------------------

_font_cache = {}


def _font_path(name, font_dir=None):
    dirs = ([font_dir] if font_dir else []) + FONT_CANDIDATES
    for d in dirs:
        p = os.path.join(d, name)
        if os.path.exists(p):
            return p
    for fb in FONT_FALLBACK.get(name, []):
        for d in FONT_FALLBACK_DIRS:
            p = os.path.join(d, fb)
            if os.path.exists(p):
                return p
    raise FileNotFoundError(f'no font for {name}; pass --font-dir')


def font(name, px, font_dir=None):
    key = (name, px, font_dir)
    if key not in _font_cache:
        _font_cache[key] = ImageFont.truetype(_font_path(name, font_dir), px)
    return _font_cache[key]


# ---------------------------------------------------------------------------
# Field helpers
# ---------------------------------------------------------------------------

def gaussian_grid(arr, sigma):
    if sigma <= 0:
        return arr.astype(np.float32)
    r = int(math.ceil(3 * sigma))
    x = np.arange(-r, r + 1, dtype=np.float32)
    k = np.exp(-(x * x) / (2 * sigma * sigma))
    k /= k.sum()
    a = np.pad(arr.astype(np.float32), r, mode='edge')
    a = np.apply_along_axis(lambda v: np.convolve(v, k, mode='valid'), 0, a)
    a = np.apply_along_axis(lambda v: np.convolve(v, k, mode='valid'), 1, a)
    return a


def upsample_vertices(arr, size, resample=Image.BICUBIC):
    """Grid index i sits at vertex position i (PIL centre convention shifted by half a cell)."""
    g = arr.shape[0]
    padded = np.pad(arr.astype(np.float32), ((0, 1), (0, 1)), mode='edge')
    im = Image.fromarray(padded, mode='F').resize((size, size), resample, box=(0.5, 0.5, g + 0.5, g + 0.5))
    return np.asarray(im, dtype=np.float32)


def aa_coverage(field):
    """Anti-aliased coverage of field > 0 from the signed distance (in canvas px)."""
    gy, gx = np.gradient(field)
    g = np.sqrt(gx * gx + gy * gy) + 1e-6
    dpx = field / g
    return np.clip(0.5 + 0.5 * dpx, 0.0, 1.0), dpx


# ---------------------------------------------------------------------------
# Layout helpers (output px space; drawing converts to canvas px)
# ---------------------------------------------------------------------------

class Placer:
    """Axis-aligned boxes in output px. Used for label and icon collision avoidance."""

    def __init__(self, size):
        self.size = size
        self.boxes = []

    def inside(self, box, margin=3):
        return box[0] >= margin and box[1] >= margin and box[2] <= self.size - margin and box[3] <= self.size - margin

    def free(self, box, pad=1.0, margin=3):
        if not self.inside(box, margin):
            return False
        for b in self.boxes:
            if box[0] < b[2] + pad and box[2] + pad > b[0] and box[1] < b[3] + pad and box[3] + pad > b[1]:
                return False
        return True

    def add(self, box):
        self.boxes.append(box)


def _box_at(cx, cy, w, h):
    return (cx - w / 2.0, cy - h / 2.0, cx + w / 2.0, cy + h / 2.0)


class Canvas:
    def __init__(self, out_size, font_dir=None):
        self.out = out_size
        self.S = out_size * SUPERSAMPLE
        self.u = self.S / out_size          # canvas px per output px
        self.img = None
        self.font_dir = font_dir

    def p(self, v):
        return v * self.u

    def text_size(self, text, fnt, stroke=0):
        d = ImageDraw.Draw(Image.new('RGB', (4, 4)))
        bb = d.textbbox((0, 0), text, font=fnt, stroke_width=stroke)
        return (bb[2] - bb[0]) / self.u, (bb[3] - bb[1]) / self.u, bb

    def draw_text(self, cx, cy, text, fnt, fill, outline_px, outline_rgb, rotate=0, alpha=255):
        """Draw text centred on output px (cx, cy) with an outline; returns its output-px box."""
        stroke = max(0, int(round(self.p(outline_px))))
        w_out, h_out, bb = self.text_size(text, fnt, stroke)
        pad = int(round(self.p(2)))
        tw = int(round(bb[2] - bb[0])) + 2 * pad
        th = int(round(bb[3] - bb[1])) + 2 * pad
        layer = Image.new('RGBA', (tw, th), (0, 0, 0, 0))
        ld = ImageDraw.Draw(layer)
        ox, oy = pad - bb[0], pad - bb[1]
        if stroke:
            ol = Image.new('RGBA', (tw, th), (0, 0, 0, 0))
            ImageDraw.Draw(ol).text((ox, oy), text, font=fnt, fill=outline_rgb + (235,),
                                    stroke_width=stroke, stroke_fill=outline_rgb + (235,))
            ol = ol.filter(ImageFilter.GaussianBlur(0.6))
            layer = Image.alpha_composite(layer, ol)
            ld = ImageDraw.Draw(layer)
        ld.text((ox, oy), text, font=fnt, fill=fill + (alpha,))
        if rotate:
            layer = layer.rotate(rotate, expand=True)   # +90 reads bottom-to-top, -90 top-to-bottom
            tw, th = layer.size
        x0 = int(round(self.p(cx) - tw / 2.0))
        y0 = int(round(self.p(cy) - th / 2.0))
        self._composite(layer, x0, y0)
        return (x0 / self.u, y0 / self.u, (x0 + tw) / self.u, (y0 + th) / self.u)

    def _composite(self, layer, x0, y0):
        """Alpha-composite layer at (x0, y0), clipped to the canvas."""
        lx0, ly0 = max(0, -x0), max(0, -y0)
        x1, y1 = min(self.S, x0 + layer.size[0]), min(self.S, y0 + layer.size[1])
        if x1 <= max(0, x0) or y1 <= max(0, y0):
            return
        part = layer.crop((lx0, ly0, lx0 + (x1 - max(0, x0)), ly0 + (y1 - max(0, y0))))
        dx, dy = max(0, x0), max(0, y0)
        region = self.img.crop((dx, dy, dx + part.size[0], dy + part.size[1]))
        self.img.paste(Image.alpha_composite(region, part), (dx, dy))


def text_box_out(canvas, text, fnt, outline_px, rotate=False):
    w, h, _ = canvas.text_size(text, fnt, max(0, int(round(canvas.p(outline_px)))))
    return (h, w) if rotate else (w, h)


# ---------------------------------------------------------------------------
# Icons
# ---------------------------------------------------------------------------

ICON_R = 5.8  # output px disc radius (11.6 px diameter)


def draw_icon(canvas, kind, cx, cy):
    """Light glyph on a dark disc with a light rim. Coordinates in output px."""
    u = canvas.u
    d = ImageDraw.Draw(canvas.img, 'RGBA')
    x, y = cx * u, cy * u
    r = ICON_R * u
    d.ellipse([x - r, y - r, x + r, y + r], fill=ICON_DISC + (236,), outline=PARCHMENT + (240,), width=max(2, int(round(u))))
    g = LABEL_FILL + (255,)
    w = max(2, int(round(1.1 * u)))

    def P(a, b):
        return (x + a * u, y + b * u)

    if kind == 'bind':  # standing stone
        d.rounded_rectangle([x - 1.9 * u, y - 3.0 * u, x + 1.9 * u, y + 3.0 * u], radius=1.0 * u, fill=g)
        d.line([P(-1.2, -1.2), P(1.2, -1.2)], fill=ICON_DISC + (200,), width=max(1, int(0.6 * u)))
    elif kind == 'trainer':  # sword
        d.line([P(-2.6, 2.6), P(2.6, -2.6)], fill=g, width=w + 1)
        d.line([P(-3.1, 0.3), P(0.3, 3.1)], fill=g, width=w + 1)
        d.ellipse([x - 0.8 * u, y + 2.2 * u, x + 0.8 * u, y + 3.8 * u], fill=g)
    elif kind == 'merchant':  # coin
        r2 = 2.8 * u
        d.ellipse([x - r2, y - r2, x + r2, y + r2], outline=g, width=w + 1)
        r3 = 0.9 * u
        d.ellipse([x - r3, y - r3, x + r3, y + r3], fill=g)
    elif kind == 'stable':  # horseshoe
        r2 = 2.8 * u
        d.arc([x - r2, y - r2 * 0.9, x + r2, y + r2 * 1.1], start=200, end=340, fill=g, width=w + 1)
    elif kind in ('porter', 'portal'):  # ring with centre dot
        r2 = 2.8 * u
        d.ellipse([x - r2, y - r2, x + r2, y + r2], outline=g, width=w + 1)
        r3 = 0.9 * u
        d.ellipse([x - r3, y - r3, x + r3, y + r3], fill=g)
    elif kind == 'dungeon':  # arch doorway
        d.rounded_rectangle([x - 2.3 * u, y - 2.9 * u, x + 2.3 * u, y + 3.0 * u], radius=2.0 * u, outline=g, width=w + 1)
        d.rectangle([x - 1.0 * u, y - 0.2 * u, x + 1.0 * u, y + 3.0 * u], fill=g)
    elif kind == 'keep':  # crenellated tower
        d.rectangle([x - 2.0 * u, y - 1.5 * u, x + 2.0 * u, y + 3.0 * u], fill=g)
        for a in (-2.0, -0.5, 1.0):
            d.rectangle([x + a * u, y - 3.0 * u, x + (a + 1.0) * u, y - 1.4 * u], fill=g)
    elif kind == 'exit':  # chevron pointing out of the zone
        d.polygon([P(-2.2, -2.6), P(2.6, 0.0), P(-2.2, 2.6), P(-0.9, 0.0)], fill=g)
    else:
        r3 = 1.4 * u
        d.ellipse([x - r3, y - r3, x + r3, y + r3], fill=g)


# Icon and label helpers for the terrain render (output px space)

ICON_BOX = 12.4       # output px box of one map icon
# Offsets tried when an icon collides: the spot, the 9 px and 7 px ring, then a 14 px ring so a
# dungeon sharing a keep's spot still gets its own icon.
ICON_OFFSETS = [(0, 0), (9, 0), (-9, 0), (0, 9), (0, -9), (7, 7), (-7, 7), (7, -7), (-7, -7),
                (14, 0), (-14, 0), (0, 14), (0, -14), (10, 10), (-10, 10), (10, -10), (-10, -10)]
NEIGHBOUR_SIDES = ('north', 'south', 'west', 'east')
EXTRA_OFFSETS = [(0, 0), (0, -8), (0, 8), (-12, 0), (12, 0)]


def _icon_candidates(cx, cy):
    return [(cx + dx, cy + dy) for dx, dy in ICON_OFFSETS]


def _neighbour_names(zones, zid):
    """{side: [names]} for the neighbour edge labels. Unnamed and 'dummy' zones are dropped and
    each name is listed once (the first side that has it wins)."""
    seen = set()
    out = {}
    nb = D.neighbours(zones, zid)
    for side in NEIGHBOUR_SIDES:
        names = []
        for oid in nb.get(side, []):
            name = (zones[oid].name or '').strip()
            if not name or 'dummy' in name.lower() or name.upper() in seen:
                continue
            seen.add(name.upper())
            names.append(name)
        out[side] = names
    return out


def _place_icon(cv, placer, kind, cx, cy):
    """Draw an icon at the first free offset from (cx, cy). Returns the drawn centre or None."""
    for ix, iy in _icon_candidates(cx, cy):
        ib = _box_at(ix, iy, ICON_BOX, ICON_BOX)
        if placer.free(ib):
            draw_icon(cv, kind, ix, iy)
            placer.add(ib)
            return ix, iy
    return None


def _label_beside(cv, placer, ix, iy, text, fnt, fill, outline_px, outline_rgb, gap=10.0):
    """Text next to an icon: right, left, below, above, then two lower-offset variants."""
    w, h = text_box_out(cv, text, fnt, outline_px)
    for dx, dy in [(gap + w / 2, 0), (-(gap + w / 2), 0), (0, gap), (0, -gap),
                   (gap + w / 2, 8), (-(gap + w / 2), 8)]:
        box = _box_at(ix + dx, iy + dy, w + 2, h + 1)
        if placer.free(box):
            cv.draw_text(ix + dx, iy + dy, text, fnt, fill, outline_px, outline_rgb)
            placer.add(box)
            return True
    return False


def _label_edge_exit(cv, placer, ix, iy, cx, cy, size, name, fnt):
    """Zone exit on the map edge: the name runs along the frame just inside the icon.
    cx, cy give the true position (edge side); ix, iy the drawn icon."""
    edge = min([('w', cx), ('e', size - cx), ('n', cy), ('s', size - cy)], key=lambda t: t[1])[0]
    w, h = text_box_out(cv, name, fnt, 1.0)
    if edge in ('w', 'e'):
        lx = ix + (1 if edge == 'w' else -1) * (ICON_BOX / 2 + 3.0 + h / 2)
        box = _box_at(lx, iy, h + 2, w + 2)
        if placer.free(box):
            cv.draw_text(lx, iy, name, fnt, LABEL_FILL, 1.0, LABEL_OUTLINE, rotate=90 if edge == 'w' else -90)
            placer.add(box)
            return True
        return False
    ly = iy + (1 if edge == 'n' else -1) * (ICON_BOX / 2 + 3.0 + h / 2)
    box = _box_at(ix, ly, w + 2, h + 2)
    if placer.free(box):
        cv.draw_text(ix, ly, name, fnt, LABEL_FILL, 1.0, LABEL_OUTLINE)
        placer.add(box)
        return True
    return False


def _wrap_label(cv, text, fnt, outline_px, max_w=90.0):
    """One line, or two lines split at the space nearest the middle when the line is too wide."""
    w, _ = text_box_out(cv, text, fnt, outline_px)
    spaces = [i for i, ch in enumerate(text) if ch == ' ']
    if w <= max_w or not spaces:
        return [text]
    mid = len(text) / 2.0
    cut = min(spaces, key=lambda i: abs(i - mid))
    return [text[:cut], text[cut + 1:]]


def _draw_extra(cv, placer, ex, ey, text, fnt, fill, outline_px, outline_rgb):
    """Curated label centred on (ex, ey) output px, trying small offsets. False when none is free."""
    lines = _wrap_label(cv, text, fnt, outline_px)
    sizes = [text_box_out(cv, ln, fnt, outline_px) for ln in lines]
    bw = max(w for w, _ in sizes)
    bh = sum(h for _, h in sizes) + (len(lines) - 1) * 1.0
    for dx, dy in EXTRA_OFFSETS:
        cx, cy = ex + dx, ey + dy
        box = _box_at(cx, cy, bw + 2, bh + 1)
        if not placer.free(box):
            continue
        y = cy - bh / 2.0
        for ln, (w, h) in zip(lines, sizes):
            cv.draw_text(cx, y + h / 2.0, ln, fnt, fill, outline_px, outline_rgb)
            y += h + 1.0
        placer.add(box)
        return True
    return False


# ---------------------------------------------------------------------------
# Terrain render
# ---------------------------------------------------------------------------

def _resample_256(arr, size, resample):
    """Resample a 256 texel field to size x size (float32 result)."""
    im = Image.fromarray(arr.astype(np.float32), mode='F').resize((size, size), resample)
    return np.asarray(im, dtype=np.float32)


def _missing_water(stock_mask, terrain_mask, max_frac=0.4, min_covered=0.5, min_area=160, min_residual=250):
    """Bool 256 mask of the stock water that the terrain water does not cover.

    terrain_mask is the thresholded terrain water at 256 texels. It is grown by two texels
    before the test. Stock components covering max_frac of the map or more are ignored
    (large blue areas on foggy stock maps). A component of at least min_area texels is added
    whole when less than min_covered of it falls in the grown terrain water; of a mostly
    covered component only the uncovered pieces of at least min_residual texels are added
    (Aldland's bay). The size floors keep out shore fringes and blue realm shields (about
    100-150 texels).
    """
    grown = D.dilate_mask(terrain_mask, 2)
    added = np.zeros(stock_mask.shape, bool)
    limit = max_frac * stock_mask.size
    for comp in D.water_components(stock_mask, min_area):
        area = int(comp.sum())
        if area >= limit:
            continue
        if int((comp & grown).sum()) < min_covered * area:
            added |= comp
            continue
        for piece in D.water_components(comp & ~grown, min_residual):
            added |= piece
    return added


def _stock_water_fields(added, size):
    """Canvas-resolution coverage and depth ramp for stock-only water (added: bool 256 mask).

    Coverage is the mask upsampled, blurred 1.2 texels and sharpened to an anti-aliased edge.
    Depth is the mask blurred 5 texels, so water deepens away from the shore.
    """
    up = _resample_256(added.astype(np.float32), size, Image.BILINEAR)
    cov = np.clip((gaussian_grid(up, 1.2 * size / 256.0) - 0.5) * 4.0 + 0.5, 0.0, 1.0)
    deep = _resample_256(gaussian_grid(added.astype(np.float32), 5.0), size, Image.BILINEAR)
    dep = np.clip((deep - 0.5) * 2.0, 0.0, 1.0)
    return cov.astype(np.float32), dep.astype(np.float32)


def _is_placeholder_zone(name):
    """Dummy and test zones from zones.dat. They have no real map."""
    low = (name or '').strip().lower()
    return 'dummy' in low or low.startswith('test')


def render_terrain(client, zinfo, zone, ground_rgb, stock_rgb, towns, pois, zones, size, font_dir=None,
                   extras=None):
    """Compose the v4 terrain map. extras: curated labels for this zone (load_extra_labels).
    Returns (PIL RGB image, metadata dict)."""
    cv = Canvas(size, font_dir)
    S, u = cv.S, cv.u
    grid = zone.grid
    k = S / grid                      # canvas px per heightmap cell
    loc_per_px = zinfo.extent_loc[0] / S

    # --- height, shading ----------------------------------------------------
    H = zone.height.astype(np.float32)
    hs = gaussian_grid(H, 0.75)
    height_s = upsample_vertices(hs, S)
    gy, gx = np.gradient(height_s)
    ex = 1.0
    dzdx = gx / loc_per_px * ex
    dzdy = gy / loc_per_px * ex
    nx, ny, nz = -dzdx, -dzdy, np.ones_like(dzdx)
    nn = np.sqrt(nx * nx + ny * ny + nz * nz)
    nx, ny, nz = nx / nn, ny / nn, nz / nn
    alt = math.radians(45.0)
    lx, ly, lz = -math.cos(alt) * math.sqrt(0.5), -math.cos(alt) * math.sqrt(0.5), math.sin(alt)
    shade = np.clip(nx * lx + ny * ly + nz * lz, 0, 1)
    relief = np.clip(1.0 + 1.1 * (shade - math.sin(alt)), 0.55, 1.35)

    # --- ground ----------------------------------------------------------------
    if ground_rgb is not None:
        g = ground_rgb.astype(np.float32)
        lum = g.mean(-1, keepdims=True)
        g = lum + (g - lum) * 1.08                           # saturation +8 %
        g = (g - 128.0) * 1.10 + 128.0                       # contrast +10 %
        g = np.clip(g, 0, 255)
        ground_src = 'zone-wide ground texture'
        ground_lum = g.mean(-1)
    else:
        lo = float(np.percentile(height_s, 1.0))
        hi = float(np.percentile(height_s, 99.5))
        t = np.clip((height_s - lo) / max(hi - lo, 1.0), 0, 1)
        g = np.zeros(t.shape + (3,), np.float32)
        ts = [s[0] for s in LAND_STOPS]
        for ch in range(3):
            g[..., ch] = np.interp(t, ts, [s[1][ch] for s in LAND_STOPS])
        ground_src = 'hypsometric fallback'
        ground_lum = g.mean(-1)
    # lift the darkest painted patches (mud, dirt) so they read as ground, not as holes
    g = 255.0 * np.power(np.clip(g, 0, 255) / 255.0, 0.92)
    g = auto_levels(g)
    ground_img = Image.fromarray(g.astype(np.uint8), 'RGB').filter(ImageFilter.GaussianBlur(0.8 * u))
    g = np.asarray(ground_img).astype(np.float32)
    snow = np.clip((ground_lum - 185.0) / 45.0, 0, 1)
    mult = 1.0 + (relief - 1.0) * (1.0 - 0.7 * snow)
    land = g * mult[..., None]

    # --- forest: soft multiply shading from tree density -----------------------
    tree_dens = tree_density_grid(zone)
    dens = np.clip(upsample_vertices(tree_dens, S, Image.BILINEAR), 0, 1)
    a = (np.clip(dens * 1.1, 0, 1) * 0.42)[..., None]
    land = land * (1 - a + a * np.array(FOREST_TINT, np.float32))

    # --- water: signed depth, anti-aliased contour at zero ----------------------
    depth_field = upsample_vertices(zone.water_depth_grid(), S, Image.BILINEAR)
    cov, dpx = aa_coverage(depth_field)
    river_img = Image.new('L', (S, S), 0)
    dr = ImageDraw.Draw(river_img)
    for r in zone.rivers:
        if r['type'].upper() == 'RIVER' and r['mid']:
            dr.line([(x * k, y * k) for x, y in r['mid']], fill=255, width=max(2, int(round(0.8 * k))), joint='curve')
    river = np.asarray(river_img.filter(ImageFilter.GaussianBlur(0.6))).astype(np.float32) / 255.0
    water = np.clip(np.maximum(cov, np.clip(river * 1.4, 0, 1)), 0, 1)
    # water the stock map paints where the terrain has none (lakes and sea at the zone edge)
    added_cov = np.zeros((S, S), np.float32)
    added_dep = np.zeros((S, S), np.float32)
    stock_water_texels = 0
    if stock_rgb is not None:
        terrain_256 = _resample_256(water, 256, Image.BOX) > 0.5
        added = _missing_water(D.stock_water_mask(stock_rgb), terrain_256)
        stock_water_texels = int(added.sum())
        if stock_water_texels:
            added_cov, added_dep = _stock_water_fields(added, S)
            water = np.maximum(water, added_cov)
    dep = np.maximum(np.clip(np.maximum(depth_field, 0) / 1200.0, 0, 1), added_dep)[..., None]
    wcol = np.array(WATER_SHALLOW, np.float32) * (1 - dep) + np.array(WATER_DEEP, np.float32) * dep
    wcol = wcol * (0.96 + 0.04 * mult[..., None])
    halo = np.clip(1.0 - np.clip(dpx, 0, None) / (1.5 * u), 0, 1) * (dpx > 0) * (depth_field > 0)
    halo = np.maximum(halo, added_cov * np.clip(1.0 - added_dep / 0.35, 0, 1))   # thin rim on stock-only edges
    wcol = wcol * (1 - 0.18 * halo[..., None]) + np.array(SHORE_DARK, np.float32) * 0.18 * halo[..., None]
    img = land * (1 - water[..., None]) + wcol * water[..., None]
    cv.img = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8), 'RGB').convert('RGBA')

    # --- sector grid (every 8192 loc, 12 % ink) -------------------------------
    grid_layer = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    gd = ImageDraw.Draw(grid_layer)
    step = 8192
    ticks = []
    ext = zinfo.extent_loc[0]
    for kk in range(1, ext // step):
        pos = kk * step / ext * S
        gd.line([(pos, 0), (pos, S)], fill=GRID_INK + (31,), width=max(1, int(round(u))))
        gd.line([(0, pos), (S, pos)], fill=GRID_INK + (31,), width=max(1, int(round(u))))
        ticks.append((kk * step / 1000, pos / u))
    cv.img = Image.alpha_composite(cv.img, grid_layer)

    # --- roads from the stock texture ------------------------------------------
    roads = D.extract_roads(stock_rgb) if stock_rgb is not None else []
    rscale = S / 256.0
    road_layer = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    rd = ImageDraw.Draw(road_layer)
    casing_w = max(2, int(round(4.75 * u)))
    road_w = max(2, int(round(2.75 * u)))
    lines_c = [[(x * rscale, y * rscale) for x, y in l] for l in roads]
    for pts in lines_c:
        rd.line(pts, fill=PARCHMENT + (232,), width=casing_w, joint='curve')
    for pts in lines_c:
        rd.line(pts, fill=ROAD_RED + (255,), width=road_w, joint='curve')
    cv.img = Image.alpha_composite(cv.img, road_layer)

    # --- tree canopy blobs at fixture positions where density is high ----------
    # Dark blobs with a faint shadow, never a bright dot, so they cannot read as map markers.
    trees = [f for f in zone.fixtures if f.get('class') == 'tree']
    cand = []
    for f in trees:
        row = min(grid - 1, max(0, int(f['y'] // D.CELL)))
        col = min(grid - 1, max(0, int(f['x'] // D.CELL)))
        if tree_dens[row, col] >= 0.5:
            cand.append(f)
    cand.sort(key=lambda f: ((f['x'] * 0.6180339887) % 1.0, f['y']))
    accepted = []
    min_space = 5.5
    for f in cand:
        ox_ = f['x'] / ext * size
        oy_ = f['y'] / ext * size
        if all(math.hypot(ox_ - ax, oy_ - ay) >= min_space for ax, ay, _ in accepted):
            accepted.append((ox_, oy_, f))
    tree_layer = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    td = ImageDraw.Draw(tree_layer)
    r = 1.7 * u
    for ox_, oy_, f in accepted:
        cx, cy = ox_ * u, oy_ * u
        snowy = ground_lum[min(S - 1, max(0, int(cy))), min(S - 1, max(0, int(cx)))] > 185.0
        canopy = (52, 64, 58, 200) if snowy else (26, 40, 24, 190)
        td.ellipse([cx - r + 0.5 * u, cy - r + 0.5 * u, cx + r + 0.5 * u, cy + r + 0.5 * u], fill=(0, 0, 0, 60))
        td.ellipse([cx - r, cy - r, cx + r, cy + r], fill=canopy)
    cv.img = Image.alpha_composite(cv.img, tree_layer)

    # --- labels and icons (collision-aware; the Placer forbids overlap, so draw order is priority)
    placer = Placer(size)
    placer.add((0, 0, size, 1.2))  # keep clear of the frame edge
    fnt_grid = font('georgia.ttf', int(round(7.5 * u)), font_dir)
    fnt_nb = font('georgiab.ttf', int(round(11.0 * u)), font_dir)
    fnt_keep = font('georgiab.ttf', int(round(13.0 * u)), font_dir)
    fnt_relic = font('georgiai.ttf', int(round(11.0 * u)), font_dir)
    fnt_town = font('georgiab.ttf', int(round(15.0 * u)), font_dir)
    fnt_dun = font('georgiai.ttf', int(round(12.0 * u)), font_dir)
    fnt_exit = font('georgiai.ttf', int(round(11.5 * u)), font_dir)
    fnt_extra = font('georgiab.ttf', int(round(12.0 * u)), font_dir)
    fnt_water = font('georgiai.ttf', int(round(12.0 * u)), font_dir)
    drawn = []           # every text label drawn on this map (grid ticks excluded)
    icons_placed = 0
    extra_drawn = 0

    # 1) neighbour zones on the four edges (stock style: "▲ NAME" on the edges)
    nb_drawn = []
    for side, names in _neighbour_names(zones, zinfo.id).items():
        for i, raw in enumerate(names):
            name = raw.upper()
            frac = (i + 1) / (len(names) + 1)
            if side in ('north', 'south'):
                cx = size * frac
                cy = 11.0 if side == 'north' else size - 11.0
                w, h = text_box_out(cv, name, fnt_nb, 1.0)
                box = _box_at(cx, cy, w + 9, h + 2)
                if placer.free(box):
                    # small triangle then text
                    tri_x = cx - w / 2 - 4
                    ts = 2.2
                    d = ImageDraw.Draw(cv.img, 'RGBA')
                    dirn = -1 if side == 'north' else 1
                    d.polygon([((tri_x) * u, (cy - ts * dirn * 0.8) * u), ((tri_x + 3.2) * u, (cy - ts * dirn * 0.8) * u),
                               ((tri_x + 1.6) * u, (cy + ts * dirn * 0.8) * u)], fill=LABEL_FILL + (255,))
                    cv.draw_text(cx + 2, cy, name, fnt_nb, LABEL_FILL, 1.0, LABEL_OUTLINE)
                    placer.add(box)
                    nb_drawn.append(name)
            else:
                cy = size * frac
                cx = 11.0 if side == 'west' else size - 11.0
                w, h = text_box_out(cv, name, fnt_nb, 1.0)
                box = _box_at(cx, cy, h + 2, w + 9)
                if placer.free(box):
                    cv.draw_text(cx, cy, name, fnt_nb, LABEL_FILL, 1.0, LABEL_OUTLINE, rotate=90 if side == 'west' else -90)
                    placer.add(box)
                    nb_drawn.append(name)
    drawn.extend(nb_drawn)

    # 2) keeps: icon plus name beside it, relic sub-line under the name
    for p in pois:
        if p['type'] != 'keep':
            continue
        cx, cy = to_out(zinfo, p['x'], p['y'], size)
        if not (4 <= cx <= size - 4 and 4 <= cy <= size - 4):
            continue
        name = p['name']
        relic = f"({p['relic']})" if p.get('relic') else None
        nw, nh = text_box_out(cv, name, fnt_keep, 2.0)
        rw, rh = text_box_out(cv, relic, fnt_relic, 1.4) if relic else (0.0, 0.0)
        bw = max(nw, rw)
        bh = nh + (rh + 1.0 if relic else 0.0)
        chosen = None
        for ix, iy in _icon_candidates(cx, cy):
            ib = _box_at(ix, iy, ICON_BOX, ICON_BOX)
            if not placer.free(ib):
                continue
            for lx, ly in [(ix, iy + 6.5 + bh / 2), (ix, iy - 6.5 - bh / 2),
                           (ix + 6.5 + bw / 2, iy), (ix - 6.5 - bw / 2, iy)]:
                lb = _box_at(lx, ly, bw + 4, bh + 2)
                if placer.free(lb):
                    chosen = (ix, iy, ib, lx, ly, lb)
                    break
            if chosen is not None:
                break
        if chosen is None:
            continue    # never draw a keep icon without its name
        ix, iy, ib, lx, ly, lb = chosen
        draw_icon(cv, 'keep', ix, iy)
        placer.add(ib)
        top = ly - bh / 2.0
        cv.draw_text(lx, top + nh / 2.0, name, fnt_keep, LABEL_FILL, 2.0, LABEL_OUTLINE)
        drawn.append(name)
        if relic:
            cv.draw_text(lx, top + nh + 1.0 + rh / 2.0, relic, fnt_relic, LABEL_FILL, 1.4, LABEL_OUTLINE)
            drawn.append(relic)
        placer.add(lb)
        icons_placed += 1

    # 3) towns with one icon per service type in a row beneath the name
    town_icons = 0
    placed_pois = set()
    for t in towns:
        cx, cy = to_out(zinfo, t['x'], t['y'], size)
        if not (4 <= cx <= size - 4 and 4 <= cy <= size - 4):
            continue
        members = t['members']
        kinds = []
        for p in members:
            if p['type'] not in kinds:
                kinds.append(p['type'])
        lw, lh = text_box_out(cv, t['name'], fnt_town, 2.0)
        row_w = len(kinds) * 14.0 - 2.4 if kinds else 0
        group_w = max(lw, row_w) + 6
        group_h = lh + (14.0 if kinds else 0) + 4
        placed = False
        for ox_, oy_ in [(0, 0), (0, -group_h / 2 - 9), (0, group_h / 2 + 9), (-group_w / 2 - 9, 0),
                         (group_w / 2 + 9, 0), (-group_w / 2 - 6, -group_h / 2 - 6),
                         (group_w / 2 + 6, -group_h / 2 - 6), (-group_w / 2 - 6, group_h / 2 + 6),
                         (group_w / 2 + 6, group_h / 2 + 6)]:
            gx_, gy_ = cx + ox_, cy + oy_
            top = gy_ - group_h / 2
            label_box = _box_at(gx_, top + lh / 2, lw + 4, lh + 2)
            icon_boxes = [_box_at(gx_ - row_w / 2 + 5.8 + i * 14.0, top + lh + 2 + 7.0, 12.4, 12.4) for i in range(len(kinds))]
            if placer.free(label_box) and all(placer.free(b) for b in icon_boxes):
                cv.draw_text(gx_, top + lh / 2, t['name'], fnt_town, LABEL_FILL, 2.0, LABEL_OUTLINE)
                placer.add(label_box)
                drawn.append(t['name'])
                for i, kind in enumerate(kinds):
                    draw_icon(cv, kind, gx_ - row_w / 2 + 5.8 + i * 14.0, top + lh + 2 + 7.0)
                    placer.add(icon_boxes[i])
                    town_icons += 1
                placed = True
                break
        if not placed:
            # fall back to the name alone
            for ox_, oy_ in [(0, 0), (0, -lh), (0, lh), (-lw, 0), (lw, 0)]:
                box = _box_at(cx + ox_, cy + oy_, lw + 4, lh + 2)
                if placer.free(box):
                    cv.draw_text(cx + ox_, cy + oy_, t['name'], fnt_town, LABEL_FILL, 2.0, LABEL_OUTLINE)
                    placer.add(box)
                    drawn.append(t['name'])
                    break
        for p in members:
            placed_pois.add(id(p))

    # 4) dungeons and zone exits: icon, then the name (edge exits along the frame)
    for p in pois:
        if p['type'] not in ('dungeon', 'exit'):
            continue
        cx, cy = to_out(zinfo, p['x'], p['y'], size)
        if not (4 <= cx <= size - 4 and 4 <= cy <= size - 4):
            continue
        spot = _place_icon(cv, placer, p['type'], cx, cy)
        if spot is None:
            continue
        ix, iy = spot
        icons_placed += 1
        name = p.get('name')
        if not name:
            continue
        if p['type'] == 'dungeon':
            if _label_beside(cv, placer, ix, iy, name, fnt_dun, LABEL_FILL, 1.6, LABEL_OUTLINE):
                drawn.append(name)
            continue
        if p.get('target_kind') not in ('city', 'overworld'):
            continue
        if min(cx, size - cx, cy, size - cy) < 14:
            if _label_edge_exit(cv, placer, ix, iy, cx, cy, size, name, fnt_nb):
                drawn.append(name)
        else:
            text = f'to {name}'
            if _label_beside(cv, placer, ix, iy, text, fnt_exit, LABEL_FILL, 1.5, LABEL_OUTLINE):
                drawn.append(text)

    # 5) curated labels from client_map_labels.json (stock 256-texel units to output px)
    seen = {s.lower() for s in drawn}
    for e in extras or []:
        name = e['name']
        if name.lower() in seen:
            continue
        ex_, ey_ = e['x'] * size / 256.0, e['y'] * size / 256.0
        if e['kind'] == 'water':
            ok = _draw_extra(cv, placer, ex_, ey_, name, fnt_water, WATER_LABEL_FILL, 1.6, WATER_LABEL_OUTLINE)
        else:
            ok = _draw_extra(cv, placer, ex_, ey_, name, fnt_extra, LABEL_FILL, 1.8, LABEL_OUTLINE)
        if ok:
            drawn.append(name)
            seen.add(name.lower())
            extra_drawn += 1

    # 6) remaining bare icons (bind, trainer, merchant, stable, porter, portal)
    for p in pois:
        if p['type'] not in ('bind', 'trainer', 'merchant', 'stable', 'porter', 'portal') or id(p) in placed_pois:
            continue
        cx, cy = to_out(zinfo, p['x'], p['y'], size)
        if not (4 <= cx <= size - 4 and 4 <= cy <= size - 4):
            continue
        if _place_icon(cv, placer, p['type'], cx, cy) is not None:
            icons_placed += 1

    # 7) grid tick labels last (tiny, along the top and left edges); skip any that collide
    tick_drawn = []
    for kl, pos_out in ticks:
        label = f'{int(kl)}k'
        w, h = text_box_out(cv, label, fnt_grid, 0.6)
        cx = pos_out
        box = _box_at(cx, 6.0, w + 2, h + 1)
        if placer.free(box, pad=0.5, margin=1):
            cv.draw_text(cx, 6.0, label, fnt_grid, (250, 244, 228), 0.6, GRID_INK, alpha=235)
            placer.add(box)
            tick_drawn.append(label)
        box2 = _box_at(6.0, pos_out, w + 1, h + 2)
        if placer.free(box2, pad=0.5, margin=1):
            cv.draw_text(6.0, pos_out, label, fnt_grid, (250, 244, 228), 0.6, GRID_INK, alpha=235, rotate=90)
            placer.add(box2)
            tick_drawn.append(label)

    # --- frame ------------------------------------------------------------------
    fd = ImageDraw.Draw(cv.img, 'RGBA')
    w = max(2, int(round(1.5 * u)))
    fd.rectangle([0, 0, S - 1, S - 1], outline=FRAME_INK + (255,), width=w)

    out = cv.img.convert('RGB').resize((size, size), Image.LANCZOS)
    meta = {
        'ground': ground_src,
        'roads': len(roads),
        'trees_drawn': len(accepted),
        'towns': len(towns),
        'town_icons': town_icons,
        'icons': icons_placed,
        'neighbour_labels': nb_drawn,
        'grid_ticks': tick_drawn,
        'labels_drawn': sorted(drawn),
        'extra_labels': extra_drawn,
        'stock_water_texels': stock_water_texels,
    }
    return out, meta


def auto_levels(rgb, lo_out=34.0, hi_out=226.0):
    """Brightness and contrast only: stretch the 1st..99th percentile of luminance to
    lo_out..hi_out and shift each channel by the same amount, so hue and chroma are unchanged."""
    lum = rgb.mean(-1, keepdims=True)
    lo, hi = np.percentile(lum, [1.0, 99.0])
    if hi - lo < 8:
        return rgb
    lum_new = (lum - lo) * ((hi_out - lo_out) / (hi - lo)) + lo_out
    return np.clip(rgb + (lum_new - lum), 0, 255)


def tree_density_grid(zone):
    """Canopy density per cell from the tree fixtures (count blurred, scaled to 0..1 at the peak)."""
    counts = np.zeros((zone.grid, zone.grid), np.float32)
    for f in zone.fixtures:
        if f.get('class') == 'tree':
            row = min(zone.grid - 1, max(0, int(f['y'] // D.CELL)))
            col = min(zone.grid - 1, max(0, int(f['x'] // D.CELL)))
            counts[row, col] += 1.0
    blur = gaussian_grid(counts, 2.0)
    peak = float(blur.max())
    return blur / peak if peak > 0 else blur


def to_out(zinfo, wx, wy, size):
    ox, oy = zinfo.origin_loc
    ext = zinfo.extent_loc[0]
    return ((wx - ox) / ext * size, (wy - oy) / ext * size)


# ---------------------------------------------------------------------------
# Upscale and DXT1 output
# ---------------------------------------------------------------------------

def upscale_stock(stock_rgb, size=OUT_SIZE):
    im = stock_rgb.resize((size, size), Image.LANCZOS)
    return im.filter(ImageFilter.UnsharpMask(radius=1.0, percent=80, threshold=2))


def save_dxt1(img, path):
    """RGB image -> DXT1 DDS with the stock header layout (linear size, no mips)."""
    import struct
    img = img.convert('RGB')
    img.save(path, pixel_format='DXT1')
    data = bytearray(open(path, 'rb').read())
    w, h = img.size
    linear = max(1, (w + 3) // 4) * max(1, (h + 3) // 4) * 8
    flags = struct.unpack_from('<I', data, 8)[0] | 0x80000
    struct.pack_into('<I', data, 8, flags)
    struct.pack_into('<I', data, 20, linear)
    struct.pack_into('<I', data, 28, 0)
    open(path, 'wb').write(bytes(data))


# ---------------------------------------------------------------------------
# Batch
# ---------------------------------------------------------------------------

def target_resolver(zones, db=None):
    """Callable(target_region) -> (kind, name) for zone-exit classification."""
    by_region = {}
    for zid, zi in zones.items():
        by_region.setdefault(zi.region, []).append(zi)

    def resolve(tr):
        cands = list(by_region.get(tr, []))
        if tr in zones and zones[tr] not in cands:
            cands.append(zones[tr])
        if any(z.housing for z in cands):
            return 'housing', None
        types = [z.type for z in cands]
        if D.ZONE_TYPE_DUNGEON in types:
            kind = 'dungeon'
        elif D.ZONE_TYPE_CITY in types:
            kind = 'city'
        else:
            kind = 'overworld'
        name = next((z.name for z in cands if z.name), None)
        if name is None and db is not None:
            name = db.zone_name(tr)
        return kind, name
    return resolve


def build_pois(db, zinfo, zones):
    data = db.zone_pois(zinfo, target_resolver(zones, db))
    towns = data['towns']
    pois = data['pois']
    for p in pois:
        p.setdefault('name', p['type'])
    # members of each town: service POIs whose nearest named area is that town
    service = [p for p in pois if p['type'] in ('merchant', 'trainer', 'stable', 'porter')]
    out_towns = []
    for t in towns:
        out_towns.append({'name': t['name'], 'x': t['x'], 'y': t['y'], 'r': t['r'], 'members': []})
    for p in service:
        best = None
        for i, t in enumerate(towns):
            d = math.hypot(p['x'] - t['x'], p['y'] - t['y'])
            if d <= max(t['r'], 1) + 1500 and (best is None or d < best[0]):
                best = (d, i)
        if best is not None:
            out_towns[best[1]]['members'].append(p)
            p['town'] = towns[best[1]]['name']
    out_towns = [t for t in out_towns if t['members'] or True]
    return out_towns, pois


def poi_json(zinfo, towns, pois):
    def pt(x, y):
        ox, oy = zinfo.origin_loc
        return {'world': [x, y], 'local': [x - ox, y - oy],
                'texel_256': [int((x - ox) // D.CELL), int((y - oy) // D.CELL)],
                'px_512': [round((x - ox) / 128.0, 1), round((y - oy) / 128.0, 1)]}
    return {
        'towns': [{'name': t['name'], **pt(t['x'], t['y']), 'members': len(t['members'])} for t in towns],
        'pois': [{'type': p['type'], 'name': p.get('name'), 'count': p.get('count'),
                  'source': p.get('source'), 'target_region': p.get('target_region'),
                  'target_kind': p.get('target_kind'), 'town': p.get('town'),
                  **pt(p['x'], p['y'])} for p in pois],
    }


def render_zone_outputs(client, zid, zones, db, stock_path, out_dir, size, preview, font_dir, ui_dir, extras=None):
    zinfo = zones[zid]
    label, zdir = D.find_zone_archives(client, zid)
    arch = D.zone_archives(zdir, zid)
    zone = D.TerrainZone(zinfo, arch['dat'], arch['csv'])
    ground, layer = None, None
    ground, layer = D.load_ground_rgb(client, arch['ter'], size * SUPERSAMPLE)
    stock = D.read_stock_rgb(stock_path) if stock_path and os.path.exists(stock_path) else None
    towns, pois = build_pois(db, zinfo, zones) if db is not None else ([], [])
    img, meta = render_terrain(client, zinfo, zone, ground, stock, towns, pois, zones, size, font_dir,
                               extras=extras)
    return img, meta, towns, pois, label


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--client', required=True, help='client app directory (contains zones/ and ui/)')
    ap.add_argument('--world-db', required=True, help='world database (copied and opened read-only)')
    ap.add_argument('--output', required=True, help='new output directory (created if missing)')
    ap.add_argument('--zones', help='comma-separated zone ids to process (default: all)')
    ap.add_argument('--size', type=int, default=OUT_SIZE, help='output edge in px (default 512)')
    ap.add_argument('--preview-png', action='store_true', help='also write a PNG preview of each output')
    ap.add_argument('--font-dir', help='directory with georgia.ttf and georgiab.ttf / georgiai.ttf')
    ap.add_argument('--include-new', action='store_true',
                    help='also write maps for zones that have no stock map (not installed by default)')
    ap.add_argument('--terrain-all', action='store_true',
                    help='keep terrain renders that fall back to height tint; default upscales the stock map')
    ap.add_argument('--labels', default=DEFAULT_LABELS,
                    help='curated label JSON (default: client_map_labels.json next to this script; missing is fine)')
    args = ap.parse_args(argv)

    t0 = time.time()
    client = args.client
    ui_dir = D.ci_path(client, 'ui', 'maps')
    if ui_dir is None:
        raise SystemExit('ui/maps not found under the client directory')
    if os.path.exists(args.output) and os.listdir(args.output):
        raise SystemExit(f'output directory is not empty: {args.output}')
    os.makedirs(args.output, exist_ok=True)

    try:
        extra_labels = D.load_extra_labels(args.labels)
    except ValueError as exc:
        raise SystemExit(f'bad label file: {exc}')
    zones = D.read_zones_table(client)
    only = None
    if args.zones:
        only = {int(z) for z in args.zones.split(',') if z.strip()}

    stock_files = {n.lower(): n for n in os.listdir(ui_dir) if n.lower().endswith('.dds')}
    zone_ids = sorted({int(m.group(1)) for n in stock_files for m in [re.fullmatch(r'z(\d{3})\.dds', n)] if m}
                      | {zid for zid, zi in zones.items() if zi.enabled and zi.type == 0})
    if only is not None:
        zone_ids = [z for z in zone_ids if z in only]

    db = D.WorldDb(args.world_db)
    entries, skipped = [], []
    counts = {'terrain': 0, 'upscaled': 0, 'new': 0, 'skipped': 0, 'failed': 0}
    try:
        for zid in zone_ids:
            name = f'z{zid:03d}.dds'
            stock_name = stock_files.get(name)
            stock_path = os.path.join(ui_dir, stock_name) if stock_name else None
            zi = zones.get(zid)
            terrain_ok = False
            label = zdir = None
            if zi is not None and zi.enabled and zi.type == D.ZONE_TYPE_NORMAL:
                label, zdir = D.find_zone_archives(client, zid)
                terrain_ok = zdir is not None and label is not None
            if terrain_ok and stock_path is None and not args.include_new:
                skipped.append({'file': name, 'zone': zid, 'reason': 'no stock map; pass --include-new'})
                counts['skipped'] += 1
                continue
            if terrain_ok and stock_path is None and _is_placeholder_zone(zi.name):
                skipped.append({'file': name, 'zone': zid, 'reason': 'placeholder zone'})
                counts['skipped'] += 1
                continue
            if terrain_ok and not args.terrain_all:
                # Without a zone-wide ground texture the render is a flat height tint that
                # reads worse than the painted stock map, so keep (upscale) the stock art.
                arch = D.zone_archives(zdir, zid)
                terrain_ok = D.load_ground_rgb(client, arch['ter'], 64)[0] is not None or stock_path is None
            if terrain_ok:
                t1 = time.time()
                try:
                    img, meta, towns, pois, _label = render_zone_outputs(
                        client, zid, zones, db, stock_path, args.output, args.size, args.preview_png,
                        args.font_dir, ui_dir, extras=extra_labels.get(zid))
                except Exception as exc:  # one bad zone must not stop the batch
                    skipped.append({'file': name, 'zone': zid, 'reason': f'render failed: {exc}'})
                    counts['failed'] += 1
                    print(f'{name} FAILED: {exc}', flush=True)
                    continue
                source = 'terrain' if stock_path else 'new'
                out_path = os.path.join(args.output, name)
                save_dxt1(img, out_path)
                if args.preview_png:
                    img.save(os.path.join(args.output, f'z{zid:03d}_preview.png'))
                entries.append(_entry(args, name, zid, zi, source, label, stock_path, out_path,
                                      meta, towns, pois, time.time() - t1))
                counts[source] += 1
                print(f'{name} {source} {time.time() - t1:.2f}s', flush=True)
            elif stock_path is not None:
                out_path = os.path.join(args.output, name)
                upscaled = upscale_stock(D.read_stock_rgb(stock_path), args.size)
                save_dxt1(upscaled, out_path)
                reason = 'dungeon/city/sky-city or no terrain archive'
                if zdir is not None:
                    reason = 'no zone-wide ground texture; stock art kept'
                if zi is None:
                    reason = 'not in zones.dat'
                entries.append(_entry(args, name, zid, zi, 'upscaled', None, stock_path, out_path,
                                      {'reason': reason}, [], [], 0.0))
                counts['upscaled'] += 1
            else:
                skipped.append({'file': name, 'zone': zid, 'reason': 'no terrain and no stock texture'})
                counts['skipped'] += 1

        if only is None:
            for lower, original in sorted(stock_files.items()):
                if re.fullmatch(r'z\d{3}\.dds', lower):
                    continue
                stock_path = os.path.join(ui_dir, original)
                if not _is_square_256(stock_path):
                    skipped.append({'file': original, 'reason': 'not a 256x256 stock map'})
                    counts['skipped'] += 1
                    continue
                out_path = os.path.join(args.output, original)
                upscaled = upscale_stock(D.read_stock_rgb(stock_path), args.size)
                save_dxt1(upscaled, out_path)
                entries.append(_entry(args, original, None, None, 'upscaled', None, stock_path, out_path,
                                      {'reason': 'area or region map'}, [], [], 0.0))
                counts['upscaled'] += 1
    finally:
        db.close()

    manifest = {
        'generator': 'source/server/tools/render_client_maps.py',
        'version': TOOL_VERSION,
        'generated_utc': datetime.datetime.now(datetime.timezone.utc).replace(microsecond=0, tzinfo=None).isoformat() + 'Z',
        'output_size': args.size,
        'format': 'DXT1 DDS, linear size, no mip levels (stock header layout)',
        'render_scale': f'{SUPERSAMPLE}x supersample then Lanczos',
        'grid': 'zone-local loc, every 8192 loc (8k, 16k, ...); world origin in each entry',
        'database': 'world database copied and opened read-only; static world tables only',
        'counts': counts,
        'archive_warnings': D.WARNINGS,
        'runtime_seconds': round(time.time() - t0, 1),
        'entries': entries,
        'skipped': skipped,
    }
    with open(os.path.join(args.output, 'maps-manifest.json'), 'w', encoding='utf-8') as f:
        json.dump(manifest, f, indent=1, ensure_ascii=False)
    print(json.dumps(counts), f'runtime {time.time() - t0:.1f}s')
    return 0


def _is_square_256(path):
    try:
        with Image.open(path) as im:
            return im.size == (256, 256)
    except OSError:
        return False


def _entry(args, name, zid, zi, source, addon_label, stock_path, out_path, meta, towns, pois, seconds):
    e = {
        'file': name,
        'zone': zid,
        'name': zi.name if zi else None,
        'source': source,
        'addon_folder': addon_label or 'base',
        'stock_file': os.path.basename(stock_path) if stock_path else None,
        'stock_sha256': D.sha256_file(stock_path) if stock_path else 'absent',
        'output_sha256': D.sha256_file(out_path),
        'output_size': args.size,
        'seconds': round(seconds, 2),
    }
    if zi is not None:
        e['origin_loc'] = list(zi.origin_loc)
        e['extent_loc'] = list(zi.extent_loc)
    if source in ('terrain', 'new'):
        e['ground'] = meta.get('ground')
        e['roads'] = meta.get('roads')
        e['trees'] = meta.get('trees_drawn')
        e['pois'] = len(pois)
        e['towns'] = len(towns)
        e['neighbour_labels'] = meta.get('neighbour_labels')
        e['labels_drawn'] = meta.get('labels_drawn')
        e['extra_labels'] = meta.get('extra_labels')
        e['stock_water_texels'] = meta.get('stock_water_texels')
    else:
        e['reason'] = meta.get('reason')
    return e


if __name__ == '__main__':
    sys.exit(main())
