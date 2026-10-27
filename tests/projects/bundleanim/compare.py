#!/usr/bin/env python3
"""compare.py REFERENCE CANDIDATE: the animation-from-bundle probe's records side by side (anim.jsonl
in each directory). Numbers match within 1e-4 (at least relative 1e-5); everything else exactly.
For each record, the first value that differs."""
import json, os, sys

ABS, REL = 1e-4, 1e-5


def load(directory):
    records = {}
    for line in open(os.path.join(directory, "anim.jsonl")):
        line = line.strip()
        if line:
            r = json.loads(line)
            records[r["case"] + ":" + r.get("name", "")] = r
    return records


def first_difference(a, b, path=""):
    if isinstance(a, bool) or isinstance(b, bool) or isinstance(a, str) or a is None:
        return None if a == b else (path, a, b)
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        return None if abs(a - b) <= max(ABS, REL * abs(a)) else (path, a, b)
    if isinstance(a, list) and isinstance(b, list):
        if len(a) != len(b):
            return (path + ".length", len(a), len(b))
        for i, (x, y) in enumerate(zip(a, b)):
            d = first_difference(x, y, f"{path}[{i}]")
            if d:
                return d
        return None
    if isinstance(a, dict) and isinstance(b, dict):
        for k in a:
            if k not in b:
                return (f"{path}.{k}", a[k], "<missing>")
            d = first_difference(a[k], b[k], f"{path}.{k}")
            if d:
                return d
        return None
    return None if a == b else (path, a, b)


def main():
    ref, cand = load(sys.argv[1]), load(sys.argv[2])
    matched = differ = 0
    for key, a in ref.items():
        b = cand.get(key)
        if b is None:
            print(f"{key}: MISSING")
            differ += 1
            continue
        d = first_difference(a, b)
        if d:
            differ += 1
            print(f"{key}: {d[0]}: {json.dumps(d[1])[:200]} vs {json.dumps(d[2])[:200]}")
        else:
            matched += 1
    print(f"anim: {matched}/{len(ref)} records match, {differ} differ")


main()
