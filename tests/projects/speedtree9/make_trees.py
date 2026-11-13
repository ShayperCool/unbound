#!/usr/bin/env python3
"""Writes the probe's SpeedTree 9 trees (Assets/Trees/*.st9) and their textures: small trees of
our own, laid out as SpeedTree 9 files are (see README.md), so the probe needs no third-party asset.

    python3 tests/projects/speedtree9/make_trees.py

A .st9 file is the 16-byte signature "SpeedTree9______" followed by tables. A table is a uint32
count and that many uint32 offsets, each relative to the table's start, to its entries' data. An
entry is a scalar (uint32, float), a struct (floats), a string (uint32 length including the
terminating zero, then UTF-8 bytes and the zero), an array (uint32 count, then the items packed)
or another table. All values are little-endian.
"""
import math
from pathlib import Path
import struct
import zlib

PROBE = Path(__file__).resolve().parent
TREES = PROBE/'Assets/Trees'
SIGNATURE = b'SpeedTree9______'

# Root table entries (unused indices hold a uint32 zero).
ROOT_ENTRIES = 21
ROOT_VERSION_MAJOR, ROOT_VERSION_MINOR, ROOT_BOUNDS = 0, 1, 2
ROOT_LODS, ROOT_BILLBOARD, ROOT_COLLISIONS, ROOT_MATERIALS, ROOT_WIND = 5, 6, 7, 10, 15
# Wind table: common, shared, branch 1, branch 2, ripple; scalars; flags.
WIND_ENTRIES = 20
COMMON_ENTRIES = 16
CURVE = 20  # samples per wind curve


class Blob:
    """Builds a file: tables are written after their entries' data, aligned to 4 bytes."""

    def __init__(self):
        self.data = bytearray()

    def align(self):
        while len(self.data) % 4:
            self.data.append(0)

    def put(self, raw):
        self.align()
        at = len(self.data)
        self.data += raw
        return at

    def table(self, entries):
        """entries: callables that write an entry and return its absolute offset."""
        self.align()
        start = len(self.data)
        self.data += b'\0' * (4 + 4 * len(entries))
        struct.pack_into('<I', self.data, start, len(entries))
        for i, write in enumerate(entries):
            at = write()
            struct.pack_into('<I', self.data, start + 4 + 4 * i, at - start)
        return start


def u32(v):
    return lambda b: b.put(struct.pack('<I', v))


def f32(v):
    return lambda b: b.put(struct.pack('<f', v))


def floats(*vs):
    return lambda b: b.put(struct.pack('<%df' % len(vs), *vs))


def string(s):
    raw = s.encode() + b'\0'
    return lambda b: b.put(struct.pack('<I', len(raw)) + raw)


def array(fmt, items):
    return lambda b: b.put(struct.pack('<I', len(items)) + b''.join(struct.pack(fmt, *i) for i in items))


def table(entries):
    return lambda b: b.table([lambda e=e: e(b) for e in entries])


def table_array(tables):
    return table(tables)


VERTEX = '<' + 'f' * 34 + 'II'  # 144 bytes: 34 floats, the camera-facing flag, the bone id
DRAW_CALL = '<IIII'


def vertex(anchor, offset=(0, 0, 0), normal=(0, 1, 0), tangent=(1, 0, 0), binormal=(0, 0, 1), uv=(0, 0),
           lightmap_uv=(0, 0), color=(1, 1, 1), ao=1.0, blend=1.0, branch1=(0, 0, 0), branch2=(0, 0, 0),
           ripple=0.0, facing=False, bone=0):
    lod_offset = (0, 0, 0)
    return (*anchor, *offset, *lod_offset, *normal, *tangent, *binormal, *uv, *lightmap_uv, *color, ao, blend,
            *branch1, *branch2, ripple, 1 if facing else 0, bone)


def trunk(height, radius, sides, lod_seed):
    """A cylinder: vertices and triangle indices."""
    verts, idx = [], []
    for ring, y in enumerate((0.0, height)):
        for s in range(sides + 1):
            a = 2 * math.pi * s / sides
            n = (math.cos(a), 0.0, math.sin(a))
            p = (radius * n[0], y, radius * n[2])
            verts.append(vertex(p, normal=n, tangent=(-n[2], 0, n[0]), binormal=(0, 1, 0), uv=(s / sides, ring),
                                lightmap_uv=(s / sides * 0.5, ring * 0.5), color=(0.9, 0.8 + 0.05 * lod_seed, 0.7), ao=0.5 + 0.5 * ring,
                                blend=0.25 * ring, branch1=(0.1 * ring, 0.2, 0.3 * s / sides), ripple=0.0))
    for s in range(sides):
        a, b2, c, d = s, s + 1, s + sides + 1, s + sides + 2
        idx += [a, c, b2, b2, c, d]
    return verts, idx


