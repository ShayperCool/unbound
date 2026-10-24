#!/usr/bin/env python3
"""Compares Unbound's URP probe image with Unity's: per-channel differences over the image and at a
few points (the cube, the sphere, the ground, the sky).

    python3 tests/projects/urp/compare.py tests/projects/urp/Reference/unity-6000.0.71f1.png /tmp/urp.png
"""
import struct
import sys
import zlib


def read_png(path):
    data = open(path, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", path
    pos, chunks = 8, {}
    idat = b""
    while pos < len(data):
        length, kind = struct.unpack(">I4s", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + length]
        if kind == b"IHDR":
            width, height, depth, color, _, _, interlace = struct.unpack(">IIBBBBB", body)
            assert depth == 8 and interlace == 0 and color in (2, 6), (depth, color, interlace)
            channels = 3 if color == 2 else 4
        elif kind == b"IDAT":
            idat += body
        pos += 12 + length
    raw = zlib.decompress(idat)
    stride = width * channels
    rows, prev = [], bytearray(stride)
    for y in range(height):
        f = raw[y * (stride + 1)]
        line = bytearray(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)])
        for x in range(stride):
            a = line[x - channels] if x >= channels else 0
            b = prev[x]
            c = prev[x - channels] if x >= channels else 0
            if f == 1:
                line[x] = (line[x] + a) & 255
            elif f == 2:
                line[x] = (line[x] + b) & 255
            elif f == 3:
                line[x] = (line[x] + (a + b) // 2) & 255
            elif f == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append(line)
        prev = line
    # Top row first, RGB.
    return width, height, [[tuple(r[x * channels:x * channels + 3]) for x in range(width)] for r in rows]


w1, h1, a = read_png(sys.argv[1])
w2, h2, b = read_png(sys.argv[2])
assert (w1, h1) == (w2, h2), ((w1, h1), (w2, h2))
total, worst, over = 0, 0, 0
for y in range(h1):
    for x in range(w1):
        d = max(abs(p - q) for p, q in zip(a[y][x], b[y][x]))
        total += d
        worst = max(worst, d)
        over += d > 8
print(f"mean max-channel difference {total / (w1 * h1):.2f}, worst {worst}, pixels over 8: {over} of {w1 * h1}")
for name, (x, y) in {"cube": (128, 90), "sphere": (190, 88), "ground": (160, 150), "sky": (160, 20)}.items():
    print(f"  {name:7} unity {a[y][x]}  unbound {b[y][x]}")
