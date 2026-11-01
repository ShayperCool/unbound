#!/usr/bin/env python3
"""Records the halo and lens flare probe in the Unity Editor or runs it in Unbound.

    run.py create  --release R --label L   makes the probe's assets and scene in a fresh copy (batchmode)
    run.py editor  --release R --label L   two independent windowed Editor captures (native1, native2)
    run.py check   --release R --label L   validates the two Editor captures without publishing
    run.py publish --release R --label L   checks that the two captures agree, writes Reference/unity-R
    run.py extend  --release R --label L   like publish, for a probe that gained cases: every value and
                                           image of the existing reference must be reproduced unchanged
    run.py unbound --release R --label L   an Unbound capture (UNBOUND_CLI: the binary)

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/flares/<label>; nothing is written here except by
publish/extend. Editors run through tests/editor_runner.py (each takes its own launcher lease,
windowed on a private Xvfb, on the GPU through VirtualGL); do not wrap this driver in
with-heavy-job. Publishing requires each capture to be complete: an accepted Editor result
(exit 0, or the shutdown guard after a logged shutdown), the NVIDIA renderer, the probe's last
record `complete: true` (and no `fatal`), its images exactly those it recorded at their sizes,
and two byte-identical repeats.
"""
import argparse
import gzip
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_root, run_root
from editor_runner import EditorJob, differences, result_problem, run as run_editor, run_parallel, summary

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
# The whole windowed run (import, compile, about 1 500 frames) took about four minutes; the
# probe flushes values.jsonl after every case, and the first write follows the cold import.
EDITOR_TIMEOUT = 1800
PROGRESS_TIMEOUT = 900


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


def create(work):
    project = copy_project(work/'projects'/'create')
    out = fresh_output(work/'create')
    result = run_editor(EditorJob(project, out/'editor.log', out, graphics=False, timeout=900,
                                  args=['-quit', '-executeMethod', 'ProbeRunner.Create']))
    print(summary(result))
    print('create', project/'Assets/Scenes')
    return 0 if result.get('exit_code') == 0 and not result.get('forced_exit') else 1


def editor(work):
    jobs = []
    for repeat in ('native1', 'native2'):
        project = copy_project(work/'projects'/repeat)
        out = fresh_output(work/repeat)
        jobs.append(EditorJob(project, out/'editor.log', out, env={'FLARES_PROBE_OUT': str(out)},
                              timeout=EDITOR_TIMEOUT, progress_files=[out/'values.jsonl'],
                              progress_timeout=PROGRESS_TIMEOUT))
    results = run_parallel(jobs)
    for result in results:
        print(summary(result))
    return int(any(result_problem(job.out) for job in jobs))


def images(directory):
    return sorted(p.name for p in directory.glob('*.rgba'))


def incomplete(repeat, editor=True):
    """Why a capture cannot be published (None when it is complete)."""
    if editor:
        reason = result_problem(repeat, gpu=True)
        if reason:
            return reason
    if (repeat/'watchdog.txt').exists() or not (repeat/'values.jsonl').exists():
        return 'the capture did not finish'
    try:
        rows = [json.loads(line) for line in (repeat/'values.jsonl').read_text().splitlines() if line.strip()]
    except json.JSONDecodeError:
        return 'values.jsonl is not complete JSON lines'
    keys = [r.get('k') for r in rows]
    if not rows or rows[-1] != {'k': 'complete', 'v': True} or keys.count('complete') != 1:
        return 'the probe did not record complete: true last'
    if 'fatal' in keys:
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
    for repeat in (work/'native1', work/'native2'):
        print(repeat.name, incomplete(repeat) or 'complete')
    return 0


def agreed(work):
    """The first of two complete, byte-identical Editor captures."""
    a, b = work/'native1', work/'native2'
    for repeat in (a, b):
        reason = incomplete(repeat)
        if reason:
            sys.exit(f'{repeat.name}: {reason}; not publishing')
    problems = differences([a, b], lambda d: ['values.jsonl', *images(d)])
    if problems:
        sys.exit('the two Editor captures differ ('+'; '.join(problems)+'); not publishing')
    return a


def gz(source, target):
    with open(target, 'wb') as raw, gzip.GzipFile(filename='', mode='wb', fileobj=raw, mtime=0, compresslevel=9) as z:
        z.write(source.read_bytes())


def publish(work):
    a = agreed(work)
    target = reference_root(PROBE)
    if target.exists():
        sys.exit(f'refusing to overwrite reference: {target}')
    target.mkdir(parents=True)
    shutil.copyfile(a/'values.jsonl', target/'values.jsonl')
    for name in images(a):
        gz(a/name, target/(name+'.gz'))
    print('published', target, len(images(a)), 'images')
    return 0


def extend(work):
    """Adds the values and images of new cases to the existing reference. The captures must
    reproduce every existing value line in order (the new cases follow them, before the final
    `complete`) and every existing image byte for byte; nothing existing is rewritten."""
    a = agreed(work)
    target = reference_root(PROBE)
    old = (target/'values.jsonl').read_text().splitlines()
    new = (a/'values.jsonl').read_text().splitlines()
    if not old or old[-1] != new[-1] or new[:len(old)-1] != old[:-1]:
        sys.exit('the captures do not reproduce the existing reference values; not extending')
    existing = sorted(p.name.removesuffix('.gz') for p in target.glob('*.rgba.gz'))
    missing = [n for n in existing if n not in images(a)]
    changed = [n for n in existing if n not in missing and gzip.decompress((target/(n+'.gz')).read_bytes()) != (a/n).read_bytes()]
    if missing or changed:
        sys.exit(f'the captures do not reproduce the existing reference images (missing {missing[:5]}, '
                 f'changed {changed[:5]}); not extending')
    added = [n for n in images(a) if n not in existing]
    if len(new) == len(old):
        sys.exit('the captures add no values; nothing to extend')
    for name in added:
        gz(a/name, target/(name+'.gz'))
    shutil.copyfile(a/'values.jsonl', target/'values.jsonl')
    print('extended', target, len(new)-len(old), 'values,', len(added), 'images:', ' '.join(added))
    return 0


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound')
    out = fresh_output(work/'unbound')
    env = dict(os.environ, FLARES_PROBE_OUT=str(out))
    with (out/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '900', cli, 'run', '-p', str(project),
                                 '--no-watch', '--frames', '3000', '--size', '128x128'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('create', 'editor', 'check', 'publish', 'extend', 'unbound'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    modes = {'create': create, 'editor': editor, 'check': check, 'publish': publish,
             'extend': extend, 'unbound': unbound}
    return modes[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
