#!/usr/bin/env python3
"""Compare full material captures; store run reports in the validation cache."""
import argparse
import gzip
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_root, run_root

fixture=Path(__file__).resolve().parent
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('label', nargs='?', default='current')
for space in ('linear','gamma'):
 for kind in ('','native-','repeat-','before-'):
  parser.add_argument('--'+kind+space, type=Path)
parser.add_argument('--out', type=Path)
parser.add_argument('--record-reference', action='store_true')
args=parser.parse_args()
work=Path(os.environ.get('MATERIAL_COLOR_TRANSFER_ROOT',run_root(fixture,args.label)))
out=args.out or run_root(fixture,args.label)/'reports'
out.mkdir(parents=True,exist_ok=True)

def hashes(root):
 result={}
 for p in sorted(root.iterdir()):
  name=p.name.removesuffix('.gz')
  if not name.endswith(('.jsonl','.rgba')):continue
  data=gzip.decompress(p.read_bytes()) if p.suffix=='.gz' else p.read_bytes()
  assert name not in result, 'duplicate buffer '+name
  result[name]=hashlib.sha256(data).hexdigest()
 assert 'values.jsonl' in result, 'empty capture '+str(root)
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
 actual=getattr(args,space.lower()) or work/space
 if repeat:
  assert hashes(native)==hashes(repeat), space+' Native repeats differ'
 if args.record_reference:
  assert repeat, '--record-reference needs two Native recordings per color space'
  dest=reference_root(fixture)/space
  assert not dest.exists(), 'refusing to overwrite '+str(dest)
  dest.mkdir(parents=True)
  shutil.copyfile(native/'values.jsonl',dest/'values.jsonl')
  for p in native.iterdir():
   name=p.name.removesuffix('.gz')
   if not name.endswith('.rgba'):continue
   data=gzip.decompress(p.read_bytes()) if p.suffix=='.gz' else p.read_bytes()
   (dest/(name+'.gz')).write_bytes(gzip.compress(data,mtime=0))
  manifest[space]={'native':str(native),'sha256':hashes(native)}
  continue
 for phase,source in [('before',getattr(args,'before_'+space.lower())),('actual',actual)]:
  if source is None:continue
  result=subprocess.run([sys.executable,str(fixture/'compare.py'),str(native),str(source)],text=True,capture_output=True)
  (out/(phase+'-'+space.lower()+'.txt')).write_text(result.stdout+result.stderr)
  failures.append(result.returncode)
  manifest[phase+space]={'source':str(source),'sha256':hashes(source)}
  print(phase,space,'exit',result.returncode,result.stdout.splitlines()[-2:])
(out/'recordings.json').write_text(json.dumps(manifest,indent=2)+'\n')
raise SystemExit(int(any(failures)))
