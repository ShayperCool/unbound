"""Capture-session provenance must remain an independently compared header value."""
import array
import gzip
import json
from pathlib import Path
import tempfile
import unittest
from compare import compare


class CaptureContextTests(unittest.TestCase):
    def test_equal_audio_does_not_hide_a_different_or_missing_capture_session(self):
        for context in ({'other': ['signal']}, None):
            with self.subTest(context=context), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                for name in ('reference', 'candidate'):
                    target = root/name
                    target.mkdir()
                    meta = dict(release='6000.0.71f1', rate=48000)
                    for suffix in ('', '.master'):
                        file = 'signal'+suffix+'.f32'
                        (target/file).write_bytes(array.array('f', [0.25, -0.125]).tobytes())
                        meta['signal'+suffix] = dict(file=file, frames=1, channels=2)
                    if name == 'reference':
                        meta['sessions'] = {'kernel': ['signal']}
                    elif context is not None:
                        meta['sessions'] = context
                    (target/'capture.json').write_text(json.dumps(meta))
                report = root/'report.jsonl.gz'
                self.assertEqual(compare(root/'reference', root/'candidate', report, {'signal': None}), 1)
                with gzip.open(report, 'rt') as stream:
                    failures = [row for line in stream if 'summary' not in (row := json.loads(line))]
                self.assertEqual([row['key'] for row in failures], ['sessions'])


if __name__ == '__main__':
    unittest.main()
