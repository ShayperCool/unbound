"""Reject bookkeeping-only warm-up and any additional global-warm-up mismatch."""
import contextlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import warmup


class WarmupGateTests(unittest.TestCase):
    def compare(self, expected, actual, log='', mode='collection'):
        with tempfile.TemporaryDirectory() as directory, contextlib.redirect_stdout(io.StringIO()):
            reference, candidate = (Path(directory)/p for p in ('reference', 'candidate'))
            reference.write_text(''.join(json.dumps(r)+'\n' for r in expected))
            candidate.write_text(''.join(json.dumps(r)+'\n' for r in actual))
            return warmup.compare(reference, candidate, log, mode)

    def test_pipelines_prepared_before_all_six_draws(self):
        self.assertEqual(self.compare([], [], 'WARMUP_GPU_PROOF collection 0 0 6 6 6 6\n' * 6), 0)

    def test_counter_only_warmup_is_rejected(self):
        self.assertNotEqual(self.compare([], [], 'WARMUP_GPU_PROOF collection 0 0 0 0 1 1\n' * 6), 0)

    def test_first_draw_compile_is_rejected(self):
        self.assertNotEqual(self.compare([], [], 'WARMUP_GPU_PROOF collection 0 0 6 6 7 7\n' * 6), 0)

    def test_missing_draw_proof_is_rejected(self):
        self.assertNotEqual(self.compare([], [], 'WARMUP_GPU_PROOF collection 0 0 6 6 6 6\n' * 5), 0)

    def test_partial_progress_prepares_gpu_objects_immediately(self):
        proofs = 'WARMUP_GPU_PROOF progressive 0 0 6 24 6 24\n' * 6
        self.assertNotEqual(self.compare([], [], proofs, 'progressive'), 0)
        self.assertEqual(self.compare([], [], proofs + 'WARMUP_PARTIAL_GPU_PROOF 0 0 2 8\n', 'progressive'), 0)
        self.assertNotEqual(self.compare([], [], proofs + 'WARMUP_PARTIAL_GPU_PROOF 0 0 0 0\n', 'progressive'), 0)

    def test_startup_preparation_is_complete_before_the_first_frame_draws(self):
        self.assertEqual(self.compare([], [], 'WARMUP_GPU_PROOF startup 6 24 6 24 6 24\n' * 6, 'startup'), 0)
        self.assertNotEqual(self.compare([], [], 'WARMUP_GPU_PROOF startup 0 0 0 0 1 1\n' * 6, 'startup'), 0)

    def test_global_requires_exact_unsupported_error_and_keeps_other_rows(self):
        call = {'case': 'warmup-cold', 'step': 'all call', 'result': 'ok'}
        error = {**call, 'result': warmup.GLOBAL_ERROR}
        self.assertEqual(self.compare([call], [error], mode='all'), 0)
        self.assertNotEqual(self.compare([call], [call], mode='all'), 0)
        self.assertNotEqual(self.compare([call], [error, {'extra': True}], mode='all'), 0)


if __name__ == '__main__':
    unittest.main()
