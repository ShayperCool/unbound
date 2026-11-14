#!/usr/bin/env python3
"""Records the GPU Resident Drawer probe in the Unity Editor or runs it in Unbound.

    run.py native --label L       two independent GPU Editor captures (L-1, L-2)
    run.py publish L-1 L-2        two identical captures become the profile's reference
    run.py engine --label L       an Unbound capture (UNBOUND_CLI, else UNBOUND, else target/release)
    run.py check                  an Unbound capture compared with the reference (the land gate)
    run.py compare REF ACTUAL     compares two capture directories

--profile selects the drawer's settings (controls: direct's, with more refusal controls, see the
README): direct (the default: occlusion culling off, so the drawer
draws with direct draw commands) or occlusion (GPU occlusion culling in cameras on, so the main
camera's commands are indirect ones the drawer's compute shaders fill). Both sides use the Vulkan
device: the Editor windowed with -force-vulkan on the GPU-backed display :0 (the drawer dispatches
its changes from the player loop, which a batch-mode Editor does not run between frames), Unbound
with --force-vulkan; the reference is Reference/unity-R/<profile>. On the OpenGL Core device the
drawer refuses (it needs raw buffers), which tests/projects/brg records.

The probe's scripts are staged over a fresh copy of the shader graph probe's URP project
(tests/projects/shadergraph: its scene, pipeline asset and renderer; its own script is left out)
under $XDG_CACHE_HOME/unbound/validation/runs/residentdrawer/<label>, with the renderer in Forward+,
the drawer in Instanced Drawing and the BatchRendererGroup variants kept, as the Manual's GPU
Resident Drawer setup asks; nothing is written here except by publish.

A capture is values.jsonl (the drawer's state, the renderers it holds and the renderers' isVisible
per capture frame, a new ObjectDispatcher's history length, one {"k", "v"} row each) and one PNG
per capture frame. Values
compare exactly over the union of keys; every missing, extra and failing value is printed. Images
print the mean of the per-pixel largest channel difference, the worst one and the pixels over 8;
an image fails above a mean of 0.5 or with more than 0.5% of its pixels over 8. The exit status is
nonzero when anything fails, except for the keys residuals-<profile>.json lists with their cause
(a key may be a glob pattern), which are printed with their reason. A new capture replaces a
published reference only when it keeps every row and image of it as they are.
"""
import argparse
import fnmatch
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import time
import zlib

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import reference_root, run_root, unity_version  # noqa: E402
import editor_runner  # noqa: E402

LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
CAPTURE = 'capture'
VALUES = 'values.jsonl'
PROFILE = 'direct'
FRAMES = 600
MEAN_BOUND = 0.5
OVER_SHARE = 0.005


def reference():
    return reference_root(ROOT)/PROFILE


def residuals():
    path = ROOT/f'residuals-{PROFILE}.json'
    return json.loads(path.read_text()) if path.exists() else {}


def edit(path, old, new):
    text = path.read_text()
    if text.count(old) != 1:
        sys.exit(f'{path}: expected one {old!r}')
    path.write_text(text.replace(old, new))


def stage(label):
    work = run_root(ROOT, label)
    if work.exists():
        sys.exit(f'refusing to overwrite run: {work}')
    project = work/'project'
    source = ROOT.parent/'shadergraph'
    for part in ('Packages', 'ProjectSettings'):
        shutil.copytree(source/part, project/part)
    shutil.copytree(source/'Assets', project/'Assets', ignore=lambda d, names: ['Scripts', 'Scripts.meta', 'Editor', 'Editor.meta'] if Path(d) == source/'Assets' else [])
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+unity_version()+'\n')
    # The Animation module (the probe's Animator and Animation controls).
    manifest = json.loads((project/'Packages/manifest.json').read_text())
    manifest['dependencies']['com.unity.modules.animation'] = '1.0.0'
    (project/'Packages/manifest.json').write_text(json.dumps(manifest, indent=2)+'\n')
    lock = json.loads((project/'Packages/packages-lock.json').read_text())
    lock['dependencies']['com.unity.modules.animation'] = {'version': '1.0.0', 'depth': 0, 'source': 'builtin', 'dependencies': {}}
    (project/'Packages/packages-lock.json').write_text(json.dumps(lock, indent=2)+'\n')
    edit(project/'ProjectSettings/GraphicsSettings.asset', 'm_BrgStripping: 0', 'm_BrgStripping: 2')
    edit(project/'Assets/Settings/ProbeRenderer.asset', 'm_RenderingMode: 0', 'm_RenderingMode: 2')
    edit(project/'Assets/Settings/ProbeURP.asset', 'm_GPUResidentDrawerMode: 0', 'm_GPUResidentDrawerMode: 1')
    if PROFILE == 'occlusion':
        edit(project/'Assets/Settings/ProbeURP.asset', 'm_GPUResidentDrawerEnableOcclusionCullingInCameras: 0',
             'm_GPUResidentDrawerEnableOcclusionCullingInCameras: 1')
    if PROFILE == 'controls':
        # A second sorting layer for the SortingLayer control.
        edit(project/'ProjectSettings/TagManager.asset', '  - name: Default\n    uniqueID: 0\n    locked: 0\n',
             '  - name: Default\n    uniqueID: 0\n    locked: 0\n  - name: Probe\n    uniqueID: 1876543210\n    locked: 0\n')
    for part in ('Scripts', 'Editor', 'Shaders'):
        shutil.copytree(ROOT/'Assets'/part, project/'Assets'/part)
        shutil.copy2(ROOT/'Assets'/(part+'.meta'), project/'Assets'/(part+'.meta'))
    return work, project


