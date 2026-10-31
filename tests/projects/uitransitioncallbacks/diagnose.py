#!/usr/bin/env python3
"""Group the complete ordered comparison by scenario and callback phase without filtering failures."""
import argparse
from collections import Counter
import json
from pathlib import Path
import sys

from compare import compare

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import cache_artifact_path


def context(rows, key):
    parts = key.strip('/').split('/')
    index = int(parts[0])
    if index >= len(rows):
        return {'sample': index, 'missing': True}
    row = rows[index]
    result = {k: row[k] for k in ('scenario', 'ms')}
    result['sample'] = index
    if len(parts) > 2 and parts[1] == 'events':
        event_index = int(parts[2])
        result['event'] = event_index
        if event_index < len(row['events']):
            event = row['events'][event_index]
            result.update({k: event[k] for k in ('kind', 'target', 'who', 'phase', 'properties')})
        else:
            result['missing_event'] = True
    return result


def diagnose(reference, candidate, before=None):
    report = compare(reference, candidate)
    rows = [json.loads(line) for line in Path(reference).read_text().splitlines()]
    actual = [json.loads(line) for line in Path(candidate).read_text().splitlines()]
    report['candidate_samples'] = len(actual)
    report['candidate_callbacks'] = sum(sum(not e['kind'].startswith('after-') for e in r['events']) for r in actual)
    previous = {f['key'] for f in compare(reference, before)['failures']} if before else None
    current = {f['key'] for f in report['failures']}
    if previous is not None:
        report['changes'] = {'resolved': sorted(previous - current), 'retained': sorted(previous & current), 'introduced': sorted(current - previous)}
    groups = {}
    for failure in report['failures']:
        expected = context(rows, failure['key'])
        observed = context(actual, failure['key'])
        scenario = expected.get('scenario', observed.get('scenario', '<extra sample>'))
        group = groups.setdefault(scenario, {'failed': 0, 'phases': Counter(), 'fields': Counter(), 'first_divergence': {'failure': failure, 'reference': expected, 'candidate': observed}})
        group['failed'] += 1
        group['phases'][expected.get('kind', '<sample>') + '/' + expected.get('phase', '<values>')] += 1
        parts = failure['key'].strip('/').split('/')
        field = '/'.join(parts[3:4] if len(parts) > 2 and parts[1] == 'events' else parts[1:2])
        group['fields'][field] += 1
        if previous is not None:
            status = 'retained' if failure['key'] in previous else 'introduced'
            group[status] = group.get(status, 0) + 1
    report['scenarios'] = groups
    return report


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--release', required=True)
    p.add_argument('--candidate', type=Path, required=True)
    p.add_argument('--before', type=Path)
    p.add_argument('--output', type=Path, required=True)
    args = p.parse_args()
    try:
        cache_artifact_path(args.output)
    except ValueError as error:
        p.error(str(error))
    reference = Path(__file__).resolve().parent / 'Reference' / f'unity-{args.release}.jsonl'
    report = diagnose(reference, args.candidate, args.before)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({k: v for k, v in report.items() if k not in ('failures', 'changes', 'scenarios')}))
    if 'changes' in report:
        print(json.dumps({k: len(v) for k, v in report['changes'].items()}))
    return int(report['failed'] != 0)


if __name__ == '__main__':
    raise SystemExit(main())
