"""A private display stays available between independent client connections."""
import ctypes
import ctypes.util
import importlib.util
from pathlib import Path
import shutil
import sys
import subprocess
import tempfile
import time
from unittest import mock
import unittest

spec = importlib.util.spec_from_file_location('gameview_display_run', Path(__file__).with_name('run.py'))
run = importlib.util.module_from_spec(spec)
spec.loader.exec_module(run)


@unittest.skipUnless(sys.platform.startswith('linux') and shutil.which('Xvfb') and ctypes.util.find_library('X11'),
                     'requires Linux Xvfb and libX11')
class DisplayTests(unittest.TestCase):
    def test_display_number_can_arrive_in_separate_writes(self):
        directory = run.base.run_root(run.PROBE, 'displays')
        directory.mkdir(parents=True, exist_ok=True)
        original = subprocess.Popen
        with tempfile.TemporaryDirectory(dir=directory) as temporary:
            complete = Path(temporary)/'complete'
            script = ("import os,sys,time; from pathlib import Path; "
                      "os.write(int(sys.argv[1]), b'123'); time.sleep(0.05); "
                      "os.write(int(sys.argv[1]), b'\\n'); "
                      "Path(sys.argv[2]).write_text('complete'); time.sleep(30)")
            def writer(command, **kwargs):
                descriptor = kwargs['pass_fds'][0]
                return original([sys.executable, '-c', script, str(descriptor), str(complete)], **kwargs)
            with mock.patch.object(run.subprocess, 'Popen', side_effect=writer):
                name, server = run.display()
            try:
                self.assertEqual(':123', name)
                deadline = time.monotonic()+3
                while not complete.exists() and server.poll() is None and time.monotonic()<deadline:
                    time.sleep(0.01)
                self.assertTrue(complete.exists(), server.startup_log.read_text())
                self.assertIsNone(server.poll())
            finally:
                server.terminate()
                server.wait()

    def test_independent_clients_can_reconnect_without_a_reset_race(self):
        x11 = ctypes.CDLL(ctypes.util.find_library('X11'))
        x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
        x11.XOpenDisplay.restype = ctypes.c_void_p
        x11.XCloseDisplay.argtypes = [ctypes.c_void_p]
        x11.XCloseDisplay.restype = ctypes.c_int
        name, server = run.display()
        try:
            for client in range(300):
                display = x11.XOpenDisplay(name.encode())
                self.assertTrue(display, f'client {client} could not open {name}; server={server.poll()}; log={server.startup_log}\n{server.startup_log.read_text()}')
                x11.XCloseDisplay(display)
            self.assertIsNone(server.poll())
        finally:
            server.terminate()
            server.wait()


if __name__ == '__main__':
    unittest.main()
