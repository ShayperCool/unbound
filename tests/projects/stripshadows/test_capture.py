import gzip
import json
from pathlib import Path
import struct
import tempfile
import unittest

import run


class CompletenessTests(unittest.TestCase):
    def setUp(self):
        run.select_cascades(False)
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


class CascadeReferenceTests(unittest.TestCase):
    def tearDown(self):
        run.select_cascades(False)

    def test_diagnostic_controls_are_opt_in_and_do_not_change_base_keys(self):
        run.select_cascades(False)
        self.assertEqual(run.BASE_KEYS, run.KEYS)
        self.assertEqual(145, len(run.KEYS))
        run.select_cascades(True)
        self.assertEqual(217, len(run.KEYS))
        self.assertEqual(run.BASE_KEYS, [key for key in run.KEYS if key in run.BASE_KEYS])
        self.assertEqual(run.CASCADE_KEYS, [key for key in run.KEYS if key not in run.BASE_KEYS])

    def test_extension_preserves_raw_rows_order_and_image_bytes(self):
        with tempfile.TemporaryDirectory() as temporary:
            old, new = (Path(temporary)/name for name in ('old', 'new'))
            old.mkdir(); new.mkdir()
            a = b'{"k":"a","v":1}\n'
            b = b'{"k":"complete","v":true}\n'
            extra = b'{"k":"new","v":2}\n'
            (old/'values.jsonl').write_bytes(a+b)
            (new/'values.jsonl').write_bytes(a+extra+b)
            (old/'a.rgba.gz').write_bytes(gzip.compress(b'original', mtime=0))
            (new/'a.rgba.gz').write_bytes(gzip.compress(b'original', mtime=1))
            original = {p.name:p.read_bytes() for p in old.iterdir()}
            self.assertEqual(extra, run.validate_extension(old, new))
            for invalid in (b+extra+a, a, a.replace(b'1', b'9')+extra+b, a+a+extra+b):
                (new/'values.jsonl').write_bytes(invalid)
                with self.assertRaises(RuntimeError): run.validate_extension(old, new)
            (new/'values.jsonl').write_bytes(a+extra+b)
            (new/'a.rgba.gz').write_bytes(gzip.compress(b'changed'))
            with self.assertRaises(RuntimeError): run.validate_extension(old, new)
            (new/'a.rgba.gz').unlink()
            with self.assertRaises(RuntimeError): run.validate_extension(old, new)
            self.assertEqual(original, {p.name:p.read_bytes() for p in old.iterdir()})


if __name__ == '__main__':
    unittest.main()
