#!/usr/bin/env python3
"""Precommitted mechanism hypotheses of the activation mechanism probe and their fit.

    analyze.py predict [OUT.json]                                PREDICTIONS.json from the committed probe bytes
    analyze.py fit VALUES.jsonl --report OUT.json [--diagnostics D]   which hypotheses each case's raw trace fits

`predict` reads only the committed probe source tables (Assets/Scripts/ActivationMechanismsProbe.cs)
and the committed scene (Assets/Scenes/Probe.unity), never a capture.

Already-awake operations (op.*; OnDisable for off, OnEnable for on, over the recorders of the
target's subtree, each object's recorders in its component-list order unless stated):
  traversals       PRE, POST, REVPRE, REVPOST, LEVEL (breadth-first), REVLEVEL (deepest level first,
                   siblings left to right), REVBFS (reverse breadth-first); each also *_REVCOMP
                   (an object's recorders in reverse component-list order)
  construction     CREATE / REVCREATE (GameObject construction order), ADD / REVADD (recorder
                   AddComponent order) -- runtime trees
  authored chronology  AFIRSTAWAKE / REVAFIRSTAWAKE (first Awake as the construction statements
                   imply: adds on active objects wake at once, wake steps later), ALASTENABLE /
                   REVALASTENABLE (last OnEnable implied by adds, wakes and enabled toggles)
  scene documents  DOCMB / REVDOCMB (recorder MonoBehaviour document order), DOCGO / REVDOCGO
                   (GameObject document order), FILEIDMB / REVFILEIDMB, FILEIDGO / REVFILEIDGO;
                   the runtime recorder (no document) last for the forward and first for the
                   reverse variants -- scene tree
  capture-derived (rules precommitted; values from each capture's own record/diagnostics only)
                   FIRSTAWAKE / REVFIRSTAWAKE, LASTENABLE / REVLASTENABLE (actual chronology up to
                   the operation), IDDESC / IDASC (actual recorder component ids), GOIDDESC /
                   GOIDASC (actual GameObject ids), TRIDDESC / TRIDASC (actual Transform ids); the
                   GameObject/Transform variants keep component-list order inside an object.
                   Observer component, GameObject and Transform ids are separate hypotheses; no id
                   is ever inferred.

Built-in interleaving cases (contact.*, particles.*; target P deactivated, child C, partner E):
  order models     CUR (pre-order objects, component order), F (children first, component order),
                   F_REVCOMP, S (built-ins pre-order first, then scripts children first), S_POST
                   (built-ins children first, then scripts children first), SCRIPTS_FIRST (scripts
                   children first, then built-ins)
  exit placement   AT_REMOVAL (2D exits at the first Rigidbody2D/Collider2D removal), DEFERRED
                   (none in the call, some at the next explicit Simulate), NONE (none at all)
  state reads      REMOVAL (a built-in read inside OnDisable shows the removal iff it happened
                   before), FLAGS (every read inside OnDisable already shows the deactivated state)
  The skeleton of a call is its recorder OnDisable tokens (role.OnDisable[annotation]) with every
  run of observer entries collapsed to EXITS. Annotations come from the entry's own state read:
  touching (physics) or each ParticleSystem's read against its before/after state.

`fit` gives ONE, MULTIPLE (an indistinguishable cell or several cells), NONE or NOT_EVALUABLE per
case; every outcome is valid. A case whose precondition does not hold (tree/components not the
authored ones, recorders not awake/enabled, no contact before the call, no observable state change)
is NOT_EVALUABLE and never refitted. Combined summaries intersect the names consistent with every
evaluable case. Nothing in run.py or guards.py reads the analyzer; no answer gates anything.
"""
import argparse
import json
from pathlib import Path
import re
import sys

PROBE = Path(__file__).resolve().parent
SOURCE = PROBE/'Assets/Scripts/ActivationMechanismsProbe.cs'
SCENE = PROBE/'Assets/Scenes/Probe.unity'
SCRIPT_META = PROBE/'Assets/Scripts/ActivationMechanismsProbe.cs.meta'
RECORDER_TYPE = 'ActivationMechanismsProbe'
OUTCOMES = ('ONE', 'MULTIPLE', 'NONE', 'NOT_EVALUABLE')
TRAVERSALS = ('PRE', 'POST', 'REVPRE', 'REVPOST', 'LEVEL', 'REVLEVEL', 'REVBFS')
ORDER_MODELS = ('CUR', 'F', 'F_REVCOMP', 'S', 'S_POST', 'SCRIPTS_FIRST')
PLACEMENTS = ('AT_REMOVAL', 'DEFERRED', 'NONE')
READS = ('REMOVAL', 'FLAGS')
CAPTURE_DERIVED = {
    'FIRSTAWAKE': 'recorders in the order of their first recorded Awake (boot, setup, settle and earlier operation lists)',
    'REVFIRSTAWAKE': 'reverse of FIRSTAWAKE',
    'LASTENABLE': 'recorders in the order of their last recorded OnEnable before the operation',
    'REVLASTENABLE': 'reverse of LASTENABLE',
    'IDDESC': 'recorder components by actual instance id, descending (signed)',
    'IDASC': 'recorder components by actual instance id, ascending (signed)',
    'GOIDDESC': 'objects by actual GameObject instance id descending; component-list order inside an object',
    'GOIDASC': 'objects by actual GameObject instance id ascending; component-list order inside an object',
    'TRIDDESC': 'objects by actual Transform instance id descending; component-list order inside an object',
    'TRIDASC': 'objects by actual Transform instance id ascending; component-list order inside an object',
}


# ---------------------------------------------------------------- the committed fixture

