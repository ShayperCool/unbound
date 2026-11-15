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
for suite checks (--against), the same 600 s budget and 300 s limit per check with one rerun
of time-limited checks, and the same strict comparisons.

A failing check is run once more alone at the same commit, so that a load timeout or a flake
is not blamed on a landing. A check that still fails is run at the range's base (unless the range
adds its probe): when it fails there too, the failure predates the range. Otherwise the landings
of the range are bisected; a non-passing result counts only when a second run agrees, and a time
limit, a flake or a commit that cannot be built ends the search with a report. When one landing
turns the check from passing to failing, confirmed by two failing runs at its tip and two passing
runs at its parent, its commits are reverted on a branch and landed through land.py (at normal
priority, as it may lead a train) if the revert applies cleanly and that landing was not reverted
before. Every finding, revert or not, is written to
~/.cache/unbound/agents/postland/<time>-<probe>.json for the orchestrator. Suite output uses
fresh labels; sheriff suites and baselines are only read.
"""
import argparse
import contextlib
import datetime
import fcntl
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

# Background work: below ordinary jobs, which the job runner keeps at niceness 10.
BACKGROUND_NICE = 15
WORKTREE = 'postland-checker'
BUILD_ENVIRONMENT = ('CARGO_TARGET_DIR', 'CARGO_BUILD_BUILD_DIR', 'UNBOUND_MANAGED_DIR',
                     'UNBOUND_RUNTIME', 'UNBOUND', 'UNBOUND_CLI', 'UNBOUND_LAND_GATE')


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
        name = re.sub(r'[^A-Za-z0-9._-]+', '-', str(check[0]))
        path = self.root/f'{stamp()}-{name}.json'
        value = {'time': f'{datetime.datetime.now():%F %T}', 'probe': check[0], 'command': check[1],
                 'reason': check[2], 'release': check[3] if len(check) > 3 else None, 'kind': kind, **fields}
        land.atomic_json(path, value)
        print(f'postland: {kind}: {check[0]}; {path}', flush=True)
        return path

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
        self.git('switch', '--quiet', '-C', branch, main, cwd=self.worktree)
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
            path = self.report(check, 'revert did not land', land_exit=landing.returncode,
                               land_output=(landing.stdout+landing.stderr)[-4000:], culprit=culprit,
                               logs=logs, **common)
        else:
            self.save(reverted=[*reverted, culprit['new']])
            path = self.report(check, 'reverted', revert=self.git('rev-parse', 'HEAD', cwd=self.worktree),
                               culprit=culprit, logs=logs, **common)
        self.git('switch', '--quiet', '--detach', self.git('rev-parse', 'main'), cwd=self.worktree)
        self.git('branch', '-D', branch, cwd=self.worktree, check=False)
        return path

    def isolate(self, rows, check, logs, target):
        """Report a confirmed failure, and revert its landing when one landing is the clear cause.
        Only two agreeing runs decide; a time limit, a flake or an unbuildable commit is reported."""
        base = rows[0]['old']
        common = {'range': [base, target], 'landings': [
            {key: row.get(key) for key in ('branch', 'old', 'new', 'request_id', 'log')} for row in rows]}
        if self.exists(base, check):
            result = self.decide(base, check, 'base', logs)
            if result == 'fail':
                return self.report(check, 'fails before the range', logs=logs, **common)
            if result != 'pass':
                return self.report(check, 'undecided', at=base, result=result, logs=logs, **common)
        low, high = -1, len(rows)-1  # rows[low]['new'] (or base) passes, rows[high]['new'] fails
        while high-low > 1:
            middle = (low+high)//2
            commit = rows[middle]['new']
            result = self.decide(commit, check, 'bisect', logs) if self.exists(commit, check) else 'pass'
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
        tip = self.decide(culprit['new'], check, 'confirm', logs)
        parent = (self.decide(culprit['old'], check, 'confirm', logs)
                  if self.exists(culprit['old'], check) else 'pass')
        if (tip, parent) != ('fail', 'pass'):
            return self.report(check, 'undecided', at=culprit['new'], result=f'tip {tip}, parent {parent}',
                               culprit=culprit, logs=logs, **common)
        return self.revert(culprit, check, logs, common)

    def round(self):
        """Check every landing up to main; True when main was reached."""
        target = self.git('rev-parse', 'main')
        checked = self.state().get('checked')
        rows = self.pending(checked, target)
        checks = land.automatic_union([[tuple(row) for row in item['automatic']] for item in rows])
        if not checks:
            self.save(checked=target)
            return True
        span = [rows[0]['old'], target]
        for check, log, code in self.run(target, checks, 'main'):
            # A rerun alone separates a flake or a load timeout from a result change.
            again, alone = self.outcome(target, check, 'alone')
            logs = [log, *([alone] if alone else [])]
            if again == 'pass':
                self.report(check, 'passed alone after failing beside others', logs=logs, range=span)
            elif again != 'fail':
                self.report(check, 'undecided', at=target, result=f'{again} alone', logs=logs, range=span)
            elif json.loads(Path(self.isolate(rows, check, logs, target)).read_text())['kind'] == 'reverted':
                # Main moved; the other failures are checked again with the revert.
                return False
        self.save(checked=target)
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
