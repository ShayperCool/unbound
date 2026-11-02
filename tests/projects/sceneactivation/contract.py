#!/usr/bin/env python3
"""The scene activation witness contract: record schema, capture gate, canonical form, analysis.

    contract.py gate RAW.jsonl                 the capture gate (exit 1 with the reasons)
    contract.py canonical RAW.jsonl OUT IDS    writes the canonical form and the raw-id side file
    contract.py analyze CAPTURE.jsonl OUT      observed orders against the 28 candidates

Schema (strict: exact keys in this order, these types; `int` ids are raw GetInstanceID values):

    bind      k scene obj type cid gid tid go tr mb      Edit mode, one per marker
    roots     k at scene [frame] roots                   at=edit (no frame) | loaded | settled
    tree      k at scene [frame] nodes                   pre-order paths, same `at`/frame rule
    cb        k ev scene obj type frame cid gid tid      Awake | OnEnable | Start in Play mode
    loaded    k scene mode frame                         SceneManager.sceneLoaded
    unloaded  k scene frame                              SceneManager.sceneUnloaded
    request   k scene frame                              the driver's LoadScene(Single) call
    complete  k v                                        {"k":"complete","v":true}, last line only

Gate (each capture; ORDER IS NEVER GATED OR NORMALIZED):
* every line is a JSON object of the schema; the single completion marker is the last line;
* an Edit section first (only roots/tree at=edit and bind rows, before any Play row): exactly one
  roots and one tree record of Wake and one bind per fixture marker, of the fixture's class;
* Play: for Wake (Play entry) and WakeCopy (LoadScene Single) exactly one Awake, one OnEnable
  and one Start per fixture marker, of the fixture's class, and no other callback; exactly one
  loaded record and one roots/tree record at=loaded and at=settled per scene; exactly one request
  (WakeCopy) and one unloaded (Wake), nothing else (no record after the log closed);
* membership: every roots list is a permutation of the fixture's 9 roots and every nodes list a
  permutation of its 15 pre-order paths (which fixes every parent); no duplicates;
* identity: within one phase a marker keeps one component/GameObject/Transform id, and those ids
  are distinct across markers; the bind GlobalObjectIds of plain objects equal the fixture's
  (scene object, Wake's GUID, local fileID, prefab id 0); those of prefab-instance objects are
  scene objects of Wake's GUID; their object and prefab ids are compared with the fixture's
  prediction in the analysis (reported, not gated: the nested-instance id composition is not
  established).

Canonical form (predeclared; published only from two gate-passing captures that are byte-equal
in it): every raw line in raw order with every field verbatim, except that each raw id key
`cid`/`gid`/`tid` is replaced in place by `<key>_rank` and `<key>_sign`. The rank is the index of
the id in the ascending numeric order of that key's 15 distinct ids within the row's phase
(edit = bind rows, Wake / WakeCopy = cb rows of that scene); the sign is "-", "0" or "+".
Serialization: compact JSON (separators ',' ':'), keys in record order, one line per record.
The raw ids go to the side file (one JSON line per id-bearing raw line: line, phase, obj, k/ev,
cid, gid, tid). Ranks are an observable for an id-order hypothesis; they are NOT allocation
chronology.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
EVENTS = ('Awake', 'OnEnable', 'Start')
PHASES = ('Wake', 'WakeCopy')
IDS = ('cid', 'gid', 'tid')
KEYS = {
    'bind': ['k', 'scene', 'obj', 'type', 'cid', 'gid', 'tid', 'go', 'tr', 'mb'],
    'roots_edit': ['k', 'at', 'scene', 'roots'],
    'roots': ['k', 'at', 'scene', 'frame', 'roots'],
    'tree_edit': ['k', 'at', 'scene', 'nodes'],
    'tree': ['k', 'at', 'scene', 'frame', 'nodes'],
    'cb': ['k', 'ev', 'scene', 'obj', 'type', 'frame', 'cid', 'gid', 'tid'],
    'loaded': ['k', 'scene', 'mode', 'frame'],
    'unloaded': ['k', 'scene', 'frame'],
    'request': ['k', 'scene', 'frame'],
    'complete': ['k', 'v'],
}
TYPES = {'k': str, 'scene': str, 'obj': str, 'type': str, 'ev': str, 'at': str, 'mode': str,
         'go': str, 'tr': str, 'mb': str, 'frame': int, 'cid': int, 'gid': int, 'tid': int,
         'roots': list, 'nodes': list, 'v': bool}
COMPLETE = {'k': 'complete', 'v': True}


def load_fixture():
    return json.loads((PROBE/'fixture.json').read_text())


def load_predictions():
    return json.loads((PROBE/'predictions.json').read_text())['predictions']


def read_rows(path):
    """(rows, problem)."""
    try:
        text = Path(path).read_text()
    except FileNotFoundError:
        return None, f'{Path(path).name} is missing'
    if not text.endswith('\n'):
        return None, 'the capture does not end with a newline (incomplete last write)'
    rows = []
    for i, line in enumerate(text.split('\n')[:-1]):
        try:
            row = json.loads(line)
        except json.JSONDecodeError:
            return None, f'line {i} is not JSON'
        if not isinstance(row, dict):
            return None, f'line {i} is not a JSON object'
        rows.append(row)
    return rows, None


def schema_key(row):
    kind = row.get('k')
    if kind in ('roots', 'tree'):
        return kind+'_edit' if row.get('at') == 'edit' else kind
    return kind


def schema_problem(i, row):
    key = schema_key(row)
    if key not in KEYS:
        return f'line {i}: unknown record kind {row.get("k")!r}'
    if list(row) != KEYS[key]:
        return f'line {i}: keys {list(row)} are not {KEYS[key]}'
    for name, value in row.items():
        want = TYPES[name]
        if want is int and (isinstance(value, bool) or not isinstance(value, int)):
            return f'line {i}: {name} is not an integer'
        if want is not int and not isinstance(value, want):
            return f'line {i}: {name} is not {want.__name__}'
        if want is list and not all(isinstance(v, str) for v in value):
            return f'line {i}: {name} has non-string entries'
    if row['k'] in ('roots', 'tree') and row['at'] not in ('edit', 'loaded', 'settled'):
        return f'line {i}: unknown at {row["at"]!r}'
    if row['k'] == 'cb' and row['ev'] not in EVENTS:
        return f'line {i}: unknown callback {row["ev"]!r}'
    if row['k'] == 'complete' and row != COMPLETE:
        return f'line {i}: malformed completion marker'
    return None


def parse_gid(text):
    """GlobalObjectId_V1-<type>-<guid>-<object id>-<prefab id> -> tuple, or None."""
    parts = text.split('-')
    if len(parts) != 5 or parts[0] != 'GlobalObjectId_V1':
        return None
    try:
        return int(parts[1]), parts[2], int(parts[3]), int(parts[4])
    except ValueError:
        return None


def gate(rows, fixture=None):
    """All problems of one capture's rows (empty list: the capture passes)."""
    fixture = fixture or load_fixture()
    objects = {o['n']: o for o in fixture['objects']}
    roots, tree = sorted(fixture['roots']), sorted(fixture['tree'])
    problems = []
    for i, row in enumerate(rows):
        p = schema_problem(i, row)
        if p:
            return [p]
    if not rows or rows[-1] != COMPLETE or rows.count(COMPLETE) != 1:
        problems.append('the completion marker is not the single last line')
    play_start = next((i for i, r in enumerate(rows) if r['k'] not in ('bind', 'roots', 'tree')
                       or r.get('at') != 'edit' and r['k'] in ('roots', 'tree')), len(rows))
    edit, play = rows[:play_start], rows[play_start:]
    late_edit = [i for i, r in enumerate(play) if r['k'] == 'bind' or r.get('at') == 'edit']
    if late_edit:
        problems.append(f'{len(late_edit)} Edit-mode record(s) after Play mode began')

    def count(selection, what, expected=1):
        if len(selection) != expected:
            problems.append(f'{what}: {len(selection)} record(s), expected {expected}')

    # Edit section.
    count([r for r in edit if r['k'] == 'roots' and r['scene'] == 'Wake'], 'Edit roots of Wake')
    count([r for r in edit if r['k'] == 'tree' and r['scene'] == 'Wake'], 'Edit tree of Wake')
    other_edit = [r for r in edit if r['k'] in ('roots', 'tree') and r['scene'] != 'Wake']
    if other_edit:
        problems.append(f'{len(other_edit)} Edit hierarchy record(s) of another scene')
    binds = [r for r in edit if r['k'] == 'bind']
    for name, o in objects.items():
        mine = [r for r in binds if r['obj'] == name]
        count(mine, f'bind {name}')
        for r in mine:
            if r['type'] != o['c'] or r['scene'] != 'Wake':
                problems.append(f'bind {name}: type/scene {r["type"]}/{r["scene"]}, expected {o["c"]}/Wake')
            for key in ('go', 'tr', 'mb'):
                got = parse_gid(r[key])
                want = parse_gid(o['globalobjectid'][key])
                if got is None:
                    problems.append(f'bind {name}: {key} is not a GlobalObjectId: {r[key]!r}')
                elif key in o['gate_globalobjectid']:
                    if got != want:
                        problems.append(f'bind {name}: {key} {r[key]} is not the fixture\'s {o["globalobjectid"][key]}')
                elif (got[0], got[1]) != (want[0], want[1]):
                    problems.append(f'bind {name}: {key} {r[key]} is not a scene object of Wake')
    stray = [r['obj'] for r in binds if r['obj'] not in objects]
    if stray:
        problems.append(f'bind records of unknown objects {stray}')
    # Play section.
    for phase in PHASES:
        cbs = [r for r in play if r['k'] == 'cb' and r['scene'] == phase]
        for name, o in objects.items():
            for ev in EVENTS:
                mine = [r for r in cbs if r['obj'] == name and r['ev'] == ev]
                count(mine, f'{phase} {ev} {name}')
                for r in mine:
                    if r['type'] != o['c']:
                        problems.append(f'{phase} {ev} {name}: type {r["type"]}, expected {o["c"]}')
        stray = sorted({r['obj'] for r in cbs if r['obj'] not in objects})
        if stray:
            problems.append(f'{phase}: callbacks of unknown objects {stray}')
        count([r for r in play if r['k'] == 'loaded' and r['scene'] == phase], f'loaded {phase}')
        for kind in ('roots', 'tree'):
            for at in ('loaded', 'settled'):
                count([r for r in play if r['k'] == kind and r['at'] == at and r['scene'] == phase],
                      f'{kind} at {at} of {phase}')
    other = [r for r in play if r['k'] in ('cb', 'loaded', 'roots', 'tree') and r['scene'] not in PHASES]
    if other:
        problems.append(f'{len(other)} Play record(s) of other scenes: {sorted({r["scene"] for r in other})}')
    count([r for r in play if r['k'] == 'request'], 'request')
    count([r for r in play if r['k'] == 'request' and r['scene'] == 'WakeCopy'], 'request of WakeCopy')
    count([r for r in play if r['k'] == 'unloaded'], 'unloaded')
    count([r for r in play if r['k'] == 'unloaded' and r['scene'] == 'Wake'], 'unloaded Wake')
    # Membership (never order).
    for i, r in enumerate(rows):
        if r['k'] == 'roots' and sorted(r['roots']) != roots:
            problems.append(f'line {i}: roots {r["roots"]} are not the fixture roots (as a set, no duplicates)')
        if r['k'] == 'tree' and sorted(r['nodes']) != tree:
            problems.append(f'line {i}: nodes are not the fixture objects/parents (as a set, no duplicates)')
    # Identity.
    for phase, selection in phase_rows(rows).items():
        for key in IDS:
            per = {}
            for r in selection:
                per.setdefault(r['obj'], set()).add(r[key])
            if any(len(v) != 1 for v in per.values()):
                problems.append(f'{phase}: a marker changes its {key}')
            values = [next(iter(v)) for v in per.values() if len(v) == 1]
            if len(set(values)) != len(values):
                problems.append(f'{phase}: two markers share a {key}')
    return problems


