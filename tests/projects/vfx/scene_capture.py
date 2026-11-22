#!/usr/bin/env python3
"""A scene of any project seen by a fixed camera, in the Unity Editor and in Unbound, compared.

Debug tooling for effects in real content (README.md, "Scene captures"): nothing of the project
is kept in Git. Each run works on a private copy of the project in the run cache (a reflink copy
where the file system has them, the Editor's Library included so that its run need not import
the project again), with SceneCapture/ copied into its Assets/ (UnboundSceneCapture.cs: the same
script in both).

    scene_capture.py editor PROJECT SCENE --label L [settings]   one Editor capture (GPU display)
    scene_capture.py engine PROJECT SCENE --label L [settings]   one Unbound capture
    scene_capture.py compare A B                                  the frames of two captures

Settings: one view per --position X,Y,Z, each with its --rotation X,Y,Z (Euler degrees), or
--target NAME (an active object; of several, the one nearest --target-near X,Y,Z) with each
--position a camera's offset from it, looking at it;
[--fov F] [--size WxH] [--step SECONDS] [--frames N,N,...] [--seed N: every effect's seed, so
runs draw alike] [--list-renderers: the renderers of the scene's active objects, which compare
then tells apart] [--vulkan: on the Vulkan device instead of OpenGL Core]. compare prints, per frame, the mean and worst max-channel difference and the
pixels over 8, and writes the two frames and their difference side by side into B's run
directory (compare-vK-frame-NNN.png).
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import editor_runner  # noqa: E402
import probe_paths  # noqa: E402
import value_probe  # noqa: E402

HERE = Path(__file__).resolve().parent
SCRIPTS = HERE/'SceneCapture'
# The self-test's fixtures (copied only into its own runs) and the scene it captures.
SELF_TEST = 'SelfTest'
FIXTURES = SCRIPTS/SELF_TEST
SELF_TEST_SCENE = 'Assets/Scenes/Probe.unity'
VARIABLE = 'UNBOUND_SCENE_CAPTURE'
RUNS = 'vfx-scene'
# Frames the engine may run after the last shot before the script has ended the run.
MARGIN_FRAMES = 60


def vector(text):
    values = [float(v) for v in text.split(',')]
    if len(values) != 3:
        raise argparse.ArgumentTypeError(f'three numbers, not {text!r}')
    return values


def settings(args):
    width, height = (int(v) for v in args.size.split('x'))
    frames = sorted({int(v) for v in args.frames.split(',')})
    if not frames or frames[0] < 1:
        raise SystemExit('--frames: frame numbers from 1')
    rotations = args.rotation or []
    if not args.target and len(rotations) != len(args.position):
        raise SystemExit('one --rotation per --position, or --target')
    if args.target:
        # The camera looks back along its offset: none, or straight up or down, leaves its
        # roll undecided.
        for x, y, z in args.position:
            if x == 0 and z == 0:
                raise SystemExit(f'--position {x},{y},{z}: an offset from the target needs a horizontal part')
    views = [{'position': dict(zip('xyz', p)), 'rotation': dict(zip('xyz', rotations[i] if i < len(rotations) else (0, 0, 0)))}
             for i, p in enumerate(args.position)]
    return {'target': args.target or '', 'hasTargetNear': args.target_near is not None,
            'targetNear': dict(zip('xyz', args.target_near or (0, 0, 0))), 'views': views,
            'fieldOfView': args.fov, 'width': width, 'height': height, 'deltaTime': args.step, 'frames': frames,
            'seed': args.seed, 'keep': getattr(args, 'keep', '') or '', 'soleCameras': bool(getattr(args, 'sole_cameras', False)),
            'listRenderers': bool(getattr(args, 'list_renderers', False)), 'selfTestThrowAtFrame': getattr(args, 'throw_at', 0)}


def prepare(args, fixtures=None):
    """The run's directory with a private copy of the project and the capture's settings
    (and, for the self-test, its fixtures in the copy)."""
    source = Path(args.project).resolve()
    if probe_paths.checkout_project(source):
        raise SystemExit(f'{source} is inside the checkout: captures are of other projects')
    if not (source/args.scene).is_file():
        raise SystemExit(f'{args.scene}: no such scene in {source}')
    out = probe_paths.run_root(RUNS, args.label)
    if out.exists():
        raise SystemExit(f'{out} exists: choose another --label')
    project = out/'project'
    project.parent.mkdir(parents=True)
    subprocess.run(['cp', '-a', '--reflink=auto', str(source), str(project)], check=True)
    for transient in ('Temp', 'Logs', 'UserSettings'):
        shutil.rmtree(project/transient, ignore_errors=True)
    shutil.copytree(SCRIPTS, project/'Assets'/'UnboundSceneCapture', ignore=shutil.ignore_patterns(SELF_TEST))
    if fixtures:
        shutil.copytree(fixtures, project/'Assets'/'UnboundSceneCaptureSelfTest')
    capture = out/'capture'
    capture.mkdir()
    shot = settings(args)
    (capture/'capture.json').write_text(json.dumps(shot, indent=1)+'\n')
    return out, project, capture, shot


def run_editor(args, fixtures=None):
    """One Editor capture: its run directory, capture directory and problem (None when complete)."""
    out, project, capture, _ = prepare(args, fixtures)
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'UnboundSceneCaptureRunner.Record'],
                                  batch=False, graphics=True, timeout=args.timeout, label=f'{RUNS}-{args.label}',
                                  vulkan=bool(getattr(args, 'vulkan', False)),
                                  env={VARIABLE: str(capture), 'UNBOUND_SCENE_CAPTURE_SCENE': args.scene})
    print(editor_runner.summary(editor_runner.run(job)))
    return out, capture, editor_runner.result_problem(out, gpu=True) or editor_runner.jsonl_complete()(capture)


def run_engine(args, fixtures=None):
    """One Unbound capture: its run directory, capture directory and problem (None when complete)."""
    out, project, capture, shot = prepare(args, fixtures)
    frames = max(shot['frames']) + MARGIN_FRAMES
    with (out/'engine.log').open('w') as log:
        code = subprocess.run([str(value_probe.LAUNCHER), '--kind', 'engine-gpu', 'timeout', str(args.timeout), value_probe.engine_cli(),
                               'run', '-p', str(project), '--no-watch', '--scene', args.scene, '--frames', str(frames),
                               *(['--force-vulkan'] if getattr(args, 'vulkan', False) else [])],
                              env={**os.environ, VARIABLE: str(capture)}, stdout=log, stderr=subprocess.STDOUT).returncode
    problem = editor_runner.jsonl_complete()(capture)
    return out, capture, problem and f'{problem}; engine exit {code}; log {out}/engine.log'


def editor(args):
    out, capture, problem = run_editor(args)
    if problem:
        raise SystemExit(f'{out}: {problem}')
    print('captured', capture)
    return 0


def engine(args):
    out, capture, problem = run_engine(args)
    if problem:
        raise SystemExit(f'{out}: {problem}')
    print('captured', capture)
    return 0


def frames_of(capture):
    return {p.name for p in capture.glob('v*-frame-*.png')}


def compare(args):
    from PIL import Image, ImageChops
    first = probe_paths.run_root(RUNS, args.a)/'capture'
    second = probe_paths.run_root(RUNS, args.b)/'capture'
    worst = 0
    settings_a, settings_b = (json.loads((c/'capture.json').read_text()) for c in (first, second))
    for key in sorted(set(settings_a) | set(settings_b)):
        if settings_a.get(key) != settings_b.get(key):
            print(f'settings differ: {key}: {settings_a.get(key)!r} in {args.a}, {settings_b.get(key)!r} in {args.b}')
            worst = 1
    lists = [capture/'renderers.txt' for capture in (first, second)]
    if all(path.is_file() for path in lists):
        from collections import Counter
        a, b = (Counter(path.read_text().splitlines()) for path in lists)
        only_a, only_b = sorted((a - b).elements()), sorted((b - a).elements())
        print(f'renderers: {sum(a.values())} in {args.a}, {sum(b.values())} in {args.b}; {len(only_a)} only in {args.a}, {len(only_b)} only in {args.b}')
        for label, lines in ((args.a, only_a), (args.b, only_b)):
            for line in lines[:40]:
                print(f'  only in {label}: {line}')
        worst = worst or int(bool(only_a or only_b))
    names = sorted(frames_of(first) | frames_of(second))
    if not names:
        raise SystemExit(f'{first}, {second}: no frames')
    for name in names:
        missing = [label for label, capture in ((args.a, first), (args.b, second)) if not (capture/name).exists()]
        if missing:
            print(f'{name}: missing in {", ".join(missing)}')
            worst = 1
            continue
        measured = value_probe.image_difference(first/name, second/name)
        if measured is None:
            print(f'{name}: size differs')
            worst = 1
            continue
        print(f'{name}: mean {measured[0]:.2f}, worst {measured[1]}, over 8: {measured[2]}')
        a = Image.open(first/name).convert('RGB')
        b = Image.open(second/name).convert('RGB')
        sheet = Image.new('RGB', (a.width*3, a.height))
        sheet.paste(a, (0, 0))
        sheet.paste(b, (a.width, 0))
        sheet.paste(ImageChops.difference(a, b), (a.width*2, 0))
        sheet.save(second.parent/('compare-'+name))
    return worst


def selftest(args):
    """The tool on a copy of this probe's project with SceneCapture/SelfTest's fixtures, in the
    Editor and in Unbound: a main camera whose script keeps one instance of itself (the copies
    must keep rendering: two shots of a moving cube differ), and a shot that fails (the run
    must end at once, incomplete, well before its time limit)."""
    from PIL import Image, ImageChops
    stamp = probe_paths.private_label('selftest')
    source = probe_paths.project_copy(HERE, probe_paths.run_root(RUNS, stamp)/'source')
    # The probe's own script plays the scene through and ends the run: not in the self-test.
    shutil.rmtree(source/'Assets'/'Scripts')
    failures = []
    clocks = {}
    sides = {'editor': run_editor, 'engine': run_engine}
    for side in (args.side,) if args.side != 'both' else ('editor', 'engine'):
        run = sides[side]
        base = dict(project=str(source), scene=SELF_TEST_SCENE, position=[[0.0, 0.0, 0.0]], rotation=[[0.0, 0.0, 0.0]],
                    target='', target_near=None, fov=60.0, size='160x90', step=1/30, seed=-1, timeout=args.timeout)
        moving = argparse.Namespace(**base, label=f'{stamp}-{side}-moving', frames='5,20', throw_at=0)
        _, capture, problem = run(moving, FIXTURES)
        if problem:
            failures.append(f'{side} moving: {problem}')
        else:
            a, b = (Image.open(capture/f'v0-frame-{n:03}.png').convert('RGB') for n in (5, 20))
            if ImageChops.difference(a, b).getbbox() is None:
                failures.append(f'{side} moving: frames 5 and 20 are the same image (the copied camera stopped rendering)')
            clocks[side] = [line for line in (capture/'values.jsonl').read_text().splitlines() if '"time/' in line]
        started = time.monotonic()
        failing = argparse.Namespace(**base, label=f'{stamp}-{side}-failing', frames='10', throw_at=3)
        _, capture, problem = run(failing, FIXTURES)
        elapsed = time.monotonic() - started
        if not problem:
            failures.append(f'{side} failing: the run completed although its shot failed')
        elif elapsed > args.timeout / 2:
            failures.append(f'{side} failing: the run took {elapsed:.0f} s of its {args.timeout} s limit to end')
    # A shot's frame number names the same frame on both sides: under the fixed step their
    # clocks are equal.
    if len(clocks) == 2 and clocks['editor'] != clocks['engine']:
        failures.append(f"the shots' clocks differ: Editor {clocks['editor']}, Unbound {clocks['engine']}")
    for failure in failures:
        print('FAIL', failure)
    print('selftest:', 'ok' if not failures else f'{len(failures)} failures', f'(runs {stamp}-*)')
    return 1 if failures else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    modes = parser.add_subparsers(dest='mode', required=True)
    for mode in ('editor', 'engine'):
        run = modes.add_parser(mode)
        run.add_argument('project')
        run.add_argument('scene', help='Assets/... .unity')
        run.add_argument('--label', required=True)
        run.add_argument('--position', type=vector, action='append', required=True)
        run.add_argument('--rotation', type=vector, action='append')
        run.add_argument('--target-near', type=vector)
        run.add_argument('--target', default='', help="an active object's name: the camera looks at it from --position away")
        run.add_argument('--fov', type=float, default=0.0, help="the camera's own when 0")
        run.add_argument('--size', default='480x270')
        run.add_argument('--step', type=float, default=1/30)
        run.add_argument('--frames', default='30')
        run.add_argument('--seed', type=int, default=-1)
        run.add_argument('--sole-cameras', action='store_true', help='every other camera of the scene turned off')
        run.add_argument('--list-renderers', action='store_true', help="the scene's renderers listed in renderers.txt")
        run.add_argument('--vulkan', action='store_true', help='the Vulkan device (the Editor on the GPU display :0)')
        run.add_argument('--keep', choices=('effects', 'others'),
                         help="only that kind of renderer under --target's parent is drawn")
        run.add_argument('--timeout', type=int, default=1800)
    pair = modes.add_parser('compare')
    pair.add_argument('a')
    pair.add_argument('b')
    test = modes.add_parser('selftest')
    test.add_argument('--side', choices=('editor', 'engine', 'both'), default='both')
    test.add_argument('--timeout', type=int, default=900)
    args = parser.parse_args()
    return {'editor': editor, 'engine': engine, 'compare': compare, 'selftest': selftest}[args.mode](args)


if __name__ == '__main__':
    sys.exit(main())
