#!/usr/bin/env python3
"""compare.py REFERENCE CANDIDATE [--residuals RESIDUALS.json]: the terrain probe's records side by
side (terrain.jsonl in each directory), numbers within a tolerance; per case: how many records
match and the largest difference. Without --residuals it prints and exits 0; with a residual list
(tests/residuals.py; keys "<case>[<n>]<path>", the n-th record of the case, bounded by "max_abs")
it exits 1 when a difference is not listed there within its bound."""
import json, os, sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
import residuals as residual_lists  # noqa: E402

TOLERANCE = 1e-4


def load(directory):
    records = []
    for line in open(os.path.join(directory, "terrain.jsonl")):
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


def compare(a, b, failing=None):
    """The largest difference and the problems; `failing`, when given, receives each failing
    path's difference (infinite for a missing value or one of another kind)."""
    worst, problems = 0.0, []
    fa, fb = dict(numbers(a)), dict(numbers(b))
    for key in sorted(fa.keys() | fb.keys()):
        x, y = fa.get(key, "<missing>"), fb.get(key, "<missing>")
        if isinstance(x, float) and isinstance(y, float):
            d = abs(x - y)
            worst = max(worst, d)
            if d > TOLERANCE * max(1.0, abs(x)):
                problems.append(f"{key}: {x} vs {y}")
                if failing is not None:
                    failing[key] = d
        elif x != y:
            problems.append(f"{key}: {x} vs {y}")
            worst = float("inf")
            if failing is not None:
                failing[key] = float("inf")
    return worst, problems


def main():
    args = sys.argv[1:]
    listed = None
    if "--residuals" in args:
        at = args.index("--residuals")
        listed = residual_lists.load(args[at + 1])
        del args[at:at + 2]
    ref, cand = load(args[0]), load(args[1])
    verdict = residual_lists.Gate(listed or {})
    reference_keys = set()
    by_case = {}
    for i in range(max(len(ref), len(cand))):
        a = ref[i] if i < len(ref) else {"case": "missing"}
        b = cand[i] if i < len(cand) else {"case": "missing"}
        case = a.get("case", "?")
        index = f"{case}[{by_case.get(case, [0])[0]}]"
        failing = {}
        worst, problems = compare(a, b, failing)
        reference_keys |= {index + path for path, _ in numbers(a)}
        for path, _ in numbers(a):
            if path not in failing:
                verdict.exact_key(index + path)
        for path, difference in failing.items():
            verdict.differs(index + path, {"max_abs": difference})
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
    if listed is not None:
        verdict.finish(reference_keys)
        print("\n".join(verdict.lines()))
    print(f"terrain: {matched}/{total} records match")
    return 0 if listed is None or verdict.ok else 1


sys.exit(main())
