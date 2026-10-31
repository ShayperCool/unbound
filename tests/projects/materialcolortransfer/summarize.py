#!/usr/bin/env python3
"""Retain every raw buffer, observation and strict comparison from this run."""
import gzip,hashlib,json,os,shutil,subprocess,sys
from pathlib import Path
f=Path(__file__).resolve().parent;work=Path(os.environ['MATERIAL_COLOR_TRANSFER_ROOT']);out=f/'Evidence';out.mkdir(exist_ok=True)
manifest={}
for space in ['linear','gamma']:
 a=work/f'oracle-v2-{space}1';b=work/f'oracle-v2-{space}2'
 def hashes(root):return {p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(root.iterdir()) if p.is_file()}
 assert hashes(a)==hashes(b),'Native repeats differ'
 for phase,source in [('Native',a),('Before',work/f'before-v2-{space}'),('Final',work/f'final-{space}')]:
  dest=f/'Reference'/(phase+space.title());dest.mkdir(parents=True,exist_ok=True)
  shutil.copyfile(source/'values.jsonl',dest/'values.jsonl')
  for p in source.glob('*.rgba'):(dest/(p.name+'.gz')).write_bytes(gzip.compress(p.read_bytes(),mtime=0))
  manifest[dest.name]={'raw':str(source),'sha256':hashes(source),'observations':len((source/'values.jsonl').read_text().splitlines()),'images':len(list(source.glob('*.rgba')))}
  if phase!='Native':
   result=subprocess.run([sys.executable,str(f/'compare.py'),str(a),str(source)],text=True,capture_output=True)
   (out/(phase.lower()+'-'+space+'.txt')).write_text(result.stdout+result.stderr)
   print(phase,space,'exit',result.returncode,result.stdout.splitlines()[-2:])
manifest['repeatsByteIdentical']=True
(out/'recordings.json').write_text(json.dumps(manifest,indent=2)+'\n')
