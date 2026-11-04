#!/usr/bin/env python3
"""Precommitted callback-order hypotheses of the activation order probe and their fit.

    analyze.py predict [OUT.json]                  PREDICTIONS.json from the committed probe source
    analyze.py fit VALUES.jsonl --report OUT.json  which hypotheses each operation's raw trace fits

`predict` reads the node table, creation order, recorder order and operation table from the
committed bytes of Assets/Scripts/ActivationOrderProbe.cs (never from a capture) and writes, per
operation, the exact callback list each hypothesis predicts:

  already-awake operations (group op; OnDisable for false, OnEnable for true, over the subtree of
  the target whose activeInHierarchy changes, i.e. P, C1, G, C2):
    PRE         hierarchy pre-order              P, C1, G, C2
    POST        hierarchy post-order             G, C1, C2, P
    REVPRE      reverse pre-order                C2, G, C1, P
  and four order confounds the fixture separates from them (each predicts another list):
    CREATE / REVCREATE   GameObject creation order (instance order) and its reverse
    ADD / REVADD         recorder AddComponent order and its reverse

  first-wake controls (group wake; target X inactive with one child X1, neither awake):
    PARENT_INTERLEAVED   X.Awake X.OnEnable X1.Awake X1.OnEnable
    PARENT_PHASED        X.Awake X1.Awake X.OnEnable X1.OnEnable
    CHILD_INTERLEAVED    X1.Awake X1.OnEnable X.Awake X.OnEnable
    CHILD_PHASED         X1.Awake X.Awake X1.OnEnable X.OnEnable
  (with one child, child-first coincides with these pairs' creation and recorder order; the wake
  controls preserve existing first-wake behaviour and are not hierarchy-order evidence).

and two relations per direction: SAME_PATH (plain, span and NativeArray calls give equal lists)
and SEPARATE_PATH (at least one batch overload differs from plain), plus the wake relation (plain
and batch first wake give the same pattern under the label map W->X, V->X).

`fit` compares only the label/callback sequence of each operation's recorded `calls` with every
prediction. Per operation the outcome is ONE, MULTIPLE or NONE (all valid; nothing gates). An
operation is NOT_EVALUABLE, never refitted, when its precondition does not hold: the set-up tree
is not the authored tree, or the recorded before-state is not the one the operation assumes
(already-awake: every target-subtree node awake once and active (off) / inactive in hierarchy
(on); first wake: target and child never awake, target inactive). The per-callback `self` states,
counts and stray calls are reported, not hypothesized. One operation is never extrapolated to
another lifecycle path.
"""
import argparse
import json
from pathlib import Path
import re
import sys

PROBE = Path(__file__).resolve().parent
SOURCE = PROBE/'Assets/Scripts/ActivationOrderProbe.cs'
sys.path.insert(0, str(PROBE))
import guards  # noqa: E402

ORDER_HYPOTHESES = ('PRE', 'POST', 'REVPRE', 'CREATE', 'REVCREATE', 'ADD', 'REVADD')
PRIMARY = ('PRE', 'POST', 'REVPRE')
WAKE_HYPOTHESES = ('PARENT_INTERLEAVED', 'PARENT_PHASED', 'CHILD_INTERLEAVED', 'CHILD_PHASED')


# ---------------------------------------------------------------- the committed fixture

def source_tables(text=None):
    """The probe's authored tables, parsed from its committed source bytes."""
    code = re.sub(r'//[^\n]*', '', text if text is not None else SOURCE.read_text())

    def table(name):
        return re.search(name + r'\s*=\s*\{(.*?)\};', code, re.S).group(1)
    nodes = [(label, None if parent == 'null' else parent.strip('"'), active == 'true')
             for label, parent, active in re.findall(r'\("(\w+)",\s*(null|"\w+"),\s*(true|false)\)', table('Nodes'))]
    consts = dict(re.findall(r'const string (\w+) = "([^"]*)";', code))
    ops = [(g, n, consts[c], t, v == 'true')
           for g, n, c, t, v in re.findall(r'\("(\w+)",\s*"(\w+)",\s*(\w+),\s*"(\w+)",\s*(true|false)\)', table('Ops'))]
    return {'nodes': nodes, 'create': re.findall(r'"(\w+)"', table('CreateOrder')),
            'add': re.findall(r'"(\w+)"', table('AddOrder')), 'ops': ops}


def children(nodes):
    kids = {label: [] for label, _, _ in nodes}
    for label, parent, _ in nodes:
        if parent is not None:
            kids[parent].append(label)
    return kids


def preorder(root, kids):
    out = [root]
    for k in kids[root]:
        out += preorder(k, kids)
    return out


def postorder(root, kids):
    out = []
    for k in kids[root]:
        out += postorder(k, kids)
    return out+[root]