def source_tables(text=None):
    """The probe's authored tables, parsed from its committed source bytes."""
    code = re.sub(r'//[^\n]*', '', text if text is not None else SOURCE.read_text())

    def table(name):
        return re.search(r'\b' + name + r'\s*=\s*\{(.*?)\};', code, re.S).group(1)
    nodes = [(tree, label, None if parent == 'null' else parent.strip('"'), active == 'true')
             for tree, label, parent, active in re.findall(r'\("(\w+)",\s*"(\w+)",\s*(null|"\w+"),\s*(true|false)\)', table('Nodes'))]
    strings = lambda name: re.findall(r'"(\w+)"', table(name))  # noqa: E731
    ops = [(n, t, g, v == 'true') for n, t, g, v in re.findall(r'\("(\w+)",\s*"(\w+)",\s*"(\w+)",\s*(true|false)\)', table('Ops'))]
    contacts = [(n, a == 'true', b == 'true', c == 'true')
                for n, a, b, c in re.findall(r'\("(\w+)",\s*(true|false),\s*(true|false),\s*(true|false)\)', table('Contacts'))]
    particles = [(n, a == 'true') for n, a in re.findall(r'\("(\w+)",\s*(true|false)\)', table('Particles'))]
    return {'nodes': nodes, 'create': strings('CreateOrder'), 'add': strings('AddOrder'), 'wake': strings('WakeOrder'),
            'toggle': strings('ToggleOrder'),
            'scene_adds': [list(x) for x in re.findall(r'\("(\w+)",\s*"(\w+)"\)', table('SceneAdds'))],
            'scene_labels': strings('SceneLabels'), 'ops': ops, 'contacts': contacts, 'particles': particles,
            'emitted': [int(x) for x in re.findall(r'\d+', table('Emitted'))]}


def scene_model(text=None, guid=None):
    """The committed scene's documents: document order, fileIDs, hierarchy, component lists, slots."""
    text = text if text is not None else SCENE.read_text()
    guid = guid or re.search(r'guid: (\w+)', SCRIPT_META.read_text()).group(1)
    docs = []
    for m in re.finditer(r'^--- !u!(\d+) &(-?\d+)\n(.*?)(?=^--- |\Z)', text, re.S | re.M):
        docs.append((int(m.group(1)), int(m.group(2)), m.group(3)))
    gos, transforms, behaviours, roots = {}, {}, {}, []
    for cls, fid, body in docs:
        if cls == 1:
            gos[fid] = {'name': re.search(r'm_Name: (.*)', body).group(1).strip(),
                        'components': [int(x) for x in re.findall(r'- component: \{fileID: (-?\d+)\}', body)],
                        'active': re.search(r'm_IsActive: (\d)', body).group(1) == '1'}
        elif cls == 4:
            children = re.search(r'm_Children:(.*?)\n  m_Father', body, re.S).group(1)
            transforms[fid] = {'go': int(re.search(r'm_GameObject: \{fileID: (-?\d+)\}', body).group(1)),
                               'children': [int(x) for x in re.findall(r'\{fileID: (-?\d+)\}', children)],
                               'father': int(re.search(r'm_Father: \{fileID: (-?\d+)\}', body).group(1))}
        elif cls == 114:
            script = re.search(r'm_Script: \{fileID: 11500000, guid: (\w+), type: 3\}', body)
            behaviours[fid] = {'go': int(re.search(r'm_GameObject: \{fileID: (-?\d+)\}', body).group(1)),
                               'recorder': bool(script and script.group(1) == guid),
                               'slot': (re.search(r'^  slot: (.*)$', body, re.M) or [None, None])[1]}
        elif cls == 1660057539:
            roots = [int(x) for x in re.findall(r'\{fileID: (-?\d+)\}', body)]
    name = {fid: g['name'] for fid, g in gos.items()}
    labels = {}
    for tid, t in transforms.items():
        labels[tid] = name[t['go']]
    comp_list = {name[fid]: [f"{name[fid]}/{behaviours[c]['slot']}" for c in g['components'] if c in behaviours
                             and behaviours[c]['recorder']] for fid, g in gos.items()}
    children = {labels[tid]: [labels[c] for c in t['children']] for tid, t in transforms.items()}
    parents = {labels[tid]: (labels[t['father']] if t['father'] else None) for tid, t in transforms.items()}
    return {
        'documents': [[cls, fid] for cls, fid, _ in docs],
        'roots': [labels[r] for r in roots],
        'children': children, 'parents': parents, 'components': comp_list,
        'active': {g['name']: g['active'] for g in gos.values()},
        'doc_mb': [f"{name[b['go']]}/{b['slot']}" for cls, fid, _ in docs if cls == 114
                   for b in [behaviours[fid]] if b['recorder']],
        'doc_go': [name[fid] for cls, fid, _ in docs if cls == 1],
        'fileid_mb': [f"{name[behaviours[fid]['go']]}/{behaviours[fid]['slot']}" for fid in sorted(behaviours)
                      if behaviours[fid]['recorder']],
        'fileid_go': [name[fid] for fid in sorted(gos)],
        'transform_fileids': {labels[tid]: tid for tid in transforms},
        'gameobject_fileids': {g['name']: fid for fid, g in gos.items()},
        'recorder_fileids': {f"{name[b['go']]}/{b['slot']}": fid for fid, b in behaviours.items() if b['recorder']},
    }


# ---------------------------------------------------------------- trees and orders

