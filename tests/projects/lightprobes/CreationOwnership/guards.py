#!/usr/bin/env python3
"""Synthetic controls for the creation-ownership gate (no Editor, no engine).

    guards.py NEW_DIR

Builds fake capture directories (editor-run.json + witness output + raw rows) under NEW_DIR and
checks that run.py's creation_problem accepts a well-formed one and rejects every damaged one:
incomplete, duplicate, error row, wrong source, forced stop, missing ownership row, marker not
last (and a few neighbours). Also checks the publication gate on identical and differing pairs.
Exit 0 only if every expectation holds; NEW_DIR/guards.json lists each case and its reason.
"""
import copy
import json
from pathlib import Path
import sys

SUITE = Path(__file__).resolve().parent
sys.path.insert(0, str(SUITE.parent))
import run  # noqa: E402
import editor_runner  # noqa: E402

SCHEMA = run.creation_schema()
GOOD_RESULT = {'format': 1, 'exit_code': 0, 'forced_exit': None, 'renderer': 'NVIDIA GeForce RTX 4060/PCIe/SSE2',
               'require_gpu': True, 'shutdown_logged': True}
MARKER = {'k': 'complete', 'v': True}


def object_state(alias, created):
    if not created:
        return {'created': False}
    state = {'created': True, 'alive': True}
    for field in SCHEMA['ownership_fields']:
        state[field] = 'x'
    return state


def good_rows():
    source = {'k': 'source', 'suite': SCHEMA['suite'], 'schema': SCHEMA['schema'],
              'release': SCHEMA['release'], 'aliases': SCHEMA['aliases']}
    body = []
    for point in SCHEMA['points']:
        body.append({'kind': 'lifecycle', 'at': 'Awake', 'alias': 'i-new'})
        state = {field: 'x' for field in SCHEMA['state_fields']}
        state['objects'] = {alias: object_state(alias, alias != 'iv-ondestroy') for alias in SCHEMA['aliases']}
        body.append({'kind': 'snapshot', 'point': point, 'at': point, 'state': state})
    body.append({'kind': 'snapshot', 'point': None, 'at': 'unlabelled', 'state': {}})
    for index, row in enumerate(body):
        row['seq'] = index
    return [source]+body+[MARKER]


def raw_rows(rows):
    return [{'seq': row['seq'], 'findByTypeRaw': []} for row in rows if row.get('kind') == 'snapshot']


def write_capture(directory, rows, raw=None, result=GOOD_RESULT, text=None):
    directory.mkdir(parents=True)
    (directory/editor_runner.RESULT).write_text(json.dumps(result))
    out = directory/SCHEMA['output']
    out.write_text(text if text is not None else ''.join(json.dumps(r)+'\n' for r in rows))
    if raw is not False:
        raw = raw_rows(rows) if raw is None else raw
        (directory/SCHEMA['raw_output']).write_text(''.join(json.dumps(r)+'\n' for r in raw))
    return directory


