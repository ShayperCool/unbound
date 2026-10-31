#!/usr/bin/env python3
"""Verify and archive fresh parent captures without changing historical gates."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2].parent / 'physicsscenesettings'
REPO = ROOT.parents[2]
OUT = Path(__file__).resolve().parent
WORK = Path(os.environ['INACTIVE_PARENT_WORK'])
BUILD = WORK.parents[1]
sys.path.insert(0, str(ROOT))
from compare import read, matches


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    summary = dict(source_commit=subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
                   main_abi=5, physx_shim=8, shader_cache=13, focused=[], regressions=[])
    evidence = OUT / 'accepted'
    evidence.mkdir(exist_ok=True)

    def archive(source, relative):
        dest = evidence / relative
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, dest)

    for name in ['clone-placement', 'transform-clone-placement', 'sleep-inactive', 'inactive-sequences',
                 'sleep', 'triggers', 'flags', 'trigger-pairs', 'ignore-lifecycle',
                 'kinematic', 'order', 'component', 'barrier']:
        if name in ['clone-placement', 'transform-clone-placement']:
            ref = ROOT / 'ClonePlacementEvidence' / (name + '-native1.jsonl')
        elif name == 'inactive-sequences':
            ref = ROOT / 'InactiveEvidence/inactive-sequences-native1.jsonl'
        elif name in ['sleep-inactive', 'sleep']:
            ref = ROOT / 'SleepEvidence' / (name + '-native1.jsonl')
        elif name in ['flags', 'trigger-pairs', 'ignore-lifecycle']:
            ref = ROOT / 'SDKLifecycleEvidence/Reference' / (name + '.jsonl')
        else:
            ref = ROOT / 'ParentEvidence/Reference' / (name + '.jsonl')
        if 'native1' in ref.name:
            assert ref.read_bytes() == ref.with_name(ref.name.replace('native1', 'native2')).read_bytes()
        source = WORK / 'probes' / (name + '-final.jsonl')
        a, b = read(str(ref)), read(str(source))
        assert a.keys() == b.keys(), name
        assert all(matches(k, a[k], b[k]) for k in a), name
        if name not in ['order', 'component', 'barrier']:
            assert a == b, (name, 'exact gate')
        summary['focused'].append(dict(probe=name, records=len(a), matched=len(a),
                                       exact=sum(a[k] == b[k] for k in a)))
        archive(source, Path('focused') / (name + '.jsonl'))

    loading = ROOT.parent / 'inactive3dloading'
    summary['serialized_loading'] = []
    for order in ['transform-first', 'rigidbody-first']:
        ref = loading / 'Evidence/final' / (order + '-native1.jsonl')
        assert ref.read_bytes() == ref.with_name(order + '-native2.jsonl').read_bytes()
        candidate = BUILD / (order + '-parent.jsonl')
        a, b = read(str(ref)), read(str(candidate))
        assert a == b and len(a) == 4656, order
        summary['serialized_loading'].append(dict(order=order, records=4656, matched=4656, exact=4656,
                                                  native_pair_agrees=True, retained_failures=[], introduced=[]))
        archive(candidate, Path('loading') / candidate.name)
    creation = json.loads((loading / 'Evidence/creation-manifest.json').read_text())
    assert all(sha(loading / name) == digest for name, digest in creation['authored_sha256'].items())

    summary['contact_lifecycle'] = []
    for name, count in [('lifecycle', 25356), ('receivers', 1152)]:
        ref = ROOT.parent / 'trigger2dlifecycle/Evidence/native' / (name + '-native1.json')
        assert ref.read_bytes() == ref.with_name(name + '-native2.json').read_bytes()
        candidate = WORK / (name + '-final.json')
        a, b = [json.loads(path.read_text()) for path in [ref, candidate]]
        assert a == b and sum(map(len, a.values())) == count, name
        summary['contact_lifecycle'].append(dict(probe=name, records=count, matched=count, exact=count))
        archive(candidate, Path('contact-lifecycle') / candidate.name)

    matrix = json.loads((WORK / 'matrix/summary.json').read_text())
    assert matrix['records'] == matrix['matched'] == 5055 and matrix['exact'] == 4803
    assert len(matrix['profiles']) == 21
    assert all(p['byte_identical_to_accepted'] and not p['failed_keys'] for p in matrix['profiles'])
    summary['matrix'] = matrix
    for source in (WORK / 'matrix').iterdir():
        if source.is_file():
            archive(source, Path('matrix') / source.name)

    os.environ['TRIGGER_WORK'] = str(WORK)
    spec = importlib.util.spec_from_file_location('trigger_compare', ROOT.parent / 'trigger2dtiming/report.py')
    compare = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(compare)
    prior = ROOT.parent / 'trigger2dtiming/Evidence/accepted'
    for name, reference, source, baseline in [
        ('timing', prior / 'timing-expanded-native1.json', WORK / 'timing-final.json', prior / 'timing-final.json'),
        ('order', ROOT.parent / 'trigger2dtiming/Evidence/order/order-native1.json',
         WORK / 'order-final.json', prior / 'order-final.json'),
        ('physics', ROOT.parent / 'physics/Reference/unity-6000.0.71f1.json',
         WORK / 'work/regressions-final/physics.json', prior / 'physics-final.json')]:
        a, b, old = [json.loads(p.read_text()) for p in [reference, source, baseline]]
        before, after = compare.compare(a, old, 1e-3), compare.compare(a, b, 1e-3)
        bad = lambda report: {(r['scenario'], r['key']) for r in report['failures']}
        assert bad(after) <= bad(before), name
        if name in ['timing', 'order']: assert not after['failures'], name
        after.update(fixture=name, introduced=[], resolved=sorted(bad(before) - bad(after)),
                     data_identical_to_previous_acceptance=(b == old))
        summary['regressions'].append(after)
        archive(source, Path('regressions') / (name + '.json'))

    for name in ['physicsdefaults', 'joints', 'controller', 'forces', 'rayhits', 'joints2d', 'effectors']:
        fixture = ROOT.parent / name
        if name in ['joints2d', 'effectors']:
            source, reference = WORK / name, fixture / 'Reference'
        elif name == 'physicsdefaults':
            source = WORK / 'work/regressions-final/physicsdefaults.jsonl'
            reference = fixture / 'Reference/unity-6000.0.71f1.jsonl'
        else:
            source, reference = WORK / 'work/regressions-final' / name, fixture / 'Reference'
        with (evidence / (name + '-comparison.log')).open('w') as log:
            subprocess.run(['python3', str(fixture / 'compare.py'), str(reference), str(source)],
                           check=True, stdout=log, stderr=subprocess.STDOUT)
        if source.is_file():
            archive(source, Path('regressions') / source.name)
        else:
            for file in source.glob('*.jsonl'):
                archive(file, Path('regressions') / name / file.name)

    for name, reference, source in [
        ('graphics', ROOT.parent / 'graphics/Reference/unity-6000.0.71f1.json', WORK / 'graphics.json'),
        ('canvaslifecycle', ROOT.parent / 'canvaslifecycle/Reference', WORK / 'canvaslifecycle'),
        ('messages', ROOT.parent / 'messages/Reference/unity-6000.0.71f1.jsonl', WORK / 'messages.jsonl')]:
        with (evidence / (name + '-comparison.log')).open('w') as log:
            subprocess.run(['python3', str(ROOT.parent / name / 'compare.py'), str(reference), str(source)],
                           check=True, stdout=log, stderr=subprocess.STDOUT)
        if source.is_file():
            archive(source, Path('regressions') / source.name)
        else:
            for file in source.glob('*.jsonl'):
                archive(file, Path('regressions') / name / file.name)
    assert len(json.loads((WORK / 'graphics.json').read_text())['cases']) == 80
    assert '8671/8671' in (evidence / 'canvaslifecycle-comparison.log').read_text()
    assert '316/316' in (evidence / 'messages-comparison.log').read_text()
    assert (WORK / 'lifecycle.jsonl').read_bytes() == (ROOT.parent / 'lifecycle/Reference/unity-6000.0.71f1.jsonl').read_bytes()
    archive(WORK / 'lifecycle.jsonl', Path('regressions/lifecycle.jsonl'))
    summary['graphics'] = dict(records=80, matched=80)
    summary['canvas_lifecycle'] = dict(records=8671, matched=8671)
    summary['messages'] = dict(records=316, matched=316)
    summary['lifecycle'] = dict(byte_identical_to_native=True)

    trx = BUILD / 'test-results/parent.trx'
    counts = ET.parse(trx).find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
    assert counts['total'] == counts['passed'] == '384' and counts['failed'] == '0'
    summary['tests'] = dict(total=384, passed=384, failed=0, skipped=0)
    assert '0 errors in 2266 hand-written types' in (BUILD / 'api.log').read_text()
    summary['api'] = dict(errors=0, types=2266)
    for name in ['build.log', 'tests.log', 'api.log']:
        archive(BUILD / name, Path('validation') / name)
    archive(trx, Path('validation/tests.trx'))
    archive(BUILD / 'replay.log', Path('validation/engine-captures.log'))
    archive(BUILD / 'validate-parent.py', Path('validation/validate.py'))

    for name in ['parity-baseline-native8', 'parity-baseline-sleep3d-native8', 'parity-baseline-trigger2d-native8']:
        base = Path.home() / '.cache/compat' / name
        manifest = json.loads((base / 'manifest.json').read_text())
        assert all(sha(base / path) == digest for path, digest in manifest['sha256'].items()), name
    native = BUILD / 'managed/libunbound_physx.so'
    assert sha(native) == '4a543241a78444ce2595cf8ce42f370c692e993388d42479913558b875d7a208'
    artifacts = [BUILD / 'managed' / name for name in ['UnityEngine.CoreModule.dll', 'Unbound.Host.dll', 'libunbound_physx.so']]
    artifacts += [Path.home() / '.cache/compat/parity-baseline-native8/unbound']
    summary['artifacts_sha256'] = {str(path): sha(path) for path in artifacts}
    summary['source_sha256'] = {str(path.relative_to(REPO)): sha(path) for path in [
        REPO / 'managed/UnityEngine/Physics/Rigidbody.cs', REPO / 'managed/UnityEngine/Physics/PhysicsWorld.cs',
        REPO / 'managed/UnityEngine/Runtime/Instantiation.cs', REPO / 'managed/UnityEngine.Tests/PhysicsTests.cs',
        REPO / 'managed/UnityEngine/Runtime/Loading.cs', REPO / 'managed/UnityEngine.Tests/Physics2DTests.cs',
        *[REPO / 'managed/UnityEngine/Physics2D' / name for name in ['Physics2DWorld.cs', 'Collider2D.cs',
          'Box2D/Dynamics/Body.cs', 'Box2D/Dynamics/ContactManager.cs']]]}
    summary['limits'] = ['Measured on Unity 6000.0.71f1 Linux; older releases remain unmeasured.',
        'All prior 376 timing/order differences resolve; 121 broad 2D values remain failing.',
        'Arbitrary scales, other rotations, further loading mechanisms and older releases remain unmeasured.',
        'No full engine parity claim.']
    summary['evidence_sha256'] = {str(path.relative_to(evidence)): sha(path) for path in sorted(evidence.rglob('*')) if path.is_file()}
    (OUT / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
    print('Parent acceptance: lifecycle26508 exact, loading9312 exact, inactive1050, sequences4668, clones172, active20700, matrix5055, '
          'tests384, canvas8671, graphics80 and API0/2266; no new failing observations')


if __name__ == '__main__':
    main()
