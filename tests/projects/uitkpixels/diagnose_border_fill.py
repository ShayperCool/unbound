#!/usr/bin/env python3
"""Offline corner algebra; reads captures, never launches a renderer or changes acceptance.

Retains complete RGBA/key unions, including rejected trials. The local pixel equations
are hypotheses, not rendered candidates. Their domain is derived from input geometry,
without selecting pixels by their observed error. Every equation disagreement is kept.
"""
import argparse
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import subprocess
import sys

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent
COLORS = np.array([[192, 64, 192], [224, 192, 64],
                   [64, 192, 96], [64, 128, 224]], dtype=float)
RADII = [0, 2, 4, 8, 12, 18]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest() if path.is_file() else None


def rgba(path):
    return np.array(Image.open(path).convert("RGBA"))


def stats(delta):
    return dict(differing=int(np.count_nonzero(delta)),
                alpha_differing=int(np.count_nonzero(delta[:, :, 3])),
                worst=int(np.abs(delta).max()))


def quantize(value):
    return np.floor(value + 0.5).astype(int).tolist()


def blend(background, color, alpha):
    return np.asarray(background) * (1 - alpha) + color * alpha


def corner_equations(scale, images):
    """All four corners of all bordered cells, including radius-zero controls.

    Meshes first generated at scale 1 retain their widths when scale changes to 1.25.
    Their 48x36 layout sizes stay the same; world positions round at the new scale.
    These inputs follow PixelProbe and UaRetainedGeometry, not a fit to RGB values.
    """
    observations = []
    for row, radius in enumerate(RADII):
        for col in range(1, 10):
            widths = [2 if col == 1 else col, 2,
                      2 if col == 1 else 4, 2 if col == 1 else 6]
            left = math.floor((12 + col * 61 + (col % 3) * .2) * scale + .5)
            top = math.floor((12 + row * 63 + (row % 3) * .2) * scale + .5)
            right, bottom = left + 48 * scale, top + 36 * scale
            corners = [
                (left, top, 1, 1, 0, 1),
                (right, top, -1, 1, 2, 1),
                (right, bottom, -1, -1, 2, 3),
                (left, bottom, 1, -1, 0, 3),
            ]
            for corner, (ox, oy, dx, dy, vi, hi) in enumerate(corners):
                if radius > widths[vi] and radius > widths[hi]:
                    continue  # Two positive inner radii use the existing ellipse path.
                ix = ox + dx * widths[vi] * scale
                iy = oy + dy * widths[hi] * scale
                for y in range(math.ceil(iy - 1), math.floor(iy) + 1):
                    for x in range(math.ceil(ix - 1), math.floor(ix) + 1):
                        px, py = x + .5, y + .5
                        if abs(px - ix) > .5 or abs(py - iy) > .5:
                            continue
                        av = max(0, min(1, .5 - (px - ix) * dx))
                        ah = max(0, min(1, .5 - (py - iy) * dy))
                        line = ((px - ox) * dx * widths[hi]
                                - (py - oy) * dy * widths[vi])
                        side = hi if line >= 0 else vi
                        reference = images["native_g"][y, x, :3].astype(int).tolist()
                        predictions = {
                            "single_side": quantize(blend(48, COLORS[side], ah if side == hi else av)),
                            "vertical_then_horizontal": quantize(blend(blend(48, COLORS[vi], av), COLORS[hi], ah)),
                            "horizontal_then_vertical": quantize(blend(blend(48, COLORS[hi], ah), COLORS[vi], av)),
                            "product_vertical": quantize(blend(48, COLORS[vi], av * ah)),
                            "product_horizontal": quantize(blend(48, COLORS[hi], av * ah)),
                        }
                        observations.append(dict(
                            scale=scale, row=row, col=col, radius=radius, corner=corner,
                            widths=widths, outer=[ox, oy], inner=[ix, iy], xy=[x, y],
                            inner_radii=[radius - widths[vi], radius - widths[hi]],
                            coverage_vertical=av, coverage_horizontal=ah,
                            diagonal=line, diagonal_tie=line == 0,
                            in_inner_quadrant=(px - ix) * dx >= 0 and (py - iy) * dy >= 0,
                            reference=reference,
                            captures={key: value[y, x].tolist() for key, value in images.items()},
                            predictions=predictions,
                            exact={key: value == reference for key, value in predictions.items()},
                        ))
    return observations


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--release", required=True)
    parser.add_argument("--run", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    run, output = args.run.expanduser().resolve(), args.output.expanduser().resolve()
    cache = (Path.home() / ".cache/unbound/validation/runs").resolve()
    if not output.is_relative_to(cache) or output.exists():
        parser.error("output must be a fresh directory under ~/.cache/unbound/validation/runs")
    output.mkdir(parents=True)
    spec = importlib.util.spec_from_file_location("pixel_verify", ROOT / "verify.py")
    verifier = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(verifier)
    reference = ROOT / "Reference" / ("unity-" + args.release)
    originals = ROOT.parent / "uitk/Reference"
    sources = dict(native_g=run / "native-g/capture", native_h=run / "native-h/capture",
                   accepted=run / "sampler11-pixels/capture",
                   before=run / "before-pixels3/capture",
                   rejected2=run / "candidate2-pixels/capture",
                   rejected3=run / "candidate3-pixels/capture")
    refs = {p.name: p for p in reference.glob("*.png")}
    for phase in ("initial", "final", "scaled"):
        refs["record-" + phase + ".png"] = originals / ("unity-" + args.release + "-" + phase + ".png")
    names = set(refs)
    for source in sources.values():
        names.update(p.name for p in source.glob("*.png"))
    report = dict(release=args.release, command=sys.argv, scope="Offline historic captures only; no current-runtime replay",
                  source_head=subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
                  sources={key: str(value) for key, value in sources.items()}, images={}, json={}, cells={},
                  mechanism="Independent side coverage and compositing at degenerate inner corners",
                  acceptance="Unchanged; local equations are diagnostic hypotheses, never acceptance masks")
    report["geometry_sources"] = {str(path): digest(path) for path in (
        ROOT / "Assets/Scripts/PixelProbe.cs",
        ROOT.parents[2] / "managed/UnityEngine/UIElements/Rendering/UaPainter.Shapes.cs",
        ROOT.parents[2] / "managed/UnityEngine/UIElements/Rendering/UaPainter.Retained.cs")}
    report["capture_metadata"] = {}
    for key, source in sources.items():
        execution = source.parent / "execution.json"
        staged = source.parent / "project/Assets/Scripts/PixelProbe.cs"
        report["capture_metadata"][key] = {
            str(execution): digest(execution), str(staged): digest(staged)}
        if execution.is_file():
            assert json.loads(execution.read_text())["release"] == args.release, execution
    all_equations = []
    for name in sorted(names):
        paths = {key: source / name for key, source in sources.items()}
        paths["reference"] = refs.get(name)
        entry = dict(paths={k: str(v) if v else None for k, v in paths.items()},
                     sha256={k: digest(v) if v else None for k, v in paths.items()},
                     missing=[k for k, v in paths.items() if v is None or not v.is_file()], values={})
        arrays = {key: rgba(path) for key, path in paths.items() if path and path.is_file()}
        archive = dict(arrays)
        if "native_g" in arrays and "native_h" in arrays:
            entry["native_repeat_exact"] = np.array_equal(arrays["native_g"], arrays["native_h"])
            assert entry["native_repeat_exact"], name
            assert np.array_equal(arrays["reference"], arrays["native_g"]), name
        if "reference" in arrays:
            a = arrays["reference"].astype(np.int16)
            for key, value in arrays.items():
                if key == "reference":
                    continue
                if value.shape != a.shape:
                    entry["values"][key] = dict(shape=list(value.shape), reference_shape=list(a.shape))
                    continue
                delta = value.astype(np.int16) - a
                archive[key + "/signed_delta"] = delta
                archive[key + "/failing_flat_indices"] = np.flatnonzero(delta).astype(np.int32)
                entry["values"][key] = stats(delta)
                if key.startswith("rejected") and "accepted" in arrays:
                    before = np.abs(arrays["accepted"].astype(np.int16) - a)
                    after = np.abs(delta)
                    entry["values"][key]["against_accepted"] = dict(
                        resolved=int(np.sum((before != 0) & (after == 0))),
                        introduced=int(np.sum((before == 0) & (after != 0))),
                        retained=int(np.sum((before != 0) & (after != 0))),
                        worsened=int(np.sum(after > before)))
        np.savez_compressed(output / (name + ".npz"), **archive)
        report["images"][name] = entry
        if name in ("shapes-1.png", "shapes-1.25.png"):
            scale = 1 if name == "shapes-1.png" else 1.25
            all_equations.extend(corner_equations(scale, arrays))
            cells = []
            for row, radius in enumerate(RADII):
                for col in range(10):
                    x = (12 + col * 61 + col % 3 * .2) * scale
                    y = (12 + row * 63 + row % 3 * .2) * scale
                    region = np.s_[int(y) - 2:int(y + 36 * scale) + 3,
                                   int(x) - 2:int(x + 48 * scale) + 3]
                    a = arrays["reference"][region].astype(np.int16)
                    cells.append(dict(row=row, col=col, radius=radius,
                                      values={k: stats(v[region].astype(np.int16) - a)
                                              for k, v in arrays.items()}))
            report["cells"][name] = cells
    for filename, ref in (("record.jsonl", originals / ("unity-" + args.release + ".jsonl")),
                          ("pixels.jsonl", reference / "pixels.jsonl")):
        report["json"][filename] = {}
        for key, source in sources.items():
            path = source / filename
            result = verifier.json_union(ref, path) if path.is_file() else {"missing": str(path)}
            report["json"][filename][key] = result
    # Test the same algebra on every eligible corner, retaining all counterexamples.
    overlap_domain = [v for v in all_equations if v["radius"] > 0
                      and max(v["inner_radii"]) <= 0 and v["in_inner_quadrant"]
                      and v["coverage_vertical"] > 0 and v["coverage_horizontal"] > 0]
    report["corner_equations"] = all_equations
    report["overlap_hypothesis"] = dict(
        domain="Nonzero outer radius, both inner radii <= 0, inside inner quadrant, both coverages > 0",
        observations=len(overlap_domain),
        per_corner={str(c): dict(observations=sum(v["corner"] == c for v in overlap_domain),
                                exact=sum(v["corner"] == c and v["exact"]["vertical_then_horizontal"] for v in overlap_domain))
                    for c in range(4)},
        failures=[v for v in overlap_domain if not v["exact"]["vertical_then_horizontal"]],
        successes=[v for v in overlap_domain if v["exact"]["vertical_then_horizontal"]],
        decision="No general correction established; do not apply a production hunk")
    report["script_sha256"] = digest(Path(__file__))
    (output / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(dict(images={k: v["values"].get("accepted", v["missing"])
                                 for k, v in report["images"].items()},
                          overlap={k: v for k, v in report["overlap_hypothesis"].items()
                                   if k not in ("failures", "successes")}), indent=2))


if __name__ == "__main__":
    main()