def authored_tree(nodes):
    """The tree the probe must read back: pre-order from each authored root (labels, parents, sibling
    indices among authored children, depth, child count); root sibling indices are not authored."""
    kids = children(nodes)
    parents = {label: parent for label, parent, _ in nodes}
    rows = []

    def visit(label, depth):
        parent = parents[label]
        rows.append({'n': label, 'parent': parent, 'sibling': None if parent is None else kids[parent].index(label),
                     'depth': depth, 'children': len(kids[label])})
        for k in kids[label]:
            visit(k, depth+1)
    for label, parent, _ in nodes:
        if parent is None:
            visit(label, 0)
    return rows


def calls(labels, callback):
    return [{'n': n, 'cb': callback} for n in labels]


def predictions(text=None):
    t = source_tables(text)
    kids = children(t['nodes'])
    result = {'source': str(SOURCE.relative_to(PROBE)), 'tables': t, 'tree': authored_tree(t['nodes']), 'operations': {}}
    for group, name, call, target, value in t['ops']:
        key = f'{group}.{name}'
        sub = preorder(target, kids)
        if group == 'op':
            cb = 'OnEnable' if value else 'OnDisable'
            pre, post = sub, postorder(target, kids)
            orders = {'PRE': pre, 'POST': post, 'REVPRE': pre[::-1],
                      'CREATE': [n for n in t['create'] if n in sub], 'REVCREATE': [n for n in t['create'] if n in sub][::-1],
                      'ADD': [n for n in t['add'] if n in sub], 'REVADD': [n for n in t['add'] if n in sub][::-1]}
            hyp = {h: calls(orders[h], cb) for h in ORDER_HYPOTHESES}
        else:
            parent, child = sub[0], sub[1:]
            if len(child) != 1:
                raise ValueError(f'{key}: a first-wake target needs exactly one child')
            child = child[0]
            hyp = {'PARENT_INTERLEAVED': [{'n': parent, 'cb': 'Awake'}, {'n': parent, 'cb': 'OnEnable'}, {'n': child, 'cb': 'Awake'}, {'n': child, 'cb': 'OnEnable'}],
                   'PARENT_PHASED': [{'n': parent, 'cb': 'Awake'}, {'n': child, 'cb': 'Awake'}, {'n': parent, 'cb': 'OnEnable'}, {'n': child, 'cb': 'OnEnable'}],
                   'CHILD_INTERLEAVED': [{'n': child, 'cb': 'Awake'}, {'n': child, 'cb': 'OnEnable'}, {'n': parent, 'cb': 'Awake'}, {'n': parent, 'cb': 'OnEnable'}],
                   'CHILD_PHASED': [{'n': child, 'cb': 'Awake'}, {'n': parent, 'cb': 'Awake'}, {'n': child, 'cb': 'OnEnable'}, {'n': parent, 'cb': 'OnEnable'}]}
        names = list(hyp)
        same = [[a, b] for i, a in enumerate(names) for b in names[i+1:] if hyp[a] == hyp[b]]
        result['operations'][key] = {'call': call, 'target': target, 'value': value, 'subtree': sub,
                                     'hypotheses': hyp, 'identical_pairs': same}
    directions = {}
    for value, word in ((False, 'Off'), (True, 'On')):
        ops = [f'{g}.{n}' for g, n, c, tg, v in t['ops'] if g == 'op' and v == value]
        directions[word] = ops
    result['relations'] = {
        'directions': directions,
        'hypotheses': {'SAME_PATH': 'plain, span and NativeArray lists are equal',
                       'SEPARATE_PATH': 'at least one batch overload list differs from plain'},
        'wake': {'pair': [f'{g}.{n}' for g, n, *_ in t['ops'] if g == 'wake'],
                 'hypotheses': {'SAME_PATH': 'equal patterns under the label map target->X, child->X1',
                                'SEPARATE_PATH': 'different patterns'}}}
    result['notes'] = {
        'name_order_is_not_a_hypothesis': sorted(preorder(t['ops'][0][3], kids)),
        'wake_confound': 'first-wake pairs are created and given recorders child first; child-first wake '
                         'hypotheses coincide with creation/recorder order there',
        'scope': 'per operation; already-awake order and first-wake interleaving are separate scopes'}
    return result


def predict(out):
    data = predictions()
    for key, op in data['operations'].items():
        if op['identical_pairs']:
            raise SystemExit(f'{key}: hypotheses predict the same list: {op["identical_pairs"]}')
    Path(out).write_text(json.dumps(data, indent=1)+'\n')
    print('wrote', out)


# ---------------------------------------------------------------- fit

