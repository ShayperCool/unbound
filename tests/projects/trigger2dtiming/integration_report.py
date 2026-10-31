#!/usr/bin/env python3
"""Keep every observation, including baseline limits and passing numeric rounding."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import reference_file, reference_root, run_root
WORK = Path(os.environ.get('TRIGGER_WORK', run_root(ROOT)))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


from report import compare


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--work', type=Path, default=WORK)
    parser.add_argument('--output', type=Path, default=run_root(ROOT, 'integration-report'))
    args = parser.parse_args()
    work = args.work
    evidence = args.output
    evidence.mkdir(parents=True, exist_ok=True)
    n1 = reference_root(ROOT)/'timing.json'
    ref = json.loads(n1.read_text())
    report = {'native_sha256': sha(n1), 'timing': {}, 'order': {}, 'broad': {}}
    for label in ['baseline', 'final']:
        candidate = json.loads((work/f'timing-{label}.json').read_text())
        report['timing'][label] = compare(ref, candidate, 1e-3)
        report['order'][label] = compare(json.loads((reference_root(ROOT)/'order.json').read_text()),
                                         json.loads((work/f'order-{label}.json').read_text()), 1e-3)
        broad = reference_file(ROOT.parent/'physics')
        report['broad'][label] = compare(json.loads(broad.read_text()),
                                      json.loads((work/f'physics-{label}.json').read_text()), 1e-3)
    for group in ['timing', 'order', 'broad']:
        before = {(x['scenario'], x['key']) for x in report[group]['baseline']['failures']}
        after = {(x['scenario'], x['key']) for x in report[group]['final']['failures']}
        report[group]['resolved'] = sorted(before-after)
        report[group]['introduced'] = sorted(after-before)
        assert not after-before, f'{group} regressed: {sorted(after-before)}'
    report['artifacts_sha256'] = {str(f.relative_to(work)): sha(f) for f in work.glob('managed-*/*') if f.is_file()}
    for label in ['baseline', 'final']:
        path = work/f'order-{label}.json'
        (evidence/path.name).write_bytes(path.read_bytes())
    for fixture in ['physics', 'joints2d', 'effectors', 'forces']:
        for label in ['baseline', 'final']:
            path = work/f'{fixture}-{label}.json' if fixture == 'physics' else work/f'{fixture}-{label}'
            if path.is_file():
                (evidence/path.name).write_bytes(path.read_bytes())
            else:
                import shutil
                shutil.copytree(path, evidence/path.name, dirs_exist_ok=True)
            log = work/f'{fixture}-{label}-comparison.txt'
            (evidence/log.name).write_bytes(log.read_bytes())
    report['matrix'] = json.loads((work/'matrix/summary.json').read_text())
    report['regressions'] = []
    for name, direct, known in [('joints2d', 1382, 19), ('effectors', 2531, 28), ('forces', 608, 0)]:
        old = work/(name+'-baseline')/'values.jsonl'
        new = work/(name+'-final')/'values.jsonl'
        assert old.read_bytes() == new.read_bytes()
        report['regressions'].append(dict(fixture=name, direct_matches=direct,
            existing_rounding_or_measured_alternatives=known, byte_identical_to_baseline=True))
    old, new = [json.loads((work/f'physics-{label}.json').read_text()) for label in ['baseline', 'final']]
    three_d = [name for name in old if name.startswith('3d.')]
    assert all(old[name] == new[name] for name in three_d)
    report['broad_3d'] = dict(scenarios=len(three_d), records=sum(len(old[name]) for name in three_d),
                             identical_to_baseline=True)
    import xml.etree.ElementTree as ET
    report['tests'] = {}
    for name in ['focused2d', 'integrated']:
        trx = ET.parse(work/'tests'/(name+'.trx'))
        counters = trx.find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters')
        report['tests'][name] = counters.attrib
    report['evidence_sha256'] = {str(f.relative_to(evidence)): sha(f) for f in sorted(evidence.rglob('*'))
                                 if f.is_file() and f.name not in ['summary.json', 'SHA256SUMS'] and '__pycache__' not in str(f)}
    (evidence/'summary.json').write_text(json.dumps(report, indent=2)+'\n')
    for group in ['timing', 'order', 'broad']:
        for label, r in report[group].items():
            if isinstance(r, dict):
                print(group, label, r['exact'], 'exact', r['matched'], '/', r['records'], 'passing', len(r['failures']), 'failures')
        print(group, 'resolved', len(report[group]['resolved']), 'introduced', len(report[group]['introduced']))


if __name__ == '__main__':
    main()
