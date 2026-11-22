#!/usr/bin/env python3
"""Profile the first software-decoded ASTC image and later allocations."""
import argparse
import os
from pathlib import Path
import shutil
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import inside_git_checkout, private_label, run_root, unity_version  # noqa: E402
import value_probe  # noqa: E402

PROBE = Path(__file__).resolve().parent
BASE = (PROBE.parent/'formatmapping', ['Assets/Scenes', 'Assets/Scenes.meta', 'Packages', 'ProjectSettings'])
CAPTURE = 'ASTC_MEMORY_OUT'
PROFILES = ('Ordinary', 'Wide', 'CubeFirst', 'ResizeFirst', 'UninitializedFirst', 'CloneFirst',
            'ArrayFirst', 'VolumeFirst', 'HdrFirst', 'UninitializedApplyFirst', 'ImportFirst',
            'ArrayBeforeApply', 'CubeUninitialized', 'ArrayUninitialized', 'SwitchFootprint',
            'ExplicitGraphicsFormatFirst', 'HdrCloneFirst')


def include_astc_import(project):
    source = PROBE.parent/'texturecompression'/'Assets'/'Textures'/'OddASTC6NoMips.png'
    resources = project/'Assets'/'Resources'
    resources.mkdir()
    for suffix in ('', '.meta'):
        shutil.copyfile(str(source) + suffix, resources/('WorkspaceInput.png' + suffix))


def directory(label, profile):
    if not label or label in ('.', '..') or Path(label).name != label:
        raise SystemExit(f'{label}: expected a single run directory name')
    base = run_root(PROBE, '').resolve()
    out = base/f'{label}-{profile}'
    if out.resolve() != out or inside_git_checkout(out):
        raise SystemExit(f'{out}: run directory must stay in the probe cache outside a checkout')
    return out


def validate_capture(root, profile):
    rows = value_probe.read_values(root/'capture'/value_probe.VALUES)
    if (rows.get('device'), rows.get('unity'), rows.get('profile')) != ('OpenGLCore', unity_version(), profile):
        raise SystemExit(f'{root}: unexpected device, version or run profile')


def run(mode, label, profile):
    os.environ['ASTC_MEMORY_PROFILE'] = profile
    out = directory(label, profile)
    if out.exists():
        raise SystemExit(f'{out}: expected a fresh run directory')
    android = profile == 'ImportFirst'
    configure = include_astc_import if android else None
    if mode == 'record':
        editor_args = ('-buildTarget', 'Android') if android else ()
        value_probe.record(PROBE, 'RecordAstcMemory.Record', CAPTURE, str(out), batch=True, base=BASE,
                           configure=configure, editor_args=editor_args)
        validate_capture(out, profile)
        value_probe.seal_value_capture(out)
        return 0
    engine_args = ['--headless', '12', '--force-glcore']
    if android:
        engine_args += ['--target', 'Android']
    return value_probe.gate(PROBE, CAPTURE, str(out), 0, engine_args,
                            base=BASE, profile=profile, configure=configure,
                            residuals_file=f'residuals-{profile}.json')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--profile', choices=PROFILES)
    args = parser.parse_intermixed_args()
    if args.mode != 'gate' and args.profile is None:
        parser.error('record and publish require --profile')
    if args.mode == 'publish':
        roots = [directory(label, args.profile) for label in args.labels]
        value_probe.verify_sealed_value_repeats(roots)
        for root in roots:
            validate_capture(root, args.profile)
        return value_probe.publish(PROBE, [str(root) for root in roots], profile=args.profile)

    if args.labels:
        parser.error('use --label for record and gate')
    if sys.platform != 'linux':
        if args.mode == 'gate':
            print('textureastcmemory: skipped (the reference uses the Linux OpenGL Core device)')
            return 0
        parser.error('record requires the Linux OpenGL Core device')
    label = private_label() if args.label is None else args.label
    profiles = (args.profile,) if args.profile else PROFILES
    return max([run(args.mode, label, profile) for profile in profiles])


if __name__ == '__main__':
    sys.exit(main())
