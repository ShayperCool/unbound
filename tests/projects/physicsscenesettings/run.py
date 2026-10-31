#!/usr/bin/env python3
"""Prepare isolated projects and record one heavy process at a time. Run from repo root."""
import argparse, hashlib, json, os, pathlib, shutil, subprocess
ROOT = pathlib.Path(__file__).resolve().parent
PROFILES = {
    'default': {},
    'tgs': {'m_SolverType': 1},
    'one': {'m_FrictionType': 1},
    'two': {'m_FrictionType': 2},
    'tgs-one': {'m_SolverType': 1, 'm_FrictionType': 1},
    'tgs-two': {'m_SolverType': 1, 'm_FrictionType': 2},
    'pairs-kk': {'m_ContactPairsMode': 1},
    'pairs-ks': {'m_ContactPairsMode': 2},
    'pairs-all': {'m_ContactPairsMode': 3},
    'determinism': {'m_EnableEnhancedDeterminism': 1},
    'tgs-det': {'m_SolverType': 1, 'm_EnableEnhancedDeterminism': 1},
    'improved': {'m_ImprovedPatchFriction': 1},
    'pcm-off': {'m_EnablePCM': 0},
}
def main():
    p = argparse.ArgumentParser(); p.add_argument('mode',choices=['prepare','native','baseline','final'])
    p.add_argument('--profiles',nargs='*',default=list(PROFILES)); args=p.parse_args()
    work=pathlib.Path(os.environ['SCENE_WORK']); heavy=pathlib.Path.home()/'.cache/compat/with-heavy-job'
    for profile in args.profiles:
        project=work/'projects'/profile
        if args.mode=='prepare':
            for d in ['Assets','ProjectSettings','Packages']:
                shutil.copytree(ROOT/d,project/d,dirs_exist_ok=True)
            # Keep the original oracle's exact C# input while the supplemental probe evolves.
            for source in (ROOT/'Sources/original').rglob('*.cs'):
                shutil.copy2(source,project/'Assets'/source.relative_to(ROOT/'Sources/original'))
            settings=project/'ProjectSettings/DynamicsManager.asset'; text=settings.read_text()
            import re
            for key,value in PROFILES[profile].items():
                if re.search(r'^  '+key+':',text,re.M): text=re.sub(r'^  '+key+r': .*$',f'  {key}: {value}',text,flags=re.M)
                else: text+=f'  {key}: {value}\n'
            settings.write_text(text)
            (ROOT/'Profiles'/f'{profile}.asset').write_text(text)
            continue
        for repeat in ([1,2] if args.mode=='native' else [1]):
            folder=work/(f'native{repeat}' if args.mode=='native' else args.mode); folder.mkdir(exist_ok=True)
            output=folder/(profile+'.jsonl'); env=dict(os.environ,SCENE_SETTINGS_OUT=str(output))
            if args.mode=='native':
                command=[str(pathlib.Path.home()/'Unity/Hub/Editor/6000.0.71f1/Editor/Unity'),'-batchmode','-nographics','-projectPath',str(project),'-executeMethod','ProbeRunner.Record','-logFile',str(folder/(profile+'.log'))]
            else:
                cli=pathlib.Path.home()/'.cache/compat/parity-baseline-native6/unbound' if args.mode=='baseline' else pathlib.Path(os.environ.get('SCENE_SETTINGS_CLI',str(work/'cargo/release/unbound')))
                command=[str(cli),'run','-p',str(project),'--headless','30000','--no-watch']
            with open(folder/(profile+'.stdout'),'w') as log:
                code=subprocess.run([str(heavy),'timeout','240',*command],env=env,stdout=log,stderr=subprocess.STDOUT).returncode
            print(args.mode,profile,repeat,'exit',code,flush=True)
            if code or not output.exists(): raise SystemExit(f'missing or failed {output}')
if __name__=='__main__': main()
