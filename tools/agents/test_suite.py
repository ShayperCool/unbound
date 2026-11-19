#!/usr/bin/env python3
"""Unit checks of the suite's regression rules, declared probe gates and the land gate's stub check:
python3 -m unittest tools/agents/test_suite.py"""
import contextlib
import fcntl
import importlib.util
import io
import json
import os
import shlex
import shutil
import signal
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
import unittest.mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import probe_runtime

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


class MetalReferenceTests(unittest.TestCase):
    """On macOS arm64 a compare step reads the probe's Metal reference where it has one."""

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.probe = Path(self.directory.name)/'probe'
        self.root = self.probe/'Reference/unity-R'
        (self.root/'metal/sub').mkdir(parents=True)
        (self.root/'sub').mkdir()
        for name in ('unity-R.png', 'unity-R.jsonl', 'unity-R/metal.png', 'unity-R/sub/a.png',
                     'unity-R/metal/sub/a.png', 'unity-R/other.png'):
            (self.probe/'Reference'/name).write_text('x')

    def tearDown(self):
        self.directory.cleanup()

    def metal(self, argument):
        return suite.metal_reference(argument, str(self.probe), 'R')

    def test_a_reference_with_a_metal_one_is_replaced(self):
        self.assertEqual(self.metal(str(self.probe/'Reference/unity-R.png')), str(self.root/'metal.png'))
        self.assertEqual(self.metal(str(self.root/'sub/a.png')), str(self.root/'metal/sub/a.png'))
        # A reference set compared as a whole directory.
        self.assertEqual(self.metal(str(self.root)), str(self.root/'metal'))

    def test_everything_else_is_kept(self):
        # No Metal counterpart, the Metal reference itself, another release, the Reference
        # directory, a residual list and a flag; the release's directory without a Metal one.
        for argument in (str(self.probe/'Reference/unity-R.jsonl'), str(self.root/'other.png'),
                         str(self.root/'metal.png'), str(self.root/'metal/sub/a.png'),
                         str(self.probe/'Reference/unity-S.png'), str(self.probe/'Reference'),
                         str(self.probe/'image-residuals.json'), '--gate', 3):
            self.assertEqual(self.metal(argument), argument)
        other = self.probe.parent/'other'/'Reference'/'unity-R'
        other.mkdir(parents=True)
        self.assertEqual(suite.metal_reference(str(other), str(other.parents[1]), 'R'), str(other))

    def test_compare_steps_read_it_on_macos_arm64_only(self):
        step = suite.Step('compare', ['{probe}/Reference/unity-{release}.png', '{work}/out.png'])
        values = {'probe': str(self.probe), 'release': 'R', 'work': self.directory.name}
        engine = suite.Step('engine', ['{probe}/Reference/unity-{release}.png'])
        with unittest.mock.patch.object(suite, 'MACOS_ARM64', True):
            self.assertEqual(suite.step_argv(step, values)[0], str(self.root/'metal.png'))
            self.assertEqual(suite.step_argv(engine, values)[0], str(self.probe/'Reference/unity-R.png'))
        with unittest.mock.patch.object(suite, 'MACOS_ARM64', False):
            self.assertEqual(suite.step_argv(step, values)[0], str(self.probe/'Reference/unity-R.png'))


class WorseTests(unittest.TestCase):
    def test_more_differences(self):
        old = failing('17 nodes compared, 0 differences || 11 nodes compared, 1 differences')
        new = failing('17 nodes compared, 0 differences || 11 nodes compared, 7 differences')
        self.assertIsNotNone(suite.worse(old, new))
        self.assertIsNone(suite.worse(new, old))
        self.assertIsNone(suite.worse(old, old))

    def test_values_differ_counts_numerator_and_detects_growth(self):
        old = failing('372 cases; 8226 of 615779 values differ (font 2, atlas 12, case 7780, mesh 432)')
        new = failing('372 cases; 8227 of 615779 values differ (font 2, atlas 12, case 7781, mesh 432)')
        self.assertEqual(suite.counted(old['summary']), [8226])
        self.assertIsNotNone(suite.worse(old, new))
        self.assertIsNone(suite.worse(new, old))
        self.assertIsNone(suite.worse(old, old))
        self.assertEqual(suite.counted('0 of 19 values differ; 2 missing; {"failed":3}'), [0, 2, 3])
        self.assertEqual(suite.counted('19 values compared; 5 of 9 cases differ'), [])

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



class AcceptedCountTests(unittest.TestCase):
    """A decided rise of a failing probe's counted failures (gate-accepted.json)."""
    SUMMARY = '{{"fields":1774,"differences":{},"tolerance_failures":{},"validation_errors":0}}'
    ENTRY = {'against': 'sheriff-1', 'baseline': 599, 'accepted': 601, 'reason': 'two last-digit tangents'}

    def run_of(self, differences, outside=0):
        summary = self.SUMMARY.format(differences, outside)
        return dict(failing(summary, failures=differences + outside),
                    steps=[{'status': 'pass', 'failures': 0, 'summary': ''},
                           {'status': 'fail', 'failures': differences + outside, 'summary': summary}])

    def test_an_entry_covers_a_rise_of_its_count_only(self):
        accepted, old = [self.ENTRY], self.run_of(599)
        self.assertIn('step 2', suite.worse(old, self.run_of(601)))
        self.assertIsNone(suite.worse(old, self.run_of(601), accepted))
        self.assertIsNone(suite.worse(old, self.run_of(600), accepted))
        # Past the accepted count, or from another baseline count, it is worse as before.
        self.assertIn('step 2', suite.worse(old, self.run_of(602), accepted))
        self.assertIsNotNone(suite.worse(self.run_of(598), self.run_of(601), accepted))
        self.assertIsNotNone(suite.worse(self.run_of(601), self.run_of(602), accepted))
        # Another count of the same summary may not rise under it, whatever the total is.
        self.assertIsNotNone(suite.worse(old, self.run_of(600, outside=1), accepted))
        self.assertIsNotNone(suite.worse(old, self.run_of(590, outside=11), accepted))
        self.assertIsNotNone(suite.worse(old, self.run_of(599, outside=2), accepted))
        # Nothing but counts is covered: a step that passed and fails now stays worse.
        broken = self.run_of(601)
        broken['steps'][0]['status'] = 'fail'
        self.assertIn('step 1', suite.worse(old, broken, accepted))
        # A summary that lost a count (cut short, reworded) is not covered.
        reworded = self.run_of(601)
        reworded['steps'][1]['summary'] = '601 differences'
        self.assertIsNotNone(suite.worse(old, reworded, accepted))

    def test_compare_runs_lists_an_accepted_rise_with_its_reason(self):
        other = dict(self.ENTRY, baseline=7, accepted=9, reason='unrelated')
        accepted = {'p': [other, self.ENTRY]}
        current = {'probes': {'p': self.run_of(601), 'q': self.run_of(601)}}
        previous = {'label': 'sheriff-1', 'probes': {'p': self.run_of(599), 'q': self.run_of(599)}}
        lines, regressed = suite.compare_runs(current, previous, accepted)
        self.assertTrue(regressed)  # q declares nothing
        self.assertTrue(lines[0].startswith('accepted  p: step 2 counts 601 failures, was 599'), lines)
        self.assertIn('gate-accepted.json: two last-digit tangents)', lines[0])
        self.assertTrue(lines[1].startswith('worse     q'), lines)
        del current['probes']['q']
        lines, regressed = suite.compare_runs(current, previous, accepted)
        self.assertFalse(regressed)

    def test_an_entry_against_another_baseline_run_is_stale_and_covers_nothing(self):
        # The count is 599 again in a later baseline: the old decision does not come back.
        current = {'probes': {'p': self.run_of(601)}}
        previous = {'label': 'sheriff-2', 'probes': {'p': self.run_of(599)}}
        lines, regressed = suite.compare_runs(current, previous, {'p': [self.ENTRY]})
        self.assertTrue(regressed)
        self.assertTrue(lines[0].startswith('worse     p'), lines)
        self.assertTrue(lines[1].startswith('stale     p'), lines)
        # Once the baseline holds the accepted count the entry is only reported.
        previous['probes']['p'] = self.run_of(601)
        lines, regressed = suite.compare_runs(current, previous, {'p': [self.ENTRY]})
        self.assertFalse(regressed)
        self.assertEqual(len(lines), 1)
        self.assertTrue(lines[0].startswith('stale     p: the gate-accepted.json entry against sheriff-1'), lines)

    def test_an_unusable_declaration_fails_the_comparison(self):
        current = {'probes': {'p': self.run_of(599)}}
        previous = {'label': 'sheriff-1', 'probes': {'p': self.run_of(599)}}
        lines, regressed = suite.compare_runs(current, previous, {'p': 'gate-accepted.json is not JSON: x'})
        self.assertTrue(regressed)
        self.assertTrue(lines[0].startswith('invalid   p'), lines)

    def test_declarations_are_read_from_the_probe_directories(self):
        with tempfile.TemporaryDirectory() as directory:
            projects = Path(directory)

            def declare(name, value):
                (projects/name).mkdir()
                (projects/name/'gate-accepted.json').write_text(value if isinstance(value, str) else json.dumps(value))
            declare('good', [self.ENTRY])
            declare('withdrawn', [])
            declare('lower', [dict(self.ENTRY, accepted=599)])
            declare('unexplained', [dict(self.ENTRY, reason=' ')])
            declare('unbound', [{k: v for k, v in self.ENTRY.items() if k != 'against'}])
            declare('extra', [dict(self.ENTRY, step=2)])
            declare('object', self.ENTRY)
            declare('broken', '[')
            (projects/'none').mkdir()
            names = ['good', 'withdrawn', 'lower', 'unexplained', 'unbound', 'extra', 'object', 'broken', 'none', 'absent']
            counts = suite.accepted_counts(names, projects)
            self.assertEqual(counts['good'], [self.ENTRY])
            self.assertEqual(counts['withdrawn'], [])
            for name in ('lower', 'unexplained', 'unbound', 'extra', 'object', 'broken'):
                self.assertIsInstance(counts[name], str, name)
            self.assertEqual(sorted(counts), sorted(names[:8]))

    def test_the_committed_declarations_are_usable(self):
        for name, entries in suite.accepted_counts(suite.discover()).items():
            self.assertIsInstance(entries, list, f'{name}: {entries}')


