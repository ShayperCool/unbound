#!/usr/bin/env python3
"""Bakes, records or runs the light probe API probe.

    run.py create --release R --label L    the Editor builds and bakes Assets/Scenes (Main, Extra) on a
                                           copy; the scenes, bakes and metas are copied back here
    run.py editor --release R --label L    two independent Editor captures
    run.py publish --release R --label L   checks the two captures agree, copies one to Reference/
    run.py unbound --release R --label L   one Unbound capture (UNBOUND_CLI: the binary)
    run.py lifecycle --release R --label L          two independent Editor captures of the scene
                                                    unload event-order witness (Lifecycle/)
    run.py lifecycle-publish --release R --label L  checks they are complete, strict exit 0 and
                                                    identical; writes Reference/.../lifecycle.jsonl
    run.py creation-ownership --release R --label L          two SEQUENTIAL Editor captures of the
                                                             creation-ownership witness
                                                             (CreationOwnership/); the second starts
                                                             only if the first passes every gate
    run.py creation-ownership-publish --release R --label L  checks both are complete, strict exit 0,
                                                             gated, satisfy the schema's contract and
                                                             have identical canonical forms; writes
                                                             that canonical form to
                                                             Reference/.../creation-ownership.jsonl

Every capture runs on a fresh copy of this project in the validation cache. Editors run through
the shared heavy-job launcher on the GPU (a private Xvfb and VirtualGL); do not wrap this driver.
"""
import argparse
import filecmp
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, reference_root, run_root
import editor_runner

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'


