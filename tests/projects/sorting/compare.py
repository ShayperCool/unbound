#!/usr/bin/env python3
"""Compare the sorting probe's winners: Unity's reference against a Unbound run.

    compare.py REFERENCE.json CELLS.json SCREENSHOT.png

CELLS.json is what the probe wrote under Unbound (cell names and pixels, bottom-left origin);
SCREENSHOT.png is the window's image from `unbound run --screenshot`. Needs ffmpeg to decode it.
"""
import json
import subprocess
import sys


def winner(r, g, b):
    if r > 127 and g < 128:
        return "canvas"
    if g > 127 and r < 128:
        return "renderer"
    return f"none({r / 255:.2f},{g / 255:.2f},{b / 255:.2f})"


def main():
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    reference = {c["name"]: c["winner"] for c in json.load(open(sys.argv[1]))["cells"]}
    cells = json.load(open(sys.argv[2]))["cells"]
    probe = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries",
                            "stream=width,height", "-of", "csv=p=0", sys.argv[3]],
                           capture_output=True, text=True, check=True).stdout.strip()
    width, height = (int(v) for v in probe.split(","))
    rgb = subprocess.run(["ffmpeg", "-v", "error", "-i", sys.argv[3], "-f", "rawvideo", "-pix_fmt",
                          "rgb24", "-"], capture_output=True, check=True).stdout
    differ = 0
    for cell in cells:
        x, y = cell["x"], height - 1 - cell["y"]
        i = (y * width + x) * 3
        got = winner(rgb[i], rgb[i + 1], rgb[i + 2])
        want = reference.get(cell["name"], "?")
        mark = "  " if got == want else "!!"
        differ += got != want
        print(f"{mark} {cell['name']:32} unity {want:10} unbound {got}")
    print(f"{differ} of {len(cells)} differ")
    sys.exit(1 if differ else 0)


if __name__ == "__main__":
    main()