class OracleGuardTests(unittest.TestCase):
    """The guard over accepted failure counts, in a scratch repository."""
    GUARD = Path(__file__).resolve().parent/'oracle_guard.py'
    ENTRY = AcceptedCountTests.ENTRY
    FILE = 'tests/projects/p/gate-accepted.json'

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.repo = Path(self.directory.name)
        self.git('init', '-q', '-b', 'main')
        self.commit('base', 'x', file='README')
        self.git('checkout', '-q', '-b', 'work')

    def git(self, *args):
        subprocess.run(['git', '-c', 'user.name=t', '-c', 'user.email=t@example.invalid', *args],
                       cwd=self.repo, check=True, capture_output=True)

    def commit(self, message, content, file=FILE):
        path = self.repo/file
        path.parent.mkdir(parents=True, exist_ok=True)
        if path.is_symlink():
            path.unlink()
        path.write_text(content if isinstance(content, str) else json.dumps(content))
        self.git('add', '-A')
        self.git('commit', '-q', '-m', message)

    def landed(self, content):
        self.git('checkout', '-q', 'main')
        self.commit('landed', content)
        self.git('checkout', '-q', '-B', 'work')

    def guard(self):
        return subprocess.run([sys.executable, str(self.GUARD), 'main', 'HEAD'], cwd=self.repo,
                              capture_output=True, text=True)

    def test_a_new_accepted_count_needs_the_trailer(self):
        self.commit('raise', [self.ENTRY])
        refused = self.guard()
        self.assertEqual(refused.returncode, 4, refused.stdout)
        self.assertIn('accepted failure count declared', refused.stdout)
        self.git('commit', '-q', '--amend', '-m', 'raise\n\nOracle-Change: decided by the owner')
        self.assertEqual(self.guard().returncode, 0)

    def test_a_changed_entry_needs_it_too(self):
        self.landed([self.ENTRY])
        for change in ({'accepted': 605}, {'against': 'sheriff-2'}, {'baseline': 600}):
            self.commit('change', [dict(self.ENTRY, **change)])
            self.assertEqual(self.guard().returncode, 4, change)

    def test_what_is_not_a_list_needs_it(self):
        self.landed([])
        self.commit('object', self.ENTRY)
        self.assertEqual(self.guard().returncode, 4)
        # A link in the file's place: the suite would read where it points.
        (self.repo/'elsewhere.json').write_text(json.dumps([self.ENTRY]))
        (self.repo/self.FILE).unlink()
        (self.repo/self.FILE).symlink_to(self.repo/'elsewhere.json')
        self.git('add', '-A')
        self.git('commit', '-q', '-m', 'link')
        self.assertEqual(self.guard().returncode, 4)

    def test_withdrawing_or_keeping_an_entry_is_free(self):
        other = dict(self.ENTRY, baseline=7, accepted=9)
        self.landed([self.ENTRY, other])
        self.commit('reorder', [other, self.ENTRY])
        self.assertEqual(self.guard().returncode, 0)
        self.commit('withdraw one', [other])
        self.assertEqual(self.guard().returncode, 0)
        (self.repo/self.FILE).unlink()
        self.git('add', '-A')
        self.git('commit', '-q', '-m', 'withdraw all')
        self.assertEqual(self.guard().returncode, 0)

    def test_image_residuals_added_or_raised_need_the_trailer(self):
        residuals = 'tests/projects/p/image-residuals.json'
        entry = {'pixels': 5, 'worst': 3, 'reason': 'r'}
        self.commit('add', {'a': entry}, file=residuals)
        refused = self.guard()
        self.assertEqual(refused.returncode, 4, refused.stdout)
        self.assertIn('image residual added or raised: a: 5 pixels up to 3 (new)', refused.stdout)
        self.git('checkout', '-q', 'main')
        self.commit('landed', {'a': entry}, file=residuals)
        self.git('checkout', '-q', '-B', 'work')
        for raised in ({'pixels': 6}, {'worst': 4}):
            self.commit('raise', {'a': dict(entry, **raised)}, file=residuals)
            self.assertEqual(self.guard().returncode, 4, raised)
        for free in ({'a': dict(entry, pixels=4, reason='other')}, {}):
            self.commit('lower or withdraw', free, file=residuals)
            self.assertEqual(self.guard().returncode, 0, free)
        self.commit('not an object', [entry], file=residuals)
        self.assertEqual(self.guard().returncode, 4)
        self.commit('a truth value', {'a': dict(entry, pixels=True)}, file=residuals)
        self.assertEqual(self.guard().returncode, 4)
        self.commit('not a number', {'a': dict(entry, pixels='100')}, file=residuals)
        refused = self.guard()
        self.assertEqual(refused.returncode, 4, refused.stderr)
        self.assertIn('not an object with integer pixels and worst', refused.stdout)


