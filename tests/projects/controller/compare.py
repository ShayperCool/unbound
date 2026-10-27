#!/usr/bin/env python3
"""compare.py REFERENCE CANDIDATE: the controller probe's records side by side (controller.jsonl in
each directory). Numbers match within 0.2% (at least 0.002 absolute), flags and names exactly. A hit
point on a face is undetermined along the face (PhysX picks it from the triangle that wins a tie), so
hit points need only lie on the same plane: the reference point's along its normal."""
import json, os, sys

REL, ABS = 0.002, 0.002

KNOWN = {}


def load(directory):
    records = {}
    for line in open(os.path.join(directory, "controller.jsonl")):
        line = line.strip()
        if line:
            r = json.loads(line)
            records[r["case"] + ":" + r.get("name", "")] = r
    return records


def close(a, b):
    if isinstance(a, list):
        return isinstance(b, list) and len(a) == len(b) and all(close(x, y) for x, y in zip(a, b))
    if isinstance(a, dict):
        return isinstance(b, dict) and a.keys() == b.keys() and all(close(a[k], b[k]) for k in a)
    if isinstance(a, bool) or isinstance(b, bool):
        return a == b
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        return abs(a - b) <= max(ABS, REL * abs(a))
    return a == b


def hit_close(a, b):
    if a.keys() != b.keys():
        return False
    if "collider" not in a:
        return a == b
    if any(not close(a[k], b[k]) for k in a if k != "point"):
        return False
    offset = sum((p - q) * n for p, q, n in zip(a["point"], b["point"], a["normal"]))
    return abs(offset) <= ABS


def step_problem(a, b):
    """What differs in a step: [index, state, hits, triggers]."""
    if not close(a[1], b[1]):
        return f"state {a[1]} vs {b[1]}"
    if len(a[2]) != len(b[2]) or not all(hit_close(x, y) for x, y in zip(a[2], b[2])):
        return f"hits {json.dumps(a[2])} vs {json.dumps(b[2])}"
    if a[3] != b[3]:
        return f"triggers {a[3]} vs {b[3]}"
    return None


def main():
    ref, cand = load(sys.argv[1]), load(sys.argv[2])
    matched = known = differ = 0
    for key, a in ref.items():
        b = cand.get(key)
        name = a.get("name", "")
        if b is None:
            print(f"{key}: MISSING")
            differ += 1
            continue
        if a["case"] == "scenario":
            problems = [(sa[0], step_problem(sa, sb)) for sa, sb in zip(a["steps"], b["steps"])]
            problems = [p for p in problems if p[1]]
            if len(a["steps"]) != len(b["steps"]):
                problems.append((len(b["steps"]), f"{len(a['steps'])} steps vs {len(b['steps'])}"))
        else:
            problems = [] if close(a, b) else [(0, f"{json.dumps(a)} vs {json.dumps(b)}")]
        if not problems:
            matched += 1
            continue
        if name in KNOWN:
            known += 1
            print(f"{key}: known difference ({KNOWN[name]}): {len(problems)} steps")
            continue
        differ += 1
        step, what = problems[0]
        print(f"{key}: {len(problems)} steps differ; first, step {step}: {what[:400]}")
    print(f"controller: {matched}/{len(ref)} records match, {known} known difference(s), {differ} differ")


main()
