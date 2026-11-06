#!/usr/bin/env python3
"""Compares a camerastack capture with the Unity reference of one color space.

    compare.py REFERENCE_DIR CANDIDATE_DIR

Every record of the reference's values.jsonl must be in the candidate with the same value
(missing and extra keys are listed). Every case image is compared pixel for pixel over RGB: a
pixel differs when a channel differs by more than 3/255. A case passes only with no differing
pixel; the report lists, per failing case, the differing pixel count, the mean and worst
channel difference and the bounding box of the differing pixels (x, y from the top left).
Exit status 1 when anything is missing, extra or failing.
"""
import json
import os
import struct
import sys
import zlib

TOLERANCE = 3


def read_png(path):
    """(width, height, rows of RGB tuples, top row first) of an 8-bit RGB/RGBA PNG."""
    data = open(path, 'rb').read()
    assert data[:8] == b'\x89PNG\r\n\x1a\n', path
    pos, idat = 8, b''
    while pos < len(data):
        length, kind = struct.unpack('>I4s', data[pos:pos+8])
        body = data[pos+8:pos+8+length]
        if kind == b'IHDR':
            width, height, depth, color, _, _, interlace = struct.unpack('>IIBBBBB', body)
            assert depth == 8 and interlace == 0 and color in (2, 6), (path, depth, color, interlace)
            channels = 3 if color == 2 else 4
        elif kind == b'IDAT':
            idat += body
        pos += 12+length
    raw = zlib.decompress(idat)
    stride = width*channels
    rows, prev = [], bytearray(stride)
    for y in range(height):
        f = raw[y*(stride+1)]
        line = bytearray(raw[y*(stride+1)+1:(y+1)*(stride+1)])
        for x in range(stride):
            a = line[x-channels] if x >= channels else 0
            b = prev[x]
            c = prev[x-channels] if x >= channels else 0
            if f == 1:
                line[x] = (line[x]+a) & 255
            elif f == 2:
                line[x] = (line[x]+b) & 255
            elif f == 3:
                line[x] = (line[x]+(a+b)//2) & 255
            elif f == 4:
                p = a+b-c
                pa, pb, pc = abs(p-a), abs(p-b), abs(p-c)
                line[x] = (line[x]+(a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append(line)
        prev = line
    return width, height, [[tuple(r[x*channels:x*channels+3]) for x in range(width)] for r in rows]


def load(path):
    out = {}
    if os.path.exists(path):
        for line in open(path):
            if line.strip():
                r = json.loads(line)
                out[r['k']] = r['v']
    return out


def main():
    ref_dir, cand_dir = sys.argv[1], sys.argv[2]
    ref, cand = load(os.path.join(ref_dir, 'values.jsonl')), load(os.path.join(cand_dir, 'values.jsonl'))
    bad = 0
    for k in sorted(ref.keys() | cand.keys()):
        if k not in cand:
            print(f'missing value {k}: unity {json.dumps(ref[k])}')
            bad += 1
        elif k not in ref:
            print(f'extra value {k}: unbound {json.dumps(cand[k])}')
            bad += 1
        elif ref[k] != cand[k]:
            print(f'value {k}: unity {json.dumps(ref[k])} unbound {json.dumps(cand[k])}')
            bad += 1
    names = sorted(n for n in os.listdir(ref_dir) if n.endswith('.png'))
    extra = sorted(n for n in os.listdir(cand_dir) if n.endswith('.png') and n not in names)
    for n in extra:
        print(f'extra image {n}')
    passed = 0
    for name in names:
        path = os.path.join(cand_dir, name)
        if not os.path.exists(path):
            print(f'missing image {name}')
            continue
        w1, h1, a = read_png(os.path.join(ref_dir, name))
        w2, h2, b = read_png(path)
        if (w1, h1) != (w2, h2):
            print(f'{name}: {w2}x{h2}, not {w1}x{h1}')
            continue
        count = total = worst = 0
        box = [w1, h1, -1, -1]
        for y in range(h1):
            for x in range(w1):
                d = max(abs(p-q) for p, q in zip(a[y][x], b[y][x]))
                total += d
                worst = max(worst, d)
                if d > TOLERANCE:
                    count += 1
                    box = [min(box[0], x), min(box[1], y), max(box[2], x), max(box[3], y)]
        if count == 0:
            passed += 1
        else:
            print(f'{name}: {count} pixels differ, mean {total/(w1*h1):.2f}, worst {worst}, box {box}')
    print(f'camerastack: {len(ref)-bad}/{len(ref)} values; {passed}/{len(names)} images pass'
          f'{"; " + str(len(extra)) + " extra images" if extra else ""}')
    return int(bad > 0 or passed != len(names) or bool(extra))


if __name__ == '__main__':
    sys.exit(main())