def leaves(count, height, facing):
    """Quads around the top; camera-facing ones keep their corner offsets."""
    verts, idx = [], []
    for q in range(count):
        a = 2 * math.pi * q / count
        anchor = (0.8 * math.cos(a), height + 0.2 * q, 0.8 * math.sin(a))
        base = len(verts)
        for (u, v) in ((0, 0), (1, 0), (1, 1), (0, 1)):
            off = ((u - 0.5) * 0.6, (v - 0.5) * 0.6, 0.0)
            pos = anchor if facing else (anchor[0] + off[0], anchor[1] + off[1], anchor[2])
            verts.append(vertex(pos, offset=off if facing else (0, 0, 0), normal=(0, 0, -1), tangent=(1, 0, 0),
                                binormal=(0, -1, 0) if q % 2 else (0, 1, 0), uv=(u, v), lightmap_uv=(u * 0.25 + 0.5, v * 0.25),
                                color=(0.6, 0.9, 0.5), ao=0.75, blend=0.5 + 0.1 * q, branch1=(0.4, 0.5, 0.6),
                                branch2=(0.7, 0.1 * q, 0.2), ripple=0.3 + 0.1 * q, facing=facing))
        idx += [base, base + 2, base + 1, base, base + 3, base + 2]
    return verts, idx


def billboard(height):
    verts = [vertex((x, y, 0.0), normal=(0, 0, -1), uv=(u, v), lightmap_uv=(u, v), blend=0.0)
             for (x, y, u, v) in ((-1, 0, 0, 0), (1, 0, 1, 0), (1, height, 1, 1), (-1, height, 0, 1))]
    return verts, [0, 2, 1, 0, 3, 2]


def lod(parts):
    """parts: (material index, contains facing geometry, (vertices, indices)) per draw call."""
    verts, idx, draws = [], [], []
    for material, facing, (pv, pi) in parts:
        base = len(verts)
        draws.append((material, 1 if facing else 0, len(idx), len(pi)))
        verts += pv
        idx += [base + i for i in pi]
    return table([array(VERTEX, verts), array('<I', [(i,) for i in idx]), array(DRAW_CALL, draws)])


def material_map(used, path, color):
    return table([u32(1 if used else 0), string(path), floats(*color)])


def material(name, two_sided, flip, is_billboard, maps):
    return table([string(name), u32(1 if two_sided else 0), u32(1 if flip else 0), u32(1 if is_billboard else 0),
                  table_array(maps)])


def curve(scale, phase):
    return [scale * (0.5 + 0.5 * math.sin(phase + i * 0.3)) for i in range(CURVE)]


def branch(scale, phase, independence):
    return table([array('<f', [(v,) for v in curve(scale, phase)]), array('<f', [(v,) for v in curve(scale * 0.5, phase + 1)]),
                  array('<f', [(v,) for v in curve(1.0, phase + 2)]), array('<f', [(v,) for v in curve(0.25, phase + 3)]),
                  array('<f', [(v,) for v in curve(0.75, phase + 4)]), f32(independence)])


def ripple(scale, shimmer, independence):
    return table([array('<f', [(v,) for v in curve(scale, 0.5)]), array('<f', [(v,) for v in curve(scale * 2, 1.5)]),
                  array('<f', [(v,) for v in curve(2.0, 2.5)]), array('<f', [(v,) for v in curve(0.5, 3.5)]),
                  f32(shimmer), f32(independence)])


def wind(flags):
    common = [f32(0.0)] * COMMON_ENTRIES
    for i, v in ((0, 4.0), (1, 2.0), (5, 0.3), (6, 0.1), (7, 0.6), (8, 1.0), (9, 3.0), (10, 0.8), (11, 0.4), (15, 0.5)):
        common[i] = f32(v)
    entries = [table(common), branch(0.2, 0.0, 0.1), branch(0.4, 1.0, 0.3), branch(0.6, 2.0, 0.5), ripple(0.05, 0.7, 0.25)]
    entries += [u32(0)] * 5
    entries += [f32(0.35), f32(0.15), f32(0.1)]
    entries += [u32(0)] * 2
    entries += [u32(1 if f else 0) for f in flags]
    assert len(entries) == WIND_ENTRIES
    return table(entries)


