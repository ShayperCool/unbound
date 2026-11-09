#!/usr/bin/env python3
"""Publish one verified Native capture as the canonical gzip reference (refuses to overwrite)."""
import argparse
import gzip
import json
from pathlib import Path
import shutil
from common import FIXTURE, header, samples
from verify import verify

p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--rate', type=int, choices=(44100, 48000), required=True)
p.add_argument('--capture', type=Path, required=True)
p.add_argument('--extend', action='store_true', help='Add cases only; every existing metadata value and sample must remain identical')
p.add_argument('--repeat', type=Path, required=True, help='Independent capture that must match every metadata value and raw sample')
p.add_argument('--contexts', action='store_true', help='Publish the separately measured output layouts awaiting graph support')
a = p.parse_args()
verify(a.capture, a.release, a.rate, contexts=a.contexts)
verify(a.repeat, a.release, a.rate, contexts=a.contexts)
assert header(a.capture) == header(a.repeat), 'independent capture metadata differs'
for key, record in header(a.capture).items():
    if isinstance(record, dict) and 'file' in record:
        assert samples(a.capture, record).tobytes() == samples(a.repeat, header(a.repeat)[key]).tobytes(), 'independent capture differs: '+key
target = FIXTURE/'Reference'/('unity-'+a.release)
if a.contexts: target = target/'layouts'
target = target/('rate-'+str(a.rate))
if target.exists():
    assert a.extend, 'reference already exists; --extend may only add identical controls'
    existing, incoming = header(target), header(a.capture)
    assert all(key in incoming and incoming[key] == value for key, value in existing.items()), 'existing metadata changed'
    for record in existing.values():
        if isinstance(record, dict) and 'file' in record:
            assert gzip.decompress((target/(record['file']+'.gz')).read_bytes()) == (a.capture/record['file']).read_bytes(), record['file']
else:
    target.mkdir(parents=True)
shutil.copy2(a.capture/'capture.json', target/'capture.json')
for f in sorted(a.capture.glob('*.f32')):
    if not (target/(f.name+'.gz')).exists():
        with gzip.GzipFile(target/(f.name+'.gz'), 'wb', mtime=0) as out: out.write(f.read_bytes())
verify(target, a.release, a.rate, contexts=a.contexts)
assert header(target) == header(a.capture)
print('Published', target)
