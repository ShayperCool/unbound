#!/usr/bin/env python3
"""Compares two 2D probe recordings (Unity's reference, then Unbound's) value by value.

usage: compare.py REFERENCE.json CANDIDATE.json [--scenario PREFIX] [--details N] [--tolerance T]
                  [--images DIR]

Numbers match within the tolerance (relative above 1); `*.image` values ("WxH:rrggbb...") match
when every pixel is within 2/255 per channel; --images writes both images and their difference
as PNG files for the images that differ.
"""
import json
import os
import math
import struct
import sys
import zlib


def png(path, w, h, pixels):
    rows = b"".join(b"\x00" + bytes(pixels[(h - 1 - y) * w * 3:(h - y) * w * 3]) for y in range(h))

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b""))


def image(value):
    size, data = value.split(":")
    w, h = map(int, size.split("x"))
    return w, h, bytes.fromhex(data)


def compare_image(key, a, b, images):
    wa, ha, pa = image(a)
    wb, hb, pb = image(b)
    if (wa, ha) != (wb, hb):
        return f"size {wa}x{ha} vs {wb}x{hb}"
    bad = 0
    first = None
    diff = bytearray(len(pa))
    for i in range(0, len(pa), 3):
        d = max(abs(pa[i + c] - pb[i + c]) for c in range(3))
        if d > 2:
            bad += 1
            diff[i:i + 3] = b"\xff\x00\xff"
            if first is None:
                p = i // 3
                first = f"({p % wa},{p // wa}) {pa[i:i + 3].hex()} vs {pb[i:i + 3].hex()}"
        else:
            diff[i:i + 3] = bytes(v // 3 for v in pa[i:i + 3])
    if not bad:
        return None
    if images:
        os.makedirs(images, exist_ok=True)
        stem = f"{images}/{key}"
        png(stem + ".unity.png", wa, ha, pa)
        png(stem + ".unbound.png", wa, ha, pb)
        png(stem + ".diff.png", wa, ha, diff)
    return f"{bad}/{wa * ha} pixels differ, first {first}"


def main():
    args = sys.argv[1:]
    prefix, details, tolerance, images = "", 8, 1e-3, None
    for flag in ("--scenario", "--details", "--tolerance", "--images"):
        if flag in args:
            i = args.index(flag)
            value = args[i + 1]
            del args[i:i + 2]
            if flag == "--scenario": prefix = value
            elif flag == "--details": details = int(value)
            elif flag == "--tolerance": tolerance = float(value)
            else: images = value
    ref = json.load(open(args[0]))
    cand = json.load(open(args[1]))
    total_bad = 0
    for scenario, values in ref.items():
        if not scenario.startswith(prefix):
            continue
        other = cand.get(scenario)
        if other is None:
            print(f"{scenario}: MISSING in candidate")
            total_bad += 1
            continue
        if "error" in other and "error" not in values:
            print(f"{scenario}: candidate failed: {other['error']}")
            total_bad += 1
            continue
        problems = []
        for key, a in values.items():
            if key not in other:
                problems.append((math.inf, key, a, "<missing>"))
                continue
            b = other[key]
            if key.endswith(".image"):
                result = compare_image(f"{scenario}.{key[:-6]}", a, b, images)
                if result:
                    problems.append((math.inf, key, result, ""))
            elif isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool):
                diff = abs(a - b)
                if diff > tolerance * max(1.0, abs(a)):
                    problems.append((diff, key, a, b))
            elif a != b:
                problems.append((math.inf, key, a, b))
        for k in other:
            if k not in values:
                problems.append((math.inf, k, "<missing>", other[k]))
        if not problems:
            print(f"{scenario}: ok ({len(values)} values)")
            continue
        total_bad += 1
        worst = max(p[0] for p in problems)
        print(f"{scenario}: {len(problems)}/{len(values)} differ (worst {worst:.4g})")
        order = list(values.keys())
        shown = sorted(problems, key=lambda p: order.index(p[1]) if p[1] in values else 1e9)
        for diff, key, a, b in shown[:details]:
            print(f"    {key}: unity {a!r}  unbound {b!r}")
    print(f"\n{total_bad} scenario(s) differ")
    return 1 if total_bad else 0


sys.exit(main())
