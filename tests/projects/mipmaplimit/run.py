#!/usr/bin/env python3
"""Which mips the GPU samples under QualitySettings.globalTextureMipmapLimit; see README.md.

    run.py record --label L [--device vulkan] [--profile android]   one Editor capture
    run.py publish A B [--device vulkan] [--profile android]        two identical captures -> Reference/
    run.py gate [--label L]                                         the engine vs every reference
"""
import argparse
import os
from pathlib import Path
import sys
import time

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

VARIABLE = 'MIPMAP_LIMIT_OUT'
ENGINE_ARGS = ['--frames', '10', '--size', '96x64']
# profile: (Editor arguments, engine arguments); None is the Standalone target.
PROFILES = {
    None: ((), ()),
    'android': (('-buildTarget', 'Android'), ('--target', 'Android')),
}
# The references the gate compares: (device, profile).
REFERENCES = [(None, None), ('vulkan', None), (None, 'android')]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--device', choices=('vulkan',))
    parser.add_argument('--profile', choices=[p for p in PROFILES if p])
    args = parser.parse_args()
    label = args.label or time.strftime('check-%Y%m%d-%H%M%S-') + str(os.getpid())
    if args.mode == 'record':
        return value_probe.record(PROBE, 'ProbeSetup.Record', VARIABLE, label, True, device=args.device,
                                  editor_args=PROFILES[args.profile][0])
    if args.mode == 'publish':
        return value_probe.publish(PROBE, args.labels, device=args.device, profile=args.profile)
    codes = []
    for device, profile in REFERENCES:
        codes.append(value_probe.gate(PROBE, VARIABLE, f'{label}-{device or "opengl"}-{profile or "standalone"}', 0,
                                      [*ENGINE_ARGS, *PROFILES[profile][1]], device=device, profile=profile,
                                      residuals_file=f'residuals-{profile}.json' if profile else 'residuals.json'))
    return max(codes)


if __name__ == '__main__':
    sys.exit(main())
