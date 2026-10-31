#!/usr/bin/env python3
"""Exact, union-key comparison. Every baseline and candidate difference is retained."""
import sys
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_root, run_root

ROOT = Path(__file__).resolve().parent


def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()


def read(p):
    rows = [json.loads(line) for line in p.read_text().splitlines()]
    assert len(rows) == len({r['key'] for r in rows}) > 0, str(p)
    return {r['key']:r['value'] for r in rows}


def compare(ref, candidate):
    keys = sorted(ref.keys() | candidate.keys())
    failures = [dict(key=k,reference=ref.get(k,'<missing>'),candidate=candidate.get(k,'<missing>'))
                for k in keys if k not in ref or k not in candidate or ref[k] != candidate[k]]
    return dict(records=len(keys),reference_records=len(ref),candidate_records=len(candidate),
                exact=len(keys)-len(failures),failures=failures)


def main():
    p=argparse.ArgumentParser(); p.add_argument('--label',required=True); a=p.parse_args()
    w=Path(os.environ.get('LOADING_WORK', run_root(ROOT, 'loading'))); dest=run_root(ROOT, 'loading-report')/a.label
    if dest.exists(): raise SystemExit('refusing to overwrite ' + str(dest))
    dest.mkdir(parents=True)
    result={}
    for order in ['transform-first','rigidbody-first']:
        n1=reference_root(ROOT)/'loading.jsonl'
        ref=read(n1); baseline=compare(ref,read(w/(order+'-baseline.jsonl')))
        candidate=compare(ref,read(w/(order+'-'+a.label+'.jsonl')))
        before={r['key'] for r in baseline['failures']}; after={r['key'] for r in candidate['failures']}
        result[order]=dict(native_records=len(ref),native_sha256=sha(n1),
                           baseline=baseline,candidate=candidate,resolved=sorted(before-after),
                           retained=sorted(before&after),introduced=sorted(after-before))
        for label in ['baseline',a.label]:
            source=w/(order+'-'+label+'.jsonl'); shutil.copy2(source,dest/source.name)
        print(order, 'baseline',baseline['exact'],'candidate',candidate['exact'],'/',candidate['records'],
              'resolved',len(before-after),'retained',len(before&after),'introduced',len(after-before))
    result['capture_sha256']={p.name:sha(p) for p in dest.glob('*.jsonl')}
    (dest/'summary.json').write_text(json.dumps(result,indent=2)+'\n')
    if a.label == 'final':
        assert all(result[o]['candidate']['exact'] == result[o]['candidate']['records'] == 4656
                   and not result[o]['introduced'] for o in ['transform-first','rigidbody-first'])


if __name__=='__main__': main()
