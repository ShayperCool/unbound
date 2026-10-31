#!/usr/bin/env python3
"""Compare complete event streams, renaming pointer identities bijectively only."""
import argparse
import hashlib
import itertools
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import cache_artifact_path


def normalized(path):
    maps = {k: {'0x0': None} for k in ['body', 'fixture', 'contact', 'edge', 'proxy']}
    kinds = dict(ptr='body', other='body', fixture='fixture', contact='contact',
                 edge='edge', head='edge', prev='edge', next='edge', proxyData='proxy')
    def visit(value, key=None):
        if isinstance(value, dict): return {k: visit(v, k) for k, v in value.items()}
        if isinstance(value, list): return [visit(v) for v in value]
        if value is None: return None
        if key in kinds:
            mapping = maps[kinds[key]]
            if value not in mapping: mapping[value] = len(mapping)-1
            return mapping[value]
        return value
    return [visit(json.loads(line)) for line in path.read_text().splitlines()]


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--release', required=True)
    p.add_argument('--candidate', type=Path, required=True)
    p.add_argument('--reference', type=Path)
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    cache_artifact_path(a.output)
    ref = a.reference or Path(__file__).resolve().parent/'Reference'/('unity-'+a.release)/'pairs.jsonl'
    rows = [normalized(path) for path in [ref, a.candidate]]
    differences = [dict(record=i, reference=x, candidate=y) for i, (x, y) in
                   enumerate(itertools.zip_longest(*rows), 1) if x != y]
    result = dict(records=list(map(len, rows)), agreed=bool(rows[0]) and not differences,
                  differences=differences, method='Bijective pointer renaming only; all other fields exact',
                  inputs_sha256={str(f.resolve()): hashlib.sha256(f.read_bytes()).hexdigest()
                                 for f in [ref, a.candidate]})
    a.output.parent.mkdir(parents=True, exist_ok=True)
    a.output.write_text(json.dumps(result, indent=2)+'\n')
    print(result['records'], len(differences), 'different')
    return int(not result['agreed'])


if __name__ == '__main__': raise SystemExit(main())
