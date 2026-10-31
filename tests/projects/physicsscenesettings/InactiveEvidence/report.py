#!/usr/bin/env python3
"""Strict inactive continuation gate. Historical SleepEvidence is read-only input."""
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from compare import matches, read
from run import PROFILES


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def load(path):
    rows = [json.loads(line) for line in Path(path).read_text().splitlines() if line.strip()]
    assert len({r['key'] for r in rows}) == len(rows), ('duplicate key', str(path))
    return {r['key']: r['value'] for r in rows}


def differences(reference, candidate, exact=False):
    assert reference.keys() == candidate.keys(), 'record keys changed'
    return [dict(key=k, reference=v, candidate=candidate[k]) for k, v in reference.items()
            if (v != candidate[k] if exact else not matches(k, v, candidate[k]))]


def main():
    work = Path(os.environ['INACTIVE_WORK'])
    output = ROOT / 'InactiveEvidence/final'
    output.mkdir(parents=True, exist_ok=True)
    summary = dict(source_commit=subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
                   parent='d5d42fb25dca89d9d8541f553cb3697417d030e9', main_abi=5, physx_shim=8,
                   shader_cache=13, profiles=[], focused=[], regressions=[])
    manifest = {}

    def archive(source, relative):
        dest = output / relative
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, dest)
        manifest[str(relative)] = digest(dest)

    native_manifest = json.loads((ROOT/'InactiveEvidence/native-manifest.json').read_text())
    assert all(digest(p) == h for p,h in native_manifest['sha256'].items()), 'Native inputs changed'
    summary['native_manifest_sha256'] = digest(ROOT/'InactiveEvidence/native-manifest.json')
    nonexact = []
    for prefix, names in [('', list(PROFILES)), ('contacts-', ['default', 'pairs-kk', 'pairs-ks', 'pairs-all']),
                          ('islands-', ['default', 'determinism', 'tgs', 'tgs-det'])]:
        for name in names:
            filename = name + '.jsonl'
            ref = load(ROOT / 'Reference' / prefix.rstrip('-') / filename)
            assert ref == load(ROOT / 'Evidence' / (prefix + 'native1') / filename)
            assert ref == load(ROOT / 'Evidence' / (prefix + 'native2') / filename)
            source = work / 'work' / (prefix + 'final') / filename
            candidate = load(source)
            failed = differences(ref, candidate)
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
    assert summary['exact'] == 4803

    for name in ['sleep-inactive', 'inactive-sequences', 'sleep', 'triggers', 'flags', 'trigger-pairs',
                 'ignore-lifecycle', 'kinematic', 'order', 'barrier', 'component']:
        if name == 'inactive-sequences':
            refpath = ROOT / 'InactiveEvidence/inactive-sequences-native1.jsonl'
        elif name in ['sleep', 'sleep-inactive']:
            refpath = ROOT / 'SleepEvidence' / (name + '-native1.jsonl')
        else:
            directory = 'SDKLifecycleEvidence' if name in ['flags', 'trigger-pairs', 'ignore-lifecycle'] else 'ParentEvidence'
            refpath = ROOT / directory / 'Reference' / (name + '.jsonl')
        if name in ['sleep-inactive', 'inactive-sequences', 'sleep']:
            assert refpath.read_bytes() == refpath.with_name(name + '-native2.jsonl').read_bytes()
        source = work / 'probes' / (name + '-final.jsonl')
        ref, candidate = load(refpath), load(source)
        exact_required = name not in ['order', 'barrier', 'component']
        failed = differences(ref, candidate, exact=exact_required)
        assert not failed, (name, failed)
        row = dict(probe=name, records=len(ref), matched=len(ref), exact=sum(ref[k] == candidate[k] for k in ref),
                   introduced_failures=[], retained_failures=[])
        if name in ['sleep-inactive', 'inactive-sequences']:
            baseline_path = work / 'probes' / (name + '-baseline8.jsonl')
            baseline = load(baseline_path)
            before = differences(ref, baseline, exact=True)
            accepted = {k for k in ref if ref[k] == baseline[k]}
            assert all(candidate[k] == ref[k] for k in accepted)
            row.update(baseline_matched=len(ref)-len(before), resolved_records=len(before),
                       preserved_accepted_records=len(accepted), native_pair_byte_identical=True,
                       baseline_failures=before)
            if name == 'sleep-inactive':
                assert len(ref) == 1050 and len(before) == 732 and len(accepted) == 318
                assert baseline_path.read_bytes() == (ROOT / 'SleepEvidence/final/inactive/baseline8.jsonl').read_bytes()
            for label in ['baseline8', 'candidate1', 'candidate2']:
                archive(work / 'probes' / (name + '-' + label + '.jsonl'), Path('experiments') / (name + '-' + label + '.jsonl'))
                attempt = load(work / 'probes' / (name + '-' + label + '.jsonl'))
                (output / 'experiments' / (name + '-' + label + '-failures.json')).write_text(
                    json.dumps(differences(ref, attempt, exact=True), indent=2) + '\n')
        summary['focused'].append(row)
        archive(source, Path('focused') / (name + '.jsonl'))
    assert summary['focused'][1]['records'] == 4668
    assert summary['focused'][2]['records'] == 20700

    accepted = ROOT / 'SleepEvidence/final'
    for relative in ['physicsdefaults.jsonl', 'physics.json', 'joints/joints.jsonl',
                     'controller/controller.jsonl', 'forces/values.jsonl', 'rayhits/values.jsonl']:
        source = work / 'work/regressions-final' / relative
        old = accepted / 'regressions' / relative
        identical = source.read_bytes() == old.read_bytes()
        row = dict(file=relative, byte_identical_to_accepted_parent=identical)
        if relative == 'physics.json':
            a, b, prior = [json.loads(p.read_text()) for p in [
                ROOT.parent / 'physics/Reference/unity-6000.0.71f1.json', source, old]]
            assert a.keys() == b.keys() == prior.keys()
            assert all(b[s].keys() == prior[s].keys() for s in a), 'broad keys changed from accepted parent'
            def equal(x, y):
                if isinstance(x, (int, float)) and not isinstance(x, bool):
                    return isinstance(y, (int, float)) and math.isfinite(y) and abs(x-y) <= 1e-3*max(1,abs(x))
                return x == y
            missing = object()
            bad = lambda candidate: {(s,k) for s in a for k in a[s].keys() | candidate[s].keys()
                                      if not equal(a[s].get(k,missing), candidate[s].get(k,missing))}
            before, after = bad(prior), bad(b)
            assert after == before and len(after) == 133, ('broad physics failures changed', sorted(after-before))
            assert identical, 'retain the exact pre-existing 2D values'
            row.update(scenarios=len(a), passing_scenarios=sum(not any(s == case for s,k in after) for case in a),
                       prior_failed_values=len(before), failed_values=len(after), resolved_values=0,
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
    assert int(counts['total']) == int(counts['passed']) == 337 and int(counts['failed']) == 0
    summary['tests'] = dict(existing=327, added=10, total=337, passed=337, failed=0, skipped=0)
    for filename in ['integrated-tests.log', 'build-candidate1.log', 'build-candidate2.log', 'build-final.log', 'tests/integrated.trx',
                     'validate.log', 'validate.sh', 'env.sh', 'graphics.log', 'graphics-comparison.log',
                     'graphics-device-lost.log', 'build-importguard.log']:
        archive(work/filename, Path('validation')/Path(filename).name)
    for name in ['inactive-sequences', 'sleep-inactive']:
        for path in sorted((work/'probes').glob(name+'-*.log')):
            archive(path, Path('validation')/path.name)
    summary['rejected_attempts'] = [dict(label='candidate1', reason='Private copied outputs retained read-only snapshot permissions; build failed. Captures are diagnostic only and never accepted.',
                                        build_log='validation/build-candidate1.log'),
                                    dict(label='rejected-import-order', reason='Attachment assumed a Transform was already allocated. The strict key gate rejected missing imported-body controls. Complete first regression run is retained.', path='rejected-import-order'),
                                    dict(label='graphics-device-lost', reason='Vulkan RequestDeviceError Device(Lost); final fresh rerun passed 80/80.', path='validation/graphics-device-lost.log')]
    for path in sorted((work/'rejected-import-order').rglob('*')):
        if path.is_file(): archive(path, Path('rejected-import-order')/path.relative_to(work/'rejected-import-order'))
    summary['immutable_baselines'] = []
    for name in ['parity-baseline-native6', 'parity-baseline-native7', 'parity-baseline-native8', 'parity-baseline-sleep3d-native8']:
        frozen = Path.home()/'.cache/compat'/name
        snapshot = json.loads((frozen/'manifest.json').read_text())
        assert all(digest(frozen/p) == h for p,h in (snapshot['sha256'] if 'sha256' in snapshot else {p:v['sha256'] for p,v in snapshot.items()}).items()), ('immutable snapshot changed', name)
        summary['immutable_baselines'].append(dict(path=str(frozen), manifest_sha256=digest(frozen/'manifest.json'), verified=True))
    frozen = Path.home()/'.cache/compat/parity-baseline-sleep3d-native8'
    snapshot = json.loads((frozen/'manifest.json').read_text())
    artifacts = dict(cli=Path(os.environ['SCENE_SETTINGS_CLI']),
                     managed=Path(os.environ['UNBOUND_MANAGED_DIR'])/'UnityEngine.CoreModule.dll',
                     host=Path(os.environ['UNBOUND_MANAGED_DIR'])/'Unbound.Host.dll',
                     physx8=Path(os.environ['UNBOUND_MANAGED_DIR'])/'libunbound_physx.so',
                     test_managed=work/'test-build/bin/UnityEngine/Release/net10.0/UnityEngine.CoreModule.dll',
                     test_physx8=work/'test-build/bin/UnityEngine.Tests/Release/net10.0/libunbound_physx.so')
    summary['artifacts'] = {name:dict(path=str(p), sha256=digest(p)) for name,p in artifacts.items()}
    assert digest(artifacts['test_physx8']) == digest(artifacts['physx8']) == snapshot['sha256']['libunbound_physx.so']
    assert digest(artifacts['cli']) == snapshot['sha256']['unbound']
    assert artifacts['physx8'].stat().st_ino != (frozen/'managed/libunbound_physx.so').stat().st_ino
    graphics = json.loads((work/'graphics.json').read_text())
    graphics_ref = json.loads((ROOT.parent/'graphics/Reference/unity-6000.0.71f1.json').read_text())
    assert graphics.keys() == graphics_ref.keys()
    assert [case['name'] for case in graphics['cases']] == [case['name'] for case in graphics_ref['cases']]
    assert len(graphics['cases']) == 80
    subprocess.run(['python3', str(ROOT.parent/'graphics/compare.py'),
                    str(ROOT.parent/'graphics/Reference/unity-6000.0.71f1.json'), str(work/'graphics.json')], check=True)
    summary['graphics'] = dict(records=80, matched=80)
    archive(work/'graphics.json', Path('regressions/graphics.json'))
    summary['source_sha256'] = {str(p.relative_to(ROOT.parents[2])):digest(p) for p in [
        ROOT.parents[2]/'managed/UnityEngine/Physics/Rigidbody.cs',
        ROOT.parents[2]/'managed/UnityEngine/Physics/PhysicsWorld.cs',
        ROOT.parents[2]/'managed/UnityEngine.Tests/PhysicsTests.cs',
        ROOT/'ParentProbes/InactiveCommandSequences.cs', Path(__file__)]}
    artifact_files = {str(p):digest(p) for p in sorted(Path(os.environ['UNBOUND_MANAGED_DIR']).iterdir()) if p.is_file()}
    artifact_files.update({str(p):digest(p) for p in artifacts.values()})
    (output/'artifact-manifest.json').write_text(json.dumps(artifact_files,indent=2)+'\n')
    summary['limits'] = ['Measured on Unity 6000.0.71f1 Linux; older releases remain unmeasured.',
                         'Seven baseline 2D scenarios retain 133 failing values, unchanged.',
                         'Final integrated API check and shared documentation belong to the parent.',
                         'No full engine parity claim.']
    (output/'all-nonexact-values.json').write_text(json.dumps(nonexact,indent=2)+'\n')
    # Hash every archived file, including all explicit experimental failure lists.
    summary['evidence_sha256'] = {str(p.relative_to(output)):digest(p) for p in sorted(output.rglob('*'))
                                  if p.is_file() and p.name != 'summary.json'}
    (output/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
    print('Inactive gate: 1050/1050 exact, 732 resolved, 0 introduced/retained; 4668/4668 controls; '
          '20700/20700 active; 5055/5055 matrix; 337/337 tests; graphics 80/80; no introduced broad failures')


if __name__ == '__main__':
    main()
