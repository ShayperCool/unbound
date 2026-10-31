#!/usr/bin/env python3
"""Independent Native projects and explicitly selected engine outputs."""
import argparse, hashlib, json, os, shutil, subprocess, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import reference_file, reference_root, run_root, unity_version, editor_path
WORK = Path(os.environ.get('DYNAMICS_WORK', run_root(ROOT))).resolve()
WORK.mkdir(parents=True, exist_ok=True)
CLI = os.environ.get('UNBOUND_CLI', os.environ.get('SCENE_SETTINGS_CLI', shutil.which('unbound') or str(REPO/'target/release/unbound')))
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
p = argparse.ArgumentParser(); p.add_argument('mode', choices=['prepare','native','engine']); p.add_argument('--label', required=True)
a = p.parse_args()
project = WORK/('project-'+a.label)
if a.mode == 'prepare':
    if project.exists(): raise SystemExit('Refusing to overwrite '+str(project))
    for folder in ['Assets','Packages','ProjectSettings']:
        shutil.copytree(REPO/'tests/projects/physics'/folder, project/folder)
    shutil.copy2(ROOT/'Sources'/os.environ.get('DYNAMICS_SOURCE', 'ProbeEntry.cs'), project/'Assets/Scripts/ProbeEntry.cs')
    manifest = {str(f.relative_to(project)): hashlib.sha256(f.read_bytes()).hexdigest() for d in ['Assets','Packages','ProjectSettings'] for f in sorted((project/d).rglob('*')) if f.is_file()}
    (WORK/(a.label+'-input-manifest.json')).write_text(json.dumps(manifest,indent=2)+'\n')
else:
    out = WORK/(a.label+'.json'); log = WORK/(a.label+'.log')
    if out.exists() or log.exists(): raise SystemExit('Refusing to overwrite '+a.label)
    env = dict(os.environ, PHYSICS_PROBE_OUT=str(out))
    if a.mode == 'native':
        cmd = [os.environ.get('UNITY_EDITOR', str(editor_path())), '-batchmode','-nographics','-projectPath',str(project),'-executeMethod','ProbeRunner.Record','-logFile',str(WORK/(a.label+'-unity.log'))]
    else:
        project = WORK/os.environ.get('DYNAMICS_PROJECT', 'project-native1')
        env['UNBOUND_MANAGED_DIR'] = os.environ.get('UNBOUND_MANAGED_DIR', str(WORK/os.environ.get('DYNAMICS_MANAGED', 'managed-baseline' if a.label == 'baseline' else 'managed-final')))
        env['LD_LIBRARY_PATH'] = env['UNBOUND_MANAGED_DIR']
        cmd = [CLI,'run','-p',str(project),'--headless','30000','--no-watch']
    command = [str(HEAVY),'timeout','600',*cmd]
    audit = dict(command=command, environment={k:env.get(k) for k in ['UNBOUND_MANAGED_DIR','LD_LIBRARY_PATH','PHYSICS_PROBE_OUT','CARGO_TARGET_DIR','UnboundTarget','UnboundNativeDir']})
    if a.mode == 'engine':
        runtime=Path(env['UNBOUND_MANAGED_DIR'])
        audit['artifacts_sha256']={str(f):hashlib.sha256(f.read_bytes()).hexdigest() for f in [Path(CLI),runtime/'UnityEngine.CoreModule.dll',runtime/'libunbound_physx.so']}
    (WORK/(a.label+'-command.json')).write_text(json.dumps(audit,indent=2)+'\n')
    with log.open('w') as f: result = subprocess.run(command,env=env,stdout=f,stderr=subprocess.STDOUT)
    print(a.label,'exit',result.returncode,flush=True)
    if result.returncode or not out.exists(): raise SystemExit('Run failed: '+str(log))
