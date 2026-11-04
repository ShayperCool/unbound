#!/usr/bin/env python3
"""Records the core API probe in the Unity Editor or runs it in Unbound.

    run.py create  --release R --label L    runs ProbeRunner.Create on a copy, copies its assets back here
    run.py editor  --release R --label L --repeat native1 --preflight P
                                            one windowed GPU Editor capture (the Game view renders)
    run.py check   --release R --label L --repeat native1
                                            every guard problem of that capture; a passing capture is
                                            admitted (admission.json, written once)
    run.py editor  --release R --label L --repeat native2 --preflight P
                                            the independent repeat; only after native1's admission,
                                            with the same sources, inputs and preflight
    run.py check   --release R --label L --repeat native2
    run.py publish --release R --label L    both admitted repeats agree byte for byte. A new release's
                                            reference is written once (never overwritten). An existing
                                            reference is only compared: equal bytes leave it as it is;
                                            any difference prints the full delta and stops (exit 3)
    run.py unbound --release R --label L    an Unbound capture (UNBOUND_CLI: the binary)
    run.py editor|publish ... --dry-run     prints the exact plan; starts and writes nothing

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/coreapis/<label>; nothing is written here (except by
create and by publishing a new release's reference). Editors run through tests/editor_runner.py:
each repeat takes its own GPU lease (do not wrap this driver in with-heavy-job), a private Xvfb
inside the lease and `vglrun -d egl`, windowed: no -batchmode, -nographics, -quit or
-executeMethod (ProbeRunner enters Play mode from InitializeOnLoad and quits from Edit mode).
Batch mode does not render the Game view, so the 17-frame OnDemandRendering window (screen camera,
OnGUI, WaitForEndOfFrame) is never measured there; this runner has no batch capture.
The repeats run one after the other; a label and each repeat directory are write-once.

`--preflight` names the preflight.json of a passing preflight.py run whose compiled sources are
byte-identical to this checkout's probe scripts; without it no Editor starts.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
from probe_paths import editor_path, reference_file, run_root  # noqa: E402
import editor_runner  # noqa: E402

sys.path.insert(0, str(PROBE))
import compare  # noqa: E402
import guards  # noqa: E402

LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
REPEATS = ('native1', 'native2')
# Hard deadline of one admitted Editor run, cold import and shutdown included.
HARD_TIMEOUT = 1200
# The probe writes its record once, at the end (CoreProbe.Finish), so the progress deadline is the
# longest time from admission to the record, cold import included. The previous Native pair
# (two concurrent cold-import windowed GPU Editors) wrote it 53 s and 90 s after their copies were
# made (an earlier pair: 76 s and 86 s); 300 s is more than three times the slowest. Only the
# record counts as progress, never Editor log output. ProbeRunner's own 600 s Play mode watchdog
# stays as a further check inside the Editor.
PROGRESS_TIMEOUT = 300
SHUTDOWN_GRACE = 30
KILL_GRACE = 10
VARIABLE = guards.OUTPUT_VARIABLE
SOURCES = tuple(sorted(str(p.relative_to(PROBE)) for p in (PROBE/'Assets').rglob('*.cs')))
TOOLS = ('run.py', 'guards.py', 'compare.py', 'preflight.py')
SHARED = ('tests/editor_runner.py', 'tests/probe_paths.py')
EXIT_STOP = 3


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def profile_path(release):
    return REPO/'data/profiles'/('unity-'+'.'.join(release.split('.')[:2])+'.json')


def prepared_files(project):
    files = {}
    for label, part in (('assets', 'Assets'), ('packages', 'Packages'), ('settings', 'ProjectSettings')):
        files.update({label+'/'+k: v for k, v in guards.tree(Path(project)/part).items()})
    return files


def copy_project(dest):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text(
        'm_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    return dest


def git_state():
    def git(*args):
        return subprocess.run(['git', '-C', str(REPO), *args], capture_output=True, text=True).stdout
    paths = [str(PROBE)]+[str(REPO/s) for s in SHARED]
    status = git('status', '--porcelain', '--untracked-files=all', '--', *paths)
    return {'commit': git('rev-parse', 'HEAD').strip(), 'tree': git('rev-parse', 'HEAD^{tree}').strip(),
            'status': status.splitlines(), 'clean': status == ''}


def source_manifest(release, preflight):
    """What a label's repeats must share: checkout, fixture, scripts, runner, shared helpers,
    profile, the existing reference and the preflight record."""
    target = reference_file(PROBE, 'jsonl')
    return {'release': release, 'git': git_state(), 'fixture': prepared_files(PROBE),
            'sources': {s: sha(PROBE/s) for s in SOURCES}, 'tools': {t: sha(PROBE/t) for t in TOOLS},
            'shared': {s: sha(REPO/s) for s in SHARED}, 'profile': {str(profile_path(release)): sha(profile_path(release))},
            'reference': {'path': str(target), 'sha256': sha(target) if target.exists() else None},
            'preflight': {'path': str(Path(preflight).resolve()), 'sha256': sha(preflight)} if preflight else None,
            'deadlines': {'hard_s': HARD_TIMEOUT, 'progress_s': PROGRESS_TIMEOUT,
                          'shutdown_grace_s': SHUTDOWN_GRACE, 'kill_grace_s': KILL_GRACE}}


def paths(work, repeat):
    return work/'projects'/repeat, work/repeat


def job(work, repeat, release):
    project, out = paths(work, repeat)
    project, out = project.resolve(), out.resolve()
    inputs = {'assets': str(project/'Assets'), 'packages': str(project/'Packages'),
              'settings': str(project/'ProjectSettings'), 'profile': str(profile_path(release))}
    inputs.update({t.split('.')[0]: str(PROBE/t) for t in TOOLS})
    inputs.update({Path(s).stem: str(REPO/s) for s in SHARED})
    return editor_runner.EditorJob(
        project, out/'editor.log', out, env={VARIABLE: str(out/'values.jsonl'), 'UNBOUND_PROBE_UNITY_VERSION': release},
        batch=False, graphics=True, timeout=HARD_TIMEOUT, shutdown_grace=SHUTDOWN_GRACE, kill_grace=KILL_GRACE,
        progress_files=(out/'values.jsonl',), progress_timeout=PROGRESS_TIMEOUT, label='coreapis-'+repeat,
        launch_inputs={'release': release, 'paths': inputs})


def describe(j):
    return {'label': j.label, 'kind': j.kind, 'command': j.command(), 'batch': j.batch, 'graphics': j.graphics,
            'env': j.env, 'timeout': j.timeout, 'shutdown_grace': j.shutdown_grace, 'kill_grace': j.kill_grace,
            'progress_files': j.progress_files, 'progress_timeout': j.progress_timeout, 'project': str(j.project),
            'out': str(j.out), 'cwd': str(j.cwd), 'screen': j.screen, 'launch_inputs': j.launch_inputs}


def preflight_problems(path, release):
    try:
        record = json.loads(Path(path).read_text())
    except (OSError, TypeError, json.JSONDecodeError) as error:
        return [f'no preflight record: {error}']
    problems = []
    if record.get('passed') is not True:
        problems.append('the preflight did not pass')
    if record.get('release') != release:
        problems.append(f"the preflight compiled for {record.get('release')}")
    compiled = record.get('sources', {})
    for source in SOURCES:
        if compiled.get(source) != sha(PROBE/source):
            problems.append(f'{source} differs from the preflight-compiled bytes')
    if set(compiled) != set(SOURCES):
        problems.append(f'the preflight compiled {sorted(compiled)}, not {list(SOURCES)}')
    if set(record.get('results', {})) != {'Assembly-CSharp', 'Assembly-CSharp-Editor'}:
        problems.append('the preflight did not compile both assemblies')
    return problems


DAG = re.compile(r'Library/Bee/artifacts/[^/"]+\.dag/')


def graph_problems(project, preflight):
    """The launched Editor's own response files must be the ones the preflight compiled: the same
    sources, references, defines, analyzers and options (only the graph directory name is neutral),
    and its source generators' additional file must name the launched copy."""
    dags = sorted(Path(project).glob('Library/Bee/artifacts/*.dag'))
    dags = [d for d in dags if (d/'Assembly-CSharp.rsp').is_file()]
    if len(dags) != 1:
        return [f'the launched copy has {len(dags)} compile graphs with Assembly-CSharp.rsp, not one']
    problems = []
    for assembly, result in preflight.get('results', {}).items():
        for suffix in ('rsp', 'rsp2'):
            path = dags[0]/f'{assembly}.{suffix}'
            want = result.get(suffix, {}).get('compiled_text')
            if not path.is_file():
                problems.append(f'the launched copy has no {assembly}.{suffix}')
            elif want is None or DAG.sub('DAG/', path.read_text()) != DAG.sub('DAG/', want):
                problems.append(f'{assembly}.{suffix} differs from the preflight-compiled graph')
        extra = dags[0]/f'{assembly}.UnityAdditionalFile.txt'
        if not extra.is_file() or Path(extra.read_text().strip()).resolve() != Path(project).resolve():
            problems.append(f'{assembly}.UnityAdditionalFile.txt does not name the launched copy')
    return problems


