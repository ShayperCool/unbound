"""Bounded process controls for benchmark failure, deadline and cancellation."""
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest

import build_benchmark as bench


class BenchmarkControlTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def job(self, name, code, **extra):
        return dict(name=name, cwd=str(self.root), resource_key=str(self.root/name),
                    command=[sys.executable, '-c', code], **extra)

    def start(self, jobs):
        plan = self.root/'plan.json'
        plan.write_text(json.dumps({'cases': [dict(name='case', parallelism=1, jobs=jobs)]}))
        log = (self.root/'driver.log').open('w')
        self.addCleanup(log.close)
        return subprocess.Popen([sys.executable, str(Path(bench.__file__).resolve()),
                                 '--plan', str(plan), '--out', str(self.root/'out')],
                                stdout=log, stderr=subprocess.STDOUT)

    def test_shared_outputs_are_rejected_before_launch(self):
        a = self.job('a', 'pass')
        b = dict(a, name='b')
        with self.assertRaisesRegex(AssertionError, 'share build outputs'):
            bench.validate(dict(name='case', parallelism=2, jobs=[a, b]))

    def running(self, pid):
        try:
            stat = Path('/proc', str(pid), 'stat').read_text()
        except FileNotFoundError:
            return False
        # A process awaiting its OS reaper owns no running payload or scope memory.
        return stat[stat.rindex(')')+2:].split()[0] not in ('Z', 'X')

    def test_failed_serial_build_does_not_start_the_next(self):
        flag = self.root/'unexpected'
        process = self.start([self.job('first', 'raise SystemExit(7)'),
                              self.job('second', f'from pathlib import Path;Path({str(flag)!r}).touch()')])
        self.assertEqual(process.wait(15), 1)
        self.assertFalse(flag.exists())
        report = json.loads((self.root/'out/case/result.json').read_text())
        self.assertEqual(len(report['jobs']), 1)
        self.assertEqual(report['jobs'][0]['launcher_exit'], 7)

    def test_build_deadline(self):
        process = self.start([self.job('slow', 'import time;time.sleep(30)', timeout_s=.3)])
        self.assertEqual(process.wait(15), 1)
        report = json.loads((self.root/'out/case/result.json').read_text())
        self.assertEqual(report['jobs'][0]['launcher_exit'], 124)

    def test_cancel_stops_the_owned_build_scope(self):
        pidfile = self.root/'payload.pid'
        process = self.start([self.job('slow', 'import os,time;from pathlib import Path;'
                                      f'Path({str(pidfile)!r}).write_text(str(os.getpid()));time.sleep(30)')])
        try:
            deadline = time.monotonic()+10
            while not pidfile.exists() and time.monotonic() < deadline:
                time.sleep(.05)
            self.assertTrue(pidfile.exists(), (self.root/'driver.log').read_text())
            pid = int(pidfile.read_text())
            process.send_signal(signal.SIGTERM)
            self.assertNotEqual(process.wait(15), 0)
            deadline = time.monotonic()+3
            while self.running(pid) and time.monotonic() < deadline:
                time.sleep(.05)
            self.assertFalse(self.running(pid))
            self.assertTrue((self.root/'out/case/cancelled.json').exists())
        finally:
            if process.poll() is None:
                process.send_signal(signal.SIGTERM)
                process.wait(15)


if __name__ == '__main__':
    unittest.main()