def cases():
    good = good_rows()
    yield 'good', True, good, {}

    yield 'incomplete (no marker)', False, good[:-1], {}
    yield 'incomplete (truncated line)', False, None, {'text': ''.join(json.dumps(r)+'\n' for r in good[:5])+'{"seq": 4, "ki'}

    rows = copy.deepcopy(good)
    rows.insert(3, copy.deepcopy(rows[2]))
    yield 'duplicate sequence row', False, rows, {'raw': raw_rows(good)}
    yield 'duplicate marker', False, good+[MARKER], {}
    rows = copy.deepcopy(good)
    point = next(r for r in rows if r.get('point') == 't1.b')
    dup = copy.deepcopy(point)
    rows.insert(-1, dup)
    for index, row in enumerate(rows[1:-1]):
        row['seq'] = index
    yield 'duplicate point', False, rows, {}

    rows = copy.deepcopy(good)
    rows[3] = {'seq': rows[3]['seq'], 'kind': 'error', 'at': 'sceneUnloaded handler', 'exception': 'X: y'}
    yield 'error row', False, rows, {}

    rows = copy.deepcopy(good)
    rows[0] = dict(rows[0], suite='lifecycle')
    yield 'wrong source (suite)', False, rows, {}
    rows = copy.deepcopy(good)
    rows[0] = dict(rows[0], release='6000.1.0f1')
    yield 'wrong source (release)', False, rows, {}
    rows = copy.deepcopy(good)
    rows[0] = {'seq': 0, 'stage': 'play', 'at': 'mark start', 'args': '', 'v': 'global probes0 18 18 73 73'}
    yield 'wrong source (lifecycle-shaped output)', False, rows, {}

    yield 'forced stop (watchdog)', False, good, {'result': dict(GOOD_RESULT, forced_exit='watchdog', exit_code=-9)}
    yield 'forced stop (shutdown guard)', False, good, {'result': dict(GOOD_RESULT, forced_exit='shutdown-guard')}
    yield 'nonzero exit', False, good, {'result': dict(GOOD_RESULT, exit_code=1)}
    yield 'wrong renderer', False, good, {'result': dict(GOOD_RESULT, renderer='llvmpipe (LLVM 17.0.6, 256 bits)')}

    rows = copy.deepcopy(good)
    rows = [r for r in rows if r.get('point') != 't2.a.made']
    for index, row in enumerate(rows[1:-1]):
        row['seq'] = index
    yield 'missing point row', False, rows, {}
    rows = copy.deepcopy(good)
    del next(r for r in rows if r.get('point') == 't1.c')['state']['objects']['iii-child']['created']
    yield 'missing ownership row (alias without created)', False, rows, {}
    rows = copy.deepcopy(good)
    del next(r for r in rows if r.get('point') == 't2.c')['state']['objects']['ii-prefab']['scene']
    yield 'missing ownership field (scene)', False, rows, {}
    rows = copy.deepcopy(good)
    del next(r for r in rows if r.get('point') == 't1.a.i')['state']['objects']['i-new']
    yield 'missing ownership row (alias absent)', False, rows, {}
    rows = copy.deepcopy(good)
    del next(r for r in rows if r.get('point') == 't3.c')['state']['lightProbes']
    yield 'missing state field (lightProbes)', False, rows, {}

    rows = copy.deepcopy(good)
    rows.insert(-2, rows.pop())
    yield 'marker not last', False, rows, {}

    yield 'raw rows missing', False, good, {'raw': False}
    yield 'raw rows short', False, good, {'raw': raw_rows(good)[:-1]}


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    work = Path(sys.argv[1])
    if work.exists():
        sys.exit(f'refusing to overwrite {work}')
    report, ok = [], True
    for index, (name, accept, rows, extra) in enumerate(cases()):
        options = {'raw': extra.get('raw'), 'result': extra.get('result', GOOD_RESULT), 'text': extra.get('text')}
        if rows is None:
            rows = good_rows()
            options['raw'] = raw_rows(rows)
        directory = write_capture(work/f'case{index:02d}', rows, **options)
        reason = run.creation_problem(directory)
        passed = (reason is None) == accept
        ok &= passed
        report.append({'case': name, 'dir': directory.name, 'expect': 'accept' if accept else 'reject',
                       'reason': reason, 'ok': passed})
        print(('ok  ' if passed else 'FAIL'), name, '->', reason or 'accepted')

    # The publication gate: identical good pair publishes once; differing pair and an existing target refuse.
    good = good_rows()
    a = write_capture(work/'pub-a', good)
    b = write_capture(work/'pub-b', good)
    other = copy.deepcopy(good)
    other[1]['at'] = 'Awake differs'
    c = write_capture(work/'pub-c', other)
    target = work/'published'/SCHEMA['output']
    target.parent.mkdir()

    def publish(dirs):
        try:
            return editor_runner.publish(dirs, target, lambda d: [SCHEMA['output']],
                                         lambda t, first: t.write_bytes((first/SCHEMA['output']).read_bytes()),
                                         complete=[run.creation_rows_problem], gpu=True), None
        except SystemExit as stop:
            return None, str(stop)
    for name, dirs, accept in (('publish differing pair', [a, c], False), ('publish identical pair', [a, b], True),
                               ('publish onto existing target', [a, b], False)):
        code, reason = publish(dirs)
        passed = (code == 0) == accept
        ok &= passed
        report.append({'case': name, 'expect': 'accept' if accept else 'reject', 'reason': reason, 'ok': passed})
        print(('ok  ' if passed else 'FAIL'), name, '->', reason or 'published')
    (work/'guards.json').write_text(json.dumps({'ok': ok, 'cases': report}, indent=1)+'\n')
    print('GUARDS OK' if ok else 'GUARDS FAILED')
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
