#!/usr/bin/env python3
"""Compare all Editor API rows, with exact, named follow-ups that must not drift."""
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from playmode_api_probe import compare, main


def compare_with_gaps(reference, actual, report):
    compare(reference, actual, report)
    result = json.loads(report.read_text())
    gaps = json.loads(reference.with_name('known-gaps.json').read_text())
    by_key = {gap['key']: gap for gap in gaps}
    if len(by_key) != len(gaps) or any(not gap.get('reason', '').strip() for gap in gaps):
        raise ValueError('known gaps require unique keys and nonempty reasons')
    queued, unexpected = [], []
    for failure in result['failures']:
        gap = by_key.get(failure['key'])
        if gap and all(failure[field] == gap[field] for field in ('reference', 'actual')):
            queued.append(dict(failure, reason=gap['reason']))
        else:
            unexpected.append(failure)
    queued_keys = {row['key'] for row in queued}
    stale = [gap for gap in gaps if gap['key'] not in queued_keys]
    result.update(queued=queued, unexpected=unexpected, stale=stale)
    report.write_text(json.dumps(result, indent=2)+'\n')
    print(f"{result['matched']}/{result['reference']} exact API rows; {len(queued)} queued differences; "
          f"{len(unexpected)} unexpected; {len(stale)} stale")
    return int(bool(result['capture_problem'] or unexpected or stale))


if __name__ == '__main__':
    sys.exit(main(Path(__file__).resolve().parent, 'EDITORAPI_PROBE_OUT', compare_rows=compare_with_gaps))
