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

class SixInputLayouts(unittest.TestCase):
    """The published six-layouts reference follows one balance stage and one matrix per layout."""
    MATRICES = {
        1: [[1], [1], [1], [1], [1], [1]],
        2: [[1, 0], [0, 1], [.5, .5], [.5, .5], [.5, 0], [0, .5]],
        3: [[1, 0, 0, 0], [0, 1, 0, 0], [.5, .5, 0, 0], [.25, .25, 0, 0], [0] * 4, [0] * 4],
        4: [[1, 0, 0, 0, 0], [0, 1, 0, 0, 0], [0, 0, 1, 0, 0], [0] * 5, [0] * 5, [0, 0, 0, 1, 0]],
        5: [[1 if i == o else 0 for o in range(6)] for i in range(6)],
        6: [[1 if o == i or (i >= 4 and o == i + 2) else 0 for o in range(8)] for i in range(6)],
    }

    def test_reference_rows_match_the_balance_and_layout_matrix(self):
        from common import FIXTURE, header, samples
        for rate in (48000, 44100):
            root = FIXTURE/'Reference/unity-6000.0.71f1/six-layouts'/f'rate-{rate}'
            meta = header(root)
            cases = [key[:-len('.master')] for key in meta if key.startswith('context_sixlayout_') and key.endswith('.master')]
            self.assertEqual(len(cases), 17)
            for case in cases:
                _, _, mode, variant = case.split('_', 3)
                pan = {'impulses': 0.0, 'pan_left': -0.65, 'pan_right': 0.65}[variant]
                balance = [min(1, 1 - pan), min(1, 1 + pan), 1, 1, min(1, 1 - pan), min(1, 1 + pan)]
                for suffix in ('.master', ''):
                    record = meta[case+suffix]
                    values = samples(root, record)
                    width = record['channels']
                    self.assertEqual(len(values), record['frames'] * width, (rate, case+suffix))
                    rows = {4096 + 2048 * channel: row for channel, row in enumerate(self.MATRICES[int(mode)])}
                    # Nothing reaches the output outside the six impulse frames.
                    stray = [i for i, value in enumerate(values) if value and i // width not in rows]
                    self.assertEqual(stray, [], (rate, case+suffix))
                    for channel, row in enumerate(self.MATRICES[int(mode)]):
                        self.assertEqual(width, len(row), (rate, case+suffix))
                        frame = 4096 + 2048 * channel
                        actual = values[frame * width:frame * width + len(row)]
                        expected = [0.25 * balance[channel] * gain for gain in row]
                        for got, want in zip(actual, expected):
                            # Device PCM may differ from the exact gain by one 16-bit step.
                            delta = 3.1e-5 if suffix == '' else 1e-6
                            self.assertAlmostEqual(got, want, delta=delta, msg=(rate, case+suffix, channel))


if __name__ == '__main__':
    unittest.main()