def load(path):
    values = {}
    for line in Path(path).read_text().splitlines():
        if line.strip():
            row = json.loads(line)
            values.setdefault(row['k'], row['v'])
    return values


def sequence(entries):
    return [{'n': e['n'], 'cb': e['cb']} for e in entries]


def precondition(values, key, op, tree_ok):
    """Why an operation cannot be evaluated against the precommitted predictions (empty: it can)."""
    problems = [] if tree_ok else ['the set-up tree is not the authored tree']
    if not isinstance(values.get(key+'.calls'), list):
        problems.append('no calls list')
    for label in op['subtree']:
        state = values.get(f'{key}.before.{label}')
        if not isinstance(state, dict):
            problems.append(f'no before-state of {label}')
            continue
        counts = state['counts']
        if key.startswith('op.'):
            if counts['Awake'] != 1:
                problems.append(f'{label} awake {counts["Awake"]} times before {key}')
            if state['activeInHierarchy'] == op['value']:
                problems.append(f'{label} activeInHierarchy is already {op["value"]}')
        else:
            if counts['Awake'] != 0:
                problems.append(f'{label} already awake before {key}')
    target = values.get(f'{key}.before.{op["target"]}')
    if isinstance(target, dict) and target['activeSelf'] == op['value']:
        problems.append(f'target {op["target"]} activeSelf is already {op["value"]}')
    return problems


def relabel(entries, mapping):
    return [{'n': mapping.get(e['n'], e['n']), 'cb': e['cb']} for e in entries]


def fit(path, report):
    data = predictions()
    values = load(path)
    tree = values.get('setup.tree')
    shape = [{k: r[k] for k in ('n', 'parent', 'depth', 'children')} | {'sibling': r['sibling'] if r['parent'] else None}
             for r in tree] if isinstance(tree, list) else None
    tree_ok = shape == data['tree']
    out = {'record': str(path), 'tree_matches_authored': tree_ok, 'operations': {}}
    observed = {}
    for key, op in data['operations'].items():
        got = values.get(key+'.calls')
        problems = precondition(values, key, op, tree_ok)
        entry = {'observed': got, 'returned': values.get(key+'.returned', values.get(key+'.returned.exception'))}
        if problems:
            entry.update(outcome='NOT_EVALUABLE', reasons=problems, fits=[])
        else:
            seq = sequence(got)
            observed[key] = seq
            fits = [h for h, want in op['hypotheses'].items() if want == seq]
            entry.update(fits=fits, outcome='NONE' if not fits else 'ONE' if len(fits) == 1 else 'MULTIPLE',
                         primary=[h for h in fits if h in PRIMARY])
        out['operations'][key] = entry
    relations = {}
    for word, ops in data['relations']['directions'].items():
        plain = next(k for k in ops if data['operations'][k]['call'] == guards.PLAIN)
        if not all(k in observed for k in ops):
            relations[word] = {'outcome': 'NOT_EVALUABLE', 'missing': [k for k in ops if k not in observed]}
            continue
        equal = {k: observed[k] == observed[plain] for k in ops if k != plain}
        relations[word] = {'equal_to_plain': equal, 'fits': ['SAME_PATH'] if all(equal.values()) else ['SEPARATE_PATH'],
                           'outcome': 'ONE'}
    pair = data['relations']['wake']['pair']
    if all(k in observed for k in pair):
        maps = [{data['operations'][k]['subtree'][0]: 'X', data['operations'][k]['subtree'][1]: 'X1'} for k in pair]
        same = relabel(observed[pair[0]], maps[0]) == relabel(observed[pair[1]], maps[1])
        relations['wake'] = {'fits': ['SAME_PATH'] if same else ['SEPARATE_PATH'], 'outcome': 'ONE'}
    else:
        relations['wake'] = {'outcome': 'NOT_EVALUABLE', 'missing': [k for k in pair if k not in observed]}
    out['relations'] = relations
    out['strayCalls'] = values.get('capture.strayCalls')
    out['scope'] = 'per operation; no answer gates a capture or publication'
    Path(report).write_text(json.dumps(out, indent=1)+'\n')
    for key, entry in out['operations'].items():
        print(f'{key}: {entry["outcome"]} {entry.get("fits")}')
    for word, entry in relations.items():
        print(f'relation {word}: {entry["outcome"]} {entry.get("fits")}')
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('predict', 'fit'))
    parser.add_argument('path', nargs='?')
    parser.add_argument('--report')
    args = parser.parse_args()
    if args.mode == 'predict':
        predict(args.path or PROBE/'PREDICTIONS.json')
        return 0
    if not args.path or not args.report:
        parser.error('fit needs VALUES.jsonl and --report')
    return fit(args.path, args.report)


if __name__ == '__main__':
    sys.exit(main())
