#!/usr/bin/env python3
"""Post-land checks: the automatic checks of landed branches, run on main outside the land lock.

A landing's gate runs the release build, stub generation and API checks, the managed and Rust
tests, the oracle guard and the requested --probe commands. The automatic checks that land.py
selects from a branch's changed files and gate.json metadata (engine-path recipes and fast
checks) are recorded with the branch in landed.jsonl and run here, after main has advanced:

    python3 tools/agents/postland.py [--repo <main checkout>]

land.py starts it in the background after every landing (systemd-run, Nice=15). One checker
runs at a time; it covers every landing between the last checked main and the current main,
so landings that arrive while it runs are checked in its next round. It checks out main in its
own locked worktree beside the land trains, builds it, freezes the runtime and runs the union
of the landings' checks with the gate's rules: the same selection, the sheriff baseline frozen
for suite checks (--against), one rerun of time-limited checks, and the same strict comparisons.
A suite check's time limit follows its duration in the recent sheriff baselines (recorded_limits);
other checks keep the gate's 300 s. A suite check recorded too long for the checker's cap is not run; it
is reported for an explicit --probe. Holding no lock, it has no shared budget and runs as
many checks at once as the machine's free capacity allows (background_workers).

A failing check is run once more alone at the same commit, so that a load timeout or a flake
is not blamed on a landing; one that passes then is a line in postland/passed-alone.jsonl. A check that still fails is run at the range's base (unless the range
adds its probe): when it fails there too, the failure predates the range. Otherwise the landings
of the range are bisected; a non-passing result counts only when a second run agrees, and a time
limit, a flake or a commit that cannot be built ends the search with a report. When one landing
turns the check from passing to failing, confirmed by two failing runs at its tip and two passing
runs at its parent, its commits are reverted on a branch and landed through land.py (at normal
priority, as it may lead a train) if the revert applies cleanly and that landing was not reverted
before. Every finding, revert or not, is written to
~/.cache/unbound/agents/postland/<time>-<probe>.json for the orchestrator.

When the range changes managed/, the managed unit tests also run built in Debug (DEBUG_TESTS):
gates build them in Release, where Debug.Assert is compiled out. Tests that fail there and did not
fail at the previous such run are a finding (no bisection or revert). Suite output uses
fresh labels; sheriff suites and baselines are only read.
"""
import argparse
import contextlib
import datetime
import fcntl
import itertools
import json
import os
from pathlib import Path
import re
import shlex
import subprocess
import sys
import time

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import land  # noqa: E402
from suite import DURATIONS  # noqa: E402

# Background work: below ordinary jobs, which the job runner keeps at niceness 10.
BACKGROUND_NICE = 15
WORKTREE = 'postland-checker'
BUILD_ENVIRONMENT = ('CARGO_TARGET_DIR', 'CARGO_BUILD_BUILD_DIR', 'UNBOUND_MANAGED_DIR',
                     'UNBOUND_RUNTIME', 'UNBOUND', 'UNBOUND_CLI', 'UNBOUND_LAND_GATE')
# The managed unit tests built in Debug, where Debug.Assert is compiled in: land gates build them
# in Release only. Run when a checked range changes managed/; a finding names the tests that fail
# in Debug and did not fail at the last check (known-test-failures.txt still applies).
DEBUG_TESTS = ['managed-tests-debug', 'dotnet test managed/UnityEngine.Tests/UnityEngine.Tests.csproj -c Debug',
               'Debug-only assertions (land gates run Release)', None]
# Hardware threads one automatic check keeps busy (an engine run plus its driver); the checker
# runs as many checks at once as the free threads allow, at least one, at most the engine pool.
THREADS_PER_CHECK = 2


