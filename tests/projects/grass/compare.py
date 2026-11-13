#!/usr/bin/env python3
"""compare.py REFERENCE CANDIDATE: the grass probe's records side by side (grass.jsonl in each
directory), numbers within a tolerance; per case: how many records match and the largest
difference."""
import json, math, os, sys

TOLERANCE = 1e-4
# Known differences: (case, name) -> why.
EXPECTED = {}
# Not reproduced yet: prototypes scaled by detailObjectDensity.
for terrain in ("DensityCounts", "DensityCoverage"):
    for layer in (0, 1, 3):
        EXPECTED[("instances", terrain, layer)] = "useDensityScaling: the density does not scale the instances yet"
CLIP_TOLERANCE = None


def load(directory):
    records = []
    for line in open(os.path.join(directory, "grass.jsonl")):
        line = line.strip()
        if not line:
            continue
        try:
            records.append(json.loads(line))
        except json.JSONDecodeError:
            records.append({"case": "unparsed", "line": line})
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
    worst, problems = 0.0, []
    tolerance = None
    fa, fb = dict(numbers(a)), dict(numbers(b))
    for key in fa.keys() | fb.keys():
        x, y = fa.get(key, "<missing>"), fb.get(key, "<missing>")
        if isinstance(x, float) and isinstance(y, float) and (math.isnan(x) or math.isnan(y)):
            # NaN matches only NaN.
            if math.isnan(x) != math.isnan(y):
                problems.append(f"{key}: {x} vs {y}")
                worst = float("inf")
        elif isinstance(x, float) and isinstance(y, float):
            d = abs(x - y)
            worst = max(worst, d)
            if d > (tolerance if tolerance is not None else TOLERANCE * max(1.0, abs(x))):
                problems.append(f"{key}: {x} vs {y}")
        elif x != y:
            problems.append(f"{key}: {x} vs {y}")
            worst = float("inf")
    return worst, problems


def main():
    ref, cand = load(sys.argv[1]), load(sys.argv[2])
    by_case = {}
    expected = []
    for i in range(max(len(ref), len(cand))):
        a = ref[i] if i < len(ref) else {"case": "missing"}
        b = cand[i] if i < len(cand) else {"case": "missing"}
        case = a.get("case", "?") + (" " + str(a.get("terrain")) if "terrain" in a else "") + (" layer " + str(a.get("layer")) if "layer" in a else "")
        worst, problems = compare(a, b)
        why = EXPECTED.get((a.get("case"), a.get("terrain"), a.get("layer")))
        if why and problems:
            expected.append(f"{case}: {why}")
            problems = []
        stats = by_case.setdefault(case, [0, 0, 0.0, []])
        stats[0] += 1
        if not problems:
            stats[1] += 1
        stats[2] = max(stats[2], worst)
        if problems and len(stats[3]) < 3:
            stats[3].append(problems[:3])
    total = sum(s[0] for s in by_case.values())
    matched = sum(s[1] for s in by_case.values())
    for case, (n, ok, worst, examples) in by_case.items():
        print(f"{case}: {ok}/{n} match, largest difference {worst:.3g}")
        for e in examples:
            print("    " + "; ".join(e))
    from collections import Counter
    for e, n in Counter(expected).items():
        print(f"expected ({n} records): " + e)
    print(f"grass: {matched}/{total} records match, {len(expected)} expected difference(s)")


main()
