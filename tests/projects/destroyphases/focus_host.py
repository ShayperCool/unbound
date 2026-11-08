#!/usr/bin/env python3
"""Drive focus through the host's real X11 window inside an admitted GPU lease."""
import os
from pathlib import Path
import subprocess
import sys
import time
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from editor_runner import Display


def main():
    cli, project, output = sys.argv[1:]
    display = Display('1280x720x24')
    env = dict(os.environ, DISPLAY=display.name, FOCUS_LOOP_OUT=output)
    env.pop('WAYLAND_DISPLAY', None)
    env['WINIT_UNIX_BACKEND'] = 'x11'
    engine = focus = None
    try:
        engine = subprocess.Popen(['vglrun', '-d', 'egl', cli, 'run', '-p', project,
                                   '--frames', '800', '--size', '320x240', '--no-watch'], env=env)
        state_file = Path(output + '.step')
        window = None
        restored = False
        deadline = time.monotonic() + 300
        def xdotool(*args):
            return subprocess.check_output(['xdotool', *args], env=env, text=True).strip()
        while engine.poll() is None and time.monotonic() < deadline:
            if window is None:
                found = subprocess.run(['xdotool', 'search', '--onlyvisible', '--name', '.'],
                                       env=env, text=True, capture_output=True)
                if found.returncode == 0:
                    window = found.stdout.splitlines()[-1]
                    print('host focus window:', window, xdotool('getwindowname', window), flush=True)
                    xdotool('windowfocus', window)
            step = state_file.read_text() if state_file.exists() else ''
            if step == '1' and focus is None and window:
                focus = subprocess.Popen(['xmessage', '-title', 'q161-host-focus', '-geometry', '+1000+600', 'focus'], env=env)
                other = xdotool('search', '--sync', '--onlyvisible', '--name', '^q161-host-focus$').splitlines()[-1]
                xdotool('windowfocus', other)
                print('host focus loss requested:', xdotool('getwindowfocus'), flush=True)
            elif step == '2' and not restored:
                xdotool('windowfocus', window)
                restored = True
                print('host focus regained', flush=True)
            time.sleep(.02)
        if engine.poll() is None:
            raise RuntimeError('host focus probe exceeded 300 seconds')
        if not restored:
            raise RuntimeError('host did not deliver a focus loss and regain')
        return engine.returncode
    finally:
        for process in (engine, focus):
            if process is not None and process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill(); process.wait()
        display.close()


if __name__ == '__main__':
    raise SystemExit(main())
