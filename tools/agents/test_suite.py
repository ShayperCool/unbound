#!/usr/bin/env python3
"""Unit checks of the suite's regression rules and the land gate's stub check:
python3 -m unittest tools/agents/test_suite.py"""
import contextlib
import importlib.util
import io
from pathlib import Path
import subprocess
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('suite', Path(__file__).resolve().parent/'suite.py')
suite = importlib.util.module_from_spec(spec)
spec.loader.exec_module(suite)
spec = importlib.util.spec_from_file_location('land', Path(__file__).resolve().parent/'land.py')
land = importlib.util.module_from_spec(spec)
spec.loader.exec_module(land)


def failing(summary, passed=0, failed=1, step=None, failures=None):
    result = {'status': 'fail', 'summary': summary, 'checks': {'passed': passed, 'failed': failed},
              'failed_step': step}
    if failures is not None:
        result['failures'] = failures
    return result


class WorseTests(unittest.TestCase):
    def test_more_differences(self):
        old = failing('17 nodes compared, 0 differences || 11 nodes compared, 1 differences')
        new = failing('17 nodes compared, 0 differences || 11 nodes compared, 7 differences')
        self.assertIsNotNone(suite.worse(old, new))
        self.assertIsNone(suite.worse(new, old))
        self.assertIsNone(suite.worse(old, old))

    def test_compact_json_counts(self):
        old = failing('{"tested":10,"failed":2061,"exact":false}')
        new = failing('{"tested":10,"failed":9000,"exact":false}')
        self.assertIsNotNone(suite.worse(old, new))

    def test_recorded_failures_beat_truncated_text(self):
        old = failing('cut', failures=3)
        new = failing('cut', failures=4)
        self.assertIsNotNone(suite.worse(old, new))
        self.assertIsNone(suite.worse(new, old))

    def test_earlier_failing_step(self):
        old = failing('x 1 differ || y 1 differ', failed=2)
        new = failing('x 1 differ || step 4 (driver) exited 1: boom', failed=1, step=4)
        self.assertIn('step 4', suite.worse(old, new))
        # Older summaries without failed_step: the step is read from the text, anywhere in it.
        del old['failed_step'], new['failed_step']
        self.assertIn('step 4', suite.worse(old, new))
        self.assertIn('step 3', suite.worse(failing('step 5 (driver) exited 1', step=5),
                                            failing('step 3 (engine) exited 1', step=3)))

    def test_getting_further_is_not_worse(self):
        crashed = failing('step 2 (driver) exited 1: boom', failed=0, step=2)
        compared = failing('5 missing', failed=1)
        self.assertIsNone(suite.worse(crashed, compared))
        later = failing('a 1 differ || step 6 (driver) exited 1', passed=1, failed=2, step=6)
        self.assertIsNone(suite.worse(failing('step 4 (driver) exited 1', passed=1, failed=0, step=4), later))

    def test_suite_error_is_worse_than_a_step_failure(self):
        error = {'probe': 'p', 'status': 'fail', 'failed_step': 0, 'seconds': 0, 'summary': 'suite error: boom'}
        self.assertIsNotNone(suite.worse(failing('step 2 (driver) exited 1', step=2), error))

    def test_compare_runs_flags_worse(self):
        old = failing('1 differences')
        new = failing('7 differences')
        lines, regressed = suite.compare_runs({'probes': {'p': new}}, {'probes': {'p': old}})
        self.assertTrue(regressed)
        self.assertTrue(lines[0].startswith('worse'))
        lines, regressed = suite.compare_runs({'probes': {'p': old}}, {'probes': {'p': new}})
        self.assertFalse(regressed)


def imaged(text, status='report', command='/usr/bin/python3 /x/tests/projects/particles/compare_images.py a b'):
    """A result as run_probe records it for an engine step and one image comparator step."""
    images = {suite.image_key(2, command, name): values for name, values in suite.image_measures(text).items()}
    return {'status': status, 'summary': suite.measurement(text), 'checks': {'passed': 0, 'failed': 0},
            'failed_step': None, 'images': images,
            'steps': [{'kind': 'engine', 'command': 'unbound run'}, {'kind': 'compare', 'command': command}]}


