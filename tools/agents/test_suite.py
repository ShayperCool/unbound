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


    def test_an_accepted_count_covers_a_rise_from_its_baseline_only(self):
        accepted = [{'baseline': 599, 'accepted': 601, 'reason': 'two last-digit tangents'}]
        old = failing('cut', failures=599)
        self.assertIsNotNone(suite.worse(old, failing('cut', failures=601)))
        self.assertIsNone(suite.worse(old, failing('cut', failures=601), accepted))
        self.assertIsNone(suite.worse(old, failing('cut', failures=600), accepted))
        # Past the accepted count, or from another baseline count, it is worse as before.
        self.assertIsNotNone(suite.worse(old, failing('cut', failures=602), accepted))
        self.assertIsNotNone(suite.worse(failing('cut', failures=598), failing('cut', failures=601), accepted))
        self.assertIsNotNone(suite.worse(failing('cut', failures=601), failing('cut', failures=602), accepted))
        # The per-step counts follow the same rule.
        steps = lambda count: dict(failing('cut', failures=count),
                                   steps=[{'status': 'pass', 'failures': 0}, {'status': 'fail', 'failures': count}])
        self.assertIn('step 2', suite.worse(steps(599), steps(601)))
        self.assertIsNone(suite.worse(steps(599), steps(601), accepted))
        self.assertIn('step 2', suite.worse(steps(599), steps(602), accepted))
        # Nothing but the count is covered: a step that passed and fails now stays worse.
        broken = dict(failing('cut', failures=601),
                      steps=[{'status': 'fail', 'failures': 0}, {'status': 'fail', 'failures': 601}])
        self.assertIn('step 1', suite.worse(steps(599), broken, accepted))

    def test_compare_runs_lists_an_accepted_rise_with_its_reason(self):
        accepted = {'p': [{'baseline': 599, 'accepted': 601, 'reason': 'two last-digit tangents'}]}
        old, new = failing('599 differences'), failing('601 differences')
        lines, regressed = suite.compare_runs({'probes': {'p': new, 'q': new}}, {'probes': {'p': old, 'q': old}},
                                              accepted)
        self.assertTrue(regressed)  # q declares nothing
        self.assertTrue(lines[0].startswith('accepted  p: 601 counted failures, was 599'), lines)
        self.assertIn('two last-digit tangents', lines[0])
        self.assertTrue(lines[1].startswith('worse     q'), lines)
        lines, regressed = suite.compare_runs({'probes': {'p': new}}, {'probes': {'p': old}}, accepted)
        self.assertFalse(regressed)

    def test_accepted_counts_come_from_valid_gate_declarations(self):
        with tempfile.TemporaryDirectory() as directory:
            projects = Path(directory)
            def declare(name, value):
                (projects/name).mkdir()
                (projects/name/'gate.json').write_text(json.dumps({'engine_paths': ['crates/'], **value}))
            entry = {'baseline': 599, 'accepted': 601, 'reason': 'decided'}
            declare('good', {'accepted_failures': [entry]})
            declare('none', {})
            declare('lower', {'accepted_failures': [dict(entry, accepted=599)]})
            declare('unexplained', {'accepted_failures': [dict(entry, reason=' ')]})
            declare('extra', {'accepted_failures': [dict(entry, step=2)]})
            names = ['good', 'none', 'lower', 'unexplained', 'extra', 'absent']
            self.assertEqual(suite.accepted_counts(names, projects), {'good': [entry]})
            for name in ('lower', 'unexplained', 'extra'):
                self.assertIsInstance(suite.gate_spec(projects/name/'gate.json'), suite.NoRecipe)
            # The entry is metadata beside a built-in recipe, not a recipe of its own.
            (projects/'good'/'gate.json').write_text(json.dumps({'accepted_failures': [entry]}))
            self.assertIsNone(suite.declared('good', '6000.0.71f1', projects))


