#!/usr/bin/env python3
"""compare.py REFERENCE CANDIDATE: the video probe's records side by side (video.jsonl in each
directory). Everything but the frames' pixel samples must be equal (numbers within 1e-4); the
samples count as matching within PIXEL_TOLERANCE per channel, and are tallied apart."""
import json, os, sys

TOLERANCE = 1e-4
PIXEL_TOLERANCE = 4


def load(directory):
    return [json.loads(line) for line in open(os.path.join(directory, "video.jsonl")) if line.strip()]


def flat(value, path=""):
    if isinstance(value, list):
        for i, v in enumerate(value):
            yield from flat(v, f"{path}[{i}]")
    elif isinstance(value, dict):
        for k, v in value.items():
            yield from flat(v, f"{path}.{k}")
    else:
        yield path, value


def main():
    ref, cand = load(sys.argv[1]), load(sys.argv[2])
    matched, pixels, pixels_ok, worst_pixel = 0, 0, 0, 0
    for i in range(max(len(ref), len(cand))):
        a = ref[i] if i < len(ref) else {"case": "missing"}
        b = cand[i] if i < len(cand) else {"case": "missing"}
        problems = []
        pa, pb = a.pop("pixels", []), b.pop("pixels", [])
        for x, y in zip(pa, pb):
            pixels += 1
            d = max(abs(p - q) for p, q in zip(x, y))
            worst_pixel = max(worst_pixel, d)
            if d <= PIXEL_TOLERANCE:
                pixels_ok += 1
        if len(pa) != len(pb):
            problems.append(f"pixels: {len(pa)} vs {len(pb)} samples")
        fa, fb = dict(flat(a)), dict(flat(b))
        for key in sorted(fa.keys() | fb.keys()):
            x, y = fa.get(key, "<missing>"), fb.get(key, "<missing>")
            numeric = all(isinstance(v, (int, float)) and not isinstance(v, bool) for v in (x, y))
            if numeric and abs(x - y) <= TOLERANCE * max(1.0, abs(x)):
                continue
            if x != y:
                problems.append(f"{key}: {x} vs {y}")
        if problems:
            print(f"{a.get('case', '?')}: " + "; ".join(problems[:4]))
        else:
            matched += 1
    print(f"video: {matched}/{max(len(ref), len(cand))} records match, pixels {pixels_ok}/{pixels} within {PIXEL_TOLERANCE} (worst {worst_pixel})")


main()
