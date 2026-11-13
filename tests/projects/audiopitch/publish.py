#!/usr/bin/env python3
"""Publish one verified Native capture as the canonical gzip reference (refuses to overwrite)."""
import argparse
import gzip
import json
from pathlib import Path
import shutil
from common import FIXTURE, devices, header, samples, surround_cases, transition_cases, wet_cases, wet_edge_cases, settings_cases, prologic_cases, zone_cases, mono_cases
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
p.add_argument('--zones', action='store_true', help='Publish live and overlapping zone controls')
p.add_argument('--mono', action='store_true', help='Publish Mono output and Stereo control captures')
p.add_argument('--listener-scripts', action='store_true', help='Publish listener script and zone-return controls')
p.add_argument('--wide', action='store_true', help='Publish the independent wide output-settings controls')
p.add_argument('--wide-pcm', action='store_true', help='Publish the certified wide settings')
p.add_argument('--schedule', action='store_true', help='Publish fractional scheduling and audio-update controls')
p.add_argument('--encoding', action='store_true', help='Publish six-input encoder availability diagnostics, without engine PCM admission')
a = p.parse_args()
if a.settings and a.rate != 48000: p.error('settings controls use a 48000 Hz session with per-case configurations')
selected = mono_cases() if a.mono else zone_cases() if a.zones else prologic_cases() if a.prologic else settings_cases() if a.settings else wet_edge_cases() if a.wet_edges else wet_cases() if a.wet else transition_cases() if a.transitions else surround_cases() if a.surround else [key for key, _ in devices()] if a.devices else None
if a.schedule:
    from common import schedule_cases
    selected = schedule_cases()
if a.encoding:
    from common import encoding_cases
    selected = encoding_cases()
if a.listener_scripts:
    from common import listener_script_cases
    selected = listener_script_cases()
if a.wide or a.wide_pcm:
    from common import profile
    assert a.rate == 48000
    selected = list(profile('wide-pcm' if a.wide_pcm else 'wide', a.rate))
    assert not a.wide_pcm or not header(a.capture).get('reset_cases', False)
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
if a.zones: target = target/'zones'
if a.mono: target = target/'mono'
if a.listener_scripts: target = target/'listener-scripts'
if a.schedule: target = target/'schedule'
if a.encoding: target = target/'encoding'
if a.wide: target = target/'wide'
if a.wide_pcm: target = target/'wide-pcm'
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
