#!/usr/bin/env python3
"""Read saved line mask arrays; test empty-stencil image algebra without rendering.

No builds, captures, geometry reconstruction or comparison-tolerance changes.
Inputs are the arrays already retained by the combined line comparator. Output
must be a new directory in Library/Unbound or an Unbound cache.
"""

import argparse
import json
from pathlib import Path

import numpy as np


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--release", required=True, choices=["6000.0.71f1"])
    parser.add_argument("--arrays", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    resolved = args.out.resolve()
    if not ("Library/Unbound" in str(resolved) or "unbound" in str(resolved.parent).lower()):
        parser.error("outputs must be in Library/Unbound or an Unbound cache")
    args.out.mkdir(parents=True, exist_ok=False)
    tolerance = 2 / 255
    data = {}
    inputs = {}
    for interaction in ("VisibleInsideMask", "VisibleOutsideMask"):
        for version in ("reference", "root", "blend", "final"):
            path = args.arrays / f"img.mask.{interaction}.{version}.npy"
            array = np.load(path, allow_pickle=False).reshape(64, 96, 4)
            if not np.isfinite(array).all():
                raise ValueError(f"nonfinite input: {path}")
            key = f"{interaction}_{version}"
            data[key] = array
            inputs[key] = str(path.resolve())

    inside = data["VisibleInsideMask_final"]
    outside = data["VisibleOutsideMask_final"]
    # The saved engine's complementary stencil tests partition the same line.
    # This sum retains all float channels; it is not a boundary/pixel mask.
    overlap = np.any(inside != 0, axis=-1) & np.any(outside != 0, axis=-1)
    if overlap.any():
        raise ValueError("saved inside/outside images overlap; cannot add them as a partition")
    data["VisibleInsideMask_prediction"] = np.zeros_like(inside)
    data["VisibleOutsideMask_prediction"] = inside + outside
    result = {
        "release": args.release,
        "kind": "offline empty-stencil prediction, not runtime acceptance",
        "inputs": inputs,
        "pixel_tolerance": tolerance,
        "partition_overlap_pixels": int(overlap.sum()),
        "images": {},
    }
    for interaction in ("VisibleInsideMask", "VisibleOutsideMask"):
        arrays = {v: data[f"{interaction}_{v}"] for v in ("reference", "root", "blend", "final", "prediction")}
        reference = arrays["reference"].astype(np.float64)
        errors = {v: np.abs(a.astype(np.float64) - reference) for v, a in arrays.items()}
        for v, error in errors.items():
            data[f"{interaction}_{v}_absolute_error"] = error
        summary = {}
        for v, a in arrays.items():
            error = errors[v]
            summary[v] = {
                "nonzero_pixels": int(np.any(a != 0, axis=-1).sum()),
                "failing_pixels": int(np.any(error > tolerance, axis=-1).sum()),
                "failing_channels": int((error > tolerance).sum()),
                "max_error": float(error.max()),
                "mean_error": float(error.mean()),
            }
        changes = {}
        for v in ("final", "prediction"):
            changes[v] = {}
            for base in ("root", "blend", "final"):
                changed = arrays[v] != arrays[base]
                worse = errors[v] > errors[base]
                introduced = (errors[v] > tolerance) & (errors[base] <= tolerance)
                new_pixels = np.any(errors[v] > tolerance, axis=-1) & ~np.any(errors[base] > tolerance, axis=-1)
                data[f"{interaction}_{base}_to_{v}_error_increase"] = worse
                data[f"{interaction}_{base}_to_{v}_new_failure"] = introduced
                changes[v][base] = {
                    "changed_channels": int(changed.sum()),
                    "raw_error_increases": int(worse.sum()),
                    "newly_failing_pixels": int(new_pixels.sum()),
                    "newly_failing_channels": int(introduced.sum()),
                    "newly_failing_channel_indices": np.flatnonzero(introduced).tolist(),
                    "error_increase_channel_indices": np.flatnonzero(worse).tolist(),
                }
        # Preserve every differing/failing component and all versions' raw values.
        relevant = np.zeros_like(reference, dtype=bool)
        for v, a in arrays.items():
            relevant |= (a != arrays["reference"]) | (a != arrays["final"])
        components = []
        for index in np.flatnonzero(relevant):
            components.append({
                "channel_index": int(index),
                "pixel_index": int(index // 4),
                "component": "rgba"[index % 4],
                "raw": {v: float(a.flat[index]) for v, a in arrays.items()},
                "absolute_errors": {v: float(e.flat[index]) for v, e in errors.items()},
            })
        result["images"][interaction] = {"summary": summary, "changes": changes, "components": components}
    np.savez_compressed(args.out / "all-raw-colors-and-errors.npz", **data)
    (args.out / "algebra.json").write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps({k: v["summary"] for k, v in result["images"].items()}, indent=2))


if __name__ == "__main__":
    main()
