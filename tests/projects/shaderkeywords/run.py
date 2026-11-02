#!/usr/bin/env python3
"""Records the shader keyword probe in the Unity Editor or runs it in Unbound.

    run.py editor  --release R --label L --repeat native1 --preflight P   one batchmode GPU Editor capture
    run.py editor  --release R --label L --repeat native2 --preflight P   the repeat; only after native1
                                                                          passed `check`
    run.py editor  ... --dry-run                 prints the exact EditorJob and command, starts nothing
    run.py check   --release R --label L --repeat X   every strict guard problem of one capture
    run.py publish --release R --label L          both repeats pass and agree byte for byte: copies one
                                                  to Reference/unity-R.jsonl (never overwrites)
    run.py unbound --release R --label L          an Unbound capture (UNBOUND_CLI: the binary)

Each capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/shaderkeywords/<label>; nothing is written here except by
publish. The Editor runs through tests/editor_runner.py (one installed GPU lease per launch; do
not wrap this driver in with-heavy-job) as `EditorJob(batch=True, graphics=True)`: `vglrun -d egl
Unity -batchmode -projectPath P -executeMethod ProbeRunner.Record -logFile L` on a private Xvfb,
with no -nographics and no -quit (Play mode is asynchronous; ProbeRunner quits). The repeats run
one after the other; a repeat directory is write-once, so a launch is never retried in a label.

`--preflight` names the preflight.json of a passing preflight.py run whose compiled sources are
byte-identical to this checkout's probe scripts; without it no Editor starts.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
from probe_paths import reference_file, run_root  # noqa: E402
from editor_runner import EditorJob, jsonl_complete, publish as publish_reference, run, summary  # noqa: E402

sys.path.insert(0, str(PROBE))
import guards  # noqa: E402

LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
REPEATS = ('native1', 'native2')
# The graphics device of the Linux GPU Editor (OpenGL Core). Platform-conditional declarations
# (the SHADER_API_D3D11 block) are measured for this device only.
DEVICE = 'OpenGLCore'
# Hard deadline of the whole admitted run (cold import included). ProbeRunner's own watchdog
# quits after 600 s from Record. The record is written once at the end, so output-progress
# monitoring stays off.
HARD_TIMEOUT = 1200
SHUTDOWN_GRACE = 30
KILL_GRACE = 10
VARIABLE = 'UNBOUND_PROBE_SHADERKEYWORDS_OUT'
SOURCES = ('Assets/Scripts/ShaderKeywordsProbe.cs', 'Assets/Editor/ProbeRunner.cs')


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
    return EditorJob(project.resolve() if project.exists() else project, out/'editor.log', out,
                     args=['-executeMethod', 'ProbeRunner.Record'], batch=True, graphics=True,
                     env={VARIABLE: str(capture.resolve() if capture.exists() else capture),
                          'UNBOUND_PROBE_UNITY_VERSION': release},
                     timeout=HARD_TIMEOUT, shutdown_grace=SHUTDOWN_GRACE, kill_grace=KILL_GRACE,
                     label='shaderkeywords-'+repeat,
                     launch_inputs={'release': release,
                                    'paths': {'assets': str(project/'Assets'),
                                              'packages': str(project/'Packages'),
                                              'settings': str(project/'ProjectSettings'),
                                              'runner': str(PROBE/'run.py'),
                                              'guards': str(PROBE/'guards.py'),
                                              'comparator': str(PROBE/'compare.py'),
                                              'supervisor': str(REPO/'tests/editor_runner.py'),
                                              'paths': str(REPO/'tests/probe_paths.py'),
                                              'profile': str(profile_path(release))}})


def preflight_problems(path):
    try:
        record = json.loads(Path(path).read_text())
    except (OSError, json.JSONDecodeError) as error:
        return [f'no preflight record: {error}']
    problems = []
    if record.get('passed') is not True:
        problems.append('the preflight did not pass')
    compiled = record.get('sources', {})
    for source in SOURCES:
        if compiled.get(source) != sha(PROBE/source):
            problems.append(f'{source} differs from the preflight-compiled bytes')
    return problems


def describe(j):
    return {'label': j.label, 'kind': j.kind, 'command': j.command(), 'batch': j.batch,
            'graphics': j.graphics, 'env': j.env, 'timeout': j.timeout,
            'shutdown_grace': j.shutdown_grace, 'kill_grace': j.kill_grace,
            'progress_files': j.progress_files, 'progress_timeout': j.progress_timeout,
            'project': str(j.project), 'out': str(j.out), 'cwd': str(j.cwd),
            'launch_inputs': j.launch_inputs, 'device': DEVICE}


def editor(work, repeat, release, preflight, dry_run=False):
    j = job(work, repeat, release)
    plan = describe(j)
    if dry_run:
        print(json.dumps(plan, indent=1))
        return 0
    problems = preflight_problems(preflight)
    if problems:
        sys.exit('no launch: '+'; '.join(problems))
    if repeat == 'native2':
        first = check_problems(work, 'native1', release)
        if first:
            sys.exit(f'native1 did not pass ({len(first)} problems, first: {first[0]}); the repeat is not admitted')
    project, out, capture = paths(work, repeat)
    if out.exists() or project.exists():
        sys.exit(f'refusing to reuse {out}: a launch is never repeated in a label')
    copy_project(project, release)
    capture.mkdir(parents=True)
    j = job(work, repeat, release)
    plan = describe(j)
    (out/'inputs.json').write_text(json.dumps({
        'release': release, 'repeat': repeat, 'device': DEVICE, 'files': prepared_files(project),
        'sources': {s: sha(PROBE/s) for s in SOURCES}, 'preflight': str(Path(preflight).resolve()),
        'preflight_sha256': sha(preflight), 'git_head': git_head(), 'plan': plan}, indent=1)+'\n')
    result = run(j)
    (out/'authored-after.json').write_text(json.dumps(prepared_files(project), indent=1)+'\n')
    print(summary(result))
    problems = check_problems(work, repeat, release)
    for p in problems:
        print(' ', p)
    print(f'{repeat}:', 'passes' if not problems else f'{len(problems)} problems')
    return int(bool(problems))


def git_head():
    result = subprocess.run(['git', '-C', str(REPO), 'rev-parse', 'HEAD'], capture_output=True, text=True)
    status = subprocess.run(['git', '-C', str(REPO), 'status', '--porcelain', '--', str(PROBE)],
                            capture_output=True, text=True)
    return {'commit': result.stdout.strip(), 'probe_clean': status.stdout == ''}


def check_problems(work, repeat, release):
    project, out, _ = paths(work, repeat)
    if not out.exists():
        return [f'{repeat} was not captured']
    return guards.capture_problems(out, project, release, DEVICE)


def check(work, repeat, release):
    problems = check_problems(work, repeat, release)
    for p in problems:
        print(p)
    print(f'{repeat}:', 'passes' if not problems else f'{len(problems)} problems')
    return int(bool(problems))


def publish(work, release):
    for repeat in REPEATS:
        problems = check_problems(work, repeat, release)
        if problems:
            sys.exit(f'{repeat}: {len(problems)} problems (first: {problems[0]}); not publishing')

    def write(target, first):
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open('xb') as stream:
            stream.write((first/'capture/values.jsonl').read_bytes())
        print('published', target)
    return publish_reference([work/r for r in REPEATS], reference_file(PROBE, 'jsonl'),
                             lambda directory: ['capture/values.jsonl'], write,
                             complete=[jsonl_complete('capture/values.jsonl', guards.MARKER)], gpu=True)


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
