#!/usr/bin/env python3
"""Strict admission/publication gate for one indirectspec capture directory.

    gate.py DIRECTORY --profile linear|gamma [--release R]

A capture passes only if every condition holds; every problem is reported, none is dropped:
* Editor result: exit 0, no forced stop of any kind (no shutdown-guard exception), no signal,
  an NVIDIA renderer, and a launch-bound input record of this release whose witness source is
  this checkout's and whose color space is the profile's;
* Editor log: no C# compile error, no shader error, no "Scripts have compiler errors", no
  exception through the witness or its entrypoint, and the witness's own "written" line once;
* output: JSON lines, each exactly {"k", "v"}; no duplicate key; exactly one completion marker,
  the last line; no diagnostic error record (a key ending in " error": they are listed);
  every predeclared key present and no unknown key (optional: the lighting data's serialized
  "*AmbientProbe*" properties). The key set is derived from the witness source itself (Slots,
  Settings), so it cannot drift from what the witness writes;
* consumers: the Standard material has the predeclared keywords and parameters, the custom SH is
  black, all lights were disabled, every capture records the profile's color space and the set
  intensities; every dump slot, Standard centre, ambient probe and HDR decode value is finite;
  no dump slot is the shader's placeholder (-1 -1 -1 -1) and no capture's slots are all equal
  (an error shader or a missing draw); both Standard histograms are finite and cover 64 x 64.
"""
import argparse
import base64
import hashlib
import json
import math
from pathlib import Path
import re
import sys

PROBE = Path(__file__).resolve().parent
OUT = 'indirectspec.jsonl'
MARKER = {'k': 'complete', 'v': True}
WITNESS = PROBE/'Assets/Scripts/IndirectSpecWitness.cs'
WITNESS_KEY = 'project/Assets/Scripts/IndirectSpecWitness.cs'
COLOR = {'linear': ('Linear', b'm_ActiveColorSpace: 1'), 'gamma': ('Gamma', b'm_ActiveColorSpace: 0')}
CONTEXT = ('format', 'activeColorSpace', 'graphicsDeviceType', 'graphics activeTier', 'activeBuildTarget',
           'tier standardShaderQuality', 'lightingDataAsset', 'defaultTexture', 'skybox',
           'customReflectionTexture', 'sun', 'standard material', 'custom SH', 'lights disabled')
RECORD = ('RenderSettings', 'ambientProbe', 'defaultTextureHDRDecodeValues', 'activeColorSpace')
STANDARD = ('standard bytes centre', 'standard bytes histogram', 'standard floats centre',
            'standard floats histogram')
OPTIONAL = re.compile(r'^lightingData \S*AmbientProbe\S*$')
PIXELS = 64*64
LOG_REJECT = (re.compile(r'error CS\d+'), re.compile(r'Shader error in'),
              re.compile(r'Scripts have compiler errors'), re.compile(r'Compilation failed'))


def witness_layout(source=WITNESS):
    """(slots, settings) as declared in the witness source."""
    text = Path(source).read_text()
    slots_block = re.search(r'string\[\] Slots =\s*\{(.*?)\};', text, re.S).group(1)
    slots = re.findall(r'"([^"]+)"', slots_block)
    settings_block = re.search(r'\)\[\] Settings =\s*\{(.*?)\};', text, re.S).group(1)
    settings = re.findall(r'\("([^"]+)",\s*([0-9.]+)f,\s*([0-9.]+)f\)', settings_block)
    return slots, [(name, float(a), float(r)) for name, a, r in settings]


def expected_keys(slots, settings):
    keys = list(CONTEXT)+['lights enabled '+r for r in RECORD]+[f'lights enabled dump {s}' for s in slots]
    for name, _, _ in settings:
        for when in ('immediate', 'settled'):
            prefix = f'{name} {when}'
            keys += [f'{prefix} {r}' for r in RECORD]+[f'{prefix} dump {s}' for s in slots]
            keys += [f'{prefix} {s}' for s in STANDARD]
    return keys


def finite_floats(text, count):
    try:
        values = [float(t) for t in text.split()]
    except (ValueError, AttributeError):
        return None
    return values if len(values) == count and all(math.isfinite(v) for v in values) else None


def fmt(x):
    """C# float.ToString("R") of the predeclared intensities."""
    return str(int(x)) if x == int(x) else repr(x)


