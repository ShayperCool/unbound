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
is not blamed on a landing. A check that still fails is run at the range's base: when it fails
there too, the failure predates the range. Otherwise the landings of the range are bisected.
When one landing turns the check from passing to failing, its commits are reverted on a branch
and landed through land.py with the reason, if the revert applies cleanly and that landing was
not reverted before. Every failure, revert or not, is written to
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
        """The failing checks of `checks` at commit, as (check, log) pairs; [] when all pass."""
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
            for row in gate.failures:
                index = int(Path(row[3]).name.split('.')[0])
                failed.append((list(checks[index-1]), str(logs/Path(row[3]).name)))
            return failed
        return []

    def passes(self, commit, check, label):
        return not self.run(commit, [check], label)

    # --- outcomes --------------------------------------------------------------------------
    def report(self, check, kind, **fields):
        name = re.sub(r'[^A-Za-z0-9._-]+', '-', str(check[0]))
        path = self.root/f'{stamp()}-{name}.json'
        value = {'time': f'{datetime.datetime.now():%F %T}', 'probe': check[0], 'command': check[1],
                 'reason': check[2], 'release': check[3] if len(check) > 3 else None, 'kind': kind, **fields}
        land.atomic_json(path, value)
        print(f'postland: {kind}: {check[0]}; {path}', flush=True)
        return path

    def revert(self, culprit, check, logs):
        """Land a revert of the culprit landing; a report either way."""
        reverted = self.state().get('reverted', [])
        common = {'culprit': culprit, 'logs': logs}
        if culprit['new'] in reverted:
            return self.report(check, 'fails after its revert', **common)
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
            return self.report(check, 'revert does not apply', error=applied.stderr.strip(), **common)
        message = (f"fix(postland): revert {culprit['branch']}\n\n"
                   f"Its landing ({culprit['old'][:10]}..{culprit['new'][:10]}) turned the automatic check "
                   f"{check[0]} from passing to failing on main; the check fails alone at the landing's tip "
                   f"and passes at its parent. Check log: {logs[-1]}\n")
        self.git('commit', '--quiet', '-m', message, cwd=self.worktree)
        landing = subprocess.run([sys.executable, str(self.worktree/'tools/agents/land.py')], cwd=self.worktree,
                                 capture_output=True, text=True, env=self.environment())
        if landing.returncode:
            path = self.report(check, 'revert did not land', land_exit=landing.returncode,
                               land_output=(landing.stdout+landing.stderr)[-4000:], **common)
        else:
            self.save(reverted=[*reverted, culprit['new']])
            path = self.report(check, 'reverted', revert=self.git('rev-parse', 'HEAD', cwd=self.worktree),
                               **common)
        self.git('switch', '--quiet', '--detach', self.git('rev-parse', 'main'), cwd=self.worktree)
        self.git('branch', '-D', branch, cwd=self.worktree, check=False)
        return path

    def isolate(self, rows, check, logs, target):
        """Report a confirmed failure, and revert its landing when one landing is the clear cause."""
        base = rows[0]['old']
        common = {'range': [base, target], 'landings': [
            {key: row.get(key) for key in ('branch', 'old', 'new', 'request_id', 'log')} for row in rows]}
        if not self.passes(base, check, 'base'):
            return self.report(check, 'fails before the range', logs=logs, **common)
        low, high = -1, len(rows)-1  # rows[low]['new'] (or base) passes, rows[high]['new'] fails
        while high-low > 1:
            middle = (low+high)//2
            if self.passes(rows[middle]['new'], check, 'bisect'):
                low = middle
            else:
                high = middle
        culprit = {key: rows[high].get(key) for key in ('branch', 'old', 'new', 'request_id', 'log')}
        before = rows[high-1]['new'] if high else base
        if before != culprit['old']:
            # Another landing sits between them on main; no single landing is the clear cause.
            return self.report(check, 'no single landing', culprit=culprit, logs=logs, **common)
        return self.revert(culprit, check, logs)

    def round(self):
        """Check every landing up to main; True when main was reached."""
        target = self.git('rev-parse', 'main')
        checked = self.state().get('checked')
        rows = self.pending(checked, target)
        checks = land.automatic_union([[tuple(row) for row in item['automatic']] for item in rows])
        if not checks:
            self.save(checked=target)
            return True
        failures = self.run(target, checks, 'main')
        reverted = False
        for check, log in failures:
            # A rerun alone separates a flake or a load timeout from a result change.
            again = self.run(target, [check], 'alone')
            if not again:
                self.report(check, 'passed alone after failing beside others', logs=[log],
                            range=[rows[0]['old'], target])
                continue
            path = self.isolate(rows, check, [log, again[0][1]], target)
            reverted |= json.loads(Path(path).read_text())['kind'] == 'reverted'
        if reverted:
            return False  # main moved: check the range again, revert included
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
            except CheckerError as error:
                land.atomic_json(checker.root/f'{stamp()}-checker.json',
                                 {'time': f'{datetime.datetime.now():%F %T}', 'kind': 'checker error',
                                  'error': str(error), 'main': checker.git('rev-parse', 'main')})
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
