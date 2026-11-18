"""Strict default-state comparison, with exact, reasoned migration entries kept visible."""
import json
from pathlib import Path
import re

COMPLETE = {'key': 'complete', 'status': 'value', 'value': '1'}


def rows(path):
    result = {}
    records = [json.loads(line) for line in Path(path).read_text().splitlines()]
    if not records or records[-1] != COMPLETE or records.count(COMPLETE) != 1:
        raise ValueError(f'incomplete capture {path}')
    for row in records:
        if set(row) != {'key', 'status', 'value'} or not all(isinstance(v, str) for v in row.values()):
            raise ValueError(f'invalid row in {path}: {row}')
        key = row.pop('key')
        if key in result:
            raise ValueError(f'duplicate key {key} in {path}')
        result[key] = row
    return result


CONTROL_VALUES = {
    'FreshStateControl/Property/FreshStateControl/AChanges': '1',
    'FreshStateControl/Property/FreshStateControl/ZReads': '0',
    'FreshStateControl/Property/FreshStateControl/ZNested': 'NestedStateDerived{Answer=7;field=2}',
}


def validate_controls(capture):
    for key, value in CONTROL_VALUES.items():
        if capture.get(key) != {'status': 'value', 'value': value}:
            raise ValueError('recorder control failed: ' + key)


def entries(document, release):
    if document['release'] != release:
        raise ValueError(f"debt release {document['release']} does not match {release}")
    result = {}
    for entry in document['entries']:
        key = entry['key']
        if key in result or not entry.get('reason', '').strip():
            raise ValueError(f'duplicate entry or empty reason: {key}')
        result[key] = entry
    return result


def host_decided(document, release, reference, source):
    """The expected rows the host OS decides, read from the probe that measures each on both hosts.

    `source(probe, file, host)` is the path of `probe`'s reference `file` measured on the Linux
    Editor (host False) or on this host's Editor (host True). Each entry takes one field of the one
    record matching its `record` fields there. The Linux value must be this probe's reference row,
    so the source stands for it; the result maps each key to the value measured on this host."""
    if document['release'] != release:
        raise ValueError(f"host-decided release {document['release']} does not match {release}")
    result = {}
    for entry in document['entries']:
        key = entry['key']
        if key in result or not entry.get('reason', '').strip():
            raise ValueError(f'duplicate host-decided entry or empty reason: {key}')

        def measured(host):
            path = source(entry['probe'], entry['file'], host)
            found = [row for row in map(json.loads, Path(path).read_text().splitlines())
                     if all(row.get(k) == v for k, v in entry['record'].items())]
            if len(found) != 1:
                raise ValueError(f'{key}: {len(found)} records match {entry["record"]} in {path}')
            value = found[0]
            for part in entry['field']:
                if not isinstance(value, dict) or part not in value:
                    raise ValueError(f'{key}: no field {entry["field"]} in {path}')
                value = value[part]
            return {'status': 'value', 'value': entry['value'].format(value)}

        if reference.get(key) != measured(False):
            raise ValueError(f'{key}: the reference row {reference.get(key)} is not its Linux source value')
        result[key] = measured(True)
    return result


def matches(actual, entry, side):
    pattern = entry.get(side + 'Pattern')
    if pattern is not None:
        return actual is not None and actual['status'] == 'value' and re.fullmatch(pattern, actual['value']) is not None
    return actual == entry.get(side)


def compare(expected, actual, debt):
    differences, failures, used = [], [], set()
    for key in sorted(expected.keys() | actual.keys()):
        wanted, found = expected.get(key), actual.get(key)
        entry = debt.get(key)
        variable = entry is not None and 'expectedPattern' in entry
        if wanted == found and not variable:
            continue
        difference = {'key': key, 'expected': wanted, 'actual': found}
        differences.append(difference)
        if entry and matches(wanted, entry, 'expected') and matches(found, entry, 'actual'):
            difference['reason'] = entry['reason']
            used.add(key)
        else:
            failures.append(difference)
    stale = sorted(set(debt) - used)
    return {'matched': sum(expected.get(k) == actual.get(k) for k in expected.keys() & actual.keys()),
            'differences': differences, 'failures': failures, 'stale': stale,
            'missing': sorted(expected.keys() - actual.keys()), 'extra': sorted(actual.keys() - expected.keys())}


def check_constructor_stores(report, debt):
    actual = {row['constructor'] + '/' + row['field']: row for row in report['differences']}
    accepted = {}
    for row in debt:
        key = row['constructor'] + '/' + row['field']
        if key in accepted or not row.get('reason', '').strip():
            raise ValueError('invalid constructor-store migration entry: ' + key)
        accepted[key] = {k: v for k, v in row.items() if k != 'reason'}
    return [dict(key=key, expected=accepted.get(key), actual=actual.get(key))
            for key in sorted(actual.keys() | accepted.keys()) if actual.get(key) != accepted.get(key)]
