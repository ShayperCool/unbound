#!/usr/bin/env python3
"""Makes, records or runs the NavMesh path cost, raycast and agent placement probe (see README.md).

    run.py setup --release R --label L     makes the scene and NavMesh in the Editor; copies the
                                           new Assets files (scene, NavMesh, .meta) back here
    run.py editor --release R --label L    two independent Editor captures (native1, native2)
    run.py publish --release R --label L   checks that the two captures agree, copies one to Reference/
    run.py unbound --release R --label L   an Unbound capture (UNBOUND_CLI: the binary)
    run.py check --release R [--label L]   an Unbound capture compared with Reference/ (the gate)

Every run works on a fresh copy of the probe's Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/navcost/<label>; labels are never reused. Editors run
through the shared heavy-job launcher, on the GPU (a private Xvfb and VirtualGL), and exit by
themselves (a watchdog in ProbeRunner; the launcher's timeout kills them at 900 s).
"""
import argparse
import filecmp
import os
import re
from pathlib import Path
import shutil
import subprocess
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, reference_file, run_root

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


def run_editor(project, log, args, env):
    return subprocess.Popen(
        [str(LAUNCHER), 'timeout', '-s', 'KILL', '900', 'vglrun', '-d', 'egl', str(editor_path()),
         '-batchmode', '-projectPath', str(project), *args, '-logFile', str(log)], env=env)


def setup(work):
    project = copy_project(work/'project')
    before = {p.relative_to(project) for p in (project/'Assets').rglob('*')}
    name, server = display()
    try:
        code = run_editor(project, work/'setup.log', ['-quit', '-executeMethod', 'ProbeSetup.Create'],
                          dict(os.environ, DISPLAY=name)).wait()
    finally:
        server.terminate()
    if code:
        sys.exit(f'setup failed ({code}); see {work/"setup.log"}')
    for path in sorted((project/'Assets').rglob('*')):
        rel = path.relative_to(project)
        if path.is_file() and (rel not in before or rel.parts[-1].endswith('.unity') or rel.name.endswith('.asset')):
            (PROBE/rel).parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, PROBE/rel)
            print('copied', rel)
    shutil.copyfile(project/'ProjectSettings/EditorBuildSettings.asset', PROBE/'ProjectSettings/EditorBuildSettings.asset')
    return 0


def editor(work):
    name, server = display()
    try:
        jobs = []
        for repeat in ('native1', 'native2'):
            project = copy_project(work/'projects'/repeat)
            out = work/repeat/'values.json'
            out.parent.mkdir(parents=True, exist_ok=True)
            if out.exists():
                sys.exit(f'refusing to overwrite capture: {out}')
            env = dict(os.environ, DISPLAY=name, NAVCOST_OUT=str(out))
            jobs.append(run_editor(project, work/repeat/'editor.log', ['-executeMethod', 'ProbeRunner.Record'], env))
        codes = [job.wait() for job in jobs]
    finally:
        server.terminate()
    print('editor exits', codes)
    return int(any(codes))


def publish(work):
    a, b = work/'native1'/'values.json', work/'native2'/'values.json'
    if not filecmp.cmp(a, b, shallow=False):
        sys.exit('the two Editor captures differ; not publishing')
    target = reference_file(PROBE, 'json')
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(a, target)
    print('published', target)
    return 0


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    project = copy_project(work/'projects'/'unbound')
    out = work/'unbound'/'values.json'
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        sys.exit(f'refusing to overwrite capture: {out}')
    env = dict(os.environ, NAVCOST_OUT=str(out))
    with (work/'unbound'/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                 '--headless', '30000', '--no-watch', '--scene', 'Assets/Scenes/Cost.unity'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    return result.returncode


# Every scenario is gated except the walking agents' frames (`walk.*`: Unity moves its agents in
# the early AI update, before Update; Unbound after the scripts) and the carved NavMesh's triangle
# fans (`fans.*`: the first vertex of a carved polygon), residuals listed in
# docs/COMPATIBILITY_GAPS.md; they are compared and listed, not gated.
def gated(name):
    return not name.startswith(('walk.', 'fans.'))


def check(work):
    code = unbound(work)
    out = work/'unbound'/'values.json'
    if not out.exists():
        print(f'no capture (unbound exit {code})')
        return 1
    report = work/'unbound'/'report.txt'
    with report.open('w') as f:
        subprocess.run([sys.executable, str(REPO/'tests/projects/navmesh/compare.py'),
                        str(reference_file(PROBE, 'json')), str(out), '--details', '1000'],
                       stdout=f, stderr=subprocess.STDOUT)
    text = report.read_text()
    print(text, end='')
    names = [line.split(':')[0] for line in text.splitlines() if re.match(r'^\S+: ', line)]
    ok = {line.split(':')[0] for line in text.splitlines() if re.match(r'^\S+: ok \(', line)}
    scenarios = [name for name in names if gated(name)]
    failing = [name for name in scenarios if name not in ok]
    print(f'gated: {len(scenarios) - len(failing)}/{len(scenarios)} scenarios match'
          + (f' (failing: {", ".join(failing)})' if failing else '') + f'; unbound exit {code}')
    return 1 if failing or code else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('setup', 'editor', 'publish', 'unbound', 'check'))
    parser.add_argument('--label', default=None)
    parser.add_argument('--release', required=True)
    args = parser.parse_args()
    if args.label is None:
        if args.mode != 'check':
            parser.error('--label is required')
        args.label = f'check-{time.strftime("%Y%m%d-%H%M%S")}-{os.getpid()}'
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    if args.mode == 'publish':
        return publish(work)
    if args.mode != 'publish' and (work/args.mode).exists():
        sys.exit(f'refusing to reuse label: {work/args.mode}')
    (work/args.mode).mkdir(parents=True)
    return {'setup': setup, 'editor': editor, 'unbound': unbound, 'check': check}[args.mode](work if args.mode != 'setup' else work/'setup')


if __name__ == '__main__':
    sys.exit(main())
