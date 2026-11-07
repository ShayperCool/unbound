#!/usr/bin/env python3
"""Record the Unity Editor (native), run Unbound (engine), publish and verify.

    run.py native --label native1          # a private project copy, GPU Editor through the job pool
    run.py publish native1 native2         # two identical captures become Reference/unity-<version>/
    run.py engine --label final            # Unbound (UNBOUND_CLI or target/release/unbound), with a GPU
    run.py verify [--candidate DIR]        # compare against the reference (default: the engine run 'final')
    run.py check                           # engine run into a new directory, then verify (the landing gate)

The release is $UNBOUND_PROBE_UNITY_VERSION (default 6000.0.71f1). Run output stays under
$XDG_CACHE_HOME/unbound/validation/runs/particletrails/.
"""
import argparse
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import reference_root, run_root  # noqa: E402
import editor_runner  # noqa: E402

WORK = run_root(ROOT, 'runs')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'values.jsonl'
IMAGES = 'images'


def native(label):
    out = WORK/label
    project = out/'project'
    if project.exists():
        raise SystemExit(f'refusing to overwrite {project}')
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(ROOT/folder, project/folder)
    env = {'PARTICLE_TRAILS_OUT': str(out/OUT), 'PARTICLE_TRAILS_IMAGES': str(out/IMAGES)}
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'ProbeRunner.Record'],
                                  batch=True, graphics=True, env=env, timeout=1200, label='particletrails-'+label)
    print(editor_runner.summary(editor_runner.run(job)))
    return 0 if (out/OUT).exists() else 1


def engine(label):
    out = WORK/label
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    out.mkdir(parents=True)
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    env = dict(os.environ, PARTICLE_TRAILS_OUT=str(out/OUT), PARTICLE_TRAILS_IMAGES=str(out/IMAGES))
    command = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '600', cli, 'run', '-p', str(ROOT),
               '--frames', '400', '--size', '320x180', '--no-watch']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    print(label, 'exit', code)
    return 0 if code == 0 and (out/OUT).exists() else 1


def names(directory):
    images = sorted('images/'+p.name for p in (Path(directory)/IMAGES).glob('*.png'))
    return [OUT] + images


def publish(labels):
    def write(target, first):
        target.mkdir(parents=True)
        shutil.copy2(first/OUT, target/OUT)
        shutil.copytree(first/IMAGES, target/IMAGES)
    return editor_runner.publish([WORK/label for label in labels], reference_root(ROOT), names, write,
                                 complete=[editor_runner.jsonl_complete(OUT, {'key': 'complete', 'value': [1]})])


def verify(candidate):
    reference = reference_root(ROOT)
    code = subprocess.run([sys.executable, str(ROOT/'compare.py'), str(reference/OUT), str(candidate/OUT),
                           '--known', str(ROOT/'known.json')]).returncode
    subprocess.run([sys.executable, str(ROOT.parent/'particles/compare_images.py'), str(reference/IMAGES), str(candidate/IMAGES)])
    return code


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['native', 'engine', 'publish', 'verify', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--candidate', type=Path)
    args = parser.parse_args()
    if args.mode == 'native':
        return native(args.label)
    if args.mode == 'engine':
        return engine(args.label)
    if args.mode == 'publish':
        return publish(args.labels)
    if args.mode == 'check':
        label = time.strftime('check-%Y%m%d-%H%M%S-') + str(os.getpid())
        return engine(label) or verify(WORK/label)
    return verify(args.candidate or WORK/'final')


if __name__ == '__main__':
    sys.exit(main())
