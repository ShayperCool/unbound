#!/usr/bin/env python3
"""Publish one verified Native capture as the canonical gzip reference (refuses to overwrite)."""
import argparse
import gzip
import json
from pathlib import Path
import shutil
from common import FIXTURE, header
from verify import verify

p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--profile', choices=('static', 'spatial', 'spatial-factor0', 'spatial-factor2', 'live'), default='static')
p.add_argument('--rate', type=int, choices=(44100, 48000), required=True)
p.add_argument('--capture', type=Path, required=True)
p.add_argument('--extend', action='store_true', help='Add cases only; every existing metadata value and sample must remain identical')
a = p.parse_args()
if a.profile == 'live':
    from live import verify
elif a.profile.startswith('spatial'):
    from spatial import verify
verify_args = (a.release, a.rate, a.profile) if a.profile.startswith('spatial') else (a.release, a.rate)
verify(a.capture, *verify_args)
target = FIXTURE/'Reference'/('unity-'+a.release)/(('rate-' if a.profile == 'static' else a.profile+'-rate-')+str(a.rate))
if target.exists():
    assert a.extend, 'reference already exists; --extend may only add identical controls'
    existing, incoming = header(target), header(a.capture)
    for key, value in existing.items():
        if key == 'sessions' and a.profile == 'live':
            # Extending a session appends controls after its existing buffers. Preserve every
            # earlier case and boundary; sample identity is checked independently below.
            assert list(incoming[key])[:len(value)] == list(value), 'existing sessions changed'
            assert all(incoming[key][session][:len(cases)] == cases for session, cases in value.items()), 'existing session order changed'
        else:
            assert key in incoming and incoming[key] == value, 'existing metadata changed'
    for record in existing.values():
        if isinstance(record, dict) and 'file' in record:
            assert gzip.decompress((target/(record['file']+'.gz')).read_bytes()) == (a.capture/record['file']).read_bytes(), record['file']
else:
    target.mkdir(parents=True)
shutil.copy2(a.capture/'capture.json', target/'capture.json')
for f in sorted(a.capture.glob('*.f32')):
    if not (target/(f.name+'.gz')).exists():
        with gzip.GzipFile(target/(f.name+'.gz'), 'wb', mtime=0) as out: out.write(f.read_bytes())
verify(target, *verify_args)
assert header(target) == header(a.capture)
print('Published', target)
