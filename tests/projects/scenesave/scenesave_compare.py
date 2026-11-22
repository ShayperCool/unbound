"""Compares scenesave captures: rows by key, written files byte for byte.

The Editor gives new objects random file ids, new managed references ids from its clock and new
assets random GUIDs, so two Editor captures differ there (tests/projects/scenesave README). The
comparison therefore maps the ids of the actual file onto the reference's: ids both files have stay
as they are; a new object's document is matched to the reference document of the same class whose
body agrees once the ids mapped so far are applied, repeatedly, and its managed reference ids by
their order of first use; a reference into an asset the run created (its GUID, from its .meta, and
its file ids) is mapped onto the reference's asset at the same path, and the modifications of an
instance of such an asset, in the order of its random target ids, are ordered by the mapped ids. The mapped file must then equal the reference byte for byte, and each
file's documents must be in the Editor's order: a scene's by ascending file id; a prefab's by
GameObject, the GameObjects by ascending file id, each followed by its components in its
m_Component order, then its PrefabInstance documents by file id, then the other documents by file
id, a stripped GameObject followed by the documents on it (prefab-nested Outer.prefab and
RichVariant.prefab in two captures whose random ids order them differently). A row holding a
file hash is compared by which hashes of the run are equal, not by the value.

Every difference is reported. Those named exactly (case, key, reference and actual value) in the
reference's known-gaps.json, each with its reason, are queued; any other difference, or a gap that
no longer occurs, fails the comparison.
"""
import difflib
import json
import re
from pathlib import Path

HEADER = re.compile(r'^--- !u!(\d+) &(-?\d+)( stripped)?$')
FILE_ID = re.compile(r'(fileID: |&)(-?\d+)')
RID = re.compile(r'(rid: )(-?\d+)')
HASH = re.compile(r'^[0-9a-f]{64}$')
GUID_LINE = re.compile(r'^guid: [0-9a-f]{32}$', re.M)
GUID_VALUE = re.compile(r'^guid: ([0-9a-f]{32})$', re.M)
ASSET_REF = re.compile(r'\{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: (\d+)\}')


def documents(text):
    """[(class id, file id, stripped, text)] in file order, and the preamble."""
    docs, preamble, current = [], [], None
    for line in text.split('\n'):
        m = HEADER.match(line)
        if m:
            current = [int(m.group(1)), int(m.group(2)), bool(m.group(3)), [line]]
            docs.append(current)
        elif current is None:
            preamble.append(line)
        else:
            current[3].append(line)
    return [(c, i, s, '\n'.join(lines)) for c, i, s, lines in docs], '\n'.join(preamble)


def rids_in_order(text):
    seen = []
    for m in RID.finditer(text):
        value = int(m.group(2))
        if value >= 0 and value not in seen:
            seen.append(value)
    return seen


def signature(doc, mapping, own):
    """A document's text with its file ids replaced: mapped ones by their image, others by '?',
    and every managed reference id by '?'."""
    cls, _, stripped, text = doc
    body = text.split('\n', 1)[1] if '\n' in text else ''

    def ids(m):
        value = int(m.group(2))
        image = mapping.get(value) if not own else (value if value in mapping else None)
        return m.group(1) + ('?' if image is None else str(image))
    body = FILE_ID.sub(ids, body)
    body = RID.sub(lambda m: m.group(1) + ('?' if int(m.group(2)) >= 0 else m.group(2)), body)
    return (cls, stripped, body)


