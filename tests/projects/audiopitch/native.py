#!/usr/bin/env python3
"""Create an independent private project and capture offline Unity Pitch Shifter audio."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess

p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--work', type=Path, required=True)
p.add_argument('--editor', type=Path)
p.add_argument('--rate', type=int, choices=(44100, 48000), required=True)
p.add_argument('--buffer', type=int, choices=(256, 512, 1024, 2048), default=1024)
p.add_argument('--cases', help='Comma-separated subset, for diagnostics only')
a = p.parse_args()
fixture = Path(__file__).resolve().parent
if (fixture/'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0] != 'm_EditorVersion: '+a.release:
    p.error('release does not match fixture')
a.work.mkdir(parents=True, exist_ok=False)
project = a.work/'project'
shutil.copytree(fixture, project, ignore=shutil.ignore_patterns('Reference', 'Library', 'Logs', '__pycache__', '*.py', '*.md'))
output = a.work/'capture'
editor = a.editor or Path.home()/'Unity/Hub/Editor'/a.release/'Editor/Unity'
import sys
sys.path.insert(0, str(fixture.parents[1]))
from editor_runner import EditorJob, run
import time
from common import cases, header


def capture(work, cases, env=None):
    """One Editor session recording `cases` into WORK/capture."""
    job = EditorJob(project, work/'editor.log', work/'editor-run',
                    args=['-executeMethod', 'ProbeRunner.Record'], batch=True, graphics=True,
                    editor=editor, env=dict(EFFECT_PROBE_OUT=str(work/'capture'), EFFECT_PROBE_RATE=str(a.rate),
                        EFFECT_PROBE_CASES=','.join(cases), EFFECT_PROBE_BUFFER=str(a.buffer), **(env or {})),
                    timeout=900)
    result = run(job)
    if result['exit_code'] != 0: raise SystemExit('Editor capture failed: '+str(result))


def surround_server(root):
    """A private PulseAudio server whose default sink has eight 7.1 channels.

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
        f'load-module module-null-sink sink_name=surround rate={a.rate} channels=8 '
        'channel_map=front-left,front-right,front-center,lfe,rear-left,rear-right,side-left,side-right\n'
        'set-default-sink surround\n')
    env = dict(os.environ, HOME=str(root), PULSE_RUNTIME_PATH=str(root), PULSE_STATE_PATH=str(root))
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


if a.cases:
    selected = a.cases.split(',')
    if any(key.startswith('context_') for key in selected) and any(not key.startswith('context_') for key in selected):
        p.error('select layout and stereo controls separately')
    if selected[0].startswith('context_'):
        server, address = surround_server(a.work/'pulse')
        try: capture(a.work, selected, dict(PULSE_SERVER=address))
        finally: server.terminate(); server.wait()
    else:
        capture(a.work, selected)
    from verify import verify
    verify(a.work/'capture', a.release, a.rate, contexts=selected[0].startswith('context_'), selected=selected)
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
from verify import verify
verify(output, a.release, a.rate)
verify(a.work/'contexts', a.release, a.rate, contexts=True)
