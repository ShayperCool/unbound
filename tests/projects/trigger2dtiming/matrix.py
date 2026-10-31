#!/usr/bin/env python3
"""Read-only reuse of the accepted 3D controls, prepared in private projects."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
SCENE = ROOT.parent/'physicsscenesettings'
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import reference_root, run_root
WORK = Path(os.environ.get('TRIGGER_WORK', run_root(ROOT)))/'matrix'
SPEC = importlib.util.spec_from_file_location('scene_compare', SCENE/'compare.py')
COMPARE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(COMPARE)
PROFILES = [p.stem for p in sorted(reference_root(SCENE).glob('*.jsonl'))]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--work', type=Path, default=WORK)
    parser.add_argument('--accepted', type=Path, help='Optional previous matrix capture directory for exact regression checks')
    args = parser.parse_args()
    work = args.work.resolve()
    work.mkdir(parents=True, exist_ok=True)
    profiles = []
    for prefix, names in [('', PROFILES), ('contacts-', ['default','pairs-kk','pairs-ks','pairs-all']),
                           ('islands-', ['default','determinism','tgs','tgs-det'])]:
        for name in names:
            project = work/'projects'/(prefix+name)
            for folder in ['Assets','ProjectSettings','Packages']:
                shutil.copytree(SCENE/folder, project/folder, dirs_exist_ok=True)
            source = 'original' if not prefix else 'supplemental'
            for file in (SCENE/'Sources'/source).rglob('*.cs'):
                shutil.copyfile(file, project/'Assets'/file.relative_to(SCENE/'Sources'/source))
            shutil.copyfile(SCENE/'Profiles'/(name+'.asset'), project/'ProjectSettings/DynamicsManager.asset')
            output = work/(prefix+name+'.jsonl')
            env = dict(os.environ, SCENE_SETTINGS_OUT=str(output),
                       SCENE_SETTINGS_QUERY_CONTACTS='1' if prefix=='contacts-' else '0',
                       SCENE_SETTINGS_NOISE='1' if prefix=='islands-' else '0')
            with (work/(prefix+name+'.log')).open('w') as log:
                r = subprocess.run([str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '240',
                    os.environ.get('UNBOUND_CLI', os.environ.get('SCENE_SETTINGS_CLI', shutil.which('unbound') or str(ROOT.parents[2]/'target/release/unbound'))), 'run', '-p',
                    str(project), '--headless','30000','--no-watch'], env=env, stdout=log, stderr=subprocess.STDOUT)
            assert r.returncode == 0, (prefix+name, r.returncode)
            ref = COMPARE.read(str(reference_root(SCENE)/prefix.rstrip('-')/(name+'.jsonl')))
            value = COMPARE.read(str(output))
            assert value.keys() == ref.keys()
            failed = [k for k in ref if not COMPARE.matches(k,ref[k],value[k])]
            profiles.append(dict(profile=prefix+name, records=len(ref),exact=sum(ref[k]==value[k] for k in ref),
                                 matched=len(ref)-len(failed),failed_keys=failed,
                                 byte_identical_to_accepted=(output.read_bytes()==(args.accepted/(prefix+name+'.jsonl')).read_bytes()) if args.accepted else None))
            print(profiles[-1],flush=True)
            assert not failed
            if args.accepted:
                assert profiles[-1]['byte_identical_to_accepted'], prefix+name
    summary=dict(profiles=profiles,records=sum(p['records'] for p in profiles),
                 exact=sum(p['exact'] for p in profiles),matched=sum(p['matched'] for p in profiles))
    assert summary['records']==5055
    (work/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')


if __name__=='__main__':
    main()
