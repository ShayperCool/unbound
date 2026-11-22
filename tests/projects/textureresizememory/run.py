#!/usr/bin/env python3
"""Record Texture2D memory before and after reinitialization on each graphics device."""
import argparse
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import inside_git_checkout, private_label, run_root, unity_version  # noqa: E402
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = (PROBE.parent/'formatmapping', ['Assets/Scenes', 'Assets/Scenes.meta', 'Packages', 'ProjectSettings'])
CAPTURE = 'TEXTURE_RESIZE_MEMORY_OUT'
DEVICES = ('opengl', 'vulkan', 'metal')
HOST_DEVICES = ('metal',) if sys.platform == 'darwin' else ('opengl', 'vulkan')


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
    if len(roots) < 2 or len(roots) != len(set(roots)):
        raise SystemExit('publish requires at least two distinct Editor runs')
    launches = set()
    for root in roots:
        capture = root/'capture'
        name = value_probe.editor_runner.RESULT
        values = capture/value_probe.VALUES
        paths = (capture, capture/name, values, root/name)
        if any(path.resolve() != path for path in paths):
            raise SystemExit(f'{root}: capture files must stay in their run directory')
        if (capture/name).exists() and (capture/name).stat().st_nlink != 1:
            raise SystemExit(f'{capture/name}: capture metadata must have a single filesystem link')
        launch = (root/name).read_bytes()
        if launch in launches:
            raise SystemExit(f'{root}: duplicate Editor launch record')
        launches.add(launch)
        rows = value_probe.read_values(values)
        expected_device = {'opengl': 'OpenGLCore', 'vulkan': 'Vulkan', 'metal': 'Metal'}[device]
        if rows.get('device') != expected_device or rows.get('unity') != unity_version():
            raise SystemExit(f'{root}: expected a {expected_device} Editor capture for {unity_version()}')
    value_probe.METAL = device == 'metal'
    return value_probe.publish(PROBE, [str(root) for root in roots], device=device)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=None)
    parser.add_argument('--device', choices=DEVICES)
    args = parser.parse_intermixed_args()
    if args.mode != 'gate' and len(HOST_DEVICES) > 1 and not args.device:
        parser.error('record and publish require an explicit --device')
    devices = (args.device,) if args.device else HOST_DEVICES
    if args.mode == 'publish':
        return publish(args.labels, devices[0])
    if any(device not in HOST_DEVICES for device in devices):
        parser.error('select a device available on this host')
    if args.labels:
        parser.error('use --label for record and gate')
    label = private_label() if args.label is None else args.label
    statuses = []
    for device in devices:
        value_probe.METAL = device == 'metal'
        out = directory(label, device)
        if out.exists():
            raise SystemExit(f'{out}: expected a fresh run directory')
        if args.mode == 'record':
            value_probe.record(PROBE, 'RecordTextureResizeMemory.Record', CAPTURE, str(out), batch=True,
                               macos_batch=True, base=BASE, device=device)
            return 0
        statuses.append(value_probe.gate(PROBE, CAPTURE, str(out), 0, ['--headless', '12'],
                                         base=BASE, device=device))
    return max(statuses)


if __name__ == '__main__':
    sys.exit(main())
