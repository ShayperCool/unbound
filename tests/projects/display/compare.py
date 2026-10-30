#!/usr/bin/env python3
"""Compares the display probe (Unity's reference directory, then Unbound's output directory and its
window screenshot): values.jsonl record by record, then the Game view image (Unity's capture)
against the screenshot.

usage: compare.py REFERENCE_DIR CANDIDATE_DIR SCREENSHOT.png
"""
import json
import os
import sys

exec(open(os.path.join(os.path.dirname(__file__), "..", "urp", "compare.py")).read().split("\nw1, h1, a = read_png")[0])


def load(path):
    out = {}
    if os.path.exists(path):
        for line in open(path):
            if line.strip():
                r = json.loads(line)
                out[r["k"]] = r["v"]
    return out


ref_dir, cand_dir, shot = sys.argv[1], sys.argv[2], sys.argv[3]
ref = load(os.path.join(ref_dir, "values.jsonl"))
cand = load(os.path.join(cand_dir, "values.jsonl"))
matched = 0
for k, v in ref.items():
    if cand.get(k) == v:
        matched += 1
    else:
        print(f"value {k}: unity {v} unbound {cand.get(k, 'MISSING')}")
w1, h1, a = read_png(os.path.join(ref_dir, "game.png"))
w2, h2, b = read_png(shot)
if (w1, h1) != (w2, h2):
    print(f"display: {matched}/{len(ref)} values match; screenshot {w2}x{h2}, not {w1}x{h1}")
    sys.exit()
total = worst = 0
for y in range(h1):
    for x in range(w1):
        d = max(abs(p - q) for p, q in zip(a[y][x], b[y][x]))
        total += d
        worst = max(worst, d)
print(f"display: {matched}/{len(ref)} values match; image mean {total / (w1 * h1):.2f}, worst {worst}")