def runtime_tree(t, tree):
    nodes = [n for n in t['nodes'] if n[0] == tree]
    children = {n[1]: [] for n in nodes}
    parents = {}
    for _, label, parent, _ in nodes:
        parents[label] = parent
        if parent is not None:
            children[parent].append(label)
    return {'children': children, 'parents': parents, 'components': {n[1]: [f'{n[1]}/r'] for n in nodes}}


def scene_tree(t, scene):
    components = {k: list(v) for k, v in scene['components'].items()}
    for label, slot in t['scene_adds']:
        components[label].append(f'{label}/{slot}')
    return {'children': scene['children'], 'parents': scene['parents'], 'components': components}


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


def levels(root, kids):
    out, frontier = [], [root]
    while frontier:
        out.append(frontier)
        frontier = [c for n in frontier for c in kids[n]]
    return out


def object_orders(root, kids):
    lv = levels(root, kids)
    return {'PRE': preorder(root, kids), 'POST': postorder(root, kids), 'REVPRE': preorder(root, kids)[::-1],
            'REVPOST': postorder(root, kids)[::-1], 'LEVEL': [n for level in lv for n in level],
            'REVLEVEL': [n for level in lv[::-1] for n in level], 'REVBFS': [n for level in lv for n in level][::-1]}


def expand(objects, components, reverse=False):
    return [r for o in objects for r in (components[o][::-1] if reverse else components[o])]


def entries(tokens, callback):
    return [{'n': tok.split('/')[0], 'r': tok.split('/')[1], 'cb': callback} for tok in tokens]


def authored_chronology(t, tree, tokens):
    """First Awake and last OnEnable as the construction statements imply (runtime trees)."""
    nodes = {n[1]: n for n in t['nodes'] if n[0] == tree}
    active = {label: n[3] for label, n in nodes.items()}
    parent = {label: n[2] for label, n in nodes.items()}

    def in_hierarchy(label):
        while label is not None:
            if not active[label]:
                return False
            label = parent[label]
        return True
    first, enables = [], []
    for label in t['add']:
        if label in nodes and in_hierarchy(label):
            first.append(f'{label}/r')
            enables.append(f'{label}/r')
    for label in t['wake']:
        if label in nodes:
            active[label] = True
            woke = [n for n in nodes if in_hierarchy(n) and f'{n}/r' not in first]
            # one woken node per wake step in this fixture (its own recorder)
            for n in woke:
                first.append(f'{n}/r')
                enables.append(f'{n}/r')
    for label in t['toggle']:
        if label in nodes:
            enables.append(f'{label}/r')
    last = []
    for tok in reversed(enables):
        if tok not in last:
            last.append(tok)
    last = last[::-1]
    return [x for x in first if x in tokens], [x for x in last if x in tokens]


def op_predictions(t, scene, op):
    name, tree, target, value = op
    cb = 'OnEnable' if value else 'OnDisable'
    shape = scene_tree(t, scene) if tree == 'scene' else runtime_tree(t, tree)
    kids, comps = shape['children'], shape['components']
    objects = object_orders(target, kids)
    tokens = expand(objects['PRE'], comps)
    orders = {}
    for trav, objs in objects.items():
        orders[trav] = expand(objs, comps)
        orders[trav+'_REVCOMP'] = expand(objs, comps, reverse=True)
    if tree == 'scene':
        runtime = [f'{a}/{s}' for a, s in t['scene_adds'] if f'{a}/{s}' in tokens]

        def scene_order(seq):
            return [x for x in seq if x in tokens]
        orders['DOCMB'] = scene_order(scene['doc_mb'])+runtime
        orders['REVDOCMB'] = runtime[::-1]+scene_order(scene['doc_mb'])[::-1]
        orders['FILEIDMB'] = scene_order(scene['fileid_mb'])+runtime
        orders['REVFILEIDMB'] = runtime[::-1]+scene_order(scene['fileid_mb'])[::-1]
        sub = set(objects['PRE'])
        orders['DOCGO'] = expand([g for g in scene['doc_go'] if g in sub], comps)
        orders['REVDOCGO'] = orders['DOCGO'][::-1]
        orders['FILEIDGO'] = expand([g for g in scene['fileid_go'] if g in sub], comps)
        orders['REVFILEIDGO'] = orders['FILEIDGO'][::-1]
    else:
        sub = set(objects['PRE'])
        orders['CREATE'] = expand([n for n in t['create'] if n in sub], comps)
        orders['REVCREATE'] = orders['CREATE'][::-1]
        orders['ADD'] = [f'{n}/r' for n in t['add'] if n in sub]
        orders['REVADD'] = orders['ADD'][::-1]
        first, last = authored_chronology(t, tree, tokens)
        orders['AFIRSTAWAKE'], orders['REVAFIRSTAWAKE'] = first, first[::-1]
        orders['ALASTENABLE'], orders['REVALASTENABLE'] = last, last[::-1]
    hyp = {h: entries(o, cb) for h, o in orders.items()}
    return {'tree': tree, 'target': target, 'value': value, 'callback': cb, 'objects': objects['PRE'],
            'recorders': tokens, 'components': {o: comps[o] for o in objects['PRE']},
            'hypotheses': hyp, 'cells': cells(hyp)}


def cells(hyp):
    """Groups of hypotheses that predict the same list (indistinguishable in this case)."""
    groups = {}
    for name, want in hyp.items():
        groups.setdefault(json.dumps(want, sort_keys=True), []).append(name)
    return sorted(groups.values(), key=lambda g: list(hyp).index(g[0]))


# ---------------------------------------------------------------- built-in models

