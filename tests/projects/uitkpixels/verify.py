#!/usr/bin/env python3
"""Retain full RGBA arrays and JSON key unions; exact differences are never masked."""
import argparse
import hashlib
import json
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent


def flatten(value, path=""):
    result = {}
    if isinstance(value, dict):
        result[path + "/@keys"] = sorted(value)
        for key, item in value.items():
            result.update(flatten(item, path + "/" + str(key)))
    elif isinstance(value, list):
        result[path + "/@length"] = len(value)
        for key, item in enumerate(value):
            result.update(flatten(item, path + "/" + str(key)))
    else:
        result[path] = value
    return result


def json_union(reference, candidate):
    a = flatten([json.loads(s) for s in reference.read_text().splitlines() if s.strip()])
    b = flatten([json.loads(s) for s in candidate.read_text().splitlines() if s.strip()])
    failures = {k: {"reference": a.get(k), "candidate": b.get(k),
                    "missing": k not in b, "extra": k not in a}
                for k in sorted(a.keys() | b.keys()) if k not in a or k not in b or a[k] != b[k]}
    return {"observations": len(a.keys() | b.keys()), "failed": len(failures), "failures": failures}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--release", required=True)
    p.add_argument("--candidate", type=Path, required=True)
    p.add_argument("--before", type=Path)
    p.add_argument("--reference", type=Path)
    p.add_argument("--native-repeat", action="store_true")
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()
    reference = args.reference or ROOT / "Reference" / ("unity-" + args.release)
    original = ROOT.parent / "uitk" / "Reference"
    refs = {f.name: f for f in reference.glob("*.png")}
    for phase in ("initial", "final", "scaled"):
        refs["record-" + phase + ".png"] = original / ("unity-" + args.release + "-" + phase + ".png")
    names = refs.keys() | {f.name for f in args.candidate.glob("*.png")}
    if args.before:
        names |= {f.name for f in args.before.glob("*.png")}
    report = {"release": args.release, "images": {}, "json": json_union(
        original / ("unity-" + args.release + ".jsonl"), args.candidate / "record.jsonl")}
    arrays = {}
    for name in sorted(names):
        paths = {"reference": refs.get(name), "candidate": args.candidate / name}
        if args.before:
            paths["before"] = args.before / name
        missing = {k: str(v) for k, v in paths.items() if v is None or not v.is_file()}
        if missing:
            report["images"][name] = {"missing_or_extra": missing}
            continue
        images = {k: np.array(Image.open(v).convert("RGBA")) for k, v in paths.items()}
        for key, image in images.items():
            arrays[name + "/" + key] = image
        if len({v.shape for v in images.values()}) != 1:
            report["images"][name] = {"shapes": {k: list(v.shape) for k, v in images.items()}}
            continue
        delta = images["candidate"].astype(np.int16) - images["reference"].astype(np.int16)
        arrays[name + "/delta"] = delta
        result = {"channels": delta.size, "differing": int(np.count_nonzero(delta)),
                  "alpha_differing": int(np.count_nonzero(delta[:, :, 3])),
                  "worst": int(np.abs(delta).max()),
                  "pixels_over16": int(np.sum(np.abs(delta).max(2) > 16)),
                  "sha256": {k: hashlib.sha256(v.read_bytes()).hexdigest() for k, v in paths.items()}}
        if args.before:
            previous = images["before"].astype(np.int16) - images["reference"].astype(np.int16)
            arrays[name + "/before_delta"] = previous
            old, new = np.abs(previous), np.abs(delta)
            result.update(before_differing=int(np.count_nonzero(previous)),
                          resolved=int(np.sum((old != 0) & (new == 0))),
                          introduced=int(np.sum((old == 0) & (new != 0))),
                          improved=int(np.sum(new < old)), worsened=int(np.sum(new > old)),
                          retained=int(np.sum((old != 0) & (new != 0))))
        report["images"][name] = result
    args.output.parent.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(args.output.with_suffix(".npz"), **arrays)
    args.output.write_text(json.dumps(report, indent=2) + "\n")
    bad = report["json"]["failed"] or any(
        "differing" not in r or (r["differing"] if args.native_repeat else r.get("worsened", 0))
        for r in report["images"].values())
    print(json.dumps({"json": report["json"]["failed"], "images": report["images"]}, indent=2))
    raise SystemExit(bool(bad))


if __name__ == "__main__":
    main()
