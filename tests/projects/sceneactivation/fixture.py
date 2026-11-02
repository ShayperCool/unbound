#!/usr/bin/env python3
"""Derives the witness's object bindings and its 28 candidate orders from the committed fixture.

    fixture.py write [--audit PREDICTIONS.json]   writes fixture.json and predictions.json
    fixture.py check [--audit PREDICTIONS.json]   recomputes both and fails if they differ

Everything is read back from the committed bytes (Assets/Scenes/Wake.unity, the prefabs, the
script sources and every .meta); make_fixture.py's table is NOT imported. fixture.json records:

* `inputs`: sha256 of every file under Assets/, Packages/ and ProjectSettings/ (the fixture
  manifest every launch is checked against before and after);
* per marker object (path from the scene root): kind (plain object or prefab-instance object),
  marker class and its script execution order, the scene document position of its object group
  (`doc`: plain GameObject document, or the outermost PrefabInstance document), its numeric
  local fileID key (`fid`: plain GameObject fileID, or the outermost PrefabInstance fileID), its
  hierarchy pre-order index (`pre`), its rank inside the instance's prefab expansion (`r`) and
  whether it comes from a nested instance (`nested`), the actual fileIDs of its GameObject,
  Transform and MonoBehaviour (plain) or its PrefabInstance id, source prefab and source fileIDs
  (instance), and the GlobalObjectId fields predicted for it (see contract.py for which gate);
* the expected root set/order (SceneRoots) and pre-order paths.

predictions.json holds the audit's 7 base hypotheses x {+/- execution-order grouping} x {+/- type
grouping} = 28 orders for Awake/OnEnable (also compared with Start), evaluated on THIS object
table, and their pairwise collisions. These are candidates, not an exhaustive set of mechanisms.
"""
import argparse
import hashlib
import itertools
import json
from pathlib import Path
import re
import sys

PROBE = Path(__file__).resolve().parent
SCENE = 'Assets/Scenes/Wake.unity'
COPY = 'Assets/Scenes/WakeCopy.unity'
MASK = 0x7FFFFFFFFFFFFFFF
DOC = re.compile(r'^--- !u!(\d+) &(-?\d+)( stripped)?$', re.M)
REF = re.compile(r'\{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}), type: \d+)?\}')


def sha(data):
    return hashlib.sha256(data).hexdigest()


def manifest(root=PROBE):
    files = {}
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        for path in sorted((root/part).rglob('*')):
            if path.is_file():
                files[str(path.relative_to(root))] = sha(path.read_bytes())
    return files


def documents(path):
    """[(class id, fileID, stripped, body text)] in document order."""
    text = path.read_text()
    marks = list(DOC.finditer(text))
    return [(int(m.group(1)), int(m.group(2)), bool(m.group(3)),
             text[m.end():marks[i+1].start() if i+1 < len(marks) else len(text)])
            for i, m in enumerate(marks)]


def field_ref(body, key):
    m = re.search(r'^ +'+re.escape(key)+r': '+REF.pattern, body, re.M)
    return (int(m.group(1)), m.group(2)) if m else None


def ref_list(body, key):
    m = re.search(r'^  '+re.escape(key)+r':\n((?:  - .*\n)*)', body, re.M)
    return [int(r.group(1)) for r in REF.finditer(m.group(1))] if m else []


def name_of(body):
    m = re.search(r'^  m_Name: (.*)$', body, re.M)
    return m.group(1) if m else None


def modifications(body):
    """[(target fileID, target guid, propertyPath, value)]."""
    out = []
    for m in re.finditer(r'    - target: '+REF.pattern+r'\n      propertyPath: (.*)\n      value: (.*)\n', body):
        out.append((int(m.group(1)), m.group(2), m.group(3), m.group(4)))
    return out


def added_objects(body):
    m = re.search(r'    m_AddedGameObjects:\n((?:    - [\s\S]*?)?)    m_AddedComponents:', body)
    if not m:
        return []
    return [(int(t.group(1)), int(a.group(1))) for t, a in zip(
        re.finditer(r'targetCorrespondingSourceObject: '+REF.pattern, m.group(1)),
        re.finditer(r'addedObject: '+REF.pattern, m.group(1)))]


