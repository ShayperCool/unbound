"""The optional fast path must retain the scalar comparator's entire report and counts."""
import array
import gzip
import json
import os
from pathlib import Path
import tempfile
import unittest
import compare


class FastComparisonTests(unittest.TestCase):
    def test_every_failure_and_summary_matches_scalar_including_missing_and_nonfinite(self):
        if compare.np is None:
            self.skipTest('optional numpy fast path is unavailable')
        cache = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/audioflange'
        cache.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=cache) as work:
            work = Path(work)
            roots = [work/'reference', work/'candidate']
            for root, data in zip(roots, [[0.0, -0.0, 0.5, -0.25, float('nan'), float('inf'), 0.0],
                                          [-0.0, 0.0, 0.500121, -0.250071, float('nan'), float('inf'), 1/32768]]):
                root.mkdir()
                meta = dict(release='6000.0.71f1', rate=48000)
                for suffix in ('', '.master'):
                    name = 'case'+suffix
                    array.array('f', data).tofile((root/(name+'.f32')).open('wb'))
                    meta[name] = dict(file=name+'.f32', frames=3, channels=2)
                meta['missing' if root == roots[0] else 'extra'] = dict(file='case.f32', frames=3, channels=2)
                if root == roots[1]: meta['unexpected_scalar'] = 123
                (root/'capture.json').write_text(json.dumps(meta))
            numpy = compare.np
            try:
                for exact in (False, True):
                    outputs = []
                    for fast in (False, True):
                        compare.np = numpy if fast else None
                        report = work/f'{exact}-{fast}.jsonl.gz'
                        compare.compare(*roots, report, dict(case=None, missing=None, extra=None),
                                        exact=exact, pcm_steps=1)
                        rows = list(map(json.loads, gzip.open(report, 'rt')))
                        outputs.append(rows)
                    self.assertEqual(*outputs)
                    self.assertGreater(outputs[0][-1]['summary']['failed'], 0)
            finally:
                compare.np = numpy


if __name__ == '__main__':
    unittest.main()
