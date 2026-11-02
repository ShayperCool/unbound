#!/usr/bin/env python3
"""Compares one capture of the creation-ownership witness with Reference creation-ownership.jsonl.

    compare_creation.py REFERENCE.jsonl CAPTURE_DIR [--native DIR ...] [--report REPORT.json]

CAPTURE_DIR holds the three outputs named by CreationOwnership/schema.json (main, raw
FindObjectsByType, teardown). Every section is computed whatever the others conclude:

- gate: the witness output's own gate (run.py creation_rows_problem), unchanged.
- contract: each of the schema's three families' predicates against the capture's own trace,
  reported separately (a failing family does not hide the others).
- canonical: only if every predicate holds, contract.canonical(CAPTURE_DIR) compared row by row
  and leaf by leaf with the Reference; otherwise the failing predicates are the result. The
  Native contract is never relaxed to make a capture canonicalizable.
- raw: the capture's raw main rows against the Reference body (the Reference without its
  contract row), positional and leaf by leaf. Each differing leaf is labelled with the family
  whose canonical rewrite can explain it, or None (strict). Labels are information: a family
  leaf is only admissible through the canonical comparison above.
- aligned: the same rows aligned by event signature (difflib), so an inserted or missing row
  shows once instead of shifting the tail; matched rows keep their complete leaf comparison.
- semantic: for every declared point present on both sides (found by name, not position) and
  every alias, the ownership, lifetime and Find fields compared as separate groups, plus the
  scene listing/composition, light probes, each alias's lifecycle message sequence and the
  Error name sets. Already-red rows are therefore still assessed per concern.
- native (with --native): the complete raw leaf union of the capture against each raw Native
  repeat (main, raw FindObjectsByType and teardown outputs), with the same family labels.

Nothing is filtered, renamed or toleranced; every missing, extra and differing value is kept.
Exit 0 only if the canonical comparison exists and has no difference.
"""
import argparse
import difflib
import hashlib
import json
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE/'CreationOwnership'))
import contract  # noqa: E402

OWNERSHIP = ('scene', 'parent', 'root', 'inSceneRoots')
LIFETIME = ('created', 'alive', 'name', 'activeSelf', 'activeInHierarchy', 'status', 'updatedThisEpoch')
FIND = ('find', 'findByType', 'tracker')
LISTING = ('active', 'sceneCount', 'loadedSceneCount', 'listed', 'controllerScene')


def read(path):
    rows = []
    for number, line in enumerate(Path(path).read_text().splitlines(), 1):
        if not line.strip():
            continue
        try:
            rows.append(json.loads(line))
        except json.JSONDecodeError:
            rows.append({'unparsable': line, 'line': number})
    return rows


def leaves(value):
    return dict(contract._leaves(value))


def leaf_diff(a, b):
    la, lb = leaves(a), leaves(b)
    return [{'path': p, 'reference': la.get(p, '<absent>'), 'capture': lb.get(p, '<absent>')}
            for p in sorted(set(la) | set(lb)) if la.get(p, '<absent>') != lb.get(p, '<absent>')]


def family(ref_row, cap_row, path, spec, ref_index, cap_index):
    """The family whose canonical rewrite can explain a raw/Reference difference at this leaf."""
    header = spec['contract']['families']['not-cleaned-up-error-names']['header']
    for row, index in ((cap_row, cap_index), (ref_row, ref_index)):
        if not isinstance(row, dict):
            continue
        label = contract.classify(row, path, spec, index)
        if label:
            return label
        message = row.get('message')
        if (row.get('kind') == 'log' and row.get('at') == 'Error' and path.startswith('/message')
                and (str(message).startswith(header) or (isinstance(message, dict) and message.get('header') == header))):
            return 'not-cleaned-up-error-names'
    return None


def ref_outgoing_index(body):
    start = contract._point(body, 't1.a.start')
    iv = contract._point(body, 't1.a.iv')
    if start is None or iv is None:
        return None
    found = [i for i, s in enumerate(iv['state'].get('scenes') or []) if isinstance(s, dict) and s.get('ref') == start.get('args')]
    return found[0] if len(found) == 1 else None


