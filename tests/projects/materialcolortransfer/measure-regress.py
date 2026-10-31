#!/usr/bin/env python3
"""Compare untouched strict regression checkers and retain their full output."""
from pathlib import Path
import argparse,os,subprocess,sys,json,hashlib
sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_root,reference_file,run_root,unity_version
f=Path(__file__).resolve().parent;p=f.parent
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--before',type=Path)
parser.add_argument('--after',type=Path)
parser.add_argument('--out',type=Path)
args=parser.parse_args()
legacy=os.environ.get('MATERIAL_COLOR_TRANSFER_ROOT')
final_name='regress-final'+os.environ.get('MATERIAL_COLOR_TRANSFER_FINAL_SUFFIX','')
before=args.before or (Path(legacy)/'regress-before' if legacy else run_root(f,'before')/'regress')
after=args.after or (Path(legacy)/final_name if legacy else run_root(f,'current')/'regress')
e=args.out or Path(os.environ.get('MATERIAL_COLOR_TRANSFER_EVIDENCE_DIR',run_root(f,'regression-reports')));e.mkdir(parents=True,exist_ok=True)
def external_reference(project,profile=''):
 versioned=reference_root(project)
 if versioned.exists():return versioned/profile
 assert unity_version()=='6000.0.71f1', f'no {unity_version()} reference for {project}'
 return project/'Reference'/profile

checks=[]
for name,project,profile in [('graphics','graphics',None),('resources','resources',''),('builtinrp','builtinrp',''),('builtinfx','builtinfx',''),('rectclip-linear','uirectclipping','Linear'),('rectclip-gamma','uirectclipping','Gamma'),('rectclip-msaa','uirectclipping','LinearMSAA'),('colors-linear','uicolors','Linear'),('colors-gamma','uicolors','Gamma')]:
 ref=reference_file(p/project) if profile is None else external_reference(p/project,profile)
 checks.append((name,p/project/'compare.py',ref,''))
for s in ['Linear','Gamma']:checks.append(('camera-focused-'+s.lower(),p/'cameramsaa/compare.py',reference_root(p/'cameramsaa')/('Focused'+s),''))
checks.append(('uitk',p/'uitk/compare.py',reference_file(p/'uitk','jsonl'),'unbound.jsonl'))
summary=[];errors=[]
def folder(phase,name):
 if phase=='before' and name=='builtinrp':return os.environ.get('MATERIAL_COLOR_TRANSFER_BEFORE_BUILTINRP_FOLDER',name)
 if phase=='final' and name=='camera-focused-gamma':return os.environ.get('MATERIAL_COLOR_TRANSFER_GAMMA_CAMERA_FOLDER',name)
 return name
for phase in ['before','final']:
 for name,checker,ref,tail in checks:
  result=subprocess.run([sys.executable,str(checker),str(ref),str((before if phase=='before' else after)/folder(phase,name)/tail)],capture_output=True,text=True)
  (e/(phase+'-'+name+'.txt')).write_text(result.stdout+result.stderr)
  summary.append(f'{phase} {name} exit {result.returncode}\n'+ '\n'.join(result.stdout.splitlines()[-3:]))
  if result.returncode:errors.append(phase+' '+name)
  if name=='builtinrp' and ('0 differ, 124 expected; 33/33 images' not in result.stdout):errors.append(phase+' builtinrp counts')
for name,_,_,_ in checks:
 def hashes(root):return {root.name:hashlib.sha256(root.read_bytes()).hexdigest()} if root.is_file() else {str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in root.rglob('*') if p.is_file()}
 a=hashes(before/folder('before',name));b=hashes(after/folder('final',name));changed=sorted(k for k in a.keys()|b.keys() if a.get(k)!=b.get(k))
 summary.append(f'{name}: {len(a)} files; changed: {changed}')
 if name=='uitk' and changed:errors.append('uitk captures changed')
result=subprocess.run([sys.executable,str(p/'uitk/compare.py'),str(before/'uitk/unbound.jsonl'),str(after/'uitk/unbound.jsonl')],capture_output=True,text=True)
(e/'uitk-before-final.txt').write_text(result.stdout+result.stderr)
summary.append(f'uitk independent before/final comparison exit {result.returncode}\n'+result.stdout)
if result.returncode:errors.append('uitk before/final')
(e/'regression-summary.txt').write_text('\n\n'.join(summary)+'\n');print('\n\n'.join(summary))

if errors:raise SystemExit('Retained strict comparison failures: '+', '.join(errors))
