#!/usr/bin/env python3
"""compare.py REFERENCE CANDIDATE: the Editor UI probe's calls side by side (editorui.jsonl in each
directory): each call's result, exception and logs. Floats within 1e-4. EXPECTED marks known
differences. Prints each differing call; exits non-zero when any differs unexpectedly."""
import json, os, sys

EXPECTED = {
    # Unity's reorderable list keeps its elements' heights from its first layout: an element
    # expanded after the list was measured once does not count yet.
    "EditorGUI.GetPropertyHeight.pairs.expandedElements": "the reorderable list's height cache",
}


def load(directory):
    rows = [json.loads(l) for l in open(os.path.join(directory, "editorui.jsonl")) if l.strip()]
    return {r.get("name", r["case"]): r for r in rows}


def same(a, b):
    if isinstance(a, bool) or isinstance(b, bool):
        return a == b
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        return abs(a - b) <= 1e-4 * max(1.0, abs(a))
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(same(x, y) for x, y in zip(a, b))
    if isinstance(a, dict) and isinstance(b, dict):
        return a.keys() == b.keys() and all(same(a[k], b[k]) for k in a)
    return a == b


def main():
    ref, cand = load(sys.argv[1]), load(sys.argv[2])
    matched = differ = expected = 0
    for name, a in ref.items():
        b = cand.get(name)
        bad = None
        if b is None:
            bad = "MISSING"
        else:
            for k in ("result", "error", "logs"):
                if not same(a.get(k), b.get(k)):
                    bad = f"{k}: {json.dumps(a.get(k))[:200]} vs {json.dumps(b.get(k))[:200]}"
                    break
        if bad is None:
            matched += 1
        elif name in EXPECTED:
            expected += 1
            print(f"{name}: EXPECTED ({EXPECTED[name]}) {bad}")
        else:
            differ += 1
            print(f"{name}: {bad}")
    if "error" in cand:
        differ += 1
        print("error:", cand["error"]["message"][:1500])
    print(f"editorui: {matched}/{len(ref)} calls match, {expected} expected, {differ} differ")
    sys.exit(1 if differ else 0)


if __name__ == "__main__":
    main()
