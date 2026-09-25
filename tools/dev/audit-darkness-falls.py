#!/usr/bin/env python3
"""Offline DF route audit and nine-tile patch packaging; never opens a live save.

Use a disposable SQLite snapshot, the installed mesh (read-only), and a scratch
Detour library built from source/server/Pathing/Detour with DT_POLYREF64 enabled.
See docs/CAMLANN_PVP_REVIEW.md for the geometry recipe and reproduction commands.
"""
import argparse
import base64
import ctypes as C
import hashlib
import json
import math
from pathlib import Path
import sqlite3
import struct

STAIR_TILES = {(18, 30, 0), (20, 30, 0), (21, 30, 0), (77, 61, 0),
               (78, 61, 0), (79, 61, 0), (110, 93, 0), (111, 93, 0), (112, 93, 0)}


def tiles(blob):
    if struct.unpack_from('<ii', blob) != (0x4D534554, 1):
        raise ValueError('Not the supported 64-bit Detour mesh format')
    offset = 40
    count = 0
    while offset < len(blob):
        ref, size = struct.unpack_from('<Qi', blob, offset)
        if size < 100 or offset + 16 + size > len(blob):
            raise ValueError('Invalid tile size')
        data = blob[offset + 16:offset + 16 + size]
        yield offset, ref, size, struct.unpack_from('<3i', data, 8), data
        count += 1
        offset += 16 + size
    if count != struct.unpack_from('<i', blob, 8)[0]:
        raise ValueError('Tile count does not match header')


def package(args):
    baseline = args.baseline.read_bytes()
    rebuilt = args.rebuilt.read_bytes()
    if baseline[12:40] != rebuilt[12:40]:
        raise ValueError('Mesh origins/grid parameters differ; retain the baseline bounds before rebuilding')
    replacement = {key: data for _, _, _, key, data in tiles(rebuilt) if key in STAIR_TILES}
    if set(replacement) != STAIR_TILES or any(struct.unpack_from('<i', data, 52)[0] != 1 for data in replacement.values()):
        raise ValueError('Expected exactly one stair link in each of the nine audited tiles')
    result = bytearray(baseline[:40])
    patches = []
    for offset, ref, size, key, data in tiles(baseline):
        if key in replacement:
            data = replacement[key]
            chunk = struct.pack('<Qi4x', ref, len(data)) + data
            patches.append(dict(Offset=offset, Length=16 + size, Data=base64.b64encode(chunk).decode()))
            result.extend(chunk)
        else:
            result.extend(baseline[offset:offset + 16 + size])
    if len(patches) != 9:
        raise ValueError('Baseline is missing an audited stair tile')
    manifest = dict(SourceSha256=hashlib.sha256(baseline).hexdigest(),
                    ResultSha256=hashlib.sha256(result).hexdigest(), Patches=patches)
    args.output.write_text(json.dumps(manifest, separators=(',', ':')) + '\n')
    args.mesh_output.write_bytes(result)
    print('Packaged nine tiles; every other tile and the mesh header are byte-identical.')