class Assets:
    def __init__(self, root):
        self.root = root
        self.by_guid = {}
        for meta in (root/'Assets').rglob('*.meta'):
            m = re.search(r'^guid: ([0-9a-f]{32})', meta.read_text(), re.M)
            self.by_guid[m.group(1)] = meta.with_suffix('')
        self.prefabs = {}

    def script(self, guid):
        path = self.by_guid[guid]
        text = path.read_text()
        cls = re.search(r'^public class (\w+) : MonoBehaviour', text, re.M).group(1)
        order = re.search(r'^\[DefaultExecutionOrder\((-?\d+)\)\]', text, re.M)
        return {'class': cls, 'exec': int(order.group(1)) if order else 0,
                'script': str(path.relative_to(self.root)), 'script_guid': guid}

    def prefab(self, guid):
        """The expansion of a prefab asset: pre-order nodes with ids local to that asset."""
        if guid in self.prefabs:
            return self.prefabs[guid]
        path = self.by_guid[guid]
        docs = documents(path)
        by_id = {d[1]: d for d in docs}
        nodes = []

        def own(t_id):
            _, _, _, body = by_id[t_id]
            go = field_ref(body, 'm_GameObject')[0]
            go_body = by_id[go][3]
            mbs = [c for c in ref_list(go_body, 'm_Component') if by_id[c][0] == 114]
            if len(mbs) != 1:
                raise SystemExit(f'{path}: {name_of(go_body)} must carry exactly one marker')
            info = self.script(field_ref(by_id[mbs[0]][3], 'm_Script')[1])
            return go_body, {'name': name_of(go_body), **info, 'go': go, 't': t_id, 'mb': mbs[0]}

        def walk(t_id, prefix, nested):
            cls, _, stripped, body = by_id[t_id]
            if stripped:
                pi = field_ref(body, 'm_PrefabInstance')[0]
                source_t = field_ref(body, 'm_CorrespondingSourceObject')[0]
                pi_body = by_id[pi][3]
                inner = self.prefab(field_ref(pi_body, 'm_SourcePrefab')[1])
                if inner[0]['t'] != source_t:
                    raise SystemExit(f'{path}: nested instance {pi} is not attached by its root')
                rename = {t: v for t, _, p, v in modifications(pi_body) if p == 'm_Name'}
                root_name = rename.get(inner[0]['go'], inner[0]['name'])
                for i, node in enumerate(inner):
                    local = dict(node)
                    tail = node['rel'].split('/', 1)
                    local['name'] = root_name if i == 0 else node['name']
                    local['rel'] = (prefix+'/' if prefix else '')+root_name+('/'+tail[1] if len(tail) > 1 else '')
                    for key in ('go', 't', 'mb'):
                        local[key] = (pi ^ node[key]) & MASK
                    local['nested'] = True
                    nodes.append(local)
                return
            _, node = own(t_id)
            node['rel'] = (prefix+'/' if prefix else '')+node['name']
            node['nested'] = nested
            nodes.append(node)
            for child in ref_list(body, 'm_Children'):
                walk(child, node['rel'], nested)

        roots = [d[1] for d in docs if d[0] == 4 and not d[2] and field_ref(d[3], 'm_Father')[0] == 0]
        if len(roots) != 1:
            raise SystemExit(f'{path}: expected one root Transform')
        walk(roots[0], '', False)
        for node in nodes:
            node['source'] = str(path.relative_to(self.root))
            node['source_guid'] = guid
        self.prefabs[guid] = nodes
        return nodes


