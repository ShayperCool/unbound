#!/usr/bin/env python3
"""Precomputed realtime global illumination probe.

    run.py setup                              # Editor: (re)make the scenes and precompute them in a copy, copy them back
    run.py setup-systems                      # Editor: add the RoomSystems scene alone
    run.py setup-baked-environment            # Editor: add the RoomBakedEnvironment scene alone
    run.py setup-charts                       # Editor: add the RoomCharts scene alone
    run.py record --label native1             # Editor capture (GPU, through the job pool)
    run.py publish native1 native2            # two identical captures become Reference/unity-<version>/
    run.py gate --label gate-1                # engine run compared with Reference/ (the land gate)
    run.py record|publish|gate --pipeline urp # the same project drawn by URP: its own reference set, Reference/unity-<version>/urp

The release is $UNBOUND_PROBE_UNITY_VERSION (default 6000.0.71f1). Run output stays under
$XDG_CACHE_HOME/unbound/validation/runs/realtimegi/<label>/.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(PROBE.parents[1]))
from probe_paths import cache_artifact_path, reference_root, run_root, unity_version  # noqa: E402
import editor_runner  # noqa: E402

HEAVY = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'values.jsonl'
END = {'case': 'end'}
MADE = ('Scenes', 'Settings')


# The scriptable render pipeline a run can give the project (`--pipeline`): its package, and the
# pipeline assets and graphics settings of the probe that owns them.
PIPELINES = {'urp': {'package': ('com.unity.render-pipelines.universal', '17.0.4'),
                     'modules': ('com.unity.modules.physics', 'com.unity.modules.terrain'),
                     'project': PROBE.parent/'urp',
                     'assets': ('Assets/Settings/ProbeURP.asset', 'Assets/Settings/ProbeRenderer.asset',
                                'Assets/UniversalRenderPipelineGlobalSettings.asset', 'Assets/DefaultVolumeProfile.asset')}}


def copy_project(target, pipeline=None):
    if target.exists():
        raise SystemExit(f'refusing to overwrite {target}')
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/folder, target/folder)
    (target/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+unity_version()+'\n')
    if pipeline:
        # The same project drawn by the pipeline: its package, its assets (with their GUIDs) and
        # the graphics settings that name them.
        made = PIPELINES[pipeline]
        manifest = json.loads((target/'Packages/manifest.json').read_text())
        manifest['dependencies'][made['package'][0]] = made['package'][1]
        manifest['dependencies'].update({module: '1.0.0' for module in made['modules']})
        (target/'Packages/manifest.json').write_text(json.dumps(manifest, indent=2)+'\n')
        (target/'Assets/Pipeline').mkdir()
        for asset in made['assets']:
            for name in (asset, asset+'.meta'):
                shutil.copy2(made['project']/name, target/'Assets/Pipeline'/Path(name).name)
        shutil.copy2(made['project']/'ProjectSettings/GraphicsSettings.asset', target/'ProjectSettings/GraphicsSettings.asset')
    return target


def reference(pipeline=None):
    """The reference set: the built-in pipeline's, or a pipeline's own beside it."""
    return reference_root(PROBE)/pipeline if pipeline else reference_root(PROBE)


def work(label):
    return cache_artifact_path(run_root(PROBE, label))


def outputs(directory):
    """The capture's files: values.jsonl, then every atlas, directionality and frame it wrote."""
    directory = Path(directory)
    names = sorted(p.name for p in directory.iterdir() if p.suffix in ('.atlas', '.dir', '.png'))
    return [OUT, *names]


def expected(pipeline=None):
    """Atlas and frame names the probe writes (from the committed reference when present)."""
    committed = reference(pipeline)
    return outputs(committed)[1:] if committed.is_dir() else []


def complete(directory, pipeline=None):
    problem = editor_runner.jsonl_complete(OUT, END)(directory)
    if problem:
        return problem
    missing = [name for name in expected(pipeline) if not (Path(directory)/name).is_file()]
    return f'missing {missing}' if missing else None


