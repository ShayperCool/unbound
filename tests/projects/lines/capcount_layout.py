#!/usr/bin/env python3
"""Proposes a separate cap-count reference file next to the unchanged line reference.

    capcount_layout.py prepare --release R --label L --out DIR
        checks the two Editor captures under the label with run.py's unchanged Editor,
        completeness and byte-identity checks, then requires that every key the canonical
        Reference/unity-R/values.jsonl holds is reproduced byte for byte by key (raw line
        and newline), every canonical image byte for byte, and that the only keys in the
        captures beyond the canonical ones are the PREFIX keys. It bounds the order change
        exactly and writes the proposed PREFIX-only file and its receipt to DIR (a cache
        directory). Nothing under Reference/ is written.
    capcount_layout.py publish --release R --label L --receipt FILE
        repeats every prepare check, additionally binds both repeats' recorded pre-launch probe
        inputs to this checkout's probe sources, requires the order change to be exactly the
        known one (`complete` moved last, the canonical records after it shifted by one place),
        extracts the PREFIX rows from each repeat independently and requires them byte
        identical and equal to the eight predeclared cases (inputs and mesh each), then creates
        Reference/unity-R/capcount.values.jsonl exclusively and atomically. An existing file
        or any failed check leaves Reference/ unchanged. values.jsonl and its images are never
        written. The receipt (a cache path) records both repeats and the before/after hashes.
    capcount_layout.py verify --release R
        checks a published Reference/unity-R/capcount.values.jsonl against the canonical
        reference alone: newline-terminated JSON records, exactly the predeclared PREFIX keys in
        order, disjoint from values.jsonl.

The layout keeps Reference/unity-R/values.jsonl and its images as they are and adds
Reference/unity-R/capcount.values.jsonl with only the cap-count rows in capture order.
The refused `run.py publish --append` stays refused, because the canonical file has
`complete` before its blend rows and a complete capture must end with it. This tool does
not relabel or bypass that refusal.

`load_reference(directory, mode)` is the loader for comparisons: mode `canonical` reads
values.jsonl alone, mode `capcount` adds the control file after verifying it. Both return
every record; nothing is filtered, so missing, extra and failing keys stay visible.
"""
import argparse
import gzip
import os
import tempfile
import hashlib
import importlib.util
import json
from pathlib import Path
import sys

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('lines_run', HERE/'run.py')
run = importlib.util.module_from_spec(spec)
spec.loader.exec_module(run)
sys.path.insert(0, str(HERE.parents[1]))
from probe_paths import cache_artifact_path

PREFIX = 'capcount.'
FILE = 'capcount.values.jsonl'
# Two renderers x two alignments x two cap counts, one inputs and one mesh record each, in the
# probe's loop order. Unity spells LineAlignment.TransformZ by its alias `Local`.
CASES = [f'{PREFIX}{r}.{a}.cap{c}' for r in ('line', 'trail') for a in ('View', 'Local') for c in (1, 2)]
EXPECTED_KEYS = [f'{case}.{part}' for case in CASES for part in ('inputs', 'mesh')]
EXPECTED = len(EXPECTED_KEYS)
MODES = ('canonical', 'capcount')
# Pre-launch input names recorded by the Editor runner and the probe directories they come from.
SOURCE_ROOTS = {'assets': 'Assets', 'packages': 'Packages', 'settings': 'ProjectSettings'}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def lines_by_key(data):
    out, order = {}, []
    for line in data.splitlines(keepends=True):
        if not line.endswith(b'\n'):
            raise SystemExit('a record has no newline terminator')
        key = json.loads(line)['k']
        if key in out:
            raise SystemExit(f'duplicate key {key}')
        out[key] = line
        order.append(key)
    return out, order


def order_delta(canonical, captured):
    """Every canonical key whose position among the canonical keys differs in the capture."""
    kept = [k for k in captured if k in set(canonical)]
    at = {k: i for i, k in enumerate(kept)}
    return [dict(key=k, canonical_index=i, capture_index_among_canonical=at[k]) for i, k in enumerate(canonical) if at[k] != i]


