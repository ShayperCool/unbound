#!/usr/bin/env python3
"""Run the probe against its loopback server (server.py).

    run.py check  [--release R] [--unbound PATH] [--work DIR]   Unbound, compared with Reference/
    run.py editor [--release R] --out DIR [--editor PATH]      record a Unity Editor capture

Each run starts the server for a fresh output directory, runs the command with
NETWORK_CACHING_OUT set and stops the server even when the run fails. The heavy-job launcher
(~/.cache/compat/with-heavy-job) admits the run where it is installed. `check` writes under
$XDG_CACHE_HOME/unbound/validation/runs and exits with the comparator's status.
"""
import argparse
import datetime
import os
from pathlib import Path
import platform
import subprocess
import sys
import time

fixture = Path(__file__).resolve().parent
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
TIMEOUT = 600


def default_editor(release):
    if platform.system() == 'Darwin':
        return Path('/Applications/Unity/Hub/Editor')/release/'Unity.app/Contents/MacOS/Unity'
    return Path.home()/'Unity/Hub/Editor'/release/'Editor/Unity'


def run(out, command, kind):
    out.mkdir(parents=True, exist_ok=False)
    server = subprocess.Popen([sys.executable, str(fixture/'server.py'), str(out)])
    try:
        deadline = time.monotonic() + 10
        while not (out/'url').exists():
            if server.poll() is not None or time.monotonic() > deadline:
                raise SystemExit('the loopback server failed to start')
            time.sleep(0.01)
        if LAUNCHER.exists():
            command = [str(LAUNCHER), '--kind', kind, 'timeout', str(TIMEOUT), *command]
        env = dict(os.environ, NETWORK_CACHING_OUT=str(out))
        with (out/'run.log').open('w') as log:
            result = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT,
                                    timeout=None if LAUNCHER.exists() else TIMEOUT)
        if result.returncode:
            raise SystemExit(f'the run exited with {result.returncode}; see {out/"run.log"}')
        if not (out/'finished').exists():
            raise SystemExit(f'the probe did not finish; see {out/"run.log"}')
    finally:
        server.terminate()
        server.wait(timeout=10)


p = argparse.ArgumentParser()
p.add_argument('mode', choices=('check', 'editor'))
p.add_argument('--release', default=os.environ.get('UNBOUND_PROBE_UNITY_VERSION') or '6000.0.71f1')
p.add_argument('--unbound', type=Path, default=os.environ.get('UNBOUND'))
p.add_argument('--editor', type=Path)
p.add_argument('--work', type=Path)
p.add_argument('--out', type=Path)
a = p.parse_args()
if (fixture/'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0] != 'm_EditorVersion: ' + a.release:
    p.error(f'the probe project is not on {a.release}')

if a.mode == 'editor':
    if not a.out:
        p.error('editor needs --out')
    out = a.out.resolve()
    editor = a.editor or default_editor(a.release)
    run(out, [str(editor), '-batchmode', '-projectPath', str(fixture), '-executeMethod', 'ProbeRunner.Record',
              '-logFile', str(out/'editor.log')], 'editor-gpu')
    print('capture:', out/'records.jsonl')
    raise SystemExit(0)

if not a.unbound:
    p.error('check needs --unbound or $UNBOUND')
cache = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/validation/runs'
work = (a.work or cache/('networkcaching-' + datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f'))).resolve()
run(work, [str(a.unbound), 'run', '-p', str(fixture), '--scene', 'Assets/Scenes/Probe.unity',
           '--headless', '10000000', '--no-watch'], 'engine-cpu')
reference = fixture/'Reference'/f'unity-{a.release}.jsonl'
raise SystemExit(subprocess.run([sys.executable, str(fixture/'compare.py'), str(reference), str(work/'records.jsonl')]).returncode)