def scene_objects(root=PROBE):
    assets = Assets(root)
    scene_path = root/SCENE
    docs = documents(scene_path)
    by_id = {d[1]: d for d in docs}
    position = {d[1]: i for i, d in enumerate(docs)}
    scene_guid = re.search(r'^guid: ([0-9a-f]{32})', (root/(SCENE+'.meta')).read_text(), re.M).group(1)
    # Object groups in document order: a plain GameObject, or an outermost PrefabInstance.
    groups = [d[1] for d in docs if (d[0] == 1 and not d[2]) or d[0] == 1001]
    group_doc = {g: i for i, g in enumerate(groups)}
    scene_roots = next(d for d in docs if d[0] == 1660057539)
    root_ids = ref_list(scene_roots[3], 'm_Roots')
    stripped = {d[1]: (field_ref(d[3], 'm_PrefabInstance')[0], field_ref(d[3], 'm_CorrespondingSourceObject')[0])
                for d in docs if d[2]}
    objects, order = [], []

    def plain(t_id, prefix):
        body = by_id[t_id][3]
        go = field_ref(body, 'm_GameObject')[0]
        go_body = by_id[go][3]
        mbs = [c for c in ref_list(go_body, 'm_Component') if by_id[c][0] == 114]
        if len(mbs) != 1:
            raise SystemExit(f'{scene_path}: {name_of(go_body)} must carry exactly one marker')
        info = assets.script(field_ref(by_id[mbs[0]][3], 'm_Script')[1])
        path = (prefix+'/' if prefix else '')+name_of(go_body)
        objects.append({'n': path, 'k': 'plain', 'c': info['class'], 'x': info['exec'],
                        'fid': go, 'doc': group_doc[go], 'pre': len(objects),
                        'ids': {'go': go, 't': t_id, 'mb': mbs[0]},
                        'documents': {'go': position[go], 't': position[t_id], 'mb': position[mbs[0]]},
                        'script': info['script'], 'script_guid': info['script_guid'],
                        'globalobjectid': {key: f'GlobalObjectId_V1-2-{scene_guid}-{value}-0'
                                           for key, value in (('go', go), ('tr', t_id), ('mb', mbs[0]))},
                        'gate_globalobjectid': ['go', 'tr', 'mb']})
        for child in ref_list(body, 'm_Children'):
            if child in stripped:
                raise SystemExit('a prefab instance under a plain object is not part of this fixture')
            plain(child, path)

    def instance(pi, prefix):
        body = by_id[pi][3]
        if field_ref(body, 'm_TransformParent')[0] != 0:
            raise SystemExit('only root prefab instances are part of this fixture')
        guid = field_ref(body, 'm_SourcePrefab')[1]
        nodes = assets.prefab(guid)
        rename = {t: v for t, _, p, v in modifications(body) if p == 'm_Name'}
        root_name = rename.get(nodes[0]['go'], nodes[0]['name'])
        added = {}
        for target, obj in added_objects(body):
            added.setdefault(target, []).append(obj)
        for r, node in enumerate(nodes):
            rel = node['rel'].split('/', 1)
            path = (prefix+'/' if prefix else '')+root_name+('/'+rel[1] if len(rel) > 1 else '')
            objects.append({'n': path, 'k': 'inst', 'c': node['class'], 'x': node['exec'],
                            'fid': pi, 'doc': group_doc[pi], 'pre': len(objects), 'r': r,
                            'nested': node['nested'],
                            'ids': {'pi': pi, 'source': node['source'], 'source_guid': node['source_guid'],
                                    'source_go': node['go'], 'source_t': node['t'], 'source_mb': node['mb']},
                            'documents': {'pi': position[pi]},
                            'script': node['script'], 'script_guid': node['script_guid'],
                            'globalobjectid': {key: f'GlobalObjectId_V1-2-{scene_guid}-{node[src]}-{pi}'
                                               for key, src in (('go', 'go'), ('tr', 't'), ('mb', 'mb'))},
                            'gate_globalobjectid': []})
        # Added GameObjects (insertIndex -1): appended as the last children of their target.
        for target, objs in added.items():
            owner = next(n for n in nodes if n['t'] == target)
            owner_path = next(o['n'] for o in objects if o.get('ids', {}).get('pi') == pi
                              and o['ids']['source_t'] == owner['t'])
            insert_after = max(i for i, o in enumerate(objects)
                               if o['n'] == owner_path or o['n'].startswith(owner_path+'/'))
            for obj in objs:
                start = len(objects)
                plain(obj, owner_path)
                moved = objects[start:]
                del objects[start:]
                objects[insert_after+1:insert_after+1] = moved
                insert_after += len(moved)

    for rid in root_ids:
        cls = by_id[rid][0]
        start = len(objects)
        if cls == 4:
            plain(rid, '')
        elif cls == 1001:
            instance(rid, '')
        else:
            raise SystemExit(f'unexpected SceneRoots entry {rid}')
        order.append(objects[start]['n'])
    for i, o in enumerate(objects):
        o['pre'] = i
    unlisted = [d[1] for d in docs if d[0] == 4 and not d[2] and field_ref(d[3], 'm_Father')[0] == 0
                and d[1] not in root_ids]
    if unlisted:
        raise SystemExit(f'root Transforms missing from SceneRoots: {unlisted}')
    return {'scene': SCENE, 'scene_guid': scene_guid,
            'copy_guid': re.search(r'^guid: ([0-9a-f]{32})', (root/(COPY+'.meta')).read_text(), re.M).group(1),
            'roots': order, 'tree': [o['n'] for o in objects], 'objects': objects}