class ImageTests(unittest.TestCase):
    """Image measurements as run_probe records them, from stand-in comparator scripts."""
    PARTICLES = 'a: mean 0.20, worst 1, over 8: 0\nb: mean 1.46, worst 48, over 8: 49\nworst mean 1.46\n'
    URP = 'mean max-channel difference {}, worst 3, pixels over 8: {} of 57600\n  center unity (1, 2, 3)\n'
    DISPLAY = 'display: 6/6 values match; image mean {}, worst 0\n'

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.runs = 0

    def tearDown(self):
        self.temp.cleanup()

    def probe(self, outputs, verdict='none'):
        """run_probe on one compare step per (comparator, reference, printed text)."""
        self.runs += 1
        recipe = []
        # Comparators and references live in the repository's tests/ tree, as in the suite.
        tests = self.root/'tests/projects'
        for index, (comparator, reference, text) in enumerate(outputs):
            script = tests/f'run{self.runs}-{index}'/comparator
            script.parent.mkdir(parents=True, exist_ok=True)
            script.write_text(f'print({text!r}, end="")\n')
            recipe.append(suite.Step('compare', ['{python}', str(script), str(tests/reference), '{work}/out'],
                                     verdict=verdict))
        context = {'work_root': self.root/f'work{self.runs}', 'env': dict(os.environ),
                   'values': {'python': sys.executable, 'label': 'suite-test'}}
        result = suite.run_probe('p', recipe, context)
        self.assertNotIn('suite error', result['summary'])
        return result

    def urp(self, *scenarios):
        return self.probe([('urp/compare.py', f'scenarios/{name}.png', self.URP.format(mean, over))
                           for name, mean, over in scenarios])

    def display(self, text):
        return self.probe([('display/compare.py', 'display/Reference', text)], verdict='exit')

    def test_measures_of_each_comparator(self):
        self.assertEqual(suite.image_measures(self.PARTICLES)['b'], {'mean': 1.46, 'worst': 48, 'over': 49})
        self.assertEqual(suite.image_measures(self.PARTICLES)['worst mean']['mean'], 1.46)
        self.assertEqual(suite.image_measures(self.URP.format('0.06', 101)), {'': {'mean': 0.06, 'worst': 3, 'over': 101}})
        self.assertEqual(suite.image_measures(self.DISPLAY.format('175.31')),
                         {'': {'mean': 175.31, 'worst': 0, 'over': None}})
        self.assertEqual(suite.image_measures('5/5 records match'), {})

    def test_every_step_of_a_recipe_is_measured(self):
        result = self.urp(('lit', '0.00', 0), ('unlit', '0.01', 2))
        self.assertEqual(result['status'], 'report')
        self.assertEqual(sorted(result['images']), ['urp/compare.py scenarios/lit.png',
                                                    'urp/compare.py scenarios/unlit.png'])

    def test_higher_mean_or_more_pixels_over_8_is_worse(self):
        old = self.urp(('lit', '0.00', 0), ('unlit', '0.01', 2))
        self.assertEqual(suite.images_worse(old, self.urp(('lit', '0.00', 0), ('unlit', '0.01', 2))), [])
        self.assertEqual(suite.images_worse(old, self.urp(('lit', '0.00', 0), ('unlit', '0.00', 2))), [])
        self.assertEqual(suite.images_worse(old, self.urp(('lit', '0.01', 0), ('unlit', '0.01', 2))),
                         ['urp/compare.py scenarios/lit.png: mean 0.00 -> 0.01'])
        self.assertEqual(suite.images_worse(old, self.urp(('lit', '0.00', 0), ('unlit', '0.01', 3))),
                         ['urp/compare.py scenarios/unlit.png: pixels over 8 2 -> 3'])

    def test_steps_are_matched_by_reference_not_position(self):
        old = self.urp(('lit', '0.00', 0), ('unlit', '0.50', 0))
        added = self.urp(('extra', '0.40', 0), ('lit', '0.00', 0), ('unlit', '0.50', 0))
        self.assertEqual(suite.images_worse(old, added), [])

    def test_passing_display_that_gets_worse_regresses(self):
        good, bad = self.display(self.DISPLAY.format('0.00')), self.display(self.DISPLAY.format('175.31'))
        self.assertEqual((good['status'], bad['status']), ('pass', 'pass'))
        lines, regressed = suite.compare_runs({'probes': {'display': bad}}, {'probes': {'display': good}})
        self.assertTrue(regressed)
        self.assertIn('display/compare.py display/Reference: mean 0.00 -> 175.31', lines[-1])
        lines, regressed = suite.compare_runs({'probes': {'display': good}}, {'probes': {'display': bad}})
        self.assertFalse(regressed)

    def test_a_measurement_the_step_no_longer_prints_is_worse(self):
        good = self.display(self.DISPLAY.format('0.00'))
        resized = self.display('display: 6/6 values match; screenshot 10x10, not 320x180\n')
        self.assertEqual(resized['status'], 'pass')
        self.assertIn('no longer measured', suite.images_worse(good, resized)[0])
        # A step that did not run this time (an earlier failure, a removed profile) is not judged here.
        self.assertEqual(suite.images_worse(good, dict(resized, steps=[])), [])

    def test_older_summaries_give_their_stored_measurements(self):
        old = self.probe([('terrain/compare.py', 'terrain/Reference', '214/216 records match\n'),
                          ('particles/compare_images.py', 'Reference/images', self.PARTICLES)])
        # Written before images were recorded: no measurements and no identities in the records.
        del old['images']
        for step in old['steps']:
            step.pop('images', None), step.pop('step', None)
        self.assertEqual(old['summary'], '214/216 records match || worst mean 1.46')
        self.assertEqual(suite.recorded_images(old), {'particles/compare_images.py Reference/images': {
            'worst mean': {'mean': 1.46, 'worst': None, 'over': None}}})
        new = self.probe([('terrain/compare.py', 'terrain/Reference', '214/216 records match\n'),
                          ('particles/compare_images.py', 'Reference/images',
                           self.PARTICLES.replace('worst mean 1.46', 'worst mean 1.50'))])
        self.assertEqual(suite.images_worse(old, new),
                         ['particles/compare_images.py Reference/images worst mean: mean 1.46 -> 1.50'])
        self.assertEqual(suite.images_worse(old, old), [])
        # A summary cut at its 1000-character limit: its last part is not read.
        parts = ['a: 1/1 match'] + ['x' * 240] * 3 + ['y' * 234, 'worst mean 9.91']
        steps = [{'kind': 'compare', 'command': f'python3 /x/c.py ref{i}'} for i in range(6)]
        cut = {'summary': ' || '.join(parts)[:1000], 'steps': steps}
        self.assertTrue(cut['summary'].endswith('worst mean 9.9'))
        self.assertEqual(suite.recorded_images(cut), {})

    def declared(self, profiles):
        """run_probe on a gate.json recipe of one command per (profile, printed text), each running
        the same script with its own --profile, as tests/projects/residentdrawer/gate.json does."""
        self.runs += 1
        projects = self.root/'tests/projects'
        script = projects/'p/run.py'
        script.parent.mkdir(parents=True, exist_ok=True)
        (projects/'p/outputs.json').write_text(json.dumps(dict(profiles)))
        script.write_text('import json, pathlib, sys\n'
                          'profile = sys.argv[sys.argv.index("--profile") + 1] if "--profile" in sys.argv else ""\n'
                          'print(json.loads((pathlib.Path(__file__).parent/"outputs.json").read_text())[profile], end="")\n')
        commands = [f'{shlex.quote(sys.executable)} {script} check' + (f' --profile {profile}' if profile else '')
                    for profile, _ in profiles]
        (projects/'p/gate.json').write_text(json.dumps({'commands': commands}))
        recipe = suite.declared('p', '6000.0.71f1', projects)
        context = {'work_root': self.root/f'work{self.runs}', 'env': dict(os.environ),
                   'values': {'python': sys.executable, 'label': f'suite-run{self.runs}',
                              'cli': '/x/unbound', 'managed': '/x/managed'}}
        return suite.run_probe('p', recipe, context)

    def test_declared_steps_are_matched_by_their_command(self):
        line = self.URP.format('0.00', 3)
        old = self.declared([('', line), ('motion', self.URP.format('0.00', 0)), ('controls', line)])
        self.assertEqual(sorted(old['images']), ['p/run.py check', 'p/run.py check --profile controls',
                                                 'p/run.py check --profile motion'])
        # A profile inserted before the others shifts every later step by one position: with
        # positions, motion's 0 pixels over 8 would be compared with the inserted step's 3.
        inserted = self.declared([('', line), ('occlusion', line), ('motion', self.URP.format('0.00', 0)),
                                  ('controls', line)])
        self.assertEqual(suite.images_worse(old, inserted), [])
        lines, regressed = suite.compare_runs({'label': 'b', 'probes': {'p': inserted}},
                                              {'label': 'a', 'probes': {'p': old}})
        self.assertFalse(regressed, lines)
        self.assertEqual(lines[-1], 'unmatched p: step p/run.py check --profile occlusion has no baseline')
        # The shifted step getting worse is still found, under its own command.
        worse = self.declared([('', line), ('occlusion', line), ('motion', self.URP.format('0.00', 1)),
                               ('controls', line)])
        self.assertEqual(suite.images_worse(old, worse),
                         ['p/run.py check --profile motion: pixels over 8 0 -> 1'])
        # A removed profile is listed, not judged; the remaining steps are still compared.
        removed = self.declared([('', line), ('controls', self.URP.format('0.00', 4))])
        self.assertEqual(suite.images_worse(old, removed),
                         ['p/run.py check --profile controls: pixels over 8 3 -> 4'])
        self.assertEqual(suite.images_unmatched(old, removed), (['p/run.py check --profile motion'], [], []))

    def test_a_step_whose_command_changed_is_still_judged(self):
        def run(gate, mean):
            command = f'python3 /r/tests/projects/urp/compare.py /r/tests/projects/urp/Reference/lit.png --gate {gate}'
            return {'status': 'pass', 'steps': [{'kind': 'compare', 'command': command, 'step': suite.step_id(command),
                               'images': {'': {'mean': mean, 'worst': 3, 'over': 0}}}]}
        old, new = run('/r/tests/projects/urp/residuals.json', 0.10), run('/r/tests/projects/urp/renamed.json', 5.0)
        self.assertEqual(suite.images_worse(old, new),
                         ['urp/compare.py Reference/lit.png --gate urp/renamed.json: mean 0.10 -> 5.00'])
        self.assertEqual(suite.images_unmatched(old, new), ([], [], [
            ('urp/compare.py Reference/lit.png --gate urp/residuals.json',
             'urp/compare.py Reference/lit.png --gate urp/renamed.json')]))
        lines, regressed = suite.compare_runs({'probes': {'p': new}}, {'probes': {'p': old}})
        self.assertTrue(regressed)
        self.assertIn('(command changed)', lines[-1])
        # An argument appended: paired, judged and listed as changed.
        strict = {'status': 'pass', 'steps': [dict(new['steps'][0], step=new['steps'][0]['step'] + ' --strict')]}
        self.assertEqual(len(suite.images_worse(old, strict)), 1)
        self.assertEqual(len(suite.images_unmatched(old, strict)[2]), 1)
        # Another value of the same option is another step (a profile removed, another added).
        def profile(name, mean):
            sid = f'p/run.py check --profile {name}'
            return {'status': 'pass', 'steps': [{'kind': 'compare', 'command': sid, 'step': sid,
                                                 'images': {'': {'mean': mean, 'worst': 0, 'over': 0}}}]}
        self.assertEqual(suite.images_worse(profile('motion', 0.0), profile('occlusion', 0.5)), [])
        self.assertEqual(suite.images_unmatched(profile('motion', 0.0), profile('occlusion', 0.5)),
                         (['p/run.py check --profile motion'], ['p/run.py check --profile occlusion'], []))
        # Two changed steps with the same script and first argument: not paired by guessing.
        two = lambda gates, mean: {'steps': run(gates[0], mean)['steps'] + run(gates[1], mean)['steps']}
        old2, new2 = two(['/r/tests/a.json', '/r/tests/b.json'], 0.1), two(['/r/tests/c.json', '/r/tests/d.json'], 5.0)
        self.assertEqual(suite.images_worse(old2, new2), [])
        self.assertEqual([len(part) for part in suite.images_unmatched(old2, new2)], [2, 2, 0])

    def test_step_identity_is_its_command_without_run_paths(self):
        label = 'suite-sheriff-20261118-090834'
        work = f'/home/u/.cache/unbound/validation/suite/{label[6:]}/urp'
        argv = ['/usr/bin/python3', '/slots/slot-10/tests/projects/urp/compare.py',
                '/slots/slot-10/tests/projects/urp/Reference/unity-6000.0.71f1/scenarios/lit.png',
                f'{work}/out/lit.png', '--gate', '/slots/slot-10/tests/projects/urp/image-residuals.json']
        self.assertEqual(suite.step_id(argv, label),
                         'urp/compare.py scenarios/lit.png {work}/out/lit.png --gate urp/image-residuals.json')
        # Another checkout, run label and runtime give the same identity.
        other = [a.replace('/slots/slot-10', '/data/integration').replace(label[6:], 'r2') for a in argv]
        self.assertEqual(suite.step_id(other, 'suite-r2'), suite.step_id(argv, label))
        self.assertEqual(suite.step_id(shlex.join(argv), label), suite.step_id(argv, label))
        # gate.json commands: the shell command unwrapped; a label argument read as {label}.
        self.assertEqual(suite.step_id(['bash', '-c', 'python3 tests/projects/p/run.py check --label '
                                        f'{label}-quit --out={work}/x.json --leave'], label),
                         'p/run.py check --label {label}-quit --out={work}/x.json --leave')
        self.assertEqual(suite.step_id([f'/runtimes/snapshots/ab12/unbound', 'check']), 'check')
        runs = f'/home/u/.cache/unbound/validation/runs/p/{label}-gamma/gamma/out.jsonl'
        self.assertEqual(suite.step_id(['/r/tests/projects/p/compare.py', runs], label),
                         'p/compare.py {label}-gamma/gamma/out.jsonl')
        # A recorded command cut inside the quoted shell command: its last partial word is left out.
        cut = "bash -c 'python3 tests/projects/p/run.py check --profile motion --out /home/u/.cac"
        self.assertEqual(suite.step_id(cut, label, cut=True), 'p/run.py check --profile motion --out')

    def test_cut_commands_of_older_records_match_by_prefix(self):
        reference = '/slots/slot-10/tests/projects/urp/Reference/unity-6000.0.71f1/scenarios/'
        def step(name, mean, cut):
            argv = ['/usr/bin/python3', '/slots/slot-10/tests/projects/urp/compare.py', reference + name + '.png',
                    '/home/u/.cache/unbound/validation/suite/' + 'x' * 260 + '/urp/out/' + name + '.png',
                    '--gate', '/slots/slot-10/tests/projects/urp/image-residuals.json']
            command = shlex.join(argv)
            record = {'kind': 'compare', 'command': command[:suite.COMMAND_LIMIT] if cut else command,
                      'images': {'': {'mean': mean, 'worst': 3, 'over': 0}}}
            if not cut:
                record['step'] = suite.step_id(argv)
            return record
        old = {'steps': [step('lit', 0.0, True), step('unlit', 0.5, True)]}
        self.assertTrue(all(len(s['command']) == suite.COMMAND_LIMIT for s in old['steps']))
        self.assertEqual([c for _, _, c in suite.step_ids(old['steps'])], [False, False])
        new = {'steps': [step('extra', 0.4, False), step('lit', 0.01, False), step('unlit', 0.5, False)]}
        self.assertEqual(suite.images_worse(old, new),
                         ['urp/compare.py scenarios/lit.png {work}/out/lit.png --gate urp/image-residuals.json: '
                          'mean 0.00 -> 0.01'])
        self.assertEqual(suite.images_unmatched(old, new),
                         ([], ['urp/compare.py scenarios/extra.png {work}/out/extra.png --gate urp/image-residuals.json'],
                          []))

