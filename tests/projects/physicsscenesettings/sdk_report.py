#!/usr/bin/env python3
"""Archive SDK acceptance while retaining every nonexact and pre-existing failing value."""
import sys
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import shutil
import subprocess

from compare import matches, read
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_root, reference_file, run_root, archived_root

from run import ROOT, PROFILES


def digest(path):
    hasher = hashlib.sha256()
    with Path(path).open('rb') as source:
        for block in iter(lambda: source.read(4 * 1024 * 1024), b''):
            hasher.update(block)
    return hasher.hexdigest()


def main():
    work = Path(os.environ.get('SDK_WORK', run_root(ROOT, 'sdk')))
    output = run_root(ROOT, 'sdk-report')
    output.mkdir(parents=True, exist_ok=True)
    profiles, nonexact, focused, regressions = [], [], [], []
    for prefix, names in [('', list(PROFILES)), ('contacts-', ['default', 'pairs-kk', 'pairs-ks', 'pairs-all']),
                          ('islands-', ['default', 'determinism', 'tgs', 'tgs-det'])]:
        for name in names:
            filename = name + '.jsonl'
            ref = read(str(reference_root(ROOT) / prefix.rstrip('-') / filename))
            source = work / 'work' / (prefix + 'final') / filename
            candidate = read(str(source))
            assert ref.keys() == candidate.keys(), prefix + name
            failed = [k for k in ref if not matches(k, ref[k], candidate[k])]
            assert not failed, (prefix + name, failed)
            exact = sum(ref[k] == candidate[k] for k in ref)
            profiles.append(dict(profile=prefix + name, records=len(ref), exact=exact,
                                 matched=len(ref), failed_keys=failed))
            for key in ref:
                if ref[key] != candidate[key]:
                    nonexact.append(dict(profile=prefix + name, key=key,
                                         reference=ref[key], candidate=candidate[key], passing=True))
            dest = output / (prefix + 'final') / filename
            dest.parent.mkdir(exist_ok=True); shutil.copy2(source, dest)
    assert sum(p['records'] for p in profiles) == 5055
    for name in ['flags', 'trigger-pairs', 'ignore-lifecycle', 'kinematic', 'triggers', 'order']:
        new_probe = name in ['flags', 'trigger-pairs', 'ignore-lifecycle']
        refdir = reference_root(ROOT) / 'focused'
        baseline = archived_root(ROOT, 'SDKLifecycleEvidence/baseline7' if new_probe else 'ParentEvidence/accepted/focused')
        source = work / 'probes' / (name + '-integrated-sdk8.jsonl')
        ref, old, candidate = [read(str(p)) for p in [refdir / (name + '.jsonl'), baseline / (name + '.jsonl'), source]]
        assert ref.keys() == candidate.keys(), name
        failed = [k for k in ref if not matches(k, ref[k], candidate[k])]
        assert all(not matches(k, ref[k], old.get(k)) for k in failed), (name, 'introduced failure')
        assert all(not k.endswith(('/events', '/order', '/raw-contacts', '/motion', '/ignored')) for k in failed), name
        differences = [dict(key=k, reference=ref[k], candidate=candidate[k]) for k in failed]
        focused.append(dict(probe=name, records=len(ref), matched=len(ref)-len(failed), failures=differences))
        dest = output / 'focused' / (name + '.jsonl')
        dest.parent.mkdir(exist_ok=True); shutil.copy2(source, dest)
    for relative in ['physicsdefaults.jsonl', 'physics.json', 'joints/joints.jsonl',
                     'controller/controller.jsonl', 'forces/values.jsonl', 'rayhits/values.jsonl']:
        source = work / 'work/regressions-final' / relative
        baseline = archived_root(ROOT, 'ParentEvidence/accepted/regressions') / relative
        identical = source.read_bytes() == baseline.read_bytes()
        if relative == 'physics.json':
            a, old, b = [json.loads(p.read_text()) for p in [
                reference_file(ROOT.parent / 'physics', 'json'), baseline, source]]
            assert a.keys() == b.keys()
            def equal(x, y):
                if isinstance(x, (int, float)) and not isinstance(x, bool):
                    return isinstance(y, (int, float)) and math.isfinite(y) and abs(x-y) <= 1e-3*max(1,abs(x))
                return x == y
            missing = object()
            keys = {s: a[s].keys() | old[s].keys() | b[s].keys() for s in a}
            old_bad = {(s,k) for s in a for k in keys[s] if not equal(a[s].get(k,missing),old[s].get(k,missing))}
            new_bad = {(s,k) for s in a for k in keys[s] if not equal(a[s].get(k,missing),b[s].get(k,missing))}
            assert new_bad <= old_bad, 'introduced broad physics value'
            regressions.append(dict(file=relative, byte_identical=identical,
                                    prior_failed_values=len(old_bad), failed_values=len(new_bad),
                                    resolved_values=len(old_bad-new_bad)))
            details = [dict(scenario=s,key=k,reference=a[s].get(k,'<missing>'),
                            candidate=b[s].get(k,'<missing>')) for s,k in sorted(new_bad)]
            (output/'regressions').mkdir(exist_ok=True)
            (output/'regressions/physics-retained-failures.json').write_text(json.dumps(details,indent=2)+'\n')
        elif relative.startswith('forces/'):
            spec = importlib.util.spec_from_file_location('forces_compare', ROOT.parent / 'forces/compare.py')
            compare = importlib.util.module_from_spec(spec); spec.loader.exec_module(compare)
            a = compare.load(ROOT.parent / 'forces/Reference'); b = compare.load(source.parent)
            assert a.keys() == b.keys() and all(not list(compare.differences(a[k],b[k])) for k in a)
            regressions.append(dict(file=relative, byte_identical=identical, matched=len(a)))
        else:
            assert identical, 'changed regression output: ' + relative
            regressions.append(dict(file=relative, byte_identical=True))
        dest = output / 'regressions' / relative
        dest.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(source, dest)
    logs = output / 'validation'; logs.mkdir(exist_ok=True)
    shutil.copy2(work/'graphics.json',output/'regressions/graphics.json')
    for filename in ['integrated-api.log', 'integrated-tests.log', 'graphics-comparison.log',
                     'integrated-rust-build.log', 'integrated-rust-cache-check.log', 'integrated-managed-build.log']:
        shutil.copy2(work / filename, logs / filename)
    report = dict(source_commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),
                  main_abi=5, physx_shim=8, shader_cache=13, records=5055,
                  exact=sum(p['exact'] for p in profiles), matched=5055,
                  profiles=profiles, focused=focused, regressions=regressions,
                  artifacts_sha256={name:digest(path) for name,path in {
                      'cli':os.environ['SCENE_SETTINGS_CLI'],
                      'managed':Path(os.environ['UNBOUND_MANAGED_DIR'])/'UnityEngine.CoreModule.dll',
                      'physx8':Path(os.environ['UNBOUND_MANAGED_DIR'])/'libunbound_physx.so'}.items()})
    (output/'summary.json').write_text(json.dumps(report,indent=2)+'\n')
    (output/'all-nonexact-values.json').write_text(json.dumps(nonexact,indent=2)+'\n')
    print(f"SDK gate: 5055/5055, {report['exact']} exact; all retained focused failures explicitly recorded")


if __name__ == '__main__':
    main()
