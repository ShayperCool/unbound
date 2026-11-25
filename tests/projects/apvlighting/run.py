#!/usr/bin/env python3
"""Objects lit by a baked Adaptive Probe Volume, in the Editor and in Unbound; see README.md.

    run.py record --label L       one Editor capture (windowed, OpenGL Core) of the baked project
    run.py publish A B            two byte-identical captures -> Reference/unity-<release>/
    run.py gate [--label L]       Unbound on the baked project, compared with Reference/

The baked scene is the authored set `baked` (bake.py). The capture's scripts live in Capture/,
outside the fixture: `record` and `gate` copy them into the assembled project's
Assets/ApvLightingCapture/, so a change to them leaves the bake usable.
"""
import argparse
import os
from pathlib import Path
import shutil
import sys
import time

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
sys.dont_write_bytecode = True  # nothing is written into the checkout
import authored_project  # noqa: E402
import editor_runner  # noqa: E402
import value_probe  # noqa: E402
from probe_paths import run_root  # noqa: E402

VARIABLE = 'APV_PROBE_OUT'
SET = 'baked'
ENGINE_ARGS = ['--frames', '40', '--size', '64x64']
CAPTURE = 'ApvLightingCapture'


def release():
    return value_probe.unity_version()


def overlay_capture(project):
    """The capture's scripts in the project (Assets/ApvLightingCapture)."""
    target = project/'Assets'/CAPTURE
    if target.exists():
        raise SystemExit(f'{target} exists')
    shutil.copytree(PROBE/'Capture', target, ignore=shutil.ignore_patterns('*.pyc'))
    shutil.copyfile(PROBE/'Capture.meta', project/'Assets'/(CAPTURE+'.meta'))


def editor(project, out, method, label, *, batch, vulkan=False, env=None, timeout=1800):
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', method], batch=batch, graphics=True,
                                  vulkan=vulkan, env=env or {}, timeout=timeout, label='apvlighting-'+label)
    result = editor_runner.run(job)
    print(editor_runner.summary(result))
    return result


def record(label):
    out = run_root(PROBE, label)
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    project = out/'project'
    authored_project.assemble(PROBE.name, release(), SET, project)
    overlay_capture(project)
    capture = out/'capture'
    capture.mkdir(parents=True)
    result = editor(project, out, 'ApvLightingRunner.Record', label, batch=False, env={VARIABLE: str(capture)}, timeout=1200)
    problem = editor_runner.result_problem(out, gpu=True) or editor_runner.jsonl_complete()(capture)
    if problem:
        raise SystemExit(f'{out}: {problem}')
    print('captured', capture)
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=None)
    args = parser.parse_args()
    label = args.label or time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid())
    if args.mode == 'record':
        return record(label)
    if args.mode == 'publish':
        return value_probe.publish(PROBE, args.labels)
    return value_probe.gate(PROBE, VARIABLE, label, 0, ENGINE_ARGS, authored=SET, configure=overlay_capture)


if __name__ == '__main__':
    sys.exit(main())
