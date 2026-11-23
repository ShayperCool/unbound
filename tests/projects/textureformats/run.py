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

--device selects the graphics device: opengl (the default: the Linux Editor's own device,
Reference/unity-R.jsonl), vulkan (Reference/unity-R/vulkan-q58.jsonl) or metal (macOS arm64:
the macOS Editor in batch mode, which draws with Metal, through tests/macos_editor.py;
Reference/unity-R/metal.jsonl, the readback cases). On macOS the engine draws with Metal: an
opengl check compares it with the Linux OpenGL Core reference (what the device does not decide
compares with the Linux references, AGENTS.md Platforms), and a vulkan check, a device macOS
does not have, checks the Metal reference instead (said so on its first line).
The opengl-resolve profile uses Reference/unity-R/opengl-resolve.jsonl on Linux.

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
import platform
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_file, reference_root, run_root, unity_version
from editor_runner import EditorJob, jsonl_complete, publish as publish_repeats, run, run_parallel, summary
import macos_editor

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
HELLO = PROBE.parent/'hello'
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
REPEATS = ('native1', 'native2')
OUTPUT = 'values.jsonl'
# The cases Unbound matches (TEXTURE_FORMATS_PROBE_ONLY prefixes): every one.
SUPPORTED = 'rt,tex,feedback'
BYTE_CASE = 'rt.unorm16Byte'
BYTE_DEVICES = {'opengl-r16-byte': 'OpenGLCore', 'vulkan-r16-byte': 'Vulkan'}
VULKAN_DEVICES = ('vulkan', 'vulkan-r16-byte')
# Unorm16Byte reads 30 words per channel and records tightly packed payloads.
BYTE_TEXELS = 30
BYTE_INPUT_CHANNELS = {'r': 1, 'rg': 2, 'rgba': 4}
BYTE_CREATED = {f'{BYTE_CASE}.{source}.created' for source in BYTE_INPUT_CHANNELS}
BYTE_PAYLOAD_BYTES = {
    f'{BYTE_CASE}.{source}.{image}.{record}': BYTE_TEXELS * bytes_per_texel
    for source, channels in BYTE_INPUT_CHANNELS.items()
    for image in ('texture', 'render')
    for record, bytes_per_texel in (
        ('raw', 2 * channels), ('rgba8', 4), ('argb8', 4), ('bgra8', 4),
        ('rgb8', 3), ('r8', 1), ('rg8', 2), ('half', 8)
    )
}
BYTE_RECORDS = ({'complete', BYTE_CASE + '.device', BYTE_CASE + '.release'}
                | BYTE_CREATED | set(BYTE_PAYLOAD_BYTES))
RESOLVE_CASES = 'rt.commandResolve'
RESOLVE_DEVICES = ('opengl-resolve', 'opengl-resolve-linear')
DEVICE = 'opengl'
# This device's reference covers the added format/clear/feedback mechanisms. Older packed
# UNorm async-to-float cases do not repeat on this device and have no canonical reference.
VULKAN_CASES = 'rt.partial,rt.readControl,rt.R16_SNorm,rt.R16G16_SNorm,rt.R16G16B16_SNorm,rt.R16G16B16A16_SNorm,rt.A2B10G10R10_UIntPack32,feedback.integer'
# The Metal reference's cases: reading targets back whatever their ReadPixels usage answers.
METAL_CASES = 'readback'
MACOS = sys.platform == 'darwin' and platform.machine() == 'arm64'


def device_cases():
    """The cases the device's reference was recorded with (None: every case)."""
    if DEVICE in BYTE_DEVICES:
        return BYTE_CASE
    if DEVICE in RESOLVE_DEVICES:
        return RESOLVE_CASES
    return {'vulkan': VULKAN_CASES, 'metal': METAL_CASES}.get(DEVICE)


def reference():
    if DEVICE == 'opengl':
        return reference_file(PROBE, 'jsonl')
    if DEVICE in BYTE_DEVICES or DEVICE in RESOLVE_DEVICES:
        return reference_root(PROBE)/f'{DEVICE}.jsonl'
    return reference_root(PROBE)/('vulkan-q58.jsonl' if DEVICE == 'vulkan' else 'metal.jsonl')


def residuals():
    if DEVICE in BYTE_DEVICES or DEVICE in RESOLVE_DEVICES:
        return None
    return PROBE/('residuals.json' if DEVICE == 'opengl' else f'residuals-{DEVICE}.json')


