#!/usr/bin/env python3
"""Archive private stage logs and hash all matching build/native artifacts."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
WORK = Path(os.environ['LOADING_WORK'])


def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()


def main():
    evidence=ROOT/'Evidence/final'
    assert evidence.is_dir()
    for p in (WORK/'logs').iterdir():
        if p.is_file(): shutil.copy2(p,evidence/p.name)
    captures=WORK/'validation/engine'
    for folder in [captures, captures/'probes', captures/'work/regressions-final', captures/'matrix']:
        for p in folder.glob('*.log'):
            dest=ROOT/'Evidence/validation/raw-logs'/p.relative_to(captures)
            dest.parent.mkdir(parents=True,exist_ok=True)
            shutil.copy2(p,dest)
    shutil.copy2(WORK/'env.sh',evidence/'env.sh')
    shutil.copy2(WORK/'tests/final.trx',evidence/'tests.trx')
    paths=[WORK/'unbound',Path.home()/'.cache/compat/parity-baseline-native8/unbound',WORK/'libunbound_physx.so',*(WORK/'managed-final').rglob('*'),
           *(WORK/'managed-baseline').rglob('*'),*(WORK/'test-build').rglob('libunbound_physx.so')]
    test_output=WORK/'test-build/bin/UnityEngine.Tests/Release/net10.0'
    paths += [test_output/'UnityEngine.Tests.dll',test_output/'UnityEngine.CoreModule.dll',WORK/'tests/final.trx']
    artifacts={str(p):sha(p) for p in paths if p.is_file()}
    assert sha(WORK/'unbound') == sha(Path.home()/'.cache/compat/parity-baseline-native8/unbound')
    native='4a543241a78444ce2595cf8ce42f370c692e993388d42479913558b875d7a208'
    assert all(h==native for p,h in artifacts.items() if p.endswith('libunbound_physx.so'))
    assert len([p for p in artifacts if p.endswith('libunbound_physx.so')])>=4
    manifest=dict(runtime_commit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),
                  parent='7c0bde630f630c55181a31ac7563954323e774da',main_abi=5,physx_shim=8,shader_cache=13,
                  artifacts_sha256=artifacts)
    manifest['source_sha256']={str(p.relative_to(REPO)):sha(p) for p in [
        REPO/'managed/UnityEngine/Physics/Rigidbody.cs',REPO/'managed/UnityEngine/Physics/PhysicsWorld.cs',
        REPO/'managed/UnityEngine/Runtime/Loading.cs',REPO/'managed/UnityEngine.Tests/PhysicsTests.cs']}
    creation=json.loads((ROOT/'Evidence/creation-manifest.json').read_text())
    assert all(sha(ROOT/p)==h for p,h in creation['authored_sha256'].items())
    frozen=json.loads((ROOT/'Evidence/native-manifest.json').read_text())
    assert all(sha(Path(p))==h for p,h in frozen['files_sha256'].items())
    assert all(sha(WORK/p)==h for p,h in frozen['managed_baseline_sha256'].items())
    (evidence/'artifact-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    files={str(p.relative_to(ROOT)):sha(p) for p in ROOT.rglob('*')
           if p.is_file() and '__pycache__' not in str(p) and p.name != 'evidence-manifest.json'}
    (ROOT/'Evidence/evidence-manifest.json').write_text(json.dumps(files,indent=2)+'\n')
    print('All authored, Native, baseline and copied native hashes verified')


if __name__=='__main__': main()