def phase_rows(rows):
    return {'edit': [r for r in rows if r['k'] == 'bind'],
            **{p: [r for r in rows if r['k'] == 'cb' and r['scene'] == p] for p in PHASES}}


def phase_of(row):
    return 'edit' if row['k'] == 'bind' else row['scene'] if row['k'] == 'cb' else None


def canonical(rows):
    """(canonical text, ids side-file text) of a gate-passing capture."""
    ranks = {}
    for phase, selection in phase_rows(rows).items():
        for key in IDS:
            values = sorted({r[key] for r in selection})
            ranks[phase, key] = {v: i for i, v in enumerate(values)}
    lines, ids = [], []
    for i, row in enumerate(rows):
        phase = phase_of(row)
        if phase is None:
            lines.append(json.dumps(row, ensure_ascii=False, separators=(',', ':')))
            continue
        out = {}
        for key, value in row.items():
            if key in IDS:
                out[key+'_rank'] = ranks[phase, key][value]
                out[key+'_sign'] = '-' if value < 0 else '+' if value > 0 else '0'
            else:
                out[key] = value
        lines.append(json.dumps(out, ensure_ascii=False, separators=(',', ':')))
        ids.append(json.dumps({'line': i, 'phase': phase, 'obj': row['obj'],
                               'event': row['ev'] if row['k'] == 'cb' else 'bind',
                               **{key: row[key] for key in IDS}}, separators=(',', ':')))
    return '\n'.join(lines)+'\n', '\n'.join(ids)+'\n'