def copy_project(dest, release):
    if dest.exists():
        sys.exit(f'refusing to overwrite project: {dest}')
    for part in ('Packages', 'ProjectSettings'):
        shutil.copytree(HELLO/part, dest/part)
    if DEVICE == 'opengl-resolve-linear':
        settings = dest/'ProjectSettings'/'ProjectSettings.asset'
        settings.write_text(settings.read_text().replace('m_ActiveColorSpace: 0', 'm_ActiveColorSpace: 1'))
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


def capture_problem(directory, publishing=False):
    reason = jsonl_complete(OUTPUT)(directory)
    if reason:
        return reason
    rows = [json.loads(line) for line in (Path(directory)/OUTPUT).read_text().splitlines() if line.strip()]
    values = {row['k']: row['v'] for row in rows}
    if 'fatal' in values:
        return str(values['fatal'])
    if len(values) != len(rows):
        return 'duplicate keys'
    if DEVICE in BYTE_DEVICES:
        return byte_capture_problem(values)
    if publishing and any(key.startswith(BYTE_CASE + '.') for key in values):
        return 'R16 byte records belong in a dedicated Linux profile'
    return None


def byte_capture_problem(values):
    missing = sorted(BYTE_RECORDS - values.keys())
    extra = sorted(values.keys() - BYTE_RECORDS)
    if missing or extra:
        return f'R16 byte profile records differ (missing {missing}, extra {extra})'
    if values[BYTE_CASE + '.device'] != BYTE_DEVICES[DEVICE]:
        return f'the capture did not record the {BYTE_DEVICES[DEVICE]} device'
    if values[BYTE_CASE + '.release'] != unity_version():
        return 'the capture did not record the requested Unity release'
    invalid = [f'{key} did not create a render texture'
               for key in sorted(BYTE_CREATED) if values[key] is not True]
    for key, size in BYTE_PAYLOAD_BYTES.items():
        payload = values[key]
        try:
            valid = isinstance(payload, str) and len(payload) == 2 * size and len(bytes.fromhex(payload)) == size
        except ValueError:
            valid = False
        if not valid:
            invalid.append(f'{key} did not read {size} bytes as hexadecimal')
    return 'invalid R16 byte results: ' + ', '.join(invalid) if invalid else None


def editor(work, release):
    if DEVICE == 'metal':
        codes = []
        for repeat in REPEATS:
            project, out = work/'projects'/repeat, work/repeat
            copy_project(project, release)
            fresh_output(out)
            codes.append(macos_editor.capture(project, out, ['-executeMethod', 'TexFormatsRunner.Record'],
                                              {'TEXTURE_FORMATS_PROBE_OUT': str(out/OUTPUT),
                                               'TEXTURE_FORMATS_PROBE_ONLY': METAL_CASES}, timeout=900))
            print(repeat, 'exit', codes[-1], capture_problem(out) or 'complete')
        return int(any(codes))
    jobs = []
    cases = device_cases()
    editor_args = ['-executeMethod', 'TexFormatsRunner.Record']
    if DEVICE == 'opengl-r16-byte':
        editor_args.append('-force-glcore')
    for repeat in REPEATS:
        project, out = work/'projects'/repeat, work/repeat
        copy_project(project, release)
        fresh_output(out)
        jobs.append(EditorJob(project, out/'editor.log', out, args=editor_args,
                              batch=True, graphics=True, vulkan=DEVICE in VULKAN_DEVICES,
                              env={'TEXTURE_FORMATS_PROBE_OUT': str(out/OUTPUT),
                                   **({'TEXTURE_FORMATS_PROBE_ONLY': cases} if cases else {})},
                              timeout=900, shutdown_grace=120, progress_files=[out/OUTPUT], progress_timeout=600,
                              label='textureformats-'+repeat))
    results = [run(job) for job in jobs] if DEVICE in VULKAN_DEVICES else run_parallel(jobs)
    for result in results:
        print(summary(result))
    problems = [capture_problem(work/repeat) for repeat in REPEATS]
    for repeat, problem in zip(REPEATS, problems):
        print(repeat, problem or 'complete')
    return int(any(r['exit_code'] != 0 or r.get('forced_exit') for r in results)
               or (DEVICE in BYTE_DEVICES and any(problems)))


