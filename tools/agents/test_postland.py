#!/usr/bin/env python3
"""Post-land checker: ranges, flake rule, bisection, reverts, reports and coalescing."""
import contextlib
import fcntl
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import land
import postland

REAL_RUN = subprocess.run


class PostlandTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.repo = self.root/'repo'
        self.repo.mkdir()
        self.state = self.root/'state'
        for patcher in (mock.patch.object(land, 'STATE', self.state),
                        mock.patch.object(land, 'integration_root', lambda: self.root/'trains'),
                        contextlib.redirect_stdout(io.StringIO())):
            patcher.__enter__()
            self.addCleanup(patcher.__exit__, None, None, None)
        self.git('init', '-q', '-b', 'main')
        self.git('config', 'user.name', 'Control')
        self.git('config', 'user.email', 'control@example.invalid')
        (self.repo/'value.txt').write_text('0\n')
        for name in ('probe', 'old', 'flaky', 'shared', 'left', 'a', 'b'):
            (self.repo/'tests/projects'/name).mkdir(parents=True)
            (self.repo/'tests/projects'/name/'gate.json').write_text('{}\n')
        self.git('add', '.')
        self.git('commit', '-qm', 'base')
        self.base = self.git('rev-parse', 'HEAD')
        self.rows = []

    def git(self, *args):
        return REAL_RUN(['git', *args], cwd=self.repo, check=True, capture_output=True, text=True).stdout.strip()

    def land_branch(self, name, files, checks):
        old = self.git('rev-parse', 'main')
        for path, text in files.items():
            (self.repo/path).write_text(text)
        self.git('add', '.')
        self.git('commit', '-qm', f'feat(control): {name}')
        new = self.git('rev-parse', 'HEAD')
        row = {'branch': name, 'old': old, 'new': new, 'commits': 1, 'request_id': name*4,
               'log': f'/logs/{name}.log', 'automatic': checks, 'probes': [], 'releases': []}
        self.rows.append(row)
        self.state.mkdir(parents=True, exist_ok=True)
        with (self.state/'landed.jsonl').open('a') as stream:
            stream.write(json.dumps(row)+'\n')
        return row

    def checker(self, outcome):
        """A checker whose checks fail where outcome(commit, check, label) is true."""
        checker = postland.Checker(self.repo)
        calls = []

        def run(commit, checks, label):
            calls.append((commit, [check[0] for check in checks], label))
            results = [(list(check), f'/logs/{label}-{commit[:7]}-{check[0]}.log', outcome(commit, check, label))
                       for check in checks]
            # outcome: False passes, True fails, or an exit code.
            return [(check, log, 1 if code is True else code) for check, log, code in results if code]
        checker.run = run
        checker.calls = calls

        def no_landing():
            raise AssertionError('a test reached the real revert landing')
        checker.land_revert = no_landing
        return checker

    def reports(self):
        return [json.loads(path.read_text()) for path in sorted((self.state/'postland').glob('*.json'))
                if path.name != 'state.json']

    def test_range_union_runs_once_and_advances_the_checked_main(self):
        shared = ['shared', 'shared-fast', 'fast check', '6000.0.71f1']
        self.land_branch('first', {'a.txt': '1'}, [shared, ['left', 'left-check', 'engine paths', '6000.0.71f1']])
        self.land_branch('second', {'b.txt': '1'}, [shared])
        checker = self.checker(lambda *args: False)
        self.assertTrue(checker.round())
        self.assertEqual(checker.calls, [(self.git('rev-parse', 'main'), ['shared', 'left'], 'main')])
        self.assertEqual(checker.state()['checked'], self.git('rev-parse', 'main'))
        # Nothing new: no further checks.
        self.assertTrue(checker.round())
        self.assertEqual(len(checker.calls), 1)
        self.assertEqual(self.reports(), [])

    def test_rows_written_before_the_checker_and_checked_landings_are_skipped(self):
        legacy = {'branch': 'legacy', 'old': self.base, 'new': self.base}
        self.state.mkdir(parents=True)
        (self.state/'landed.jsonl').write_text(json.dumps(legacy)+'\n')
        first = self.land_branch('first', {'a.txt': '1'}, [['a', 'a-check', 'fast check', 'r']])
        checker = self.checker(lambda *args: False)
        checker.save(checked=first['new'])
        second = self.land_branch('second', {'b.txt': '1'}, [['b', 'b-check', 'fast check', 'r']])
        self.assertEqual([row['branch'] for row in checker.pending(first['new'], second['new'])], ['second'])
        self.assertTrue(checker.round())
        self.assertEqual(checker.calls[0][1], ['b'])

    def test_a_failure_that_passes_alone_is_reported_and_blames_nobody(self):
        check = ['flaky', 'flaky-check', 'fast check', 'r']
        self.land_branch('first', {'a.txt': '1'}, [check])
        checker = self.checker(lambda commit, check, label: label == 'main')
        self.assertTrue(checker.round())
        self.assertEqual([call[2] for call in checker.calls], ['main', 'alone'])
        reports = self.reports()
        self.assertEqual([report['kind'] for report in reports], ['passed alone after failing beside others'])
        self.assertEqual(reports[0]['probe'], 'flaky')

    def test_a_failure_before_the_range_is_reported_without_a_revert(self):
        check = ['old', 'old-check', 'fast check', 'r']
        self.land_branch('first', {'a.txt': '1'}, [check])
        checker = self.checker(lambda *args: True)
        self.assertTrue(checker.round())
        self.assertEqual([report['kind'] for report in self.reports()], ['fails before the range'])
        self.assertEqual(self.git('rev-parse', 'main'), self.rows[-1]['new'])

    def test_bisection_reverts_the_landing_that_turned_the_check(self):
        check = ['probe', 'probe-check', 'engine paths', 'r']
        self.land_branch('first', {'a.txt': '1'}, [check])
        culprit = self.land_branch('second', {'value.txt': 'broken\n'}, [check])
        self.land_branch('third', {'c.txt': '1'}, [check])
        broken = lambda commit, check, label: (self.git('show', f'{commit}:value.txt') == 'broken')
        checker = self.checker(broken)
        landed = []

        def land_revert():
            # Stand-in for land.py: fast-forward main to the revert branch.
            head = REAL_RUN(['git', 'rev-parse', 'HEAD'], cwd=checker.worktree, capture_output=True,
                            text=True).stdout.strip()
            self.git('merge', '--ff-only', '-q', head)
            landed.append(head)
            return subprocess.CompletedProcess([], 0, 'landed', '')
        checker.land_revert = land_revert
        self.assertFalse(checker.round())
        self.assertEqual(len(landed), 1)
        self.assertEqual((self.repo/'value.txt').read_text(), '0\n')
        message = self.git('log', '-1', '--format=%B', 'main')
        self.assertIn('revert second', message)
        self.assertIn('probe', message)
        report = self.reports()[-1]
        self.assertEqual(report['kind'], 'reverted')
        self.assertEqual(report['culprit']['new'], culprit['new'])
        self.assertEqual(checker.state()['reverted'], [culprit['new']])
        # The range is checked again with the revert, so the state did not advance past it.
        self.assertNotEqual(checker.state().get('checked'), self.git('rev-parse', 'main'))
        labels = [call[2] for call in checker.calls]
        self.assertEqual(labels[:3], ['main', 'alone', 'base'])
        self.assertEqual(labels[-4:], ['confirm']*4)
        self.assertTrue(all(label == 'bisect' for label in labels[3:-4]))
        self.assertGreaterEqual(labels.count('bisect'), 1)

    def test_a_revert_that_does_not_apply_is_reported(self):
        check = ['probe', 'probe-check', 'engine paths', 'r']
        self.land_branch('first', {'value.txt': 'broken\n'}, [check])
        self.land_branch('second', {'value.txt': 'broken and edited\n'}, [])
        checker = self.checker(lambda commit, check, label: self.git('show', f'{commit}:value.txt') != '0')
        before = self.git('rev-parse', 'main')
        self.assertTrue(checker.round())
        self.assertEqual(self.git('rev-parse', 'main'), before)
        report = self.reports()[-1]
        self.assertEqual(report['kind'], 'revert does not apply')
        self.assertEqual(report['culprit']['branch'], 'first')
        self.assertEqual(subprocess.run(['git', 'status', '--porcelain'], cwd=checker.worktree,
                                        capture_output=True, text=True).stdout, '')
        self.assertNotIn('postland/', self.git('branch', '--list', 'postland/*'))

    def test_a_landing_is_never_reverted_twice(self):
        check = ['probe', 'probe-check', 'engine paths', 'r']
        culprit = self.land_branch('first', {'value.txt': 'broken\n'}, [check])
        checker = self.checker(lambda commit, check, label: self.git('show', f'{commit}:value.txt') != '0')
        checker.save(reverted=[culprit['new']])
        self.assertTrue(checker.round())
        self.assertEqual(self.reports()[-1]['kind'], 'fails after its revert')

    def test_a_running_checker_absorbs_new_requests(self):
        (self.state/'postland').mkdir(parents=True)
        with (self.state/'postland/lock').open('a') as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            with mock.patch.object(postland.Checker, 'round', side_effect=AssertionError('second checker ran')):
                self.assertEqual(postland.run(self.repo), 0)

    def test_landings_during_a_round_get_another_round(self):
        check = ['probe', 'probe-check', 'fast check', 'r']
        self.land_branch('first', {'a.txt': '1'}, [check])
        rounds = []
        original = postland.Checker.round

        def round_and_land(checker):
            rounds.append(checker.git('rev-parse', 'main'))
            checker.run = lambda commit, checks, label: []
            result = original(checker)
            if len(rounds) == 1:
                self.land_branch('second', {'b.txt': '1'}, [check])
            return result
        with mock.patch.object(postland.Checker, 'round', round_and_land):
            self.assertEqual(postland.run(self.repo), 0)
        self.assertEqual(len(rounds), 2)
        self.assertEqual(json.loads((self.state/'postland/state.json').read_text())['checked'],
                         self.git('rev-parse', 'main'))

    def test_time_limits_never_lead_to_a_revert(self):
        check = ['probe', 'probe-check', 'engine paths', 'r']
        self.land_branch('first', {'a.txt': '1'}, [check])
        self.land_branch('second', {'value.txt': 'broken\n'}, [check])
        self.land_branch('third', {'c.txt': '1'}, [check])
        middle = self.rows[1]['new']

        def outcome(commit, check, label):
            # The check really fails from the second landing on, but that bisection point times out.
            if commit == middle and label == 'bisect':
                return land.TIME_LIMIT_EXIT
            return self.git('show', f'{commit}:value.txt') == 'broken'
        checker = self.checker(outcome)
        before = self.git('rev-parse', 'main')
        self.assertTrue(checker.round())
        self.assertEqual(self.git('rev-parse', 'main'), before)
        report = self.reports()[-1]
        self.assertEqual(report['kind'], 'undecided')
        self.assertEqual(report['result'], 'time limit then time limit')

    def test_a_time_limit_alone_at_main_is_reported_without_bisecting(self):
        check = ['probe', 'probe-check', 'engine paths', 'r']
        self.land_branch('first', {'a.txt': '1'}, [check])
        checker = self.checker(lambda commit, check, label: land.TIME_LIMIT_EXIT)
        self.assertTrue(checker.round())
        self.assertEqual(self.reports()[-1]['result'], 'time limit alone')
        self.assertEqual([call[2] for call in checker.calls], ['main', 'alone'])

    def test_a_flaky_parent_blocks_the_revert(self):
        check = ['probe', 'probe-check', 'engine paths', 'r']
        culprit = self.land_branch('first', {'value.txt': 'broken\n'}, [check])
        confirms = []

        def outcome(commit, check, label):
            if label == 'confirm' and commit == culprit['old']:
                confirms.append(commit)
                return len(confirms) == 2  # passes once, then fails
            return self.git('show', f'{commit}:value.txt') == 'broken'
        checker = self.checker(outcome)
        self.assertTrue(checker.round())
        report = self.reports()[-1]
        self.assertEqual(report['kind'], 'undecided')
        self.assertEqual(report['result'], 'tip fail, parent pass then fail')

    def test_an_unbuildable_bisection_point_is_reported_and_the_checker_moves_on(self):
        check = ['probe', 'probe-check', 'engine paths', 'r']
        self.land_branch('first', {'a.txt': '1'}, [check])
        self.land_branch('second', {'value.txt': 'broken\n'}, [check])
        self.land_branch('third', {'c.txt': '1'}, [check])
        middle = self.rows[1]['new']
        checker = self.checker(lambda commit, check, label: self.git('show', f'{commit}:value.txt') == 'broken')
        original = checker.run

        def run(commit, checks, label):
            if commit == middle:
                raise postland.CheckerError('build failed')
            return original(commit, checks, label)
        checker.run = run
        self.assertTrue(checker.round())
        report = self.reports()[-1]
        self.assertEqual(report['kind'], 'undecided')
        self.assertIn('unchecked', report['result'])
        self.assertEqual(checker.state()['checked'], self.git('rev-parse', 'main'))

    def test_a_probe_added_by_the_range_is_not_blamed_on_the_base(self):
        check = ['newprobe', 'newprobe-check', 'changed files', 'r']
        self.land_branch('first', {'a.txt': '1'}, [])
        (self.repo/'tests/projects/newprobe').mkdir()
        culprit = self.land_branch('second', {'tests/projects/newprobe/gate.json': '{}'}, [check])
        checker = self.checker(lambda commit, check, label: True)
        landed = []
        checker.land_revert = lambda: landed.append(1) or subprocess.CompletedProcess([], 1, '', 'refused')
        self.assertTrue(checker.round())
        kinds = [report['kind'] for report in self.reports()]
        self.assertNotIn('fails before the range', kinds)
        self.assertNotIn('base', [call[2] for call in checker.calls])
        self.assertEqual(self.reports()[-1]['culprit']['new'], culprit['new'])
        self.assertEqual(landed, [1])

    def test_unexpected_errors_are_written_for_the_orchestrator(self):
        self.land_branch('first', {'a.txt': '1'}, [['probe', 'probe-check', 'fast check', 'r']])
        with mock.patch.object(postland.Checker, 'round', side_effect=ValueError('bad landed line')):
            self.assertEqual(postland.run(self.repo), 1)
        errors = [json.loads(path.read_text()) for path in (self.state/'postland').glob('*-checker.json')]
        self.assertEqual([error['error'] for error in errors], ['ValueError: bad landed line'])

    def test_checker_errors_are_written_for_the_orchestrator(self):
        self.land_branch('first', {'a.txt': '1'}, [['probe', 'probe-check', 'fast check', 'r']])
        with mock.patch.object(postland.Checker, 'run', side_effect=postland.CheckerError('build failed')):
            self.assertEqual(postland.run(self.repo), 1)
        errors = [json.loads(path.read_text()) for path in (self.state/'postland').glob('*-checker.json')]
        self.assertEqual([error['kind'] for error in errors], ['checker error'])
        self.assertNotIn('checked', json.loads((self.state/'postland/state.json').read_text())
                         if (self.state/'postland/state.json').exists() else {})

    def test_checks_run_in_a_locked_worktree_with_the_gates_rules(self):
        self.land_branch('first', {'a.txt': '1'}, [])
        checker = postland.Checker(self.repo)
        captured = {}

        def check_only(gate):
            captured.update(automatic=gate.automatic, env=gate.env, worktree=gate.worktree)
        with mock.patch.object(land.Gate, 'check_only', check_only), \
             mock.patch.dict(os.environ, {'UNBOUND_LAND_GATE': '1', 'CARGO_TARGET_DIR': '/elsewhere'}):
            failures = checker.run(self.git('rev-parse', 'main'),
                                   [['probe', 'python3 tools/agents/suite.py --only probe --label x', 'engine paths', 'r']],
                                   'main')
        self.assertEqual(failures, [])
        self.assertEqual(captured['worktree'], checker.worktree)
        self.assertNotIn('UNBOUND_LAND_GATE', captured['env'])
        self.assertNotIn('CARGO_TARGET_DIR', captured['env'])
        self.assertIn('--against', captured['automatic'][0][1])
        locked = self.git('worktree', 'list', '--porcelain')
        self.assertIn('locked post-land checker', locked)

    def test_failing_checks_map_to_their_requests(self):
        self.land_branch('first', {'a.txt': '1'}, [])
        checker = postland.Checker(self.repo)
        checks = [['a', 'a-check', 'fast check', 'r'], ['b', 'b-check', 'fast check', 'r']]

        def check_only(gate):
            logs = gate.log.with_suffix('.automatic')
            gate.failures = [(('auto b', 'b-check', 'r', logs/'002.log', False), 1)]
            raise SystemExit(5)
        with mock.patch.object(land.Gate, 'check_only', check_only):
            failures = checker.run(self.git('rev-parse', 'main'), checks, 'main')
        self.assertEqual([(check, code) for check, _, code in failures], [(checks[1], 1)])
        with mock.patch.object(land.Gate, 'check_only', side_effect=SystemExit(5)):
            with self.assertRaises(postland.CheckerError):
                checker.run(self.git('rev-parse', 'main'), checks, 'main')


