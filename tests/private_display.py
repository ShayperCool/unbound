#!/usr/bin/env python3
"""A private X display for one run, so that what the run shares through the desktop (its
clipboard above all) belongs to that run alone.

    python3 tests/private_display.py [--screen 1280x720x24] [--gpu] -- COMMAND ...

starts an Xvfb that picks a free display number itself and runs COMMAND with DISPLAY naming it
and without the Wayland variables, so that its windows and its clipboard use that X server. The
display's clipboard starts empty and ends with the server: nothing COMMAND copies reaches the
shared desktop, and nothing copied there reaches COMMAND. The server stops when COMMAND ends,
when this wrapper is stopped (TERM, INT and HUP are passed on to COMMAND) and when the wrapper
dies without cleaning up (the kernel sends the server TERM: `setpriv --pdeathsig`).

--gpu runs COMMAND through VirtualGL (`vglrun -d egl`): it draws on the GPU and shows its frames
on the private display, which has no GPU of its own (OpenGL there is software rendering).

The wrapper exits with COMMAND's status, 128+N when signal N ended it.
"""
import argparse
import os
import shutil
import signal
import subprocess
import sys

# What a run on the private display must not see: the desktop's Wayland compositor (winit and the
# clipboard would use it instead of DISPLAY) and the desktop X server's authorization file.
DESKTOP_VARIABLES = ('WAYLAND_DISPLAY', 'WAYLAND_SOCKET', 'XAUTHORITY')
GPU_PREFIX = ('vglrun', '-d', 'egl')


class Display:
    """A private Xvfb; the server picks a free display number itself and writes it to a pipe."""

    def __init__(self, screen):
        read, write = os.pipe()
        # The server goes when its creator does, even when the creator is killed outright.
        guard = ['setpriv', '--pdeathsig', 'TERM'] if shutil.which('setpriv') else []
        self.server = subprocess.Popen([*guard, 'Xvfb', '-displayfd', str(write), '-screen', '0', screen,
                                        '-nolisten', 'tcp'], pass_fds=(write,),
                                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        os.close(write)
        number = b''
        with os.fdopen(read, 'rb') as handle:
            while not number.endswith(b'\n'):
                chunk = handle.read(1)
                if not chunk:
                    self.close()
                    raise RuntimeError('Xvfb did not start')
                number += chunk
        self.name = ':'+number.decode().strip()

    def close(self):
        if self.server.poll() is None:
            self.server.terminate()
            try:
                self.server.wait(10)
            except subprocess.TimeoutExpired:
                self.server.kill()
                self.server.wait()


def environment(base, display):
    """`base` for a run on `display`: X11 only, the private server's DISPLAY."""
    env = {key: value for key, value in base.items() if key not in DESKTOP_VARIABLES}
    env.update(DISPLAY=display, XDG_SESSION_TYPE='x11', WINIT_UNIX_BACKEND='x11')
    return env


def run(command, *, screen='1280x720x24', gpu=False, base=None):
    """Runs `command` on a private display; returns its exit status (128+N for signal N)."""
    display = Display(screen)
    stopping = []
    child = None

    def forward(number, frame):
        stopping.append(number)
        if child is not None and child.poll() is None:
            child.send_signal(number)
    handled = (signal.SIGTERM, signal.SIGINT, signal.SIGHUP)
    previous = {number: signal.signal(number, forward) for number in handled}
    try:
        if not stopping:
            child = subprocess.Popen([*(GPU_PREFIX if gpu else ()), *command],
                                     env=environment(os.environ if base is None else base, display.name))
            if stopping:
                child.send_signal(stopping[0])
            code = child.wait()
        else:
            code = -stopping[0]
    finally:
        display.close()
        for number, handler in previous.items():
            signal.signal(number, handler)
    return 128-code if code < 0 else code


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--screen', default='1280x720x24', help="Xvfb's screen 0 (WIDTHxHEIGHTxDEPTH)")
    parser.add_argument('--gpu', action='store_true', help='draw on the GPU through VirtualGL')
    parser.add_argument('command', nargs=argparse.REMAINDER)
    args = parser.parse_args(argv)
    command = args.command[1:] if args.command[:1] == ['--'] else args.command
    if not command:
        parser.error('name the command to run after --')
    return run(command, screen=args.screen, gpu=args.gpu)


if __name__ == '__main__':
    sys.exit(main())
