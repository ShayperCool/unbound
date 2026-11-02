#!/usr/bin/env python3
"""Synthetic controls for the creation-ownership gate and contract (no Editor, no engine).

    guards.py NEW_DIR

Builds fake capture directories (editor-run.json + witness output + raw and teardown rows) under
NEW_DIR. The synthetic trace has the witness's real structure where the contract looks: setup
roots with teardown witnesses, the destroyer's OnDestroy snapshot (t1.a.iv), the outgoing
sceneUnloaded (t1.a.start), Unity's not-cleaned-up Error and the t2 cleanup block. Checks that
run.py's creation_problem accepts well-formed captures and rejects every damaged one (gate cases:
incomplete, duplicate, error row, wrong source, forced stop, missing rows/fields, marker not last;
contract cases: each predicate of the three families), and that creation_publish publishes a pair
that differs only inside the declared families and refuses every other pair.
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
FAMILIES = SCHEMA['contract']['families']
HEADER = FAMILIES['not-cleaned-up-error-names']['header']
MADE = FAMILIES['inter-object-cleanup-order']['aliases']
GOOD_RESULT = {'format': 1, 'exit_code': 0, 'forced_exit': None, 'renderer': 'NVIDIA GeForce RTX 4060/PCIe/SSE2',
               'require_gpu': True, 'shutdown_logged': True}
MARKER = {'k': 'complete', 'v': True}
AUTHORED = ['Main Camera', 'Sun', 'Cube', 'Light Probe Group']
NAMES = ['CreatedNew', 'CreatedInOnDestroy', 'CreationTiny(Clone)', 'CreatedChild']


def object_state(alias, created):
    if not created:
        return {'created': False}
    state = {'created': True, 'alive': True}
    for field in SCHEMA['ownership_fields']:
        state[field] = 'x'
    return state


def snapshot(point, at, args='', scenes='x'):
    state = {field: 'x' for field in SCHEMA['state_fields']}
    state['objects'] = {alias: object_state(alias, alias != 'iv-ondestroy') for alias in SCHEMA['aliases']}
    state['scenes'] = scenes
    return {'kind': 'snapshot', 'point': point, 'at': at, 'args': args, 'state': state}


def scene0(roots, count=None):
    return [{'ref': 'scene0', 'name': 'Main', 'roots': roots, 'rootCount': len(roots) if count is None else count},
            {'ref': 'scene1', 'name': 'DontDestroyOnLoad', 'roots': ['controller'], 'rootCount': 1}]


def trace(before=('Cube',), names=NAMES, cleanup=MADE, adjacent=True, iv_roots=None, iv_count=None, late=(),
          after_start=None, extra_in_block=False, swap_pair=None, extra_name=None):
    """(rows, teardown) of a well-formed schema-2 capture, varied only where the arguments say.
    `before`: authored roots destroyed before the destroyer; the rest after it (before
    t1.a.start) unless listed in `late` (destroyed after t1.a.start)."""
    setup_roots = ['name:'+n for n in AUTHORED]+['destroyer']
    so_far = ['name:'+n for n in before]
    body, marks = [], {}

    def add(row, mark=None):
        row = dict(row, stage=row.get('stage', stage[0]))
        body.append(row)
        if mark:
            marks[mark] = row
        return row
    stage = ['setup']
    add({'kind': 'create', 'at': 'attach teardown witnesses', 'result': ['name:'+n for n in AUTHORED]})
    add(snapshot('setup', 'after setup', scenes=scene0(setup_roots)))
    stage[0] = 't1'
    add({'kind': 'snapshot', 'point': None, 'at': 'LoadScene returned', 'state': {}}, 'returned')
    add({'kind': 'lifecycle', 'at': 'OnDisable', 'alias': 'destroyer'}, 'destroyer')
    add({'kind': 'lifecycle', 'at': 'OnDestroy', 'alias': 'destroyer'})
    add({'kind': 'create', 'at': 'iv new GameObject in OnDestroy', 'result': 'iv-ondestroy'})
    roots = [r for r in setup_roots if r not in so_far] if iv_roots is None else iv_roots
    add(snapshot('t1.a.iv', 'after (iv)', scenes=scene0(roots, iv_count)), 'iv')
    add(snapshot('t1.a.start', 'sceneUnloaded', 'scene0'), 'start')
    for point in ('t1.a.i', 't1.a.ii', 't1.a.iii.parented', 't1.a.iii.detached'):
        add(snapshot(point, point))
    error = HEADER+''.join(n+'\n' for n in names) if extra_name is None else extra_name
    add({'kind': 'log', 'at': 'Error', 'message': error})
    add(snapshot('t1.b', 'sceneLoaded', 'scene2 Single'))
    add(snapshot('t1.c', 'LateUpdate after sceneLoaded'))
    stage[0] = 't2'
    add({'kind': 'snapshot', 'point': None, 'at': 'LoadScene returned', 'state': {}})
    pairs = [(a, m) for a in cleanup for m in ('OnDisable', 'OnDestroy')]
    if not adjacent:
        pairs = [(a, 'OnDisable') for a in cleanup[:2]]+[(a, 'OnDestroy') for a in cleanup[:2]]+pairs[4:]
    if swap_pair is not None:
        i = pairs.index((swap_pair, 'OnDisable'))
        pairs[i], pairs[i+1] = pairs[i+1], pairs[i]
    for index, (alias, message) in enumerate(pairs):
        if after_start == alias and message == 'OnDestroy':
            continue
        add({'kind': 'lifecycle', 'at': message, 'alias': alias, 'scene': {'ref': 'scene2'}, 'activeScene': 'scene2'})
        if extra_in_block and index == 3:
            add({'kind': 'log', 'at': 'Log', 'message': 'inside the block'})
    add(snapshot('t2.a.start', 'sceneUnloaded', 'scene2'))
    if after_start is not None:
        add({'kind': 'lifecycle', 'at': 'OnDestroy', 'alias': after_start, 'scene': {'ref': 'scene2'},
             'activeScene': 'scene2'})
    for point in ('t2.a.made', 't2.b', 't2.c'):
        add(snapshot(point, point))
    stage[0] = 't3'
    for point in ('t3.a.start', 't3.b', 't3.c'):
        add(snapshot(point, point))
    for index, row in enumerate(body):
        row['seq'] = index
    teardown = [{'event': 'attached', 'name': n, 'stage': 'setup', 'mainSeq': 0, 'scene': 'scene0'} for n in AUTHORED]
    for name in before:
        teardown.append({'event': 'OnDestroy', 'name': name, 'stage': 't1', 'mainSeq': marks['destroyer']['seq'],
                         'scene': 'scene0'})
    for name in AUTHORED:
        if name not in before:
            seq = marks['start']['seq']+1 if name in late else marks['start']['seq']
            teardown.append({'event': 'OnDestroy', 'name': name, 'stage': 't1', 'mainSeq': seq, 'scene': 'scene0'})
    source = {'k': 'source', 'suite': SCHEMA['suite'], 'schema': SCHEMA['schema'],
              'release': SCHEMA['release'], 'aliases': SCHEMA['aliases']}
    return [source]+body+[MARKER], teardown


def raw_rows(rows):
    return [{'seq': row['seq'], 'findByTypeRaw': []} for row in rows if row.get('kind') == 'snapshot']


def write_capture(directory, rows, raw=None, result=GOOD_RESULT, text=None, teardown=None):
    directory.mkdir(parents=True)
    (directory/editor_runner.RESULT).write_text(json.dumps(result))
    out = directory/SCHEMA['output']
    out.write_text(text if text is not None else ''.join(json.dumps(r)+'\n' for r in rows))
    if raw is not False:
        raw = raw_rows(rows) if raw is None else raw
        (directory/SCHEMA['raw_output']).write_text(''.join(json.dumps(r)+'\n' for r in raw))
    if teardown is not False:
        (directory/SCHEMA['teardown_output']).write_text(''.join(json.dumps(r)+'\n' for r in teardown or []))
    return directory


def renumber(rows):
    for index, row in enumerate(rows[1:-1]):
        row['seq'] = index
    return rows


def cases():
    good, td = trace()
    yield 'good', True, good, {'teardown': td}
    rows, t = trace(before=(), names=list(reversed(NAMES)), cleanup=list(reversed(MADE)))
    yield 'good variant (nothing destroyed first, other name and cleanup order)', True, rows, {'teardown': t}
    rows, t = trace(before=tuple(AUTHORED))
    yield 'good variant (every authored root destroyed first)', True, rows, {'teardown': t}
    rows, t = trace(adjacent=False)
    yield 'good variant (cleanup interleaved, per-object order kept)', True, rows, {'teardown': t}

    # --- the gate (schema-independent damage)
    yield 'incomplete (no marker)', False, good[:-1], {'teardown': td}
    yield 'incomplete (truncated line)', False, good, {
        'teardown': td, 'text': ''.join(json.dumps(r)+'\n' for r in good[:5])+'{"seq": 4, "ki'}
    rows = copy.deepcopy(good)
    rows.insert(3, copy.deepcopy(rows[2]))
    yield 'duplicate sequence row', False, rows, {'raw': raw_rows(good), 'teardown': td}
    yield 'duplicate marker', False, good+[MARKER], {'teardown': td}
    rows = copy.deepcopy(good)
    rows.insert(-1, copy.deepcopy(next(r for r in rows if r.get('point') == 't1.b')))
    yield 'duplicate point', False, renumber(rows), {'teardown': td}
    rows = copy.deepcopy(good)
    rows[3] = {'seq': rows[3]['seq'], 'stage': 't1', 'kind': 'error', 'at': 'sceneUnloaded handler', 'exception': 'X: y'}
    yield 'error row', False, rows, {'teardown': td}
    rows = copy.deepcopy(good)
    rows[0] = dict(rows[0], suite='lifecycle')
    yield 'wrong source (suite)', False, rows, {'teardown': td}
    rows = copy.deepcopy(good)
    rows[0] = dict(rows[0], release='6000.1.0f1')
    yield 'wrong source (release)', False, rows, {'teardown': td}
    rows = copy.deepcopy(good)
    rows[0] = dict(rows[0], schema=1)
    yield 'wrong source (schema 1 capture)', False, rows, {'teardown': td}
    rows = copy.deepcopy(good)
    rows[0] = {'seq': 0, 'stage': 'play', 'at': 'mark start', 'args': '', 'v': 'global probes0 18 18 73 73'}
    yield 'wrong source (lifecycle-shaped output)', False, rows, {'teardown': td}
    yield 'forced stop (watchdog)', False, good, {'teardown': td,
                                                 'result': dict(GOOD_RESULT, forced_exit='watchdog', exit_code=-9)}
    yield 'forced stop (shutdown guard)', False, good, {'teardown': td,
                                                       'result': dict(GOOD_RESULT, forced_exit='shutdown-guard')}
    yield 'nonzero exit', False, good, {'teardown': td, 'result': dict(GOOD_RESULT, exit_code=1)}
    yield 'wrong renderer', False, good, {'teardown': td,
                                         'result': dict(GOOD_RESULT, renderer='llvmpipe (LLVM 17.0.6, 256 bits)')}
    rows = [r for r in copy.deepcopy(good) if r.get('point') != 't2.a.made']
    yield 'missing point row', False, renumber(rows), {'teardown': td}
    rows = copy.deepcopy(good)
    del next(r for r in rows if r.get('point') == 't1.c')['state']['objects']['iii-child']['created']
    yield 'missing ownership row (alias without created)', False, rows, {'teardown': td}
    rows = copy.deepcopy(good)
    del next(r for r in rows if r.get('point') == 't2.c')['state']['objects']['ii-prefab']['scene']
    yield 'missing ownership field (scene)', False, rows, {'teardown': td}
    rows = copy.deepcopy(good)
    del next(r for r in rows if r.get('point') == 't1.a.i')['state']['objects']['i-new']
    yield 'missing ownership row (alias absent)', False, rows, {'teardown': td}
    rows = copy.deepcopy(good)
    del next(r for r in rows if r.get('point') == 't3.c')['state']['lightProbes']
    yield 'missing state field (lightProbes)', False, rows, {'teardown': td}
    rows = copy.deepcopy(good)
    rows.insert(-2, rows.pop())
    yield 'marker not last', False, rows, {'teardown': td}
    yield 'raw rows missing', False, good, {'teardown': td, 'raw': False}
    yield 'raw rows short', False, good, {'teardown': td, 'raw': raw_rows(good)[:-1]}

    # --- family 1: outgoing roots at t1.a.iv against this repeat's own teardown trace
    yield 'contract: teardown output missing', False, good, {'teardown': False}
    rows, t = trace(before=('Cube',), iv_roots=['name:'+n for n in AUTHORED]+['destroyer'])
    yield 'contract: a root destroyed before t1.a.iv is still listed', False, rows, {'teardown': t}
    rows, t = trace(before=(), iv_roots=['name:Main Camera', 'name:Sun', 'name:Light Probe Group', 'destroyer'])
    yield 'contract: a root not yet destroyed is missing at t1.a.iv', False, rows, {'teardown': t}
    rows, t = trace(before=(), iv_roots=['name:Sun', 'name:Main Camera', 'name:Cube', 'name:Light Probe Group',
                                         'destroyer'])
    yield 'contract: outgoing roots reordered at t1.a.iv', False, rows, {'teardown': t}
    rows, t = trace(iv_count=99)
    yield 'contract: outgoing rootCount differs from the roots', False, rows, {'teardown': t}
    rows, t = trace(before=(), iv_roots=['name:'+n for n in AUTHORED])
    yield 'contract: the destroyer missing from its own OnDestroy snapshot', False, rows, {'teardown': t}
    rows, t = trace()
    yield 'contract: a witnessed root never destroyed', False, rows, {
        'teardown': [r for r in t if not (r['event'] == 'OnDestroy' and r['name'] == 'Sun')]}
    yield 'contract: a witnessed root destroyed twice', False, rows, {
        'teardown': t+[next(r for r in t if r['event'] == 'OnDestroy')]}
    rows, t = trace(late=('Sun',))
    yield "contract: a root destroyed after the outgoing sceneUnloaded", False, rows, {'teardown': t}
    rows, t = trace()
    yield 'contract: attached witnesses are not the setup roots', False, rows, {'teardown': t[1:]}
    yield 'contract: an undeclared teardown event', False, rows, {
        'teardown': t+[{'event': 'OnDisable', 'name': 'Sun', 'stage': 't1', 'mainSeq': 1, 'scene': 'scene0'}]}

    # --- family 2: the error's names are a set, the text otherwise exact
    rows, t = trace(names=['CreatedNew', 'CreatedNew', 'CreatedChild'])
    yield 'contract: a repeated name in the error', False, rows, {'teardown': t}
    rows, t = trace(extra_name=HEADER+'CreatedNew\nCreatedChild')
    yield 'contract: the error without its final newline', False, rows, {'teardown': t}
    rows, t = trace(extra_name=HEADER+'CreatedNew\n\nCreatedChild\n')
    yield 'contract: an empty name in the error', False, rows, {'teardown': t}

    # --- family 3: per-object OnDisable -> OnDestroy, before the owner's sceneUnloaded
    rows, t = trace(swap_pair='ii-prefab')
    yield 'contract: OnDestroy before OnDisable', False, rows, {'teardown': t}
    rows, t = trace(cleanup=MADE[:3])
    yield 'contract: a made object not cleaned up at t2', False, rows, {'teardown': t}
    rows, t = trace(extra_in_block=True)
    yield 'contract: another row inside the cleanup block', False, rows, {'teardown': t}
    rows, t = trace(after_start='iii-child')
    yield "contract: a made object destroyed after its scene's sceneUnloaded", False, rows, {'teardown': t}


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    work = Path(sys.argv[1])
    if work.exists():
        sys.exit(f'refusing to overwrite {work}')
    report, ok = [], True
    for index, (name, accept, rows, extra) in enumerate(cases()):
        options = {'raw': extra.get('raw'), 'result': extra.get('result', GOOD_RESULT), 'text': extra.get('text'),
                   'teardown': extra.get('teardown')}
        directory = write_capture(work/f'case{index:02d}', rows, **options)
        reason = run.creation_problem(directory)
        passed = (reason is None) == accept
        ok &= passed
        report.append({'case': name, 'dir': directory.name, 'expect': 'accept' if accept else 'reject',
                       'reason': reason, 'ok': passed})
        print(('ok  ' if passed else 'FAIL'), name, '->', reason or 'accepted')

    # The publication gate: canonical forms identical only when every raw difference is declared.
    def capture(name, **kwargs):
        rows, t = trace(**kwargs)
        return write_capture(work/name, rows, teardown=t)

    def strict_change(name):
        rows, t = trace()
        next(r for r in rows if r.get('point') == 't1.b')['state']['objects']['i-new']['scene'] = 'scene0'
        return write_capture(work/name, rows, teardown=t)

    def renamed_error(name):
        rows, t = trace(names=NAMES[:3]+['Other'])
        return write_capture(work/name, rows, teardown=t)

    def lifecycle_change(name):
        rows, t = trace()
        next(r for r in rows if r.get('alias') == 'i-new' and r.get('at') == 'OnDisable')['activeScene'] = 'scene3'
        return write_capture(work/name, rows, teardown=t)

    def unwitnessed_change(name):
        rows, t = trace()
        roots = next(r for r in rows if r.get('point') == 't1.a.iv')['state']['scenes'][1]['roots']
        roots.append('name:Stray')
        return write_capture(work/name, rows, teardown=t)

    a = capture('pub-a')
    pairs = [
        ('publish: pair differing only in the three families', [a, capture(
            'pub-b', before=('Main Camera', 'Light Probe Group'), names=list(reversed(NAMES)),
            cleanup=[MADE[3], MADE[0], MADE[2], MADE[1]])], True),
        ('publish: onto an existing target', [a, capture('pub-c')], False),
        ('publish: a strict ownership leaf differs', [a, strict_change('pub-d')], False),
        ('publish: the error name SET differs', [a, renamed_error('pub-e')], False),
        ('publish: a strict field of a cleanup row differs', [a, lifecycle_change('pub-f')], False),
        ('publish: another scene in the t1.a.iv snapshot differs', [a, unwitnessed_change('pub-g')], False),
        ('publish: cleanup adjacency differs (strict fact)', [a, capture('pub-h', adjacent=False)], False),
        ('publish: a damaged repeat', [a, write_capture(work/'pub-i', trace()[0][:-1], teardown=trace()[1])], False),
    ]
    for index, (name, dirs, accept) in enumerate(pairs):
        target = work/f'published{index}'/SCHEMA['output']
        if name == 'publish: onto an existing target':
            target.parent.mkdir()
            target.write_text('existing\n')
        try:
            code, reason = run.creation_publish(dirs, target, report=work/f'publication{index}.json'), None
        except SystemExit as stop:
            code, reason = None, str(stop)
        passed = (code == 0) == accept
        ok &= passed
        report.append({'case': name, 'expect': 'accept' if accept else 'reject', 'reason': reason, 'ok': passed})
        print(('ok  ' if passed else 'FAIL'), name, '->', reason or 'published')
        if accept and code == 0:
            published = target.read_text()
            same = (published == run.creation_contract.dumps(run.creation_contract.canonical(dirs[1]))
                    and json.loads(published.splitlines()[1])['k'] == 'contract')
            ok &= same
            report.append({'case': 'publish: the target is the canonical form of either repeat', 'ok': same})
            print(('ok  ' if same else 'FAIL'), 'publish: the target is the canonical form of either repeat')
    (work/'guards.json').write_text(json.dumps({'ok': ok, 'cases': report}, indent=1)+'\n')
    print('GUARDS OK' if ok else 'GUARDS FAILED')
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
