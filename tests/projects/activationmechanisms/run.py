#!/usr/bin/env python3
"""Records the activation mechanism probe in the Unity Editor or runs it in Unbound.

    run.py editor  --release R --label L --repeat native1 --preflight P   one batchmode GPU Editor capture
    run.py check   --release R --label L --repeat native1                 strict gate + actual compile graph;
                                                                          writes the capture's admission seal
    run.py editor  --release R --label L --repeat native2 --preflight P   only after native1 is admitted and
                                                                          under native1's label manifest
    run.py editor  ... --dry-run                 prints the exact EditorJob, command and inputs; starts nothing
    run.py publish --release R --label L          both repeats admitted, unchanged since admission and byte
                                                  identical: Reference/unity-R.jsonl (never overwritten)
    run.py unbound --release R --label L          an Unbound capture (UNBOUND_CLI: the binary)

Each capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/activationmechanisms/<label>; nothing is written here
except by publish. The Editor runs through tests/editor_runner.py (one installed GPU lease per
launch; do not wrap this driver in with-heavy-job) as `EditorJob(batch=True, graphics=True)`:
`vglrun -d egl Unity -batchmode -projectPath P -executeMethod ProbeRunner.Record -logFile L` on a
private Xvfb, with no -nographics and no -quit (Play mode is asynchronous; ProbeRunner quits). A
label holds at most the two repeats, one after the other; a repeat directory is write-once, so a
launch is never retried in a label, and there is no windowed or -nographics fallback.

Sealing: `check` admits a repeat once by writing admission.json with the hash of every artifact
of the launch except editor_runner's append-only cleanup journal (hub-sweeps.jsonl), plus the
journal's completed prefix. Later registered, well-typed journal rows may append; any change of a
sealed artifact or of the sealed prefix revokes the admission. Final manifests hash the complete
journal after the bounded sweeps.

`--preflight` names the preflight.json of a passing preflight.py run whose compiled sources are
byte-identical to this checkout's probe scripts; without it no Editor starts.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
from probe_paths import reference_file, run_root  # noqa: E402
import editor_runner  # noqa: E402

sys.path.insert(0, str(PROBE))
import guards  # noqa: E402

LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
REPEATS = ('native1', 'native2')
# The graphics device of the Linux GPU Editor in batchmode (OpenGL Core).
DEVICE = 'OpenGLCore'
# The external progress deadline also covers cold import and an unresponsive Editor. Only the
# runner's transitions and the completed record count as progress, never Editor log noise.
# ProbeRunner's 600 s watchdog (unchanged from the proven runner) is an additional check while
# Editor updates still run.
HARD_TIMEOUT = 1200
PROGRESS_TIMEOUT = 300
SHUTDOWN_GRACE = 30
KILL_GRACE = 10
VARIABLE = guards.OUTPUT_VARIABLE
SOURCES = ('Assets/Scripts/ActivationMechanismsProbe.cs', 'Assets/Editor/ProbeRunner.cs')
TOOLS = ('run.py', 'guards.py', 'compare.py', 'analyze.py', 'preflight.py', 'PREDICTIONS.json')
# editor_runner owns this append-only diagnostic after Editor exit. It never contributes capture
# values or progress.
CLEANUP_LOG = 'hub-sweeps.jsonl'
# Written by this runner after the launch; not part of the sealed launch artifacts.
UNSEALED = ('artifact-manifest.json', 'admission.json')


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def profile_path(release):
    return REPO/'data/profiles'/('unity-'+'.'.join(release.split('.')[:2])+'.json')


def prepared_files(project):
    files = {}
    for label, part in (('assets', 'Assets'), ('packages', 'Packages'), ('settings', 'ProjectSettings')):
        for key, digest in guards.tree(Path(project)/part).items():
            files[label+'/'+key] = digest
    return files


def copy_project(dest, release):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    version = dest/'ProjectSettings/ProjectVersion.txt'
    if not version.read_text().startswith('m_EditorVersion: '+release+'\n'):
        sys.exit(f'the fixture is not a {release} project: {version.read_text().splitlines()[0]}')
    return dest


def paths(work, repeat):
    out = work/repeat
    return work/'projects'/repeat, out, out/'capture'


def job(work, repeat, release):
    project, out, capture = paths(work, repeat)
    inputs = {'assets': str(project/'Assets'), 'packages': str(project/'Packages'),
              'settings': str(project/'ProjectSettings'),
              'supervisor': str(REPO/'tests/editor_runner.py'), 'paths': str(REPO/'tests/probe_paths.py'),
              'profile': str(profile_path(release))}
    inputs.update({name.split('.')[0] if name != 'PREDICTIONS.json' else 'predictions': str(PROBE/name) for name in TOOLS})
    return editor_runner.EditorJob(
        project.resolve() if project.exists() else project, out/'editor.log', out,
        args=['-executeMethod', 'ProbeRunner.Record'], batch=True, graphics=True,
        env={VARIABLE: str(capture.resolve() if capture.exists() else capture), 'UNBOUND_PROBE_UNITY_VERSION': release},
        timeout=HARD_TIMEOUT, shutdown_grace=SHUTDOWN_GRACE, kill_grace=KILL_GRACE,
        progress_files=(capture/'runner-events.jsonl', capture/'values.jsonl'),
        progress_timeout=PROGRESS_TIMEOUT, label='activationmechanisms-'+repeat,
        launch_inputs={'release': release, 'paths': inputs})


def preflight_problems(path, release):
    try:
        record = json.loads(Path(path).read_text())
    except (OSError, TypeError, json.JSONDecodeError) as error:
        return [f'no preflight record: {error}']
    problems = []
    if record.get('passed') is not True:
        problems.append('the preflight did not pass')
    if record.get('identity_only'):
        problems.append('an identity-only preflight compiled nothing')
    if record.get('release') != release:
        problems.append(f"the preflight compiled for {record.get('release')}")
    compiled = record.get('sources', {})
    for source in SOURCES:
        if compiled.get(source) != sha(PROBE/source):
            problems.append(f'{source} differs from the preflight-compiled bytes')
    if set(record.get('results', {})) != {'Assembly-CSharp', 'Assembly-CSharp-Editor'}:
        problems.append('the preflight did not compile both assemblies')
    return problems


def describe(j):
    return {'label': j.label, 'kind': j.kind, 'command': j.command(), 'batch': j.batch,
            'graphics': j.graphics, 'env': j.env, 'timeout': j.timeout,
            'shutdown_grace': j.shutdown_grace, 'kill_grace': j.kill_grace,
            'progress_files': [str(p) for p in j.progress_files], 'progress_timeout': j.progress_timeout,
            'late_sweeps': list(j.late_sweeps), 'project': str(j.project), 'out': str(j.out), 'cwd': str(j.cwd),
            'launch_inputs': j.launch_inputs, 'device': DEVICE}


def git_state():
    head = subprocess.run(['git', '-C', str(REPO), 'rev-parse', 'HEAD'], capture_output=True, text=True)
    status = subprocess.run(['git', '-C', str(REPO), 'status', '--porcelain', '--', str(PROBE),
                             str(REPO/'tests/editor_runner.py'), str(REPO/'tests/probe_paths.py')],
                            capture_output=True, text=True)
    lines = status.stdout.splitlines()
    return {'commit': head.stdout.strip(), 'clean': not lines, 'status': lines}


def source_manifest(release, preflight):
    """What a label's launches must share: release, sources, tools, fixture, shared runner,
    profile, the preflight and the (absent or existing) reference."""
    reference = reference_file(PROBE, 'jsonl')
    return {'release': release, 'sources': {s: sha(PROBE/s) for s in SOURCES},
            'tools': {t: sha(PROBE/t) for t in TOOLS}, 'fixture': prepared_files(PROBE),
            'shared': {'tests/editor_runner.py': sha(REPO/'tests/editor_runner.py'),
                       'tests/probe_paths.py': sha(REPO/'tests/probe_paths.py'),
                       str(profile_path(release).relative_to(REPO)): sha(profile_path(release))},
            'preflight': {'path': str(Path(preflight).resolve()), 'sha256': sha(preflight)} if preflight else None,
            'reference': {'path': str(reference), 'sha256': sha(reference) if reference.exists() else None},
            'git': git_state()}


def write_once(path, value):
    with Path(path).open('x') as stream:
        stream.write(json.dumps(value, indent=1)+'\n')


def artifact_manifest(out):
    """SHA-256 and size of every file the launch wrote under its output directory."""
    return {str(p.relative_to(out)): {'sha256': sha(p), 'bytes': p.stat().st_size}
            for p in sorted(Path(out).rglob('*')) if p.is_file() and p.name not in UNSEALED}


def sealed_artifacts(out):
    return {k: v for k, v in artifact_manifest(out).items() if k != CLEANUP_LOG}


def cleanup_prefix(out):
    path = Path(out)/CLEANUP_LOG
    raw = path.read_bytes() if path.is_file() else b''
    complete = raw[:raw.rfind(b'\n')+1]
    return {'present': path.is_file(), 'bytes': len(complete), 'sha256': hashlib.sha256(complete).hexdigest()}


def cleanup_problems(out, prefix=None):
    """Only registered, well-typed cleanup rows may append; an admitted prefix cannot change. One
    unfinished trailing write is left to the owned writer and the final receipt, never read as a
    capture value."""
    out = Path(out)
    path = out/CLEANUP_LOG
    try:
        result = json.loads((out/'editor-run.json').read_text())
        schedule = result.get('late_sweeps')
        raw = path.read_bytes() if path.is_file() else b''
        if schedule is None:
            if path.exists():
                return ['an unregistered cleanup journal appeared']
            delays = []
        else:
            if Path(schedule['log']).resolve() != path.resolve():
                return ['the cleanup journal belongs to another output directory']
            delays = schedule['delays']
            if not isinstance(delays, list) or any(type(d) not in (int, float) or not math.isfinite(d) or d < 0 for d in delays):
                return ['invalid cleanup schedule']
        if prefix is not None:
            n = prefix.get('bytes')
            if type(n) is not int or n < 0 or n > len(raw) or hashlib.sha256(raw[:n]).hexdigest() != prefix.get('sha256') \
                    or prefix.get('present') and not path.is_file():
                return ['the admitted cleanup journal prefix changed or was removed']
        complete = raw[:raw.rfind(b'\n')+1]
        previous = -1
        for line in complete.splitlines():
            row = json.loads(line)
            if not isinstance(row, dict) or set(row) != {'after_s', 'time', 'swept'} \
                    or type(row['after_s']) not in (int, float) or row['after_s'] not in delays \
                    or type(row['time']) not in (int, float) or not math.isfinite(row['time']) \
                    or not isinstance(row['swept'], list):
                return ['invalid cleanup journal row']
            at = delays.index(row['after_s'])
            if at <= previous:
                return ['cleanup journal schedule was reordered or repeated']
            previous = at
        return []
    except (OSError, KeyError, TypeError, ValueError) as error:
        return [f'no valid cleanup journal: {error}']


def editor(work, repeat, release, preflight, dry_run=False):
    j = job(work, repeat, release)
    project, out, capture = paths(work, repeat)
    if dry_run:
        print(json.dumps({'plan': describe(j), 'fresh': not out.exists() and not project.exists(),
                          'label_exists': work.exists(),
                          'label_manifest_exists': (work/'label-manifest.json').exists(),
                          'native1_admitted': (work/'native1/admission.json').exists(),
                          'requires': ('a passing preflight' if repeat == 'native1' else
                                       'native1 admitted by check, unchanged since, and the same label manifest'),
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
        recorded = json.loads(label.read_text())
        if {k: v for k, v in recorded.items() if k != 'git'} != {k: v for k, v in manifest.items() if k != 'git'} \
                or recorded['git']['commit'] != manifest['git']['commit']:
            sys.exit('the checkout, fixture, runner, reference or preflight differ from native1\'s; the repeat does not start')
    if out.exists() or project.exists():
        sys.exit(f'refusing to reuse {out}: a launch is never repeated in a label')
    if repeat == 'native1':
        work.mkdir(parents=True, exist_ok=True)
        write_once(label, manifest)
    copy_project(project, release)
    capture.mkdir(parents=True)
    j = job(work, repeat, release)
    write_once(out/'inputs.json', {
        'release': release, 'repeat': repeat, 'device': DEVICE, 'files': prepared_files(project),
        'label_manifest_sha256': sha(label), 'preflight': str(Path(preflight).resolve()),
        'preflight_sha256': sha(preflight), 'plan': describe(j)})
    result = editor_runner.run(j)
    (out/'authored-after.json').write_text(json.dumps(prepared_files(project), indent=1)+'\n')
    (out/'authored-delta.json').write_text(json.dumps(guards.authored_delta(out, project), indent=1)+'\n')
    # Raw diagnostics (instance ids, absolute frames, real times, device names, the runner's Editor
    # times) stay out of the compared record; they are kept and hashed with every other output.
    (out/'artifact-manifest.json').write_text(json.dumps(artifact_manifest(out), indent=1)+'\n')
    print(editor_runner.summary(result))
    problems = check_problems(work, repeat, release)
    for p in problems:
        print(' ', p)
    print(f'{repeat}:', 'passes the guards (admit it with check)' if not problems else f'{len(problems)} problems')
    return int(bool(problems))


def check_problems(work, repeat, release):
    """The strict capture gate, the launch identity (label manifest, checkout) and the actual
    compile graph against the recorded preflight, plus the cleanup journal."""
    project, out, _ = paths(work, repeat)
    if not out.exists():
        return [f'{repeat} was not captured']
    problems = guards.capture_problems(out, project, release, DEVICE)
    try:
        label = work/'label-manifest.json'
        manifest = json.loads(label.read_text())
        prepared = json.loads((out/'inputs.json').read_text())
        if prepared.get('label_manifest_sha256') != sha(label):
            problems.append('the capture was not launched under this label\'s manifest')
        if prepared.get('release') != release or manifest.get('release') != release:
            problems.append('the capture was launched for another release')
        if manifest['tools'] != {t: sha(PROBE/t) for t in TOOLS} or manifest['sources'] != {s: sha(PROBE/s) for s in SOURCES}:
            problems.append('this checkout\'s probe scripts or tools differ from the launched ones')
        if prepared['files'] != manifest['fixture']:
            problems.append('the project copy was not prepared with the fixture\'s bytes')
        if (manifest['reference']['sha256'] or None) != (sha(manifest['reference']['path'])
                                                        if Path(manifest['reference']['path']).exists() else None):
            problems.append('the reference changed after the label was made')
        if sha(prepared['preflight']) != prepared['preflight_sha256'] or \
                manifest['preflight']['sha256'] != prepared['preflight_sha256']:
            problems.append('the preflight record changed after the launch')
        problems += [f'graph: {p}' for p in guards.graph_problems(project, json.loads(Path(prepared['preflight']).read_text()))]
    except (OSError, KeyError, TypeError, json.JSONDecodeError) as error:
        problems.append(f'no launch record for the identity checks: {error}')
    return problems+cleanup_problems(out)


def admission_problems(work, repeat, release):
    out = work/repeat
    try:
        admission = json.loads((out/'admission.json').read_text())
    except (OSError, json.JSONDecodeError) as error:
        return [f'{repeat} has no admission: {error}']
    problems = []
    if admission.get('artifacts') != sealed_artifacts(out):
        problems.append(f'{repeat} changed after its admission')
    prefix = admission.get('cleanup_prefix')
    if not isinstance(prefix, dict):
        problems.append(f'{repeat} has no cleanup prefix seal')
    else:
        problems += cleanup_problems(out, prefix)
    return problems+check_problems(work, repeat, release)


def check(work, repeat, release):
    problems = check_problems(work, repeat, release)
    for p in problems:
        print(p)
    if problems:
        print(f'{repeat}: {len(problems)} problems; not admitted')
        return 1
    out = work/repeat
    if (out/'admission.json').exists():
        changed = admission_problems(work, repeat, release)
        if changed:
            sys.exit(f'{repeat}: an earlier admission names other bytes: {changed}')
        print(f'{repeat}: passes; already admitted')
        return 0
    write_once(out/'admission.json', {'repeat': repeat, 'release': release,
                                      'values_sha256': sha(out/'capture/values.jsonl'),
                                      'artifacts': sealed_artifacts(out), 'cleanup_prefix': cleanup_prefix(out)})
    print(f'{repeat}: passes; admitted')
    return 0


def publish(work, release):
    """Two admitted, unchanged, byte-identical repeats. A new release's reference is written once;
    an existing reference is compared, never rewritten."""
    for repeat in REPEATS:
        problems = admission_problems(work, repeat, release)
        if problems:
            sys.exit(f'{repeat}: {len(problems)} problems (first: {problems[0]}); not publishing')
    first, second = (work/r/'capture/values.jsonl' for r in REPEATS)
    if first.read_bytes() != second.read_bytes():
        sys.exit('the two admitted repeats differ; not publishing')
    target = reference_file(PROBE, 'jsonl')
    if target.exists():
        same = target.read_bytes() == first.read_bytes()
        print('the reference exists and', 'equals the pair' if same else 'DIFFERS from the pair; it is not rewritten')
        return 0 if same else 1
    target.parent.mkdir(parents=True, exist_ok=True)
    with target.open('xb') as stream:
        stream.write(first.read_bytes())
    print('published', target)
    return 0


def unbound(work, release):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound', release)
    out = work/'unbound'
    if out.exists():
        sys.exit(f'refusing to overwrite capture: {out}')
    out.mkdir(parents=True)
    env = dict(os.environ, **{VARIABLE: str(out)})
    with (out/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '900', cli, 'run', '-p', str(project),
                                 '--no-watch', '--frames', '30', '--size', '64x32'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('editor', 'check', 'publish', 'unbound'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    parser.add_argument('--repeat', choices=REPEATS)
    parser.add_argument('--preflight', type=Path)
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    if args.mode in ('editor', 'check') and not args.repeat:
        parser.error(f'{args.mode} needs --repeat')
    if args.mode == 'editor' and not args.dry_run and not args.preflight:
        parser.error('editor needs --preflight (a passing preflight.json)')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    if not profile_path(args.release).exists():
        sys.exit(f'no compatibility profile for {args.release}: {profile_path(args.release)}')
    work = run_root(PROBE, args.label)
    if args.mode == 'editor':
        return editor(work, args.repeat, args.release, args.preflight, args.dry_run)
    if args.mode == 'check':
        return check(work, args.repeat, args.release)
    if args.mode == 'publish':
        return publish(work, args.release)
    return unbound(work, args.release)


if __name__ == '__main__':
    sys.exit(main())