def checked(label, target):
    """Every prepare check; returns the evidence needed to propose or publish."""
    work = run.run_root(run.PROBE, label)
    repeats = [work/r for r in run.REPEATS]
    for repeat in repeats:
        if not repeat.is_dir():
            sys.exit(f'{repeat.name}: no capture')
        reason = run.editor_problem(repeat) or run.capture_problem(repeat)
        if reason:
            sys.exit(f'{repeat.name}: {reason}')
    problems = run.differences(repeats, lambda d: ['values.jsonl', *run.images(d)])
    if problems:
        sys.exit('the repeats differ ('+'; '.join(problems)+')')
    reference = (target/'values.jsonl').read_bytes()
    canonical, canonical_order = lines_by_key(reference)
    raw = [(r/'values.jsonl').read_bytes() for r in repeats]
    captured, captured_order = lines_by_key(raw[0])
    missing = [k for k in canonical_order if k not in captured]
    changed = [k for k in canonical_order if k in captured and captured[k] != canonical[k]]
    extra = [k for k in captured_order if k not in canonical]
    if missing or changed:
        sys.exit(f'the captures do not reproduce the canonical records by key: missing {missing[:5]}, changed {changed[:5]}')
    if any(not k.startswith(PREFIX) for k in extra) or len(extra) != EXPECTED:
        sys.exit(f'unexpected keys beyond the canonical reference: {extra[:8]}')
    if any(k.startswith(PREFIX) for k in canonical):
        sys.exit(f'the canonical reference already holds {PREFIX} keys')
    present = sorted(p.name.removesuffix('.gz') for p in target.glob('*.rgba.gz'))
    if present != run.images(repeats[0]):
        sys.exit('the captures have other images than the canonical reference')
    images = {n: sha((repeats[0]/n).read_bytes()) for n in present}
    if any(gzip.decompress((target/(n+'.gz')).read_bytes()) != (repeats[0]/n).read_bytes() for n in present):
        sys.exit('the captures do not reproduce the canonical images')
    proposed = b''.join(captured[k] for k in extra)
    delta = order_delta(canonical_order, captured_order)
    return dict(work=work, repeats=repeats, raw=raw, reference=reference, canonical_order=canonical_order,
                captured=captured, captured_order=captured_order, extra=extra, proposed=proposed,
                delta=delta, images=images, present=present)


def prepare(release, label, out):
    c = checked(label, run.reference_root(run.PROBE))
    repeats, raw, reference, canonical_order = c['repeats'], c['raw'], c['reference'], c['canonical_order']
    captured, captured_order, extra, proposed = c['captured'], c['captured_order'], c['extra'], c['proposed']
    delta, images, present = c['delta'], c['images'], c['present']
    out = cache_artifact_path(out.resolve())
    out.mkdir(parents=True, exist_ok=True)
    (out/FILE).write_bytes(proposed)
    receipt = dict(
        status='PROPOSAL ONLY: not published; Reference/ unchanged; publish --append remains refused',
        release=release, label=label, layout=f'Reference/unity-{release}/{FILE}',
        repeats={r.name: dict(values_sha256=sha(b), records=len(b.splitlines())) for r, b in zip(repeats, raw)},
        repeats_byte_identical=raw[0] == raw[1],
        canonical=dict(values_sha256=sha(reference), records=len(canonical_order), images=images),
        canonical_records_reproduced_by_key=len(canonical_order), canonical_images_reproduced=len(present),
        proposed=dict(sha256=sha(proposed), records=len(extra),
                      rows=[dict(key=k, capture_index=captured_order.index(k), sha256=sha(captured[k])) for k in extra]),
        order_delta=dict(moved_canonical_keys=delta,
                         rule='capture order of the canonical keys equals canonical order except the keys listed'))
    (out/'capcount-layout-proposal.json').write_text(json.dumps(receipt, indent=1)+'\n')
    print('proposed', len(extra), 'records,', len(canonical_order), 'canonical records and', len(present),
          'images reproduced,', len(delta), 'canonical keys moved; wrote', out)
    return 0


def known_order_delta(canonical_order, captured_order):
    """The only accepted order change: `complete` moves to the end and each canonical record
    after it moves up one place. Anything else is a different reference layout."""
    at = canonical_order.index('complete')
    expected = [dict(key='complete', canonical_index=at, capture_index_among_canonical=len(canonical_order)-1)]
    expected += [dict(key=k, canonical_index=i, capture_index_among_canonical=i-1)
                 for i, k in enumerate(canonical_order) if i > at]
    return order_delta(canonical_order, captured_order) == expected and captured_order[-1] == 'complete'


def source_binding(repeats, probe):
    """Each repeat's recorded pre-launch probe inputs equal this checkout's probe files."""
    out = {}
    for repeat in repeats:
        files = json.loads((repeat/'prelaunch-inputs.json').read_text())['files']
        recorded = {k: v['sha256'] for k, v in files.items() if k.split('/', 1)[0] in SOURCE_ROOTS}
        current = {}
        for name, folder in SOURCE_ROOTS.items():
            for f in sorted((probe/folder).rglob('*')):
                if f.is_file():
                    current[name+'/'+f.relative_to(probe/folder).as_posix()] = sha(f.read_bytes())
        differ = sorted(k for k in recorded.keys() | current.keys() if recorded.get(k) != current.get(k))
        if differ:
            sys.exit(f'{repeat.name}: recorded probe inputs differ from this checkout: {differ[:8]}')
        out[repeat.name] = dict(files=len(recorded), sha256=sha(json.dumps(recorded, sort_keys=True).encode()),
                                tooling={k: files[k]['sha256'] for k in files if k.split('/', 1)[0] not in SOURCE_ROOTS})
    return out


