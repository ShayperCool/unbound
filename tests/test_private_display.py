#!/usr/bin/env python3
"""Tests of tests/private_display.py against real Xvfb servers (skipped without Xvfb)."""
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import unittest
import unittest.mock

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import private_display as pd  # noqa: E402

SCRIPT = HERE/'private_display.py'


def server_pid(wrapper):
    """The pid of the X server a running wrapper (pid) started: its child named Xvfb."""
    for task in Path(f'/proc/{wrapper}/task').iterdir():
        for child in (task/'children').read_text().split():
            try:
                if Path(f'/proc/{child}/comm').read_text().strip() == 'Xvfb':
                    return int(child)
            except FileNotFoundError:
                continue
    raise AssertionError(f'no Xvfb under {wrapper}')


def alive(pid):
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    # A zombie (not yet reaped by its parent) has ended too.
    try:
        return Path(f'/proc/{pid}/stat').read_text().rsplit(')', 1)[1].split()[0] != 'Z'
    except (FileNotFoundError, IndexError):
        return False


def wait_until(condition, seconds=10):
    end = time.monotonic()+seconds
    while time.monotonic() < end:
        if condition():
            return True
        time.sleep(.05)
    return condition()


@unittest.skipUnless(shutil.which('Xvfb'), 'needs Xvfb')
class PrivateDisplayTest(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.dir = Path(self.temporary.name)

    def desktop(self):
        """A stand-in for the shared desktop display."""
        display = pd.Display('64x64x24')
        self.addCleanup(display.close)
        return display

    def test_a_display_starts_and_stops(self):
        display = pd.Display('64x64x24')
        try:
            self.assertRegex(display.name, r'^:\d+$')
            self.assertIsNone(display.server.poll())
        finally:
            display.close()
        self.assertIsNotNone(display.server.poll())

    def test_the_command_runs_on_its_own_x_display_only(self):
        desktop = self.desktop()
        base = dict(os.environ, DISPLAY=desktop.name, WAYLAND_DISPLAY='wayland-desktop',
                    WAYLAND_SOCKET='5', XAUTHORITY='/nonexistent/desktop-cookie', KEPT='yes')
        out = self.dir/'env'
        code = pd.run([sys.executable, '-c', 'import json, os, sys; json.dump(dict(os.environ), open(sys.argv[1], "w"))',
                       str(out)], screen='64x64x24', base=base)
        self.assertEqual(code, 0)
        seen = json.loads(out.read_text())
        self.assertRegex(seen['DISPLAY'], r'^:\d+$')
        self.assertNotEqual(seen['DISPLAY'], desktop.name)
        for name in ('WAYLAND_DISPLAY', 'WAYLAND_SOCKET', 'XAUTHORITY'):
            self.assertNotIn(name, seen)
        self.assertEqual((seen['XDG_SESSION_TYPE'], seen['WINIT_UNIX_BACKEND'], seen['KEPT']), ('x11', 'x11', 'yes'))
        # The server is gone once the command ended: nothing answers on its display.
        probe = subprocess.run(['xdpyinfo'], env=dict(base, DISPLAY=seen['DISPLAY']), capture_output=True) \
            if shutil.which('xdpyinfo') else None
        self.assertTrue(probe is None or probe.returncode != 0)

    @unittest.skipUnless(shutil.which('xclip'), 'needs xclip')
    def test_the_clipboard_is_the_runs_own(self):
        desktop = self.desktop()
        base = dict(os.environ, DISPLAY=desktop.name)
        base.pop('WAYLAND_DISPLAY', None)
        # Junk on the desktop's clipboard (xclip serves it in the background until replaced).
        subprocess.run(['xclip', '-selection', 'clipboard', '-i'], input=b'desktop junk', env=base, check=True)
        read = ['sh', '-c', 'xclip -selection clipboard -o > "$1" 2>/dev/null; true', 'read']
        write = ['sh', '-c', 'printf run-copy | xclip -selection clipboard -i && '
                 'xclip -selection clipboard -o > "$1"', 'write']
        self.assertEqual(pd.run([*read, str(self.dir/'first')], screen='64x64x24', base=base), 0)
        self.assertEqual(pd.run([*write, str(self.dir/'own')], screen='64x64x24', base=base), 0)
        self.assertEqual(pd.run([*read, str(self.dir/'second')], screen='64x64x24', base=base), 0)
        # A run starts with an empty clipboard, reads back what it copied, and leaves nothing behind.
        self.assertEqual((self.dir/'first').read_bytes(), b'')
        self.assertEqual((self.dir/'own').read_bytes(), b'run-copy')
        self.assertEqual((self.dir/'second').read_bytes(), b'')
        desktop_now = subprocess.run(['xclip', '-selection', 'clipboard', '-o'], env=base, capture_output=True,
                                     timeout=8)
        self.assertEqual(desktop_now.stdout, b'desktop junk')

    def test_exit_status_passes_through(self):
        self.assertEqual(pd.run(['sh', '-c', 'exit 7'], screen='64x64x24'), 7)
        self.assertEqual(pd.run(['sh', '-c', 'kill -KILL $$'], screen='64x64x24'), 128+signal.SIGKILL)

    def test_the_command_line(self):
        self.assertEqual(subprocess.run([sys.executable, str(SCRIPT), '--screen', '64x64x24', '--',
                                         'sh', '-c', 'test -n "$DISPLAY"; exit 5']).returncode, 5)
        refused = subprocess.run([sys.executable, str(SCRIPT), '--'], capture_output=True, text=True)
        self.assertEqual(refused.returncode, 2)
        self.assertIn('name the command', refused.stderr)

    def test_missing_programs_are_named(self):
        real = shutil.which
        def which(name, *args, **kwargs):
            return None if name in self.absent else real(name, *args, **kwargs)
        for absent, gpu, named in [(('vglrun',), True, 'vglrun'), (('Xvfb', 'vglrun'), True, 'Xvfb and vglrun'),
                                   (('Xvfb',), False, 'Xvfb')]:
            self.absent = absent
            with self.subTest(absent=absent), unittest.mock.patch.object(pd.shutil, 'which', which):
                with self.assertRaisesRegex(RuntimeError, f'needs {named} '):
                    pd.run(['true'], screen='64x64x24', gpu=gpu)
        # Without --gpu VirtualGL is not needed.
        self.absent = ('vglrun',)
        with unittest.mock.patch.object(pd.shutil, 'which', which):
            self.assertEqual(pd.run(['true'], screen='64x64x24'), 0)
        refused = subprocess.run([sys.executable, '-c', 'import sys, shutil; sys.path.insert(0, sys.argv[1]); '
                                  'shutil.which = lambda *a, **k: None; import private_display as p; '
                                  'sys.exit(p.main(["--", "true"]))', str(HERE)], capture_output=True, text=True)
        self.assertEqual(refused.returncode, 127)
        self.assertIn('private_display: a private X display needs Xvfb (X11 on Linux)', refused.stderr)

    def start_wrapper(self, command):
        """The wrapper running `command` (which writes $DISPLAY to `display`), and that display."""
        marker = self.dir/'display'
        wrapper = subprocess.Popen([sys.executable, str(SCRIPT), '--screen', '64x64x24', '--', 'sh', '-c',
                                    f'echo "$DISPLAY" > {marker}.tmp && mv {marker}.tmp {marker}; {command}'],
                                   start_new_session=True)

        def stop_group():
            try:
                os.killpg(wrapper.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            wrapper.wait()
        self.addCleanup(stop_group)
        self.assertTrue(wait_until(marker.exists, 20), 'the command did not start')
        return wrapper, marker.read_text().strip()

    def test_stopping_the_wrapper_stops_the_command_and_the_server(self):
        wrapper, display = self.start_wrapper(f'trap "echo stopped > {self.dir}/trap; exit 3" TERM; '
                                              'sleep 30 & wait')
        server = server_pid(wrapper.pid)
        wrapper.send_signal(signal.SIGTERM)
        self.assertEqual(wrapper.wait(20), 3)
        self.assertEqual((self.dir/'trap').read_text(), 'stopped\n')
        self.assertTrue(wait_until(lambda: not alive(server)))

    @unittest.skipUnless(shutil.which('setpriv'), 'needs setpriv')
    def test_a_killed_wrapper_takes_its_server_along(self):
        wrapper, display = self.start_wrapper('sleep 30')
        server = server_pid(wrapper.pid)
        self.assertTrue(alive(server))
        wrapper.kill()
        wrapper.wait(10)
        self.assertTrue(wait_until(lambda: not alive(server)), 'the private Xvfb outlived its wrapper')


if __name__ == '__main__':
    unittest.main()
