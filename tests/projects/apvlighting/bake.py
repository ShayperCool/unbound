#!/usr/bin/env python3
"""Authors the apvlighting probe's baked scene (the authored set `baked`); see README.md.

    bake.py create            makes Assets/Scenes and Assets/Materials (the Editor, batch mode)
    bake.py bake --label L    bakes the scene in the Vulkan Editor (batch mode) and keeps what it
                              wrote as Authored/unity-<release>/baked

The set records this driver's hash: run.py (record, publish, gate) may change without a new bake.
"""
import argparse
from pathlib import Path
import shutil
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
sys.dont_write_bytecode = True  # nothing is written into the checkout
import authored_project  # noqa: E402
import editor_runner  # noqa: E402
import value_probe  # noqa: E402
from probe_paths import private_label, project_copy, run_root  # noqa: E402

SET = 'baked'
BAKE_COMMAND = 'python3 tests/projects/apvlighting/bake.py bake'


def editor(project, out, method, label, *, batch, vulkan=False, env=None, timeout=1800):
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', method], batch=batch, graphics=True,
                                  vulkan=vulkan, env=env or {}, timeout=timeout, label='apvlighting-'+label)
    result = editor_runner.run(job)
    print(editor_runner.summary(result))
    return result


def create():
    """Makes the scene, its materials and Lighting Settings; nothing that exists is replaced."""
    for part in ('Assets/Scenes', 'Assets/Materials'):
        if (PROBE/part).exists():
            raise SystemExit(f'refusing to overwrite {PROBE/part}')
    out = run_root(PROBE, private_label('create'))
    project_copy(PROBE, out/'project', package_cache=False)
    result = editor(out/'project', out, 'ApvLightingSetup.Create', 'create', batch=True)
    if result.get('exit_code') != 0 or result.get('forced_exit'):
        return 1
    for part in ('Assets/Scenes', 'Assets/Materials'):
        shutil.copytree(out/'project'/part, PROBE/part)
        shutil.copyfile(out/'project'/(part+'.meta'), PROBE/(part+'.meta'))
    for path in ('Packages/packages-lock.json', 'ProjectSettings/EditorBuildSettings.asset'):
        shutil.copyfile(out/'project'/path, PROBE/path)
    return 0


def bake(label):
    out = run_root(PROBE, label)
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    project = out/'project'
    value_probe.assemble(PROBE, out)
    result = editor(project, out, 'ApvLightingSetup.Bake', label, batch=True, vulkan=True)
    if result.get('exit_code') != 0 or result.get('forced_exit'):
        return 1
    authored_project.record(PROBE.name, value_probe.unity_version(), SET, project, BAKE_COMMAND)
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'bake'))
    parser.add_argument('--label')
    args = parser.parse_args()
    if args.mode == 'create':
        return create()
    if not args.label:
        parser.error('bake needs --label')
    return bake(args.label)


if __name__ == '__main__':
    sys.exit(main())
