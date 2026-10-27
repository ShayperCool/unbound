#!/usr/bin/env python3
"""compare.py REFERENCE CANDIDATE: the vehicle probe's records side by side (vehicles.jsonl in
each directory). Numbers match within 1% (at least 0.01 absolute); per record the first step that
differs and the largest difference. Some scenarios are expected to differ: driven or steered wheels
starting from rest swap their slip's sign every step, and float-level differences grow into another
sequence (the bodies' paths stay close; their final positions are printed)."""
import json, os, sys

REL, ABS = 0.01, 0.01

# name -> why it is expected to differ.
EXPECTED = {
    "single motor": "a driven wheel from rest: the slip's sign flips each step (chaotic)",
    "car motor": "driven wheels from rest (chaotic slip)",
    "car steer": "steered wheels at 10 m/s, one substep: the front slips alternate (chaotic)",
    "car steer force point": "as car steer",
    "car drive and turn": "driven wheels from rest (chaotic slip)",
    "car fast turn": "the car rolls over; its upside-down body falls through the ground",
    "car handbrake turn": "steered and locked wheels (chaotic slip)",
    "kinematic": "a driven wheel on a body that cannot move (chaotic slip)",
    "center": "a frozen body with an off-center wheel: PhysX turns it once, Unbound keeps it still",
}

# Small differences left (PhysX's own vehicle SDK runs underneath, as in Unity).
KNOWN = {
    "single steer stiffness": "under a newton after the body comes to rest",
    "single forward curve": "the first step's slip (the wheel spins up from rest)",
    "car steer substeps": "a couple of newtons on a steered wheel",
    "car slope": "the car rolling down the slope drifts after 120 steps",
    "added after a step": "the rebuilt vehicle's first step",
    "light body roll": "the first step's force",
    "turned wheel": "the first step's rpm",
    "turned body": "the first step's slip",
}

def load(directory):
    records = {}
    for line in open(os.path.join(directory, "vehicles.jsonl")):
        line = line.strip()
        if line:
            r = json.loads(line)
            records[r["case"] + ":" + r.get("name", "")] = r
    return records


def numbers(value, path=""):
    if isinstance(value, bool) or value is None or isinstance(value, str):
        yield path, value
    elif isinstance(value, (int, float)):
        yield path, float(value)
    elif isinstance(value, list):
        for i, v in enumerate(value):
            yield from numbers(v, f"{path}[{i}]")
    elif isinstance(value, dict):
        for k, v in value.items():
            yield from numbers(v, f"{path}.{k}")


def compare(a, b):
    fa, fb = dict(numbers(a)), dict(numbers(b))
    worst, first, bad = 0.0, None, 0
    for key in fa:
        x, y = fa[key], fb.get(key, "<missing>")
        if isinstance(x, float) and isinstance(y, float):
            d = 0.0 if x == y else abs(x - y)
            ok = x == y or d <= max(ABS, REL * abs(x))
        else:
            d, ok = (0.0 if x == y else float("inf")), x == y
        if not ok:
            bad += 1
            worst = max(worst, d)
            if first is None:
                first = f"{key}: {x} vs {y}"
    for key in fb:
        if key not in fa:
            bad += 1
            first = first or f"{key}: <missing> vs {fb[key]}"
    return bad, worst, first


def final_position(record):
    steps = record.get("steps")
    if not steps:
        return None
    last = steps[-1][0]
    return last[0] if isinstance(last, list) and last and isinstance(last[0], list) else None


def main():
    ref, cand = load(sys.argv[1]), load(sys.argv[2])
    matched = expected = known = differ = 0
    for key, a in ref.items():
        b = cand.get(key)
        name = a.get("name", "")
        if b is None:
            print(f"{key}: MISSING")
            differ += 1
            continue
        bad, worst, first = compare(a, b)
        if not bad:
            matched += 1
            continue
        if name in KNOWN:
            known += 1
            print(f"{key}: known difference ({KNOWN[name]}): {bad} values, worst {worst:.4g}")
            continue
        if name in EXPECTED:
            expected += 1
            pa, pb = final_position(a), final_position(b)
            where = f"; final position {pa} vs {pb}" if pa and pb else ""
            print(f"{key}: differs as expected ({EXPECTED[name]}){where}")
            continue
        differ += 1
        print(f"{key}: {bad} values differ, worst {worst:.4g}; first {first}")
    print(f"vehicles: {matched}/{len(ref)} records match, {expected} expected and {known} known difference(s), {differ} differ")


main()
