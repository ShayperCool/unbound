#!/usr/bin/env python3
"""Compares the built-in pipeline features probe's output (Unity's reference directory, then Unbound's):
values.jsonl record by record (numbers within 2e-3, or 1e-3 relative), then every PNG (mean over
pixels of the largest channel difference, the worst pixel, pixels over 8). Differing values and
images EXPECTED lists differ for a reason the README gives and are counted apart.

usage: compare.py REFERENCE_DIR CANDIDATE_DIR [-q]
"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "urp"))
exec(open(os.path.join(os.path.dirname(__file__), "..", "urp", "compare.py")).read().split("\nw1, h1, a = read_png")[0])

import re

# Values and images that differ for a reason the README gives.
EXPECTED = [
    r"^material\.crc\.values$",
]
EXPECTED_IMAGES = []


def close(a, b):
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(close(x, y) for x, y in zip(a, b))
    if isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool) and not isinstance(b, bool):
        return abs(a - b) <= max(2e-3, 1e-3 * max(abs(a), abs(b)))
    return a == b


def load(path):
    out = {}
    if not os.path.exists(path):
        return out
    for line in open(path):
        line = line.strip()
        if line:
            record = json.loads(line)
            out[record["k"]] = record["v"]
    return out


def main():
    ref_dir, cand_dir = sys.argv[1], sys.argv[2]
    quiet = "-q" in sys.argv
    ref = load(os.path.join(ref_dir, "values.jsonl"))
    cand = load(os.path.join(cand_dir, "values.jsonl"))
    matched = differ = expected = 0
    for key, value in ref.items():
        if key in cand and close(value, cand[key]):
            matched += 1
            continue
        if any(re.match(p, key) for p in EXPECTED):
            expected += 1
            continue
        differ += 1
        if not quiet:
            print(f"value {key}: unity {json.dumps(value)[:200]} unbound {json.dumps(cand.get(key, 'MISSING'))[:200]}")
    worst_mean = 0.0
    images = over_images = 0
    expected_images = 0
    for name in sorted(os.listdir(ref_dir)):
        if not name.endswith(".png"):
            continue
        if any(re.match(p, name[:-4]) for p in EXPECTED_IMAGES):
            expected_images += 1
            continue
        images += 1
        path = os.path.join(cand_dir, name)
        if not os.path.exists(path):
            print(f"image {name[:-4]}: MISSING")
            worst_mean = float("inf")
            continue
        w1, h1, a = read_png(os.path.join(ref_dir, name))
        w2, h2, b = read_png(path)
        if (w1, h1) != (w2, h2):
            print(f"image {name[:-4]}: size {w2}x{h2}, not {w1}x{h1}")
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
        if mean > 2.0:
            over_images += 1
        if not quiet:
            print(f"image {name[:-4]}: mean {mean:.2f}, worst {worst}, over 8: {over}")
    print(f"builtinfx: {matched}/{len(ref)} values match, {differ} differ, {expected} expected; "
          f"{images - over_images}/{images} images within a mean of 2 ({expected_images} expected), worst mean {worst_mean:.2f}")


main()