def setup(label):
    """Runs RealtimeGISetup.Create in a copy, then copies the made scenes, settings and data back."""
    out = work(label)
    project = out/'project'
    copy_project(project)
    for made in MADE:
        shutil.rmtree(project/'Assets'/made, ignore_errors=True)
        (project/'Assets'/(made+'.meta')).unlink(missing_ok=True)
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'RealtimeGISetup.Create'],
                                  batch=True, graphics=True, timeout=3600, label='realtimegi-'+label)
    result = editor_runner.run(job)
    print(editor_runner.summary(result))
    if result.get('exit_code') != 0:
        return 1
    for made in (*MADE, 'Scripts', 'Editor', 'Shaders'):
        shutil.rmtree(PROBE/'Assets'/made, ignore_errors=True)
        shutil.copytree(project/'Assets'/made, PROBE/'Assets'/made)
        shutil.copy2(project/'Assets'/(made+'.meta'), PROBE/'Assets'/(made+'.meta'))
    for name in ('EditorBuildSettings.asset', 'GraphicsSettings.asset'):
        shutil.copy2(project/'ProjectSettings'/name, PROBE/'ProjectSettings'/name)
    return 0


# The scenes added to the made ones one at a time: the mode's name, the scene and what makes it.
ADDED = {'setup-systems': ('RoomSystems', 'RealtimeGISetup.CreateSystems'),
         'setup-baked-environment': ('RoomBakedEnvironment', 'RealtimeGISetup.CreateBakedEnvironment'),
         'setup-charts': ('RoomCharts', 'RealtimeGISetup.CreateCharts')}


def setup_scene(label, scene, method):
    """Runs `method` in a copy, then copies the scene it adds, its settings and the build settings
    back (the other scenes stay as they are)."""
    out = work(label)
    project = out/'project'
    copy_project(project)
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', method],
                                  batch=True, graphics=True, timeout=3600, label='realtimegi-'+label)
    result = editor_runner.run(job)
    print(editor_runner.summary(result))
    if result.get('exit_code') != 0:
        return 1
    for made in MADE:
        for path in (project/'Assets'/made).iterdir():
            if path.name.startswith(scene):
                target = PROBE/'Assets'/made/path.name
                if path.is_dir():
                    shutil.rmtree(target, ignore_errors=True)
                    shutil.copytree(path, target)
                else:
                    shutil.copy2(path, target)
    shutil.copy2(project/'ProjectSettings'/'EditorBuildSettings.asset', PROBE/'ProjectSettings'/'EditorBuildSettings.asset')
    return 0


def record(label, pipeline=None):
    out = work(label)
    project = copy_project(out/'project', pipeline)
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'RealtimeGIRunner.Record'],
                                  batch=True, graphics=True, env={'REALTIME_GI_OUT': str(out)},
                                  timeout=1800, label='realtimegi-'+label)
    print(editor_runner.summary(editor_runner.run(job)))
    problem = editor_runner.result_problem(out, gpu=True) or complete(out, pipeline)
    if problem:
        print(f'capture {label}: {problem}', file=sys.stderr)
        return 1
    return 0


def publish(labels, pipeline=None):
    target = reference(pipeline)

    def write(path, source):
        path.mkdir(parents=True)
        for name in outputs(source):
            shutil.copy2(source/name, path/name)
    return editor_runner.publish([work(label) for label in labels], target, outputs, write,
                                 complete=[lambda directory: complete(directory, pipeline)], gpu=True)


def gate(label, frames, pipeline=None):
    out = work(label)
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    out.mkdir(parents=True)
    project = copy_project(out/'project', pipeline)
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    env = {**os.environ, 'REALTIME_GI_OUT': str(out/'engine')}
    command = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '1500', cli, 'run', '-p', str(project),
               '--no-watch', '--frames', str(frames), '--size', '320x180']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    print(f'engine exit {code}; log {out}/engine.log')
    return subprocess.run([sys.executable, str(PROBE/'compare.py'), str(reference(pipeline)),
                           str(out/'engine')]).returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['setup', *ADDED, 'record', 'publish', 'gate'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default='current')
    # Enough frames for every case to converge (each waits at least 360) and the run to end itself.
    parser.add_argument('--frames', type=int, default=40000)
    parser.add_argument('--pipeline', choices=sorted(PIPELINES),
                        help='record, publish or gate the project drawn by this render pipeline (its own reference set)')
    args = parser.parse_args()
    if args.mode == 'setup':
        return setup(args.label)
    if args.mode in ADDED:
        return setup_scene(args.label, *ADDED[args.mode])
    if args.mode == 'record':
        return record(args.label, args.pipeline)
    if args.mode == 'publish':
        return publish(args.labels, args.pipeline)
    return gate(args.label, args.frames, args.pipeline)


if __name__ == '__main__':
    sys.exit(main())