def layout(kind, scripts_first):
    """Per role, the authored component order of a case (Transform omitted; the observer has no
    OnDisable). kind 'contact': P body, C circle, E box; 'particles': P and C systems."""
    if kind == 'contact':
        built = {'P': 'rb', 'C': 'col', 'E': 'box'}
        return {role: (['rec', 'obs', b] if scripts_first else [b, 'rec', 'obs']) for role, b in built.items()}
    return {role: (['rec', 'ps'] if scripts_first else ['ps', 'rec']) for role in ('P', 'C')}


def model_events(model, comps):
    """The (role, component) disable events of deactivating P (objects P and its child C)."""
    pre, post = ['P', 'C'], ['C', 'P']
    scripts = {'rec'}
    passive = {'obs'}

    def events(objs, keep, reverse=False):
        return [(o, c) for o in objs for c in (comps[o][::-1] if reverse else comps[o]) if keep(c) and c not in passive]
    if model == 'CUR':
        return events(pre, lambda c: True)
    if model == 'F':
        return events(post, lambda c: True)
    if model == 'F_REVCOMP':
        return events(post, lambda c: True, reverse=True)
    if model == 'S':
        return events(pre, lambda c: c not in scripts)+events(post, lambda c: c in scripts)
    if model == 'S_POST':
        return events(post, lambda c: c not in scripts)+events(post, lambda c: c in scripts)
    if model == 'SCRIPTS_FIRST':
        return events(post, lambda c: c in scripts)+events(pre, lambda c: c not in scripts)
    raise ValueError(model)


def contact_skeleton(model, placement, reads, comps):
    """(call tokens, sim3 tokens) predicted for one 2D case."""
    tokens, removed, exits_done = [], False, False
    for role, comp in model_events(model, comps):
        if comp in ('rb', 'col'):
            if placement == 'AT_REMOVAL' and not exits_done:
                tokens.append('EXITS')
                exits_done = True
            removed = True
        elif comp == 'rec':
            state = 'removed' if reads == 'FLAGS' or removed else 'live'
            tokens.append(f'{role}.OnDisable[{state}]')
    return tokens, (['EXITS'] if placement == 'DEFERRED' else [])


def particle_skeleton(model, reads, comps):
    tokens, off = [], set()
    for role, comp in model_events(model, comps):
        if comp == 'ps':
            off.add(role)
        elif comp == 'rec':
            state = ','.join(f'{r}:{"off" if reads == "FLAGS" or r in off else "live"}' for r in ('P', 'C'))
            tokens.append(f'{role}.OnDisable[{state}]')
    return tokens


def contact_predictions(case):
    name, trigger, callbacks, scripts_first = case
    comps = layout('contact', scripts_first)
    hyp = {}
    for model in ORDER_MODELS:
        for placement in PLACEMENTS:
            for reads in READS:
                call, sim3 = contact_skeleton(model, placement, reads, comps)
                hyp[f'{model}/{placement}/{reads}'] = {'call': call, 'sim3': sim3}
    return {'trigger': trigger, 'callbacksOnDisable': callbacks,
            'layout': 'scriptsFirst' if scripts_first else 'builtinsFirst', 'components': comps,
            'hypotheses': hyp, 'cells': cells(hyp)}


def particle_predictions(case):
    name, scripts_first = case
    comps = layout('particles', scripts_first)
    hyp = {f'{model}/{reads}': {'call': particle_skeleton(model, reads, comps)} for model in ORDER_MODELS for reads in READS}
    return {'layout': 'scriptsFirst' if scripts_first else 'builtinsFirst', 'components': comps,
            'hypotheses': hyp, 'cells': cells(hyp)}


def same_cell(cellset, a, b):
    return any(a in c and b in c for c in cellset)


def separations(result):
    """The separations this matrix claims, and the cells it retains as indistinguishable."""
    ops, contacts = result['operations'], result['contacts']
    claims = {
        'offA POST vs REVLEVEL': ('op.offAOff', 'POST', 'REVLEVEL'),
        'mirror POST vs REVLEVEL': ('op.mirrorOff', 'POST', 'REVLEVEL'),
        'offA POST vs CREATE/ADD': ('op.offAOff', 'POST', 'ADD'),
        'baseline POST vs ADD (on)': ('op.baseOn', 'POST', 'ADD'),
        'on1 ADD vs AFIRSTAWAKE': ('op.on1On', 'ADD', 'AFIRSTAWAKE'),
        'on1 ADD vs PRE': ('op.on1On', 'ADD', 'PRE'),
        'on1 AFIRSTAWAKE vs REVPOST': ('op.on1On', 'AFIRSTAWAKE', 'REVPOST'),
        'on3 ALASTENABLE vs AFIRSTAWAKE': ('op.on3On', 'ALASTENABLE', 'AFIRSTAWAKE'),
        'on3 ALASTENABLE vs ADD': ('op.on3On', 'ALASTENABLE', 'ADD'),
        'on2 DOCMB vs PRE': ('op.on2On', 'DOCMB', 'PRE'),
        'on2 DOCMB vs FILEIDMB': ('op.on2On', 'DOCMB', 'FILEIDMB'),
        'on2 DOCGO vs DOCMB': ('op.on2On', 'DOCGO', 'DOCMB'),
        'on2 PRE vs PRE_REVCOMP': ('op.on2On', 'PRE', 'PRE_REVCOMP'),
    }
    out = {'separated': {}, 'retained_indistinguishable': {}}
    for label, (key, a, b) in claims.items():
        out['separated'][label] = not same_cell(ops[key]['cells'], a, b)
    for name, case in contacts.items():
        f, s = 'F/AT_REMOVAL/REMOVAL', 'S/AT_REMOVAL/REMOVAL'
        verdict = not same_cell(case['cells'], f, s)
        (out['separated'] if case['layout'] == 'scriptsFirst' else out['retained_indistinguishable'])[
            f'contact.{name} F vs S (order and touching reads)'] = verdict if case['layout'] == 'scriptsFirst' else not verdict
    for name, case in result['particles'].items():
        # Under REMOVAL reads the child's OnDisable sees the parent system live (F) or off (S) in both layouts.
        out['separated'][f'particles.{name} F vs S (order and ParticleSystem reads)'] = \
            not same_cell(case['cells'], 'F/REMOVAL', 'S/REMOVAL')
        out['retained_indistinguishable'][f'particles.{name} S = every model under FLAGS reads'] = same_cell(
            case['cells'], 'S/REMOVAL', 'F/FLAGS')
    out['retained_indistinguishable']['on1 ALASTENABLE = AFIRSTAWAKE (no toggle)'] = same_cell(
        ops['op.on1On']['cells'], 'ALASTENABLE', 'AFIRSTAWAKE')
    out['retained_indistinguishable']['on1 LEVEL = PRE (flat tree)'] = same_cell(ops['op.on1On']['cells'], 'LEVEL', 'PRE')
    return out


