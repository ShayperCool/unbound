#!/usr/bin/env python3
"""Pack complete camera recordings and their byte hashes for review."""
from pathlib import Path
import gzip
import hashlib
import json
import shutil
base=Path.home()/'.cache/compat'
fixture=Path(__file__).resolve().parent
runs=[('boundary1','Linear'),('boundary-gamma1','Gamma'),('boundary-before','BeforeLinear'),('boundary-gamma-before','BeforeGamma')]
info={}
for run,name in runs:
    src=base/('cameramsaa-'+run);dst=fixture/'Reference'/name;dst.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(src/'values.jsonl',dst/'values.jsonl')
    manifest={}
    for p in sorted(src.glob('*.rgba')):
        b=p.read_bytes();(dst/(p.name+'.gz')).write_bytes(gzip.compress(b,mtime=0));manifest[p.name]=hashlib.sha256(b).hexdigest()
    (dst/'sha256.json').write_text(json.dumps(manifest,indent=2)+'\n')
    for key in ['ARGB32.hdr','ARGBFloat.hdr','ARGB32.s2.q0','ARGBFloat.s4.q0','screen.hFalse.q4']:
        shutil.copyfile(src/(key+'.png'),dst/(key+'.png'))
    info[name]={'raw':str(src),'images':len(manifest),'records':len((src/'values.jsonl').read_text().splitlines()),'record_sha256':hashlib.sha256((src/'values.jsonl').read_bytes()).hexdigest()}
for run in ['boundary2','boundary-gamma2','final-policy','final-policy-gamma']:
    p=base/('cameramsaa-'+run);info[run]={'raw':str(p),'record_sha256':hashlib.sha256((p/'values.jsonl').read_bytes()).hexdigest()}
info['baseline_commit']='f2063fce'
info['baseline_managed']=str(base/'cameramsaa-before-managed')
info['final_managed']=str(base/'cameramsaa-managed')
info['managed_sha256']={}
for phase,folder in [('before','cameramsaa-before-managed'),('final','cameramsaa-managed')]:
    info['managed_sha256'][phase]={name:hashlib.sha256((base/folder/name).read_bytes()).hexdigest() for name in ['UnityEngine.CoreModule.dll','Unbound.Host.dll']}
info['invalid_image_keys']=['ARGB32.resolve','ARGBFloat.resolve']
info['runtime_commits']=['4e4811dd','d814000e']
(fixture/'evidence.json').write_text(json.dumps(info,indent=2)+'\n')
