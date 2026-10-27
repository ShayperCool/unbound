#!/usr/bin/env python3
"""compare.py REFERENCE CANDIDATE: the audio-from-bundle probe's records side by side (audio.jsonl
in each directory). Numbers match within 1e-4 (a Vorbis sample may be a unit of the last bit off:
FMOD's decoder is not libvorbis), the sum of squares within relative 1e-3; everything else exactly.
A project asset's samples are its source's, not the lossy codec's, so only PCM assets compare their
samples. For each record, the first value that differs; EXPECTED marks the known differences."""
import json, os, sys

ABS, REL = 1e-4, 1e-5
ENERGY_REL = 1e-3

# Below 32 kHz FMOD's Vorbis setup headers are its own: Unbound reports the clip and plays nothing.
EXPECTED = {"clip:vorbis30_mono_22050"}


def load(directory):
    records = {}
    for line in open(os.path.join(directory, "audio.jsonl")):
        line = line.strip()
        if line:
            r = json.loads(line)
            records[r["case"] + ":" + r.get("name", "")] = r
    return records


def first_difference(a, b, path=""):
    if isinstance(a, bool) or isinstance(b, bool) or isinstance(a, str) or a is None:
        return None if a == b else (path, a, b)
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        rel = ENERGY_REL if path == ".energy" else REL
        return None if abs(a - b) <= max(ABS, rel * abs(a)) else (path, a, b)
    if isinstance(a, list) and isinstance(b, list):
        if len(a) != len(b):
            return (path + ".length", len(a), len(b))
        for i, (x, y) in enumerate(zip(a, b)):
            d = first_difference(x, y, f"{path}[{i}]")
            if d:
                return d
        return None
    if isinstance(a, dict) and isinstance(b, dict):
        for k in a:
            if k not in b:
                return (f"{path}.{k}", a[k], "<missing>")
            d = first_difference(a[k], b[k], f"{path}.{k}")
            if d:
                return d
        return None
    return None if a == b else (path, a, b)


def main():
    ref, cand = load(sys.argv[1]), load(sys.argv[2])
    matched = differ = expected = 0
    for key, a in ref.items():
        b = cand.get(key)
        if b is None:
            print(f"{key}: MISSING")
            differ += 1
            continue
        if a["case"] == "asset" and not a["name"].startswith("pcm"):
            a = {k: v for k, v in a.items() if k not in ("picked", "energy")}
        d = first_difference(a, b)
        if d and key in EXPECTED:
            expected += 1
            print(f"{key}: EXPECTED {d[0]}: {json.dumps(d[1])[:200]} vs {json.dumps(d[2])[:200]}")
        elif d:
            differ += 1
            print(f"{key}: {d[0]}: {json.dumps(d[1])[:200]} vs {json.dumps(d[2])[:200]}")
        else:
            matched += 1
    print(f"audio: {matched}/{len(ref)} records match, {expected} expected, {differ} differ")
    sys.exit(1 if differ else 0)


if __name__ == "__main__":
    main()
