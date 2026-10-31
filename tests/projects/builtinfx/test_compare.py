"""Verify that the built-in rendering comparator rejects actual regressions."""
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
import zlib


class CompareTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.ref = Path(self.temp.name) / 'reference'
        self.cand = Path(self.temp.name) / 'candidate'
        for directory in (self.ref, self.cand):
            directory.mkdir()
            (directory / 'values.jsonl').write_text(json.dumps({'k': 'sample', 'v': 1}) + '\n')

    def compare(self):
        return subprocess.run(
            [sys.executable, str(Path(__file__).with_name('compare.py')), str(self.ref), str(self.cand), '-q'],
            capture_output=True, text=True,
        )

    def png(self, directory, width=1, rgb=(0, 0, 0)):
        def chunk(kind, data):
            return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
        data = b'\x89PNG\r\n\x1a\n'
        data += chunk(b'IHDR', struct.pack('>IIBBBBB', width, 1, 8, 2, 0, 0, 0))
        data += chunk(b'IDAT', zlib.compress(b'\0' + bytes(rgb) * width))
        data += chunk(b'IEND', b'')
        # This formerly skipped case now matches Native and must be checked normally.
        (directory / 'hdr_half_target.png').write_bytes(data)

    def test_matching_values_and_image_pass(self):
        self.png(self.ref)
        self.png(self.cand)
        self.assertEqual(self.compare().returncode, 0)

    def test_different_or_missing_value_fails(self):
        (self.cand / 'values.jsonl').write_text(json.dumps({'k': 'sample', 'v': 2}) + '\n')
        self.assertEqual(self.compare().returncode, 1)
        (self.cand / 'values.jsonl').unlink()
        self.assertEqual(self.compare().returncode, 1)

    def test_empty_reference_fails(self):
        (self.ref / 'values.jsonl').write_text('')
        self.assertEqual(self.compare().returncode, 1)

    def test_pixel_mismatch_fails(self):
        self.png(self.ref)
        self.png(self.cand, rgb=(255, 0, 0))
        self.assertEqual(self.compare().returncode, 1)

    def test_missing_image_fails_and_is_counted(self):
        self.png(self.ref)
        result = self.compare()
        self.assertEqual(result.returncode, 1)
        self.assertIn('0/1 images', result.stdout)

    def test_wrong_dimensions_fail_and_are_counted(self):
        self.png(self.ref)
        self.png(self.cand, width=2)
        result = self.compare()
        self.assertEqual(result.returncode, 1)
        self.assertIn('0/1 images', result.stdout)

    def test_existing_expected_value_remains_reported(self):
        for directory, value in [(self.ref, 1), (self.cand, 2)]:
            (directory / 'values.jsonl').write_text(json.dumps({'k': 'material.crc.values', 'v': value}) + '\n')
        result = self.compare()
        self.assertEqual(result.returncode, 0)
        self.assertIn('1 expected', result.stdout)


if __name__ == '__main__':
    unittest.main()
