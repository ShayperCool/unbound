#!/usr/bin/env python3
"""Unit checks of the suite's regression rules, declared probe gates and the land gate's stub check:
python3 -m unittest tools/agents/test_suite.py"""
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
import unittest.mock

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
        for index, (comparator, reference, text) in enumerate(outputs):
            script = self.root/f'run{self.runs}-{index}'/comparator
            script.parent.mkdir(parents=True, exist_ok=True)
            script.write_text(f'print({text!r}, end="")\n')
            recipe.append(suite.Step('compare', ['{python}', str(script), str(self.root/reference), '{work}/out'],
                                     verdict=verdict))
        context = {'work_root': self.root/f'work{self.runs}', 'env': dict(os.environ),
                   'values': {'python': sys.executable, 'label': 'test'}}
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
        del old['images']
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
        self.poll, suite.EDITOR_POLL = suite.EDITOR_POLL, 0.1

    def tearDown(self):
        suite.EDITOR_POLL = self.poll
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
            gate.probes = ['test "$UNBOUND_PROBE_UNITY_VERSION" = 2022.3.10f1']
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
        self.assertEqual(self.run_stubs(f'touch {marker}', compiled_from='/elsewhere/crates/unbound'), 5)
        self.assertFalse(marker.exists())
        # Another crate's directory of this tree is not the CLI's own.
        self.assertEqual(self.run_stubs(f'touch {marker}', compiled_from=self.tree/'crates/unbound-host'), 5)
        self.assertFalse(marker.exists())


if __name__ == '__main__':
    unittest.main()
