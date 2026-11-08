#!/usr/bin/env python3
"""Compares the reflection probe probe's output (Unity's reference directory, then Unbound's).

* values.jsonl record by record: numbers within 2e-3 or 1e-3 relative (as builtinfx), names and
  list lengths exactly; every reference record must be present.
* *.bin cube dumps mip by mip (RGB). Imported float cubes: every texel within 0.5% of the mip's
  largest value (the convolution's whole-level weights differ from the Editor's by a fraction of
  that). Byte cubes: within 1. Realtime (half float) cubes: within 1% of the mip's largest value,
  a face's outermost texels within 2% (the GPU's filtering across cube seams; a few 1/255).
* *.png: the mean over pixels of the largest channel difference at most 2 (as builtinfx); the
  worst pixel and pixels over 8 are reported.

usage: compare.py REFERENCE_DIR CANDIDATE_DIR [-q]
"""
import json
import os
import struct
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "urp"))
exec(open(os.path.join(os.path.dirname(__file__), "..", "urp", "compare.py")).read().split("\nw1, h1, a = read_png")[0])

IMAGE_MEAN = 2.0
FLOAT_SHARE = 0.005
HALF_SHARE = 0.01
HALF_EDGE_SHARE = 0.02
BYTE_STEPS = 1


def close(a, b):
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(close(x, y) for x, y in zip(a, b))
    if isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool) and not isinstance(b, bool):
        return abs(a - b) <= max(2e-3, 1e-3 * max(abs(a), abs(b)))
    return a == b


def load(path):
    out = {}
    if os.path.exists(path):
        for line in open(path):
            if line.strip():
                record = json.loads(line)
                out[record["k"]] = record["v"]
    return out


def read_cube(path):
    data = open(path, "rb").read()
    size, mips, width = struct.unpack_from("<iii", data)
    fmt = {4: "f", 2: "e", 1: "B"}[width]
    count = sum(max(1, size >> m) ** 2 for m in range(mips)) * 6 * 3
    values = struct.unpack_from(f"<{count}{fmt}", data, 12)
    faces, at = [], 0
    for _ in range(6):
        levels = []
        for m in range(mips):
            n = max(1, size >> m) ** 2 * 3
            levels.append(values[at:at + n])
            at += n
        faces.append(levels)
    return size, mips, width, faces


def compare_cube(name, ref_path, cand_path, quiet):
    if not os.path.exists(cand_path):
        print(f"cube {name}: MISSING")
        return False
    rs, rm, rw, ref = read_cube(ref_path)
    cs, cm, cw, cand = read_cube(cand_path)
    if (rs, rm, rw) != (cs, cm, cw):
        print(f"cube {name}: {cs} texels, {cm} mips, {cw}-byte channels; not {rs}, {rm}, {rw}")
        return False
    ok = True
    for mip in range(rm):
        side = max(1, rs >> mip)
        a = [v for f in range(6) for v in ref[f][mip]]
        b = [v for f in range(6) for v in cand[f][mip]]
        peak = max(abs(x) for x in a)
        # Per value: its texel's position on its face (an edge texel touches a seam).
        edge = [i // 3 % side in (0, side - 1) or i // 3 // side % side in (0, side - 1) for i in range(len(a))]
        worst = max(abs(x - y) for x, y in zip(a, b))
        if rw == 1:
            bound = BYTE_STEPS
            fail = worst > bound
        elif rw == 4:
            bound = FLOAT_SHARE * peak
            fail = worst > bound + 1e-9
        else:
            bound = HALF_SHARE * peak
            inner = max([abs(x - y) for x, y, e in zip(a, b, edge) if not e] or [0.0])
            fail = inner > bound + 1e-9 or worst > HALF_EDGE_SHARE * peak + 1e-9
        ok &= not fail
        if fail or not quiet:
            print(f"cube {name} mip {mip}: worst {worst:.4g} (bound {bound:.4g}, largest {peak:.4g}){' FAIL' if fail else ''}")
    return ok


def main():
    ref_dir, cand_dir = sys.argv[1], sys.argv[2]
    quiet = "-q" in sys.argv
    ref = load(os.path.join(ref_dir, "values.jsonl"))
    cand = load(os.path.join(cand_dir, "values.jsonl"))
    if not ref:
        print("reflectionprobes: reference contains no observations")
        return 1
    matched = differ = 0
    for key, value in ref.items():
        if key in cand and close(value, cand[key]):
            matched += 1
            continue
        differ += 1
        print(f"value {key}: unity {json.dumps(value)[:200]} unbound {json.dumps(cand.get(key, 'MISSING'))[:200]}")
    cubes = failed_cubes = 0
    images = over_images = 0
    worst_mean = 0.0
    for name in sorted(os.listdir(ref_dir)):
        path = os.path.join(cand_dir, name)
        if name.endswith(".bin"):
            cubes += 1
            failed_cubes += not compare_cube(name[:-4], os.path.join(ref_dir, name), path, quiet)
            continue
        if not name.endswith(".png"):
            continue
        images += 1
        if not os.path.exists(path):
            print(f"image {name[:-4]}: MISSING")
            over_images += 1
            worst_mean = float("inf")
            continue
        w1, h1, a = read_png(os.path.join(ref_dir, name))
        w2, h2, b = read_png(path)
        if (w1, h1) != (w2, h2):
            print(f"image {name[:-4]}: size {w2}x{h2}, not {w1}x{h1}")
            over_images += 1
            worst_mean = float("inf")
            continue
        total = worst = over = 0
        for y in range(h1):
            for x in range(w1):
                d = max(abs(p - q) for p, q in zip(a[y][x], b[y][x]))
                total += d
                worst = max(worst, d)
                over += d > 8
        mean = total / (w1 * h1)
        worst_mean = max(worst_mean, mean)
        over_images += mean > IMAGE_MEAN
        if mean > IMAGE_MEAN or not quiet:
            print(f"image {name[:-4]}: mean {mean:.2f}, worst {worst}, over 8: {over}")
    print(f"reflectionprobes: {matched}/{len(ref)} values match, {differ} differ; "
          f"{cubes - failed_cubes}/{cubes} cubes within bounds; "
          f"{images - over_images}/{images} images within a mean of {IMAGE_MEAN:g}, worst mean {worst_mean:.2f}")
    return int(bool(differ or failed_cubes or over_images))


sys.exit(main())
