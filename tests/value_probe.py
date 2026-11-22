#!/usr/bin/env python3
"""Shared driver for probes whose runtime script writes keyed values (and optional images).

A probe's script writes `values.jsonl` into the directory named by its output variable: one
`{"k": key, "v": value}` line per observation (keys unique), the last line
`{"k": "complete", "v": true}`. Images are PNG files next to it. The probe's `run.py` calls
`main(...)` with its project directory, Editor method and output variable:

    run.py record --label L     one Editor capture on the GPU display (run cache); on macOS,
                                for a probe that declares `macos_batch` (values that depend
                                on neither the OS nor the graphics device), a batch-mode
                                capture with the Hub's Editor (tests/editor_runner.py; the
                                caller holds the machine's heavy-job lock)
    run.py publish A B          two byte-identical captures -> Reference/unity-<release>/
    run.py gate [--label L]     the engine on a fresh copy, compared with Reference/

A probe that declares `metal` has values the graphics device decides (its rounding, formats and
answers: AGENTS.md Platforms). On macOS arm64, where the Editor and Unbound draw with Metal, it
records with the macOS Editor (which draws with Metal; windowed for a probe that is), publishes to
Reference/unity-<release>/metal/ and gates against that, with `residuals-metal.json` and
`image_limits-metal.json` in place of the files below (a probe that has one of those files
needs its Metal counterpart, `{}` where nothing applies); elsewhere it is unchanged.

Values compare exactly, except numbers: |expected - actual| <= `tolerance` (the probe's own,
named in its README). Every missing, extra and differing key is printed. Images print their
mean and worst max-channel difference and the pixels over 8; without a limit that is no verdict
(the suite tracks them). A probe may set limits in `image_limits.json` next to its run.py:
`{image: {"mean": m, "worst": w, "over8": n, "reason": "..."}}`, the recorded residuals with their
cause; an image over any of them fails.

A probe that declares `color_spaces` records and gates each of them: the project as committed,
with its color-space setting replaced (`set_color_space`), and a reference of each in
Reference/unity-<release>/<space>/. `record` and `publish` then take `--space`; `gate` runs
every space, or the one `--space` names.

A probe that declares `devices` (Linux: `('opengl', 'vulkan')`) has values the graphics device
may decide: `record` and `publish` take `--device` (default opengl, the Linux Editor's own
device; vulkan records the Editor with -force-vulkan on the GPU display :0), the OpenGL Core
reference stays in Reference/unity-<release>/ and the Vulkan one is Reference/unity-<release>/vulkan/;
`gate` runs Unbound on every device (vulkan with --force-vulkan), or the one `--device` names.
On macOS arm64 the gate runs the probe's macOS check alone.

A probe may list known residuals in `residuals.json` next to its run.py: `{reason: [key, ...]}`,
open differences with a named cause and follow-up. Such a key that differs is printed with its
reason and does not fail the gate; one that matches again is printed as resolved (remove it).
Every other difference fails.
"""
import argparse
import hashlib
import json
import math
import os
import re
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import time
import zlib

TESTS = Path(__file__).resolve().parent
sys.path.insert(0, str(TESTS))
from probe_paths import MACOS_ARM64, run_root, reference_root, unity_version  # noqa: E402
import editor_runner  # noqa: E402
import authored_project  # noqa: E402

REPO = TESTS.parent
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
VALUES = 'values.jsonl'
COMPLETE = {'k': 'complete', 'v': True}
# Set by main for a probe that declares `metal` on macOS arm64: its Metal reference and files.
METAL = False


def reference_dir(probe, space=None, device=None, profile=None):
    """The reference the probe records and gates against here (its Metal one on macOS, see METAL),
    that of a color space for a probe recorded in several, of the Vulkan device for a probe
    recorded on several devices (`device` 'vulkan')."""
    root = reference_root(probe)/'metal' if METAL else reference_root(probe)
    if device == 'vulkan':
        root = root/'vulkan'
    if space:
        root = root/space
    return root/profile if profile else root


def include_builtin_module(project, name):
    """Adds a built-in module to the manifest of a probe's private copy."""
    manifest = project/'Packages/manifest.json'
    settings = json.loads(manifest.read_text())
    settings['dependencies']['com.unity.modules.'+name] = '1.0.0'
    manifest.write_text(json.dumps(settings, indent=2) + '\n')


def set_color_space(project, space):
    """The project's color-space setting (ProjectSettings' m_ActiveColorSpace) set to `space`,
    'Gamma' or 'Linear'."""
    settings = Path(project)/'ProjectSettings/ProjectSettings.asset'
    text, count = re.subn(r'(?m)^(\s*m_ActiveColorSpace:) [01]$',
                          r'\g<1> '+str({'Gamma': 0, 'Linear': 1}[space]), settings.read_text())
    if count != 1:
        raise ValueError(f'expected one color-space setting, got {count}')
    settings.write_text(text)


