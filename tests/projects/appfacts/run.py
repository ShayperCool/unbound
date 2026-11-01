#!/usr/bin/env python3
"""Records the application facts probe in the Unity Editor or runs it in Unbound.

    run.py editor  --release R --label L [--dry-run]
                    two independent windowed Editor captures (native1, native2)
    run.py check   --release R --label L   validates the captures and their machine facts
    run.py publish --release R --label L   checks that the two captures agree, writes Reference/unity-R
    run.py unbound --release R --label L   an Unbound capture (UNBOUND_CLI: the binary)

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/appfacts/<label>; nothing is written here except by
publish, which refuses to overwrite an existing reference. Each Editor runs through
tests/editor_runner.py: one launcher lease per repeat, a private Xvfb started inside the
lease, the GPU through VirtualGL, the renderer checked, hard and progress deadlines, and the
project copy's Hub/Roslyn helpers swept afterwards. Do not wrap this driver in with-heavy-job.

URL openers (xdg-open, open and the browsers) are stubs first on PATH that append their name
and arguments to openurl.log, so Application.OpenURL never starts a browser. Unbound's capture
also sets UNBOUND_OPEN_URL to the xdg-open stub.

Publishing requires each Editor to exit with 0 without a forced stop on the NVIDIA GPU, and
values.jsonl to end with `complete: true` and be byte-identical between the two captures.
machine.jsonl is what the machine decides; each capture saves what the OS reports (os.json)
beside it, and `check` / compare.py --machine verify the facts against it rather than against
another machine's capture. Neither file is published: they identify this machine and its user.
"""
import argparse
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_root, run_root
from editor_runner import EditorJob, load_result, publish as publish_repeats, result_problem, \
    run_parallel, summary

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
OPENERS = ('xdg-open', 'open', 'gio', 'gnome-open', 'kde-open', 'kde-open5', 'gvfs-open', 'x-www-browser',
           'sensible-browser', 'www-browser', 'google-chrome', 'google-chrome-stable', 'chromium',
           'chromium-browser', 'firefox')
REPEATS = ('native1', 'native2')
# Deadlines, counted from admission into the lease (the old runner's 1800 s also covered the
# wait for it). The probe flushes its records after each step. In the published pair the
# first write came 34 s and 172 s after the Editor started (the slower repeat spent 133 s
# resolving packages) and the Editor exited within 40 s of it. With the 140 s AssetDatabase
# refresh outlier of tools/validation/README.md on top of that start, 480 s without a write
# is a stall. A stall inside Play mode therefore ends as no-progress before ProbeRunner's own
# 600 s watchdog; neither can publish. The hard timeout stays as the outer bound
# (EDITOR_RUNNER.md), above the progress budget plus that watchdog.
PROGRESS_TIMEOUT = 480
HARD_TIMEOUT = 1200
PROGRESS_FILES = ('values.jsonl', 'machine.jsonl', 'openurl.log')


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


def stub_env(out):
    """The environment that puts the recording URL openers in `out` first on PATH."""
    directory = out/'stubs'
    return {'APPFACTS_OPENURL_LOG': str(out/'openurl.log'), 'APPFACTS_OPENURL_STUBS': str(directory),
            'PATH': f'{directory}:{os.environ["PATH"]}', 'BROWSER': str(directory/'sensible-browser')}


def stubs(out):
    """Recording URL openers and the environment that puts them first on PATH."""
    directory = out/'stubs'
    directory.mkdir()
    log = out/'openurl.log'
    for name in OPENERS:
        script = directory/name
        script.write_text('#!/bin/sh\n{ printf "%s" "$(basename "$0")"; for a in "$@"; do printf "\\t%s" "$a"; done; '
                          f'printf "\\n"; }} >> "{log}"\n')
        script.chmod(0o755)
    return stub_env(out)


def read(path, default=None):
    try:
        return Path(path).read_text().strip()
    except OSError:
        return default


def os_facts():
    """What this machine's OS reports, for the machine-dependent values."""
    facts = {'hostname': platform.node(), 'kernel': platform.release(), 'machineId': read('/etc/machine-id')}
    cpu = {}
    for line in (read('/proc/cpuinfo', '') or '').splitlines():
        key, _, value = line.partition(':')
        cpu.setdefault(key.strip(), value.strip())
    facts['cpuVendor'] = cpu.get('vendor_id')
    facts['cpuModel'] = cpu.get('model name')
    facts['cpuMHz'] = cpu.get('cpu MHz')
    facts['cpuCount'] = os.cpu_count()
    facts['cpufreqMax'] = read('/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq')
    facts['cpufreqBase'] = read('/sys/devices/system/cpu/cpu0/cpufreq/base_frequency')
    for key in ('product_name', 'product_version', 'board_name', 'board_vendor', 'sys_vendor', 'product_family'):
        facts['dmi.'+key] = read('/sys/devices/virtual/dmi/id/'+key)
    for line in (read('/proc/meminfo', '') or '').splitlines():
        if line.startswith('MemTotal:'):
            facts['memTotalKiB'] = int(line.split()[1])
    gpus = []
    for card in sorted(Path('/sys/class/drm').glob('card[0-9]')):
        gpus.append({'card': card.name, 'vendor': read(card/'device/vendor'), 'device': read(card/'device/device'),
                     'vram': read(card/'device/mem_info_vram_total')})
    facts['gpus'] = gpus
    facts['nvidiaVersion'] = read('/sys/module/nvidia/version')
    for info in sorted(Path('/proc/driver/nvidia/gpus').glob('*/information')):
        for line in info.read_text().splitlines():
            if line.startswith('Model:'):
                facts.setdefault('nvidiaModel', line.split(':', 1)[1].strip())
    try:
        out = subprocess.run(['nvidia-smi', '--query-gpu=name,memory.total,pci.device_id', '--format=csv,noheader,nounits'],
                             capture_output=True, text=True, timeout=30).stdout.strip().splitlines()
        facts['nvidiaSmi'] = out[0] if out else None
    except (OSError, subprocess.TimeoutExpired):
        facts['nvidiaSmi'] = None
    facts['powerSupplies'] = sorted(p.name for p in Path('/sys/class/power_supply').glob('*'))
    return facts