# ---------------------------------------------------------------- analysis (never a gate)

def lcs(a, b):
    t = [[0]*(len(b)+1) for _ in range(len(a)+1)]
    for i in range(len(a)):
        for j in range(len(b)):
            t[i+1][j+1] = t[i][j]+1 if a[i] == b[j] else max(t[i][j+1], t[i+1][j])
    return t[-1][-1]


def compare(observed, preds):
    matches = [name for name, seq in preds.items() if seq == observed]
    return {'observed': observed,
            'verdict': 'match' if len(matches) == 1 else 'none' if not matches else 'multiple',
            'matches': matches,
            'out_of_order': {name: len(observed)-lcs(observed, seq) for name, seq in preds.items()}}


def analyze(rows, fixture=None, preds=None):
    """Observed orders of a canonical OR raw capture (only order fields are read)."""
    fixture = fixture or load_fixture()
    preds = preds or load_predictions()
    objects = {o['n']: o for o in fixture['objects']}
    out = {'phases': {}, 'notes': []}
    rank_key = 'cid_rank' if 'cid_rank' in next(r for r in rows if r['k'] == 'cb') else 'cid'
    for phase in PHASES:
        cbs = [r for r in rows if r['k'] == 'cb' and r['scene'] == phase]
        seqs = {ev: [r['obj'] for r in cbs if r['ev'] == ev] for ev in EVENTS}
        stream = [(r['ev'], r['obj']) for r in cbs]
        aw = [(e, o) for e, o in stream if e in ('Awake', 'OnEnable')]
        paired = all(aw[2*i] == ('Awake', aw[2*i+1][1]) and aw[2*i+1][0] == 'OnEnable' for i in range(len(aw)//2))
        entry = {
            'events': {ev: compare(seqs[ev], preds) for ev in EVENTS},
            'awake_onenable_interleaving': 'per object (Awake, OnEnable)' if paired else 'not per-object pairs',
            'start_equals_awake': seqs['Start'] == seqs['Awake'],
            'onenable_equals_awake': seqs['OnEnable'] == seqs['Awake'],
            'frames': {ev: sorted({r['frame'] for r in cbs if r['ev'] == ev}) for ev in EVENTS},
            'exec_order_first': seqs['Awake'][:1] == ['E'],
            'positions': {name: {ev: seqs[ev].index(name) for ev in EVENTS} for name in ('E', 'D')},
        }
        for kind in ('roots', 'tree'):
            for at in ('loaded', 'settled'):
                r = next(r for r in rows if r['k'] == kind and r['at'] == at and r['scene'] == phase)
                got = r['roots' if kind == 'roots' else 'nodes']
                want = fixture['roots' if kind == 'roots' else 'tree']
                entry[f'{kind}_{at}'] = {'observed': got, 'fixture_order': got == want}
        ids = {}
        for key in IDS:
            k = key+'_rank' if rank_key != 'cid' else key
            per = {r['obj']: r[k] for r in cbs if r['ev'] == 'Awake'}
            asc = sorted(per, key=per.get)
            ids[key] = {'ascending': asc, 'awake_equals_ascending': asc == seqs['Awake'],
                        'awake_equals_descending': asc[::-1] == seqs['Awake'],
                        'awake_out_of_order_vs_ascending': len(asc)-lcs(asc, seqs['Awake'])}
        entry['instance_id_order'] = ids
        if rank_key != 'cid':
            entry['signs'] = sorted({r[k] for r in cbs for k in ('cid_sign', 'gid_sign', 'tid_sign')})
        out['phases'][phase] = entry
    edit = [r for r in rows if r['k'] in ('roots', 'tree') and r['at'] == 'edit']
    out['edit'] = {r['k']: r['roots' if r['k'] == 'roots' else 'nodes'] for r in edit}
    out['edit']['roots_fixture_order'] = out['edit'].get('roots') == fixture['roots']
    out['edit']['tree_fixture_order'] = out['edit'].get('tree') == fixture['tree']
    binds = [r for r in rows if r['k'] == 'bind']
    out['edit']['bind_order'] = [r['obj'] for r in binds]
    out['globalobjectid_object_ids'] = {
        r['obj']: {key: {'observed': parse_gid(r[key])[2:], 'fixture': parse_gid(objects[r['obj']]['globalobjectid'][key])[2:],
                         'equal': parse_gid(r[key]) == parse_gid(objects[r['obj']]['globalobjectid'][key])}
                   for key in ('go', 'tr', 'mb')}
        for r in binds if objects.get(r['obj'], {}).get('k') == 'inst'}
    out['phase_agreement'] = {ev: out['phases']['Wake']['events'][ev]['observed'] ==
                              out['phases']['WakeCopy']['events'][ev]['observed'] for ev in EVENTS}
    return out


def raw_id_facts(rows):
    """Raw-id observations that the canonical ranks cannot show (raw captures only)."""
    per = {p: {r['obj']: {k: r[k] for k in IDS} for r in sel} for p, sel in phase_rows(rows).items()}
    return {'wake_ids_equal_edit_ids': {k: all(per['Wake'][o][k] == per['edit'][o][k] for o in per['edit'])
                                        for k in IDS},
            'copy_ids_equal_wake_ids': {k: all(per['WakeCopy'][o][k] == per['Wake'][o][k] for o in per['Wake'])
                                        for k in IDS},
            'ranges': {p: {k: [min(v[k] for v in d.values()), max(v[k] for v in d.values())] for k in IDS}
                       for p, d in per.items()}}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='mode', required=True)
    g = sub.add_parser('gate')
    g.add_argument('raw', type=Path)
    c = sub.add_parser('canonical')
    c.add_argument('raw', type=Path)
    c.add_argument('out', type=Path)
    c.add_argument('ids', type=Path)
    a = sub.add_parser('analyze')
    a.add_argument('capture', type=Path)
    a.add_argument('out', type=Path)
    args = parser.parse_args()
    if args.mode == 'gate':
        rows, problem = read_rows(args.raw)
        problems = [problem] if problem else gate(rows)
        print('\n'.join(problems) or 'gate passes')
        return int(bool(problems))
    if args.mode == 'canonical':
        rows, problem = read_rows(args.raw)
        problems = [problem] if problem else gate(rows)
        if problems:
            sys.exit('gate fails: '+'; '.join(problems))
        text, ids = canonical(rows)
        for path, data in ((args.out, text), (args.ids, ids)):
            with path.open('x') as stream:
                stream.write(data)
        print(hashlib.sha256(text.encode()).hexdigest(), args.out)
        print(hashlib.sha256(ids.encode()).hexdigest(), args.ids)
        return 0
    rows, problem = read_rows(args.capture)
    if problem:
        sys.exit(problem)
    result = analyze(rows)
    if 'cid' in next(r for r in rows if r['k'] == 'cb'):
        result['raw_ids'] = raw_id_facts(rows)
    with args.out.open('x') as stream:
        stream.write(json.dumps(result, indent=1)+'\n')
    for phase, entry in result['phases'].items():
        for ev, cmp in entry['events'].items():
            print(f'{phase:8} {ev:8} {cmp["verdict"]:8} {cmp["matches"]} {" ".join(cmp["observed"])}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