def device_file(probe, name):
    """`name` next to the probe's run.py, `<stem>-metal<suffix>` for its Metal reference. A probe
    with the file for its other references needs a Metal one too (`{}` where nothing
    applies): its limits or residuals are never dropped silently."""
    path = Path(probe)/name
    if not METAL:
        return path
    metal = path.with_name(f'{path.stem}-metal{path.suffix}')
    if path.exists() and not metal.exists():
        raise SystemExit(f'{Path(probe).name}: {name} has no Metal counterpart {metal.name}; '
                         'record what applies to the Metal reference there')
    return metal


PARTS = ('Assets', 'Packages', 'ProjectSettings')


def assemble(probe, out, base=None):
    """The probe's project in out/project. `base` is (another probe's directory, the paths of
    its project to take), or a list of them: those first, then the probe's own parts over them
    (where it has them)."""
    target = out/'project'
    if target.exists():
        raise SystemExit(f'refusing to overwrite {target}')
    for directory, paths in ([base] if isinstance(base, tuple) else base or []):
        for path in paths:
            source = directory/path
            if source.is_dir():
                shutil.copytree(source, target/path, dirs_exist_ok=True)
            else:
                (target/path).parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(source, target/path)
    for part in PARTS:
        if (probe/part).exists() or not base:
            shutil.copytree(probe/part, target/part, dirs_exist_ok=True)
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
        # Equal numbers match, infinities of one sign included (their difference is NaN).
        return expected == actual or abs(expected-actual) <= tolerance
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
    path = device_file(probe, 'residuals.json')
    if not path.exists():
        return {}
    known = {}
    for reason, keys in json.loads(path.read_text()).items():
        for key in keys:
            if key in known:
                raise ValueError(f'{path}: {key} is listed twice')
            known[key] = reason
    return known


def image_limits(probe):
    """{image: (mean, worst, over 8, reason)} of the probe's image_limits.json (none without one)."""
    path = device_file(probe, 'image_limits.json')
    if not path.exists():
        return {}
    limits = {}
    for name, limit in json.loads(path.read_text()).items():
        if not limit.get('reason'):
            raise ValueError(f'{path}: {name} has no reason')
        limits[name] = (limit['mean'], limit['worst'], limit['over8'], limit['reason'])
    return limits


def images(directory):
    return sorted(p.name for p in Path(directory).glob('*.png'))


RECORD_TIMEOUT = 900


def record(probe, method, variable, label, batch, timeout=RECORD_TIMEOUT, base=None, macos_batch=False, space=None, device=None,
           *, configure=None):
    if sys.platform == 'darwin' and not (METAL or (macos_batch and batch)):
        raise SystemExit(f'{probe.name} records only with the Linux GPU Editor')
    out = run_root(probe, label)
    project = assemble(probe, out, base)
    if configure:
        configure(project)
    if space:
        set_color_space(project, space)
    capture = out/'capture'
    capture.mkdir(parents=True)
    # On macOS arm64 editor_runner runs the macOS Editor, which draws with Metal.
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', method], batch=batch,
        graphics=True, env={variable: str(capture)}, timeout=timeout, label=probe.name+'-'+label,
        vulkan=device == 'vulkan')
    print(editor_runner.summary(editor_runner.run(job)))
    problem = editor_runner.result_problem(out, gpu=True) or editor_runner.jsonl_complete()(capture)
    if problem:
        raise SystemExit(f'{out}: {problem}')
    print('captured', capture)


def value_capture_fingerprints(root):
    """Hashes the launch and payload of a value-only capture inside its private run."""
    root = Path(root)
    capture = root/'capture'
    name = editor_runner.RESULT
    values, launch = capture/VALUES, root/name
    paths = (capture, capture/name, values, launch, root/'capture.sha256')
    if any(path.resolve() != path for path in paths):
        raise SystemExit(f'{root}: capture files must stay in their run directory')
    if images(capture):
        raise SystemExit(f'{root}: sealed value-only captures cannot contain screenshots')
    for path in paths[1:]:
        if path.exists() and path.stat().st_nlink != 1:
            raise SystemExit(f'{path}: capture file must have a single filesystem link')
    return [hashlib.sha256(path.read_bytes()).hexdigest() for path in (launch, values)]


def seal_value_capture(root):
    """Binds the values to the completed launch before authoring may publish them."""
    fingerprints = value_capture_fingerprints(root)
    with (root/'capture.sha256').open('x') as seal:
        seal.write('\n'.join(fingerprints) + '\n')


def verify_sealed_value_repeats(roots):
    """Checks distinct launch records and intact payloads for a values-only probe."""
    if len(roots) < 2 or len(set(roots)) != len(roots):
        raise SystemExit('publish requires at least two distinct Editor runs')
    launches = set()
    for root in roots:
        try:
            fingerprints = value_capture_fingerprints(root)
            if fingerprints != (root/'capture.sha256').read_text().splitlines():
                raise SystemExit(f'{root}: captured values or launch metadata changed after recording')
        except OSError as error:
            raise SystemExit(f'{root}: missing or invalid Editor record: {error}') from error
        launch = fingerprints[0]
        if launch in launches:
            raise SystemExit(f'{root}: duplicate Editor launch record')
        launches.add(launch)


