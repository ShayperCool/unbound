#!/usr/bin/env python3
"""SerializedObject over TextureImporter: record in two Editors, publish, compare Unbound; see README.md.

`create` (authoring) slices the probe's textures in a batch Editor run on a copy and copies the
`.meta` files the Editor wrote back into the probe."""
import json
from pathlib import Path
import shutil
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import editor_runner  # noqa: E402
from playmode_api_probe import compare, main, stage  # noqa: E402
from probe_paths import private_label, run_root  # noqa: E402


def compare_with_gaps(reference, actual, report):
    """Every difference must be a named gap with the same reference and actual value."""
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
    print(f"{result['matched']}/{result['reference']} exact rows; {len(queued)} queued differences; "
          f"{len(unexpected)} unexpected; {len(stale)} stale")
    return int(bool(result['capture_problem'] or unexpected or stale))


def create(release):
    work = run_root(PROBE, private_label('create'))
    project = stage(PROBE, work/'project', release)
    for meta in (project/'Assets').rglob('*.meta'):
        meta.unlink()
    job = editor_runner.EditorJob(project, work/'create.log', work, ['-quit', '-executeMethod', 'ImporterSerializedProbe.Create'],
                                  batch=True, graphics=False, timeout=900, label='importerserialized-create')
    result = editor_runner.run(job)
    print(editor_runner.summary(result))
    if editor_runner.result_problem(work, gpu=False):
        return 1
    for meta in [*sorted((project/'Assets').rglob('*.meta')), project/'Packages/packages-lock.json']:
        target = PROBE/meta.relative_to(project)
        shutil.copyfile(meta, target)
        print('wrote', target.relative_to(PROBE))
    return 0


if __name__ == '__main__':
    if sys.argv[1:2] == ['create']:
        release = sys.argv[sys.argv.index('--release') + 1] if '--release' in sys.argv else '6000.0.71f1'
        sys.exit(create(release))
    sys.exit(main(PROBE, 'IMPORTER_SERIALIZED_OUT', compare_rows=compare_with_gaps))