def artifact_manifest(out):
    """SHA-256 and size of every file the launch wrote under its output directory."""
    return {str(p.relative_to(out)): {'sha256': sha(p), 'bytes': p.stat().st_size}
            for p in sorted(Path(out).rglob('*')) if p.is_file() and p.name not in ('artifact-manifest.json', 'admission.json')}


# The one deliberate change of the private copy (copy_project, as for the existing reference's
# captures): ProjectVersion.txt names only the selected release. The Editor writes the release's
# full version file back on open (both copies of the previous Native pair held the fixture's bytes
# afterwards); guards.authored_problems accepts exactly that.
DELIBERATE = 'settings/ProjectVersion.txt'


def deliberate(manifest, project):
    return {DELIBERATE: {'fixture_sha256': manifest['fixture'][DELIBERATE],
                         'prepared_sha256': prepared_files(project)[DELIBERATE],
                         'prepared_text': (Path(project)/'ProjectSettings/ProjectVersion.txt').read_text()}}


def write_once(path, value):
    with Path(path).open('x') as stream:
        stream.write(json.dumps(value, indent=1)+'\n')


def editor(work, repeat, release, preflight, dry_run=False):
    j = job(work, repeat, release)
    project, out = paths(work, repeat)
    if dry_run:
        print(json.dumps({'plan': describe(j), 'fresh': not out.exists() and not project.exists(),
                          'label_manifest_exists': (work/'label-manifest.json').exists(),
                          'native1_admitted': (work/'native1/admission.json').exists(),
                          'manifest': source_manifest(release, preflight if preflight and Path(preflight).exists() else None)},
                         indent=1))
        return 0
    problems = preflight_problems(preflight, release)
    if problems:
        sys.exit('no launch: '+'; '.join(problems))
    manifest = source_manifest(release, preflight)
    if not manifest['git']['clean']:
        sys.exit(f"no launch: the probe or the shared runner has uncommitted changes: {manifest['git']['status'][:5]}")
    label = work/'label-manifest.json'
    if repeat == 'native1':
        if label.exists() or (work/'native2').exists():
            sys.exit(f'refusing to reuse label {work.name}: a label is write-once')
    else:
        first = admission_problems(work, 'native1', release)
        if first:
            sys.exit(f'native1 is not admitted ({len(first)} problems, first: {first[0]}); the repeat does not start')
        if json.loads(label.read_text()) != manifest:
            sys.exit('the checkout, fixture, runner, reference or preflight differ from native1\'s; the repeat does not start')
    if out.exists() or project.exists():
        sys.exit(f'refusing to reuse {out}: a launch is never repeated in a label')
    if repeat == 'native1':
        work.mkdir(parents=True, exist_ok=True)
        write_once(label, manifest)
    copy_project(project)
    out.mkdir(parents=True)
    j = job(work, repeat, release)
    write_once(out/'inputs.json', {'release': release, 'repeat': repeat, 'files': prepared_files(project),
                                   'deliberate': deliberate(manifest, project),
                                   'label_manifest_sha256': sha(label), 'plan': describe(j)})
    result = editor_runner.run(j)
    (out/'authored-after.json').write_text(json.dumps(prepared_files(project), indent=1)+'\n')
    (out/'authored-delta.json').write_text(json.dumps(guards.authored_delta(out, project), indent=1)+'\n')
    (out/'artifact-manifest.json').write_text(json.dumps(artifact_manifest(out), indent=1)+'\n')
    print(editor_runner.summary(result))
    problems = check_problems(work, repeat, release)
    for p in problems:
        print(' ', p)
    print(f'{repeat}:', 'passes the guards (admit it with check)' if not problems else f'{len(problems)} problems')
    return int(bool(problems))