def predictions():
    t = source_tables()
    scene = scene_model()
    result = {'source': str(SOURCE.relative_to(PROBE)), 'scene': str(SCENE.relative_to(PROBE)), 'tables': t,
              'scene_model': scene, 'outcomes': list(OUTCOMES), 'capture_derived': CAPTURE_DERIVED,
              'order_models': list(ORDER_MODELS), 'placements': list(PLACEMENTS), 'reads': list(READS),
              'operations': {}, 'contacts': {}, 'particles': {}}
    for op in t['ops']:
        result['operations'][f'op.{op[0]}'] = op_predictions(t, scene, op)
    for case in t['contacts']:
        result['contacts'][case[0]] = contact_predictions(case)
    for case in t['particles']:
        result['particles'][case[0]] = particle_predictions(case)
    result['separations'] = separations(result)
    result['notes'] = {
        'scope': 'per case; the default-order recorder class only; no answer is extrapolated to other script types, '
                 'execution orders, Destroy, re-parenting or other lifecycle paths',
        'ids': 'id hypotheses use the ids each capture records in diagnostics.jsonl; none are inferred from '
               'another probe or from document order',
        'first_wake': 'setup Awake order is recorded data; no child-first or parent-first rule is adopted',
        'particles': 'ParticleSystem reads observe one built-in; D-1 is not claimed for every OnEngineDisable override',
    }
    return result


def predict(out):
    data = predictions()
    failed = [k for k, v in data['separations']['separated'].items() if not v]
    if failed:
        raise SystemExit(f'claimed separations do not hold: {failed}')
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


def load_diagnostics(path):
    rows = {}
    try:
        for line in Path(path).read_text().splitlines():
            row = json.loads(line)
            rows.setdefault(row.get('what'), row.get('extra'))
    except (OSError, json.JSONDecodeError):
        pass
    return rows


def outcome(fits, cellset):
    if not fits:
        return 'NONE'
    hit = [c for c in cellset if set(c) & set(fits)]
    return 'ONE' if len(hit) == 1 and len(hit[0]) == 1 else 'MULTIPLE'


def chronology(values, t, upto):
    """Raw recorder entries in record order: boot, setup, settle, then earlier operations."""
    keys = ['boot.calls', 'setup.calls', 'settle.calls'] + [f'op.{op[0]}.calls' for op in t['ops']]
    out = []
    for k in keys:
        if k == upto:
            break
        out += [e for e in values.get(k, []) if isinstance(e, dict) and e.get('t') == 'recorder']
    return out


def derived_orders(values, diagnostics, t, key, pred):
    tokens = pred['recorders']
    comps = pred['components']
    trace = chronology(values, t, key+'.calls')
    orders, missing = {}, {}
    first = []
    for e in trace:
        tok = f"{e['n']}/{e['r']}"
        if e['cb'] == 'Awake' and tok in tokens and tok not in first:
            first.append(tok)
    last = []
    for e in trace:
        tok = f"{e['n']}/{e['r']}"
        if e['cb'] == 'OnEnable' and tok in tokens:
            if tok in last:
                last.remove(tok)
            last.append(tok)
    for name, seq in (('FIRSTAWAKE', first), ('LASTENABLE', last)):
        if sorted(seq) == sorted(tokens):
            orders[name], orders['REV'+name] = seq, seq[::-1]
        else:
            missing[name] = missing['REV'+name] = 'not every recorder has the event before the operation'
    ids = dict(diagnostics.get('ids:boot') or {})
    ids.update(diagnostics.get('ids:setup') or {})
    rec, go, tr = {}, {}, {}
    for obj in pred['objects']:
        row = ids.get(obj)
        if not isinstance(row, dict):
            continue
        go[obj], tr[obj] = row.get('go'), row.get('transform')
        for comp, ident in row.get('components', []):
            if comp.startswith(RECORDER_TYPE+':'):
                rec[f"{obj}/{comp.split(':', 1)[1]}"] = ident
    if sorted(rec) == sorted(tokens) and all(isinstance(v, int) for v in rec.values()):
        orders['IDDESC'] = sorted(tokens, key=lambda x: -rec[x])
        orders['IDASC'] = sorted(tokens, key=lambda x: rec[x])
    else:
        missing['IDDESC'] = missing['IDASC'] = 'no recorded recorder ids'
    for prefix, table in (('GOID', go), ('TRID', tr)):
        if sorted(table) == sorted(pred['objects']) and all(isinstance(v, int) for v in table.values()):
            orders[prefix+'DESC'] = expand(sorted(pred['objects'], key=lambda o: -table[o]), comps)
            orders[prefix+'ASC'] = expand(sorted(pred['objects'], key=lambda o: table[o]), comps)
        else:
            missing[prefix+'DESC'] = missing[prefix+'ASC'] = 'no recorded ids'
    return orders, missing, {'recorder_ids': rec, 'gameobject_ids': go, 'transform_ids': tr}


