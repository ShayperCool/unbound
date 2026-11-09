#!/usr/bin/env python3
"""Record the native rendering plugin probe in the Unity Editor (native), run Unbound (engine),
publish, verify.

    run.py native --label L          two independent GPU Editor captures (L-1, L-2)
    run.py publish L-1 L-2           two identical captures become Reference/unity-<version>.jsonl
    run.py engine --label L          an Unbound capture (UNBOUND_CLI, else UNBOUND, else target/release)
    run.py verify --candidate PATH   compare a capture with the reference
    run.py check                     an engine run into a new directory, then verify (the landing gate)

Each run builds the probe's plugin (Plugins/nativegfx_probe.c, the C compiler `cc`, else $CC)
into the project copy's Assets/Plugins/x86_64, three times: libNativeGfxProbe.so, which the
scripts call, libNativeGfxPreload.so (its importer's "Load on startup", isPreloaded) and
libGfxPluginNativeGfxPrefix.so (only the GfxPlugin name prefix), which no script calls. The
plugins' records continue in a file after the scripts wrote theirs: what the first records from
then on (device events and UnityPluginUnload at shutdown) becomes `plugin.exit`, everything the
others record `plugin.preloaded` and `plugin.prefixed`. Linux x86_64 only: the plugin draws with
OpenGL.

The release is $UNBOUND_PROBE_UNITY_VERSION (else tests/probe_paths.py's default). Run output
stays under $XDG_CACHE_HOME/unbound/validation/runs/nativegfx/.
"""
import argparse
import json
import os
import platform
import shlex
import shutil
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import reference_file, run_root, unity_version  # noqa: E402
import editor_runner  # noqa: E402

WORK = run_root(ROOT, 'runs')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'values.jsonl'
SCRIPTS_OUT = 'scripts.jsonl'
PLUGIN_LOG = 'plugin.log'
LIBRARY = 'libNativeGfxProbe.so'
# The libraries built from the plugin source: the record prefix of the variants no script calls.
LIBRARIES = ((LIBRARY, None), ('libNativeGfxPreload.so', 'preload'), ('libGfxPluginNativeGfxPrefix.so', 'prefix'))


def require_platform():
    if sys.platform != 'linux' or platform.machine() not in ('x86_64', 'AMD64'):
        raise SystemExit('nativegfx: the probe plugin draws with OpenGL on Linux x86_64 only')


def copy_project(target):
    if target.exists():
        raise SystemExit(f'refusing to overwrite {target}')
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(ROOT/folder, target/folder)
    (target/'ProjectSettings/ProjectVersion.txt').write_text(f'm_EditorVersion: {unity_version()}\n')
    plugins = target/'Assets/Plugins/x86_64'
    plugins.mkdir(parents=True)
    # $CC may be a command with arguments (a compiler cache before the compiler).
    compiler = shlex.split(os.environ.get('CC') or 'cc')
    for library, prefix in LIBRARIES:
        defines = [f'-DNATIVEGFX_PRELOAD="{prefix} "'] if prefix else []
        subprocess.run([*compiler, *defines, '-shared', '-fPIC', '-O2', '-fvisibility=hidden', '-o', str(plugins/library),
                        str(ROOT/'Plugins/nativegfx_probe.c'), '-ldl', '-lpthread'], check=True)
        shutil.copyfile(ROOT/'Plugins'/(library + '.meta'), plugins/(library + '.meta'))


def assemble(out):
    """values.jsonl: the scripts' record with the plugin's shutdown record before the completion
    marker (the scripts' file is kept as it was written)."""
    scripts = out/SCRIPTS_OUT
    if not scripts.exists():
        return False
    rows = [json.loads(line) for line in scripts.read_text().splitlines() if line.strip()]
    lines = (out/PLUGIN_LOG).read_text().splitlines() if (out/PLUGIN_LOG).exists() else []
    variants = {prefix: [line for line in lines if line.startswith(prefix + ' ')] for _, prefix in LIBRARIES if prefix}
    lines = [line for line in lines if not any(line in found for found in variants.values())]
    exit_at = next((i for i, line in enumerate(lines) if line.startswith('mark exit')), None)
    tail = lines[exit_at + 1:] if exit_at is not None else ['(no exit mark)']
    complete = rows.pop() if rows and rows[-1].get('k') == 'complete' else None
    rows.append({'k': 'plugin.exit', 'v': tail})
    rows.append({'k': 'plugin.preloaded', 'v': variants['preload']})
    rows.append({'k': 'plugin.prefixed', 'v': variants['prefix']})
    if complete is not None:
        rows.append(complete)
    (out/OUT).write_text(''.join(json.dumps(row, separators=(',', ':')) + '\n' for row in rows))
    return True


def environment(out):
    return {'NATIVEGFX_PROBE_OUT': str(out/SCRIPTS_OUT), 'NATIVEGFX_PLUGIN_LOG': str(out/PLUGIN_LOG)}


def native(label):
    require_platform()
    jobs = []
    for repeat in (f'{label}-1', f'{label}-2'):
        out = WORK/repeat
        copy_project(out/'project')
        jobs.append(editor_runner.EditorJob(out/'project', out/'editor.log', out, ['-executeMethod', 'ProbeRunner.Record'],
                                            batch=True, graphics=True, env=environment(out),
                                            timeout=900, label='nativegfx-' + repeat))
    for result in editor_runner.run_parallel(jobs):
        print(editor_runner.summary(result))
    return 0 if all(assemble(job.out) for job in jobs) else 1


def engine(label):
    require_platform()
    out = WORK/label
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    copy_project(out/'project')
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    env = dict(os.environ, **environment(out))
    command = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '600', cli, 'run', '-p', str(out/'project'),
               '--no-watch', '--frames', '400', '--size', '64x32']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    print(label, 'exit', code)
    return 0 if code == 0 and assemble(out) else 1


def publish(labels):
    def write(path, first):
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(first/OUT, path)
    return editor_runner.publish([WORK/label for label in labels], reference_file(ROOT, 'jsonl'), lambda d: [OUT], write,
                                 complete=[editor_runner.jsonl_complete(OUT)], gpu=True)


def verify(candidate):
    return subprocess.run([sys.executable, str(ROOT/'compare.py'), str(reference_file(ROOT, 'jsonl')), str(candidate)]).returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['native', 'engine', 'publish', 'verify', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--candidate', type=Path)
    args = parser.parse_args()
    if args.mode in ('native', 'engine') and (not args.label or Path(args.label).name != args.label):
        parser.error('--label must be a single directory name')
    if args.mode == 'native':
        return native(args.label)
    if args.mode == 'engine':
        return engine(args.label)
    if args.mode == 'publish':
        return publish(args.labels)
    if args.mode == 'check':
        label = time.strftime('check-%Y%m%d-%H%M%S-') + str(os.getpid())
        return engine(label) or verify(WORK/label/OUT)
    if not args.candidate:
        parser.error('verify needs --candidate')
    return verify(args.candidate)


if __name__ == '__main__':
    sys.exit(main())
