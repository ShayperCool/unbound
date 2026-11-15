import json
from pathlib import Path
import struct
import tempfile
import unittest

import run


class CompletenessTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.directory = Path(self.temporary.name)
        self.rows = [{'k': key, 'v': True if key == 'complete' else 1} for key in run.KEYS]
        for key in run.KEYS:
            if key.endswith('.pixels'):
                (self.directory/(key.removesuffix('.pixels')+'.rgba')).write_bytes(struct.pack('<4f', .5, .5, .5, 1))
        self.write()

    def tearDown(self):
        self.temporary.cleanup()

    def write(self):
        (self.directory/'values.jsonl').write_text(''.join(json.dumps(row)+'\n' for row in self.rows))

    def test_complete_capture_and_independent_image_count(self):
        self.assertIsNone(run.complete(self.directory))
        self.rows[0]['v'] = 2
        self.write()
        self.assertIsNotNone(run.complete(self.directory))

    def test_missing_extra_duplicate_and_reordered_records_are_refused(self):
        original = self.rows.copy()
        for rows in (original[:-1], original + [{'k': 'extra', 'v': 1}], original + original[:1], original[1:] + original[:1]):
            self.rows = rows
            self.write()
            self.assertIsNotNone(run.complete(self.directory))

    def test_missing_extra_and_nonfinite_images_are_refused(self):
        image = next(self.directory.glob('*.rgba'))
        image.write_bytes(struct.pack('<4f', float('nan'), 0, 0, 1))
        self.assertIsNotNone(run.complete(self.directory))
        image.unlink()
        self.assertIsNotNone(run.complete(self.directory))
        image.write_bytes(struct.pack('<4f', 0, 0, 0, 1))
        (self.directory/'unexpected.rgba').write_bytes(image.read_bytes())
        self.assertIsNotNone(run.complete(self.directory))


if __name__ == '__main__':
    unittest.main()