def positional(ref_rows, cap_rows, spec, labelled=True):
    ri = ref_outgoing_index(ref_rows) if labelled else None
    ci = ref_outgoing_index(cap_rows) if labelled else None
    rows, counts = [], {'pass': 0, 'differ': 0, 'missing': 0, 'extra': 0}
    leaves_total = {'differ': 0, 'strict': 0, 'family': {}}
    for i in range(max(len(ref_rows), len(cap_rows))):
        a = ref_rows[i] if i < len(ref_rows) else None
        b = cap_rows[i] if i < len(cap_rows) else None
        if b is None:
            counts['missing'] += 1
            rows.append({'index': i, 'status': 'missing', 'reference': a})
        elif a is None:
            counts['extra'] += 1
            rows.append({'index': i, 'status': 'extra', 'capture': b})
        elif a == b:
            counts['pass'] += 1
        else:
            counts['differ'] += 1
            diffs = leaf_diff(a, b)
            for d in diffs:
                d['family'] = family(a, b, d['path'], spec, ri, ci) if labelled else None
                leaves_total['differ'] += 1
                if d['family'] is None:
                    leaves_total['strict'] += 1
                else:
                    leaves_total['family'][d['family']] = leaves_total['family'].get(d['family'], 0)+1
            rows.append({'index': i, 'status': 'differ', 'reference_signature': signature(a),
                         'capture_signature': signature(b), 'leaves': diffs})
    return {'reference_rows': len(ref_rows), 'capture_rows': len(cap_rows), 'counts': counts,
            'leaves': leaves_total, 'rows': rows}


def signature(row):
    if not isinstance(row, dict):
        return str(row)
    if 'k' in row:
        return 'k:'+str(row['k'])
    return '|'.join(str(row.get(f)) for f in ('stage', 'kind', 'point', 'at', 'alias', 'args'))


def aligned(ref_rows, cap_rows, spec):
    sa, sb = [signature(r) for r in ref_rows], [signature(r) for r in cap_rows]
    ri, ci = ref_outgoing_index(ref_rows), ref_outgoing_index(cap_rows)
    ops, matched = [], []
    for op, i1, i2, j1, j2 in difflib.SequenceMatcher(None, sa, sb, autojunk=False).get_opcodes():
        if op == 'equal':
            for i, j in zip(range(i1, i2), range(j1, j2)):
                if ref_rows[i] != cap_rows[j]:
                    diffs = leaf_diff(ref_rows[i], cap_rows[j])
                    for d in diffs:
                        d['family'] = family(ref_rows[i], cap_rows[j], d['path'], spec, ri, ci)
                    matched.append({'reference_index': i, 'capture_index': j, 'signature': sa[i], 'leaves': diffs})
        else:
            ops.append({'op': op, 'reference_rows': [i1, i2], 'capture_rows': [j1, j2],
                        'reference': ref_rows[i1:i2], 'capture': cap_rows[j1:j2]})
    return {'unmatched': ops, 'matched_differing': matched,
            'counts': {'matched': sum(i2-i1 for op, i1, i2, j1, j2 in
                                      difflib.SequenceMatcher(None, sa, sb, autojunk=False).get_opcodes() if op == 'equal'),
                       'matched_differing': len(matched),
                       'reference_only': sum(o['reference_rows'][1]-o['reference_rows'][0] for o in ops),
                       'capture_only': sum(o['capture_rows'][1]-o['capture_rows'][0] for o in ops)}}


def group(a, b, fields):
    a = a if isinstance(a, dict) else {}
    b = b if isinstance(b, dict) else {}
    diff = {f: {'reference': a.get(f, '<absent>'), 'capture': b.get(f, '<absent>')}
            for f in fields if a.get(f, '<absent>') != b.get(f, '<absent>')}
    return {'ok': not diff, 'differ': diff}