class ContinuationTests(unittest.TestCase):
    def probe(self, tail_code=0, tail_text='9 nodes compared, 0 differences'):
        outputs = [(0, '2/2 records match'), (7, 'engine broke'), (1, 'MISSING capture'),
                   (0, ''), (tail_code, tail_text)]
        recipe = [suite.Step(kind, ['profile'+str(i)]) for i, kind in
                  enumerate(['compare', 'engine', 'compare', 'engine', 'compare'])]
        with tempfile.TemporaryDirectory() as temp:
            with unittest.mock.patch.object(suite, 'run_step', side_effect=outputs) as run:
                result = suite.run_probe('p', recipe, {'work_root': Path(temp), 'env': {},
                                                      'values': {'label': 'test'}})
                self.assertEqual(run.call_count, 5)
        return result

    def test_failed_engine_preserves_prefix_and_records_later_profiles(self):
        result = self.probe()
        self.assertEqual(result['status'], 'fail')
        self.assertEqual(result['failed_step'], 2)
        self.assertEqual(result['checks'], {'passed': 1, 'failed': 0, 'report': 0, 'total': 1})
        self.assertEqual(result['summary'], '2/2 records match || step 2 (engine) exited 7: engine broke')
        self.assertEqual([step['status'] for step in result['steps']], ['pass', 'fail', 'fail', 'pass', 'pass'])
        self.assertIn('MISSING capture', result['continuation_summary'])
        self.assertIn('0 differences', result['steps'][-1]['summary'])

    def test_an_improved_early_step_does_not_hide_a_later_regression(self):
        old = self.probe()
        new = self.probe(1, '9 nodes compared, 8 differences')
        new.update(failed_step=None, checks={'passed': 2, 'failed': 2})
        self.assertIn('step 5', suite.worse(old, new))

    def test_old_baseline_does_not_judge_newly_reached_profiles(self):
        result = self.probe(1, '9 nodes compared, 8 differences')
        old = dict(result, steps=result['steps'][:2])
        for step in old['steps']:
            step.pop('status')
        self.assertIsNone(suite.worse(old, result))

    def test_new_baseline_detects_later_profile_regressions(self):
        old = self.probe()
        self.assertIn('step 5', suite.worse(old, self.probe(1, '9 nodes compared, 8 differences')))
        failing = self.probe(1, '9 nodes compared, 1 differences')
        self.assertIn('step 5', suite.worse(failing, self.probe(1, '9 nodes compared, 8 differences')))
        self.assertIsNone(suite.worse(failing, self.probe()))


def ps_listing(rows):
    """A fake probe_runtime.ps: the process listing of rows (pid, parent, state, start, comm,
    args) answering the columns ps_snapshot asks for."""
    def ps(*columns):
        if 'lstart=' in columns:
            return ''.join(f'{pid:>6} {parent:>6} {state:<4} {start} {comm}\n'
                           for pid, parent, state, start, comm, _ in rows)
        return ''.join(f'{pid:>6} {args}\n' for pid, _, _, _, _, args in rows)
    return ps


class PsBackendTests(unittest.TestCase):
    """The macOS process guard (ps snapshots), exercised on every platform with a fake ps."""
    START = 'Tue Nov 17 07:35:09 2026'
    LATER = 'Tue Nov 17 07:35:41 2026'

    def test_snapshot_reads_names_with_spaces_and_zombies(self):
        rows = [(100, 1, 'Ss', self.START, '/Applications/Unity Hub.app/Contents/MacOS/Unity Hub', '/x/Unity Hub --a'),
                (101, 100, 'Z', self.LATER, '<defunct>', '<defunct>')]
        with unittest.mock.patch.object(probe_runtime, 'ps', ps_listing(rows)):
            snapshot = probe_runtime.ps_snapshot()
        self.assertEqual(snapshot[100].executable, '/Applications/Unity Hub.app/Contents/MacOS/Unity Hub')
        self.assertEqual((snapshot[100].start, snapshot[100].argv), (self.START, ['/x/Unity', 'Hub', '--a']))
        self.assertEqual((snapshot[101].parent, snapshot[101].state), (100, 'Z'))

    def test_snapshot_refuses_a_row_it_cannot_read(self):
        # A start time of another word count (a locale's date) must not shift the columns.
        listing = '   100      1 Ss   2026年11月17日 火曜日 10:35:09 /bin/sh\n'
        with unittest.mock.patch.object(probe_runtime, 'ps', lambda *columns: listing):
            with self.assertRaisesRegex(OSError, 'unreadable ps row'):
                probe_runtime.ps_snapshot()

    def test_ps_runs_in_the_c_locale_and_utc(self):
        if not shutil.which('ps'):
            self.skipTest('no ps')
        starts = []
        for zone in ('Asia/Tokyo', 'America/Los_Angeles'):
            with unittest.mock.patch.dict(os.environ, {'LANG': 'ja_JP.UTF-8', 'LC_ALL': 'ru_RU.UTF-8', 'TZ': zone}):
                starts.append(probe_runtime.ps_snapshot()[os.getpid()].start)
        # The same five C-locale words whatever the caller's locale and zone.
        self.assertEqual(starts[0], starts[1])
        self.assertRegex(starts[0], r'^[A-Z][a-z]{2} [A-Z][a-z]{2} +\d+ \d\d:\d\d:\d\d \d{4}$')

    def test_tree_never_signals_a_pid_used_again(self):
        tree = probe_runtime.PsTree(100)
        tree.fds = {100: self.START, 101: self.START}
        # 101 is now another process (a later start); 102 is the root's new child.
        rows = [(100, 1, 'S', self.START, 'sh', 'sh'), (101, 1, 'S', self.LATER, 'other', 'other'),
                (102, 100, 'S', self.LATER, 'child', 'child')]
        with unittest.mock.patch.object(probe_runtime, 'ps', ps_listing(rows)), \
                unittest.mock.patch.object(probe_runtime, 'pidfd_backend', lambda: False), \
                unittest.mock.patch.object(probe_runtime.os, 'kill') as kill, \
                unittest.mock.patch.object(probe_runtime.os, 'killpg') as killpg:
            tree.signal(signal.SIGTERM)
            tree.prune()
        self.assertEqual(sorted(call.args for call in kill.call_args_list), [(100, signal.SIGTERM), (102, signal.SIGTERM)])
        killpg.assert_called_once_with(100, signal.SIGTERM)
        self.assertEqual(set(tree.fds), {100, 102})

    def test_a_failed_listing_signals_the_group_only_and_keeps_the_watch(self):
        def failing(*columns):
            raise OSError('ps exited 1')
        tree = probe_runtime.PsTree(100)
        tree.fds = {100: self.START, 101: self.START}
        watch = probe_runtime.EditorWatch()
        watch.trees[100] = tree
        with unittest.mock.patch.object(probe_runtime, 'ps', failing), \
                unittest.mock.patch.object(probe_runtime, 'pidfd_backend', lambda: False), \
                unittest.mock.patch.object(probe_runtime.os, 'kill') as kill, \
                unittest.mock.patch.object(probe_runtime.os, 'killpg') as killpg, \
                contextlib.redirect_stderr(io.StringIO()) as err:
            tree.signal(signal.SIGKILL)
            self.assertTrue(tree.alive())
            watch.scan()
        kill.assert_not_called()
        killpg.assert_called_once_with(100, signal.SIGKILL)
        self.assertEqual(watch.found, {})
        self.assertIn('ps exited 1', err.getvalue())

    @unittest.skipIf(probe_runtime.pidfd_backend(), 'Linux holds processes by pidfd')
    def test_an_exited_unreaped_child_is_not_alive(self):
        process = subprocess.Popen([sys.executable, '-c', 'pass'])
        try:
            tree = probe_runtime.Tree(process.pid)
            tree.collect()
            self.assertIn(process.pid, tree.fds)
            deadline = time.monotonic() + 10
            while tree.alive() and time.monotonic() < deadline:
                time.sleep(0.05)
            self.assertFalse(tree.alive())
            self.assertIsNone(process.returncode)  # still a zombie: never reaped above
        finally:
            process.wait()

    def test_stop_groups_reaches_descendants_in_the_callers_group(self):
        if not shutil.which('ps'):
            self.skipTest('no ps')
        with tempfile.TemporaryDirectory() as temp:
            pidfile = Path(temp)/'grandchild.pid'
            # Without a session of its own the step shares this test's process group.
            process = subprocess.Popen(['bash', '-c', f'sleep 60 & echo $! > {shlex.quote(str(pidfile))}; wait'])
            deadline = time.monotonic() + 10
            while not pidfile.exists() or not pidfile.read_text().strip():
                self.assertLess(time.monotonic(), deadline)
                time.sleep(0.02)
            grandchild = int(pidfile.read_text())
            self.assertEqual(os.getpgid(grandchild), os.getpgrp())
            probe_runtime.stop_groups(process, 5)
            self.assertIsNotNone(process.returncode)
            deadline = time.monotonic() + 5
            while time.monotonic() < deadline:
                try:
                    os.kill(grandchild, 0)
                except ProcessLookupError:
                    break
                time.sleep(0.02)
            else:
                self.fail('the grandchild in the caller\'s group still runs')


