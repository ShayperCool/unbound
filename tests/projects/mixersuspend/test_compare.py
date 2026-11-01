"""The parity report must retain failures, including missing and extra samples."""
import gzip
import json
from pathlib import Path
import struct
import tempfile
import unittest
from compare import compare


class ReportTests(unittest.TestCase):
    def test_complete_union_and_exact_repeats(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name, values in [('native', [0., 1., 2., 3.]),
                                 ('before', [0., 2., 4., 3.]),
                                 ('after', [1e-6, 1., 4., 5., 6.])]:
                target = root/name
                target.mkdir()
                (target/'capture.json').write_text(json.dumps({'signal': {
                    'file': 'signal.f32', 'frames': 4, 'channels': 1}}))
                (target/'signal.f32.gz').write_bytes(gzip.compress(
                    struct.pack('<'+'f'*len(values), *values), mtime=0))
            report = root/'union.jsonl.gz'
            self.assertEqual(compare(root/'native', root/'after', report,
                                     before=root/'before'), 2)
            rows = [json.loads(line) for line in gzip.open(report, 'rt')]
            self.assertEqual([(r['sample'], r['status']) for r in rows[:-1]],
                             [(1, 'resolved'), (2, 'retained'), (3, 'introduced'),
                              (4, 'introduced')])
            self.assertEqual(rows[-2]['native'], '<missing>')
            self.assertEqual(rows[-1]['summary']['tolerated_changed'], 1)
            exact = root/'exact.jsonl.gz'
            self.assertEqual(compare(root/'native', root/'after', exact, exact=True), 4)
            exact_rows = [json.loads(line) for line in gzip.open(exact, 'rt')]
            self.assertEqual([r['sample'] for r in exact_rows[:-1]], [0, 2, 3, 4])


if __name__ == '__main__':
    unittest.main()