def semantic(ref_rows, cap_rows, spec):
    def points(rows):
        out = {}
        for r in rows:
            if isinstance(r, dict) and r.get('kind') == 'snapshot' and r.get('point') is not None:
                out.setdefault(r['point'], []).append(r)
        return out
    rp, cp = points(ref_rows), points(cap_rows)
    result, totals = {}, {}
    for point in spec['points']:
        a, b = rp.get(point, []), cp.get(point, [])
        if len(a) != 1 or len(b) != 1:
            result[point] = {'reference_count': len(a), 'capture_count': len(b)}
            totals['point-missing-or-repeated'] = totals.get('point-missing-or-repeated', 0)+1
            continue
        sa, sb = a[0]['state'], b[0]['state']
        entry = {'listing': group(sa, sb, LISTING), 'lightProbes': group(sa, sb, ('lightProbes',)),
                 'findByTypeSet': group(sa, sb, ('findByType',))}
        scenes_a = {s.get('ref'): s for s in sa.get('scenes') or [] if isinstance(s, dict)}
        scenes_b = {s.get('ref'): s for s in sb.get('scenes') or [] if isinstance(s, dict)}
        entry['scenes'] = {ref: group(scenes_a.get(ref), scenes_b.get(ref), sorted(set(scenes_a.get(ref) or {}) | set(scenes_b.get(ref) or {})))
                           for ref in sorted(set(scenes_a) | set(scenes_b))}
        for ref, g in entry['scenes'].items():
            # The Reference holds the canonical (contract) form of the outgoing roots at t1.a.iv.
            if (not g['ok'] and set(g['differ']) <= {'roots', 'rootCount'}
                    and isinstance((scenes_a.get(ref) or {}).get('roots'), dict)):
                g['family'] = 'outgoing-roots-destruction-prefix'
        objs = {}
        for alias in spec['aliases']:
            oa, ob = (sa.get('objects') or {}).get(alias), (sb.get('objects') or {}).get(alias)
            objs[alias] = {'ownership': group(oa, ob, OWNERSHIP), 'lifetime': group(oa, ob, LIFETIME),
                           'find': group(oa, ob, FIND),
                           'inFindByTypeSet': {'reference': alias in (sa.get('findByType') or []),
                                               'capture': alias in (sb.get('findByType') or [])}}
            objs[alias]['find']['ok'] = objs[alias]['find']['ok'] and (
                objs[alias]['inFindByTypeSet']['reference'] == objs[alias]['inFindByTypeSet']['capture'])
        entry['objects'] = objs
        result[point] = entry
        for key in ('listing', 'lightProbes', 'findByTypeSet'):
            if not entry[key]['ok']:
                totals[key] = totals.get(key, 0)+1
        if any(not g['ok'] and 'family' not in g for g in entry['scenes'].values()):
            totals['scenes'] = totals.get('scenes', 0)+1
        if any('family' in g for g in entry['scenes'].values()):
            totals['scenes-family-only'] = totals.get('scenes-family-only', 0)+1
        for alias, groups in objs.items():
            for key in ('ownership', 'lifetime', 'find'):
                if not groups[key]['ok']:
                    totals[key] = totals.get(key, 0)+1

    def messages(rows):
        out = {}
        for r in rows:
            if isinstance(r, dict) and r.get('kind') == 'lifecycle':
                out.setdefault(r.get('alias'), []).append(f"{r.get('stage')}:{r.get('at')}")
        return out
    ma, mb = messages(ref_rows), messages(cap_rows)
    lifecycle = {alias: {'ok': ma.get(alias) == mb.get(alias), 'reference': ma.get(alias), 'capture': mb.get(alias)}
                 for alias in sorted(set(ma) | set(mb), key=str)}
    header = spec['contract']['families']['not-cleaned-up-error-names']['header']

    def errors(rows):
        out = []
        for r in rows:
            if isinstance(r, dict) and r.get('kind') == 'log' and r.get('at') == 'Error':
                m = r.get('message')
                if isinstance(m, dict) and m.get('header') == header:
                    out.append({'stage': r.get('stage'), 'names': sorted(m['names'])})
                elif str(m).startswith(header):
                    out.append({'stage': r.get('stage'), 'names': sorted(m[len(header):].rstrip('\n').split('\n'))})
                else:
                    out.append({'stage': r.get('stage'), 'message': m})
        return out
    ea, eb = errors(ref_rows), errors(cap_rows)
    return {'totals': totals, 'points': result,
            'lifecycle_sequences': {'ok': all(v['ok'] for v in lifecycle.values()), 'aliases': lifecycle},
            'error_logs': {'ok': ea == eb, 'reference': ea, 'capture': eb}}


def contract_families(directory, spec):
    rows, teardown = contract.load(directory, spec)
    body = rows[1:-1]
    out = {}
    for name, check in (('outgoing-roots-destruction-prefix', lambda: contract._roots_family(body, teardown, spec)),
                        ('not-cleaned-up-error-names', lambda: contract._error_family(body, spec)),
                        ('inter-object-cleanup-order', lambda: contract._cleanup_family(body, spec))):
        try:
            reason, _ = check()
        except Exception as error:  # a malformed capture is a failure, recorded as such
            reason = 'not evaluable: '+repr(error)
        out[name] = {'satisfied': reason is None, 'reason': reason}
    return out


