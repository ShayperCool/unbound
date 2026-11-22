#!/usr/bin/env python3
"""Check consecutive captures and quitting through the package's normal entry points."""
import argparse
import os
import struct
import subprocess

from run import DRIVER, PROBE, value_probe
from probe_paths import editor_path, run_root

PNG_HEADER = b'\x89PNG\r\n\x1a\n'
PNG_TRAILER = bytes.fromhex('0000000049454e44ae426082')  # PNG IEND chunk


def check_png(path):
    with path.open('rb') as image:
        header = image.read(24)
        if len(header) != 24 or header[:8] != PNG_HEADER or header[12:16] != b'IHDR':
            raise ValueError(f'{path}: missing PNG header')
        width, height = struct.unpack('>II', header[16:24])
        if width == 0 or height == 0:
            raise ValueError(f'{path}: empty image')
        image.seek(-len(PNG_TRAILER), os.SEEK_END)
        if image.read() != PNG_TRAILER:
            raise ValueError(f'{path}: incomplete PNG')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('gate', 'editor'))
    parser.add_argument('--label')
    args = parser.parse_args()
    label = args.label or f'check-shots-{os.getpid()}'
    out = run_root(PROBE, label)
    project = value_probe.assemble(PROBE, out, DRIVER)
    capture = out/'shots'
    capture.mkdir()
    # A shot may reuse a name from a previous run. The stale file cannot satisfy the check.
    (capture/'first.png').write_text('previous capture')
    env = dict(os.environ, GAMEDRIVE_OUT=str(capture),
               GAMEDRIVE_SCRIPT='wait:0.25;shot:first;shot:second;quit:7')
    if args.mode == 'editor':
        env.update(GAMEDRIVE_SCENE='Assets/Scenes/Probe.unity', GAMEDRIVE_SIZE='320x240')
        argv = [str(editor_path()), '-projectPath', str(project), '-executeMethod',
                'Unbound.GameDrive.GameDriveRun.Run', '-logFile', str(out/'editor.log')]
    else:
        argv = [value_probe.engine_cli(), 'run', '-p', str(project), '--no-watch', '--size', '320x240']
    with (out/'process.log').open('w') as log:
        code = subprocess.run([str(value_probe.LAUNCHER), '--kind', 'engine-gpu', 'timeout', '120', *argv],
                              env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    expected = 7 if args.mode == 'editor' else 0
    if code != expected:
        raise SystemExit(f'gamedrive screenshots: expected process exit {expected}, got {code}; log {out}/process.log')
    lines = (capture/'drive.log').read_text().splitlines()
    if not lines or not lines[-1].endswith(' done 7'):
        raise SystemExit(f'gamedrive screenshots: expected done 7 in {capture}/drive.log')
    try:
        for name in ('first', 'second'):
            check_png(capture/f'{name}.png')
    except (OSError, ValueError) as error:
        raise SystemExit(f'gamedrive screenshots: {error}') from error
    print(f'gamedrive screenshots: 2/2 complete PNGs, done 7, process exit {code}')


if __name__ == '__main__':
    main()
