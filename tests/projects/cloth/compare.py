#!/usr/bin/env python3
"""compare.py REFERENCE CANDIDATE: the cloth probe's records side by side (cloth.jsonl in each
directory). Vertices match within 5 mm at every sampled frame, normals within 0.1 (where both
have one: a folded cloth's summed normals can cancel to nothing); the other records exactly.
Scenarios listed as EXPECTED are sensitive (the cloth swings far, a tiny difference grows): they are
compared over their first 20 frames only, then reported with their largest difference."""
import json, os, sys

ABS, NORMAL = 0.005, 0.1

EXPECTED = {
    "capsule": "a difference of 1e-5 from step 3 grows as the cloth wraps around the capsule",
    "continuous": "a fast sphere tears through the cloth",
    "bones": "the bone swings the cloth far",
}


def load(directory):
    records = {}
    for line in open(os.path.join(directory, "cloth.jsonl")):
        line = line.strip()
        if line:
            r = json.loads(line)
            records[r["case"] + ":" + r.get("name", "")] = r
    return records


def worst(a, b):
    if len(a) != len(b):
        return float("inf")
    return max((max(abs(x - y) for x, y in zip(p, q)) for p, q in zip(a, b)), default=0.0)


def main():
    ref, cand = load(sys.argv[1]), load(sys.argv[2])
    matched = expected = differ = 0
    for key, a in ref.items():
        b = cand.get(key)
        name = a.get("name", "")
        if b is None:
            print(f"{key}: MISSING")
            differ += 1
            continue
        if a["case"] != "scenario":
            if a == b:
                matched += 1
            else:
                differ += 1
                print(f"{key}: {json.dumps({k: (a[k], b.get(k)) for k in a if a[k] != b.get(k)})[:300]}")
            continue
        problems = []
        if (a["count"], a["coefficients"]) != (b["count"], b["coefficients"]):
            problems.append(f"particles {a['count']}/{a['coefficients']} vs {b['count']}/{b['coefficients']}")
        largest = 0.0
        for sa, sb in zip(a["samples"], b["samples"]):
            e = worst(sa[2], sb[2])
            largest = max(largest, e)
            if e > ABS and (name not in EXPECTED or sa[0] <= 20):
                problems.append(f"frame {sa[0]}: vertices differ by {e:.4f}")
        if name not in EXPECTED:
            n = max((max(abs(x - y) for x, y in zip(p, q)) for p, q in zip(a["normals"], b["normals"]) if any(p) and any(q)), default=0.0)
            if n > NORMAL:
                problems.append(f"normals differ by {n:.3f}")
        if problems:
            differ += 1
            print(f"{key}: {problems[0]} ({len(problems)} problems)")
        elif name in EXPECTED:
            expected += 1
            print(f"{key}: differs as expected ({EXPECTED[name]}): largest {largest:.4f}")
        else:
            matched += 1
    print(f"cloth: {matched}/{len(ref)} records match, {expected} expected difference(s), {differ} differ")


main()
