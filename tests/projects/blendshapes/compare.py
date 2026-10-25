#!/usr/bin/env python3
"""compare.py REFERENCE CANDIDATE: the blend shape probe's records side by side (blend.jsonl in each
directory), numbers within a tolerance; per case: how many records match and the largest
difference."""
import json, os, sys

TOLERANCE = 1e-4
# Known differences: (case, name) -> why. Clip samples are compared within CLIP_TOLERANCE (Unity
# reduces the imported keys; Unbound interpolates ufbx's baked keys).
EXPECTED = {
    ("misuse", "vertices change"): "Unbound accepts a vertex array smaller than the triangles reference (Unity logs an error and ignores it)",
}
CLIP_TOLERANCE = 0.1


def load(directory):
    records = []
    for line in open(os.path.join(directory, "blend.jsonl")):
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
    tolerance = CLIP_TOLERANCE if a.get("case") == "clip" else None
    fa, fb = dict(numbers(a)), dict(numbers(b))
    for key in fa.keys() | fb.keys():
        x, y = fa.get(key, "<missing>"), fb.get(key, "<missing>")
        if isinstance(x, float) and isinstance(y, float):
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
        case = a.get("case", "?") + (" " + str(a.get("mesh")) if "mesh" in a else "")
        worst, problems = compare(a, b)
        why = EXPECTED.get((a.get("case"), a.get("name")))
        if why and problems:
            expected.append(f"{case} {a.get('name')}: {why}")
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
    for e in expected:
        print("expected: " + e)
    print(f"blend: {matched}/{total} records match, {len(expected)} expected difference(s)")


main()
