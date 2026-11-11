"""fonts.py: copies the two fonts only when they are the reference's (`python3 -m unittest` here)."""
import gzip
import hashlib
import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import fonts


class FontsTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.project = self.root/'project'
        folder = self.project/'Assets/Fonts'
        folder.mkdir(parents=True)
        for name in ('LiberationSans-Regular', 'Inter-Regular'):
            (folder/f'{name}.ttf.meta').write_text('fileFormatVersion: 2\n')
        self.liberation = self.root/'os'/'LiberationSans-Regular.ttf'
        self.inter = self.root/'editor'/'Inter-Regular.ttf'
        for path, data in ((self.liberation, b'liberation'), (self.inter, b'inter')):
            path.parent.mkdir()
            path.write_bytes(data)
        self.reference = self.root/'unity.jsonl.gz'
        self.write_reference({'LiberationSans-Regular': b'liberation', 'Inter-Regular': b'inter'})
        patch = mock.patch.dict(os.environ, {'TEXTCORE_LIBERATION_SANS': str(self.liberation),
                                             'TEXTCORE_INTER': str(self.inter)})
        patch.start()
        self.addCleanup(patch.stop)
        self.addCleanup(self.temp.cleanup)

    def write_reference(self, fonts_data):
        with gzip.open(self.reference, 'wt', encoding='utf-8') as out:
            out.write('{"type":"cold","font":0}\n')
            for index, (name, data) in enumerate(fonts_data.items()):
                record = {'type': 'font', 'font': index, 'name': name, 'sha256': hashlib.sha256(data).hexdigest()}
                out.write(json.dumps(record, separators=(',', ':'))+'\n')

    def installed(self, name):
        return self.project/'Assets/Fonts'/f'{name}.ttf'

    def test_copies_fonts_that_match_the_reference(self):
        self.assertEqual(fonts.install(self.project, self.reference), [])
        self.assertEqual(self.installed('LiberationSans-Regular').read_bytes(), b'liberation')
        self.assertEqual(self.installed('Inter-Regular').read_bytes(), b'inter')

    def test_replaces_a_stale_copy(self):
        self.installed('Inter-Regular').write_bytes(b'older build of the font')
        self.assertEqual(fonts.install(self.project, self.reference), [])
        self.assertEqual(self.installed('Inter-Regular').read_bytes(), b'inter')

    def test_refuses_a_font_the_reference_was_not_recorded_with(self):
        self.inter.write_bytes(b'another release of inter')
        errors = fonts.install(self.project, self.reference)
        self.assertEqual(len(errors), 1)
        self.assertIn('Inter-Regular', errors[0])
        self.assertIn('SHA-256', errors[0])
        self.assertFalse(self.installed('Inter-Regular').exists())
        self.assertTrue(self.installed('LiberationSans-Regular').exists())

    def test_reports_a_missing_font_with_its_path(self):
        self.liberation.unlink()
        errors = fonts.install(self.project, self.reference)
        self.assertEqual(len(errors), 1)
        self.assertIn(str(self.liberation), errors[0])
        self.assertFalse(self.installed('LiberationSans-Regular').exists())

    def test_refuses_another_project(self):
        (self.project/'Assets/Fonts/Inter-Regular.ttf.meta').unlink()
        errors = fonts.install(self.project, self.reference)
        self.assertEqual(len(errors), 1)
        self.assertIn('Inter-Regular.ttf.meta', errors[0])

    def test_reports_a_reference_without_the_font(self):
        self.write_reference({'LiberationSans-Regular': b'liberation'})
        errors = fonts.install(self.project, self.reference)
        self.assertEqual(len(errors), 1)
        self.assertIn('no font record named Inter-Regular', errors[0])


if __name__ == '__main__':
    unittest.main()