class SharedGuardTests(unittest.TestCase):
    @unittest.skipUnless(probe_runtime.pidfd_backend(), 'pidfd process handles are Linux-only')
    def test_process_liveness_handles_large_descriptors_and_exited_children(self):
        process = subprocess.Popen([sys.executable, '-c', 'pass'])
        fd = os.pidfd_open(process.pid)
        high_fd = fcntl.fcntl(fd, fcntl.F_DUPFD_CLOEXEC, 1100)
        os.close(fd)
        tree = probe_runtime.Tree(process.pid)
        tree.fds[process.pid] = high_fd
        try:
            process.wait(timeout=5)
            self.assertFalse(tree.alive())
        finally:
            tree.close()
            process.wait()

    def test_unsupported_platform_fails_before_starting_a_probe(self):
        with tempfile.TemporaryDirectory() as temp:
            gate = land.Gate(Path(temp), 'HEAD', ['touch unsafe'], False, Path(temp)/'gate.log')
            with unittest.mock.patch.object(probe_runtime.sys, 'platform', 'win32'):
                with self.assertRaisesRegex(RuntimeError, 'unsupported on win32'):
                    gate.run_probes()
                with self.assertRaisesRegex(RuntimeError, 'unsupported on win32'):
                    suite.run_step(suite.Step('driver', ['touch', 'unsafe']), {}, Path(temp)/'log', {})
            self.assertFalse((Path(temp)/'unsafe').exists())

    def test_gate_stops_an_editor_driver_before_later_shell_commands(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            editor = root/'6000.0.71f1/Editor/Unity'
            probes = ['bash -c '+shlex.quote(f'(exec -a {editor} sleep 60); touch after; touch later')]
            gate = land.Gate(root, 'HEAD', probes, False, root/'gate.log')
            start = time.monotonic()
            with unittest.mock.patch.object(probe_runtime, 'EDITOR_POLL', 0.05):
                with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                    with self.assertRaises(SystemExit) as stopped:
                        gate.run_probes()
            self.assertEqual(stopped.exception.code, 5)
            self.assertLess(time.monotonic() - start, 20)
            self.assertIn('launched the Unity Editor', gate.log.read_text())
            self.assertFalse((root/'after').exists())
            self.assertFalse((root/'later').exists())

    def test_gate_process_waits_for_a_sigterm_resistant_editor_child(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            pidfile = root/'editor.pid'
            # bash ignoring SIGTERM, named as the Editor (macOS's framework python3 starts its
            # interpreter again under its own name, so a python child would not keep the name).
            child = f'trap "" TERM; echo $$ > {shlex.quote(str(pidfile))}; while :; do sleep 1; done'
            editor = root/'6000.0.71f1/Editor/Unity'
            command = 'bash -c '+shlex.quote(
                f'(exec -a {shlex.quote(str(editor))} bash -c {shlex.quote(child)}); '
                'touch after')
            script = root/'gate.py'
            script.write_text(
                f'import sys; sys.path.insert(0, {str(Path(land.__file__).parent)!r})\n'
                'import land, probe_runtime\nfrom pathlib import Path\n'
                'probe_runtime.EDITOR_POLL = 0.1\nprobe_runtime.EDITOR_WATCH.grace = 0.5\n'
                f'land.Gate(Path({temp!r}), "HEAD", [{command!r}], False, '
                f'Path({str(root/"gate.log")!r})).run_probes()\n')
            try:
                result = subprocess.run([sys.executable, str(script)], capture_output=True, text=True, timeout=15)
                self.assertEqual(result.returncode, 5, result.stdout+result.stderr)
                self.assertTrue(pidfile.is_file(), (root/'gate.log').read_text())
                pid = int(pidfile.read_text())
                # A zombie owns no running payload. Allow the kernel to deliver SIGKILL.
                deadline = time.monotonic() + 2
                while time.monotonic() < deadline:
                    state = subprocess.run(['ps', '-o', 'stat=', '-p', str(pid)], capture_output=True, text=True).stdout.strip()
                    if not state or state[0] in ('Z', 'X'):
                        break
                    time.sleep(0.01)
                else:
                    self.fail('the Editor child survived the gate process')
                self.assertFalse((root/'after').exists())
            finally:
                if pidfile.is_file():
                    try:
                        os.kill(int(pidfile.read_text()), signal.SIGKILL)
                    except ProcessLookupError:
                        pass

    def test_gate_refuses_editor_runner_and_queued_editor_job(self):
        for probe in ['test "$UNBOUND_SUITE_NO_EDITOR" = 1',
                      'bash -c "sleep 60; true" with-heavy-job --kind editor-cpu']:
            with tempfile.TemporaryDirectory() as temp:
                gate = land.Gate(Path(temp), 'HEAD', [probe], False, Path(temp)/'gate.log')
                with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                    if 'sleep' in probe:
                        with self.assertRaises(SystemExit) as stopped:
                            gate.run_probes()
                        self.assertEqual(stopped.exception.code, 5)
                    else:
                        gate.run_probes()

    def test_managed_tests_use_the_test_pool(self):
        with tempfile.TemporaryDirectory() as temp:
            target = Path(temp)
            gate = land.Gate(target, 'HEAD', [], False, target/'gate.log')
            def step(name, command):
                self.assertEqual(command[:3], [str(land.LAUNCHER), '--kind', 'test'])
                results = target/'land-test-results'
                results.mkdir()
                (results/'land.trx').write_text('<TestRun/>')
                return 0
            with unittest.mock.patch.object(land.Path, 'is_file', return_value=True), \
                 unittest.mock.patch.object(gate, 'step', side_effect=step), \
                 contextlib.redirect_stdout(io.StringIO()):
                gate.managed_tests(target)


class PerProbeReleaseTests(unittest.TestCase):
    def test_suite_selects_and_exports_each_project_release_and_overrides(self):
        with tempfile.TemporaryDirectory() as temp, unittest.mock.patch.dict(os.environ):
            os.environ.pop(land.RELEASE_VARIABLE, None)
            root = Path(temp)
            versions = {'old': '2022.3.10f1', 'new': '6000.0.71f1', 'unset': land.default_release()}
            for name, version in versions.items():
                project = root/'tests/projects'/name
                (project/'Reference').mkdir(parents=True)
                if name != 'unset':
                    (project/'ProjectSettings').mkdir()
                    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+version+'\n')
            cli = root/'cli'
            cli.symlink_to(sys.executable)
            def recipes(release):
                return {name: [suite.Step('compare', [sys.executable, '-c',
                            'import os; print(os.environ["UNBOUND_PROBE_UNITY_VERSION"])'],
                            summary=release)] for name in versions}
            for label, override in [('per-project', []), ('override', ['--release', '2021.3.10f1']),
                                    ('exported', [])]:
                if label == 'exported':
                    os.environ[land.RELEASE_VARIABLE] = '2020.3.10f1'
                argv = ['suite.py', '--cli', str(cli), '--label', label, '--against', 'none', *override]
                with unittest.mock.patch.multiple(suite, REPO=root, PROJECTS=root/'tests/projects',
                                                  CACHE=root/'cache', AGENTS=root/'agents'):
                    with unittest.mock.patch.object(sys, 'argv', argv), \
                         unittest.mock.patch.object(suite, 'recipes', recipes), \
                         unittest.mock.patch.object(suite, 'managed_for', return_value=root), \
                         unittest.mock.patch.object(suite, 'make_runtime', return_value=root), \
                         unittest.mock.patch.object(suite.signal, 'signal'), \
                         contextlib.redirect_stdout(io.StringIO()):
                        self.assertEqual(suite.main(), 0)
                summary = json.loads((root/'agents/suite'/label/'summary.json').read_text())
                results = summary['probes']
                self.assertEqual(summary['release'], override[-1] if override else
                                 os.environ.get(land.RELEASE_VARIABLE))
                self.assertEqual(summary['releases'], {name: row['release'] for name, row in results.items()})
                for name, version in versions.items():
                    expected = override[-1] if override else os.environ.get(land.RELEASE_VARIABLE, version)
                    self.assertEqual(results[name]['release'], expected)
                    self.assertEqual(results[name]['summary'], expected)

    def test_a_probe_that_writes_into_the_checkout_fails_the_run(self):
        with tempfile.TemporaryDirectory() as temp, unittest.mock.patch.dict(os.environ):
            os.environ.pop(land.RELEASE_VARIABLE, None)
            root = Path(temp)
            for name in ('writer', 'clean'):
                (root/'tests/projects'/name/'Reference').mkdir(parents=True)
                (root/'tests/projects'/name/'Reference/ref.txt').write_text(name)
            git = lambda *args: subprocess.run(['git', *args], cwd=root, check=True, capture_output=True)
            git('init', '-q')
            git('add', '.')
            git('-c', 'user.name=t', '-c', 'user.email=t@t', 'commit', '-q', '-m', 'probes')
            cli = root/'cli'
            cli.symlink_to(sys.executable)
            write = 'import sys; open(sys.argv[1], "w").write("guid")'
            def recipes(release):
                return {'writer': [suite.Step('compare', [sys.executable, '-c', write, '{probe}/New.cs.meta'])],
                        'clean': [suite.Step('compare', [sys.executable, '-c', 'print("1/1 match")'])]}
            argv = ['suite.py', '--cli', str(cli), '--label', 'writes', '--against', 'none']
            with unittest.mock.patch.multiple(suite, REPO=root, PROJECTS=root/'tests/projects',
                                              CACHE=root/'cache', AGENTS=root/'agents'), \
                 unittest.mock.patch.object(sys, 'argv', argv), \
                 unittest.mock.patch.object(suite, 'recipes', recipes), \
                 unittest.mock.patch.object(suite, 'managed_for', return_value=root), \
                 unittest.mock.patch.object(suite, 'make_runtime', return_value=root), \
                 unittest.mock.patch.object(suite.signal, 'signal'), \
                 contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()) as error:
                self.assertEqual(suite.main(), 1)
            summary = json.loads((root/'agents/suite/writes/summary.json').read_text())
            self.assertEqual(summary['checkout_writes'], ['?? tests/projects/writer/New.cs.meta'])
            self.assertEqual(summary['probes']['writer']['status'], 'fail')
            self.assertIn('wrote into the checkout', summary['probes']['writer']['summary'])
            self.assertEqual(summary['probes']['clean']['status'], 'pass')
            self.assertIn('tests/projects/writer/New.cs.meta', error.getvalue())

    def test_mixed_project_releases_require_an_explicit_override(self):
        with tempfile.TemporaryDirectory() as temp, unittest.mock.patch.dict(os.environ):
            os.environ.pop(land.RELEASE_VARIABLE, None)
            root = Path(temp)
            for name, version in [('a', '6000.0.71f1'), ('b', '2022.3.10f1')]:
                directory = root/'tests/projects'/name/'ProjectSettings'
                directory.mkdir(parents=True)
                (directory/'ProjectVersion.txt').write_text('m_EditorVersion: '+version+'\n')
            with self.assertRaisesRegex(ValueError, 'names probes of releases'):
                land.probe_release('tests/projects/a/run.py tests/projects/b/run.py', root)


