#!/usr/bin/env python3
"""Records the line/trail geometry probe in the Unity Editor or runs it in Unbound.

    run.py editor  --release R --label L [--dry-run]
                    two independent windowed Editor captures (native1, native2)
    run.py check   --release R --label L   validates the two Editor captures without publishing
    run.py publish --release R --label L   checks that the two captures agree, writes Reference/unity-R
    run.py publish --release R --label L --append PREFIX
                                           the same checks, then appends the captures' PREFIX keys
                                           to an existing Reference/unity-R (see publish_append)
    run.py unbound --release R --label L   an Unbound capture (UNBOUND_CLI: the binary; the
                                           environment's UNBOUND_MANAGED_DIR selects the engine)

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/lines/<label>; nothing is written here except by
publish, which refuses to overwrite an existing reference. Each Editor runs through
tests/editor_runner.py: one launcher lease per repeat, a private Xvfb started inside the
lease, the GPU through VirtualGL, the renderer checked, hard and progress deadlines, and the
project copy's Hub/Roslyn helpers swept afterwards. Do not wrap this driver in with-heavy-job.

Publishing requires each capture to be complete: the Editor exited with 0 without a forced
stop, the probe's last record is `complete: true` (and no `fatal`), and its images are
exactly those it recorded, at their sizes. The two repeats must be byte-identical in
values.jsonl and every image.
"""
import json
import argparse
import gzip
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_root, run_root
from editor_runner import EditorJob, differences, load_result, publish as publish_repeats, \
    result_problem, run_parallel, summary

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
REPEATS = ('native1', 'native2')
# Deadlines, counted from admission into the lease (the old runner's 1800 s also covered the
# wait for it). The probe writes all its output in one burst at the end of Play mode: the
# published pair wrote it 53-55 s after the Editor started and exited 7 s later. Cold starts on
# this machine have taken 133 s for package resolution alone and 140 s for one AssetDatabase
# refresh (tools/validation/README.md), so 480 s without a first write is a stall. A stall
# inside Play mode therefore ends as no-progress before ProbeRunner's own 600 s watchdog;
# neither can publish. The hard timeout stays as the outer bound (EDITOR_RUNNER.md), above
# the progress budget plus that watchdog.
PROGRESS_TIMEOUT = 480
HARD_TIMEOUT = 1200


