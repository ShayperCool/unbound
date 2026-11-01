#!/usr/bin/env python3
"""Records the application facts probe in the Unity Editor or runs it in Unbound.

    run.py editor  --release R --label L   two independent windowed Editor captures (native1, native2)
    run.py check   --release R --label L   validates the captures and their machine facts
    run.py publish --release R --label L   checks that the two captures agree, writes Reference/unity-R
    run.py unbound --release R --label L   an Unbound capture (UNBOUND_CLI: the binary)

Every capture runs on a fresh copy of the probe's Assets/Packages/ProjectSettings under
$XDG_CACHE_HOME/unbound/validation/runs/appfacts/<label>; nothing is written here except by
publish, which refuses to overwrite an existing reference. Editors run through the shared
heavy-job launcher, windowed on a private Xvfb each, on the GPU through VirtualGL.

URL openers (xdg-open, open and the browsers) are stubs first on PATH that append their name
and arguments to openurl.log, so Application.OpenURL never starts a browser. Unbound's capture
also sets UNBOUND_OPEN_URL to the xdg-open stub.

values.jsonl must be byte-identical between the two Editor captures. machine.jsonl is what the
machine decides; each capture saves what the OS reports (os.json) beside it, and `check` /
compare.py --machine verify the facts against it rather than against another machine's capture.
"""
import argparse
import json
import os
from pathlib import Path
import platform
import shutil
import signal
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, reference_root, run_root

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
OPENERS = ('xdg-open', 'open', 'gio', 'gnome-open', 'kde-open', 'kde-open5', 'gvfs-open', 'x-www-browser',
           'sensible-browser', 'www-browser', 'google-chrome', 'google-chrome-stable', 'chromium',
           'chromium-browser', 'firefox')


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
    return {'APPFACTS_OPENURL_LOG': str(log), 'APPFACTS_OPENURL_STUBS': str(directory),
            'PATH': f'{directory}:{os.environ["PATH"]}', 'BROWSER': str(directory/'sensible-browser')}


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


def display():
    """A private X server for one Editor."""
    for number in range(120, 200):
        if not Path(f'/tmp/.X{number}-lock').exists() and not Path(f'/tmp/.X11-unix/X{number}').exists():
            server = subprocess.Popen(['Xvfb', f':{number}', '-screen', '0', '1280x720x24', '-nolisten', 'tcp'],
                                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            return f':{number}', server
    sys.exit('no free X display')


def stop_compilers(project):
    """Stops only the Unity Roslyn servers and the Hub a windowed Editor started for this
    project copy (the Hub's command line names the project)."""
    for process in Path('/proc').glob('[0-9]*'):
        try:
            command = (process/'cmdline').read_bytes()
            if b'unityhub-bin' in command and str(project).encode()+b'\0' in command:
                os.kill(int(process.name), signal.SIGTERM)
                continue
            if b'VBCSCompiler' not in command:
                continue
            env = dict(i.split(b'=', 1) for i in (process/'environ').read_bytes().split(b'\0') if b'=' in i)
            if env.get(b'PWD') == str(project).encode():
                os.kill(int(process.name), signal.SIGTERM)
        except (FileNotFoundError, ProcessLookupError, PermissionError):
            pass


def editor(work):
    started = []
    codes = []
    try:
        for repeat in ('native1', 'native2'):
            project = copy_project(work/'projects'/repeat)
            out = fresh_output(work/repeat)
            (out/'os.json').write_text(json.dumps(os_facts(), indent=1, sort_keys=True)+'\n')
            name, server = display()
            env = dict(os.environ, DISPLAY=name, APPFACTS_PROBE_OUT=str(out), PWD=str(project), **stubs(out))
            env.pop('XAUTHORITY', None)
            job = subprocess.Popen(
                [str(LAUNCHER), 'timeout', '-s', 'KILL', '1800', 'vglrun', '-d', 'egl', str(editor_path()),
                 '-projectPath', str(project), '-logFile', str(out/'editor.log')], cwd=project, env=env)
            started.append((repeat, project, server, job))
        codes = [job.wait() for _, _, _, job in started]
        for (repeat, _, _, _), code in zip(started, codes):
            (work/repeat/'exit.txt').write_text(f'{code}\n')
    finally:
        for _, project, server, job in started:
            if job.poll() is None:
                job.kill()
            server.terminate()
            server.wait()
            stop_compilers(project)
    print('editor exits', codes)
    return int(any(codes))


def rows(path):
    return [json.loads(line) for line in path.read_text().splitlines() if line.strip()]


def incomplete(repeat, editor=True):
    """Why a capture cannot be published (None when it is complete)."""
    if editor:
        log = (repeat/'editor.log').read_text(errors='replace') if (repeat/'editor.log').exists() else ''
        if 'NVIDIA' not in next((line for line in log.splitlines() if 'Renderer:' in line), ''):
            return 'NVIDIA GPU renderer was not verified'
        exit_file = repeat/'exit.txt'
        if not exit_file.exists() or exit_file.read_text().strip() != '0':
            return 'the Editor did not exit with 0'
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
    for repeat in (work/'native1', work/'native2'):
        reason = incomplete(repeat)
        print(repeat.name, reason or 'complete')
        if reason is None:
            failures = compare.machine(repeat, editor=True)
            print(repeat.name, 'machine facts', 'agree with the OS' if not failures else failures)
            status |= bool(failures)
        status |= reason is not None
    return int(status)


def publish(work):
    a, b = work/'native1', work/'native2'
    for repeat in (a, b):
        reason = incomplete(repeat)
        if reason:
            sys.exit(f'{repeat.name}: {reason}; not publishing')
    if (a/'values.jsonl').read_bytes() != (b/'values.jsonl').read_bytes():
        sys.exit('the two Editor captures differ in values; not publishing')
    target = reference_root(PROBE)
    if target.exists():
        sys.exit(f'refusing to overwrite reference: {target}')
    target.mkdir(parents=True)
    shutil.copyfile(a/'values.jsonl', target/'values.jsonl')
    print('published', target)
    return 0


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
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    return {'editor': editor, 'publish': publish, 'unbound': unbound, 'check': check}[args.mode](work)


if __name__ == '__main__':
    sys.exit(main())
