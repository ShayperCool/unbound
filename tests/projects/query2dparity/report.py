#!/usr/bin/env python3
"""Archive this stage and enforce union-key no-regression comparisons."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET
from compare import compare

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
WORK = Path(os.environ['QUERY_WORK'])
OUT = ROOT / 'Evidence'
SCENE = ROOT.parent / 'physicsscenesettings'
PARENT = SCENE / 'InactiveEvidence/ParentIntegration'

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def load(path):
    return json.loads(path.read_text())

def dump(path, obj):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, indent=2) + '\n')

def archive(source, relative):
    dest = OUT / relative
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, dest)

def bad(report):
    return {(x['scenario'], x['key']) for x in report['failures']}

def delta(reference, before, after):
    a, b = compare(reference, before), compare(reference, after)
    b.update(resolved=sorted(bad(a)-bad(b)), introduced=sorted(bad(b)-bad(a)),
             retained=sorted(bad(a)&bad(b)), baseline_failures=a['failures'])
    return b

def main():
    native = load(WORK / 'native1.json')
    assert (WORK / 'native1.json').read_bytes() == (WORK / 'native2.json').read_bytes()
    baseline = load(WORK / 'baseline.json')
    summary = dict(parent='7c0bde630f630c55181a31ac7563954323e774da', unity='6000.0.71f1 Linux',
                   main_abi=5, physx_shim=8, shader_cache=13, native_agreement=True,
                   native_records=sum(map(len,native.values())), native_sha256=sha(WORK/'native1.json'),
                   candidates={})
    for name in ['baseline', 'toi1', 'manifold2', 'clipping3', 'cache4', 'feature5', 'first6', 'oriented7', 'group8', 'final', 'fixed9', 'query10', 'final10', 'order11', 'core12', 'final12']:
        source = WORK / (name + '.json')
        if not source.exists(): continue
        result = delta(native, baseline, load(source))
        summary['candidates'][name] = {k:v for k,v in result.items() if k not in ['passing_rounding','baseline_failures','failures']}
        archive(source, Path('captures') / source.name)
        dump(OUT / 'comparisons' / (name+'.json'), result)
        if name == 'final12': assert not result['introduced'], result['introduced']
    summary['additional_controls'] = {}
    for probe in ['selection', 'fine', 'boundary', 'tests']:
        first, second = WORK/(probe+'-native1.json'), WORK/(probe+'-native2.json')
        assert first.read_bytes() == second.read_bytes()
        reference = load(first)
        before = load(WORK/(probe+'-baseline.json'))
        rows = {}
        for source in sorted(WORK.glob(probe+'-*.json')):
            if source.name.endswith('-comparison.json') or '-native' in source.name or source.name.endswith('-manifest.json'): continue
            result = delta(reference, before, load(source))
            dump(OUT/'comparisons'/source.name, result)
            archive(source, Path('captures')/source.name)
            rows[source.stem] = {k:v for k,v in result.items() if k not in ['failures','passing_rounding','baseline_failures']}
        summary['additional_controls'][probe] = dict(native_records=sum(map(len,reference.values())), native_sha256=sha(first), results=rows)
    # Support geometry includes a pre-existing edge-chain grouping difference. Keep it visible.
    geometry = compare(load(WORK/'geometry-native1.json'), load(WORK/'geometry-engine.json'))
    dump(OUT/'comparisons/geometry.json', geometry)
    archive(WORK/'geometry-engine.json','captures/geometry-engine.json')
    summary['geometry'] = geometry
    summary['strict_acceptance'] = False
    summary['blocker'] = 'New offset/geometry-lifetime controls introduce failures; see OWNER-PROPOSAL.md. Parent gates passing does not waive these observations.'
    for source in WORK.glob('*.log'): archive(source, Path('logs')/source.name)
    for source in (WORK/'rejected').rglob('*'):
        if source.is_file(): archive(source, Path('rejected')/source.relative_to(WORK/'rejected'))
    summary['broad'] = delta(load(ROOT.parent/'physics/Reference/unity-6000.0.71f1.json'),
                            load(PARENT/'accepted/regressions/physics.json'),
                            load(Path(os.environ['SCENE_WORK'])/'regressions-final/physics.json'))
    assert not summary['broad']['introduced']
    assert len(summary['broad']['resolved']) == 76
    assert len(summary['broad']['failures']) == 55
    assert not any(x['scenario'] in ['2d.queries','2d.queries.options','2d.casts.inside'] for x in summary['broad']['failures'])
    archive(Path(os.environ['SCENE_WORK'])/'regressions-final/physics.json','regressions/physics.json')
    for probe, ref in [('timing', ROOT.parent/'trigger2dtiming/Evidence/accepted/timing-expanded-native1.json'),
                       ('order', ROOT.parent/'trigger2dtiming/Evidence/order/order-native1.json')]:
        source = Path(os.environ['TRIGGER_WORK'])/(probe+'-final.json')
        result = delta(load(ref),load(PARENT/'accepted/regressions'/(probe+'.json')),load(source))
        assert not result['introduced']
        summary[probe] = result
        archive(source,Path('regressions')/(probe+'.json'))
    spec=importlib.util.spec_from_file_location('scene_compare',SCENE/'compare.py')
    scene=importlib.util.module_from_spec(spec);spec.loader.exec_module(scene)
    summary['focused']=[]
    for prior in (PARENT/'accepted/focused').glob('*.jsonl'):
        source=Path(os.environ['SCENE_PARENT_WORK'])/(prior.stem+'-final.jsonl')
        a,b=scene.read(str(prior)),scene.read(str(source))
        assert a.keys()==b.keys() and all(scene.matches(k,a[k],b[k]) for k in a),prior.stem
        if prior.stem not in ['order','component','barrier']:assert a==b,prior.stem
        summary['focused'].append(dict(probe=prior.stem,records=len(a),exact=sum(a[k]==b[k] for k in a)))
        archive(source,Path('focused')/prior.name)
    summary['matrix']=load(Path(os.environ['TRIGGER_WORK'])/'matrix/summary.json')
    assert summary['matrix']['matched']==5055 and summary['matrix']['exact']==4803
    assert all(x['byte_identical_to_accepted'] for x in summary['matrix']['profiles'])
    for p in (Path(os.environ['TRIGGER_WORK'])/'matrix').glob('*'):
        if p.is_file():archive(p,Path('matrix')/p.name)
    for name in ['physicsdefaults','joints','controller','forces','rayhits','joints2d','effectors','graphics','canvaslifecycle','messages']:
        fixture=ROOT.parent/name
        if name in ['joints2d','effectors']: source=Path(os.environ['TRIGGER_WORK'])/name
        elif name in ['graphics','messages']:source=Path(os.environ['TRIGGER_WORK'])/(name+('.json' if name=='graphics' else '.jsonl'))
        elif name=='canvaslifecycle':source=Path(os.environ['TRIGGER_WORK'])/name
        elif name=='physicsdefaults':source=Path(os.environ['SCENE_WORK'])/'regressions-final/physicsdefaults.jsonl'
        else:source=Path(os.environ['SCENE_WORK'])/'regressions-final'/name
        ref=fixture/'Reference'
        if source.is_file():ref=ref/('unity-6000.0.71f1'+source.suffix)
        log=OUT/'regressions'/(name+'-comparison.log')
        with log.open('w') as f:subprocess.run(['python3',str(fixture/'compare.py'),str(ref),str(source)],stdout=f,stderr=subprocess.STDOUT,check=True)
        if source.is_file():archive(source,Path('regressions')/source.name)
        else:
            for p in source.glob('*.jsonl'):archive(p,Path('regressions')/name/p.name)
    source=Path(os.environ['TRIGGER_WORK'])/'lifecycle.jsonl'
    assert source.read_bytes()==(ROOT.parent/'lifecycle/Reference/unity-6000.0.71f1.jsonl').read_bytes()
    archive(source,'regressions/lifecycle.jsonl')
    trx=WORK/'validation/integrated.trx'
    summary['tests']=ET.parse(trx).find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
    assert summary['tests']['total']==summary['tests']['passed']=='390'
    for base in (Path.home()/'.cache/compat').glob('parity-baseline-*'):
        if not (base/'manifest.json').exists():continue
        m=load(base/'manifest.json')
        assert all(sha(base/p)==h for p,h in (m['sha256'] if 'sha256' in m else {p:v['sha256'] for p,v in m.items()}).items()),str(base)
    native_hash='4a543241a78444ce2595cf8ce42f370c692e993388d42479913558b875d7a208'
    assert sha(WORK/'managed-final/libunbound_physx.so')==native_hash
    tests=list((WORK/'test-build/bin/UnityEngine.Tests').rglob('libunbound_physx.so'))
    assert tests and all(sha(p)==native_hash for p in tests)
    summary['artifacts_sha256']={str(p.relative_to(WORK)):sha(p) for folder in ['managed-final','managed-baseline'] for p in sorted((WORK/folder).rglob('*')) if p.is_file()}
    summary['artifacts_sha256']['unbound']=sha(WORK/'unbound')
    for p in (WORK/'validation').glob('*'):
        if p.is_file():archive(p,Path('validation')/p.name)
    summary['limits']=['Only Unity 6000.0.71f1 Linux measured; older releases unmeasured.',
        '55 unrelated broad motion/contact values remain failing.',
        'Every retained expanded-control failure is recorded, including initial-overlap ties and near-touch cases.',
        'Shared API check belongs to parent. No full engine parity claim.']
    summary['evidence_sha256']={str(p.relative_to(OUT)):sha(p) for p in sorted(OUT.rglob('*')) if p.is_file() and p.name!='summary.json'}
    summary['source_sha256'] = {str(p.relative_to(REPO)):sha(p) for p in [REPO/'managed/UnityEngine/Physics2D/Physics2DQueries.cs', *sorted((REPO/'managed/UnityEngine/Physics2D').glob('QueryParity*.cs')), REPO/'managed/UnityEngine.Tests/Physics2DQueryParityTests.cs']}
    summary['native_suites'] = {p.stem:load(p) for p in OUT.glob('*manifest.json') if 'independent_processes' in load(p)}
    summary['private_work'] = str(WORK)
    summary['runtime_commit'] = '64beecb0'
    summary['stopped_replays'] = ['group8', 'query10']
    summary['api_check'] = 'Parent-owned, not rerun by query delegate.'
    dump(OUT/'summary.json',summary)
    print('Parent gates preserved; broad resolves76/retains55/introduces0; tests390. Strict stage acceptance BLOCKED by additional controls; see OWNER-PROPOSAL.md.')

if __name__=='__main__':main()
