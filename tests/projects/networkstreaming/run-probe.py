#!/usr/bin/env python3
"""Own the loopback server lifetime and run an Editor/Unbound command under the shared lock."""
import argparse
import os
import pathlib
import subprocess
import sys
import time

p = argparse.ArgumentParser()
p.add_argument('output', type=pathlib.Path)
p.add_argument('command', nargs=argparse.REMAINDER)
a = p.parse_args()
a.output = a.output.resolve()
a.output.mkdir(parents=True, exist_ok=True)
if list(a.output.glob('*.release')) or (a.output / 'records.jsonl').exists():
    p.error('use a fresh output directory')
server = subprocess.Popen([sys.executable, str(pathlib.Path(__file__).with_name('server.py')), str(a.output)])
try:
    deadline = time.monotonic() + 5
    while not (a.output / 'url').exists():
        if server.poll() is not None or time.monotonic() > deadline:
            raise RuntimeError('loopback server failed to start')
        time.sleep(0.01)
    command = a.command[1:] if a.command[:1] == ['--'] else a.command
    if not command:
        p.error('provide a command after --')
    result = subprocess.run([str(pathlib.Path.home() / '.cache/compat/with-heavy-job'), 'timeout', '180', *command],
                            env=dict(os.environ, NETWORK_STREAMING_OUT=str(a.output)))
    if result.returncode:
        sys.exit(result.returncode)
    if not (a.output / 'finished').exists():
        raise RuntimeError('probe did not finish')
finally:
    server.terminate()
    server.wait(timeout=5)
