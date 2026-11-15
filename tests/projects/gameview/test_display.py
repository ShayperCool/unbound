"""A private display stays available between independent client connections."""
import ctypes
import ctypes.util
import importlib.util
from pathlib import Path
import shutil
import sys
import unittest

spec = importlib.util.spec_from_file_location('gameview_display_run', Path(__file__).with_name('run.py'))
run = importlib.util.module_from_spec(spec)
spec.loader.exec_module(run)


@unittest.skipUnless(sys.platform.startswith('linux') and shutil.which('Xvfb') and ctypes.util.find_library('X11'),
                     'requires Linux Xvfb and libX11')
class DisplayTests(unittest.TestCase):
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
                self.assertTrue(display, f'client {client} could not open {name}')
                x11.XCloseDisplay(display)
            self.assertIsNone(server.poll())
        finally:
            server.terminate()
            server.wait()


if __name__ == '__main__':
    unittest.main()