def result_problems(directory, profile, release):
    problems = []
    path = directory/'editor-run.json'
    if not path.exists():
        return ['no editor-run.json']
    result = json.loads(path.read_text())
    if result.get('exit_code') != 0:
        problems.append(f"Editor exit {result.get('exit_code')}")
    if result.get('forced_exit') is not None:
        problems.append(f"forced stop {result.get('forced_exit')}: {result.get('forced_detail')}")
    if result.get('signal') is not None:
        problems.append(f"signal {result.get('signal')}")
    if 'NVIDIA' not in (result.get('renderer') or '') or result.get('renderer_ok') is not True:
        problems.append(f"renderer {result.get('renderer')!r} is not the verified NVIDIA GPU")
    inputs = directory/'prelaunch-inputs.json'
    if not inputs.exists():
        return problems+['no launch-bound input record']
    record = json.loads(inputs.read_text())
    if record.get('selected_release') != release:
        problems.append(f"input record release {record.get('selected_release')} != {release}")
    witness = record.get('files', {}).get(WITNESS_KEY)
    expected = hashlib.sha256(WITNESS.read_bytes()).hexdigest()
    if not witness or witness['sha256'] != expected:
        problems.append(f'launched witness source is not this checkout\'s ({expected[:16]})')
    settings = record.get('files', {}).get('project/ProjectSettings/ProjectSettings.asset')
    text = base64.b64decode(settings['bytes_base64']) if settings else b''
    if text.count(COLOR[profile][1]) != 1 or text.count(b'm_ActiveColorSpace:') != 1:
        problems.append(f'launched ProjectSettings do not select the {profile} color space')
    return problems


def log_problems(directory):
    path = directory/'editor.log'
    if not path.exists():
        return ['no editor.log']
    lines = path.read_text(errors='replace').splitlines()
    problems = [f'log: {line.strip()[:240]}' for line in lines if any(p.search(line) for p in LOG_REJECT)]
    for i, line in enumerate(lines):
        if 'Exception' in line and any(n in ' '.join(lines[i:i+12]) for n in ('IndirectSpecWitness', 'ProbeRunner')):
            problems.append(f'log: witness exception: {line.strip()[:240]}')
    written = sum(1 for line in lines if line.strip() == 'indirectspec witness written')
    if written != 1:
        problems.append(f'log: the witness wrote its completion line {written} times')
    return problems