def tree_shape(rows):
    return [{k: r[k] for k in ('n', 'parent', 'sibling', 'depth', 'children', 'components')} for r in rows] \
        if isinstance(rows, list) else None


def expected_rows(roots, kids, comps):
    rows = []

    def visit(label, depth, sibling, parent):
        rows.append({'n': label, 'parent': parent, 'sibling': sibling, 'depth': depth, 'children': len(kids[label]),
                     'components': ['Transform']+[f'{RECORDER_TYPE}:{c.split("/")[1]}' for c in comps[label]]})
        for i, k in enumerate(kids[label]):
            visit(k, depth+1, i, label)
    for root in roots:
        visit(root, 0, None, None)
    return rows


def tree_ok(got, want):
    """Readback equals the authored tree; root sibling indices are not authored."""
    if got is None or len(got) != len(want):
        return False
    return all(g == dict(w, sibling=g['sibling'] if w['sibling'] is None else w['sibling'])
               and (w['sibling'] is not None or isinstance(g['sibling'], int)) for g, w in zip(got, want))


def op_precondition(values, key, pred):
    problems = []
    if not isinstance(values.get(key+'.calls'), list):
        problems.append('no calls list')
    for obj in pred['objects']:
        state = values.get(f'{key}.before.{obj}')
        if not isinstance(state, dict):
            problems.append(f'no before-state of {obj}')
            continue
        if state['activeInHierarchy'] == pred['value']:
            problems.append(f'{obj} activeInHierarchy is already {pred["value"]}')
        slots = [r['r'] for r in state['recorders']]
        if [f'{obj}/{s}' for s in slots] != pred['components'][obj]:
            problems.append(f'{obj} recorders {slots} are not the authored component list')
        for r in state['recorders']:
            if r['counts']['Awake'] != 1 or not r['enabled']:
                problems.append(f'{obj}/{r["r"]} awake {r["counts"]["Awake"]} times, enabled {r["enabled"]}')
    target = values.get(f'{key}.before.{pred["target"]}')
    if isinstance(target, dict) and target['activeSelf'] == pred['value']:
        problems.append(f'target {pred["target"]} activeSelf is already {pred["value"]}')
    return problems


def fit_operation(values, diagnostics, data, key, trees_ok):
    pred = data['operations'][key]
    t = data['tables']
    got = values.get(key+'.calls')
    entry = {'observed': got, 'returned': values.get(key+'.returned', values.get(key+'.returned.exception'))}
    problems = op_precondition(values, key, pred)
    if not trees_ok[pred['tree'] == 'scene']:
        problems.insert(0, 'the read-back tree is not the authored tree')
    if pred['tree'] in ('on1', 'on3'):
        hyp = pred['hypotheses']
        if hyp['ADD'] == hyp['AFIRSTAWAKE']:
            problems.append('component add and first Awake coincide')
    hyp = {k: v for k, v in pred['hypotheses'].items()}
    derived, missing, ids = derived_orders(values, diagnostics, t, key, pred)
    for name, order in derived.items():
        hyp[name] = entries(order, pred['callback'])
    entry['capture_derived'] = {k: [f"{e['n']}/{e['r']}" for e in v] for k, v in hyp.items() if k in derived}
    entry['not_evaluable_candidates'] = missing
    entry['ids'] = ids
    if pred['tree'] in ('on1', 'on3') and 'FIRSTAWAKE' in derived and \
            [f"{e['n']}/{e['r']}" for e in hyp['ADD']] == derived['FIRSTAWAKE']:
        problems.append('the actual first-Awake order equals the add order')
    if problems:
        entry.update(outcome='NOT_EVALUABLE', reasons=problems, fits=[])
        return entry
    seq = [{'n': e['n'], 'r': e['r'], 'cb': e['cb']} for e in got]
    fits = [h for h, want in hyp.items() if want == seq]
    allcells = cells(hyp)
    entry.update(fits=fits, outcome=outcome(fits, allcells), cells=[c for c in allcells if set(c) & set(fits)])
    return entry


def roles(prefix, labels):
    return {label: label[len(prefix):] for label in labels}


def annotate_contact(e, before, after):
    """live/removed from the entry's own touching read, if before/after show a change."""
    peer = e.get('peer') if isinstance(e.get('peer'), dict) else {}
    value = peer.get('touching')
    if before is True and after is False and isinstance(value, bool):
        return 'live' if value else 'removed'
    return '?'


def skeleton(calls, role_of, annotate):
    tokens, content = [], []
    for e in calls:
        if e['t'] == 'observer':
            if not tokens or tokens[-1] != 'EXITS':
                tokens.append('EXITS')
                content.append([])
            content[-1].append([role_of.get(e['n'], e['n']), e['cb'], role_of.get(e['other'], e['other']), e['contacts']])
        elif e['t'] == 'recorder':
            tokens.append(f"{role_of.get(e['n'], e['n'])}.{e['cb']}[{annotate(e)}]")
        else:
            tokens.append(f"{e['n']}.{e['cb']}")
    return tokens, content


def strip(tokens):
    return [re.sub(r'\[.*\]$', '', t) for t in tokens]