class DeclaredGateTests(unittest.TestCase):
    """A probe without a recipe in suite.py runs the commands its gate.json declares."""

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.projects, suite.PROJECTS = suite.PROJECTS, self.root/'projects'
        self.runs = 0

    def tearDown(self):
        suite.PROJECTS = self.projects
        self.temp.cleanup()

    def probe(self, name, gate=None):
        (suite.PROJECTS/name/'Reference').mkdir(parents=True)
        if gate is not None:
            (suite.PROJECTS/name/suite.GATE_FILE).write_text(gate if isinstance(gate, str) else json.dumps(gate))
        return suite.recipe_for(name, {}, '6000.0.71f1')

    def run_recipe(self, recipe):
        self.runs += 1
        context = {'work_root': self.root/f'work{self.runs}', 'env': dict(os.environ),
                   'values': {'python': sys.executable, 'label': 'test', 'cli': '/x/unbound', 'managed': '/x/managed'}}
        return suite.run_probe('p', recipe, context)

    def test_nothing_declared_is_no_recipe(self):
        recipe = self.probe('quiet')
        self.assertIsInstance(recipe, suite.NoRecipe)
        self.assertIn('no gate.json', recipe.reason)
        self.assertEqual(self.run_recipe(recipe)['status'], 'no recipe')

    def test_an_explicit_recipe_wins(self):
        self.probe('both', {'commands': ['exit 1']})
        explicit = [suite.Step('compare', ['true'])]
        self.assertIs(suite.recipe_for('both', {'both': explicit}, '6000.0.71f1'), explicit)

    def test_a_wrong_declaration_is_no_recipe(self):
        for name, gate, reason in [('notjson', '{', 'not JSON'), ('list', '[]', 'not a JSON object'),
                                   ('empty', {'commands': []}, 'non-empty list'),
                                   ('blank', {'commands': [' ']}, 'non-empty list'),
                                   ('typo', {'command': ['true']}, 'unknown keys: command'),
                                   ('regex', {'commands': ['true'], 'summary': '('}, 'not a regex'),
                                   ('release', {'commands': ['run.py --release 2022.3.10f1']}, '2022.3.10f1')]:
            recipe = self.probe(name, gate)
            self.assertIsInstance(recipe, suite.NoRecipe, name)
            self.assertIn(reason, recipe.reason, name)
        self.assertNotIsInstance(self.probe('same', {'commands': ['run.py --release 6000.0.71f1']}), suite.NoRecipe)

    def test_commands_run_in_order_from_the_repository_with_the_engine_set(self):
        recipe = self.probe('ok', {'commands': [
            'test "$UNBOUND_CLI" = /x/unbound && test "$UNBOUND_MANAGED_DIR" = /x/managed && test -d tools/agents',
            # Braces reach the shell untouched: no {placeholder} substitution.
            'echo "${NOT_SET:-3}/3 records match; 0 differ {}"; echo trailing noise'],
            'summary': r'\d+/\d+ records match.*'})
        result = self.run_recipe(recipe)
        self.assertEqual(result['status'], 'pass', result['summary'])
        self.assertEqual(result['checks']['passed'], 2)
        self.assertTrue(result['summary'].endswith('3/3 records match; 0 differ {}'), result['summary'])
        self.assertIn("bash -c 'echo", result['steps'][1]['command'])

    def test_a_failing_command_fails_and_the_last_line_is_the_default_summary(self):
        recipe = self.probe('bad', {'commands': ['echo "1/2 records match"; exit 1', 'echo after']})
        result = self.run_recipe(recipe)
        self.assertEqual(result['status'], 'fail')
        self.assertEqual(result['checks'], {'passed': 1, 'failed': 1, 'report': 0, 'total': 2})
        self.assertEqual(result['summary'], '1/2 records match || after')

    def test_metadata_only_declarations_need_a_builtin_recipe(self):
        self.probe('metadata', {'engine_paths': ['managed/Animation/']})
        self.assertIsInstance(suite.recipe_for('metadata', {}, '6000.0.71f1'), suite.NoRecipe)
        explicit = [suite.Step('compare', ['true'])]
        self.assertIs(suite.recipe_for('metadata', {'metadata': explicit}, '6000.0.71f1'), explicit)

    def test_landing_metadata_is_validated_without_changing_suite_commands(self):
        recipe = self.probe('selected', {'commands': ['echo ok'], 'engine_paths': ['managed/Animation/'],
                                         'fast_commands': ['echo quick']})
        self.assertEqual(len(recipe), 1)
        self.assertEqual(self.run_recipe(recipe)['status'], 'pass')
        self.assertIsInstance(self.probe('badpaths', {'commands': ['true'], 'engine_paths': '../outside'}), suite.NoRecipe)
        self.assertIsInstance(self.probe('badfast', {'commands': ['true'], 'fast_commands': [False]}), suite.NoRecipe)

    def test_summary_regex_takes_every_match(self):
        text = ('{"tested": 9, "failed": 3, "pcm_steps": 0, "roots": ["/a"]}\n'
                '{"tested": 8, "failed": 0, "pcm_steps": 1, "roots": ["/a"]}\nreports: /tmp/x\n') * 2
        pattern = json.loads((Path(__file__).resolve().parents[2]/'tests/projects/mixerreverb/gate.json')
                             .read_text())['summary']
        self.assertEqual(suite.matched_summary(text, pattern), '"tested": 8, "failed": 0 | "tested": 8, "failed": 0')
        self.assertEqual(suite.matched_summary('a\nb\n', 'nothing'), 'b')

    def test_committed_declarations_are_valid(self):
        suite.PROJECTS = self.projects
        for path in sorted(self.projects.glob('*/' + suite.GATE_FILE)):
            recipe = suite.declared(path.parent.name, '6000.0.71f1')
            self.assertNotIsInstance(recipe, suite.NoRecipe, f'{path}: {getattr(recipe, "reason", "")}')


class EditorGuardTests(unittest.TestCase):
    """A suite step that starts the Unity Editor, or queues an Editor job, is stopped and fails."""

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.poll, probe_runtime.EDITOR_POLL = probe_runtime.EDITOR_POLL, 0.1

    def tearDown(self):
        probe_runtime.EDITOR_POLL = self.poll
        self.temp.cleanup()

    def run_recipe(self, recipe):
        context = {'work_root': self.root/'work', 'env': dict(os.environ),
                   'values': {'python': sys.executable, 'label': 'test', 'cli': '/x/unbound', 'managed': '/x/managed'}}
        started = time.time()
        result = suite.run_probe('p', recipe, context)
        self.assertLess(time.time() - started, 20, 'the step must be stopped, not waited for')
        return result

    def fake_editor(self):
        """The path of an Editor executable, <release>/Editor/Unity (the fake runs `sleep` under it)."""
        return self.root/'Hub/Editor/6000.0.71f1/Editor/Unity'

    def test_a_step_that_starts_the_editor_fails_and_stops(self):
        editor = self.fake_editor()
        marker = self.root/'after'
        recipe = [suite.Step('driver', ['bash', '-c', f'(exec -a {editor} sleep 60); touch {marker}']),
                  suite.Step('compare', ['true'])]
        result = self.run_recipe(recipe)
        self.assertEqual(result['status'], 'fail')
        self.assertEqual(result['failed_step'], 1)
        self.assertIn('step 1 (driver) launched the Unity Editor', result['summary'])
        self.assertIn(str(editor), result['summary'])
        self.assertEqual(len(result['steps']), 1, 'later steps do not run')
        self.assertTrue(result['steps'][0]['editor'])
        self.assertFalse(marker.exists())

    def test_a_queued_editor_job_fails(self):
        recipe = [suite.Step('compare', ['bash', '-c', 'sleep 60; true', 'with-heavy-job', '--kind', 'editor-cpu'])]
        result = self.run_recipe(recipe)
        self.assertEqual(result['status'], 'fail')
        self.assertIn('launched the Unity Editor', result['summary'])

    def test_an_engine_job_or_an_editor_path_argument_is_not_an_editor(self):
        editor = self.fake_editor()
        recipe = [suite.Step('compare', ['bash', '-c', 'sleep 1; echo "1/1 records match"', 'with-heavy-job',
                                         '--kind', 'engine-cpu', '--unity', str(editor.parent)])]
        result = self.run_recipe(recipe)
        self.assertEqual(result['status'], 'pass', result['summary'])

    def test_editor_runner_refuses_in_a_suite_run(self):
        sys.path.insert(0, str(Path(__file__).resolve().parents[2]/'tests'))
        try:
            import editor_runner
        finally:
            sys.path.pop(0)
        job = editor_runner.EditorJob(self.root/'project', self.root/'editor.log', self.root/'out',
                                      editor=self.root/'none')
        with unittest.mock.patch.dict(os.environ, {suite.NO_EDITOR: '1'}):
            with self.assertRaises(SystemExit) as stop:
                editor_runner.run(job)
        self.assertIn('refusing to start the Unity Editor', str(stop.exception))
        self.assertFalse((self.root/'out').exists())

    def test_no_recipe_or_declared_gate_runs_a_native_driver(self):
        """Native drivers author in the Editor; suite recipes use the committed Authored/ sets."""
        release = '6000.0.71f1'
        table = suite.recipes(release)
        for name in suite.discover():
            recipe = suite.recipe_for(name, table, release)
            if isinstance(recipe, suite.NoRecipe):
                continue
            for step in recipe:
                command = ' '.join(step.argv)
                self.assertNotRegex(command, r'\bnative\.py\b|--native(?![-\w])', f'{name}: {command}')