def probe_env(work):
    """The probe's environment: its capture directory, and the controls profile's switch."""
    env = {'RESIDENT_DRAWER_OUT': str(work/CAPTURE)}
    if PROFILE == 'controls':
        env['RESIDENT_DRAWER_CONTROLS'] = '1'
    return env


def native(label):
    jobs = []
    for repeat in (f'{label}-1', f'{label}-2'):
        work, project = stage(repeat)
        jobs.append(editor_runner.EditorJob(project, work/'editor.log', work, ['-executeMethod', 'ResidentDrawerRunner.Record'],
                                            batch=False, graphics=True, env=probe_env(work),
                                            vulkan=True, timeout=1800, label='residentdrawer-'+repeat))
    # The Vulkan Editors share the display :0: one after the other.
    results = [editor_runner.run(job) for job in jobs]
    for result in results:
        print(editor_runner.summary(result))
    return 0 if all((job.out/CAPTURE/VALUES).is_file() for job in jobs) else 1


def engine(label):
    work, project = stage(label)
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    env = dict(os.environ, **probe_env(work))
    command = [str(LAUNCHER), '--kind', 'engine-gpu', 'timeout', '-s', 'KILL', '1500', cli, 'run', '-p', str(project),
               '--no-watch', '--frames', str(FRAMES), '--size', '320x180', '--force-vulkan']
    with (work/'process.log').open('w') as log:
        code = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    values = work/CAPTURE/VALUES
    if code or not values.is_file() or '"complete"' not in values.read_text():
        sys.exit(f'{label}: exit {code}, capture incomplete; see {work}')
    print('engine', work/CAPTURE)
    return work/CAPTURE


def capture_names(directory):
    return sorted(p.name for p in Path(directory).iterdir() if p.suffix in ('.png', '.jsonl'))


def kept(old, new):
    """Whether the capture `new` keeps every image of the reference `old` byte for byte and
    every row of its values as it is, in its order."""
    for name in capture_names(old):
        if name != VALUES and (not (new/name).exists() or (new/name).read_bytes() != (old/name).read_bytes()):
            return False
    rest = iter((new/VALUES).read_bytes().splitlines())
    return all(row in rest for row in (old/VALUES).read_bytes().splitlines())


def publish(labels):
    target = reference()
    fresh = target.with_name(target.name+'.new')
    if fresh.exists():
        shutil.rmtree(fresh)

    def write(path, first):
        path.mkdir(parents=True)
        for name in capture_names(first/CAPTURE):
            shutil.copyfile(first/CAPTURE/name, path/name)
    editor_runner.publish([run_root(ROOT, label) for label in labels], fresh,
                          lambda d: [f'{CAPTURE}/{n}' for n in capture_names(Path(d)/CAPTURE)], write,
                          complete=[editor_runner.jsonl_complete(f'{CAPTURE}/{VALUES}')], gpu=True)
    if target.exists():
        if not kept(target, fresh):
            shutil.rmtree(fresh)
            sys.exit(f'the captures change rows or images of the reference; not replacing {target}')
        shutil.rmtree(target)
    fresh.rename(target)
    print('published', target)
    return 0


def rows(path):
    result = {}
    for line in Path(path).read_text().splitlines():
        if line.strip():
            row = json.loads(line)
            if row['k'] in result:
                raise SystemExit(f'{path}: duplicate key {row["k"]}')
            result[row['k']] = row['v']
    return result


