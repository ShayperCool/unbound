#!/usr/bin/env python3
"""Create an independent private project and capture offline Unity Pitch Shifter audio."""
import argparse
import json
import os
import re
from pathlib import Path
import shutil
import subprocess

p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--work', type=Path, required=True)
p.add_argument('--editor', type=Path)
p.add_argument('--rate', type=int, choices=(44100, 48000), required=True)
p.add_argument('--buffer', type=int, choices=(256, 512, 1024, 2048), default=1024)
p.add_argument('--cases', help='Comma-separated subset of one set, for diagnostics and part sessions')
p.add_argument('--reset-cases', action='store_true', help='Reset output before each case, then run its normal preflight')
p.add_argument('--set', choices=('all', 'layouts', 'devices', 'surround', 'transitions', 'resets', 'scheduled-resets', 'wet', 'wet-edges', 'settings', 'prologic', 'zones', 'mono', 'recovery', 'wide', 'wide-pcm', 'configurations', 'listener-scripts', 'encoding', 'schedule'), default='all',
               help='all: stereo, buffer and layout controls (plus device controls at 48000 Hz)')
a = p.parse_args()
if a.set == 'wide-pcm' and a.reset_cases:
    p.error('wide-pcm uses per-cell startup; --reset-cases is diagnostic with --set wide')
