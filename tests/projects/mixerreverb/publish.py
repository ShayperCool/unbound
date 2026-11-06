#!/usr/bin/env python3
"""Publish one verified Native capture as the canonical gzip reference (refuses to overwrite)."""
import argparse
import gzip
from pathlib import Path
import shutil
from common import FIXTURE, header
from verify import verify

p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--rate', type=int, choices=(44100, 48000), required=True)
p.add_argument('--capture', type=Path, required=True)
a = p.parse_args()
verify(a.capture, a.release, a.rate)
target = FIXTURE/'Reference'/('unity-'+a.release)/('rate-'+str(a.rate))
target.mkdir(parents=True, exist_ok=False)
shutil.copy2(a.capture/'capture.json', target/'capture.json')
for f in sorted(a.capture.glob('*.f32')):
    with gzip.GzipFile(target/(f.name+'.gz'), 'wb', mtime=0) as out: out.write(f.read_bytes())
verify(target, a.release, a.rate)
assert header(target) == header(a.capture)
print('Published', target)
