#!/usr/bin/env python3
"""Record the native rendering plugin probe in the Unity Editor (native), run Unbound (engine),
publish, verify.

    run.py native --label L          two independent GPU Editor captures (L-1, L-2)
    run.py publish L-1 L-2           two identical captures become Reference/unity-<version>.jsonl
    run.py engine --label L          an Unbound capture (UNBOUND_CLI, else UNBOUND, else target/release)
    run.py verify --candidate PATH   compare a capture with the reference
    run.py check                     an engine run into a new directory, then verify (the landing gate)

--device selects the graphics device of both sides: opengl (the default: the Linux Editor's own
device, on a private display through VirtualGL; Unbound with --force-glcore) or vulkan (the Editor
with -force-vulkan on the GPU-backed display :0, Unbound with --force-vulkan). On Vulkan the
probe's Vulkan plugin (Plugins/nativegfx_vulkan.c, libNativeGfxVulkan.so, against the system's
Vulkan headers) and NativeGfxVulkanProbe.cs record the Vulkan plugin interface instead; their
reference is Reference/unity-<version>/vulkan.jsonl.

Each run builds the probe's plugin (Plugins/nativegfx_probe.c, the C compiler `cc`, else $CC)
into the project copy's Assets/Plugins/x86_64, three times: libNativeGfxProbe.so, which the
scripts call, libNativeGfxPreload.so (its importer's "Load on startup", isPreloaded) and
libGfxPluginNativeGfxPrefix.so (only the GfxPlugin name prefix), which no script calls. The
plugins' records continue in a file after the scripts wrote theirs: what the first records from
then on (device events and UnityPluginUnload at shutdown) becomes `plugin.exit`, everything the
others record `plugin.preloaded` and `plugin.prefixed`. The plugin draws with OpenGL: the Editor
captures and `check` run on Linux x86_64 only. `engine` also runs on macOS arm64, where the
libraries are .dylib files in Assets/Plugins/macOS with the importer settings OS OSX, CPU
ARM64; Unbound gives plugins no graphics context there, so that capture shows what works
without one (README.md, "macOS").

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
VULKAN_LIBRARY = 'libNativeGfxVulkan.so'
DEVICE = 'opengl'
STENCIL = False


LINUX = sys.platform == 'linux' and platform.machine() in ('x86_64', 'AMD64')
MACOS = sys.platform == 'darwin' and platform.machine() == 'arm64'


def require_platform(engine=False):
    if LINUX or (engine and MACOS and DEVICE == 'opengl'):
        return
    raise SystemExit('nativegfx: the probe plugin draws with OpenGL on Linux x86_64 only '
                     '(an engine run of the OpenGL probe also runs on macOS arm64)')


def library_file(name):
    """The file of a probe library on this OS: libX.so on Linux, libX.dylib on macOS."""
    return name[:-len('.so')] + '.dylib' if MACOS else name


def plugin_meta(name):
    """The importer settings of a probe library: as committed (Linux), or with OS OSX and CPU ARM64
    (macOS)."""
    text = (ROOT/'Plugins'/(name + '.meta')).read_text()
    return text.replace('CPU: x86_64', 'CPU: ARM64').replace('OS: Linux', 'OS: OSX') if MACOS else text


def copy_project(target):
    if target.exists():
        raise SystemExit(f'refusing to overwrite {target}')
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(ROOT/folder, target/folder)
    (target/'ProjectSettings/ProjectVersion.txt').write_text(f'm_EditorVersion: {unity_version()}\n')
    plugins = target/'Assets/Plugins'/('macOS' if MACOS else 'x86_64')
    plugins.mkdir(parents=True)
    # $CC may be a command with arguments (a compiler cache before the compiler).
    compiler = shlex.split(os.environ.get('CC') or 'cc')
    # The event queue is a C++ interface: that part of the plugin the scripts call is C++
    # (`c++`, else $CXX), linked into it.
    events = plugins/'nativegfx_events.o'
    subprocess.run([str(HEAVY), '--kind', 'build', *shlex.split(os.environ.get('CXX') or 'c++'), '-c', '-fPIC', '-O2', '-fvisibility=hidden', '-fno-exceptions',
                    '-fno-rtti', '-o', str(events), str(ROOT/'Plugins/nativegfx_events.cpp')], check=True)
    for library, prefix in LIBRARIES:
        defines = [f'-DNATIVEGFX_PRELOAD="{prefix} "'] if prefix else []
        # On macOS the C++ event part needs libc++ (Linux links it through the C++ object).
        if MACOS:
            libraries = [] if prefix else ['-lc++']
        else:
            libraries = ['-ldl']
        file = library_file(library)
        subprocess.run([str(HEAVY), '--kind', 'build', *compiler, *defines, '-shared', '-fPIC', '-O2', '-fvisibility=hidden', '-o', str(plugins/file),
                        str(ROOT/'Plugins/nativegfx_probe.c'), *([] if prefix else [str(events)]), *libraries, '-lpthread'],
                       check=True)
        (plugins/(file + '.meta')).write_text(plugin_meta(library))
    events.unlink()
    if DEVICE == 'vulkan':
        subprocess.run([str(HEAVY), '--kind', 'build', *compiler, '-shared', '-fPIC', '-O2', '-fvisibility=hidden', '-o', str(plugins/VULKAN_LIBRARY),
                        str(ROOT/'Plugins/nativegfx_vulkan.c'), '-lpthread'], check=True)
        shutil.copyfile(ROOT/'Plugins'/(VULKAN_LIBRARY + '.meta'), plugins/(VULKAN_LIBRARY + '.meta'))


def assemble(out):
    """values.jsonl: the scripts' record with the plugin's shutdown record before the completion
    marker (the scripts' file is kept as it was written)."""
    scripts = out/SCRIPTS_OUT
    if not scripts.exists():
        return False
    rows = [json.loads(line) for line in scripts.read_text().splitlines() if line.strip()]
    lines = (out/PLUGIN_LOG).read_text().splitlines() if (out/PLUGIN_LOG).exists() else []
    # What the variants' rendering extension exports were asked (lines of their own kind).
    extensions = [line[4:] for line in lines if line.startswith('ext ')]
    lines = [line for line in lines if not line.startswith('ext ')]
    variants = {prefix: [line for line in lines if line.startswith(prefix + ' ')] for _, prefix in LIBRARIES if prefix}
    lines = [line for line in lines if not any(line in found for found in variants.values())]
    exit_at = next((i for i, line in enumerate(lines) if line.startswith('mark exit')), None)
    tail = lines[exit_at + 1:] if exit_at is not None else ['(no exit mark)']
    complete = rows.pop() if rows and rows[-1].get('k') == 'complete' else None
    rows.append({'k': 'plugin.exit', 'v': tail})
    rows.append({'k': 'plugin.preloaded', 'v': variants['preload']})
    rows.append({'k': 'plugin.prefixed', 'v': variants['prefix']})
    for _, prefix in LIBRARIES:
        if prefix:
            rows.append({'k': 'ext.' + prefix, 'v': [line for line in extensions if line.startswith(prefix + ' ')]})
    if complete is not None:
        rows.append(complete)
    (out/OUT).write_text(''.join(json.dumps(row, separators=(',', ':')) + '\n' for row in rows))
    return True


def environment(out):
    if DEVICE == 'vulkan':
        return {'NATIVEGFX_VULKAN_OUT': str(out/OUT)}
    return {'NATIVEGFX_PROBE_OUT': str(out/SCRIPTS_OUT), 'NATIVEGFX_PLUGIN_LOG': str(out/PLUGIN_LOG),
            'NATIVEGFX_STENCIL': '1' if STENCIL else '0'}


def finish(out):
    """The capture's values.jsonl: assembled from the OpenGL probe's records; written whole by
    the Vulkan probe."""
    return (out/OUT).exists() if DEVICE == 'vulkan' else assemble(out)


def reference():
    if STENCIL:
        return ROOT/'Reference'/f'unity-{unity_version()}'/'stencil.jsonl'
    return reference_file(ROOT, 'jsonl') if DEVICE == 'opengl' else ROOT/'Reference'/f'unity-{unity_version()}'/'vulkan.jsonl'


def native(label):
    require_platform()
    jobs = []
    for repeat in (f'{label}-1', f'{label}-2'):
        out = WORK/repeat
        copy_project(out/'project')
        jobs.append(editor_runner.EditorJob(out/'project', out/'editor.log', out, ['-executeMethod', 'ProbeRunner.Record'],
                                            batch=True, graphics=True, env=environment(out), vulkan=DEVICE == 'vulkan',
                                            timeout=900, label='nativegfx-' + repeat))
    # The Vulkan Editors share the display :0: one after the other.
    results = [editor_runner.run(job) for job in jobs] if DEVICE == 'vulkan' else editor_runner.run_parallel(jobs)
    for result in results:
        print(editor_runner.summary(result))
    return 0 if all(finish(job.out) for job in jobs) else 1


def engine(label):
    require_platform(engine=True)
    out = WORK/label
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    copy_project(out/'project')
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    env = dict(os.environ, **environment(out))
    # macOS has one device (the default); Linux selects the probe's.
    device = [] if MACOS else ['--force-' + ('vulkan' if DEVICE == 'vulkan' else 'glcore')]
    command = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '600', cli, 'run', '-p', str(out/'project'),
               '--no-watch', '--frames', '400', '--size', '64x32', *device]
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    print(label, 'exit', code)
    return 0 if code == 0 and finish(out) else 1


