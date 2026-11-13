"""Source callback floats retain strict bounds when device quantization is admitted."""
import array
import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import compare as comparison


class SourceFloatBounds(unittest.TestCase):
    def test_source_precision_is_not_a_device_pcm_step(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            reference, candidate = root/'reference', root/'candidate'
            for folder, value in ((reference, 0.0), (candidate, 3e-5)):
                folder.mkdir()
                manifest = {'release': '6000.0.71f1', 'rate': 48000}
                for suffix in ('', '.master', '.source_float'):
                    name = 'control'+suffix
                    manifest[name] = dict(file=name+'.f32', frames=1, channels=1)
                    (folder/(name+'.f32')).write_bytes(array.array('f', [value]).tobytes())
                (folder/'capture.json').write_text(json.dumps(manifest))
            backends = [None] + ([comparison.np] if comparison.np is not None else [])
            for index, backend in enumerate(backends):
                report = root/f'report-{index}.jsonl.gz'
                with patch.object(comparison, 'np', backend), contextlib.redirect_stdout(io.StringIO()):
                    self.assertEqual(comparison.compare(reference, candidate, report,
                                                       {'control': None}, pcm_steps=1), 2)
                import gzip
                rows = [json.loads(row) for row in gzip.open(report, 'rt')]
                self.assertEqual({row['key'] for row in rows if 'sample' in row},
                                 {'control.master', 'control.source_float'})


if __name__ == '__main__':
    unittest.main()
