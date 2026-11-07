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
p.add_argument('--extend', action='store_true', help='Add cases while requiring every existing value and sample bit to remain identical')
a = p.parse_args()
verify(a.capture, a.release, a.rate)
target = FIXTURE/'Reference'/('unity-'+a.release)/('rate-'+str(a.rate))
if a.extend:
    previous, current = header(target), header(a.capture)
    if not previous: raise SystemExit('no reference to extend')
    for key, value in previous.items():
        if current.get(key) != value: raise SystemExit('existing reference metadata changed: '+key)
    for packed in target.glob('*.f32.gz'):
        raw = a.capture/packed.name.removesuffix('.gz')
        if not raw.is_file() or gzip.decompress(packed.read_bytes()) != raw.read_bytes():
            raise SystemExit('existing reference samples changed: '+packed.name)
else:
    target.mkdir(parents=True, exist_ok=False)
for f in sorted(a.capture.glob('*.f32')):
    packed = target/(f.name+'.gz')
    if not packed.exists():
        with gzip.GzipFile(packed, 'wb', mtime=0) as out: out.write(f.read_bytes())
shutil.copy2(a.capture/'capture.json', target/'capture.json')
verify(target, a.release, a.rate)
assert header(target) == header(a.capture)
print('Published', target)