def map_ids(reference, actual):
    """The actual file's ids mapped onto the reference's (file ids, then managed reference ids)."""
    ref_docs, _ = documents(reference)
    act_docs, _ = documents(actual)
    ref_ids = {d[1] for d in ref_docs}
    mapping = {d[1]: d[1] for d in act_docs if d[1] in ref_ids}
    images = set(mapping.values())
    while True:
        open_ref = [d for d in ref_docs if d[1] not in images]
        open_act = [d for d in act_docs if d[1] not in mapping]
        if not open_ref or not open_act:
            break
        inverse = {v: v for v in images}
        ref_sigs = {}
        for d in open_ref:
            ref_sigs.setdefault(signature(d, inverse, True), []).append(d)
        act_sigs = {}
        for d in open_act:
            act_sigs.setdefault(signature(d, mapping, False), []).append(d)
        progress = False
        for sig, docs in act_sigs.items():
            if len(docs) == 1 and len(ref_sigs.get(sig, [])) == 1:
                mapping[docs[0][1]] = ref_sigs[sig][0][1]
                images.add(ref_sigs[sig][0][1])
                progress = True
        if not progress:
            # Ambiguous or differing documents: pair the rest by class, in file order.
            for d in open_act:
                for r in open_ref:
                    if r[0] == d[0] and r[1] not in images:
                        mapping[d[1]] = r[1]
                        images.add(r[1])
                        break
            break
    # Documents with no counterpart (a difference): placeholder ids after the reference's, in an
    # order of their content, so a reported difference reads the same in every run.
    unmatched = [d for d in act_docs if d[1] not in mapping]
    unmatched.sort(key=lambda d: (d[0], signature(d, {}, False)))
    base = max([d[1] for d in ref_docs] + [0])
    for k, d in enumerate(unmatched):
        mapping[d[1]] = base + 1 + k
    rid_map = {}
    by_id = {d[1]: d for d in ref_docs}
    for d in act_docs:
        target = by_id.get(mapping.get(d[1]))
        if target is None:
            continue
        ref_rids = rids_in_order(target[3])
        for a, r in zip(rids_in_order(d[3]), ref_rids):
            rid_map.setdefault(a, r)
    return mapping, rid_map


COMPONENT = re.compile(r'^  - component: \{fileID: (-?\d+)\}$', re.M)
OWNER = re.compile(r'^  m_GameObject: \{fileID: (-?\d+)\}$', re.M)


def ordered(docs, prefab):
    """The documents in the Editor's order for the file's kind."""
    if not prefab:
        return sorted(docs, key=lambda d: d[1])
    by_id = {d[1]: d for d in docs}
    out, placed = [], set()
    for go in sorted((d for d in docs if d[0] == 1 and not d[2]), key=lambda d: d[1]):
        out.append(go)
        placed.add(go[1])
        for m in COMPONENT.finditer(go[3]):
            c = by_id.get(int(m.group(1)))
            if c is not None and c[1] not in placed:
                out.append(c)
                placed.add(c[1])
    out.extend(sorted((d for d in docs if d[0] == 1001 and d[1] not in placed), key=lambda d: d[1]))
    placed.update(d[1] for d in docs if d[0] == 1001)
    stripped_gos = {d[1] for d in docs if d[0] == 1 and d[2]}
    on_stripped = {}
    for d in docs:
        m = OWNER.search(d[3])
        if d[1] not in placed and m and int(m.group(1)) in stripped_gos:
            on_stripped.setdefault(int(m.group(1)), []).append(d)
    attached = {d[1] for group in on_stripped.values() for d in group}
    for d in sorted((d for d in docs if d[1] not in placed and d[1] not in attached), key=lambda d: d[1]):
        out.append(d)
        out.extend(sorted(on_stripped.get(d[1], []), key=lambda c: c[1]))
    return out


def apply_mapping(actual, mapping, rid_map, prefab):
    docs, preamble = documents(actual)
    renamed = []
    for cls, fid, stripped, text in docs:
        text = FILE_ID.sub(lambda m: m.group(1) + str(mapping.get(int(m.group(2)), int(m.group(2)))), text)
        text = RID.sub(lambda m: m.group(1) + str(rid_map.get(int(m.group(2)), int(m.group(2)))), text)
        renamed.append((cls, mapping.get(fid, fid), stripped, text))
    return '\n'.join([preamble] + [d[3] for d in ordered(renamed, prefab)])


def in_order(text, prefab):
    docs = documents(text)[0]
    return [d[1] for d in docs] == [d[1] for d in ordered(docs, prefab)]


def created_assets(rf, af):
    """The assets both runs created (a .meta each), as (GUID map actual -> reference, file id map
    of each actual GUID): references into them are mapped before a file is compared."""
    def guids(files):
        out = {}
        for rel, path in files.items():
            if rel.endswith('.meta') and not rel.startswith('snapshot:'):
                m = GUID_VALUE.search(path.read_text('utf-8', 'replace'))
                if m:
                    out[rel[:-len('.meta')]] = m.group(1)
        return out
    ref_guids, act_guids = guids(rf), guids(af)
    guid_map = {act_guids[p]: ref_guids[p] for p in act_guids if p in ref_guids}
    id_maps = {}
    for p, guid in act_guids.items():
        if p not in ref_guids or p not in rf or p not in af:
            continue
        try:
            ref_t, act_t = rf[p].read_text('utf-8'), af[p].read_text('utf-8')
        except UnicodeDecodeError:
            continue
        if ref_t.startswith('%YAML') and act_t.startswith('%YAML'):
            id_maps[guid] = map_ids(ref_t, map_asset_refs(act_t, guid_map, {}))[0]
    return guid_map, id_maps


