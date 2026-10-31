#!/usr/bin/env python3
"""Pack the independent effect-chain and color-preservation oracle."""
from pathlib import Path
import gzip
import hashlib
import json
import shutil
import subprocess
import sys

fixture=Path(__file__).resolve().parent
cache=Path.home()/'.cache/compat'
out=fixture/'Evidence/Followup'
info={}
for space in ('linear','gamma'):
    native=cache/('camerafollowup-focused-native-'+space+'1')
    repeat=cache/('camerafollowup-focused-native-'+space+'2')
    def manifest(root):
        return {p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(root.iterdir()) if p.suffix in ('.rgba','.png','.jsonl')}
    assert manifest(native)==manifest(repeat)
    for phase,source in [('Native',native),('Before',cache/('camerafollowup-focused-before2-'+space)),('Final',cache/('camerafollowup-focused-final3-'+space))]:
        name='FollowupFocused'+phase+space.title()
        destination=fixture/'Reference'/name;destination.mkdir(exist_ok=True)
        shutil.copyfile(source/'values.jsonl',destination/'values.jsonl')
        for p in sorted(source.glob('*.rgba')):(destination/(p.name+'.gz')).write_bytes(gzip.compress(p.read_bytes(),mtime=0))
        info[name]={'raw':str(source),'observations':len((source/'values.jsonl').read_text().splitlines()),'sha256':manifest(source)}
        if phase!='Native':
            result=subprocess.run([sys.executable,str(fixture/'compare.py'),str(native),str(source)],capture_output=True,text=True)
            (out/('focused-'+phase.lower()+'-'+space+'.txt')).write_text(result.stdout+result.stderr)
            print(phase,space,'exit',result.returncode,result.stdout.splitlines()[-2:])
    info[space+'-repeat-byte-identical']=True
(out/'focused-recordings.json').write_text(json.dumps(info,indent=2)+'\n')