def check_problems(work, repeat, release):
    """Every guard problem of one capture, plus its launch identity: the label's manifest, the
    checkout's current runner and the compile graph the Editor actually used."""
    project, out = paths(work, repeat)
    if not out.exists():
        return [f'{repeat} was not captured']
    problems = guards.capture_problems(out, project, release, HARD_TIMEOUT, PROGRESS_TIMEOUT)
    try:
        label = work/'label-manifest.json'
        manifest = json.loads(label.read_text())
        prepared = json.loads((out/'inputs.json').read_text())
        if prepared.get('label_manifest_sha256') != sha(label):
            problems.append('the capture was not launched under this label\'s manifest')
        if prepared.get('release') != release or manifest.get('release') != release:
            problems.append('the capture was launched for another release')
        if manifest['tools'] != {t: sha(PROBE/t) for t in TOOLS} or manifest['sources'] != {s: sha(PROBE/s) for s in SOURCES}:
            problems.append('this checkout\'s probe scripts or runner differ from the launched ones')
        intended = dict(manifest['fixture'])
        intended[DELIBERATE] = hashlib.sha256(('m_EditorVersion: '+release+'\n').encode()).hexdigest()
        if prepared['files'] != intended or prepared.get('deliberate', {}).get(DELIBERATE, {}).get('fixture_sha256') \
                != manifest['fixture'][DELIBERATE]:
            problems.append('the project copy was not prepared with the fixture\'s bytes and the one deliberate setting')
        if (manifest['reference']['sha256'] or None) != (sha(manifest['reference']['path'])
                                                        if Path(manifest['reference']['path']).exists() else None):
            problems.append('the existing reference changed after the label was made')
        preflight = manifest['preflight']
        if sha(preflight['path']) != preflight['sha256']:
            problems.append('the preflight record changed after the launch')
        problems += [f'graph: {p}' for p in graph_problems(project, json.loads(Path(preflight['path']).read_text()))]
    except (OSError, KeyError, TypeError, json.JSONDecodeError) as error:
        problems.append(f'no launch record for the identity checks: {error}')
    return problems