def copy_project(dest):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text(
        'm_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    return dest


def fresh_output(directory):
    if directory.exists():
        sys.exit(f'refusing to overwrite capture: {directory}')
    directory.mkdir(parents=True)
    return directory


def job(work, repeat):
    project, out = work/'projects'/repeat, work/repeat
    return EditorJob(project, out/'editor.log', out, env={'LINE_PROBE_OUT': str(out)},
                     timeout=HARD_TIMEOUT, progress_files=[out/'values.jsonl'],
                     progress_timeout=PROGRESS_TIMEOUT, label='lines-'+repeat)


def editor(work, dry_run=False):
    jobs = [job(work, repeat) for repeat in REPEATS]
    if dry_run:
        for j in jobs:
            print(json.dumps({'label': j.label, 'kind': j.kind, 'command': j.command(),
                              'env': j.env, 'timeout': j.timeout, 'progress_files': j.progress_files,
                              'progress_timeout': j.progress_timeout}, indent=1))
        return 0
    for j in jobs:
        copy_project(j.project)
        fresh_output(j.out)
    results = run_parallel(jobs)
    for result in results:
        print(summary(result))
    return int(any(r['exit_code'] != 0 or r.get('forced_exit') for r in results))


def images(directory):
    return sorted(p.name for p in directory.glob('*.rgba'))


def editor_problem(repeat):
    """Why the Editor run cannot back a reference: strict exit 0 on the NVIDIA GPU."""
    reason = result_problem(repeat, gpu=True)
    if not reason and (load_result(repeat) or {}).get('forced_exit'):
        reason = 'forced exits are not accepted by this probe'
    return reason


def capture_problem(repeat):
    """Why the probe output is incomplete (None when it is complete)."""
    if (repeat/'watchdog.txt').exists() or not (repeat/'values.jsonl').exists():
        return 'the capture did not finish'
    try:
        rows = [json.loads(line) for line in (repeat/'values.jsonl').read_text().splitlines() if line.strip()]
    except json.JSONDecodeError:
        return 'values.jsonl is not complete JSON lines'
    keys = [r.get('k') for r in rows]
    if not rows or rows[-1] != {'k': 'complete', 'v': True} or keys.count('complete') != 1:
        return 'the probe did not record complete: true last'
    if any(k == 'fatal' or k.endswith('.fatal') for k in keys):
        return 'the probe recorded a fatal error'
    if len(set(keys)) != len(keys):
        return 'duplicate keys'
    recorded = {r['k'].removesuffix('.pixels'): r['v'] for r in rows if r['k'].endswith('.pixels')}
    present = {name.removesuffix('.rgba'): (repeat/name).stat().st_size for name in images(repeat)}
    if set(recorded) != set(present):
        return f'images differ from those recorded: {sorted(set(recorded) ^ set(present))[:5]}'
    sizes = [k for k, v in recorded.items() if not isinstance(v, int) or present[k] != 16*v]
    if sizes:
        return f'image sizes differ from their pixel counts: {sizes[:5]}'
    return None


def check(work):
    status = 0
    for repeat in (work/r for r in REPEATS):
        reason = editor_problem(repeat) or capture_problem(repeat)
        print(repeat.name, reason or 'complete')
        status |= reason is not None
    return int(status)


def write_reference(target, first):
    target.mkdir(parents=True)
    shutil.copyfile(first/'values.jsonl', target/'values.jsonl')
    for name in images(first):
        with open(target/(name+'.gz'), 'wb') as raw, \
                gzip.GzipFile(filename='', mode='wb', fileobj=raw, mtime=0, compresslevel=9) as z:
            z.write((first/name).read_bytes())
    print('published', target, len(images(first)), 'images')


def publish(work):
    repeats = [work/r for r in REPEATS]
    for repeat in repeats:
        if not repeat.is_dir():
            sys.exit(f'{repeat.name}: no capture; not publishing')
        reason = editor_problem(repeat)
        if reason:
            sys.exit(f'{repeat.name}: {reason}; not publishing')
    return publish_repeats(repeats, reference_root(PROBE), lambda d: ['values.jsonl', *images(d)],
                           write_reference, complete=[capture_problem], gpu=True)


def publish_append(work, prefix):
    """Adds new keys to an existing reference without rewriting it. Besides every check of a
    new publication (both repeats complete, strict exit 0 on the GPU, byte-identical), the
    captures with their `prefix` keys removed must reproduce the reference line for line, byte
    for byte, and every reference image exactly; the reference must not hold a `prefix` key.
    The reference's values.jsonl then gets the captures' `prefix` lines appended, in capture
    order; its existing bytes and images are left as they are."""
    repeats = [work/r for r in REPEATS]
    for repeat in repeats:
        if not repeat.is_dir():
            sys.exit(f'{repeat.name}: no capture; not publishing')
        reason = editor_problem(repeat) or capture_problem(repeat)
        if reason:
            sys.exit(f'{repeat.name}: {reason}; not publishing')
    problems = differences(repeats, lambda d: ['values.jsonl', *images(d)])
    if problems:
        sys.exit('the repeats differ ('+'; '.join(problems)+'); not publishing')
    target = reference_root(PROBE)
    if not (target/'values.jsonl').is_file():
        sys.exit(f'no reference to append to: {target}')
    reference = (target/'values.jsonl').read_bytes()
    if not reference.endswith(b'\n'):
        sys.exit('the reference does not end with a newline; not appending')
    old = reference.splitlines(keepends=True)
    if any(json.loads(line)['k'].startswith(prefix) for line in old if line.strip()):
        sys.exit(f'the reference already holds {prefix} keys; not appending')
    captured = (repeats[0]/'values.jsonl').read_bytes().splitlines(keepends=True)
    if any(not line.endswith(b'\n') for line in captured):
        sys.exit('a captured record has no newline terminator; not appending')
    new = [line for line in captured if json.loads(line)['k'].startswith(prefix)]
    kept = [line for line in captured if not json.loads(line)['k'].startswith(prefix)]
    if not new:
        sys.exit(f'the captures hold no {prefix} keys; not appending')
    if kept != old:
        bad = next(i for i in range(max(len(kept), len(old))) if kept[i:i+1] != old[i:i+1])
        sys.exit(f'the captures do not reproduce the reference (first difference at line {bad+1}); not appending')
    present = sorted(p.name.removesuffix('.gz') for p in target.glob('*.rgba.gz'))
    if present != images(repeats[0]):
        sys.exit(f'the captures have other images than the reference: {sorted(set(present) ^ set(images(repeats[0])))[:5]}')
    changed = [n for n in present if gzip.decompress((target/(n+'.gz')).read_bytes()) != (repeats[0]/n).read_bytes()]
    if changed:
        sys.exit(f'the captures do not reproduce the reference images {changed[:5]}; not appending')
    with open(target/'values.jsonl', 'ab') as out:
        out.write(b''.join(new))
    print('appended', len(new), prefix, 'records to', target/'values.jsonl', '; reproduced', len(old), 'records and',
          len(present), 'images')
    return 0


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound')
    out = fresh_output(work/'unbound')
    env = dict(os.environ, LINE_PROBE_OUT=str(out))
    with (out/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '900', cli, 'run', '-p', str(project),
                                 '--no-watch', '--frames', '600', '--size', '96x64'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('editor', 'publish', 'unbound', 'check'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    parser.add_argument('--dry-run', action='store_true',
                        help='editor: print the Editor jobs without copying projects or launching')
    parser.add_argument('--append', metavar='PREFIX',
                        help='publish: append the captures\' PREFIX keys to the existing reference')
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    if args.dry_run and args.mode != 'editor':
        parser.error('--dry-run applies to editor only')
    if args.append is not None and (args.mode != 'publish' or not args.append.endswith('.')):
        parser.error('--append applies to publish only and takes a key prefix ending with a dot')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    if args.mode == 'editor':
        return editor(work, args.dry_run)
    if args.append:
        return publish_append(work, args.append)
    return {'publish': publish, 'unbound': unbound, 'check': check}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
