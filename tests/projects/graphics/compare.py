#!/usr/bin/env python3
"""Compare the graphics probe's output under Unbound with Unity's reference.

    compare.py REFERENCE.json UNBOUND.json

Pixels match within 2/255 (8-bit targets) or 1e-3 (half floats); a case's first differing pixels
are shown as (x, y) unity -> unbound.
"""
import json
import sys


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    reference = {c["name"]: c for c in json.load(open(sys.argv[1]))["cases"]}
    got = {c["name"]: c for c in json.load(open(sys.argv[2]))["cases"]}
    differ = 0
    for name, want in reference.items():
        have = got.get(name)
        if have is None:
            print(f"!! {name:28} missing")
            differ += 1
            continue
        if "floats" in want:
            have_floats = have.get("floats", [])
            ok = len(have_floats) == len(want["floats"]) and all(abs(a - b) <= 1e-4 for a, b in zip(want["floats"], have_floats))
            print(f"{'  ' if ok else '!!'} {name:28} unity {want['floats']} unbound {have_floats}")
            differ += not ok
            continue
        if "value" in want:
            ok = want["value"] == have.get("value")
            print(f"{'  ' if ok else '!!'} {name:28} unity {want['value']} unbound {have.get('value')}")
            differ += not ok
            continue
        tolerance = 1e-3 if any(abs(v) > 1 or v < 0 for p in want["pixels"] for v in p) else 2 / 255 + 1e-4
        bad = []
        for i, (a, b) in enumerate(zip(want["pixels"], have["pixels"])):
            if any(abs(x - y) > tolerance for x, y in zip(a, b)):
                bad.append((i % 8, i // 8, a, b))
        ok = not bad and len(want["pixels"]) == len(have["pixels"])
        detail = "" if ok else "  " + "; ".join(f"({x},{y}) {a} -> {b}" for x, y, a, b in bad[:3]) + (f" (+{len(bad) - 3})" if len(bad) > 3 else "")
        print(f"{'  ' if ok else '!!'} {name:28}{detail}")
        differ += not ok
    print(f"{differ} of {len(reference)} differ")
    sys.exit(1 if differ else 0)


if __name__ == "__main__":
    main()
