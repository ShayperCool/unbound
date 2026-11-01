#!/usr/bin/env python3
"""Recording protocol controls; no Editor or engine. Usage: test_recording.py NEW_CACHE_OUT.
The managed fixture deadline tests live in AppFactsUrlRecordingTests; these check that
failed/incomplete fixture output cannot pass the runner and exact comparator.
"""
import json
from pathlib import Path
import sys
import compare
import run


def main():
    out = Path(sys.argv[1])
    out.mkdir(parents=True, exist_ok=False)
    reference = Path(__file__).parent/'Reference/unity-6000.0.71f1'
    rows = run.rows(reference/'values.jsonl')
    records = [r for r in rows if r['k'].startswith('openurl.') and r['k'].endswith('.runs')]
    assert len(records) == 9 and all(len(r['v']) == 1 for r in records)
    # The existing Native protocol is one opener, one tab, unchanged argv, one newline.
    assert all(r['v'][0].startswith('xdg-open\t') and '\n' not in r['v'][0] for r in records)
    for case in ('compatible', 'absent', 'late', 'incomplete'):
        capture = out/case
        capture.mkdir()
        actual = [dict(r) for r in rows]
        if case != 'compatible':
            actual = [r for r in actual if r['k'] != records[0]['k']]
            if case == 'late':
                actual.insert(-1, {'k': records[0]['k'], 'v': []})
            if case == 'incomplete':
                actual.insert(-1, {'k': 'fatal', 'v': 'recording stub did not complete'})
                actual[-1] = {'k': 'complete', 'v': False}
        (capture/'values.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in actual))
        (capture/'machine.jsonl').write_text('{}\n')
        problem = run.capture_problem(capture)
        result = compare.compare(reference, capture)
        if case == 'compatible':
            assert problem is None and result['matched'] == 555 and not result['failures']
        else:
            assert any(f['key'] == records[0]['k'] for f in result['failures']), result
            if case == 'incomplete': assert problem is not None
        (capture/'report.json').write_text(json.dumps({'runner_problem': problem, 'comparison': result}, indent=1)+'\n')
        print(case, 'accepted' if case == 'compatible' else 'rejected')
    return 0


if __name__ == '__main__':
    sys.exit(main())
