#!/usr/bin/env python3
"""Read-only client and world data access for render_client_maps.py.

Everything here reads the DAoC client install (zones, addon folders, ui/maps) and a
copy of the world database. Nothing is written to the client or to the live database.

Dependencies: Python 3 standard library, numpy, Pillow.
"""
import collections
import hashlib
import math
import os
import re
import shutil
import sqlite3
import struct
import tempfile
import zlib

import numpy as np
from PIL import Image

CELL = 256            # zone-local loc units per heightmap cell and per stock texel (256 texel grid)
SECTOR = 8192         # zone-local loc units per sector; region offsets and zone sizes are in sectors
ZONE_TYPE_NORMAL = 0
ZONE_TYPE_CITY = 1
ZONE_TYPE_DUNGEON = 2
ZONE_TYPE_SKYCITY = 4
# Addon folders searched after the base zones/ folder (base zone data always wins).
ADDON_FOLDERS = ('newtowns', 'frontiers', 'phousing', 'Tutorial')

# Static NPC class names that become map icons. Bots and companions live in other tables.
MERCHANT_CLASSES = frozenset({
    'DOL.GS.GameMerchant', 'DOL.GS.Scripts.OFMerchant', 'DOL.GS.Scripts.OFMerchantHome',
    'DOL.GS.BuffMerchant', 'DOL.GS.GameBountyMerchant', 'DOL.GS.Scripts.RPTradeInMerchant',
})
PORTER_CLASSES = frozenset({
    'DOL.GS.Scripts.LiveTeleporter', 'DOL.GS.Scripts.OFTeleporter', 'DOL.GS.SimpleTeleporter',
    'DOL.GS.MidgardSITeleporter', 'DOL.GS.AlbionSITeleporter', 'DOL.GS.HiberniaSITeleporter',
    'DOL.GS.Scripts.InlandTeleporter',
})
STABLE_CLASSES = frozenset({'DOL.GS.GameStableMaster'})
TRAINER_PREFIX = 'DOL.GS.Trainer.'
TRAINER_EXTRA = frozenset({'DOL.GS.Scripts.AtlasTrainer'})


# ---------------------------------------------------------------------------
# Hashing and paths
# ---------------------------------------------------------------------------

def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def ci_path(base, *parts):
    """Case-insensitive path resolution (the client ships mixed-case folder names)."""
    cur = base
    for part in parts:
        if cur is None:
            return None
        exact = os.path.join(cur, part)
        if os.path.exists(exact):
            cur = exact
            continue
        try:
            match = next(n for n in os.listdir(cur) if n.lower() == part.lower())
        except (StopIteration, FileNotFoundError, NotADirectoryError):
            return None
        cur = os.path.join(cur, match)
    return cur


# ---------------------------------------------------------------------------
# MPAK v2 archives (same format as tools/asset-tool/archive.py)
# ---------------------------------------------------------------------------

class Entry:
    __slots__ = ('name', 'data')

    def __init__(self, name, data):
        self.name = name
        self.data = data


def _inflate(data, limit=128 * 1024 * 1024):
    decoder = zlib.decompressobj()
    out = decoder.decompress(data, limit + 1)
    if len(out) > limit or not decoder.eof or decoder.unused_data:
        raise ValueError('oversized or truncated compressed stream')
    return out


