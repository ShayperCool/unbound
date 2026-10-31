#!/usr/bin/env python3
"""Retain new recordings separately; never rewrite the original camera evidence."""
from pathlib import Path
import gzip
import hashlib
import json
import shutil
import subprocess
import sys

fixture = Path(__file__).resolve().parent
cache = Path.home()/'.cache/compat'
phase = sys.argv[1]
assert phase in ('before', 'final', 'final2')
evidence = fixture/'Evidence/Followup'
evidence.mkdir(exist_ok=True)

def compare(name, reference, actual):
    result = subprocess.run([sys.executable, str(fixture/'compare.py'), str(reference), str(actual)], capture_output=True, text=True)
    (evidence/(name+'.txt')).write_text(result.stdout+result.stderr)
    print(name, 'exit', result.returncode, '\n'.join(result.stdout.splitlines()[-2:]))

def pack(source, name):
    destination = fixture/'Reference'/name
    destination.mkdir(exist_ok=True)
    shutil.copyfile(source/'values.jsonl', destination/'values.jsonl')
    manifest = {}
    for path in sorted(source.glob('*.rgba')):
        data = path.read_bytes()
        (destination/(path.name+'.gz')).write_bytes(gzip.compress(data,mtime=0))
        manifest[path.name] = hashlib.sha256(data).hexdigest()
    (destination/'sha256.json').write_text(json.dumps(manifest,indent=2)+'\n')
    return {'raw': str(source), 'observations': len((source/'values.jsonl').read_text().splitlines()),
            'images': len(manifest), 'values_sha256': hashlib.sha256((source/'values.jsonl').read_bytes()).hexdigest()}

info = {}
for space in ('linear','gamma'):
    native = cache/('camerafollowup-oracle-v3-'+space+'1')
    actual = cache/('camerafollowup-'+phase+('-v3-' if phase=='before' else '-')+space)
    if phase=='before':
        compare('native-repeat-'+space,native,cache/('camerafollowup-oracle-v3-'+space+'2'))
        info['native-'+space] = pack(native,'Followup'+space.title())
        info['before-'+space] = pack(actual,'FollowupBefore'+space.title())
    compare(phase+'-'+space,native,actual)
    # Export the entire original 948/195 matrix, preserving all keys and invalid inputs.
    rows = [json.loads(s) for s in (fixture/'Reference'/space.title()/'values.jsonl').read_text().splitlines()]
    keys = {r['k'] for r in rows}
    selected = [r for r in map(json.loads,(actual/'values.jsonl').read_text().splitlines()) if r['k'] in keys]
    assert len(selected)==len(rows)==948
    legacy = cache/('camerafollowup-legacy-'+phase+'-'+space)
    legacy.mkdir(exist_ok=True)
    (legacy/'values.jsonl').write_text(''.join(json.dumps(r,separators=(',',':'))+'\n' for r in selected))
    for row in rows:
        if row['k'].endswith('.pixels'):
            for suffix in ('.rgba','.png'):
                shutil.copyfile(actual/(row['k'][:-7]+suffix),legacy/(row['k'][:-7]+suffix))
    compare('legacy-'+phase+'-'+space,fixture/'Reference'/space.title(),legacy)
if phase=='before':
    info['baseline_commit']='1ac656a0'
    info['managed_sha256']={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (cache/'camerafollowup-before-managed').glob('*.dll')}
    (evidence/'recordings.json').write_text(json.dumps(info,indent=2)+'\n')
