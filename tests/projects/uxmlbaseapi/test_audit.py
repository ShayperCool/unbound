import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('uxml_base_api_runner', Path(__file__).with_name('run.py'))
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)


class AuditTests(unittest.TestCase):
    def test_comparison_keeps_missing_extra_and_wrong_values_with_an_accurate_match_count(self):
        matched, failures = probe.compare_values({'a': 'same', 'b': 'before', 'c': 'missing'},
                                               {'a': 'same', 'b': 'after', 'd': 'extra'})
        self.assertEqual(matched, 1)
        self.assertCountEqual(failures, [{'key': 'b', 'expected': 'before', 'actual': 'after'},
                                         {'key': 'c', 'missing': 'missing'}, {'key': 'd', 'extra': 'extra'}])

    def test_existing_particle_errors_remain_explicit(self):
        report = "access (1)\n  UnityEngine.ParticleSystem+MinMaxCurve\n    float m_ConstantMax is private, Unity's is internal\n1 errors in 2427 hand-written types checked against Unity\n"
        issues = probe.parse_api_audit(report, 1)
        self.assertEqual(len(issues), 1)
        self.assertTrue(probe.baseline_issue(issues[0]))
        self.assertIn('m_ConstantMax', issues[0]['message'])

    def test_new_uxml_or_other_diagnostics_are_never_baseline(self):
        for type_name in ['UnityEngine.UIElements.BaseUxmlFactory`2', 'UnityEditor.UIElements.Test', 'UnityEngine.Other']:
            report = f"silent (1)\n  {type_name}\n    method always returns true\n1 errors in 2427 hand-written types checked against Unity\n"
            self.assertFalse(probe.baseline_issue(probe.parse_api_audit(report, 1)[0]))
        issue = {'category': 'access', 'type': 'UnityEngine.ParticleSystem+MinMaxCurve', 'message': 'new error'}
        self.assertFalse(probe.baseline_issue(issue))

    def test_zero_errors_is_a_complete_success(self):
        self.assertEqual(probe.parse_api_audit('0 errors in 2427 hand-written types checked against Unity\n', 0), [])

    def test_missing_or_inconsistent_counts_are_failures(self):
        for report in ['Build failed', 'access (1)\n0 errors in 2427 hand-written types checked against Unity\n',
                       '1 errors in 2427 hand-written types checked against Unity\n']:
            with self.assertRaises(ValueError):
                probe.parse_api_audit(report, 1)

    def test_exit_disagreement_is_a_failure(self):
        with self.assertRaises(ValueError):
            probe.parse_api_audit('0 errors in 2427 hand-written types checked against Unity\n', 1)


if __name__ == '__main__':
    unittest.main()