def read_mpk(blob):
    """Return a list of Entry for an MPAK v2 archive.

    Strict checks as in archive.py. Some shipped archives (for example
    zones/Zone000/dat000.mpk) restart their expanded-memory pointers per entry, which
    the strict reader rejects. For those the tolerant path skips only that pointer check;
    every payload is still CRC- and size-checked.
    """
    if blob[:5] != b'MPAK\x02' or len(blob) < 21:
        raise ValueError('not an MPAK v2 archive')
    checksum, ds, ns, count = struct.unpack('<4I', bytes(v ^ i for i, v in enumerate(blob[5:21])))
    if not (0 < count <= 100000 and ds > 0 and ns > 0 and 21 + ns + ds <= len(blob)):
        raise ValueError('invalid MPK header')
    directory = blob[21 + ns:21 + ns + ds]
    if zlib.crc32(directory) & 0xffffffff != checksum:
        raise ValueError('MPK directory CRC mismatch')
    rows = _inflate(directory)
    if len(rows) != count * 284:
        raise ValueError('MPK directory size mismatch')
    payload = blob[21 + ns + ds:]
    entries, names = [], set()
    expanded = compressed = 0
    for i in range(count):
        row = rows[i * 284:(i + 1) * 284]
        filename = row[:256].split(b'\0')[0].decode('latin1')
        if not filename or filename.lower() in names:
            raise ValueError('duplicate or empty MPK filename')
        names.add(filename.lower())
        _stamp, _flags, memory, size, offset, length, crc = struct.unpack('<7I', row[256:])
        if memory != expanded:
            return _read_mpk_tolerant(blob, rows, count, payload)
        if offset != compressed or offset + length > len(payload):
            raise ValueError(f'{filename}: invalid compressed offset')
        compressed_bytes = payload[offset:offset + length]
        if zlib.crc32(compressed_bytes) & 0xffffffff != crc:
            raise ValueError(f'{filename}: compressed CRC mismatch')
        data = _inflate(compressed_bytes)
        if len(data) != size:
            raise ValueError(f'{filename}: expanded size mismatch')
        entries.append(Entry(filename, data))
        expanded += size
        compressed += length
    return entries


def _read_mpk_tolerant(blob, rows, count, payload):
    entries = []
    for i in range(count):
        row = rows[i * 284:(i + 1) * 284]
        filename = row[:256].split(b'\0')[0].decode('latin1')
        _stamp, _flags, _memory, size, offset, length, crc = struct.unpack('<7I', row[256:])
        compressed_bytes = payload[offset:offset + length]
        if zlib.crc32(compressed_bytes) & 0xffffffff != crc:
            raise ValueError(f'{filename}: compressed CRC mismatch')
        data = _inflate(compressed_bytes)
        if len(data) != size:
            raise ValueError(f'{filename}: expanded size mismatch')
        entries.append(Entry(filename, data))
    return entries


def read_mpk_file(path):
    with open(path, 'rb') as f:
        return read_mpk(f.read())


# ---------------------------------------------------------------------------
# zones.dat and region metadata
# ---------------------------------------------------------------------------

class ZoneInfo:
    def __init__(self, zid, d, region_flags):
        self.id = zid
        self.name = d.get('name', f'zone{zid}').strip()
        self.region = int(d.get('region', '0') or 0)
        self.enabled = d.get('enabled', '1') != '0'
        self.type = int(d.get('type', '0') or 0)
        self.width_sectors = int(d.get('width', '8') or 8)
        self.height_sectors = int(d.get('height', '8') or 8)
        self.off_x_sectors = int(d.get('region_offset_x', '0') or 0)
        self.off_y_sectors = int(d.get('region_offset_y', '0') or 0)
        self.housing = region_flags.get(self.region, {}).get('phousing') == '1'

    @property
    def origin_loc(self):
        return (self.off_x_sectors * SECTOR, self.off_y_sectors * SECTOR)

    @property
    def extent_loc(self):
        return (self.width_sectors * SECTOR, self.height_sectors * SECTOR)

    def __repr__(self):
        return (f'Z{self.id:03d} {self.name!r} region={self.region} type={self.type} '
                f'origin_sectors=({self.off_x_sectors},{self.off_y_sectors}) '
                f'size_sectors={self.width_sectors}x{self.height_sectors}')


def _parse_sections(text):
    sections = collections.OrderedDict()
    cur = None
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith(';'):
            continue
        if line.startswith('[') and line.endswith(']'):
            cur = line[1:-1].strip().lower()
            sections.setdefault(cur, {})
            continue
        if '=' in line and cur is not None:
            k, v = line.split('=', 1)
            sections[cur][k.strip().lower()] = v.strip()
    return sections


