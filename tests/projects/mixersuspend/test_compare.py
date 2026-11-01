"""The parity report must retain failures, including missing and extra samples."""
import gzip
import json
from pathlib import Path
import struct
import tempfile
import os
from unittest.mock import patch
import unittest
from compare import compare


class ReportTests(unittest.TestCase):
    def temporary(self):
        path=Path(os.environ.get('XDG_CACHE_HOME',str(Path.home()/'.cache')))/'unbound/validation/compare-tests'
        path.mkdir(parents=True,exist_ok=True)
        return tempfile.TemporaryDirectory(dir=path)

    def test_batched_reports_expand_to_the_complete_legacy_report(self):
        with self.temporary() as directory:
            root=Path(directory)
            for name,values in [('native',[0.,1.,2.,3.]),('before',[0.,2.,4.,3.]),('after',[1e-6,1.,4.,5.])]:
                target=root/name;target.mkdir()
                (target/'capture.json').write_text(json.dumps({'signal':{'file':'signal.f32','frames':4,'channels':1}}))
                (target/'signal.f32').write_bytes(struct.pack('<4f',*values))
            def records(file):
                result=[]
                for line in gzip.open(file,'rt'):
                    row=json.loads(line)
                    if 'summary' in row:continue
                    if 'samples' not in row:result.append(row);continue
                    for i,sample in enumerate(row['samples']):
                        result.append(dict(key=row['key'],sample=sample,**{k:v[i] for k,v in row.items() if k not in ('key','samples')}))
                return result
            compare(root/'native',root/'after',root/'legacy.jsonl.gz',before=root/'before')
            compare(root/'native',root/'after',root/'batch.jsonl.gz',before=root/'before',batch=True)
            with patch.dict('sys.modules',{'numpy':None}):
                compare(root/'native',root/'after',root/'fallback.jsonl.gz',before=root/'before',batch=True)
            self.assertEqual(records(root/'legacy.jsonl.gz'),records(root/'batch.jsonl.gz'))
            self.assertEqual(records(root/'legacy.jsonl.gz'),records(root/'fallback.jsonl.gz'))
            # A crash before metadata publication must not hide the partial file's samples.
            (root/'before/capture.json').write_text('{}')
            (root/'before/signal.f32').write_bytes(struct.pack('<f',9.))
            (root/'before/extra.f32').write_bytes(struct.pack('<f',7.))
            compare(root/'native',root/'after',root/'partial.jsonl.gz',before=root/'before',batch=True)
            rows=records(root/'partial.jsonl.gz')
            self.assertTrue(any(r.get('sample')==0 and r.get('before')==9. for r in rows))
            self.assertTrue(any(r.get('key')=='undeclared_file:extra.f32' and r.get('before')==7. for r in rows))

    def test_complete_union_and_exact_repeats(self):
        with self.temporary() as directory:
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