# ---------------------------------------------------------------- the 28 candidates

def within(o, nested_first):
    return ((0 if o.get('nested') else 1) if nested_first else 0, o.get('r', 0))


def base(key, nested_first=False):
    return lambda o: (o[key],)+within(o, nested_first)


HYPOTHESES = {
    'document (outer-first)': base('doc'),
    'document (nested-first)': base('doc', True),
    'fileID (outer-first)': base('fid'),
    'fileID (nested-first)': base('fid', True),
    'hierarchy pre-order': lambda o: (o['pre'],),
    'Unbound creation (plain docs, then instances by doc)': lambda o: (o['k'] != 'plain', o['doc'])+within(o, False),
    'instances first (fileID, nested-first), then plain by fileID': lambda o: (o['k'] == 'plain', o['fid'])+within(o, True),
}


def ordered(objects, key, exec_grouping=False, type_grouping=False):
    seq = sorted(objects, key=key)
    if type_grouping:
        first = []
        for o in seq:
            if o['c'] not in first:
                first.append(o['c'])
        seq = sorted(seq, key=lambda o: first.index(o['c']))
    if exec_grouping:
        seq = sorted(seq, key=lambda o: o['x'])
    return [o['n'] for o in seq]


def predictions(objects):
    out = {}
    for name, key in HYPOTHESES.items():
        for execg, typeg in itertools.product((False, True), repeat=2):
            out[name+(' +exec' if execg else '')+(' +type' if typeg else '')] = ordered(objects, key, execg, typeg)
    seqs = {}
    for k, v in out.items():
        seqs.setdefault(tuple(v), []).append(k)
    return out, [ks for ks in seqs.values() if len(ks) > 1]


def build(audit=None):
    table = scene_objects()
    preds, collisions = predictions(table['objects'])
    fixture = {'format': 1, 'inputs': manifest(), **table}
    result = {'format': 1, 'count': len(preds), 'distinct': not collisions, 'collisions': collisions,
              'applies_to': ['Wake Awake', 'Wake OnEnable', 'Wake Start',
                             'WakeCopy Awake', 'WakeCopy OnEnable', 'WakeCopy Start'],
              'note': 'Candidates, not an exhaustive set of mechanisms. None or several matches is a valid result.',
              'predictions': preds}
    if audit:
        data = json.loads(Path(audit).read_text())
        result['audit'] = {'path': str(audit), 'sha256': sha(Path(audit).read_bytes()),
                           'equal': data['predictions'] == preds,
                           'differing': sorted(k for k in set(data['predictions']) | set(preds)
                                               if data['predictions'].get(k) != preds.get(k))}
    return fixture, result


def dump(value):
    return json.dumps(value, indent=1)+'\n'


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('write', 'check'))
    parser.add_argument('--audit', type=Path)
    args = parser.parse_args()
    fixture, result = build(args.audit)
    # fixture.json must not list itself or predictions.json (they live outside Assets/...).
    if args.mode == 'write':
        (PROBE/'fixture.json').write_text(dump(fixture))
        (PROBE/'predictions.json').write_text(dump(result))
    else:
        for name, value in (('fixture.json', fixture), ('predictions.json', result)):
            committed = json.loads((PROBE/name).read_text())
            if name == 'predictions.json' and 'audit' not in value:
                committed.pop('audit', None)
            if committed != value:
                sys.exit(f'{name} does not match the committed fixture')
    for name, seq in result['predictions'].items():
        print(f'{name:64} {" ".join(seq)}')
    print('distinct:', result['distinct'], 'collisions:', result['collisions'])
    if 'audit' in result:
        print('audit predictions equal:', result['audit']['equal'], result['audit']['differing'])
    return int(bool(result['collisions']))


if __name__ == '__main__':
    sys.exit(main())