def read_zones_table(client):
    """Parse zones/zones.mpk (zones.dat) into {zone_id: ZoneInfo}."""
    mpk = ci_path(client, 'zones', 'zones.mpk')
    if mpk is None:
        raise FileNotFoundError('zones/zones.mpk not found under client')
    entries = read_mpk_file(mpk)
    text = next(e.data for e in entries if e.name.lower() == 'zones.dat').decode('utf-8-sig', 'replace')
    sections = _parse_sections(text)
    region_flags = {}
    for name, d in sections.items():
        m = re.fullmatch(r'region(\d+)', name)
        if m:
            region_flags[int(m.group(1))] = d
    zones = {}
    for name, d in sections.items():
        m = re.fullmatch(r'zone(\d+)', name)
        if m:
            zid = int(m.group(1))
            zones[zid] = ZoneInfo(zid, d, region_flags)
    return zones


def neighbours(zones, zid):
    """Enabled zones of the same region whose sector rectangle touches zid's edge.

    Returns {'north': [...], 'south': [...], 'west': [...], 'east': [...]} with zone ids.
    """
    z = zones[zid]
    out = {'north': [], 'south': [], 'west': [], 'east': []}
    zx0, zy0 = z.off_x_sectors, z.off_y_sectors
    zx1, zy1 = zx0 + z.width_sectors, zy0 + z.height_sectors
    for oid, o in zones.items():
        if oid == zid or not o.enabled or o.region != z.region:
            continue
        ox0, oy0 = o.off_x_sectors, o.off_y_sectors
        ox1, oy1 = ox0 + o.width_sectors, oy0 + o.height_sectors
        x_overlap = min(zx1, ox1) - max(zx0, ox0)
        y_overlap = min(zy1, oy1) - max(zy0, oy0)
        if oy1 == zy0 and x_overlap > 0:
            out['north'].append(oid)
        if oy0 == zy1 and x_overlap > 0:
            out['south'].append(oid)
        if ox1 == zx0 and y_overlap > 0:
            out['west'].append(oid)
        if ox0 == zx1 and y_overlap > 0:
            out['east'].append(oid)
    return out


# ---------------------------------------------------------------------------
# Zone archives (base folder first, then addon folders)
# ---------------------------------------------------------------------------

def find_zone_archives(client, zid):
    """Return (folder_label, zone_dir) for the first location holding terrain for zid.

    Base zones/ is searched first. Addon folders are used only when the base folder has
    no terrain archive for the zone, so existing base zones keep their data.
    """
    candidates = [('base', ci_path(client, 'zones', f'zone{zid:03d}'))]
    for addon in ADDON_FOLDERS:
        candidates.append((addon, ci_path(client, addon, 'zones', f'zone{zid:03d}')))
    first_dir = None
    for label, zdir in candidates:
        if zdir is None:
            continue
        first_dir = first_dir or (label, zdir)
        dat = ci_path(zdir, f'dat{zid:03d}.mpk')
        if dat is None:
            continue
        try:
            names = {e.name.lower() for e in read_mpk_file(dat)}
        except (OSError, ValueError):
            continue
        if 'terrain.pcx' in names:
            return label, zdir
    return None, first_dir[1] if first_dir else None


WARNINGS = []


def load_archive_map(path):
    """{lowercase_name: bytes} for an MPAK file. Missing or unreadable archives give {} and a
    warning in WARNINGS, so one damaged file does not stop a batch."""
    if path is None or not os.path.exists(path):
        return {}
    try:
        return {e.name.lower(): e.data for e in read_mpk_file(path)}
    except (OSError, ValueError) as exc:
        WARNINGS.append(f'{os.path.basename(path)}: {exc}')
        return {}


def zone_archives(zdir, zid):
    """Return {'dat': {...}, 'csv': {...}, 'ter': {...}} for a zone folder."""
    out = {}
    for kind in ('dat', 'csv', 'ter'):
        out[kind] = load_archive_map(ci_path(zdir, f'{kind}{zid:03d}.mpk')) if zdir else {}
    return out