class ImageTests(unittest.TestCase):
    PARTICLES = 'a: mean 0.20, worst 1, over 8: 0\nb: mean 1.46, worst 48, over 8: 49\nworst mean 1.46\n'

    def test_measures_of_each_comparator(self):
        self.assertEqual(suite.image_measures(self.PARTICLES)['b'], {'mean': 1.46, 'worst': 48, 'over': 49})
        self.assertEqual(suite.image_measures(self.PARTICLES)['worst mean']['mean'], 1.46)
        urp = 'mean max-channel difference 0.06, worst 19, pixels over 8: 101 of 57600\n  center unity (1, 2, 3)'
        self.assertEqual(suite.image_measures(urp), {'': {'mean': 0.06, 'worst': 19, 'over': 101}})
        display = 'display: 6/6 values match; image mean 175.31, worst 255'
        self.assertEqual(suite.image_measures(display), {'': {'mean': 175.31, 'worst': 255, 'over': None}})
        self.assertEqual(suite.image_measures('5/5 records match'), {})

    def test_higher_mean_or_more_pixels_over_8_is_worse(self):
        old = imaged(self.PARTICLES)
        self.assertEqual(suite.images_worse(old, old), [])
        mean = imaged(self.PARTICLES.replace('mean 0.20', 'mean 0.21'))
        self.assertEqual(len(suite.images_worse(old, mean)), 1)
        self.assertEqual(suite.images_worse(mean, old), [])
        over = imaged(self.PARTICLES.replace('over 8: 49', 'over 8: 50'))
        self.assertIn('pixels over 8 49 -> 50', suite.images_worse(old, over)[0])

    def test_passing_display_that_gets_worse_regresses(self):
        command = '/usr/bin/python3 /x/tests/projects/display/compare.py ref out out.png'
        good = imaged('display: 6/6 values match; image mean 0.00, worst 0', 'pass', command)
        bad = imaged('display: 6/6 values match; image mean 175.31, worst 255', 'pass', command)
        lines, regressed = suite.compare_runs({'probes': {'display': bad}}, {'probes': {'display': good}})
        self.assertTrue(regressed)
        self.assertIn('display/compare.py: mean 0.00 -> 175.31', lines[-1])
        lines, regressed = suite.compare_runs({'probes': {'display': good}}, {'probes': {'display': bad}})
        self.assertFalse(regressed)

    def test_older_summaries_give_their_stored_measurements(self):
        new = imaged(self.PARTICLES.replace('worst mean 1.46', 'worst mean 1.50'), 'fail')
        old = dict(new, summary='terrain: 214/216 records match || worst mean 1.46',
                   steps=[{'kind': 'engine', 'command': 'unbound run'},
                          {'kind': 'compare', 'command': '/usr/bin/python3 /old/tests/projects/terrain/compare.py'},
                          {'kind': 'compare', 'command': '/usr/bin/python3 /old/tests/projects/particles/compare_images.py'}])
        del old['images']
        new['steps'] = old['steps']
        new['images'] = {suite.image_key(3, old['steps'][2]['command'], k): v
                         for k, v in suite.image_measures(self.PARTICLES.replace('worst mean 1.46', 'worst mean 1.50')).items()}
        self.assertEqual(suite.recorded_images(old),
                         {'step 3 particles/compare_images.py worst mean': {'mean': 1.46, 'worst': None, 'over': None}})
        self.assertEqual(suite.images_worse(old, new),
                         ['step 3 particles/compare_images.py worst mean: mean 1.46 -> 1.50'])
        # A summary cut at its 1000-character limit: its last part is not read.
        parts = ['a: 1/1 match'] + ['x' * 240] * 3 + ['y' * 234, 'worst mean 9.91']
        cut = dict(old, summary=' || '.join(parts)[:1000], steps=[{'kind': 'compare', 'command': 'c.py'}] * 6)
        self.assertTrue(cut['summary'].endswith('worst mean 9.9'))
        self.assertEqual(suite.recorded_images(cut), {})


class StubGateTests(unittest.TestCase):
    """land.py's generated-stubs step, with a stand-in CLI whose gen-stubs is a shell script."""

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.tree = Path(self.temp.name)/'tree'
        generated = self.tree/land.GENERATED/'6000.0'
        generated.mkdir(parents=True)
        (generated/'A.g.cs').write_text('// a\n')
        (self.tree/'managed/UnityEngine/Hand.cs').write_text('// hand\n')
        for command in (['init', '-q'], ['add', '.'],
                        ['-c', 'user.name=t', '-c', 'user.email=t@t', 'commit', '-qm', 'base']):
            subprocess.run(['git', *command], cwd=self.tree, check=True)
        self.launcher, land.LAUNCHER = land.LAUNCHER, Path(self.temp.name)/'no-launcher'

    def tearDown(self):
        land.LAUNCHER = self.launcher
        self.temp.cleanup()

    def gate(self, script, compiled_from=None):
        cli = Path(self.temp.name)/'unbound'
        source = compiled_from or self.tree/'managed/UnityEngine'
        cli.write_text(f'#!/bin/sh\n# compiled from {source}\n{script}\n')
        cli.chmod(0o755)
        gate = land.Gate(self.tree, 'HEAD', [], False, Path(self.temp.name)/'gate.log')
        return gate, cli

    def run_stubs(self, script, compiled_from=None):
        gate, cli = self.gate(script, compiled_from)
        try:
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                gate.stubs(cli)
        except SystemExit as stop:
            return stop.code
        finally:
            status = subprocess.run(['git', 'status', '--porcelain'], cwd=self.tree, capture_output=True,
                                    text=True).stdout
            self.assertEqual(status, '', 'the gate must leave Generated/ as committed')
        return 0

    def test_unchanged_output_passes(self):
        self.assertEqual(self.run_stubs('printf "// a\\n" > managed/UnityEngine/Generated/6000.0/A.g.cs'), 0)

    def test_drift_fails_and_is_restored(self):
        changed = 'echo "// b" > managed/UnityEngine/Generated/6000.0/A.g.cs'
        self.assertEqual(self.run_stubs(changed), 5)
        added = 'echo "// n" > managed/UnityEngine/Generated/6000.0/New.g.cs'
        self.assertEqual(self.run_stubs(added), 5)
        removed = 'rm managed/UnityEngine/Generated/6000.0/A.g.cs'
        self.assertEqual(self.run_stubs(removed), 5)

    def test_failing_no_stubs_build_fails(self):
        self.assertEqual(self.run_stubs('echo "building the implementation without stubs failed"; exit 1'), 5)

    def test_cli_from_another_checkout_is_not_run(self):
        marker = Path(self.temp.name)/'ran'
        self.assertEqual(self.run_stubs(f'touch {marker}', compiled_from='/elsewhere/managed/UnityEngine'), 5)
        self.assertFalse(marker.exists())


if __name__ == '__main__':
    unittest.main()
