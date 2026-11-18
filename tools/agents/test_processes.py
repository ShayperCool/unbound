#!/usr/bin/env python3
"""The macOS process reader (processes.py): parsing runs anywhere, live reading on macOS."""
import errno
import os
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import processes
import janitor


def procargs_area(executable, arguments, environment):
    """A KERN_PROCARGS2 area: argc, the executable path and its padding, arguments, environment."""
    return (struct.pack('i', len(arguments)) + executable + b'\0\0\0'
            + b''.join(a + b'\0' for a in arguments) + b''.join(e + b'\0' for e in environment) + b'\0\0')


class ParsingTests(unittest.TestCase):
    def test_lsof_records_give_each_process_its_cwd_executable_and_files(self):
        output = '\n'.join([
            'p10', 'fcwd', 'n/work/run', 'ftxt', 'n/usr/bin/python3', 'ftxt', 'n/usr/lib/libz.dylib',
            'f3', 'n/work/run/out.log', 'f4', 'n127.0.0.1:80->127.0.0.1:5000', 'f5', 'n/dev/null',
            'p11', 'fcwd', 'n/', 'ftxt', 'n/bin/sleep',
        ])
        found = processes.parse_lsof(output)
        self.assertEqual(sorted(found), [10, 11])
        self.assertEqual(found[10].cwd, Path('/work/run'))
        self.assertEqual(found[10].executable, Path('/usr/bin/python3'))
        # Sockets and other names that are not paths are left out.
        self.assertEqual(found[10].files, [Path(p) for p in (
            '/work/run', '/usr/bin/python3', '/usr/lib/libz.dylib', '/work/run/out.log', '/dev/null')])
        self.assertEqual(found[11].executable, Path('/bin/sleep'))

    def test_procargs_split_arguments_from_the_environment(self):
        raw = procargs_area(b'/usr/bin/python3', [b'python3', b'-c', b'pass', b''],
                            [b'UNBOUND_RUNTIME=/cache/snapshots/abc', b'HOME=/Users/me'])
        arguments, environment = processes.parse_procargs(raw)
        # An empty argument stays one; the environment ends at the padding.
        self.assertEqual(arguments, [b'python3', b'-c', b'pass', b''])
        self.assertEqual(environment, [b'UNBOUND_RUNTIME=/cache/snapshots/abc', b'HOME=/Users/me'])
        with self.assertRaises(ValueError):
            processes.parse_procargs(b'\0')


class FailClosedTests(unittest.TestCase):
    def listing(self, pids, lsof):
        def run(command, *args, **kwargs):
            if command[0] == 'ps' and '-ax' in command:
                # Another user's process (a setuid one) is not listed as the user's.
                listing = ''.join(f'{p} 501\n' for p in pids) + '99 0\n'
                return subprocess.CompletedProcess(command, 0, listing, '')
            if command[0] == 'lsof':
                return subprocess.CompletedProcess(command, 1, lsof, '')
            raise AssertionError(command)
        return mock.patch.object(processes.subprocess, 'run', side_effect=run)

    def test_a_process_that_exits_meanwhile_is_skipped(self):
        with self.listing([10, 11], 'p10\nfcwd\nn/work\n'), \
                mock.patch.object(processes, 'procargs', return_value=([b'a'], [])), \
                mock.patch.object(processes, 'alive', return_value=False):
            self.assertEqual([p.pid for p in processes.darwin_processes(501)], [10])

    def test_a_live_process_that_cannot_be_read_is_an_error(self):
        unreadable = OSError(errno.EPERM, 'Operation not permitted')
        with self.listing([10], 'p10\nfcwd\nn/work\n'), \
                mock.patch.object(processes, 'procargs', side_effect=unreadable), \
                mock.patch.object(processes, 'alive', return_value=True):
            with self.assertRaisesRegex(OSError, 'cannot inspect live process 10'):
                processes.darwin_processes(501)
        # Missing from lsof's listing while alive: also an error.
        with self.listing([10, 12], 'p10\nfcwd\nn/work\n'), \
                mock.patch.object(processes, 'procargs', return_value=([], [])), \
                mock.patch.object(processes, 'alive', return_value=True):
            with self.assertRaisesRegex(OSError, 'cannot inspect live process 12'):
                processes.darwin_processes(501)

    def test_janitor_paths_cover_arguments_environment_cwd_and_files(self):
        process = processes.Process(10, cwd=Path('/work/run'), executable=Path('/usr/bin/python3'),
                                    files=[Path('/work/run/out.log')],
                                    arguments=[b'python3', b'tools/check.py', b'--flag'],
                                    environment=[b'UNBOUND_CLI=/cache/builds/unbound', b'PATH=/a/bin:/b/bin'])
        with mock.patch.object(processes, 'darwin_processes', return_value=[process]):
            found = janitor.darwin_process_paths(501)
        for path in ('/work/run', '/usr/bin/python3', '/work/run/out.log', '/work/run/tools/check.py',
                     '/cache/builds/unbound', '/a/bin', '/b/bin'):
            self.assertIn(Path(path).resolve(), found)
        self.assertNotIn(Path('/work/run/--flag').resolve(), found)


@unittest.skipUnless(processes.MACOS, 'reads live processes on macOS')
class LiveTests(unittest.TestCase):
    def test_a_child_is_read_with_its_cwd_open_file_arguments_and_environment(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            held = root/'held.txt'
            held.write_text('x')
            script = 'import sys; f=open(sys.argv[1]); print("ready", flush=True); sys.stdin.read()'
            child = subprocess.Popen([sys.executable, '-c', script, str(held)], cwd=root,
                                     env=dict(os.environ, UNBOUND_RUNTIME=str(root/'runtime')),
                                     stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True)
            try:
                self.assertEqual(child.stdout.readline().strip(), 'ready')
                found = {p.pid: p for p in processes.darwin_processes(os.getuid())}
                process = found[child.pid]
                self.assertEqual(process.cwd.resolve(), root)
                self.assertIn(held, [p.resolve() for p in process.files])
                self.assertIn(str(held).encode(), process.arguments)
                self.assertIn(f'UNBOUND_RUNTIME={root/"runtime"}'.encode(), process.environment)
                start = processes.start_time(child.pid)
                self.assertTrue(start)
                self.assertEqual(start, processes.start_time(child.pid))
            finally:
                child.communicate(input='', timeout=5)
            self.assertIsNone(processes.start_time(child.pid))


if __name__ == '__main__':
    unittest.main()
