"""Failure cases for the resource probe's comparison command."""
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
        self.ref.mkdir()
        self.cand.mkdir()

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
        (directory / 'sample.png').write_bytes(data)

    def values(self, directory, value):
        (directory / 'values.jsonl').write_text(json.dumps({'k': 'sample', 'v': value}) + '\n')

    def test_missing_image_fails_and_is_counted(self):
        self.png(self.ref)
        result = self.compare()
        self.assertEqual(result.returncode, 1)
        self.assertIn('0/1 images', result.stdout)
        self.assertIn('MISSING', result.stdout)

    def test_wrong_dimensions_fail_and_are_counted(self):
        self.png(self.ref)
        self.png(self.cand, width=2)
        result = self.compare()
        self.assertEqual(result.returncode, 1)
        self.assertIn('0/1 images', result.stdout)

    def test_pixels_outside_tolerance_fail(self):
        self.png(self.ref)
        self.png(self.cand, rgb=(255, 0, 0))
        self.assertEqual(self.compare().returncode, 1)

    def test_missing_or_different_values_fail(self):
        self.values(self.ref, [1, 'texture'])
        self.assertEqual(self.compare().returncode, 1)
        self.values(self.cand, [1, 'other texture'])
        self.assertEqual(self.compare().returncode, 1)

    def test_matching_values_and_images_pass(self):
        self.values(self.ref, [1, 'texture'])
        self.values(self.cand, [1.001, 'texture'])
        self.png(self.ref)
        self.png(self.cand)
        self.assertEqual(self.compare().returncode, 0)

    def test_empty_reference_fails(self):
        self.assertEqual(self.compare().returncode, 1)


if __name__ == '__main__':
    unittest.main()