def publish(labels):
    """A capture replaces an existing reference only when it keeps every row of it as it is, in
    its order (a probe with added records, recorded again)."""
    target = reference()
    fresh = target.with_name(target.name + '.new')

    def write(path, first):
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(first/OUT, path)
    fresh.unlink(missing_ok=True)
    code = editor_runner.publish([WORK/label for label in labels], fresh, lambda d: [OUT], write,
                                 complete=[editor_runner.jsonl_complete(OUT)], gpu=True)
    if code:
        return code
    if target.exists():
        rest = iter(fresh.read_bytes().splitlines())
        if not all(row in rest for row in target.read_bytes().splitlines()):
            fresh.unlink()
            raise SystemExit(f'the captures change rows of the reference; not replacing {target}')
    fresh.replace(target)
    return 0


def verify(candidate):
    return subprocess.run([sys.executable, str(ROOT/'compare.py'), str(reference()), str(candidate), "--device", DEVICE]).returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['native', 'engine', 'publish', 'verify', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--candidate', type=Path)
    parser.add_argument('--device', choices=('opengl', 'vulkan'), default='opengl')
    parser.add_argument('--stencil', action='store_true', help='OpenGL retained-stencil controls')
    args = parser.parse_args()
    global DEVICE, STENCIL
    DEVICE = args.device
    STENCIL = args.stencil
    if STENCIL and (DEVICE != 'opengl' or not LINUX):
        parser.error('--stencil is measured on Linux OpenGL only')
    if args.mode in ('native', 'engine') and (not args.label or Path(args.label).name != args.label):
        parser.error('--label must be a single directory name')
    if args.mode == 'native':
        return native(args.label)
    if args.mode == 'engine':
        return engine(args.label)
    if args.mode == 'publish':
        return publish(args.labels)
    if args.mode == 'check':
        require_platform()
        label = time.strftime('check-%Y%m%d-%H%M%S-') + str(os.getpid())
        return engine(label) or verify(WORK/label/OUT)
    if not args.candidate:
        parser.error('verify needs --candidate')
    return verify(args.candidate)


if __name__ == '__main__':
    sys.exit(main())
