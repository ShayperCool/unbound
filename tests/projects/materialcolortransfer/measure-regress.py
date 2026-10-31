#!/usr/bin/env python3
"""Compare untouched strict regression checkers and retain their full output."""
from pathlib import Path
import os,subprocess,sys,json,hashlib
f=Path(__file__).resolve().parent;p=f.parent;w=Path(os.environ['MATERIAL_COLOR_TRANSFER_ROOT']);e=f/'Evidence';e.mkdir(exist_ok=True)
checks=[]
for name,project,ref in [('graphics','graphics','Reference/unity-6000.0.71f1.json'),('resources','resources','Reference'),('builtinrp','builtinrp','Reference'),('builtinfx','builtinfx','Reference'),('rectclip-linear','uirectclipping','Reference/Linear'),('rectclip-gamma','uirectclipping','Reference/Gamma'),('rectclip-msaa','uirectclipping','Reference/LinearMSAA'),('colors-linear','uicolors','Reference/Linear'),('colors-gamma','uicolors','Reference/Gamma')]:checks.append((name,p/project/'compare.py',p/project/ref,''))
for s in ['Linear','Gamma']:checks.append(('camera-focused-'+s.lower(),p/'cameramsaa/compare.py',p/'cameramsaa/Reference'/('FollowupFocusedNative'+s),''))
checks.append(('uitk',p/'uitk/compare.py',p/'uitk/Reference/unity-6000.0.71f1.jsonl','unbound.jsonl'))
summary=[];errors=[]
for phase in ['before','final']:
 for name,checker,ref,tail in checks:
  result=subprocess.run([sys.executable,str(checker),str(ref),str(w/('regress-'+phase)/('builtinrp-correct' if phase=='before' and name=='builtinrp' else name)/tail)],capture_output=True,text=True)
  (e/(phase+'-'+name+'.txt')).write_text(result.stdout+result.stderr)
  summary.append(f'{phase} {name} exit {result.returncode}\n'+ '\n'.join(result.stdout.splitlines()[-3:]))
  if name!='uitk' and result.returncode:errors.append(phase+' '+name)
  if name=='builtinrp' and ('0 differ, 124 expected; 33/33 images' not in result.stdout):errors.append(phase+' builtinrp counts')
for name,_,_,_ in checks:
 def hashes(root):return {str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in root.rglob('*') if p.is_file()}
 a=hashes(w/'regress-before'/('builtinrp-correct' if name=='builtinrp' else name));b=hashes(w/'regress-final'/name);changed=sorted(k for k in a.keys()|b.keys() if a.get(k)!=b.get(k))
 summary.append(f'{name}: {len(a)} files; changed: {changed}')
result=subprocess.run([sys.executable,str(p/'uitk/compare.py'),str(w/'regress-before/uitk/unbound.jsonl'),str(w/'regress-final/uitk/unbound.jsonl')],capture_output=True,text=True)
(e/'uitk-before-final.txt').write_text(result.stdout+result.stderr)
summary.append(f'uitk independent before/final comparison exit {result.returncode}\n'+result.stdout)
if result.returncode:errors.append('uitk before/final')
(e/'regression-summary.txt').write_text('\n\n'.join(summary)+'\n');print('\n\n'.join(summary))

if errors:raise SystemExit('Unexpected regression failures: '+', '.join(errors))
