#!/usr/bin/env python3
"""Append-only raw captures and complete union-key stage reports."""
import hashlib,importlib.util,json,os,shutil,subprocess
from pathlib import Path
from compare import read,compare
ROOT=Path(__file__).resolve().parent
WORK=Path(os.environ['CONTACT_WORK'])
EVIDENCE=ROOT/'Evidence'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def archive(source,dest):
    dest.parent.mkdir(parents=True,exist_ok=True)
    if dest.exists():
        if sha(dest)==sha(source):return
        dest=dest.with_name(dest.name+'.complete')
        if dest.exists():
            assert sha(dest)==sha(source),str(dest)
            return
    shutil.copy2(source,dest)
def main():
    for source in WORK.glob('*.jsonl'):
        archive(source,EVIDENCE/'raw'/source.name)
        for ext in ['log','editor.log']:
            log=source.with_suffix('.'+ext)
            if log.exists():archive(log,EVIDENCE/'raw'/log.name)
        script=WORK/source.stem/'Assets/Scripts/ContactProbe.cs'
        if script.exists():archive(script,EVIDENCE/'raw'/(source.stem+'-source.cs.txt'))
    rejected=WORK/'rejected-axis-validation/engine'
    excluded={'Assets','Packages','ProjectSettings','Library','projects','managed-final'}
    for source in rejected.rglob('*'):
        if source.is_file() and source.suffix in {'.json','.jsonl','.log','.txt'} and not (set(source.relative_to(rejected).parts)&excluded):
            archive(source,EVIDENCE/'rejected-axis-raw'/source.relative_to(rejected))
    for source in (WORK/'logs').iterdir():
        if source.is_file():archive(source,EVIDENCE/'builds'/source.name)
    for source in (WORK/'tests').glob('*.trx'):archive(source,EVIDENCE/'builds'/source.name)
    for name in ['native','native3','native5','native-final']:
        archive(WORK/name/'build.json',EVIDENCE/'builds'/(name+'-build.json'))
    summary=dict(source_commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),main_abi=5,physx_shim=9,shader_cache=13,profiles=[])
    for i in range(4):
        a,b=[WORK/f'named{n}-p{i}.jsonl' for n in [1,2]]
        assert a.read_bytes()==b.read_bytes(),f'Native disagreement profile{i}'
        ref=read(a);before=compare(ref,read(WORK/f'named-baseline-p{i}.jsonl'),True);after=compare(ref,read(WORK/f'named-p{i}.jsonl'),True)
        old={x['key'] for x in before['differences']};new={x['key'] for x in after['differences']}
        summary['profiles'].append(dict(profile=i,native_records=len(ref),native_pair_byte_identical=True,native_sha256=sha(a),baseline=before,candidate=after,resolved=sorted(old-new),retained=sorted(old&new),introduced=sorted(new-old)))
    summary['controls']=compare(read(WORK/'controls-order1-p0.jsonl'),read(WORK/'controls-accepted-p0.jsonl'))
    summary['controls_native_pair']=compare(read(WORK/'controls-order1-p0.jsonl'),read(WORK/'controls-order2-p0.jsonl'))
    summary['process_hash_invariants']={}
    for name in ['controls-order1-p0','controls-order2-p0','controls-accepted-p0']:
        v=read(WORK/(name+'.jsonl'));summary['process_hash_invariants'][name]=v['scene/0/hash']==v['scene/4/defaultHash']
    summary['geometry']=compare(read(WORK/'geometry-exact1-p0.jsonl'),read(WORK/'geometry-exact-p0.jsonl'),True)
    assert (WORK/'geometry-exact1-p0.jsonl').read_bytes()==(WORK/'geometry-exact2-p0.jsonl').read_bytes()
    summary['geometry_native_pair_byte_identical']=True
    summary['regressions']=json.loads((EVIDENCE/'Regression/summary.json').read_text())
    summary['loading']=[]
    for order in ['transform-first','rigidbody-first']:
        source=WORK/'loading'/(order+'-accepted.jsonl');ref=ROOT.parent/'inactive3dloading/Evidence/ParentIntegration/accepted/loading'/(order+'-parent.jsonl')
        def data(p):return {x['key']:x['value'] for x in map(json.loads,p.read_text().splitlines())}
        a,b=data(ref),data(source);assert a.keys()==b.keys() and a==b
        summary['loading'].append(dict(order=order,records=len(a),exact=len(a),differences=[]));archive(source,EVIDENCE/'loading'/source.name)
    summary['api_check']=json.loads((WORK/'logs/api-check-platforms.json').read_text())
    assert not summary['api_check']['Errors']
    native=WORK/'native/libunbound_physx.so'
    artifacts=[WORK/'unbound',WORK/'managed/UnityEngine.CoreModule.dll',WORK/'managed/Unbound.Host.dll',WORK/'managed/libunbound_physx.so',WORK/'libunbound_physx.so',native,*list((WORK/'test-build/bin/UnityEngine.Tests').rglob('libunbound_physx.so'))]
    assert all(sha(p)==sha(native) for p in artifacts if p.name=='libunbound_physx.so')
    summary['artifacts_sha256']={str(p):sha(p) for p in artifacts}
    summary['source_sha256']={str(p):sha(p) for p in [*Path('managed/UnityEngine/Physics').glob('Contact*.cs'),Path('managed/UnityEngine/Physics/Box3D.cs'),Path('managed/UnityEngine/Physics/PhysicsWorld.cs'),Path('native/unbound_physx.cpp'),Path('managed/UnityEngine.Tests/PhysicsContactEventTests.cs')]}
    summary['native_layout_agrees']=(WORK/'layout-native1-p0.jsonl').read_bytes()==(WORK/'layout-native2-p0.jsonl').read_bytes()
    summary['evidence_sha256']={str(p.relative_to(EVIDENCE)):sha(p) for p in EVIDENCE.rglob('*') if p.is_file() and p.name!='summary.json'}
    output=EVIDENCE/'delivery-summary.json'
    if output.exists():raise SystemExit('refusing overwrite '+str(output))
    output.write_text(json.dumps(summary,indent=2)+'\n')
    print('Profiles',[(r['profile'],r['candidate']['matched'],r['candidate']['records'],len(r['resolved']),len(r['retained']),len(r['introduced'])) for r in summary['profiles']])
if __name__=='__main__':main()