fixture = Path(__file__).resolve().parent
if (fixture/'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0] != 'm_EditorVersion: '+a.release:
    p.error('release does not match fixture')
a.work.mkdir(parents=True, exist_ok=False)
project = a.work/'project'
if a.set in ('recovery', 'wide', 'wide-pcm', 'configurations', 'listener-scripts', 'encoding', 'schedule'):
    import sys
    sys.path.insert(0, str(fixture.parents[1]))
    from authored_project import assemble
    assemble(fixture.name, a.release, 'default', project)
    if a.set in ('wide', 'wide-pcm', 'configurations'):
        from wide_boot import prepare
        prepare(project)
    if a.set in ('listener-scripts', 'encoding', 'schedule'):
        # Only the alias used by this independent private capture project is imported.
        source = (fixture/'Assets/Scripts/EffectProbe.cs').read_text()
        prefix = {'listener-scripts': 'context_scriptzone_', 'encoding': 'context_encoding_', 'schedule': 'context_schedule_'}[a.set]
        keep = set(re.findall(r'new\[\] \{ "'+prefix+r'[^"]+", "asset=([^"]+)"', source))
        assert keep, 'prepared controls have no mixer aliases'
        for mixer in (project/'Assets/Resources').glob('*.mixer'):
            if mixer.stem not in keep:
                mixer.unlink()
                mixer.with_name(mixer.name+'.meta').unlink(missing_ok=True)
    if a.set == 'configurations':
        from configuration_controls import prepare
        prepare(project)
else:
    shutil.copytree(fixture, project, ignore=shutil.ignore_patterns('Reference', 'Library', 'Logs', '__pycache__', '*.py', '*.md'))
output = a.work/'capture'
editor = a.editor or Path.home()/'Unity/Hub/Editor'/a.release/'Editor/Unity'
import sys
sys.path.insert(0, str(fixture.parents[1]))
from editor_runner import EditorJob, run
import time
from common import cases, devices, header, surround_cases, transition_cases, reset_cases, scheduled_reset_cases, wet_cases, wet_edge_cases, settings_cases, settings_sessions, context_sessions, prologic_cases, zone_cases, mono_cases, recovery_cases, listener_script_cases, schedule_cases


def capture(work, cases, env=None, prepared=False, buffer=None):
    """One Editor session recording `cases` into WORK/capture."""
    job = EditorJob(project, work/'editor.log', work/'editor-run',
                    args=['-executeMethod', 'ProbeRunner.RecordPrepared' if prepared else 'ProbeRunner.Record'], batch=True, graphics=True,
                    editor=editor, env=dict(EFFECT_PROBE_OUT=str(work/'capture'), EFFECT_PROBE_RATE=str(a.rate),
                        EFFECT_PROBE_RESET_CASES='1' if a.reset_cases else '0',
                        EFFECT_PROBE_CASES=','.join(cases), EFFECT_PROBE_BUFFER=str(buffer or a.buffer), **(env or {})),
                    timeout=900)
    result = run(job)
    if result['exit_code'] != 0: raise SystemExit('Editor capture failed: '+str(result))


def surround_server(root, channels=8):
    """A private PulseAudio server whose default sink has `channels` channels (eight: 7.1).

    The output device bounds the active DSP layout: on a two-channel default device, Quad,
    Surround, 5.1 and 7.1 requests keep the requested configuration getter value but run stereo
    DSP callbacks. The layout controls therefore record against this isolated server; the
    session's own audio server and default device are left untouched."""
    binary = shutil.which('pulseaudio')
    if not binary: raise SystemExit('layout controls need the pulseaudio binary for a private 7.1 sink')
    root.mkdir(parents=True)
    socket = root/'native'
    (root/'server.pa').write_text(
        f'load-module module-native-protocol-unix socket={socket} auth-anonymous=1\n'
        f'load-module module-null-sink sink_name=surround rate={a.rate} channels={channels} '
        + ('channel_map=front-left,front-right,front-center,lfe,rear-left,rear-right,side-left,side-right'
           if channels == 8 else '') + '\n'
        'set-default-sink surround\n')
    env = dict(os.environ, PULSE_RUNTIME_PATH=str(root), PULSE_STATE_PATH=str(root),
               DBUS_SESSION_BUS_ADDRESS='unix:path='+str(root/'no-session-bus'))
    server = subprocess.Popen([binary, '-n', '-F', str(root/'server.pa'), '--daemonize=no', '--exit-idle-time=-1',
                               '--use-pid-file=no', '--system=false'], env=env,
                              stdout=(root/'server.log').open('wb'), stderr=subprocess.STDOUT)
    for _ in range(100):
        if socket.exists(): break
        if server.poll() is not None: raise SystemExit('private PulseAudio server failed; see '+str(root/'server.log'))
        time.sleep(0.1)
    else:
        server.kill(); raise SystemExit('private PulseAudio server did not start')
    return server, 'unix:'+str(socket)


def kind(key):
    return 'device' if key.startswith('device_') else 'layout' if key.startswith('context_') else 'stereo'


def part(work, selected, prepared=False, buffer=None):
    """One session of one set: layouts against an eight-channel device, a device control against
    a device of its own channel count, stereo controls on the session's default device."""
    if kind(selected[0]) == 'stereo':
        capture(work, selected, prepared=prepared, buffer=buffer)
        return
    channels = dict(devices()).get(selected[0], 8)
    server, address = surround_server(work/'pulse', channels)
    try:
        capture(work, selected, dict(PULSE_SERVER=address), prepared=prepared, buffer=buffer)
    finally:
        try:
            modules = subprocess.check_output(['pactl', '--server', address, 'list', 'short', 'modules'], text=True)
            for row in modules.splitlines():
                fields = row.split('\t')
                if len(fields) > 1 and fields[1] == 'module-null-sink':
                    index = str(int(fields[0]))
                    subprocess.run(['pactl', '--server', address, 'unload-module', index], check=True)
        finally:
            server.terminate(); server.wait()


def record_devices(work):
    """Every device control in its own session, merged into WORK/capture (48000 Hz only)."""
    merged = {}
    for key, _ in devices():
        subprocess.run([sys.executable, str(fixture/'native.py'), '--release', a.release, '--rate', str(a.rate),
                        '--work', str(work/key), '--cases', key, '--editor', str(editor)], check=True)
        for name, value in header(work/key/'capture').items():
            assert merged.get(name, value) == value, name
            merged[name] = value
    (work/'capture').mkdir()
    (work/'capture'/'capture.json').write_text(json.dumps(merged, indent=2)+'\n')


from verify import verify
if a.set == 'configurations':
    if a.cases: p.error('configuration metadata controls do not select PCM cases')
    part(a.work, ['context_configuration'], prepared=True, buffer=8192)
    assert 'Assertion failed on expression:' not in (a.work/'editor.log').read_text(), 'Editor assertion'
    from configuration_controls import verify as verify_configuration
    verify_configuration(output/'settings.json', a.rate, a.release)
    raise SystemExit
if a.set in ('wide', 'wide-pcm'):
    if a.rate != 48000: p.error('wide controls use a 48000 Hz session with per-case configurations')
    merged = {}
    for session, selected in context_sessions(a.set).items():
        if a.cases:
            selected = [key for key in selected if key in a.cases.split(',')]
            if not selected: continue
        work = a.work/session
        work.mkdir()
        part(work, selected, prepared=True, buffer=8192)
        assert 'Assertion failed on expression:' not in (work/'editor.log').read_text(), 'Editor assertion'
        verify(work/'capture', a.release, a.rate, contexts=True, selected=selected)
        for key, value in header(work/'capture').items():
            if key in ('release', 'rate', 'reset_cases'): assert merged.get(key, value) == value, key
            else: assert key not in merged, key
            merged[key] = value
        output.mkdir(exist_ok=True)
        for file in (work/'capture').glob('*.f32'): os.link(file, output/file.name)
        (output/'capture.json').write_text(json.dumps(merged, indent=2)+'\n')
    verify(output, a.release, a.rate, contexts=True, selected=a.cases.split(',') if a.cases else list(__import__('common').profile(a.set, a.rate)))
    raise SystemExit
if a.cases:
    selected = a.cases.split(',')
    if len({kind(key) for key in selected}) != 1 or (kind(selected[0]) == 'device' and len(selected) != 1):
        p.error('select controls of one set (one device control per session)')
    part(a.work, selected, prepared=a.set == 'encoding')
    if any(key.startswith(('context_settings_', 'context_prologic_', 'context_encoding_')) for key in selected):
        assert 'Assertion failed on expression:' not in (a.work/'editor.log').read_text(), 'Editor assertion; see '+str(a.work/'editor.log')
    verify(a.work/'capture', a.release, a.rate, contexts=kind(selected[0]) == 'layout', selected=selected)
    raise SystemExit
if a.set == 'devices':
    if a.rate != 48000: p.error('device controls are recorded at 48000 Hz')
    record_devices(a.work)
    verify(a.work/'capture', a.release, a.rate, selected=[key for key, _ in devices()])
    raise SystemExit
if a.set == 'layouts':
    part(a.work, cases(True))
    verify(a.work/'capture', a.release, a.rate, contexts=True)
    raise SystemExit
if a.set in ('settings', 'prologic', 'encoding'):
    if a.set == 'settings' and a.rate != 48000: p.error('settings controls use a 48000 Hz session with per-case configurations')
    merged = {}
    for index, (session, selected) in enumerate(context_sessions(a.set).items()):
        work = a.work/session
        work.mkdir()
        buffer = int(session.split('_')[1]) if a.set == 'settings' else 1024
        part(work, selected, prepared=a.set == 'encoding' or index > 0, buffer=buffer)
        assert 'Assertion failed on expression:' not in (work/'editor.log').read_text(), 'Editor assertion; see '+str(work/'editor.log')
        verify(work/'capture', a.release, a.rate, contexts=True, selected=selected)
        for key, value in header(work/'capture').items():
            if key in ('release', 'rate'):
                assert merged.get(key, value) == value, key
            else:
                assert key not in merged, key
            merged[key] = value
        (a.work/'capture').mkdir(exist_ok=True)
        for f in (work/'capture').glob('*.f32'): shutil.copy2(f, a.work/'capture'/f.name)
    (a.work/'capture/capture.json').write_text(json.dumps(merged, indent=2)+'\n')
    verify(a.work/'capture', a.release, a.rate, contexts=True, selected=list(__import__('common').profile(a.set, a.rate)))
    if a.set == 'encoding':
        from encoding_pairs import compare_encoding_pairs
        assert compare_encoding_pairs(a.work/'capture', a.work/'paired-six-input-modes.jsonl.gz', a.release, a.rate) == 0
    if a.set == 'prologic':
        from prologic_pairs import compare_pairs
        assert compare_pairs(a.work/'capture', a.work/'paired-modes.jsonl.gz', a.release, a.rate) == 0
    raise SystemExit
if a.set in ('zones', 'mono', 'recovery', 'listener-scripts', 'schedule'):
    selected = schedule_cases() if a.set == 'schedule' else listener_script_cases() if a.set == 'listener-scripts' else recovery_cases() if a.set == 'recovery' else zone_cases() if a.set == 'zones' else mono_cases()
    part(a.work, selected, prepared=a.set in ('recovery', 'listener-scripts', 'schedule'))
    assert 'Assertion failed on expression:' not in (a.work/'editor.log').read_text(), 'Editor assertion'
    verify(a.work/'capture', a.release, a.rate, contexts=True, selected=selected)
    raise SystemExit
if a.set == 'wet-edges':
    part(a.work, wet_edge_cases())
    verify(a.work/'capture', a.release, a.rate, contexts=True, selected=wet_edge_cases())
    raise SystemExit
if a.set == 'wet':
    part(a.work, wet_cases())
    verify(a.work/'capture', a.release, a.rate, contexts=True, selected=wet_cases())
    raise SystemExit
if a.set == 'scheduled-resets':
    part(a.work, scheduled_reset_cases())
    verify(a.work/'capture', a.release, a.rate, contexts=True, selected=scheduled_reset_cases())
    raise SystemExit
if a.set == 'resets':
    part(a.work, reset_cases())
    verify(a.work/'capture', a.release, a.rate, contexts=True, selected=reset_cases())
    raise SystemExit
if a.set == 'transitions':
    part(a.work, transition_cases())
    verify(a.work/'capture', a.release, a.rate, contexts=True, selected=transition_cases())
    raise SystemExit
if a.set == 'surround':
    part(a.work, surround_cases())
    verify(a.work/'capture', a.release, a.rate, contexts=True, selected=surround_cases())
    raise SystemExit
capture(a.work, [key for key in cases() if not key.startswith('buffer_')])
output = a.work/'capture'
merged = header(output)
for size in (256, 512, 2048):
    part = a.work/('buffer-'+str(size))
    subprocess.run([sys.executable, str(fixture/'native.py'), '--release', a.release,
                    '--rate', str(a.rate), '--work', str(part), '--buffer', str(size),
                    '--cases', 'buffer_'+str(size), '--editor', str(editor)], check=True)
    for key, value in header(part/'capture').items():
        if key in ('release', 'rate'):
            assert value == merged[key], key
            continue
        assert key not in merged, key
        merged[key] = value
        if isinstance(value, dict) and 'file' in value:
            shutil.copy2(part/'capture'/value['file'], output/value['file'])
(output/'capture.json').write_text(json.dumps(merged, indent=2)+'\n')
layouts = a.work/'layouts'
subprocess.run([sys.executable, str(fixture/'native.py'), '--release', a.release, '--rate', str(a.rate),
                '--work', str(layouts), '--cases', ','.join(cases(True)), '--editor', str(editor)], check=True)
(a.work/'contexts').symlink_to(layouts/'capture', target_is_directory=True)
verify(output, a.release, a.rate)
verify(a.work/'contexts', a.release, a.rate, contexts=True)
if a.rate == 48000:
    record_devices(a.work/'devices')