def admission_problems(work, repeat, release):
    out = work/repeat
    try:
        admission = json.loads((out/'admission.json').read_text())
    except (OSError, json.JSONDecodeError) as error:
        return [f'{repeat} has no admission: {error}']
    problems = []
    if admission.get('artifacts') != artifact_manifest(out):
        problems.append(f'{repeat} changed after its admission')
    return problems+check_problems(work, repeat, release)


def check(work, repeat, release):
    problems = check_problems(work, repeat, release)
    for p in problems:
        print(p)
    if problems:
        print(f'{repeat}: {len(problems)} problems; not admitted')
        return 1
    out = work/repeat
    _, notes = guards.values_problems(out/'values.jsonl')
    admission = {'repeat': repeat, 'release': release, 'values_sha256': sha(out/'values.jsonl'),
                 'artifacts': artifact_manifest(out), 'notes': notes}
    if (out/'admission.json').exists():
        if json.loads((out/'admission.json').read_text()) != admission:
            sys.exit(f'{repeat}: an earlier admission names other bytes')
        print(f'{repeat}: passes; already admitted')
        return 0
    write_once(out/'admission.json', admission)
    print(f'{repeat}: passes; admitted ({len(notes)} notes)')
    return 0


def publish(work, release, dry_run=False, target=None):
    """Two admitted, byte-identical repeats. A new release's reference is written once. An existing
    reference is never rewritten: the pair is compared with it in full, and any difference stops
    for adjudication with every missing and extra line."""
    target = Path(target) if target else reference_file(PROBE, 'jsonl')
    report = work/'publish-comparison.json'
    problems = [p for r in REPEATS for p in admission_problems(work, r, release)]
    if not problems:
        problems = editor_runner.differences([work/r for r in REPEATS], lambda d: ['values.jsonl'])
    if dry_run:
        print(json.dumps({'repeats': [str(work/r) for r in REPEATS], 'problems': problems, 'target': str(target),
                          'target_exists': target.exists(), 'target_sha256': sha(target) if target.exists() else None,
                          'report': str(report), 'report_exists': report.exists()}, indent=1))
        return 0
    if problems:
        sys.exit(f'{len(problems)} problems (first: {problems[0]}); not publishing')
    values = work/'native1/values.jsonl'
    if not target.exists():
        def write(path, first):
            path.parent.mkdir(parents=True, exist_ok=True)
            with path.open('xb') as stream:
                stream.write((first/'values.jsonl').read_bytes())
            print('published', path)
        return editor_runner.publish([work/r for r in REPEATS], target, lambda d: ['values.jsonl'], write, gpu=True)
    if report.exists():
        sys.exit(f'the comparison with the existing reference is already recorded ({report}); it is not repeated')
    before = sha(target)
    rows = compare.compare(compare.load(target), compare.load(values))
    summary = {s: sum(r['status'] == s for r in rows) for s in ('pass', 'missing', 'extra')}
    identical = values.read_bytes() == target.read_bytes()
    write_once(report, {'reference': str(target), 'reference_sha256': before, 'pair_sha256': sha(values),
                        'identical': identical, 'summary': summary, 'sequence': rows})
    if sha(target) != before:
        sys.exit('the reference changed during the comparison')
    if identical:
        print(f'the pair equals the existing reference byte for byte ({before}); it stays as it is')
        return 0
    for r in rows:
        if r['status'] != 'pass':
            print(('-' if r['status'] == 'missing' else '+'), r['line'])
    print(json.dumps(summary, sort_keys=True))
    print(f'STOP: the admitted pair differs from the existing reference {target}; it is not replaced. '
          f'Full comparison: {report}')
    return EXIT_STOP


