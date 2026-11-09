#!/usr/bin/env python3
"""Records the texture format probe in the Unity Editor or runs it in Unbound.

    run.py editor  --release R --label L   two independent batch-mode GPU Editor captures
    run.py publish --release R --label L   checks that the captures agree and writes
                                           Reference/unity-R.jsonl
    run.py unbound --release R --label L   an Unbound capture (UNBOUND_CLI: the binary; the
                                           environment's UNBOUND_MANAGED_DIR selects the engine)
    run.py verify  --release R --label L [--cases all]
                                           an Unbound capture of the cases Unbound supports
                                           (default; --cases all: every case) compared with the
                                           reference, the recorded residuals.json allowed

Every capture runs on a fresh project under $XDG_CACHE_HOME/unbound/validation/runs/
textureformats/<label>: the probe's Assets with the hello project's Packages, ProjectSettings
and camera scene. Nothing is written here except by publish, which refuses to overwrite an
existing reference. Each Editor runs through tests/editor_runner.py (one launcher lease per
repeat, a private Xvfb, the GPU through VirtualGL, the renderer checked); do not wrap this
driver in with-heavy-job.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_file, run_root
from editor_runner import EditorJob, jsonl_complete, publish as publish_repeats, run_parallel, summary

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
HELLO = PROBE.parent/'hello'
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
REPEATS = ('native1', 'native2')
OUTPUT = 'values.jsonl'
# The cases Unbound matches (TEXTURE_FORMATS_PROBE_ONLY prefixes): every one.
SUPPORTED = 'rt,tex,feedback'


def copy_project(dest, release):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Packages', 'ProjectSettings'):
        shutil.copytree(HELLO/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+release+'\n')
    shutil.copytree(PROBE/'Assets', dest/'Assets')
    # The hello scene's camera, without its scripted GameObject.
    (dest/'Assets/Scenes').mkdir()
    scene = (HELLO/'Assets/Scenes/Main.unity').read_text()
    (dest/'Assets/Scenes/Main.unity').write_text(scene.split('--- !u!1 &200\n')[0])
    shutil.copyfile(HELLO/'Assets/Scenes/Main.unity.meta', dest/'Assets/Scenes/Main.unity.meta')
    return dest


def fresh_output(directory):
    if directory.exists():
        sys.exit(f'refusing to overwrite capture: {directory}')
    directory.mkdir(parents=True)
    return directory


def capture_problem(directory):
    reason = jsonl_complete(OUTPUT)(directory)
    if reason:
        return reason
    keys = [json.loads(line)['k'] for line in (Path(directory)/OUTPUT).read_text().splitlines() if line.strip()]
    if 'fatal' in keys:
        return 'the probe recorded a fatal error'
    if len(set(keys)) != len(keys):
        return 'duplicate keys'
    return None


def editor(work, release):
    jobs = []
    for repeat in REPEATS:
        project, out = work/'projects'/repeat, work/repeat
        copy_project(project, release)
        fresh_output(out)
        jobs.append(EditorJob(project, out/'editor.log', out, args=['-executeMethod', 'TexFormatsRunner.Record'],
                              batch=True, graphics=True, env={'TEXTURE_FORMATS_PROBE_OUT': str(out/OUTPUT)},
                              timeout=900, shutdown_grace=120, progress_files=[out/OUTPUT], progress_timeout=600,
                              label='textureformats-'+repeat))
    results = run_parallel(jobs)
    for result in results:
        print(summary(result))
    for repeat in REPEATS:
        print(repeat, capture_problem(work/repeat) or 'complete')
    return int(any(r['exit_code'] != 0 or r.get('forced_exit') for r in results))


def publish(work, append=False):
    def write(target, first):
        target.parent.mkdir(parents=True, exist_ok=True)
        if append:
            old = target.read_bytes().splitlines(keepends=True)
            captured = (first/OUTPUT).read_bytes().splitlines(keepends=True)
            previous = {json.loads(row)['k']: row for row in old}
            current = {json.loads(row)['k']: row for row in captured}
            if not all(current.get(key) == row for key, row in previous.items()):
                raise SystemExit('an existing reference record changed or is missing')
            if json.loads(old[-1]) != {'k': 'complete', 'v': True}:
                raise SystemExit('existing reference is incomplete')
            extra = [row for row in captured if json.loads(row)['k'] not in previous]
            if not extra:
                raise SystemExit('no new reference records')
            target.write_bytes(b''.join(old[:-1] + extra + old[-1:]))
        else:
            shutil.copyfile(first/OUTPUT, target)
        print('published', target)
    target = reference_file(PROBE, 'jsonl')
    if append:
        # The repeat validator refuses overwriting a reference. Validate into a private candidate,
        # then append only new keys while keeping every earlier record byte for byte.
        candidate = work/'append-candidate.jsonl'
        code = publish_repeats([work/r for r in REPEATS], candidate, lambda d: [OUTPUT],
                              lambda path, first: shutil.copyfile(first/OUTPUT, path),
                              complete=[capture_problem], gpu=True)
        if code: return code
        write(target, work/REPEATS[0])
        return 0
    return publish_repeats([work/r for r in REPEATS], target, lambda d: [OUTPUT], write,
                           complete=[capture_problem], gpu=True)


def unbound(work, release, only=None):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound', release)
    out = fresh_output(work/'unbound')
    env = dict(os.environ, TEXTURE_FORMATS_PROBE_OUT=str(out/OUTPUT))
    if only is not None:
        env['TEXTURE_FORMATS_PROBE_ONLY'] = only
    with (out/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), '--kind', 'engine-gpu', 'timeout', '600', cli, 'run', '-p', str(project),
                                 '--no-watch', '--frames', '30', '--size', '64x64'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    if result.returncode == 0:
        reason = capture_problem(out)
        if reason:
            print('incomplete:', reason)
            return 1
    return result.returncode


def verify(work, release, cases):
    only = None if cases == 'all' else SUPPORTED
    code = unbound(work, release, only)
    if code:
        return code
    command = [sys.executable, str(PROBE/'compare.py'), str(reference_file(PROBE, 'jsonl')),
               str(work/'unbound'/OUTPUT), '--report', str(work/'report.json'),
               '--residuals', str(PROBE/'residuals.json')]
    return subprocess.run(command + (['--only', only] if only else [])).returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('editor', 'publish', 'unbound', 'verify'))
    parser.add_argument('--cases', choices=('supported', 'all'), default='supported', help='verify: which cases')
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    parser.add_argument('--append', action='store_true', help='publish: append new keys, preserving every existing record')
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    if args.mode == 'editor':
        return editor(work, args.release)
    if args.mode == 'unbound':
        return unbound(work, args.release, os.environ.get('TEXTURE_FORMATS_PROBE_ONLY'))
    if args.mode == 'verify':
        return verify(work, args.release, args.cases)
    return publish(work, args.append)


if __name__ == '__main__':
    sys.exit(main())
