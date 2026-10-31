#!/usr/bin/env python3
"""Archive the integrated candidate without overwriting the delegate's before/after evidence."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

from compare import matches, read
from run import PROFILES, ROOT


def main():
    work = Path(os.environ['SCENE_PARENT_WORK'])
    output = ROOT / 'ParentEvidence/accepted'
    summary = []
    transitions = []
    groups = [('', list(PROFILES)),
              ('contacts-', ['default', 'pairs-kk', 'pairs-ks', 'pairs-all']),
              ('islands-', ['default', 'determinism', 'tgs', 'tgs-det'])]
    for prefix, profiles in groups:
        for name in profiles:
            reference = ROOT / 'Reference' / prefix.rstrip('-') / (name + '.jsonl')
            ref = read(str(reference))
            first = read(str(ROOT / 'Evidence' / (prefix + 'native1') / reference.name))
            second = read(str(ROOT / 'Evidence' / (prefix + 'native2') / reference.name))
            assert ref == first == second, 'Native disagreement: ' + prefix + name
            source = work / 'work' / (prefix + 'final') / reference.name
            candidate = read(str(source))
            baseline = read(str(ROOT / 'Evidence' / (prefix + 'baseline') / reference.name))
            delegate = read(str(ROOT / 'Evidence' / (prefix + 'final') / reference.name))
            keys = sorted(ref.keys() | candidate.keys())
            failed = [k for k in keys if not matches(k, ref.get(k), candidate.get(k))]
            introduced = [k for k in keys if matches(k, ref.get(k), baseline.get(k)) and k in failed]
            exact = sum(ref.get(k) == candidate.get(k) for k in keys)
            assert len(ref) == (299 if prefix == 'contacts-' else 227)
            assert not introduced, 'new failure: ' + prefix + name + ': ' + repr(introduced)
            row = dict(profile=prefix + name, records=len(keys), exact=exact,
                       matched=len(keys)-len(failed), failed_keys=failed,
                       introduced_vs_frozen6=introduced)
            summary.append(row)
            folder = output / (prefix + 'final')
            folder.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, folder / reference.name)
            for key in keys:
                if ref.get(key) != candidate.get(key):
                    transitions.append(dict(profile=prefix + name, key=key,
                        reference=ref.get(key), candidate=candidate.get(key),
                        passing=key not in failed,
                        baseline=baseline.get(key), delegate=delegate.get(key)))
    assert sum(r['records'] for r in summary) == 5055
    focused = []
    for name in ['kinematic', 'triggers', 'order']:
        reference = ROOT / 'ParentEvidence/Reference' / (name + '.jsonl')
        source = work / (name + '-integrated.jsonl')
        a, b = read(str(reference)), read(str(source))
        keys = a.keys() | b.keys()
        failed = [k for k in sorted(keys) if not matches(k, a.get(k), b.get(k))]
        focused.append(dict(probe=name, records=len(keys), matched=len(keys)-len(failed), failed_keys=failed))
        folder = output / 'focused'; folder.mkdir(exist_ok=True)
        shutil.copy2(source, folder / reference.name)
    regressions = []
    data = ['physicsdefaults.jsonl', 'physics.json', 'joints/joints.jsonl',
            'controller/controller.jsonl', 'forces/values.jsonl', 'rayhits/values.jsonl']
    for relative in data:
        source = work / 'work/regressions-final' / relative
        baseline = ROOT / 'Evidence/regressions-final' / relative
        identical = source.read_bytes() == baseline.read_bytes()
        assert identical, 'changed regression output: ' + relative
        regressions.append(dict(file=relative, byte_identical_to_delegate=True))
        dest = output / 'regressions' / relative
        dest.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(source, dest)
    sha256 = lambda p: hashlib.sha256(Path(p).read_bytes()).hexdigest()
    artifacts = {name: sha256(path) for name, path in {
        'cli': os.environ['SCENE_SETTINGS_CLI'],
        'managed': Path(os.environ['UNBOUND_MANAGED_DIR']) / 'UnityEngine.CoreModule.dll',
        'physx7': Path(os.environ['UNBOUND_MANAGED_DIR']) / 'libunbound_physx.so',
    }.items()}
    report = dict(source_commit=subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
        observations=sum(r['records'] for r in summary),
        exact=sum(r['exact'] for r in summary), matched=sum(r['matched'] for r in summary),
        profiles=summary, focused=focused, regressions=regressions, artifacts_sha256=artifacts)
    (output / 'summary.json').write_text(json.dumps(report, indent=2) + '\n')
    (output / 'all-nonexact-values.json').write_text(json.dumps(transitions, indent=2) + '\n')
    print(report['matched'], '/', report['observations'], '; no introduced failures; all6 regression outputs byte-identical')


if __name__ == '__main__':
    main()
