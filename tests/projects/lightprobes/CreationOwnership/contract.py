"""The creation-ownership schema's predeclared contract (schema.json "contract").

Three order families of the witness vary between Unity runs; schema.json declares them and this
module is their only implementation. `problem(directory)` checks one repeat's predicates against
its own trace; `canonical(directory)` rewrites exactly those families and copies every other row
and leaf unchanged, so two repeats publish only when their canonical bytes are identical.
`differences(a, b)` lists every leaf where the raw main outputs differ and the family (or None)
that permits it. The raw outputs are never changed.
"""
import copy
import json
from pathlib import Path

SUITE = Path(__file__).resolve().parent


def schema():
    return json.loads((SUITE/'schema.json').read_text())


def _lines(path):
    return [json.loads(line) for line in Path(path).read_text().splitlines() if line.strip()]


def load(directory, spec=None):
    spec = spec or schema()
    directory = Path(directory)
    rows = _lines(directory/spec['output'])
    teardown_path = directory/spec['teardown_output']
    teardown = _lines(teardown_path) if teardown_path.exists() else None
    return rows, teardown


def _point(body, point):
    found = [r for r in body if r.get('kind') == 'snapshot' and r.get('point') == point]
    return found[0] if len(found) == 1 else None


def _scene(row, ref):
    found = [s for s in row['state']['scenes'] if isinstance(s, dict) and s.get('ref') == ref]
    return found[0] if len(found) == 1 else None


def _cleanup_block(body, spec):
    family = spec['contract']['families']['inter-object-cleanup-order']
    aliases = family['aliases']
    start = _point(body, 't2.a.start')
    before = [r for r in body if r.get('stage') == 't2' and r.get('kind') == 'lifecycle'
              and r.get('at') in ('OnDisable', 'OnDestroy') and r.get('alias') in aliases
              and start is not None and r['seq'] < start['seq']]
    after = [r for r in body if r.get('stage') == 't2' and r.get('kind') == 'lifecycle'
             and r.get('at') in ('OnDisable', 'OnDestroy') and r.get('alias') in aliases
             and start is not None and r['seq'] > start['seq']]
    return aliases, start, before, after


def _roots_family(body, teardown, spec):
    """(problem, facts) for the outgoing-roots destruction prefix."""
    aliases = set(spec['aliases'])
    setup, iv, start = _point(body, 'setup'), _point(body, 't1.a.iv'), _point(body, 't1.a.start')
    if setup is None or iv is None or start is None:
        return 'the setup, t1.a.iv or t1.a.start snapshot is missing', None
    outgoing = start.get('args')
    setup_scene, iv_scene = _scene(setup, outgoing), _scene(iv, outgoing)
    if setup_scene is None or iv_scene is None:
        return f'the outgoing scene {outgoing!r} is not listed exactly once at setup and t1.a.iv', None
    if teardown is None:
        return f"{spec['teardown_output']} is missing", None
    attached = [r for r in teardown if r.get('event') == 'attached']
    destroyed = [r for r in teardown if r.get('event') == 'OnDestroy']
    if len(attached)+len(destroyed) != len(teardown):
        return 'the teardown output has rows of an undeclared event', None
    if any(r.get('stage') != 'setup' or r.get('scene') != outgoing for r in attached):
        return 'a teardown witness was attached outside setup or the outgoing scene', None
    refs = ['name:'+r['name'] for r in attached]
    expected = [r for r in setup_scene['roots'] if r not in aliases]
    if refs != expected:
        return f'the teardown-witnessed roots {refs} are not the setup roots without an alias {expected}', None
    by_name = {}
    for r in destroyed:
        by_name.setdefault('name:'+str(r.get('name')), []).append(r)
    if sorted(by_name) != sorted(refs) or any(len(v) != 1 for v in by_name.values()):
        return f'the witnessed roots were not each destroyed exactly once: {sorted(by_name)} vs {sorted(refs)}', None
    for ref, (r,) in by_name.items():
        if r.get('stage') != 't1' or r.get('scene') != outgoing:
            return f'{ref} was destroyed at stage {r.get("stage")} in {r.get("scene")}, not t1 in {outgoing}', None
        if not isinstance(r.get('mainSeq'), int) or r['mainSeq'] > start['seq']:
            return f"{ref} was destroyed after the outgoing scene's sceneUnloaded (mainSeq {r.get('mainSeq')})", None
    so_far = {ref for ref, (r,) in by_name.items() if r['mainSeq'] <= iv['seq']}
    want = [r for r in setup_scene['roots'] if r not in so_far]
    if iv_scene['roots'] != want:
        return (f"the outgoing roots at t1.a.iv {iv_scene['roots']} are not the setup roots minus those destroyed "
                f'so far in this trace {sorted(so_far)} ({want})'), None
    if iv_scene.get('rootCount') != len(want):
        return f"the outgoing rootCount at t1.a.iv {iv_scene.get('rootCount')} is not {len(want)}", None
    return None, {'outgoing': outgoing, 'witnessed': set(refs), 'destroyed': sorted(refs), 'so_far': sorted(so_far)}


