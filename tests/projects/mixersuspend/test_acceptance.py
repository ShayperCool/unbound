#!/usr/bin/env python3
"""Residual admission must reject new audio errors and incomplete reports."""
import gzip
import json
import os
from pathlib import Path
import tempfile
import unittest
from acceptance import RESET_FRAMES, STATE_PCM_SAMPLE, check, reset_residual


class AcceptanceTests(unittest.TestCase):
    def test_reset_is_limited_to_listener_delivery(self):
        for key in ('reset', 'reset.master', 'reset.listener'):
            for sample, pulse in [(792576, 0.25), (794625, -0.125)]:
                self.assertFalse(reset_residual(dict(key=key, sample=sample, reference=0.0, candidate=pulse)))
        row = dict(key='reset.listener', sample=2*RESET_FRAMES, reference=1e-20, candidate='<missing>')
        self.assertTrue(reset_residual(row))
        self.assertFalse(reset_residual(dict(row, key='reset.master')))
        self.assertFalse(reset_residual(dict(row, sample=2*(RESET_FRAMES+1024))))
        self.assertFalse(reset_residual(dict(row, reference=float('nan'))))

    def test_complete_recorded_reset_failures_are_classified_at_both_rates(self):
        # Literal positions from the capture, independently of the admission constants.
        for rate in (48000, 44100):
            rows = [dict(key='reset.listener',
                         reference=dict(file='reset.listener.f32', frames=1049600, channels=2),
                         candidate=dict(file='reset.listener.f32', frames=1048576, channels=2))]
            for sample in range(2097152, 2099200):
                rows.append(dict(key='reset.listener', sample=sample, reference=1e-20, candidate='<missing>'))
            self.assertEqual(2049, len(rows))
            self.assertFalse(check(self.report(rows), 'suspension', rate))
            rows[1] = dict(rows[1], sample=792576)
            self.assertTrue(check(self.report(rows), 'suspension', rate))

    def report(self, rows, failed=None):
        cache = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/q43/tests'
        cache.mkdir(parents=True, exist_ok=True)
        temp = tempfile.TemporaryDirectory(dir=cache)
        self.addCleanup(temp.cleanup)
        path = Path(temp.name)/'report.jsonl.gz'
        with gzip.open(path, 'wt') as output:
            for row in rows: output.write(json.dumps(row)+'\n')
            output.write(json.dumps(dict(summary=dict(tested=10, failed=len(rows) if failed is None else failed)))+'\n')
        return path

    def test_pcm_exception_does_not_admit_other_samples_streams_or_larger_errors(self):
        for rate, sample in STATE_PCM_SAMPLE.items():
            row = dict(key='reverb_disabled', sample=sample, reference=-3/32768, candidate=-2/32768)
            self.assertFalse(check(self.report([row]), 'state', rate))
            for changed in [dict(row, sample=sample+1), dict(row, key='reverb_disabled.master'),
                            dict(row, candidate=0), dict(row, candidate='<missing>'),
                            dict(row, candidate=float('nan'))]:
                self.assertTrue(check(self.report([changed]), 'state', rate))

    def test_truncated_report_and_incomplete_reset_shape_fail(self):
        row = dict(key='reset', sample=792576, reference=0.0, candidate=0.25)
        self.assertTrue(check(self.report([row]), 'suspension', 48000))
        with self.assertRaises(ValueError): check(self.report([row], failed=2), 'suspension', 48000)


if __name__ == '__main__': unittest.main()