# ---------------------------------------------------------------------------
# Format parsers
# ---------------------------------------------------------------------------

def parse_pcx(data):
    """DAoC 8-bit single-plane PCX (RLE). Returns uint8 array [row=y, col=x]."""
    if data[0] != 10 or data[3] != 8 or data[65] != 1:
        raise ValueError('unsupported PCX variant')
    xmin, ymin, xmax, ymax = struct.unpack_from('<4H', data, 4)
    w, h = xmax - xmin + 1, ymax - ymin + 1
    bpl = struct.unpack_from('<H', data, 66)[0]
    out = np.zeros((h, bpl), np.uint8)
    i = 128
    for y in range(h):
        x = 0
        while x < bpl:
            c = data[i]
            i += 1
            if c & 0xC0 == 0xC0:
                n = c & 0x3F
                out[y, x:x + n] = data[i]
                i += 1
                x += n
            else:
                out[y, x] = c
                x += 1
    return out[:, :w]


def parse_sector_dat(data):
    return _parse_sections(data.decode('latin1'))


def parse_fixtures(csv_bytes, nif_bytes):
    """Return (fixtures, nifs). Fixture X/Y are zone-local loc; Scale is percent / 100."""
    nifs = {}
    if nif_bytes:
        for ln in nif_bytes.decode('latin1').splitlines():
            p = [c.strip() for c in ln.split(',')]
            if len(p) > 2 and p[0].isdigit():
                nifs[int(p[0])] = (p[1], p[2])
    fixtures = []
    if csv_bytes:
        for ln in csv_bytes.decode('latin1').splitlines():
            p = [c.strip() for c in ln.split(',')]
            if len(p) < 10 or not p[0].isdigit() or not p[1].isdigit():
                continue
            try:
                scale = float(p[7])
                fixtures.append(dict(
                    nif=int(p[1]), name=p[2], x=float(p[3]), y=float(p[4]), angle=float(p[6]),
                    scale=scale / 100.0 if scale else 1.0, radius=int(float(p[9]) or 0),
                ))
            except (ValueError, IndexError):
                continue
    return fixtures, nifs


_TREE_RE = re.compile(r'pine|evergreen|tree|oak|birch|willow|\bfir\b|\bash\b', re.I)
_ROCK_RE = re.compile(r'\brock|boulder', re.I)
_STRUCT_RE = re.compile(r'house|hut|tavern|tower|hall|gate|blacksmith|fort|castle|keep|temple|shrine|barn|lodge|ship', re.I)


def classify_fixture(name):
    if _TREE_RE.search(name):
        return 'tree'
    if _ROCK_RE.search(name):
        return 'rock'
    if _STRUCT_RE.search(name):
        return 'structure'
    return None