def map_asset_refs(text, guid_map, id_maps):
    """References into created assets renamed onto the reference's GUIDs and file ids."""
    if not guid_map:
        return text

    def rename(m):
        fid, guid = int(m.group(1)), m.group(2)
        if guid not in guid_map:
            return m.group(0)
        fid = id_maps.get(guid, {}).get(fid, fid)
        return f'{{fileID: {fid}, guid: {guid_map[guid]}, type: {m.group(3)}}}'
    return ASSET_REF.sub(rename, text)


MODIFICATION = re.compile(r'^    - target: \{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: \d+\}$')


def modification_lists(lines):
    """Each m_Modifications list of a text: the (start, end) line ranges of its entries."""
    lists, i = [], 0
    while i < len(lines):
        if lines[i] != '    m_Modifications:':
            i += 1
            continue
        i += 1
        entries = []
        while i < len(lines) and lines[i].startswith('    - target: '):
            start = i
            i += 1
            while i < len(lines) and lines[i].startswith('      '):
                i += 1
            entries.append((start, i))
        lists.append(entries)
    return lists


def created_modifications_in_order(text, guid_map):
    """Whether each modification list of an instance of an asset the run created is in the
    order of its own (random) target ids, as the Editor orders them by target file id."""
    lines = text.split('\n')
    for entries in modification_lists(lines):
        keys = [MODIFICATION.match(lines[a]) for a, _ in entries]
        if not entries or not all(k and k.group(2) in guid_map for k in keys):
            continue
        ids = [int(k.group(1)) for k in keys]
        if ids != sorted(ids):
            return False
    return True


def sort_created_modifications(text, created_guids):
    """Mapped modification lists of created assets, stably sorted by their (mapped) target ids."""
    lines = text.split('\n')
    out = []
    i = 0
    for entries in modification_lists(lines):
        keys = [MODIFICATION.match(lines[a]) for a, _ in entries]
        if not entries or not all(k and k.group(2) in created_guids for k in keys):
            continue
        first, last = entries[0][0], entries[-1][1]
        out.extend(lines[i:first])
        blocks = [(int(k.group(1)), lines[a:b]) for k, (a, b) in zip(keys, entries)]
        blocks.sort(key=lambda block: block[0])
        for _, block in blocks:
            out.extend(block)
        i = last
    out.extend(lines[i:])
    return '\n'.join(out)


def compare_file(reference, actual, path, created=({}, {})):
    """None when equal (after the id mapping), else a short description."""
    ref, act = reference.read_bytes(), actual.read_bytes()
    if ref == act:
        return None
    if path.endswith('.meta'):
        ref_t, act_t = ref.decode('utf-8', 'replace'), act.decode('utf-8', 'replace')
        if GUID_LINE.sub('guid: G', ref_t) == GUID_LINE.sub('guid: G', act_t):
            return None
        return 'differs'
    try:
        ref_t, act_t = ref.decode('utf-8'), act.decode('utf-8')
    except UnicodeDecodeError:
        return 'differs (binary)'
    if not ref_t.startswith('%YAML'):
        return 'differs'
    # The final line break belongs to the file, not to its last document (a reordered prefab
    # would move it).
    if ref_t.endswith('\n') != act_t.endswith('\n'):
        return 'the final line break differs'
    ref_t = ref_t[:-1] if ref_t.endswith('\n') else ref_t
    act_t = act_t[:-1] if act_t.endswith('\n') else act_t
    if created[0]:
        if not created_modifications_in_order(act_t, created[0]):
            return 'modifications not in the Editor\'s order'
        act_t = sort_created_modifications(map_asset_refs(act_t, *created), set(created[0].values()))
    prefab = ref_t.count('\n--- !u!1 &') > 0 and '\n--- !u!1660057539 ' not in ref_t and '\nOcclusionCullingSettings:' not in ref_t
    if not in_order(act_t, prefab):
        return 'documents not in the Editor\'s order'
    mapping, rid_map = map_ids(ref_t, act_t)
    mapped = apply_mapping(act_t, mapping, rid_map, prefab)
    if mapped == ref_t:
        return None
    # Every differing line (a queued gap then names them all, so any further difference is new).
    out = []
    matcher = difflib.SequenceMatcher(a=ref_t.split('\n'), b=mapped.split('\n'), autojunk=False)
    for tag, i1, i2, j1, j2 in matcher.get_opcodes():
        if tag != 'equal':
            where = f'lines {i1 + 1}-{i2}' if i2 > i1 else f'after line {i1}'
            out.append(f'{where}: reference {matcher.a[i1:i2]!r}, actual {matcher.b[j1:j2]!r}')
    return '; '.join(out)


