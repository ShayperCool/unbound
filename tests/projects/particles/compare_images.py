#!/usr/bin/env python3
"""Compares a probe's images (Unity's reference directory, then Unbound's): per image, the mean
over pixels of the largest channel difference (0-255), the worst pixel, and how many pixels differ
by more than 8.

usage: compare_images.py REFERENCE_DIR CANDIDATE_DIR [--gate [RESIDUALS.json]]
(--gate takes the argument after it as the residual list only when it ends in .json)

Without --gate the comparison is a measurement and always exits 0. With --gate every image must
match its reference pixel for pixel, except those RESIDUALS.json lists, each as
{"<image>": {"pixels": N, "worst": W, "reason": "..."}}: at most N pixels may differ, by at most W.
It exits 1 when an image is missing or goes beyond that, and names a listed image that now matches
exactly (NOW MATCHES) so that its entry can be withdrawn.
"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "urp"))
exec(open(os.path.join(os.path.dirname(__file__), "..", "urp", "compare.py")).read().split("\nw1, h1, a = read_png")[0])

args = sys.argv[1:]
gate = "--gate" in args
residuals = {}
if gate:
    at = args.index("--gate")
    if at + 1 < len(args) and args[at + 1].endswith(".json"):
        with open(args[at + 1]) as handle:
            residuals = json.load(handle)
        del args[at + 1]
    del args[at]
ref_dir, cand_dir = args[0], args[1]
worst_mean = 0.0
exact = recorded = 0
failed = []
for name in sorted(os.listdir(ref_dir)):
    if not name.endswith(".png"):
        continue
    key = name[:-4]
    cand = os.path.join(cand_dir, name)
    if not os.path.exists(cand):
        print(f"{name}: MISSING")
        worst_mean = float("inf")
        failed.append(f"{key}: missing")
        continue
    w1, h1, a = read_png(os.path.join(ref_dir, name))
    w2, h2, b = read_png(cand)
    if (w1, h1) != (w2, h2):
        print(f"{name}: size {w2}x{h2}, reference {w1}x{h1}")
        worst_mean = float("inf")
        failed.append(f"{key}: size {w2}x{h2}, reference {w1}x{h1}")
        continue
    total = worst = over = differing = 0
    for y in range(h1):
        for x in range(w1):
            d = max(abs(p - q) for p, q in zip(a[y][x], b[y][x]))
            total += d
            worst = max(worst, d)
            over += d > 8
            differing += d > 0
    mean = total / (w1 * h1)
    worst_mean = max(worst_mean, mean)
    print(f"{key}: mean {mean:.2f}, worst {worst}, over 8: {over}")
    if not gate:
        continue
    allowed = residuals.get(key)
    if differing == 0:
        exact += 1
        if allowed is not None:
            print(f"NOW MATCHES {key}: its recorded residual can be withdrawn")
    elif allowed is not None and differing <= allowed["pixels"] and worst <= allowed["worst"]:
        recorded += 1
        print(f"recorded {key}: {differing} pixels differ by up to {worst} ({allowed['reason']})")
    else:
        bound = f"recorded {allowed['pixels']} pixels up to {allowed['worst']}" if allowed else "not recorded"
        failed.append(f"{key}: {differing} pixels differ by up to {worst} ({bound})")
for key in sorted(set(residuals) - {n[:-4] for n in os.listdir(ref_dir) if n.endswith(".png")}):
    failed.append(f"{key}: a recorded residual for an image the reference does not have")
print(f"worst mean {worst_mean:.2f}")
if gate:
    for line in failed:
        print(f"FAIL {line}")
    print(f"gate: {exact} exact, {recorded} within recorded residuals, {len(failed)} failed")
    sys.exit(1 if failed else 0)