class TerrainZone:
    """Heightmap, water, rivers, tree density and fixtures of one zone, in cell units."""

    def __init__(self, info, dat, csv):
        if 'terrain.pcx' not in dat or 'offset.pcx' not in dat:
            raise ValueError('terrain.pcx / Offset.pcx missing')
        terrain = parse_pcx(dat['terrain.pcx'])
        offset = parse_pcx(dat['offset.pcx'])
        if terrain.shape != offset.shape or terrain.shape[0] != terrain.shape[1]:
            raise ValueError('terrain/offset maps are not a square matching pair')
        sector = parse_sector_dat(dat['sector.dat']) if 'sector.dat' in dat else {}
        terr = sector.get('terrain', {})
        self.terrain_scale = int(terr.get('scalefactor', '0') or 0)
        self.offset_scale = int(terr.get('offsetfactor', '0') or 0)
        self.height = terrain.astype(np.int32) * self.terrain_scale + offset.astype(np.int32) * self.offset_scale
        self.grid = terrain.shape[0]
        self.water_idx = parse_pcx(dat['water.pcx']) if 'water.pcx' in dat else None
        self.density = parse_pcx(dat['densemap.pcx']) if 'densemap.pcx' in dat else np.zeros(self.height.shape, np.uint8)
        wd = sector.get('waterdefs', {})
        n_water = int(wd.get('num', '0') or 0)
        self.water_height = []
        self.rivers = []
        for i in range(n_water):
            rs = sector.get(f'river{i:02d}')
            if rs is None:
                self.water_height.append(None)
                continue
            self.water_height.append(int(rs.get('height', '0') or 0))
            lefts, rights = [], []
            for p in range(int(rs.get('bankpoints', '0') or 0)):
                try:
                    lx, ly = (float(v) for v in rs[f'left{p:02d}'].split(',')[:2])
                    rx, ry = (float(v) for v in rs[f'right{p:02d}'].split(',')[:2])
                except (KeyError, ValueError):
                    continue
                lefts.append((lx, ly))
                rights.append((rx, ry))
            if len(lefts) >= 2:
                mid = [((lx + rx) / 2.0, (ly + ry) / 2.0) for (lx, ly), (rx, ry) in zip(lefts, rights)]
                self.rivers.append({'mid': mid, 'type': rs.get('type', ''), 'height': self.water_height[-1]})
        self.fixtures, nifs = parse_fixtures(csv.get('fixtures.csv'), csv.get('nifs.csv'))
        for f in self.fixtures:
            tname = nifs.get(f['nif'], ('', ''))[0] or f['name']
            f['class'] = classify_fixture(f'{f["name"]} {tname}')

    def water_depth_grid(self):
        """Per cell: water surface minus ground (loc). Dry cells get -300 (below the line)."""
        H = self.height.astype(np.float32)
        surf = np.full(H.shape, np.nan, np.float32)
        if self.water_idx is not None:
            idx = self.water_idx.astype(np.int64)
            for i, h in enumerate(self.water_height):
                if h is not None:
                    surf[idx == i] = float(h)
        return np.where(np.isnan(surf), -300.0, surf - H).astype(np.float32)


# ---------------------------------------------------------------------------
# Ground texture
# ---------------------------------------------------------------------------

def zone_ground_layer(ter_map):
    """Name of the zone-wide ground layer (1 tile, not tileable) from textures.csv, or None."""
    tc = ter_map.get('textures.csv')
    if tc is None:
        return None
    counts = collections.Counter()
    for ln in tc.decode('latin1').splitlines()[1:]:
        p = [c.strip() for c in ln.split(',')]
        if len(p) >= 11 and p[8] == '1' and p[9] == '1' and p[10] == '0':
            counts[p[2]] += 1
    return counts.most_common(1)[0][0] if counts else None


def load_ground_rgb(client, ter_map, size):
    """(RGB float array size x size, layer_name) or (None, layer_name)."""
    name = zone_ground_layer(ter_map)
    if not name:
        return None, None
    path = ci_path(client, 'zones', 'TerrainTex', f'{name}.dds')
    if path is None:
        return None, name
    im = Image.open(path)
    im.load()
    im = im.convert('RGB')
    if im.size != (size, size):
        im = im.resize((size, size), Image.BILINEAR)
    return np.asarray(im).astype(np.float32), name


# ---------------------------------------------------------------------------
# Stock map texture and roads
# ---------------------------------------------------------------------------

def read_stock_rgb(path):
    im = Image.open(path)
    im.load()
    return im.convert('RGB')


def road_mask(stock_rgb):
    a = np.asarray(stock_rgb).astype(np.int32)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    return (r - g > 40) & (r - b > 40) & (r > 80)


def _dilate(mask):
    out = mask.copy()
    out[1:, :] |= mask[:-1, :]
    out[:-1, :] |= mask[1:, :]
    out[:, 1:] |= mask[:, :-1]
    out[:, :-1] |= mask[:, 1:]
    return out


