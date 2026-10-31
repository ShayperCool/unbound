#!/usr/bin/env python3
"""Keep every regression comparator result, including inherited Native failures."""
from pathlib import Path
import subprocess
import sys
import json
base=Path.home()/'.cache/compat'
fixture=Path(__file__).resolve().parent
projects=fixture.parent
before=base/'cameramsaa-regress-before';after=base/('cameramsaa-'+(sys.argv[1] if len(sys.argv)>1 else 'regress-final'))
def check(name,script,ref,actual):
    p=subprocess.run([sys.executable,str(script),str(ref),str(actual)],capture_output=True,text=True)
    (base/('cameramsaa-'+name+'.txt')).write_text(p.stdout+p.stderr)
    print(name,'exit',p.returncode, '\n'.join(p.stdout.splitlines()[-3:]),flush=True)
for phase,out in [('before',before),('after',after)]:
    check(phase+'-graphics',projects/'graphics/compare.py',projects/'graphics/Reference/unity-6000.0.71f1.json',out/'graphics')
    for key,project in [('resources','resources'),('builtinrp','builtinrp'),('builtinfx','builtinfx'),('colors-linear','uicolors'),('colors-gamma','uicolors'),('lifecycle','canvaslifecycle')]:
        ref=projects/project/'Reference'
        if key.startswith('colors-'):ref/=key.split('-')[1].title()
        check(phase+'-'+key,projects/project/'compare.py',ref,out/key)
    for key,ref in [('rectclip-linear','Linear'),('rectclip-gamma','Gamma'),('rectclip-msaa','LinearMSAA')]:
        check(phase+'-'+key,projects/'uirectclipping/compare.py',projects/'uirectclipping/Reference'/ref,out/key)
    check(phase+'-transitions',projects/'uitransitions/compare.py',projects/'uitransitions/Reference/transitions.jsonl',out/'transitions/transitions.jsonl')
    check(phase+'-uitk-native',projects/'uitk/compare.py',projects/'uitk/Reference/unity-6000.0.71f1.jsonl',out/'uitk/unbound.jsonl')
check('uitk-before-after',projects/'uitk/compare.py',before/'uitk/unbound.jsonl',after/'uitk/unbound.jsonl')
for key in ['graphics','resources','builtinrp','builtinfx','uitk']:
    x=before/key;y=after/key
    if x.is_file():print(key,'byte-identical',x.read_bytes()==y.read_bytes());continue
    a={str(p.relative_to(x)):p.read_bytes() for p in x.rglob('*') if p.is_file()}
    b={str(p.relative_to(y)):p.read_bytes() for p in y.rglob('*') if p.is_file()}
    changed=[k for k in sorted(set(a)|set(b)) if a.get(k)!=b.get(k)]
    print(key,len(a),'files, changed',changed)