def tree(target):
    return {f.name: sha(f.read_bytes()) for f in sorted(target.iterdir()) if f.is_file()}


def create_exclusive(path, data):
    """Writes path only if it does not exist, never leaving a partial file under its name."""
    fd, temp = tempfile.mkstemp(prefix='.'+path.name+'.', dir=path.parent)
    try:
        with os.fdopen(fd, 'wb') as f:
            f.write(data)
            f.flush()
            os.fsync(f.fileno())
        os.link(temp, path)
    finally:
        os.unlink(temp)


def publish(release, label, receipt_path, probe=None):
    probe = probe or run.PROBE
    target = run.reference_root(probe)
    receipt_path = cache_artifact_path(receipt_path.resolve())
    if (target/FILE).exists():
        sys.exit(f'{target/FILE} already exists; not publishing')
    before = tree(target)
    c = checked(label, target)
    binding = source_binding(c['repeats'], probe)
    if not known_order_delta(c['canonical_order'], c['captured_order']):
        sys.exit('the capture order differs from the canonical order by more than the known completion move')
    per_repeat = []
    for repeat, raw in zip(c['repeats'], c['raw']):
        rows, order = lines_by_key(raw)
        per_repeat.append(b''.join(rows[k] for k in order if k.startswith(PREFIX)))
        if [k for k in order if k.startswith(PREFIX)] != EXPECTED_KEYS:
            sys.exit(f'{repeat.name}: the {PREFIX} rows are not the predeclared cases in order')
    if per_repeat[0] != per_repeat[1] or per_repeat[0] != c['proposed']:
        sys.exit(f'the repeats extract different {PREFIX} rows')
    data = per_repeat[0]
    try:
        create_exclusive(target/FILE, data)
    except FileExistsError:
        sys.exit(f'{target/FILE} already exists; not publishing')
    after = tree(target)
    unchanged = {k: v for k, v in after.items() if k != FILE}
    if unchanged != before or after[FILE] != sha(data) or (target/FILE).read_bytes() != data:
        sys.exit('the reference changed beyond the new control file')
    receipt = dict(
        status='published', release=release, label=label, file=str(target/FILE), sha256=sha(data),
        records=EXPECTED, keys=EXPECTED_KEYS, cases=CASES,
        repeats={r.name: dict(values_sha256=sha(b), records=len(b.splitlines()), extracted_sha256=sha(x))
                 for r, b, x in zip(c['repeats'], c['raw'], per_repeat)},
        source_binding=binding, canonical_records_reproduced_by_key=len(c['canonical_order']),
        canonical_images_reproduced=len(c['present']), images=c['images'],
        order_delta=dict(moved_canonical_keys=c['delta'], known=True),
        reference_before=before, reference_after=after,
        append='run.py publish --append stays refused; values.jsonl and images unchanged')
    receipt_path.parent.mkdir(parents=True, exist_ok=True)
    with receipt_path.open('x') as f:
        f.write(json.dumps(receipt, indent=1)+'\n')
    print('published', target/FILE, EXPECTED, 'records', sha(data))
    return 0


def control_rows(target, canonical):
    """The verified control file's records by key, in file order."""
    path = target/FILE
    if not path.is_file():
        sys.exit(f'no {FILE} in {target}')
    rows, order = lines_by_key(path.read_bytes())
    if order != EXPECTED_KEYS:
        sys.exit(f'{FILE} must hold exactly the {EXPECTED} predeclared {PREFIX} records in order')
    if set(order) & set(canonical):
        sys.exit(f'{FILE} repeats canonical keys')
    return rows, order


def load_reference(directory, mode):
    """All reference records (key -> decoded value) for the selected mode."""
    if mode not in MODES:
        sys.exit(f'unknown reference mode {mode}')
    target = Path(directory)
    canonical, order = lines_by_key((target/'values.jsonl').read_bytes())
    rows = dict(canonical)
    if mode == 'capcount':
        extra, extra_order = control_rows(target, canonical)
        rows.update(extra)
        order = order + extra_order
    return {k: json.loads(rows[k])['v'] for k in order}


def verify(release):
    target = run.reference_root(run.PROBE)
    canonical, _ = lines_by_key((target/'values.jsonl').read_bytes())
    _, order = control_rows(target, canonical)
    print(release, FILE, len(order), 'records, disjoint from', len(canonical), 'canonical records')
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('prepare', 'publish', 'verify'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--label')
    parser.add_argument('--out', type=Path)
    parser.add_argument('--receipt', type=Path)
    args = parser.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    if args.mode == 'prepare':
        if not args.label or not args.out:
            parser.error('prepare needs --label and --out')
        return prepare(args.release, args.label, args.out)
    if args.mode == 'publish':
        if not args.label or not args.receipt:
            parser.error('publish needs --label and --receipt')
        return publish(args.release, args.label, args.receipt)
    return verify(args.release)


if __name__ == '__main__':
    sys.exit(main())