def read_png(path):
    data = Path(path).read_bytes()
    assert data[:8] == b'\x89PNG\r\n\x1a\n', path
    pos, idat = 8, b''
    while pos < len(data):
        length, kind = struct.unpack('>I4s', data[pos:pos+8])
        body = data[pos+8:pos+8+length]
        if kind == b'IHDR':
            width, height, depth, color, _, _, interlace = struct.unpack('>IIBBBBB', body)
            assert depth == 8 and interlace == 0 and color in (2, 6), (path, depth, color, interlace)
            channels = 3 if color == 2 else 4
        elif kind == b'IDAT':
            idat += body
        pos += 12+length
    raw = zlib.decompress(idat)
    stride = width*channels
    out, prev = [], bytearray(stride)
    for y in range(height):
        kind = raw[y*(stride+1)]
        line = bytearray(raw[y*(stride+1)+1:(y+1)*(stride+1)])
        for x in range(stride):
            a = line[x-channels] if x >= channels else 0
            b = prev[x]
            c = prev[x-channels] if x >= channels else 0
            if kind == 1:
                line[x] = (line[x]+a) & 255
            elif kind == 2:
                line[x] = (line[x]+b) & 255
            elif kind == 3:
                line[x] = (line[x]+(a+b)//2) & 255
            elif kind == 4:
                p = a+b-c
                pa, pb, pc = abs(p-a), abs(p-b), abs(p-c)
                line[x] = (line[x]+(a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        out.append([tuple(line[x*channels:x*channels+3]) for x in range(width)])
        prev = line
    return width, height, out


def residual(known, key):
    """The listed cause of a key's difference (a listed key or glob pattern), else None."""
    if key in known:
        return known[key]
    return next((cause for pattern, cause in known.items() if fnmatch.fnmatchcase(key, pattern)), None)


def compare(expected_dir, actual_dir):
    expected_dir, actual_dir = Path(expected_dir), Path(actual_dir)
    known = residuals()
    failures = 0
    want, got = rows(expected_dir/VALUES), rows(actual_dir/VALUES)
    matched = 0
    for key in sorted(set(want) | set(got)):
        cause = residual(known, key)
        if key in want and key in got and want[key] == got[key]:
            matched += 1
            if key in known:
                print(f'resolved residual {key}')
            continue
        what = ('missing' if key not in got else 'extra' if key not in want else 'differs')
        line = f'{what} {key}: unity {json.dumps(want.get(key))} unbound {json.dumps(got.get(key))}'
        if cause is not None:
            print(f'residual {line} ({cause})')
        else:
            print(line)
            failures += 1
    for name in sorted(p.name for p in expected_dir.glob('*.png')):
        actual = actual_dir/name
        if not actual.exists():
            print(f'missing image {name}')
            failures += 1
            continue
        w1, h1, a = read_png(expected_dir/name)
        w2, h2, b = read_png(actual)
        if (w1, h1) != (w2, h2):
            print(f'image {name}: size {w2}x{h2}, expected {w1}x{h1}')
            failures += 1
            continue
        total = worst = over = 0
        for y in range(h1):
            for x in range(w1):
                d = max(abs(p-q) for p, q in zip(a[y][x], b[y][x]))
                total += d
                worst = max(worst, d)
                over += d > 8
        mean = total/(w1*h1)
        print(f'{name}: mean max-channel difference {mean:.2f}, worst {worst}, pixels over 8: {over} of {w1 * h1}')
        if mean > MEAN_BOUND or over > OVER_SHARE*w1*h1:
            failures += 1
    print(f'{matched}/{len(set(want) | set(got))} records match, {failures} failures')
    return 1 if failures else 0


def check():
    capture = engine(f'check-{PROFILE}-{time.time_ns()}')
    return compare(reference(), capture)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['native', 'engine', 'publish', 'check', 'compare'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--profile', choices=('direct', 'occlusion', 'controls'), default='direct')
    args = parser.parse_args()
    global PROFILE
    PROFILE = args.profile
    if args.mode == 'publish':
        if len(args.labels) != 2:
            parser.error('publish takes two native labels')
        return publish(args.labels)
    if args.mode == 'compare':
        if len(args.labels) != 2:
            parser.error('compare takes two capture directories')
        return compare(*args.labels)
    if args.mode == 'check':
        return check()
    if not args.label or Path(args.label).name != args.label:
        parser.error('--label must be a single directory name')
    if args.mode == 'native':
        return native(args.label)
    engine(args.label)
    return 0


if __name__ == '__main__':
    sys.exit(main())