def outputs(directory, spec):
    out = {}
    for key in ('output', 'raw_output', 'teardown_output'):
        path = Path(directory)/spec[key]
        out[spec[key]] = ({'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'rows': len(read(path))}
                          if path.exists() else None)
    return out


def native_union(directory, native, spec):
    a, b = Path(native), Path(directory)
    main = positional(read(a/spec['output']), read(b/spec['output']), spec)
    plain = {}
    for key in ('raw_output', 'teardown_output'):
        pa, pb = a/spec[key], b/spec[key]
        plain[spec[key]] = positional(read(pa) if pa.exists() else [], read(pb) if pb.exists() else [], spec, labelled=False)
    for section in [main, *plain.values()]:
        for row in section['rows']:
            row['native'] = row.pop('reference', None) if 'reference' in row else None
    return {'native': str(a), 'main': main, **plain}


def compare(reference, capture, natives=()):
    import run  # the witness gate lives with the runner
    spec = contract.schema()
    ref = read(reference)
    ref_body = [r for r in ref if r.get('k') != 'contract']
    capture = Path(capture)
    report = {'reference': str(reference), 'capture': str(capture), 'schema': spec['schema'],
              'contract_version': spec['contract']['version'], 'outputs': outputs(capture, spec)}
    main = capture/spec['output']
    if not main.exists():
        report.update(ok=False, problem=f"{spec['output']} is missing")
        return report
    cap = read(main)
    report['gate'] = run.creation_rows_problem(capture, spec)
    report['contract'] = contract_families(capture, spec)
    satisfied = all(f['satisfied'] for f in report['contract'].values())
    if satisfied:
        canon = contract.canonical(capture, spec)
        report['canonical'] = {'computed': True, **positional(ref, canon, spec, labelled=False)}
    else:
        report['canonical'] = {'computed': False, 'reason': 'contract predicates failed: '+'; '.join(
            f"{k}: {v['reason']}" for k, v in report['contract'].items() if not v['satisfied'])}
    report['raw'] = positional(ref_body, cap, spec)
    report['aligned'] = aligned(ref_body, cap, spec)
    report['semantic'] = semantic(ref_body, cap, spec)
    teardown = capture/spec['teardown_output']
    report['teardown_trace'] = read(teardown) if teardown.exists() else None
    report['native'] = [native_union(capture, n, spec) for n in natives]
    c = report['canonical']
    report['ok'] = bool(c.get('computed')) and c['counts']['pass'] == max(c['reference_rows'], c['capture_rows'])
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference')
    parser.add_argument('capture')
    parser.add_argument('--native', action='append', default=[], help='a raw Native repeat directory')
    parser.add_argument('--report')
    args = parser.parse_args()
    report = compare(args.reference, args.capture, args.native)
    if args.report:
        Path(args.report).write_text(json.dumps(report, indent=1, default=str)+'\n')
    if 'problem' in report:
        print(report['problem'])
        return 1
    print('gate:', report['gate'] or 'passed')
    for name, value in report['contract'].items():
        print(f"contract {name}: {'satisfied' if value['satisfied'] else value['reason']}")
    c = report['canonical']
    if c['computed']:
        print(f"canonical: {c['counts']['pass']}/{max(c['reference_rows'], c['capture_rows'])} rows pass, "
              f"{c['counts']['differ']} differ, {c['counts']['missing']} missing, {c['counts']['extra']} extra")
    else:
        print('canonical: not computed:', c['reason'])
    r = report['raw']
    print(f"raw: {r['counts']['pass']}/{max(r['reference_rows'], r['capture_rows'])} rows pass, {r['counts']['differ']} differ, "
          f"{r['counts']['missing']} missing, {r['counts']['extra']} extra; leaves {r['leaves']}")
    print('aligned:', report['aligned']['counts'])
    print('semantic totals:', report['semantic']['totals'], 'lifecycle sequences ok:',
          report['semantic']['lifecycle_sequences']['ok'], 'error logs ok:', report['semantic']['error_logs']['ok'])
    for n in report['native']:
        print(f"native {n['native']}: main {n['main']['counts']} leaves {n['main']['leaves']}")
    return 0 if report['ok'] else 1


if __name__ == '__main__':
    sys.exit(main())
