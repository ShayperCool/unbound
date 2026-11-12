#!/usr/bin/env python3
"""Publish one verified Native capture as the canonical gzip reference (refuses to overwrite)."""
import argparse
import gzip
import json
from pathlib import Path
import shutil
from common import FIXTURE, devices, header, samples, surround_cases, transition_cases, wet_cases, wet_edge_cases, settings_cases, prologic_cases
from verify import verify

p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--rate', type=int, choices=(44100, 48000), required=True)
p.add_argument('--capture', type=Path, required=True)
p.add_argument('--extend', action='store_true', help='Add cases only; every existing metadata value and sample must remain identical')
p.add_argument('--repeat', type=Path, required=True, help='Independent capture that must match every metadata value and raw sample')
p.add_argument('--contexts', action='store_true', help='Publish the output layout controls (Reference/.../layouts)')
p.add_argument('--devices', action='store_true', help='Publish the device controls (Reference/.../devices, metadata only)')
p.add_argument('--surround', action='store_true', help='Publish the independent surround effects profile')
p.add_argument('--transitions', action='store_true', help='Publish the independent live surround profile')
p.add_argument('--wet', action='store_true', help='Publish the independent surround wet profile')
p.add_argument('--wet-edges', action='store_true', help='Publish independent wet placement boundary controls')
p.add_argument('--settings', action='store_true', help='Publish independent sample-rate and DSP-buffer controls')
p.add_argument('--prologic', action='store_true', help='Publish independent Prologic and Stereo controls')
a = p.parse_args()
if a.settings and a.rate != 48000: p.error('settings controls use a 48000 Hz session with per-case configurations')
selected = prologic_cases() if a.prologic else settings_cases() if a.settings else wet_edge_cases() if a.wet_edges else wet_cases() if a.wet else transition_cases() if a.transitions else surround_cases() if a.surround else [key for key, _ in devices()] if a.devices else None
verify(a.capture, a.release, a.rate, contexts=a.contexts, selected=selected)
verify(a.repeat, a.release, a.rate, contexts=a.contexts, selected=selected)
assert header(a.capture) == header(a.repeat), 'independent capture metadata differs'
for key, record in header(a.capture).items():
    if isinstance(record, dict) and 'file' in record:
        assert samples(a.capture, record).tobytes() == samples(a.repeat, header(a.repeat)[key]).tobytes(), 'independent capture differs: '+key
target = FIXTURE/'Reference'/('unity-'+a.release)
if a.contexts: target = target/'layouts'
if a.devices: target = target/'devices'
if a.surround: target = target/'surround'
if a.transitions: target = target/'transitions'
if a.wet: target = target/'wet'
if a.wet_edges: target = target/'wet-edges'
if a.settings: target = target/'settings'
if a.prologic: target = target/'prologic'
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
verify(target, a.release, a.rate, contexts=a.contexts, selected=selected)
assert header(target) == header(a.capture)
print('Published', target)
