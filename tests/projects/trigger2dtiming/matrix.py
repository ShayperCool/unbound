#!/usr/bin/env python3
"""Read-only reuse of the accepted 3D controls, prepared in private projects."""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parent
SCENE = ROOT.parent/'physicsscenesettings'
WORK = Path(os.environ['TRIGGER_WORK'])/'matrix'
SPEC = importlib.util.spec_from_file_location('scene_compare', SCENE/'compare.py')
COMPARE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(COMPARE)
PROFILES = [p.stem for p in sorted((SCENE/'Reference').glob('*.jsonl'))]


def main():
    profiles = []
    for prefix, names in [('', PROFILES), ('contacts-', ['default','pairs-kk','pairs-ks','pairs-all']),
                           ('islands-', ['default','determinism','tgs','tgs-det'])]:
        for name in names:
            project = WORK/'projects'/(prefix+name)
            for folder in ['Assets','ProjectSettings','Packages']:
                shutil.copytree(SCENE/folder, project/folder, dirs_exist_ok=True)
            source = 'original' if not prefix else 'supplemental'
            for file in (SCENE/'Sources'/source).rglob('*.cs'):
                shutil.copyfile(file, project/'Assets'/file.relative_to(SCENE/'Sources'/source))
            shutil.copyfile(SCENE/'Profiles'/(name+'.asset'), project/'ProjectSettings/DynamicsManager.asset')
            output = WORK/(prefix+name+'.jsonl')
            env = dict(os.environ, SCENE_SETTINGS_OUT=str(output),
                       SCENE_SETTINGS_QUERY_CONTACTS='1' if prefix=='contacts-' else '0',
                       SCENE_SETTINGS_NOISE='1' if prefix=='islands-' else '0')
            with (WORK/(prefix+name+'.log')).open('w') as log:
                r = subprocess.run([str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '240',
                    str(Path.home()/'.cache/compat/parity-baseline-native8/unbound'), 'run', '-p',
                    str(project), '--headless','30000','--no-watch'], env=env, stdout=log, stderr=subprocess.STDOUT)
            assert r.returncode == 0, (prefix+name, r.returncode)
            ref = COMPARE.read(str(SCENE/'Reference'/prefix.rstrip('-')/(name+'.jsonl')))
            value = COMPARE.read(str(output))
            assert value.keys() == ref.keys()
            failed = [k for k in ref if not COMPARE.matches(k,ref[k],value[k])]
            profiles.append(dict(profile=prefix+name, records=len(ref),exact=sum(ref[k]==value[k] for k in ref),
                                 matched=len(ref)-len(failed),failed_keys=failed,
                                 byte_identical_to_accepted=output.read_bytes()==(SCENE/'SDKLifecycleEvidence/accepted'/(prefix+'final')/(name+'.jsonl')).read_bytes()))
            print(profiles[-1],flush=True)
            assert not failed
    summary=dict(profiles=profiles,records=sum(p['records'] for p in profiles),
                 exact=sum(p['exact'] for p in profiles),matched=sum(p['matched'] for p in profiles))
    assert summary['records']==5055
    (WORK/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
    dest = ROOT/'Evidence/matrix'; dest.mkdir(exist_ok=True)
    for file in WORK.glob('*'):
        if file.is_file(): shutil.copyfile(file,dest/file.name)


if __name__=='__main__':
    main()
