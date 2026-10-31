#!/usr/bin/env python3
"""Verify strict terrain comparisons and exact Native repetition."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_root,run_root

fixture=Path(__file__).resolve().parent
parser=argparse.ArgumentParser(description=__doc__)
for space in ('linear','gamma'):
 for kind in ('','native-','repeat-'):
  parser.add_argument('--'+kind+space,type=Path)
parser.add_argument('--out',type=Path)
parser.add_argument('--record-reference',action='store_true')
args=parser.parse_args()
out=args.out or run_root(fixture,'current')/'reports';out.mkdir(parents=True,exist_ok=True)

def hashes(root):
 result={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in root.iterdir() if p.suffix=='.png' or p.name=='records.jsonl'}
 assert 'records.jsonl' in result and len(result)>1, 'empty recording '+str(root)
 return result

if args.record_reference:
 for space in ('Linear','Gamma'):
  native=getattr(args,'native_'+space.lower());repeat=getattr(args,'repeat_'+space.lower())
  assert native and repeat, '--record-reference requires two explicit Native captures per color space'
  assert hashes(native)==hashes(repeat), space+' Native repeats differ'
  assert not (reference_root(fixture)/space).exists(), 'reference already exists: '+space

manifest={};failures=[]
for space in ('Linear','Gamma'):
 native=getattr(args,'native_'+space.lower()) or reference_root(fixture)/space
 repeat=getattr(args,'repeat_'+space.lower())
 if repeat:assert hashes(native)==hashes(repeat), space+' Native repeats differ'
 if args.record_reference:
  assert repeat, '--record-reference requires two Native captures per space'
  dest=reference_root(fixture)/space
  assert not dest.exists(), 'refusing to overwrite '+str(dest)
  dest.mkdir(parents=True)
  for name in hashes(native):shutil.copyfile(native/name,dest/name)
  manifest[space]={'native':str(native),'sha256':hashes(native)}
  continue
 actual=getattr(args,space.lower()) or run_root(fixture,'current')/space
 result=subprocess.run([sys.executable,str(fixture/'compare.py'),str(native),str(actual)],capture_output=True,text=True)
 (out/(space.lower()+'.txt')).write_text(result.stdout+result.stderr)
 failures.append(result.returncode)
 manifest[space]={'native':str(native),'actual':str(actual),'sha256':hashes(actual),'exit':result.returncode}
 print(space,'exit',result.returncode,'\n'.join(line for line in result.stdout.splitlines() if line.startswith(('records:','images:'))),flush=True)
(out/'recordings.json').write_text(json.dumps(manifest,indent=2)+'\n')
raise SystemExit(int(any(failures)))
