#!/usr/bin/env python3
"""Record preview object preparation in Unity and verify both Editor modes."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402
from probe_paths import private_label  # noqa: E402

BASE = [(PROBE.parent/'imguiskin', ('Assets/Scenes', 'Assets/Editor')),
        (PROBE.parent/'analyticssession', ('ProjectSettings',))]
VARIABLE = 'PREVIEW_INIT_OUT'
ENTRIES = {'edit': 'PreviewInitProbe.RecordEdit', 'play': 'ImguiSkinRunner.Record'}
ORDER_RESIDUALS = {}
for pipeline in ('builtin', 'srp'):
    for key, stem in (('callbacks', 'Preview'), ('directFlagsCallbacks', 'Plain')):
        root, child = stem+'Root', stem+'Child'
        ORDER_RESIDUALS[f'{pipeline}.{key}'] = (
            f'{child}:disable,{root}:disable,{root}:enable,{child}:enable',
            f'{root}:disable,{child}:disable,{root}:enable,{child}:enable',
        )


def gate(profile, label):
    out = value_probe.run_root(PROBE, label)
    project = value_probe.assemble(PROBE, out, BASE)
    capture = out/'engine'
    capture.mkdir(parents=True)
    cli = value_probe.engine_cli()
    if profile == 'edit':
        command = [cli, 'test', '-p', str(project), '--platform', 'EditMode', '--results', str(out/'results.xml')]
    else:
        command = [cli, 'run', '-p', str(project), '--no-watch', '--headless', '120']
    env = {**os.environ, VARIABLE: str(capture)}
    with (out/'engine.log').open('w') as log:
        code = subprocess.run([str(value_probe.LAUNCHER), '--kind', 'engine-cpu', 'timeout', '240', *command],
                              stdout=log, stderr=subprocess.STDOUT, env=env).returncode
    expected = value_probe.read_values(value_probe.reference_dir(PROBE, profile=profile)/value_probe.VALUES)
    try:
        actual = value_probe.read_values(capture/value_probe.VALUES)
    except (OSError, ValueError) as error:
        print(f'previewinit {profile}: no complete engine capture ({error}); engine exit {code}; log {out}/engine.log')
        return 1
    matched, failures = value_probe.compare(expected, actual, 0)
    residuals = 0
    for failure in failures:
        key = failure['key']
        allowed = ORDER_RESIDUALS.get(key)
        if allowed and failure == {'key': key, 'expected': allowed[0], 'actual': allowed[1]}:
            failure['residual'] = 'The existing hideFlags path disables the root before its child'
            residuals += 1
        print(json.dumps(failure))
    errors = len(failures) - residuals
    print(f'previewinit {profile}: {matched}/{len(expected)} values match, {errors} failures, {residuals} known residuals; engine exit {code}; logs {out}')
    return int(errors != 0 or code != 0)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=private_label())
    parser.add_argument('--profile', choices=ENTRIES)
    args = parser.parse_intermixed_args()
    if any(Path(label).name != label or label in ('.', '..') for label in (args.label, *args.labels)):
        parser.error('labels must be single directory names')
    if args.mode in ('record', 'publish') and args.profile is None:
        parser.error('select the recorded profile')
    if args.mode == 'record':
        value_probe.record(PROBE, ENTRIES[args.profile], VARIABLE, args.label, batch=True, base=BASE)
        value_probe.seal_value_capture(value_probe.run_root(PROBE, args.label).resolve())
        return 0
    if args.mode == 'publish':
        roots = [value_probe.run_root(PROBE, label).resolve() for label in args.labels]
        value_probe.verify_sealed_value_repeats(roots)
        for root in roots:
            values = value_probe.read_values(root/'capture'/value_probe.VALUES)
            if values.get('editor.isPlaying') != str(args.profile == 'play'):
                parser.error(f'{root.name} records a different play mode')
        return value_probe.publish(PROBE, args.labels, profile=args.profile)
    profiles = [args.profile] if args.profile else list(ENTRIES)
    return int(any([gate(profile, f'{args.label}-{profile}') for profile in profiles]))


if __name__ == '__main__':
    sys.exit(main())
