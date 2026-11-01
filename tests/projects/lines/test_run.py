"""Verify that appending to the line reference compares and writes raw record bytes.

The completeness and Editor checks are covered by the shared runner; they are stubbed here so
each control isolates the byte guard. Every refusal must leave the reference byte-unchanged.
"""
import gzip
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('lines_run', Path(__file__).with_name('run.py'))
run = importlib.util.module_from_spec(spec)
spec.loader.exec_module(run)

OLD = [{'k': 'line.a', 'v': 1}, {'k': 'line.b', 'v': [0.5, 2]}]
NEW = [{'k': 'blend.x', 'v': 3}, {'k': 'blend.y', 'v': 4}]
COMPLETE = {'k': 'complete', 'v': True}
IMAGE = bytes(range(64))


def record(row, end=b'\n'):
    return json.dumps(row, separators=(',', ':')).encode() + end


class AppendTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.work = root/'work'
        self.reference = root/'Reference'/'unity-test'
        self.reference.mkdir(parents=True)
        (self.reference/'a.rgba.gz').write_bytes(gzip.compress(IMAGE))
        for name, value in (('reference_root', lambda probe: self.reference),
                            ('editor_problem', lambda repeat: None),
                            ('capture_problem', lambda repeat: None)):
            self.addCleanup(setattr, run, name, getattr(run, name))
            setattr(run, name, value)

    def write_reference(self, data):
        (self.reference/'values.jsonl').write_bytes(data)
        self.before = data

    def capture(self, data):
        for repeat in run.REPEATS:
            d = self.work/repeat
            d.mkdir(parents=True, exist_ok=True)
            (d/'values.jsonl').write_bytes(data)
            (d/'a.rgba').write_bytes(IMAGE)

    def refused(self, reason):
        with self.assertRaises(SystemExit) as raised:
            run.publish_append(self.work, 'blend.')
        self.assertIn(reason, str(raised.exception))
        self.assertEqual((self.reference/'values.jsonl').read_bytes(), self.before)
        self.assertEqual(gzip.decompress((self.reference/'a.rgba.gz').read_bytes()), IMAGE)

    def test_matching_captures_append_their_new_record_bytes(self):
        self.write_reference(b''.join(record(r) for r in OLD + [COMPLETE]))
        self.capture(record(OLD[0]) + record(NEW[0]) + record(OLD[1]) + record(NEW[1]) + record(COMPLETE))
        self.assertEqual(run.publish_append(self.work, 'blend.'), 0)
        self.assertEqual((self.reference/'values.jsonl').read_bytes(), self.before + record(NEW[0]) + record(NEW[1]))

    def test_crlf_records_are_kept_as_they_are(self):
        self.write_reference(b''.join(record(r, b'\r\n') for r in OLD + [COMPLETE]))
        self.capture(b''.join(record(r, b'\r\n') for r in OLD + NEW + [COMPLETE]))
        self.assertEqual(run.publish_append(self.work, 'blend.'), 0)
        self.assertEqual((self.reference/'values.jsonl').read_bytes(),
                         self.before + record(NEW[0], b'\r\n') + record(NEW[1], b'\r\n'))

    def test_a_crlf_reference_record_is_not_reproduced_by_lf_captures(self):
        self.write_reference(record(OLD[0], b'\r\n') + record(OLD[1]) + record(COMPLETE))
        self.capture(b''.join(record(r) for r in OLD + NEW + [COMPLETE]))
        self.refused('do not reproduce the reference (first difference at line 1)')

    def test_a_crlf_captured_record_does_not_reproduce_an_lf_reference(self):
        self.write_reference(b''.join(record(r) for r in OLD + [COMPLETE]))
        self.capture(record(OLD[0]) + record(OLD[1], b'\r\n') + b''.join(record(r) for r in NEW + [COMPLETE]))
        self.refused('do not reproduce the reference (first difference at line 2)')

    def test_a_captured_last_record_without_a_newline_is_refused(self):
        self.write_reference(b''.join(record(r) for r in OLD + [COMPLETE]))
        self.capture(b''.join(record(r) for r in OLD + NEW) + record(COMPLETE, b''))
        self.refused('no newline terminator')

    def test_a_new_record_without_a_newline_is_refused(self):
        self.write_reference(b''.join(record(r) for r in OLD))
        self.capture(b''.join(record(r) for r in OLD) + record(NEW[0]) + record(NEW[1], b''))
        self.refused('no newline terminator')

    def test_a_reference_without_a_final_newline_is_refused(self):
        self.write_reference(record(OLD[0]) + record(OLD[1], b''))
        self.capture(b''.join(record(r) for r in OLD + NEW))
        self.refused('does not end with a newline')

    def test_a_second_append_is_refused(self):
        self.write_reference(b''.join(record(r) for r in OLD + NEW))
        self.capture(b''.join(record(r) for r in OLD + NEW))
        self.refused('already holds blend. keys')


if __name__ == '__main__':
    unittest.main()