def copy_project(dest):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text(
        'm_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    return dest


def display():
    """A private X server for the Editors, which draw with the GPU through VirtualGL."""
    for number in range(120, 161):
        if not Path(f'/tmp/.X{number}-lock').exists():
            server = subprocess.Popen(['Xvfb', f':{number}', '-screen', '0', '1280x720x24', '-nolisten', 'tcp'],
                                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            return f':{number}', server
    sys.exit('no free X display')


def editor_command(project, method, log):
    return [str(LAUNCHER), 'timeout', '-s', 'KILL', '1200', 'vglrun', '-d', 'egl', str(editor_path()),
            '-batchmode', '-buildTarget', 'Standalone', '-projectPath', str(project),
            '-executeMethod', method, '-logFile', str(log)]


def create(work):
    project = copy_project(work/'projects'/'create')
    name, server = display()
    try:
        code = subprocess.run(editor_command(project, 'LightProbesSetup.Create', work/'create.log'),
                              env=dict(os.environ, DISPLAY=name)).returncode
    finally:
        server.terminate()
    print('editor exit', code)
    if code:
        return code
    shutil.copytree(project/'Assets/Scenes', PROBE/'Assets/Scenes', dirs_exist_ok=True)
    for meta in ('Scenes.meta', 'Editor.meta', 'Scripts.meta'):
        shutil.copyfile(project/'Assets'/meta, PROBE/'Assets'/meta)
    for folder in ('Editor', 'Scripts'):
        for meta in (project/'Assets'/folder).glob('*.meta'):
            shutil.copyfile(meta, PROBE/'Assets'/folder/meta.name)
    shutil.copyfile(project/'ProjectSettings/EditorBuildSettings.asset', PROBE/'ProjectSettings/EditorBuildSettings.asset')
    print('copied the Editor-made scenes, bakes and metas into', PROBE/'Assets')
    return 0


def editor(work):
    name, server = display()
    try:
        jobs = []
        for repeat in ('native1', 'native2'):
            project = copy_project(work/'projects'/repeat)
            out = work/repeat/'probe.jsonl'
            out.parent.mkdir(parents=True, exist_ok=True)
            if out.exists():
                sys.exit(f'refusing to overwrite capture: {out}')
            env = dict(os.environ, DISPLAY=name, LIGHT_PROBES_PROBE_OUT=str(out))
            jobs.append(subprocess.Popen(editor_command(project, 'ProbeRunner.Record', work/repeat/'editor.log'), env=env))
        codes = [job.wait() for job in jobs]
        print('editor exits', codes)
        return int(any(codes))
    finally:
        server.terminate()


def publish(work):
    target = reference_root(PROBE)
    target.mkdir(parents=True, exist_ok=True)
    for name in ('probe.jsonl', 'probe.png'):
        a, b = work/'native1'/name, work/'native2'/name
        if not filecmp.cmp(a, b, shallow=False):
            sys.exit(f'{name}: the two Editor captures differ; not publishing')
    for log in (work/'native1'/'editor.log', work/'native2'/'editor.log'):
        if 'NVIDIA' not in next((l for l in open(log, errors='replace') if 'Renderer:' in l), ''):
            sys.exit(f'{log}: the Editor did not render on the NVIDIA GPU; not publishing')
    for name in ('probe.jsonl', 'probe.png'):
        shutil.copyfile(work/'native1'/name, target/name)
        print('published', target/name)
    return 0


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound')
    out = work/'unbound'/'probe.jsonl'
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        sys.exit(f'refusing to overwrite capture: {out}')
    env = dict(os.environ, LIGHT_PROBES_PROBE_OUT=str(out))
    with (work/'unbound'/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                 '--target', 'Standalone', '--no-watch', '--frames', '40', '--size', '320x180'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode)
    return result.returncode


LIFECYCLE = 'lifecycle.jsonl'
LIFECYCLE_TIMEOUT = 1200   # hard deadline per admitted Editor
# Silence budget, including the cold import before the witness creates its output (the main
# probe's fresh-copy captures import in about 10 s and finish in about a minute).
LIFECYCLE_PROGRESS = 600


def lifecycle_project(dest):
    """A fresh copy of the fixture (every scene, bake, meta and setting) in which the witness
    replaces the main probe's recorder: LightProbesProbe quits the Editor when its own output
    variable is unset, and no scene references it."""
    project = copy_project(dest)
    for name in ('LightProbesProbe.cs', 'LightProbesProbe.cs.meta'):
        (project/'Assets/Scripts'/name).unlink()
    for name in ('LightProbesLifecycle.cs', 'LightProbesLifecycle.cs.meta'):
        shutil.copyfile(PROBE/'Lifecycle'/name, project/'Assets/Scripts'/name)
    return project


def lifecycle(work, release):
    jobs = []
    for repeat in ('native1', 'native2'):
        out = work/repeat
        if (out/LIFECYCLE).exists() or (out/editor_runner.RESULT).exists():
            sys.exit(f'refusing to overwrite capture: {out}')
        project = lifecycle_project(work/'projects'/repeat)
        out.mkdir(parents=True, exist_ok=True)
        inputs = {'release': release, 'paths': {
            'project/Assets': str(project/'Assets'), 'project/Packages': str(project/'Packages'),
            'project/ProjectSettings': str(project/'ProjectSettings'),
            'probe/Lifecycle': str(PROBE/'Lifecycle'), 'probe/run.py': str(PROBE/'run.py'),
            'tests/editor_runner.py': str(REPO/'tests/editor_runner.py'),
            'tests/probe_paths.py': str(REPO/'tests/probe_paths.py'),
            'profile': str(REPO/'data/profiles'/('unity-'+'.'.join(release.split('.')[:2])+'.json'))}}
        jobs.append(editor_runner.EditorJob(
            project, out/'editor.log', out, ['-buildTarget', 'Standalone', '-executeMethod', 'ProbeRunner.Record'],
            batch=True, env={'LIGHT_PROBES_LIFECYCLE_OUT': str(out/LIFECYCLE)}, timeout=LIFECYCLE_TIMEOUT,
            progress_files=[out/LIFECYCLE], progress_timeout=LIFECYCLE_PROGRESS, label='lifecycle-'+repeat,
            launch_inputs=inputs))
    results = editor_runner.run_parallel(jobs)
    for result in results:
        print(editor_runner.summary(result))
    problems = [lifecycle_problem(work/r) for r in ('native1', 'native2')]
    for repeat, problem in zip(('native1', 'native2'), problems):
        print(repeat, problem or 'complete, exit 0, no forced exit')
    return int(any(problems))


def lifecycle_problem(directory):
    """Strict: exit 0 without any forced stop, the NVIDIA renderer, a complete witness."""
    result = editor_runner.load_result(directory)
    if result is None:
        return 'no Editor result'
    if result.get('forced_exit'):
        return f"forced exit ({result['forced_exit']}: {result.get('forced_detail')})"
    return editor_runner.result_problem(directory, gpu=True) or editor_runner.jsonl_complete(LIFECYCLE)(directory)


def lifecycle_publish(work):
    for repeat in ('native1', 'native2'):
        problem = lifecycle_problem(work/repeat)
        if problem:
            sys.exit(f'{repeat}: {problem}; not publishing')
    return editor_runner.publish(
        [work/'native1', work/'native2'], reference_root(PROBE)/LIFECYCLE, lambda d: [LIFECYCLE],
        lambda target, first: (shutil.copyfile(first/LIFECYCLE, target), print('published', target)),
        complete=[editor_runner.jsonl_complete(LIFECYCLE)], gpu=True)


CREATION = PROBE/'CreationOwnership'
CREATION_SCRIPTS = ('SceneCreationOwnership.cs', 'CreationOwnershipTracker.cs', 'CreationTeardownWitness.cs')
sys.path.insert(0, str(CREATION))
import contract as creation_contract  # noqa: E402


def creation_schema():
    return json.loads((CREATION/'schema.json').read_text())


def creation_project(dest):
    """A fresh fixture copy in which the creation-ownership witness replaces the main probe's
    recorder, with its tiny prefab under Assets/Resources."""
    project = copy_project(dest)
    for name in ('LightProbesProbe.cs', 'LightProbesProbe.cs.meta'):
        (project/'Assets/Scripts'/name).unlink()
    for name in CREATION_SCRIPTS:
        for file in (name, name+'.meta'):
            shutil.copyfile(CREATION/file, project/'Assets/Scripts'/file)
    shutil.copytree(CREATION/'Resources', project/'Assets/Resources')
    shutil.copyfile(CREATION/'Resources.meta', project/'Assets/Resources.meta')
    return project


def creation_rows_problem(directory, schema=None):
    """The witness output's own gate: complete with the marker last and unique, the declared
    source first, sequence numbers 0..n-1 without duplicates, no error row, every declared point
    exactly once with every alias and its ownership fields, and a raw row for each snapshot."""
    schema = schema or creation_schema()
    name = schema['output']
    problem = editor_runner.jsonl_complete(name)(directory)
    if problem:
        return problem
    rows = [json.loads(line) for line in (Path(directory)/name).read_text().splitlines() if line.strip()]
    source = rows[0]
    want = {'k': 'source', 'suite': schema['suite'], 'schema': schema['schema'],
            'release': schema['release'], 'aliases': schema['aliases']}
    if source != want:
        return f'wrong source row {json.dumps(source)}'
    body = rows[1:-1]
    seqs = [row.get('seq') for row in body]
    if len(set(seqs)) != len(seqs):
        return 'duplicate sequence numbers'
    if seqs != list(range(len(body))):
        return 'sequence numbers are not 0..n-1 in order'
    kinds = set(schema['row_kinds'])
    for row in body:
        if row.get('kind') in schema['rejected_row_kinds']:
            return f"error row {row['seq']}: {row.get('at')}: {row.get('exception')}"
        if row.get('kind') not in kinds:
            return f"row {row['seq']} has an undeclared kind {row.get('kind')!r}"
    points = {}
    for row in body:
        if row['kind'] == 'snapshot' and row.get('point') is not None:
            points.setdefault(row['point'], []).append(row)
    extra = sorted(set(points)-set(schema['points']))
    if extra:
        return f'undeclared points {extra}'
    for point in schema['points']:
        found = points.get(point, [])
        if len(found) != 1:
            return f'point {point} appears {len(found)} times'
        state = found[0].get('state') or {}
        missing = [f for f in schema['state_fields'] if f not in state]
        if missing:
            return f'point {point} lacks state fields {missing}'
        objects = state['objects']
        if list(objects) != schema['aliases']:
            return f'point {point} has object rows {list(objects)}, not {schema["aliases"]}'
        for alias, value in objects.items():
            if 'created' not in value:
                return f'point {point}: {alias} has no ownership row'
            if value['created'] and value.get('alive') is None:
                return f'point {point}: {alias} has no existence'
            if value['created'] and value['alive']:
                lacking = [f for f in schema['ownership_fields'] if f not in value]
                if lacking:
                    return f'point {point}: {alias} lacks ownership fields {lacking}'
    raw_path = Path(directory)/schema['raw_output']
    if not raw_path.exists():
        return f"{schema['raw_output']} is missing"
    raw = [json.loads(line) for line in raw_path.read_text().splitlines() if line.strip()]
    snapshot_seqs = [row['seq'] for row in body if row['kind'] == 'snapshot']
    if [row.get('seq') for row in raw] != snapshot_seqs:
        return 'the raw FindObjectsByType rows do not match the snapshots'
    return None


def creation_problem(directory):
    """Strict: exit 0 without any forced stop, the NVIDIA renderer, the witness gate and the
    schema contract's predicates against this repeat's own trace."""
    result = editor_runner.load_result(directory)
    if result is None:
        return 'no Editor result'
    if result.get('forced_exit'):
        return f"forced exit ({result['forced_exit']}: {result.get('forced_detail')})"
    return (editor_runner.result_problem(directory, gpu=True) or creation_rows_problem(directory)
            or creation_contract.problem(directory))


def creation_ownership(work, release):
    schema = creation_schema()
    if release != schema['release']:
        sys.exit(f"the creation-ownership schema declares {schema['release']}, not {release}")
    for repeat in ('native1', 'native2'):
        out = work/repeat
        if (out/schema['output']).exists() or (out/editor_runner.RESULT).exists():
            sys.exit(f'refusing to overwrite capture: {out}')
        project = creation_project(work/'projects'/repeat)
        out.mkdir(parents=True, exist_ok=True)
        inputs = {'release': release, 'paths': {
            'project/Assets': str(project/'Assets'), 'project/Packages': str(project/'Packages'),
            'project/ProjectSettings': str(project/'ProjectSettings'),
            'probe/CreationOwnership': str(CREATION), 'probe/run.py': str(PROBE/'run.py'),
            'tests/editor_runner.py': str(REPO/'tests/editor_runner.py'),
            'tests/probe_paths.py': str(REPO/'tests/probe_paths.py'),
            'profile': str(REPO/'data/profiles'/('unity-'+'.'.join(release.split('.')[:2])+'.json'))}}
        result = editor_runner.run(editor_runner.EditorJob(
            project, out/'editor.log', out, ['-buildTarget', 'Standalone', '-executeMethod', 'ProbeRunner.Record'],
            batch=True, env={'LIGHT_PROBES_CREATION_OUT': str(out/schema['output']),
                             'LIGHT_PROBES_CREATION_RAW_OUT': str(out/schema['raw_output']),
                             'LIGHT_PROBES_CREATION_TEARDOWN_OUT': str(out/schema['teardown_output'])},
            timeout=LIFECYCLE_TIMEOUT, progress_files=[out/schema['output']], progress_timeout=LIFECYCLE_PROGRESS,
            label='creation-ownership-'+repeat, launch_inputs=inputs))
        print(editor_runner.summary(result))
        problem = creation_problem(out)
        print(repeat, problem or 'complete, exit 0, no forced exit, gate passed')
        if problem:
            print('stopping: no further launch')
            return 1
    return 0


def creation_publish(directories, target, report=None):
    """Both repeats pass every gate and the contract; their canonical forms are byte-identical
    (every raw difference is in a declared family); `target` is new. Writes the canonical form."""
    directories = [Path(d) for d in directories]
    if len(directories) != 2:
        sys.exit('publishing needs exactly two independent repeats')
    for directory in directories:
        problem = creation_problem(directory)
        if problem:
            sys.exit(f'{directory.name}: {problem}; not publishing')
    texts = [creation_contract.dumps(creation_contract.canonical(d)) for d in directories]
    raw = creation_contract.differences(*directories)
    strict = [d for d in raw if d['family'] is None]
    if report is not None:
        Path(report).write_text(json.dumps({
            'repeats': [str(d) for d in directories], 'canonical_identical': texts[0] == texts[1],
            'raw_differences': raw, 'strict_differences': strict,
            'canonical_differences': creation_contract.canonical_differences(*directories)},
            indent=1, default=str)+'\n')
    if texts[0] != texts[1] or strict:
        sys.exit(f'the canonical forms differ ({len(strict)} strict raw differences of {len(raw)}); not publishing')
    target = Path(target)
    if target.exists():
        sys.exit(f'refusing to overwrite reference: {target}')
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(texts[0])
    print('published', target)
    return 0


def creation_ownership_publish(work):
    schema = creation_schema()
    return creation_publish([work/'native1', work/'native2'], reference_root(PROBE)/schema['output'],
                            report=work/'publication.json')


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'editor', 'publish', 'unbound', 'lifecycle', 'lifecycle-publish',
                                           'creation-ownership', 'creation-ownership-publish'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    if args.mode == 'lifecycle':
        return lifecycle(work, args.release)
    if args.mode == 'lifecycle-publish':
        return lifecycle_publish(work)
    if args.mode == 'creation-ownership':
        return creation_ownership(work, args.release)
    if args.mode == 'creation-ownership-publish':
        return creation_ownership_publish(work)
    return {'create': create, 'editor': editor, 'publish': publish, 'unbound': unbound}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