def audit(args):
    lib = C.CDLL(str(args.library.resolve()))
    ptr = C.c_void_p
    vector = C.c_float * 3
    filters = (C.c_ushort * 2)(0xFFEF, 0)
    floats = C.POINTER(C.c_float)
    lib.LoadNavMesh.argtypes = [C.c_char_p, C.POINTER(ptr)]
    lib.LoadNavMesh.restype = C.c_bool
    lib.CreateNavMeshQuery.argtypes = [ptr, C.POINTER(ptr)]
    lib.CreateNavMeshQuery.restype = C.c_bool
    lib.FreeNavMeshQuery.argtypes = [ptr]
    lib.FreeNavMesh.argtypes = [ptr]
    lib.FindClosestPoint.argtypes = [ptr, floats, floats, C.POINTER(C.c_ushort), floats]
    lib.FindClosestPoint.restype = C.c_uint
    lib.PathStraight.argtypes = [ptr, floats, floats, floats, C.POINTER(C.c_ushort), C.c_int,
                                C.POINTER(C.c_int), floats, C.POINTER(C.c_ushort)]
    lib.PathStraight.restype = C.c_uint
    mesh, query = ptr(), ptr()
    if not lib.LoadNavMesh(str(args.mesh.resolve()).encode(), C.byref(mesh)):
        raise ValueError('Mesh load failed')
    if not lib.CreateNavMeshQuery(mesh, C.byref(query)):
        lib.FreeNavMesh(mesh)
        raise ValueError('Query creation failed')

    def native(point):
        return vector(point[0] / 32, point[2] / 32, point[1] / 32)

    def world(point):
        return [round(point[0] * 32, 2), round(point[2] * 32, 2), round(point[1] * 32, 2)]

    def floor(point):
        result = vector()
        status = lib.FindClosestPoint(query, native(point), native((64, 64, 96)), filters, result)
        return world(result) if status & 0x40000000 else None

    def complete(start, end):
        count = C.c_int()
        nodes = (C.c_float * 768)()
        flags = (C.c_ushort * 256)()
        status = lib.PathStraight(query, native(start), native(end), vector(1, 2, 1), filters,
                                  2, C.byref(count), nodes, flags)
        # Reject PARTIAL_RESULT even when findStraightPath echoes the requested
        # endpoint on another floor; it is not proof of a connected corridor.
        return status == 0x40000000 and 0 < count.value < 256 and math.dist(
            world(nodes[3 * (count.value - 1):3 * count.value]), end) < 16

    try:
        with sqlite3.connect(args.snapshot.resolve().as_uri() + '?mode=ro&immutable=1', uri=True) as db:
            if db.execute('pragma quick_check').fetchone()[0] != 'ok':
                raise ValueError('Snapshot integrity check failed')
            entries = list(db.execute('select distinct TargetX,TargetY,TargetZ from ZonePoint '
                                      'where TargetRegion=249 and SourceRegion<>249'))
            floors = [floor(entry) for entry in entries]
            rows = db.execute("select Mob_ID,Name,X,Y,Z,Level from Mob where Region=249 and Realm=0 "
                              "and Level between 1 and 65 and ClassType='DOL.GS.GameNPC'").fetchall()
        proofs = []
        entry_counts = [0] * len(entries)
        for identifier, name, x, y, z, level in rows:
            position = floor((x, y, z))
            if position is None or math.dist((x, y, z), position) > 112:
                continue
            usable = []
            for i, (entry, start) in enumerate(zip(entries, floors)):
                if start is not None and complete(start, position) and complete(position, start):
                    usable.append(list(entry))
                    entry_counts[i] += 1
            if usable:
                proofs.append(dict(id=identifier, zone=249, region=249, name=name.lower(),
                                   spawn=[x, y, z], point=position, entries=usable))
        args.output.write_text('{\n  "version": 1,\n  "spawns": [\n' + ',\n'.join(
            '    ' + json.dumps(point, separators=(',', ':')) for point in proofs) + '\n  ]\n}\n')
        print(json.dumps(dict(audited=len(rows), verified=len(proofs),
                              entrance_counts=entry_counts)))
    finally:
        lib.FreeNavMeshQuery(query)
        lib.FreeNavMesh(mesh)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    pack = commands.add_parser('package')
    for key in ('baseline', 'rebuilt', 'output', 'mesh-output'):
        pack.add_argument('--' + key, type=Path, required=True)
    check = commands.add_parser('audit')
    for key in ('snapshot', 'mesh', 'library', 'output'):
        check.add_argument('--' + key, type=Path, required=True)
    args = parser.parse_args()
    inputs = [value.resolve() for key, value in vars(args).items()
              if isinstance(value, Path) and key not in ('output', 'mesh_output')]
    for key in ('output', 'mesh_output'):
        path = getattr(args, key, None)
        if path is not None and (path.resolve() in inputs or path.exists()):
            parser.error('Output must be a new scratch file, never an input or installed file')
    (package if args.command == 'package' else audit)(args)


if __name__ == '__main__':
    main()