def tree(lods, billboard_last, collisions, materials, wind_flags, bounds):
    root = [u32(0)] * ROOT_ENTRIES
    root[ROOT_VERSION_MAJOR] = u32(9)
    root[ROOT_VERSION_MINOR] = u32(5)
    root[ROOT_BOUNDS] = floats(*bounds)
    root[ROOT_LODS] = table_array(lods)
    root[ROOT_BILLBOARD] = table([u32(1 if billboard_last else 0), u32(0), u32(4 if billboard_last else 0)])
    root[ROOT_COLLISIONS] = table_array([table([floats(*p1), floats(*p2), f32(r), string(user)])
                                         for (p1, p2, r, user) in collisions])
    root[ROOT_MATERIALS] = table_array(materials)
    root[ROOT_WIND] = wind(wind_flags)
    blob = Blob()
    blob.data += SIGNATURE
    blob.table([lambda e=e: e(blob) for e in root])
    return bytes(blob.data)


def png(path, w, h, pixel):
    rows = b''.join(b'\0' + b''.join(bytes(pixel(x, y)) for x in range(w)) for y in range(h))

    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
                     + chunk(b'IDAT', zlib.compress(rows, 9)) + chunk(b'IEND', b''))


def main():
    TREES.mkdir(parents=True, exist_ok=True)
    png(TREES/'Bark_Color.png', 16, 16, lambda x, y: (120 + 4 * x, 80 + 2 * y, 40, 255))
    png(TREES/'Bark_Normal.png', 16, 16, lambda x, y: (128, 128 + 3 * x, 255, 255))
    png(TREES/'Leaf_Color.png', 16, 16, lambda x, y: (40, 120 + 6 * y, 30, 255 if (x - 8) ** 2 + (y - 8) ** 2 < 49 else 0))
    png(TREES/'Leaf_Extra.png', 16, 16, lambda x, y: (200, 0, 255, 255))
    png(TREES/'Billboard_Color.png', 16, 16, lambda x, y: (60, 140, 50, 255 if abs(x - 8) < 6 else 0))

    bark = material('Bark', False, False, False, [
        material_map(True, 'Bark_Color.png', (1, 1, 1, 1)),
        material_map(True, 'Bark_Normal.png', (0.5, 0.5, 1, 1)),
        material_map(False, '', (0.2, 0.0, 1, 1)),
        material_map(False, '', (1, 1, 1, 1))])
    leaf = material('Leaves', True, True, False, [
        material_map(True, 'Leaf_Color.png', (1, 1, 1, 1)),
        material_map(False, '', (0.5, 0.5, 1, 1)),
        material_map(True, 'Leaf_Extra.png', (0.3, 0.1, 1, 1)),
        material_map(True, '', (0.8, 0.9, 0.4, 1))])
    board = material('Billboard', False, False, True, [
        material_map(True, 'Billboard_Color.png', (1, 1, 1, 1)),
        material_map(False, '', (0.5, 0.5, 1, 1)),
        material_map(False, '', (0.0, 0.0, 1, 1)),
        material_map(True, '', (0.6, 0.7, 0.3, 1))])
    full_lods = [
        lod([(0, False, trunk(3.0, 0.3, 8, 0)), (1, True, leaves(6, 3.0, True))]),
        lod([(0, False, trunk(3.0, 0.3, 5, 1)), (1, True, leaves(3, 3.0, True))]),
        lod([(0, False, trunk(3.0, 0.3, 3, 2))]),
        lod([(2, False, billboard(4.0))]),
    ]
    collisions = [((0, 1.5, 0), (0, 1.5, 0), 0.4, 'sphere'), ((0, 0, 0), (0.2, 2.5, 0.1), 0.3, 'capsule')]
    bounds = (-1.2, 0.0, -1.2, 1.2, 4.5, 1.2)
    (TREES/'Full.st9').write_bytes(tree(full_lods, True, collisions, [bark, leaf, board], [1, 1, 1, 1, 1], bounds))
    (TREES/'Settings.st9').write_bytes(tree(full_lods, True, collisions, [bark, leaf, board], [1, 1, 0, 1, 0], bounds))
    simple_lods = [lod([(0, False, trunk(2.0, 0.25, 6, 3)), (1, False, leaves(4, 2.0, False))])]
    simple = tree(simple_lods, False, [], [bark, leaf], [0, 0, 0, 0, 0], (-1, 0, -1, 1, 3, 1))
    (TREES/'Simple.st9').write_bytes(simple)
    # The same tree, its .meta remapping the bark material to a project material.
    (TREES/'Remapped.st9').write_bytes(simple)
    print('wrote', ', '.join(sorted(p.name for p in TREES.glob('*.st9'))))


if __name__ == '__main__':
    main()