def job(work, repeat):
    project, out = work/'projects'/repeat, work/repeat
    return EditorJob(project, out/'editor.log', out, env={'APPFACTS_PROBE_OUT': str(out), **stub_env(out)},
                     timeout=HARD_TIMEOUT, progress_files=[out/name for name in PROGRESS_FILES],
                     progress_timeout=PROGRESS_TIMEOUT, label='appfacts-'+repeat)


def editor(work, dry_run=False):
    jobs = [job(work, repeat) for repeat in REPEATS]
    if dry_run:
        for j in jobs:
            print(json.dumps({'label': j.label, 'kind': j.kind, 'command': j.command(),
                              'env': {k: v for k, v in j.env.items() if k != 'PATH'},
                              'path_prefix': j.env['PATH'].split(':', 1)[0], 'timeout': j.timeout,
                              'progress_files': j.progress_files, 'progress_timeout': j.progress_timeout},
                             indent=1))
        return 0
    for j in jobs:
        copy_project(j.project)
        out = fresh_output(j.out)
        stubs(out)
        (out/'os.json').write_text(json.dumps(os_facts(), indent=1, sort_keys=True)+'\n')
    results = run_parallel(jobs)
    for result in results:
        print(summary(result))
    return int(any(r['exit_code'] != 0 or r.get('forced_exit') for r in results))


def rows(path):
    return [json.loads(line) for line in path.read_text().splitlines() if line.strip()]


def editor_problem(repeat):
    """Why the Editor run cannot back a reference: strict exit 0 on the NVIDIA GPU."""
    reason = result_problem(repeat, gpu=True)
    if not reason and (load_result(repeat) or {}).get('forced_exit'):
        reason = 'forced exits are not accepted by this probe'
    return reason


def capture_problem(repeat):
    """Why the probe output is incomplete (None when it is complete)."""
    if (repeat/'watchdog.txt').exists() or not (repeat/'values.jsonl').exists() or not (repeat/'machine.jsonl').exists():
        return 'the capture did not finish'
    try:
        values = rows(repeat/'values.jsonl')
        rows(repeat/'machine.jsonl')
    except json.JSONDecodeError:
        return 'a record is not complete JSON lines'
    keys = [r.get('k') for r in values]
    if not values or values[-1] != {'k': 'complete', 'v': True} or keys.count('complete') != 1:
        return 'the probe did not record complete: true last'
    if 'fatal' in keys:
        return 'the probe recorded a fatal error'
    if len(set(keys)) != len(keys):
        return 'duplicate keys'
    return None


def check(work):
    sys.path.insert(0, str(PROBE))
    import compare
    status = 0
    for repeat in (work/r for r in REPEATS):
        reason = editor_problem(repeat) or capture_problem(repeat)
        print(repeat.name, reason or 'complete')
        if reason is None:
            failures = compare.machine(repeat, editor=True)
            print(repeat.name, 'machine facts', 'agree with the OS' if not failures else failures)
            status |= bool(failures)
        status |= reason is not None
    return int(status)


def write_reference(target, first):
    target.mkdir(parents=True)
    shutil.copyfile(first/'values.jsonl', target/'values.jsonl')
    print('published', target)


def publish(work):
    repeats = [work/r for r in REPEATS]
    for repeat in repeats:
        if not repeat.is_dir():
            sys.exit(f'{repeat.name}: no capture; not publishing')
        reason = editor_problem(repeat)
        if reason:
            sys.exit(f'{repeat.name}: {reason}; not publishing')
    return publish_repeats(repeats, reference_root(PROBE), lambda d: ['values.jsonl'],
                           write_reference, complete=[capture_problem], gpu=True)


def unbound(work):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound')
    out = fresh_output(work/'unbound')
    (out/'os.json').write_text(json.dumps(os_facts(), indent=1, sort_keys=True)+'\n')
    env = dict(os.environ, APPFACTS_PROBE_OUT=str(out), **stubs(out))
    env['UNBOUND_OPEN_URL'] = str(out/'stubs/xdg-open')
    with (out/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), 'timeout', '900', cli, 'run', '-p', str(project),
                                 '--no-watch', '--headless', '3000', '--size', '64x32'],
                                env=env, stdout=log, stderr=subprocess.STDOUT, cwd=project)
    (out/'exit.txt').write_text(f'{result.returncode}\n')
    print('unbound exit', result.returncode, out)
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('editor', 'publish', 'unbound', 'check'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    parser.add_argument('--dry-run', action='store_true',
                        help='editor: print the Editor jobs without copying projects or launching')
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    if args.dry_run and args.mode != 'editor':
        parser.error('--dry-run applies to editor only')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    if args.mode == 'editor':
        return editor(work, args.dry_run)
    return {'publish': publish, 'unbound': unbound, 'check': check}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