def background_workers():
    """Checks to run at once: the machine's free capacity, not the gate's driver count. Under
    load, parallel checks time each other out and then need reruns alone."""
    free = (os.cpu_count() or 1)-os.getloadavg()[0]
    return max(1, min(land.retry_workers(), int(free // THREADS_PER_CHECK)))


# A suite check's limit is this multiple of its duration in the sheriff baselines, which ran beside
# the whole suite: realtimegi took 1139 s there and 17-35 min after landings on the loaded machine
# (the margin is thin under the heaviest load). At least the gate's per-check limit. A check that
# would need more than RECORDED_LIMIT_MAX_SECONDS is not run here (each round may run a check four
# times); it is reported for an explicit --probe instead.
RECORDED_LIMIT_FACTOR = 2
RECORDED_LIMIT_MAX_SECONDS = 3600
# The duration is the shortest among the current baseline's and the complete runs (passed or
# reported) of the probe at the same release in the last RECENT_BASELINES promoted baselines
# (suite.py's DURATIONS beside the summary). A baseline that ran beside other heavy work then does
# not double a limit or put a check beyond the cap (audiopitch took 9186 s in one, most of it
# waiting for engine slots, and 4736 s in the one before); a run cut short by a failure does not
# shorten it. A probe that really got longer keeps its older, shorter time for up to two more
# promotions: a check then over its limit is run again alone before it is reported.
RECENT_BASELINES = 3
COMPLETE = ('pass', 'report')


def recent_durations(path):
    """The probe entries of the last RECENT_BASELINES promoted baselines; none when unreadable."""
    try:
        baselines = json.loads(Path(path).read_text())['baselines']
    except (OSError, ValueError, KeyError, TypeError):
        return []
    if not isinstance(baselines, list):
        return []
    return [item['probes'] for item in baselines[-RECENT_BASELINES:]
            if isinstance(item, dict) and isinstance(item.get('probes'), dict)]


def duration(seconds):
    return isinstance(seconds, (int, float)) and not isinstance(seconds, bool) and seconds > 0


def recorded_seconds(checks, summary=None):
    """{land.automatic_limit_key(check): seconds} for the suite checks whose probe the sheriff
    baseline timed at the check's release: the shortest of that time and the complete runs of the
    recent baselines (RECENT_BASELINES)."""
    summary = Path(summary or land.STATE/'baseline/summary.json')
    try:
        probes = json.loads(summary.read_text()).get('probes', {})
    except (OSError, ValueError):
        return {}
    earlier = recent_durations(summary.parent/DURATIONS)
    found = {}
    for check in checks:
        entry = probes.get(check[0])
        if land.suite_arguments(check[1]) is None or not isinstance(entry, dict):
            continue
        seconds, release = entry.get('seconds'), entry.get('release')
        if not isinstance(seconds, (int, float)) or len(check) > 3 and release and check[3] != release:
            continue
        for baseline in earlier:
            then = baseline.get(check[0])
            if (isinstance(then, dict) and then.get('status') in COMPLETE and then.get('release') == release
                    and duration(then.get('seconds'))):
                seconds = min(seconds, then['seconds'])
        found[land.automatic_limit_key(check)] = seconds
    return found


def recorded_limits(checks, summary=None):
    """Per-check time limits for the suite checks that fit under RECORDED_LIMIT_MAX_SECONDS."""
    return {key: int(max(land.AUTO_PROBE_TIMEOUT_SECONDS, RECORDED_LIMIT_FACTOR*seconds))
            for key, seconds in recorded_seconds(checks, summary).items()
            if RECORDED_LIMIT_FACTOR*seconds <= RECORDED_LIMIT_MAX_SECONDS}


def too_long(checks, summary=None):
    """[(check, recorded seconds)] of the suite checks that would need more than the cap."""
    seconds = recorded_seconds(checks, summary)
    return [(check, seconds[land.automatic_limit_key(check)]) for check in checks
            if RECORDED_LIMIT_FACTOR*seconds.get(land.automatic_limit_key(check), 0) > RECORDED_LIMIT_MAX_SECONDS]


def root():
    return land.STATE/'postland'


def stamp():
    return f'{datetime.datetime.now():%Y%m%d-%H%M%S}'


class CheckerError(Exception):
    """The checker cannot judge the range (a build failure, a dirty worktree)."""


class Checker:
    def __init__(self, repo):
        self.repo = Path(repo).resolve()
        self.root = root()
        self.root.mkdir(parents=True, exist_ok=True)
        self.worktree = land.integration_root().resolve()/WORKTREE
        # The time limits of the round's checks, read once from the baseline (see round()).
        self.limits = None

    # --- state -----------------------------------------------------------------------------
    def git(self, *args, cwd=None, check=True):
        return land.git(*args, cwd=cwd or self.repo, check=check)

    def ancestor(self, older, newer):
        return subprocess.run(['git', 'merge-base', '--is-ancestor', older, newer], cwd=self.repo,
                              capture_output=True).returncode == 0

    def state(self):
        path = self.root/'state.json'
        return json.loads(path.read_text()) if path.is_file() else {}

    def save(self, **values):
        land.atomic_json(self.root/'state.json', dict(self.state(), **values))

    def rows(self):
        path = land.STATE/'landed.jsonl'
        if not path.is_file():
            return []
        return [json.loads(line) for line in path.read_text().splitlines() if line.strip()]

    def pending(self, checked, target):
        """Landings on target after checked, in landing order. Rows written before landings
        recorded their automatic checks carry none: their gates ran those checks."""
        result, seen = [], set()
        for row in self.rows():
            if 'automatic' not in row or row['new'] in seen:
                continue
            if not self.ancestor(row['new'], target):
                continue
            if checked and self.ancestor(row['new'], checked):
                continue
            seen.add(row['new'])
            result.append(row)
        return result

    # --- checks ----------------------------------------------------------------------------
    def checkout(self, commit):
        if not self.worktree.exists():
            self.worktree.parent.mkdir(parents=True, exist_ok=True)
            self.git('worktree', 'add', '--quiet', '--detach', str(self.worktree), commit)
            self.git('worktree', 'lock', '--reason', 'post-land checker', str(self.worktree))
        try:
            land.require_clean(self.worktree, 'post-land checker worktree')
        except SystemExit as error:
            raise CheckerError(f'{self.worktree} has changes; inspect it before checks continue') from error
        self.git('switch', '--quiet', '--detach', commit, cwd=self.worktree)

    def environment(self):
        environment = dict(os.environ)
        for key in BUILD_ENVIRONMENT:
            environment.pop(key, None)
        return environment

    def run(self, commit, checks, label):
        """The failing checks of `checks` at commit as (check, log, exit code); [] when all pass."""
        self.checkout(commit)
        log = land.STATE/'logs'/f'{stamp()}-postland-{label}-{commit[:10]}.log'
        log.parent.mkdir(parents=True, exist_ok=True)
        frozen = land.freeze_automatic_baseline([tuple(check) for check in checks])
        gate = land.Gate(self.worktree, commit, [], False, log, environment=self.environment(),
                         automatic=frozen)
        # No land lock is held: no shared wall-clock budget, and probes known to take longer
        # than the gate's per-check limit get time by their recorded durations.
        gate.automatic_workers, gate.automatic_budget = background_workers(), False
        gate.automatic_limits = self.limits if self.limits is not None else recorded_limits(frozen)
        print(f'postland: {len(checks)} checks at {commit[:10]}; log {log}', flush=True)
        try:
            gate.check_only()
        except SystemExit:
            if not gate.failures:
                raise CheckerError(f'checks could not run at {commit[:10]}; log {log}')
            logs = gate.log.with_suffix('.automatic')
            failed = []
            for row, code in gate.failures:
                index = int(Path(row[3]).name.split('.')[0])
                failed.append((list(checks[index-1]), str(logs/Path(row[3]).name), code))
            return failed
        return []

    def debug_tests(self, base, target):
        """The managed unit tests in Debug at target when the range from base changes managed/;
        tests failing there that did not fail at the previous run are a finding."""
        changed = self.git('diff', '--name-only', f'{base}..{target}').splitlines()
        if not any(path.startswith('managed/') for path in changed):
            return
        self.checkout(target)
        log = land.STATE/'logs'/f'{stamp()}-postland-debug-{target[:10]}.log'
        log.parent.mkdir(parents=True, exist_ok=True)
        gate = land.Gate(self.worktree, target, [], False, log, environment=self.environment())
        print(f'postland: managed tests in Debug at {target[:10]}; log {log}', flush=True)
        try:
            _, build = gate.build()
            code, failed = gate.managed_test_failures(build, 'Debug')
        except SystemExit:
            self.report(DEBUG_TESTS, 'undecided', at=target, result='Debug tests could not run', logs=[str(log)],
                        range=[base, target])
            return
        if code and not failed:
            # A test host that crashed (an assertion on another thread) lists only finished tests.
            self.report(DEBUG_TESTS, 'undecided', at=target, result=f'Debug tests exited {code} without failing tests',
                        logs=[str(log)], range=[base, target])
            return
        failed -= land.known_test_failures()
        new = sorted(failed-set(self.state().get('debug_failures', [])))
        if new:
            self.report(DEBUG_TESTS, 'managed Debug test failures', failures=new, logs=[str(log)],
                        range=[base, target])
        self.save(debug_failures=sorted(failed))

    def outcome(self, commit, check, label):
        """('pass' | 'fail' | 'time limit' | 'unchecked', log) of one check alone at commit."""
        try:
            failed = self.run(commit, [check], label)
        except CheckerError as error:
            return 'unchecked', str(error)
        if not failed:
            return 'pass', None
        _, log, code = failed[0]
        return ('time limit' if code == land.TIME_LIMIT_EXIT else 'fail'), log

    def decide(self, commit, check, label, logs):
        """'pass' or 'fail' only when two runs agree on it; otherwise why it is undecided."""
        results = []
        for attempt in range(2):
            result, log = self.outcome(commit, check, label)
            if log:
                logs.append(log)
            results.append(result)
            if attempt == 0 and result == 'pass' and label != 'confirm':
                return 'pass'
        if results == ['pass', 'pass']:
            return 'pass'
        if results == ['fail', 'fail']:
            return 'fail'
        return ' then '.join(results)

    def exists(self, commit, check):
        """Whether the check's probe project exists at commit (a range can add it)."""
        name = check[0]
        return subprocess.run(['git', 'cat-file', '-e', f'{commit}:tests/projects/{name}'], cwd=self.repo,
                              capture_output=True).returncode == 0

    # --- outcomes --------------------------------------------------------------------------
    def report(self, check, kind, **fields):
        """Write a finding once per checked range (and culprit); returns its kind."""
        # A round that ends in a revert leaves its range unchecked; the next round checks the same
        # range again with the revert and must not write what it already wrote a second time.
        key = json.dumps([(fields.get('range') or [None])[0], *check[:4], kind,
                          (fields.get('culprit') or {}).get('new'), fields.get('result')])
        reported = self.state().get('reported', {})
        if key in reported:
            print(f'postland: {kind}: {check[0]}; already reported in {reported[key]}', flush=True)
            return kind
        name = re.sub(r'[^A-Za-z0-9._-]+', '-', str(check[0]))
        path = self.root/f'{stamp()}-{name}.json'
        for number in itertools.count(2):
            if not path.exists():
                break
            path = self.root/f'{stamp()}-{name}-{number}.json'
        value = {'time': f'{datetime.datetime.now():%F %T}', 'probe': check[0], 'command': check[1],
                 'reason': check[2], 'release': check[3] if len(check) > 3 else None, 'kind': kind, **fields}
        land.atomic_json(path, value)
        self.save(reported=dict(reported, **{key: str(path)}))
        print(f'postland: {kind}: {check[0]}; {path}', flush=True)
        return kind

    def note_flake(self, check, logs, span):
        entry = {'time': f'{datetime.datetime.now():%F %T}', 'probe': check[0], 'command': check[1],
                 'release': check[3] if len(check) > 3 else None, 'logs': logs, 'range': span,
                 'load': round(os.getloadavg()[0], 1)}
        with (self.root/'passed-alone.jsonl').open('a') as stream:
            stream.write(json.dumps(entry)+'\n')
        print(f'postland: passed alone after failing beside others: {check[0]}', flush=True)

    def land_revert(self):
        """land.py for the revert branch, at normal priority: it may lead other workers' train."""
        settings = [f'--setenv={key}={os.environ[key]}' for key in land.POSTLAND_ENVIRONMENT if key in os.environ]
        return subprocess.run(['systemd-run', '--user', '--wait', '--pipe', '--quiet', '--collect',
                               f'--unit=unbound-postland-revert-{time.time_ns()}',
                               f'--working-directory={self.worktree}', *settings,
                               sys.executable, str(self.worktree/'tools/agents/land.py')],
                              cwd=self.worktree, capture_output=True, text=True, stdin=subprocess.DEVNULL)

    def revert(self, culprit, check, logs, common):
        """Land a revert of the culprit landing; a report either way."""
        reverted = self.state().get('reverted', [])
        if culprit['new'] in reverted:
            return self.report(check, 'fails after its revert', culprit=culprit, logs=logs, **common)
        main = self.git('rev-parse', 'main')
        branch = f"postland/revert-{culprit['new'][:10]}"
        self.checkout(main)
        self.git('switch', '--quiet', '--no-track', '-C', branch, main, cwd=self.worktree)
        applied = subprocess.run(['git', 'revert', '--no-commit', f"{culprit['old']}..{culprit['new']}"],
                                 cwd=self.worktree, capture_output=True, text=True)
        if applied.returncode:
            subprocess.run(['git', 'revert', '--abort'], cwd=self.worktree, capture_output=True)
            self.git('switch', '--quiet', '--detach', main, cwd=self.worktree)
            self.git('branch', '-D', branch, cwd=self.worktree)
            return self.report(check, 'revert does not apply', error=applied.stderr.strip(), culprit=culprit,
                               logs=logs, **common)
        message = (f"fix(postland): revert {culprit['branch']}\n\n"
                   f"Its landing ({culprit['old'][:10]}..{culprit['new'][:10]}) turned the automatic check "
                   f"{check[0]} from passing to failing on main: twice failing at the landing's tip, "
                   f"twice passing at its parent.\n")
        self.git('commit', '--quiet', '-m', message, cwd=self.worktree)
        landing = self.land_revert()
        if landing.returncode:
            kind = self.report(check, 'revert did not land', land_exit=landing.returncode,
                               land_output=(landing.stdout+landing.stderr)[-4000:], culprit=culprit,
                               logs=logs, **common)
        else:
            self.save(reverted=[*reverted, culprit['new']])
            kind = self.report(check, 'reverted', revert=self.git('rev-parse', 'HEAD', cwd=self.worktree),
                               culprit=culprit, logs=logs, **common)
        self.git('switch', '--quiet', '--detach', self.git('rev-parse', 'main'), cwd=self.worktree)
        self.git('branch', '-D', branch, cwd=self.worktree, check=False)
        return kind

    def isolate(self, rows, check, logs, target):
        """Report a confirmed failure, and revert its landing when one landing is the clear cause.
        Only two agreeing runs decide; a time limit, a flake or an unbuildable commit is reported."""
        base = rows[0]['old']
        common = {'range': [base, target], 'landings': [
            {key: row.get(key) for key in ('branch', 'old', 'new', 'request_id', 'log')} for row in rows]}
        # A probe the range adds cannot pass or fail before it exists. A probe missing at main, or
        # missing after it existed (removed or renamed in the range), decides nothing.
        present = [self.exists(commit, check) for commit in [base, *(row['new'] for row in rows)]]
        if not present[-1] or any(earlier and not later for earlier, later in zip(present, present[1:])):
            return self.report(check, 'undecided', at=target, result='probe missing', logs=logs, **common)
        added = not present[0]

        def result_at(commit, label):
            # Only commits before the probe's first appearance lack it here.
            if self.exists(commit, check):
                return self.decide(commit, check, label, logs)
            return 'pass'
        if not added:
            result = self.decide(base, check, 'base', logs)
            if result == 'fail':
                return self.report(check, 'fails before the range', logs=logs, **common)
            if result != 'pass':
                return self.report(check, 'undecided', at=base, result=result, logs=logs, **common)
        low, high = -1, len(rows)-1  # rows[low]['new'] (or base) passes, rows[high]['new'] fails
        while high-low > 1:
            middle = (low+high)//2
            commit = rows[middle]['new']
            result = result_at(commit, 'bisect')
            if result == 'pass':
                low = middle
            elif result == 'fail':
                high = middle
            else:
                return self.report(check, 'undecided', at=commit, result=result, logs=logs, **common)
        culprit = {key: rows[high].get(key) for key in ('branch', 'old', 'new', 'request_id', 'log')}
        before = rows[high-1]['new'] if high else base
        if before != culprit['old']:
            # Another landing sits between them on main; no single landing is the clear cause.
            return self.report(check, 'no single landing', culprit=culprit, logs=logs, **common)
        # Confirm before touching main: twice failing at the tip, twice passing at the parent.
        tip = result_at(culprit['new'], 'confirm')
        parent = result_at(culprit['old'], 'confirm')
        if (tip, parent) != ('fail', 'pass'):
            return self.report(check, 'undecided', at=culprit['new'], result=f'tip {tip}, parent {parent}',
                               culprit=culprit, logs=logs, **common)
        return self.revert(culprit, check, logs, common)

    def round(self):
        """Check every landing up to main; True when main was reached."""
        target = self.git('rev-parse', 'main')
        checked = self.state().get('checked')
        rows = self.pending(checked, target)
        if rows:
            self.debug_tests(checked or rows[0]['old'], target)
        checks = land.automatic_union([[tuple(row) for row in item['automatic']] for item in rows])
        if not checks:
            self.save(checked=target, reported={})
            return True
        span = [rows[0]['old'], target]
        # One reading of the baseline's durations for the whole round (its reruns, bisection and
        # confirmations), so that a promotion meanwhile changes no limit.
        self.limits = recorded_limits(checks)
        long = too_long(checks)
        for check, seconds in long:
            self.report(check, 'longer than the post-land limit', recorded_seconds=seconds,
                        limit_seconds=RECORDED_LIMIT_MAX_SECONDS, range=span,
                        advice='run it as an explicit --probe of a landing, or in a sheriff suite')
        checks = [check for check in checks if check not in [row for row, _ in long]]
        if not checks:
            self.save(checked=target, reported={})
            return self.git('rev-parse', 'main') == target
        for check, log, code in self.run(target, checks, 'main'):
            # A rerun alone separates a flake or a load timeout from a result change.
            again, alone = self.outcome(target, check, 'alone')
            logs = [log, *([alone] if alone else [])]
            if again == 'pass':
                # A load effect, not a finding: one line in a summary file.
                self.note_flake(check, logs, span)
            elif again != 'fail':
                self.report(check, 'undecided', at=target, result=f'{again} alone', logs=logs, range=span)
            elif self.isolate(rows, check, logs, target) == 'reverted':
                # Main moved; the other failures are checked again with the revert.
                return False
        self.save(checked=target, reported={})
        return self.git('rev-parse', 'main') == target


def run(repo):
    checker = Checker(repo)
    while True:
        with (checker.root/'lock').open('a') as lock:
            try:
                fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                print('postland: another checker runs; it covers the new landings', flush=True)
                return 0
            try:
                while not checker.round():
                    pass
            except (CheckerError, SystemExit, Exception) as error:
                # Whatever stops a round is written for the orchestrator; the range stays unchecked.
                main = checker.git('rev-parse', 'main', check=False)
                land.atomic_json(checker.root/f'{stamp()}-checker.json',
                                 {'time': f'{datetime.datetime.now():%F %T}', 'kind': 'checker error',
                                  'error': f'{type(error).__name__}: {error}', 'main': main})
                print(f'postland: {error}', file=sys.stderr, flush=True)
                return 1
        # A landing between the last round and releasing the lock found the lock held.
        if checker.git('rev-parse', 'main') == checker.state().get('checked'):
            return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--repo', type=Path, default=HERE.parents[1])
    args = parser.parse_args(argv)
    os.environ.pop('UNBOUND_LAND_GATE', None)
    if os.getpriority(os.PRIO_PROCESS, 0) < BACKGROUND_NICE:
        os.setpriority(os.PRIO_PROCESS, 0, BACKGROUND_NICE)
    return run(args.repo)


if __name__ == '__main__':
    raise SystemExit(main())
