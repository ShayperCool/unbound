#!/usr/bin/env python3
"""Shared driver for probes whose runtime script writes keyed values (and optional images).

A probe's script writes `values.jsonl` into the directory named by its output variable: one
`{"k": key, "v": value}` line per observation (keys unique), the last line
`{"k": "complete", "v": true}`. Images are PNG files next to it. The probe's `run.py` calls
`main(...)` with its project directory, Editor method and output variable:

    run.py record --label L     one Editor capture on the GPU display (run cache)
    run.py publish A B          two byte-identical captures -> Reference/unity-<release>/
    run.py gate [--label L]     the engine on a fresh copy, compared with Reference/

Values compare exactly, except numbers: |expected - actual| <= `tolerance` (the probe's own,
named in its README). Every missing, extra and differing key is printed. Images print their
mean and worst max-channel difference and the pixels over 8 (no verdict; the suite tracks them).

A probe may list known residuals in `residuals.json` next to its run.py: `{reason: [key, ...]}`,
open differences with a named cause and follow-up. Such a key that differs is printed with its
reason and does not fail the gate; one that matches again is printed as resolved (remove it).
Every other difference fails.
"""
import argparse
import json
import math
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import time
import zlib

TESTS = Path(__file__).resolve().parent
sys.path.insert(0, str(TESTS))
from probe_paths import run_root, reference_root, unity_version  # noqa: E402
import editor_runner  # noqa: E402

REPO = TESTS.parent
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
VALUES = 'values.jsonl'
COMPLETE = {'k': 'complete', 'v': True}