def components_ok(got, labels, comps, kind):
    """The read-back component lists keep the authored script/built-in order per role."""
    names = {'rec': RECORDER_TYPE+':r', 'obs': 'Contact2DObserver', 'rb': 'Rigidbody2D', 'col': 'CircleCollider2D',
             'box': 'BoxCollider2D', 'ps': 'ParticleSystem'}
    if not isinstance(got, dict):
        return ['no component read-back']
    problems = []
    for label, role in labels.items():
        lst = got.get(label)
        if not isinstance(lst, list):
            problems.append(f'no components of {label}')
            continue
        want = [names[c] for c in comps[role]]
        seen = [c for c in lst if c in want]
        if seen != want:
            problems.append(f'{label} components {lst} do not keep the authored order {want}')
    return problems


def fit_contact(values, data, index, name):
    pred = data['contacts'][name]
    key = f'contact.{name}'
    prefix = f'B{index}'
    role_of = roles(prefix, [prefix+r for r in ('P', 'C', 'E')])
    before = (values.get(key+'.before') or {}).get('physics') if isinstance(values.get(key+'.before'), dict) else None
    after = (values.get(key+'.after') or {}).get('physics') if isinstance(values.get(key+'.after'), dict) else None
    problems = components_ok(values.get(key+'.components'), role_of, pred['components'], 'contact')
    calls, sim3 = values.get(key+'.calls'), values.get(key+'.sim3.calls')
    if not isinstance(calls, list) or not isinstance(sim3, list):
        problems.append('no call/sim3 lists')
    if not isinstance(before, dict) or before.get('touching') is not True:
        problems.append(f'no contact before the call (touching {None if not isinstance(before, dict) else before.get("touching")})')
    nodes = (values.get(key+'.before') or {}).get('nodes', {}) if isinstance(values.get(key+'.before'), dict) else {}
    for label in (prefix+'P', prefix+'C'):
        st = nodes.get(label)
        if not isinstance(st, dict) or not st.get('activeInHierarchy') or not st['recorders'] or \
                st['recorders'][0]['counts']['Awake'] != 1:
            problems.append(f'{label} is not an awake active recorder before the call')
    entry = {'enter': skeleton(values.get(key+'.sim1.calls', []) + values.get(key+'.sim2.calls', []), role_of, lambda e: '')[1]
             if isinstance(values.get(key+'.sim1.calls'), list) else None}
    if problems:
        entry.update(outcome='NOT_EVALUABLE', reasons=problems, fits=[])
        return entry
    touch_before = before.get('touching')
    touch_after = after.get('touching') if isinstance(after, dict) else None
    call_tokens, call_content = skeleton(calls, role_of, lambda e: annotate_contact(e, touch_before, touch_after))
    sim3_tokens, sim3_content = skeleton(sim3, role_of, lambda e: '')
    sim3_tokens = ['EXITS' if t == 'EXITS' else t for t in sim3_tokens]
    annotated = all('[?]' not in t for t in call_tokens)
    entry.update(observed={'call': call_tokens, 'sim3': sim3_tokens}, exit_content={'call': call_content, 'sim3': sim3_content},
                 annotations='evaluated' if annotated else 'NOT_EVALUABLE: touching did not change from true to false')
    fits = []
    for h, want in pred['hypotheses'].items():
        got_call = call_tokens if annotated else strip(call_tokens)
        want_call = want['call'] if annotated else strip(want['call'])
        if got_call == want_call and [t for t in sim3_tokens if t == 'EXITS'] == want['sim3'] \
                and all(t == 'EXITS' for t in sim3_tokens):
            fits.append(h)
    allcells = pred['cells'] if annotated else cells({h: {'call': strip(w['call']), 'sim3': w['sim3']}
                                                      for h, w in pred['hypotheses'].items()})
    entry.update(fits=fits, outcome=outcome(fits, allcells), cells=[c for c in allcells if set(c) & set(fits)])
    return entry


def ps_class(read, before, after):
    if before == after:
        return '?'
    if read == before:
        return 'live'
    if read == after:
        return 'off'
    return 'other'


def fit_particles(values, data, index, name):
    pred = data['particles'][name]
    key = f'particles.{name}'
    prefix = f'F{index}'
    role_of = roles(prefix, [prefix+r for r in ('P', 'C')])
    problems = components_ok(values.get(key+'.components'), role_of, pred['components'], 'particles')
    b, a = values.get(key+'.before'), values.get(key+'.after')
    before = b.get('particles') if isinstance(b, dict) else None
    after = a.get('particles') if isinstance(a, dict) else None
    calls = values.get(key+'.calls')
    if not isinstance(calls, list) or not isinstance(before, dict) or not isinstance(after, dict):
        problems.append('no call list or particle states')
    entry = {}
    if problems:
        entry.update(outcome='NOT_EVALUABLE', reasons=problems, fits=[])
        return entry
    observable = {role: before.get(label) != after.get(label) for label, role in role_of.items()}
    entry['observable'] = observable
    if not any(observable.values()):
        entry.update(outcome='NOT_EVALUABLE', fits=[], reasons=[
            'no ParticleSystem state read changes on deactivation: D-1 is only partly discriminated (Physics2D); '
            'the missing observable is a ParticleSystem public state that its disable changes'])
        return entry

    def annotate(e):
        peer = e.get('peer') if isinstance(e.get('peer'), dict) else {}
        return ','.join(f'{role}:{ps_class(peer.get(label), before.get(label), after.get(label))}'
                        for label, role in sorted(role_of.items(), key=lambda kv: ('P', 'C').index(kv[1])))
    tokens, _ = skeleton(calls, role_of, annotate)
    entry['observed'] = tokens
    usable = [r for r in ('P', 'C') if observable[r]]

    def project(tok):
        m = re.match(r'(.*)\[(.*)\]$', tok)
        if not m:
            return tok
        parts = dict(p.split(':') for p in m.group(2).split(','))
        return m.group(1)+'['+','.join(f'{r}:{parts[r]}' for r in usable)+']'
    got = [project(t) for t in tokens]
    fits = [h for h, want in pred['hypotheses'].items() if [project(t) for t in want['call']] == got]
    allcells = cells({h: [project(t) for t in w['call']] for h, w in pred['hypotheses'].items()})
    entry.update(fits=fits, outcome=outcome(fits, allcells), cells=[c for c in allcells if set(c) & set(fits)],
                 annotated_systems=usable)
    return entry