def _error_family(body, spec):
    header = spec['contract']['families']['not-cleaned-up-error-names']['header']
    found = []
    for r in body:
        if r.get('kind') == 'log' and r.get('at') == 'Error' and str(r.get('message', '')).startswith(header):
            tail = r['message'][len(header):]
            if not tail.endswith('\n'):
                return f"error row {r['seq']} does not end with a newline", None
            names = tail[:-1].split('\n')
            if not names or any(not n for n in names) or len(set(names)) != len(names):
                return f"error row {r['seq']} has an empty or repeated name: {names}", None
            found.append((r['seq'], names))
    return None, found


def _cleanup_family(body, spec):
    aliases, start, block, after = _cleanup_block(body, spec)
    if start is None:
        return 'the t2.a.start snapshot is missing', None
    if after:
        return f"made objects are disabled/destroyed after the t2 sceneUnloaded: seqs {[r['seq'] for r in after]}", None
    seqs = [r['seq'] for r in block]
    if not seqs or seqs != list(range(seqs[0], seqs[0]+len(seqs))):
        return f'the t2 cleanup rows are not one contiguous block: seqs {seqs}', None
    for alias in aliases:
        mine = [r['at'] for r in block if r['alias'] == alias]
        if mine != ['OnDisable', 'OnDestroy']:
            return f'{alias} cleanup at t2 is {mine}, not OnDisable then OnDestroy', None
    adjacent = all(block[i]['at'] == 'OnDisable' and block[i+1]['at'] == 'OnDestroy'
                   and block[i]['alias'] == block[i+1]['alias'] for i in range(0, len(block), 2))
    return None, {'block': block, 'adjacent': adjacent, 'aliases': aliases}


def problem(directory, spec=None):
    """The contract's predicates for one complete repeat, or None."""
    spec = spec or schema()
    rows, teardown = load(directory, spec)
    body = rows[1:-1]
    for check in (lambda: _roots_family(body, teardown, spec), lambda: _error_family(body, spec),
                  lambda: _cleanup_family(body, spec)):
        reason, _ = check()
        if reason:
            return 'contract: '+reason
    return None


def canonical(directory, spec=None):
    """The canonical rows of one repeat that satisfies the contract (raises otherwise)."""
    spec = spec or schema()
    reason = problem(directory, spec)
    if reason:
        raise ValueError(reason)
    rows, teardown = load(directory, spec)
    rows = copy.deepcopy(rows)
    body = rows[1:-1]
    _, roots = _roots_family(body, teardown, spec)
    _, errors = _error_family(body, spec)
    _, cleanup = _cleanup_family(body, spec)
    families = spec['contract']['families']

    iv = _point(body, 't1.a.iv')
    scene = _scene(iv, roots['outgoing'])
    scene['roots'] = {'contract': 'destruction-prefix',
                      'unwitnessed': [r for r in scene['roots'] if r not in roots['witnessed']]}
    scene['rootCount'] = 'contract:destruction-prefix'

    header = families['not-cleaned-up-error-names']['header']
    by_seq = {r['seq']: r for r in body}
    for seq, names in errors:
        by_seq[seq]['message'] = {'header': header, 'names': sorted(names)}

    block = cleanup['block']
    first = block[0]['seq']
    order = {alias: i for i, alias in enumerate(cleanup['aliases'])}
    ordered = sorted(block, key=lambda r: (order[r['alias']], r['at'] != 'OnDisable'))
    index = body.index(block[0])
    for offset, row in enumerate(ordered):
        row = copy.deepcopy(row)
        row['seq'] = first+offset
        body[index+offset] = row

    contract_row = {'k': 'contract', 'schema': spec['schema'], 'contract': spec['contract']['version'],
                    'outgoing-roots-destruction-prefix': {'scene': roots['outgoing'], 'destroyedInT1': roots['destroyed'],
                                                          'beforeSceneUnloaded': True, 'validated': True},
                    'not-cleaned-up-error-names': {'rows': len(errors), 'validated': True},
                    'inter-object-cleanup-order': {'aliases': cleanup['aliases'],
                                                   'eachDisableImmediatelyFollowedByOwnDestroy': cleanup['adjacent'],
                                                   'beforeSceneUnloaded': True, 'validated': True}}
    return [rows[0], contract_row]+body+[rows[-1]]