class RequestTests(unittest.TestCase):
    def test_landing_starts_a_background_checker_and_never_fails(self):
        with tempfile.TemporaryDirectory() as temporary:
            main = Path(temporary)
            (main/'tools/agents').mkdir(parents=True)
            (main/'tools/agents/postland.py').write_text('')
            with mock.patch.object(land.subprocess, 'run') as run, contextlib.redirect_stdout(io.StringIO()), \
                 mock.patch.dict(os.environ, {'UNBOUND_LAND_GATE': '1', 'DISPLAY': ':7'}):
                land.request_postland(main)
                command = run.call_args.args[0]
                self.assertEqual(command[:2], ['systemd-run', '--user'])
                self.assertIn('--property=Nice=15', command)
                self.assertIn('--setenv=DISPLAY=:7', command)
                self.assertFalse(any('UNBOUND_LAND_GATE' in part for part in command))
                self.assertEqual(command[-3:], [str(main/'tools/agents/postland.py'), '--repo', str(main)])
                run.side_effect = subprocess.CalledProcessError(1, command, stderr='no manager')
                with contextlib.redirect_stderr(io.StringIO()) as errors:
                    land.request_postland(main)
                self.assertIn('the next landing covers this range', errors.getvalue())


if __name__ == '__main__':
    unittest.main()
