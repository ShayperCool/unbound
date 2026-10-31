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
    result = {'records': sum(len(v) for v in ref.values()), 'exact': 0, 'matched': 0,
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
    evidence = ROOT/'Evidence/expanded'
    evidence.mkdir(exist_ok=True)
    for label in ['expanded-native1', 'expanded-native2', 'baseline', 'final']:
        source = WORK/f'timing-{label}.json'
        (evidence/source.name).write_bytes(source.read_bytes())
    n1 = WORK/'timing-expanded-native1.json'
    n2 = WORK/'timing-expanded-native2.json'
    assert n1.read_bytes() == n2.read_bytes(), 'Native recordings disagree'
    ref = json.loads(n1.read_text())
    report = {'native_agreement': True, 'native_sha256': sha(n1), 'timing': {}, 'broad': {}}
    for label in ['baseline', 'final']:
        candidate = json.loads((WORK/f'timing-{label}.json').read_text())
        report['timing'][label] = compare(ref, candidate, 1e-3)
        broad = ROOT.parent/'physics/Reference/unity-6000.0.71f1.json'
        report['broad'][label] = compare(json.loads(broad.read_text()),
                                      json.loads((WORK/f'physics-{label}.json').read_text()), 1e-3)
    for group in ['timing', 'broad']:
        before = {(x['scenario'], x['key']) for x in report[group]['baseline']['failures']}
        after = {(x['scenario'], x['key']) for x in report[group]['final']['failures']}
        report[group]['resolved'] = sorted(before-after)
        report[group]['introduced'] = sorted(after-before)
        assert not after-before, f'{group} regressed: {sorted(after-before)}'
    report['artifacts_sha256'] = {str(f.relative_to(WORK)): sha(f) for f in
        [WORK/'managed-baseline/UnityEngine.CoreModule.dll', WORK/'managed-final/UnityEngine.CoreModule.dll',
         WORK/'managed-final/libunbound_physx.so']}
    report['artifacts_sha256']['cli'] = sha(Path.home()/'.cache/compat/parity-baseline-native8/unbound')
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
    report['evidence_sha256'] = {str(f.relative_to(ROOT)): sha(f) for f in sorted(ROOT.rglob('*'))
                                 if f.is_file() and f.name != 'summary.json' and '__pycache__' not in str(f)}
    (ROOT/'Evidence/summary.json').write_text(json.dumps(report, indent=2)+'\n')
    for group in ['timing', 'broad']:
        for label, r in report[group].items():
            if isinstance(r, dict):
                print(group, label, r['exact'], 'exact', r['matched'], '/', r['records'], 'passing', len(r['failures']), 'failures')
        print(group, 'resolved', len(report[group]['resolved']), 'introduced', len(report[group]['introduced']))


if __name__ == '__main__':
    main()