def dumps(rows):
    return ''.join(json.dumps(r, sort_keys=False, ensure_ascii=False)+'\n' for r in rows)


def _leaves(value, path=''):
    if isinstance(value, dict):
        if not value:
            yield path, '{}'
        for key, item in value.items():
            yield from _leaves(item, path+'/'+key)
    elif isinstance(value, list):
        yield path+'#len', len(value)
        for index, item in enumerate(value):
            yield from _leaves(item, f'{path}/{index}')
    else:
        yield path, value


def classify(row, path, spec, outgoing_index):
    """The family that may permit a raw difference at this row/leaf, or None (strict). The family
    then decides by its predicates and the canonical comparison, never by this label alone."""
    families = spec['contract']['families']
    if (row.get('kind') == 'snapshot' and row.get('point') == 't1.a.iv' and outgoing_index is not None
            and (path.startswith(f'/state/scenes/{outgoing_index}/roots')
                 or path == f'/state/scenes/{outgoing_index}/rootCount')):
        return 'outgoing-roots-destruction-prefix'
    if (row.get('kind') == 'log' and row.get('at') == 'Error' and path == '/message'
            and str(row.get('message', '')).startswith(families['not-cleaned-up-error-names']['header'])):
        return 'not-cleaned-up-error-names'
    # Only which object/message sits at a position of the block may vary; its other fields stay strict.
    if (row.get('stage') == 't2' and row.get('kind') == 'lifecycle' and row.get('at') in ('OnDisable', 'OnDestroy')
            and row.get('alias') in families['inter-object-cleanup-order']['aliases'] and path in ('/alias', '/at')):
        return 'inter-object-cleanup-order'
    return None


def _same_names(x, y, spec):
    header = spec['contract']['families']['not-cleaned-up-error-names']['header']
    return sorted(x['message'][len(header):].split('\n')) == sorted(y['message'][len(header):].split('\n'))


def _outgoing_index(rows):
    body = rows[1:-1]
    start, iv = _point(body, 't1.a.start'), _point(body, 't1.a.iv')
    if start is None or iv is None:
        return None
    found = [i for i, s in enumerate(iv['state'].get('scenes') or []) if isinstance(s, dict) and s.get('ref') == start.get('args')]
    return found[0] if len(found) == 1 else None


def canonical_differences(first, second, spec=None):
    """Every leaf where two repeats' canonical forms differ (empty: they publish)."""
    spec = spec or schema()
    a, b = canonical(first, spec), canonical(second, spec)
    out = [] if len(a) == len(b) else [{'row': None, 'path': 'row count', 'first': len(a), 'second': len(b)}]
    for index, (x, y) in enumerate(zip(a, b)):
        lx, ly = dict(_leaves(x)), dict(_leaves(y))
        for path in sorted(set(lx) | set(ly)):
            if lx.get(path) != ly.get(path):
                out.append({'row': index, 'seq': x.get('seq'), 'path': path, 'first': lx.get(path),
                            'second': ly.get(path)})
    return out


def differences(first, second, spec=None):
    """Every raw main-output leaf where two repeats differ, with its permitting family (None:
    strict, so the pair cannot publish)."""
    spec = spec or schema()
    a, _ = load(first, spec)
    b, _ = load(second, spec)
    ia, ib = _outgoing_index(a), _outgoing_index(b)
    out = []
    if len(a) != len(b):
        out.append({'row': None, 'path': 'row count', 'first': len(a), 'second': len(b), 'family': None})
    for index, (x, y) in enumerate(zip(a, b)):
        if x == y:
            continue
        lx, ly = dict(_leaves(x)), dict(_leaves(y))
        for path in sorted(set(lx) | set(ly)):
            if lx.get(path) != ly.get(path):
                fx, fy = classify(x, path, spec, ia), classify(y, path, spec, ib)
                if fx == 'not-cleaned-up-error-names' and fx == fy and not _same_names(x, y, spec):
                    fx = None
                out.append({'row': index, 'seq': x.get('seq'), 'kind': x.get('kind'), 'at': x.get('at'),
                            'point': x.get('point'), 'path': path, 'first': lx.get(path), 'second': ly.get(path),
                            'family': fx if fx == fy else None})
    return out
