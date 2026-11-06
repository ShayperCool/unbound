"""The landing gate admits only the task-required invalid Append skip."""
import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest
import run


class SafeAppendGateTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(dir=run.ROOT/'Library'/'Unbound')
        self.addCleanup(self.directory.cleanup)
        self.out = Path(self.directory.name)
        self.rows = [json.loads(line) for line in run.reference_file(run.ROOT, 'jsonl').read_text().splitlines()]
        self.rows = [row for row in self.rows if row['k'] != 'uav.append.nocounter.log']
        next(row for row in self.rows if row['k'] == 'uav.append.nocounter')['v'] = [0xAAAA0000] * 48
        (self.out/'engine.log').write_text('shader uses the counter of buffer `_App`, which is not set or not an Append or Counter buffer: the draw is skipped')

    def verify(self, allow=True):
        candidate = self.out/'values.jsonl'
        candidate.write_text(''.join(json.dumps(row)+'\n' for row in self.rows))
        return run.verify(candidate, safe_append_skip=allow)

    def test_safe_skip_is_separate_from_strict_verdict(self):
        self.assertNotEqual(self.verify(allow=False), 0)
        self.assertEqual(self.verify(), 0)

    def test_payload_write_still_fails(self):
        next(row for row in self.rows if row['k'] == 'uav.append.nocounter')['v'][0] = 1
        self.assertNotEqual(self.verify(), 0)

    def test_missing_warning_still_fails(self):
        (self.out/'engine.log').write_text('')
        self.assertNotEqual(self.verify(), 0)

    def test_unrelated_missing_value_still_fails(self):
        self.rows = [row for row in self.rows if row['k'] != 'cmd.instanced.blockscalar']
        self.assertNotEqual(self.verify(), 0)

    def test_unrelated_extra_value_still_fails(self):
        self.rows.append({'k': 'unexpected', 'v': 123})
        self.assertNotEqual(self.verify(), 0)


if __name__ == '__main__':
    unittest.main()
