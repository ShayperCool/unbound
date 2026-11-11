#!/usr/bin/env python3
"""Records the shader pass count probe in the Unity Editor or runs it in Unbound.

    run.py editor  --release R --label L --repeat native1 --preflight P   one windowed GPU Editor capture
    run.py editor  --release R --label L --repeat native2 --preflight P   the repeat; only after native1
                                                                          passed `check` (strict gate and
                                                                          actual-rsp graph check)
    run.py editor  ... --dry-run                 prints the exact EditorJob, command and inputs; starts nothing
    run.py check   --release R --label L --repeat X   every strict guard problem of one capture
    run.py publish --release R --label L          both repeats pass and agree byte for byte: copies one
                                                  to Reference/unity-R.jsonl (never overwrites)
    run.py unbound --release R --label L          an Unbound capture (UNBOUND_CLI: the binary)

Each capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/shaderpasscount/<label>; nothing is written here
except by publish. The Editor runs through tests/editor_runner.py (one installed GPU lease per
launch; do not wrap this driver in with-heavy-job) as `EditorJob(batch=False, graphics=True, private_display=False)`:
`vglrun -d egl Unity -force-glcore -projectPath P -executeMethod ProbeRunner.Record -logFile L` on a
existing GPU display, with no -nographics and no -quit (Play mode is asynchronous; ProbeRunner quits). The
repeats run one after the other; a repeat directory is write-once, so a launch is never retried in
a label, and there is no batchmode or -nographics fallback.

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
from editor_runner import vulkan_display_env, EditorJob, jsonl_complete, publish as publish_reference, run, summary  # noqa: E402

sys.path.insert(0, str(PROBE))
import guards  # noqa: E402

LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
REPEATS = ('native1', 'native2')
# The graphics device of the windowed Linux GPU Editor (OpenGL Core).
DEVICE = 'OpenGLCore'
# The external progress deadline also covers cold import and an unresponsive Editor. Only the
# runner's transitions and the completed record count as progress, never Editor log noise.
# ProbeRunner's 600 s watchdog is an additional check while Editor updates still run.
HARD_TIMEOUT = 1200
PROGRESS_TIMEOUT = 300
SHUTDOWN_GRACE = 30
KILL_GRACE = 10
VARIABLE = guards.OUTPUT_VARIABLE
SOURCES = ('Assets/Scripts/ShaderPassCountProbe.cs', 'Assets/Editor/ProbeRunner.cs')
TOOLS = ('run.py', 'guards.py', 'compare.py', 'analyze.py', 'preflight.py', 'PREDICTIONS.json')


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
    return EditorJob(project.resolve() if project.exists() else project, out/'editor.log', out,
                     args=['-force-glcore', '-executeMethod', 'ProbeRunner.Record'], batch=False, graphics=True, private_display=False,
                     env={**vulkan_display_env(), VARIABLE: str(capture.resolve() if capture.exists() else capture),
                          'UNBOUND_PROBE_UNITY_VERSION': release},
                     timeout=HARD_TIMEOUT, shutdown_grace=SHUTDOWN_GRACE, kill_grace=KILL_GRACE,
                     progress_files=(capture/'runner-events.jsonl', capture/'values.jsonl'),
                     progress_timeout=PROGRESS_TIMEOUT, label='shaderpasscount-'+repeat,
                     launch_inputs={'release': release, 'paths': inputs})


def preflight_problems(path):
    try:
        record = json.loads(Path(path).read_text())
    except (OSError, json.JSONDecodeError) as error:
        return [f'no preflight record: {error}']
    problems = []
    if record.get('passed') is not True:
        problems.append('the preflight did not pass')
    if record.get('release') != os.environ.get('UNBOUND_PROBE_UNITY_VERSION'):
        problems.append(f"the preflight compiled for {record.get('release')}")
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


def git_head():
    result = subprocess.run(['git', '-C', str(REPO), 'rev-parse', 'HEAD'], capture_output=True, text=True)
    status = subprocess.run(['git', '-C', str(REPO), 'status', '--porcelain', '--', str(PROBE)],
                            capture_output=True, text=True)
    return {'commit': result.stdout.strip(), 'probe_clean': status.stdout == ''}


def editor(work, repeat, release, preflight, dry_run=False):
    j = job(work, repeat, release)
    plan = describe(j)
    if dry_run:
        project, out, _ = paths(work, repeat)
        print(json.dumps({'plan': plan, 'fresh': not out.exists() and not project.exists(),
                          'fixture': {k: v for k, v in prepared_files(PROBE).items()},
                          'tools': {name: sha(PROBE/name) for name in TOOLS},
                          'sources': {s: sha(PROBE/s) for s in SOURCES}, 'git': git_head()}, indent=1))
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
        'sources': {s: sha(PROBE/s) for s in SOURCES}, 'tools': {name: sha(PROBE/name) for name in TOOLS},
        'preflight': str(Path(preflight).resolve()), 'preflight_sha256': sha(preflight),
        'git_head': git_head(), 'plan': plan}, indent=1)+'\n')
    result = run(j)
    (out/'authored-after.json').write_text(json.dumps(prepared_files(project), indent=1)+'\n')
    (out/'authored-delta.json').write_text(json.dumps(guards.authored_delta(out, project), indent=1)+'\n')
    # Raw diagnostics (real times, frames, device names, the runner's Editor times) stay out of the
    # compared record; they are kept and hashed with every other output of the launch.
    (out/'shader-log.json').write_text(json.dumps(guards.listed_log_lines(out/'editor.log'), indent=1)+'\n')
    (out/'artifact-manifest.json').write_text(json.dumps(artifact_manifest(out), indent=1)+'\n')
    print(summary(result))
    problems = check_problems(work, repeat, release)
    for p in problems:
        print(' ', p)
    print(f'{repeat}:', 'passes' if not problems else f'{len(problems)} problems')
    return int(bool(problems))


def artifact_manifest(out):
    """SHA-256 and size of every file the launch wrote under its output directory."""
    return {str(p.relative_to(out)): {'sha256': sha(p), 'bytes': p.stat().st_size}
            for p in sorted(Path(out).rglob('*')) if p.is_file() and p.name != 'artifact-manifest.json'}


def check_problems(work, repeat, release):
    """The strict capture gate plus the actual compile graph against the recorded preflight."""
    project, out, _ = paths(work, repeat)
    if not out.exists():
        return [f'{repeat} was not captured']
    problems = guards.capture_problems(out, project, release, DEVICE)
    try:
        prepared = json.loads((out/'inputs.json').read_text())
        preflight = json.loads(Path(prepared['preflight']).read_text())
        if sha(prepared['preflight']) != prepared['preflight_sha256']:
            problems.append('the preflight record changed after the launch')
        problems += [f'graph: {p}' for p in guards.graph_problems(project, preflight)]
        tools = {name: sha(PROBE/name) for name in TOOLS}
        if prepared.get('tools') != tools or prepared.get('sources') != {s: sha(PROBE/s) for s in SOURCES}:
            problems.append('the checkout\'s probe sources or tools differ from the launched ones')
    except (OSError, KeyError, json.JSONDecodeError) as error:
        problems.append(f'no launch/preflight record for the graph check: {error}')
    return problems


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
