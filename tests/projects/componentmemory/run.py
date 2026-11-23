#!/usr/bin/env python3
"""Record and compare native allocation sizes of attached components."""
import argparse
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import inside_git_checkout, private_label, run_root  # noqa: E402
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = (PROBE.parent/'formatmapping', ['Assets/Scenes', 'Assets/Scenes.meta', 'Packages', 'ProjectSettings'])
AUDIO_BASE = [BASE, (PROBE/'Fixtures/audio', ['Assets/Scenes/Probe.unity'])]
RENDERER_BASE = [BASE, (PROBE/'Fixtures/renderer', ['Assets/Scenes/Probe.unity', 'Assets/Resources'])]
TRANSFORM_BASE = [BASE, (PROBE/'Fixtures/transform', ['Assets/Scenes/Probe.unity'])]
PROFILES = {
    'fixed': ('RecordComponentMemory.Record', 'COMPONENT_MEMORY_OUT', None, BASE),
    'light': ('RecordLightAllocation.Record', 'LIGHT_ALLOCATION_OUT', 'light', BASE),
    'audio': ('RecordAudioSourceMemory.Record', 'AUDIO_SOURCE_MEMORY_OUT', 'audio', AUDIO_BASE),
    'renderer': ('RecordMeshRendererMemory.Record', 'MESH_RENDERER_MEMORY_OUT', 'renderer', RENDERER_BASE),
    'transform': ('RecordTransformMemory.Record', 'TRANSFORM_MEMORY_OUT', 'transform', TRANSFORM_BASE),
    'camera': ('RecordCameraMemory.Record', 'CAMERA_MEMORY_OUT', 'camera', BASE),
}
CAMERA_DEVICES = {'metal': 'Metal', 'opengl': 'OpenGLCore', 'vulkan': 'Vulkan'}
CAMERA_ENGINE_FLAGS = {'metal': (), 'opengl': ('--force-glcore',), 'vulkan': ('--force-vulkan',)}


def directory(label, profile):
    if not label or label in ('.', '..') or Path(label).name != label:
        raise SystemExit(f'{label}: expected a single run directory name')
    namespace = PROBE.name if profile is None else f'{PROBE.name}-{profile}'
    base = run_root(namespace, '').resolve()
    out = base/label
    if out.resolve() != out or inside_git_checkout(out):
        raise SystemExit(f'{out}: run directory must stay in the probe cache outside any checkout')
    return out


def validate_capture(root, release, method, device=None):
    rows = value_probe.read_values(root/'capture'/value_probe.VALUES)
    if rows.get('unity') != release:
        raise SystemExit(f'{root}: expected Editor capture for {release}')
    if device is not None and rows.get('device') != device:
        raise SystemExit(f'{root}: expected Editor capture on {device}')
    receipt = value_probe.editor_runner.load_result(root) or {}
    command = receipt.get('command')
    if not isinstance(command, list) or ('-executeMethod', method) not in zip(command, command[1:]):
        raise SystemExit(f'{root}: expected Editor capture from {method}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--release', required=True)
    parser.add_argument('--label')
    parser.add_argument('--profile', choices=PROFILES, default='fixed')
    parser.add_argument('--backend', choices=CAMERA_DEVICES, help='Camera capture device')
    args = parser.parse_intermixed_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    method, variable, profile, base = PROFILES[args.profile]
    backends = (None,)
    if args.profile == 'camera':
        local_backends = ('metal',) if sys.platform == 'darwin' else ('opengl', 'vulkan')
        if args.backend is None:
            backends = local_backends if args.mode == 'gate' else local_backends[:1]
        else:
            if args.backend not in local_backends and args.mode != 'publish':
                parser.error(f'camera {args.backend} requires a host with that backend')
            backends = (args.backend,)
    elif args.backend is not None:
        parser.error('--backend applies to the camera profile')
    if args.mode != 'publish' and args.labels:
        parser.error('use --label for record and gate')
    label = private_label() if args.label is None else args.label
    status = 0
    for backend in backends:
        device = CAMERA_DEVICES.get(backend)
        selected_profile = f'{profile}-{backend}' if backend else profile
        if args.mode == 'publish':
            roots = [directory(name, selected_profile) for name in args.labels]
            value_probe.verify_sealed_value_repeats(roots)
            for root in roots:
                validate_capture(root, args.release, method, device)
            return value_probe.publish(PROBE, [str(root) for root in roots], profile=selected_profile)
        out = directory(label, selected_profile)
        if out.exists():
            raise SystemExit(f'{out}: expected a fresh run directory')
        if args.mode == 'record':
            value_probe.record(PROBE, method, variable, str(out), batch=True, macos_batch=True, base=base,
                               timeout=240 if backend else value_probe.RECORD_TIMEOUT,
                               device='vulkan' if backend == 'vulkan' else None)
            validate_capture(out, args.release, method, device)
            value_probe.seal_value_capture(out)
            return 0
        engine_args = ['--headless', '8', *CAMERA_ENGINE_FLAGS.get(backend, ())]
        status = max(status, value_probe.gate(PROBE, variable, str(out), 0, engine_args, base=base,
                                             profile=selected_profile))
    return status


if __name__ == '__main__':
    sys.exit(main())
