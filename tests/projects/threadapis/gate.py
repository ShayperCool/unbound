#!/usr/bin/env python3
"""Replay the main-thread API matrix against its release's committed Editor reference."""
import os
from pathlib import Path
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
from probe_paths import reference_file, run_root  # noqa: E402
from value_probe import assemble, LAUNCHER  # noqa: E402


def main():
    label = time.strftime('gate-%Y%m%d-%H%M%S-')+str(os.getpid())
    out = run_root(PROBE, label)
    project = assemble(PROBE, out)
    cli = os.environ.get('UNBOUND') or str(PROBE.parents[2]/'target/release/unbound')
    capture = out/'values.jsonl'
    with (out/'engine.log').open('w') as log:
        code = subprocess.call([str(LAUNCHER), '--kind', 'engine-cpu', 'timeout', '600', cli, 'run',
                                '-p', str(project), '--headless', '30', '--no-watch'],
                               env={**os.environ, 'THREAD_APIS_OUT': str(capture)}, stdout=log, stderr=subprocess.STDOUT)
    if code or not capture.exists():
        print(f'threadapis: engine exit {code}, capture exists {capture.exists()}; log {out}/engine.log')
        return 1
    return subprocess.call([sys.executable, str(PROBE/'compare.py'), str(reference_file(PROBE, 'jsonl')), str(capture)])


if __name__ == '__main__':
    sys.exit(main())