def publish(probe, labels, space=None, device=None, *, profile=None):
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
    return editor_runner.publish(captures, reference_dir(probe, space, device, profile), names, write,
                                 complete=[editor_runner.jsonl_complete()], gpu=True)


def engine_cli():
    """The Unbound command a run starts: $UNBOUND, else $UNBOUND_CLI, else the checkout's release build."""
    return os.environ.get('UNBOUND') or os.environ.get('UNBOUND_CLI') or str(REPO/'target/release/unbound')


def gate(probe, variable, label, tolerance, engine_args, authored=None, base=None, space=None, device=None,
         *, configure=None, profile=None):
    # The limits and residuals are read first: a missing Metal counterpart stops before the run.
    residuals(probe), image_limits(probe)
    out = run_root(probe, label)
    if authored:
        project = out/'project'
        authored_project.assemble(probe.name, unity_version(), authored, project)
    else:
        project = assemble(probe, out, base)
    if configure:
        configure(project)
    if space:
        set_color_space(project, space)
    capture = out/'engine'
    capture.mkdir(parents=True)
    cli = engine_cli()
    env = {**os.environ, variable: str(capture)}
    with (out/'engine.log').open('w') as log:
        code = subprocess.run([str(LAUNCHER), '--kind', 'engine-gpu', 'timeout', '600', cli, 'run', '-p', str(project),
                               '--no-watch', *engine_args, *(['--force-vulkan'] if device == 'vulkan' else [])],
                              env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    reference = reference_dir(probe, space, device, profile)
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
    limits = image_limits(probe)
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
            limit = limits.get(name)
            if limit and (measured[0] > limit[0] or measured[1] > limit[1] or measured[2] > limit[2]):
                print(f'{name}: over its recorded limit (mean {limit[0]}, worst {limit[1]}, over 8: {limit[2]}; {limit[3]})')
                failures.append({'image': name})
    name = ' '.join(part for part in (probe.name, space, device, profile) if part)
    print(f'{name}: {matched}/{len(expected)} values match, {len(failures)} failures, {len(residual)} known residuals; engine exit {code}; logs {out}')
    return 1 if failures or code else 0


def main(probe, method, variable, tolerance, engine_args, batch=True, authored=None, record_timeout=RECORD_TIMEOUT, base=None,
         macos_batch=False, metal=False, color_spaces=None, devices=None):
    """batch=False records in a windowed Editor (on the private display): batch mode calls no OnGUI.
    authored names the probe's authored set (tests/authored_project.py) when its Editor run also
    authors inputs the engine needs: publish keeps the first capture's project as that set and
    the gate assembles it instead of the bare fixture. record_timeout is how many seconds an
    Editor capture may take before it is stopped (a probe whose capture authors and builds much
    gives itself more). base is (another probe's directory, paths in it), or a list of them: parts of those probes'
    project (scenes, bakes, settings) this probe's own files are laid over. macos_batch: the
    probe's values depend on neither the OS nor the graphics device, so it may be recorded with
    the macOS Editor in batch mode. metal: the graphics device decides the probe's values, so on
    macOS arm64 it records, publishes and gates against a Metal reference (see the module's
    docstring). color_spaces: the color spaces the probe is recorded and gated in, devices the
    Linux graphics devices (see the module's docstring)."""
    global METAL
    METAL = metal and MACOS_ARM64
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=None)
    if color_spaces:
        parser.add_argument('--space', choices=color_spaces)
    if devices:
        if color_spaces:
            raise ValueError('a probe declares color spaces or devices, not both')
        parser.add_argument('--device', choices=devices)
    args = parser.parse_args()
    device = getattr(args, 'device', None)
    if device and MACOS_ARM64:
        parser.error('--device names a Linux graphics device; on macOS the probe runs its macOS check')
    space = getattr(args, 'space', None)
    if color_spaces and args.mode != 'gate' and not space:
        parser.error(f'{args.mode} needs --space ({", ".join(color_spaces)})')
    label = args.label or time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid())
    if args.mode == 'record':
        return record(probe, method, variable, label, batch, record_timeout, base, macos_batch, space, device)
    if args.mode == 'publish':
        code = publish(probe, args.labels, space, device)
        if authored and not code:
            authored_project.record(probe.name, unity_version(), authored, run_root(probe, args.labels[0])/'project',
                                    f'python3 tests/projects/{probe.name}/run.py record')
        return code
    if color_spaces:
        spaces = [space] if space else color_spaces
        return max(gate(probe, variable, f'{label}-{s}', tolerance, engine_args, authored, base, s) for s in spaces)
    if devices and not MACOS_ARM64:
        chosen = [device] if device else devices
        return max(gate(probe, variable, f'{label}-{d}', tolerance, engine_args, authored, base, device=None if d == 'opengl' else d)
                   for d in chosen)
    return gate(probe, variable, label, tolerance, engine_args, authored, base)