class OracleGuardTests(unittest.TestCase):
    """The guard over accepted failure counts, in a scratch repository."""
    GUARD = Path(__file__).resolve().parent/'oracle_guard.py'
    ENTRY = {'baseline': 599, 'accepted': 601, 'reason': 'decided'}

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.repo = Path(self.directory.name)
        self.git('init', '-q', '-b', 'main')
        self.commit('base', {'engine_paths': ['crates/']})
        self.git('checkout', '-q', '-b', 'work')

    def git(self, *args):
        subprocess.run(['git', '-c', 'user.name=t', '-c', 'user.email=t@example.invalid', *args],
                       cwd=self.repo, check=True, capture_output=True)

    def commit(self, message, declaration, name='p'):
        path = self.repo/'tests/projects'/name/'gate.json'
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(declaration))
        self.git('add', '-A')
        self.git('commit', '-q', '-m', message)

    def guard(self):
        return subprocess.run([sys.executable, str(self.GUARD), 'main', 'HEAD'], cwd=self.repo,
                              capture_output=True, text=True)

    def test_a_new_accepted_count_needs_the_trailer(self):
        self.commit('raise', {'engine_paths': ['crates/'], 'accepted_failures': [self.ENTRY]})
        refused = self.guard()
        self.assertEqual(refused.returncode, 4, refused.stdout)
        self.assertIn('accepted failure count declared', refused.stdout)
        self.git('commit', '-q', '--amend', '-m', 'raise\n\nOracle-Change: decided by the owner')
        self.assertEqual(self.guard().returncode, 0)

    def test_a_new_declaration_file_and_a_raised_entry_need_it_too(self):
        self.commit('new probe', {'accepted_failures': [self.ENTRY]}, name='q')
        self.assertEqual(self.guard().returncode, 4)
        self.git('reset', '-q', '--hard', 'main')
        self.git('checkout', '-q', 'main')
        self.commit('landed', {'accepted_failures': [self.ENTRY]})
        self.git('checkout', '-q', '-B', 'work')
        self.commit('higher', {'accepted_failures': [dict(self.ENTRY, accepted=605)]})
        self.assertEqual(self.guard().returncode, 4)

    def test_withdrawing_or_keeping_an_entry_is_free(self):
        self.git('checkout', '-q', 'main')
        self.commit('landed', {'accepted_failures': [self.ENTRY]})
        self.git('checkout', '-q', '-B', 'work')
        self.commit('paths', {'engine_paths': ['managed/'], 'accepted_failures': [self.ENTRY]})
        self.assertEqual(self.guard().returncode, 0)
        self.commit('withdraw', {'engine_paths': ['managed/']})
        self.assertEqual(self.guard().returncode, 0)



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


class SharedGuardTests(unittest.TestCase):
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
            with unittest.mock.patch.object(probe_runtime.sys, 'platform', 'darwin'):
                with self.assertRaisesRegex(RuntimeError, 'unsupported on darwin'):
                    gate.run_probes()
                with self.assertRaisesRegex(RuntimeError, 'unsupported on darwin'):
                    suite.run_step(suite.Step('driver', ['touch', 'unsafe']), {}, Path(temp)/'log', {})
            self.assertFalse((Path(temp)/'unsafe').exists())

    def test_gate_stops_an_editor_launch_and_does_not_run_later_commands(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            editor = root/'6000.0.71f1/Editor/Unity'
            probes = ['bash -c '+shlex.quote(f'(exec -a {editor} sleep 60); touch after'), 'touch later']
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
            child = ('import os,signal,time; from pathlib import Path; '
                     'signal.signal(signal.SIGTERM, signal.SIG_IGN); '
                     f'Path({str(pidfile)!r}).write_text(str(os.getpid())); time.sleep(60)')
            editor = root/'6000.0.71f1/Editor/Unity'
            command = 'bash -c '+shlex.quote(
                f'(exec -a {shlex.quote(str(editor))} {shlex.quote(sys.executable)} -c {shlex.quote(child)}); '
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
                    stat = Path(f'/proc/{pid}/stat')
                    if not stat.exists() or stat.read_text().rsplit(')', 1)[1].split()[0] in ('Z', 'X'):
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


class PartialRecipeTests(unittest.TestCase):
    def test_direct_thread_captures_stage_fixtures_and_require_completion(self):
        spec = importlib.util.spec_from_file_location('thread_direct', suite.PROJECTS/'threadapis/run.py')
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
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
            raw = next(s for s in steps if s.kind == 'compare' and 'controls-p0.json' in ' '.join(s.argv))
            strict = next(s for s in steps if 'verify_controls.py' in ' '.join(s.argv))
            self.assertEqual(raw.verdict, 'none')
            self.assertEqual(strict.verdict, 'exit')
        finally:
            sys.path.pop(0)


if __name__ == '__main__':
    unittest.main()
