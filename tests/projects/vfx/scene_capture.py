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
runs draw alike]. compare prints, per frame, the mean and worst max-channel difference and the
pixels over 8, and writes the two frames and their difference side by side into B's run
directory (compare-vK-frame-NNN.png).
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import editor_runner  # noqa: E402
import probe_paths  # noqa: E402
import value_probe  # noqa: E402

HERE = Path(__file__).resolve().parent
SCRIPTS = HERE/'SceneCapture'
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
    rotations = args.rotation or []
    if not args.target and len(rotations) != len(args.position):
        raise SystemExit('one --rotation per --position, or --target')
    views = [{'position': dict(zip('xyz', p)), 'rotation': dict(zip('xyz', rotations[i] if i < len(rotations) else (0, 0, 0)))}
             for i, p in enumerate(args.position)]
    return {'target': args.target or '', 'hasTargetNear': args.target_near is not None,
            'targetNear': dict(zip('xyz', args.target_near or (0, 0, 0))), 'views': views,
            'fieldOfView': args.fov, 'width': width, 'height': height, 'deltaTime': args.step, 'frames': frames,
            'seed': args.seed}


def prepare(args):
    """The run's directory with a private copy of the project and the capture's settings."""
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
    shutil.copytree(SCRIPTS, project/'Assets'/'UnboundSceneCapture')
    capture = out/'capture'
    capture.mkdir()
    shot = settings(args)
    (capture/'capture.json').write_text(json.dumps(shot, indent=1)+'\n')
    return out, project, capture, shot


def editor(args):
    out, project, capture, _ = prepare(args)
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'UnboundSceneCaptureRunner.Record'],
                                  batch=False, graphics=True, timeout=args.timeout, label=f'{RUNS}-{args.label}',
                                  env={VARIABLE: str(capture), 'UNBOUND_SCENE_CAPTURE_SCENE': args.scene})
    print(editor_runner.summary(editor_runner.run(job)))
    problem = editor_runner.result_problem(out, gpu=True) or editor_runner.jsonl_complete()(capture)
    if problem:
        raise SystemExit(f'{out}: {problem}')
    print('captured', capture)
    return 0


def engine(args):
    out, project, capture, shot = prepare(args)
    cli = os.environ.get('UNBOUND') or os.environ.get('UNBOUND_CLI') or str(value_probe.REPO/'target/release/unbound')
    frames = max(shot['frames']) + MARGIN_FRAMES
    with (out/'engine.log').open('w') as log:
        code = subprocess.run([str(value_probe.LAUNCHER), '--kind', 'engine-gpu', 'timeout', str(args.timeout), cli, 'run',
                               '-p', str(project), '--no-watch', '--scene', args.scene, '--frames', str(frames)],
                              env={**os.environ, VARIABLE: str(capture)}, stdout=log, stderr=subprocess.STDOUT).returncode
    problem = editor_runner.jsonl_complete()(capture)
    if problem:
        raise SystemExit(f'{out}: {problem}; engine exit {code}; log {out}/engine.log')
    print('captured', capture, f'(engine exit {code})')
    return 0


def compare(args):
    from PIL import Image, ImageChops
    first = probe_paths.run_root(RUNS, args.a)/'capture'
    second = probe_paths.run_root(RUNS, args.b)/'capture'
    names = sorted(p.name for p in first.glob('v*-frame-*.png'))
    if not names:
        raise SystemExit(f'{first}: no frames')
    worst = 0
    for name in names:
        if not (second/name).exists():
            print(f'{name}: missing in {args.b}')
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
        run.add_argument('--timeout', type=int, default=1800)
    pair = modes.add_parser('compare')
    pair.add_argument('a')
    pair.add_argument('b')
    args = parser.parse_args()
    return {'editor': editor, 'engine': engine, 'compare': compare}[args.mode](args)


if __name__ == '__main__':
    sys.exit(main())
