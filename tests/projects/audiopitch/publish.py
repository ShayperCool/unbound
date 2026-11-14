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
p.add_argument('--listener-chain', action='store_true', help='Publish listener component and contiguous script controls')
p.add_argument('--listener-diagnostics', action='store_true', help='Publish separate listener topology diagnostics without engine admission')
p.add_argument('--mono-live', action='store_true', help='Publish live Mono and component topology controls')
p.add_argument('--topology-diagnostics', action='store_true', help='Publish separate multiple-listener topology diagnostics')
p.add_argument('--topology-pcm', action='store_true', help='Publish live Echo/Chorus and zone PCM controls')
p.add_argument('--voice-input', action='store_true', help='Publish source callback width and processing diagnostics')
p.add_argument('--device-channels', type=int, choices=(2, 4, 6, 8), default=8, help='Private device width for encoding or source-input controls')
a = p.parse_args()
if a.device_channels != 8 and not (a.encoding or a.voice_input):
    p.error('--device-channels requires --encoding or --voice-input')
if a.settings and a.rate != 48000: p.error('settings controls use a 48000 Hz session with per-case configurations')
selected = mono_cases() if a.mono else zone_cases() if a.zones else prologic_cases() if a.prologic else settings_cases() if a.settings else wet_edge_cases() if a.wet_edges else wet_cases() if a.wet else transition_cases() if a.transitions else surround_cases() if a.surround else [key for key, _ in devices()] if a.devices else None
if a.listener_chain or a.listener_diagnostics:
    from common import profile
    selected = list(profile('listener-chain' if a.listener_chain else 'listener-diagnostics', a.rate))
if a.topology_diagnostics:
    from common import profile
    selected = list(profile('topology-diagnostics', a.rate))
if a.topology_pcm:
    from common import profile
    selected = list(profile('topology-pcm', a.rate))
if a.mono_live:
    from common import profile
    selected = list(profile('mono-live', a.rate))
if a.schedule:
    from common import schedule_cases
    selected = schedule_cases()
if a.voice_input:
    from common import profile
    selected = list(profile('voice-input', a.rate))
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
verify(a.capture, a.release, a.rate, contexts=a.contexts, selected=selected, device_channels=a.device_channels)
verify(a.repeat, a.release, a.rate, contexts=a.contexts, selected=selected, device_channels=a.device_channels)
assert header(a.capture) == header(a.repeat), 'independent capture metadata differs'
for key, record in header(a.capture).items():
    if isinstance(record, dict) and 'file' in record:
        assert samples(a.capture, record).tobytes() == samples(a.repeat, header(a.repeat)[key]).tobytes(), 'independent capture differs: '+key
if a.topology_pcm:
    # Store one canonical PCM stream; this profile adds a separately captured case manifest.
    parent = FIXTURE/'Reference'/('unity-'+a.release)/'mono-live'/('rate-'+str(a.rate))
    original = header(parent)
    for key, record in header(a.capture).items():
        assert original.get(key) == record, 'parent metadata differs: '+key
        if isinstance(record, dict) and 'file' in record:
            assert samples(parent, record).tobytes() == samples(a.capture, record).tobytes(), key
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
if a.mono_live: target = target/'mono-live'
if a.topology_diagnostics: target = target/'topology-diagnostics'
if a.topology_pcm: target = target/'topology-pcm'
if a.listener_chain: target = target/'listener-chain'
if a.listener_diagnostics: target = target/'listener-diagnostics'
if a.voice_input: target = target/'voice-input' if a.device_channels == 8 else target/'voice-input-devices'/('device-'+str(a.device_channels))
if a.encoding: target = target/'encoding' if a.device_channels == 8 else target/'encoding-devices'/('device-'+str(a.device_channels))
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
    if a.voice_input and a.device_channels != 8:
        # Reuse equal samples without requiring device contexts to share their PCM.
        shared = target.parents[2]/'voice-input'/('rate-'+str(a.rate))/(f.name+'.gz')
        packed = target/(f.name+'.gz')
        if shared.exists() and gzip.decompress(shared.read_bytes()) == f.read_bytes():
            if not packed.exists():
                packed.symlink_to(Path('../../../voice-input')/('rate-'+str(a.rate))/(f.name+'.gz'))
            continue
    if a.topology_pcm:
        packed = target/(f.name+'.gz')
        if not packed.exists(): packed.symlink_to(Path('../../mono-live')/('rate-'+str(a.rate))/(f.name+'.gz'))
        continue
    if not (target/(f.name+'.gz')).exists():
        with gzip.GzipFile(target/(f.name+'.gz'), 'wb', mtime=0) as out: out.write(f.read_bytes())
verify(target, a.release, a.rate, contexts=a.contexts, selected=selected, device_channels=a.device_channels)
assert header(target) == header(a.capture)
print('Published', target)
