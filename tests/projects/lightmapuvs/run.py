#!/usr/bin/env python3
"""Records the lightmap UV probe in the Unity Editor or runs it in Unbound.

    run.py editor --release R --label L     two independent Editor captures
    run.py publish --release R --label L    checks they agree, writes Reference/unity-R.jsonl
    run.py unbound --release R --label L    Unbound capture (UNBOUND_CLI: the binary)

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings in the
validation cache (nothing is written here). Compare with compare.py.
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(PROBE.parents[1]))
from probe_paths import reference_file, run_root  # noqa: E402
from editor_runner import EditorJob, publish as publish_repeats, run_parallel  # noqa: E402

LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'lightmapuvs.jsonl'


def copy_project(dest):
    if dest.exists():
        shutil.rmtree(dest)
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text(
        'm_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    return dest


def editor(work):
    jobs = []
    for repeat in ('native1', 'native2'):
        project = copy_project(work/'projects'/repeat)
        out = work/repeat
        out.mkdir(parents=True, exist_ok=True)
        jobs.append(EditorJob(project, out/'editor.log', out, args=['-executeMethod', 'ProbeRunner.Record'],
                              graphics=False, env={'LIGHTMAP_UV_PROBE_OUT': str(out/OUT)}, timeout=900,
                              progress_files=[out/OUT], progress_timeout=600))
    results = run_parallel(jobs)
    codes = [r['exit_code'] for r in results]
    print('editor exits', codes)
    return int(any(code != 0 for code in codes) or any(not (work/r/OUT).exists() for r in ('native1', 'native2')))


def publish(work):
    def complete(directory):
        return None if (directory/OUT).exists() else f'no {OUT}'

    def write(target, first):
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(first/OUT, target)
        print('published', target)
    return publish_repeats([work/'native1', work/'native2'], reference_file(PROBE, 'jsonl'), lambda _: [OUT], write,
                           complete=[complete])


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound')
    out = work/'unbound'
    out.mkdir(parents=True, exist_ok=True)
    (out/OUT).unlink(missing_ok=True)
    env = dict(os.environ, LIGHTMAP_UV_PROBE_OUT=str(out/OUT))
    with (out/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                 '--no-watch', '--frames', '10', '--size', '64x64'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out/OUT)
    return result.returncode or int(not (out/OUT).exists())


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('editor', 'publish', 'unbound'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    args = parser.parse_args()
    if Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    work.mkdir(parents=True, exist_ok=True)
    return {'editor': editor, 'publish': publish, 'unbound': unbound}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
