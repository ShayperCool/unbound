#!/usr/bin/env python3
"""Archive the sleep continuation without changing any historical acceptance evidence."""
import sys
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET

from compare import matches, read
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_root, reference_file, run_root, archived_root

from run import PROFILES, ROOT


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main():
    work = Path(os.environ.get('SLEEP_WORK', run_root(ROOT, 'sleep')))
    output = run_root(ROOT, 'sleep-report')
    output.mkdir(parents=True, exist_ok=True)
    accepted = archived_root(ROOT, 'SDKLifecycleEvidence/accepted')
    summary = dict(source_commit=subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
                   main_abi=5, physx_shim=8, shader_cache=13, profiles=[], focused=[], regressions=[])
    manifest = {}

    def archive(source, relative):
        dest = output / relative
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, dest)
        manifest[str(relative)] = digest(dest)

    nonexact = []
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
            summary['profiles'].append(dict(profile=prefix + name, records=len(ref), matched=len(ref), exact=exact))
            nonexact += [dict(profile=prefix + name, key=k, reference=ref[k], candidate=candidate[k], passing=True)
                         for k in ref if ref[k] != candidate[k]]
            archive(source, Path(prefix + 'final') / filename)
    summary.update(records=sum(p['records'] for p in summary['profiles']),
                   matched=sum(p['matched'] for p in summary['profiles']),
                   exact=sum(p['exact'] for p in summary['profiles']))
    assert summary['records'] == summary['matched'] == 5055
    for name in ['flags', 'trigger-pairs', 'ignore-lifecycle', 'kinematic', 'triggers', 'order', 'barrier', 'component', 'sleep']:
        new_sdk = name in ['flags', 'trigger-pairs', 'ignore-lifecycle']
        reference = reference_root(ROOT) / 'focused' / (name + '.jsonl')
        source = work / 'probes' / (name + '-final.jsonl')
        if name == 'sleep':
            reference = reference_root(ROOT) / 'focused/sleep.jsonl'
            source = work / 'probes/sleep-candidate2.jsonl'
        ref, candidate = read(str(reference)), read(str(source))
        assert ref.keys() == candidate.keys(), name
        assert all(matches(k, ref[k], candidate[k]) for k in ref), (name, 'focused failure')
        if name in ['flags', 'trigger-pairs', 'ignore-lifecycle', 'kinematic', 'triggers', 'sleep']:
            assert ref == candidate, (name, 'sleep controls must match exactly')
        old = accepted / 'focused' / (name + '.jsonl')
        if name == 'sleep':
            old = archived_root(ROOT, 'SleepEvidence/sleep-baseline8.jsonl')
        old_bad = None
        if old.exists():
            baseline = read(str(old))
            old_bad = sum(not matches(k, ref[k], baseline.get(k)) for k in ref)
        summary['focused'].append(dict(probe=name, records=len(ref), exact=sum(ref[k] == candidate[k] for k in ref), matched=len(ref),
                                       resolved_failures=old_bad, baseline=str(old) if old.exists() else None, introduced_failures=[], retained_failures=[]))
        archive(source, Path('focused') / (name + '.jsonl'))
    # This added boundary is diagnostic evidence, not an expected-failure mask. Preserve
    # every Native and engine record and require the frozen baseline's entire output unchanged.
    inactive_ref = reference_root(ROOT) / 'focused/sleep-inactive.jsonl'
    prior_inactive = work / 'probes/sleep-inactive-baseline8.jsonl'
    new_inactive = work / 'probes/sleep-inactive-final.jsonl'
    assert prior_inactive.read_bytes() == new_inactive.read_bytes(), 'inactive boundary changed'
    a, b = read(str(inactive_ref)), read(str(new_inactive))
    assert a.keys() == b.keys()
    failures = [dict(key=k, reference=a[k], candidate=b[k]) for k in a if not matches(k,a[k],b[k])]
    summary['inactive_boundary'] = dict(records=len(a), matched=len(a)-len(failures),
                                       byte_identical_to_frozen8=True, introduced_failures=[],
                                       retained_failures=failures)
    archive(prior_inactive, Path('inactive')/'baseline8.jsonl')
    archive(new_inactive, Path('inactive')/'final.jsonl')
    for relative in ['physicsdefaults.jsonl', 'physics.json', 'joints/joints.jsonl',
                     'controller/controller.jsonl', 'forces/values.jsonl', 'rayhits/values.jsonl']:
        source = work / 'work/regressions-final' / relative
        old = accepted / 'regressions' / relative
        identical = source.read_bytes() == old.read_bytes()
        row = dict(file=relative, byte_identical_to_sdk8=identical)
        if relative == 'physics.json':
            a, b, prior = [json.loads(p.read_text()) for p in [
                reference_file(ROOT.parent / 'physics', 'json'), source, old]]
            assert a.keys() == b.keys() == prior.keys()
            def equal(x, y):
                if isinstance(x, (int, float)) and not isinstance(x, bool):
                    return isinstance(y, (int, float)) and math.isfinite(y) and abs(x-y) <= 1e-3*max(1,abs(x))
                return x == y
            missing = object()
            bad = lambda candidate: {(s,k) for s in a for k in a[s].keys() | candidate[s].keys()
                                      if not equal(a[s].get(k,missing), candidate[s].get(k,missing))}
            before, after = bad(prior), bad(b)
            assert after <= before, ('introduced broad physics values', sorted(after-before))
            row.update(scenarios=len(a), passing_scenarios=sum(not any(s == case for s,k in after) for case in a),
                       prior_failed_values=len(before), failed_values=len(after), resolved_values=len(before-after),
                       introduced_values=[], retained_failures=[dict(scenario=s,key=k,reference=a[s].get(k,'<missing>'),
                                                                    candidate=b[s].get(k,'<missing>')) for s,k in sorted(after)])
        elif relative.startswith('forces/'):
            spec = importlib.util.spec_from_file_location('forces_compare', ROOT.parent / 'forces/compare.py')
            compare = importlib.util.module_from_spec(spec); spec.loader.exec_module(compare)
            a, b = compare.load(ROOT.parent / 'forces/Reference'), compare.load(source.parent)
            assert a.keys() == b.keys() and all(not list(compare.differences(a[k],b[k])) for k in a)
            row.update(records=len(a), matched=len(a))
        else:
            assert identical, ('changed existing regression', relative)
        summary['regressions'].append(row)
        archive(source, Path('regressions') / relative)
    tree = ET.parse(work/'tests/integrated.trx')
    counts = tree.find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
    assert int(counts['total']) == int(counts['passed']) == 327 and int(counts['failed']) == 0
    summary['tests'] = dict(existing=318, added=9, total=327, passed=327, failed=0, skipped=0)
    for filename in ['integrated-tests.log', 'build-candidate2.log', 'tests/integrated.trx']:
        archive(work/filename, Path('validation')/Path(filename).name)
    frozen = Path.home()/'.cache/compat/parity-baseline-native8'
    snapshot = json.loads((frozen/'manifest.json').read_text())
    assert all(digest(frozen/p) == h for p,h in snapshot['sha256'].items()), 'immutable snapshot changed'
    artifacts = dict(cli=Path(os.environ['SCENE_SETTINGS_CLI']),
                     managed=Path(os.environ['UNBOUND_MANAGED_DIR'])/'UnityEngine.CoreModule.dll',
                     host=Path(os.environ['UNBOUND_MANAGED_DIR'])/'Unbound.Host.dll',
                     physx8=Path(os.environ['UNBOUND_MANAGED_DIR'])/'libunbound_physx.so')
    artifacts['test_managed'] = work/'test-build/bin/UnityEngine/Release/net10.0/UnityEngine.CoreModule.dll'
    artifacts['test_physx8'] = work/'test-build/bin/UnityEngine.Tests/Release/net10.0/libunbound_physx.so'
    summary['artifacts'] = {name:dict(path=str(p), sha256=digest(p)) for name,p in artifacts.items()}
    assert digest(artifacts['test_physx8']) == snapshot['sha256']['libunbound_physx.so']
    assert digest(artifacts['cli']) == snapshot['sha256']['unbound']
    assert digest(artifacts['physx8']) == snapshot['sha256']['libunbound_physx.so']
    summary['baseline8_manifest_verified'] = True
    graphics = json.loads((work/'graphics.json').read_text())
    graphics_reference = json.loads((reference_file(ROOT.parent / 'graphics', 'json')).read_text())
    assert graphics.keys() == graphics_reference.keys()
    assert [case['name'] for case in graphics['cases']] == [case['name'] for case in graphics_reference['cases']]
    subprocess.run(['python3', str(ROOT.parent/'graphics/compare.py'),
                    str(reference_file(ROOT.parent / 'graphics', 'json')), str(work/'graphics.json')], check=True)
    summary['graphics'] = dict(records=len(graphics['cases']), matched=len(graphics['cases']))
    archive(work/'graphics.json', Path('regressions/graphics.json'))
    archive(work/'graphics-comparison.log', Path('validation/graphics-comparison.log'))
    summary['limits'] = ['Measured on Unity 6000.0.71f1 Linux; other releases remain unmeasured.',
                         'Seven baseline 2D scenarios retain their explicit failures.',
                         'Inactive actor pose/setter and reactivation state differences remain byte-identical to frozen shim8.',
                         'Final integrated API check and shared documentation belong to the parent.',
                         'No full engine parity claim.']
    (output/'all-nonexact-values.json').write_text(json.dumps(nonexact,indent=2)+'\n')
    manifest['all-nonexact-values.json'] = digest(output/'all-nonexact-values.json')
    summary['evidence_sha256'] = manifest
    (output/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
    print('Sleep gate: 5055/5055 matrix; all focused records pass; 327/327 tests; no introduced broad failures')


if __name__ == '__main__':
    main()
