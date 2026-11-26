#!/usr/bin/env python3
"""Launches of the Unity Editor's executable from a game Unbound runs; see README.md.

`check` runs the engine on a private copy of the probe (the launcher) with another private copy as
the virtual player's project, and checks what the launcher and the player wrote. Linux only."""
import argparse
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(REPO/'tests'))
from probe_paths import project_copy, run_root, private_label  # noqa: E402
import value_probe  # noqa: E402


def check():
    if sys.platform != 'linux':
        print('clonelaunch: the launch of the Editor\'s executable is guarded on Linux only (macOS: not implemented); not run')
        return 0
    out = run_root(ROOT, private_label())
    main = project_copy(ROOT, out/'main')
    clone = project_copy(ROOT, out/'clone')
    env = dict(os.environ, CLONELAUNCH_OUT=str(out), CLONELAUNCH_CLONE=str(clone))
    cli = value_probe.engine_cli()
    with open(out/'engine.log', 'w') as log:
        # The player runs inside the launcher's lease.
        code = subprocess.run([str(value_probe.LAUNCHER), '--kind', 'engine-gpu', 'timeout', '900', cli, 'run', '-p', str(main),
                               '--headless', '100000', '--no-watch'], env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    main_text = (out/'main.txt').read_text() if (out/'main.txt').exists() else ''
    clone_lines = (out/'clone.txt').read_text().splitlines() if (out/'clone.txt').exists() else []
    clone_log = out/'clone-log.txt'
    checks = [
        ('launcher exit', code == 0, code),
        ('plain launch refused', 'plain launch: Win32Exception:' in main_text and 'Operation not permitted' in main_text,
         [l for l in main_text.splitlines() if l.startswith('plain launch')]),
        ('clone ran', 'clone marker True' in main_text, main_text.strip()),
        ('clone exited 0', 'clone exited True code 0' in main_text, main_text.strip()),
        ('clone command line', clone_lines[1:11] == [
            '-projectPath', str(clone), '--virtual-project-clone', '-forgetProjectPath', '-logFile', str(clone_log),
            '-vp-channel-name=vp-channel', '-ump-channel-service-port', '1234', '-vpId=abc'], clone_lines[:11]),
        ('clone command line names the Editor', bool(clone_lines) and clone_lines[0].endswith('/Unity'), clone_lines[:1]),
        ('clone output in its -logFile', clone_log.exists() and clone_log.stat().st_size > 0, str(clone_log)),
    ]
    failed = [(name, detail) for name, ok, detail in checks if not ok]
    for name, detail in failed:
        print(f'FAIL {name}: {detail}')
    print(f'clonelaunch: {len(checks) - len(failed)}/{len(checks)} checks pass; engine exit {code}; logs {out}')
    return 1 if failed else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['check'])
    parser.parse_args()
    return check()


if __name__ == '__main__':
    sys.exit(main())
