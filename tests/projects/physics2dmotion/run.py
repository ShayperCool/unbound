#!/usr/bin/env python3
"""Fresh independent Native projects and frozen-runtime private engine outputs."""
import argparse, hashlib, json, os, shutil, subprocess
from pathlib import Path
ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
WORK = Path(os.environ['MOTION_WORK'])
BASE = Path.home()/'.cache/compat/parity-baseline-lifecycle2d-native8'
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
p = argparse.ArgumentParser(); p.add_argument('mode', choices=['prepare','native','engine']); p.add_argument('--label', required=True)
a = p.parse_args()
project = WORK/('project-'+a.label)
if a.mode == 'prepare':
    if project.exists(): raise SystemExit('Refusing to overwrite '+str(project))
    for folder in ['Assets','Packages','ProjectSettings']:
        shutil.copytree(REPO/'tests/projects/physics'/folder, project/folder)
    shutil.copy2(ROOT/'Sources'/os.environ.get('MOTION_SOURCE', 'ProbeEntry.cs'), project/'Assets/Scripts/ProbeEntry.cs')
    manifest = {str(f.relative_to(project)): hashlib.sha256(f.read_bytes()).hexdigest() for d in ['Assets','Packages','ProjectSettings'] for f in sorted((project/d).rglob('*')) if f.is_file()}
    (WORK/(a.label+'-input-manifest.json')).write_text(json.dumps(manifest,indent=2)+'\n')
else:
    out = WORK/(a.label+'.json'); log = WORK/(a.label+'.log')
    if out.exists() or log.exists(): raise SystemExit('Refusing to overwrite '+a.label)
    env = dict(os.environ, PHYSICS_PROBE_OUT=str(out))
    if a.mode == 'native':
        cmd = [str(Path.home()/'Unity/Hub/Editor/6000.0.71f1/Editor/Unity'), '-batchmode','-nographics','-projectPath',str(project),'-executeMethod','ProbeRunner.Record','-logFile',str(WORK/(a.label+'-unity.log'))]
    else:
        project = WORK/os.environ.get('MOTION_PROJECT', 'project-native1')
        env['UNBOUND_MANAGED_DIR'] = str(WORK/os.environ.get('MOTION_MANAGED', 'managed-baseline' if a.label == 'baseline' else 'managed-final'))
        env['LD_LIBRARY_PATH'] = env['UNBOUND_MANAGED_DIR']
        cmd = [str(BASE/'unbound'),'run','-p',str(project),'--headless','30000','--no-watch']
    command = [str(HEAVY),'timeout','600',*cmd]
    audit = dict(command=command, environment={k:env.get(k) for k in ['UNBOUND_MANAGED_DIR','LD_LIBRARY_PATH','PHYSICS_PROBE_OUT','CARGO_TARGET_DIR','UnboundTarget','UnboundNativeDir']})
    if a.mode == 'engine':
        runtime=Path(env['UNBOUND_MANAGED_DIR'])
        audit['artifacts_sha256']={str(f):hashlib.sha256(f.read_bytes()).hexdigest() for f in [BASE/'unbound',runtime/'UnityEngine.CoreModule.dll',runtime/'libunbound_physx.so']}
    (WORK/(a.label+'-command.json')).write_text(json.dumps(audit,indent=2)+'\n')
    with log.open('w') as f: result = subprocess.run(command,env=env,stdout=f,stderr=subprocess.STDOUT)
    print(a.label,'exit',result.returncode,flush=True)
    if result.returncode or not out.exists(): raise SystemExit('Run failed: '+str(log))