def create(work):
    """Runs ProbeRunner.Create in a batch-mode Editor on a copy and copies what it made (the scene,
    prefabs, assets and every .meta) back into this probe."""
    project = copy_project(work/'projects'/'create')
    result = subprocess.run([str(LAUNCHER), 'timeout', '-s', 'KILL', '900', str(editor_path()), '-batchmode', '-nographics',
                             '-quit', '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Create',
                             '-logFile', str(work/'create.log')])
    if result.returncode:
        sys.exit(f'create failed: {result.returncode}')
    for path in sorted((project/'Assets').rglob('*')):
        relative = path.relative_to(project)
        if path.is_file() and not (PROBE/relative).exists():
            (PROBE/relative).parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, PROBE/relative)
            print('created', relative)
    shutil.copyfile(project/'ProjectSettings/EditorBuildSettings.asset', PROBE/'ProjectSettings/EditorBuildSettings.asset')
    return 0


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound')
    out = work/'unbound'/'values.jsonl'
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        sys.exit(f'refusing to overwrite capture: {out}')
    env = dict(os.environ, CORE_APIS_OUT=str(out))
    with (work/'unbound'/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                 '--no-watch', '--frames', '200', '--size', '320x240'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'editor', 'check', 'batch', 'publish', 'unbound'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    parser.add_argument('--repeat', choices=REPEATS)
    parser.add_argument('--preflight', type=Path)
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()
    if Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    if args.mode == 'batch':
        parser.error('no batch capture: batch mode does not render the Game view, so the OnDemandRendering '
                     'window is not measured (use editor)')
    if args.mode in ('editor', 'check') and not args.repeat:
        parser.error(f'{args.mode} needs --repeat')
    if args.mode == 'editor' and not args.dry_run and not args.preflight:
        parser.error('editor needs --preflight (a passing preflight.json)')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    if args.mode == 'editor':
        return editor(work, args.repeat, args.release, args.preflight, args.dry_run)
    if args.mode == 'check':
        return check(work, args.repeat, args.release)
    if args.mode == 'publish':
        return publish(work, args.release, args.dry_run)
    return {'create': create, 'unbound': unbound}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
