#!/usr/bin/env python3
"""Controls for independent audio-context captures and deterministic merging."""
import contextlib
import importlib.util
import io
import json
import os
import signal
import time
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

FIXTURE = Path(__file__).resolve().parents[2]/'tests/projects/audiopitch'
sys.path.insert(0, str(FIXTURE))
spec = importlib.util.spec_from_file_location('audio_context_run', FIXTURE/'context_run.py')
context = importlib.util.module_from_spec(spec)
spec.loader.exec_module(context)


class CapturePoolTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)

    def assert_exited(self, pid):
        result = subprocess.run(['ps', '-p', str(pid), '-o', 'stat='],
                                capture_output=True, text=True)
        self.assertTrue(result.returncode == 1 or result.stdout.strip().startswith('Z'),
                        f'process {pid} is still running: {result.stdout}')

    def capture(self, name, source):
        return ([sys.executable, '-c', source, str(self.root), name], self.root/(name+'.log'))

    def run_captures(self, captures, jobs):
        with contextlib.redirect_stdout(io.StringIO()):
            context.run_captures(captures, jobs)

    def test_independent_captures_overlap_and_keep_the_worker_bound(self):
        source = '''
from pathlib import Path
import sys, time
root, name = Path(sys.argv[1]), sys.argv[2]
i = int(name)
(root/(name+'.started')).touch()
if i < 2:
    deadline = time.monotonic()+5
    while not all((root/(str(k)+'.started')).exists() for k in range(2)):
        assert time.monotonic() < deadline, 'captures did not overlap'
        time.sleep(.01)
    assert not (root/'2.started').exists(), 'worker bound exceeded'
    (root/(name+'.barrier')).touch()
    while not all((root/(str(k)+'.barrier')).exists() for k in range(2)):
        time.sleep(.01)
else:
    assert all((root/(str(k)+'.finished')).exists() for k in range(2))
print('private log '+name)
(root/(name+'.finished')).touch()
'''
        self.run_captures([self.capture(str(i), source) for i in range(3)], 2)
        for i in range(3):
            self.assertTrue((self.root/(str(i)+'.finished')).exists())
            self.assertEqual((self.root/(str(i)+'.log')).read_text(), f'private log {i}\n')

    def test_one_worker_preserves_serial_order(self):
        source = '''
from pathlib import Path
import sys
root, name = Path(sys.argv[1]), sys.argv[2]
i = int(name)
assert all((root/(str(k)+'.finished')).exists() for k in range(i))
assert not any((root/(str(k)+'.started')).exists() for k in range(i+1, 4))
(root/(name+'.started')).touch()
(root/(name+'.finished')).touch()
'''
        self.run_captures([self.capture(str(i), source) for i in range(4)], 1)
        self.assertEqual(len(list(self.root.glob('*.finished'))), 4)

    def test_later_failure_stops_running_descendants_and_queued_work(self):
        sleeper = '''
from pathlib import Path
import subprocess, sys, time
root = Path(sys.argv[1])
child = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'], start_new_session=True)
(root/'child.pid').write_text(str(child.pid))
(root/'driver.pid').write_text(str(__import__('os').getpid()))
time.sleep(60)
'''
        failure = '''
from pathlib import Path
import sys, time
root = Path(sys.argv[1])
deadline = time.monotonic()+5
while not (root/'child.pid').exists():
    assert time.monotonic() < deadline
    time.sleep(.01)
raise SystemExit(7)
'''
        queued = "from pathlib import Path; import sys; (Path(sys.argv[1])/'unwanted').touch()"
        with self.assertRaises(subprocess.CalledProcessError) as raised:
            self.run_captures([self.capture('sleep', sleeper), self.capture('fail', failure),
                               self.capture('queued', queued)], 2)
        self.assertEqual(raised.exception.returncode, 7)
        self.assertFalse((self.root/'unwanted').exists())
        for name in ('driver', 'child'):
            pid = int((self.root/(name+'.pid')).read_text())
            self.assert_exited(pid)

    def test_launch_exception_cancels_an_already_running_driver(self):
        process = None
        real_popen = subprocess.Popen
        def start(command, **kwargs):
            nonlocal process
            if command[0] == '/missing-capture-driver':
                raise OSError('launch refused')
            process = real_popen(command, **kwargs)
            return process
        with mock.patch.object(context.subprocess, 'Popen', side_effect=start):
            with self.assertRaisesRegex(OSError, 'launch refused'):
                self.run_captures([self.capture('sleep', 'import time; time.sleep(60)'),
                                   (['/missing-capture-driver'], self.root/'bad.log')], 2)
        self.assertIsNotNone(process.returncode)

    def test_interrupt_cancels_the_active_driver(self):
        processes = []
        real_popen = subprocess.Popen
        def start(command, **kwargs):
            process = real_popen(command, **kwargs)
            processes.append(process)
            return process
        with mock.patch.object(context.subprocess, 'Popen', side_effect=start), \
             mock.patch.object(context, 'time') as clock:
            clock.sleep.side_effect = KeyboardInterrupt
            with self.assertRaises(KeyboardInterrupt):
                self.run_captures([self.capture('sleep', 'import time; time.sleep(60)')], 1)
        self.assertTrue(all(process.poll() is not None for process in processes))

    def test_missing_linux_apis_still_stops_detached_descendants(self):
        source = """
from pathlib import Path
import os, subprocess, sys, time
root = Path(sys.argv[1])
child = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'], start_new_session=True)
(root/'child.pid').write_text(str(child.pid))
(root/'driver.pid').write_text(str(os.getpid()))
time.sleep(60)
"""
        def interrupt(_):
            deadline = time.monotonic()+5
            while not (self.root/'child.pid').exists():
                self.assertLess(time.monotonic(), deadline)
                time.sleep(.01)
            raise KeyboardInterrupt
        with mock.patch.object(os, 'pidfd_open', None, create=True), \
             mock.patch.object(signal, 'pidfd_send_signal', None, create=True), \
             mock.patch.object(context, 'time') as clock:
            clock.sleep.side_effect = interrupt
            with self.assertRaises(KeyboardInterrupt):
                self.run_captures([self.capture('sleep', source)], 2)
        for name in ('driver', 'child'):
            pid = int((self.root/(name+'.pid')).read_text())
            self.assert_exited(pid)

    def test_invalid_worker_count_starts_nothing(self):
        with self.assertRaises(ValueError):
            self.run_captures([], 0)
        self.assertEqual(list(self.root.iterdir()), [])


class MergeTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)

    def write_capture(self, session, case, **header):
        capture = self.root/session/'capture'
        capture.mkdir(parents=True)
        data = {'release': '6000.0.71f1', 'rate': 48000, 'reset_cases': False,
                case: {'file': case+'.f32'}}
        data.update(header)
        (capture/'capture.json').write_text(json.dumps(data))
        (capture/(case+'.f32')).write_bytes(case.encode())

    def test_completion_order_does_not_change_merged_rows_or_pcm(self):
        for hardlink in (False, True):
            with self.subTest(hardlink=hardlink):
                # Finish the second declared session first.
                self.write_capture('second', 'case2')
                self.write_capture('first', 'case1')
                context.merge_sessions(self.root, {'first': [], 'second': []}, hardlink=hardlink)
                merged = self.root/'capture'
                data = json.loads((merged/'capture.json').read_text())
                self.assertEqual(list(data), ['release', 'rate', 'reset_cases', 'case1', 'case2'])
                for session, case in [('first', 'case1'), ('second', 'case2')]:
                    output = merged/(case+'.f32')
                    self.assertEqual(output.read_bytes(), case.encode())
                    self.assertEqual(output.stat().st_ino == (self.root/session/'capture'/output.name).stat().st_ino,
                                     hardlink)
                import shutil
                for directory in self.root.iterdir():
                    shutil.rmtree(directory)

    def test_metadata_disagreement_and_duplicate_cases_still_fail(self):
        self.write_capture('first', 'case1')
        self.write_capture('second', 'case2', rate=44100)
        with self.assertRaisesRegex(AssertionError, 'rate'):
            context.merge_sessions(self.root, {'first': [], 'second': []}, hardlink=False)
        import shutil
        shutil.rmtree(self.root/'capture')
        shutil.rmtree(self.root/'second')
        self.write_capture('second', 'case1')
        with self.assertRaisesRegex(AssertionError, 'case1'):
            context.merge_sessions(self.root, {'first': [], 'second': []}, hardlink=False)

    def test_single_unnamed_session_remains_in_place(self):
        self.write_capture('', 'case1')
        before = (self.root/'capture/capture.json').read_bytes()
        context.merge_sessions(self.root, {'': ['case1']}, hardlink=False)
        self.assertEqual((self.root/'capture/capture.json').read_bytes(), before)


if __name__ == '__main__':
    unittest.main()