def publish(work, append=False):
    def reference_capture_problem(directory):
        return capture_problem(directory, publishing=True)

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
    target = reference()
    if DEVICE == 'metal':
        if append:
            raise SystemExit('the Metal reference is published whole')
        for repeat in REPEATS:
            problem = reference_capture_problem(work/repeat)
            if problem:
                raise SystemExit(f'{repeat}: {problem}; not publishing')
        code = macos_editor.publish([work/r for r in REPEATS], target, OUTPUT)
        print('published', target)
        return code
    if append:
        # The repeat validator refuses overwriting a reference. Validate into a private candidate,
        # then append only new keys while keeping every earlier record byte for byte.
        candidate = work/'append-candidate.jsonl'
        code = publish_repeats([work/r for r in REPEATS], candidate, lambda d: [OUTPUT],
                              lambda path, first: shutil.copyfile(first/OUTPUT, path),
                              complete=[reference_capture_problem], gpu=True)
        if code: return code
        write(target, work/REPEATS[0])
        return 0
    return publish_repeats([work/r for r in REPEATS], target, lambda d: [OUTPUT], write,
                           complete=[reference_capture_problem], gpu=True)


def unbound(work, release, only=None):
    cli = os.environ.get('UNBOUND_CLI', str(REPO/'target/release/unbound'))
    project = copy_project(work/'projects'/'unbound', release)
    out = fresh_output(work/'unbound')
    env = dict(os.environ, TEXTURE_FORMATS_PROBE_OUT=str(out/OUTPUT))
    if only is not None:
        env['TEXTURE_FORMATS_PROBE_ONLY'] = only
    with (out/'unbound.log').open('w') as log:
        result = subprocess.run([str(LAUNCHER), '--kind', 'engine-gpu', 'timeout', '600', cli, 'run', '-p', str(project),
                                 '--no-watch', '--frames', '30', '--size', '64x64',
                                 *([] if MACOS else ['--force-'+('vulkan' if DEVICE in VULKAN_DEVICES else 'glcore')])],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    print('unbound exit', result.returncode, out)
    if result.returncode == 0:
        reason = capture_problem(out)
        if reason:
            print('incomplete:', reason)
            return 1
    return result.returncode


def verify(work, release, cases):
    only = device_cases() or (None if cases == 'all' else SUPPORTED)
    code = unbound(work, release, only)
    if code:
        return code
    command = [sys.executable, str(PROBE/'compare.py'), str(reference()),
               str(work/'unbound'/OUTPUT), '--report', str(work/'report.json')]
    residual_file = residuals()
    if residual_file is not None:
        command.extend(['--residuals', str(residual_file)])
    if only and DEVICE not in BYTE_DEVICES and DEVICE not in RESOLVE_DEVICES:
        command.extend(['--only', only])
    return subprocess.run(command).returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('editor', 'publish', 'unbound', 'verify'))
    parser.add_argument('--cases', choices=('supported', 'all'), default='supported', help='verify: which cases')
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    parser.add_argument('--device', choices=('opengl', *RESOLVE_DEVICES, 'vulkan', 'metal', *BYTE_DEVICES), default='opengl')
    parser.add_argument('--append', action='store_true', help='publish: append new keys, preserving every existing record')
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    global DEVICE
    DEVICE = args.device
    if DEVICE in BYTE_DEVICES and sys.platform != 'linux':
        if args.mode == 'verify':
            print('textureformats: the R16 byte profiles require Linux')
            return 0
        parser.error('the R16 byte profiles record Linux OpenGL Core or Vulkan')
    if DEVICE in RESOLVE_DEVICES and sys.platform != 'linux':
        if args.mode == 'verify':
            print('textureformats: OpenGL Core resolve profile requires Linux')
            return 0
        parser.error('the OpenGL Core resolve profile runs on Linux')
    if MACOS and DEVICE != 'metal' and args.mode not in ('unbound', 'verify'):
        parser.error(f'the {DEVICE} Editor runs on Linux; on macOS record --device metal')
    if MACOS and DEVICE == 'vulkan':
        print('textureformats: macOS has no Vulkan device; checking the Metal reference instead')
        DEVICE = 'metal'
    if DEVICE == 'metal' and not MACOS:
        parser.error('--device metal runs on macOS arm64 only')
    if (DEVICE in ('vulkan', 'metal') or DEVICE in BYTE_DEVICES) and args.cases == 'all':
        parser.error(f'the {DEVICE} reference covers its own cases; --cases all is not recorded')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    if args.mode == 'editor':
        return editor(work, args.release)
    if args.mode == 'unbound':
        return unbound(work, args.release, device_cases() or os.environ.get('TEXTURE_FORMATS_PROBE_ONLY'))
    if args.mode == 'verify':
        return verify(work, args.release, args.cases)
    return publish(work, args.append)


if __name__ == '__main__':
    sys.exit(main())