def zhang_suen(img):
    """Zhang-Suen thinning of a boolean array (vectorised)."""
    img = img.astype(np.uint8).copy()
    while True:
        changed = False
        for step in (0, 1):
            P = np.pad(img, 1)
            p2 = P[:-2, 1:-1]; p3 = P[:-2, 2:]; p4 = P[1:-1, 2:]; p5 = P[2:, 2:]
            p6 = P[2:, 1:-1]; p7 = P[2:, :-2]; p8 = P[1:-1, :-2]; p9 = P[:-2, :-2]
            p1 = img
            B = p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9
            seq = [p2, p3, p4, p5, p6, p7, p8, p9, p2]
            A = sum(((seq[i] == 0) & (seq[i + 1] == 1)).astype(np.uint8) for i in range(8))
            if step == 0:
                c = (p2 * p4 * p6 == 0) & (p4 * p6 * p8 == 0)
            else:
                c = (p2 * p4 * p8 == 0) & (p2 * p6 * p8 == 0)
            rm = (p1 == 1) & (B >= 2) & (B <= 6) & (A == 1) & c
            if rm.any():
                img[rm] = 0
                changed = True
        if not changed:
            break
    return img.astype(bool)


def _trace_skeleton(skel):
    """Trace a one-pixel skeleton into polylines of (x, y) texel centres."""
    pts = set(zip(*np.nonzero(skel)))
    nbr = [(-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1)]

    def neighbours_of(p):
        r, c = p
        return [(r + dr, c + dc) for dr, dc in nbr if (r + dr, c + dc) in pts]

    deg = {p: len(neighbours_of(p)) for p in pts}
    used = set()
    lines = []

    def key(a, b):
        return (a, b) if a < b else (b, a)

    for start in [p for p in pts if deg[p] != 2]:
        for nb in neighbours_of(start):
            if key(start, nb) in used:
                continue
            used.add(key(start, nb))
            path = [start, nb]
            prev, cur = start, nb
            while deg[cur] == 2:
                nxt = [q for q in neighbours_of(cur) if q != prev]
                if not nxt or key(cur, nxt[0]) in used:
                    break
                used.add(key(cur, nxt[0]))
                path.append(nxt[0])
                prev, cur = cur, nxt[0]
            lines.append(path)
    return [[(c + 0.5, r + 0.5) for r, c in path] for path in lines]


def _polyline_length(pts):
    return sum(math.hypot(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1]) for i in range(len(pts) - 1))


def _join_close_ends(lines, max_gap):
    """Merge polylines whose endpoints lie within max_gap texels (bridges icon gaps).

    Each pass finds the closest pair of endpoints from different lines (vectorised) and
    joins those two lines; passes repeat until no pair is within max_gap.
    """
    lines = [list(l) for l in lines if len(l) > 0]
    while len(lines) > 1:
        owners, ends = [], []
        for i, l in enumerate(lines):
            owners.append((i, 0))
            ends.append(l[0])
            owners.append((i, -1))
            ends.append(l[-1])
        pts = np.asarray(ends, dtype=np.float64)
        d = np.hypot(pts[:, None, 0] - pts[None, :, 0], pts[:, None, 1] - pts[None, :, 1])
        line_idx = np.array([o[0] for o in owners])
        d[line_idx[:, None] == line_idx[None, :]] = np.inf
        a, b = np.unravel_index(int(np.argmin(d)), d.shape)
        if d[a, b] > max_gap:
            break
        (i, ei), (j, ej) = owners[a], owners[b]
        left = lines[i] if ei == -1 else lines[i][::-1]    # ends at the join point
        right = lines[j] if ej == 0 else lines[j][::-1]    # starts at the join point
        merged = left + right
        lines = [l for k, l in enumerate(lines) if k not in (i, j)] + [merged]
    return lines


def _chaikin(points, iterations):
    pts = np.asarray(points, dtype=np.float64)
    for _ in range(iterations):
        if len(pts) < 3:
            break
        q = 0.75 * pts[:-1] + 0.25 * pts[1:]
        r = 0.25 * pts[:-1] + 0.75 * pts[1:]
        new = np.empty((2 * len(q), 2))
        new[0::2] = q
        new[1::2] = r
        pts = np.vstack([pts[:1], new, pts[-1:]])
    return [tuple(p) for p in pts]