def assemble(probe, out):
    target = out/'project'
    if target.exists():
        raise SystemExit(f'refusing to overwrite {target}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(probe/part, target/part)
    (target/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+unity_version()+'\n')
    return target


def read_values(path):
    rows = [json.loads(line) for line in Path(path).read_text().splitlines() if line.strip()]
    if not rows or rows[-1] != COMPLETE:
        raise ValueError(f'{path} has no completion marker')
    values = {}
    for row in rows[:-1]:
        if row['k'] in values:
            raise ValueError(f'duplicate key {row["k"]}')
        values[row['k']] = row['v']
    return values


def same(expected, actual, tolerance):
    if isinstance(expected, bool) or isinstance(actual, bool):
        return expected == actual
    if isinstance(expected, (int, float)) and isinstance(actual, (int, float)):
        if math.isnan(expected) or math.isnan(actual):
            return math.isnan(expected) and math.isnan(actual)
        return abs(expected-actual) <= tolerance
    if isinstance(expected, list) and isinstance(actual, list):
        return len(expected) == len(actual) and all(same(e, a, tolerance) for e, a in zip(expected, actual))
    if isinstance(expected, dict) and isinstance(actual, dict):
        return expected.keys() == actual.keys() and all(same(expected[k], actual[k], tolerance) for k in expected)
    return expected == actual


def compare(expected, actual, tolerance):
    """(matched, failures): every key of either side that is missing, extra or different."""
    failures, matched = [], 0
    for key in sorted(expected.keys() | actual.keys()):
        if key not in actual:
            failures.append({'key': key, 'missing': expected[key]})
        elif key not in expected:
            failures.append({'key': key, 'extra': actual[key]})
        elif not same(expected[key], actual[key], tolerance):
            failures.append({'key': key, 'expected': expected[key], 'actual': actual[key]})
        else:
            matched += 1
    return matched, failures


def read_png(path):
    """(width, height, rows of RGBA bytes) of an 8-bit RGB/RGBA non-interlaced PNG (what
    Texture2D.EncodeToPNG writes), decoded without third-party modules."""
    data = Path(path).read_bytes()
    if data[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError(f'{path} is not a PNG')
    pos, idat, header = 8, b'', None
    while pos < len(data):
        length, kind = struct.unpack('>I4s', data[pos:pos+8])
        chunk = data[pos+8:pos+8+length]
        pos += 12+length
        if kind == b'IHDR':
            header = struct.unpack('>IIBBBBB', chunk)
        elif kind == b'IDAT':
            idat += chunk
    width, height, depth, color, _, _, interlace = header
    if depth != 8 or color not in (2, 6) or interlace:
        raise ValueError(f'{path}: unsupported PNG layout {header}')
    channels = 4 if color == 6 else 3
    raw = zlib.decompress(idat)
    stride = width*channels
    rows, previous = [], bytearray(stride)
    for y in range(height):
        kind, line = raw[y*(stride+1)], bytearray(raw[y*(stride+1)+1:(y+1)*(stride+1)])
        for i in range(stride):
            a = line[i-channels] if i >= channels else 0
            b = previous[i]
            c = previous[i-channels] if i >= channels else 0
            if kind == 1:
                line[i] = (line[i]+a) & 255
            elif kind == 2:
                line[i] = (line[i]+b) & 255
            elif kind == 3:
                line[i] = (line[i]+(a+b)//2) & 255
            elif kind == 4:
                p = a+b-c
                pa, pb, pc = abs(p-a), abs(p-b), abs(p-c)
                line[i] = (line[i]+(a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append(bytes(line) if channels == 4 else bytes(
            v for x in range(width) for v in (*line[x*3:x*3+3], 255)))
        previous = line
    return width, height, rows


def image_difference(expected, actual):
    """(mean, worst, over 8) of the per-pixel max-channel difference, or None if sizes differ."""
    wa, ha, a = read_png(expected)
    wb, hb, b = read_png(actual)
    if (wa, ha) != (wb, hb):
        return None
    total = worst = over = 0
    for ra, rb in zip(a, b):
        for x in range(0, len(ra), 4):
            d = max(abs(ra[x+i]-rb[x+i]) for i in range(4))
            total += d
            worst = max(worst, d)
            over += d > 8
    return total/(wa*ha), worst, over


def residuals(probe):
    """{key: reason} of the probe's residuals.json (none without one)."""
    path = Path(probe)/'residuals.json'
    if not path.exists():
        return {}
    known = {}
    for reason, keys in json.loads(path.read_text()).items():
        for key in keys:
            if key in known:
                raise ValueError(f'{path}: {key} is listed twice')
            known[key] = reason
    return known


def images(directory):
    return sorted(p.name for p in Path(directory).glob('*.png'))


def record(probe, method, variable, label, batch):
    out = run_root(probe, label)
    project = assemble(probe, out)
    capture = out/'capture'
    capture.mkdir(parents=True)
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', method], batch=batch,
        graphics=True, env={variable: str(capture)}, timeout=900, label=probe.name+'-'+label)
    print(editor_runner.summary(editor_runner.run(job)))
    problem = editor_runner.result_problem(out, gpu=True) or editor_runner.jsonl_complete()(capture)
    if problem:
        raise SystemExit(f'{out}: {problem}')
    print('captured', capture)


def publish(probe, labels):
    roots = [run_root(probe, label) for label in labels]
    for root in roots:
        problem = editor_runner.result_problem(root, gpu=True)
        if problem:
            raise SystemExit(f'{root.name}: {problem}; not publishing')
    captures = [root/'capture' for root in roots]

    def names(directory):
        return [VALUES, *images(directory)]

    def write(target, first):
        target.mkdir(parents=True)
        for name in names(first):
            shutil.copyfile(first/name, target/name)
        print('published', target)
    # Editor results live one level above the captures; publish() checks the capture files.
    for capture, root in zip(captures, roots):
        shutil.copyfile(root/editor_runner.RESULT, capture/editor_runner.RESULT)
    return editor_runner.publish(captures, reference_root(probe), names, write,
                                 complete=[editor_runner.jsonl_complete()], gpu=True)


def gate(probe, variable, label, tolerance, engine_args):
    out = run_root(probe, label)
    project = assemble(probe, out)
    capture = out/'engine'
    capture.mkdir(parents=True)
    cli = os.environ.get('UNBOUND') or os.environ.get('UNBOUND_CLI') or str(REPO/'target/release/unbound')
    env = {**os.environ, variable: str(capture)}
    with (out/'engine.log').open('w') as log:
        code = subprocess.run([str(LAUNCHER), '--kind', 'engine-gpu', 'timeout', '600', cli, 'run', '-p', str(project),
                               '--no-watch', *engine_args], env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    reference = reference_root(probe)
    expected = read_values(reference/VALUES)
    try:
        actual = read_values(capture/VALUES)
    except (OSError, ValueError) as error:
        print(f'{probe.name}: no complete engine capture ({error}); engine exit {code}; log {out}/engine.log')
        return 1
    matched, failures = compare(expected, actual, tolerance)
    known = residuals(probe)
    residual = [f for f in failures if f['key'] in known]
    failures = [f for f in failures if f['key'] not in known]
    for failure in failures:
        print(json.dumps(failure))
    for failure in residual:
        print(json.dumps({**failure, 'residual': known[failure['key']]}))
    differing = {f['key'] for f in residual}
    for key in sorted(k for k in known if k not in differing and k in expected):
        print(f'residual resolved (remove it from residuals.json): {key}')
    for name in images(reference):
        if not (capture/name).exists():
            print(f'{name}: missing')
            failures.append({'image': name})
            continue
        measured = image_difference(reference/name, capture/name)
        if measured is None:
            print(f'{name}: size differs')
            failures.append({'image': name})
        else:
            print(f'{name}: mean {measured[0]:.2f}, worst {measured[1]}, over 8: {measured[2]}')
    print(f'{probe.name}: {matched}/{len(expected)} values match, {len(failures)} failures, {len(residual)} known residuals; engine exit {code}; logs {out}')
    return 1 if failures or code else 0


def main(probe, method, variable, tolerance, engine_args, batch=True):
    """batch=False records in a windowed Editor (on the private display): batch mode calls no OnGUI."""
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=None)
    args = parser.parse_args()
    label = args.label or time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid())
    if args.mode == 'record':
        return record(probe, method, variable, label, batch)
    if args.mode == 'publish':
        return publish(probe, args.labels)
    return gate(probe, variable, label, tolerance, engine_args)