def load_rows(path):
    rows = {}
    for line in Path(path).read_text().splitlines():
        if line.strip():
            row = json.loads(line)
            rows.setdefault(row['key'], []).append(row['value'])
    return rows


def hash_labels(rows):
    """Hash values replaced by the first row that logged the same hash (`same as <key>`)."""
    first = {}
    out = {}
    for key, values in rows.items():
        out[key] = ['same as ' + first.setdefault(v, key) if isinstance(v, str) and HASH.match(v) else v
                    for v in values]
    return out


TEMP_SCENE = re.compile(r'InitTestScene[0-9a-f-]{36}')


def stable_path(rel):
    """The Test Framework names the scene it saves for a Play Mode run with a new GUID."""
    return TEMP_SCENE.sub('InitTestScene<guid>', rel)


def file_set(case_dir, summary):
    files = {stable_path(rel): case_dir/'files'/rel for rel in summary['changed']}
    snapshots = case_dir/'snapshots'
    if snapshots.is_dir():
        for path in sorted(snapshots.iterdir()):
            files['snapshot:' + path.name] = path
    return files


def compare_runs(reference, actual, cases, report=None):
    failures, compared = [], 0
    for case in cases:
        ref, act = Path(reference)/case, Path(actual)/case
        if not (act/'summary.json').exists():
            compared += 1
            failures.append({'case': case, 'key': 'run', 'reference': 'captured', 'actual': 'missing'})
            continue
        rs, acs = json.loads((ref/'summary.json').read_text()), json.loads((act/'summary.json').read_text())
        if rs['exit'] != acs['exit']:
            failures.append({'case': case, 'key': 'exit', 'reference': rs['exit'], 'actual': acs['exit']})
        rr, ar = hash_labels(load_rows(ref/'rows.jsonl')), hash_labels(load_rows(act/'rows.jsonl'))
        for key in sorted(set(rr) | set(ar)):
            compared += 1
            if rr.get(key) != ar.get(key):
                failures.append({'case': case, 'key': key, 'reference': rr.get(key), 'actual': ar.get(key)})
        rf, af = file_set(ref, rs), file_set(act, acs)
        created = created_assets(rf, af)
        for rel in sorted(set(rf) | set(af)):
            compared += 1
            if rel not in rf or rel not in af:
                failures.append({'case': case, 'key': 'file:' + rel, 'reference': rel in rf, 'actual': rel in af})
                continue
            problem = compare_file(rf[rel], af[rel], rel, created)
            if problem:
                failures.append({'case': case, 'key': 'file:' + rel, 'reference': 'written', 'actual': problem})
        compared += 1
        if sorted(rs['removed']) != sorted(acs['removed']):
            failures.append({'case': case, 'key': 'removed', 'reference': rs['removed'], 'actual': acs['removed']})
    gaps_file = Path(reference)/'known-gaps.json'
    gaps = [g for g in (json.loads(gaps_file.read_text()) if gaps_file.exists() else []) if g.get('case') in cases]
    if any(not g.get('reason', '').strip() for g in gaps):
        raise ValueError('known gaps need nonempty reasons')
    queued, unexpected = [], []
    for f in failures:
        gap = next((g for g in gaps if all(g.get(k) == f.get(k) for k in ('case', 'key', 'reference', 'actual'))), None)
        (queued if gap else unexpected).append(dict(f, reason=gap['reason']) if gap else f)
    stale = [g for g in gaps if not any(all(g.get(k) == q.get(k) for k in ('case', 'key', 'reference', 'actual')) for q in queued)]
    for f in failures:
        print(json.dumps(f))
    for g in stale:
        print('stale gap: ' + json.dumps(g))
    print(f'{compared - len(failures)}/{compared} rows and files identical; {len(queued)} queued differences; '
          f'{len(unexpected)} unexpected; {len(stale)} stale')
    if report:
        Path(report).write_text(json.dumps({'compared': compared, 'failures': failures, 'queued': queued,
                                            'unexpected': unexpected, 'stale': stale}, indent=2) + '\n')
    return int(bool(unexpected or stale))
