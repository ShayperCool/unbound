#!/usr/bin/env python3
"""Launches of the Unity Editor's executable from a game Unbound runs; see README.md.

`check` runs the engine on a private copy of the probe (the launcher) with another private copy as
the virtual player's project, and checks what the launcher and the player wrote. Linux and macOS."""
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
    if sys.platform not in ('linux', 'darwin'):
        print(f'clonelaunch: the launch of the Editor\'s executable is guarded on Linux and macOS only ({sys.platform}: not implemented); not run')
        return 0
    editor_suffix = '/Contents/MacOS/Unity' if sys.platform == 'darwin' else '/Editor/Unity'
    out = run_root(ROOT, private_label())
    main = project_copy(ROOT, out/'main')
    clone = project_copy(ROOT, out/'clone')
    env = dict(os.environ, CLONELAUNCH_OUT=str(out), CLONELAUNCH_CLONE=str(clone))
    cli = value_probe.engine_cli()
    with open(out/'engine.log', 'w') as log:
        # The player runs inside the launcher's lease.
        # The launcher keeps its project data elsewhere (--project-data): the player keeps its own.
        code = subprocess.run([str(value_probe.LAUNCHER), '--kind', 'engine-gpu', 'timeout', '900', cli,
                               '--project-data', str(out/'main-data'), 'run', '-p', str(main),
                               '--headless', '100000', '--no-watch'], env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    main_text = (out/'main.txt').read_text() if (out/'main.txt').exists() else ''
    clone_lines = (out/'clone.txt').read_text().splitlines() if (out/'clone.txt').exists() else []
    editor_log = clone/'Logs'/'Editor.log'
    native_log = clone/'LibraryUnbound'/'VirtualPlayer.log'
    text = lambda path: path.read_text(errors='replace') if path.exists() else ''
    checks = [
        ('launcher exit', code == 0, code),
        ('plain launch refused', 'plain launch: Win32Exception:' in main_text and 'Operation not permitted' in main_text,
         [l for l in main_text.splitlines() if l.startswith('plain launch')]),
        ('refusal said on the launcher\'s standard error', "Unbound: refusing to run the Unity Editor's executable" in text(out/'engine.log'), 'engine.log'),
        ('clone ran', 'clone marker True' in main_text, main_text.strip()),
        ('clone exited', 'clone exited True' in main_text, main_text.strip()),
        ('clone command line', clone_lines[1:11] == [
            '-projectPath', str(clone), '--virtual-project-clone', '-forgetProjectPath', '-logFile', str(editor_log),
            '-vp-channel-name=vp-channel', '-ump-channel-service-port', '1234', '-vpId=abc'], clone_lines[:11]),
        ('clone command line names the Editor', bool(clone_lines) and clone_lines[0].endswith(editor_suffix), clone_lines[:1]),
        ('clone console log in its -logFile', 'The virtual player logs this.' in text(editor_log), str(editor_log)),
        ('clone process output in its own project data', 'INFO' in text(native_log), str(native_log)),
        ('clone project data its own', (clone/'LibraryUnbound'/'ProjectView').is_dir() and (out/'main-data'/'ProjectView').is_dir()
         and not (out/'main-data'/'VirtualPlayer.log').exists(), str(out/'main-data')),
        ('nothing beside the clone log', sorted(p.name for p in (clone/'Logs').iterdir()) == ['Editor.log'] if (clone/'Logs').is_dir() else False,
         sorted(p.name for p in (clone/'Logs').iterdir()) if (clone/'Logs').is_dir() else None),
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