def extract_roads(stock_rgb, join_texels=5.0, min_texels=4.0):
    """Road polylines in 256-texel coordinates.

    Red-dominant pixels are dilated once, thinned to a skeleton, traced, and joined where
    endpoints are within join_texels (about 10 px at 512). Fragments shorter than
    min_texels (about 8 px at 512) are dropped. Chaikin smoothing is applied last.
    """
    skel = zhang_suen(_dilate(road_mask(stock_rgb)))
    lines = _trace_skeleton(skel)
    lines = _join_close_ends(lines, join_texels)
    lines = [l for l in lines if _polyline_length(l) >= min_texels]
    return [_chaikin(l, 3) for l in lines]


# ---------------------------------------------------------------------------
# World database (read-only copy)
# ---------------------------------------------------------------------------

class WorldDb:
    """Opens a private copy of the world database read-only.

    The live database and its -wal/-shm files are copied to a temporary directory first.
    Only static world tables are queried: Area, BindPoint, Keep, Mob (static NPC rows
    filtered by class), ZonePoint, JumpPoint, Teleport. Bot and companion tables, account
    and character tables are never read.
    """

    def __init__(self, source_path):
        if not os.path.isfile(source_path):
            raise FileNotFoundError(f'world database not found: {source_path}')
        self.tmpdir = tempfile.mkdtemp(prefix='daoc-map-db-')
        base = os.path.basename(source_path)
        dest = os.path.join(self.tmpdir, base)
        shutil.copy2(source_path, dest)
        for suffix in ('-wal', '-shm'):
            if os.path.exists(source_path + suffix):
                shutil.copy2(source_path + suffix, dest + suffix)
        self.con = sqlite3.connect(f'file:{dest}?mode=ro', uri=True)
        self.cur = self.con.cursor()

    def close(self):
        try:
            self.con.close()
        finally:
            shutil.rmtree(self.tmpdir, ignore_errors=True)

    def zone_name(self, region):
        """Name of a zone or region from the static Zones table (fallback for zone-exit labels)."""
        row = self.cur.execute(
            'SELECT Name FROM Zones WHERE (ZoneID = ? OR RegionID = ?) AND Name <> \'\' ORDER BY ZoneID = ? DESC LIMIT 1',
            (region, region, region)).fetchone()
        return row[0] if row else None

    def zone_pois(self, zinfo, zones_by_region_names):
        """POIs and towns for a zone (bounds from its sector rectangle).

        zones_by_region_names: callable(target_region) -> (kind, name) where kind is one of
        dungeon, city, overworld, housing.
        """
        reg = zinfo.region
        ox, oy = zinfo.origin_loc
        ex, ey = zinfo.extent_loc
        X0, X1, Y0, Y1 = ox, ox + ex, oy, oy + ey

        def inside(x, y):
            return X0 <= x < X1 and Y0 <= y < Y1

        towns, pois = [], []
        for desc, x, y, r, cls in self.cur.execute(
                'SELECT Description, X, Y, Radius, ClassType FROM Area WHERE Region = ?', (reg,)):
            if desc and inside(x, y) and 'Bind' not in (cls or ''):
                towns.append({'name': desc, 'x': x, 'y': y, 'r': r})
        for x, y in self.cur.execute('SELECT X, Y FROM BindPoint WHERE Region = ?', (reg,)):
            if inside(x, y):
                pois.append({'type': 'bind', 'name': 'Bind point', 'x': x, 'y': y, 'source': 'BindPoint'})
        for name, x, y in self.cur.execute('SELECT Name, X, Y FROM Keep WHERE Region = ?', (reg,)):
            if inside(x, y):
                pois.append({'type': 'keep', 'name': name, 'x': x, 'y': y, 'source': 'Keep'})

        classes = sorted(MERCHANT_CLASSES | PORTER_CLASSES | STABLE_CLASSES | TRAINER_EXTRA)
        placeholders = ','.join('?' * len(classes))
        svc = collections.defaultdict(list)
        q = ('SELECT ClassType, Name, X, Y FROM Mob WHERE Region = ? AND '
             f'(ClassType LIKE ? OR ClassType IN ({placeholders}))')
        for ct, name, x, y in self.cur.execute(q, (reg, TRAINER_PREFIX + '%', *classes)):
            if not inside(x, y):
                continue
            cat = service_category(ct)
            if cat:
                svc[cat].append({'x': x, 'y': y, 'name': name or ''})
        for cat, items in svc.items():
            for cl in cluster_points(items, 1500):
                cx = int(round(sum(i['x'] for i in cl) / len(cl)))
                cy = int(round(sum(i['y'] for i in cl) / len(cl)))
                names = sorted({i['name'] for i in cl if i['name']})
                pois.append({'type': cat, 'name': ', '.join(names[:4]) or cat, 'count': len(cl),
                             'x': cx, 'y': cy, 'source': 'Mob', 'members': names[:12]})

        zp = collections.defaultdict(list)
        for sx, sy, tr, dfclass in self.cur.execute(
                'SELECT SourceX, SourceY, TargetRegion, ClassType FROM ZonePoint WHERE SourceRegion = ?', (reg,)):
            if not inside(sx, sy):
                continue
            kind, name = zones_by_region_names(tr)
            if kind == 'housing':
                continue
            if 'DFEnter' in (dfclass or ''):
                kind = 'dungeon'
            zp[(kind, tr)].append({'x': sx, 'y': sy, 'name': name})
        for (kind, tr), items in zp.items():
            for cl in cluster_points(items, 2500):
                cx = int(round(sum(i['x'] for i in cl) / len(cl)))
                cy = int(round(sum(i['y'] for i in cl) / len(cl)))
                nm = next((i['name'] for i in cl if i['name']), None) or f'Zone {tr}'
                pois.append({'type': 'dungeon' if kind == 'dungeon' else 'exit', 'name': nm,
                             'count': len(cl), 'x': cx, 'y': cy, 'target_region': tr,
                             'target_kind': kind, 'source': 'ZonePoint'})
        for name, x, y in self.cur.execute('SELECT Name, Xpos, Ypos FROM JumpPoint WHERE Region = ?', (reg,)):
            if inside(x, y):
                pois.append({'type': 'portal', 'name': name, 'x': x, 'y': y, 'source': 'JumpPoint'})
        for tid, x, y in self.cur.execute('SELECT TeleportID, X, Y FROM Teleport WHERE RegionID = ?', (reg,)):
            if inside(x, y):
                pois.append({'type': 'portal', 'name': tid, 'x': x, 'y': y, 'source': 'Teleport'})
        return {'towns': towns, 'pois': pois}


def service_category(class_type):
    ct = class_type or ''
    if ct.startswith(TRAINER_PREFIX) or ct in TRAINER_EXTRA:
        return 'trainer'
    if ct in MERCHANT_CLASSES or 'SealsMerchant' in ct:
        return 'merchant'
    if ct in STABLE_CLASSES:
        return 'stable'
    if ct in PORTER_CLASSES:
        return 'porter'
    return None


def cluster_points(items, radius):
    """Single-link clustering of dicts with 'x' and 'y'."""
    clusters = []
    for it in items:
        touching = [cl for cl in clusters
                    if any(math.hypot(it['x'] - o['x'], it['y'] - o['y']) <= radius for o in cl)]
        merged = [it]
        for cl in touching:
            merged.extend(cl)
            clusters.remove(cl)
        clusters.append(merged)
    return clusters


def snap_town(towns, p):
    """Name of the named area containing p (or the nearest within its radius + 1500 loc)."""
    best = None
    for t in towns:
        d = math.hypot(p['x'] - t['x'], p['y'] - t['y'])
        if d <= max(t['r'], 1) + 1500 and (best is None or d < best[0]):
            best = (d, t)
    return best[1] if best else None
