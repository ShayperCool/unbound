#!/usr/bin/env python3
"""Archive final gates and hash artifacts, retaining every failing or rounded record."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent
WORK = Path(os.environ['LIFECYCLE_WORK'])
SCENE = ROOT.parent/'physicsscenesettings'
DEST = ROOT/'Evidence/validation'


def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()


def main():
    DEST.mkdir(exist_ok=True)
    result = {'base_commit': '7c0bde630f630c55181a31ac7563954323e774da',
              'main_abi': 5, 'physx_shim': 8, 'shader_cache': 13, 'focused': [], 'comparators': {}}
    def archive(src, name):
        dest = DEST/name; dest.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(src, dest)
    spec = importlib.util.spec_from_file_location('scene_cmp', SCENE/'compare.py')
    cmp = importlib.util.module_from_spec(spec); spec.loader.exec_module(cmp)
    for name in ['clone-placement', 'transform-clone-placement', 'sleep-inactive', 'inactive-sequences', 'sleep',
                 'triggers', 'flags', 'trigger-pairs', 'ignore-lifecycle', 'kinematic', 'order', 'component', 'barrier']:
        if name in ['clone-placement', 'transform-clone-placement']:
            ref = SCENE/'ClonePlacementEvidence'/(name+'-native1.jsonl')
        elif name == 'inactive-sequences': ref = SCENE/'InactiveEvidence/inactive-sequences-native1.jsonl'
        elif name in ['sleep-inactive', 'sleep']: ref = SCENE/'SleepEvidence'/(name+'-native1.jsonl')
        elif name in ['flags', 'trigger-pairs', 'ignore-lifecycle']: ref = SCENE/'SDKLifecycleEvidence/Reference'/(name+'.jsonl')
        else: ref = SCENE/'ParentEvidence/Reference'/(name+'.jsonl')
        source = Path(os.environ['SCENE_PARENT_WORK'])/(name+'-final.jsonl')
        a,b = [cmp.read(str(p)) for p in [ref,source]]
        assert a.keys()==b.keys() and all(cmp.matches(k,a[k],b[k]) for k in a), name
        if name not in ['order','component','barrier']: assert a==b, name
        result['focused'].append(dict(probe=name,records=len(a),matched=len(a),exact=sum(a[k]==b[k] for k in a)))
        archive(source, Path('focused')/source.name)
    old_broad = json.loads((ROOT.parent/'trigger2dtiming/Evidence/accepted/physics-final.json').read_text())
    final_broad = json.loads((WORK/'physics-final.json').read_text())
    three_d = [n for n in old_broad if n.startswith('3d.')]
    assert all(old_broad[n] == final_broad[n] for n in three_d)
    result['broad_3d'] = dict(scenarios=len(three_d), records=sum(len(old_broad[n]) for n in three_d), data_identical=True)
    result['matrix'] = json.loads((WORK/'matrix/summary.json').read_text())
    assert result['matrix']['records']==result['matrix']['matched']==5055
    assert result['matrix']['exact']==4803
    assert all(p['byte_identical_to_accepted'] for p in result['matrix']['profiles'])
    for f in (WORK/'matrix').iterdir():
        if f.is_file(): archive(f,Path('matrix')/f.name)
    for name,src,ref in [
        ('physicsdefaults',Path(os.environ['SCENE_WORK'])/'regressions-final/physicsdefaults.jsonl',ROOT.parent/'physicsdefaults/Reference/unity-6000.0.71f1.jsonl'),
        *[(n,Path(os.environ['SCENE_WORK'])/'regressions-final'/n, ROOT.parent/n/'Reference') for n in ['joints','controller','rayhits']],
        *[(n,WORK/(n+'-final'),ROOT.parent/n/'Reference') for n in ['joints2d','effectors','forces']],
        ('graphics',WORK/'graphics.json',ROOT.parent/'graphics/Reference/unity-6000.0.71f1.json'),
        ('canvaslifecycle',WORK/'canvaslifecycle',ROOT.parent/'canvaslifecycle/Reference'),
        ('messages',WORK/'messages.jsonl',ROOT.parent/'messages/Reference/unity-6000.0.71f1.jsonl')]:
        log = DEST/(name+'-comparison.log')
        with log.open('w') as f:
            subprocess.run(['python3',str(ROOT.parent/name/'compare.py'),str(ref),str(src)],check=True,stdout=f,stderr=subprocess.STDOUT)
        result['comparators'][name] = log.read_text()
        if src.is_file(): archive(src,Path('regressions')/src.name)
        else:
            for f in src.glob('*.jsonl'): archive(f,Path('regressions')/name/f.name)
    assert '8671/8671' in result['comparators']['canvaslifecycle']
    assert '316/316' in result['comparators']['messages']
    assert len(json.loads((WORK/'graphics.json').read_text())['cases'])==80
    assert (WORK/'lifecycle.jsonl').read_bytes()==(ROOT.parent/'lifecycle/Reference/unity-6000.0.71f1.jsonl').read_bytes()
    archive(WORK/'lifecycle.jsonl',Path('regressions/lifecycle.jsonl'))
    result['lifecycle_native_byte_identical'] = True
    result['tests'] = ET.parse(WORK/'tests/integrated.trx').find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
    assert result['tests']['passed']==result['tests']['total'] and int(result['tests']['passed'])>=362
    assert result['tests']['failed']=='0'
    for name in ['tests-integrated.log','build-final.log']: archive(WORK/name,Path('logs')/name)
    archive(WORK/'tests/integrated.trx',Path('logs/integrated.trx'))
    result['snapshot_verification']={}
    for base in sorted((Path.home()/'.cache/compat').glob('parity-baseline*')):
        if not (base/'manifest.json').exists(): continue
        manifest=json.loads((base/'manifest.json').read_text())
        hashes=manifest.get('sha256') or {p:v['sha256'] for p,v in manifest.items() if isinstance(v,dict) and 'sha256' in v}
        assert hashes and all(sha(base/p)==h for p,h in hashes.items()), str(base)
        result['snapshot_verification'][str(base)]={'files':len(hashes),'manifest_sha256':sha(base/'manifest.json')}
    native='4a543241a78444ce2595cf8ce42f370c692e993388d42479913558b875d7a208'
    paths=[Path(os.environ['SCENE_SETTINGS_CLI']), WORK/'libunbound_physx.so']
    paths += [f for f in (WORK/'managed-final').rglob('*') if f.is_file()]
    paths += list((WORK/'test-build').rglob('libunbound_physx.so'))
    for p in paths:
        if p.name=='libunbound_physx.so': assert sha(p)==native, str(p)
    result['artifacts_sha256']={str(p):sha(p) for p in paths}
    result['private_candidate_artifacts_sha256'] = {str(p):sha(p) for n in ['candidate2','candidate3'] for p in (WORK/'artifacts'/n).rglob('*') if p.is_file()}
    for name in ['tests-joints-comparator.log','tests-effectors-comparator.log','validation-focused-final.log','validation-broad-final.log','validation-matrix-final.log','validation-visual-final.log','verification-final.log','ccc-final-refresh.log','ccc-final-refresh-status.log']:
        archive(WORK/name, Path('logs')/name)
    result['source_sha256']={str(p):sha(p) for p in [ROOT.parents[2]/n for n in subprocess.check_output(['git','diff','--name-only','7c0bde63','--','managed'],text=True).splitlines()]}
    result['limits']=['Unity 6000.0.71f1 Linux only; older releases unmeasured.', 'No full engine parity claim.',
                      'Final API check and sibling integration belong to the parent.']
    result['evidence_sha256']={str(p.relative_to(ROOT)):sha(p) for p in ROOT.rglob('*') if p.is_file() and '__pycache__' not in str(p) and p.name!='validation-summary.json'}
    (ROOT/'Evidence/validation-summary.json').write_text(json.dumps(result,indent=2)+'\n')
    print('All historical gates verified; private artifacts and immutable snapshots hashed.')


if __name__=='__main__':main()