def output_problems(directory, profile, layout=None):
    slots, settings = layout or witness_layout()
    path = directory/OUT
    if not path.exists():
        return [f'{OUT} is missing']
    problems, rows = [], []
    for number, line in enumerate(path.read_text().split('\n'), 1):
        if not line:
            continue
        try:
            row = json.loads(line)
        except json.JSONDecodeError:
            problems.append(f'line {number} is not JSON')
            continue
        if not isinstance(row, dict) or set(row) != {'k', 'v'}:
            problems.append(f'line {number} is not a {{"k","v"}} row')
            continue
        rows.append(row)
    markers = [i for i, r in enumerate(rows) if r == MARKER]
    if len(markers) != 1 or not rows or rows[-1] != MARKER:
        problems.append(f'{len(markers)} completion markers, last row {rows[-1] if rows else None}')
    values, seen = {}, {}
    for row in rows:
        if row == MARKER:
            continue
        key, value = row['k'], row['v']
        if not isinstance(key, str) or not isinstance(value, str):
            problems.append(f'non-string row {row}')
            continue
        seen[key] = seen.get(key, 0)+1
        values.setdefault(key, value)
    problems += [f'duplicate key {k!r} x{n}' for k, n in seen.items() if n > 1]
    problems += [f'diagnostic error record {k!r}: {v}' for k, v in values.items() if k.endswith(' error')]
    expected = expected_keys(slots, settings)
    problems += [f'missing key {k!r}' for k in expected if k not in values]
    known = set(expected)|{'lightingData properties'}
    problems += [f'unknown key {k!r}' for k in values if k not in known and not OPTIONAL.match(k)
                 and not k.endswith(' error')]
    if values.get('format') != 'indirectspec 1':
        problems.append(f"format {values.get('format')!r}")
    space = COLOR[profile][0]
    for key, value in values.items():
        if key == 'activeColorSpace' or key.endswith(' activeColorSpace'):
            if value != space:
                problems.append(f'{key} = {value!r}, the profile is {space}')
    if values.get('tier standardShaderQuality') not in ('High', 'Medium', 'Low'):
        problems.append(f"tier standardShaderQuality {values.get('tier standardShaderQuality')!r}")
    if values.get('lightingDataAsset', 'null') == 'null':
        problems.append('no lighting data asset')
    elif 'lightingData properties' not in values:
        problems.append("missing key 'lightingData properties'")
    material = values.get('standard material', '')
    if not (material.startswith('Standard keywords ') and '_GLOSSYREFLECTIONS_OFF' in material.split()
            and '_SPECULARHIGHLIGHTS_OFF' in material.split() and material.endswith('metallic 0 glossiness 0')
            and 'color 1 1 1 1' in material):
        problems.append(f'Standard consumer setup {material!r}')
    custom = finite_floats(values.get('custom SH'), 27)
    if custom is None or any(custom):
        problems.append(f"custom SH {values.get('custom SH')!r} is not black")
    lights = values.get('lights disabled', '')
    if '=False' not in lights or '=True' in lights:
        problems.append(f"lights not all disabled: {values.get('lights disabled')!r}")
    captures = ['lights enabled']+[f'{n} {w}' for n, _, _ in settings for w in ('immediate', 'settled')]
    for capture in captures:
        dump = [values.get(f'{capture} dump {s}') for s in slots]
        parsed = [finite_floats(v, 4) for v in dump]
        for s, v, p in zip(slots, dump, parsed):
            if p is None and v is not None:
                problems.append(f'{capture} dump {s} = {v!r} is not 4 finite floats')
            if p == [-1.0, -1.0, -1.0, -1.0]:
                problems.append(f'{capture} dump {s} is the shader placeholder')
        if all(p is not None for p in parsed) and len({tuple(p) for p in parsed}) == 1:
            problems.append(f'{capture}: every dump slot is {parsed[0]} (error shader or no draw)')
        for key, count in ((f'{capture} ambientProbe', 27), (f'{capture} defaultTextureHDRDecodeValues', 4)):
            if key in values and finite_floats(values[key], count) is None:
                problems.append(f'{key} = {values[key]!r} is not {count} finite floats')
    for name, ambient, reflection in settings:
        for when in ('immediate', 'settled'):
            prefix = f'{name} {when}'
            rs = values.get(f'{prefix} RenderSettings')
            if rs is not None and f'ambientIntensity {fmt(ambient)} reflectionIntensity {fmt(reflection)} ' not in rs:
                problems.append(f'{prefix} RenderSettings do not hold the set intensities: {rs!r}')
            centre = values.get(f'{prefix} standard floats centre')
            if centre is not None and finite_floats(centre, 4) is None:
                problems.append(f'{prefix} standard floats centre {centre!r} is not 4 finite floats')
            byte = values.get(f'{prefix} standard bytes centre')
            if byte is not None:
                parts = byte.split()
                if len(parts) != 4 or not all(p.isdigit() and int(p) <= 255 for p in parts):
                    problems.append(f'{prefix} standard bytes centre {byte!r}')
            for kind, count in (('bytes', 4), ('floats', 4)):
                histogram = values.get(f'{prefix} standard {kind} histogram')
                if histogram is None:
                    continue
                total = 0
                for entry in histogram.split('; '):
                    match = re.match(r'^(\d+)x (.*)$', entry)
                    if not match or finite_floats(match.group(2), count) is None:
                        problems.append(f'{prefix} standard {kind} histogram entry {entry[:80]!r}')
                        break
                    total += int(match.group(1))
                else:
                    if total != PIXELS:
                        problems.append(f'{prefix} standard {kind} histogram covers {total} pixels')
    return problems


def problems(directory, profile, release, layout=None):
    directory = Path(directory)
    return result_problems(directory, profile, release)+log_problems(directory) \
        + output_problems(directory, profile, layout)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--profile', choices=tuple(COLOR), required=True)
    parser.add_argument('--release', default='6000.0.71f1')
    args = parser.parse_args()
    found = problems(args.directory, args.profile, args.release)
    for problem in found:
        print('REJECT', problem)
    print('PASS' if not found else f'REJECTED ({len(found)} problems)')
    return 1 if found else 0


if __name__ == '__main__':
    sys.exit(main())
