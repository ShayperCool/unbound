#!/usr/bin/env python3
"""Keep every observation, including baseline limits and passing numeric rounding."""
import hashlib
import json
import math
import os
from pathlib import Path

ROOT = Path(__file__).resolve().parent
WORK = Path(os.environ['TRIGGER_WORK'])


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def compare(ref, candidate, tolerance):
    result = {'records': sum(len(ref.get(k, {}).keys() | candidate.get(k, {}).keys()) for k in ref.keys() | candidate.keys()),
              'reference_records': sum(map(len, ref.values())), 'candidate_records': sum(map(len, candidate.values())),
              'exact': 0, 'matched': 0,
              'rounding': [], 'failures': []}
    for scenario in sorted(ref.keys() | candidate.keys()):
        a, b = ref.get(scenario, {}), candidate.get(scenario, {})
        for key in sorted(a.keys() | b.keys()):
            x, y = a.get(key, '<missing>'), b.get(key, '<missing>')
            record = {'scenario': scenario, 'key': key, 'reference': x, 'candidate': y}
            if key in a and key in b and x == y:
                result['exact'] += 1
                result['matched'] += 1
            elif (isinstance(x, (int, float)) and not isinstance(x, bool)
                  and isinstance(y, (int, float)) and not isinstance(y, bool)
                  and math.isfinite(x) and math.isfinite(y)
                  and abs(x-y) <= tolerance*max(1, abs(x))):
                result['matched'] += 1
                result['rounding'].append(record)
            else:
                result['failures'].append(record)
    return result


def main():
    evidence = ROOT/'Evidence/accepted'
    evidence.mkdir(exist_ok=True)
    for label in ['expanded-native1', 'expanded-native2', 'baseline', 'final']:
        source = WORK/f'timing-{label}.json'
        (evidence/source.name).write_bytes(source.read_bytes())
    n1 = WORK/'timing-expanded-native1.json'
    n2 = WORK/'timing-expanded-native2.json'
    assert n1.read_bytes() == n2.read_bytes(), 'Native recordings disagree'
    ref = json.loads(n1.read_text())
    report = {'native_agreement': True, 'native_sha256': sha(n1), 'timing': {}, 'order': {}, 'broad': {}}
    for label in ['baseline', 'final']:
        candidate = json.loads((WORK/f'timing-{label}.json').read_text())
        report['timing'][label] = compare(ref, candidate, 1e-3)
        report['order'][label] = compare(json.loads((WORK/'order-native1.json').read_text()),
                                         json.loads((WORK/f'order-{label}.json').read_text()), 1e-3)
        broad = ROOT.parent/'physics/Reference/unity-6000.0.71f1.json'
        report['broad'][label] = compare(json.loads(broad.read_text()),
                                      json.loads((WORK/f'physics-{label}.json').read_text()), 1e-3)
    for group in ['timing', 'order', 'broad']:
        before = {(x['scenario'], x['key']) for x in report[group]['baseline']['failures']}
        after = {(x['scenario'], x['key']) for x in report[group]['final']['failures']}
        report[group]['resolved'] = sorted(before-after)
        report[group]['introduced'] = sorted(after-before)
        assert not after-before, f'{group} regressed: {sorted(after-before)}'
    assert (WORK/'order-native1.json').read_bytes() == (WORK/'order-native2.json').read_bytes()
    report['artifacts_sha256'] = {str(f.relative_to(WORK)): sha(f) for f in
        [WORK/'managed-baseline/UnityEngine.CoreModule.dll', WORK/'managed-final/UnityEngine.CoreModule.dll',
         WORK/'managed-final/libunbound_physx.so']}
    report['artifacts_sha256']['cli'] = sha(Path.home()/'.cache/compat/parity-baseline-native8/unbound')
    for label in ['baseline', 'final']:
        path = WORK/f'order-{label}.json'
        (evidence/path.name).write_bytes(path.read_bytes())
    for fixture in ['physics', 'joints2d', 'effectors', 'forces']:
        for label in ['baseline', 'final']:
            path = WORK/f'{fixture}-{label}.json' if fixture == 'physics' else WORK/f'{fixture}-{label}'
            if path.is_file():
                (evidence/path.name).write_bytes(path.read_bytes())
            else:
                import shutil
                shutil.copytree(path, evidence/path.name, dirs_exist_ok=True)
            log = WORK/f'{fixture}-{label}-comparison.txt'
            (evidence/log.name).write_bytes(log.read_bytes())
    report['matrix'] = json.loads((ROOT/'Evidence/matrix/summary.json').read_text())
    report['runtime_commit'] = '2102b330659ab368dbe5795435d9e680d024c315'
    report['base_commit'] = 'c1ca2ce07f1a6bef457354971f63ce02c59aaa2b'
    report['regressions'] = []
    for name, direct, known in [('joints2d', 1382, 19), ('effectors', 2531, 28), ('forces', 608, 0)]:
        old = WORK/(name+'-baseline')/'values.jsonl'
        new = WORK/(name+'-final')/'values.jsonl'
        assert old.read_bytes() == new.read_bytes()
        report['regressions'].append(dict(fixture=name, direct_matches=direct,
            existing_rounding_or_measured_alternatives=known, byte_identical_to_baseline=True))
    old, new = [json.loads((WORK/f'physics-{label}.json').read_text()) for label in ['baseline', 'final']]
    three_d = [name for name in old if name.startswith('3d.')]
    assert all(old[name] == new[name] for name in three_d)
    report['broad_3d'] = dict(scenarios=len(three_d), records=sum(len(old[name]) for name in three_d),
                             identical_to_baseline=True)
    import xml.etree.ElementTree as ET
    report['tests'] = {}
    for name in ['focused2d', 'integrated']:
        trx = ET.parse(WORK/'tests'/(name+'.trx'))
        counters = trx.find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters')
        report['tests'][name] = counters.attrib
    report['evidence_sha256'] = {str(f.relative_to(ROOT)): sha(f) for f in sorted(ROOT.rglob('*'))
                                 if f.is_file() and f.name not in ['summary.json', 'SHA256SUMS'] and '__pycache__' not in str(f)}
    (ROOT/'Evidence/summary.json').write_text(json.dumps(report, indent=2)+'\n')
    for group in ['timing', 'order', 'broad']:
        for label, r in report[group].items():
            if isinstance(r, dict):
                print(group, label, r['exact'], 'exact', r['matched'], '/', r['records'], 'passing', len(r['failures']), 'failures')
        print(group, 'resolved', len(report[group]['resolved']), 'introduced', len(report[group]['introduced']))


if __name__ == '__main__':
    main()
