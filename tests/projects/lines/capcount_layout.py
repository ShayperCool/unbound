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
    capcount_layout.py verify --release R
        checks a published Reference/unity-R/capcount.values.jsonl against the canonical
        reference alone: newline-terminated JSON records, unique PREFIX keys disjoint from
        values.jsonl, the expected count.

The proposed layout keeps Reference/unity-R/values.jsonl and its images as they are and
adds Reference/unity-R/capcount.values.jsonl with only the cap-count rows in capture order.
It is published only when Root approves the layout; the refused `run.py publish --append`
stays refused, because the canonical file has `complete` before its blend rows and a
complete capture must end with it. This tool does not relabel or bypass that refusal.
"""
import argparse
import gzip
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
# Two renderers x two alignments x two cap counts, one inputs and one mesh record each.
EXPECTED = 16


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


def prepare(release, label, out):
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
    target = run.reference_root(run.PROBE)
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


def verify(release):
    target = run.reference_root(run.PROBE)
    canonical, _ = lines_by_key((target/'values.jsonl').read_bytes())
    path = target/FILE
    if not path.is_file():
        sys.exit(f'no {FILE} in {target}')
    rows, order = lines_by_key(path.read_bytes())
    if len(order) != EXPECTED or any(not k.startswith(PREFIX) for k in order):
        sys.exit(f'{FILE} must hold exactly {EXPECTED} {PREFIX} records')
    if set(order) & set(canonical):
        sys.exit(f'{FILE} repeats canonical keys')
    print(release, FILE, len(order), 'records, disjoint from', len(canonical), 'canonical records')
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('prepare', 'verify'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--label')
    parser.add_argument('--out', type=Path)
    args = parser.parse_args()
    import os
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    if args.mode == 'prepare':
        if not args.label or not args.out:
            parser.error('prepare needs --label and --out')
        return prepare(args.release, args.label, args.out)
    return verify(args.release)


if __name__ == '__main__':
    sys.exit(main())