class ReleaseTests(unittest.TestCase):
    """land.py gives every --probe command $UNBOUND_PROBE_UNITY_VERSION."""

    def test_the_named_probe_or_the_default_gives_the_release(self):
        root = Path(__file__).resolve().parents[2]
        with unittest.mock.patch.dict(os.environ):
            os.environ.pop(land.RELEASE_VARIABLE, None)
            self.assertEqual(land.default_release(), '6000.0.71f1')
            self.assertEqual(land.probe_release('python3 tests/projects/mixerduck/run.py --release '
                                                '"$UNBOUND_PROBE_UNITY_VERSION"', root), '6000.0.71f1')
            self.assertEqual(land.probe_release('true', root), land.default_release())
            os.environ[land.RELEASE_VARIABLE] = '2022.3.10f1'
            self.assertEqual(land.probe_release('python3 tests/projects/mixerduck/run.py', root), '2022.3.10f1')

    def test_every_probe_command_gets_the_release(self):
        with tempfile.TemporaryDirectory() as temp, unittest.mock.patch.dict(os.environ):
            os.environ.pop(land.RELEASE_VARIABLE, None)
            probes = ['test "$UNBOUND_PROBE_UNITY_VERSION" = 6000.0.71f1',
                      'python3 -c "import os,sys; sys.exit(os.environ[\'UNBOUND_PROBE_UNITY_VERSION\'] != \'6000.0.71f1\')"']
            gate = land.Gate(Path(__file__).resolve().parents[2], 'HEAD', probes, False, Path(temp)/'gate.log')
            with contextlib.redirect_stdout(io.StringIO()):
                gate.run_probes()  # a probe that exits nonzero ends the gate with exit 5
            os.environ[land.RELEASE_VARIABLE] = '2022.3.10f1'
            gate = land.Gate(gate.worktree, 'HEAD',
                             ['test "$UNBOUND_PROBE_UNITY_VERSION" = 2022.3.10f1'], False, gate.log)
            with contextlib.redirect_stdout(io.StringIO()):
                gate.run_probes()
            gate.probes = ['test -n "$NOT_THE_RELEASE_${UNBOUND_PROBE_UNITY_VERSION:?}" && exit 1']
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit) as stop:
                    gate.run_probes()
            self.assertEqual(stop.exception.code, 5)


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
        source = compiled_from or self.tree/'crates/unbound'
        cli.write_text(f'#!/bin/sh\n# {source}-host\n# {source}\n{script}\n')
        cli.chmod(0o755)
        gate = land.Gate(self.tree, 'HEAD', [], False, Path(self.temp.name)/'gate.log')
        step = gate.step
        # The stand-in checkout has no .NET project. Exercise the real shell CLI
        # and generated-file checks, with the separate tooling-test step stubbed.
        gate.step = lambda name, *args, **kwargs: (0 if name == 'API tooling tests' else step(name, *args, **kwargs))
        return gate, cli

    def run_stubs(self, script, compiled_from=None):
        gate, cli = self.gate(script, compiled_from)
        try:
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                gate.generate_stubs(cli)
                gate.api_checks(cli)
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
        self.assertEqual(self.run_stubs(f'touch {marker}', compiled_from='/elsewhere/crates/unbound'), 5)
        self.assertFalse(marker.exists())
        # Another crate's directory of this tree is not the CLI's own.
        self.assertEqual(self.run_stubs(f'touch {marker}', compiled_from=self.tree/'crates/unbound-host'), 5)
        self.assertFalse(marker.exists())


class PartialRecipeTests(unittest.TestCase):
    def test_direct_thread_captures_stage_fixtures_and_require_completion(self):
        spec = importlib.util.spec_from_file_location('thread_direct', suite.PROJECTS/'threadapis/run.py')
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp).resolve()  # macOS: /var/folders is /private/var/folders
            for index, (followups, content, engine_exit, expected) in enumerate([
                (False, '{"complete":true}\n', 0, 0),
                (True, '{"complete":true}\n', 0, 0),
                (True, None, 0, 1),
                (True, '{"thread":"state","call":"partial","result":"ok"}\n', 0, 1),
                (True, '{broken', 0, 1),
                (True, '{"complete":true}\n{"complete":true}\n', 0, 1),
                (True, '{"complete":true}\n', 7, 7),
            ]):
                with self.subTest(followups=followups, content=content, engine_exit=engine_exit):
                    output = root/(str(index)+'.jsonl')
                    def engine(argv, env):
                        project = Path(argv[argv.index('-p')+1])
                        self.assertNotEqual(project, module.PROBE)
                        self.assertEqual((project/'Assets/Resources/HumanA.fbx').exists(), followups)
                        self.assertEqual((project/'Assets/Resources/HumanA.fbx.meta').exists(), followups)
                        variable = 'THREAD_FOLLOWUP_OUT' if followups else 'THREAD_APIS_OUT'
                        self.assertEqual(env[variable], str(output))
                        self.assertNotIn('THREAD_APIS_OUT' if followups else 'THREAD_FOLLOWUP_OUT', env)
                        if content is not None:
                            output.write_text(content)
                        return engine_exit
                    with unittest.mock.patch.object(module, 'work', return_value=root/('run'+str(index))), \
                         unittest.mock.patch.object(module, 'LAUNCHER', root/'absent'), \
                         unittest.mock.patch.object(module.subprocess, 'call', side_effect=engine), \
                         contextlib.redirect_stderr(io.StringIO()):
                        self.assertEqual(module.unbound(output, '/fake/unbound', followups), expected)
            existing = root/'existing.jsonl'
            existing.write_text('original')
            with unittest.mock.patch.object(module.subprocess, 'call') as call, contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(module.unbound(existing, '/fake/unbound', True), 1)
                call.assert_not_called()
                self.assertEqual(existing.read_text(), 'original')

    def test_thread_human_fixture_is_copied_unchanged_and_declares_its_dependency(self):
        spec = importlib.util.spec_from_file_location('thread_driver', suite.PROJECTS/'threadapis/run.py')
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        declaration = json.loads((suite.PROJECTS/'threadapis/gate.json').read_text())
        with tempfile.TemporaryDirectory() as temp:
            project = Path(temp)
            (project/'Assets/Resources').mkdir(parents=True)
            module.add_human_fixture(project)
            for name in ('HumanA.fbx', 'HumanA.fbx.meta'):
                source = suite.PROJECTS/'humanoid/Assets/Resources'/name
                self.assertEqual((project/'Assets/Resources'/name).read_bytes(), source.read_bytes())
                self.assertIn(str(source.relative_to(suite.REPO)), declaration['engine_paths'])

    def test_thread_capture_rejects_duplicate_observations_and_trailing_data(self):
        spec = importlib.util.spec_from_file_location('thread_compare', suite.PROJECTS/'threadapis/compare.py')
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        row = '{"thread":"main","call":"get","result":"ok"}\n'
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp)/'capture.jsonl'
            for content in [row+row+'{"complete":true}\n', row+'{"complete":true}\n'+row]:
                path.write_text(content)
                with self.assertRaises(ValueError):
                    module.load(path)
            path.write_text(row+'{"complete":true}\n')
            values, complete = module.load(path)
            self.assertTrue(complete)
            self.assertEqual(values, {('main', 'get'): 'ok'})

    def test_rtdepth_runs_on_vulkan_too_on_linux_only(self):
        for macos, expected in ((False, True), (True, False)):
            with unittest.mock.patch.object(suite, 'MACOS_ARM64', macos):
                steps = suite.recipes('6000.0.71f1')['rtdepth']
            self.assertEqual(any('--device' in step.argv for step in steps), expected and
                             (suite.PROJECTS/'rtdepth/Reference/unity-6000.0.71f1/vulkan').is_dir())

    def test_uncommitted_probe_fonts_go_to_the_staged_project_only(self):
        fonts, engine, compare = suite.recipes('6000.0.71f1')['textcore']
        self.assertEqual(fonts.kind, 'driver')
        self.assertEqual(fonts.argv[1:3], ['{probe}/fonts.py', '{project}'])
        self.assertIn('{project}', engine.argv)
        self.assertNotIn('{probe}', engine.argv)
        self.assertEqual(compare.kind, 'compare')

    def test_promotion_records_the_recent_baselines_durations(self):
        with tempfile.TemporaryDirectory() as temporary, unittest.mock.patch.object(suite, 'AGENTS', Path(temporary)), \
                contextlib.redirect_stdout(io.StringIO()):
            def promote(label, seconds):
                summary = Path(temporary)/label/'summary.json'
                summary.parent.mkdir()
                summary.write_text(json.dumps({'label': label, 'probes': {
                    'a': {'seconds': seconds, 'release': '6000.0.71f1', 'status': 'pass', 'summary': 'long'},
                    'broken': 'not a probe entry'}}))
                suite.promote(summary)
            path = Path(temporary)/'baseline'/suite.DURATIONS
            for number in range(1, 8):
                promote(f'run{number}', number*10)
            recorded = json.loads(path.read_text())['baselines']
            self.assertEqual([item['label'] for item in recorded], [f'run{n}' for n in range(3, 8)])
            self.assertEqual(recorded[-1]['probes'],
                             {'a': {'seconds': 70, 'release': '6000.0.71f1', 'status': 'pass'}})
            self.assertEqual(json.loads((Path(temporary)/'baseline/summary.json').read_text())['label'], 'run7')
            # A label promoted again is one entry, the newest.
            shutil.rmtree(Path(temporary)/'run5')
            promote('run5', 55)
            recorded = json.loads(path.read_text())['baselines']
            self.assertEqual([item['label'] for item in recorded], ['run3', 'run4', 'run6', 'run7', 'run5'])
            # An unreadable record starts again.
            path.write_text('{')
            promote('run8', 80)
            self.assertEqual([item['label'] for item in json.loads(path.read_text())['baselines']], ['run8'])

    def test_a_probe_reading_the_clipboard_runs_on_its_own_display(self):
        table = suite.recipes('6000.0.71f1')
        engine, compare = table['editorui']
        self.assertEqual(engine.kind, 'engine')
        self.assertEqual(engine.argv[:4], ['{python}', '{repo}/tests/private_display.py', '--', '{cli}'])
        self.assertIn('--headless', engine.argv)
        self.assertEqual(compare.kind, 'compare')
        # The other probes of its recipe group stay on the shared display.
        self.assertEqual(table['settings'][0].argv[0], '{cli}')

    def test_color_variants_have_independent_settings_outputs_and_comparisons(self):
        table = suite.recipes('6000.0.71f1')
        for name, variants in [('uicolors', ('Linear', 'Gamma')),
                               ('uirectclipping', ('Linear', 'Gamma', 'LinearMSAA'))]:
            steps = table[name]
            self.assertEqual(len(steps), len(variants)*3)
            for index, variant in enumerate(variants):
                prepare, engine, compare = steps[index*3:index*3+3]
                self.assertEqual(prepare.argv[-1], 'Gamma' if variant == 'Gamma' else 'Linear')
                self.assertIn('{work}/'+variant+'/project', engine.argv)
                self.assertIn('{work}/'+variant+'/out', compare.argv)
                self.assertIn('{probe}/Reference/'+variant, compare.argv)
                self.assertEqual(engine.env.get('RECT_CLIP_MSAA'), ('1' if variant == 'LinearMSAA' else '0') if name == 'uirectclipping' else None)
                self.assertEqual(compare.verdict, 'exit')

    def test_color_space_changes_only_private_project_and_rejects_reuse(self):
        sys.path.insert(0, str(suite.REPO/'tests'))
        try:
            from project_variant import prepare
            with tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                source = root/'source'
                for part in ('Assets', 'Packages', 'ProjectSettings'):
                    (source/part).mkdir(parents=True)
                settings = source/'ProjectSettings/ProjectSettings.asset'
                settings.write_text('PlayerSettings:\n  m_ActiveColorSpace: 1\n')
                for space, number in [('Gamma', 0), ('Linear', 1)]:
                    project = prepare(source, root/space, space)
                    self.assertIn(f'm_ActiveColorSpace: {number}', (project/'ProjectSettings/ProjectSettings.asset').read_text())
                    self.assertEqual(settings.read_text(), 'PlayerSettings:\n  m_ActiveColorSpace: 1\n')
                    with self.assertRaises(SystemExit):
                        prepare(source, root/space, space)
        finally:
            sys.path.pop(0)

    def test_color_variant_rejects_missing_or_ambiguous_color_setting(self):
        sys.path.insert(0, str(suite.REPO/'tests'))
        try:
            from project_variant import prepare
            with tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                source = root/'source'
                for part in ('Assets', 'Packages', 'ProjectSettings'):
                    (source/part).mkdir(parents=True)
                for index, text in enumerate(['PlayerSettings:\n', 'm_ActiveColorSpace: 0\nm_ActiveColorSpace: 1\n']):
                    (source/'ProjectSettings/ProjectSettings.asset').write_text(text)
                    with self.assertRaises(ValueError):
                        prepare(source, root/str(index), 'Gamma')
        finally:
            sys.path.pop(0)

    def test_contact_controls_gate_stable_values_and_retain_hash_differences(self):
        folder = suite.PROJECTS/'contactevents3d'
        sys.path.insert(0, str(folder))
        try:
            spec = importlib.util.spec_from_file_location('contact_controls', folder/'verify_controls.py')
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
            reference = {'scene/0/hash': '1', 'scene/4/defaultHash': '1', 'stable': 'True'}
            candidate = dict(reference, **{'scene/0/hash': '3', 'scene/4/defaultHash': '3'})
            raw, failures = module.verify(reference, candidate)
            self.assertEqual(len(raw['differences']), 2)
            self.assertFalse(failures)
            for changed in [dict(candidate, **{'scene/4/defaultHash': '4'}), dict(candidate, stable='False'), dict(candidate, extra='x'),
                            {'scene/0/hash': '3', 'scene/4/defaultHash': '2'},
                            dict(candidate, **{'scene/0/hash': 'bad'}),
                            dict(candidate, **{'scene/0/hash': str(2**31)}),
                            {key: value for key, value in candidate.items() if key != 'scene/4/defaultHash'}]:
                self.assertTrue(module.verify(reference, changed)[1])
            steps = suite.recipes('6000.0.71f1')['contactevents3d']
            # The raw comparison is a record step: kept in full, deciding nothing (its exit code
            # is 0 with the hash differences); verify_controls.py decides.
            raw = next(s for s in steps if 'compare.py' in ' '.join(s.argv) and 'controls-p0.json' in ' '.join(s.argv))
            strict = next(s for s in steps if 'verify_controls.py' in ' '.join(s.argv))
            self.assertEqual(raw.kind, 'driver')
            self.assertEqual(strict.kind, 'compare')
            self.assertEqual(strict.verdict, 'exit')
            with tempfile.TemporaryDirectory() as cache:
                (Path(cache)/'unbound').mkdir()
                ref, cand = Path(cache)/'reference.jsonl', Path(cache)/'candidate.jsonl'
                ref.write_text(''.join(json.dumps({'key': k, 'value': v}) + '\n' for k, v in reference.items()))
                cand.write_text(''.join(json.dumps({'key': k, 'value': v}) + '\n' for k, v in candidate.items()))
                done = subprocess.run([sys.executable, str(folder/'compare.py'), str(ref), str(cand), '--output',
                                       str(Path(cache)/'unbound'/'controls.json')],
                                      env=dict(os.environ, XDG_CACHE_HOME=cache), capture_output=True, text=True)
                self.assertEqual(done.returncode, 0, done.stderr)
                self.assertIn("'differences': 2", done.stdout)
        finally:
            sys.path.pop(0)


