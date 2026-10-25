#!/usr/bin/env python3
"""compare.py REFERENCE CANDIDATE: the LOD probe's records (lod.jsonl in each directory). Sweeps
compare their levels up to a distance of 15 (farther the cube covers less than a pixel and the
center pixel's coverage, not the level, decides); other records compare whole."""
import json, os, sys


def load(d):
    return [json.loads(l) for l in open(os.path.join(d, "lod.jsonl")) if l.strip()]


ref, cand = load(sys.argv[1]), load(sys.argv[2])
bad = 0
for a, b in zip(ref, cand):
    name = a.get("name", a["case"])
    if "levels" in a:
        limit = 726 if a["case"] == "sweep" else len(a["levels"])
        la, lb = a["levels"][:limit], b.get("levels", [])[:limit]
        diffs = [i for i in range(min(len(la), len(lb))) if la[i] != lb[i]]
        if diffs or len(la) != len(lb):
            bad += 1
            step, start = (0.02, 0.5) if a["case"] == "sweep" else (0.01, 0.2)
            print(f"{name}: {len(diffs)} samples differ, first at {start + step * diffs[0]:.2f}" if diffs else f"{name}: lengths differ")
        else:
            print(f"{name}: ok")
    else:
        same = {k: v for k, v in a.items() if k != "startBias" and k != "startMax"} == {k: v for k, v in b.items() if k != "startBias" and k != "startMax"}
        if not same:
            bad += 1
            print(f"{name}: {a} vs {b}")
        else:
            print(f"{name}: ok")
if len(ref) != len(cand):
    bad += 1
    print(f"records: {len(ref)} vs {len(cand)}")
print(f"lod: {bad} record(s) differ")
