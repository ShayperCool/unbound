#!/usr/bin/env python3
"""Check that recorded residual admission cannot mask other capture changes."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('fontlayout_probe', Path(__file__).with_name('run.py'))
PROBE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROBE)


class AdmissionTests(unittest.TestCase):
    def test_exact_resolved_and_recorded_values_pass_but_other_changes_fail(self):
        original = PROBE.rows(PROBE.reference_file(PROBE.ROOT, 'jsonl'))
        known = json.loads((PROBE.ROOT/'known-residuals.json').read_text())
        candidate = dict(original)
        for key, value in known.items():
            self.assertEqual(original[key], value['expected'])
            candidate[key] = value['actual']
        PROBE.WORK.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='admission-', dir=PROBE.WORK) as directory:
            path = Path(directory)/'values.jsonl'

            def verdict(values):
                path.write_text(''.join(json.dumps({'key': key, 'value': value})+'\n' for key, value in values.items()))
                with contextlib.redirect_stdout(io.StringIO()):
                    return PROBE.admit_residuals(path)

            self.assertEqual(0, verdict(original))
            self.assertEqual(0, verdict(candidate))
            removed = dict(candidate)
            del removed['Controlled/import-all/ligatures/count']
            self.assertEqual(1, verdict(removed))
            extra = dict(candidate, unexpected='0')
            self.assertEqual(1, verdict(extra))
            changed = dict(candidate)
            key = next(iter(known))
            changed[key] = '00000000,00000000,00000000'
            self.assertEqual(1, verdict(changed))
            changed = dict(candidate)
            changed['Controlled/import-all/ligatures/count'] = '0'
            self.assertEqual(1, verdict(changed))


if __name__ == '__main__':
    unittest.main()
