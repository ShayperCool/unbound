#!/usr/bin/env python3
"""Records the sampler state probe in the Unity Editor or runs it in Unbound.

    run.py native --label L       two independent GPU Editor captures (L-1, L-2)
    run.py publish L-1 L-2        two identical captures become the device's reference
    run.py engine --label L       an Unbound capture (UNBOUND_CLI, else UNBOUND, else target/release)
    run.py check                  an Unbound capture compared with the reference (the land gate)

--device selects the graphics device of both sides: opengl (the default: the Linux Editor's own
device, on a private display through VirtualGL; Unbound with --force-glcore; the reference is
Reference/unity-R.jsonl) or vulkan (the Editor with -force-vulkan on the GPU-backed display :0,
Unbound with --force-vulkan; Reference/unity-R/vulkan.jsonl).

The probe's script and shaders are staged over a fresh copy of the UI Toolkit fixture
(tests/projects/uitk: its scene runs the script) under
$XDG_CACHE_HOME/unbound/validation/runs/samplerstates/<label>; nothing is written here except by
publish. A capture replaces an existing reference only when it keeps every row of it as it is
(a probe with added cases, recorded again). The release is $UNBOUND_PROBE_UNITY_VERSION (else
tests/probe_paths.py's default). Every missing, extra and failing value is printed; the keys the
device's residuals file lists (residuals.json, residuals-vulkan.json) are printed with their
reason and do not fail the check.
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(ROOT.parents[1]))
sys.path.insert(0, str(ROOT.parent/'nativegfx'))
from probe_paths import reference_file, reference_root, run_root, unity_version  # noqa: E402
import compare  # noqa: E402
import editor_runner  # noqa: E402

LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'capture/values.jsonl'
DEVICE = 'opengl'


def reference():
    return reference_file(ROOT, 'jsonl') if DEVICE == 'opengl' else reference_root(ROOT)/'vulkan.jsonl'


def residuals():
    return ROOT/('residuals.json' if DEVICE == 'opengl' else 'residuals-vulkan.json')


def stage(label):
    work = run_root(ROOT, label)
    if work.exists():
        sys.exit(f'refusing to overwrite run: {work}')
    project = work/'project'
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(ROOT.parent/'uitk'/part, project/part)
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+unity_version()+'\n')
    shutil.copy2(ROOT/'Assets/Scripts/SamplerStatesProbe.cs', project/'Assets/Scripts/UitkProbe.cs')
    shutil.copytree(ROOT/'Assets/Shaders', project/'Assets/Shaders')
    return work, project


def native(label):
    jobs = []
    for repeat in (f'{label}-1', f'{label}-2'):
        work, project = stage(repeat)
        jobs.append(editor_runner.EditorJob(project, work/'editor.log', work, ['-executeMethod', 'ProbeRunner.Record'],
                                            batch=True, graphics=True, env={'SAMPLER_STATES_OUT': str(work/'capture')},
                                            vulkan=DEVICE == 'vulkan', timeout=1500, label='samplerstates-'+repeat))
    # The Vulkan Editors share the display :0: one after the other.
    results = [editor_runner.run(job) for job in jobs] if DEVICE == 'vulkan' else editor_runner.run_parallel(jobs)
    for result in results:
        print(editor_runner.summary(result))
    return 0 if all((job.out/OUT).is_file() for job in jobs) else 1


def engine(label):
    work, project = stage(label)
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    env = dict(os.environ, SAMPLER_STATES_OUT=str(work/'capture'))
    command = [str(LAUNCHER), '--kind', 'engine-gpu', 'timeout', '-s', 'KILL', '1500', cli, 'run', '-p', str(project),
               '--no-watch', '--frames', '35', '--size', '320x180',
               '--force-'+('vulkan' if DEVICE == 'vulkan' else 'glcore')]
    with (work/'process.log').open('w') as log:
        code = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    values = work/OUT
    if code or not values.is_file() or '"complete"' not in values.read_text():
        sys.exit(f'{label}: exit {code}, capture incomplete; see {work}')
    print('engine', values)
    return values


def kept(old, new):
    """Whether `new` has every row of `old` as it is, in its order (the completion marker is
    the last row of both)."""
    rest = iter(new.read_bytes().splitlines())
    return all(row in rest for row in old.read_bytes().splitlines())


def publish(labels):
    target = reference()
    fresh = target.with_name(target.name+'.new')

    def write(path, first):
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(first/OUT, path)
    fresh.unlink(missing_ok=True)
    editor_runner.publish([run_root(ROOT, label) for label in labels], fresh, lambda d: [OUT], write,
                          complete=[editor_runner.jsonl_complete(OUT)], gpu=True)
    if target.exists() and not kept(target, fresh):
        fresh.unlink()
        sys.exit(f'the captures change rows of the reference; not replacing {target}')
    fresh.replace(target)
    print('published', target)
    return 0


def check():
    values = engine(f'check-{DEVICE}-{time.time_ns()}')
    compare.RESIDUALS = residuals()
    sys.argv = ['compare.py', str(reference()), str(values)]
    return compare.main()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['native', 'engine', 'publish', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--device', choices=('opengl', 'vulkan'), default='opengl')
    args = parser.parse_args()
    global DEVICE
    DEVICE = args.device
    if args.mode == 'publish':
        if len(args.labels) != 2:
            parser.error('publish takes two native labels')
        return publish(args.labels)
    if args.mode == 'check':
        return check()
    if not args.label or Path(args.label).name != args.label:
        parser.error('--label must be a single directory name')
    if args.mode == 'native':
        return native(args.label)
    engine(args.label)
    return 0


if __name__ == '__main__':
    sys.exit(main())
