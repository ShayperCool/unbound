#!/usr/bin/env python3
"""compare.py REFERENCE CANDIDATE: the settings probe's records side by side (settings.jsonl in each
directory). Properties compare per file, in the iterator's order (path, type, depth, array size,
value; floats within 1e-6 relative; a reference's m_FileID, an instance ID, only as zero or not);
the other records whole. Prints each file's first difference
and a summary; exits non-zero when anything differs."""
import json, os, sys


def load(directory):
    return [json.loads(l) for l in open(os.path.join(directory, "settings.jsonl")) if l.strip()]


def same(a, b):
    if isinstance(a, float) or isinstance(b, float):
        try:
            return abs(float(a) - float(b)) <= 1e-6 * max(1.0, abs(float(a)))
        except (TypeError, ValueError):
            return False
    return a == b


def diff(a, b):
    for k in a:
        if k not in b:
            return (k, a[k], "<missing>")
        if not same(a[k], b[k]):
            return (k, a[k], b[k])
    return None


def main():
    ref, cand = load(sys.argv[1]), load(sys.argv[2])
    props = lambda rows: {}
    by_file = {}
    for side, rows in (("ref", ref), ("cand", cand)):
        for r in rows:
            if r["case"] == "property":
                by_file.setdefault(r["file"], {"ref": [], "cand": []})[side].append(r)
    differ = matched = 0
    for f, sides in sorted(by_file.items()):
        a, b = sides["ref"], sides["cand"]
        bad = None
        for i, x in enumerate(a):
            if i >= len(b):
                bad = f"{x['path']}: missing (Unbound has {len(b)} properties, Unity {len(a)})"
                break
            want, got = {k: v for k, v in x.items() if k != "index"}, dict(b[i])
            if x["name"] == "m_FileID" and x["path"].endswith(".m_FileID"):
                # A loaded object's instance ID: only whether there is one compares.
                want["value"], got["value"] = bool(want["value"]), bool(got.get("value"))
            d = diff(want, got)
            if d:
                bad = f"[{i}] {x['path']}: {d[0]}: {json.dumps(d[1])} vs {json.dumps(d[2])} (Unbound at {b[i].get('path')})"
                break
        if bad is None and len(b) > len(a):
            bad = f"Unbound has {len(b)} properties, Unity {len(a)}"
        if bad:
            differ += 1
            print(f"properties {f}: {bad}")
        else:
            matched += 1
    keyed = lambda rows: {(r["case"], r.get("file", r.get("name", ""))): r for r in rows if r["case"] != "property"}
    kr, kc = keyed(ref), keyed(cand)
    for key, a in kr.items():
        b = kc.get(key)
        if b is None:
            differ += 1
            print(f"{key}: MISSING")
            continue
        d = diff(a, b)
        if d:
            differ += 1
            print(f"{key}: {d[0]}: {json.dumps(d[1])[:300]} vs {json.dumps(d[2])[:300]}")
        else:
            matched += 1
    if any(r["case"] == "error" for r in cand):
        differ += 1
        print("error:", next(r for r in cand if r["case"] == "error")["message"][:2000])
    print(f"settings: {matched}/{matched + differ} records match, {differ} differ")
    sys.exit(1 if differ else 0)


if __name__ == "__main__":
    main()
