#!/usr/bin/env python3
"""Record Texture2D memory before and after reinitialization on each graphics device."""
import argparse
import hashlib
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
CAPTURE_SEAL = 'capture.sha256'


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


def capture_fingerprints(root, device):
    capture = root/'capture'
    name = value_probe.editor_runner.RESULT
    values = capture/value_probe.VALUES
    launch = root/name
    if any(path.resolve() != path for path in (capture, capture/name, values, launch, root/CAPTURE_SEAL)):
        raise SystemExit(f'{root}: capture files must stay in their run directory')
    for path in (values, launch, capture/name, root/CAPTURE_SEAL):
        if path.exists() and path.stat().st_nlink != 1:
            raise SystemExit(f'{path}: capture file must have a single filesystem link')
    rows = value_probe.read_values(values)
    expected_device = {'opengl': 'OpenGLCore', 'metal': 'Metal'}[device]
    if rows.get('device') != expected_device or rows.get('unity') != unity_version():
        raise SystemExit(f'{root}: expected a {expected_device} Editor capture for {unity_version()}')
    return [hashlib.sha256(path.read_bytes()).hexdigest() for path in (launch, values)]


def publish(labels, device):
    roots = [directory(label, device) for label in labels]
    if len(roots) < 2 or len(roots) != len(set(roots)):
        raise SystemExit('publish requires at least two distinct Editor runs')
    launches = set()
    for root in roots:
        try:
            fingerprints = capture_fingerprints(root, device)
            if fingerprints != (root/CAPTURE_SEAL).read_text().splitlines():
                raise SystemExit(f'{root}: captured values or launch metadata changed after recording')
        except OSError as error:
            raise SystemExit(f'{root}: missing or invalid Editor record: {error}') from error
        launch = fingerprints[0]
        if launch in launches:
            raise SystemExit(f'{root}: duplicate Editor launch record')
        launches.add(launch)
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
        with (out/CAPTURE_SEAL).open('x') as seal:
            seal.write('\n'.join(capture_fingerprints(out, device)) + '\n')
        return 0

    engine_args = ['--headless', '12']
    if device == 'opengl':
        engine_args.append('--force-glcore')
    return value_probe.gate(PROBE, CAPTURE, str(out), 0, engine_args, base=BASE, configure=include_image_conversion,
                            profile='opengl' if device == 'opengl' else None)


if __name__ == '__main__':
    sys.exit(main())
