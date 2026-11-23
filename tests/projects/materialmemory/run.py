#!/usr/bin/env python3
"""Record the runtime material property storage layout."""
import argparse
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import inside_git_checkout, private_label, run_root  # noqa: E402
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = (PROBE.parent/'formatmapping', ['Assets/Scenes', 'Assets/Scenes.meta', 'Packages', 'ProjectSettings'])
METHOD = 'RecordMaterialMemory.Record'
VARIABLE = 'MATERIAL_MEMORY_OUT'
DEVICES = {'metal': ('Metal', ()), 'opengl': ('OpenGLCore', ('--force-glcore',)),
           'vulkan': ('Vulkan', ('--force-vulkan',))}


def directory(label, backend):
    if not label or label in ('.', '..') or Path(label).name != label:
        raise SystemExit(f'{label}: expected a single run directory name')
    out = run_root(f'{PROBE.name}-{backend}', '').resolve()/label
    if out.resolve() != out or inside_git_checkout(out):
        raise SystemExit(f'{out}: run directory must stay in the probe cache outside any checkout')
    return out


def validate_capture(root, release, device):
    rows = value_probe.read_values(root/'capture'/value_probe.VALUES)
    if rows.get('unity') != release or rows.get('device') != device:
        raise SystemExit(f'{root}: expected an Editor capture for {release} on {device}')
    command = (value_probe.editor_runner.load_result(root) or {}).get('command')
    if not isinstance(command, list) or ('-executeMethod', METHOD) not in zip(command, command[1:]):
        raise SystemExit(f'{root}: expected an Editor capture from {METHOD}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--release', required=True)
    parser.add_argument('--backend', choices=DEVICES)
    parser.add_argument('--label')
    args = parser.parse_intermixed_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    local = ('metal',) if sys.platform == 'darwin' else ('opengl', 'vulkan')
    if args.backend and args.backend not in local and args.mode != 'publish':
        parser.error(f'{args.backend} requires a host with that backend')
    if args.mode != 'publish' and args.labels:
        parser.error('use --label for record and gate')
    backends = (args.backend,) if args.backend else local if args.mode == 'gate' else local[:1]
    label = private_label() if args.label is None else args.label
    status = 0
    for backend in backends:
        device, flags = DEVICES[backend]
        if args.mode == 'publish':
            roots = [directory(name, backend) for name in args.labels]
            value_probe.verify_sealed_value_repeats(roots)
            for root in roots:
                validate_capture(root, args.release, device)
            return value_probe.publish(PROBE, [str(root) for root in roots], profile=backend)

        out = directory(label, backend)
        if out.exists():
            raise SystemExit(f'{out}: expected a fresh run directory')
        if args.mode == 'record':
            value_probe.record(PROBE, METHOD, VARIABLE, str(out), batch=True, macos_batch=True, base=BASE,
                               device='vulkan' if backend == 'vulkan' else None)
            validate_capture(out, args.release, device)
            value_probe.seal_value_capture(out)
            return 0

        status = max(status, value_probe.gate(PROBE, VARIABLE, str(out), 0, ['--headless', '8', *flags],
                                             base=BASE, profile=backend))
    return status


if __name__ == '__main__':
    sys.exit(main())
