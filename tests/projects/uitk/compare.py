#!/usr/bin/env python3
"""Compares Unbound's UI Toolkit probe record with Unity's.

    python3 tests/projects/uitk/compare.py tests/projects/uitk/Reference/unity-6000.0.71f1.jsonl /tmp/uitk/unbound.jsonl [-v]

Trees (per scenario): the elements in order (type, name, classes), their layout and world rects
(exact), text and resolved styles (exact). Steps: the events in order, the values and focus, the
watched elements. Images: the PNGs next to each record (`<record>-<scenario>.png`), per-channel
differences.
"""
import json
import os
import struct
import sys
import zlib


def load(path):
    records = [json.loads(line) for line in open(path) if line.strip()]
    trees, steps, other = {}, [], []
    for r in records:
        if r["k"] == "tree":
            trees.setdefault(r["scenario"], []).append(r)
        elif r["k"] == "step":
            steps.append(r)
        else:
            other.append(r)
    return trees, steps, other


def describe(r):
    return f'{r["type"]}#{r["name"] or ""}.{".".join(r["classes"])}'


def compare_trees(a, b, verbose):
    ok = True
    for scenario in a:
        ta, tb = a[scenario], b.get(scenario, [])
        if len(ta) != len(tb):
            print(f"[{scenario}] element count: unity {len(ta)}, unbound {len(tb)}")
            ok = False
        structure = layout = text = style = 0
        shown = 0
        limit = 1000 if verbose else 25
        for ra, rb in zip(ta, tb):
            if (ra["d"], describe(ra)) != (rb["d"], describe(rb)):
                structure += 1
                if shown < limit:
                    print(f"[{scenario}] #{ra['i']} structure: unity {'  ' * ra['d']}{describe(ra)}  unbound {'  ' * rb['d']}{describe(rb)}")
                    shown += 1
                continue
            if ra["layout"] != rb["layout"] or ra["world"] != rb["world"]:
                layout += 1
                if shown < limit:
                    print(f"[{scenario}] #{ra['i']} {describe(ra)} layout unity {ra['layout']} {ra['world']}  unbound {rb['layout']} {rb['world']}")
                    shown += 1
            if ra.get("text") != rb.get("text"):
                text += 1
                if shown < limit:
                    print(f"[{scenario}] #{ra['i']} {describe(ra)} text unity {ra.get('text')!r} unbound {rb.get('text')!r}")
                    shown += 1
            for key, va in ra["style"].items():
                vb = rb["style"].get(key)
                if va != vb:
                    style += 1
                    if shown < limit:
                        print(f"[{scenario}] #{ra['i']} {describe(ra)} style {key}: unity {va} unbound {vb}")
                        shown += 1
        n = min(len(ta), len(tb))
        print(f"[{scenario}] {n} elements: structure {n - structure}/{n}, layout {n - structure - layout}/{n - structure}, "
              f"text mismatches {text}, style mismatches {style}")
        ok = ok and structure == 0 and layout == 0 and text == 0 and style == 0
    return ok


def compare_steps(a, b, verbose):
    ok = True
    for sa, sb in zip(a, b):
        name = sa["step"]
        good = True
        ea, eb = sa["events"], sb["events"]
        if ea != eb:
            good = False
            print(f"[step {name}] events differ ({len(ea)} vs {len(eb)}):")
            for i in range(max(len(ea), len(eb))):
                xa = json.dumps(ea[i]) if i < len(ea) else "-"
                xb = json.dumps(eb[i]) if i < len(eb) else "-"
                mark = "  " if xa == xb else "! "
                if verbose or xa != xb:
                    print(f"   {mark}unity  {xa}\n   {mark}unbound {xb}")
        for key in sa:
            if key in ("events", "k", "watch"):
                continue
            if sa[key] != sb.get(key):
                good = False
                print(f"[step {name}] {key}: unity {sa[key]} unbound {sb.get(key)}")
        if sa.get("watch") != sb.get("watch"):
            good = False
            for wa, wb in zip(sa.get("watch", []), sb.get("watch", [])):
                if wa != wb:
                    print(f"[step {name}] watch unity {wa}\n{' ' * (len(name) + 14)}unbound {wb}")
        print(f"[step {name}] {'ok' if good else 'DIFFERS'}")
        ok = ok and good
    if len(a) != len(b):
        print(f"steps: unity {len(a)}, unbound {len(b)}")
        ok = False
    return ok


def read_png(path):
    data = open(path, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", path
    pos, idat = 8, b""
    while pos < len(data):
        length, kind = struct.unpack(">I4s", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + length]
        if kind == b"IHDR":
            width, height, depth, color, _, _, interlace = struct.unpack(">IIBBBBB", body)
            assert depth == 8 and interlace == 0 and color in (2, 6), (depth, color, interlace)
            channels = 3 if color == 2 else 4
        elif kind == b"IDAT":
            idat += body
        pos += 12 + length
    raw = zlib.decompress(idat)
    stride = width * channels
    rows, prev = [], bytearray(stride)
    for y in range(height):
        f = raw[y * (stride + 1)]
        line = bytearray(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)])
        for x in range(stride):
            a = line[x - channels] if x >= channels else 0
            b = prev[x]
            c = prev[x - channels] if x >= channels else 0
            if f == 1:
                line[x] = (line[x] + a) & 255
            elif f == 2:
                line[x] = (line[x] + b) & 255
            elif f == 3:
                line[x] = (line[x] + (a + b) // 2) & 255
            elif f == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append(line)
        prev = line
    return width, height, channels, rows


def compare_image(pa, pb):
    if not os.path.exists(pb):
        print(f"[image] {os.path.basename(pb)} missing")
        return False
    w1, h1, c1, a = read_png(pa)
    w2, h2, c2, b = read_png(pb)
    if (w1, h1) != (w2, h2):
        print(f"[image] size unity {w1}x{h1} unbound {w2}x{h2}")
        return False
    total, worst, over = 0, 0, 0
    for y in range(h1):
        ra, rb = a[y], b[y]
        for x in range(w1):
            d = max(abs(ra[x * c1 + k] - rb[x * c2 + k]) for k in range(3))
            total += d
            worst = max(worst, d)
            over += d > 16
    mean = total / (w1 * h1)
    print(f"[image] {os.path.basename(pa)}: mean max-channel difference {mean:.2f}, worst {worst}, pixels over 16: {over} of {w1 * h1}")
    return mean < 2


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("-")]
    verbose = "-v" in sys.argv
    ref, out = args
    ta, sa, _ = load(ref)
    tb, sb, ob = load(out)
    for r in ob:
        if r["k"] == "error":
            print("unbound error:", r["message"])
    ok = compare_trees(ta, tb, verbose)
    ok = compare_steps(sa, sb, verbose) and ok
    ref_base, out_base = os.path.splitext(ref)[0], os.path.splitext(out)[0]
    for scenario in ("initial", "final", "scaled"):
        if os.path.exists(f"{ref_base}-{scenario}.png"):
            ok = compare_image(f"{ref_base}-{scenario}.png", f"{out_base}-{scenario}.png") and ok
    print("MATCH" if ok else "DIFFERENT")
    sys.exit(0 if ok else 1)


main()
