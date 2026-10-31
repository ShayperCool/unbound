#!/usr/bin/env python3
"""Archive continuation experiments with every difference and the original tolerances."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET

from compare import matches, read
from run import ROOT

WORK = Path(os.environ['KD_WORK'])
OUT = ROOT / 'ContinuationEvidence'


def copy(source, dest):
    dest.parent.mkdir(parents=True, exist_ok=True)
    if dest.exists():
        assert dest.read_bytes() == source.read_bytes(), f'refusing to overwrite evidence: {dest}'
    else:
        shutil.copy2(source, dest)


def matrix(label):
    profiles, differences = [], []
    for prefix in ['', 'contacts-', 'islands-']:
        for source in sorted((WORK / 'work' / (prefix + label)).glob('*.jsonl')):
            reference = ROOT / 'Reference' / prefix.rstrip('-') / source.name
            a, b = read(str(reference)), read(str(source))
            assert a == read(str(ROOT / 'Evidence' / (prefix + 'native1') / source.name))
            assert a == read(str(ROOT / 'Evidence' / (prefix + 'native2') / source.name))
            baseline = read(str(ROOT / 'ParentEvidence/accepted' / (prefix + 'final') / source.name))
            keys = a.keys() | b.keys()
            bad = sorted(k for k in keys if not matches(k, a.get(k), b.get(k)))
            introduced = [k for k in bad if matches(k, a.get(k), baseline.get(k))]
            fixed = sorted(k for k in keys if not matches(k, a.get(k), baseline.get(k)) and k not in bad)
            profiles.append(dict(profile=prefix + source.stem, records=len(keys),
                                 exact=sum(a.get(k) == b.get(k) for k in keys),
                                 matched=len(keys)-len(bad), failed_keys=bad,
                                 introduced=introduced, fixed=fixed))
            for key in sorted(keys):
                if a.get(key) != b.get(key):
                    differences.append(dict(profile=prefix + source.stem, key=key,
                        native=a.get(key), candidate=b.get(key), accepted=baseline.get(key),
                        passing=key not in bad))
            copy(source, OUT / 'experiments' / label / (prefix + 'final') / source.name)
    assert len(profiles) == 21 and sum(p['records'] for p in profiles) == 5055
    report = dict(profiles=profiles, records=5055,
                  exact=sum(p['exact'] for p in profiles), matched=sum(p['matched'] for p in profiles),
                  introduced=sum(len(p['introduced']) for p in profiles),
                  fixed=sum(len(p['fixed']) for p in profiles))
    dest = OUT / 'experiments' / label
    (dest / 'summary.json').write_text(json.dumps(report, indent=2) + '\n')
    (dest / 'all-nonexact-values.json').write_text(json.dumps(differences, indent=2) + '\n')
    return report


def main():
    reports = {}
    for label in ['local7', 'update8']:
        row = matrix(label)
        reports[label] = {k: row[k] for k in ['records', 'exact', 'matched', 'introduced', 'fixed']}
    focused = {}
    reference = read(str(WORK / 'controls/original-native1.jsonl'))
    baseline = read(str(WORK / 'controls/original-frozen7.jsonl'))
    for label in ['flush8', 'perbody7', 'local7', 'update8', 'born8']:
        source = WORK / 'controls' / ('original-' + label + '.jsonl')
        candidate = read(str(source)); keys = reference.keys() | candidate.keys()
        bad = sorted(k for k in keys if not matches(k, reference.get(k), candidate.get(k)))
        focused[label] = dict(records=len(keys), exact=sum(reference.get(k) == candidate.get(k) for k in keys),
            matched=len(keys)-len(bad), failed_keys=bad,
            introduced=sorted(k for k in bad if matches(k, reference.get(k), baseline.get(k))))
        folder = OUT / 'experiments' / label
        copy(source, folder / 'focused.jsonl')
        copy(WORK / 'experiments' / label / 'runtime.patch', folder / 'runtime.patch')
        (folder / 'focused-summary.json').write_text(json.dumps(focused[label], indent=2) + '\n')
    final_focused = []
    for name in ['kinematic', 'triggers']:
        a = read(str(ROOT / 'ParentEvidence/Reference' / (name + '.jsonl')))
        source = WORK / 'probes' / (name + '-final.jsonl')
        b = read(str(source)); accepted = read(str(ROOT / 'ParentEvidence/accepted/focused' / (name + '.jsonl')))
        assert b == accepted, name + ' differs from accepted values'
        bad = sorted(k for k in a.keys() | b.keys() if not matches(k, a.get(k), b.get(k)))
        final_focused.append(dict(probe=name, records=len(a.keys() | b.keys()), matched=len(a.keys() | b.keys())-len(bad), failed_keys=bad))
        copy(source, OUT / 'final/focused' / (name + '.jsonl'))
    regressions = []
    for path in ['physicsdefaults.jsonl', 'physics.json', 'joints/joints.jsonl', 'controller/controller.jsonl', 'forces/values.jsonl', 'rayhits/values.jsonl']:
        source = WORK / 'regressions-final' / path
        assert source.read_bytes() == (ROOT / 'ParentEvidence/accepted/regressions' / path).read_bytes(), path
        copy(source, OUT / 'final/regressions' / path)
        regressions.append(dict(file=path, byte_identical_to_accepted=True))
    trx = ET.parse(WORK / 'tests/final.trx')
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    counts = trx.find('.//t:Counters', ns).attrib
    assert counts['total'] == counts['passed'] == '302'
    physics = [e for e in trx.findall('.//t:UnitTestResult', ns) if any(x in e.attrib['testName'] for x in ['PhysicsTests.', 'Physics2DTests.'])]
    assert len(physics) == 41 and all(e.attrib['outcome'] == 'Passed' for e in physics)
    copy(WORK / 'tests/final.trx', OUT / 'final/tests/integrated.trx')
    copy(WORK / 'tests/local7.trx', OUT / 'experiments/local7/tests.trx')
    sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
    snapshot = Path.home() / '.cache/compat/parity-baseline-native7'
    manifest = json.loads((snapshot / 'manifest.json').read_text())
    assert all(sha(snapshot / p) == digest for p, digest in manifest['sha256'].items())
    copy(snapshot / 'manifest.json', OUT / 'final/snapshot-manifest.json')
    summary = dict(runtime_changes_accepted=False, final_records=5055, final_exact=4775, final_matched=5027,
        final_failed=28, final_rounding=252, final_complete_matrix='frozen7/; every value equals parent accepted',
        final_uses_unchanged_frozen7_runtime=True, experiments=reports, focused_experiments=focused,
        final_focused=final_focused, final_regressions=regressions, integrated_tests=302, physics_tests=41,
        private_work=str(WORK), runtime_source_sha256={p: sha(Path(p)) for p in [
            'managed/UnityEngine/Physics/PhysicsWorld.cs', 'managed/UnityEngine/Physics/Rigidbody.cs',
            'managed/UnityEngine/Physics/Physics.cs', 'managed/UnityEngine/Physics/Box3D.cs', 'native/unbound_physx.cpp']})
    (OUT / 'final/summary.json').write_text(json.dumps(summary, indent=2) + '\n')
    print('Final 5027/5055, 28 retained; 302/302 tests; six regression outputs unchanged')


if __name__ == '__main__':
    main()