def combined(entries_, names):
    evaluable = {k: e for k, e in entries_.items() if e['outcome'] != 'NOT_EVALUABLE'}
    if not evaluable:
        return {'outcome': 'NOT_EVALUABLE', 'cases': []}
    alive = set(names)
    for e in evaluable.values():
        alive &= set(e['fits'])
    survivors = [n for n in names if n in alive]
    return {'cases': sorted(evaluable), 'consistent': survivors,
            'outcome': 'NONE' if not survivors else 'ONE' if len(survivors) == 1 else 'MULTIPLE'}


def fit(path, report, diagnostics_path=None):
    data = predictions()
    t = data['tables']
    values = load(path)
    diagnostics = load_diagnostics(diagnostics_path or Path(path).with_name('diagnostics.jsonl'))
    runtime_roots = [n[1] for n in t['nodes'] if n[2] is None]
    runtime = {}
    for tree in dict.fromkeys(n[0] for n in t['nodes']):
        shape = runtime_tree(t, tree)
        runtime.update({'children': {**runtime.get('children', {}), **shape['children']},
                        'components': {**runtime.get('components', {}), **shape['components']}})
    scene = scene_tree(t, data['scene_model'])
    want_setup = expected_rows(runtime_roots, runtime['children'], runtime['components']) + \
        expected_rows(data['scene_model']['roots'], scene['children'], scene['components'])
    want_boot = expected_rows(data['scene_model']['roots'], scene['children'], data['scene_model']['components'])
    setup_ok = tree_ok(tree_shape(values.get('setup.tree')), want_setup)
    boot_ok = tree_ok(tree_shape(values.get('boot.scene')), want_boot)
    trees_ok = {False: setup_ok, True: setup_ok and boot_ok}
    out = {'record': str(path), 'setup_tree_matches_authored': setup_ok, 'scene_tree_matches_authored': boot_ok,
           'operations': {}, 'contacts': {}, 'particles': {}}
    for key in data['operations']:
        out['operations'][key] = fit_operation(values, diagnostics, data, key, trees_ok)
    for i, case in enumerate(t['contacts']):
        out['contacts'][case[0]] = fit_contact(values, data, i, case[0])
    for i, case in enumerate(t['particles']):
        out['particles'][case[0]] = fit_particles(values, data, i, case[0])
    ops = out['operations']
    off_keys = [k for k in ops if data['operations'][k]['value'] is False]
    on_keys = [k for k in ops if data['operations'][k]['value'] is True]
    def names(keys):
        """Every hypothesis name of these operations; a name some operation lacks cannot survive."""
        return list(dict.fromkeys([n for k in keys for n in data['operations'][k]['hypotheses']] + list(CAPTURE_DERIVED)))
    rank_keys = [k for k in on_keys if data['operations'][k]['tree'] in ('on1', 'on3', 'scene')]
    out['summary'] = {
        'off_traversal': combined({k: ops[k] for k in off_keys}, names(off_keys)),
        'on_rank': combined({k: ops[k] for k in rank_keys}, names(rank_keys)),
        'mirror_relation': {'offA': ops['op.offAOff'].get('fits'), 'mirror': ops['op.mirrorOff'].get('fits'),
                            'consistent': sorted(set(ops['op.offAOff'].get('fits', [])) & set(ops['op.mirrorOff'].get('fits', [])))},
    }
    # Built-in interleaving across cases: the order model part of each fitted name.
    order_fits = {}
    for k, v in out['contacts'].items():
        order_fits[f'contact.{k}'] = {'outcome': v['outcome'], 'fits': sorted({f.split('/')[0] for f in v['fits']})}
    for k, v in out['particles'].items():
        order_fits[f'particles.{k}'] = {'outcome': v['outcome'], 'fits': sorted({f.split('/')[0] for f in v['fits']})}
    out['summary']['builtin_interleaving'] = combined(order_fits, list(ORDER_MODELS))
    out['strayCalls'] = values.get('capture.strayCalls')
    out['scope'] = 'per case; no answer gates a capture or publication; fits are fixture-scoped consistency'
    Path(report).write_text(json.dumps(out, indent=1)+'\n')
    for group in ('operations', 'contacts', 'particles'):
        for key, entry in out[group].items():
            print(f'{key}: {entry["outcome"]} {entry.get("fits")}')
    for key, entry in out['summary'].items():
        print(f'summary {key}: {entry}')
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('predict', 'fit'))
    parser.add_argument('path', nargs='?')
    parser.add_argument('--report')
    parser.add_argument('--diagnostics')
    args = parser.parse_args()
    if args.mode == 'predict':
        predict(args.path or PROBE/'PREDICTIONS.json')
        return 0
    if not args.path or not args.report:
        parser.error('fit needs VALUES.jsonl and --report')
    return fit(args.path, args.report, args.diagnostics)


if __name__ == '__main__':
    sys.exit(main())
