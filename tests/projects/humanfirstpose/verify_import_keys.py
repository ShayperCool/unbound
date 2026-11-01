#!/usr/bin/env python3
"""Verify an import metadata change while retaining both complete comparison unions.

The Native comparison still contains every float/bit residual. This control only
accepts changes from the supplied before capture in imported key weightedMode;
all other values, including float decimals and bits, must remain identical.
"""
import argparse
import json
from pathlib import Path

from compare import compare, load, validate
from probe_paths import cache_artifact_path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--release', required=True)
    parser.add_argument('--before', required=True, type=Path)
    parser.add_argument('--actual', required=True, type=Path)
    parser.add_argument('--json', required=True, type=Path)
    args = parser.parse_args()
    cache_artifact_path(args.json)
    reference = load(Path(__file__).parent / 'Reference' / ('unity-' + args.release + '.jsonl'))
    before, actual = load(args.before), load(args.actual)
    native = compare(reference, actual)
    changes = compare(before, actual)
    errors = []
    for name, capture in [('reference', reference), ('before', before), ('actual', actual)]:
        try:
            validate(capture)
        except (ValueError, KeyError, TypeError) as error:
            errors.append({'capture': name, 'error': str(error)})
    metadata = [row for row in native['union'] if row['field'].endswith('.weightedMode')]
    if len(metadata) != 217 or any(not row['exact'] for row in metadata):
        errors.append({'error': 'expected all 217 imported key weighted modes to match Native'})
    unexpected = [row for row in changes['union']
                  if not row['field'].endswith('.weightedMode')
                  and (row['status'] in ('missing', 'extra') or row['reference'] != row['actual'])]
    if unexpected:
        errors.append({'error': 'values other than key weightedMode changed', 'fields': unexpected})
    report = {'native': native, 'before_after': changes, 'errors': errors}
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(report, indent=2, allow_nan=False) + '\n')
    print(json.dumps({'metadata_fields': len(metadata), 'metadata_changes': len(changes['differences']),
                      'native_exact_residuals': len(native['differences']),
                      'native_tolerance_failures': len(native['tolerance_failures']), 'errors': errors}))
    return int(bool(errors))


if __name__ == '__main__':
    raise SystemExit(main())
