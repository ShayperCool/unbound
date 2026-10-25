#!/usr/bin/env python3
"""Compares the particles probe's images (Unity's reference directory, then Unbound's): per image,
the mean over pixels of the largest channel difference (0-255), the worst pixel, and how many
pixels differ by more than 8.

usage: compare_images.py REFERENCE_DIR CANDIDATE_DIR
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "urp"))
exec(open(os.path.join(os.path.dirname(__file__), "..", "urp", "compare.py")).read().split("\nw1, h1, a = read_png")[0])

ref_dir, cand_dir = sys.argv[1], sys.argv[2]
worst_mean = 0.0
for name in sorted(os.listdir(ref_dir)):
    if not name.endswith(".png"):
        continue
    cand = os.path.join(cand_dir, name)
    if not os.path.exists(cand):
        print(f"{name}: MISSING")
        worst_mean = float("inf")
        continue
    w1, h1, a = read_png(os.path.join(ref_dir, name))
    w2, h2, b = read_png(cand)
    total = worst = over = 0
    for y in range(h1):
        for x in range(w1):
            d = max(abs(p - q) for p, q in zip(a[y][x], b[y][x]))
            total += d
            worst = max(worst, d)
            over += d > 8
    mean = total / (w1 * h1)
    worst_mean = max(worst_mean, mean)
    print(f"{name[:-4]}: mean {mean:.2f}, worst {worst}, over 8: {over}")
print(f"worst mean {worst_mean:.2f}")