class CheckoutGuardTests(unittest.TestCase):
    """No probe run may write into the checkout (probe_runtime.checkout_state/checkout_writes)."""

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        git = lambda *args: subprocess.run(['git', *args], cwd=self.root, check=True, capture_output=True)
        git('init', '-q')
        for name in ('a/Assets/Kept.cs', 'a/Assets/Edited.cs', 'a/Assets/Removed.cs', 'b/Assets/Other.cs'):
            path = self.root/'tests/projects'/name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(name)
        (self.root/'.gitignore').write_text('Library/\n')
        git('add', '.')
        git('-c', 'user.name=t', '-c', 'user.email=t@t', 'commit', '-q', '-m', 'probes')
        # A developer's uncommitted edit and untracked file, present before the run.
        (self.root/'tests/projects/a/Assets/Edited.cs').write_text('uncommitted edit')
        (self.root/'tests/projects/a/Assets/Untracked.cs').write_text('untracked')

    def test_every_file_a_run_wrote_is_listed(self):
        before = probe_runtime.checkout_state(self.root, ('tests/projects',))
        self.assertEqual(probe_runtime.checkout_writes(self.root, before, ('tests/projects',)), [])
        projects = self.root/'tests/projects'
        (projects/'a/Assets/Kept.cs.meta').write_text('guid')                 # created
        (projects/'a/Assets/Edited.cs').write_text('uncommitted edit, then the run')  # already dirty
        (projects/'a/Assets/Removed.cs').unlink()                            # removed
        (projects/'a/Library').mkdir()
        (projects/'a/Library/cache').write_text('ignored')                   # ignored: not the checkout's
        self.assertEqual(probe_runtime.checkout_writes(self.root, before, ('tests/projects',)),
                         [' M tests/projects/a/Assets/Edited.cs', '?? tests/projects/a/Assets/Kept.cs.meta',
                          ' D tests/projects/a/Assets/Removed.cs'])
        # Only the named paths: another probe's project is checked on its own.
        self.assertEqual(probe_runtime.checkout_writes(self.root, before, ('tests/projects/b',)), [])
        # A run that restores a file to its committed text changed it too.
        (projects/'a/Assets/Edited.cs').write_text('a/Assets/Edited.cs')
        self.assertIn(' M tests/projects/a/Assets/Edited.cs (restored)',
                      probe_runtime.checkout_writes(self.root, before, ('tests/projects/a',)))
        (projects/'a/Assets/Untracked.cs').unlink()
        self.assertIn('?? tests/projects/a/Assets/Untracked.cs (removed)',
                      probe_runtime.checkout_writes(self.root, before, ('tests/projects/a',)))

    def test_staging_or_committing_during_a_run_is_not_a_write(self):
        before = probe_runtime.checkout_state(self.root, ('tests/projects',))
        git = lambda *args: subprocess.run(['git', *args], cwd=self.root, check=True, capture_output=True)
        git('add', 'tests/projects/a/Assets/Untracked.cs')
        self.assertEqual(probe_runtime.checkout_writes(self.root, before, ('tests/projects',)), [])
        git('-c', 'user.name=t', '-c', 'user.email=t@t', 'commit', '-q', '-am', 'work in progress')
        self.assertEqual(probe_runtime.checkout_writes(self.root, before, ('tests/projects',)), [])
        # A file committed during the run and then changed by it still counts.
        (self.root/'tests/projects/a/Assets/Untracked.cs').write_text('changed by the run')
        self.assertEqual(probe_runtime.checkout_writes(self.root, before, ('tests/projects',)),
                         [' M tests/projects/a/Assets/Untracked.cs'])

    def test_a_tree_outside_any_checkout_has_no_state(self):
        with tempfile.TemporaryDirectory() as scratch:
            self.assertEqual(probe_runtime.checkout_state(scratch), {})

    def test_a_probe_that_wrote_into_the_checkout_fails(self):
        passed = {'probe': 'a', 'status': 'pass', 'summary': '3/3 match'}
        self.assertIs(suite.note_checkout_writes(passed, []), passed)
        failed = suite.note_checkout_writes(passed, ['?? tests/projects/a/Assets/Kept.cs.meta'])
        self.assertEqual(failed['status'], 'fail')
        self.assertEqual(failed['checkout_writes'], ['?? tests/projects/a/Assets/Kept.cs.meta'])
        self.assertTrue(failed['summary'].startswith('wrote into the checkout: ?? tests/projects/a/Assets/Kept.cs.meta || 3/3 match'))


if __name__ == '__main__':
    unittest.main()
