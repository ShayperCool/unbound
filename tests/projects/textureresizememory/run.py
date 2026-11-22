#!/usr/bin/env python3
"""Record Texture2D memory before and after reinitialization on each graphics device."""
import argparse
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import inside_git_checkout, private_label, run_root, unity_version  # noqa: E402
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = (PROBE.parent/'formatmapping', ['Assets/Scenes', 'Assets/Scenes.meta', 'Packages', 'ProjectSettings'])
CAPTURE = 'TEXTURE_RESIZE_MEMORY_OUT'
DEVICES = ('opengl', 'metal')
HOST_DEVICE = 'metal' if sys.platform == 'darwin' else 'opengl'


def include_image_conversion(project):
    manifest = project/'Packages/manifest.json'
    settings = json.loads(manifest.read_text())
    settings['dependencies']['com.unity.modules.imageconversion'] = '1.0.0'
    manifest.write_text(json.dumps(settings, indent=2) + '\n')


def directory(label, device):
    if not label or label in ('.', '..') or Path(label).name != label:
        raise SystemExit(f'{label}: expected a single run directory name')
    base = run_root(PROBE, '').resolve()
    out = base/f'{label}-{device}'
    if out.resolve() != out or inside_git_checkout(out):
        raise SystemExit(f'{out}: run directory must stay in the probe cache outside any checkout')
    return out


def publish(labels, device):
    roots = [directory(label, device) for label in labels]
    value_probe.verify_sealed_value_repeats(roots)
    for root in roots:
        rows = value_probe.read_values(root/'capture'/value_probe.VALUES)
        expected_device = {'opengl': 'OpenGLCore', 'metal': 'Metal'}[device]
        if rows.get('device') != expected_device or rows.get('unity') != unity_version():
            raise SystemExit(f'{root}: expected a {expected_device} Editor capture for {unity_version()}')
    value_probe.METAL = device == 'metal'
    return value_probe.publish(PROBE, [str(root) for root in roots],
                               profile='opengl' if device == 'opengl' else None)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=None)
    parser.add_argument('--device', choices=DEVICES)
    args = parser.parse_intermixed_args()
    device = args.device or HOST_DEVICE
    if args.mode == 'publish':
        return publish(args.labels, device)
    if device != HOST_DEVICE:
        parser.error('select a device available on this host')
    if args.labels:
        parser.error('use --label for record and gate')
    value_probe.METAL = device == 'metal'
    label = private_label() if args.label is None else args.label
    out = directory(label, device)
    if out.exists():
        raise SystemExit(f'{out}: expected a fresh run directory')
    if args.mode == 'record':
        value_probe.record(PROBE, 'RecordTextureResizeMemory.Record', CAPTURE, str(out), batch=True,
                           macos_batch=True, base=BASE, configure=include_image_conversion)
        value_probe.seal_value_capture(out)
        return 0

    engine_args = ['--headless', '12']
    if device == 'opengl':
        engine_args.append('--force-glcore')
    return value_probe.gate(PROBE, CAPTURE, str(out), 0, engine_args, base=BASE, configure=include_image_conversion,
                            profile='opengl' if device == 'opengl' else None)


if __name__ == '__main__':
    sys.exit(main())
