#!/usr/bin/env python3
"""Keep every regression comparator result, including inherited Native failures."""
from pathlib import Path
import subprocess
import sys
import json
import os
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_root, reference_file, run_root
from recordings import external_reference
fixture=Path(__file__).resolve().parent
projects=fixture.parent
base=run_root(fixture,'regression-reports');base.mkdir(parents=True,exist_ok=True)
prefix=os.environ.get('CAMERA_MSAA_REPORT_PREFIX','cameramsaa')
before=Path(os.environ.get('CAMERA_MSAA_BEFORE',run_root(fixture,'before')/'regress'))
after=Path(os.environ.get('CAMERA_MSAA_AFTER',run_root(fixture,sys.argv[1] if len(sys.argv)>1 else 'current')/'regress'))
statuses=[]
def check(name,script,ref,actual):
    p=subprocess.run([sys.executable,str(script),str(ref),str(actual)],capture_output=True,text=True)
    statuses.append({'name':name,'exit':p.returncode,'reference':str(ref),'actual':str(actual)})
    (base/(prefix+'-'+name+'.txt')).write_text(p.stdout+p.stderr)
    print(name,'exit',p.returncode, '\n'.join(p.stdout.splitlines()[-3:]),flush=True)
for phase,out in [('before',before),('after',after)]:
    check(phase+'-graphics',projects/'graphics/compare.py',reference_file(projects/'graphics'),out/'graphics')
    for key,project in [('resources','resources'),('builtinrp','builtinrp'),('builtinfx','builtinfx'),('colors-linear','uicolors'),('colors-gamma','uicolors'),('lifecycle','canvaslifecycle')]:
        ref=external_reference(projects/project)
        if key.startswith('colors-'):ref/=key.split('-')[1].title()
        check(phase+'-'+key,projects/project/'compare.py',ref,out/key)
    for key,ref in [('rectclip-linear','Linear'),('rectclip-gamma','Gamma'),('rectclip-msaa','LinearMSAA')]:
        check(phase+'-'+key,projects/'uirectclipping/compare.py',external_reference(projects/'uirectclipping',ref),out/key)
    check(phase+'-transitions',projects/'uitransitions/compare.py',external_reference(projects/'uitransitions','transitions.jsonl'),out/'transitions/transitions.jsonl')
    check(phase+'-uitk-native',projects/'uitk/compare.py',reference_file(projects/'uitk','jsonl'),out/'uitk/unbound.jsonl')
check('uitk-before-after',projects/'uitk/compare.py',before/'uitk/unbound.jsonl',after/'uitk/unbound.jsonl')
for key in ['graphics','resources','builtinrp','builtinfx','uitk']:
    x=before/key;y=after/key
    if x.is_file():print(key,'byte-identical',x.read_bytes()==y.read_bytes());continue
    a={str(p.relative_to(x)):p.read_bytes() for p in x.rglob('*') if p.is_file()}
    b={str(p.relative_to(y)):p.read_bytes() for p in y.rglob('*') if p.is_file()}
    changed=[k for k in sorted(set(a)|set(b)) if a.get(k)!=b.get(k)]
    print(key,len(a),'files, changed',changed)

(base/(prefix+'-summary.json')).write_text(json.dumps(statuses,indent=2)+'\n')
raise SystemExit(int(any(item['exit'] for item in statuses)))
